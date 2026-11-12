using UnityEngine;

namespace UA
{
    /// <summary>
    /// Watches the frame rate during a match. When it stays well under the target for a while, the
    /// graphics come down one step at a time (resolution first, then quality, then 30 fps) so the game
    /// stays smooth and the phone does not overheat or close the game. Only for the match being played:
    /// what the player saved in the settings is never changed, and after the match it applies again.
    /// Once the player has chosen the graphics by hand, the guard leaves them alone altogether.
    /// </summary>
    public class PerfGuard : MonoBehaviour
    {
        private const float Window = 8f;
        private float elapsed, frames, slowWindows;

        private void Update()
        {
            var s = Session.I;
            if (s == null || !s.InMatch) { elapsed = frames = 0; Settings.RestoreSaved(); return; }
            if (!Settings.AutoGraphics || Settings.GraphicsByHand || !Application.isMobilePlatform) { elapsed = frames = 0; return; }
            elapsed += Time.unscaledDeltaTime;
            frames++;
            if (elapsed < Window) return;
            float fps = frames / elapsed;
            elapsed = frames = 0;
            bool slow = fps < Settings.FrameRate * 0.72f;
            slowWindows = slow ? slowWindows + 1 : 0;
            if (slowWindows < 2) return;
            slowWindows = 0;
            if (!Settings.LowerForNow()) return;
            App.I?.Toast(Loc.F("GRAPHICS LOWERED FOR SMOOTH PLAY ({0} FPS)", Mathf.RoundToInt(fps)));
        }
    }
}
