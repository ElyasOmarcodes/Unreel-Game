using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UA
{
    /// <summary>Small helpers to build the uGUI screens from code (reference resolution 1920x1080).</summary>
    public static class UIKit
    {
        public static Font Font;
        public static Sprite Rounded, Circle, Ring, Soft;

        public static readonly Color Accent = new Color(1f, 0.6f, 0.1f);
        public static readonly Color AccentDark = new Color(0.75f, 0.38f, 0.02f);
        public static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.11f, 0.9f);
        public static readonly Color ButtonColor = new Color(0.13f, 0.16f, 0.21f, 0.96f);
        public static readonly Color TextColor = new Color(0.93f, 0.95f, 0.97f);
        public static readonly Color Muted = new Color(0.62f, 0.67f, 0.74f);
        public static readonly Color Blue = new Color(0.25f, 0.5f, 1f);
        public static readonly Color Red = new Color(0.95f, 0.3f, 0.25f);
        public static readonly Color Green = new Color(0.3f, 0.85f, 0.45f);

        public static void Init()
        {
            Font = Resources.Load<Font>("Fonts/RussoOne-Regular");
            if (Font == null) Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Rounded = MakeSprite(64, (x, y) => RoundedAlpha(x, y, 64, 18), new Vector4(22, 22, 22, 22));
            Circle = MakeSprite(128, (x, y) => CircleAlpha(x, y, 128, false), Vector4.zero);
            Ring = MakeSprite(128, (x, y) => CircleAlpha(x, y, 128, true), Vector4.zero);
            Soft = MakeSprite(64, (x, y) => { float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f; return Mathf.Clamp01(1f - d); }, Vector4.zero);
        }

        private static float RoundedAlpha(int x, int y, int size, float radius)
        {
            float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            return Mathf.Clamp01(radius - d + 0.5f);
        }

        private static float CircleAlpha(int x, int y, int size, bool ring)
        {
            float r = size / 2f;
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
            return ring ? Mathf.Clamp01(1f - Mathf.Abs(d - (r - 5f)) / 3f) : Mathf.Clamp01(r - d);
        }

        private static Sprite MakeSprite(int size, Func<int, int, float> alpha, Vector4 border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * alpha(x, y)));
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
        }

        public static Canvas CreateCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return canvas;
        }

        /// <summary>Width of the canvas in reference units (height is always 1080).</summary>
        public static float Width => 1080f * Screen.width / Mathf.Max(1, Screen.height);

        public static RectTransform Node(Transform parent, string name = "Node")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Fill(this RectTransform r, float inset = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        /// <summary>Anchor at a normalized point of the parent and set position (from that anchor) and size.</summary>
        public static RectTransform At(this RectTransform r, float ax, float ay, float x, float y, float w, float h, float px = 0.5f, float py = 0.5f)
        {
            r.anchorMin = r.anchorMax = new Vector2(ax, ay);
            r.pivot = new Vector2(px, py);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        public static Image Img(Transform parent, Color color, Sprite sprite = null, bool raycast = false)
        {
            var r = Node(parent, "Image");
            var image = r.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text Txt(Transform parent, string text, int size, TextAnchor align = TextAnchor.MiddleCenter, Color? color = null)
        {
            var r = Node(parent, "Text");
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? TextColor;
            t.text = text;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Text Shadowed(this Text t)
        {
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.75f);
            shadow.effectDistance = new Vector2(2, -2);
            return t;
        }

        public static Button Btn(Transform parent, string label, Action onClick, int size = 40, Color? color = null)
        {
            var image = Img(parent, color ?? ButtonColor, Rounded, true);
            image.name = "Button " + label;
            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                Sfx.Play("click", 0.5f);
                onClick?.Invoke();
            });
            var t = Txt(image.transform, label, size);
            t.rectTransform.Fill();
            return button;
        }

        public static void SetLabel(this Button b, string label) => b.GetComponentInChildren<Text>().text = label;

        public static InputField Input(Transform parent, string value, string placeholder, int size = 36, int limit = 14)
        {
            var image = Img(parent, new Color(0, 0, 0, 0.55f), Rounded, true);
            var field = image.gameObject.AddComponent<InputField>();
            var text = Txt(image.transform, "", size, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.rectTransform.Fill();
            text.rectTransform.offsetMin = new Vector2(24, 0);
            text.rectTransform.offsetMax = new Vector2(-24, 0);
            var ph = Txt(image.transform, placeholder, size, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.35f));
            ph.rectTransform.Fill();
            ph.rectTransform.offsetMin = new Vector2(24, 0);
            ph.rectTransform.offsetMax = new Vector2(-24, 0);
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = limit;
            field.text = value;
            return field;
        }

        /// <summary>A row "LABEL    &lt;  value  &gt;" for option screens.</summary>
        public static Text Stepper(Transform parent, string label, float y, Func<string> value, Action<int> change, float width = 1000)
        {
            var row = Node(parent, "Row " + label).At(0.5f, 1f, 0, y, width, 84, 0.5f, 1f);
            var bg = Img(row, new Color(1, 1, 1, 0.04f), Rounded);
            bg.rectTransform.Fill();
            var l = Txt(row, label, 34, TextAnchor.MiddleLeft, Muted);
            l.rectTransform.Fill();
            l.rectTransform.offsetMin = new Vector2(30, 0);
            var v = Txt(row, value(), 36, TextAnchor.MiddleCenter, Accent);
            v.rectTransform.At(1f, 0.5f, -230, 0, 300, 84);
            var minus = Btn(row, "<", () => { change(-1); v.text = value(); }, 40);
            minus.GetComponent<RectTransform>().At(1f, 0.5f, -430, 0, 90, 70);
            var plus = Btn(row, ">", () => { change(1); v.text = value(); }, 40);
            plus.GetComponent<RectTransform>().At(1f, 0.5f, -40, 0, 90, 70, 1f, 0.5f);
            return v;
        }

        public static RectTransform Panel(Transform parent, float w, float h, float y = 0)
        {
            var image = Img(parent, PanelColor, Rounded, true);
            var r = image.rectTransform.At(0.5f, 0.5f, 0, y, w, h);
            return r;
        }

        public static Text Title(Transform parent, string text, float y, int size = 64)
        {
            var t = Txt(parent, text, size, TextAnchor.MiddleCenter, TextColor).Shadowed();
            t.rectTransform.At(0.5f, 1f, 0, y, 1600, size + 30, 0.5f, 1f);
            return t;
        }
    }
}
