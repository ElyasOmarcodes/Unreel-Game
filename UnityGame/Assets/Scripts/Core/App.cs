using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>Entry point: builds the menus and moves between screens following the session state.</summary>
    public partial class App : MonoBehaviour
    {
        public static App I;
        public const string GameName = "AGENT WARFARE";
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
        private GameObject[] botRows = new GameObject[0];
        private Button createButton;
        private InputField groupNameField, ipField, passwordField, joinPasswordField;
        private GameObject passwordRow;
        private Text joinPasswordTitle;
        private IPEndPoint lastJoinHost;
        private RectTransform joinList;
        private Text joinStatus;
        private float joinRefresh;
        private Text lobbyTitle, lobbyInfo, lobbyIp, lobbyLeft, lobbyRight, lobbyLeftTitle, lobbyRightTitle, lobbyWait, primaryText, secondaryText;
        private Button lobbyStart, lobbyTeam;
        private RectTransform pausePanel;
        private Text resultsTitle, resultsInfo;
        private Button resultsLobby;

        private void Awake()
        {
            I = this;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Settings.Apply();
            gameObject.AddComponent<Session>();
            gameObject.AddComponent<Controls>();
            gameObject.AddComponent<PerfGuard>();
            gameObject.AddComponent<Music>();
            // the phone is short of memory: let go of everything not in use right now
            Application.lowMemory += () => Resources.UnloadUnusedAssets();
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
            BuildJoinPassword();
            BuildLobby();
            BuildSettings();
            BuildLayoutEditor();
            BuildProfile();
            BuildGame();
            BuildResults();
            toast = UIKit.Txt(root, "", 34, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
            toast.rectTransform.At(0.5f, 0f, 0, 70, 1600, 60);

            Session.I.PhaseChanged += OnPhase;
            Session.I.Disconnected += reason =>
            {
                Paused = false;
                // a locked group: ask for its password and try again
                if ((reason == Session.PasswordNeeded || reason == Session.WrongPassword) && lastJoinHost != null) OpenJoinPassword(lastJoinHost, null);
                else if (Current != "results") ShowScreen("menu");   // the results table stays until closed
                Toast(reason);
            };
            Session.I.ShotSeen += OnRemoteShot;
            Session.I.Healed += OnHealed;
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
            if (name == "menu") menuPlayer.text = "PLAYER:  <color=#ffa020>" + Rtl.Shape(Settings.PlayerName) + "</color>";
            if (name == "lobby") RefreshLobby();
            if (name != "profile" && stage != null) stage.SetActive(false);
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
                // the results table stays open until the player closes it
                case Phase.Lobby: if (Current != "results") ShowScreen("lobby"); break;
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
            if (Current == "results")
            {
                resultsInfo.text = s.Phase == Phase.Ended
                    ? $"RATING = score next to the best player   -   the group goes back to the lobby in {Mathf.CeilToInt(s.TimeLeft)}s"
                    : "RATING = score next to the best player   -   the group is in the lobby: close the table when you are ready";
                resultsLobby.gameObject.SetActive(s.IsHost && s.Phase == Phase.Ended);
            }
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
                case "layout": ShowScreen("settings"); break;
                case "profile": ShowScreen("menu"); break;
                case "settings":
                    ShowScreen(settingsReturn);
                    if (settingsReturn == "game") SetPaused(true);
                    break;
                case "create": ShowScreen(DraftPractice ? "menu" : "wifi"); break;
                case "join": ShowScreen("wifi"); break;
                case "password": ShowScreen("join"); break;
                case "results": ShowScreen(Session.I.Active ? "lobby" : "menu"); break;
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
            var logo = Logo();
            if (logo != null) Badge(s).At(0.5f, 0.5f, 0, 150, 560, 560);
            else
            {
                var title = UIKit.Txt(s, GameName, 150, TextAnchor.MiddleCenter, UIKit.Accent).Shadowed();
                title.rectTransform.At(0.5f, 0.5f, 0, 90, 1700, 190);
            }
            var sub = UIKit.Txt(s, "LAN MULTIPLAYER  -  WI-FI & HOTSPOT  -  THIRD PERSON SHOOTER", 34, TextAnchor.MiddleCenter, UIKit.Muted);
            sub.rectTransform.At(0.5f, 0.5f, 0, -170, 1700, 50);
            var barBack = UIKit.Img(s, new Color(1, 1, 1, 0.1f), UIKit.Rounded);
            barBack.rectTransform.At(0.5f, 0.5f, 0, -250, 700, 16);
            splashBar = UIKit.Img(barBack.transform, UIKit.Accent, UIKit.Rounded);
            splashBar.rectTransform.Fill();
            splashBar.rectTransform.anchorMax = new Vector2(0, 1);
            var credits = UIKit.Txt(s, "Made with Unity  -  3D characters, animations and city by Quaternius (CC0)\n"
                + "Military gear based on \"Military Character Kit 1.1\" by britdawgmasterfunk (sketchfab.com), CC-BY-4.0\n"
                + "Pain and death voices: \"11 male human pain/death sounds\" by Michel Baradari (opengameart.org), CC-BY 3.0; footsteps by Fantozzi, CC0; PKM model by ElyasOmarcodes", 24, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.4f));
            credits.rectTransform.At(0.5f, 0f, 0, 40, 1800, 105);
        }

        private static Sprite logoSprite;

        /// <summary>The round Agent Warfare badge (also the app icon).</summary>
        private static Sprite Logo()
        {
            if (logoSprite != null) return logoSprite;
            var tex = Resources.Load<Texture2D>("Branding/icon");
            return logoSprite = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) : null;
        }

        /// <summary>The badge cut round (the picture has dark corners).</summary>
        private static RectTransform Badge(Transform parent)
        {
            var round = UIKit.Img(parent, Color.white, UIKit.Circle);
            round.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var badge = UIKit.Img(round.transform, Color.white, Logo());
            badge.rectTransform.Fill();
            return round.rectTransform;
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
            var title = UIKit.Txt(side.transform, "AGENT\n<color=#ff3b30>WARFARE</color>", 84, TextAnchor.UpperLeft).Shadowed();
            title.lineSpacing = 0.85f;
            title.rectTransform.At(0f, 1f, 90, -90, 700, 240, 0f, 1f);
            if (Logo() != null) Badge(side.transform).At(0f, 1f, 560, -90, 170, 170, 0f, 1f);
            var tag = UIKit.Txt(side.transform, "Squad up on the same Wi-Fi or hotspot. No internet needed.", 28, TextAnchor.UpperLeft, UIKit.Muted);
            tag.rectTransform.At(0f, 1f, 92, -350, 620, 80, 0f, 1f);
            string[] labels = { "PLAY ON WI-FI", "PRACTICE VS BOTS", "PROFILE", "SETTINGS", "QUIT" };
            string[] iconNames = { "wifi", "smart_toy", "person", "settings", "exit_to_app" };
            System.Action[] actions =
            {
                () => ShowScreen("wifi"),
                () => OpenCreate(true),
                () => OpenProfile(),
                () => { settingsReturn = "menu"; OpenSettings(); },
                () => Application.Quit(),
            };
            for (int i = 0; i < labels.Length; i++)
            {
                var b = UIKit.Btn(side.transform, labels[i], actions[i], 42, i == 0 ? UIKit.AccentDark : (Color?)null, iconNames[i]);
                b.GetComponent<RectTransform>().At(0f, 1f, 90, -440 - i * 106, 560, 90, 0f, 1f);
            }
            menuPlayer = UIKit.Txt(side.transform, "", 30, TextAnchor.LowerLeft);
            menuPlayer.rectTransform.At(0f, 0f, 92, 50, 620, 50, 0f, 0f);
            var info = UIKit.Txt(s, "v1.0  -  LAN multiplayer  -  up to 10 players", 24, TextAnchor.LowerRight, new Color(1, 1, 1, 0.5f));
            info.rectTransform.At(1f, 0f, -40, 30, 900, 40, 1f, 0f);
            BuildSocial(s);
        }

        /// <summary>Where to follow the game. Only the GitHub page is live for now; the rest are filled in later.</summary>
        public static readonly (string name, string icon, string url)[] SocialLinks =
        {
            ("GITHUB", "code", "https://github.com/ElyasOmarcodes"),
            ("YOUTUBE", "play_circle_filled", ""),
            ("FACEBOOK", "public", ""),
            ("TELEGRAM", "send", ""),
            ("EMAIL", "email", ""),
        };

        private void BuildSocial(Transform s)
        {
            var label = UIKit.Txt(s, "FOLLOW AGENT WARFARE", 24, TextAnchor.LowerRight, new Color(1, 1, 1, 0.6f));
            label.rectTransform.At(1f, 0f, -40, 190, 700, 34, 1f, 0f);
            for (int i = 0; i < SocialLinks.Length; i++)
            {
                var link = SocialLinks[i];
                var b = UIKit.Btn(s, "", () =>
                {
                    if (string.IsNullOrEmpty(link.url)) Toast(link.name + ": the link is coming soon");
                    else Application.OpenURL(link.url);
                }, 10, new Color(0.08f, 0.1f, 0.14f, 0.85f));
                b.GetComponent<RectTransform>().At(1f, 0f, -40 - (SocialLinks.Length - 1 - i) * 112, 80, 96, 96, 1f, 0f);
                var ic = UIKit.IconImage(b.transform, link.icon, 52, string.IsNullOrEmpty(link.url) ? UIKit.Muted : UIKit.Accent);
                ic.rectTransform.At(0.5f, 0.5f, 0, 8, 52, 52);
                var name = UIKit.Txt(b.transform, link.name, 14, TextAnchor.LowerCenter, UIKit.Muted);
                name.rectTransform.At(0.5f, 0f, 0, 4, 96, 20, 0.5f, 0f);
            }
        }

        private void BuildWifi()
        {
            var s = MakeScreen("wifi");
            UIKit.Title(s, "PLAY ON WI-FI / HOTSPOT", -70, 70);
            var help = UIKit.Txt(s, "All phones must be on the same Wi-Fi - or one phone turns on its hotspot and the others connect to it.", 30, TextAnchor.MiddleCenter, UIKit.Muted);
            help.rectTransform.At(0.5f, 1f, 0, -180, 1500, 80, 0.5f, 1f);
            Card(s, -390, "CREATE GROUP", "Host a match on this phone.\nChoose the mode, map and time,\nthen start when everyone has joined.", () => OpenCreate(false), "group_add");
            Card(s, 390, "JOIN GROUP", "Find the groups hosted nearby\nand join with one tap - or type the\nhost's IP address.", () => ShowScreen("join"), "login");
            var back = UIKit.Btn(s, "BACK", () => ShowScreen("menu"), 36, null, "arrow_back");
            back.GetComponent<RectTransform>().At(0.5f, 0f, 0, 70, 360, 90, 0.5f, 0f);
        }

        private void Card(Transform parent, float x, string title, string text, System.Action onClick, string icon)
        {
            var b = UIKit.Btn(parent, "", onClick, 10, new Color(0.1f, 0.13f, 0.18f, 0.95f));
            b.GetComponent<RectTransform>().At(0.5f, 0.5f, x, -40, 700, 500);
            var circle = UIKit.Img(b.transform, UIKit.AccentDark, UIKit.Circle);
            circle.rectTransform.At(0.5f, 1f, 0, -40, 150, 150, 0.5f, 1f);
            var ic = UIKit.IconImage(circle.transform, icon, 96);
            ic.rectTransform.At(0.5f, 0.5f, 0, 0, 96, 96);
            var t = UIKit.Txt(b.transform, title, 54, TextAnchor.UpperCenter, UIKit.Accent).Shadowed();
            t.rectTransform.At(0.5f, 1f, 0, -210, 640, 70, 0.5f, 1f);
            var d = UIKit.Txt(b.transform, text, 28, TextAnchor.UpperCenter, UIKit.TextColor);
            d.rectTransform.At(0.5f, 1f, 0, -300, 640, 180, 0.5f, 1f);
        }

        public void OpenCreate(bool practice)
        {
            DraftPractice = practice;
            if (Draft.GroupName == "Group")
                Draft.GroupName = Rtl.HasRtl(Settings.PlayerName) ? "د " + Settings.PlayerName + " ګروپ" : Settings.PlayerName + "'s group";
            createTitle.text = practice ? "PRACTICE VS BOTS" : "CREATE GROUP";
            createButton.SetLabel(practice ? "START PRACTICE" : "CREATE GROUP");
            groupNameField.text = Draft.GroupName;
            groupNameField.transform.parent.gameObject.SetActive(!practice);
            passwordRow.SetActive(!practice);
            if (practice && Draft.Bots == 0) Draft.Bots = 5;
            // a group on Wi-Fi is for people: bots only in practice
            if (!practice) Draft.Bots = 0;
            foreach (var row in botRows) row.SetActive(practice);
            World.I?.ApplyLighting(Draft.Map);
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
            // optional password: only friends who know it can join
            var passRow = UIKit.Node(panel, "PasswordRow").At(0.5f, 1f, 0, -510, 1080, 84, 0.5f, 1f);
            passwordRow = passRow.gameObject;
            var passBg = UIKit.Img(passRow, new Color(1, 1, 1, 0.04f), UIKit.Rounded);
            passBg.rectTransform.Fill();
            var passIcon = UIKit.IconImage(passRow, "lock", 44, UIKit.Muted);
            passIcon.rectTransform.At(0f, 0.5f, 52, 0, 44, 44);
            var passLabel = UIKit.Txt(passRow, "PASSWORD", 34, TextAnchor.MiddleLeft, UIKit.Muted);
            passLabel.rectTransform.Fill();
            passLabel.rectTransform.offsetMin = new Vector2(90, 0);
            passwordField = UIKit.Input(passRow, "", "Optional - empty = open", 30, 12);
            passwordField.GetComponent<RectTransform>().At(1f, 0.5f, -20, 0, 560, 74, 1f, 0.5f);
            byte[] minutes = { 3, 5, 8, 10, 15, 20, 30, 45, 60 };
            UIKit.Stepper(panel, "GAME MODE", -130, () => MatchConfig.ModeNames[(int)Draft.Mode], d => Draft.Mode = (Mode)(((int)Draft.Mode + 2 + d) % 2), 1080);
            UIKit.Stepper(panel, "MAP", -225, () => MatchConfig.MapNames[Draft.Map], d =>
            {
                Draft.Map = (byte)((Draft.Map + MatchConfig.MapNames.Length + d) % MatchConfig.MapNames.Length);
                World.I?.ApplyLighting(Draft.Map);   // the map behind the menu changes with the choice
            }, 1080);
            UIKit.Stepper(panel, "MATCH TIME", -320, () => Draft.Minutes + " MIN", d =>
            {
                int i = System.Array.IndexOf(minutes, Draft.Minutes);
                Draft.Minutes = minutes[Mathf.Clamp((i < 0 ? 1 : i) + d, 0, minutes.Length - 1)];
            }, 1080);
            UIKit.Stepper(panel, "MAX PLAYERS", -415, () => Draft.MaxPlayers.ToString(), d => Draft.MaxPlayers = (byte)Mathf.Clamp(Draft.MaxPlayers + d, 2, 10), 1080);
            botRows = new[]
            {
                UIKit.Stepper(panel, "BOTS", -510, () => Draft.Bots == 0 ? "NONE" : Draft.Bots.ToString(), d => Draft.Bots = (byte)Mathf.Clamp(Draft.Bots + d, 0, 9), 1080).transform.parent.gameObject,
                UIKit.Stepper(panel, "BOT SKILL", -605, () => MatchConfig.SkillNames[Draft.BotSkill], d => Draft.BotSkill = (byte)Mathf.Clamp(Draft.BotSkill + d, 0, 2), 1080).transform.parent.gameObject,
            };
            var back = UIKit.Btn(s, "BACK", Back, 36, null, "arrow_back");
            back.GetComponent<RectTransform>().At(0.5f, 0f, -300, 50, 380, 96, 0.5f, 0f);
            createButton = UIKit.Btn(s, "CREATE GROUP", CreateFromDraft, 38, UIKit.AccentDark, "play_arrow");
            createButton.GetComponent<RectTransform>().At(0.5f, 0f, 200, 50, 560, 96, 0.5f, 0f);
        }

        public void CreateFromDraft()
        {
            Draft.GroupName = Settings.Clean(groupNameField.text.Length > 0 ? groupNameField.text : Draft.GroupName, 24);
            var config = new MatchConfig
            {
                GroupName = Draft.GroupName, Mode = Draft.Mode, Map = Draft.Map, Minutes = Draft.Minutes,
                MaxPlayers = Draft.MaxPlayers, Bots = Draft.Bots, BotSkill = Draft.BotSkill,
                Password = DraftPractice ? "" : passwordField.text.Trim(),
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
            var connect = UIKit.Btn(ipRow, "CONNECT", ConnectToTypedIp, 32, UIKit.AccentDark, "link");
            connect.GetComponent<RectTransform>().At(1f, 0.5f, 0, 0, 300, 80, 1f, 0.5f);
            var back = UIKit.Btn(s, "BACK", Back, 36, null, "arrow_back");
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

        public void JoinGroup(IPEndPoint host, string password = "")
        {
            lastJoinHost = host;
            Session.I.Join(host, password);
            ShowScreen("lobby");
        }

        /// <summary>The small page that asks for a locked group's password.</summary>
        private void BuildJoinPassword()
        {
            var s = MakeScreen("password");
            joinPasswordTitle = UIKit.Title(s, "GROUP PASSWORD", -120, 62);
            var panel = UIKit.Panel(s, 1000, 330, 40);
            var icon = UIKit.IconImage(panel, "lock", 80, UIKit.Accent);
            icon.rectTransform.At(0.5f, 1f, 0, -80, 80, 80);
            joinPasswordField = UIKit.Input(panel, "", "Type the password", 40, 12);
            joinPasswordField.GetComponent<RectTransform>().At(0.5f, 0f, 0, 70, 760, 96, 0.5f, 0f);
            var back = UIKit.Btn(s, "BACK", Back, 36, null, "arrow_back");
            back.GetComponent<RectTransform>().At(0.5f, 0f, -300, 70, 380, 96, 0.5f, 0f);
            var join = UIKit.Btn(s, "JOIN", () =>
            {
                if (lastJoinHost == null) return;
                PlayerPrefs.SetString("lastpass", joinPasswordField.text.Trim());
                JoinGroup(lastJoinHost, joinPasswordField.text.Trim());
            }, 38, UIKit.AccentDark, "login");
            join.GetComponent<RectTransform>().At(0.5f, 0f, 200, 70, 440, 96, 0.5f, 0f);
        }

        private void OpenJoinPassword(IPEndPoint host, string groupName)
        {
            lastJoinHost = host;
            if (groupName != null) joinPasswordTitle.text = Rtl.Shape(groupName.ToUpperInvariant());
            joinPasswordField.text = PlayerPrefs.GetString("lastpass", "");
            ShowScreen("password");
        }

        private readonly List<string> shownGroups = new List<string>();

        private void RefreshJoin()
        {
            // the same host can answer on loopback and on its Wi-Fi address: show it once
            var all = Session.I.Groups.Values.ToList();
            var groups = all.Where(g => !IPAddress.IsLoopback(g.Address.Address) || !all.Any(o => o != g && o.Name == g.Name && o.Port == g.Port))
                .OrderBy(g => g.Name).ToList();
            var keys = groups.Select(g => g.Address + "|" + g.Players + "|" + g.Phase + "|" + g.Locked).ToList();
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
                var name = UIKit.Txt(row.transform, $"{Rtl.Shape(g.Name)}\n<size=24><color=#9aa4b0>{MatchConfig.ModeNames[(int)g.Mode]}  -  {MatchConfig.MapNames[g.Map % MatchConfig.MapNames.Length]}  -  {g.Address.Address}</color></size>", 34, TextAnchor.MiddleLeft);
                name.rectTransform.Fill();
                name.rectTransform.offsetMin = new Vector2(30, 0);
                var count = UIKit.Txt(row.transform, $"{g.Players}/{g.MaxPlayers}" + (g.Phase != Phase.Lobby ? "\n<size=22>IN MATCH</size>" : ""), 32, TextAnchor.MiddleCenter, UIKit.Accent);
                count.rectTransform.At(1f, 0.5f, -330, 0, 160, 90);
                var target = g.Address;
                string groupName = g.Name;
                if (g.Locked)
                {
                    var lockIcon = UIKit.IconImage(row.transform, "lock", 46, UIKit.Accent);
                    lockIcon.rectTransform.At(1f, 0.5f, -450, 0, 46, 46);
                }
                var join = g.Locked
                    ? UIKit.Btn(row.transform, "JOIN", () => OpenJoinPassword(target, groupName), 34, UIKit.AccentDark)
                    : UIKit.Btn(row.transform, "JOIN", () => JoinGroup(target), 34, UIKit.AccentDark);
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
            secondaryText = LoadoutStepper(loadout, 400, "SECOND", false);

            var leave = UIKit.Btn(s, "LEAVE", LeaveGroup, 34, null, "exit_to_app");
            leave.GetComponent<RectTransform>().At(0.5f, 0f, -560, 50, 320, 96, 0.5f, 0f);
            lobbyTeam = UIKit.Btn(s, "SWITCH TEAM", () => { var me = Session.I.Local; if (me != null) Session.I.SetTeam((byte)(1 - me.Team)); RefreshLobby(); }, 32, null, "swap_horiz");
            lobbyTeam.GetComponent<RectTransform>().At(0.5f, 0f, -160, 50, 400, 96, 0.5f, 0f);
            lobbyStart = UIKit.Btn(s, "START MATCH", () => Session.I.StartMatch(), 38, UIKit.AccentDark, "play_arrow");
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
                if (primary) p = (byte)((p + Weapons.Count + d) % Weapons.Count);
                else sec = (byte)((sec + Weapons.Count + d) % Weapons.Count);
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
            lobbyTitle.text = s.Practice ? "PRACTICE" : Rtl.Shape(s.Config.GroupName.ToUpperInvariant());
            lobbyInfo.text = $"{MatchConfig.ModeNames[(int)s.Config.Mode]}   -   {MatchConfig.MapNames[s.Config.Map % MatchConfig.MapNames.Length]}   -   {s.Config.Minutes} MIN   -   BOTS: {s.Config.Bots} ({MatchConfig.SkillNames[s.Config.BotSkill % 3]})";
            lobbyIp.text = s.Practice ? "Only you and the bots - no network used" :
                s.IsHost ? "Friends: JOIN GROUP, or type this IP:  " + (s.HostAddresses.Length > 0 ? s.HostAddresses : "(no Wi-Fi found)")
                    + (string.IsNullOrEmpty(s.Config.Password) ? "" : "   -   PASSWORD: " + s.Config.Password) : "Connected - " + s.Players.Count(p => !p.Value.IsBot) + " players in the group";
            lobbyLeftTitle.text = tdm ? "TEAM BLUE" : "PLAYERS";
            lobbyRightTitle.text = tdm ? "TEAM RED" : "";
            var ordered = s.Players.Values.OrderBy(p => p.IsBot).ThenBy(p => p.Id).ToList();
            string Line(PlayerState p) => (p.Id == s.LocalId ? "<color=#ffa020>" : "") + (p.Id == 0 && !p.IsBot ? "[HOST] " : "") + Rtl.Shape(p.Name) + (p.Id == s.LocalId ? "</color>" : "")
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
            ShowScreen("settings");
        }

        private void BuildSettings()
        {
            var s = MakeScreen("settings");
            UIKit.Title(s, "SETTINGS", -40, 66);
            var panel = UIKit.Panel(s, 1760, 820, -10);
            // left column: player and game, right column: controls
            const float W = 820f, L = -440f, R = 440f;
            void Row(float x, Text value) => ((RectTransform)value.transform.parent).anchoredPosition += new Vector2(x, 0);
            string Speed(float v) => "x" + v.ToString("0.0");
            float Step(float v, int d) => Mathf.Round((v + d * 0.1f) * 10f) / 10f;
            // the player's name is set on the profile page only (the same name in the menus and in battle)
            Row(L, UIKit.Stepper(panel, "GRAPHICS", -30, () => Settings.QualityNames[Settings.Quality], d => Settings.Quality = Mathf.Clamp(Settings.Quality + d, 0, 2), W));
            Row(L, UIKit.Stepper(panel, "RESOLUTION", -125, () => Settings.RenderScale + "%", d => Settings.RenderScale += d * 5, W));
            Row(L, UIKit.Stepper(panel, "FRAME RATE", -215, () => Settings.FrameRate + " FPS", d =>
            {
                int i = System.Array.IndexOf(Settings.FrameRates, Settings.FrameRate);
                Settings.FrameRate = Settings.FrameRates[Mathf.Clamp((i < 0 ? 1 : i) + d, 0, Settings.FrameRates.Length - 1)];
            }, W));
            Row(L, UIKit.Stepper(panel, "SOUND VOLUME", -305, () => Mathf.RoundToInt(Settings.Volume * 100) + "%", d => Settings.Volume = Mathf.Round(Settings.Volume * 10 + d) / 10f, W));
            Row(L, UIKit.Stepper(panel, "SHOW FPS", -395, () => Settings.ShowFps ? "ON" : "OFF", d => Settings.ShowFps = !Settings.ShowFps, W));
            Row(L, UIKit.Stepper(panel, "AUTO GRAPHICS", -485, () => Settings.AutoGraphics ? "ON" : "OFF", d => Settings.AutoGraphics = !Settings.AutoGraphics, W));
            Row(R, UIKit.Stepper(panel, "TURN SPEED", -30, () => Speed(Settings.Sensitivity), d => Settings.Sensitivity = Step(Settings.Sensitivity, d), W));
            Row(R, UIKit.Stepper(panel, "AIM SPEED", -130, () => Speed(Settings.AimSensitivity), d => Settings.AimSensitivity = Step(Settings.AimSensitivity, d), W));
            Row(R, UIKit.Stepper(panel, "SCOPE SPEED", -225, () => Speed(Settings.ScopeSensitivity), d => Settings.ScopeSensitivity = Step(Settings.ScopeSensitivity, d), W));
            Row(R, UIKit.Stepper(panel, "LEFT / RIGHT", -320, () => Settings.ReverseHorizontal ? "REVERSED" : "NORMAL", d => Settings.ReverseHorizontal = !Settings.ReverseHorizontal, W));
            Row(R, UIKit.Stepper(panel, "UP / DOWN", -415, () => Settings.ReverseVertical ? "REVERSED" : "NORMAL", d => Settings.ReverseVertical = !Settings.ReverseVertical, W));
            Row(R, UIKit.Stepper(panel, "AIM ASSIST", -505, () => Settings.AimAssist ? "ON" : "OFF", d => Settings.AimAssist = !Settings.AimAssist, W));
            Row(R, UIKit.Stepper(panel, "MUSIC", -595, () => !Settings.Music ? "OFF" : Mathf.RoundToInt(Settings.MusicVolume * 100) + "%", d =>
            {
                // OFF, 20 % ... 100 %
                float v = Settings.Music ? Settings.MusicVolume : 0f;
                v = Mathf.Clamp01(Mathf.Round(v * 5f + d) / 5f);
                Settings.Music = v > 0f;
                if (v > 0f) Settings.MusicVolume = v;
            }, W));
            var hint = UIKit.Txt(panel, "Running speed is the same for everyone and cannot be changed.", 24, TextAnchor.MiddleCenter, UIKit.Muted);
            hint.rectTransform.At(0.5f, 1f, R, -690, W, 40, 0.5f, 1f);
            var layout = UIKit.Btn(panel, "CONTROLS LAYOUT", OpenLayoutEditor, 30, UIKit.AccentDark, "open_with");
            layout.GetComponent<RectTransform>().At(0.5f, 1f, L, -600, 480, 76, 0.5f, 1f);
            var back = UIKit.Btn(s, "BACK", Back, 36, null, "arrow_back");
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
            var resume = UIKit.Btn(panel, "RESUME", () => SetPaused(false), 38, UIKit.AccentDark, "play_arrow");
            resume.GetComponent<RectTransform>().At(0.5f, 1f, 0, -170, 520, 96, 0.5f, 1f);
            var settings = UIKit.Btn(panel, "SETTINGS", () => { settingsReturn = "game"; OpenSettings(); }, 38, null, "settings");
            settings.GetComponent<RectTransform>().At(0.5f, 1f, 0, -290, 520, 96, 0.5f, 1f);
            var leave = UIKit.Btn(panel, "LEAVE MATCH", LeaveGroup, 38, null, "exit_to_app");
            leave.GetComponent<RectTransform>().At(0.5f, 1f, 0, -410, 520, 96, 0.5f, 1f);
            var hint = UIKit.Txt(panel, "", 22, TextAnchor.MiddleCenter, UIKit.Muted);
            hint.rectTransform.At(0.5f, 0f, 0, 40, 640, 40);
            pausePanel.gameObject.SetActive(false);
        }

        private RectTransform resultsRows;

        // results table columns: header, x of the left edge, width
        private static readonly (string head, float x, float w)[] ResultColumns =
        {
            ("#", 20, 60), ("PLAYER", 90, 400), ("KILLS", 490, 120), ("DEATHS", 610, 130), ("K/D", 740, 110),
            ("DAMAGE", 850, 150), ("HEAD", 1000, 110), ("ACCURACY", 1110, 170), ("SCORE", 1280, 140), ("RATING", 1420, 160),
        };

        private void BuildResults()
        {
            var s = MakeScreen("results");
            resultsTitle = UIKit.Title(s, "MATCH OVER", -30, 70);
            var panel = UIKit.Panel(s, 1640, 700, 10);
            var head = UIKit.Img(panel, new Color(1, 1, 1, 0.06f), UIKit.Rounded);
            head.rectTransform.At(0.5f, 1f, 0, -24, 1600, 64, 0.5f, 1f);
            foreach (var c in ResultColumns)
                Cell(head.transform, c.head, c.x, c.w, 24, UIKit.Muted);
            // the rows scroll when there are many players; the table stays until the player closes it
            var view = UIKit.Img(panel, new Color(0, 0, 0, 0.01f), null, true);
            view.rectTransform.At(0.5f, 1f, 0, -96, 1600, 580, 0.5f, 1f);
            view.gameObject.AddComponent<RectMask2D>();
            resultsRows = UIKit.Node(view.transform, "Rows");
            resultsRows.anchorMin = new Vector2(0, 1);
            resultsRows.anchorMax = new Vector2(1, 1);
            resultsRows.pivot = new Vector2(0.5f, 1);
            resultsRows.anchoredPosition = Vector2.zero;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = resultsRows;
            scroll.viewport = view.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            resultsInfo = UIKit.Txt(s, "", 26, TextAnchor.MiddleCenter, UIKit.Muted);
            resultsInfo.rectTransform.At(0.5f, 0f, 0, 158, 1600, 40);
            var leave = UIKit.Btn(s, "LEAVE GROUP", LeaveGroup, 34, null, "exit_to_app");
            leave.GetComponent<RectTransform>().At(0.5f, 0f, -520, 40, 440, 96, 0.5f, 0f);
            var close = UIKit.Btn(s, "CLOSE", () => ShowScreen(Session.I.Active ? "lobby" : "menu"), 36, null, "arrow_back");
            close.GetComponent<RectTransform>().At(0.5f, 0f, 0, 40, 400, 96, 0.5f, 0f);
            resultsLobby = UIKit.Btn(s, "BACK TO LOBBY", () => { Session.I.BackToLobby(); ShowScreen("lobby"); }, 36, UIKit.AccentDark, "groups");
            resultsLobby.GetComponent<RectTransform>().At(0.5f, 0f, 520, 40, 500, 96, 0.5f, 0f);
        }

        private static Text Cell(Transform row, string text, float x, float w, int size, Color? color = null)
        {
            var t = UIKit.Txt(row, text, size, x < 100 || x > 400 ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, color);
            t.rectTransform.anchorMin = new Vector2(0, 0);
            t.rectTransform.anchorMax = new Vector2(0, 1);
            t.rectTransform.pivot = new Vector2(0, 0.5f);
            t.rectTransform.anchoredPosition = new Vector2(x, 0);
            t.rectTransform.sizeDelta = new Vector2(w, 0);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        /// <summary>
        /// A fair match score, not kills alone: kills, the damage done (help on kills others finished),
        /// headshots and accuracy count for, deaths count against; the winning team gets a bonus.
        /// </summary>
        public static float MatchScore(PlayerState p, Session s)
        {
            float accuracy = p.Shots > 0 ? (float)p.ShotHits / p.Shots : 0f;
            float score = p.Kills * 100f + p.DamageDone * 0.5f + p.Heads * 15f - p.Deaths * 40f;
            if (p.Shots >= 15) score += accuracy * 150f;
            if (s.Config.Mode == Mode.TeamDeathmatch && s.TeamScore[p.Team & 1] > s.TeamScore[1 - (p.Team & 1)]) score += 60f;
            return Mathf.Max(0f, score);
        }

        private void FillResults()
        {
            var s = Session.I;
            string title;
            if (s.Config.Mode == Mode.TeamDeathmatch)
            {
                title = s.TeamScore[0] == s.TeamScore[1] ? "DRAW" : s.TeamScore[0] > s.TeamScore[1] ? "<color=#4d84ff>TEAM BLUE WINS</color>" : "<color=#f24d40>TEAM RED WINS</color>";
                title += $"  <size=40>{s.TeamScore[0]} : {s.TeamScore[1]}</size>";
                var me = s.Local;
                if (me != null && s.TeamScore[0] != s.TeamScore[1])
                    title += (s.TeamScore[me.Team] > s.TeamScore[1 - me.Team]) ? "  <size=40>VICTORY</size>" : "  <size=40>DEFEAT</size>";
            }
            else
            {
                var best = s.Players.Values.OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths).FirstOrDefault();
                title = best == null ? "MATCH OVER" : (best.Id == s.LocalId ? "<color=#ffa020>YOU WIN!</color>" : Rtl.Shape(best.Name.ToUpperInvariant()) + " WINS");
            }
            resultsTitle.text = title;

            // a snapshot of this match: the numbers stay while the next one starts behind it
            foreach (Transform child in resultsRows) Destroy(child.gameObject);
            var players = s.Players.Values.Select(p => (p, score: MatchScore(p, s))).OrderByDescending(x => x.score).ThenByDescending(x => x.p.Kills).ToList();
            float top = players.Count > 0 ? Mathf.Max(1f, players[0].score) : 1f;
            const float RowHeight = 66;
            for (int i = 0; i < players.Count; i++)
            {
                var (p, score) = players[i];
                bool mine = p.Id == s.LocalId;
                var row = UIKit.Img(resultsRows, mine ? new Color(1f, 0.6f, 0.1f, 0.16f) : new Color(1, 1, 1, i % 2 == 0 ? 0.035f : 0.015f), UIKit.Rounded);
                row.rectTransform.At(0.5f, 1f, 0, -i * RowHeight, 1600, RowHeight - 6, 0.5f, 1f);
                if (s.Config.Mode == Mode.TeamDeathmatch)
                {
                    var team = UIKit.Img(row.transform, p.Team == 0 ? UIKit.Blue : UIKit.Red, UIKit.Rounded);
                    team.rectTransform.At(0f, 0.5f, 4, 0, 8, RowHeight - 18, 0f, 0.5f);
                }
                float kd = p.Deaths > 0 ? (float)p.Kills / p.Deaths : p.Kills;
                int accuracy = p.Shots > 0 ? Mathf.RoundToInt(100f * p.ShotHits / p.Shots) : 0;
                int rating = Mathf.RoundToInt(100f * score / top);
                string name = Rtl.Shape(p.Name.Length > 16 ? p.Name.Substring(0, 16) : p.Name) + (p.IsBot ? "  <size=20><color=#8a94a0>BOT</color></size>" : "");
                Color text = mine ? UIKit.Accent : UIKit.TextColor;
                string[] values =
                {
                    i == 0 ? "MVP" : (i + 1).ToString(), name, p.Kills.ToString(), p.Deaths.ToString(), kd.ToString("0.0"),
                    p.DamageDone.ToString(), p.Heads.ToString(), p.Shots > 0 ? accuracy + "%" : "-", Mathf.RoundToInt(score).ToString(), rating + "%",
                };
                for (int c = 0; c < values.Length; c++)
                {
                    var col = ResultColumns[c];
                    var cell = Cell(row.transform, values[c], col.x, col.w, c == 1 ? 30 : 28, c == 0 && i == 0 ? UIKit.Accent : text);
                    if (c == values.Length - 1) cell.color = Color.Lerp(UIKit.Red, UIKit.Green, rating / 100f);
                }
            }
            resultsRows.sizeDelta = new Vector2(0, players.Count * RowHeight);
            resultsRows.anchoredPosition = Vector2.zero;
            resultsLobby.gameObject.SetActive(s.IsHost);
        }

        /// <summary>A syringe or blood was given: a message for the two players, the syringe in the giver's hand for everyone.</summary>
        private void OnHealed(HealEvent e)
        {
            var s = Session.I;
            string Name(byte id) => s.Players.TryGetValue(id, out var p) ? Rtl.Shape(p.Name) : "?";
            if (e.Target == s.LocalId && e.Giver != s.LocalId)
                Toast(e.Kind == 1 ? $"{Name(e.Giver)} GAVE YOU BLOOD  +{e.Amount}" : $"{Name(e.Giver)} HEALED YOU  +{e.Amount}");
            else if (e.Giver == s.LocalId && e.Target != s.LocalId)
                Toast(e.Kind == 1 ? $"YOU GAVE {Name(e.Target)} BLOOD  +{e.Amount}" : $"YOU HEALED {Name(e.Target)}  +{e.Amount}");
            else if (e.Giver == s.LocalId)
                Toast($"SYRINGE  +{e.Amount} HEALTH");
            if (e.Target == s.LocalId) Sfx.Play(Sfx.Has("dry_0") ? "dry_0" : "click", 0.6f, 1.4f);
            if (e.Giver != s.LocalId && World.I != null && World.I.Agents.TryGetValue(e.Giver, out var giver) && giver != null)
            {
                giver.ShowSyringe(e.Kind, 1.2f);
                giver.PlayReload();
            }
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
