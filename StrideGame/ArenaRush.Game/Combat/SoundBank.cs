using System.Collections.Generic;
using Stride.Audio;
using Stride.Engine;

namespace ArenaRush.Combat
{
    /// <summary>Plays the game's sound effects (non-spatialized, a few voices per sound).</summary>
    public class SoundBank : StartupScript
    {
        public Sound ShotAk { get; set; }
        public Sound ShotM4 { get; set; }
        public Sound ShotPistol { get; set; }
        public Sound EnemyShot { get; set; }
        public Sound Reload { get; set; }
        public Sound Empty { get; set; }
        public Sound Hit { get; set; }
        public Sound Pickup { get; set; }
        public Sound Hurt { get; set; }
        public Sound Click { get; set; }

        public static SoundBank Instance { get; private set; }

        private readonly Dictionary<Sound, SoundInstance[]> voices = new();
        private readonly Dictionary<Sound, int> nextVoice = new();

        public override void Start()
        {
            Instance = this;
        }

        public override void Cancel()
        {
            foreach (var list in voices.Values)
                foreach (var v in list)
                    v?.Dispose();
            if (Instance == this) Instance = null;
        }

        public static void Play(string name, float volume = 1f)
        {
            var bank = Instance;
            if (bank == null) return;
            var sound = name switch
            {
                "ak" => bank.ShotAk,
                "m4" => bank.ShotM4,
                "pistol" => bank.ShotPistol,
                "enemy" => bank.EnemyShot,
                "reload" => bank.Reload,
                "empty" => bank.Empty,
                "hit" => bank.Hit,
                "pickup" => bank.Pickup,
                "hurt" => bank.Hurt,
                "click" => bank.Click,
                _ => null,
            };
            bank.PlaySound(sound, volume);
        }

        private void PlaySound(Sound sound, float volume)
        {
            if (sound == null || GameOptions.Volume <= 0.001f) return;
            try
            {
                if (!voices.TryGetValue(sound, out var list))
                {
                    list = new SoundInstance[4];
                    voices[sound] = list;
                    nextVoice[sound] = 0;
                }
                var index = nextVoice[sound];
                nextVoice[sound] = (index + 1) % list.Length;
                var voice = list[index] ??= sound.CreateInstance();
                voice.Stop();
                voice.Volume = System.Math.Clamp(volume * GameOptions.Volume, 0f, 1f);
                voice.Play();
            }
            catch (System.Exception)
            {
                // audio device unavailable (e.g. emulator without audio): ignore
            }
        }
    }
}
