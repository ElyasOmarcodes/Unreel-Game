using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Simple meshes with world-scaled UVs (boxes, gable roofs, cylinders), so tiling PBR textures keep
/// their real size on objects of any dimension. Meshes are cached and saved as assets.
/// </summary>
public static class MeshGen
{
    private const string Folder = "Assets/Generated/Meshes";
    private static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

    public static void Reset()
    {
        cache.Clear();
        AssetDatabase.DeleteAsset(Folder);
        AssetSetup.Folder(Folder);
    }

    private static Mesh Save(string key, Mesh mesh)
    {
        mesh.name = key;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        AssetDatabase.CreateAsset(mesh, $"{Folder}/{key}.asset");
        cache[key] = mesh;
        return mesh;
    }

    private static string K(float v) => Mathf.RoundToInt(v * 100).ToString();

    /// <summary>Box centered at the origin; texScale = meters covered by one texture repeat.</summary>
    public static Mesh Box(Vector3 size, float texScale)
    {
        string key = $"box_{K(size.x)}_{K(size.y)}_{K(size.z)}_{K(texScale)}";
        if (cache.TryGetValue(key, out var cached)) return cached;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        var h = size / 2f;
        void Face(Vector3 normal, Vector3 u, Vector3 w, float du, float dw)
        {
            int i = v.Count;
            var c = Vector3.Scale(normal, h);
            var a = Vector3.Scale(u, h); var b = Vector3.Scale(w, h);
            v.Add(c - a - b); v.Add(c + a - b); v.Add(c + a + b); v.Add(c - a + b);
            for (int k = 0; k < 4; k++) n.Add(normal);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(du / texScale, 0)); uv.Add(new Vector2(du / texScale, dw / texScale)); uv.Add(new Vector2(0, dw / texScale));
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
        Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
        Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
        Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
        Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
        Face(Vector3.down, Vector3.left, Vector3.forward, size.x, size.z);
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        return Save(key, mesh);
    }

