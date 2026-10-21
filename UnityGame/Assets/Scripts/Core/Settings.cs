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
        /// <summary>Touch look directions (players asked for: left/right with the finger, up/down reversed).</summary>
        public static bool ReverseHorizontal { get => PlayerPrefs.GetInt("revH", 0) == 1; set => PlayerPrefs.SetInt("revH", value ? 1 : 0); }
        public static bool ReverseVertical { get => PlayerPrefs.GetInt("revV", 1) == 1; set => PlayerPrefs.SetInt("revV", value ? 1 : 0); }
        /// <summary>Character look: body * 16 + camo * 4 + head gear.</summary>
        public static byte Look
        {
            get
            {
                if (!PlayerPrefs.HasKey("look")) PlayerPrefs.SetInt("look", Random.Range(0, 32));
                return (byte)PlayerPrefs.GetInt("look");
            }
            set => PlayerPrefs.SetInt("look", value % 32);
        }
        public static float ControlsOpacity { get => PlayerPrefs.GetFloat("ctrlAlpha", 1f); set => PlayerPrefs.SetFloat("ctrlAlpha", Mathf.Clamp(value, 0.3f, 1.5f)); }
        public static bool ShowFps { get => PlayerPrefs.GetInt("fps", 0) == 1; set => PlayerPrefs.SetInt("fps", value ? 1 : 0); }
        public static byte Primary { get => (byte)PlayerPrefs.GetInt("primary", 0); set => PlayerPrefs.SetInt("primary", value); }
        public static byte Secondary { get => (byte)PlayerPrefs.GetInt("secondary", 4); set => PlayerPrefs.SetInt("secondary", value); }

        public static readonly string[] QualityNames = { "LOW", "MEDIUM", "HIGH" };

        public static string Clean(string name, int maxLength = 14)
        {
            name = (name ?? "").Trim();
            if (name.Length > maxLength) name = name.Substring(0, maxLength);
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
        }
    }
}
