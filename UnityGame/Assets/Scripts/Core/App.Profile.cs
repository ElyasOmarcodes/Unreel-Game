using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UA
{
    /// <summary>Profile: name, the character built piece by piece (Wardrobe) and the two weapons, with live 3D previews.</summary>
    public partial class App
    {
        private PreviewStage stage;
        private InputField profileName;
        private RawImage characterView, gunView;
        private Text gunTitle, gunStats;
        private int previewWeapon;

        private RectTransform characterTab, weaponTab;
        private Button characterTabButton, weaponTabButton;
        private readonly System.Collections.Generic.List<(Text text, System.Func<string> value)> lookValues = new System.Collections.Generic.List<(Text, System.Func<string>)>();

        private class RotateDrag : MonoBehaviour, IDragHandler
        {
            public PreviewStage Stage;
            public void OnDrag(PointerEventData e) { if (Stage != null) Stage.CharacterYaw -= e.delta.x * 0.4f; }
        }

        private static void ChangeLook(Wardrobe.Slot slot, int delta)
        {
            int look = Settings.Look;
            look = Wardrobe.Set(look, slot, Wardrobe.Get(look, slot) + delta);
            // the camo of a combat jacket and cargo trousers match until one is changed on its own
            if (slot == Wardrobe.Slot.Top || slot == Wardrobe.Slot.TopColor)
                if (Wardrobe.Get(look, Wardrobe.Slot.Top) == 0 && Wardrobe.Get(look, Wardrobe.Slot.Bottom) == 0)
                    look = Wardrobe.Set(look, Wardrobe.Slot.BottomColor, Wardrobe.Get(look, Wardrobe.Slot.TopColor));
            Settings.Look = look;
        }

        private static void ChangeStyle(int delta)
        {
            int n = Wardrobe.Styles.Length;
            int style = (Wardrobe.StyleOf(Settings.Look) + n + delta) % n;
            int look = Wardrobe.Apply(Settings.Look, Wardrobe.Styles[style]);
            // a woman's style picks the woman's body (and back), keeping the skin tone
            bool female = style >= 8;
            if (female != Wardrobe.Female(look)) look = Wardrobe.Set(look, Wardrobe.Slot.Body, (female ? 2 : 0) + Wardrobe.Get(look, Wardrobe.Slot.Body) % 2);
            Settings.Look = look;
        }

        private void ShowTab(bool character)
        {
            characterTab.gameObject.SetActive(character);
            weaponTab.gameObject.SetActive(!character);
            characterTabButton.GetComponent<Image>().color = character ? UIKit.AccentDark : UIKit.ButtonColor;
            weaponTabButton.GetComponent<Image>().color = character ? UIKit.ButtonColor : UIKit.AccentDark;
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
            var nameRow = UIKit.Node(right, "NameRow").At(0.5f, 1f, 0, -24, 1020, 76, 0.5f, 1f);
            var nameLabel = UIKit.Txt(nameRow, "NAME", 32, TextAnchor.MiddleLeft, UIKit.Muted);
            nameLabel.rectTransform.Fill();
            nameLabel.rectTransform.offsetMin = new Vector2(30, 0);
            profileName = UIKit.Input(nameRow, Settings.PlayerName, "Your name", 32);
            profileName.GetComponent<RectTransform>().At(1f, 0.5f, -20, 0, 560, 70, 1f, 0.5f);
            profileName.onEndEdit.AddListener(v => Settings.PlayerName = v);

            characterTabButton = UIKit.Btn(right, "CHARACTER", () => ShowTab(true), 30, null, "person");
            characterTabButton.GetComponent<RectTransform>().At(0.5f, 1f, -255, -108, 500, 62, 0.5f, 1f);
            weaponTabButton = UIKit.Btn(right, "WEAPONS", () => { ShowTab(false); RefreshProfile(); }, 30, null, "gps_fixed");
            weaponTabButton.GetComponent<RectTransform>().At(0.5f, 1f, 255, -108, 500, 62, 0.5f, 1f);

            // character: every piece chosen on its own, in two columns
            characterTab = UIKit.Node(right, "CharacterTab");
            characterTab.Fill();
            lookValues.Clear();
            void Look(Wardrobe.Slot slot, int column, int row)
            {
                System.Func<string> value = () => Wardrobe.Label(Settings.Look, slot);
                var t = UIKit.MiniStepper(characterTab, Wardrobe.SlotNames[(int)slot], column == 0 ? -257 : 257, -184 - row * 88, 506, value, d => { ChangeLook(slot, d); RefreshProfile(); });
                lookValues.Add((t, value));
            }
            System.Func<string> styleValue = () => Wardrobe.Styles[Wardrobe.StyleOf(Settings.Look)].Name;
            var styleText = UIKit.MiniStepper(characterTab, "STYLE", 257, -184, 506, styleValue, d => { ChangeStyle(d); RefreshProfile(); });
            lookValues.Add((styleText, styleValue));
            Look(Wardrobe.Slot.Body, 0, 0);
            Look(Wardrobe.Slot.Top, 0, 1); Look(Wardrobe.Slot.TopColor, 1, 1);
            Look(Wardrobe.Slot.Bottom, 0, 2); Look(Wardrobe.Slot.BottomColor, 1, 2);
            Look(Wardrobe.Slot.Head, 0, 3); Look(Wardrobe.Slot.HeadColor, 1, 3);
            Look(Wardrobe.Slot.Vest, 0, 4); Look(Wardrobe.Slot.Bag, 1, 4);
            Look(Wardrobe.Slot.Feet, 0, 5); Look(Wardrobe.Slot.Gloves, 1, 5);
            Look(Wardrobe.Slot.Kit, 0, 6); Look(Wardrobe.Slot.Beard, 1, 6);

            // weapons: both slots take any weapon, with the real data of the one shown
            weaponTab = UIKit.Node(right, "WeaponTab");
            weaponTab.Fill();
            UIKit.Stepper(weaponTab, "WEAPON 1", -190, () => Weapons.Get(Settings.Primary).Name, d =>
            {
                Settings.Primary = (byte)((Settings.Primary + Weapons.Count + d) % Weapons.Count);
                previewWeapon = Settings.Primary;
                RefreshProfile();
            }, 980);
            UIKit.Stepper(weaponTab, "WEAPON 2", -284, () => Weapons.Get(Settings.Secondary).Name, d =>
            {
                Settings.Secondary = (byte)((Settings.Secondary + Weapons.Count + d) % Weapons.Count);
                previewWeapon = Settings.Secondary;
                RefreshProfile();
            }, 980);
            var gunBox = UIKit.Img(weaponTab, new Color(0, 0, 0, 0.35f), UIKit.Rounded);
            gunBox.rectTransform.At(0.5f, 0f, -190, 30, 580, 380, 0.5f, 0f);
            gunView = UIKit.Node(gunBox.transform, "Gun").gameObject.AddComponent<RawImage>();
            gunView.rectTransform.Fill(6);
            gunTitle = UIKit.Txt(weaponTab, "", 34, TextAnchor.UpperLeft, UIKit.Accent);
            gunTitle.rectTransform.At(1f, 0f, -16, 350, 370, 50, 1f, 0f);
            gunStats = UIKit.Txt(weaponTab, "", 23, TextAnchor.UpperLeft, UIKit.TextColor);
            gunStats.rectTransform.At(1f, 0f, -16, 30, 370, 310, 1f, 0f);
            ShowTab(true);

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
            foreach (var (text, value) in lookValues) text.text = value();
            stage.ShowGun(previewWeapon);
            var w = Weapons.Get(previewWeapon);
            int bodyShots = Mathf.CeilToInt(100f / (w.Damage * w.Pellets));
            int headShots = Mathf.CeilToInt(100f / (w.Damage * w.Pellets * w.HeadMultiplier));
            gunTitle.text = w.Name;
            gunStats.text = $"{w.Caliber}\n{w.MuzzleVelocity} m/s muzzle velocity\n{w.Action}\n{1000f / w.FireRate:0} ms between shots\nRange {w.EffectiveRange} m  ·  Mag {w.MagSize}\nScope {(w.Scope ? $"{60f / w.AimFov:0.#}x" : "none")}\nDamage {w.Damage * w.Pellets:0}\nKill: {bodyShots} body / {headShots} head";
            var me = Session.I.Local;
            if (me != null && Session.I.Phase == Phase.Lobby) { me.Look = Settings.Look; Session.I.SetLoadout(Settings.Primary, Settings.Secondary); }
        }
    }
}
