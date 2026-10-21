using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>Entry point: builds the menus and moves between screens following the session state.</summary>
    public class App : MonoBehaviour
    {
        public static App I;
        public const string GameName = "URBAN AGENTS";
        public static void Log(string message) => Debug.Log("[UA] " + message);

        public string Current { get; private set; }
        public MatchConfig Draft = new MatchConfig();
        public bool DraftPractice;
        public bool Paused { get; private set; }

        private RectTransform root;
        private readonly Dictionary<string, RectTransform> screens = new Dictionary<string, RectTransform>();
        private Hud hud;
        private Text toast, menuPlayer;
        private Image splashBar;
        private float toastUntil;
        private string settingsReturn = "menu";

        // create / join / lobby / results widgets
        private Text createTitle;
        private Button createButton;
        private InputField groupNameField, ipField, nameField;
        private RectTransform joinList;
        private Text joinStatus;
        private float joinRefresh;
        private Text lobbyTitle, lobbyInfo, lobbyIp, lobbyLeft, lobbyRight, lobbyLeftTitle, lobbyRightTitle, lobbyWait, primaryText, secondaryText;
        private Button lobbyStart, lobbyTeam;
        private RectTransform pausePanel;
        private Text resultsTitle, resultsTable, resultsInfo;
        private Button resultsLobby;

        private void Awake()
        {
            I = this;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Settings.Apply();
            gameObject.AddComponent<Session>();
            gameObject.AddComponent<Controls>();
            UIKit.Init();
            hud = new GameObject("HUD").AddComponent<Hud>();
            hud.Build();
            hud.SetVisible(false);
            var canvas = UIKit.CreateCanvas("Menus", 10);
            root = (RectTransform)canvas.transform;

            BuildSplash();
            BuildMenu();
            BuildWifi();
            BuildCreate();
            BuildJoin();
            BuildLobby();
            BuildSettings();
            BuildGame();
            BuildResults();
            toast = UIKit.Txt(root, "", 34, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
            toast.rectTransform.At(0.5f, 0f, 0, 70, 1600, 60);

            Session.I.PhaseChanged += OnPhase;
            Session.I.Disconnected += reason => { Paused = false; ShowScreen("menu"); Toast(reason); };
            Session.I.ShotSeen += OnRemoteShot;
            ShowScreen("splash");
            Log("ui-start");
        }

        private IEnumerator Start()
        {
            float t = 0;
            while (t < 2.6f)
            {
                t += Time.unscaledDeltaTime;
                splashBar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(t / 2.4f), 1f);
                yield return null;
            }
            if (Current == "splash") ShowScreen("menu");
        }

        // ------------------------------------------------------------------ navigation

        public void ShowScreen(string name)
        {
            if (name != "game" && name != "settings" && Paused)
            {
                Paused = false;
                Session.I.Paused = false;
                Time.timeScale = 1f;
            }
            Current = name;
            foreach (var pair in screens) pair.Value.gameObject.SetActive(pair.Key == name);
            hud.SetVisible(name == "game");
            if (pausePanel != null) pausePanel.gameObject.SetActive(Paused);
            if (name == "join") Session.I.StartDiscovery(); else Session.I.StopDiscovery();
            if (name == "menu") menuPlayer.text = "PLAYER:  <color=#ffa020>" + Settings.PlayerName + "</color>";
            if (name == "lobby") RefreshLobby();
            Log("state=" + name);
        }

        public void Toast(string message)
        {
            toast.text = message;
            toastUntil = Time.unscaledTime + 4f;
        }

        private void OnPhase(Phase phase)
        {
            var s = Session.I;
            if (!s.Active) return;
            switch (phase)
            {
                case Phase.Lobby: ShowScreen("lobby"); break;
                case Phase.Countdown:
                case Phase.Playing:
                    if (Current != "game") ShowScreen("game");
                    break;
                case Phase.Ended:
                    FillResults();
                    ShowScreen("results");
                    break;
            }
        }

        private void Update()
        {
            var s = Session.I;
            Controls.Enabled = Current == "game" && !Paused;
            if (Controls.PausePressed)
            {
                if (Current == "game") SetPaused(!Paused);
                else Back();
            }
            if (Current == "join" && (joinRefresh -= Time.unscaledDeltaTime) <= 0) { joinRefresh = 0.5f; RefreshJoin(); }
            if (Current == "lobby") RefreshLobby();
            if (Current == "results") resultsInfo.text = s.IsHost ? $"Back to the lobby in {Mathf.CeilToInt(s.TimeLeft)}s" : $"Next round in {Mathf.CeilToInt(s.TimeLeft)}s";
            toast.color = new Color(1, 1, 1, Mathf.Clamp01(toastUntil - Time.unscaledTime));
            if (!Application.isMobilePlatform && !AutoTest.Running)
            {
                bool lockCursor = Current == "game" && !Paused;
                Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !lockCursor;
            }
        }

        private void Back()
        {
            switch (Current)
            {
                case "wifi": ShowScreen("menu"); break;
                case "settings":
                    ShowScreen(settingsReturn);
                    if (settingsReturn == "game") SetPaused(true);
                    break;
                case "create": ShowScreen(DraftPractice ? "menu" : "wifi"); break;
                case "join": ShowScreen("wifi"); break;
                case "lobby": LeaveGroup(); break;
                case "menu": break;
            }
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            pausePanel.gameObject.SetActive(paused);
            if (Session.I.Practice)
            {
                Session.I.Paused = paused;
                Time.timeScale = paused ? 0f : 1f;
            }
            Log("paused=" + paused);
        }

        public void LeaveGroup()
        {
            Paused = false;
            Time.timeScale = 1f;
            Session.I.Leave();
            World.I?.Clear();
            ShowScreen("menu");
        }

        // ------------------------------------------------------------------ screens

        private RectTransform MakeScreen(string name, bool dim = true)
        {
            var r = UIKit.Node(root, "Screen " + name).Fill();
            if (dim)
            {
                var bg = UIKit.Img(r, new Color(0.02f, 0.03f, 0.05f, 0.55f));
                bg.rectTransform.Fill();
            }
            screens[name] = r;
            return r;
        }

        private void BuildSplash()
        {
            var s = MakeScreen("splash", false);
            var bg = UIKit.Img(s, new Color(0.02f, 0.025f, 0.035f, 1f));
            bg.rectTransform.Fill();
            var glow = UIKit.Img(s, new Color(1f, 0.55f, 0.1f, 0.12f), UIKit.Soft);
            glow.rectTransform.At(0.5f, 0.5f, 0, 60, 1500, 700);
            var title = UIKit.Txt(s, GameName, 150, TextAnchor.MiddleCenter, UIKit.Accent).Shadowed();
            title.rectTransform.At(0.5f, 0.5f, 0, 90, 1700, 190);
            var sub = UIKit.Txt(s, "LAN MULTIPLAYER  -  WI-FI & HOTSPOT  -  THIRD PERSON SHOOTER", 34, TextAnchor.MiddleCenter, UIKit.Muted);
            sub.rectTransform.At(0.5f, 0.5f, 0, -20, 1700, 50);
            var barBack = UIKit.Img(s, new Color(1, 1, 1, 0.1f), UIKit.Rounded);
            barBack.rectTransform.At(0.5f, 0.5f, 0, -130, 700, 16);
            splashBar = UIKit.Img(barBack.transform, UIKit.Accent, UIKit.Rounded);
            splashBar.rectTransform.Fill();
            splashBar.rectTransform.anchorMax = new Vector2(0, 1);
            var credits = UIKit.Txt(s, "Made with Unity  -  3D characters, animations and city by Quaternius (CC0)", 26, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.4f));
            credits.rectTransform.At(0.5f, 0f, 0, 60, 1700, 40);
        }

        private void BuildMenu()
        {
            var s = MakeScreen("menu", false);
            var side = UIKit.Img(s, new Color(0.02f, 0.03f, 0.05f, 0.78f));
            side.rectTransform.anchorMin = Vector2.zero;
            side.rectTransform.anchorMax = new Vector2(0, 1);
            side.rectTransform.pivot = new Vector2(0, 0.5f);
            side.rectTransform.sizeDelta = new Vector2(760, 0);
            side.rectTransform.anchoredPosition = Vector2.zero;
            var title = UIKit.Txt(side.transform, "URBAN\n<color=#ffa020>AGENTS</color>", 110, TextAnchor.UpperLeft).Shadowed();
            title.lineSpacing = 0.85f;
            title.rectTransform.At(0f, 1f, 90, -80, 700, 260, 0f, 1f);
            var tag = UIKit.Txt(side.transform, "Squad up on the same Wi-Fi or hotspot. No internet needed.", 28, TextAnchor.UpperLeft, UIKit.Muted);
            tag.rectTransform.At(0f, 1f, 92, -350, 620, 80, 0f, 1f);
            string[] labels = { "PLAY ON WI-FI", "PRACTICE VS BOTS", "SETTINGS", "QUIT" };
            System.Action[] actions =
            {
                () => ShowScreen("wifi"),
                () => OpenCreate(true),
                () => { settingsReturn = "menu"; OpenSettings(); },
                () => Application.Quit(),
            };
            for (int i = 0; i < labels.Length; i++)
            {
                var b = UIKit.Btn(side.transform, labels[i], actions[i], 44, i == 0 ? UIKit.AccentDark : (Color?)null);
                b.GetComponent<RectTransform>().At(0f, 1f, 90, -470 - i * 118, 560, 98, 0f, 1f);
            }
            menuPlayer = UIKit.Txt(side.transform, "", 30, TextAnchor.LowerLeft);
            menuPlayer.rectTransform.At(0f, 0f, 92, 50, 620, 50, 0f, 0f);
            var info = UIKit.Txt(s, "v1.0  -  LAN multiplayer  -  up to 10 players", 24, TextAnchor.LowerRight, new Color(1, 1, 1, 0.5f));
            info.rectTransform.At(1f, 0f, -40, 30, 900, 40, 1f, 0f);
        }

        private void BuildWifi()
        {
            var s = MakeScreen("wifi");
            UIKit.Title(s, "PLAY ON WI-FI / HOTSPOT", -70, 70);
            var help = UIKit.Txt(s, "All phones must be on the same Wi-Fi - or one phone turns on its hotspot and the others connect to it.", 30, TextAnchor.MiddleCenter, UIKit.Muted);
            help.rectTransform.At(0.5f, 1f, 0, -180, 1500, 80, 0.5f, 1f);
            Card(s, -390, "CREATE GROUP", "Host a match on this phone.\nChoose the mode, map, time and bots,\nthen start when everyone has joined.", () => OpenCreate(false));
            Card(s, 390, "JOIN GROUP", "Find the groups hosted nearby\nand join with one tap - or type the\nhost's IP address.", () => ShowScreen("join"));
            var back = UIKit.Btn(s, "BACK", () => ShowScreen("menu"), 38);
            back.GetComponent<RectTransform>().At(0.5f, 0f, 0, 70, 360, 90, 0.5f, 0f);
        }

        private void Card(Transform parent, float x, string title, string text, System.Action onClick)
        {
            var b = UIKit.Btn(parent, "", onClick, 10, UIKit.PanelColor);
            b.GetComponent<RectTransform>().At(0.5f, 0.5f, x, -40, 700, 470);
            var t = UIKit.Txt(b.transform, title, 58, TextAnchor.UpperCenter, UIKit.Accent).Shadowed();
            t.rectTransform.At(0.5f, 1f, 0, -60, 640, 80, 0.5f, 1f);
            var d = UIKit.Txt(b.transform, text, 30, TextAnchor.UpperCenter, UIKit.TextColor);
            d.rectTransform.At(0.5f, 1f, 0, -190, 640, 220, 0.5f, 1f);
        }

        public void OpenCreate(bool practice)
        {
            DraftPractice = practice;
            if (Draft.GroupName == "Group") Draft.GroupName = Settings.PlayerName + "'s group";
            createTitle.text = practice ? "PRACTICE VS BOTS" : "CREATE GROUP";
            createButton.SetLabel(practice ? "START PRACTICE" : "CREATE GROUP");
            groupNameField.text = Draft.GroupName;
            groupNameField.transform.parent.gameObject.SetActive(!practice);
            if (practice && Draft.Bots == 0) Draft.Bots = 5;
            ShowScreen("create");
        }

        private void BuildCreate()
        {
            var s = MakeScreen("create");
            createTitle = UIKit.Title(s, "CREATE GROUP", -40, 66);
            var panel = UIKit.Panel(s, 1180, 800, -40);
            var nameRow = UIKit.Node(panel, "NameRow").At(0.5f, 1f, 0, -30, 1080, 84, 0.5f, 1f);
            var nameLabel = UIKit.Txt(nameRow, "GROUP NAME", 34, TextAnchor.MiddleLeft, UIKit.Muted);
            nameLabel.rectTransform.Fill();
            nameLabel.rectTransform.offsetMin = new Vector2(30, 0);
            groupNameField = UIKit.Input(nameRow, Draft.GroupName, "Group name", 34, 20);
            groupNameField.GetComponent<RectTransform>().At(1f, 0.5f, -20, 0, 560, 74, 1f, 0.5f);
            groupNameField.onEndEdit.AddListener(v => Draft.GroupName = Settings.Clean(v, 24));
            byte[] minutes = { 3, 5, 8, 10, 15 };
            UIKit.Stepper(panel, "GAME MODE", -130, () => MatchConfig.ModeNames[(int)Draft.Mode], d => Draft.Mode = (Mode)(((int)Draft.Mode + 2 + d) % 2), 1080);
            UIKit.Stepper(panel, "MAP", -225, () => MatchConfig.MapNames[Draft.Map], d => Draft.Map = (byte)((Draft.Map + 3 + d) % 3), 1080);
            UIKit.Stepper(panel, "MATCH TIME", -320, () => Draft.Minutes + " MIN", d =>
            {
                int i = System.Array.IndexOf(minutes, Draft.Minutes);
                Draft.Minutes = minutes[Mathf.Clamp((i < 0 ? 1 : i) + d, 0, minutes.Length - 1)];
            }, 1080);
            UIKit.Stepper(panel, "MAX PLAYERS", -415, () => Draft.MaxPlayers.ToString(), d => Draft.MaxPlayers = (byte)Mathf.Clamp(Draft.MaxPlayers + d, 2, 10), 1080);
            UIKit.Stepper(panel, "BOTS", -510, () => Draft.Bots == 0 ? "NONE" : Draft.Bots.ToString(), d => Draft.Bots = (byte)Mathf.Clamp(Draft.Bots + d, 0, 9), 1080);
            UIKit.Stepper(panel, "BOT SKILL", -605, () => MatchConfig.SkillNames[Draft.BotSkill], d => Draft.BotSkill = (byte)Mathf.Clamp(Draft.BotSkill + d, 0, 2), 1080);
            var back = UIKit.Btn(s, "BACK", Back, 38);
            back.GetComponent<RectTransform>().At(0.5f, 0f, -300, 50, 380, 96, 0.5f, 0f);
            createButton = UIKit.Btn(s, "CREATE GROUP", CreateFromDraft, 40, UIKit.AccentDark);
            createButton.GetComponent<RectTransform>().At(0.5f, 0f, 200, 50, 560, 96, 0.5f, 0f);
        }

        public void CreateFromDraft()
        {
            Draft.GroupName = Settings.Clean(groupNameField.text.Length > 0 ? groupNameField.text : Draft.GroupName, 24);
            var config = new MatchConfig
            {
                GroupName = Draft.GroupName, Mode = Draft.Mode, Map = Draft.Map, Minutes = Draft.Minutes,
                MaxPlayers = Draft.MaxPlayers, Bots = Draft.Bots, BotSkill = Draft.BotSkill,
            };
            if (!Session.I.Host(config, DraftPractice))
            {
                Toast(Session.I.Status);
                return;
            }
            ShowScreen("lobby");
        }

        private void BuildJoin()
        {
            var s = MakeScreen("join");
            UIKit.Title(s, "JOIN GROUP", -40, 66);
            var panel = UIKit.Panel(s, 1500, 610, 40);
            joinList = UIKit.Node(panel, "List").Fill(24);
            joinStatus = UIKit.Txt(panel, "", 32, TextAnchor.MiddleCenter, UIKit.Muted);
            joinStatus.rectTransform.Fill(60);
            var ipRow = UIKit.Node(s, "IpRow").At(0.5f, 0f, 0, 190, 1500, 90, 0.5f, 0.5f);
            var ipLabel = UIKit.Txt(ipRow, "OR TYPE THE HOST IP:", 30, TextAnchor.MiddleLeft, UIKit.Muted);
            ipLabel.rectTransform.Fill();
            ipField = UIKit.Input(ipRow, PlayerPrefs.GetString("lastip", ""), "192.168.43.1", 34, 21);
            ipField.contentType = InputField.ContentType.Standard;
            ipField.GetComponent<RectTransform>().At(0.5f, 0.5f, 120, 0, 520, 80);
            var connect = UIKit.Btn(ipRow, "CONNECT", ConnectToTypedIp, 34, UIKit.AccentDark);
            connect.GetComponent<RectTransform>().At(1f, 0.5f, 0, 0, 300, 80, 1f, 0.5f);
            var back = UIKit.Btn(s, "BACK", Back, 38);
            back.GetComponent<RectTransform>().At(0.5f, 0f, 0, 50, 380, 96, 0.5f, 0f);
        }

        private void ConnectToTypedIp()
        {
            string text = ipField.text.Trim();
            int port = Session.GamePort;
            if (text.Contains(":"))
            {
                int.TryParse(text.Substring(text.IndexOf(':') + 1), out port);
                text = text.Substring(0, text.IndexOf(':'));
            }
            if (!IPAddress.TryParse(text, out var address)) { Toast("Type an address like 192.168.43.1"); return; }
            PlayerPrefs.SetString("lastip", ipField.text.Trim());
            JoinGroup(new IPEndPoint(address, port == 0 ? Session.GamePort : port));
        }

        public void JoinGroup(IPEndPoint host)
        {
            Session.I.Join(host);
            ShowScreen("lobby");
        }

        private readonly List<string> shownGroups = new List<string>();

        private void RefreshJoin()
        {
            // the same host can answer on loopback and on its Wi-Fi address: show it once
            var all = Session.I.Groups.Values.ToList();
            var groups = all.Where(g => !IPAddress.IsLoopback(g.Address.Address) || !all.Any(o => o != g && o.Name == g.Name && o.Port == g.Port))
                .OrderBy(g => g.Name).ToList();
            var keys = groups.Select(g => g.Address + "|" + g.Players + "|" + g.Phase).ToList();
            joinStatus.text = groups.Count == 0 ? "Searching for groups on this Wi-Fi / hotspot ...\n\nAsk a friend to tap CREATE GROUP." : "";
            if (keys.SequenceEqual(shownGroups)) return;
            shownGroups.Clear();
            shownGroups.AddRange(keys);
            foreach (Transform child in joinList) Destroy(child.gameObject);
            for (int i = 0; i < groups.Count && i < 5; i++)
            {
                var g = groups[i];
                var row = UIKit.Img(joinList, new Color(1, 1, 1, 0.05f), UIKit.Rounded);
                row.rectTransform.At(0.5f, 1f, 0, -i * 112, 1440, 100, 0.5f, 1f);
                var name = UIKit.Txt(row.transform, $"{g.Name}\n<size=24><color=#9aa4b0>{MatchConfig.ModeNames[(int)g.Mode]}  -  {MatchConfig.MapNames[g.Map % 3]}  -  {g.Address.Address}</color></size>", 34, TextAnchor.MiddleLeft);
                name.rectTransform.Fill();
                name.rectTransform.offsetMin = new Vector2(30, 0);
                var count = UIKit.Txt(row.transform, $"{g.Players}/{g.MaxPlayers}" + (g.Phase != Phase.Lobby ? "\n<size=22>IN MATCH</size>" : ""), 32, TextAnchor.MiddleCenter, UIKit.Accent);
                count.rectTransform.At(1f, 0.5f, -330, 0, 160, 90);
                var target = g.Address;
                var join = UIKit.Btn(row.transform, "JOIN", () => JoinGroup(target), 34, UIKit.AccentDark);
                join.GetComponent<RectTransform>().At(1f, 0.5f, -20, 0, 200, 76, 1f, 0.5f);
            }
        }

        private void BuildLobby()
        {
            var s = MakeScreen("lobby");
            lobbyTitle = UIKit.Title(s, "LOBBY", -30, 62);
            lobbyInfo = UIKit.Txt(s, "", 30, TextAnchor.MiddleCenter, UIKit.Muted);
            lobbyInfo.rectTransform.At(0.5f, 1f, 0, -120, 1700, 44, 0.5f, 1f);
            lobbyIp = UIKit.Txt(s, "", 30, TextAnchor.MiddleCenter, UIKit.Accent);
            lobbyIp.rectTransform.At(0.5f, 1f, 0, -170, 1700, 44, 0.5f, 1f);
            var left = UIKit.Panel(s, 760, 520, 30);
            left.anchoredPosition = new Vector2(-400, 30);
            var right = UIKit.Panel(s, 760, 520, 30);
            right.anchoredPosition = new Vector2(400, 30);
            lobbyLeftTitle = UIKit.Txt(left, "TEAM BLUE", 40, TextAnchor.UpperCenter, UIKit.Blue);
            lobbyLeftTitle.rectTransform.At(0.5f, 1f, 0, -20, 700, 56, 0.5f, 1f);
            lobbyRightTitle = UIKit.Txt(right, "TEAM RED", 40, TextAnchor.UpperCenter, UIKit.Red);
            lobbyRightTitle.rectTransform.At(0.5f, 1f, 0, -20, 700, 56, 0.5f, 1f);
            lobbyLeft = UIKit.Txt(left, "", 32, TextAnchor.UpperLeft);
            lobbyLeft.rectTransform.Fill(40);
            lobbyLeft.rectTransform.offsetMax = new Vector2(-40, -90);
            lobbyRight = UIKit.Txt(right, "", 32, TextAnchor.UpperLeft);
            lobbyRight.rectTransform.Fill(40);
            lobbyRight.rectTransform.offsetMax = new Vector2(-40, -90);

            var loadout = UIKit.Node(s, "Loadout").At(0.5f, 0f, 0, 200, 1560, 84, 0.5f, 0.5f);
            primaryText = LoadoutStepper(loadout, -400, "PRIMARY", true);
            secondaryText = LoadoutStepper(loadout, 400, "PISTOL", false);

            var leave = UIKit.Btn(s, "LEAVE", LeaveGroup, 36);
            leave.GetComponent<RectTransform>().At(0.5f, 0f, -560, 50, 320, 96, 0.5f, 0f);
            lobbyTeam = UIKit.Btn(s, "SWITCH TEAM", () => { var me = Session.I.Local; if (me != null) Session.I.SetTeam((byte)(1 - me.Team)); RefreshLobby(); }, 34);
            lobbyTeam.GetComponent<RectTransform>().At(0.5f, 0f, -160, 50, 400, 96, 0.5f, 0f);
            lobbyStart = UIKit.Btn(s, "START MATCH", () => Session.I.StartMatch(), 40, UIKit.AccentDark);
            lobbyStart.GetComponent<RectTransform>().At(0.5f, 0f, 340, 50, 560, 96, 0.5f, 0f);
            lobbyWait = UIKit.Txt(s, "Waiting for the host to start ...", 32, TextAnchor.MiddleCenter, UIKit.Muted);
            lobbyWait.rectTransform.At(0.5f, 0f, 340, 98, 700, 96);
        }

        private Text LoadoutStepper(Transform parent, float x, string label, bool primary)
        {
            var row = UIKit.Node(parent, label).At(0.5f, 0.5f, x, 0, 740, 84);
            var bg = UIKit.Img(row, new Color(0, 0, 0, 0.5f), UIKit.Rounded);
            bg.rectTransform.Fill();
            var l = UIKit.Txt(row, label, 28, TextAnchor.MiddleLeft, UIKit.Muted);
            l.rectTransform.Fill();
            l.rectTransform.offsetMin = new Vector2(26, 0);
            var v = UIKit.Txt(row, "", 34, TextAnchor.MiddleCenter, UIKit.Accent);
            v.rectTransform.At(0.5f, 0.5f, 90, 0, 320, 84);
            System.Action<int> change = d =>
            {
                byte p = Settings.Primary, sec = Settings.Secondary;
                if (primary) p = (byte)((p + 4 + d) % 4);
                else sec = (byte)(sec == 4 ? 5 : 4);
                Session.I.SetLoadout(p, sec);
                RefreshLobby();
            };
            var minus = UIKit.Btn(row, "<", () => change(-1), 38);
            minus.GetComponent<RectTransform>().At(0.5f, 0.5f, -110, 0, 84, 70);
            var plus = UIKit.Btn(row, ">", () => change(1), 38);
            plus.GetComponent<RectTransform>().At(1f, 0.5f, -10, 0, 84, 70, 1f, 0.5f);
            return v;
        }

        private void RefreshLobby()
        {
            var s = Session.I;
            if (lobbyTitle == null) return;
            bool tdm = s.Config.Mode == Mode.TeamDeathmatch;
            var me = s.Local;
            if (!s.Active) return;
            if (!s.IsHost && me == null)
            {
                lobbyTitle.text = "JOINING ...";
                lobbyInfo.text = s.Status;
                lobbyIp.text = "";
                lobbyLeft.text = lobbyRight.text = "";
                lobbyStart.gameObject.SetActive(false);
                lobbyTeam.gameObject.SetActive(false);
                lobbyWait.text = "";
                return;
            }
            lobbyTitle.text = s.Practice ? "PRACTICE" : s.Config.GroupName.ToUpperInvariant();
            lobbyInfo.text = $"{MatchConfig.ModeNames[(int)s.Config.Mode]}   -   {MatchConfig.MapNames[s.Config.Map % 3]}   -   {s.Config.Minutes} MIN   -   BOTS: {s.Config.Bots} ({MatchConfig.SkillNames[s.Config.BotSkill % 3]})";
            lobbyIp.text = s.Practice ? "Only you and the bots - no network used" :
                s.IsHost ? "Friends: JOIN GROUP, or type this IP:  " + (s.HostAddresses.Length > 0 ? s.HostAddresses : "(no Wi-Fi found)") : "Connected - " + s.Players.Count(p => !p.Value.IsBot) + " players in the group";
            lobbyLeftTitle.text = tdm ? "TEAM BLUE" : "PLAYERS";
            lobbyRightTitle.text = tdm ? "TEAM RED" : "";
            var ordered = s.Players.Values.OrderBy(p => p.IsBot).ThenBy(p => p.Id).ToList();
            string Line(PlayerState p) => (p.Id == s.LocalId ? "<color=#ffa020>" : "") + (p.Id == 0 && !p.IsBot ? "[HOST] " : "") + p.Name + (p.Id == s.LocalId ? "</color>" : "")
                + $"  <size=22><color=#8a94a0>{Weapons.Get(p.Primary).Name} / {Weapons.Get(p.Secondary).Name}</color></size>";
            if (tdm)
            {
                lobbyLeft.text = string.Join("\n", ordered.Where(p => p.Team == 0).Select(Line));
                lobbyRight.text = string.Join("\n", ordered.Where(p => p.Team == 1).Select(Line));
            }
            else
            {
                var list = ordered.Select(Line).ToList();
                lobbyLeft.text = string.Join("\n", list.Take(7));
                lobbyRight.text = string.Join("\n", list.Skip(7));
            }
            primaryText.text = Weapons.Get(Settings.Primary).Name;
            secondaryText.text = Weapons.Get(Settings.Secondary).Name;
            lobbyStart.gameObject.SetActive(s.IsHost && s.Phase == Phase.Lobby);
            lobbyTeam.gameObject.SetActive(tdm && s.Phase == Phase.Lobby);
            lobbyWait.text = s.IsHost ? "" : s.Phase == Phase.Lobby ? "Waiting for the host to start ..." : "Match in progress - joining ...";
        }

        private void OpenSettings()
        {
            nameField.text = Settings.PlayerName;
            ShowScreen("settings");
        }

        private void BuildSettings()
        {
            var s = MakeScreen("settings");
            UIKit.Title(s, "SETTINGS", -40, 66);
            var panel = UIKit.Panel(s, 1180, 720, -20);
            var nameRow = UIKit.Node(panel, "NameRow").At(0.5f, 1f, 0, -30, 1080, 84, 0.5f, 1f);
            var nameLabel = UIKit.Txt(nameRow, "PLAYER NAME", 34, TextAnchor.MiddleLeft, UIKit.Muted);
            nameLabel.rectTransform.Fill();
            nameLabel.rectTransform.offsetMin = new Vector2(30, 0);
            nameField = UIKit.Input(nameRow, Settings.PlayerName, "Your name", 34);
            nameField.GetComponent<RectTransform>().At(1f, 0.5f, -20, 0, 560, 74, 1f, 0.5f);
            nameField.onEndEdit.AddListener(v => Settings.PlayerName = v);
            UIKit.Stepper(panel, "GRAPHICS", -130, () => Settings.QualityNames[Settings.Quality], d => Settings.Quality = Mathf.Clamp(Settings.Quality + d, 0, 2), 1080);
            UIKit.Stepper(panel, "LOOK SENSITIVITY", -225, () => "x" + Settings.Sensitivity.ToString("0.0"), d => Settings.Sensitivity = Mathf.Round((Settings.Sensitivity + d * 0.1f) * 10f) / 10f, 1080);
            UIKit.Stepper(panel, "LOOK DIRECTION", -320, () => Settings.ReverseLook ? "REVERSED" : "NORMAL", d => Settings.ReverseLook = !Settings.ReverseLook, 1080);
            UIKit.Stepper(panel, "SOUND VOLUME", -415, () => Mathf.RoundToInt(Settings.Volume * 100) + "%", d => Settings.Volume = Mathf.Round(Settings.Volume * 10 + d) / 10f, 1080);
            UIKit.Stepper(panel, "SHOW FPS", -510, () => Settings.ShowFps ? "ON" : "OFF", d => Settings.ShowFps = !Settings.ShowFps, 1080);
            var hint = UIKit.Txt(panel, "Controls: left side = move, drag right side = look, drag the FIRE button to aim while shooting.", 24, TextAnchor.MiddleCenter, UIKit.Muted);
            hint.rectTransform.At(0.5f, 0f, 0, 50, 1080, 60);
            var back = UIKit.Btn(s, "BACK", () => { Settings.PlayerName = nameField.text; Back(); }, 38);
            back.GetComponent<RectTransform>().At(0.5f, 0f, 0, 50, 380, 96, 0.5f, 0f);
        }

        private void BuildGame()
        {
            var s = MakeScreen("game", false);
            pausePanel = UIKit.Node(s, "Pause").Fill();
            var dim = UIKit.Img(pausePanel, new Color(0, 0, 0, 0.6f), null, true);
            dim.rectTransform.Fill();
            var panel = UIKit.Panel(pausePanel, 700, 620);
            var title = UIKit.Txt(panel, "PAUSED", 64, TextAnchor.UpperCenter, UIKit.Accent).Shadowed();
            title.rectTransform.At(0.5f, 1f, 0, -40, 600, 90, 0.5f, 1f);
            var resume = UIKit.Btn(panel, "RESUME", () => SetPaused(false), 40, UIKit.AccentDark);
            resume.GetComponent<RectTransform>().At(0.5f, 1f, 0, -170, 520, 96, 0.5f, 1f);
            var settings = UIKit.Btn(panel, "SETTINGS", () => { settingsReturn = "game"; OpenSettings(); }, 40);
            settings.GetComponent<RectTransform>().At(0.5f, 1f, 0, -290, 520, 96, 0.5f, 1f);
            var leave = UIKit.Btn(panel, "LEAVE MATCH", LeaveGroup, 40);
            leave.GetComponent<RectTransform>().At(0.5f, 1f, 0, -410, 520, 96, 0.5f, 1f);
            var hint = UIKit.Txt(panel, "", 22, TextAnchor.MiddleCenter, UIKit.Muted);
            hint.rectTransform.At(0.5f, 0f, 0, 40, 640, 40);
            pausePanel.gameObject.SetActive(false);
        }

        private void BuildResults()
        {
            var s = MakeScreen("results");
            resultsTitle = UIKit.Title(s, "MATCH OVER", -40, 80);
            var panel = UIKit.Panel(s, 1300, 640, 0);
            resultsTable = UIKit.Txt(panel, "", 32, TextAnchor.UpperLeft);
            resultsTable.rectTransform.Fill(50);
            resultsInfo = UIKit.Txt(s, "", 30, TextAnchor.MiddleCenter, UIKit.Muted);
            resultsInfo.rectTransform.At(0.5f, 0f, 0, 170, 1200, 50);
            var leave = UIKit.Btn(s, "LEAVE GROUP", LeaveGroup, 36);
            leave.GetComponent<RectTransform>().At(0.5f, 0f, -300, 50, 440, 96, 0.5f, 0f);
            resultsLobby = UIKit.Btn(s, "BACK TO LOBBY", () => Session.I.BackToLobby(), 38, UIKit.AccentDark);
            resultsLobby.GetComponent<RectTransform>().At(0.5f, 0f, 260, 50, 500, 96, 0.5f, 0f);
        }

        private void FillResults()
        {
            var s = Session.I;
            string title;
            if (s.Config.Mode == Mode.TeamDeathmatch)
            {
                title = s.TeamScore[0] == s.TeamScore[1] ? "DRAW" : s.TeamScore[0] > s.TeamScore[1] ? "<color=#4d84ff>TEAM BLUE WINS</color>" : "<color=#f24d40>TEAM RED WINS</color>";
                var me = s.Local;
                if (me != null && s.TeamScore[0] != s.TeamScore[1])
                    title += (s.TeamScore[me.Team] > s.TeamScore[1 - me.Team]) ? "\n<size=40>VICTORY</size>" : "\n<size=40>DEFEAT</size>";
            }
            else
            {
                var best = s.Players.Values.OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths).FirstOrDefault();
                title = best == null ? "MATCH OVER" : (best.Id == s.LocalId ? "<color=#ffa020>YOU WIN!</color>" : best.Name.ToUpperInvariant() + " WINS");
            }
            resultsTitle.text = title;
            resultsTable.text = Hud.ScoreboardText(s);
            resultsLobby.gameObject.SetActive(s.IsHost);
        }

        // ------------------------------------------------------------------ effects of other players' shots

        private void OnRemoteShot(ShotFx fx)
        {
            if (World.I == null || !World.I.Agents.TryGetValue(fx.Shooter, out var agent)) return;
            var def = Weapons.Get(fx.Weapon);
            var muzzle = agent.Muzzle;
            Effects.MuzzleFlash(muzzle, def);
            Effects.Tracer(muzzle.position, fx.End);
            var dir = (fx.End - fx.Origin).normalized;
            if (fx.HitKind == 0) Effects.Impact(fx.End, -dir);
            else if (fx.HitKind == 1) Effects.Blood(fx.End, -dir);
            Sfx.PlayAt(def.Sound, muzzle.position, 1f, def.SoundPitch);
            if (!agent.IsLocal) agent.PlayFire();
        }
    }
}
