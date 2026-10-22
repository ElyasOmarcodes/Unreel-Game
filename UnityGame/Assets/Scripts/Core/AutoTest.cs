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
            app.Draft.Bots = 4;
            app.Draft.Minutes = 3;
            app.OpenCreate(false);
            yield return Wait(1f);
            yield return Shot("05-create-group");
            app.CreateFromDraft();
            // wait for the client to find us and join
            yield return Until(() => Session.I.Players.Values.Any(p => !p.IsBot && p.Id != 0), 90);
            App.Log("host sees players: " + string.Join(", ", Session.I.Players.Values.Select(p => p.Name)));
            yield return Wait(2f);
            yield return Shot("07-lobby-host");
            Session.I.StartMatch();
            yield return Until(() => Session.I.Phase == Phase.Playing, 10);
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
                (5, "28-village-bazaar", new Vector3(14f, 5f, -24f), new Vector3(0f, 2f, 0f)),
                (5, "29-village-compound", new Vector3(-16f, 8f, -4f), new Vector3(-34f, 2f, 12f)),
                (5, "30-village-house", new Vector3(18f, 3f, 12f), new Vector3(32f, 2f, 18f)),
                (5, "31-village-tower", new Vector3(30f, 14f, 28f), new Vector3(58f, 12f, 56f)),
                (5, "32-village-river", new Vector3(30f, 5f, -25f), new Vector3(12f, 0f, -40f)),
                (5, "33-village-terraces", new Vector3(-28f, 10f, 30f), new Vector3(-52f, 5f, 60f)),
                (6, "34-village-sunset", new Vector3(-62f, 26f, -72f), new Vector3(0f, 0f, 0f)),
                (5, "35-village-ruins", new Vector3(-46f, 6f, -2f), new Vector3(-62f, 1f, -12f)),
                (5, "36-village-hills", new Vector3(20f, 22f, 70f), new Vector3(4f, 18f, 112f)),
            };
            foreach (var v in views)
            {
                World.I.ApplyLighting(v.map);
                cam.transform.SetPositionAndRotation(v.from, Quaternion.LookRotation(v.to - v.from));
                yield return Wait(0.8f);
                yield return Shot(v.name);
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
