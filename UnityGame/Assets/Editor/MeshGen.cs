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
