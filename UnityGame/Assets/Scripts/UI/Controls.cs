using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// Game input from multi-touch (floating move stick on the left, drag-to-look on the right, buttons)
    /// and from keyboard / mouse. Layout is in canvas units (height 1080, origin bottom-left).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class Controls : MonoBehaviour
    {
        public enum Button { Fire, FireLeft, Aim, Jump, Crouch, Reload, Swap, Pause, Score }

        public static bool Enabled;
        public static Vector2 Move;
        public static Vector2 Look;
        public static bool FireHeld, FirePressed, AimPressed, JumpPressed, CrouchPressed, ReloadPressed, SwapPressed, PausePressed, ScoreHeld;

        // automated test input
        public static Vector2 SimMove, SimLook;
        public static bool SimFire, SimScore;

        // for drawing the stick
        public static bool StickActive;
        public static Vector2 StickCenter, StickKnob;
        public static readonly HashSet<Button> Held = new HashSet<Button>();

        public const float StickRadius = 120f;

        private struct Finger { public Button? Button; public bool Stick, LookDrag; }
        private readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        private bool lastFireHeld;

        public static Vector2 ButtonCenter(Button b)
        {
            float w = UIKit.Width;
            switch (b)
            {
                case Button.Fire: return new Vector2(w - 250, 290);
                case Button.FireLeft: return new Vector2(250, 610);
                case Button.Aim: return new Vector2(w - 490, 150);
                case Button.Jump: return new Vector2(w - 110, 500);
                case Button.Crouch: return new Vector2(w - 110, 120);
                case Button.Reload: return new Vector2(w - 480, 360);
                case Button.Swap: return new Vector2(w - 230, 930);
                case Button.Pause: return new Vector2(80, 1000);
                default: return new Vector2(200, 1000);
            }
        }

        public static float ButtonRadius(Button b)
        {
            switch (b)
            {
                case Button.Fire: return 120;
                case Button.FireLeft: return 72;
                case Button.Aim: return 72;
                case Button.Swap: return 90;
                case Button.Pause:
                case Button.Score: return 52;
                default: return 62;
            }
        }

        private static readonly Button[] AllButtons = (Button[])System.Enum.GetValues(typeof(Button));

        private void Update()
        {
            bool fireTap = false, aim = false, jump = false, crouch = false, reload = false, swap = false, pause = false;
            Move = Vector2.zero;
            Look = Vector2.zero;
            Held.Clear();
            StickActive = false;
            float scale = 1080f / Mathf.Max(1, Screen.height);
            float sens = 0.13f * Settings.Sensitivity;
            bool fireHeld = false;

            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                var p = t.position * scale;
                if (t.phase == TouchPhase.Began)
                {
                    var f = new Finger();
                    foreach (var b in AllButtons)
                    {
                        if (Vector2.Distance(p, ButtonCenter(b)) <= ButtonRadius(b) * 1.1f) { f.Button = b; break; }
                    }
                    if (f.Button == null)
                    {
                        if (p.x < UIKit.Width * 0.42f && p.y < 860f) { f.Stick = true; StickCenter = p; }
                        else f.LookDrag = true;
                    }
                    if (f.Button.HasValue)
                    {
                        switch (f.Button.Value)
                        {
                            case Button.Fire: case Button.FireLeft: fireTap = true; break;
                            case Button.Aim: aim = true; break;
                            case Button.Jump: jump = true; break;
                            case Button.Crouch: crouch = true; break;
                            case Button.Reload: reload = true; break;
                            case Button.Swap: swap = true; break;
                            case Button.Pause: pause = true; break;
                        }
                    }
                    fingers[t.fingerId] = f;
                }
                if (!fingers.TryGetValue(t.fingerId, out var finger)) continue;
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    fingers.Remove(t.fingerId);
                    continue;
                }
                if (finger.Stick)
                {
                    var offset = Vector2.ClampMagnitude(p - StickCenter, StickRadius);
                    Move = offset / StickRadius;
                    StickActive = true;
                    StickKnob = StickCenter + offset;
                }
                else if (finger.LookDrag || finger.Button == Button.Fire)
                {
                    // dragging the fire button also aims, like most mobile shooters
                    Look += t.deltaPosition * scale * sens;
                }
                if (finger.Button.HasValue)
                {
                    Held.Add(finger.Button.Value);
                    if (finger.Button == Button.Fire || finger.Button == Button.FireLeft) fireHeld = true;
                }
            }

            // keyboard and mouse (desktop test builds, emulators with a keyboard)
            if (Input.touchCount == 0)
            {
                var k = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
                if (k != Vector2.zero) Move = k.normalized * (Input.GetKey(KeyCode.LeftShift) ? 1f : 0.7f);
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Look += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 2.2f * Settings.Sensitivity;
                    if (Input.GetMouseButton(0)) fireHeld = true;
                    if (Input.GetMouseButtonDown(0)) fireTap = true;
                    if (Input.GetMouseButtonDown(1)) aim = true;
                }
                if (Input.GetKeyDown(KeyCode.Space)) jump = true;
                if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.LeftControl)) crouch = true;
                if (Input.GetKeyDown(KeyCode.R)) reload = true;
                if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2)) swap = true;
                if (Input.GetKey(KeyCode.Tab)) Held.Add(Button.Score);
            }
            if (Input.GetKeyDown(KeyCode.Escape)) pause = true;

            if (SimMove != Vector2.zero) Move = SimMove;
            Look += SimLook;
            if (SimFire) fireHeld = true;
            if (SimScore) Held.Add(Button.Score);
            if (SimFire && !lastFireHeld) fireTap = true;

            FireHeld = fireHeld;
            FirePressed = fireTap || (fireHeld && !lastFireHeld);
            lastFireHeld = fireHeld;
            AimPressed = aim;
            JumpPressed = jump;
            CrouchPressed = crouch;
            ReloadPressed = reload;
            SwapPressed = swap;
            PausePressed = pause;
            ScoreHeld = Held.Contains(Button.Score);
        }
    }
}
