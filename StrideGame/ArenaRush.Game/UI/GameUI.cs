using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Input;
using ArenaRush.Combat;

namespace ArenaRush.UI
{
    /// <summary>
    /// All screens of the game: splash, main menu, settings, pause, victory/defeat and the in-game HUD.
    /// Drawn through <see cref="Overlay"/>; taps are hit-tested here.
    /// </summary>
    public class GameUI : SyncScript
    {
        public SpriteFont Font { get; set; }

        private static bool splashShown;

        private static readonly Color4 Accent = new Color4(1f, 0.62f, 0.1f, 1f);
        private static readonly Color4 Panel = new Color4(0.04f, 0.05f, 0.06f, 0.78f);
        private static readonly Color4 ButtonColor = new Color4(0.1f, 0.11f, 0.12f, 0.92f);

        private readonly Dictionary<int, Vector2> pressedAt = new();
        private readonly List<Vector2> clicks = new();
        private float stateTime;
        private FlowState lastState;
        private float fps;

        public override void Start()
        {
            Priority = -1000; // run first: start the overlay frame before the other scripts
            Overlay.Font = Font;
            GameOptions.Load();
            GameOptions.ApplyGraphics(SceneSystem);

            if (GameFlow.SkipMenus || GameFlow.RestartRequested)
            {
                GameFlow.RestartRequested = false;
                GameFlow.ResetMatch();
                GameFlow.Set(FlowState.Playing);
            }
            else
            {
                GameFlow.Set(splashShown ? FlowState.MainMenu : FlowState.Splash);
            }
            lastState = GameFlow.State;
        }

        public override void Update()
        {
            var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            Overlay.BeginFrame();
            CollectClicks();

            if (GameFlow.State != lastState)
            {
                lastState = GameFlow.State;
                stateTime = 0;
            }
            stateTime += dt;
            if (dt > 0) fps = MathUtil.Lerp(fps, 1f / dt, 0.05f);

            switch (GameFlow.State)
            {
                case FlowState.Splash: DrawSplash(); break;
                case FlowState.MainMenu: DrawMainMenu(); break;
                case FlowState.Settings: DrawSettings(); break;
                case FlowState.Playing:
                    DrawHud();
                    if (TouchState.PausePressed) GameFlow.Set(FlowState.Paused);
                    break;
                case FlowState.Paused:
                    DrawHud();
                    DrawPause();
                    break;
                case FlowState.Victory:
                case FlowState.Defeat:
                    DrawHud();
                    if (stateTime > 1.2f) DrawEnd(GameFlow.State == FlowState.Victory);
                    break;
            }

            if (GameOptions.ShowFps)
                Overlay.Text($"{fps:0} FPS", new Vector2(Overlay.Width - 12, Overlay.Height - 30), 18, new Color4(0.6f, 1f, 0.6f, 0.9f), 1, 50);
        }

        // ------------------------------------------------------------------ input
        private void CollectClicks()
        {
            clicks.Clear();
            foreach (var ev in Input.PointerEvents)
            {
                var p = Overlay.ToVirtual(ev.Position);
                if (ev.EventType == PointerEventType.Pressed)
                    pressedAt[ev.PointerId] = p;
                else if (ev.EventType == PointerEventType.Released)
                {
                    if (pressedAt.TryGetValue(ev.PointerId, out var start) && (start - p).Length() < 40f)
                        clicks.Add(p);
                    pressedAt.Remove(ev.PointerId);
                }
            }
        }

        private bool Button(string label, RectangleF rect, bool primary = false, int layer = 20)
        {
            var hovered = false;
            foreach (var p in pressedAt.Values)
                hovered |= rect.Contains(p);
            Overlay.Rect(rect, hovered ? new Color4(0.25f, 0.25f, 0.27f, 0.95f) : ButtonColor, layer);
            Overlay.Rect(new RectangleF(rect.X, rect.Y, 6, rect.Height), primary ? Accent : new Color4(0.55f, 0.58f, 0.6f, 1f), layer);
            Overlay.Text(label, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2 - 17), 30, Color.White, 0, layer + 1);
            foreach (var c in clicks)
            {
                if (rect.Contains(c))
                {
                    SoundBank.Play("click", 0.7f);
                    return true;
                }
            }
            return false;
        }

