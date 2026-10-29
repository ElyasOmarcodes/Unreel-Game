using UnityEngine;

namespace UA
{
    /// <summary>Player options, stored in PlayerPrefs.</summary>
    public static class Settings
    {
        public static string PlayerName
        {
            get
            {
                // until the player types a name, they are "Agent" with a number from 1 to 1000, drawn
                // once on the first start and kept
                string name = PlayerPrefs.GetString("name", "");
                return name.Length > 0 && name != "Agent" ? name : AutoName;
            }
            // an empty name goes back to the Agent number
            set => PlayerPrefs.SetString("name", string.IsNullOrWhiteSpace(value) ? "" : Clean(value));
        }

        /// <summary>The name given on the first start: Agent 1 .. Agent 1000, at random.</summary>
        public static string AutoName
        {
            get
            {
                string auto = PlayerPrefs.GetString("autoname", "");
                if (auto.Length == 0)
                {
                    auto = "Agent " + Random.Range(1, 1001);
                    PlayerPrefs.SetString("autoname", auto);
                    PlayerPrefs.Save();
                }
                return auto;
            }
        }

        /// <summary>0 English, 1 Persian, 2 Arabic, 3 Spanish (Loc); the first start follows the phone's language.</summary>
        public static int Language
        {
            get => PlayerPrefs.GetInt("lang", Application.systemLanguage == SystemLanguage.Arabic ? 2
                : Application.systemLanguage == SystemLanguage.Spanish ? 3 : SystemLanguageIsPersian ? 1 : 0);
            set { PlayerPrefs.SetInt("lang", (value % 4 + 4) % 4); PlayerPrefs.Save(); }
        }

        // Unity has no Persian in its list of system languages: the phone's culture says it
        private static bool SystemLanguageIsPersian =>
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";

