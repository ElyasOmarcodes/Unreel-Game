using UnityEngine;

namespace UA
{
    /// <summary>
    /// Watches the frame rate during a match. When it stays well under the target for a while, the
    /// graphics come down one step at a time (resolution first, then quality, then 30 fps) so the game
    /// stays smooth and the phone does not overheat or close the game. It never raises them by itself.
    /// </summary>
    public class PerfGuard : MonoBehaviour
    {
        private const float Window = 8f;
        private float elapsed, frames, slowWindows;

        private void Update()
        {
            var s = Session.I;
            if (!Settings.AutoGraphics || s == null || !s.InMatch || !Application.isMobilePlatform) { elapsed = frames = 0; return; }
            elapsed += Time.unscaledDeltaTime;
            frames++;
            if (elapsed < Window) return;
            float fps = frames / elapsed;
            elapsed = frames = 0;
            bool slow = fps < Settings.FrameRate * 0.72f;
            slowWindows = slow ? slowWindows + 1 : 0;
            if (slowWindows < 2) return;
            slowWindows = 0;
            if (Settings.RenderScale > 60) Settings.RenderScale -= 10;
            else if (Settings.Quality > 0) Settings.Quality -= 1;
            else if (Settings.FrameRate > 30) Settings.FrameRate = 30;
            else return;
            App.I?.Toast(Loc.F("GRAPHICS LOWERED FOR SMOOTH PLAY ({0} FPS)", Mathf.RoundToInt(fps)));
        }
    }
}
