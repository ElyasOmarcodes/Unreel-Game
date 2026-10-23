using UnityEngine;

namespace UA
{
    /// <summary>
    /// The menu music: starts after the splash screen, plays on in the menus and settings, fades out
    /// when a match starts and comes back after it. The clip is cut so its end runs into its start, so
    /// the loop is never heard. MUSIC in the settings turns it off.
    /// </summary>
    public class Music : MonoBehaviour
    {
        private AudioSource source;

        private void Start()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.clip = Resources.Load<AudioClip>("Audio/music_menu");
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 0;
        }

        private void Update()
        {
            if (source == null || source.clip == null) return;
            var app = App.I;
            var s = Session.I;
            bool want = Settings.Music && app != null && app.Current != null && app.Current != "splash" && !(s != null && s.InMatch);
            float target = want ? 0.55f * Settings.MusicVolume : 0f;
            source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * (want ? 0.5f : 1.2f));
            if (want && !source.isPlaying) source.Play();
            else if (!want && source.isPlaying && source.volume <= 0.001f) source.Pause();
        }
    }
}
