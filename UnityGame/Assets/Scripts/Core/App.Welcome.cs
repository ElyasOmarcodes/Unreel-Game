using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>
    /// The first start: a page before the menu that asks for a name (SAVE, or SKIP to keep the drawn
    /// "Agent" number) and lets the player pick one of the ready-made characters, nothing more. And the
    /// page that covers everything while this version is switched off (KillSwitch).
    /// </summary>
    public partial class App
    {
        private InputField welcomeName;
        private RawImage welcomeView;
        private Text welcomePreset;
        private RectTransform blockedCover;

        private void BuildWelcome()
        {
            var s = MakeScreen("welcome");
            UIKit.Title(s, "WELCOME, AGENT", -40, 66);
            // the character: a turning 3D picture and the arrows through the ready-made ones
            var left = UIKit.Panel(s, 620, 760, -10);
            left.anchoredPosition = new Vector2(-480, -10);
            welcomeView = UIKit.Node(left, "Character").gameObject.AddComponent<RawImage>();
            welcomeView.rectTransform.Fill(12);
            welcomeView.rectTransform.offsetMax = new Vector2(-12, -100);
            welcomeView.gameObject.AddComponent<RotateDrag>();
            System.Func<string> presetValue = () => Wardrobe.Presets[Settings.Preset].Name;
            welcomePreset = UIKit.MiniStepper(left, "CHARACTER", 0, -14, 560, presetValue, d =>
            {
                Settings.Preset += d;
                welcomePreset.text = presetValue();
                if (stage != null) stage.ShowCharacter(Settings.Look, Settings.Primary);
            });
            var hint = UIKit.Txt(left, "drag to turn", 22, TextAnchor.LowerCenter, UIKit.Muted);
            hint.rectTransform.At(0.5f, 0f, 0, 20, 400, 30, 0.5f, 0f);

            // the name
            var right = UIKit.Panel(s, 980, 760, -10);
            right.anchoredPosition = new Vector2(360, -10);
            var ask = UIKit.Txt(right, "What should the others call you?", 36, TextAnchor.UpperCenter, UIKit.TextColor);
            ask.rectTransform.At(0.5f, 1f, 0, -60, 900, 60, 0.5f, 1f);
            welcomeName = UIKit.Input(right, "", "Your name", 40, 14);
            welcomeName.GetComponent<RectTransform>().At(0.5f, 1f, 0, -150, 760, 96, 0.5f, 1f);
            var note = UIKit.Txt(right, "", 28, TextAnchor.UpperCenter, UIKit.Muted);
            note.rectTransform.At(0.5f, 1f, 0, -270, 900, 90, 0.5f, 1f);
            note.text = "SKIP keeps the name  <color=#ffa020>" + Settings.AutoName + "</color>\nYou can change the name and the character later in PROFILE.";
            var save = UIKit.Btn(right, "SAVE", () => FinishWelcome(true), 40, UIKit.AccentDark, "play_arrow");
            save.GetComponent<RectTransform>().At(0.5f, 1f, 170, -400, 400, 100, 0.5f, 1f);
            var skip = UIKit.Btn(right, "SKIP", () => FinishWelcome(false), 36, null, "login");
            skip.GetComponent<RectTransform>().At(0.5f, 1f, -250, -400, 340, 100, 0.5f, 1f);
            // the credits of the free models and sounds the game uses (their licences ask for them)
            var credits = UIKit.Txt(right, "3D characters, animations and city by Quaternius (CC0)  -  military gear: \"Military Character Kit 1.1\" by britdawgmasterfunk, CC-BY-4.0\n"
                + "soldiers: \"Soldier Character\" by Muhamad Mirza Arrafi, \"soldier in Urban camouflage from the 90s\" by Marko1937, CC-BY-4.0  -  \"S.W.A.T. Operator - Remastered\" by Mateusz Wolinski\n"
                + "music \"Ready to Fight\" by David Fesliyan and sounds: FesliyanStudios.com  -  death cries: Michel Baradari, CC-BY 3.0  -  textures: Poly Haven, CC0", 19, TextAnchor.LowerCenter, new Color(1, 1, 1, 0.4f));
            credits.rectTransform.At(0.5f, 0f, 0, 24, 940, 150, 0.5f, 0f);
        }

        /// <summary>The first page of the first start (afterwards the menu comes straight away).</summary>
        public void OpenWelcome()
        {
            if (stage == null) stage = PreviewStage.Create();
            welcomeView.texture = stage.CharacterTexture;
            welcomeView.GetComponent<RotateDrag>().Stage = stage;
            welcomeName.text = "";
            welcomePreset.text = Wardrobe.Presets[Settings.Preset].Name;
            ShowScreen("welcome");
            stage.SetActive(true);
            stage.ShowCharacter(Settings.Look, Settings.Primary);
        }

        public void FinishWelcome(bool save)
        {
            if (save && !string.IsNullOrWhiteSpace(welcomeName.text)) Settings.PlayerName = welcomeName.text;
            Settings.Welcomed = true;
            ShowScreen("menu");
        }

        /// <summary>The cover over everything while this version is switched off.</summary>
        private void BuildBlocked()
        {
            var canvas = UIKit.CreateCanvas("Blocked", 1000);
            blockedCover = (RectTransform)canvas.transform;
            var bg = UIKit.Img(blockedCover, new Color(0.02f, 0.025f, 0.035f, 0.97f));
            bg.rectTransform.Fill();
            if (Logo() != null) Badge(blockedCover).At(0.5f, 0.5f, 0, 230, 260, 260);
            var title = UIKit.Txt(blockedCover, "THIS VERSION IS NOT ACTIVE", 56, TextAnchor.MiddleCenter, UIKit.Accent).Shadowed();
            title.rectTransform.At(0.5f, 0.5f, 0, 40, 1600, 80);
            var text = UIKit.Txt(blockedCover, Rtl.Shape("د اجنټ وارفېر دا نسخه اوس فعاله نه ده.") + "\n"
                + "This version of Agent Warfare is switched off for now. Please update the game from Google Play,\nor check again later.", 32, TextAnchor.MiddleCenter, UIKit.TextColor);
            text.rectTransform.At(0.5f, 0.5f, 0, -80, 1600, 160);
            var again = UIKit.Btn(blockedCover, "CHECK AGAIN", () => { if (KillSwitch.I != null) KillSwitch.I.CheckNow(); Toast("Checking ..."); }, 34, UIKit.AccentDark, "refresh");
            again.GetComponent<RectTransform>().At(0.5f, 0.5f, -420, -250, 380, 96);
            var store = UIKit.Btn(blockedCover, "GOOGLE PLAY", () =>
            {
                Application.OpenURL("https://play.google.com/store/apps/details?id=" + Application.identifier);
            }, 34, null, "link");
            store.GetComponent<RectTransform>().At(0.5f, 0.5f, 0, -250, 380, 96);
            var quit = UIKit.Btn(blockedCover, "QUIT", () => Application.Quit(), 34, null, "exit_to_app");
            quit.GetComponent<RectTransform>().At(0.5f, 0.5f, 420, -250, 380, 96);
            blockedCover.gameObject.SetActive(false);
        }

        private bool wasBlocked;

        /// <summary>Every frame: while switched off the cover is up, the match is left and the game stands still.</summary>
        private void UpdateBlocked()
        {
            bool blocked = KillSwitch.Blocked;
            if (blocked == wasBlocked) return;
            wasBlocked = blocked;
            blockedCover.gameObject.SetActive(blocked);
            if (blocked)
            {
                if (Session.I.Active) Session.I.Leave();
                Time.timeScale = 0f;
                Controls.Enabled = false;
                ShowScreen("menu");
            }
            else
            {
                Time.timeScale = 1f;
                ShowScreen(Settings.Welcomed ? "menu" : "welcome");
                if (!Settings.Welcomed) OpenWelcome();
            }
        }
    }
}
