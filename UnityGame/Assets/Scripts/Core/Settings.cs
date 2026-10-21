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
                if (!PlayerPrefs.HasKey("name"))
                    PlayerPrefs.SetString("name", "Agent" + Random.Range(100, 999));
                return PlayerPrefs.GetString("name");
            }
            set => PlayerPrefs.SetString("name", Clean(value));
        }

        /// <summary>0 low, 1 medium, 2 high.</summary>
        public static int Quality { get => PlayerPrefs.GetInt("quality", 1); set { PlayerPrefs.SetInt("quality", Mathf.Clamp(value, 0, 2)); Apply(); } }
        public static float Sensitivity { get => PlayerPrefs.GetFloat("sens", 1f); set => PlayerPrefs.SetFloat("sens", Mathf.Clamp(value, 0.3f, 3f)); }
        public static float Volume { get => PlayerPrefs.GetFloat("volume", 0.8f); set { PlayerPrefs.SetFloat("volume", Mathf.Clamp01(value)); AudioListener.volume = value; } }
        public static bool InvertY { get => PlayerPrefs.GetInt("invert", 0) == 1; set => PlayerPrefs.SetInt("invert", value ? 1 : 0); }
        public static bool ShowFps { get => PlayerPrefs.GetInt("fps", 0) == 1; set => PlayerPrefs.SetInt("fps", value ? 1 : 0); }
        public static byte Primary { get => (byte)PlayerPrefs.GetInt("primary", 0); set => PlayerPrefs.SetInt("primary", value); }
        public static byte Secondary { get => (byte)PlayerPrefs.GetInt("secondary", 4); set => PlayerPrefs.SetInt("secondary", value); }

        public static readonly string[] QualityNames = { "LOW", "MEDIUM", "HIGH" };

        public static string Clean(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 14) name = name.Substring(0, 14);
            return name.Length == 0 ? "Agent" : name;
        }

        public static void Apply()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            AudioListener.volume = Volume;
            switch (Quality)
            {
                case 0:
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.shadowDistance = 0;
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.lodBias = 0.6f;
                    break;
                case 1:
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.shadowResolution = ShadowResolution.Medium;
                    QualitySettings.shadowCascades = 1;
                    QualitySettings.shadowDistance = 35;
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.lodBias = 1f;
                    break;
                default:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = ShadowResolution.High;
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.shadowDistance = 55;
                    QualitySettings.antiAliasing = 2;
                    QualitySettings.lodBias = 1.5f;
                    break;
            }
            if (Application.isMobilePlatform)
            {
                // render below native resolution on weaker settings: big win on phones with 1440p+ screens
                var display = Screen.currentResolution;
                float scale = Quality == 0 ? 0.6f : Quality == 1 ? 0.8f : 1f;
                int h = Mathf.Min(display.height, display.width);
                int target = Mathf.Min(Mathf.RoundToInt(h * scale), 1080);
                if (target < h)
                {
                    float k = (float)target / h;
                    Screen.SetResolution(Mathf.RoundToInt(display.width * k), Mathf.RoundToInt(display.height * k), true);
                }
            }
        }
    }
}
