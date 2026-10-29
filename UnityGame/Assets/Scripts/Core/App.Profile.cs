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
        private readonly System.Collections.Generic.List<(Button button, Text tag)> weaponRows = new System.Collections.Generic.List<(Button, Text)>();
        private readonly System.Collections.Generic.List<(RectTransform fill, Text value)> weaponBars = new System.Collections.Generic.List<(RectTransform, Text)>();
        private const float BarWidth = 330f;

        /// <summary>The weapon's bars, 0 .. 1 against the strongest of all the weapons, and the number beside each.</summary>
        private static readonly (string name, System.Func<WeaponDef, float> value, System.Func<WeaponDef, string> text)[] WeaponBars =
        {
            ("POWER", w => w.Damage * w.Pellets / 117f, w => (w.Damage * w.Pellets).ToString("0") + (w.Pellets > 1 ? $" ({w.Pellets}×{w.Damage:0})" : "")),
            ("FIRE RATE", w => w.FireRate / 15f, w => Mathf.RoundToInt(w.FireRate * 60f) + " RPM"),
            ("RANGE", w => w.EffectiveRange / 1000f, w => w.EffectiveRange + " m"),
            ("ACCURACY", w => 1f - Mathf.Clamp01(w.AimSpread / 4.5f * 0.75f + w.Spread / 7f * 0.25f), w => w.AimSpread <= 0.05f ? "PIN-POINT" : w.AimSpread.ToString("0.0") + "° aimed"),
            ("CONTROL", w => 1f - Mathf.Clamp01(w.Recoil / 5.5f), w => w.Recoil < 1.2f ? "LOW RECOIL" : w.Recoil < 3f ? "MEDIUM RECOIL" : "HEAVY RECOIL"),
            ("MOBILITY", w => Mathf.Clamp01((w.MoveSpeed - 0.8f) / 0.26f), w => Mathf.RoundToInt(w.MoveSpeed * 100f) + "% speed"),
            ("MAGAZINE", w => Mathf.Sqrt(w.MagSize / 100f), w => w.MagSize + " / " + w.Reserve),
        };

        private RectTransform characterTab, weaponTab;
        private Button characterTabButton, weaponTabButton;
        private readonly System.Collections.Generic.List<(Text text, System.Func<string> value)> lookValues = new System.Collections.Generic.List<(Text, System.Func<string>)>();

        private class RotateDrag : MonoBehaviour, IDragHandler
        {
            public PreviewStage Stage;
            public bool Gun;
            public void OnDrag(PointerEventData e)
            {
                if (Stage == null) return;
                if (Gun) Stage.GunYaw -= e.delta.x * 0.4f;
                else Stage.CharacterYaw -= e.delta.x * 0.4f;
            }
        }

        private static void ChangeLook(Wardrobe.Slot slot, int delta)
        {
            // a piece changed: built piece by piece again (not a ready-made character of the pack)
            int look = Wardrobe.Set(Settings.Look, Wardrobe.Slot.Character, 0);
            look = Wardrobe.Set(look, slot, Wardrobe.Get(look, slot) + delta);
            while (!Wardrobe.Offered(slot, Wardrobe.Get(look, slot))) look = Wardrobe.Set(look, slot, Wardrobe.Get(look, slot) + (delta < 0 ? -1 : 1));
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
            Settings.Look = Wardrobe.Apply(Wardrobe.Set(Settings.Look, Wardrobe.Slot.Character, 0), Wardrobe.Styles[style]);
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
            lookValues.Clear();
            var left = UIKit.Panel(s, 620, 820, -20);
            left.anchoredPosition = new Vector2(-560, -20);
            characterView = UIKit.Node(left, "Character").gameObject.AddComponent<RawImage>();
            characterView.rectTransform.Fill(12);
            characterView.rectTransform.offsetMax = new Vector2(-12, -100);
            characterView.gameObject.AddComponent<RotateDrag>();
            var dragHint = UIKit.Txt(left, "drag to turn", 22, TextAnchor.LowerCenter, UIKit.Muted);
            dragHint.rectTransform.At(0.5f, 0f, 0, 20, 400, 30, 0.5f, 0f);
            // ready-made characters: arrows go through them, every one keeps its own changes
            System.Func<string> presetValue = () => Wardrobe.Presets[Settings.Preset].Name;
            var presetText = UIKit.MiniStepper(left, "CHARACTER", 0, -14, 560, presetValue, d => { Settings.Preset += d; RefreshProfile(); });
            lookValues.Add((presetText, presetValue));
            var reset = UIKit.Btn(left, "RESET", () => { Settings.ResetLook(); RefreshProfile(); }, 24, null, "refresh");
            reset.GetComponent<RectTransform>().At(1f, 0f, -20, 60, 170, 54, 1f, 0f);

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

            // weapons: every weapon in a list; the one picked turns in 3D with its real data and its bars
            // against the others, and goes into either slot (any weapon in either)
            weaponTab = UIKit.Node(right, "WeaponTab");
            weaponTab.Fill();
            weaponRows.Clear();
            weaponBars.Clear();
            for (int i = 0; i < Weapons.Count; i++)
            {
                int id = i;
                var row = UIKit.Btn(weaponTab, Weapons.Get(id).Name, () => { previewWeapon = id; RefreshProfile(); }, 24);
                row.GetComponent<RectTransform>().At(0f, 1f, 20, -186 - i * 52, 310, 46, 0f, 1f);
                var label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(16, 0);
                var tag = UIKit.Txt(row.transform, "", 20, TextAnchor.MiddleRight, UIKit.Accent);
                tag.rectTransform.Fill();
                tag.rectTransform.offsetMax = new Vector2(-12, 0);
                weaponRows.Add((row, tag));
            }
            var gunBox = UIKit.Img(weaponTab, new Color(0, 0, 0, 0.35f), UIKit.Rounded, true);
            gunBox.rectTransform.At(0f, 1f, 350, -186, 690, 290, 0f, 1f);
            gunView = UIKit.Node(gunBox.transform, "Gun").gameObject.AddComponent<RawImage>();
            gunView.rectTransform.Fill(6);
            gunView.gameObject.AddComponent<RotateDrag>().Gun = true;
            var gunHint = UIKit.Txt(gunBox.transform, "drag to turn", 20, TextAnchor.LowerRight, UIKit.Muted);
            gunHint.rectTransform.Fill(10);
            gunTitle = UIKit.Txt(weaponTab, "", 34, TextAnchor.MiddleLeft, UIKit.Accent);
            gunTitle.rectTransform.At(0f, 1f, 356, -482, 680, 46, 0f, 1f);
            gunStats = UIKit.Txt(weaponTab, "", 18, TextAnchor.UpperLeft, UIKit.Muted);
            gunStats.rectTransform.At(0f, 1f, 356, -526, 680, 30, 0f, 1f);
            for (int i = 0; i < WeaponBars.Length; i++)
            {
                float y = -556 - i * 27;
                var name = UIKit.Txt(weaponTab, WeaponBars[i].name, 21, TextAnchor.MiddleLeft, UIKit.TextColor);
                name.rectTransform.At(0f, 1f, 356, y, 160, 26, 0f, 1f);
                var track = UIKit.Img(weaponTab, new Color(1, 1, 1, 0.08f), UIKit.Rounded);
                track.rectTransform.At(0f, 1f, 520, y - 6, BarWidth, 14, 0f, 1f);
                var fill = UIKit.Img(track.transform, UIKit.Accent, UIKit.Rounded);
                fill.rectTransform.At(0f, 0.5f, 0, 0, BarWidth, 14, 0f, 0.5f);
                var value = UIKit.Txt(weaponTab, "", 20, TextAnchor.MiddleLeft, UIKit.Muted);
                value.rectTransform.At(0f, 1f, 864, y, 180, 26, 0f, 1f);
                weaponBars.Add((fill.rectTransform, value));
            }
            var asFirst = UIKit.Btn(weaponTab, "AS WEAPON 1", () => { Settings.Primary = (byte)previewWeapon; RefreshProfile(); }, 26, null, "looks_one");
            asFirst.GetComponent<RectTransform>().At(0f, 0f, 356, 10, 330, 52, 0f, 0f);
            var asSecond = UIKit.Btn(weaponTab, "AS WEAPON 2", () => { Settings.Secondary = (byte)previewWeapon; RefreshProfile(); }, 26, null, "looks_two");
            asSecond.GetComponent<RectTransform>().At(0f, 0f, 704, 10, 330, 52, 0f, 0f);
            ShowTab(true);

            var back = UIKit.Btn(s, "SAVE", () => { Settings.PlayerName = profileName.text; ShowScreen("menu"); }, 36, UIKit.AccentDark, "play_arrow");
            back.GetComponent<RectTransform>().At(0.5f, 0f, 0, 30, 380, 90, 0.5f, 0f);
        }

        public void OpenProfile()
        {
            if (stage == null) stage = PreviewStage.Create();
            characterView.texture = stage.CharacterTexture;
            characterView.GetComponent<RotateDrag>().Stage = stage;   // dragging turns the character
            gunView.GetComponent<RotateDrag>().Stage = stage;         // and the gun
            gunView.texture = stage.GunTexture;
            profileName.text = Settings.PlayerName;
            previewWeapon = Settings.Primary;
            ShowScreen("profile");
            stage.SetActive(true);
            RefreshProfile();
        }

        /// <summary>The weapons page with one weapon shown (the auto test).</summary>
        public void ShowWeapons(int weapon)
        {
            ShowTab(false);
            previewWeapon = Mathf.Clamp(weapon, 0, Weapons.Count - 1);
            RefreshProfile();
        }

        private void RefreshProfile()
        {
            if (stage == null) return;
            stage.ShowCharacter(Settings.Look, previewWeapon);
            foreach (var (text, value) in lookValues) text.text = Loc.T(value());
            stage.ShowGun(previewWeapon);
            var w = Weapons.Get(previewWeapon);
            int bodyShots = Mathf.CeilToInt(100f / (w.Damage * w.Pellets));
            int headShots = Mathf.CeilToInt(100f / (w.Damage * w.Pellets * w.HeadMultiplier));
            float scope = w.Scope ? Mathf.Tan(31f * Mathf.Deg2Rad) * Weapons.ScopeScale / Mathf.Tan(w.AimFov * 0.5f * Mathf.Deg2Rad) : 0f;
            gunTitle.text = w.Name + $"  <size=22><color=#9aa4b0>{Loc.T(w.Primary ? "RIFLE CLASS" : "SIDEARM CLASS")}</color></size>";
            gunStats.text = $"{w.Caliber} · {w.MuzzleVelocity} m/s · {w.Action}{(w.Scope ? $" · scope {scope:0}x" : "")} · kills in {bodyShots} (head {headShots})";
            for (int i = 0; i < weaponBars.Count; i++)
            {
                float v = Mathf.Clamp01(WeaponBars[i].value(w));
                weaponBars[i].fill.sizeDelta = new Vector2(Mathf.Max(6f, BarWidth * v), weaponBars[i].fill.sizeDelta.y);
                weaponBars[i].fill.GetComponent<Image>().color = Color.Lerp(UIKit.Red, UIKit.Green, v);
                weaponBars[i].value.text = Loc.T(WeaponBars[i].text(w));
            }
            for (int i = 0; i < weaponRows.Count; i++)
            {
                weaponRows[i].button.GetComponent<Image>().color = i == previewWeapon ? UIKit.AccentDark : UIKit.ButtonColor;
                weaponRows[i].tag.text = (i == Settings.Primary ? "W1 " : "") + (i == Settings.Secondary ? "W2" : "");
            }
            var me = Session.I.Local;
            if (me != null && Session.I.Phase == Phase.Lobby) { me.Look = Settings.Look; Session.I.SetLoadout(Settings.Primary, Settings.Secondary); }
        }
    }
}
