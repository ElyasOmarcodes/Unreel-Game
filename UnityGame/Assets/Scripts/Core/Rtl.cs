using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>
    /// Pashto / Dari / Arabic text for Unity's simple text renderer: joins the letters (picks the
    /// isolated, initial, medial or final form from the Vazirmatn UA font, lam-alef ligatures) and puts
    /// right-to-left runs into display order, keeping Latin words and numbers left-to-right.
    /// </summary>
    public static partial class Rtl
    {
        private static Dictionary<char, int[]> forms;

        private static void Build()
        {
            forms = new Dictionary<char, int[]>();
            for (int i = 0; i + 4 < Forms.Length; i += 5)
                forms[(char)Forms[i]] = new[] { Forms[i + 1], Forms[i + 2], Forms[i + 3], Forms[i + 4] };
        }

        public static bool IsRtl(char c) => (c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFEFC) || (c >= 0xE000 && c <= 0xE1FF);

        public static bool HasRtl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if (IsRtl(c)) return true;
            return false;
        }

        private static bool Transparent(char c) => (c >= 0x064B && c <= 0x065F) || c == 0x0670;
        private static bool JoinsForward(char c) => forms.TryGetValue(c, out var f) && f[1] != 0;
        private static bool JoinsBack(char c) => forms.TryGetValue(c, out var f) && f[3] != 0;

        // lam + alef ligatures: alef variant -> (isolated, final)
        private static readonly Dictionary<char, (char, char)> LamAlef = new Dictionary<char, (char, char)>
        {
            { 'ا', ('ﻻ', 'ﻼ') }, { 'آ', ('ﻵ', 'ﻶ') },
            { 'أ', ('ﻷ', 'ﻸ') }, { 'إ', ('ﻹ', 'ﻺ') },
        };

        /// <summary>Text ready for display (unchanged if it has no right-to-left letters).</summary>
        public static string Shape(string text)
        {
            if (!HasRtl(text)) return text;
            if (forms == null) Build();
            var shaped = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!forms.TryGetValue(c, out var f)) { shaped.Append(c); continue; }
                int p = i - 1;
                while (p >= 0 && Transparent(text[p])) p--;
                int n = i + 1;
                while (n < text.Length && Transparent(text[n])) n++;
                bool joinBack = p >= 0 && JoinsForward(text[p]) && JoinsBack(c);
                if (c == 'ل' && n < text.Length && LamAlef.TryGetValue(text[n], out var lig))
                {
                    shaped.Append(joinBack ? lig.Item2 : lig.Item1);
                    i = n;
                    continue;
                }
                bool joinForward = n < text.Length && JoinsForward(c) && JoinsBack(text[n]);
                int form = joinBack && joinForward ? 2 : joinBack ? 3 : joinForward ? 1 : 0;
                int code = f[form];
                if (code == 0) code = form == 2 ? (f[3] != 0 ? f[3] : f[1]) : f[0];
                shaped.Append(code != 0 ? (char)code : c);
            }
            return Reorder(shaped.ToString());
        }

        private static char Mirror(char c) => c == '(' ? ')' : c == ')' ? '(' : c == '[' ? ']' : c == ']' ? '[' : c == '<' ? '>' : c == '>' ? '<' : c;

        /// <summary>Visual order for one line: right-to-left runs reversed, Latin words and digits kept.</summary>
        private static string Reorder(string s)
        {
            // base direction from the first strong character
            bool baseRtl = false;
            foreach (var c in s)
            {
                if (IsRtl(c)) { baseRtl = true; break; }
                if (char.IsLetter(c)) break;
            }
            // strong direction of every character; neutrals (spaces, punctuation) take the direction
            // of their neighbours when both sides agree, otherwise the base direction
            var dirs = new int[s.Length]; // 1 rtl, -1 ltr, 0 neutral
            for (int i = 0; i < s.Length; i++)
                dirs[i] = IsRtl(s[i]) ? 1 : char.IsLetterOrDigit(s[i]) ? -1 : 0;
            var runs = new List<(bool rtl, StringBuilder text)>();
            for (int i = 0; i < s.Length; i++)
            {
                bool dir;
                if (dirs[i] != 0) dir = dirs[i] > 0;
                else
                {
                    int before = 0, after = 0;
                    for (int j = i - 1; j >= 0 && before == 0; j--) before = dirs[j];
                    for (int j = i + 1; j < s.Length && after == 0; j++) after = dirs[j];
                    dir = before != 0 && before == after ? before > 0 : baseRtl;
                }
                if (runs.Count == 0 || runs[runs.Count - 1].rtl != dir) runs.Add((dir, new StringBuilder()));
                runs[runs.Count - 1].text.Append(s[i]);
            }
            var result = new StringBuilder(s.Length);
            if (baseRtl) runs.Reverse();
            foreach (var run in runs)
            {
                var t = run.text.ToString();
                if (run.rtl)
                    for (int i = t.Length - 1; i >= 0; i--) result.Append(Mirror(t[i]));
                else result.Append(t);
            }
            return result.ToString();
        }
    }

    /// <summary>
    /// Shows a Pashto / Dari entry joined and right-to-left while the field is not being edited
    /// (the phone's own keyboard shows the text while typing).
    /// </summary>
    public class RtlInputDisplay : MonoBehaviour
    {
        private InputField field;
        private Text overlay;

        public static void Attach(InputField field)
        {
            var go = new GameObject("RtlText");
            go.transform.SetParent(field.textComponent.transform.parent, false);
            var rect = go.AddComponent<RectTransform>();
            var src = field.textComponent.rectTransform;
            rect.anchorMin = src.anchorMin; rect.anchorMax = src.anchorMax;
            rect.offsetMin = src.offsetMin; rect.offsetMax = src.offsetMax;
            rect.pivot = src.pivot;
            var t = go.AddComponent<Text>();
            t.font = field.textComponent.font;
            t.fontSize = field.textComponent.fontSize;
            t.color = field.textComponent.color;
            t.alignment = field.textComponent.alignment;
            t.raycastTarget = false;
            var d = field.gameObject.AddComponent<RtlInputDisplay>();
            d.field = field;
            d.overlay = t;
        }

        private void LateUpdate()
        {
            if (field == null || overlay == null) return;
            bool show = !field.isFocused && Rtl.HasRtl(field.text);
            overlay.enabled = show;
            if (show) overlay.text = Rtl.Shape(field.text);
            var c = field.textComponent.color;
            c.a = show ? 0f : 1f;
            field.textComponent.color = c;
        }
    }
}
