using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Stride.Engine;
using Stride.Rendering.Compositing;
using Stride.Rendering.Images;
using Stride.Rendering.Lights;

namespace ArenaRush
{
    /// <summary>
    /// Player settings, stored as a small key=value file on the device.
    /// </summary>
    public static class GameOptions
    {
        public static readonly string[] QualityNames = { "LOW", "MEDIUM", "HIGH" };

        public static int Quality = 1;
        public static float LookSensitivity = 1.0f;
        public static float Volume = 0.8f;
        public static bool InvertY;
        public static bool ShowFps;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tropical_strike_settings.txt");

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;
                    var v = parts[1].Trim();
                    switch (parts[0].Trim())
                    {
                        case "quality": Quality = Math.Clamp(int.Parse(v, CultureInfo.InvariantCulture), 0, 2); break;
                        case "look": LookSensitivity = Math.Clamp(float.Parse(v, CultureInfo.InvariantCulture), 0.3f, 3f); break;
                        case "volume": Volume = Math.Clamp(float.Parse(v, CultureInfo.InvariantCulture), 0f, 1f); break;
                        case "invertY": InvertY = v == "1"; break;
                        case "fps": ShowFps = v == "1"; break;
                    }
                }
            }
            catch (Exception)
            {
                // corrupted settings: keep defaults
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "quality=" + Quality.ToString(CultureInfo.InvariantCulture),
                    "look=" + LookSensitivity.ToString(CultureInfo.InvariantCulture),
                    "volume=" + Volume.ToString(CultureInfo.InvariantCulture),
                    "invertY=" + (InvertY ? "1" : "0"),
                    "fps=" + (ShowFps ? "1" : "0"),
                });
            }
            catch (Exception)
            {
                // read-only storage: ignore
            }
        }

        /// <summary>Applies the graphics quality to the sun shadows and post effects.</summary>
        public static void ApplyGraphics(SceneSystem sceneSystem)
        {
            var scene = sceneSystem.SceneInstance?.RootScene;
            if (scene == null) return;

            foreach (var light in scene.Entities.Select(e => e.Get<LightComponent>()).Where(l => l != null))
            {
                if (light.Type is LightDirectional directional && directional.Shadow is LightDirectionalShadowMap shadow)
                {
                    shadow.Enabled = Quality > 0;
                    shadow.CascadeCount = Quality >= 2 ? LightShadowMapCascadeCount.TwoCascades : LightShadowMapCascadeCount.OneCascade;
                    shadow.Size = Quality >= 2 ? LightShadowMapSize.Medium : LightShadowMapSize.Small;
                }
            }

            if (sceneSystem.GraphicsCompositor?.Game is SceneCameraRenderer cameraRenderer)
            {
                foreach (var forward in Renderers(cameraRenderer.Child).OfType<ForwardRenderer>())
                {
                    if (forward.PostEffects is PostProcessingEffects post)
                    {
                        post.Fog.Enabled = Quality > 0;
                        if (post.Antialiasing is Stride.Rendering.RendererCoreBase antialiasing)
                            antialiasing.Enabled = Quality > 0;
                    }
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<ISceneRenderer> Renderers(ISceneRenderer renderer)
        {
            if (renderer == null) yield break;
            yield return renderer;
            if (renderer is SceneRendererCollection collection)
                foreach (var child in collection.Children)
                    foreach (var r in Renderers(child))
                        yield return r;
        }
    }
}