        private void Dim(float alpha, int layer = 10) =>
            Overlay.Rect(new RectangleF(0, 0, Overlay.Width, Overlay.Height), new Color4(0, 0, 0, alpha), layer);

        private void Title(string text, float y, float size = 64, int layer = 21)
        {
            Overlay.Text(text, new Vector2(Overlay.Width / 2, y), size, Accent, 0, layer);
        }

        // ------------------------------------------------------------------ screens
        private void DrawSplash()
        {
            var fadeIn = MathUtil.Clamp(stateTime / 0.8f, 0, 1);
            Overlay.Rect(new RectangleF(0, 0, Overlay.Width, Overlay.Height), new Color4(0.02f, 0.02f, 0.03f, 1f - 0.35f * fadeIn), 10);
            var c = new Color4(Accent.R, Accent.G, Accent.B, fadeIn);
            Overlay.Text("TROPICAL STRIKE", new Vector2(Overlay.Width / 2, 250), 86, c, 0, 21);
            Overlay.Text("THIRD-PERSON ACTION", new Vector2(Overlay.Width / 2, 350), 28, new Color4(1, 1, 1, fadeIn * 0.85f), 0, 21);

            var barW = 420f;
            var progress = MathUtil.Clamp(stateTime / 2.6f, 0, 1);
            var bar = new RectangleF(Overlay.Width / 2 - barW / 2, 470, barW, 8);
            Overlay.Rect(bar, new Color4(1, 1, 1, 0.15f * fadeIn), 20);
            Overlay.Rect(new RectangleF(bar.X, bar.Y, barW * progress, 8), c, 21);
            Overlay.Text("Made with the Stride engine  -  CC0 assets by Poly Haven", new Vector2(Overlay.Width / 2, 650), 18, new Color4(1, 1, 1, 0.5f * fadeIn), 0, 21);

            if (stateTime > 2.8f || (stateTime > 0.6f && clicks.Count > 0))
            {
                splashShown = true;
                GameFlow.Set(FlowState.MainMenu);
            }
        }

        private void DrawMainMenu()
        {
            var panel = new RectangleF(60, 0, 470, Overlay.Height);
            Overlay.Rect(panel, Panel, 10);
            Overlay.Text("TROPICAL", new Vector2(panel.X + 40, 70), 62, Accent, -1, 21);
            Overlay.Text("STRIKE", new Vector2(panel.X + 40, 135), 62, Color.White, -1, 21);
            Overlay.Text("Clear the factory. Survive.", new Vector2(panel.X + 42, 215), 20, new Color4(1, 1, 1, 0.7f), -1, 21);

            var x = panel.X + 40;
            if (Button("PLAY", new RectangleF(x, 280, 390, 70), true))
            {
                GameFlow.ResetMatch();
                GameFlow.Set(FlowState.Playing);
            }
            if (Button("SETTINGS", new RectangleF(x, 370, 390, 70)))
            {
                GameFlow.SettingsReturnState = FlowState.MainMenu;
                GameFlow.Set(FlowState.Settings);
            }
            if (Button("QUIT", new RectangleF(x, 460, 390, 70)))
                (Game as Stride.Games.GameBase)?.Exit();

            Overlay.Text("WEAPONS:  AK-47   M4A1   PISTOL", new Vector2(panel.X + 42, 600), 18, new Color4(1, 1, 1, 0.6f), -1, 21);
            Overlay.Text("v1.0 test build", new Vector2(panel.X + 42, 640), 16, new Color4(1, 1, 1, 0.4f), -1, 21);
        }