        /// <summary>The first-start page (name and character) was seen.</summary>
        /// <summary>The language was chosen on the first page (asked once, before anything else).</summary>
        public static bool LanguagePicked { get => PlayerPrefs.GetInt("langPicked", 0) == 1; set { PlayerPrefs.SetInt("langPicked", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static bool Welcomed { get => PlayerPrefs.GetInt("welcomed", 0) == 1; set { PlayerPrefs.SetInt("welcomed", value ? 1 : 0); PlayerPrefs.Save(); } }

        /// <summary>0 low, 1 medium, 2 high. The first start picks what the phone can carry.</summary>
        public static int Quality { get => PlayerPrefs.GetInt("quality", DeviceClass); set { PlayerPrefs.SetInt("quality", Mathf.Clamp(value, 0, 2)); Apply(); } }
        /// <summary>Share of the screen's own resolution the game draws at, in percent (the screen still fills).</summary>
        public static int RenderScale { get => PlayerPrefs.GetInt("renderScale", DeviceClass == 0 ? 65 : DeviceClass == 1 ? 80 : 90); set { PlayerPrefs.SetInt("renderScale", Mathf.Clamp(value, 50, 100)); Apply(); } }
        /// <summary>Frames per second the game aims for (a steady 30 is smoother than a jumpy 60 on a weak phone).</summary>
        public static int FrameRate { get => PlayerPrefs.GetInt("frameRate", 60); set { PlayerPrefs.SetInt("frameRate", value); Apply(); } }
        public static readonly int[] FrameRates = { 30, 60, 90 };

        /// <summary>
        /// 0 weak phone, 1 ordinary, 2 strong. Memory, processor cores and speed, and the graphics chip
        /// all count; HIGH only for flagship chips with plenty of memory, LOW as soon as one part is weak.
        /// </summary>
        public static int DeviceClass
        {
            get
            {
                if (!Application.isMobilePlatform) return 1;
                int memory = SystemInfo.systemMemorySize, cores = SystemInfo.processorCount, mhz = SystemInfo.processorFrequency;
                string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
                bool weakGpu = System.Text.RegularExpressions.Regex.IsMatch(gpu, @"mali-4|mali-t|mali-g(31|51|52|57)\b|adreno \(tm\) ([3-4]\d\d|50\d|51\d|60\d|61[0-3])|adreno ([3-4]\d\d|50\d|51\d|60\d|61[0-3])\b|powervr|sgx|ge8");
                bool strongGpu = System.Text.RegularExpressions.Regex.IsMatch(gpu, @"adreno.*(6[4-9]\d|7\d\d|8\d\d)|mali-g(7[6-9]|7\d\d|9\d\d)|immortalis|xclipse|apple");
                if ((memory > 0 && memory < 4000) || cores <= 4 || (mhz > 0 && mhz < 1800) || weakGpu) return 0;
                return memory >= 7500 && cores >= 8 && strongGpu ? 2 : 1;
            }
        }
        /// <summary>Lower the graphics by itself when the game runs slowly (on by default).</summary>
        public static bool AutoGraphics { get => PlayerPrefs.GetInt("autoGfx", 1) == 1; set => PlayerPrefs.SetInt("autoGfx", value ? 1 : 0); }
        public static float Sensitivity { get => PlayerPrefs.GetFloat("sens", 1f); set => PlayerPrefs.SetFloat("sens", Mathf.Clamp(value, 0.3f, 3f)); }
        public static float Volume { get => PlayerPrefs.GetFloat("volume", 0.8f); set { PlayerPrefs.SetFloat("volume", Mathf.Clamp01(value)); AudioListener.volume = value; } }
        /// <summary>Music in the menus (never during a match).</summary>
        public static bool Music { get => PlayerPrefs.GetInt("music", 1) == 1; set => PlayerPrefs.SetInt("music", value ? 1 : 0); }
        public static float MusicVolume { get => PlayerPrefs.GetFloat("musicVol", 0.8f); set => PlayerPrefs.SetFloat("musicVol", Mathf.Clamp01(value)); }
        public static bool InvertY { get => PlayerPrefs.GetInt("invert", 0) == 1; set => PlayerPrefs.SetInt("invert", value ? 1 : 0); }
        /// <summary>Touch look directions: both follow the finger unless reversed here.</summary>
        public static bool ReverseHorizontal { get => PlayerPrefs.GetInt("revH", 0) == 1; set => PlayerPrefs.SetInt("revH", value ? 1 : 0); }
        public static bool ReverseVertical { get => PlayerPrefs.GetInt("revV2", 0) == 1; set => PlayerPrefs.SetInt("revV2", value ? 1 : 0); }
        /// <summary>Turning speed while aiming down the sights (iron sights, red dot).</summary>
        public static float AimSensitivity { get => PlayerPrefs.GetFloat("sensAim", 1f); set => PlayerPrefs.SetFloat("sensAim", Mathf.Clamp(value, 0.2f, 3f)); }
        /// <summary>Turning speed while looking through a scope.</summary>
        public static float ScopeSensitivity { get => PlayerPrefs.GetFloat("sensScope", 1f); set => PlayerPrefs.SetFloat("sensScope", Mathf.Clamp(value, 0.2f, 3f)); }
        /// <summary>The character the player built (Wardrobe packs body, clothes, gear and colours).</summary>
        public static int Look
        {
            get
            {
                // kept by the character's name (the list can change order)
                string key = "look3_" + Wardrobe.Presets[Preset].Name;
                if (!PlayerPrefs.HasKey(key)) PlayerPrefs.SetInt(key, Wardrobe.PresetLook(Preset));
                return PlayerPrefs.GetInt(key);
            }
            set => PlayerPrefs.SetInt("look3_" + Wardrobe.Presets[Preset].Name, value);
        }
        /// <summary>Which ready-made character is played (each keeps its own changes), kept by its name.</summary>
        public static int Preset
        {
            get => Mathf.Max(0, Wardrobe.PresetIndex(PlayerPrefs.GetString("preset3", Wardrobe.Presets[0].Name)));
            set => PlayerPrefs.SetString("preset3", Wardrobe.Presets[((value % Wardrobe.Presets.Length) + Wardrobe.Presets.Length) % Wardrobe.Presets.Length].Name);
        }
        /// <summary>Back to how the ready-made character came.</summary>
        public static void ResetLook() => Look = Wardrobe.PresetLook(Preset);
        /// <summary>Help with aiming (slow down over enemies, follow them, snap when aiming, far shots bend onto the body).</summary>
        public static bool AimAssist { get => PlayerPrefs.GetInt("aimAssist", 1) == 1; set => PlayerPrefs.SetInt("aimAssist", value ? 1 : 0); }
        public static float ControlsOpacity { get => PlayerPrefs.GetFloat("ctrlAlpha", 1f); set => PlayerPrefs.SetFloat("ctrlAlpha", Mathf.Clamp(value, 0.3f, 1.5f)); }
        public static bool ShowFps { get => PlayerPrefs.GetInt("fps", 0) == 1; set => PlayerPrefs.SetInt("fps", value ? 1 : 0); }
        public static byte Primary { get => (byte)PlayerPrefs.GetInt("primary", 0); set => PlayerPrefs.SetInt("primary", value); }
        public static byte Secondary { get => (byte)PlayerPrefs.GetInt("secondary", 4); set => PlayerPrefs.SetInt("secondary", value); }
        /// <summary>The team number this player plays in: 0 alone (the default), 1 .. 10 with whoever picks the same.</summary>
        public static byte Team { get => MatchConfig.ClampTeam(PlayerPrefs.GetInt("team_number", 0)); set => PlayerPrefs.SetInt("team_number", value); }

        public static readonly string[] QualityNames = { "LOW", "MEDIUM", "HIGH" };

        public static string Clean(string name, int maxLength = 14)
        {
            name = (name ?? "").Trim();
            if (name.Length > maxLength) name = name.Substring(0, maxLength);
            return name.Length == 0 ? "Agent" : name;
        }

        public static void Apply()
        {
            Application.targetFrameRate = FrameRate;
            QualitySettings.vSyncCount = 0;
            AudioListener.volume = Volume;
            switch (Quality)
            {
                case 0:
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.shadowDistance = 0;
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.lodBias = 0.6f;
                    QualitySettings.pixelLightCount = 0;   // muzzle flashes and blasts light per vertex
                    QualitySettings.globalTextureMipmapLimit = 1;   // half-size textures: less memory, faster
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                    QualitySettings.skinWeights = SkinWeights.TwoBones;
                    break;
                case 1:
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.shadowResolution = ShadowResolution.Medium;
                    QualitySettings.shadowCascades = 1;
                    QualitySettings.shadowDistance = 30;
                    QualitySettings.antiAliasing = 2;   // MSAA: nearly free on phone GPUs, smooth edges far away
                    QualitySettings.lodBias = 1f;
                    QualitySettings.pixelLightCount = 1;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;   // sharp ground at a slant
                    QualitySettings.skinWeights = SkinWeights.FourBones;
                    break;
                default:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = ShadowResolution.High;
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.shadowDistance = 55;
                    QualitySettings.antiAliasing = 4;
                    QualitySettings.lodBias = 1.5f;
                    QualitySettings.pixelLightCount = 2;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                    QualitySettings.skinWeights = SkinWeights.FourBones;
                    break;
            }
            ApplyResolution();
            if (Camera.main != null) ApplyCamera(Camera.main);
        }

        /// <summary>Camera costs: no HDR target (nothing uses it), small props stop being drawn far away.</summary>
        public static void ApplyCamera(Camera cam)
        {
            cam.allowHDR = false;
            cam.allowMSAA = Quality > 0;
            var distances = new float[32];
            distances[DetailLayer] = Quality == 0 ? 70f : Quality == 1 ? 100f : 140f;
            distances[SceneryLayer] = Quality == 0 ? 150f : Quality == 1 ? 210f : 0f;   // 0: as far as the camera sees
            cam.layerCullDistances = distances;
            cam.layerCullSpherical = true;
        }

        /// <summary>Layer of small merged props (see MapOptimizer): render only, no colliders.</summary>
        public const int DetailLayer = 12;

        /// <summary>Layer of the scenery outside a map (trees on the hills): drawn less far on weaker phones.</summary>
        public const int SceneryLayer = 13;

        private static int nativeWidth, nativeHeight, appliedScale;

        /// <summary>Draws the game at a share of the screen's resolution: the biggest saving on phones with sharp screens.</summary>
        private static void ApplyResolution()
        {
            if (!Application.isMobilePlatform) return;
            if (nativeWidth == 0)
            {
                // the game is landscape only
                nativeWidth = Mathf.Max(Display.main.systemWidth, Display.main.systemHeight);
                nativeHeight = Mathf.Min(Display.main.systemWidth, Display.main.systemHeight);
            }
            if (nativeHeight <= 0 || appliedScale == RenderScale) return;
            appliedScale = RenderScale;
            // never below 540 lines (text and the crosshair stay clear)
            float k = Mathf.Max(RenderScale / 100f, Mathf.Min(1f, 540f / nativeHeight));
            Screen.SetResolution(Mathf.RoundToInt(nativeWidth * k), Mathf.RoundToInt(nativeHeight * k), true);
        }
    }
}
