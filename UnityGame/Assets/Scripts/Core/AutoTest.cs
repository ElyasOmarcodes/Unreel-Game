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
                var prefab = Resources.Load<GameObject>(Wardrobe.Female(look) ? "Agent_F" : "Agent");
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
                    int look = Wardrobe.Apply(Wardrobe.Set(0, Wardrobe.Slot.Body, (style >= 8 ? 2 : 0) + i % 2), Wardrobe.Styles[style]);
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
            // head wear close-ups: 12 options on alternating bodies
            for (int i = 0; i < 12; i++)
            {
                int head = i + 1;
                int look = Wardrobe.Set(0, Wardrobe.Slot.Body, head == 8 ? 2 + i % 2 : i % 2);
                look = Wardrobe.Set(Wardrobe.Set(look, Wardrobe.Slot.Head, head), Wardrobe.Slot.Top, head == 8 ? 6 : i % 6);
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
            yield return Wait(1.2f);
            yield return Shot("01-splash");
            yield return Until(() => app.Current == "menu", 10);
            yield return Showcase();
            yield return Wait(2f);
            yield return Shot("02-main-menu");
            app.ShowScreen("settings");
            yield return Wait(1f);
            yield return Shot("03-settings");
            app.OpenLayoutEditor();
            yield return Wait(1f);
            yield return Shot("03b-controls-layout");
            app.OpenProfile();
            yield return Wait(2f);
            yield return Shot("03c-profile");
            app.ShowScreen("menu");
            yield return Wait(0.5f);
            app.ShowScreen("wifi");
            yield return Wait(1f);
            yield return Shot("04-wifi");
            app.Draft.Mode = Mode.TeamDeathmatch;
            app.Draft.Map = 3; // PORT
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
            yield return Until(() => Session.I.Phase == Phase.Playing && World.I.MapReady, 30);
            yield return Wait(3f);
            yield return Shot("08-match-start");
            yield return Play(10f, "09-running-firing", "10-fight");
            Controls.SimMove = Vector2.zero;
            Controls.SimGrenade = true;
            yield return Wait(3.25f);
            yield return Shot("10b-grenade");
            yield return Wait(1f);
            Controls.SimScore = true;
            yield return Wait(0.3f);
            yield return Shot("11-scoreboard");
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
            yield return Wait(8f); // give the client time to see the results too
            app.LeaveGroup();
            yield return Wait(1f);
            yield return Tour();
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
                (0, "16-downtown-building", new Vector3(-40f, 6f, 24f), new Vector3(-52f, 7f, 42f)),
                (0, "17-downtown-inside", new Vector3(-58.5f, 1.9f, 40.5f), new Vector3(-44f, 1.5f, 45f)),
                (0, "18-downtown-stairs", new Vector3(-47.5f, 5.2f, 41f), new Vector3(-43f, 3.8f, 47f)),
                (0, "19-downtown-roof", new Vector3(-61f, 15.6f, 40.5f), new Vector3(-20f, 6f, 10f)),
                (0, "20-downtown-park", new Vector3(-14f, 2.6f, -16f), new Vector3(0f, 2f, 0f)),
                (0, "20b-downtown-street", new Vector3(29f, 2.2f, -36f), new Vector3(25f, 1f, -62f)),
                (0, "20c-ammo-crate", new Vector3(-29.5f, 1.9f, -9.5f), new Vector3(-33.5f, 0.5f, -12f)),
                (0, "20d-downtown-tower", new Vector3(38f, 3f, 18f), new Vector3(62f, 14f, 42f)),
                (0, "20e-downtown-terrace", new Vector3(-12f, 2.6f, -12.5f), new Vector3(4f, 0.5f, 3f)),
                (0, "20f-metro-entrance", new Vector3(-54f, 3.2f, -27f), new Vector3(-68f, -1.5f, -22f)),
                (0, "20g-metro-hall", new Vector3(-17.5f, -3.3f, -21.3f), new Vector3(10f, -4.6f, -29f)),
                (0, "20h-metro-tunnel", new Vector3(-24f, -3.4f, -22f), new Vector3(-60f, -3.8f, -24f)),
                (3, "21-port-office", new Vector3(44f, 4f, 4f), new Vector3(58f, 5f, 24f)),
                (3, "22-port-office-inside", new Vector3(53f, 1.7f, 16.3f), new Vector3(63f, 1.6f, 24f)),
                (3, "23-port-office-roof", new Vector3(52f, 12f, 17f), new Vector3(10f, 3f, 0f)),
                (3, "24-port-hidden-route", new Vector3(-38f, 5f, -32f), new Vector3(-56f, 3f, -45f)),
                (3, "25-port-trees", new Vector3(52f, 2.5f, 34f), new Vector3(58f, 3f, 48f)),
                (3, "26-port-fire-escape", new Vector3(-24f, 4f, 30f), new Vector3(-34f, 4.5f, 37f)),
                (3, "27-port-silos", new Vector3(-6f, 4f, 22f), new Vector3(-14f, 4f, 31f)),
                (3, "27b-port-containers", new Vector3(-4f, 2f, -24f), new Vector3(-12f, 1.5f, -14f)),
                (3, "27c-port-pit", new Vector3(66f, 4f, -40f), new Vector3(79f, -2f, -23f)),
                (3, "27d-port-crane", new Vector3(-2f, 6f, 34f), new Vector3(-21f, 8f, 57f)),
                (4, "27e-port-ship", new Vector3(40f, 9f, 48f), new Vector3(10f, 4f, 88f)),
                (3, "27f-port-tunnel", new Vector3(79f, -2.3f, -9f), new Vector3(79f, -3f, 20f)),
                (3, "27g-port-slopes", new Vector3(44f, 4f, 46f), new Vector3(62f, 1f, 56f)),
                (3, "27h-port-factory-climb", new Vector3(-22f, 8f, 12f), new Vector3(-37f, 4f, 27f)),
                (3, "27i-port-yards", new Vector3(10f, 20f, -26f), new Vector3(-34f, 1f, -56f)),
                (3, "27j-port-store-rooms", new Vector3(-10f, 3f, 36f), new Vector3(-10f, 1.5f, 20f)),
                (3, "27k-port-factory-east", new Vector3(76f, 10f, -52f), new Vector3(58f, 4f, -36f)),
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
                (2, "36c-downtown-night", new Vector3(29f, 2.2f, -36f), new Vector3(25f, 1f, -62f)),
                (5, "36d-port-night", new Vector3(-4f, 6f, -24f), new Vector3(-30f, 2f, 10f)),
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
                (12, "48-zahra-salon-rico", new Vector3(-5f, 6.4f, 16f), new Vector3(-5f, 7.5f, 42f)),
                (12, "49-zahra-inside-hall", new Vector3(-5f, 6.2f, 41f), new Vector3(-5f, 6f, 58f)),
                (12, "50-zahra-upper-garden", new Vector3(-36f, 13f, -24f), new Vector3(-5f, 4f, 6f)),
                (12, "51-zahra-portico", new Vector3(72f, 6.5f, 14f), new Vector3(36f, 8f, 8f)),
                (12, "52-zahra-ramp-street", new Vector3(47f, 5.8f, 48.5f), new Vector3(86f, 7f, 48.5f)),
                (12, "53-zahra-palace", new Vector3(70f, 4.5f, -35.5f), new Vector3(70f, 6f, -50f)),
                (12, "53b-zahra-palace-court", new Vector3(70f, 3.2f, -48.5f), new Vector3(70f, 3.5f, -70f)),
                (12, "53c-zahra-throne", new Vector3(70f, 2.6f, -74f), new Vector3(70f, 2.2f, -93f)),
                (12, "53d-zahra-palace-rooms", new Vector3(88f, 2.6f, -78f), new Vector3(91f, 3.4f, -82f)),
                (12, "54-zahra-houses", new Vector3(-60f, 2.5f, -59.5f), new Vector3(-72f, 3.8f, -56.4f)),
                (12, "54b-zahra-town", new Vector3(-60f, 22f, -100f), new Vector3(-102f, 2f, -76f)),
                (12, "54c-zahra-town-middle", new Vector3(-78f, 9f, -27f), new Vector3(-104f, 5f, 8f)),
                (12, "63-zahra-gate-board", new Vector3(-53.5f, 2.2f, -107f), new Vector3(-53.5f, 9.5f, -124f)),
                (12, "64-zahra-gate-outside", new Vector3(-53.5f, 1.5f, -143f), new Vector3(-53.5f, 8f, -125f)),
                (12, "65-zahra-history-board", new Vector3(-55.2f, 3.6f, -112f), new Vector3(-47.3f, 3.6f, -112f)),
                (12, "66-zahra-cascade", new Vector3(14f, 8f, 44f), new Vector3(21f, 7f, 56f)),
                (12, "67-zahra-channels", new Vector3(-5f, 3f, -50f), new Vector3(-5f, 2f, -32f)),
                (12, "68-zahra-mountains", new Vector3(0f, 18.5f, 121f), new Vector3(0f, 28f, 250f)),
                (12, "69-zahra-river", new Vector3(-53.5f, 17f, -126f), new Vector3(-53.5f, -3f, -290f)),
                (12, "70-zahra-lantern-day", new Vector3(-9f, 6f, -14.5f), new Vector3(-6.6f, 6.6f, -18f)),
                (12, "55-zahra-lower-garden", new Vector3(-5f, 6.5f, -32f), new Vector3(-5f, 0f, -70f)),
                (12, "56-zahra-dar-al-mulk", new Vector3(-62f, 12.5f, 63f), new Vector3(-5f, 4f, 0f)),
                (12, "57-zahra-north-gate", new Vector3(47f, 12f, 86f), new Vector3(30f, 11f, 86f)),
                (12, "58-zahra-roof-climb", new Vector3(-6f, 12.5f, 66f), new Vector3(-1f, 12f, 58f)),
                (13, "59-zahra-sunset", new Vector3(-80f, 26f, -80f), new Vector3(0f, 6f, 20f)),
                (14, "60-zahra-night", new Vector3(-5f, 6.4f, 16f), new Vector3(-5f, 7.5f, 42f)),
                (14, "60b-zahra-night-palace", new Vector3(70f, 3.2f, -48.5f), new Vector3(70f, 3.5f, -70f)),
                (14, "60c-zahra-night-gate", new Vector3(-53.5f, 2.2f, -107f), new Vector3(-53.5f, 7f, -124f)),
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
            World.I.ApplyLighting(12);
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
            CameraRig.Frozen = false;
        }

        private IEnumerator Client()
        {
            var app = App.I;
            yield return Until(() => app.Current == "menu", 15);
            yield return Wait(1f);
            Settings.Primary = 7;   // the client plays the SVD: a look through the PSO-1 scope
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
