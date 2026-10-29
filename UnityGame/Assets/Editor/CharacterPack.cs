using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The ready-made characters of the player's packs (UnityGame/Models/Characters: every part of a
/// character in one OBJ, in a T-pose, metres, feet at 0, facing +Z; one packed texture each under
/// Assets/Characters) put on the agent's own skeleton, so they play every animation:
///  - the model is scaled to the body's height and stood where the body stands at rest;
///  - every bone is mapped on the player's Mixamo skeleton (Models/Characters/mixamo_skeleton.json, made
///    on Char3, fitted to the others: as tall as each, the arms to its finger tips): the agent's bones are
///    bound where the same joints of the character are (their bind poses turned from the body's rest
///    pose onto the character's bones), so a T-pose model fits an A-pose skeleton;
///  - the skin weights come from the distance to those bones out of their flesh, blended where two
///    meet, never from a bone of the other side: no seam at the shoulders, elbows, hips or knees.
/// Each character is a part of the look (Wardrobe.Slot.Character); a team armband goes with them.
/// </summary>
public static class CharacterPack
{
    public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Models", "Characters"));
    private const string Textures = "Assets/Characters";

    /// <summary>Characters by their value in the look (1 ..): file, female (the woman is no longer built).</summary>
    public static readonly (string key, bool female)[] All =
    {
        ("Char3", false), ("Char6", false), ("Fem1", true), ("male2F", false), ("male3F", false),
        // the player's second pack (made from downloaded models: welded, cut to ~22k triangles, baked onto one
        // 1024 atlas; their own fitted Mixamo joints in Key_skeleton.json)
        ("Swat", false), ("Rifleman", false), ("RedBeret", false),
    };

