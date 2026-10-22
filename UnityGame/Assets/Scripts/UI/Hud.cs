using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>In-match overlay: controls, crosshair, health, ammo, timer, score, kill feed, names, scoreboard.</summary>
    public class Hud : MonoBehaviour
    {
        public static Hud I;
        public static bool ForceTouchUi;

        private Canvas canvas;
        private RectTransform root;
        private readonly Dictionary<Controls.Button, Image> buttons = new Dictionary<Controls.Button, Image>();
        private readonly Dictionary<Controls.Button, Text> buttonLabels = new Dictionary<Controls.Button, Text>();
        private Image stickBase, stickKnob, healthFill, healthBack, damageFlash, scope, reloadRing;
        private Image[] cross = new Image[4];
        private Image scopeLine1, scopeLine2;

        // ---- PSO-1 reticle of the SVD, drawn in thousandths (1/6000 of a circle) at the real angle:
        // the main chevron (the sights are zeroed at 100 m), three holdover chevrons below it, the
        // windage scale with a tick each thousandth (longer at 5 and 10) and the rangefinder at the
        // lower left: put the feet of a man (1.7 m) on the bottom line, where his head touches the
        // curve reads the distance in hundreds of metres.
        private RectTransform pso;
        private readonly List<(RectTransform rt, Vector2 a, Vector2 b, float width)> psoLines = new List<(RectTransform, Vector2, Vector2, float)>();
        private readonly List<(RectTransform rt, Vector2 at)> psoLabels = new List<(RectTransform, Vector2)>();
        private float psoFov = -1f;
        private static readonly Color ReticleInk = new Color(0.04f, 0.04f, 0.04f, 0.95f);

        private void BuildPso1()
        {
            pso = UIKit.Node(scope.transform, "PSO-1").At(0.5f, 0.5f, 0, 0, 10, 10);
            void Line(Vector2 a, Vector2 b, float width = 2.6f)
            {
                var img = UIKit.Img(pso, ReticleInk);
                psoLines.Add((img.rectTransform, a, b, width));
            }
            void Label(string text, Vector2 at)
            {
                var t = UIKit.Txt(pso, text, 17, TextAnchor.MiddleCenter, ReticleInk);
                psoLabels.Add((t.rectTransform, at));
            }
            void Chevron(float y, float halfWidth, float height)
            {
                Line(new Vector2(-halfWidth, y - height), new Vector2(0, y), 3f);
                Line(new Vector2(halfWidth, y - height), new Vector2(0, y), 3f);
            }
            Chevron(0f, 0.75f, 1.5f);
            Chevron(-3.2f, 0.5f, 1f);
            Chevron(-5.6f, 0.5f, 1f);
            Chevron(-8f, 0.5f, 1f);
            foreach (int side in new[] { -1, 1 })
            {
                Line(new Vector2(side * 1.6f, 0f), new Vector2(side * 10f, 0f), 1.8f);
                for (int i = 1; i <= 10; i++)
                {
                    float h = i % 5 == 0 ? 1.2f : 0.6f;
                    Line(new Vector2(side * i, 0f), new Vector2(side * i, -h), 2f);
                }
                Label("10", new Vector2(side * 10f, 1.1f));
            }
            // rangefinder: 1.7 m tall at 200 to 1000 m, from left to right
            const float baseY = -13f, left = -18f, right = -6f;
            float X(float d) => Mathf.Lerp(left, right, (d - 200f) / 800f);
            float Y(float d) => baseY + 1.7f / d * 1000f / 1.0472f;
            Line(new Vector2(left - 0.6f, baseY), new Vector2(right + 0.6f, baseY), 2f);
            for (float d = 200f; d < 1000f; d += 50f)
                if (((int)(d / 50f)) % 2 == 0) Line(new Vector2(X(d), Y(d)), new Vector2(X(d + 50f), Y(d + 50f)), 2f);
            for (int d = 200; d <= 1000; d += 200)
                Label((d / 100).ToString(), new Vector2(X(d), Y(d) + 1f));
        }

        /// <summary>Places the reticle for the camera's field of view (one thousandth = 1/6000 of a turn).</summary>
        private void LayoutPso1(float fov)
        {
            if (Mathf.Abs(fov - psoFov) < 0.01f) return;
            psoFov = fov;
            // the canvas is 1080 units high whatever the screen
            float k = 540f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * (Mathf.PI * 2f / 6000f);
            foreach (var (rt, a, b, width) in psoLines)
            {
                var d = (b - a) * k;
                rt.anchoredPosition = (a + b) * 0.5f * k;
                rt.sizeDelta = new Vector2(d.magnitude + width, width);
                rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
            foreach (var (rt, at) in psoLabels)
            {
                rt.anchoredPosition = at * k;
                rt.sizeDelta = new Vector2(44, 24);
            }
        }
        private Image hitMarker, crossDot;
        private Text health, ammo, reserve, weaponName, otherWeapon, timer, scoreBlue, scoreRed, center, centerSmall, killFeed, fps, modeLabel;
        private RectTransform scoreboard;
        private Text scoreboardText;
        private readonly List<Text> names = new List<Text>();
        private float hitMarkerUntil, damageAlpha, lastHealth = 100, fpsTimer, fpsValue;
        private ushort lastHits;
        private readonly List<(string text, float until)> feed = new List<(string, float)>();

        public void Build()
        {
            I = this;
            canvas = UIKit.CreateCanvas("HUD", 5);
            canvas.transform.SetParent(transform, false);
            root = (RectTransform)canvas.transform;

            damageFlash = UIKit.Img(root, new Color(0.8f, 0f, 0f, 0f), UIKit.Soft);
            damageFlash.rectTransform.Fill(-300);
            scope = UIKit.Img(root, new Color(0, 0, 0, 0.92f), UIKit.Ring);
            scope.rectTransform.At(0.5f, 0.5f, 0, 0, 1080, 1080);
            scopeLine1 = UIKit.Img(scope.transform, new Color(0, 0, 0, 0.9f));
            scopeLine1.rectTransform.At(0.5f, 0.5f, 0, 0, 1000, 3);
            scopeLine2 = UIKit.Img(scope.transform, new Color(0, 0, 0, 0.9f));
            scopeLine2.rectTransform.At(0.5f, 0.5f, 0, 0, 3, 1000);
            BuildPso1();
            foreach (var side in new[] { -1, 1 })
            {
                var bar = UIKit.Img(scope.transform, Color.black);
                bar.rectTransform.At(0.5f, 0.5f, side * (540 + 1000), 0, 2000, 2200);
            }

            // crosshair
            for (int i = 0; i < 4; i++)
            {
                cross[i] = UIKit.Img(root, new Color(1, 1, 1, 0.9f));
                cross[i].gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.6f);
            }
            crossDot = UIKit.Img(root, new Color(1, 1, 1, 0.9f), UIKit.Circle);
            crossDot.rectTransform.At(0.5f, 0.5f, 0, 0, 6, 6);
            hitMarker = UIKit.Img(root, new Color(1, 0.25f, 0.2f, 0), UIKit.Ring);
            hitMarker.rectTransform.At(0.5f, 0.5f, 0, 0, 46, 46);
            reloadRing = UIKit.Img(root, new Color(1, 1, 1, 0.8f), UIKit.Ring);
            reloadRing.type = Image.Type.Filled;
            reloadRing.fillMethod = Image.FillMethod.Radial360;
            reloadRing.rectTransform.At(0.5f, 0.5f, 0, 0, 84, 84);

            // top: timer and score
            var top = UIKit.Img(root, new Color(0, 0, 0, 0.45f), UIKit.Rounded);
            top.rectTransform.At(0.5f, 1f, 0, -14, 520, 96, 0.5f, 1f);
            timer = UIKit.Txt(top.transform, "5:00", 46).Shadowed();
            timer.rectTransform.At(0.5f, 0.5f, 0, 10, 200, 60);
            modeLabel = UIKit.Txt(top.transform, "", 20, TextAnchor.MiddleCenter, UIKit.Muted);
            modeLabel.rectTransform.At(0.5f, 0f, 0, 16, 500, 26);
            scoreBlue = UIKit.Txt(top.transform, "0", 50, TextAnchor.MiddleCenter, UIKit.Blue).Shadowed();
            scoreBlue.rectTransform.At(0f, 0.5f, 80, 4, 140, 70);
            scoreRed = UIKit.Txt(top.transform, "0", 50, TextAnchor.MiddleCenter, UIKit.Red).Shadowed();
            scoreRed.rectTransform.At(1f, 0.5f, -80, 4, 140, 70);

            // bottom left: health
            healthBack = UIKit.Img(root, new Color(0, 0, 0, 0.5f), UIKit.Rounded);
            healthBack.rectTransform.At(0f, 0f, 60, 40, 460, 34, 0f, 0f);
            healthFill = UIKit.Img(healthBack.transform, UIKit.Green, UIKit.Rounded);
            healthFill.rectTransform.Fill(4);
            healthFill.rectTransform.pivot = new Vector2(0, 0.5f);
            health = UIKit.Txt(root, "100", 40, TextAnchor.LowerLeft).Shadowed();
            health.rectTransform.At(0f, 0f, 64, 80, 300, 56, 0f, 0f);

            // weapon panel top right (tap to swap)
            var weaponPanel = UIKit.Img(root, new Color(0, 0, 0, 0.45f), UIKit.Rounded);
            weaponPanel.rectTransform.At(1f, 1f, -40, -40, 380, 200, 1f, 1f);
            weaponName = UIKit.Txt(weaponPanel.transform, "AK-47", 36, TextAnchor.UpperLeft, UIKit.Accent).Shadowed();
            weaponName.rectTransform.At(0f, 1f, 24, -16, 340, 46, 0f, 1f);
            ammo = UIKit.Txt(weaponPanel.transform, "30", 64, TextAnchor.LowerLeft).Shadowed();
            ammo.rectTransform.At(0f, 0f, 24, 44, 160, 80, 0f, 0f);
            reserve = UIKit.Txt(weaponPanel.transform, "/ 120", 30, TextAnchor.LowerLeft, UIKit.Muted);
            reserve.rectTransform.At(0f, 0f, 140, 52, 160, 50, 0f, 0f);
            otherWeapon = UIKit.Txt(weaponPanel.transform, "", 22, TextAnchor.LowerLeft, UIKit.Muted);
            otherWeapon.rectTransform.At(0f, 0f, 24, 14, 340, 30, 0f, 0f);
            var swapHint = UIKit.Txt(weaponPanel.transform, "TAP TO SWAP", 18, TextAnchor.LowerRight, UIKit.Muted);
            swapHint.rectTransform.At(1f, 0f, -18, 14, 200, 26, 1f, 0f);

            killFeed = UIKit.Txt(root, "", 28, TextAnchor.UpperRight).Shadowed();
            killFeed.rectTransform.At(1f, 1f, -40, -260, 820, 300, 1f, 1f);
            center = UIKit.Txt(root, "", 96, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
            center.rectTransform.At(0.5f, 0.5f, 0, 170, 1600, 130);
            centerSmall = UIKit.Txt(root, "", 38, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
            centerSmall.rectTransform.At(0.5f, 0.5f, 0, 80, 1600, 60);
            fps = UIKit.Txt(root, "", 22, TextAnchor.UpperLeft, UIKit.Green);
            fps.rectTransform.At(0f, 1f, 280, -20, 200, 30, 0f, 1f);

            // touch controls
            stickBase = UIKit.Img(root, new Color(1, 1, 1, 0.12f), UIKit.Circle);
            stickBase.rectTransform.sizeDelta = Vector2.one * Controls.StickRadius * 2f;
            stickKnob = UIKit.Img(root, new Color(1, 1, 1, 0.45f), UIKit.Circle);
            stickKnob.rectTransform.sizeDelta = Vector2.one * 110f;
            AddButton(Controls.Button.Fire, "gps_fixed", "FIRE");
            AddButton(Controls.Button.FireLeft, "gps_fixed", "");
            AddButton(Controls.Button.Aim, "center_focus_strong", "AIM");
            AddButton(Controls.Button.Jump, "expand_less", "JUMP");
            AddButton(Controls.Button.Crouch, "keyboard_double_arrow_down", "CROUCH");
            AddButton(Controls.Button.Reload, "autorenew", "RELOAD");
            AddButton(Controls.Button.Swap, "swap_horiz", "SWAP");
            AddButton(Controls.Button.Prone, "airline_seat_flat", "PRONE");
            AddButton(Controls.Button.Grenade, "grenade", "x2");
            AddButton(Controls.Button.Ammo, "ammo", "AMMO");
            AddButton(Controls.Button.Pause, "pause", "");
            AddButton(Controls.Button.Score, "leaderboard", "");

            scoreboard = UIKit.Panel(root, 1300, 760);
            var sbTitle = UIKit.Txt(scoreboard, "SCOREBOARD", 44, TextAnchor.UpperCenter, UIKit.Accent);
            sbTitle.rectTransform.At(0.5f, 1f, 0, -24, 1200, 60, 0.5f, 1f);
            scoreboardText = UIKit.Txt(scoreboard, "", 32, TextAnchor.UpperLeft);
            scoreboardText.rectTransform.Fill(60);
            scoreboardText.rectTransform.offsetMax = new Vector2(-60, -100);
            scoreboard.gameObject.SetActive(false);

            for (int i = 0; i < 12; i++)
            {
                var t = UIKit.Txt(root, "", 24, TextAnchor.LowerCenter).Shadowed();
                t.rectTransform.sizeDelta = new Vector2(300, 34);
                names.Add(t);
            }

            Session.I.KillHappened += OnKill;
        }

        private void OnDestroy()
        {
            if (Session.I != null) Session.I.KillHappened -= OnKill;
        }

        private void AddButton(Controls.Button b, string icon, string label)
        {
            var img = UIKit.Img(root, new Color(1, 1, 1, 0.16f), UIKit.Circle);
            float r = Controls.ButtonRadius(b);
            img.rectTransform.At(0f, 0f, 0, 0, r * 2, r * 2);
            var ring = UIKit.Img(img.transform, new Color(1, 1, 1, 0.55f), UIKit.Ring);
            ring.rectTransform.Fill();
            var ic = UIKit.IconImage(img.transform, icon, r, new Color(1, 1, 1, 0.92f));
            float k = label.Length > 0 ? 0.24f : 0.2f;
            ic.rectTransform.anchorMin = new Vector2(k, label.Length > 0 ? 0.3f : k);
            ic.rectTransform.anchorMax = new Vector2(1 - k, label.Length > 0 ? 0.78f : 1 - k);
            ic.rectTransform.offsetMin = ic.rectTransform.offsetMax = Vector2.zero;
            var t = UIKit.Txt(img.transform, label, Mathf.RoundToInt(Mathf.Clamp(r * 0.24f, 13, 30))).Shadowed();
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 10;
            t.resizeTextMaxSize = t.fontSize;
            t.rectTransform.anchorMin = new Vector2(0.1f, 0.1f);
            t.rectTransform.anchorMax = new Vector2(0.9f, 0.32f);
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            buttons[b] = img;
            buttonLabels[b] = t;
        }

        public void SetVisible(bool visible)
        {
            if (canvas != null) canvas.gameObject.SetActive(visible);
        }

        private void OnKill(KillEvent k)
        {
            var s = Session.I;
            string killer = s.Players.TryGetValue(k.Killer, out var a) ? Rtl.Shape(a.Name) : "?";
            string victim = s.Players.TryGetValue(k.Victim, out var b) ? Rtl.Shape(b.Name) : "?";
            string weapon = Weapons.Get(k.Weapon).Name;
            string kc = Hex(a != null ? World.TeamColor(a, s) : Color.white), vc = Hex(b != null ? World.TeamColor(b, s) : Color.white);
            string line = k.Killer == k.Victim
                ? $"<color={vc}>{victim}</color> <color=#aaaaaa>eliminated</color>"
                : $"<color={kc}>{killer}</color>  <color=#ffb040>[{weapon}]</color>{(k.Headshot ? " <color=#ff5040>HEADSHOT</color>" : "")}  <color={vc}>{victim}</color>";
            feed.Add((line, Time.unscaledTime + 6f));
            if (feed.Count > 5) feed.RemoveAt(0);
            if (k.Killer == s.LocalId && k.Victim != s.LocalId)
            {
                ShowCenter(k.Headshot ? "HEADSHOT!" : "ELIMINATED", victim, 1.5f);
                Sfx.Play("pickup", 0.8f, 1.2f);
                LocalPlayer.I?.OnKill();
            }
        }

        private float centerUntil;

        private void ShowCenter(string big, string small, float seconds)
        {
            center.text = big;
            centerSmall.text = small;
            centerUntil = Time.unscaledTime + seconds;
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(c, Color.white, 0.25f));

        private void LateUpdate()
        {
            var s = Session.I;
            if (canvas == null || !canvas.gameObject.activeSelf || s == null) return;
            var me = s.Local;
            var lp = LocalPlayer.I;
            float now = Time.unscaledTime;
            float w = UIKit.Width;
            bool tdm = s.Config.Mode == Mode.TeamDeathmatch;

            // timer and scores
            int seconds = Mathf.CeilToInt(s.TimeLeft);
            timer.text = $"{seconds / 60}:{seconds % 60:00}";
            modeLabel.text = (tdm ? "TEAM DEATHMATCH" : "FREE FOR ALL") + "  -  FIRST TO " + s.Config.KillLimit;
            if (tdm)
            {
                scoreBlue.text = s.TeamScore[0].ToString();
                scoreRed.text = s.TeamScore[1].ToString();
            }
            else
            {
                var ranked = s.Players.Values.OrderByDescending(p => p.Kills).ToList();
                scoreBlue.text = me != null ? me.Kills.ToString() : "0";
                scoreRed.text = ranked.Count > 0 ? ranked[0].Kills.ToString() : "0";
            }

            // health
            float hp = me != null ? me.Health : 0;
            health.text = Mathf.CeilToInt(hp).ToString();
            healthFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(hp / 100f), 1f);
            healthFill.color = hp > 50 ? UIKit.Green : hp > 25 ? new Color(1f, 0.75f, 0.2f) : UIKit.Red;
            if (me != null && hp < lastHealth - 0.5f && me.Alive)
            {
                damageAlpha = Mathf.Min(0.75f, damageAlpha + (lastHealth - hp) / 60f);
                if (Sfx.Count("pain_") == 0) Sfx.Play("hurt", 0.6f);   // otherwise the agent cries out itself
            }
            lastHealth = hp;
            damageAlpha = Mathf.MoveTowards(damageAlpha, hp < 30 && me != null && me.Alive ? 0.25f : 0f, Time.unscaledDeltaTime * 0.9f);
            damageFlash.color = new Color(0.75f, 0f, 0f, damageAlpha);

            // hit marker: the host counts our hits
            if (me != null && me.Hits != lastHits)
            {
                if (lastHits != 0 || me.Hits < 5) { hitMarkerUntil = now + 0.18f; Sfx.Play("hit", 0.5f); }
                lastHits = me.Hits;
            }
            hitMarker.color = new Color(1f, 0.3f, 0.25f, now < hitMarkerUntil ? 1f : 0f);

            // weapon
            bool scoped = CameraRig.I != null && CameraRig.I.Scope;
            if (lp != null)
            {
                var def = lp.Current;
                weaponName.text = def.Name;
                ammo.text = lp.Mag[lp.Slot].ToString();
                ammo.color = lp.Mag[lp.Slot] <= def.MagSize / 4 ? UIKit.Red : UIKit.TextColor;
                reserve.text = "/ " + lp.Reserve[lp.Slot];
                otherWeapon.text = "NEXT: " + lp.Other.Name;
                reloadRing.enabled = lp.Reloading;
                reloadRing.fillAmount = lp.ReloadProgress;
                float gap = 8f + lp.Spread * 9f;
                bool showCross = me != null && me.Alive && !scoped;
                for (int i = 0; i < 4; i++)
                {
                    cross[i].enabled = showCross;
                    var dir = i == 0 ? Vector2.up : i == 1 ? Vector2.down : i == 2 ? Vector2.left : Vector2.right;
                    cross[i].rectTransform.At(0.5f, 0.5f, dir.x * (gap + 9), dir.y * (gap + 9), dir.x != 0 ? 18 : 4, dir.y != 0 ? 18 : 4);
                }
                crossDot.enabled = showCross;
            }
            scope.enabled = scoped;
            foreach (var g in scope.GetComponentsInChildren<Image>()) g.enabled = scoped;
            bool pso1 = scoped && lp != null && lp.Current.Id == 7;
            scopeLine1.enabled = scopeLine2.enabled = scoped && !pso1;
            pso.gameObject.SetActive(pso1);
            if (pso1) LayoutPso1(CameraRig.I.Cam.fieldOfView);

            // countdown / death / center messages
            if (s.Phase == Phase.Countdown)
            {
                center.text = Mathf.CeilToInt(s.TimeLeft).ToString();
                centerSmall.text = "GET READY";
            }
            else if (me != null && !me.Alive && s.Phase == Phase.Playing)
            {
                string by = "";
                if (s.KillFeed.Any(k => k.Victim == s.LocalId))
                {
                    var lastDeath = s.KillFeed.Last(k => k.Victim == s.LocalId);
                    if (lastDeath.Killer != s.LocalId && s.Players.TryGetValue(lastDeath.Killer, out var killer)) by = "Killed by " + Rtl.Shape(killer.Name);
                }
                center.text = "YOU WERE ELIMINATED";
                centerSmall.text = by + (by.Length > 0 ? "  -  " : "") + "respawning...";
            }
            else if (now > centerUntil)
            {
                center.text = "";
                centerSmall.text = "";
            }

            // kill feed
            feed.RemoveAll(f => now > f.until);
            killFeed.text = string.Join("\n", feed.Select(f => f.text));

            // controls
            bool touchUi = Application.isMobilePlatform || Input.touchSupported || ForceTouchUi;
            float alpha = Settings.ControlsOpacity;
            foreach (var pair in buttons)
            {
                var c = Controls.ButtonCenter(pair.Key);
                float r = Controls.ButtonRadius(pair.Key);
                pair.Value.rectTransform.anchoredPosition = c;
                pair.Value.rectTransform.sizeDelta = new Vector2(r * 2, r * 2);
                bool show = (touchUi || pair.Key == Controls.Button.Pause || pair.Key == Controls.Button.Score || pair.Key == Controls.Button.Ammo) && Controls.ButtonVisible(pair.Key)
                            && (pair.Key != Controls.Button.Ammo || Controls.AmmoAvailable);
                pair.Value.enabled = show;
                foreach (var g in pair.Value.GetComponentsInChildren<Graphic>()) g.enabled = show;
                bool held = Controls.Held.Contains(pair.Key);
                bool active = pair.Key == Controls.Button.Aim && lp != null && lp.Aiming;
                pair.Value.color = pair.Key == Controls.Button.Fire || pair.Key == Controls.Button.FireLeft
                    ? new Color(1f, 0.3f, 0.2f, (held ? 0.55f : 0.32f) * alpha)
                    : new Color(1, 1, 1, (held || active ? 0.4f : 0.14f) * alpha);
            }
            if (buttonLabels.TryGetValue(Controls.Button.Grenade, out var grenadeLabel))
                grenadeLabel.text = "x" + (lp != null ? lp.Grenades : 0);
            stickBase.enabled = stickKnob.enabled = touchUi;
            var stickCenter = Controls.StickActive ? Controls.StickCenter : new Vector2(260, 250);
            stickBase.rectTransform.At(0f, 0f, stickCenter.x, stickCenter.y, Controls.StickRadius * 2, Controls.StickRadius * 2);
            var knob = Controls.StickActive ? Controls.StickKnob : stickCenter;
            stickKnob.rectTransform.At(0f, 0f, knob.x, knob.y, 110, 110);

            // names over heads
            UpdateNames(s, me);

            // scoreboard
            bool showBoard = Controls.ScoreHeld;
            scoreboard.gameObject.SetActive(showBoard);
            if (showBoard) scoreboardText.text = ScoreboardText(s);

            // fps
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer > 0.5f) { fpsValue = 1f / Mathf.Max(0.001f, Time.unscaledDeltaTime); fpsTimer = 0; }
            fps.text = Settings.ShowFps ? Mathf.RoundToInt(fpsValue) + " FPS" : "";
        }

        private void UpdateNames(Session s, PlayerState me)
        {
            var cam = CameraRig.I != null ? CameraRig.I.Cam : null;
            int used = 0;
            if (cam != null && World.I != null && me != null)
            {
                float scale = 1080f / Mathf.Max(1, Screen.height);
                foreach (var agent in World.I.Agents.Values)
                {
                    if (used >= names.Count) break;
                    var p = agent.State;
                    if (p == null || p.Id == s.LocalId || !p.Alive) continue;
                    var headPos = agent.transform.position + Vector3.up * 2.1f;
                    var screen = cam.WorldToScreenPoint(headPos);
                    if (screen.z < 0.5f || screen.z > 45f) continue;
                    bool friend = !World.I.IsEnemy(me, p, s);
                    if (!friend && (screen.z > 25f || !World.I.LineOfSight(cam.transform.position, headPos))) continue;
                    var t = names[used++];
                    t.enabled = true;
                    t.text = Rtl.Shape(p.Name);
                    t.color = friend ? new Color(0.55f, 0.75f, 1f) : new Color(1f, 0.55f, 0.5f);
                    t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
                    t.rectTransform.anchoredPosition = new Vector2(screen.x, screen.y) * scale;
                }
            }
            for (int i = used; i < names.Count; i++) names[i].enabled = false;
        }

        public static string ScoreboardText(Session s)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<color=#9aa4b0>PLAYER                                   KILLS     DEATHS</color>");
            IEnumerable<IGrouping<int, PlayerState>> groups = s.Config.Mode == Mode.TeamDeathmatch
                ? s.Players.Values.GroupBy(p => (int)p.Team).OrderBy(g => g.Key)
                : s.Players.Values.GroupBy(p => 0);
            foreach (var g in groups)
            {
                if (s.Config.Mode == Mode.TeamDeathmatch)
                    sb.AppendLine(g.Key == 0 ? $"<color=#4d84ff>TEAM BLUE   {s.TeamScore[0]}</color>" : $"<color=#f24d40>TEAM RED   {s.TeamScore[1]}</color>");
                foreach (var p in g.OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths))
                {
                    string name = Rtl.Shape(p.Name.Length > 18 ? p.Name.Substring(0, 18) : p.Name);
                    string line = name.PadRight(30) + p.Kills.ToString().PadLeft(10) + p.Deaths.ToString().PadLeft(12);
                    sb.AppendLine(p.Id == s.LocalId ? $"<color=#ffb040>{line}</color>" : line);
                }
            }
            return sb.ToString();
        }
    }
}