        private void DrawSettings()
        {
            Dim(0.65f);
            var w = 760f;
            var x = Overlay.Width / 2 - w / 2;
            Overlay.Rect(new RectangleF(x, 60, w, 600), Panel, 11);
            Title("SETTINGS", 80, 52);

            var y = 170f;
            SettingRow("GRAPHICS", GameOptions.QualityNames[GameOptions.Quality], x, ref y,
                d => { GameOptions.Quality = Math.Clamp(GameOptions.Quality + d, 0, 2); GameOptions.ApplyGraphics(SceneSystem); });
            SettingRow("LOOK SENSITIVITY", $"x{GameOptions.LookSensitivity:0.0}", x, ref y,
                d => GameOptions.LookSensitivity = Math.Clamp(GameOptions.LookSensitivity + d * 0.1f, 0.3f, 3f));
            SettingRow("SOUND VOLUME", $"{GameOptions.Volume * 100:0}%", x, ref y,
                d => GameOptions.Volume = Math.Clamp(GameOptions.Volume + d * 0.1f, 0f, 1f));
            SettingRow("INVERT LOOK Y", GameOptions.InvertY ? "ON" : "OFF", x, ref y, _ => GameOptions.InvertY = !GameOptions.InvertY);
            SettingRow("SHOW FPS", GameOptions.ShowFps ? "ON" : "OFF", x, ref y, _ => GameOptions.ShowFps = !GameOptions.ShowFps);

            if (Button("BACK", new RectangleF(Overlay.Width / 2 - 150, 570, 300, 64), true))
            {
                GameOptions.Save();
                GameFlow.Set(GameFlow.SettingsReturnState);
            }
        }

        private void SettingRow(string label, string value, float x, ref float y, Action<int> change)
        {
            Overlay.Text(label, new Vector2(x + 40, y + 14), 26, Color.White, -1, 21);
            var vx = x + 470;
            if (Button("-", new RectangleF(vx, y, 64, 56)))
                change(-1);
            Overlay.Text(value, new Vector2(vx + 135, y + 14), 26, Accent, 0, 21);
            if (Button("+", new RectangleF(vx + 206, y, 64, 56)))
                change(+1);
            y += 76;
        }

        private void DrawPause()
        {
            Dim(0.55f);
            Title("PAUSED", 120);
            var x = Overlay.Width / 2 - 200;
            if (Button("RESUME", new RectangleF(x, 230, 400, 66), true))
                GameFlow.Set(FlowState.Playing);
            if (Button("RESTART", new RectangleF(x, 312, 400, 66)))
                Restart(toMenu: false);
            if (Button("SETTINGS", new RectangleF(x, 394, 400, 66)))
            {
                GameFlow.SettingsReturnState = FlowState.Paused;
                GameFlow.Set(FlowState.Settings);
            }
            if (Button("MAIN MENU", new RectangleF(x, 476, 400, 66)))
                Restart(toMenu: true);
        }

        private void DrawEnd(bool victory)
        {
            Dim(0.6f);
            Overlay.Text(victory ? "MISSION COMPLETE" : "YOU DIED", new Vector2(Overlay.Width / 2, 110), 70,
                victory ? Accent : new Color4(1f, 0.25f, 0.2f, 1f), 0, 21);
            var accuracy = GameFlow.ShotsFired > 0 ? 100f * GameFlow.ShotsHit / GameFlow.ShotsFired : 0;
            Overlay.Text($"TIME  {GameFlow.FormatTime(GameFlow.MatchTime)}", new Vector2(Overlay.Width / 2, 230), 30, Color.White, 0, 21);
            Overlay.Text($"HOSTILES  {GameFlow.EnemiesKilled} / {GameFlow.EnemiesTotal}", new Vector2(Overlay.Width / 2, 275), 30, Color.White, 0, 21);
            Overlay.Text($"ACCURACY  {accuracy:0}%", new Vector2(Overlay.Width / 2, 320), 30, Color.White, 0, 21);

            var x = Overlay.Width / 2 - 200;
            if (Button(victory ? "PLAY AGAIN" : "RETRY", new RectangleF(x, 420, 400, 66), true))
                Restart(toMenu: false);
            if (Button("MAIN MENU", new RectangleF(x, 502, 400, 66)))
                Restart(toMenu: true);
        }

