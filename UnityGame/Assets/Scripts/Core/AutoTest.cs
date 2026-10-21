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

        private IEnumerator Host()
        {
            var app = App.I;
            yield return Wait(1.2f);
            yield return Shot("01-splash");
            yield return Until(() => app.Current == "menu", 10);
            yield return Wait(2f);
            yield return Shot("02-main-menu");
            app.ShowScreen("settings");
            yield return Wait(1f);
            yield return Shot("03-settings");
            app.ShowScreen("wifi");
            yield return Wait(1f);
            yield return Shot("04-wifi");
            app.Draft.Mode = Mode.TeamDeathmatch;
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
        }

        private IEnumerator Client()
        {
            var app = App.I;
            yield return Until(() => app.Current == "menu", 15);
            yield return Wait(1f);
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
