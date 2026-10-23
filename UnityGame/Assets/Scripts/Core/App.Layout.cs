using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UA
{
    /// <summary>Settings > Controls layout: drag the touch buttons, change their size, hide them.</summary>
    public partial class App
    {
        private class LayoutItem
        {
            public Controls.Button Button;
            public RectTransform Rect;
            public Image Image;
            public float Scale = 1f;
            public bool Visible = true;
        }

        private readonly List<LayoutItem> layoutItems = new List<LayoutItem>();
        private LayoutItem layoutSelected;
        private Text layoutInfo;

        /// <summary>Drag handler on one layout button.</summary>
        private class LayoutDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public System.Action Select;
            public RectTransform Rect;
            public void OnPointerDown(PointerEventData e) => Select?.Invoke();
            public void OnDrag(PointerEventData e)
            {
                float scale = 1080f / Mathf.Max(1, Screen.height);
                Rect.anchoredPosition += e.delta * scale;
            }
        }

        private static readonly Dictionary<Controls.Button, (string icon, string label)> ButtonLooks = new Dictionary<Controls.Button, (string, string)>
        {
            { Controls.Button.Fire, ("gps_fixed", "FIRE") }, { Controls.Button.FireLeft, ("gps_fixed", "FIRE 2") },
            { Controls.Button.Aim, ("center_focus_strong", "AIM") }, { Controls.Button.Jump, ("expand_less", "JUMP") },
            { Controls.Button.Crouch, ("keyboard_double_arrow_down", "CROUCH") }, { Controls.Button.Prone, ("airline_seat_flat", "PRONE") },
            { Controls.Button.Reload, ("autorenew", "RELOAD") }, { Controls.Button.Swap, ("swap_horiz", "SWAP") },
            { Controls.Button.Pause, ("pause", "PAUSE") }, { Controls.Button.Score, ("leaderboard", "SCORE") },
            { Controls.Button.Grenade, ("grenade", "GRENADE") }, { Controls.Button.Ammo, ("ammo", "AMMO") },
            { Controls.Button.Heal, ("bloodtype", "BLOOD") },
        };

        private void BuildLayoutEditor()
        {
            var s = MakeScreen("layout");
            var hint = UIKit.Txt(s, "Drag a button to move it.  Select it and use  -  /  +  to resize,  HIDE to remove it.", 30, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
            hint.rectTransform.At(0.5f, 1f, 0, -40, 1700, 50, 0.5f, 1f);
            // fake stick area
            var stick = UIKit.Img(s, new Color(1, 1, 1, 0.08f), UIKit.Circle);
            stick.rectTransform.At(0f, 0f, 260, 250, Controls.StickRadius * 2, Controls.StickRadius * 2);
            foreach (var pair in ButtonLooks)
            {
                var item = new LayoutItem { Button = pair.Key };
                var img = UIKit.Img(s, new Color(1, 1, 1, 0.25f), UIKit.Circle, true);
                var ring = UIKit.Img(img.transform, new Color(1, 1, 1, 0.7f), UIKit.Ring);
                ring.rectTransform.Fill();
                var ic = UIKit.IconImage(img.transform, pair.Value.icon, 40);
                ic.rectTransform.anchorMin = new Vector2(0.24f, 0.3f);
                ic.rectTransform.anchorMax = new Vector2(0.76f, 0.78f);
                ic.rectTransform.offsetMin = ic.rectTransform.offsetMax = Vector2.zero;
                var t = UIKit.Txt(img.transform, pair.Value.label, 18).Shadowed();
                t.rectTransform.anchorMin = new Vector2(0.05f, 0.08f);
                t.rectTransform.anchorMax = new Vector2(0.95f, 0.32f);
                t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
                item.Rect = img.rectTransform;
                item.Image = img;
                var drag = img.gameObject.AddComponent<LayoutDrag>();
                drag.Rect = item.Rect;
                drag.Select = () => SelectLayoutItem(item);
                layoutItems.Add(item);
            }
            var bar = UIKit.Img(s, new Color(0, 0, 0, 0.75f), UIKit.Rounded, true);
            bar.rectTransform.At(0.5f, 0.5f, 0, 0, 1180, 130);
            layoutInfo = UIKit.Txt(bar.transform, "", 30, TextAnchor.MiddleLeft, UIKit.Accent);
            layoutInfo.rectTransform.At(0f, 0.5f, 30, 0, 300, 100, 0f, 0.5f);
            void Tool(float x, string label, string icon, System.Action action, float w = 150)
            {
                var b = UIKit.Btn(bar.transform, label, action, 26, null, icon);
                b.GetComponent<RectTransform>().At(0f, 0.5f, x, 0, w, 90, 0f, 0.5f);
            }
            Tool(300, "", "remove", () => ResizeSelected(-0.1f), 100);
            Tool(410, "", "add", () => ResizeSelected(0.1f), 100);
            Tool(520, "HIDE", "visibility_off", ToggleSelected, 170);
            Tool(700, "RESET", "refresh", () => { Controls.ResetLayout(); LoadLayout(); }, 190);
            Tool(900, "SAVE", "play_arrow", () => { SaveLayout(); ShowScreen("settings"); Toast("Controls layout saved"); }, 260);
        }

        private void SelectLayoutItem(LayoutItem item)
        {
            layoutSelected = item;
            foreach (var i in layoutItems) i.Image.color = i == item ? new Color(1f, 0.6f, 0.1f, 0.5f) : new Color(1, 1, 1, i.Visible ? 0.25f : 0.06f);
            layoutInfo.text = ButtonLooks[item.Button].label + $"  {Mathf.RoundToInt(item.Scale * 100)}%";
        }

        private void ResizeSelected(float d)
        {
            if (layoutSelected == null) return;
            layoutSelected.Scale = Mathf.Clamp(layoutSelected.Scale + d, 0.5f, 2f);
            float r = Controls.DefaultRadius(layoutSelected.Button) * layoutSelected.Scale;
            layoutSelected.Rect.sizeDelta = new Vector2(r * 2, r * 2);
            SelectLayoutItem(layoutSelected);
        }

        private void ToggleSelected()
        {
            if (layoutSelected == null) return;
            layoutSelected.Visible = !layoutSelected.Visible;
            SelectLayoutItem(layoutSelected);
        }

        private void LoadLayout()
        {
            foreach (var item in layoutItems)
            {
                item.Scale = Controls.ButtonRadius(item.Button) / Controls.DefaultRadius(item.Button);
                item.Visible = Controls.ButtonVisible(item.Button);
                float r = Controls.ButtonRadius(item.Button);
                item.Rect.At(0f, 0f, Controls.ButtonCenter(item.Button).x, Controls.ButtonCenter(item.Button).y, r * 2, r * 2);
                item.Image.color = new Color(1, 1, 1, item.Visible ? 0.25f : 0.06f);
            }
            layoutSelected = null;
            layoutInfo.text = "";
        }

        private void SaveLayout()
        {
            foreach (var item in layoutItems)
                Controls.SaveButton(item.Button, item.Rect.anchoredPosition, item.Scale, item.Visible);
        }

        public void OpenLayoutEditor()
        {
            ShowScreen("layout");
            LoadLayout();
        }
    }
}