        private void Restart(bool toMenu)
        {
            GameFlow.RestartRequested = !toMenu;
            GameFlow.Set(toMenu ? FlowState.MainMenu : FlowState.Playing);
            Script.AddTask(async () =>
            {
                await Script.NextFrame();
                var instance = SceneSystem.SceneInstance;
                var old = instance.RootScene;
                if (!Content.TryGetAssetUrl(old, out var url))
                    url = "MainScene";
                instance.RootScene = null;
                Content.Unload(old);
                instance.RootScene = Content.Load<Scene>(url);
            });
        }

        // ------------------------------------------------------------------ HUD
        private void DrawHud()
        {
            var W = Overlay.Width;
            const int L = 1;

            // damage flash
            if (PlayerStatus.DamageFlash > 0)
                Overlay.Rect(new RectangleF(0, 0, W, Overlay.Height), new Color4(0.8f, 0f, 0f, PlayerStatus.DamageFlash * 0.9f), 0);
            var hp = PlayerStatus.MaxHealth > 0 ? PlayerStatus.Health / PlayerStatus.MaxHealth : 0;
            if (hp < 0.35f)
                Overlay.Rect(new RectangleF(0, 0, W, Overlay.Height), new Color4(0.6f, 0f, 0f, (0.35f - hp) * 0.8f), 0);

            // health
            Overlay.Text("HEALTH", new Vector2(24, 18), 18, new Color4(1, 1, 1, 0.8f), -1, L);
            var bar = new RectangleF(24, 44, 280, 16);
            Overlay.Rect(bar, new Color4(0, 0, 0, 0.5f), L);
            Overlay.Rect(new RectangleF(bar.X, bar.Y, bar.Width * hp, bar.Height),
                hp > 0.35f ? new Color4(0.35f, 0.9f, 0.4f, 0.95f) : new Color4(1f, 0.3f, 0.2f, 0.95f), L + 1);
            var left = Math.Max(0, GameFlow.EnemiesTotal - GameFlow.EnemiesKilled);
            Overlay.Text($"HOSTILES  {left}", new Vector2(24, 72), 24, Accent, -1, L);

            // timer + pause button
            Overlay.Text(GameFlow.FormatTime(GameFlow.MatchTime), new Vector2(W / 2, 74), 22, Color.White, 0, L);
            var pc = Overlay.ToVirtual(TouchState.PauseCenter);
            Overlay.Circle(pc, TouchState.PauseRadius, new Color4(0, 0, 0, 0.45f), false, L);
            Overlay.Rect(new RectangleF(pc.X - 9, pc.Y - 12, 6, 24), Color.White, L + 1);
            Overlay.Rect(new RectangleF(pc.X + 3, pc.Y - 12, 6, 24), Color.White, L + 1);

            // weapon + ammo (top right) with the swap button
            var sc = Overlay.ToVirtual(TouchState.SwapCenter);
            Overlay.Text(PlayerStatus.WeaponName, new Vector2(sc.X - 64, sc.Y - 40), 28, Color.White, 1, L);
            Overlay.Text($"{PlayerStatus.Ammo}", new Vector2(sc.X - 64, sc.Y - 6), 40,
                PlayerStatus.Ammo == 0 ? new Color4(1f, 0.3f, 0.2f, 1f) : Accent, 1, L);
            Overlay.Text($"/ {PlayerStatus.Reserve}", new Vector2(sc.X - 64, sc.Y + 38), 20, new Color4(1, 1, 1, 0.7f), 1, L);
            Overlay.Circle(sc, TouchState.SwapRadius, new Color4(0, 0, 0, 0.45f), false, L);
            Overlay.Circle(sc, TouchState.SwapRadius, new Color4(1, 1, 1, 0.7f), true, L + 1);
            Overlay.Text("SWAP", new Vector2(sc.X, sc.Y - 11), 18, Color.White, 0, L + 2, false);

            // crosshair
            var c = new Vector2(W / 2, Overlay.Height / 2);
            var s = PlayerStatus.Spread;
            var ch = new Color4(1, 1, 1, 0.9f);
            Overlay.Rect(new RectangleF(c.X - 1.5f, c.Y - s - 12, 3, 12), ch, L);
            Overlay.Rect(new RectangleF(c.X - 1.5f, c.Y + s, 3, 12), ch, L);
            Overlay.Rect(new RectangleF(c.X - s - 12, c.Y - 1.5f, 12, 3), ch, L);
            Overlay.Rect(new RectangleF(c.X + s, c.Y - 1.5f, 12, 3), ch, L);
            Overlay.Rect(new RectangleF(c.X - 2, c.Y - 2, 4, 4), ch, L);
            if (PlayerStatus.HitMarker > 0)
            {
                var hm = PlayerStatus.LastHitHeadshot ? new Color4(1f, 0.25f, 0.2f, 1f) : Color.White;
                foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    Overlay.Rect(new RectangleF(c.X + dx * 14 - 3, c.Y + dy * 14 - 3, 6, 6), hm, L + 2);
            }
            if (PlayerStatus.ReloadProgress >= 0)
            {
                Overlay.Text("RELOADING", new Vector2(c.X, c.Y + 46), 20, Color.White, 0, L);
                Overlay.Rect(new RectangleF(c.X - 70, c.Y + 74, 140, 6), new Color4(0, 0, 0, 0.5f), L);
                Overlay.Rect(new RectangleF(c.X - 70, c.Y + 74, 140 * PlayerStatus.ReloadProgress, 6), Accent, L + 1);
            }

            // message
            if (GameFlow.MessageTime > 0)
                Overlay.Text(GameFlow.Message, new Vector2(W / 2, 150), 36,
                    new Color4(1, 1, 1, MathUtil.Clamp(GameFlow.MessageTime, 0, 1)), 0, L);

            if (GameFlow.State != FlowState.Playing)
                return;

            // touch controls
            if (TouchState.StickActive)
            {
                var o = Overlay.ToVirtual(TouchState.StickOrigin);
                Overlay.Circle(o, 95, new Color4(1, 1, 1, 0.55f), true, L);
                Overlay.Circle(o + new Vector2(TouchState.Stick.X, -TouchState.Stick.Y) * 70, 42, new Color4(1, 1, 1, 0.55f), false, L + 1);
            }
            else
            {
                Overlay.Circle(new Vector2(W * 0.14f, Overlay.Height * 0.74f), 95, new Color4(1, 1, 1, 0.35f), true, L);
                Overlay.Text("MOVE", new Vector2(W * 0.14f, Overlay.Height * 0.74f - 12), 20, new Color4(1, 1, 1, 0.5f), 0, L, false);
            }

            DrawRoundButton(TouchState.FireCenter, TouchState.FireRadius, "FIRE", TouchState.FireHeld, new Color4(0.9f, 0.25f, 0.15f, 1f));
            DrawRoundButton(TouchState.JumpCenter, TouchState.JumpRadius, "JUMP", TouchState.JumpFlash > 0, new Color4(1, 1, 1, 1));
            DrawRoundButton(TouchState.ReloadCenter, TouchState.ReloadRadius, "R", PlayerStatus.ReloadProgress >= 0, new Color4(1, 1, 1, 1));
            TouchState.JumpFlash = Math.Max(0, TouchState.JumpFlash - (float)Game.UpdateTime.Elapsed.TotalSeconds);
        }

        private static void DrawRoundButton(Vector2 normalizedCenter, float radius, string label, bool active, Color4 color)
        {
            var c = Overlay.ToVirtual(normalizedCenter);
            Overlay.Circle(c, radius, new Color4(color.R * 0.4f, color.G * 0.4f, color.B * 0.4f, active ? 0.75f : 0.4f), false, 1);
            Overlay.Circle(c, radius, new Color4(color.R, color.G, color.B, active ? 1f : 0.75f), true, 2);
            Overlay.Text(label, new Vector2(c.X, c.Y - radius * 0.28f), radius * 0.48f, Color.White, 0, 3, false);
        }
    }
}
