using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The tailor: cuts clothes for the character in its bind pose ("tailor space" = world space with the
/// skeleton at rest) and skins them to the character's own bones, so every garment bends with every
/// animation. Three kinds of pieces:
///  - shells: a body region pushed out, smoothed (so the cloth bridges hollows instead of hugging the
///    body) and made loose around the limbs (sleeves, trousers);
///  - drapes: hanging cloth from the waist (shirt tails, coats, robes) built ring by ring around the
///    body and the clothes under it, following the hips and, partly, the thighs;
///  - lathes and kit models: hats, helmets, vests, boots fitted to the measured body.
/// </summary>
public static class Outfit
{
    public enum Region { None, Head, Torso, Arms, Hands, Legs, Feet }

    private const string Folder = "Assets/Generated/Outfit";

    public static Region Of(string bone)
    {
        string b = bone.ToLowerInvariant();
        if (b.Contains("hand") || b.Contains("thumb") || b.Contains("index") || b.Contains("middle") || b.Contains("ring") || b.Contains("pinky")) return Region.Hands;
        if (b.Contains("upperarm") || b.Contains("lowerarm") || b.Contains("arm")) return Region.Arms;
        if (b.Contains("head") || b.Contains("neck")) return Region.Head;
        if (b.Contains("foot") || b.Contains("ball") || b.Contains("toe")) return Region.Feet;
        if (b.Contains("thigh") || b.Contains("calf") || b.Contains("pelvis") || b.Contains("leg")) return Region.Legs;
        if (b.Contains("spine") || b.Contains("clavicle") || b.Contains("chest")) return Region.Torso;
        return Region.None;
    }

    // ------------------------------------------------------------------ the body at rest

    /// <summary>The character's skinned body in tailor space.</summary>
    public class Body
    {
        public readonly SkinnedMeshRenderer Renderer;
        public readonly string Key;
        public readonly Matrix4x4 ToWorld, ToMesh;
        public readonly Vector3[] Points, Normals;
        public readonly BoneWeight[] Weights;
        public readonly Region[] Regions;
        public readonly int[] Triangles;
        public readonly Matrix4x4[] Bindposes;
        public readonly bool Mirrored;
        private readonly Dictionary<string, int> bones = new Dictionary<string, int>();

        public Body(SkinnedMeshRenderer renderer, string key)
        {
            Renderer = renderer;
            Key = key;
            var mesh = renderer.sharedMesh;
            ToWorld = renderer.transform.localToWorldMatrix;
            ToMesh = ToWorld.inverse;
            Mirrored = ToWorld.determinant < 0;
            var toWorld = ToWorld;
            Points = mesh.vertices.Select(v => toWorld.MultiplyPoint3x4(v)).ToArray();
            Normals = mesh.normals.Select(n => toWorld.MultiplyVector(n).normalized).ToArray();
            Weights = mesh.boneWeights;
            Triangles = mesh.triangles;
            Bindposes = mesh.bindposes;
            var b = renderer.bones;
            for (int i = 0; i < b.Length; i++)
                if (b[i] != null && !bones.ContainsKey(b[i].name.ToLowerInvariant())) bones[b[i].name.ToLowerInvariant()] = i;
            Regions = new Region[Weights.Length];
            for (int i = 0; i < Weights.Length; i++)
            {
                int best = Strongest(Weights[i]);
                Regions[i] = best >= 0 && best < b.Length && b[best] != null ? Of(b[best].name) : Region.None;
            }
        }

        public int Bone(string name) => bones.TryGetValue(name.ToLowerInvariant(), out int i) ? i : -1;
        public Transform BoneTransform(string name) { int i = Bone(name); return i >= 0 ? Renderer.bones[i] : null; }
        public Matrix4x4 Bind(int bone) => ToWorld * Bindposes[bone].inverse;
        /// <summary>Rest position of a bone (or of the nearest named fallback).</summary>
        public Vector3 At(string name, params string[] fallbacks)
        {
            int i = Bone(name);
            foreach (var f in fallbacks) if (i < 0) i = Bone(f);
            return i < 0 ? Vector3.zero : Bind(i).MultiplyPoint3x4(Vector3.zero);
        }

        public IEnumerable<Vector3> PointsIn(params Region[] regions)
        {
            var set = new HashSet<Region>(regions);
            for (int i = 0; i < Points.Length; i++)
                if (set.Contains(Regions[i])) yield return Points[i];
        }

        /// <summary>Weights of the body vertex nearest to p (optionally only in some regions).</summary>
        public BoneWeight Nearest(Vector3 p, params Region[] regions)
        {
            float best = float.MaxValue;
            int at = 0;
            for (int i = 0; i < Points.Length; i++)
            {
                if (regions.Length > 0 && Array.IndexOf(regions, Regions[i]) < 0) continue;
                float d = (Points[i] - p).sqrMagnitude;
                if (d < best) { best = d; at = i; }
            }
            return Weights[at];
        }
    }

    private static int Strongest(BoneWeight w)
    {
        int best = w.boneIndex0;
        float max = w.weight0;
        if (w.weight1 > max) { best = w.boneIndex1; max = w.weight1; }
        if (w.weight2 > max) { best = w.boneIndex2; max = w.weight2; }
        if (w.weight3 > max) best = w.boneIndex3;
        return best;
    }

    public static BoneWeight Weight(params (int bone, float weight)[] items)
    {
        var list = items.Where(i => i.bone >= 0 && i.weight > 0.001f).GroupBy(i => i.bone)
            .Select(g => (bone: g.Key, weight: g.Sum(x => x.weight))).OrderByDescending(i => i.weight).Take(4).ToList();
        float sum = list.Sum(i => i.weight);
        var w = new BoneWeight();
        if (list.Count == 0 || sum <= 0) return w;
        if (list.Count > 0) { w.boneIndex0 = list[0].bone; w.weight0 = list[0].weight / sum; }
        if (list.Count > 1) { w.boneIndex1 = list[1].bone; w.weight1 = list[1].weight / sum; }
        if (list.Count > 2) { w.boneIndex2 = list[2].bone; w.weight2 = list[2].weight / sum; }
        if (list.Count > 3) { w.boneIndex3 = list[3].bone; w.weight3 = list[3].weight / sum; }
        return w;
    }

    // ------------------------------------------------------------------ garments

    /// <summary>A piece in tailor space: vertices, normals, uvs, weights and triangles per material.</summary>
    public class Garment
    {
        public List<Vector3> P = new List<Vector3>(), N = new List<Vector3>();
        public List<Vector2> UV = new List<Vector2>();
        public List<BoneWeight> W = new List<BoneWeight>();
        public List<List<int>> T = new List<List<int>> { new List<int>() };
        /// <summary>Triangles wound for tailor space (built here) rather than copied from the body mesh.</summary>
        public bool Tailored = true;

        public int Add(Vector3 p, Vector3 n, Vector2 uv, BoneWeight w = default)
        {
            P.Add(p); N.Add(n); UV.Add(uv); W.Add(w);
            return P.Count - 1;
        }

        public void Tri(int a, int b, int c, int sub = 0)
        {
            while (T.Count <= sub) T.Add(new List<int>());
            T[sub].Add(a); T[sub].Add(b); T[sub].Add(c);
        }

        /// <summary>Two triangles for the quad a-b (top edge, left to right seen from outside) over c-d.</summary>
        public void Quad(int a, int b, int c, int d, int sub = 0) { Tri(a, b, c, sub); Tri(b, d, c, sub); }

        public void Append(Garment other, int sub = 0)
        {
            int start = P.Count;
            P.AddRange(other.P); N.AddRange(other.N); UV.AddRange(other.UV); W.AddRange(other.W);
            for (int s = 0; s < other.T.Count; s++)
                foreach (var i in other.T[s]) { while (T.Count <= sub + s) T.Add(new List<int>()); T[sub + s].Add(start + i); }
        }

        public void SetWeights(BoneWeight w) { for (int i = 0; i < W.Count; i++) W[i] = w; }
        public IEnumerable<Vector3> Points => P;
    }

    private static Mesh ToMesh(Garment g, string name, Matrix4x4 toLocal, bool mirrored, Matrix4x4[] bindposes)
    {
        var mesh = new Mesh { name = name };
        if (g.P.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(g.P.Select(p => toLocal.MultiplyPoint3x4(p)).ToList());
        mesh.SetNormals(g.N.Select(n => toLocal.MultiplyVector(n).normalized).ToList());
        mesh.SetUVs(0, g.UV);
        if (bindposes != null)
        {
            mesh.boneWeights = g.W.ToArray();
            mesh.bindposes = bindposes;
        }
        mesh.subMeshCount = g.T.Count;
        for (int s = 0; s < g.T.Count; s++)
        {
            var t = g.T[s];
            if (g.Tailored && mirrored)
            {
                t = new List<int>(t);
                for (int i = 0; i < t.Count; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }
            }
            mesh.SetTriangles(t, s);
        }
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Mesh Save(Mesh mesh, string key, string name)
    {
        AssetSetup.Folder(Folder);
        string path = $"{Folder}/{key}_{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>Put on a garment: a skinned mesh on the body's bones.</summary>
    public static GameObject Wear(Body body, Garment g, string name, params Material[] materials) => Wear(body, g, name, body.Bindposes, materials);

    /// <summary>The same with bind poses of its own (a model in another pose than the body's rest pose).</summary>
    public static GameObject Wear(Body body, Garment g, string name, Matrix4x4[] bindposes, params Material[] materials)
    {
        if (g == null || g.P.Count == 0) return null;
        var mesh = Save(ToMesh(g, $"{body.Key}_{name}", body.ToMesh, body.Mirrored, bindposes), body.Key, name);
        var src = body.Renderer;
        var go = new GameObject(name);
        go.transform.SetParent(src.transform.parent, false);
        go.transform.localPosition = src.transform.localPosition;
        go.transform.localRotation = src.transform.localRotation;
        go.transform.localScale = src.transform.localScale;
        var r = go.AddComponent<SkinnedMeshRenderer>();
        r.sharedMesh = mesh;
        r.bones = src.bones;
        r.rootBone = src.rootBone;
        r.sharedMaterials = Enumerable.Range(0, mesh.subMeshCount).Select(i => materials[Mathf.Min(i, materials.Length - 1)]).ToArray();
        var bounds = src.localBounds;
        bounds.Expand(0.5f / Mathf.Max(1e-5f, src.transform.lossyScale.y));
        r.localBounds = bounds;
        r.updateWhenOffscreen = false;
        r.quality = SkinQuality.Bone4;
        return go;
    }

    /// <summary>Put on a rigid piece (hat, helmet, vest): a plain mesh riding on one bone.</summary>
    public static GameObject Attach(Body body, Garment g, string bone, string name, params Material[] materials)
    {
        if (g == null || g.P.Count == 0) return null;
        int b = body.Bone(bone);
        if (b < 0) { Debug.LogWarning($"[Setup] no bone {bone} for {name}"); return null; }
        var toBone = body.Bind(b).inverse;
        var mesh = Save(ToMesh(g, $"{body.Key}_{name}", toBone, toBone.determinant < 0, null), body.Key, name);
        var go = new GameObject(name);
        go.transform.SetParent(body.Renderer.bones[b], false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = Enumerable.Range(0, mesh.subMeshCount).Select(i => materials[Mathf.Min(i, materials.Length - 1)]).ToArray();
        return go;
    }

    private static Vector2 BoxUV(Vector3 p, Vector3 n, float tile)
    {
        var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        if (a.x >= a.y && a.x >= a.z) return new Vector2(p.z, p.y) / tile;
        if (a.y >= a.z) return new Vector2(p.x, p.z) / tile;
        return new Vector2(p.x, p.y) / tile;
    }

    // ------------------------------------------------------------------ shells

    public class Cut
    {
        public Region[] Regions;
        public float Thickness = 0.01f;
        /// <summary>Smoothing passes: more = the cloth bridges more of the body's shape.</summary>
        public int Smooth;
        public float Tile = 0.3f;
        /// <summary>Optional filter on every corner of a triangle (tailor point, normal).</summary>
        public Func<Vector3, Vector3, bool> Keep;
        /// <summary>Smallest distance of the cloth from the bone (m): wide trousers and sleeves.</summary>
        public float Thigh, Calf, UpperArm, LowerArm;
        /// <summary>A triangle needs all three corners in the regions (default: any corner).</summary>
        public bool AllCorners;
    }

    private static readonly (string part, string child)[] Limbs = { ("thigh", "calf"), ("calf", "foot"), ("upperarm", "lowerarm"), ("lowerarm", "hand") };

    /// <summary>Limb axis of every bone (thigh, calf, upper and lower arm incl. twist bones).</summary>
    private static (int limb, Vector3 a, Vector3 b)?[] LimbAxes(Body body)
    {
        var axes = new (int, Vector3, Vector3)?[body.Renderer.bones.Length];
        for (int i = 0; i < axes.Length; i++)
        {
            var t = body.Renderer.bones[i];
            if (t == null) continue;
            string n = t.name.ToLowerInvariant();
            string side = n.EndsWith("_l") ? "l" : n.EndsWith("_r") ? "r" : null;
            if (side == null) continue;
            for (int l = 0; l < Limbs.Length; l++)
            {
                if (!n.StartsWith(Limbs[l].part)) continue;
                if (body.Bone(Limbs[l].part + "_" + side) < 0 || body.Bone(Limbs[l].child + "_" + side) < 0) continue;
                axes[i] = (l, body.At(Limbs[l].part + "_" + side), body.At(Limbs[l].child + "_" + side));
            }
        }
        return axes;
    }

    /// <summary>Distance from p to the segment a-b (slightly extended) and the direction away from it.</summary>
    private static float Radial(Vector3 p, Vector3 a, Vector3 b, out Vector3 dir)
    {
        var ab = b - a;
        float t = Mathf.Clamp(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude), -0.25f, 1.1f);
        var d = p - (a + ab * t);
        float r = d.magnitude;
        dir = r > 1e-6f ? d / r : Vector3.up;
        return r;
    }

    public static Garment Shell(Body body, Cut cut)
    {
        var regions = new HashSet<Region>(cut.Regions);
        var pts = body.Points;
        var nrm = body.Normals;
        var tris = new List<int>();
        var T = body.Triangles;
        for (int i = 0; i + 2 < T.Length; i += 3)
        {
            int a = T[i], b = T[i + 1], c = T[i + 2];
            int inside = (regions.Contains(body.Regions[a]) ? 1 : 0) + (regions.Contains(body.Regions[b]) ? 1 : 0) + (regions.Contains(body.Regions[c]) ? 1 : 0);
            if (inside == 0 || (cut.AllCorners && inside < 3)) continue;
            if (cut.Keep != null && !(cut.Keep(pts[a], nrm[a]) && cut.Keep(pts[b], nrm[b]) && cut.Keep(pts[c], nrm[c]))) continue;
            tris.Add(a); tris.Add(b); tris.Add(c);
        }
        if (tris.Count == 0) return null;

        // only the vertices this garment uses
        var map = new Dictionary<int, int>();
        var used = new List<int>();
        for (int i = 0; i < tris.Count; i++)
        {
            if (!map.TryGetValue(tris[i], out int index)) { index = used.Count; map[tris[i]] = index; used.Add(tris[i]); }
            tris[i] = index;
        }
        // welded copy (the body is split at its texture seams) so the cloth is shaped as one surface
        var weld = new int[used.Count];
        var ids = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < used.Count; i++)
        {
            var q = Vector3Int.RoundToInt(pts[used[i]] * 20000f);
            if (!ids.TryGetValue(q, out int w)) { w = ids.Count; ids[q] = w; }
            weld[i] = w;
        }
        int n = ids.Count;
        var basePos = new Vector3[n];
        var baseN = new Vector3[n];
        var rep = new int[n];
        for (int i = 0; i < used.Count; i++)
        {
            basePos[weld[i]] = pts[used[i]];
            baseN[weld[i]] += nrm[used[i]];
            rep[weld[i]] = used[i];
        }
        for (int w = 0; w < n; w++) baseN[w] = baseN[w].sqrMagnitude > 0 ? baseN[w].normalized : Vector3.up;
        var neighbours = new HashSet<int>[n];
        for (int w = 0; w < n; w++) neighbours[w] = new HashSet<int>();
        var edges = new Dictionary<long, int>();
        void Edge(int a, int b)
        {
            neighbours[a].Add(b); neighbours[b].Add(a);
            long k = (long)Mathf.Min(a, b) * n + Mathf.Max(a, b);
            edges[k] = edges.TryGetValue(k, out int c) ? c + 1 : 1;
        }
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = weld[tris[i]], b = weld[tris[i + 1]], c = weld[tris[i + 2]];
            Edge(a, b); Edge(b, c); Edge(c, a);
        }
        var pinned = new bool[n];
        foreach (var e in edges)
            if (e.Value == 1) { pinned[e.Key / n] = true; pinned[e.Key % n] = true; }

        float t = cut.Thickness;
        var p = new Vector3[n];
        for (int w = 0; w < n; w++) p[w] = basePos[w] + baseN[w] * t;
        var axes = LimbAxes(body);
        float[] radius = { cut.Thigh, cut.Calf, cut.UpperArm, cut.LowerArm };
        void Loosen()
        {
            if (radius.All(r => r <= 0)) return;
            for (int w = 0; w < n; w++)
            {
                var bw = body.Weights[rep[w]];
                var shift = Vector3.zero;
                void One(int bone, float weight)
                {
                    if (weight < 0.02f || bone < 0 || bone >= axes.Length || axes[bone] == null) return;
                    var ax = axes[bone].Value;
                    float want = radius[ax.limb];
                    if (want <= 0) return;
                    float r = Radial(p[w], ax.a, ax.b, out var dir);
                    if (r < want) shift += dir * (want - r) * weight;
                }
                One(bw.boneIndex0, bw.weight0); One(bw.boneIndex1, bw.weight1); One(bw.boneIndex2, bw.weight2); One(bw.boneIndex3, bw.weight3);
                p[w] += shift;
            }
        }
        Loosen();
        var next = new Vector3[n];
        for (int pass = 0; pass < cut.Smooth; pass++)
        {
            for (int w = 0; w < n; w++)
            {
                if (pinned[w] || neighbours[w].Count == 0) { next[w] = p[w]; continue; }
                var sum = Vector3.zero;
                foreach (var o in neighbours[w]) sum += p[o];
                next[w] = Vector3.Lerp(p[w], sum / neighbours[w].Count, 0.6f);
            }
            Array.Copy(next, p, n);
            // never inside the body, and as loose as the cut says
            for (int w = 0; w < n; w++)
            {
                float d = Vector3.Dot(p[w] - basePos[w], baseN[w]);
                if (d < t) p[w] += baseN[w] * (t - d);
            }
            Loosen();
        }

        // normals of the shaped cloth (sign checked against the body's)
        var shaped = new Vector3[n];
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = weld[tris[i]], b = weld[tris[i + 1]], c = weld[tris[i + 2]];
            var fn = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
            shaped[a] += fn; shaped[b] += fn; shaped[c] += fn;
        }
        float agree = 0;
        for (int w = 0; w < n; w++) agree += Vector3.Dot(shaped[w], baseN[w]);
        for (int w = 0; w < n; w++)
        {
            var v = agree < 0 ? -shaped[w] : shaped[w];
            shaped[w] = v.sqrMagnitude > 1e-12f ? v.normalized : baseN[w];
        }

        var g = new Garment { Tailored = false };
        for (int i = 0; i < used.Count; i++)
        {
            int w = weld[i];
            g.Add(p[w], shaped[w], BoxUV(basePos[w], baseN[w], cut.Tile), body.Weights[used[i]]);
        }
        g.T[0].AddRange(tris);
        return g;
    }

    /// <summary>
    /// A new piece from part of a finished garment, lifted off it along its normals (arm bands, collars,
    /// trims): keeps the triangles whose corners all pass the filter.
    /// </summary>
    public static Garment Derive(Garment src, Func<Vector3, bool> keep, float lift, float tile = 0.2f)
    {
        var g = new Garment { Tailored = src.Tailored };
        var map = new Dictionary<int, int>();
        foreach (var t in src.T)
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                if (!keep(src.P[t[i]]) || !keep(src.P[t[i + 1]]) || !keep(src.P[t[i + 2]])) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = t[i + k];
                    if (!map.TryGetValue(v, out int index))
                    {
                        index = g.Add(src.P[v] + src.N[v] * lift, src.N[v], BoxUV(src.P[v], src.N[v], tile), src.W[v]);
                        map[v] = index;
                    }
                    g.T[0].Add(index);
                }
            }
        return g.P.Count > 0 ? g : null;
    }

    // ------------------------------------------------------------------ drapes

    public class Drape
    {
        /// <summary>Waist line (top ring) and hem height in tailor space.</summary>
        public float Top, Hem;
        /// <summary>Extra room at the hips and extra width at the hem (m).</summary>
        public float Ease = 0.015f, Flare = 0.03f;
        /// <summary>Depth of the hanging folds at the hem (m).</summary>
        public float Folds = 0.01f;
        public bool FrontOpen, SideSlits;
        /// <summary>Slits and the front opening start this far below the top.</summary>
        public float OpenFrom = 0.08f;
        public float Tile = 0.3f;
        /// <summary>How much of the thigh swing the hem follows (0 = only the hips).</summary>
        public float Follow = 0.85f;
    }

    private const int Segments = 32;
    private static float Angle(int k) => (k + 0.5f) / Segments * Mathf.PI * 2f;

    /// <summary>Distance of the outermost point in each of Segments directions around (cx, cz), for points near height y.</summary>
    public static float[] Support(IEnumerable<Vector3> points, float y, float band, float cx, float cz)
    {
        var h = new float[Segments];
        bool any = false;
        var cos = new float[Segments];
        var sin = new float[Segments];
        for (int k = 0; k < Segments; k++) { cos[k] = Mathf.Cos(Angle(k)); sin[k] = Mathf.Sin(Angle(k)); }
        foreach (var q in points)
        {
            if (Mathf.Abs(q.y - y) > band) continue;
            any = true;
            float dx = q.x - cx, dz = q.z - cz;
            for (int k = 0; k < Segments; k++)
            {
                float proj = dx * cos[k] + dz * sin[k];
                if (proj > h[k]) h[k] = proj;
            }
        }
        if (!any) return null;
        // round off: never less than the neighbours' average (no dents between far points)
        for (int pass = 0; pass < 2; pass++)
        {
            var s = (float[])h.Clone();
            for (int k = 0; k < Segments; k++)
                h[k] = Mathf.Max(s[k], (s[(k + Segments - 1) % Segments] + 2 * s[k] + s[(k + 1) % Segments]) / 4f);
        }
        return h;
    }

    /// <summary>Cloth hanging from the waist around the body and the clothes under it.</summary>
    public static Garment Robe(Body body, Drape d, List<Vector3> under, int seed)
    {
        var hip = body.At("pelvis");
        var knee = (body.At("calf_l") + body.At("calf_r")) / 2f;
        int pelvis = body.Bone("pelvis"), spine = body.Bone("spine_01"), thighL = body.Bone("thigh_l"), thighR = body.Bone("thigh_r");
        int calfL = body.Bone("calf_l"), calfR = body.Bone("calf_r");
        bool rightPlusX = body.At("thigh_r").x > hip.x;
        float cx = hip.x, cz = hip.z;
        int rings = Mathf.Max(3, Mathf.CeilToInt((d.Top - d.Hem) / 0.04f) + 1);
        var radii = new float[rings][];
        float[] last = null;
        var rnd = new System.Random(seed);
        var phase = new float[3];
        for (int i = 0; i < 3; i++) phase[i] = (float)rnd.NextDouble() * 6.28f;
        for (int i = 0; i < rings; i++)
        {
            float y = Mathf.Lerp(d.Top, d.Hem, i / (rings - 1f));
            var h = Support(under, y, 0.025f, cx, cz) ?? (last != null ? (float[])last.Clone() : Enumerable.Repeat(0.18f, Segments).ToArray());
            float depth = i / (rings - 1f);
            float below = Mathf.Clamp01((hip.y - y) / Mathf.Max(0.1f, hip.y - knee.y));
            var r = new float[Segments];
            for (int k = 0; k < Segments; k++)
            {
                float a = Angle(k);
                // hanging folds: deeper towards the hem, a little irregular
                float fold = d.Folds * depth * (0.6f * Mathf.Sin(a * 7f + phase[0]) + 0.3f * Mathf.Sin(a * 11f + phase[1]) + 0.25f * Mathf.Sin(a * 4f + phase[2]));
                r[k] = h[k] + 0.006f + d.Ease * Mathf.Clamp01(depth * 4f) + d.Flare * below * below + Mathf.Max(0, fold + d.Folds * depth * 0.5f);
                if (last != null && y < hip.y) r[k] = Mathf.Max(r[k], radii[i - 1][k] - 0.002f + d.Flare * 0.02f);
            }
            radii[i] = r;
            last = h;
        }

        var g = new Garment();
        var outer = new int[rings, Segments + 1];
        var inner = new int[rings, Segments + 1];
        float circumference = 2f * Mathf.PI * radii[rings / 2].Average();
        for (int i = 0; i < rings; i++)
        {
            float y = Mathf.Lerp(d.Top, d.Hem, i / (rings - 1f));
            // the cloth follows the thighs from the top of the legs on (else the legs show through
            // when a leg goes forward), below the knee partly the shins too
            float below = Mathf.Clamp01((hip.y - 0.02f - y) / Mathf.Max(0.1f, (hip.y - knee.y) * 0.55f));
            float follow = d.Follow * Mathf.SmoothStep(0, 1, below);
            float shin = 0.45f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(knee.y + 0.02f, knee.y - 0.3f, y));
            float up = y > hip.y ? Mathf.Clamp01((y - hip.y) / Mathf.Max(0.01f, d.Top - hip.y)) * 0.5f : 0f;
            for (int k = 0; k <= Segments; k++)
            {
                float a = Angle(k % Segments);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float r = radii[i][k % Segments];
                float side = Mathf.Clamp(Mathf.Cos(a) * 1.5f, -1f, 1f) * (rightPlusX ? 1 : -1);
                float right = 0.5f + 0.5f * side;
                var w = Weight((pelvis, (1 - follow) * (1 - up)), (spine, (1 - follow) * up),
                    (thighR, follow * right * (1 - shin)), (thighL, follow * (1 - right) * (1 - shin)),
                    (calfR, follow * right * shin), (calfL, follow * (1 - right) * shin));
                var uv = new Vector2(k / (float)Segments * circumference / d.Tile, y / d.Tile);
                var c = new Vector3(cx, y, cz);
                outer[i, k] = g.Add(c + dir * r, dir, uv, w);
                inner[i, k] = g.Add(c + dir * (r - 0.005f), -dir, uv, w);
            }
        }
        float hipY = hip.y;
        for (int i = 0; i + 1 < rings; i++)
        {
            float y = Mathf.Lerp(d.Top, d.Hem, (i + 1) / (rings - 1f));
            bool open = d.Top - y > d.OpenFrom;
            for (int k = 0; k < Segments; k++)
            {
                // front opening between the two columns either side of +Z, slits at +X and -X
                if (open && d.FrontOpen && k == Segments / 4 - 1) continue;
                if (open && d.SideSlits && (k == Segments - 1 || k == Segments / 2 - 1)) continue;
                g.Quad(outer[i, k], outer[i, k + 1], outer[i + 1, k], outer[i + 1, k + 1]);
                g.Quad(inner[i, k + 1], inner[i, k], inner[i + 1, k + 1], inner[i + 1, k]);
            }
        }
        // the folded hem: a strip joining outside and inside at the bottom
        int lastRing = rings - 1;
        for (int k = 0; k < Segments; k++)
        {
            if (d.FrontOpen && k == Segments / 4 - 1) continue;
            if (d.SideSlits && (k == Segments - 1 || k == Segments / 2 - 1)) continue;
            g.Quad(outer[lastRing, k], outer[lastRing, k + 1], inner[lastRing, k], inner[lastRing, k + 1]);
        }
        return g;
    }

    // ------------------------------------------------------------------ lathes (hats)

    /// <summary>
    /// A surface of revolution around (cx, cz): one ring per entry with a radius per direction; the last
    /// ring is closed with a cap when close is set.
    /// </summary>
    public static Garment Lathe(List<(float y, float[] r)> rings, float cx, float cz, float tile, bool close, Func<int, int, bool> skip = null, bool doubleSided = false)
    {
        var g = new Garment();
        var ids = new int[rings.Count, Segments + 1];
        var back = new int[rings.Count, Segments + 1];
        float vAcc = 0;
        for (int i = 0; i < rings.Count; i++)
        {
            if (i > 0) vAcc += Mathf.Abs(rings[i].y - rings[i - 1].y) + Mathf.Abs(rings[i].r.Average() - rings[i - 1].r.Average());
            for (int k = 0; k <= Segments; k++)
            {
                float a = Angle(k % Segments);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float r = rings[i].r[k % Segments];
                // normal from the profile slope
                int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(rings.Count - 1, i + 1);
                float dr = rings[i1].r[k % Segments] - rings[i0].r[k % Segments], dy = rings[i1].y - rings[i0].y;
                var tangent = new Vector3(0, dy, 0) + dir * dr;
                var n = (dir * Mathf.Abs(dy) - Vector3.up * Mathf.Sign(dy) * dr).normalized;
                if (tangent.sqrMagnitude < 1e-10f || n.sqrMagnitude < 0.5f) n = dir;
                var p = new Vector3(cx, rings[i].y, cz) + dir * r;
                var uv = new Vector2(k / (float)Segments * 2f * Mathf.PI * Mathf.Max(0.05f, r) / tile, vAcc / tile);
                ids[i, k] = g.Add(p, n, uv);
                if (doubleSided) back[i, k] = g.Add(p - n * 0.003f, -n, uv);
            }
        }
        for (int i = 0; i + 1 < rings.Count; i++)
        {
            bool down = rings[i + 1].y < rings[i].y;
            for (int k = 0; k < Segments; k++)
            {
                if (skip != null && skip(i, k)) continue;
                // outside faces: going down the rings and around +angle (see Robe)
                if (down)
                {
                    g.Quad(ids[i, k], ids[i, k + 1], ids[i + 1, k], ids[i + 1, k + 1]);
                    if (doubleSided) g.Quad(back[i, k + 1], back[i, k], back[i + 1, k + 1], back[i + 1, k]);
                }
                else
                {
                    g.Quad(ids[i + 1, k], ids[i + 1, k + 1], ids[i, k], ids[i, k + 1]);
                    if (doubleSided) g.Quad(back[i + 1, k + 1], back[i + 1, k], back[i, k + 1], back[i, k]);
                }
            }
        }
        if (close)
        {
            int l = rings.Count - 1;
            int c = g.Add(new Vector3(cx, rings[l].y, cz), Vector3.up, Vector2.zero);
            for (int k = 0; k < Segments; k++) g.Tri(c, ids[l, k + 1], ids[l, k]);
        }
        return g;
    }

    /// <summary>A ring of tube (cords, rolled brims) around (center) with radii rx, rz.</summary>
    public static Garment Torus(Vector3 center, float[] r, float tube, int sides = 8)
    {
        var g = new Garment();
        var ids = new int[Segments + 1, sides + 1];
        for (int k = 0; k <= Segments; k++)
        {
            float a = Angle(k % Segments);
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var c = center + dir * r[k % Segments];
            for (int s = 0; s <= sides; s++)
            {
                float b = s / (float)sides * Mathf.PI * 2f;
                var n = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                ids[k, s] = g.Add(c + n * tube, n, new Vector2(k / (float)Segments * 8f, s / (float)sides));
            }
        }
        for (int k = 0; k < Segments; k++)
            for (int s = 0; s < sides; s++)
                g.Quad(ids[k, s + 1], ids[k + 1, s + 1], ids[k, s], ids[k + 1, s]);
        return g;
    }

    /// <summary>A flat strip along a path (ties, straps, tails): width per point, facing out along normals.</summary>
    public static Garment Strip(IList<Vector3> path, IList<float> width, IList<Vector3> normal, float thickness, float tile = 0.1f)
    {
        var g = new Garment();
        int n = path.Count;
        var ids = new int[n, 4];
        float v = 0;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) v += (path[i] - path[i - 1]).magnitude;
            var along = (path[Mathf.Min(n - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized;
            var side = Vector3.Cross(normal[i], along).normalized * width[i] / 2f;
            var front = normal[i] * thickness;
            ids[i, 0] = g.Add(path[i] - side + front, normal[i], new Vector2(0, v / tile));
            ids[i, 1] = g.Add(path[i] + side + front, normal[i], new Vector2(width[i] / tile, v / tile));
            ids[i, 2] = g.Add(path[i] - side, -normal[i], new Vector2(0, v / tile));
            ids[i, 3] = g.Add(path[i] + side, -normal[i], new Vector2(width[i] / tile, v / tile));
        }
        for (int i = 0; i + 1 < n; i++)
        {
            Face(g, ids[i, 0], ids[i, 1], ids[i + 1, 0], ids[i + 1, 1], normal[i]);
            Face(g, ids[i, 3], ids[i, 2], ids[i + 1, 3], ids[i + 1, 2], -normal[i]);
        }
        return g;
    }

    /// <summary>Quad a b / c d wound so that it faces along the given direction.</summary>
    public static void Face(Garment g, int a, int b, int c, int d, Vector3 facing, int sub = 0)
    {
        var n = Vector3.Cross(g.P[b] - g.P[a], g.P[c] - g.P[a]);
        // Unity's front faces are clockwise: the cross product of (b - a, c - a) points at the viewer
        if (Vector3.Dot(n, facing) > 0) g.Quad(a, b, c, d, sub);
        else g.Quad(b, a, d, c, sub);
    }

    // ------------------------------------------------------------------ kit models (OBJ)

    /// <summary>
    /// A model of the military kit (UnityGame/Models/MilitaryKit, made by Tools/make_military_kit.py):
    /// one sub mesh per material, with the colour and shine of its MTL entry.
    /// </summary>
    public class KitModel
    {
        public Garment Mesh = new Garment();
        public List<(string name, Color color, float smoothness)> Materials = new List<(string, Color, float)>();
        public Bounds Bounds;
    }

    public static string KitFolder => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Models", "MilitaryKit"));

    public static KitModel LoadKit(string item)
    {
        string obj = System.IO.Path.Combine(KitFolder, item + ".obj"), mtl = System.IO.Path.Combine(KitFolder, item + ".mtl");
        if (!System.IO.File.Exists(obj)) { Debug.LogWarning("[Setup] kit model missing: " + obj); return null; }
        var colors = new Dictionary<string, (Color, float)>();
        string current = null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (System.IO.File.Exists(mtl))
            foreach (var line in System.IO.File.ReadAllLines(mtl))
            {
                var t = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                if (t[0] == "newmtl") { current = t[1]; colors[current] = (Color.gray, 0.2f); }
                else if (t[0] == "Kd" && current != null) colors[current] = (new Color(float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv)), colors[current].Item2);
                else if (t[0] == "Ns" && current != null) colors[current] = (colors[current].Item1, Mathf.Clamp01(float.Parse(t[1], inv) / 200f) * 0.6f);
            }
        var kit = new KitModel();
        var verts = new List<Vector3>();
        int sub = -1;
        foreach (var line in System.IO.File.ReadAllLines(obj))
        {
            var t = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            if (t[0] == "v") verts.Add(new Vector3(float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv)));
            else if (t[0] == "usemtl")
            {
                var c = colors.TryGetValue(t[1], out var found) ? found : (Color.gray, 0.2f);
                kit.Materials.Add((t[1], c.Item1, c.Item2));
                sub = kit.Materials.Count - 1;
            }
            else if (t[0] == "f" && sub >= 0)
            {
                // the file already holds Unity's left-handed space (x mirrored from the glTF, corners
                // reversed), so the corners are used in their order
                kit.Mesh.Tri(int.Parse(t[1].Split('/')[0]) - 1, int.Parse(t[2].Split('/')[0]) - 1, int.Parse(t[3].Split('/')[0]) - 1, sub);
            }
        }
        foreach (var v in verts) kit.Mesh.Add(v, Vector3.up, new Vector2(v.x + v.z, v.y) * 4f);
        // smooth normals from the faces
        var normals = new Vector3[verts.Count];
        foreach (var t in kit.Mesh.T)
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                var fn = Vector3.Cross(verts[t[i + 1]] - verts[t[i]], verts[t[i + 2]] - verts[t[i]]);
                normals[t[i]] += fn; normals[t[i + 1]] += fn; normals[t[i + 2]] += fn;
            }
        for (int i = 0; i < verts.Count; i++) kit.Mesh.N[i] = normals[i].sqrMagnitude > 0 ? normals[i].normalized : Vector3.up;
        kit.Bounds = new Bounds(verts[0], Vector3.zero);
        foreach (var v in verts) kit.Bounds.Encapsulate(v);
        return kit;
    }

    /// <summary>A copy of a kit model moved into place: p' = to + scale * (rotation * (p - from)).</summary>
    public static Garment Place(KitModel kit, Vector3 from, Vector3 to, Vector3 scale, float yaw = 0, bool mirrorX = false)
    {
        var g = new Garment();
        var rot = Quaternion.Euler(0, yaw, 0);
        var mirror = mirrorX ? new Vector3(-1, 1, 1) : Vector3.one;
        var s = Vector3.Scale(scale, mirror);
        for (int i = 0; i < kit.Mesh.P.Count; i++)
        {
            var p = Vector3.Scale(rot * (kit.Mesh.P[i] - from), s) + to;
            // normals of a scaled shape: divide by the scale
            var n = rot * kit.Mesh.N[i];
            n = new Vector3(n.x / s.x, n.y / s.y, n.z / s.z).normalized;
            g.Add(p, n, kit.Mesh.UV[i]);
        }
        g.T = kit.Mesh.T.Select(t =>
        {
            var copy = new List<int>(t);
            if (mirrorX)
                for (int i = 0; i + 2 < copy.Count; i += 3) { int x = copy[i + 1]; copy[i + 1] = copy[i + 2]; copy[i + 2] = x; }
            return copy;
        }).ToList();
        return g;
    }

    // ------------------------------------------------------------------ measuring

    /// <summary>Bounds of the head (vertices skinned mostly to the head bone) in tailor space.</summary>
    public static Bounds HeadBounds(Body body)
    {
        int head = body.Bone("head");
        Bounds? result = null;
        for (int i = 0; i < body.Points.Length; i++)
        {
            var w = body.Weights[i];
            float onHead = (w.boneIndex0 == head ? w.weight0 : 0) + (w.boneIndex1 == head ? w.weight1 : 0) + (w.boneIndex2 == head ? w.weight2 : 0) + (w.boneIndex3 == head ? w.weight3 : 0);
            if (onHead < 0.6f) continue;
            if (result == null) result = new Bounds(body.Points[i], Vector3.zero);
            else { var b = result.Value; b.Encapsulate(body.Points[i]); result = b; }
        }
        return result ?? new Bounds(body.At("head") + new Vector3(0, 0.1f, 0.02f), new Vector3(0.16f, 0.24f, 0.2f));
    }

    public static List<Vector3> HeadPoints(Body body)
    {
        int head = body.Bone("head");
        var list = new List<Vector3>();
        for (int i = 0; i < body.Points.Length; i++)
        {
            var w = body.Weights[i];
            float onHead = (w.boneIndex0 == head ? w.weight0 : 0) + (w.boneIndex1 == head ? w.weight1 : 0) + (w.boneIndex2 == head ? w.weight2 : 0) + (w.boneIndex3 == head ? w.weight3 : 0);
            if (onHead >= 0.5f) list.Add(body.Points[i]);
        }
        return list;
    }

    // ------------------------------------------------------------------ cloth textures

    /// <summary>A generated texture (tileable), saved once per name.</summary>
    public static Texture2D Texture(string name, int size, Func<float, float, Color> pixel, bool normalMap = false)
    {
        string path = $"{AssetSetup.Generated}/Textures/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                data[y * size + x] = pixel(x / (float)size, y / (float)size);
        tex.SetPixels(data);
        tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 2;
        importer.maxTextureSize = size;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Normal map from a tileable height function (u, v in 0..1).</summary>
    public static Texture2D NormalTexture(string name, int size, Func<float, float, float> height, float strength)
    {
        float e = 1f / size;
        return Texture(name, size, (u, v) =>
        {
            float dx = (height((u + e) % 1f, v) - height((u - e + 1f) % 1f, v)) * strength;
            float dy = (height(u, (v + e) % 1f) - height(u, (v - e + 1f) % 1f)) * strength;
            var n = new Vector3(-dx, -dy, 1f).normalized;
            return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }, true);
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }
    }

    /// <summary>Smooth tileable value noise with the given number of cells per tile (across, down).</summary>
    public static float Noise(float u, float v, int cells, int cellsY = 0)
    {
        if (cellsY <= 0) cellsY = cells;
        float x = u * cells, y = v * cellsY;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float H(int a, int b) => Hash(((a % cells) + cells) % cells, ((b % cellsY) + cellsY) % cellsY);
        return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx), fy);
    }
}
