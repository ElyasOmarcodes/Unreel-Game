using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// CI test driver, enabled with "-autotest host|client -shots DIR". Two copies of the desktop build
    /// play together over the loopback network: the host creates a group with bots, the client finds it
    /// through LAN discovery and joins, both play a short match and save screenshots of every screen.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        public static bool Running;
        private string role, shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autotest");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("AutoTest");
            var test = go.AddComponent<AutoTest>();
            test.role = args[i + 1];
            int s = Array.IndexOf(args, "-shots");
            test.shots = s >= 0 && s + 1 < args.Length ? args[s + 1] : Application.persistentDataPath;
            Directory.CreateDirectory(test.shots);
            Running = true;
            Hud.ForceTouchUi = true;
            DontDestroyOnLoad(go);
        }

        private IEnumerator Start()
        {
            App.Log("autotest " + role);
            yield return role == "client" ? Client() : Host();
            App.Log("autotest-done " + role);
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(shots, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            App.Log("screenshot " + name);
        }

        private static IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); }

        private static IEnumerator Until(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        /// <summary>Photograph the gun models and the character looks (used to check the assets).</summary>
        private IEnumerator Showcase()
        {
            var app = App.I;
            var cam = CameraRig.I;
            CameraRig.Frozen = true;
            app.ShowScreen("showcase");
            var lineupPrefab = Resources.Load<GameObject>("Debug/GunLineup");
            if (lineupPrefab != null)
            {
                var lineup = Instantiate(lineupPrefab, new Vector3(0, 300, 0), Quaternion.identity);
                var bounds = new Bounds(lineup.transform.position, Vector3.zero);
                foreach (var r in lineup.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                cam.transform.SetPositionAndRotation(bounds.center + new Vector3(bounds.size.z * 0.9f + 2f, 0.2f, 0), Quaternion.Euler(0, -90, 0));
                cam.Cam.fieldOfView = 50;
                yield return Wait(1f);
                yield return Shot("00-guns");
                Destroy(lineup);
            }
            // every style of the wardrobe, front and back, then close-ups of the head wear
            var row = new System.Collections.Generic.List<GameObject>();
            GameObject Dress(int look, Vector3 pos, float yaw)
            {
                var prefab = Resources.Load<GameObject>("Agent");
                if (prefab == null) return null;
                var go = Instantiate(prefab, pos, Quaternion.Euler(0, yaw, 0));
                var agent = go.GetComponent<Agent>();
                agent.SetTeamColor(row.Count % 2 == 0 ? new Color(0.18f, 0.42f, 0.95f) : new Color(0.95f, 0.25f, 0.2f));
                agent.SetLook(look);
                agent.SetWeapon(row.Count % 4 == 3 ? 4 : row.Count % 3);
                row.Add(go);
                return go;
            }
            for (int half = 0; half < 2; half++)
            {
                for (int i = 0; i < 5; i++)
                {
                    int style = half * 5 + i;
                    if (style >= Wardrobe.Styles.Length) break;
                    int look = Wardrobe.Apply(Wardrobe.Set(0, Wardrobe.Slot.Body, i % 2), Wardrobe.Styles[style]);
                    look = Wardrobe.Set(look, Wardrobe.Slot.TopColor, (style * 3) % 8);
                    look = Wardrobe.Set(look, Wardrobe.Slot.BottomColor, Wardrobe.Get(look, Wardrobe.Slot.Top) == 0 ? (style * 3) % 8 : style % 3);
                    look = Wardrobe.Set(look, Wardrobe.Slot.HeadColor, style % 4);
                    Dress(look, new Vector3(i * 1.05f, 300, 0), 180);
                }
                cam.transform.SetPositionAndRotation(new Vector3(2.1f, 301.0f, -5.4f), Quaternion.Euler(3, 0, 0));
                cam.Cam.fieldOfView = 34;
                yield return Wait(1f);
                yield return Shot(half == 0 ? "00-styles-a" : "00-styles-b");
                foreach (var go in row) go.transform.rotation = Quaternion.Euler(0, 0, 0);
                yield return Wait(0.3f);
                yield return Shot(half == 0 ? "00-styles-a-back" : "00-styles-b-back");
                foreach (var go in row) Destroy(go);
                row.Clear();
            }
            // the ready-made characters, then their feet from the side (which way the boots point)
            for (int i = 0; i < Wardrobe.Presets.Length; i++)
                Dress(Wardrobe.PresetLook(i), new Vector3(i * 0.9f, 300, 0), 180);
            // the PKM and the SVD in the hands of the first two
            row[0].GetComponent<Agent>().SetWeapon(8);
            row[1].GetComponent<Agent>().SetWeapon(7);
            cam.Cam.fieldOfView = 40;
            cam.transform.SetPositionAndRotation(new Vector3(4.5f, 301.0f, -7.6f), Quaternion.Euler(3, 0, 0));
            yield return Wait(1f);
            yield return Shot("00-presets");
            foreach (var go in row) go.transform.rotation = Quaternion.Euler(0, 90, 0);
            cam.transform.SetPositionAndRotation(new Vector3(2.7f, 300.45f, -3.0f), Quaternion.Euler(6, 0, 0));
            yield return Wait(0.3f);
            yield return Shot("00-feet-side");
            foreach (var go in row) Destroy(go);
            row.Clear();
            // the four characters of the player's pack on the game's skeleton: front, back, and close
            for (int i = 0; i < 4; i++)
            {
                var go = Dress(Wardrobe.PresetLook(Wardrobe.Presets.Length - 4 + i), new Vector3(i * 1.1f, 300, 0), 180);
                if (go != null) go.GetComponent<Agent>().SetWeapon(9 + i % 3);
            }
            cam.Cam.fieldOfView = 34;
            cam.transform.SetPositionAndRotation(new Vector3(2.2f, 301.0f, -5.6f), Quaternion.Euler(3, 0, 0));
            yield return Wait(1f);
            yield return Shot("00-pack-characters");
            foreach (var go in row) go.transform.rotation = Quaternion.Euler(0, 0, 0);
            yield return Wait(0.3f);
            yield return Shot("00-pack-characters-back");
            foreach (var go in row) Destroy(go);
            row.Clear();
            // TEAM 1 as on its reference sheet: front, back and right side, then the head and the arm patch close
            {
                int team1 = System.Array.FindIndex(Wardrobe.Presets, preset => preset.Name == "TEAM 1");
                float[] turns = { 180f, 0f, 90f };
                for (int i = 0; i < turns.Length && team1 >= 0; i++)
                    Dress(Wardrobe.PresetLook(team1), new Vector3(i * 1.2f, 300, 0), turns[i]);
                cam.Cam.fieldOfView = 34;
                cam.transform.SetPositionAndRotation(new Vector3(1.2f, 301.0f, -5.2f), Quaternion.Euler(3, 0, 0));
                yield return Wait(1f);
                yield return Shot("00-team1-sheet");
                cam.transform.SetPositionAndRotation(new Vector3(0.05f, 301.62f, -1.0f), Quaternion.Euler(4, 0, 0));
                yield return Wait(0.3f);
                yield return Shot("00-team1-head");
                // the right side figure: its shoulder and the patch on the sleeve
                cam.transform.SetPositionAndRotation(new Vector3(2.4f, 301.45f, -1.6f), Quaternion.Euler(4, 0, 0));
                yield return Wait(0.3f);
                yield return Shot("00-team1-arm");
                foreach (var go in row) Destroy(go);
                row.Clear();
            }
            // the clothes in motion (running): no gap in them anywhere, front and side
            for (int i = 0; i < 5 && i < Wardrobe.Styles.Length; i++)
            {
                var go = Dress(Wardrobe.Apply(Wardrobe.Set(0, Wardrobe.Slot.Body, i % 2), Wardrobe.Styles[i]), new Vector3(i * 1.1f, 300, 0), 180);
                var anim = go != null ? go.GetComponent<Agent>().Anim : null;
                if (anim != null) anim.SetFloat("MoveY", 1f);
            }
            cam.Cam.fieldOfView = 34;
            cam.transform.SetPositionAndRotation(new Vector3(2.2f, 301.0f, -6.2f), Quaternion.Euler(3, 0, 0));
            yield return Wait(0.6f);
            yield return Shot("00-running-clothes");
            foreach (var go in row) go.transform.rotation = Quaternion.Euler(0, 90, 0);
            yield return Wait(0.45f);
            yield return Shot("00-running-clothes-side");
            foreach (var go in row) Destroy(go);
            row.Clear();
            // head wear close-ups: the 11 offered on alternating bodies
            for (int i = 0; i < 11; i++)
            {
                int head = i + 1 >= 8 ? i + 2 : i + 1;   // no hijab
                int look = Wardrobe.Set(0, Wardrobe.Slot.Body, i % 2);
                look = Wardrobe.Set(Wardrobe.Set(look, Wardrobe.Slot.Head, head), Wardrobe.Slot.Top, i % 6);
                look = Wardrobe.Set(Wardrobe.Set(look, Wardrobe.Slot.Beard, i % 3 == 0 ? 0 : 1), Wardrobe.Slot.HeadColor, i % 4);
                Dress(look, new Vector3((i % 6) * 0.62f + (i / 6) * 6f, 300, 0), 160);
            }
            cam.Cam.fieldOfView = 30;
            cam.transform.SetPositionAndRotation(new Vector3(1.55f, 301.62f, -2.6f), Quaternion.Euler(2, 0, 0));
            yield return Wait(0.6f);
            yield return Shot("00-faces");
            cam.transform.SetPositionAndRotation(new Vector3(7.55f, 301.62f, -2.6f), Quaternion.Euler(2, 0, 0));
            yield return Wait(0.3f);
            yield return Shot("00-faces-2");
            foreach (var go in row) go.transform.rotation = Quaternion.Euler(0, 20, 0);
            cam.transform.SetPositionAndRotation(new Vector3(1.55f, 301.62f, -2.6f), Quaternion.Euler(2, 0, 0));
            yield return Wait(0.3f);
            yield return Shot("00-faces-back");
            foreach (var go in row) Destroy(go);
            CameraRig.Frozen = false;
            app.ShowScreen("menu");
        }

        private IEnumerator Host()
        {
            var app = App.I;
            Settings.PlayerName = "ستوری ښه";   // a Pashto name: checks the joined right-to-left text
            Settings.Team = 1;   // the host in team 1, the bots split over 1 and 2, the client alone (0)
            yield return Wait(1.5f);
            // the first start: the name and character page (no splash of our own any more)
            if (app.Current == "welcome")
            {
                yield return Shot("01-welcome");
                app.FinishWelcome(false);
            }
            yield return Until(() => app.Current == "menu", 10);
            // the page shown while this version is switched off from GitHub (A-W-Game.TXT = false)
            KillSwitch.Preview(true);
            yield return Wait(0.8f);
            yield return Shot("01b-version-switched-off");
            KillSwitch.Preview(false);
            yield return Wait(0.5f);
            yield return Showcase();
            yield return Wait(2f);
            yield return Shot("02-main-menu");
            app.ShowScreen("settings");
            yield return Wait(1f);
            yield return Shot("03-settings");
            // the other languages: the settings page and the menu in each, then back to English
            string[] codes = { "en", "fa", "ar", "es" };
            for (int lang = 1; lang < 4; lang++)
            {
                Settings.Language = lang;
                app.Relanguage();
                yield return Wait(0.6f);
                yield return Shot($"03L{lang}-settings-{codes[lang]}");
                app.ShowScreen("menu");
                yield return Wait(0.4f);
                yield return Shot($"03L{lang}-menu-{codes[lang]}");
                app.ShowScreen("settings");
            }
            Settings.Language = 0;
            app.Relanguage();
            yield return Wait(0.5f);
            app.OpenLayoutEditor();
            yield return Wait(1f);
            yield return Shot("03b-controls-layout");
            app.OpenProfile();
            yield return Wait(2f);
            yield return Shot("03c-profile");
            app.ShowWeapons(7);   // the weapons page on the SVD
            yield return Wait(1.5f);
            yield return Shot("03d-weapons");
            app.ShowWeapons(2);
            yield return Wait(1f);
            yield return Shot("03e-weapons-shotgun");
            app.ShowScreen("menu");
            yield return Wait(0.5f);
            app.ShowScreen("wifi");
            yield return Wait(1f);
            yield return Shot("04-wifi");
            app.Draft.Mode = Mode.TeamDeathmatch;
            app.Draft.Map = 2; // CITYLORD (STEEL CITY) at night: the lamps light the players, the carried weapon on the back
            app.Draft.Minutes = 3;
            app.OpenCreate(false);
            app.Draft.Bots = 4;   // the test match still has bots to fight (the screen offers none)
            yield return Wait(1f);
            yield return Shot("05-create-group");
            app.CreateFromDraft();
            // wait for the client to find us and join
            yield return Until(() => Session.I.Players.Values.Any(p => !p.IsBot && p.Id != 0), 90);
            App.Log("host sees players: " + string.Join(", ", Session.I.Players.Values.Select(p => p.Name)));
            yield return Wait(2f);
            yield return Shot("07-lobby-host");
            Session.I.StartMatch();
            StartCoroutine(FilmShots("08f"));
            yield return Until(() => Session.I.Phase == Phase.Playing && World.I.MapReady, 30);
            yield return Wait(3f);
            yield return Shot("08-match-start");
            // the first kill of the match, for the film: the host shoots the nearest enemy bot
            var me = Session.I.Local;
            var prey = Session.I.Players.Values.Where(p => p.IsBot && p.Alive && !MatchConfig.Mates(p, me)).OrderBy(p => (p.Pos - me.Pos).sqrMagnitude).FirstOrDefault();
            if (prey != null)
            {
                CameraRig.I.Yaw = Quaternion.LookRotation(prey.Pos - me.Pos).eulerAngles.y;
                yield return Wait(0.3f);
                Session.I.TestKill(me.Id, prey.Id);
                yield return Until(() => !KillCam.Active, 8);
            }
            yield return Play(10f, "09-running-firing", "10-fight");
            Controls.SimMove = Vector2.zero;
            Controls.SimGrenade = true;
            // the left arm wound up behind the head, the gun still in the right hand
            yield return Wait(0.28f);
            yield return Shot("10a-grenade-left-hand");
            yield return Wait(2.97f);
            yield return Shot("10b-grenade");
            yield return Wait(1f);
            // weapon change in the open (the body in sight): the hand goes back for the other gun
            // (mid-swap), then the pistol in the hand and the rifle across the back
            Controls.SimMove = Vector2.zero;
            yield return Until(() => Session.I.Local != null && Session.I.Local.Alive, 8f);
            yield return Wait(1f);
            Controls.SimSwap = true;
            yield return Wait(0.2f);
            yield return Shot("10f-weapon-swap");
            yield return Wait(0.8f);
            yield return Shot("10g-pistol-rifle-on-back");
            // crouched, the rifle back in the hand, standing up again: the pistol stays in its holster
            Controls.SimCrouch = true;
            yield return Wait(0.8f);
            Controls.SimSwap = true;
            yield return Wait(0.9f);
            yield return Shot("10h0-crouched-pistol-at-hip");
            Controls.SimCrouch = true;
            yield return Wait(0.9f);
            yield return Shot("10h-rifle-pistol-at-hip");
            // a jump: both hands stay on the gun
            Controls.SimJump = true;
            yield return Wait(0.25f);
            yield return Shot("10i-jump-arms");
            yield return Wait(1f);
            yield return BackToWall();
            Controls.SimScore = true;
            yield return Wait(0.3f);
            yield return Shot("11-scoreboard");
            Controls.SimScore = false;
            // a second tap on RANK closes it
            yield return Wait(0.1f);
            Controls.SimScore = true;
            yield return Wait(0.1f);
            Controls.SimScore = false;
            app.SetPaused(true);
            yield return Wait(0.5f);
            yield return Shot("12-pause");
            app.SetPaused(false);
            // let the bots fight a little, then end the match
            yield return Play(25f, "13-battle", null);
            Session.I.EndMatch();
            yield return Until(() => app.Current == "results", 5);
            yield return Wait(1.5f);
            yield return Shot("15-results");
            app.Results.ByTeam = !app.Results.ByTeam;
            app.Results.Fill(Session.I);
            yield return Wait(0.5f);
            yield return Shot("15b-results-other-view");
            yield return Wait(8f); // give the client time to see the results too
            app.LeaveGroup();
            yield return Wait(1f);
            yield return Tour();
        }

        /// <summary>
        /// The player walks backwards up to a wall: the camera must come round to the shoulder bit by bit
        /// and the body stay in sight (shots at 1.6, 0.9 and 0.45 m from the wall).
        /// </summary>
        private IEnumerator BackToWall()
        {
            var me = LocalPlayer.I != null ? LocalPlayer.I.GetComponent<Agent>() : null;
            if (me == null) yield break;
            Controls.SimMove = Vector2.zero;
            var start = me.transform.position;
            Vector3 wallPoint = default, normal = default;
            bool found = false;
            for (int ring = 0; ring < 6 && !found; ring++)
                for (int k = 0; k < 16 && !found; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    var from = start + new Vector3(Mathf.Cos(a * 1.3f + ring), 0, Mathf.Sin(a * 1.3f + ring)) * ring * 6f + Vector3.up * 1.2f;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    if (Physics.Raycast(from, dir, out var hit, 15f, World.EnvMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.2f
                        && !Physics.CheckCapsule(hit.point + hit.normal * 2.5f + Vector3.up * 0.4f, hit.point + hit.normal * 2.5f + Vector3.up * 1.6f, 0.35f, World.EnvMask, QueryTriggerInteraction.Ignore)
                        && Physics.Raycast(hit.point + hit.normal * 1f + Vector3.up * 0.5f, Vector3.down, 2f, World.EnvMask, QueryTriggerInteraction.Ignore))
                    {
                        wallPoint = hit.point; normal = hit.normal; normal.y = 0; normal.Normalize(); found = true;
                    }
                }
            if (!found) { App.Log("no wall for the camera test"); yield break; }
            float yaw = Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg;
            int n = 0;
            foreach (float gap in new[] { 1.6f, 0.9f, 0.45f })
            {
                var at = wallPoint + normal * gap;
                if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var ground, 3f, World.EnvMask, QueryTriggerInteraction.Ignore)) at.y = ground.point.y + 0.05f;
                me.Teleport(at, yaw);
                CameraRig.I.Yaw = yaw;
                CameraRig.I.Pitch = 0f;
                yield return Wait(1.2f);
                yield return Shot($"10{(char)('c' + n++)}-camera-back-to-wall");
            }
        }

        /// <summary>Photographs of the maps from fixed points: buildings, interiors, roofs, trees.</summary>
        private IEnumerator Tour()
        {
            var app = App.I;
            var cam = CameraRig.I;
            CameraRig.Frozen = true;
            app.ShowScreen("showcase");
            cam.Cam.fieldOfView = 62;
            (int map, string name, Vector3 from, Vector3 to)[] views =
            {
                (6, "28-village-bazaar", new Vector3(14f, 5f, -24f), new Vector3(0f, 2f, 0f)),
                (6, "29-village-compound", new Vector3(-16f, 8f, -4f), new Vector3(-34f, 2f, 12f)),
                (6, "30-village-house", new Vector3(18f, 3f, 12f), new Vector3(32f, 2f, 18f)),
                (6, "31-village-tower", new Vector3(30f, 14f, 28f), new Vector3(58f, 12f, 56f)),
                (6, "32-village-river", new Vector3(30f, 5f, -25f), new Vector3(12f, 0f, -40f)),
                (6, "33-village-terraces", new Vector3(-28f, 10f, 30f), new Vector3(-52f, 5f, 60f)),
                (7, "34-village-sunset", new Vector3(-62f, 26f, -72f), new Vector3(0f, 0f, 0f)),
                (6, "35-village-ruins", new Vector3(-46f, 6f, -2f), new Vector3(-62f, 1f, -12f)),
                (6, "36-village-hills", new Vector3(20f, 34f, 110f), new Vector3(4f, 24f, 150f)),
                (6, "36e-village-farm", new Vector3(-80f, 9f, -48f), new Vector3(-98f, 1f, -76f)),
                (8, "36b-village-night", new Vector3(14f, 5f, -24f), new Vector3(0f, 2f, 0f)),
                (9, "37d-town-enclosure-ne-corner", new Vector3(60f, 9f, 60f), new Vector3(76f, 1f, 76f)),
                (9, "37e-town-plank-wall-east", new Vector3(64f, 3f, 14f), new Vector3(76f, 1.5f, 30f)),
                (9, "37f-town-fence-south", new Vector3(-40f, 5f, -62f), new Vector3(-58f, 1f, -76f)),
                (9, "37g-town-stone-wall-meets-fence", new Vector3(-58f, 7f, 64f), new Vector3(-72f, 2f, 76f)),
                (9, "37h-town-canal-walls", new Vector3(52f, 1.6f, -34f), new Vector3(58f, -0.4f, -18f)),
                (9, "37-town-square", new Vector3(-16f, 6f, -20f), new Vector3(0f, 1.5f, 0f)),
                (9, "38-town-street", new Vector3(-62f, 2f, 0f), new Vector3(-20f, 3f, 0f)),
                (11, "39-town-night", new Vector3(-56f, 1.8f, -1.5f), new Vector3(-38f, 3f, 6f)),
                (11, "39b-town-night-square", new Vector3(-16f, 6f, -20f), new Vector3(0f, 1.5f, 0f)),
                (9, "40-town-church", new Vector3(-20f, 4f, 22f), new Vector3(0f, 12f, 44f)),
                (9, "41-town-tower-view", new Vector3(0f, 18.6f, 38.4f), new Vector3(0f, 2f, 0f)),
                (9, "42-town-canal", new Vector3(50f, 3f, -20f), new Vector3(58f, 0f, 10f)),
                (10, "43-town-sunset", new Vector3(-70f, 25f, -70f), new Vector3(0f, 0f, 0f)),
                (9, "44-town-wall-climb", new Vector3(-58f, 5f, 22f), new Vector3(-71f, 3f, 31f)),
                (9, "45-town-wall-walk", new Vector3(-71.5f, 6.4f, -20f), new Vector3(-50f, 2f, 10f)),
                (9, "46-town-garden-terrace", new Vector3(44f, 6f, 36f), new Vector3(50f, 2f, 56f)),
                (3, "48-zahra-salon-rico", new Vector3(-5f, 6.4f, 16f), new Vector3(-5f, 7.5f, 42f)),
                (3, "49-zahra-inside-hall", new Vector3(-5f, 6.2f, 41f), new Vector3(-5f, 6f, 58f)),
                (3, "50-zahra-upper-garden", new Vector3(-36f, 13f, -24f), new Vector3(-5f, 4f, 6f)),
                (3, "51-zahra-portico", new Vector3(72f, 6.5f, 14f), new Vector3(36f, 8f, 8f)),
                (3, "52-zahra-ramp-street", new Vector3(47f, 5.8f, 48.5f), new Vector3(86f, 7f, 48.5f)),
                (3, "53-zahra-palace", new Vector3(70f, 4.5f, -35.5f), new Vector3(70f, 6f, -50f)),
                (3, "50b-zahra-garden-channel", new Vector3(-5f, 6f, 24f), new Vector3(-5f, 4.2f, 13f)),
                (3, "50c-zahra-garden-walks", new Vector3(-36f, 6.5f, 24f), new Vector3(-20f, 4f, 10f)),
                (3, "48b-zahra-patio-steps", new Vector3(-12f, 6f, 31f), new Vector3(-5f, 4.6f, 38f)),
                (3, "55b-zahra-lower-channels", new Vector3(-5f, 2.4f, -52f), new Vector3(-5f, 0.4f, -60f)),
                (3, "53f-zahra-palace-hedges", new Vector3(60f, 2.6f, -55f), new Vector3(78f, 0.8f, -60f)),
                (3, "53e-zahra-palace-fountain", new Vector3(70f, 9f, -46f), new Vector3(70f, 0.6f, -59f)),
                (3, "53b-zahra-palace-court", new Vector3(70f, 3.2f, -48.5f), new Vector3(70f, 3.5f, -70f)),
                (3, "53c-zahra-throne", new Vector3(70f, 2.6f, -74f), new Vector3(70f, 2.2f, -93f)),
                (3, "53d-zahra-palace-rooms", new Vector3(88f, 2.6f, -78f), new Vector3(91f, 3.4f, -82f)),
                (3, "54-zahra-houses", new Vector3(-60f, 2.5f, -59.5f), new Vector3(-72f, 3.8f, -56.4f)),
                (3, "54b-zahra-town", new Vector3(-60f, 22f, -100f), new Vector3(-102f, 2f, -76f)),
                (3, "54c-zahra-town-middle", new Vector3(-78f, 9f, -27f), new Vector3(-104f, 5f, 8f)),
                (3, "63-zahra-gate-board", new Vector3(-53.5f, 2.2f, -107f), new Vector3(-53.5f, 9.5f, -124f)),
                (3, "64-zahra-gate-outside", new Vector3(-53.5f, 1.5f, -143f), new Vector3(-53.5f, 8f, -125f)),
                (3, "65-zahra-history-board", new Vector3(-55.2f, 3.6f, -112f), new Vector3(-47.3f, 3.6f, -112f)),
                (3, "66-zahra-cascade", new Vector3(14f, 8f, 44f), new Vector3(21f, 7f, 56f)),
                (3, "67-zahra-channels", new Vector3(-5f, 3f, -50f), new Vector3(-5f, 2f, -32f)),
                (3, "68-zahra-mountains", new Vector3(0f, 18.5f, 121f), new Vector3(0f, 28f, 250f)),
                (3, "69-zahra-river", new Vector3(-53.5f, 17f, -126f), new Vector3(-53.5f, -3f, -290f)),
                (3, "70-zahra-lantern-day", new Vector3(-9f, 6f, -14.5f), new Vector3(-6.6f, 6.6f, -18f)),
                (3, "71-zahra-wall-walk", new Vector3(-125f, 9.8f, -104f), new Vector3(-125f, 10.5f, -40f)),
                (3, "72-zahra-store-row", new Vector3(-110f, 6f, -66f), new Vector3(-122f, 4f, -49f)),
                (3, "73-zahra-palace-scaffold", new Vector3(86f, 9.5f, -84f), new Vector3(89f, 4f, -93f)),
                (3, "74-zahra-valley", new Vector3(-30f, -1.2f, -160f), new Vector3(-53f, 3f, -125f)),
                (3, "75-zahra-treasury-stair", new Vector3(93.5f, 2.6f, -88f), new Vector3(96.4f, 2.2f, -94.5f)),
                (3, "76-zahra-dar-al-mulk-stair", new Vector3(-86f, 12.4f, 79f), new Vector3(-90.9f, 11.6f, 84f)),
                (3, "77b-zahra-ramp-street-roof", new Vector3(40f, 22f, 30f), new Vector3(70f, 12f, 53f)),
                (3, "77c-zahra-ramp-street-board", new Vector3(37f, 6f, 51f), new Vector3(46f, 5.5f, 53f)),
                (3, "77d-zahra-garden-stair", new Vector3(4f, 3f, -42f), new Vector3(-5f, 2f, -32f)),
                (3, "77e-zahra-plaza-stair", new Vector3(44f, 3f, -42f), new Vector3(37f, 2f, -33f)),
                (3, "77f-zahra-palace-socle", new Vector3(104f, 2f, -102f), new Vector3(90f, 1f, -96f)),
                (3, "77g-zahra-oven", new Vector3(-95f, 2f, -36f), new Vector3(-98.2f, 0.6f, -40f)),
                (3, "77h-zahra-dar-al-mulk-stair", new Vector3(-50f, 7f, 42f), new Vector3(-58f, 6f, 52f)),
                (3, "77i-zahra-ramp-exit", new Vector3(52.2f, 11.8f, 64f), new Vector3(52.2f, 10.4f, 56f)),
                (3, "77j-zahra-city-wall", new Vector3(-110f, 2f, -20f), new Vector3(-125f, 5f, -5f)),
                (9, "37b-town-well", new Vector3(4.5f, 3.2f, 4.5f), new Vector3(0f, 1.8f, 0f)),
                (9, "37c-town-wall-stone", new Vector3(-62f, 2f, 10f), new Vector3(-71f, 3f, 20f)),
                (2, "86b-citylord-night-yard", new Vector3(-0.6f, 2f, -35.6f), new Vector3(28f, 1.5f, -24f)),
                (2, "86c-citylord-night-above", new Vector3(-30f, 26f, -50f), new Vector3(30f, 0f, -5f)),
                (2, "86d-citylord-night-hangars", new Vector3(81.5f, 3.5f, -24.6f), new Vector3(46f, 2f, 20f)),
                (3, "77k-zahra-bastion-top", new Vector3(-117f, 13f, -101f), new Vector3(-125f, 8.5f, -109.5f)),
                (3, "77l-zahra-wall-texture", new Vector3(-110f, 3f, -60f), new Vector3(-125f, 5f, -50f)),
                (3, "77-zahra-cypress", new Vector3(-12f, 6f, -14f), new Vector3(-8.6f, 9f, -20f)),
                (9, "78-town-granary-stair", new Vector3(29.5f, 2.2f, 14.5f), new Vector3(21.8f, 1.8f, 10.4f)),
                (3, "55-zahra-lower-garden", new Vector3(-5f, 6.5f, -32f), new Vector3(-5f, 0f, -70f)),
                (3, "56-zahra-dar-al-mulk", new Vector3(-62f, 12.5f, 63f), new Vector3(-5f, 4f, 0f)),
                (3, "57-zahra-north-gate", new Vector3(47f, 12f, 86f), new Vector3(30f, 11f, 86f)),
                (3, "58-zahra-roof-climb", new Vector3(-6f, 12.5f, 66f), new Vector3(-1f, 12f, 58f)),
                (4, "59-zahra-sunset", new Vector3(-80f, 26f, -80f), new Vector3(0f, 6f, 20f)),
                (5, "60-zahra-night", new Vector3(-5f, 6.4f, 16f), new Vector3(-5f, 7.5f, 42f)),
                (5, "60b-zahra-night-palace", new Vector3(70f, 3.2f, -48.5f), new Vector3(70f, 3.5f, -70f)),
                (5, "60c-zahra-night-gate", new Vector3(-53.5f, 2.2f, -107f), new Vector3(-53.5f, 7f, -124f)),
                (0, "80-citylord-overview", new Vector3(-30f, 26f, -50f), new Vector3(30f, 0f, -5f)),
                (0, "81-citylord-street", new Vector3(17.3f, 1.7f, 14.3f), new Vector3(39f, 2f, -2f)),
                (0, "82-citylord-hangars", new Vector3(81.5f, 3.5f, -24.6f), new Vector3(46f, 2f, 20f)),
                (0, "83-citylord-yard", new Vector3(-0.6f, 2f, -35.6f), new Vector3(28f, 1.5f, -24f)),
                (0, "84-citylord-from-above", new Vector3(31f, 60f, -60f), new Vector3(31f, 0f, -4f)),
                (1, "85-citylord-sunset", new Vector3(-30f, 26f, -50f), new Vector3(30f, 0f, -5f)),
                (2, "86-citylord-night", new Vector3(17.3f, 1.7f, 14.3f), new Vector3(39f, 2f, -2f)),
                (0, "87-citylord-hangar-door", new Vector3(81f, 3.6f, -13.3f), new Vector3(70f, 3f, -13.3f)),
                (0, "88-citylord-hangar-inside", new Vector3(72.6f, 3.4f, -13.3f), new Vector3(64f, 2.6f, -6f)),
                (0, "89-citylord-east-door", new Vector3(61f, 3.6f, 29.9f), new Vector3(50f, 3f, 29.9f)),
                (0, "89b-citylord-west-door", new Vector3(-24f, 3.2f, -29.8f), new Vector3(-12f, 2.5f, -29.8f)),
                (0, "89c-citylord-small-door", new Vector3(46.3f, 3.2f, 16f), new Vector3(46.3f, 2.6f, 26f)),
            };
            foreach (var v in views)
            {
                World.I.ApplyLighting(v.map);
                yield return Until(() => World.I.MapReady, 30);   // the map scene loads in the background
                cam.transform.SetPositionAndRotation(v.from, Quaternion.LookRotation(v.to - v.from));
                yield return Wait(0.8f);
                yield return Shot(v.name);
            }
            // the Old Town store houses are placed on free ground when the map is built: look them up
            World.I.ApplyLighting(9);
            yield return Until(() => World.I.MapReady, 30);
            int k = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                         .Where(x => x.name == "Loft" || x.name == "Granary").OrderBy(x => x.name).ToList())
            {
                var p = t.position;
                bool loft = t.name == "Loft";
                var from = p + (loft ? new Vector3(-13f, 8f, -12f) : new Vector3(-12f, 7f, 13f));
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(p + Vector3.up * 3f - from));
                yield return Wait(0.8f);
                yield return Shot($"47{(char)('a' + k++)}-town-{(loft ? "roof-stair" : "roof-hidden-way")}");
            }
            // the view from the top of the minaret
            World.I.ApplyLighting(3);
            yield return Until(() => World.I.MapReady, 30);
            var minaret = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "MinaretTop");
            if (minaret != null)
            {
                cam.transform.SetPositionAndRotation(minaret.position, Quaternion.LookRotation(new Vector3(0f, 4f, 0f) - minaret.position));
                yield return Wait(0.8f);
                yield return Shot("61-zahra-minaret-view");
                var foot = minaret.position - Vector3.up * 13f + minaret.forward * 9f;
                cam.transform.SetPositionAndRotation(foot, Quaternion.LookRotation(minaret.position - Vector3.up * 6f - foot));
                yield return Wait(0.5f);
                yield return Shot("62-zahra-minaret");
            }
            // CityLord: its name boards (placed on free ground near their spot) and number plates
            World.I.ApplyLighting(0);
            yield return Until(() => World.I.MapReady, 30);
            int sign = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                         .Where(x => x.name.StartsWith("Board ") || new[] { "Plate C-1", "Plate A-2", "Plate D-3", "Plate A-4", "Plate C-4", "Plate C-5" }.Contains(x.name)).OrderBy(x => x.name).ToList())
            {
                // back from the board as far as nothing stands in between (at most 7 m)
                var eye = t.position + Vector3.up * (t.name.StartsWith("Board") ? 2f : 0f);
                float back = Physics.SphereCast(eye, 0.3f, -t.forward, out var wall, 7f, World.EnvMask, QueryTriggerInteraction.Ignore) ? Mathf.Max(2.5f, wall.distance - 0.4f) : 7f;
                var from = eye - t.forward * back + Vector3.up * 0.4f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(eye - from));
                yield return Wait(0.6f);
                yield return Shot($"90{(char)('a' + sign++)}-citylord-{t.name.Replace(' ', '-').ToLowerInvariant()}");
            }
            // the buildings of the hidden ways, from high above (where they stand, what is round them)
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                         .Where(x => new[] { "Plate D-3", "Plate C-1", "Plate A-4", "Plate C-4", "Plate C-5" }.Contains(x.name)).OrderBy(x => x.name).ToList())
            {
                var above = t.position - t.forward * 14f + Vector3.up * 22f;
                cam.transform.SetPositionAndRotation(above, Quaternion.LookRotation(t.position + t.forward * 6f - above));
                yield return Wait(0.5f);
                yield return Shot($"91-citylord-above-{t.name.Substring(6).ToLowerInvariant()}");
            }
            // the whole town from straight above, north up (the map of the names): x 29 +- 110, z -6 +- 62
            {
                bool wasOrtho = cam.Cam.orthographic;
                float wasFar = cam.Cam.farClipPlane, wasSize = cam.Cam.orthographicSize;
                cam.Cam.orthographic = true;
                cam.Cam.orthographicSize = 62f;
                cam.Cam.farClipPlane = 400f;
                cam.transform.SetPositionAndRotation(new Vector3(29f, 150f, -6f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                yield return Wait(0.8f);
                yield return Shot("91z-citylord-top-view");
                cam.Cam.orthographic = wasOrtho;
                cam.Cam.orthographicSize = wasSize;
                cam.Cam.farClipPlane = wasFar;
            }
            // the new double pipe from the roof of the pipe works over to the warehouse D-4
            var pipe = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "RoofPipe");
            if (pipe != null)
            {
                var side = Vector3.Cross(Vector3.up, pipe.forward);
                var from = pipe.position + side * 14f + Vector3.up * 5f - pipe.forward * 4f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(pipe.position - from));
                yield return Wait(0.6f);
                yield return Shot("90j-citylord-pipe-d4");
                var start = pipe.position - pipe.forward * 9f + Vector3.up * 2.2f;
                cam.transform.SetPositionAndRotation(start, Quaternion.LookRotation(pipe.forward + Vector3.down * 0.15f));
                yield return Wait(0.5f);
                yield return Shot("90k-citylord-pipe-walk");
            }
            // the CityLord ammo box and wooden crates in the other maps
            int box = 0;
            foreach (int map in new[] { 0, 3, 6, 9 })
            {
                World.I.ApplyLighting(map);
                yield return Until(() => World.I.MapReady, 30);
                var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var t in new[] { all.FirstOrDefault(x => x.name == "AmmoCrate"), all.FirstOrDefault(x => x.name == "CrateStack") })
                {
                    if (t == null) continue;
                    var from = t.position + new Vector3(3.2f, 2.2f, -3.2f);
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(t.position + Vector3.up * 0.6f - from));
                    yield return Wait(0.6f);
                    yield return Shot($"92{(char)('a' + box++)}-map{map}-{t.name.ToLowerInvariant()}");
                }
            }
            // name boards of the parts of each map (Arabic and English)
            int named = 0;
            foreach (int map in new[] { 0, 3, 6, 9 })
            {
                World.I.ApplyLighting(map);
                yield return Until(() => World.I.MapReady, 30);
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(x => x.name == "BoardStand").Take(4).ToList())
                {
                    var from = t.position - t.forward * 6f + Vector3.up * 1.8f;
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(t.position + Vector3.up * 1.9f - from));
                    yield return Wait(0.5f);
                    yield return Shot($"94{(char)('a' + named++)}-map{map}-board");
                }
            }
            // the CityLord chain-link fence walls in the Village and the Old Town
            int link = 0;
            foreach (int map in new[] { 6, 9 })
            {
                World.I.ApplyLighting(map);
                yield return Until(() => World.I.MapReady, 30);
                var holder = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "ChainLinkFences");
                if (holder == null) continue;
                foreach (Transform t in holder)
                {
                    if (link >= 4 + (map == 9 ? 3 : 0)) break;
                    var from = t.position + t.right * 6f + Vector3.up * 2.2f + t.forward * 4f;
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(t.position + Vector3.up * 1.2f - from));
                    yield return Wait(0.5f);
                    yield return Shot($"97{(char)('a' + link++)}-map{map}-chain-link-fence");
                }
            }
            // the CityLord wooden ladders
            World.I.ApplyLighting(0);
            yield return Until(() => World.I.MapReady, 30);
            int ladders = 0;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Ladder>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Take(4).ToList())
            {
                var from = l.transform.position - l.Into * 5f + Vector3.up * 2f + Vector3.Cross(Vector3.up, l.Into) * 2f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(l.transform.position + Vector3.up * 1.5f - from));
                yield return Wait(0.5f);
                yield return Shot($"95{(char)('a' + ladders++)}-citylord-ladder");
            }
            // the street lamps at the edges of the streets and walks (STEEL CITY, Old Town, Al-Zahra)
            int lampShot = 0;
            foreach (var (map, lamp) in new[] { (0, "StreetLamp"), (9, "StreetLamp"), (3, "Lantern") })
            {
                World.I.ApplyLighting(map);
                yield return Until(() => World.I.MapReady, 30);
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(x => x.name == lamp).Take(3).ToList())
                {
                    var from = t.position + new Vector3(4.5f, 3.2f, -4.5f);
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(t.position + Vector3.up * 1.8f - from));
                    yield return Wait(0.5f);
                    yield return Shot($"98{(char)('a' + lampShot++)}-map{map}-lamp-at-edge");
                }
            }
            // Al-Zahra: the high palace tower (from the west roof: its door; from its top), the tiled
            // fountains, the walks crossing in the Lower Garden
            World.I.ApplyLighting(3);
            yield return Until(() => World.I.MapReady, 30);
            var all3 = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var ptower = all3.FirstOrDefault(x => x.name == "PalaceTower");
            if (ptower != null)
            {
                var from = ptower.position + new Vector3(16f, 9f, 18f);
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(ptower.position + Vector3.up * 5f - from));
                yield return Wait(0.6f);
                yield return Shot("99d-zahra-palace-tower");
                var door = ptower.position + new Vector3(1.8f, 1.7f, 7.5f);
                cam.transform.SetPositionAndRotation(door, Quaternion.LookRotation(ptower.position + new Vector3(1.8f, 1.2f, 2.7f) - door));
                yield return Wait(0.5f);
                yield return Shot("99e-zahra-palace-tower-door-from-the-roof");
                var top = all3.FirstOrDefault(x => x.name == "PalaceTowerTop");
                if (top != null)
                {
                    cam.transform.SetPositionAndRotation(top.position, Quaternion.LookRotation(new Vector3(1f, -0.35f, 1f)));
                    yield return Wait(0.5f);
                    yield return Shot("99f-zahra-palace-tower-top");
                }
            }
            foreach (var (name, from, to) in new[] { ("99g-zahra-plaza-fountain", new Vector3(64f, 7f, 2f), new Vector3(70f, 5f, 8f)),
                                                     ("99h-zahra-lower-garden-crossing", new Vector3(-12f, 4f, -68f), new Vector3(-5f, 0f, -61f)),
                                                     ("99i-zahra-gate-board", new Vector3(-53.5f, 9f, -112f), new Vector3(-53.5f, 11.7f, -122.85f)) })
            {
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
                yield return Wait(0.5f);
                yield return Shot(name);
            }
            // the Village: the hide up in the old tree (from the yard, from up in it) and a paved path
            World.I.ApplyLighting(6);
            yield return Until(() => World.I.MapReady, 30);
            var climb = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "ClimbingTree");
            if (climb != null)
            {
                var from = climb.position - climb.forward * 9f + climb.right * 3f + Vector3.up * 3f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(climb.position + Vector3.up * 4.5f - from));
                yield return Wait(0.6f);
                yield return Shot("99a-village-tree-hide");
                var up = climb.position + Vector3.up * 7.2f + climb.forward * 0.9f;
                cam.transform.SetPositionAndRotation(up, Quaternion.LookRotation(-climb.forward + Vector3.down * 0.35f));
                yield return Wait(0.5f);
                yield return Shot("99b-village-view-from-the-hide");
            }
            cam.transform.SetPositionAndRotation(new Vector3(-12f, 5f, -6f), Quaternion.LookRotation(new Vector3(-30f, 0f, -24f) - new Vector3(-12f, 5f, -6f)));
            yield return Wait(0.5f);
            yield return Shot("99c-village-paved-path");
            World.I.ApplyLighting(0);
            yield return Until(() => World.I.MapReady, 30);
            // the seven CityLord spawn points: on the streets, nothing under or above
            int spawn = 0;
            foreach (var p in World.I.Spawns.Take(7).ToList())
            {
                var from = p + new Vector3(-5f, 3.5f, -5f);
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(p + Vector3.up * 0.8f - from));
                yield return Wait(0.5f);
                yield return Shot($"96{(char)('a' + spawn++)}-citylord-spawn");
            }
            yield return ZahraSurvey();
            CameraRig.Frozen = false;
        }

        /// <summary>
        /// Al-Zahra from end to end: a look from eye height every 20 m (on the ground, in the buildings, on
        /// the roofs where there is no ground), each in another direction, and the whole city from the air.
        /// Named s-zahra-*: they go to the screenshot branch, not into the release.
        /// </summary>
        private IEnumerator ZahraSurvey()
        {
            var cam = CameraRig.I;
            World.I.ApplyLighting(3);
            yield return Until(() => World.I.MapReady, 30);
            int k = 0;
            for (int gz = -120; gz <= 120; gz += 20)
                for (int gx = -120; gx <= 120; gx += 20)
                {
                    var hits = Physics.RaycastAll(new Vector3(gx, 80f, gz), Vector3.down, 100f, World.EnvMask, QueryTriggerInteraction.Ignore)
                        .Where(h => h.normal.y > 0.6f).OrderBy(h => h.point.y).ToList();
                    if (hits.Count == 0) continue;
                    // the lowest floor with room to stand (inside a building, on the street), else the top
                    var eye = hits[hits.Count - 1].point + Vector3.up * 1.7f;
                    foreach (var h in hits)
                        if (!Physics.CheckSphere(h.point + Vector3.up * 1.7f, 0.3f, World.EnvMask, QueryTriggerInteraction.Ignore)) { eye = h.point + Vector3.up * 1.7f; break; }
                    float yaw = (k * 137.5f) % 360f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(8f, yaw, 0));
                    k++;
                    yield return Wait(0.25f);
                    yield return Shot($"s-zahra-{(gz + 120) / 20:00}-{(gx + 120) / 20:00}-y{Mathf.RoundToInt(yaw):000}");
                }
            // from the air: eight looks in from round the city, four straight down on its quarters
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var from = new Vector3(Mathf.Sin(a) * 95f, 38f, Mathf.Cos(a) * 95f);
                var to = new Vector3(Mathf.Sin(a) * 25f, 4f, Mathf.Cos(a) * 25f);
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
                yield return Wait(0.3f);
                yield return Shot($"s-zahra-air-{i}");
            }
            foreach (var (x, z) in new[] { (-62f, -62f), (62f, -62f), (-62f, 62f), (62f, 62f) })
            {
                cam.transform.SetPositionAndRotation(new Vector3(x, 75f, z - 20f), Quaternion.LookRotation(new Vector3(0, -75f, 20f)));
                yield return Wait(0.3f);
                yield return Shot($"s-zahra-top-{(x < 0 ? "w" : "e")}{(z < 0 ? "s" : "n")}");
            }
        }

        private IEnumerator Client()
        {
            var app = App.I;
            yield return Wait(1.5f);
            if (app.Current == "welcome") app.FinishWelcome(false);
            yield return Until(() => app.Current == "menu", 15);
            yield return Wait(1f);
            Settings.Primary = 7;   // the client plays the SVD: a look through the PSO-1 scope
            Settings.Team = 0;   // the client fights alone
            app.ShowScreen("join");
            yield return Until(() => Session.I.Groups.Count > 0, 90);
            yield return Wait(1.5f);
            yield return Shot("06-join-list");
            var group = Session.I.Groups.Values.First();
            App.Log("client joining " + group.Name + " at " + group.Address);
            app.JoinGroup(group.Address);
            yield return Until(() => Session.I.Local != null, 15);
            yield return Wait(2f);
            yield return Shot("07-lobby-client");
            yield return Until(() => Session.I.Phase == Phase.Playing, 120);
            StartCoroutine(FilmShots("c1f"));
            yield return Wait(4f);
            yield return Shot("c1-client-in-match");
            yield return Play(10f, "c2-client-firing", "c3-client-fight");
            if (LocalPlayer.I != null && LocalPlayer.I.Current.Scope)
            {
                Controls.SimMove = Vector2.zero;
                LocalPlayer.I.Aiming = true;
                yield return Wait(1f);
                yield return Shot("c3b-client-scope");
                LocalPlayer.I.Aiming = false;
            }
            yield return Until(() => app.Current == "results", 90);
            yield return Wait(1.5f);
            yield return Shot("c4-client-results");
        }

        /// <summary>The first kill film, on whichever machine: the shot leaving, the bullet on its way, the hit, the fall.</summary>
        private IEnumerator FilmShots(string prefix)
        {
            yield return Until(() => KillCam.Active, 150);
            if (!KillCam.Active) { App.Log("no first kill film"); yield break; }
            float start = Time.realtimeSinceStartup;
            foreach (var (at, name) in new[] { (0.6f, "0-title"), (1.35f, "a-aim"), (1.9f, "b-bullet"), (2.75f, "c-bullet-near"), (3.2f, "d-hit"), (4.3f, "e-fall") })
            {
                while (Time.realtimeSinceStartup - start < at) yield return null;
                yield return Shot($"{prefix}{name}-first-kill");
            }
        }

        /// <summary>Walk around and shoot with simulated input, taking up to two screenshots.</summary>
        private IEnumerator Play(float seconds, string firstShot, string secondShot)
        {
            float end = Time.realtimeSinceStartup + seconds;
            bool first = false, second = false;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < end)
            {
                float t = Time.realtimeSinceStartup - start;
                Controls.SimMove = new Vector2(Mathf.Sin(t * 0.7f) * 0.5f, 0.8f);
                Controls.SimLook = new Vector2(Mathf.Sin(t * 0.5f) * 0.6f, 0f);
                Controls.SimFire = (t % 2.5f) > 1.2f;
                if (!first && t > 2.5f && firstShot != null) { first = true; yield return Shot(firstShot); }
                if (!second && t > seconds * 0.7f && secondShot != null) { second = true; yield return Shot(secondShot); }
                yield return null;
            }
            Controls.SimMove = Vector2.zero;
            Controls.SimLook = Vector2.zero;
            Controls.SimFire = false;
        }
    }
}
