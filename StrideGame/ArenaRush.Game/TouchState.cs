using Stride.Core.Mathematics;

namespace ArenaRush
{
    /// <summary>
    /// On-screen touch controls. Button centres are normalized screen coordinates (0..1, Y down);
    /// radii are in virtual overlay pixels (screen height = 720).
    /// Written by PlayerInput, drawn by the HUD, consumed by the player scripts.
    /// </summary>
    public static class TouchState
    {
        public static readonly Vector2 FireCenter = new Vector2(0.86f, 0.70f);
        public const float FireRadius = 78f;
        public static readonly Vector2 JumpCenter = new Vector2(0.955f, 0.45f);
        public const float JumpRadius = 46f;
        public static readonly Vector2 ReloadCenter = new Vector2(0.72f, 0.86f);
        public const float ReloadRadius = 42f;
        public static readonly Vector2 SwapCenter = new Vector2(0.93f, 0.2f);
        public const float SwapRadius = 44f;
        public static readonly Vector2 PauseCenter = new Vector2(0.5f, 0.07f);
        public const float PauseRadius = 34f;

        // movement stick
        public static bool StickActive;
        public static Vector2 StickOrigin;
        public static Vector2 StickCurrent;
        public static Vector2 Stick;

        // buttons
        public static bool FireHeld;
        public static bool JumpPressed;
        public static bool ReloadPressed;
        public static bool SwapPressed;
        public static bool PausePressed;
        public static float JumpFlash;

        public static void ClearPresses()
        {
            JumpPressed = ReloadPressed = SwapPressed = PausePressed = false;
        }
    }
}
