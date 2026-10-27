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
        private Image stickBase, stickKnob, damageFlash, scope, reloadRing;
        private Image[] cross = new Image[4];
        private Image scopeLine1, scopeLine2;

        // ---- PSO-1 reticle of the SVD, drawn in thousandths (1/6000 of a circle) as in the real eyepiece:
        // the main chevron (where the bullet goes: shots fly straight), three holdover chevrons below it, the
        // windage scale with a tick each thousandth (longer at 5 and 10) and the rangefinder at the
        // lower left: put the feet of a man (1.7 m) on the bottom line, where his head touches the
        // curve reads the distance in hundreds of metres.
        private RectTransform pso;
        private readonly List<(RectTransform rt, Vector2 a, Vector2 b, float width)> psoLines = new List<(RectTransform, Vector2, Vector2, float)>();
        private readonly List<(RectTransform rt, Vector2 at)> psoLabels = new List<(RectTransform, Vector2)>();
        private float psoFov = -1f;
        // lit red as the PSO-1's own reticle lamp, with a thin dark edge: seen on a bright wall and at night
        private static readonly Color ReticleInk = new Color(1f, 0.16f, 0.08f, 1f);

        private void BuildPso1()
        {
            pso = UIKit.Node(scope.transform, "PSO-1").At(0.5f, 0.5f, 0, 0, 10, 10);
            void Line(Vector2 a, Vector2 b, float width = 2.6f)
            {
                var img = UIKit.Img(pso, ReticleInk);
                img.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.55f);
                psoLines.Add((img.rectTransform, a, b, width * 1.5f));
            }
            void Label(string text, Vector2 at)
            {
                var t = UIKit.Txt(pso, text, 30, TextAnchor.MiddleCenter, ReticleInk);
                t.fontStyle = FontStyle.Bold;
                t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
                psoLabels.Add((t.rectTransform, at));
            }
            void Chevron(float y, float halfWidth, float height)
            {
                Line(new Vector2(-halfWidth, y - height), new Vector2(0, y), 3f);
                Line(new Vector2(halfWidth, y - height), new Vector2(0, y), 3f);
            }
            Chevron(0f, 1f, 2f);
            Chevron(-3.2f, 0.6f, 1.2f);
            Chevron(-5.6f, 0.6f, 1.2f);
            Chevron(-8f, 0.6f, 1.2f);
            foreach (int side in new[] { -1, 1 })
            {
                Line(new Vector2(side * 1.6f, 0f), new Vector2(side * 10f, 0f), 1.8f);
                for (int i = 1; i <= 10; i++)
                {
                    float h = i % 5 == 0 ? 1.2f : 0.6f;
                    Line(new Vector2(side * i, 0f), new Vector2(side * i, -h), 2f);
                }
                Label("10", new Vector2(side * 10f, 1.6f));
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

        /// <summary>
        /// Places the reticle in the scope circle (500 canvas units to its edge), the same size whatever the
        /// zoom of the game: drawn larger than in the real eyepiece (52 thousandths to the edge) so the
        /// scale, the chevrons and the rangefinder read on a phone.
        /// </summary>
        private void LayoutPso1(float fov)
        {
            if (psoFov > 0f) return;
            psoFov = fov;
            const float k = 500f / 30f;
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
                rt.sizeDelta = new Vector2(60, 34);
            }
        }
        private Image hitMarker, crossDot;
        private Text timer, scoreBlue, scoreRed, center, centerSmall, killFeed, fps, playerName, kills, deaths, weaponLine, healthNumber;
        private readonly Image[] hpFill = new Image[5];
        private RectTransform rankBox, stopBox;
        private RectTransform scoreboard;
        private ResultsTable board;
        private float boardUntil, boardFilled;
        private const float BoardSeconds = 8f;
        private readonly List<Text> names = new List<Text>();
        private float hitMarkerUntil, damageAlpha, lastHealth = 100, fpsTimer, fpsValue;
        private ushort lastHits;
        private readonly List<(string text, float until)> feed = new List<(string, float)>();
        private Image killToast;
        private Text killToastText;
        private float killToastUntil;

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

            // as in Battle of Agents: top left the player (name, kills, deaths), the health in five
            // segments under it, the weapon line under that; top right the time, the rank and STOP
            var player = UIKit.Img(root, new Color(0, 0, 0, 0.55f));
            player.rectTransform.At(0f, 1f, 0, 0, 720, 72, 0f, 1f);
            playerName = UIKit.Txt(player.transform, "", 44, TextAnchor.MiddleLeft).Shadowed();
            playerName.fontStyle = FontStyle.Bold;
            playerName.rectTransform.At(0f, 0.5f, 22, 0, 300, 64, 0f, 0.5f);
            kills = UIKit.Txt(player.transform, "KILLS: 0", 30, TextAnchor.MiddleLeft).Shadowed();
            kills.rectTransform.At(0f, 0.5f, 340, -4, 180, 50, 0f, 0.5f);
            deaths = UIKit.Txt(player.transform, "DEATHS: 0", 30, TextAnchor.MiddleLeft).Shadowed();
            deaths.rectTransform.At(0f, 0.5f, 520, -4, 200, 50, 0f, 0.5f);
            for (int i = 0; i < 5; i++)
            {
                var seg = UIKit.Img(root, new Color(0, 0, 0, 0.45f));
                seg.rectTransform.At(0f, 1f, 14 + i * 108, -82, 100, 26, 0f, 1f);
                hpFill[i] = UIKit.Img(seg.transform, Color.white);
                hpFill[i].rectTransform.Fill();
                hpFill[i].rectTransform.pivot = new Vector2(0, 0.5f);
            }
            healthNumber = UIKit.Txt(root, "100", 26, TextAnchor.MiddleLeft).Shadowed();
            healthNumber.rectTransform.At(0f, 1f, 560, -80, 100, 30, 0f, 1f);
            var weaponBox = UIKit.Img(root, new Color(0, 0, 0, 0.55f));
            weaponBox.rectTransform.At(0f, 1f, 0, -118, 440, 52, 0f, 1f);
            weaponLine = UIKit.Txt(weaponBox.transform, "", 32, TextAnchor.MiddleLeft).Shadowed();
            weaponLine.rectTransform.At(0f, 0.5f, 22, 0, 420, 48, 0f, 0.5f);

            var timeBox = UIKit.Img(root, new Color(0, 0, 0, 0.55f));
            timeBox.rectTransform.At(1f, 1f, -500, -8, 240, 80, 1f, 1f);
            var ends = UIKit.Txt(timeBox.transform, "MATCH ENDS IN", 22, TextAnchor.MiddleCenter).Shadowed();
            ends.rectTransform.At(0.5f, 1f, 0, -18, 230, 30);
            timer = UIKit.Txt(timeBox.transform, "5:00", 34, TextAnchor.MiddleCenter).Shadowed();
            timer.rectTransform.At(0.5f, 0f, 0, 22, 230, 40);
            rankBox = UIKit.Img(root, new Color(0, 0, 0, 0.55f)).rectTransform;
            var rankHead = UIKit.Txt(rankBox, "RANK", 22, TextAnchor.MiddleCenter).Shadowed();
            rankHead.rectTransform.At(0.5f, 1f, 0, -14, 300, 26);
            scoreBlue = UIKit.Txt(rankBox, "", 30, TextAnchor.MiddleCenter, UIKit.Blue).Shadowed();
            scoreBlue.rectTransform.At(0.25f, 0f, 0, 22, 150, 40);
            scoreRed = UIKit.Txt(rankBox, "", 30, TextAnchor.MiddleCenter, UIKit.Red).Shadowed();
            scoreRed.rectTransform.At(0.75f, 0f, 0, 22, 150, 40);
            var divider = UIKit.Img(rankBox, new Color(1, 1, 1, 0.25f));
            divider.rectTransform.At(0.5f, 0f, 0, 22, 2, 34);
            stopBox = UIKit.Img(root, new Color(0, 0, 0, 0.55f)).rectTransform;
            UIKit.Txt(stopBox, "STOP", 32, TextAnchor.MiddleCenter).Shadowed().rectTransform.Fill();

            // a short see-through toast for every kill, for everyone: killer in blue-green, victim in red
            killToast = UIKit.Img(root, new Color(0, 0, 0, 0.35f), UIKit.Rounded);
            killToast.rectTransform.At(0.5f, 1f, 0, -128, 760, 60, 0.5f, 1f);
            killToastText = UIKit.Txt(killToast.transform, "", 32, TextAnchor.MiddleCenter).Shadowed();
            killToastText.rectTransform.Fill();
            killToast.gameObject.SetActive(false);
            killFeed = UIKit.Txt(root, "", 28, TextAnchor.UpperRight).Shadowed();
            killFeed.rectTransform.At(1f, 1f, -40, -110, 820, 300, 1f, 1f);
            center = UIKit.Txt(root, "", 96, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
            center.rectTransform.At(0.5f, 0.5f, 0, 170, 1600, 130);
            centerSmall = UIKit.Txt(root, "", 38, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
            centerSmall.rectTransform.At(0.5f, 0.5f, 0, 80, 1600, 60);
            fps = UIKit.Txt(root, "", 22, TextAnchor.UpperLeft, UIKit.Green);
            fps.rectTransform.At(0f, 1f, 460, -128, 160, 30, 0f, 1f);

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
            AddButton(Controls.Button.Heal, "bloodtype", "BLOOD");
            AddButton(Controls.Button.Pause, "pause", "");
            AddButton(Controls.Button.Score, "leaderboard", "");

            // RANK: the standings as on the results page, for a while (tap again to close)
            scoreboard = UIKit.Panel(root, 1640, 720, -20);
            board = new ResultsTable(scoreboard, 700);
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
            KillCam.OnKill(k);
            feed.Add((line, Time.unscaledTime + 6f));
            if (feed.Count > 5) feed.RemoveAt(0);
            if (k.Killer != k.Victim)
            {
                killToastText.text = $"<color=#2fe0c4>{killer}</color>  <color=#e8e8e8>killed</color>  <color=#ff4a4a>{victim}</color>";
                killToastUntil = Time.unscaledTime + 2.4f;
                killToast.gameObject.SetActive(true);
            }
            if (k.Killer == s.LocalId && k.Victim != s.LocalId)
            {
                ShowCenter(k.Headshot ? "HEADSHOT!" : "ELIMINATED", victim, 1.5f);
                Sfx.Play("pickup", 0.8f, 1.2f);
                LocalPlayer.I?.OnKill();
            }
        }

        private float centerUntil;

        public void ShowCenter(string big, string small, float seconds)
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
            // the first kill film: only its own bars and caption on the screen
            canvas.enabled = !KillCam.Active;
            var me = s.Local;
            var lp = LocalPlayer.I;
            float now = Time.unscaledTime;
            float w = UIKit.Width;
            bool tdm = s.Config.Mode == Mode.TeamDeathmatch;

            // timer and scores
            int seconds = Mathf.CeilToInt(s.TimeLeft);
            timer.text = $"{seconds / 60}:{seconds % 60:00}";
            if (tdm && me != null && me.Team > 0)
            {
                // my team and the best of the others, each in its own colour
                int mine = me.Team;
                int rival = Enumerable.Range(1, MatchConfig.MaxTeams).Where(t => t != mine && s.Players.Values.Any(p => p.Team == t)).OrderByDescending(t => s.TeamScore[t]).DefaultIfEmpty(mine % MatchConfig.MaxTeams + 1).First();
                scoreBlue.text = $"T{mine}  {s.TeamScore[mine]}";
                scoreBlue.color = MatchConfig.TeamColor(mine);
                scoreRed.text = $"T{rival}  {s.TeamScore[rival]}";
                scoreRed.color = MatchConfig.TeamColor(rival);
            }
            else
            {
                var ranked = s.Players.Values.OrderByDescending(p => p.Kills).ToList();
                int place = me != null ? ranked.IndexOf(me) + 1 : 0;
                scoreBlue.text = "SOLO #" + Mathf.Max(1, place);
                scoreRed.text = "TOP " + (ranked.Count > 0 ? ranked[0].Kills : 0);
                scoreBlue.color = UIKit.Blue;
                scoreRed.color = UIKit.Red;
            }
            if (me != null)
            {
                playerName.text = Rtl.Shape(me.Name);
                kills.text = "KILLS: " + me.Kills;
                deaths.text = "DEATHS: " + me.Deaths;
            }

            // health: five segments of 20
            float hp = me != null ? me.Health : 0;
            healthNumber.text = Mathf.CeilToInt(hp).ToString();
            var segColor = hp > 50 ? Color.white : hp > 25 ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.3f, 0.25f);
            for (int i = 0; i < 5; i++)
            {
                hpFill[i].rectTransform.anchorMax = new Vector2(Mathf.Clamp01((hp - i * 20f) / 20f), 1f);
                hpFill[i].color = segColor;
            }
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
                int mags = Mathf.CeilToInt(lp.Reserve[lp.Slot] / (float)def.MagSize);
                bool low = lp.Mag[lp.Slot] <= def.MagSize / 4;
                weaponLine.text = $"{def.Name}: {(low ? "<color=#ff5a4a>" : "")}{lp.Mag[lp.Slot]}{(low ? "</color>" : "")}/{lp.Reserve[lp.Slot]} ({mags})";
                reloadRing.enabled = lp.Reloading;
                reloadRing.fillAmount = lp.ReloadProgress;
                float gap = lp.Current.Pellets > 1 ? 8f + lp.Spread * 9f : 7f;   // shots go exactly to the middle: a small fixed cross
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

            // kill toast: fades out over its last half second
            if (killToast.gameObject.activeSelf)
            {
                float left = killToastUntil - now;
                if (left <= 0f) killToast.gameObject.SetActive(false);
                else
                {
                    float a = Mathf.Clamp01(left / 0.5f);
                    killToast.color = new Color(0, 0, 0, 0.35f * a);
                    killToastText.color = new Color(1, 1, 1, a);
                }
            }
            // kill feed
            feed.RemoveAll(f => now > f.until);
            killFeed.text = string.Join("\n", feed.Select(f => f.text));

            // controls
            bool touchUi = Application.isMobilePlatform || Input.touchSupported || ForceTouchUi;
            float alpha = Settings.ControlsOpacity;
            // RANK and STOP are drawn as boxes where the score and pause buttons are
            foreach (var (box, b) in new[] { (rankBox, Controls.Button.Score), (stopBox, Controls.Button.Pause) })
            {
                var c = Controls.ButtonCenter(b);
                float r = Controls.ButtonRadius(b);
                box.gameObject.SetActive(Controls.ButtonVisible(b));
                box.anchorMin = box.anchorMax = Vector2.zero;
                box.pivot = new Vector2(0.5f, 0.5f);
                box.anchoredPosition = c;
                box.sizeDelta = b == Controls.Button.Score ? new Vector2(r * 6.2f, r * 1.5f) : new Vector2(r * 2.9f, r * 1.45f);
                box.GetComponent<Image>().color = new Color(0, 0, 0, Controls.Held.Contains(b) ? 0.75f : 0.55f);
            }
            foreach (var pair in buttons)
            {
                var c = Controls.ButtonCenter(pair.Key);
                float r = Controls.ButtonRadius(pair.Key);
                pair.Value.rectTransform.anchoredPosition = c;
                pair.Value.rectTransform.sizeDelta = new Vector2(r * 2, r * 2);
                bool show = (touchUi || pair.Key == Controls.Button.Ammo) && Controls.ButtonVisible(pair.Key) && pair.Key != Controls.Button.Pause && pair.Key != Controls.Button.Score
                            && (pair.Key != Controls.Button.Ammo || Controls.AmmoAvailable) && (pair.Key != Controls.Button.Heal || Controls.HealAvailable);
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
            // scoreboard: a tap on RANK shows it for a while, another tap closes it; held, it stays
            if (Controls.ScorePressed)
            {
                boardUntil = now < boardUntil ? 0f : now + BoardSeconds;
                if (boardUntil > 0f) board.ByTeam = ResultsTable.HasTeams(s);
            }
            bool showBoard = Controls.ScoreHeld || now < boardUntil;
            if (showBoard && (!scoreboard.gameObject.activeSelf || now - boardFilled > 0.5f)) { board.Fill(s); boardFilled = now; }
            scoreboard.gameObject.SetActive(showBoard);

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
    }
}
