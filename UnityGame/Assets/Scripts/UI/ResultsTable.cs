using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>
    /// The match table, on the results page and over the match (RANK): every player ranked by the match
    /// score, or every team (and each player who fights alone) ranked by its kills, with its hits, damage,
    /// headshots and accuracy. BY TEAM / BY PLAYER switches between the two when there are teams.
    /// </summary>
    public class ResultsTable
    {
        public const float Width = 1600f;
        private const float RowHeight = 66f;

        // columns: header, x of the left edge, width
        private static readonly (string head, float x, float w)[] PlayerColumns =
        {
            ("#", 20, 60), ("PLAYER", 90, 330), ("TEAM", 420, 100), ("KILLS", 520, 100), ("DEATHS", 620, 110), ("K/D", 730, 90),
            ("HITS", 820, 100), ("DAMAGE", 920, 130), ("HEAD", 1050, 90), ("ACCURACY", 1140, 150), ("SCORE", 1290, 130), ("RATING", 1420, 160),
        };
        private static readonly (string head, float x, float w)[] TeamColumns =
        {
            ("#", 20, 60), ("TEAM", 90, 330), ("PLAYERS", 420, 120), ("KILLS", 540, 120), ("DEATHS", 660, 130), ("HITS", 790, 120),
            ("DAMAGE", 910, 150), ("HEAD", 1060, 110), ("ACCURACY", 1170, 170), ("SCORE", 1340, 140),
        };

        public bool ByTeam;
        private readonly RectTransform head, rows;
        private readonly Button toggle;
        private System.Action refill;

        /// <summary>The header and the scrolling rows, 'height' tall, at the top of 'parent'; the switch above its right end.</summary>
        public ResultsTable(Transform parent, float height)
        {
            var bar = UIKit.Img(parent, new Color(1, 1, 1, 0.06f), UIKit.Rounded);
            bar.rectTransform.At(0.5f, 1f, 0, -24, Width, 64, 0.5f, 1f);
            head = bar.rectTransform;
            // the rows scroll when there are many
            var view = UIKit.Img(parent, new Color(0, 0, 0, 0.01f), null, true);
            view.rectTransform.At(0.5f, 1f, 0, -96, Width, height - 96, 0.5f, 1f);
            view.gameObject.AddComponent<RectMask2D>();
            rows = UIKit.Node(view.transform, "Rows");
            rows.anchorMin = new Vector2(0, 1);
            rows.anchorMax = new Vector2(1, 1);
            rows.pivot = new Vector2(0.5f, 1);
            rows.anchoredPosition = Vector2.zero;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = rows;
            scroll.viewport = view.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            toggle = UIKit.Btn(parent, "BY TEAM", () => { ByTeam = !ByTeam; refill?.Invoke(); }, 28, null, "swap_horiz");
            toggle.GetComponent<RectTransform>().At(1f, 1f, -10, 62, 330, 64, 1f, 0f);
        }

        public static bool HasTeams(Session s) => s.Players.Values.Any(p => p.Team > 0);

        /// <summary>
        /// A fair match score, not kills alone: kills, the damage done (help on kills others finished),
        /// headshots and accuracy count for, deaths count against; the winning team gets a bonus.
        /// </summary>
        public static float Score(PlayerState p, Session s)
        {
            float accuracy = p.Shots > 0 ? (float)p.ShotHits / p.Shots : 0f;
            float score = p.Kills * 100f + p.DamageDone * 0.5f + p.Heads * 15f - p.Deaths * 40f;
            if (p.Shots >= 15) score += accuracy * 150f;
            if (p.Team > 0 && p.Team < s.TeamScore.Length && s.TeamScore[p.Team] > 0 && s.TeamScore[p.Team] == s.TeamScore.Max()) score += 60f;
            return Mathf.Max(0f, score);
        }

        /// <summary>The teams in play, best first (by their kills): team number, or 0 with the one player alone.</summary>
        public static List<(int team, List<PlayerState> members)> Sides(Session s)
        {
            var sides = s.Players.Values.Where(p => p.Team > 0).GroupBy(p => (int)p.Team).Select(g => (g.Key, g.ToList()))
                .Concat(s.Players.Values.Where(p => p.Team == 0).Select(p => (0, new List<PlayerState> { p })));
            return sides.OrderByDescending(x => x.Item2.Sum(p => p.Kills)).ThenByDescending(x => x.Item2.Sum(p => p.DamageDone)).ToList();
        }

        public void Fill(Session s)
        {
            refill = () => Fill(s);
            bool teams = HasTeams(s);
            toggle.gameObject.SetActive(teams);
            toggle.SetLabel(ByTeam ? "BY PLAYER" : "BY TEAM");
            bool byTeam = ByTeam && teams;
            var columns = byTeam ? TeamColumns : PlayerColumns;
            foreach (Transform child in head) Object.Destroy(child.gameObject);
            foreach (var c in columns) Cell(head, c.head, c.x, c.w, 24, UIKit.Muted);
            foreach (Transform child in rows) Object.Destroy(child.gameObject);

            var lines = new List<(string[] values, Color team, bool mine, float rating)>();
            if (byTeam)
            {
                foreach (var (team, members) in Sides(s))
                {
                    int shots = members.Sum(p => p.Shots), hits = members.Sum(p => p.ShotHits);
                    string name = team > 0 ? MatchConfig.TeamName(team) : Rtl.Shape(Short(members[0].Name)) + "  <size=20><color=#8a94a0>" + Loc.T("ALONE") + "</color></size>";
                    lines.Add((new[]
                    {
                        (lines.Count + 1).ToString(), name, members.Count.ToString(), members.Sum(p => p.Kills).ToString(), members.Sum(p => p.Deaths).ToString(),
                        members.Sum(p => p.Hits).ToString(), members.Sum(p => p.DamageDone).ToString(), members.Sum(p => p.Heads).ToString(),
                        shots > 0 ? Mathf.RoundToInt(100f * hits / shots) + "%" : "-", Mathf.RoundToInt(members.Sum(p => Score(p, s))).ToString(),
                    }, team > 0 ? MatchConfig.TeamColor(team) : Color.clear, members.Any(p => p.Id == s.LocalId), -1f));
                }
            }
            else
            {
                var players = s.Players.Values.Select(p => (p, score: Score(p, s))).OrderByDescending(x => x.score).ThenByDescending(x => x.p.Kills).ToList();
                float top = players.Count > 0 ? Mathf.Max(1f, players[0].score) : 1f;
                for (int i = 0; i < players.Count; i++)
                {
                    var (p, score) = players[i];
                    float kd = p.Deaths > 0 ? (float)p.Kills / p.Deaths : p.Kills;
                    int rating = Mathf.RoundToInt(100f * score / top);
                    string name = Rtl.Shape(Short(p.Name)) + (p.IsBot ? "  <size=20><color=#8a94a0>" + Loc.T("BOT") + "</color></size>" : "");
                    lines.Add((new[]
                    {
                        i == 0 ? "MVP" : (i + 1).ToString(), name, p.Team > 0 ? $"<color={MatchConfig.TeamHex(p.Team)}>T{p.Team}</color>" : "-",
                        p.Kills.ToString(), p.Deaths.ToString(), kd.ToString("0.0"), p.Hits.ToString(), p.DamageDone.ToString(), p.Heads.ToString(),
                        p.Shots > 0 ? Mathf.RoundToInt(100f * p.ShotHits / p.Shots) + "%" : "-", Mathf.RoundToInt(score).ToString(), rating + "%",
                    }, p.Team > 0 ? MatchConfig.TeamColor(p.Team) : Color.clear, p.Id == s.LocalId, rating / 100f));
                }
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var (values, team, mine, rating) = lines[i];
                var row = UIKit.Img(rows, mine ? new Color(1f, 0.6f, 0.1f, 0.16f) : new Color(1, 1, 1, i % 2 == 0 ? 0.035f : 0.015f), UIKit.Rounded);
                row.rectTransform.At(0.5f, 1f, 0, -i * RowHeight, Width, RowHeight - 6, 0.5f, 1f);
                if (team.a > 0f)
                {
                    var strip = UIKit.Img(row.transform, team, UIKit.Rounded);
                    strip.rectTransform.At(0f, 0.5f, 4, 0, 8, RowHeight - 18, 0f, 0.5f);
                }
                Color text = mine ? UIKit.Accent : UIKit.TextColor;
                for (int c = 0; c < values.Length; c++)
                {
                    var col = columns[c];
                    var cell = Cell(row.transform, values[c], col.x, col.w, c == 1 ? 30 : 28, c == 0 && i == 0 ? UIKit.Accent : text);
                    if (c == 1 && byTeam && team.a > 0f) cell.color = team;
                    if (rating >= 0f && c == values.Length - 1) cell.color = Color.Lerp(UIKit.Red, UIKit.Green, rating);
                }
            }
            rows.sizeDelta = new Vector2(0, lines.Count * RowHeight);
        }

        private static string Short(string name) => name.Length > 16 ? name.Substring(0, 16) : name;

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
    }
}
