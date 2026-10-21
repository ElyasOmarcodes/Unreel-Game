using System;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace ArenaRush
{
    /// <summary>
    /// Draws the virtual joystick and the jump button. Everything is generated in code (no UI assets needed).
    /// </summary>
    public class TouchHud : SyncScript
    {
        private const float VirtualHeight = 720f;

        private ImageElement stickBase;
        private ImageElement stickKnob;
        private ImageElement stickHint;
        private ImageElement jumpButton;
        private Canvas canvas;

        public override void Start()
        {
            var ring = CreateCircleTexture(128, filled: false);
            var disc = CreateCircleTexture(128, filled: true);

            canvas = new Canvas();
            stickHint = AddImage(ring, 190, 0.55f);
            stickBase = AddImage(ring, 190, 0.8f);
            stickKnob = AddImage(disc, 86, 0.85f);
            jumpButton = AddImage(disc, 120, 0.55f);

            var ui = new UIComponent
            {
                Page = new UIPage { RootElement = canvas },
                Resolution = new Vector3(VirtualHeight * 16f / 9f, VirtualHeight, 1000),
                ResolutionStretch = ResolutionStretch.FixedHeightAdaptableWidth,
                IsFullScreen = true,
            };
            Entity.Add(ui);
        }

        public override void Update()
        {
            var bounds = Game.Window.ClientBounds;
            canvas.Width = VirtualHeight * bounds.Width / Math.Max(1f, bounds.Height);
            canvas.Height = VirtualHeight;

            TouchState.JumpPressedTime = Math.Max(0, TouchState.JumpPressedTime - (float)Game.UpdateTime.Elapsed.TotalSeconds);

            stickHint.Visibility = TouchState.StickActive ? Visibility.Hidden : Visibility.Visible;
            stickBase.Visibility = TouchState.StickActive ? Visibility.Visible : Visibility.Hidden;
            stickKnob.Visibility = stickBase.Visibility;
            Place(stickHint, new Vector2(0.14f, 0.74f));

            if (TouchState.StickActive)
            {
                Place(stickBase, TouchState.StickOrigin);
                var aspect = bounds.Width / Math.Max(1f, bounds.Height);
                var knob = TouchState.StickOrigin + new Vector2(TouchState.Stick.X / aspect, -TouchState.Stick.Y) * 0.11f;
                Place(stickKnob, knob);
            }

            Place(jumpButton, TouchState.JumpButtonCenter);
            jumpButton.Opacity = TouchState.JumpPressedTime > 0 ? 0.95f : 0.55f;
        }

        private ImageElement AddImage(Texture texture, float size, float opacity)
        {
            var image = new ImageElement
            {
                Source = new SpriteFromTexture { Texture = texture },
                Width = size,
                Height = size,
                Opacity = opacity,
                StretchType = StretchType.Fill,
            };
            image.SetCanvasPinOrigin(new Vector3(0.5f, 0.5f, 0));
            canvas.Children.Add(image);
            return image;
        }

        private static void Place(UIElement element, Vector2 normalized)
        {
            element.SetCanvasRelativePosition(new Vector3(normalized.X, normalized.Y, 0));
        }

        private Texture CreateCircleTexture(int size, bool filled)
        {
            var pixels = new Color[size * size];
            var c = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a;
                    if (filled)
                        a = MathUtil.Clamp((1f - d) * 24f, 0, 1) * 0.75f;
                    else
                        a = MathUtil.Clamp((1f - Math.Abs(d - 0.92f) / 0.07f), 0, 1) + MathUtil.Clamp((1f - d) * 24f, 0, 1) * 0.18f;
                    a = MathUtil.Clamp(a, 0, 1);
                    var v = (byte)(255 * a);
                    pixels[y * size + x] = new Color(v, v, v, v); // premultiplied white
                }
            }
            return Texture.New2D(GraphicsDevice, size, size, PixelFormat.R8G8B8A8_UNorm, pixels);
        }
    }
}