    public static List<Agent.OutfitPart> Dress(Outfit.Body body, bool female, Material team, List<Renderer> teamRenderers)
    {
        var parts = new List<Agent.OutfitPart>();
        int mask = 0;
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].female || female) continue;
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

    /// <summary>
    /// The agent's bones on the pack's Mixamo skeleton: the agent bone, the agent bone its direction points
    /// to (null: only moved, not turned), the Mixamo joints of the same bone in the character, and how thick
    /// the flesh round it is (for the weights). '*' is the side: l / r for the agent, Left / Right for Mixamo.
    /// </summary>
    private static readonly (string bone, string toward, string from, string to, float radius)[] Bones =
    {
        ("pelvis", "spine_01", "Hips", "Spine", 0.12f), ("spine_01", "spine_02", "Spine", "Spine1", 0.13f),
        ("spine_02", "spine_03", "Spine1", "Spine2", 0.14f), ("spine_03", "neck_01", "Spine2", "Neck", 0.14f),
        ("neck_01", "head", "Neck", "Head", 0.05f), ("head", null, "Head", "HeadTop_End", 0.09f),
        ("clavicle_*", "upperarm_*", "*Shoulder", "*Arm", 0.06f), ("upperarm_*", "lowerarm_*", "*Arm", "*ForeArm", 0.05f),
        ("lowerarm_*", "hand_*", "*ForeArm", "*Hand", 0.04f), ("hand_*", "middle_01_*", "*Hand", "*HandMiddle1", 0.03f),
        ("thumb_01_*", "thumb_02_*", "*HandThumb1", "*HandThumb2", 0.01f), ("thumb_02_*", "thumb_03_*", "*HandThumb2", "*HandThumb3", 0.01f), ("thumb_03_*", null, "*HandThumb3", "*HandThumb4", 0.01f),
        ("index_01_*", "index_02_*", "*HandIndex1", "*HandIndex2", 0.01f), ("index_02_*", "index_03_*", "*HandIndex2", "*HandIndex3", 0.01f), ("index_03_*", null, "*HandIndex3", "*HandIndex4", 0.01f),
        ("middle_01_*", "middle_02_*", "*HandMiddle1", "*HandMiddle2", 0.01f), ("middle_02_*", "middle_03_*", "*HandMiddle2", "*HandMiddle3", 0.01f), ("middle_03_*", null, "*HandMiddle3", "*HandMiddle4", 0.01f),
        ("ring_01_*", "ring_02_*", "*HandRing1", "*HandRing2", 0.01f), ("ring_02_*", "ring_03_*", "*HandRing2", "*HandRing3", 0.01f), ("ring_03_*", null, "*HandRing3", "*HandRing4", 0.01f),
        ("pinky_01_*", "pinky_02_*", "*HandPinky1", "*HandPinky2", 0.01f), ("pinky_02_*", "pinky_03_*", "*HandPinky2", "*HandPinky3", 0.01f), ("pinky_03_*", null, "*HandPinky3", "*HandPinky4", 0.01f),
        ("thigh_*", "calf_*", "*UpLeg", "*Leg", 0.08f), ("calf_*", "foot_*", "*Leg", "*Foot", 0.05f),
        ("foot_*", "ball_*", "*Foot", "*ToeBase", 0.04f), ("ball_*", null, "*ToeBase", "*Toe_End", 0.03f),
    };

    private class Stats { public float Low, High, TipNeg, TipPos; }
    private static readonly Dictionary<string, Stats> stats = new Dictionary<string, Stats>();

    private static Stats Measure(string key, Model m)
    {
        if (!stats.TryGetValue(key, out var st))
            stats[key] = st = new Stats { Low = m.P.Min(p => p.y), High = m.P.Max(p => p.y), TipNeg = m.P.Max(p => -p.x), TipPos = m.P.Max(p => p.x) };
        return st;
    }

    /// <summary>
    /// The joints of a Mixamo skeleton JSON (the player's mixamo_skeleton.json: name, parent, local position
    /// and rotation, bind pose of every bone) where they sit in the T-pose mesh, in the loaded space; a bone
    /// without a bind pose is placed from its parent. 'madeOn' is the character it was made on.
    /// </summary>
    private static Dictionary<string, Vector3> ReadJoints(string file, out string madeOn)
    {
        var inv = CultureInfo.InvariantCulture;
        var text = File.ReadAllText(file);
        madeOn = Regex.Match(text, "\"character\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
        float[] Numbers(string part) => Regex.Matches(part, @"-?\d+(\.\d+)?([eE][-+]?\d+)?").Cast<Match>().Select(x => float.Parse(x.Value, inv)).ToArray();
        string Between(string block, string a, string b)
        {
            int i = block.IndexOf(a, StringComparison.Ordinal);
            if (i < 0) return "";
            i += a.Length;
            int j = b == null ? -1 : block.IndexOf(b, i, StringComparison.Ordinal);
            return j < 0 ? block.Substring(i) : block.Substring(i, j - i);
        }
        string Short(string name) => name.Substring(name.LastIndexOf(':') + 1);   // mixamorig:LeftArm -> LeftArm
        var world = new Dictionary<string, Matrix4x4>();
        var joints = new Dictionary<string, Vector3>();
        foreach (var block in text.Split(new[] { "\"name\"" }, StringSplitOptions.None).Skip(1))
        {
            string name = Short(Regex.Match(block, "^\\s*:\\s*\"([^\"]+)\"").Groups[1].Value);
            var parentMatch = Regex.Match(block, "\"parent\"\\s*:\\s*\"([^\"]+)\"");
            string parent = parentMatch.Success ? Short(parentMatch.Groups[1].Value) : null;
            float[] pos = Numbers(Between(block, "\"local_position\"", "\"local_rotation\""));
            float[] rot = Numbers(Between(block, "\"local_rotation\"", "\"local_scale\""));
            float[] bind = Numbers(Between(block, "\"bind_pose\"", null));
            Matrix4x4 w;
            if (bind.Length >= 16)
            {
                var b = Matrix4x4.identity;
                for (int r = 0; r < 4; r++) b.SetRow(r, new Vector4(bind[r * 4], bind[r * 4 + 1], bind[r * 4 + 2], bind[r * 4 + 3]));
                w = b.inverse;
            }
            else if (pos.Length >= 3 && rot.Length >= 4)
            {
                var local = Matrix4x4.TRS(new Vector3(pos[0], pos[1], pos[2]), new Quaternion(rot[0], rot[1], rot[2], rot[3]).normalized, Vector3.one);
                w = parent != null && world.TryGetValue(parent, out var pw) ? pw * local : local;
            }
            else continue;
            world[name] = w;
            joints[name] = w.MultiplyPoint3x4(Vector3.zero);
        }
        return joints;
    }

    /// <summary>
    /// The skeleton of a character: its own (Key_skeleton.json) when there is one, else the shared one fitted
    /// to it: as tall as the character, the arms stretched to its finger tips.
    /// </summary>
    private static Dictionary<string, Vector3> Skeleton(string key, Model m)
    {
        string own = Path.Combine(Folder, key + "_skeleton.json"), shared = Path.Combine(Folder, "mixamo_skeleton.json");
        string file = File.Exists(own) ? own : shared;
        if (!File.Exists(file)) return null;
        var joints = ReadJoints(file, out string madeOn);
        if (file == own || madeOn == key || !File.Exists(Path.Combine(Folder, madeOn + ".obj"))) return joints;
        if (!stats.ContainsKey(madeOn)) Measure(madeOn, Load(Path.Combine(Folder, madeOn + ".obj")));
        Stats r = stats[madeOn], c = Measure(key, m);
        float hr = (c.High - c.Low) / Mathf.Max(0.5f, r.High - r.Low);
        var fitted = new Dictionary<string, Vector3>();
        foreach (var kv in joints)
        {
            var p = kv.Value;
            var q = new Vector3(p.x, c.Low + (p.y - r.Low) * hr, p.z);
            string side = kv.Key.StartsWith("Left") ? "Left" : kv.Key.StartsWith("Right") ? "Right" : null;
            if (side != null && (kv.Key.Contains("Arm") || kv.Key.Contains("Hand")) && joints.TryGetValue(side + "Arm", out var shoulder))
            {
                float sg = Mathf.Sign(shoulder.x), ax = Mathf.Abs(shoulder.x);
                float k = ((sg < 0 ? c.TipNeg : c.TipPos) - ax) / Mathf.Max(0.1f, (sg < 0 ? r.TipNeg : r.TipPos) - ax);
                q.x = sg * (ax + (Mathf.Abs(p.x) - ax) * k);
            }
            fitted[kv.Key] = q;
        }
        return fitted;
    }

    /// <summary>A stretch by 'factor' along the direction 'dir' only (nothing across it).</summary>
    private static Matrix4x4 Along(Vector3 dir, float factor)
    {
        var m = Matrix4x4.identity;
        if (dir.sqrMagnitude < 1e-8f || Mathf.Abs(factor - 1f) < 1e-4f) return m;
        var d = dir.normalized;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                m[r, c] = (r == c ? 1f : 0f) + (factor - 1f) * d[r] * d[c];
        return m;
    }

    /// <summary>
    /// The skin weights of the agent's body near a point (its rest pose), from the few nearest body vertices
    /// of the same part and side, mixed by nearness (a grid of 5 cm cells to find them quickly).
    /// </summary>
    private class BodySampler
    {
        private const float Cell = 0.05f;
        private readonly Outfit.Body body;
        private readonly Vector3 middle;
        private readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();

        public BodySampler(Outfit.Body body, Vector3 middle)
        {
            this.body = body;
            this.middle = middle;
            for (int i = 0; i < body.Points.Length; i++)
            {
                var c = CellOf(body.Points[i]);
                if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
                list.Add(i);
            }
        }

        private static Vector3Int CellOf(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

        private bool Allowed(int i, char part, float side)
        {
            var region = body.Regions[i];
            float dx = body.Points[i].x - middle.x;
            bool sameSide = Mathf.Abs(dx) < 0.03f || Mathf.Sign(dx) == side;
            switch (part)
            {
                case 'H': return region == Outfit.Region.Head || region == Outfit.Region.Torso;
                case 'A': return sameSide && (region == Outfit.Region.Arms || region == Outfit.Region.Hands || region == Outfit.Region.Torso);
                case 'L': return sameSide && (region == Outfit.Region.Legs || region == Outfit.Region.Feet);
                default: return region == Outfit.Region.Torso || region == Outfit.Region.Legs || region == Outfit.Region.Head;
            }
        }

        public BoneWeight? Weights(Vector3 p, char part, float side)
        {
            var c = CellOf(p);
            var found = new List<(int index, float distance)>();
            int reach = 1;
            for (int ring = 1; ring <= 8; ring++)
            {
                found.Clear();
                for (int x = -ring; x <= ring; x++)
                    for (int y = -ring; y <= ring; y++)
                        for (int z = -ring; z <= ring; z++)
                        {
                            if (!grid.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out var list)) continue;
                            foreach (int i in list)
                                if (Allowed(i, part, side)) found.Add((i, (body.Points[i] - p).magnitude));
                        }
                // enough found, and the nearest well inside the searched cube
                if (found.Count >= 4 && found.Min(f => f.distance) < (ring - 0.5f) * Cell) { reach = ring; break; }
            }
            if (found.Count == 0) return null;
            var nearest = found.OrderBy(f => f.distance).Take(6).ToList();
            float limit = nearest[0].distance * 1.5f + 0.01f;
            var sum = new Dictionary<int, float>();
            void Add(int bone, float w) { if (w <= 0f) return; sum.TryGetValue(bone, out float had); sum[bone] = had + w; }
            foreach (var (index, distance) in nearest)
            {
                if (distance > limit) continue;
                float k = 1f / (distance * distance + 1e-4f);
                var bw = body.Weights[index];
                Add(bw.boneIndex0, bw.weight0 * k); Add(bw.boneIndex1, bw.weight1 * k);
                Add(bw.boneIndex2, bw.weight2 * k); Add(bw.boneIndex3, bw.weight3 * k);
            }
            return sum.Count == 0 ? (BoneWeight?)null : Outfit.Weight(sum.Select(kv => (kv.Key, kv.Value)).ToArray());
        }
    }

    private static float ToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return (p - (a + ab * t)).magnitude;
    }

    private static GameObject Build(Outfit.Body body, string key)
    {
        string file = Path.Combine(Folder, key + ".obj");
        if (!File.Exists(file)) { Debug.LogWarning("[Setup] character missing: " + file); return null; }
        var m = Load(file);
        var skeleton = Skeleton(key, m);
        if (skeleton == null) { Debug.LogWarning("[Setup] no Mixamo skeleton for the characters in " + Folder); return null; }

        // stand it where the body stands, as tall as the body (the skeleton with it)
        float bodyLow = body.Points.Min(p => p.y), bodyHigh = body.Points.Max(p => p.y);
        float low = m.P.Min(p => p.y), high = m.P.Max(p => p.y);
        var pelvis = body.At("pelvis", "spine_01");
        float s = (bodyHigh - bodyLow) / Mathf.Max(0.5f, high - low);
        Vector3 Place(Vector3 p) => new Vector3(pelvis.x + p.x * s, bodyLow + (p.y - low) * s, pelvis.z + p.z * s);
        for (int i = 0; i < m.P.Count; i++) m.P[i] = Place(m.P[i]);
        var joint = skeleton.ToDictionary(kv => kv.Key, kv => Place(kv.Value));

        // every agent bone bound where the same bone of the character is (the character's T-pose is turned
        // onto the agent's rest pose bone by bone), and its piece of the body for the weights
        var bindposes = (Matrix4x4[])body.Bindposes.Clone();
        var maps = new Dictionary<int, Matrix4x4>();   // agent bone: its rest pose onto the character's T-pose
        var pieces = new List<(int bone, Vector3 a, Vector3 b, float radius, float side, string limb)>();
        var missing = new List<string>();
        var lastTurn = new Dictionary<string, Quaternion>();   // per side: the turn of the bone before (for the ends)
        foreach (var (bone0, toward0, from0, to0, radius) in Bones)
        {
            foreach (string side in bone0.Contains("*") ? new[] { "l", "r" } : new[] { "" })
            {
                string Ours(string n) => n?.Replace("*", side);
                string name = Ours(bone0);
                int own = body.Bone(name);
                // no finger or neck bones on the agent: the hand / the head carry those parts of the character
                string standIn = name.StartsWith("neck") ? "head" : Outfit.Of(name) == Outfit.Region.Hands && !name.StartsWith("hand") ? Ours("hand_*") : null;
                int bone = own >= 0 ? own : standIn != null ? body.Bone(standIn) : -1;
                if (bone < 0) { missing.Add(name); continue; }
                float sign = side == "" ? 0f : Mathf.Sign(body.At(Ours("upperarm_*")).x - pelvis.x);
                // the Mixamo side on the same side of the body
                string mix = side == "" ? "" : joint.TryGetValue("LeftArm", out var la) && Mathf.Sign(la.x - pelvis.x) == sign ? "Left" : "Right";
                if (!joint.TryGetValue(from0.Replace("*", mix), out var a) || !joint.TryGetValue(to0.Replace("*", mix), out var b)) { missing.Add(from0.Replace("*", mix)); continue; }
                string limb = name.StartsWith("thigh") || name.StartsWith("calf") || name.StartsWith("foot") || name.StartsWith("ball") ? "leg"
                    : side != "" ? "arm" : "trunk";
                pieces.Add((bone, a, b, radius * s, sign, limb));
                if (own < 0) continue;   // only stands in: bound as the bone it stands in for
                var rest = body.At(name);
                int toward = toward0 == null ? -1 : body.Bone(Ours(toward0));
                // the agent's bone turned onto the character's and stretched to its length (the character's
                // knees, elbows and wrists come exactly onto the agent's: no stretched or thickened joints); an
                // end bone (head, finger tips, toes) turns as the one before it
                var restDir = toward >= 0 ? body.At(Ours(toward0)) - rest : Vector3.zero;
                var turn = toward >= 0 ? Quaternion.FromToRotation(restDir, b - a)
                    : lastTurn.TryGetValue(side, out var before) ? before : Quaternion.identity;
                lastTurn[side] = turn;
                float stretch = toward >= 0 && restDir.magnitude > 1e-3f ? Mathf.Clamp((b - a).magnitude / restDir.magnitude, 0.7f, 1.4f) : 1f;
                var map = Matrix4x4.Translate(a) * Matrix4x4.Rotate(turn) * Along(restDir, stretch) * Matrix4x4.Translate(-rest);
                maps[bone] = map;
                bindposes[bone] = (map * body.Bind(bone)).inverse * body.ToWorld;
            }
        }
        if (missing.Count > 0) Debug.Log($"[Setup] {key}: bones not mapped: {string.Join(", ", missing)}");
        // every other bone of the body (twist bones, extra spine or neck bones) moves as the nearest mapped
        // bone above it, so weights taken from the body on them keep the character in one piece
        var bones = body.Renderer.bones;
        var indexOf = new Dictionary<Transform, int>();
        for (int i = 0; i < bones.Length; i++) if (bones[i] != null && !indexOf.ContainsKey(bones[i])) indexOf[bones[i]] = i;
        int inherited = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null || maps.ContainsKey(i)) continue;
            for (var up = bones[i].parent; up != null; up = up.parent)
                if (indexOf.TryGetValue(up, out int above) && maps.TryGetValue(above, out var am))
                {
                    maps[i] = am;
                    bindposes[i] = (am * body.Bind(i)).inverse * body.ToWorld;
                    inherited++;
                    break;
                }
        }

        // the weights region by region, as a body is skinned by hand: the head rides on the head bone alone
        // (the face keeps its shape; only the throat is shared with the neck), the trunk on the spine by
        // its height (with the collar bones on top of the shoulders), each arm and hand on its own bones
        // by the distance to them (soft at the armpit, the elbow, the wrist and every finger joint), each
        // leg on its own (soft at the hip, the knee and the ankle); never a bone of the other side
        int Of(params string[] names) { foreach (var n in names) { int i = body.Bone(n); if (i >= 0) return i; } return -1; }
        var spine = new List<(int bone, float y)>();
        void Link(string mix, params string[] ours) { int b0 = Of(ours); if (b0 >= 0 && joint.TryGetValue(mix, out var j)) spine.Add((b0, j.y)); }
        Link("Hips", "pelvis", "spine_01"); Link("Spine", "spine_01"); Link("Spine1", "spine_02", "spine_01"); Link("Spine2", "spine_03", "spine_02");
        Link("Neck", "neck_01", "head");
        int headBone = Of("head");
        float neckY = joint.TryGetValue("Neck", out var nj) ? nj.y : spine[spine.Count - 1].y;
        float spine2Y = joint.TryGetValue("Spine2", out var s2) ? s2.y : neckY - 0.13f;
        (int bone, float weight)[] Trunk(float y)
        {
            if (y <= spine[0].y) return new[] { (spine[0].bone, 1f) };
            for (int k = 0; k + 1 < spine.Count; k++)
                if (y < spine[k + 1].y)
                {
                    float u = (y - spine[k].y) / Mathf.Max(0.01f, spine[k + 1].y - spine[k].y);
                    return new[] { (spine[k].bone, 1f - u), (spine[k + 1].bone, u) };
                }
            // the neck, then the head from just above it
            float h = Mathf.Clamp01((y - neckY) / 0.04f);
            return new[] { (spine[spine.Count - 1].bone, 1f - h), (headBone, h) };
        }
        (int bone, float weight)[] Near(Vector3 p, string limb, float side)
        {
            return pieces.Where(x => x.limb == limb && x.side == side)
                .Select(x => { float d = Mathf.Max(0f, ToSegment(p, x.a, x.b) - x.radius) + 0.02f; return (x.bone, 1f / (d * d * d * d)); })
                .OrderByDescending(x => x.Item2).Take(4).ToArray();
        }
        (int bone, float weight)[] Mix((int bone, float weight)[] a0, (int bone, float weight)[] b0, float t)
        {
            float sa = Mathf.Max(1e-6f, a0.Sum(x => x.weight)), sb = Mathf.Max(1e-6f, b0.Sum(x => x.weight));
            return a0.Select(x => (x.bone, x.weight / sa * (1f - t))).Concat(b0.Select(x => (x.bone, x.weight / sb * t))).ToArray();
        }
        // each side: where its arm and its leg start, its collar bone
        var sides = new Dictionary<float, (float armX, float armY, float legY, int clavicle)>();
        foreach (string side in new[] { "l", "r" })
        {
            float sign = Mathf.Sign(body.At("upperarm_" + side).x - pelvis.x);
            string mix = joint.TryGetValue("LeftArm", out var la) && Mathf.Sign(la.x - pelvis.x) == sign ? "Left" : "Right";
            if (!joint.TryGetValue(mix + "Arm", out var arm) || !joint.TryGetValue(mix + "UpLeg", out var leg)) continue;
            sides[sign] = (Mathf.Abs(arm.x - pelvis.x), arm.y, leg.y, Of("clavicle_" + side));
        }
        // first a rough skin by regions: enough to find where each vertex sits on the agent at rest
        var provisional = new (int bone, float weight)[m.P.Count][];
        var kind = new char[m.P.Count];      // H head, A arm, L leg, T trunk
        var legOf = new float[m.P.Count];    // the side of its arm or leg
        for (int i = 0; i < m.P.Count; i++)
        {
            var p = m.P[i];
            float dx = p.x - pelvis.x, ax = Mathf.Abs(dx), sign = dx < 0f ? -1f : 1f;
            (int bone, float weight)[] w;
            kind[i] = 'T';
            legOf[i] = sign;
            if (!sides.TryGetValue(sign, out var sd)) w = Trunk(p.y);
            else if (p.y >= neckY + 0.02f && ax < 0.16f) { w = Trunk(p.y); kind[i] = 'H'; }
            else if (ax > sd.armX + 0.1f || ax > sd.armX - 0.03f && Mathf.Abs(p.y - sd.armY) < 0.11f)
            {
                kind[i] = 'A';
                // a wide soft shoulder: the top of the arm shares with the trunk (it does not jut out when running)
                float t = Mathf.Clamp01((sd.armX + 0.1f - ax) / 0.16f);
                w = Mix(Near(p, "arm", sign), Trunk(p.y), t * t * (3f - 2f * t));
            }
            else if (p.y < sd.legY + 0.02f)
            {
                // the leg it belongs to by distance (not by which side of the middle it is: the inner seam of
                // trousers made in one piece lies on the middle line down to the knees)
                float legSide = sides.Keys.OrderBy(k => pieces.Where(x => x.limb == "leg" && x.side == k).Select(x => ToSegment(p, x.a, x.b)).DefaultIfEmpty(9f).Min()).First();
                float t = Mathf.Clamp01((p.y - (sd.legY - 0.1f)) / 0.12f);
                w = Mix(Near(p, "leg", legSide), Trunk(p.y), t);
                kind[i] = 'L';
                legOf[i] = legSide;
            }
            else
            {
                w = Trunk(p.y);
                // the top of the shoulder goes with the collar bone
                float c = Mathf.Clamp01((ax - 0.08f) / Mathf.Max(0.05f, sd.armX - 0.08f)) * Mathf.Clamp01((p.y - spine2Y) / Mathf.Max(0.05f, neckY - spine2Y));
                if (sd.clavicle >= 0 && c > 0f) w = Mix(w, new[] { (sd.clavicle, 1f) }, c * 0.8f);
            }
            provisional[i] = w;
        }

        // then the real skin: each vertex taken back to where it sits on the agent at rest (through the bones
        // of its rough skin) and given the weights of the agent's own body there, as its artist skinned it:
        // the shoulders, hips, knees and every finger bend as the body itself does. Only body points of the
        // same part (head, arm, leg, trunk) and the same side are used: no thigh pulls the other leg, no
        // shoulder takes the chest along.
        var weights = new BoneWeight[m.P.Count];
        var sampler = new BodySampler(body, pelvis);
        int fromBody = 0;
        for (int i = 0; i < m.P.Count; i++)
        {
            var p = m.P[i];
            var rest = Vector3.zero;
            float total = 0f;
            foreach (var (bone, weight) in provisional[i])
            {
                if (weight <= 0f) continue;
                rest += (maps.TryGetValue(bone, out var mp) ? mp.inverse.MultiplyPoint3x4(p) : p) * weight;
                total += weight;
            }
            rest = total > 0f ? rest / total : p;
            var sampled = sampler.Weights(rest, kind[i], legOf[i]);
            if (sampled != null) { weights[i] = sampled.Value; fromBody++; }
            else weights[i] = Outfit.Weight(provisional[i]);
        }
        Debug.Log($"[Setup] character {key}: {fromBody} of {m.P.Count} vertices skinned from the body, {inherited} bones moving with the bone above");

        var g = new Outfit.Garment();
        for (int i = 0; i < m.P.Count; i++) g.Add(m.P[i], m.N[i], m.UV[i], weights[i]);
        for (int i = 0; i + 2 < m.T.Count; i += 3) g.Tri(m.T[i], m.T[i + 1], m.T[i + 2]);
        Texture2D T(string kind)
        {
            string path = $"{Textures}/{key}/Body_{kind}.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            // a Library imported with the older rules: the colour texture again with the finest blocks (ImportRules)
            if (texture != null && kind == "Albedo" && texture.format != TextureFormat.ASTC_4x4
                && EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return texture;
        }
        var mat = AssetSetup.Mat("Char_" + key, Color.white, 0.3f, 0f, T("Albedo"), T("Normal"));
        // hair, lashes and beards are cut out of the texture's alpha
        mat.SetFloat("_Mode", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        var go = Outfit.Wear(body, g, "Character_" + key, bindposes, mat);
        Debug.Log($"[Setup] character {key}: {m.P.Count} vertices, scale {s:0.00}, {pieces.Count} bones from the Mixamo skeleton");
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
