using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UA
{
    /// <summary>Profile: name, character (body, head gear, camo) and the two weapons, with live 3D previews.</summary>
    public partial class App
    {
        private PreviewStage stage;
        private InputField profileName;
        private RawImage characterView, gunView;
        private Text gunTitle, gunStats;
        private int previewWeapon;

        private static readonly string[] BodyNames = { "MALE - LIGHT", "MALE - DARK", "FEMALE - LIGHT", "FEMALE - DARK" };
        private static readonly string[] HeadNames = { "COMBAT HELMET", "CAP", "BERET", "BALACLAVA" };
        private static readonly string[] CamoNames = { "URBAN", "DESERT", "WOODLAND", "BLACK OPS" };

        private class RotateDrag : MonoBehaviour, IDragHandler
        {
            public PreviewStage Stage;
            public void OnDrag(PointerEventData e) { if (Stage != null) Stage.CharacterYaw -= e.delta.x * 0.4f; }
        }

        // part 0: body and skin (0..3), 1: head gear, 2: camo
        private static int LookPart(int look, int part) => part == 0 ? ((look / 16) % 2) * 2 + (look / 32) % 2 : part == 1 ? look % 4 : (look / 4) % 4;

        private static void SetLookPart(int part, int delta)
        {
            int look = Settings.Look, person = LookPart(look, 0), camo = (look / 4) % 4, head = look % 4;
            if (part == 0) person = (person + 4 + delta) % 4;
            else if (part == 1) head = (head + 4 + delta) % 4;
            else camo = (camo + 4 + delta) % 4;
            Settings.Look = (byte)((person % 2) * 32 + (person / 2) * 16 + camo * 4 + head);
        }

        private void BuildProfile()
        {
            var s = MakeScreen("profile");
            UIKit.Title(s, "PROFILE", -30, 64);
            var left = UIKit.Panel(s, 620, 820, -20);
            left.anchoredPosition = new Vector2(-560, -20);
            characterView = UIKit.Node(left, "Character").gameObject.AddComponent<RawImage>();
            characterView.rectTransform.Fill(12);
            characterView.gameObject.AddComponent<RotateDrag>();
            var dragHint = UIKit.Txt(left, "drag to turn", 22, TextAnchor.LowerCenter, UIKit.Muted);
            dragHint.rectTransform.At(0.5f, 0f, 0, 20, 400, 30, 0.5f, 0f);

            var right = UIKit.Panel(s, 1060, 820, -20);
            right.anchoredPosition = new Vector2(330, -20);
            var nameRow = UIKit.Node(right, "NameRow").At(0.5f, 1f, 0, -30, 980, 80, 0.5f, 1f);
            var nameLabel = UIKit.Txt(nameRow, "NAME", 32, TextAnchor.MiddleLeft, UIKit.Muted);
            nameLabel.rectTransform.Fill();
            nameLabel.rectTransform.offsetMin = new Vector2(30, 0);
            profileName = UIKit.Input(nameRow, Settings.PlayerName, "Your name", 32);
            profileName.GetComponent<RectTransform>().At(1f, 0.5f, -20, 0, 560, 70, 1f, 0.5f);
            profileName.onEndEdit.AddListener(v => Settings.PlayerName = v);
            UIKit.Stepper(right, "AGENT", -120, () => BodyNames[LookPart(Settings.Look, 0)], d => { SetLookPart(0, d); RefreshProfile(); }, 980);
            UIKit.Stepper(right, "HEAD", -210, () => HeadNames[LookPart(Settings.Look, 1)], d => { SetLookPart(1, d); RefreshProfile(); }, 980);
            UIKit.Stepper(right, "CAMO", -300, () => CamoNames[LookPart(Settings.Look, 2)], d => { SetLookPart(2, d); RefreshProfile(); }, 980);
            UIKit.Stepper(right, "WEAPON 1", -390, () => Weapons.Get(Settings.Primary).Name, d =>
            {
                Settings.Primary = (byte)((Settings.Primary + Weapons.Count + d) % Weapons.Count);
                previewWeapon = Settings.Primary;
                RefreshProfile();
            }, 980);
            UIKit.Stepper(right, "WEAPON 2", -480, () => Weapons.Get(Settings.Secondary).Name, d =>
            {
                Settings.Secondary = (byte)((Settings.Secondary + Weapons.Count + d) % Weapons.Count);
                previewWeapon = Settings.Secondary;
                RefreshProfile();
            }, 980);
            var gunBox = UIKit.Img(right, new Color(0, 0, 0, 0.35f), UIKit.Rounded);
            gunBox.rectTransform.At(0.5f, 0f, -190, 20, 580, 220, 0.5f, 0f);
            gunView = UIKit.Node(gunBox.transform, "Gun").gameObject.AddComponent<RawImage>();
            gunView.rectTransform.Fill(6);
            gunTitle = UIKit.Txt(right, "", 34, TextAnchor.UpperLeft, UIKit.Accent);
            gunTitle.rectTransform.At(1f, 0f, -16, 190, 370, 50, 1f, 0f);
            gunStats = UIKit.Txt(right, "", 21, TextAnchor.UpperLeft, UIKit.TextColor);
            gunStats.rectTransform.At(1f, 0f, -16, 20, 370, 165, 1f, 0f);
            var back = UIKit.Btn(s, "SAVE", () => { Settings.PlayerName = profileName.text; ShowScreen("menu"); }, 36, UIKit.AccentDark, "play_arrow");
            back.GetComponent<RectTransform>().At(0.5f, 0f, 0, 30, 380, 90, 0.5f, 0f);
        }

        public void OpenProfile()
        {
            if (stage == null) stage = PreviewStage.Create();
            characterView.texture = stage.CharacterTexture;
            gunView.texture = stage.GunTexture;
            profileName.text = Settings.PlayerName;
            previewWeapon = Settings.Primary;
            ShowScreen("profile");
            stage.SetActive(true);
            RefreshProfile();
        }

        private void RefreshProfile()
        {
            if (stage == null) return;
            stage.ShowCharacter(Settings.Look, previewWeapon);
            stage.ShowGun(previewWeapon);
            var w = Weapons.Get(previewWeapon);
            int bodyShots = Mathf.CeilToInt(100f / (w.Damage * w.Pellets));
            int headShots = Mathf.CeilToInt(100f / (w.Damage * w.Pellets * w.HeadMultiplier));
            gunTitle.text = w.Name;
            gunStats.text = $"{w.Caliber}  ·  {w.MuzzleVelocity} m/s\n{w.Action}  ·  {1000f / w.FireRate:0} ms\nRange {w.EffectiveRange} m  ·  Mag {w.MagSize}\nScope {(w.Scope ? $"{60f / w.AimFov:0.#}x" : "none")}  ·  Dmg {w.Damage * w.Pellets:0}\nKill: {bodyShots} body / {headShots} head";
            var me = Session.I.Local;
            if (me != null && Session.I.Phase == Phase.Lobby) { me.Look = Settings.Look; Session.I.SetLoadout(Settings.Primary, Settings.Secondary); }
        }
    }
}
