using System;
using System.Collections.Generic;
using System.Reflection;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace ArenaRush.UI
{
    /// <summary>
    /// Immediate-mode 2D overlay (menus + HUD) in a landscape virtual space of Overlay.Width x 720.
    /// Scripts queue draw commands during Update; <see cref="OverlayRenderer"/> draws them on top of the scene.
    /// We draw it ourselves (instead of Stride.UI) so it follows the Android Vulkan surface pre-rotation,
    /// exactly like the 3D camera does.
    /// </summary>
    public static class Overlay
    {
        public const float Height = 720f;
        public static float Width = 1280f;

        public static SpriteFont Font;
        public static Texture White, Disc, Ring;

        internal struct Command
        {
            public int Layer;
            public int Shape;          // 0 = solid rectangle, 1 = disc, 2 = ring (textures are resolved at draw time)
            public RectangleF Rect;
            public Color4 Color;
            public string Text;
            public float Size;
            public Vector2 Position;
        }

        internal static readonly List<Command> Commands = new();

        public static void BeginFrame() => Commands.Clear();

        /// <summary>Normalized (0..1) screen position to virtual overlay coordinates.</summary>
        public static Vector2 ToVirtual(Vector2 normalized) => new Vector2(normalized.X * Width, normalized.Y * Height);

        public static void Rect(RectangleF rect, Color4 color, int layer = 0) => Shape(0, rect, color, layer);

        private static void Shape(int shape, RectangleF rect, Color4 color, int layer)
        {
            Commands.Add(new Command { Layer = layer, Shape = shape, Rect = rect, Color = Premultiply(color) });
        }

        public static void Circle(Vector2 center, float radius, Color4 color, bool ring = false, int layer = 0)
        {
            Shape(ring ? 2 : 1, new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2), color, layer);
        }

        /// <summary>Draws text; align: -1 left, 0 centre, 1 right (relative to position.X).</summary>
        public static void Text(string text, Vector2 position, float size, Color4 color, int align = -1, int layer = 0, bool shadow = true)
        {
            if (Font == null || string.IsNullOrEmpty(text))
                return;
            var width = Font.MeasureString(text, size).X;
            var x = align < 0 ? position.X : align == 0 ? position.X - width / 2 : position.X - width;
            if (shadow)
                Commands.Add(new Command { Layer = layer, Text = text, Size = size, Position = new Vector2(x + 2, position.Y + 2), Color = Premultiply(new Color4(0, 0, 0, color.A * 0.8f)) });
            Commands.Add(new Command { Layer = layer, Text = text, Size = size, Position = new Vector2(x, position.Y), Color = Premultiply(color) });
        }

        public static float MeasureText(string text, float size) => Font == null ? 0 : Font.MeasureString(text, size).X;

        private static Color4 Premultiply(Color4 c) => new Color4(c.R * c.A, c.G * c.A, c.B * c.A, c.A);

        internal static void CreateTextures(GraphicsDevice device)
        {
            if (White != null) return;
            White = Texture.New2D(device, 1, 1, PixelFormat.R8G8B8A8_UNorm, new[] { Color.White });
            Disc = CircleTexture(device, 128, filled: true);
            Ring = CircleTexture(device, 128, filled: false);
        }

        private static Texture CircleTexture(GraphicsDevice device, int size, bool filled)
        {
            var pixels = new Color[size * size];
            var c = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                var a = filled
                    ? MathUtil.Clamp((1f - d) * c * 0.5f, 0, 1)
                    : MathUtil.Clamp(1f - Math.Abs(d - 0.9f) / 0.08f, 0, 1);
                var v = (byte)(255 * a);
                pixels[y * size + x] = new Color(v, v, v, v); // premultiplied white
            }
            return Texture.New2D(device, size, size, PixelFormat.R8G8B8A8_UNorm, pixels);
        }
    }

    /// <summary>Scene renderer (added to the graphics compositor) that draws the overlay commands.</summary>
    public class OverlayRenderer : SceneRendererBase
    {
        private SpriteBatch batch;
        private static PropertyInfo surfaceRotationProperty;

        protected override void InitializeCore()
        {
            base.InitializeCore();
            batch = new SpriteBatch(GraphicsDevice);
            Overlay.CreateTextures(GraphicsDevice);
            surfaceRotationProperty = typeof(GraphicsPresenter).GetProperty("SurfaceRotation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
        {
            var viewport = drawContext.CommandList.Viewport;
            if (viewport.Width <= 0 || viewport.Height <= 0)
                return;

            // Android Vulkan pre-rotation: the swapchain stays in the device's natural orientation.
            var rotation = surfaceRotationProperty?.GetValue(GraphicsDevice.Presenter)?.ToString() ?? "Identity";
            var rotated = rotation is "Rotate90" or "Rotate270";
            var logicalWidth = rotated ? viewport.Height : viewport.Width;
            var logicalHeight = rotated ? viewport.Width : viewport.Height;
            Overlay.Width = Overlay.Height * logicalWidth / logicalHeight;

            var projection = Matrix.OrthoOffCenterRH(0, Overlay.Width, Overlay.Height, 0, -1, 1) * RotationMatrix(rotation);

            var commands = Overlay.Commands;
            if (commands.Count == 0)
                return;
            commands.Sort((a, b) => a.Layer.CompareTo(b.Layer));

            batch.Begin(drawContext.GraphicsContext, Matrix.Identity, projection, SpriteSortMode.Deferred,
                BlendStates.AlphaBlend, depthStencilState: DepthStencilStates.None);
            foreach (var c in commands)
            {
                if (c.Text != null)
                {
                    if (Overlay.Font != null)
                        batch.DrawString(Overlay.Font, c.Text, c.Size, c.Position, c.Color);
                    continue;
                }
                var texture = c.Shape switch { 1 => Overlay.Disc, 2 => Overlay.Ring, _ => Overlay.White };
                if (texture != null)
                    batch.Draw(texture, c.Rect, c.Color);
            }
            batch.End();
        }

        // Same clip-space rotation as SceneCameraRenderer uses for the 3D view.
        private static Matrix RotationMatrix(string rotation) => rotation switch
        {
            "Rotate90" => new Matrix(0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
            "Rotate180" => new Matrix(-1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
            "Rotate270" => new Matrix(0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
            _ => Matrix.Identity,
        };

        protected override void Destroy()
        {
            batch?.Dispose();
            base.Destroy();
        }
    }
}
