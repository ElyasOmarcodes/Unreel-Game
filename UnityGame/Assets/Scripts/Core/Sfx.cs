using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>Plays the sounds in Resources/Audio through a small pool of audio sources.</summary>
    public static class Sfx
    {
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private static readonly List<AudioSource> pool = new List<AudioSource>();
        private static Transform root;

        public static void Play(string name, float volume = 1f, float pitch = 1f) => PlayAt(name, Vector3.zero, volume, pitch, false);

        public static void PlayAt(string name, Vector3 position, float volume = 1f, float pitch = 1f, bool spatial = true)
        {
            var clip = Clip(name);
            if (clip == null) return;
            var source = Next();
            source.transform.position = position;
            source.spatialBlend = spatial ? 1f : 0f;
            // a little variety for the world sounds, none for the interface
            source.pitch = name == "click" || name.StartsWith("ui_") ? pitch : pitch * Random.Range(0.96f, 1.04f);
            source.volume = volume;
            source.clip = clip;
            source.Play();
        }

        private static readonly List<AudioSource> steps = new List<AudioSource>();

        /// <summary>
        /// Footsteps and other small body sounds: their own few voices that fade out within 30 m, so
        /// they never take a voice from a gunshot.
        /// </summary>
        public static void Step(string name, Vector3 position, float volume)
        {
            var clip = Clip(name);
            if (clip == null) return;
            // too far to hear a footstep at all
            var listener = CameraRig.I != null ? CameraRig.I.transform.position : position;
            if ((listener - position).sqrMagnitude > 36f * 36f) return;
            if (root == null) Next();
            AudioSource source = null;
            foreach (var s in steps)
                if (!s.isPlaying) { source = s; break; }
            if (source == null)
            {
                if (steps.Count >= 8) return;
                var go = new GameObject("Step");
                go.transform.SetParent(root);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 1.8f;
                source.maxDistance = 38f;
                source.dopplerLevel = 0f;
                source.spatialBlend = 1f;
                source.priority = 200;
                // a little softer: the gritty top of the recordings is taken off
                go.AddComponent<AudioLowPassFilter>().cutoffFrequency = 5200f;
                steps.Add(source);
            }
            source.transform.position = position;
            source.pitch = Random.Range(0.9f, 1.1f);
            source.volume = volume;
            source.clip = clip;
            source.Play();
        }

        public static bool Has(string name) => Clip(name) != null;

        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        /// <summary>How many clips prefix_0, prefix_1, ... there are (0 when the set is missing).</summary>
        public static int Count(string prefix)
        {
            if (counts.TryGetValue(prefix, out int n)) return n;
            n = 0;
            while (n < 32 && Clip(prefix + n) != null) n++;
            return counts[prefix] = n;
        }

        private static AudioClip Clip(string name)
        {
            if (!clips.TryGetValue(name, out var clip))
                clips[name] = clip = Resources.Load<AudioClip>("Audio/" + name);
            return clip;
        }

        private static AudioSource Next()
        {
            if (root == null)
            {
                root = new GameObject("Sfx").transform;
                Object.DontDestroyOnLoad(root.gameObject);
                pool.Clear();
                steps.Clear();
            }
            foreach (var s in pool)
                if (!s.isPlaying) return s;
            if (pool.Count >= 24) return pool[Random.Range(0, pool.Count)];
            var go = new GameObject("Voice");
            go.transform.SetParent(root);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            // world sounds fade with distance like real ones: loud close by, still heard far away
            // (a shot 100 m off is quiet but tells where it came from)
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 5f;
            source.maxDistance = 260f;
            source.dopplerLevel = 0f;
            pool.Add(source);
            return source;
        }
    }
}
