using System;

namespace ArenaRush
{
    public enum FlowState
    {
        Splash,
        MainMenu,
        Settings,
        Playing,
        Paused,
        Victory,
        Defeat,
    }

    /// <summary>
    /// Global game state shared by menus, HUD, player and enemies.
    /// </summary>
    public static class GameFlow
    {
        /// <summary>Set by the desktop effect recorder to start directly in gameplay.</summary>
        public static bool SkipMenus;

        public static FlowState State = FlowState.Splash;
        public static FlowState SettingsReturnState = FlowState.MainMenu;

        /// <summary>True when the player can move and shoot.</summary>
        public static bool IsPlaying => State == FlowState.Playing;

        /// <summary>True once a match is running (playing or paused), so the gameplay camera is used.</summary>
        public static bool InMatch => State is FlowState.Playing or FlowState.Paused or FlowState.Victory or FlowState.Defeat;

        // Mission progress
        public static int EnemiesTotal;
        public static int EnemiesKilled;
        public static float MatchTime;
        public static int ShotsFired;
        public static int ShotsHit;
        public static bool RestartRequested;

        public static string Message = "";
        public static float MessageTime;

        public static event Action<FlowState> StateChanged;

        public static void Set(FlowState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        public static void Show(string message, float seconds = 2.5f)
        {
            Message = message;
            MessageTime = seconds;
        }

        public static void ResetMatch()
        {
            EnemiesKilled = 0;
            MatchTime = 0;
            ShotsFired = 0;
            ShotsHit = 0;
        }

        public static string FormatTime(float seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }
    }
}
