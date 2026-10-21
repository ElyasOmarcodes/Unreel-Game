using Stride.Core.Mathematics;

namespace ArenaRush
{
    /// <summary>
    /// Shared state of the on-screen touch controls (written by PlayerInput, drawn by TouchHud).
    /// Positions are normalized screen coordinates (0..1, Y down).
    /// </summary>
    public static class TouchState
    {
        public static readonly Vector2 JumpButtonCenter = new Vector2(0.9f, 0.78f);
        public const float JumpButtonRadius = 0.075f;

        public static bool StickActive;
        public static Vector2 StickOrigin;
        public static Vector2 StickCurrent;
        public static Vector2 Stick;
        public static float JumpPressedTime;
    }
}
