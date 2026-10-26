using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The five ready-made characters of the player's pack (UnityGame/Models/Characters: every part of a
/// character in one OBJ, in a T-pose, metres, feet at 0, facing +Z; one packed texture each under
/// Assets/Characters) put on the agent's own skeleton, so they play every animation:
///  - the model is scaled to the body's height and stood where the body stands at rest;
///  - the arms are mapped bone by bone: the character's shoulder, elbow, wrist and finger tips are
///    found on its mesh, and the arm bones are bound in that pose (their bind poses turned from the
///    body's rest pose onto the character's arms), so a T-pose model fits an A-pose skeleton;
///  - the skin weights of the trunk, legs and head come from the nearest point of the body, the
///    arms are weighted along their own bones with soft elbows and wrists.
/// Each character is a part of the look (Wardrobe.Slot.Character); a team armband goes with them.
/// </summary>
public static class CharacterPack
{
    public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Models", "Characters"));
    private const string Textures = "Assets/Characters";

    /// <summary>Characters by their value in the look (1 ..): file, female.</summary>
    public static readonly (string key, bool female)[] All =
    {
        ("Char3", false), ("Char6", false), ("Fem1", true), ("male2F", false), ("male3F", false),
    };

    public static List<Agent.OutfitPart> Dress(Outfit.Body body, bool female, Material team, List<Renderer> teamRenderers)
    {
        var parts = new List<Agent.OutfitPart>();
        int mask = 0;
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].female != female) continue;
            var go = Build(body, All[i].key);
            if (go == null) continue;
            parts.Add(new Agent.OutfitPart { Object = go, Slot = Wardrobe.Slot.Character, Mask = 1 << (i + 1) });
            mask |= 1 << (i + 1);
        }
        if (mask == 0) return parts;
        // the team colour on every one of them: a band round the left upper arm
        var band = Armband(body, team);
        if (band != null)
        {
            parts.Add(new Agent.OutfitPart { Object = band, Slot = Wardrobe.Slot.Character, Mask = mask });
            teamRenderers.AddRange(band.GetComponentsInChildren<Renderer>(true));
        }
        return parts;
    }

    private class Model
    {
        public readonly List<Vector3> P = new List<Vector3>(), N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();
    }

    /// <summary>The OBJ in Unity's space (x mirrored, corners reversed), one vertex per distinct corner.</summary>
    private static Model Load(string file)
    {
        var inv = CultureInfo.InvariantCulture;
        var v = new List<Vector3>(); var vt = new List<Vector2>(); var vn = new List<Vector3>();
        var model = new Model();
        var corners = new Dictionary<(int, int, int), int>();
        foreach (var line in File.ReadLines(file))
        {
            if (line.Length < 2) continue;
            var t = line.Split(' ');
            if (t[0] == "v") v.Add(new Vector3(-float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv)));
            else if (t[0] == "vt") vt.Add(new Vector2(float.Parse(t[1], inv), float.Parse(t[2], inv)));
            else if (t[0] == "vn") vn.Add(new Vector3(-float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv)));
            else if (t[0] == "f")
            {
                var idx = new List<int>();
                for (int k = 1; k < t.Length; k++)
                {
                    if (t[k].Length == 0) continue;
                    var a = t[k].Split('/');
                    int pi = int.Parse(a[0]) - 1, ti = a.Length > 1 && a[1].Length > 0 ? int.Parse(a[1]) - 1 : -1, ni = a.Length > 2 && a[2].Length > 0 ? int.Parse(a[2]) - 1 : -1;
                    if (!corners.TryGetValue((pi, ti, ni), out int c))
                    {
                        c = corners[(pi, ti, ni)] = model.P.Count;
                        model.P.Add(v[pi]);
                        model.UV.Add(ti >= 0 ? vt[ti] : Vector2.zero);
                        model.N.Add(ni >= 0 ? vn[ni] : Vector3.up);
                    }
                    idx.Add(c);
                }
                // a fan, wound the other way round (the mirror turned the faces inside out)
                for (int k = 1; k + 1 < idx.Count; k++) { model.T.Add(idx[0]); model.T.Add(idx[k + 1]); model.T.Add(idx[k]); }
            }
        }
        return model;
    }

    private static GameObject Build(Outfit.Body body, string key)
    {
        string file = Path.Combine(Folder, key + ".obj");
        if (!File.Exists(file)) { Debug.LogWarning("[Setup] character missing: " + file); return null; }
        var m = Load(file);

        // stand it where the body stands, as tall as the body
        float bodyLow = body.Points.Min(p => p.y), bodyHigh = body.Points.Max(p => p.y);
        float low = m.P.Min(p => p.y), high = m.P.Max(p => p.y);
        var pelvis = body.At("pelvis", "spine_01");
        float s = (bodyHigh - bodyLow) / Mathf.Max(0.5f, high - low);
        for (int i = 0; i < m.P.Count; i++)
            m.P[i] = new Vector3(pelvis.x + m.P[i].x * s, bodyLow + (m.P[i].y - low) * s, pelvis.z + m.P[i].z * s);

        var bindposes = (Matrix4x4[])body.Bindposes.Clone();
        var weights = new BoneWeight[m.P.Count];
        var arm = new bool[m.P.Count];
        foreach (string side in new[] { "l", "r" })
        {
            int up = body.Bone("upperarm_" + side), low1 = body.Bone("lowerarm_" + side), hand = body.Bone("hand_" + side);
            if (up < 0 || low1 < 0 || hand < 0) { Debug.LogWarning($"[Setup] {key}: no arm bones on side {side}"); continue; }
            Vector3 shR = body.At("upperarm_" + side), elR = body.At("lowerarm_" + side), wrR = body.At("hand_" + side);
            float sign = Mathf.Sign(shR.x - pelvis.x), reach0 = Mathf.Abs(shR.x - pelvis.x);
            // the finger tip at rest: the body's hand point furthest from the wrist
            var tipR = body.PointsIn(Outfit.Region.Hands).Where(p => Mathf.Sign(p.x - pelvis.x) == sign).OrderByDescending(p => (p - wrR).sqrMagnitude).FirstOrDefault();
            // the character's arm: its line just outside the shoulder, its finger tip furthest out
            var near = m.P.Where(p => sign * (p.x - pelvis.x) > reach0 + 0.08f && sign * (p.x - pelvis.x) < reach0 + 0.2f).ToList();
            var tip = m.P.OrderByDescending(p => sign * (p.x - pelvis.x)).First();
            if (near.Count == 0 || tipR == Vector3.zero) continue;
            var shC = new Vector3(shR.x, near.Average(p => p.y), near.Average(p => p.z));
            var dir = (tip - shC).normalized;
            float l1 = (elR - shR).magnitude, l2 = (wrR - elR).magnitude, l3 = (tipR - wrR).magnitude;
            float k = (tip - shC).magnitude / Mathf.Max(0.1f, l1 + l2 + l3);
            var elC = shC + dir * l1 * k;
            var wrC = elC + dir * l2 * k;
            Matrix4x4 Map(Vector3 restFrom, Vector3 restTo, Vector3 from, Vector3 to) =>
                Matrix4x4.Translate(from) * Matrix4x4.Rotate(Quaternion.FromToRotation(restTo - restFrom, to - from)) * Matrix4x4.Translate(-restFrom);
            var upper = Map(shR, elR, shC, elC);
            var lower = Map(elR, wrR, elC, wrC);
            var palm = Map(wrR, tipR, wrC, tip);
            // every bone of this arm bound in the character's pose
            for (int b = 0; b < bindposes.Length; b++)
            {
                var bone = body.Renderer.bones[b];
                if (bone == null || !bone.name.ToLowerInvariant().EndsWith("_" + side)) continue;
                string n = bone.name.ToLowerInvariant();
                Matrix4x4? map = n.StartsWith("upperarm") ? upper : n.StartsWith("lowerarm") ? lower : Outfit.Of(n) == Outfit.Region.Hands ? palm : (Matrix4x4?)null;
                if (map.HasValue) bindposes[b] = (map.Value * body.Bind(b)).inverse * body.ToWorld;
            }
            // the arm's vertices along its bones: soft at the elbow and the wrist
            float e1 = l1 * k, e2 = (l1 + l2) * k;
            for (int i = 0; i < m.P.Count; i++)
            {
                var p = m.P[i];
                if (sign * (p.x - pelvis.x) <= reach0 + 0.03f) continue;
                arm[i] = true;
                float t = Vector3.Dot(p - shC, dir);
                float toLower = Mathf.Clamp01((t - (e1 - 0.05f)) / 0.1f), toHand = Mathf.Clamp01((t - (e2 - 0.03f)) / 0.06f);
                weights[i] = Outfit.Weight((up, 1f - toLower), (low1, toLower * (1f - toHand)), (hand, toHand));
            }
        }
        for (int i = 0; i < m.P.Count; i++)
            if (!arm[i]) weights[i] = body.Nearest(m.P[i], Outfit.Region.Torso, Outfit.Region.Legs, Outfit.Region.Head, Outfit.Region.Feet);

        var g = new Outfit.Garment();
        for (int i = 0; i < m.P.Count; i++) g.Add(m.P[i], m.N[i], m.UV[i], weights[i]);
        for (int i = 0; i + 2 < m.T.Count; i += 3) g.Tri(m.T[i], m.T[i + 1], m.T[i + 2]);
        Texture2D T(string kind) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{key}/Body_{kind}.png");
        var mat = AssetSetup.Mat("Char_" + key, Color.white, 0.3f, 0f, T("Albedo"), T("Normal"));
        // hair, lashes and beards are cut out of the texture's alpha
        mat.SetFloat("_Mode", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        var go = Outfit.Wear(body, g, "Character_" + key, bindposes, mat);
        Debug.Log($"[Setup] character {key}: {m.P.Count} vertices, scale {s:0.00}");
        return go;
    }

    /// <summary>A cloth band in the team colour round the left upper arm (riding on its bone).</summary>
    private static GameObject Armband(Outfit.Body body, Material team)
    {
        if (body.Bone("upperarm_l") < 0) return null;
        Vector3 a = body.At("upperarm_l"), b = body.At("lowerarm_l");
        var axis = (b - a).normalized;
        var c = Vector3.Lerp(a, b, 0.4f);
        var u = Vector3.Cross(axis, Vector3.forward).normalized;
        if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(axis, Vector3.up).normalized;
        var w = Vector3.Cross(axis, u);
        var g = new Outfit.Garment();
        const int n = 16;
        const float r = 0.072f, half = 0.045f;
        for (int i = 0; i <= n; i++)
        {
            float t = i * Mathf.PI * 2f / n;
            var out1 = u * Mathf.Cos(t) + w * Mathf.Sin(t);
            g.Add(c + out1 * r - axis * half, out1, new Vector2(i / (float)n, 0));
            g.Add(c + out1 * r + axis * half, out1, new Vector2(i / (float)n, 1));
        }
        // both faces (seen from outside whichever way the corners turn)
        for (int i = 0; i < n; i++) { g.Quad(2 * i + 1, 2 * i + 3, 2 * i, 2 * i + 2); g.Quad(2 * i + 3, 2 * i + 1, 2 * i + 2, 2 * i); }
        return Outfit.Attach(body, g, "upperarm_l", "TeamArmband", team);
    }
}