    /// <summary>Two roof slopes over a building of the given width (X) and length (Z), ridge along Z.</summary>
    public static Mesh GableRoof(float width, float length, float rise, float overhang, float texScale)
    {
        string key = $"roof_{K(width)}_{K(length)}_{K(rise)}_{K(texScale)}";
        if (cache.TryGetValue(key, out var cached)) return cached;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float hw = width / 2f + overhang, hl = length / 2f + overhang;
        float slope = Mathf.Sqrt(hw * hw + rise * rise);
        foreach (float side in new[] { -1f, 1f })
        {
            int i = v.Count;
            v.Add(new Vector3(side * hw, 0, -hl)); v.Add(new Vector3(0, rise, -hl)); v.Add(new Vector3(0, rise, hl)); v.Add(new Vector3(side * hw, 0, hl));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(slope / texScale, 0)); uv.Add(new Vector2(slope / texScale, 2 * hl / texScale)); uv.Add(new Vector2(0, 2 * hl / texScale));
            if (side > 0) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
        }
        // underside so the roof is not see-through from below
        int b = v.Count;
        v.Add(new Vector3(-hw, 0, -hl)); v.Add(new Vector3(hw, 0, -hl)); v.Add(new Vector3(hw, 0, hl)); v.Add(new Vector3(-hw, 0, hl));
        for (int k = 0; k < 4; k++) uv.Add(Vector2.zero);
        t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        return Save(key, mesh);
    }

    /// <summary>Triangle wall closing the end of a gable roof (in the XY plane, facing both ways).</summary>
    public static Mesh Gable(float width, float rise, float texScale)
    {
        string key = $"gable_{K(width)}_{K(rise)}_{K(texScale)}";
        if (cache.TryGetValue(key, out var cached)) return cached;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float hw = width / 2f;
        for (int s = 0; s < 2; s++)
        {
            int i = v.Count;
            float z = s == 0 ? -0.05f : 0.05f;
            v.Add(new Vector3(-hw, 0, z)); v.Add(new Vector3(hw, 0, z)); v.Add(new Vector3(0, rise, z));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(width / texScale, 0)); uv.Add(new Vector2(hw / texScale, rise / texScale));
            if (s == 0) { t.Add(i); t.Add(i + 2); t.Add(i + 1); } else { t.Add(i); t.Add(i + 1); t.Add(i + 2); }
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        return Save(key, mesh);
    }

    /// <summary>Closed cylinder along Y with UVs in meters around and up.</summary>
    public static Mesh Cylinder(float radius, float height, float texScale, int segments = 28)
    {
        string key = $"cyl_{K(radius)}_{K(height)}_{K(texScale)}_{segments}";
        if (cache.TryGetValue(key, out var cached)) return cached;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float circumference = 2 * Mathf.PI * radius;
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            v.Add(dir * radius); v.Add(dir * radius + Vector3.up * height);
            n.Add(dir); n.Add(dir);
            float u = i / (float)segments * circumference / texScale;
            uv.Add(new Vector2(u, 0)); uv.Add(new Vector2(u, height / texScale));
            if (i < segments)
            {
                int k = i * 2;
                t.Add(k); t.Add(k + 1); t.Add(k + 3); t.Add(k); t.Add(k + 3); t.Add(k + 2);
            }
        }
        foreach (float y in new[] { 0f, height })
        {
            int c = v.Count;
            v.Add(new Vector3(0, y, 0)); n.Add(y > 0 ? Vector3.up : Vector3.down); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2;
                v.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                n.Add(y > 0 ? Vector3.up : Vector3.down);
                uv.Add(new Vector2(Mathf.Cos(a) * radius / texScale, Mathf.Sin(a) * radius / texScale));
                if (i < segments)
                {
                    if (y > 0) { t.Add(c); t.Add(c + i + 2); t.Add(c + i + 1); }
                    else { t.Add(c); t.Add(c + i + 1); t.Add(c + i + 2); }
                }
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        return Save(key, mesh);
    }

    /// <summary>
    /// Box with rounded edges and corners (radius r), so walls, curbs and lawns do not look cut with a
    /// knife. UVs in meters projected along each face.
    /// </summary>
    public static Mesh Rounded(Vector3 size, float r, float texScale, int arc = 2)
    {
        r = Mathf.Min(r, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.45f);
        string key = $"rbox_{K(size.x)}_{K(size.y)}_{K(size.z)}_{K(r)}_{K(texScale)}_{arc}";
        if (cache.TryGetValue(key, out var cached)) return cached;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        var h = size / 2f;
        var inner = h - Vector3.one * r;
        // positions along one face axis: the edge arcs (sampled by angle) and the flat middle
        List<float> Axis(float half, float ir)
        {
            var list = new List<float>();
            for (int i = arc; i >= 1; i--) list.Add(-ir - r * Mathf.Tan(i / (float)arc * Mathf.PI / 4f));
            list.Add(-ir);
            list.Add(ir);
            for (int i = 1; i <= arc; i++) list.Add(ir + r * Mathf.Tan(i / (float)arc * Mathf.PI / 4f));
            return list;
        }
        void Face(Vector3 normal, Vector3 u, Vector3 w)
        {
            float hu = Vector3.Dot(Abs(u), h), hw = Vector3.Dot(Abs(w), h);
            float iu = Vector3.Dot(Abs(u), inner), iw = Vector3.Dot(Abs(w), inner);
            var us = Axis(hu, iu);
            var ws = Axis(hw, iw);
            int start = v.Count;
            var plane = Vector3.Scale(normal, h);
            foreach (var b in ws)
                foreach (var a in us)
                {
                    var p = plane + u * a + w * b;
                    var core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    var dir = (p - core).normalized;
                    var q = core + dir * r;
                    v.Add(q);
                    n.Add(dir);
                    uv.Add(new Vector2(Vector3.Dot(q, u), Vector3.Dot(q, w)) / texScale);
                }
            int cols = us.Count;
            for (int j = 0; j + 1 < ws.Count; j++)
                for (int i = 0; i + 1 < cols; i++)
                {
                    int a = start + j * cols + i, b = a + 1, c = a + cols, d = c + 1;
                    // same winding as Box: (c - a - b), (c + a - b), (c + a + b) seen from outside
                    t.Add(a); t.Add(d); t.Add(b); t.Add(a); t.Add(c); t.Add(d);
                }
        }
        Face(Vector3.forward, Vector3.left, Vector3.up);
        Face(Vector3.back, Vector3.right, Vector3.up);
        Face(Vector3.right, Vector3.forward, Vector3.up);
        Face(Vector3.left, Vector3.back, Vector3.up);
        Face(Vector3.up, Vector3.right, Vector3.forward);
        Face(Vector3.down, Vector3.left, Vector3.forward);
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        return Save(key, mesh);
    }

    private static Vector3 Abs(Vector3 a) => new Vector3(Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.z));

    public static GameObject RoundBox(string name, Vector3 center, Vector3 size, float radius, Material material, float texScale, Transform parent, float yaw = 0f, bool collider = true)
    {
        // small pieces (sandbags, curbs) need fewer steps around the edge
        var go = Object(name, Rounded(size, radius, texScale, size.magnitude < 1.2f ? 1 : 2), material, parent, center, yaw);
        if (collider) go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    /// <summary>
    /// A 20 ft shipping container (origin at the bottom centre, doors at +Z): corrugated walls, frame
    /// rails, corner posts and castings, door leaves with locking bars and hinges. Sub mesh 0 is the
    /// painted steel, 1 the dark hardware.
    /// </summary>
    public static Mesh Container()
    {
        const string key = "container20";
        if (cache.TryGetValue(key, out var cached)) return cached;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
        var paint = new List<int>(); var dark = new List<int>();
        const float W = 2.44f, H = 2.59f, L = 6.06f, tex = 1.6f;
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, List<int> t)
        {
            // a b on top, c d below: wound clockwise as seen from the side the normal points to
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), na) < 0) { (a, b) = (b, a); (c, d) = (d, c); (na, nb) = (nb, na); }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(na); n.Add(nb); n.Add(na); n.Add(nb);
            var axisU = (b - a).normalized;
            var axisV = (a - c).normalized;
            foreach (var p in new[] { a, b, c, d }) uv.Add(new Vector2(Vector3.Dot(p, axisU), Vector3.Dot(p, axisV)) / tex);
            t.Add(i); t.Add(i + 1); t.Add(i + 3); t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
        void Block(Vector3 c, Vector3 size, List<int> t)
        {
            var h = size / 2f;
            Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
            Quad(P(1, 1, -1), P(-1, 1, -1), P(1, -1, -1), P(-1, -1, -1), Vector3.back, Vector3.back, t);
            Quad(P(-1, 1, 1), P(1, 1, 1), P(-1, -1, 1), P(1, -1, 1), Vector3.forward, Vector3.forward, t);
            Quad(P(1, 1, 1), P(1, 1, -1), P(1, -1, 1), P(1, -1, -1), Vector3.right, Vector3.right, t);
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, -1), P(-1, -1, 1), Vector3.left, Vector3.left, t);
            Quad(P(-1, 1, -1), P(1, 1, -1), P(-1, 1, 1), P(1, 1, 1), Vector3.up, Vector3.up, t);
        }
        // corrugated wall: ribs along "along" (length), pushed in along -outward, from y0 to y1
        void Corrugated(Vector3 origin, Vector3 along, Vector3 outward, float length, float y0, float y1)
        {
            const float pitch = 0.278f, flat = 0.1f, slope = 0.039f, depth = 0.036f;
            var profile = new List<(float s, float d)>();
            for (float s = 0; s < length - 0.001f; s += pitch)
            {
                profile.Add((s, 0)); profile.Add((Mathf.Min(length, s + flat), 0));
                profile.Add((Mathf.Min(length, s + flat + slope), depth)); profile.Add((Mathf.Min(length, s + 2 * flat + slope), depth));
            }
            profile.Add((length, 0));
            for (int i = 0; i + 1 < profile.Count; i++)
            {
                var (s0, d0) = profile[i];
                var (s1, d1) = profile[i + 1];
                if (s1 - s0 < 0.001f) continue;
                var a = origin + along * s0 - outward * d0;
                var b = origin + along * s1 - outward * d1;
                var face = Vector3.Cross(Vector3.up, b - a).normalized;
                if (Vector3.Dot(face, outward) < 0) face = -face;
                var na = Vector3.Lerp(face, outward, 0.35f).normalized;
                Quad(a + Vector3.up * y1, b + Vector3.up * y1, a + Vector3.up * y0, b + Vector3.up * y0, na, na, paint);
            }
        }
        float hw = W / 2f, hl = L / 2f;
        const float post = 0.16f, rail = 0.16f;
        // long sides: ribs from the back post to the door post (the quads face +X / -X)
        Corrugated(new Vector3(hw - 0.01f, 0, hl - post), Vector3.back, Vector3.right, L - 2 * post, rail, H - 0.1f);
        Corrugated(new Vector3(-hw + 0.01f, 0, -hl + post), Vector3.forward, Vector3.left, L - 2 * post, rail, H - 0.1f);
        // front end (no doors), ribs across
        Corrugated(new Vector3(-hw + post, 0, -hl + 0.01f), Vector3.right, Vector3.back, W - 2 * post, rail, H - 0.1f);
        // roof panel and floor
        Quad(new Vector3(-hw, H - 0.04f, -hl), new Vector3(hw, H - 0.04f, -hl), new Vector3(-hw, H - 0.04f, hl), new Vector3(hw, H - 0.04f, hl), Vector3.up, Vector3.up, paint);
        // door end: two leaves with locking bars, cam handles and hinges
        float doorZ = hl - 0.03f;
        foreach (float side in new[] { -1f, 1f })
        {
            float x0 = side < 0 ? -hw + post : 0.005f, x1 = side < 0 ? -0.005f : hw - post;
            Quad(new Vector3(x0, H - 0.1f, doorZ), new Vector3(x1, H - 0.1f, doorZ), new Vector3(x0, rail, doorZ), new Vector3(x1, rail, doorZ), Vector3.forward, Vector3.forward, paint);
            // pressed panels on the door
            for (int k = 0; k < 3; k++)
            {
                float y0 = rail + 0.12f + k * 0.76f, y1 = y0 + 0.62f;
                Block(new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, doorZ + 0.008f), new Vector3((x1 - x0) * 0.8f, y1 - y0, 0.016f), paint);
            }
            foreach (float bx in new[] { 0.3f, 0.92f })
            {
                float x = side * bx;
                Block(new Vector3(x, (rail + H - 0.1f) / 2f, doorZ + 0.05f), new Vector3(0.035f, H - 0.1f - rail + 0.1f, 0.035f), dark);
                Block(new Vector3(x, H - 0.13f, doorZ + 0.04f), new Vector3(0.08f, 0.08f, 0.06f), dark);
                Block(new Vector3(x, rail + 0.03f, doorZ + 0.04f), new Vector3(0.08f, 0.08f, 0.06f), dark);
                Block(new Vector3(x + side * 0.09f, 1.2f, doorZ + 0.07f), new Vector3(0.2f, 0.04f, 0.03f), dark);
            }
            for (int k = 0; k < 4; k++)
                Block(new Vector3(side * (hw - post - 0.02f), rail + 0.3f + k * 0.62f, doorZ + 0.03f), new Vector3(0.07f, 0.12f, 0.06f), dark);
        }
        // frame: corner posts, top and bottom rails, corner castings
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
            {
                Block(new Vector3(x * (hw - post / 2f), H / 2f, z * (hl - post / 2f)), new Vector3(post, H, post), paint);
                foreach (float y in new[] { 0.09f, H - 0.09f })
                    Block(new Vector3(x * (hw - 0.09f), y, z * (hl - 0.09f)), new Vector3(0.19f, 0.18f, 0.19f), dark);
            }
        foreach (float x in new[] { -1f, 1f })
        {
            Block(new Vector3(x * (hw - 0.05f), rail / 2f, 0), new Vector3(0.1f, rail, L - 0.36f), paint);
            Block(new Vector3(x * (hw - 0.05f), H - 0.06f, 0), new Vector3(0.1f, 0.12f, L - 0.36f), paint);
        }
        foreach (float z in new[] { -1f, 1f })
        {
            Block(new Vector3(0, rail / 2f, z * (hl - 0.05f)), new Vector3(W - 0.36f, rail, 0.1f), paint);
            Block(new Vector3(0, H - 0.08f, z * (hl - 0.05f)), new Vector3(W - 0.36f, 0.16f, 0.1f), paint);
        }
        var mesh = new Mesh { subMeshCount = 2 };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
        mesh.SetTriangles(paint, 0); mesh.SetTriangles(dark, 1);
        return Save(key, mesh);
    }

    /// <summary>
    /// A ramp-shaped wedge: width along X, rising along +Z from 0 at the origin to height at length.
    /// Flat faces with UVs in meters.
    /// </summary>
    public static Mesh Wedge(float width, float length, float height, float texScale)
    {
        string key = $"wedge_{K(width)}_{K(length)}_{K(height)}_{K(texScale)}";
        if (cache.TryGetValue(key, out var cached)) return cached;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float hw = width / 2f;
        // a polygon facing 'outward': fan from the first corner, wound clockwise as seen from outside
        void Face(Vector3 outward, params Vector3[] corners)
        {
            int start = v.Count;
            var normal = outward.normalized;
            var u = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            var w = Vector3.Cross(normal, u);
            foreach (var c in corners) { v.Add(c); n.Add(normal); uv.Add(new Vector2(Vector3.Dot(c, u), Vector3.Dot(c, w)) / texScale); }
            for (int i = 1; i + 1 < corners.Length; i++)
            {
                var cross = Vector3.Cross(corners[i] - corners[0], corners[i + 1] - corners[0]);
                if (Vector3.Dot(cross, normal) > 0) { t.Add(start); t.Add(start + i); t.Add(start + i + 1); }
                else { t.Add(start); t.Add(start + i + 1); t.Add(start + i); }
            }
        }
        var a = new Vector3(-hw, 0, 0); var b = new Vector3(hw, 0, 0);
        var c0 = new Vector3(-hw, 0, length); var d = new Vector3(hw, 0, length);
        var e = new Vector3(-hw, height, length); var f = new Vector3(hw, height, length);
        Face(Vector3.down, a, b, d, c0);
        Face(Vector3.forward, c0, d, f, e);
        Face(new Vector3(0, length, -height), a, e, f, b);
        Face(Vector3.left, a, c0, e);
        Face(Vector3.right, b, f, d);
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        return Save(key, mesh);
    }

    /// <summary>Game object with a generated mesh, a material and (optionally) a collider.</summary>
    public static GameObject Object(string name, Mesh mesh, Material material, Transform parent, Vector3 position, float yaw = 0f, Collider collider = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    public static GameObject TexBox(string name, Vector3 center, Vector3 size, Material material, float texScale, Transform parent, float yaw = 0f, bool collider = true)
    {
        var go = Object(name, Box(size, texScale), material, parent, center, yaw);
        if (collider) go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    public static GameObject TexCylinder(string name, Vector3 bottomCenter, float radius, float height, Material material, float texScale, Transform parent, bool collider = true)
    {
        var go = Object(name, Cylinder(radius, height, texScale), material, parent, bottomCenter);
        if (collider)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = radius;
            c.height = Mathf.Max(height, radius * 2);
            c.center = new Vector3(0, height / 2f, 0);
        }
        return go;
    }
}
