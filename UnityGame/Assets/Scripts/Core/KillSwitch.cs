using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace UA
{
    /// <summary>
    /// The remote on/off switch of this version (ElyasOmarcodes/Ai-Model, branch Apps-Expire-Files,
    /// file A-W-Game.TXT: "true" plays, "false" stops). Read through the GitHub API with an ETag (an
    /// unchanged file answers 304, which GitHub does not count against its limit: every 5 s), the raw file as a fallback. Only a clear answer changes
    /// anything: no internet, an error or an unreadable file leave the game as it was, and a "false" is
    /// kept until a real "true" comes. Nothing about the player is sent: one plain GET of a public file.
    /// </summary>
    public class KillSwitch : MonoBehaviour
    {
        private const string Owner = "ElyasOmarcodes", Repo = "Ai-Model", Branch = "Apps-Expire-Files", FilePath = "A-W-Game.TXT";
        private static readonly string Api = $"https://api.github.com/repos/{Owner}/{Repo}/contents/{FilePath}?ref={Branch}";
        private static readonly string Raw = $"https://raw.githubusercontent.com/{Owner}/{Repo}/{Branch}/{FilePath}";
        private const string KBlocked = "aw_blocked", KEtag = "aw_etag";
        private const float Watch = 5f, Slow = 120f;
        private const int Timeout = 12;

        public static KillSwitch I { get; private set; }
        public static bool Blocked { get; private set; }
        public static event Action<bool> OnChanged;

        private enum Probe { None, Active, Blocked, Offline, Unclear }

        private string etag;
        private bool tight, busy;
        private Coroutine loop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (I == null) new GameObject("KillSwitch").AddComponent<KillSwitch>();
        }

        private void Awake()
        {
            if (I != null) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            // before the network: the decision kept from last time
            Blocked = PlayerPrefs.GetInt(KBlocked, 0) == 1;
            etag = PlayerPrefs.GetString(KEtag, "");
        }

        private void OnEnable() => Arm();
        private void OnDisable() { if (loop != null) StopCoroutine(loop); loop = null; busy = false; }

        private void OnApplicationFocus(bool focus)
        {
            if (focus) Arm();
            else { if (loop != null) StopCoroutine(loop); loop = null; busy = false; }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) Arm();
            else { if (loop != null) StopCoroutine(loop); loop = null; busy = false; }
        }

        private void Arm()
        {
            if (!isActiveAndEnabled) return;
            if (loop != null) StopCoroutine(loop);
            // a check stopped half way (the loop restarted when the app came back to the front) would
            // leave "busy" set for ever and no check would run again: start clean
            busy = false;
            loop = StartCoroutine(Loop());
        }

        private IEnumerator Loop()
        {
            while (true)
            {
                yield return Check();
                yield return new WaitForSecondsRealtime(tight ? Slow : Watch);
            }
        }

        /// <summary>The CI test only: shows the switched-off page for a screenshot (nothing kept).</summary>
        public static void Preview(bool blocked)
        {
            Blocked = blocked;
            OnChanged?.Invoke(blocked);
        }

        /// <summary>One look at the file now (the "check again" button).</summary>
        public void CheckNow() => StartCoroutine(Check());

        public IEnumerator Check()
        {
            if (busy) yield break;
            busy = true;
            Probe answer = Probe.Offline;
            foreach (var url in new[] { Api, Raw })
            {
                Probe r = Probe.Offline;
                yield return Fetch(url, url == Api, p => r = p);
                if (r == Probe.Active || r == Probe.Blocked) { answer = r; break; }
                if (r == Probe.None) { busy = false; yield break; }   // 304: nothing changed (never the cached raw file then)
            }
            App.Log("killswitch answer " + answer);
            if (answer == Probe.Active && Blocked) Store(false);
            else if (answer == Probe.Blocked && !Blocked) Store(true);
            busy = false;   // offline or unclear: nothing changes
        }

        private IEnumerator Fetch(string baseUrl, bool isApi, Action<Probe> done)
        {
            var url = baseUrl + (baseUrl.Contains("?") ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Timeout;
                req.SetRequestHeader("Cache-Control", "no-cache, no-store, max-age=0");
                req.SetRequestHeader("User-Agent", "AgentsWarfare");
                if (isApi)
                {
                    req.SetRequestHeader("Accept", "application/vnd.github.raw");
                    req.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
                    if (!string.IsNullOrEmpty(etag)) req.SetRequestHeader("If-None-Match", etag);
                }
                yield return req.SendWebRequest();
                long code = req.responseCode;
                if (code == 304) { done(Probe.None); yield break; }
                if (code == 403 || code == 429) { tight = true; done(Probe.Offline); yield break; }
                if (req.result != UnityWebRequest.Result.Success || code != 200) { done(Probe.Offline); yield break; }
                if (isApi)
                {
                    var tag = req.GetResponseHeader("ETag");
                    if (!string.IsNullOrEmpty(tag)) { etag = tag; PlayerPrefs.SetString(KEtag, tag); }
                    tight = false;
                }
                done(Parse(req.downloadHandler.text));
            }
        }

        private static Probe Parse(string raw)
        {
            if (raw == null) return Probe.Unclear;
            var s = raw.Replace("﻿", "").Trim().ToLowerInvariant();
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s.Substring(0, nl).Trim();
            while (s.Length >= 2 && ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\'')))
                s = s.Substring(1, s.Length - 2).Trim();
            if (s.Length == 0) return Probe.Unclear;
            if (s == "true" || s == "1" || s == "yes" || s == "on") return Probe.Active;
            if (s == "false" || s == "0" || s == "no" || s == "off") return Probe.Blocked;
            return Probe.Unclear;
        }

        private void Store(bool value)
        {
            Blocked = value;
            PlayerPrefs.SetInt(KBlocked, value ? 1 : 0);
            PlayerPrefs.Save();
            App.Log("killswitch " + (value ? "blocked" : "active"));
            OnChanged?.Invoke(value);
        }
    }
}
