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
        public enum Button { Fire, FireLeft, Aim, Jump, Crouch, Reload, Swap, Pause, Score, Prone, Grenade, Ammo, Heal }

        public static bool Enabled;
        /// <summary>The ammo button only exists while the player stands at an ammo box.</summary>
        public static bool AmmoAvailable;
        public static Vector2 Move;
        public static Vector2 Look;
        public static bool FireHeld, FirePressed, AimPressed, JumpPressed, CrouchPressed, ReloadPressed, SwapPressed, PausePressed, ScoreHeld, PronePressed, GrenadePressed, AmmoPressed, HealPressed;

        // automated test input
        public static Vector2 SimMove, SimLook;
        public static bool SimFire, SimScore, SimGrenade;

        // for drawing the stick
        public static bool StickActive;
        public static Vector2 StickCenter, StickKnob;
        public static readonly HashSet<Button> Held = new HashSet<Button>();

        public const float StickRadius = 120f;

        private struct Finger { public Button? Button; public bool Stick, LookDrag; public Vector2 Last; }
        private readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        private bool lastFireHeld;

        /// <summary>Default layout (the player's own arrangement from the layout editor).</summary>
        public static Vector2 DefaultCenter(Button b)
        {
            float w = UIKit.Width;
            switch (b)
            {
                case Button.Fire: return new Vector2(w - 222, 273);
                case Button.FireLeft: return new Vector2(374, 811);
                case Button.Grenade: return new Vector2(579, 838);
                case Button.Heal: return new Vector2(712, 838);
                case Button.Aim: return new Vector2(w - 135, 539);
                case Button.Jump: return new Vector2(w - 497, 370);
                case Button.Crouch: return new Vector2(w - 476, 144);
                case Button.Prone: return new Vector2(w - 246, 721);
                case Button.Reload: return new Vector2(w - 369, 536);
                case Button.Swap: return new Vector2(w - 214, 907);
                case Button.Ammo: return new Vector2(w / 2f + 61, 413);
                case Button.Pause: return new Vector2(80, 1000);
                default: return new Vector2(199, 1000);
            }
        }

        public static float DefaultRadius(Button b)
        {
            switch (b)
            {
                case Button.Fire: return 128;
                case Button.FireLeft: return 90;
                case Button.Aim: return 68;
                case Button.Swap: return 62;
                case Button.Pause:
                case Button.Score: return 50;
                default: return 56;
            }
        }

        // the player can move and resize every button (settings > controls layout)
        public static Vector2 ButtonCenter(Button b) => DefaultCenter(b) + new Vector2(PlayerPrefs.GetFloat($"btn2_{b}_x", 0f), PlayerPrefs.GetFloat($"btn2_{b}_y", 0f));
        public static float ButtonRadius(Button b) => DefaultRadius(b) * PlayerPrefs.GetFloat($"btn2_{b}_s", 1f);
        public static bool ButtonVisible(Button b) => PlayerPrefs.GetInt($"btn2_{b}_on", 1) == 1;

        public static void SaveButton(Button b, Vector2 center, float scale, bool visible)
        {
            var offset = center - DefaultCenter(b);
            PlayerPrefs.SetFloat($"btn2_{b}_x", offset.x);
            PlayerPrefs.SetFloat($"btn2_{b}_y", offset.y);
            PlayerPrefs.SetFloat($"btn2_{b}_s", scale);
            PlayerPrefs.SetInt($"btn2_{b}_on", visible ? 1 : 0);
        }

        public static void ResetLayout()
        {
            foreach (var b in AllButtons)
                foreach (var k in new[] { "x", "y", "s", "on" }) PlayerPrefs.DeleteKey($"btn2_{b}_{k}");
        }

        private static readonly Button[] AllButtons = (Button[])System.Enum.GetValues(typeof(Button));

        private void Update()
        {
            bool fireTap = false, aim = false, jump = false, crouch = false, reload = false, swap = false, pause = false, prone = false, grenade = false, ammo = false, heal = false;
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
                    var f = new Finger { Last = p };
                    foreach (var b in AllButtons)
                    {
                        if (ButtonVisible(b) && (b != Button.Ammo || AmmoAvailable) && Vector2.Distance(p, ButtonCenter(b)) <= ButtonRadius(b) * 1.1f) { f.Button = b; break; }
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
                            case Button.Prone: prone = true; break;
                            case Button.Grenade: grenade = true; break;
                            case Button.Ammo: ammo = true; break;
                            case Button.Heal: heal = true; break;
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
                    // dragging the fire button also aims, like most mobile shooters; the exact distance the
                    // finger moved since the last frame (no smoothing, no lost touch samples)
                    var d = (p - finger.Last) * sens;
                    Look += new Vector2(Settings.ReverseHorizontal ? -d.x : d.x, Settings.ReverseVertical ? -d.y : d.y);
                }
                finger.Last = p;
                fingers[t.fingerId] = finger;
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
                if (Input.GetKeyDown(KeyCode.Z)) prone = true;
                if (Input.GetKeyDown(KeyCode.G)) grenade = true;
                if (Input.GetKeyDown(KeyCode.F)) ammo = true;
                if (Input.GetKeyDown(KeyCode.H)) heal = true;
                if (Input.GetKeyDown(KeyCode.R)) reload = true;
                if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2)) swap = true;
                if (Input.GetKey(KeyCode.Tab)) Held.Add(Button.Score);
            }
            if (Input.GetKeyDown(KeyCode.Escape)) pause = true;

            if (SimMove != Vector2.zero) Move = SimMove;
            Look += SimLook;
            if (SimFire) fireHeld = true;
            if (SimScore) Held.Add(Button.Score);
            if (SimGrenade) { grenade = true; SimGrenade = false; }
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
            PronePressed = prone;
            GrenadePressed = grenade;
            AmmoPressed = ammo;
            HealPressed = heal;
        }
    }
}
