using System.Collections.Generic;
using System.IO;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Al-Zahra: Madinat al-Zahra, the 10th-century caliphal city below the Sierra Morena near Córdoba, in
/// spring. A compressed slice of the real site on three terraces stepping down to the south:
/// <list type="bullet">
/// <item>upper (10 m): Dar al-Mulk with its view over everything, the Edificio Basilical Superior and its
/// plaza, the bent North Gate passage with four doors, the north wall walk;</item>
/// <item>middle (4 m): the Salón Rico (five naves on pink and blue marble columns, red and cream
/// horseshoe arches in an alfiz) facing the Upper Garden (four sunken parterres, raised walks, a central
/// pavilion between four pools), the Great Portico on the Plaza de Armas, the vaulted ramp street (Calle
/// en Rampa) with its hairpin, the covered Camino de Ronda on the west;</item>
/// <item>lower (0 m): the Lower Garden, the Aljama mosque turned towards the qibla with its minaret, the
/// houses (Casa de Ya'far with its triple arcade, Patio de la Pila, Casa de la Alberca) and the south wall.</item>
/// </list>
/// Golden calcarenite ashlar laid soga y tizón, white plaster with a red almagra dado, white marble
/// floors; cypresses, palms, orange trees, myrtle hedges and spring flowers. Textures are small and
/// generated (ashlar, marble, carved ataurique) or the shared ambientCG sets.
/// </summary>
public static class ZahraBuilder
{
    public const float Half = 96f;
    private const float Lower = 0f, Middle = 4f, Upper = 10f;
    private const float MidSouth = -30f, UpSouth = 60f;   // where the middle and the upper terrace begin
    private const string MeshFolder = "Assets/Generated/Meshes/Zahra";
    private const string TexFolder = "Assets/Generated/Textures/Zahra";

    private static Transform root;
    private static System.Random rng;
    private static readonly List<Renderer> night = new List<Renderer>();
    private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

    private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>Terrace level at a point (the ground of the three terraces).</summary>
    public static float Level(float z) => z < MidSouth ? Lower : z < UpSouth ? Middle : Upper;

    // ------------------------------------------------------------------ materials

    private static Material ashlar, marble, ataurique;
    private static Material Ashlar => ashlar;
    private static Material Marble => marble;
    private static Material Ataurique => ataurique;
    private static Material Cream => AssetSetup.Solid("Z_Cream", new Color(0.93f, 0.85f, 0.68f), 0.25f);
    private static Material Red => AssetSetup.Solid("Z_Red", new Color(0.62f, 0.2f, 0.12f), 0.2f);
    private static Material Plaster => PortBuilder.Pbr("Z_PlasterWhite", "Plaster001", new Color(0.98f, 0.96f, 0.92f), 0.3f);
    private static Material Almagra => PortBuilder.Pbr("Z_Almagra", "Plaster001", new Color(0.62f, 0.2f, 0.12f), 0.3f);
    private static Material Violet => PortBuilder.Pbr("Z_Violet", "PavingStones070", new Color(0.72f, 0.62f, 0.78f), 0.4f);
    private static Material BrickFloor => PortBuilder.Pbr("Z_BrickFloor", "Bricks076A", new Color(0.95f, 0.62f, 0.48f), 0.25f);
    private static Material Paving => PortBuilder.Pbr("Z_Paving", "PavingStones070", new Color(1f, 0.9f, 0.74f), 0.3f);
    private static Material Sand => PortBuilder.Pbr("Z_Sand", "Ground104", new Color(1.05f, 0.92f, 0.72f), 0.08f);
    private static Material Grass => PortBuilder.Pbr("Z_Grass", "Grass004", new Color(0.92f, 1.02f, 0.8f), 0.15f);
    private static Material Gravel => PortBuilder.Pbr("Z_Gravel", "Gravel036L", new Color(1.02f, 0.95f, 0.84f), 0.15f);
    private static Material HedgeMat => PortBuilder.Pbr("Z_Hedge", "Grass001", new Color(0.32f, 0.5f, 0.26f), 0.1f);
    private static Material Earth => PortBuilder.Pbr("Z_Earth", "Ground104", new Color(0.82f, 0.68f, 0.5f), 0.08f);
    private static Material RoofTiles => PortBuilder.Pbr("Z_RoofTiles", "RoofingTiles006", new Color(0.9f, 0.52f, 0.36f), 0.3f);
    private static Material Wood => PortBuilder.Pbr("Z_Wood", "Wood049", new Color(0.62f, 0.44f, 0.3f), 0.4f);
    private static Material PalmTrunk => PortBuilder.Pbr("Z_PalmTrunk", "Wood049", new Color(0.62f, 0.5f, 0.38f), 0.2f);
    private static Material MarblePink => AssetSetup.Solid("Z_MarblePink", new Color(0.86f, 0.64f, 0.62f), 0.75f);
    private static Material MarbleBlue => AssetSetup.Solid("Z_MarbleBlue", new Color(0.6f, 0.67f, 0.76f), 0.75f);
    private static Material Capital => AssetSetup.Solid("Z_Capital", new Color(0.94f, 0.93f, 0.88f), 0.5f);
    private static Material Leaf => AssetSetup.Solid("Z_PalmLeaf", new Color(0.3f, 0.48f, 0.18f), 0.25f);
    private static Material Cypress => PortBuilder.Pbr("Z_Cypress", "Grass001", new Color(0.16f, 0.3f, 0.14f), 0.1f);
    private static Material Orange => AssetSetup.Solid("Z_Orange", new Color(1f, 0.55f, 0.08f), 0.5f);
    private static Material Bronze => AssetSetup.Solid("Z_Bronze", new Color(0.25f, 0.18f, 0.1f), 0.5f, 0.6f);
    private static Material Water => AssetSetup.WaterMat("Water_Pool", new Color(0.04f, 0.17f, 0.18f), new Color(0.18f, 0.42f, 0.4f), 3f, 0.15f);

    private static void Box(Transform t, string name, Vector3 center, Vector3 size, Material m, float tex = 2f, bool collider = true, float yaw = 0f)
    {
        if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) return;
        MeshGen.TexBox(name, center, size, m, tex, t, yaw, collider);
    }

    private static Transform Node(string name, Transform parent, Vector3 pos, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        return t;
    }

    /// <summary>A box placed in a node's own frame (local centre, local size).</summary>
    private static GameObject LBox(Transform t, string name, Vector3 local, Vector3 size, Material m, float tex = 2f, bool collider = true)
    {
        if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) return null;
        return MeshGen.TexBox(name, t.TransformPoint(local), size, m, tex, t, t.eulerAngles.y, collider);
    }

    // ------------------------------------------------------------------ generated textures

    private static Texture2D SaveTexture(string name, int size, System.Func<int, int, Color> pixel, bool normalMap)
    {
        AssetSetup.Folder(TexFolder);
        string path = $"{TexFolder}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, pixel(x, y));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.maxTextureSize = size;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>A normal map from a tileable height function (0..1), strength in pixels.</summary>
    private static Texture2D NormalFrom(string name, int size, float[,] h, float strength)
    {
        return SaveTexture(name, size, (x, y) =>
        {
            float dx = h[(x + 1) % size, y] - h[(x - 1 + size) % size, y];
            float dy = h[x, (y + 1) % size] - h[x, (y - 1 + size) % size];
            var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
            return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }, true);
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
        }
    }

    /// <summary>Tileable value noise (period in cells).</summary>
    private static float Noise(float u, float v, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
        float fx = u - x0, fy = v - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int M(int a) => ((a % period) + period) % period;
        float a00 = Hash(M(x0), M(y0), seed), a10 = Hash(M(x0 + 1), M(y0), seed), a01 = Hash(M(x0), M(y0 + 1), seed), a11 = Hash(M(x0 + 1), M(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
    }

    /// <summary>
    /// Golden calcarenite ashlar laid soga y tizón: one stretcher (1 m) and two headers (0.5 m) in each
    /// 0.5 m course, the courses offset; 2 x 2 m per repeat. Fossil specks, weathering, thin pale joints.
    /// </summary>
    private static void MakeAshlar()
    {
        const int size = 512;
        const float metres = 2f, course = 0.5f;
        var h = new float[size, size];
        var albedo = new Color[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float my = y / (float)size * metres, mx = x / (float)size * metres;
                int row = Mathf.FloorToInt(my / course);
                float shift = (row % 2) * 0.5f + (row % 4 >= 2 ? 0.25f : 0f);
                float u = (mx + shift) % metres;
                // stretcher 0..1, header 1..1.5, header 1.5..2
                float start = u < 1f ? 0f : u < 1.5f ? 1f : 1.5f, end = u < 1f ? 1f : u < 1.5f ? 1.5f : 2f;
                int block = row * 7 + (int)(start * 4);
                float ex = Mathf.Min(u - start, end - u), ey = Mathf.Min(my - row * course, (row + 1) * course - my);
                float edge = Mathf.Min(ex, ey);
                float joint = 0.012f;
                float height = edge < joint ? 0f : Mathf.Clamp01((edge - joint) / 0.03f);
                float n1 = Noise(x / 16f, y / 16f, size / 16, 3), n2 = Noise(x / 4f, y / 4f, size / 4, 5);
                height = height * (0.85f + 0.15f * n1) + (Hash(x, y, 9) > 0.985f ? -0.25f : 0f) * height;
                h[x, y] = height;
                float tone = 0.9f + 0.18f * Hash(block, row, 11) + 0.08f * (n1 - 0.5f) + 0.05f * (n2 - 0.5f);
                var stone = new Color(0.8f, 0.66f, 0.45f) * tone;
                stone = Color.Lerp(stone, new Color(0.62f, 0.52f, 0.38f), Hash(x, y, 13) > 0.97f ? 0.6f : 0f);   // fossils
                stone = Color.Lerp(stone, new Color(0.55f, 0.47f, 0.38f), Mathf.Clamp01((Noise(x / 32f, y / 6f, size / 32, 21) - 0.6f) * 1.4f));   // streaks
                var jointColor = new Color(0.86f, 0.8f, 0.68f);
                albedo[x, y] = edge < joint ? jointColor : stone;
                albedo[x, y].a = 1f;
            }
        var tex = SaveTexture("Ashlar", size, (x, y) => albedo[x, y], false);
        var nrm = NormalFrom("Ashlar_N", size, h, 3.5f);
        ashlar = AssetSetup.Mat("Z_Ashlar", Color.white, 0.18f, 0f, tex, nrm);
    }

    /// <summary>White marble in 1 m slabs with soft grey veins (2 x 2 m per repeat).</summary>
    private static void MakeMarble()
    {
        const int size = 256;
        var h = new float[size, size];
        var tex = SaveTexture("Marble", size, (x, y) =>
        {
            float u = x / (float)size, v = y / (float)size;
            float warp = Noise(x / 16f, y / 16f, size / 16, 31) * 6f + Noise(x / 6f, y / 6f, size / 6 + 1, 33) * 1.5f;
            float vein = Mathf.Pow(Mathf.Abs(Mathf.Sin((u * 2f + v * 3f) * Mathf.PI * 2f + warp)), 18f);
            float slab = (Mathf.Abs(u * 2f - Mathf.Round(u * 2f)) < 0.006f || Mathf.Abs(v * 2f - Mathf.Round(v * 2f)) < 0.006f) ? 1f : 0f;
            h[x, y] = 1f - slab;
            var c = Color.Lerp(new Color(0.94f, 0.93f, 0.9f), new Color(0.62f, 0.62f, 0.64f), vein * 0.7f);
            c *= 0.97f + 0.06f * Hash(Mathf.FloorToInt(u * 2f), Mathf.FloorToInt(v * 2f), 37);
            if (slab > 0) c = new Color(0.7f, 0.68f, 0.64f);
            c.a = 1f;
            return c;
        }, false);
        var nrm = NormalFrom("Marble_N", size, h, 2f);
        marble = AssetSetup.Mat("Z_Marble", Color.white, 0.7f, 0f, tex, nrm);
    }

    /// <summary>
    /// Carved stone panel (ataurique): stems winding in rings with leaves, cut deep into cream stone,
    /// as on the Salón Rico walls (1 m per repeat).
    /// </summary>
    private static void MakeAtaurique()
    {
        const int size = 256;
        var h = new float[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size * 4f, v = y / (float)size * 4f;
                // interlaced rings on a staggered grid, stems along their edges, leaves filling the gaps
                float cu = u - Mathf.Floor(u) - 0.5f, cv = v - Mathf.Floor(v) - 0.5f;
                float ring = Mathf.Abs(Mathf.Sqrt(cu * cu + cv * cv) - 0.36f);
                float du = (u + 0.5f) - Mathf.Floor(u + 0.5f) - 0.5f, dv = (v + 0.5f) - Mathf.Floor(v + 0.5f) - 0.5f;
                float ring2 = Mathf.Abs(Mathf.Sqrt(du * du + dv * dv) - 0.36f);
                float stem = Mathf.Clamp01(1f - Mathf.Min(ring, ring2) / 0.045f);
                float a = Mathf.Atan2(cv, cu);
                float leaf = Mathf.Clamp01(Mathf.Sin(a * 6f) * 0.5f + 0.5f - Mathf.Abs(Mathf.Sqrt(cu * cu + cv * cv) - 0.18f) * 6f);
                h[x, y] = Mathf.Max(stem, leaf * 0.85f);
            }
        var tex = SaveTexture("Ataurique", size, (x, y) =>
        {
            float k = h[x, y];
            var c = Color.Lerp(new Color(0.6f, 0.5f, 0.36f), new Color(0.95f, 0.87f, 0.7f), 0.35f + 0.65f * k);
            c.a = 1f;
            return c;
        }, false);
        var nrm = NormalFrom("Ataurique_N", size, h, 5f);
        ataurique = AssetSetup.Mat("Z_Ataurique", Color.white, 0.2f, 0f, tex, nrm);
    }

    // ------------------------------------------------------------------ arches

    /// <summary>Gathers faces of a mesh in a node's local space, with planar texture coordinates.</summary>
    private class Builder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        private readonly float tex;
        public Builder(float texScale) { tex = texScale; }

        /// <summary>A flat polygon (convex, in order) facing 'normal'.</summary>
        public void Face(Vector3 normal, params Vector3[] p)
        {
            normal.Normalize();
            int s = V.Count;
            foreach (var c in p)
            {
                V.Add(c);
                N.Add(normal);
                var a = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                UV.Add((a.z >= a.x && a.z >= a.y ? new Vector2(c.x, c.y) : a.y >= a.x ? new Vector2(c.x, c.z) : new Vector2(c.z, c.y)) / tex);
            }
            for (int i = 1; i + 1 < p.Length; i++)
            {
                var cross = Vector3.Cross(p[i] - p[0], p[i + 1] - p[0]);
                if (cross.sqrMagnitude < 1e-10f) continue;
                if (Vector3.Dot(cross, normal) > 0) { T.Add(s); T.Add(s + i); T.Add(s + i + 1); }
                else { T.Add(s); T.Add(s + i + 1); T.Add(s + i); }
            }
        }

        public Mesh Save(string key)
        {
            if (meshes.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = new Mesh { name = key };
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            AssetSetup.Folder(MeshFolder);
            AssetDatabase.CreateAsset(m, $"{MeshFolder}/{key}.asset");
            return meshes[key] = m;
        }
    }

    private static string K(float v) => Mathf.RoundToInt(v * 100).ToString();

    private static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material m, bool collider)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    /// <summary>
    /// One arched opening in a wall, in the frame of 't' (x along the wall, y up, z through it, the wall
    /// 'depth' thick and centred on z = 0): a horseshoe arch (alpha = how far the curve runs on below the
    /// centre, degrees; 0 = round) of red and cream voussoirs, the wall around it up to the rectangle of
    /// the alfiz, the alfiz itself, and the wall above up to 'top'. 'cx' is the centre of the opening,
    /// 'width' the width the arch takes in the wall, 'spring' the height where the curve starts.
    /// </summary>
    private static Transform Arch(Transform t, float cx, float width, float spring, float top, float depth, float alpha, Material wall, bool alfiz = true, bool jambs = true)
    {
        float a = alpha * Mathf.Deg2Rad;
        float v = Mathf.Clamp(width * 0.12f, 0.28f, 0.6f);
        float ro = width / 2f - Mathf.Max(0.12f, width * 0.05f);
        float ri = ro - v;
        float hc = spring + ri * Mathf.Sin(a);
        float yb = hc - ro * Mathf.Sin(a);
        float yt = Mathf.Min(top, hc + ro + Mathf.Max(0.25f, width * 0.08f));
        float hw = width / 2f, hd = depth / 2f;
        string key = $"arch_{K(width)}_{K(spring)}_{K(depth)}_{K(alpha)}_{K(yt)}";
        var node = new GameObject("Arch").transform;
        node.SetParent(t, false);
        node.localPosition = new Vector3(cx, 0, 0);

        // the voussoirs, red and cream by turns, the key stone cream
        float sweep = Mathf.PI + 2f * a;
        int count = Mathf.Max(9, Mathf.RoundToInt(sweep * (ri + ro) / 2f / 0.32f)) | 1;
        var red = new Builder(1f);
        var cream = new Builder(1f);
        for (int i = 0; i < count; i++)
        {
            float a0 = -a + sweep * i / count, a1 = -a + sweep * (i + 1) / count;
            var b = (i % 2 == 0) == (count / 2 % 2 == 0) ? cream : red;
            Vector3 P(float rad, float ang, float depthZ) => new Vector3(rad * Mathf.Cos(ang), hc + rad * Mathf.Sin(ang), depthZ);
            b.Face(Vector3.back, P(ri, a0, -hd), P(ro, a0, -hd), P(ro, a1, -hd), P(ri, a1, -hd));
            b.Face(Vector3.forward, P(ri, a0, hd), P(ro, a0, hd), P(ro, a1, hd), P(ri, a1, hd));
            float am = (a0 + a1) / 2f;
            var inward = new Vector3(-Mathf.Cos(am), -Mathf.Sin(am), 0);
            b.Face(inward, P(ri, a0, -hd), P(ri, a1, -hd), P(ri, a1, hd), P(ri, a0, hd));
            b.Face(-inward, P(ro, a0, -hd), P(ro, a1, -hd), P(ro, a1, hd), P(ro, a0, hd));
        }
        MeshObject(node, "VoussoirsRed", red.Save(key + "_r"), Red, true);
        MeshObject(node, "VoussoirsCream", cream.Save(key + "_c"), Cream, true);

        // the wall between the arch and the rectangle (spandrels): rays from the centre of the arch
        var angles = new List<float>();
        for (int i = 0; i <= 32; i++) angles.Add(-a + sweep * i / 32f);
        angles.Add(Mathf.Atan2(yt - hc, hw));
        angles.Add(Mathf.Atan2(yt - hc, -hw));
        angles = angles.Where(x => x >= -a - 1e-4f && x <= Mathf.PI + a + 1e-4f).OrderBy(x => x).ToList();
        Vector2 RectHit(float ang)
        {
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang), best = float.MaxValue;
            if (c > 1e-4f) best = Mathf.Min(best, hw / c);
            if (c < -1e-4f) best = Mathf.Min(best, -hw / c);
            if (s > 1e-4f) best = Mathf.Min(best, (yt - hc) / s);
            if (s < -1e-4f) best = Mathf.Min(best, (yb - hc) / s);
            return new Vector2(c * best, hc + s * best);
        }
        var sp = new Builder(2f);
        for (int i = 0; i + 1 < angles.Count; i++)
        {
            float a0 = angles[i], a1 = angles[i + 1];
            if (a1 - a0 < 1e-5f) continue;
            var o0 = new Vector2(ro * Mathf.Cos(a0), hc + ro * Mathf.Sin(a0));
            var o1 = new Vector2(ro * Mathf.Cos(a1), hc + ro * Mathf.Sin(a1));
            var r0 = RectHit(a0); var r1 = RectHit(a1);
            foreach (float z in new[] { -hd, hd })
            {
                var n = z < 0 ? Vector3.back : Vector3.forward;
                var q0 = new Vector3(o0.x, o0.y, z); var q1 = new Vector3(o1.x, o1.y, z);
                var q2 = new Vector3(r1.x, r1.y, z); var q3 = new Vector3(r0.x, r0.y, z);
                if ((q3 - q0).sqrMagnitude < 1e-6f) sp.Face(n, q0, q1, q2);
                else if ((q2 - q1).sqrMagnitude < 1e-6f) sp.Face(n, q0, q1, q3);
                else sp.Face(n, q0, q1, q2, q3);
            }
        }
        MeshObject(node, "Spandrels", sp.Save(key + "_s"), wall, true);

        // jambs from the floor to the springing (a little proud of the wall face: the impost)
        float jamb = hw - ri * Mathf.Cos(a);
        foreach (float s in new[] { -1f, 1f })
        {
            if (jambs) LBox(node, "Jamb", new Vector3(s * (hw - jamb / 2f), spring / 2f, 0), new Vector3(jamb, spring, depth + 0.04f), wall);
            else LBox(node, "JambStub", new Vector3(s * (hw - jamb / 2f), spring - 0.15f, 0), new Vector3(jamb, 0.3f, depth + 0.04f), Cream, 1f);
        }
        // above the rectangle, up to the top of the wall
        if (top - yt > 0.02f) LBox(node, "WallAbove", new Vector3(0, (yt + top) / 2f, 0), new Vector3(width, top - yt, depth), wall);
        if (!alfiz) return node;
        // the alfiz: a raised frame round the arch, down to the springing, on both faces
        foreach (float z in new[] { -hd - 0.025f, hd + 0.025f })
        {
            LBox(node, "Alfiz", new Vector3(0, yt - 0.07f, z), new Vector3(width - 0.1f, 0.14f, 0.05f), Cream, 1f, false);
            foreach (float s in new[] { -1f, 1f })
                LBox(node, "Alfiz", new Vector3(s * (hw - 0.12f), (spring + yt) / 2f, z), new Vector3(0.14f, yt - spring, 0.05f), Cream, 1f, false);
        }
        return node;
    }

    /// <summary>A marble column with base, capital and impost, from y0 up to 'top' (the springing).</summary>
    private static void Column(Transform t, Vector3 local, float top, Material shaft, float impostWidth, float depth)
    {
        var p = t.TransformPoint(local);
        float shaftH = top - 0.95f;
        var go = MeshGen.Object("Column", MeshGen.Cylinder(0.17f, shaftH, 1f, 12), shaft, t, p + Vector3.up * 0.2f);
        var cap = go.AddComponent<CapsuleCollider>();
        cap.radius = 0.17f; cap.height = shaftH; cap.center = new Vector3(0, shaftH / 2f, 0);
        LBox(t, "ColumnBase", local + new Vector3(0, 0.1f, 0), new Vector3(0.5f, 0.2f, 0.5f), Capital, 1f);
        LBox(t, "CapitalLow", local + new Vector3(0, top - 0.62f, 0), new Vector3(0.42f, 0.36f, 0.42f), Capital, 1f);
        LBox(t, "CapitalTop", local + new Vector3(0, top - 0.37f, 0), new Vector3(0.58f, 0.14f, 0.58f), Capital, 1f);
        LBox(t, "Impost", local + new Vector3(0, top - 0.15f, 0), new Vector3(impostWidth, 0.3f, depth + 0.04f), Cream, 1f);
    }

    /// <summary>
    /// An arcade in a wall from 'start' along 'dir' (floor level, centre of the wall): 'bays' arches of
    /// 'bay' metres, on piers ('pier' wide) or on marble columns, the wall up to 'top' above the floor.
    /// </summary>
    private static Transform Arcade(Transform parent, Vector3 start, Vector3 dir, int bays, float bay, float spring, float top, float depth,
        float alpha, Material wall, bool columns, bool endPiers = true, float pier = 0.7f)
    {
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f;   // local x along dir
        var t = Node("Arcade", parent, start, yaw);
        if (columns) pier = 0.36f;
        // the impost block spans between the springing points of two neighbouring arches
        float ia = alpha * Mathf.Deg2Rad, iw = bay - pier;
        float iro = iw / 2f - Mathf.Max(0.12f, iw * 0.05f), iri = iro - Mathf.Clamp(iw * 0.12f, 0.28f, 0.6f);
        float impost = Mathf.Max(0.6f, bay - 2f * iri * Mathf.Cos(ia) + 0.1f);
        for (int k = 0; k <= bays; k++)
        {
            float s = k * bay;
            bool end = k == 0 || k == bays;
            if (!columns || (end && endPiers))
                LBox(t, "Pier", new Vector3(s, top / 2f, 0), new Vector3(pier, top, depth), wall);
            else
            {
                LBox(t, "PierAbove", new Vector3(s, (spring + top) / 2f, 0), new Vector3(pier, top - spring, depth), wall);
                Column(t, new Vector3(s, 0, 0), spring, (k + bays) % 2 == 0 ? MarblePink : MarbleBlue, impost, depth);
            }
        }
        for (int i = 0; i < bays; i++)
        {
            float w = bay - pier;
            var bayNode = Node("Bay", t, t.TransformPoint(new Vector3(i * bay + bay / 2f, 0, 0)), t.eulerAngles.y);
            if (columns)
            {
                // the arch rests on the impost blocks: no jambs down to the floor
                ArchOnColumns(bayNode, w, spring, top, depth, alpha, wall);
            }
            else Arch(bayNode, 0, w, spring, top, depth, alpha, wall);
        }
        return t;
    }

    /// <summary>An arch between two columns: the impost blocks carry it, no jambs down to the floor.</summary>
    private static void ArchOnColumns(Transform t, float width, float spring, float top, float depth, float alpha, Material wall)
    {
        Arch(t, 0, width, spring, top, depth, alpha, wall, true, false);
    }

    /// <summary>A plain wall with an optional doorway (centre along the wall, width, height).</summary>
    private static void Wall(Transform parent, Vector3 a, Vector3 b, float height, float thick, Material m, float doorAt = -1f, float doorW = 1.4f, float doorH = 2.6f, float y0 = 0f)
    {
        var dir = b - a; dir.y = 0;
        float len = dir.magnitude;
        if (len < 0.05f) return;
        var t = Node("Wall", parent, a + Vector3.up * y0, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f);
        if (doorAt < 0f)
        {
            LBox(t, "Wall", new Vector3(len / 2f, height / 2f, 0), new Vector3(len + thick * 0f, height, thick), m);
            return;
        }
        float d0 = doorAt - doorW / 2f, d1 = doorAt + doorW / 2f;
        LBox(t, "Wall", new Vector3(d0 / 2f, height / 2f, 0), new Vector3(d0, height, thick), m);
        LBox(t, "Wall", new Vector3((d1 + len) / 2f, height / 2f, 0), new Vector3(len - d1, height, thick), m);
        LBox(t, "Lintel", new Vector3(doorAt, (doorH + height) / 2f, 0), new Vector3(doorW, height - doorH, thick), m);
    }

    /// <summary>A broken wall: pieces of different heights, now and then a gap (ruins, cover).</summary>
    private static void Ruin(Transform parent, Vector3 a, Vector3 b, float maxH, float thick, Material m, float minH = 0.5f)
    {
        var dir = b - a; dir.y = 0;
        float len = dir.magnitude;
        var t = Node("Ruin", parent, a, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f);
        float h = R(minH, maxH);
        for (float u = 0; u < len - 0.05f;)
        {
            float piece = Mathf.Min(len - u, R(0.8f, 2.2f));
            h = Mathf.Clamp(h + R(-0.6f, 0.6f), minH, maxH);
            if (rng.NextDouble() > 0.1) LBox(t, "RuinWall", new Vector3(u + piece / 2f, h / 2f, 0), new Vector3(piece + 0.02f, h, thick), m);
            u += piece;
        }
    }

    // ------------------------------------------------------------------ plants

    private static Mesh cypressMesh;

    /// <summary>A spindle of dark foliage on a short trunk: the Mediterranean cypress.</summary>
    private static void CypressTree(Transform parent, Vector3 p, float height)
    {
        if (cypressMesh == null)
        {
            var b = new Builder(1.2f);
            const int seg = 10, rows = 8;
            Vector3 P(int around, int row)
            {
                float f = row / (float)rows, ang = around * Mathf.PI * 2f / seg;
                float rad = 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Lerp(0.04f, 1f, f)), 0.7f) * (1f - 0.35f * f);
                return new Vector3(Mathf.Cos(ang) * rad, f, Mathf.Sin(ang) * rad);
            }
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < seg; i++)
                {
                    var p0 = P(i, r); var p1 = P(i + 1, r); var p2 = P(i + 1, r + 1); var p3 = P(i, r + 1);
                    var outward = new Vector3(p0.x + p1.x, 0, p0.z + p1.z);
                    b.Face(outward.sqrMagnitude > 1e-6f ? outward : Vector3.up, p0, p1, p2, p3);
                }
            cypressMesh = b.Save("cypress");
        }
        var t = Node("Cypress", parent, p, R(0, 360));
        LBox(t, "Trunk", new Vector3(0, 0.4f, 0), new Vector3(0.22f, 0.8f, 0.22f), PalmTrunk, 1f, false);
        var crown = MeshObject(t, "Crown", cypressMesh, Cypress, false);
        crown.transform.localPosition = new Vector3(0, 0.5f, 0);
        crown.transform.localScale = new Vector3(height * 0.24f, height - 0.5f, height * 0.24f);
        var col = t.gameObject.AddComponent<CapsuleCollider>();
        col.radius = 0.35f; col.height = height; col.center = new Vector3(0, height / 2f, 0);
    }

    /// <summary>A date palm: a slightly leaning trunk and a crown of drooping fronds.</summary>
    private static void Palm(Transform parent, Vector3 p, float height)
    {
        var t = Node("Palm", parent, p, R(0, 360));
        float lean = R(-6f, 6f);
        var trunk = MeshGen.Object("PalmTrunk", MeshGen.Cylinder(0.2f, height, 1.2f, 10), PalmTrunk, t, p);
        trunk.transform.rotation = Quaternion.Euler(lean, t.eulerAngles.y, 0);
        var c = trunk.AddComponent<CapsuleCollider>();
        c.radius = 0.22f; c.height = height; c.center = new Vector3(0, height / 2f, 0);
        var top = trunk.transform.TransformPoint(new Vector3(0, height, 0));
        for (int i = 0; i < 12; i++)
        {
            var frond = MeshGen.TexBox("Frond", top, new Vector3(0.5f, 0.04f, 2.6f), Leaf, 1f, t, 0f, false);
            frond.transform.rotation = Quaternion.Euler(0, i * 30f + R(-8, 8), 0) * Quaternion.Euler(R(18f, 38f), 0, 0);
            frond.transform.position = top + frond.transform.forward * 1.2f;
        }
    }

    /// <summary>A small orange tree in a round bed, a few fruits in the crown.</summary>
    private static void OrangeTree(Transform parent, Vector3 p)
    {
        var tree = Nature.Tree(Nature.Trees, rng, p, 3.4f, 4.4f, parent);
        if (tree == null) return;
        for (int i = 0; i < 5; i++)
        {
            var fruit = MeshGen.Object("Orange", MeshGen.Cylinder(0.07f, 0.12f, 1f, 6), Orange, parent, p + new Vector3(R(-1.1f, 1.1f), R(2f, 3.2f), R(-1.1f, 1.1f)));
            fruit.isStatic = true;
        }
    }

    private static void Hedge(Transform parent, Vector3 a, Vector3 b, float height = 0.8f)
    {
        var d = b - a;
        float len = new Vector2(d.x, d.z).magnitude;
        MeshGen.RoundBox("Myrtle", (a + b) / 2f + Vector3.up * height / 2f, new Vector3(0.7f, height, len), 0.2f, HedgeMat, 1.5f, parent, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
    }

    private static void Flowers(Transform parent, Vector3 c, float rx, float rz, int count, float y)
    {
        for (int i = 0; i < count; i++)
        {
            var p = c + new Vector3(R(-rx, rx), 0, R(-rz, rz));
            p.y = y;
            if (rng.NextDouble() < 0.5) Nature.Place("Bush_Common_Flowers", p, R(0, 360), R(0.6f, 1.1f), parent);
            else Nature.Place(Nature.Ground[rng.Next(Nature.Ground.Length)], p, R(0, 360), R(0.4f, 0.8f), parent);
        }
    }

    /// <summary>A bronze lantern on a post (lit at night).</summary>
    private static void Lantern(Transform parent, Vector3 p)
    {
        var t = Node("Lantern", parent, p, 0);
        MeshGen.TexCylinder("LanternPost", p, 0.06f, 2.6f, Bronze, 1f, t);
        LBox(t, "LanternCap", new Vector3(0, 3.02f, 0), new Vector3(0.34f, 0.08f, 0.34f), Bronze, 1f, false);
        NightLights.Lamp(t, p + Vector3.up * 2.8f, new Vector3(0.2f, 0.32f, 0.2f), night, 3f);
    }

    /// <summary>A pool with a stone rim; the water a little below the walk.</summary>
    private static void Pool(Transform parent, Rect r, float y, float depth = 0.6f)
    {
        var t = Node("Pool", parent, new Vector3(r.center.x, y, r.center.y), 0);
        Box(parent, "PoolBed", new Vector3(r.center.x, y - depth - 0.1f, r.center.y), new Vector3(r.width, 0.2f, r.height), Ashlar, 2f);
        var water = MeshGen.TexBox("Water", new Vector3(r.center.x, y - 0.25f, r.center.y), new Vector3(r.width - 0.1f, 0.02f, r.height - 0.1f), Water, 4f, t, 0f, false);
        water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // the rim, a step above the walk
        foreach (float s in new[] { -1f, 1f })
        {
            Box(parent, "Rim", new Vector3(r.center.x, y + 0.1f - depth / 2f, r.center.y + s * (r.height / 2f + 0.2f)), new Vector3(r.width + 0.8f, depth + 0.2f, 0.4f), Cream, 1f);
            Box(parent, "Rim", new Vector3(r.center.x + s * (r.width / 2f + 0.2f), y + 0.1f - depth / 2f, r.center.y), new Vector3(0.4f, depth + 0.2f, r.height), Cream, 1f);
        }
        // a fence nobody sees: no walking on the water
        var block = new GameObject("PoolFence");
        block.transform.SetParent(t, false);
        block.layer = World.FenceLayer;
        block.AddComponent<BoxCollider>().size = new Vector3(r.width, 2f, r.height);
        block.GetComponent<BoxCollider>().center = new Vector3(0, 1f, 0);
    }

    // ------------------------------------------------------------------ the map

    private static void Slab(Transform t, string name, Rect r, float top, float thick, Material m, float tex = 2f)
    {
        Box(t, name, new Vector3(r.center.x, top - thick / 2f, r.center.y), new Vector3(r.width, thick, r.height), m, tex);
    }

    private static void Slabs(Transform t, string name, Rect area, IList<Rect> holes, float top, float thick, Material m, float tex = 2f)
    {
        foreach (var r in Underground.Cover(area, holes)) Slab(t, name, r, top, thick, m, tex);
    }

    public static MapLayout Build()
    {
        rng = new System.Random(936);   // the year the building of the city began
        night.Clear();
        meshes.Clear();
        cypressMesh = null;
        AssetDatabase.DeleteAsset(MeshFolder);
        AssetSetup.Folder(MeshFolder);
        MakeAshlar();
        MakeMarble();
        MakeAtaurique();
        root = new GameObject("Map_Zahra").transform;

        Terraces();
        UpperGarden();
        SalonRico();
        Portico();
        PlazaDeArmas();
        RampStreet();
        CaminoDeRonda();
        LowerGarden();
        Mosque(new Vector3(68f, Lower, -63f), -28f);
        Houses();
        DarAlMulk();
        BasilicalHall();
        NorthGate();
        NorthWall();
        Supplies();
        Surroundings();

        float edge = Half + 2f;
        foreach (var (c, sz) in new[] { (new Vector3(0, 20, edge), new Vector3(edge * 2 + 4, 40, 1)), (new Vector3(0, 20, -edge), new Vector3(edge * 2 + 4, 40, 1)),
                                        (new Vector3(edge, 20, 0), new Vector3(1, 40, edge * 2 + 4)), (new Vector3(-edge, 20, 0), new Vector3(1, 40, edge * 2 + 4)) })
        {
            var wall = new GameObject("Edge");
            wall.layer = World.FenceLayer;
            wall.transform.SetParent(root, false);
            wall.transform.position = c;
            wall.AddComponent<BoxCollider>().size = sz;
        }
        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            if (tr.gameObject.layer != World.FenceLayer)
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);

        var layout = new MapLayout { Name = "AL-ZAHRA", Root = root.gameObject, Center = new Vector3(0, Middle, 0), Radius = Half };
        BuildNavigation(layout);
        root.gameObject.AddComponent<MapScene>().NightLights = MapOptimizer.Merge(root, "Zahra", null, night).ToArray();
        Debug.Log($"[Map] Zahra built: {root.GetComponentsInChildren<Renderer>().Length} renderers, {layout.Waypoints.Length} waypoints, {layout.Spawns.Length} spawns");
        return layout;
    }

    /// <summary>The three terraces, faced with ashlar where they step down, with a parapet along the edges.</summary>
    private static void Terraces()
    {
        var t = Node("Terraces", root, Vector3.zero, 0);
        const float far = 320f;
        // the valley beyond the south wall, then the three terraces
        Slab(t, "Valley", Rect.MinMaxRect(-far, -far, far, -93f), -3f, 2f, Grass, 6f);
        Slab(t, "LowerTerrace", Rect.MinMaxRect(-far, -93f, far, MidSouth), Lower, 5f, Earth, 4f);
        Slab(t, "MiddleTerrace", Rect.MinMaxRect(-far, MidSouth, far, UpSouth), Middle, 8f, Earth, 4f);
        Slab(t, "UpperTerrace", Rect.MinMaxRect(-far, UpSouth, far, far), Upper, 14f, Earth, 4f);
        // ashlar faces of the steps between the terraces
        Box(t, "TerraceFace", new Vector3(0, (Lower + Middle) / 2f - 0.32f, MidSouth - 0.15f), new Vector3(Half * 2f + 8f, Middle - Lower + 0.6f, 0.3f), Ashlar);
        Box(t, "TerraceFace", new Vector3(0, (Middle + Upper) / 2f - 0.32f, UpSouth - 0.15f), new Vector3(Half * 2f + 8f, Upper - Middle + 0.6f, 0.3f), Ashlar);
        // parapets on the edges, open where stairs, ramps and the hidden climb arrive
        void Parapet(float z, float y, params (float a, float b)[] gaps)
        {
            float x = -Half;
            var ordered = gaps.OrderBy(g => g.a).ToList();
            ordered.Add((Half, Half));
            foreach (var (ga, gb) in ordered)
            {
                if (ga - x > 0.1f) Box(t, "Parapet", new Vector3((x + ga) / 2f, y + 0.45f, z), new Vector3(ga - x, 0.9f, 0.45f), Ashlar, 2f);
                x = gb;
            }
        }
        Parapet(MidSouth + 0.23f, Middle, (-8.2f, -1.8f), (-50.2f, -45.8f), (33f, 41f));
        Parapet(UpSouth + 0.23f, Upper, (-60.2f, -55.8f), (49.8f, 54.2f), (-20.6f, 10.6f));   // behind the Salón Rico its back wall stands higher
        // the strip along the foot of the middle terrace: a paved street
        Slab(t, "Street", Rect.MinMaxRect(-Half, MidSouth - 4f, Half, MidSouth), Lower + 0.04f, 0.1f, Paving, 2.5f);
    }

    /// <summary>
    /// The Upper Garden on the middle terrace: four parterres between raised walks (the beds lie lower),
    /// a pavilion on the crossing with a pool on each side, myrtle hedges, cypresses, palms and orange trees.
    /// </summary>
    private static void UpperGarden()
    {
        var t = Node("UpperGarden", root, Vector3.zero, 0);
        var area = Rect.MinMaxRect(-42f, -28f, 32f, 30f);
        float walk = Middle + 0.5f;
        Slab(t, "Beds", area, Middle + 0.04f, 0.1f, Grass, 3f);
        // pools round the pavilion on the crossing, and the paved centre
        var pools = new[] { Rect.MinMaxRect(-12f, 9f, 2f, 15f), Rect.MinMaxRect(-12f, -13f, 2f, -7f), Rect.MinMaxRect(-26f, -5f, -14f, 7f), Rect.MinMaxRect(4f, -5f, 16f, 7f) };
        var centre = Rect.MinMaxRect(-28f, -15f, 18f, 17f);
        Slabs(t, "Centre", centre, pools, walk, 0.5f, Marble, 2f);
        foreach (var r in pools) Pool(t, r, walk);
        // raised walks: round the edge, and the two axes out to the edges
        var walks = new List<Rect>
        {
            Rect.MinMaxRect(area.xMin, area.yMin, area.xMax, area.yMin + 3f), Rect.MinMaxRect(area.xMin, area.yMax - 3f, area.xMax, area.yMax),
            Rect.MinMaxRect(area.xMin, area.yMin + 3f, area.xMin + 3f, area.yMax - 3f), Rect.MinMaxRect(area.xMax - 3f, area.yMin + 3f, area.xMax, area.yMax - 3f),
            Rect.MinMaxRect(-7f, area.yMin + 3f, -3f, centre.yMin), Rect.MinMaxRect(-7f, centre.yMax, -3f, area.yMax - 3f),
            Rect.MinMaxRect(area.xMin + 3f, -1f, centre.xMin, 3f), Rect.MinMaxRect(centre.xMax, -1f, area.xMax - 3f, 3f),
        };
        foreach (var r in walks) Slab(t, "Walk", r, walk, 0.5f, Paving, 2.5f);
        // the parterres: hedged beds with orange trees, flowers, palms at the corners; cypresses on the axes
        var beds = new[] { Rect.MinMaxRect(area.xMin + 3f, 3f, -7f, area.yMax - 3f), Rect.MinMaxRect(-3f, 3f, area.xMax - 3f, area.yMax - 3f),
                           Rect.MinMaxRect(area.xMin + 3f, area.yMin + 3f, -7f, -1f), Rect.MinMaxRect(-3f, area.yMin + 3f, area.xMax - 3f, -1f) };
        foreach (var bed in beds)
            foreach (var r in Underground.Cover(bed, new[] { centre }))
            {
                if (r.width < 2.5f || r.height < 2.5f) continue;
                // hedges along the edges, a gap in the middle of each side
                var c = new Vector3(r.center.x, Middle, r.center.y);
                float hx = r.width / 2f - 0.6f, hz = r.height / 2f - 0.6f;
                foreach (float sz in new[] { -1f, 1f })
                {
                    Hedge(t, c + new Vector3(-hx, 0, sz * hz), c + new Vector3(-1f, 0, sz * hz));
                    Hedge(t, c + new Vector3(1f, 0, sz * hz), c + new Vector3(hx, 0, sz * hz));
                    if (hz > 2f)
                    {
                        Hedge(t, c + new Vector3(sz * hx, 0, -hz), c + new Vector3(sz * hx, 0, -1f));
                        Hedge(t, c + new Vector3(sz * hx, 0, 1f), c + new Vector3(sz * hx, 0, hz));
                    }
                }
                for (float x = r.xMin + 4f; x < r.xMax - 2.5f; x += 8f)
                    for (float z = r.yMin + 4f; z < r.yMax - 2.5f; z += 8f)
                        if (rng.NextDouble() < 0.75) OrangeTree(t, new Vector3(x + R(-0.8f, 0.8f), Middle, z + R(-0.8f, 0.8f)));
                Flowers(t, c, r.width / 2f - 1.5f, r.height / 2f - 1.5f, Mathf.Clamp(Mathf.RoundToInt(r.width * r.height / 40f), 2, 10), Middle);
            }
        foreach (float z in new[] { 20f, 25f, -20f, -24f })
            foreach (float x in new[] { -8.6f, -1.4f })
                CypressTree(t, new Vector3(x, Middle, z), R(8f, 10f));
        foreach (var (x, z) in new[] { (-37f, 26f), (27f, 26f), (-37f, -23f), (27f, -23f), (-30f, 10f), (22f, -10f) })
            Palm(t, new Vector3(x, Middle, z), R(7f, 9.5f));
        // the pavilion on the crossing: three arches on marble columns on each side, a tiled roof
        var pc = new Vector3(-5f, walk, 1f);
        const float side = 12f;
        var corners = new[] { pc + new Vector3(-side / 2f, 0, -side / 2f), pc + new Vector3(side / 2f, 0, -side / 2f), pc + new Vector3(side / 2f, 0, side / 2f), pc + new Vector3(-side / 2f, 0, side / 2f) };
        for (int i = 0; i < 4; i++)
        {
            var dir = (corners[(i + 1) % 4] - corners[i]).normalized;
            Arcade(t, corners[i], dir, 3, side / 3f, 2.6f, 4.4f, 0.5f, 28f, Plaster, true);
        }
        Box(t, "PavilionRoof", pc + Vector3.up * 4.6f, new Vector3(side + 0.8f, 0.4f, side + 0.8f), RoofTiles, 2f);
        Box(t, "PavilionCeiling", pc + Vector3.up * 4.38f, new Vector3(side - 0.6f, 0.06f, side - 0.6f), Wood, 2f, false);
        MeshGen.TexCylinder("Basin", pc, 1.4f, 0.55f, Cream, 1f, t);
        var basinWater = MeshGen.TexCylinder("BasinWater", pc + Vector3.up * 0.5f, 1.2f, 0.02f, Water, 2f, t, false);
        basinWater.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // garden walls on the west and the east (doors to the Camino de Ronda and the Portico)
        Wall(t, new Vector3(area.xMin - 0.4f, Middle, area.yMin), new Vector3(area.xMin - 0.4f, Middle, area.yMax), 3.2f, 0.8f, Ashlar, area.height - 11f, 2.2f, 2.8f);
        Wall(t, new Vector3(area.xMax + 0.4f, Middle, area.yMin), new Vector3(area.xMax + 0.4f, Middle, area.yMax), 3.2f, 0.8f, Ashlar, 29f, 2.2f, 2.8f);
        // lanterns along the main walk
        foreach (var (x, z) in new[] { (-6.6f, -18f), (-3.4f, -18f), (-6.6f, 20f), (-3.4f, 20f), (-30f, 2.6f), (20f, 2.6f), (-40.5f, -26.5f), (30.5f, -26.5f), (-40.5f, 28.5f), (30.5f, 28.5f) })
            Lantern(t, new Vector3(x, walk, z));
        // the stair down to the Lower Garden
        Buildings.Stairs(t, new Vector3(-5f, Lower, MidSouth - 7.2f), Vector3.forward, Middle - Lower, 6f, Ashlar);
    }

    /// <summary>
    /// The Salón Rico (Hall of Abd al-Rahman III): a front of five horseshoe arches, a cross nave, then
    /// five naves on arcades of pink and blue marble columns; white walls, red dado, carved panels; the
    /// throne niche in the north wall. Its flat roof is reached from the upper terrace by fallen ashlars.
    /// </summary>
    private static void SalonRico()
    {
        var t = Node("SalonRico", root, Vector3.zero, 0);
        float floor = Middle + 0.5f, top = 8.5f, roofY = floor + top;
        const float x0 = -20f, x1 = 10f, z0 = 38f, z1 = UpSouth;
        // the patio in front, level with the garden walks, and the marble floor
        Slab(t, "Patio", Rect.MinMaxRect(-42f, 30f, 32f, z0), floor, 0.5f, Paving, 2.5f);
        Slab(t, "HallFloor", Rect.MinMaxRect(x0, z0, x1, z1), floor, 0.5f, Marble, 2f);
        // the front: five horseshoe arches on piers in an ashlar wall
        Arcade(t, new Vector3(x0, floor, z0), Vector3.right, 5, 6f, 3.6f, top, 1f, 28f, Ashlar, false, true, 0.9f);
        // the cross nave behind it opens into the hall through five arches on columns
        Arcade(t, new Vector3(x0, floor, z0 + 5f), Vector3.right, 5, 6f, 3.4f, top, 0.7f, 28f, Plaster, true);
        // four arcades divide the five naves
        foreach (float x in new[] { -14f, -8f, -2f, 4f })
            Arcade(t, new Vector3(x, floor, z0 + 5f), Vector3.forward, 4, (z1 - z0 - 5f) / 4f, 3.4f, top, 0.6f, 25f, Plaster, true);
        // side walls with a door each, the back wall against the upper terrace
        Wall(t, new Vector3(x0 - 0.5f, Middle, z0), new Vector3(x0 - 0.5f, Middle, z1), top + 0.5f, 1f, Ashlar, 12f, 1.6f, 3.5f);
        Wall(t, new Vector3(x1 + 0.5f, Middle, z0), new Vector3(x1 + 0.5f, Middle, z1), top + 0.5f, 1f, Ashlar, 12f, 1.6f, 3.5f);
        Box(t, "BackWall", new Vector3((x0 + x1) / 2f, floor + top / 2f, z1 - 0.5f), new Vector3(x1 - x0, top, 1f), Ashlar);
        // inside: white plaster over a red almagra dado on the side and back walls, carved panels
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s < 0 ? x0 + 0.03f : x1 - 0.03f;
            foreach (var (a, b) in new[] { (z0 + 0.4f, z0 + 11.2f), (z0 + 12.8f, z1 - 1.05f) })
            {
                Box(t, "Dado", new Vector3(x, floor + 0.6f, (a + b) / 2f), new Vector3(0.06f, 1.2f, b - a), Almagra, 1.5f, false);
                Box(t, "Plaster", new Vector3(x, floor + 1.2f + (top - 1.2f) / 2f, (a + b) / 2f), new Vector3(0.06f, top - 1.2f, b - a), Plaster, 2f, false);
            }
            foreach (float z in new[] { 46f, 52f, 56.5f })
                Box(t, "Ataurique", new Vector3(s < 0 ? x0 + 0.08f : x1 - 0.08f, floor + 3.2f, z), new Vector3(0.06f, 3.2f, 1.6f), Ataurique, 1f, false);
        }
        Box(t, "Dado", new Vector3((x0 + x1) / 2f, floor + 0.6f, z1 - 1.03f), new Vector3(x1 - x0, 1.2f, 0.06f), Almagra, 1.5f, false);
        // the throne niche: a blind horseshoe arch in carved stone
        var niche = Node("ThroneNiche", t, new Vector3(-5f, floor, z1 - 1.25f), 0);
        Arch(niche, 0, 4.2f, 2.8f, 6.2f, 0.4f, 30f, Ataurique);
        Box(t, "NicheBack", new Vector3(-5f, floor + 3.1f, z1 - 1.08f), new Vector3(3f, 6.2f, 0.05f), Ataurique, 1f, false);
        // roof: tiles, a wooden ceiling below, a parapet open at the north-east (the hidden way up)
        Box(t, "Roof", new Vector3((x0 + x1) / 2f, roofY + 0.2f, (z0 + z1) / 2f - 0.25f), new Vector3(x1 - x0 + 1f, 0.4f, z1 - z0 + 0.5f), RoofTiles, 2.5f);
        Box(t, "Ceiling", new Vector3((x0 + x1) / 2f, roofY - 0.03f, (z0 + z1 + 1f) / 2f), new Vector3(x1 - x0 - 0.6f, 0.06f, z1 - z0 - 1f), Wood, 2f, false);
        float py = roofY + 0.4f + 0.4f;
        Box(t, "RoofParapet", new Vector3((x0 + x1) / 2f, py, z0 - 0.3f), new Vector3(x1 - x0 + 1f, 0.8f, 0.4f), Ashlar);
        Box(t, "RoofParapet", new Vector3(x0 - 0.3f, py, (z0 + z1) / 2f), new Vector3(0.4f, 0.8f, z1 - z0), Ashlar);
        Box(t, "RoofParapet", new Vector3(x1 + 0.3f, py, (z0 + z1) / 2f), new Vector3(0.4f, 0.8f, z1 - z0), Ashlar);
        Box(t, "RoofParapet", new Vector3((x0 - 2.5f) / 2f - 0.25f, py, z1 - 0.2f), new Vector3(-2.5f - x0 + 0.5f, 0.8f, 0.4f), Ashlar);
        Box(t, "RoofParapet", new Vector3((1.5f + x1) / 2f + 0.25f, py, z1 - 0.2f), new Vector3(x1 - 1.5f + 0.5f, 0.8f, 0.4f), Ashlar);
        // fallen ashlars on the upper terrace behind it: three steps of 0.9 m up to the roof
        for (int i = 0; i < 3; i++)
        {
            float h = 0.9f * (i + 1);
            Box(t, "FallenAshlar", new Vector3(-0.5f, Upper + h / 2f, UpSouth + 0.55f + (2 - i) * 0.9f), new Vector3(1.6f, h, 0.9f), Ashlar, 1.5f);
        }
        // lanterns on the patio, lamps inside for the night
        foreach (float x in new[] { -21f, -13f, 3f, 11f }) Lantern(t, new Vector3(x, floor, 33f));
        foreach (float z in new[] { 45f, 53f }) NightLights.Lamp(t, new Vector3(-5f, floor + 5.5f, z), new Vector3(0.3f, 0.4f, 0.3f), night, 4f);
    }

    /// <summary>The Great Portico: eleven arches on piers (the middle one a tall horseshoe), a walk on top reached by a stair from the garden side.</summary>
    private static void Portico()
    {
        var t = Node("Portico", root, Vector3.zero, 0);
        const float x = 36.5f, z0 = -24f, bays = 11, bay = 6f, top = 8.5f;
        var node = Node("Arcade", t, new Vector3(x, Middle, z0), -90f);   // local x along +z
        for (int k = 0; k <= bays; k++)
            LBox(node, "Pier", new Vector3(k * bay, top / 2f, 0), new Vector3(0.9f, top, 3f), Ashlar);
        for (int i = 0; i < bays; i++)
        {
            var b = Node("Bay", node, node.TransformPoint(new Vector3(i * bay + bay / 2f, 0, 0)), node.eulerAngles.y);
            bool centre = i == 5;
            Arch(b, 0, bay - 0.9f, centre ? 4.4f : 3.4f, top, 3f, centre ? 30f : 6f, Ashlar);
        }
        // the walk on top with a parapet on both sides (gap where the stair arrives)
        float walk = Middle + top;
        float zEnd = z0 + bays * bay;
        Box(t, "Parapet", new Vector3(x + 1.3f, walk + 0.45f, (z0 + zEnd) / 2f), new Vector3(0.4f, 0.9f, zEnd - z0), Ashlar);
        Box(t, "Parapet", new Vector3(x - 1.3f, walk + 0.45f, (z0 + 26f) / 2f), new Vector3(0.4f, 0.9f, 26f - z0), Ashlar);
        Box(t, "Parapet", new Vector3(x - 1.3f, walk + 0.45f, (29f + zEnd) / 2f), new Vector3(0.4f, 0.9f, zEnd - 29f), Ashlar);
        Box(t, "Parapet", new Vector3(x, walk + 0.45f, z0 - 0.25f), new Vector3(3f, 0.9f, 0.4f), Ashlar);
        Box(t, "Parapet", new Vector3(x, walk + 0.45f, zEnd + 0.25f), new Vector3(3f, 0.9f, 0.4f), Ashlar);
        // the stair along the back, from the north end up to the walk
        Buildings.Stairs(t, new Vector3(x - 2.6f, Middle, zEnd), Vector3.back, top, 2f, Ashlar);
        Lantern(t, new Vector3(x - 4.2f, Middle, zEnd - 1f));
    }

    /// <summary>The Plaza de Armas: the parade ground in front of the Portico, with foundations, fallen column drums and trees in planters.</summary>
    private static void PlazaDeArmas()
    {
        var t = Node("Plaza", root, Vector3.zero, 0);
        Slab(t, "Plaza", Rect.MinMaxRect(38f, MidSouth, Half + 4f, 44f), Middle + 0.04f, 0.1f, Sand, 3f);
        // low foundations of lost buildings (cover), fallen drums
        Ruin(t, new Vector3(52f, Middle, 30f), new Vector3(66f, Middle, 30f), 1.5f, 0.7f, Ashlar);
        Ruin(t, new Vector3(66f, Middle, 30f), new Vector3(66f, Middle, 20f), 1.3f, 0.7f, Ashlar);
        Ruin(t, new Vector3(76f, Middle, -6f), new Vector3(90f, Middle, -6f), 1.6f, 0.7f, Ashlar);
        Ruin(t, new Vector3(76f, Middle, -6f), new Vector3(76f, Middle, -18f), 1.2f, 0.7f, Ashlar);
        Ruin(t, new Vector3(50f, Middle, -14f), new Vector3(62f, Middle, -14f), 1.1f, 0.7f, Ashlar);
        Ruin(t, new Vector3(80f, Middle, 26f), new Vector3(80f, Middle, 38f), 1.4f, 0.7f, Ashlar);
        for (int i = 0; i < 7; i++)
        {
            var p = new Vector3(R(46f, 90f), Middle + 0.35f, R(-24f, 40f));
            var drum = MeshGen.Object("ColumnDrum", MeshGen.Cylinder(0.35f, 1.2f, 1f, 12), i % 2 == 0 ? MarblePink : Cream, t, p);
            drum.transform.rotation = Quaternion.Euler(0, R(0, 180), 90f);
            var c = drum.AddComponent<CapsuleCollider>();
            c.radius = 0.35f; c.height = 1.2f; c.center = new Vector3(0, 0.6f, 0); c.direction = 1;
        }
        // orange trees in square planters along the east side and lanterns
        for (float z = -24f; z < 40f; z += 9f)
        {
            Box(t, "Planter", new Vector3(91f, Middle + 0.3f, z), new Vector3(2.4f, 0.6f, 2.4f), Ashlar, 1.5f);
            OrangeTree(t, new Vector3(91f, Middle + 0.6f, z));
        }
        foreach (var (x, z) in new[] { (44f, -20f), (44f, 10f), (44f, 38f), (70f, 10f) }) Lantern(t, new Vector3(x, Middle, z));
        // the double stair from the lower street up to the plaza
        Buildings.Stairs(t, new Vector3(37f, Lower, MidSouth - 7.2f), Vector3.forward, Middle - Lower, 3.6f, Ashlar);
    }

    /// <summary>
    /// The Calle en Rampa: a vaulted ramp street in two flights joined by a hairpin, from the upper terrace
    /// down to the plaza; dark inside, a natural choke point.
    /// </summary>
    private static void RampStreet()
    {
        var t = Node("CalleEnRampa", root, Vector3.zero, 0);
        const float xa = 48f, xb = 90f, ceiling = 13.2f;
        // lane B (south, z 46..51) from the plaza up to the hairpin; lane A (north, z 54..59) on to the upper terrace
        Underground.Wedge(t, "Ramp", new Vector3(50f, Middle, 48.5f), new Vector3(86f, 7f, 48.5f), 5f, Paving);
        Box(t, "RampBase", new Vector3((xa + xb) / 2f + 1f, (Middle + 7f) / 2f, 56.5f), new Vector3(xb - xa - 2f, 7f - Middle, 5f), Ashlar);
        Box(t, "Hairpin", new Vector3(88f, (Middle + 7f) / 2f, 50f), new Vector3(4f, 7f - Middle, 8f), Paving);
        Underground.Wedge(t, "Ramp", new Vector3(86f, 7f, 56.5f), new Vector3(55f, Upper, 56.5f), 5f, Paving);
        Box(t, "TopLanding", new Vector3(52.5f, (7f + Upper) / 2f, 56.5f), new Vector3(5f, Upper - 7f, 5f), Paving);
        // walls: outside, the middle wall between the lanes, the west end of lane A; the roof and its vaults
        Box(t, "RampWall", new Vector3((xa + xb) / 2f, (Middle + ceiling) / 2f, 45.5f), new Vector3(xb - xa + 2f, ceiling - Middle, 1f), Ashlar);
        Box(t, "RampWall", new Vector3((xa + 86f) / 2f, (Middle + ceiling) / 2f, 52.5f), new Vector3(86f - xa, ceiling - Middle, 3f), Ashlar);
        Box(t, "RampWall", new Vector3(xb + 0.5f, (Middle + ceiling) / 2f, 52.5f), new Vector3(1f, ceiling - Middle, 15f), Ashlar);
        Box(t, "RampWall", new Vector3(49f, (Middle + ceiling) / 2f, 56.5f), new Vector3(2f, ceiling - Middle, 5f), Ashlar);
        Box(t, "RampWall", new Vector3((54.2f + xb) / 2f, (Middle + ceiling) / 2f, 59.5f), new Vector3(xb - 54.2f, ceiling - Middle, 1f), Ashlar);
        Box(t, "RampWall", new Vector3(49f, (Middle + ceiling) / 2f, 59.5f), new Vector3(2f, ceiling - Middle, 1f), Ashlar);
        Box(t, "RampRoof", new Vector3((xa + xb) / 2f + 0.5f, ceiling + 0.2f, 52.5f), new Vector3(xb - xa + 1f, 0.4f, 15f), Ashlar);
        foreach (float z in new[] { 48.5f, 56.5f })
        {
            var vault = MeshGen.Object("Vault", MeshGen.Cylinder(1f, xb - xa, 2f, 14), RoofTiles, t, new Vector3(xa, ceiling + 0.75f, z));
            vault.transform.rotation = Quaternion.Euler(0, 0, -90f);
            vault.transform.localScale = new Vector3(0.7f, 1f, 2.6f);
        }
        foreach (float x in new[] { 60f, 76f })
            foreach (float z in new[] { 48.5f, 56.5f })
                NightLights.Lamp(t, new Vector3(x, ceiling - 0.6f, z), new Vector3(0.2f, 0.3f, 0.2f), night, 2.6f);
    }

    /// <summary>The Camino de Ronda: a narrow covered way climbing from the lower town to the middle terrace on the west.</summary>
    private static void CaminoDeRonda()
    {
        var t = Node("CaminoDeRonda", root, Vector3.zero, 0);
        const float x = -48f, w = 2.8f, zLow = -78f, zMid = MidSouth, zEnd = 18f, head = 3.2f;
        Underground.Wedge(t, "Ramp", new Vector3(x, Lower, zLow), new Vector3(x, Middle, zMid), w, Paving);
        float len = zMid - zLow, pitch = Mathf.Atan2(Middle - Lower, len) * Mathf.Rad2Deg;
        foreach (float s in new[] { -1f, 1f })
        {
            // the walls along the ramp follow its slope; on the terrace they are straight
            var wall = MeshGen.TexBox("RondaWall", new Vector3(x + s * (w / 2f + 0.3f), (Lower + Middle) / 2f + (head - 1f) / 2f, (zLow + zMid) / 2f), new Vector3(0.6f, head + 1f, len + 0.6f), Ashlar, 2f, t);
            wall.transform.rotation = Quaternion.Euler(-pitch, 0, 0);
            Box(t, "RondaWall", new Vector3(x + s * (w / 2f + 0.3f), Middle + head / 2f, (zMid + zEnd) / 2f), new Vector3(0.6f, head, zEnd - zMid), Ashlar);
        }
        var roof = MeshGen.TexBox("RondaRoof", new Vector3(x, (Lower + Middle) / 2f + head + 0.15f, (zLow + zMid) / 2f), new Vector3(w + 1.2f, 0.3f, len + 0.3f), Ashlar, 2f, t);
        roof.transform.rotation = Quaternion.Euler(-pitch, 0, 0);
        Box(t, "RondaRoof", new Vector3(x, Middle + head + 0.15f, (zMid + zEnd) / 2f), new Vector3(w + 1.2f, 0.3f, zEnd - zMid), Ashlar);
        Slab(t, "RondaFloor", Rect.MinMaxRect(x - w / 2f, zMid, x + w / 2f, zEnd), Middle + 0.08f, 0.1f, Paving, 2f);
        for (float z = zLow + 8f; z < zEnd; z += 16f)
            NightLights.Lamp(t, new Vector3(x, (z < zMid ? Mathf.Lerp(Lower, Middle, (z - zLow) / len) : Middle) + head - 0.4f, z), new Vector3(0.18f, 0.26f, 0.18f), night, 2.2f);
        // the open ground between the way out and the garden door
        Slab(t, "Court", Rect.MinMaxRect(-50f, zEnd, -42.8f, 30f), Middle + 0.06f, 0.1f, Paving, 2.5f);
    }

    /// <summary>The Lower Garden: four parterres between raised walks, a pool at each end of the cross walk, a fountain.</summary>
    private static void LowerGarden()
    {
        var t = Node("LowerGarden", root, Vector3.zero, 0);
        var area = Rect.MinMaxRect(-40f, -88f, 30f, -38f);
        float walk = Lower + 0.5f;
        Slab(t, "Beds", area, Lower + 0.04f, 0.1f, Grass, 3f);
        var pools = new[] { Rect.MinMaxRect(-36f, -66f, -26f, -56f), Rect.MinMaxRect(16f, -66f, 26f, -56f) };
        var walks = new List<Rect>
        {
            Rect.MinMaxRect(area.xMin, area.yMin, area.xMax, area.yMin + 3f), Rect.MinMaxRect(area.xMin, area.yMax - 3f, area.xMax, area.yMax),
            Rect.MinMaxRect(area.xMin, area.yMin + 3f, area.xMin + 3f, area.yMax - 3f), Rect.MinMaxRect(area.xMax - 3f, area.yMin + 3f, area.xMax, area.yMax - 3f),
            Rect.MinMaxRect(-7f, area.yMin + 3f, -3f, area.yMax - 3f), Rect.MinMaxRect(area.xMin + 3f, -63f, area.xMax - 3f, -59f),
        };
        var cross = Rect.MinMaxRect(-38f, -68f, 28f, -54f);
        foreach (var r in walks)
            Slabs(t, "Walk", r, pools, walk, 0.5f, Gravel, 2.5f);
        foreach (var r in pools) Pool(t, r, walk);
        MeshGen.TexCylinder("Fountain", new Vector3(-5f, walk, -61f), 1.6f, 0.6f, Cream, 1f, t);
        var fw = MeshGen.TexCylinder("FountainWater", new Vector3(-5f, walk + 0.55f, -61f), 1.4f, 0.02f, Water, 2f, t, false);
        fw.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var beds = new[] { Rect.MinMaxRect(area.xMin + 3f, -59f, -7f, area.yMax - 3f), Rect.MinMaxRect(-3f, -59f, area.xMax - 3f, area.yMax - 3f),
                           Rect.MinMaxRect(area.xMin + 3f, area.yMin + 3f, -7f, -63f), Rect.MinMaxRect(-3f, area.yMin + 3f, area.xMax - 3f, -63f) };
        bool Wet(Vector3 p, float margin) => pools.Any(r => p.x > r.xMin - margin && p.x < r.xMax + margin && p.z > r.yMin - margin && p.z < r.yMax + margin);
        foreach (var bed in beds)
        {
            var c = new Vector3(bed.center.x, Lower, bed.center.y);
            float hx = bed.width / 2f - 0.6f, hz = bed.height / 2f - 0.6f;
            foreach (float sz in new[] { -1f, 1f })
                foreach (var (a, b) in new[] { (c + new Vector3(-hx, 0, sz * hz), c + new Vector3(-1.2f, 0, sz * hz)), (c + new Vector3(1.2f, 0, sz * hz), c + new Vector3(hx, 0, sz * hz)) })
                    if (!Wet(a, 1f) && !Wet(b, 1f) && !Wet((a + b) / 2f, 1f)) Hedge(t, a, b, 1f);
            for (float x = bed.xMin + 5f; x < bed.xMax - 3f; x += 9f)
                for (float z = bed.yMin + 5f; z < bed.yMax - 3f; z += 9f)
                    if (rng.NextDouble() < 0.6 && !Wet(new Vector3(x, 0, z), 2.5f)) OrangeTree(t, new Vector3(x, Lower, z));
            Flowers(t, c, hx - 1f, hz - 1f, 8, Lower);
            var palm = c + new Vector3(hx - 1.5f, 0, hz - 1.5f);
            if (!Wet(palm, 2f)) Palm(t, palm, R(7f, 9f));
        }
        foreach (float z in new[] { -44f, -50f, -72f, -78f, -84f })
            foreach (float x in new[] { -8.6f, -1.4f })
                CypressTree(t, new Vector3(x, Lower, z), R(8f, 11f));
        // a low enclosure with gates (cover along the edges)
        Wall(t, new Vector3(area.xMin - 0.3f, Lower, area.yMin), new Vector3(area.xMin - 0.3f, Lower, area.yMax), 1.3f, 0.6f, Ashlar, 28f, 3f, 1.3f);
        Wall(t, new Vector3(area.xMax + 0.3f, Lower, area.yMin), new Vector3(area.xMax + 0.3f, Lower, area.yMax), 1.3f, 0.6f, Ashlar, 25f, 3f, 1.3f);
        Wall(t, new Vector3(area.xMin, Lower, area.yMin - 0.3f), new Vector3(area.xMax, Lower, area.yMin - 0.3f), 1.3f, 0.6f, Ashlar, 35f, 4f, 1.3f);
        foreach (var (x, z) in new[] { (-6.6f, -42f), (-3.4f, -42f), (-6.6f, -80f), (-3.4f, -80f), (-20f, -59.6f), (10f, -59.6f) })
            Lantern(t, new Vector3(x, walk, z));
    }

    /// <summary>
    /// The Aljama mosque, turned towards the qibla (south-east, so at an angle to the city): a raised
    /// walk round it, a courtyard with galleries, the prayer hall of five naves, the mihrab, and the
    /// minaret in the north-west corner of the courtyard with a stair climbing round a core to the top.
    /// </summary>
    private static void Mosque(Vector3 c, float yaw)
    {
        var t = Node("Mosque", root, c, yaw);
        const float hw = 14f, hl = 21f, wallH = 6.5f, floor = 0.5f;
        Vector3 W(float x, float y, float z) => t.TransformPoint(new Vector3(x, y, z));
        Vector3 D(float x, float z) => t.TransformDirection(new Vector3(x, 0, z));
        LBox(t, "Platform", new Vector3(0, floor / 2f, 0), new Vector3(hw * 2f + 7f, floor, hl * 2f + 7f), Ashlar, 2f);
        LBox(t, "Floor", new Vector3(0, floor + 0.02f, -9f), new Vector3(hw * 2f - 1f, 0.04f, 23f), Marble, 2f, false);
        LBox(t, "Sahn", new Vector3(0, floor + 0.02f, 12f), new Vector3(hw * 2f - 1f, 0.04f, 17.4f), Paving, 2.5f, false);
        // the double stair up to the walk on the north side
        foreach (float s in new[] { -1f, 1f })
            Buildings.Stairs(t, W(s * 6f, 0, hl + 3.5f + 1.2f), D(0, -1), floor, 3f, Ashlar);
        // outer walls: doors north and on both sides of the courtyard
        Wall(t, W(-hw, floor, hl), W(hw, floor, hl), wallH, 0.8f, Ashlar, hw, 3f, 3.6f);
        Wall(t, W(-hw, floor, -hl), W(hw, floor, -hl), wallH, 0.8f, Ashlar);
        Wall(t, W(-hw, floor, -hl), W(-hw, floor, hl), wallH, 0.8f, Ashlar, hl + 10f, 2.2f, 3f);
        Wall(t, W(hw, floor, -hl), W(hw, floor, hl), wallH, 0.8f, Ashlar, hl + 10f, 2.2f, 3f);
        // the courtyard galleries on columns
        Arcade(t, W(-10f, floor, 3f), D(0, 1), 3, 11.4f / 3f, 2.8f, 4.6f, 0.5f, 25f, Plaster, true);
        Arcade(t, W(10f, floor, 3f), D(0, 1), 4, 17.2f / 4f, 2.8f, 4.6f, 0.5f, 25f, Plaster, true);
        Arcade(t, W(-7.4f, floor, 17f), D(1, 0), 4, 17.4f / 4f, 2.8f, 4.6f, 0.5f, 25f, Plaster, true);
        LBox(t, "GalleryRoof", new Vector3(-12f, floor + 4.8f, 8.7f), new Vector3(4.4f, 0.4f, 11.4f), RoofTiles, 2f);
        LBox(t, "GalleryRoof", new Vector3(12.1f, floor + 4.8f, 11.6f), new Vector3(4.2f, 0.4f, 17.2f), RoofTiles, 2f);
        LBox(t, "GalleryRoof", new Vector3(1.3f, floor + 4.8f, 19f), new Vector3(17.4f, 0.4f, 4f), RoofTiles, 2f);
        // the prayer hall: a front of seven arches, five naves on columns, a tiled roof
        Arcade(t, W(-hw, floor, 3f), D(1, 0), 7, 4f, 3.2f, wallH, 0.8f, 28f, Ashlar, false, true, 0.8f);
        foreach (float x in new[] { -8.4f, -2.8f, 2.8f, 8.4f })
            Arcade(t, W(x, floor, 2.2f), D(0, -1), 6, 22.8f / 6f, 3.2f, wallH, 0.5f, 28f, Plaster, true, false);
        LBox(t, "HallRoof", new Vector3(0, floor + wallH + 0.2f, -9f), new Vector3(hw * 2f + 0.8f, 0.4f, 24.8f), RoofTiles, 2.5f);
        LBox(t, "HallCeiling", new Vector3(0, floor + wallH - 0.03f, -9f), new Vector3(hw * 2f - 1f, 0.06f, 23f), Wood, 2f, false);
        // the mihrab in the qibla wall
        var mihrab = Node("Mihrab", t, W(0, floor, -hl + 0.6f), t.eulerAngles.y);
        Arch(mihrab, 0, 2.6f, 2f, 4.2f, 0.4f, 32f, Ataurique);
        LBox(t, "MihrabBack", new Vector3(0, floor + 2.1f, -hl + 0.42f), new Vector3(2.2f, 4.2f, 0.05f), Ataurique, 1f, false);
        Minaret(t, new Vector3(-10.6f, floor, 17.6f), 14.4f);
        foreach (var (x, z) in new[] { (-6f, 12f), (6f, 12f), (0f, -2f), (0f, -15f) })
            NightLights.Lamp(t, W(x, floor + (z > 3f ? 3f : 5f), z), new Vector3(0.22f, 0.32f, 0.22f), night, 3f);
        foreach (var (x, z) in new[] { (-17f, 24f), (17f, 24f), (-17f, -24f), (17f, -24f) })
            CypressTree(t, W(x, 0, z), R(9f, 11f));
    }

    /// <summary>
    /// A square minaret (local position of its foot in the mosque frame): walls round a core, a stair of
    /// flights along the four sides with landings in the corners climbing to a platform with a parapet
    /// at 'height' (open above the last flights so nobody hits their head).
    /// </summary>
    private static void Minaret(Transform mosque, Vector3 local, float height)
    {
        var t = Node("Minaret", mosque, mosque.TransformPoint(local), mosque.eulerAngles.y);
        const float outer = 6f, wall = 0.6f, lane = 1.2f, rise = 1.2f, thick = 0.3f, parapet = 1.1f;
        float inner = outer / 2f - wall;       // 2.4
        float core = inner - lane;             // 1.2
        float corner = (inner + core) / 2f;    // 1.8
        float wallH = height + parapet;
        // walls, a door at the foot of the first flight (south-west corner), the core
        LBox(t, "MinaretWall", new Vector3(0, wallH / 2f, inner + wall / 2f), new Vector3(outer, wallH, wall), Ashlar);
        LBox(t, "MinaretWall", new Vector3(inner + wall / 2f, wallH / 2f, 0), new Vector3(wall, wallH, inner * 2f), Ashlar);
        LBox(t, "MinaretWall", new Vector3(-inner - wall / 2f, wallH / 2f, 0), new Vector3(wall, wallH, inner * 2f), Ashlar);
        LBox(t, "MinaretWall", new Vector3((-core + outer / 2f) / 2f, wallH / 2f, -inner - wall / 2f), new Vector3(outer / 2f + core, wallH, wall), Ashlar);
        LBox(t, "MinaretWall", new Vector3((-outer / 2f - inner) / 2f, wallH / 2f, -inner - wall / 2f), new Vector3(outer / 2f - inner, wallH, wall), Ashlar);
        LBox(t, "DoorTop", new Vector3(-corner, 2.4f + (wallH - 2.4f) / 2f, -inner - wall / 2f), new Vector3(lane, wallH - 2.4f, wall), Ashlar);
        LBox(t, "Core", new Vector3(0, height / 2f, 0), new Vector3(core * 2f, height, core * 2f), Ashlar);
        // the stair: flight k runs along side k % 4 (south, east, north, west), 6 steps of 0.2 m
        var corners = new[] { new Vector2(-corner, -corner), new Vector2(corner, -corner), new Vector2(corner, corner), new Vector2(-corner, corner) };
        int flights = Mathf.RoundToInt(height / rise);
        height = flights * rise;
        var holes = new List<Rect>();
        Rect Cell(Vector2 c) => new Rect(c.x - lane / 2f, c.y - lane / 2f, lane, lane);
        for (int k = 0; k < flights; k++)
        {
            var a = corners[k % 4];
            var b = corners[(k + 1) % 4];
            bool alongX = Mathf.Abs(b.x - a.x) > 0.1f;
            var dir = (b - a).normalized;
            for (int st = 0; st < 6; st++)
            {
                float f = (st + 0.5f) / 6f;
                var p = (a + b) / 2f + dir * (core * 2f) * (f - 0.5f);
                float h = k * rise + 0.2f * (st + 1);
                LBox(t, "Step", new Vector3(p.x, h - thick / 2f, p.y), alongX ? new Vector3(core * 2f / 6f, thick, lane) : new Vector3(lane, thick, core * 2f / 6f), Paving, 1f);
            }
            if (k < flights - 1) LBox(t, "Landing", new Vector3(b.x, (k + 1) * rise - thick / 2f, b.y), new Vector3(lane, thick, lane), Paving, 1f);
            // flights that rise into the last 2.1 m below the top: open above
            if ((k + 1) * rise > height - 2.1f)
            {
                var mid = (a + b) / 2f;
                holes.Add(alongX ? new Rect(mid.x - core, mid.y - lane / 2f, core * 2f, lane) : new Rect(mid.x - lane / 2f, mid.y - core, lane, core * 2f));
                holes.Add(Cell(a));
            }
        }
        var top = new Rect(-inner, -inner, inner * 2f, inner * 2f);
        foreach (var r in Underground.Cover(top, holes))
            LBox(t, "TopFloor", new Vector3(r.center.x, height - thick / 2f, r.center.y), new Vector3(r.width, thick, r.height), Paving, 1f);
        foreach (var (cx, cz, sx, sz) in new[] { (0f, outer / 2f, outer + 0.3f, 0.75f), (0f, -outer / 2f, outer + 0.3f, 0.75f), (outer / 2f, 0f, 0.75f, outer - 0.45f), (-outer / 2f, 0f, 0.75f, outer - 0.45f) })
            LBox(t, "Coping", new Vector3(cx * 0.95f, wallH + 0.08f, cz * 0.95f), new Vector3(sx, 0.16f, sz), Cream, 1f, false);
        var mark = new GameObject("MinaretTop");
        mark.transform.SetParent(t, false);
        mark.transform.localPosition = new Vector3(0, height + 1.6f, 0);
        NightLights.Lamp(t, t.TransformPoint(new Vector3(0, height + 0.6f, inner - 0.3f)), new Vector3(0.2f, 0.3f, 0.2f), night, 3f);
    }

    /// <summary>
    /// The houses on the west of the lower terrace: Casa de Ya'far (a triple arcade on columns over a
    /// violet stone courtyard with a marble basin), the service houses behind a narrow alley, the Patio
    /// de la Pila, the Casa de la Alberca; streets between them, broken walls.
    /// </summary>
    private static void Houses()
    {
        var t = Node("Houses", root, Vector3.zero, 0);
        const float h = 4.5f, th = 0.6f;
        Slab(t, "Street", Rect.MinMaxRect(-56f, -90f, -51f, MidSouth - 4f), Lower + 0.05f, 0.1f, Paving, 2.5f);
        // Casa de Ya'far: x -84..-60, z -56..-36
        float x0 = -84f, x1 = -60f, z0 = -56f, z1 = -36f;
        Slab(t, "YafarCourt", Rect.MinMaxRect(-78f, -52f, -66f, -42f), Lower + 0.06f, 0.1f, Violet, 2f);
        MeshGen.TexCylinder("Basin", new Vector3(-72f, Lower, -47f), 0.9f, 0.5f, Cream, 1f, t);
        Wall(t, new Vector3(x0, Lower, z1), new Vector3(x1, Lower, z1), h, th, Ashlar);
        Wall(t, new Vector3(x0, Lower, z0), new Vector3(x1, Lower, z0), h, th, Ashlar, 12f, 1.6f, 2.8f);
        Wall(t, new Vector3(x0, Lower, z0), new Vector3(x0, Lower, z1), h, th, Ashlar);
        Wall(t, new Vector3(x1, Lower, z0), new Vector3(x1, Lower, z1), h, th, Ashlar, 9f, 1.6f, 2.8f);
        Arcade(t, new Vector3(-77.4f, Lower, -42f), Vector3.right, 3, 3.6f, 2.6f, h, 0.6f, 28f, Plaster, true);
        Wall(t, new Vector3(x0, Lower, -42f), new Vector3(-77.4f, Lower, -42f), h, th, Plaster);
        Wall(t, new Vector3(-66.6f, Lower, -42f), new Vector3(x1, Lower, -42f), h, th, Plaster);
        Box(t, "HallRoof", new Vector3((x0 + x1) / 2f, h + 0.2f, -39f), new Vector3(x1 - x0 + 0.6f, 0.4f, 6.6f), RoofTiles, 2f);
        Wall(t, new Vector3(-78f, Lower, -52f), new Vector3(-78f, Lower, -42f), h, 0.4f, Plaster, 5f, 1.2f, 2.4f);
        Wall(t, new Vector3(-66f, Lower, -52f), new Vector3(-66f, Lower, -42f), h, 0.4f, Plaster, 5f, 1.2f, 2.4f);
        Wall(t, new Vector3(-78f, Lower, -52f), new Vector3(-66f, Lower, -52f), h, 0.4f, Plaster, 6f, 1.4f, 2.4f);
        // the service houses behind the alley, with a bread oven
        Ruin(t, new Vector3(-94f, Lower, -56f), new Vector3(-86f, Lower, -56f), 3.2f, th, Ashlar, 1.5f);
        Ruin(t, new Vector3(-86f, Lower, -56f), new Vector3(-86f, Lower, -36f), 3.4f, th, Ashlar, 1.8f);
        Ruin(t, new Vector3(-94f, Lower, -46f), new Vector3(-87f, Lower, -46f), 2.6f, th, Ashlar, 1f);
        MeshGen.TexCylinder("Oven", new Vector3(-90f, Lower, -40f), 1.1f, 1.4f, Earth, 1f, t);
        // Patio de la Pila: a white marble court with a raised walk round it, a basin
        Slab(t, "PilaCourt", Rect.MinMaxRect(-79f, -77f, -65f, -65f), Lower + 0.06f, 0.1f, Marble, 2f);
        foreach (var r in new[] { Rect.MinMaxRect(-82f, -80f, -62f, -77f), Rect.MinMaxRect(-82f, -65f, -62f, -62f), Rect.MinMaxRect(-82f, -77f, -79f, -65f), Rect.MinMaxRect(-65f, -77f, -62f, -65f) })
            Slab(t, "PilaWalk", r, Lower + 0.35f, 0.35f, Marble, 2f);
        MeshGen.TexCylinder("Pila", new Vector3(-72f, Lower, -71f), 1.2f, 0.6f, Capital, 1f, t);
        Ruin(t, new Vector3(-84f, Lower, -82f), new Vector3(-60f, Lower, -82f), 2.8f, th, Ashlar, 0.8f);
        Ruin(t, new Vector3(-84f, Lower, -82f), new Vector3(-84f, Lower, -60f), 3.2f, th, Ashlar, 1f);
        Ruin(t, new Vector3(-60f, Lower, -82f), new Vector3(-60f, Lower, -66f), 2.4f, th, Ashlar, 0.8f);
        // Casa de la Alberca: a small house round a garden with a pool
        Pool(t, Rect.MinMaxRect(-92f, -78f, -88f, -68f), Lower + 0.3f, 0.5f);
        Ruin(t, new Vector3(-95f, Lower, -84f), new Vector3(-86f, Lower, -84f), 2.4f, th, Ashlar, 0.8f);
        Ruin(t, new Vector3(-86f, Lower, -84f), new Vector3(-86f, Lower, -60f), 2.8f, th, Ashlar, 1f);
        Palm(t, new Vector3(-90.5f, Lower, -64f), 8f);
        CypressTree(t, new Vector3(-58f, Lower, -60f), 9f);
        CypressTree(t, new Vector3(-58f, Lower, -86f), 10f);
        foreach (var (x, z) in new[] { (-58f, -40f), (-58f, -70f), (-72f, -60.5f), (-90f, -50f) }) Lantern(t, new Vector3(x, Lower, z));
        // the south wall of the city with towers
        Box(t, "CityWall", new Vector3(0, (Lower - 3f + 7f) / 2f, -91.7f), new Vector3(Half * 2f + 10f, 10f, 2.5f), Ashlar);
        for (float x = -Half + 6f; x < Half; x += 24f)
            Box(t, "Tower", new Vector3(x, 3f, -91.7f), new Vector3(5f, 12f, 5f), Ashlar);
    }

    /// <summary>Dar al-Mulk, the caliph's house on the highest terrace: three halls behind arcades, a terrace with the view over the city.</summary>
    private static void DarAlMulk()
    {
        var t = Node("DarAlMulk", root, Vector3.zero, 0);
        float y = Upper;
        Slab(t, "Terrace", Rect.MinMaxRect(-94f, UpSouth + 0.5f, -36f, 70f), y + 0.06f, 0.1f, Marble, 2f);
        for (int i = 0; i < 3; i++)
        {
            float x0 = -92f + i * 18f, x1 = x0 + 16f;
            Arcade(t, new Vector3(x0, y, 70f), Vector3.right, 4, 4f, 2.8f, 5f, 0.6f, 28f, Plaster, true);
            Wall(t, new Vector3(x0, y, 78f), new Vector3(x1, y, 78f), 5f, 0.6f, Ashlar, 8f, 1.4f, 2.6f);
            Wall(t, new Vector3(x0, y, 70f), new Vector3(x0, y, 78f), 5f, 0.6f, Ashlar);
            Wall(t, new Vector3(x1, y, 70f), new Vector3(x1, y, 78f), 5f, 0.6f, Ashlar);
            Slab(t, "HallFloor", Rect.MinMaxRect(x0, 70f, x1, 78f), y + 0.06f, 0.1f, Marble, 2f);
            Box(t, "HallRoof", new Vector3((x0 + x1) / 2f, y + 5.2f, 74f), new Vector3(16.6f, 0.4f, 8.6f), RoofTiles, 2f);
            Box(t, "Dado", new Vector3((x0 + x1) / 2f, y + 0.6f, 77.67f), new Vector3(15.4f, 1.2f, 0.05f), Almagra, 1.5f, false);
        }
        // rooms and courtyards behind: broken walls (cover)
        for (int i = 0; i < 4; i++)
        {
            float x = -92f + i * 13f;
            Ruin(t, new Vector3(x, y, 82f), new Vector3(x + 10f, y, 82f), 2.6f, 0.6f, Ashlar, 0.8f);
            Ruin(t, new Vector3(x + 10f, y, 82f), new Vector3(x + 10f, y, 90f), 2.4f, 0.6f, Ashlar, 0.8f);
        }
        foreach (float x in new[] { -88f, -70f, -52f, -40f }) Lantern(t, new Vector3(x, y, 64f));
        CypressTree(t, new Vector3(-38f, y, 66f), 10f);
        CypressTree(t, new Vector3(-38f, y, 74f), 9f);
        // the stair from the middle terrace, west of the halls
        Buildings.Stairs(t, new Vector3(-58f, Middle, UpSouth - 10.8f), Vector3.forward, Upper - Middle, 3f, Ashlar);
    }

    /// <summary>The Edificio Basilical Superior: a portico of five arches over its plaza, three naves on columns, a brick floor, walls broken off.</summary>
    private static void BasilicalHall()
    {
        var t = Node("BasilicalHall", root, Vector3.zero, 0);
        float y = Upper;
        const float x0 = -26f, x1 = 8f, z0 = 70f, z1 = 91f;
        Slab(t, "Plaza", Rect.MinMaxRect(-34f, UpSouth + 0.5f, 12f, z0), y + 0.05f, 0.1f, Paving, 2.5f);
        Slab(t, "BrickFloor", Rect.MinMaxRect(x0, z0, x1, z1), y + 0.06f, 0.1f, BrickFloor, 2f);
        Arcade(t, new Vector3(x0, y, z0), Vector3.right, 5, (x1 - x0) / 5f, 3.4f, 6f, 0.9f, 28f, Ashlar, false, true, 0.8f);
        foreach (float x in new[] { -14.7f, -3.3f })
            Arcade(t, new Vector3(x, y, z0 + 5f), Vector3.forward, 4, (z1 - z0 - 5f) / 4f, 3f, 5f, 0.5f, 25f, Plaster, true);
        Ruin(t, new Vector3(x0, y, z0), new Vector3(x0, y, z1), 5.5f, 0.8f, Ashlar, 2.5f);
        Ruin(t, new Vector3(x1, y, z0), new Vector3(x1, y, z1), 5.5f, 0.8f, Ashlar, 2.5f);
        Ruin(t, new Vector3(x0, y, z1), new Vector3(x1, y, z1), 6f, 0.8f, Ashlar, 3f);
        var niche = Node("Niche", t, new Vector3((x0 + x1) / 2f, y, z1 - 0.65f), 0);
        Arch(niche, 0, 3.6f, 2.4f, 5f, 0.4f, 28f, Ashlar);
        foreach (float x in new[] { -30f, 10f }) Lantern(t, new Vector3(x, y, 66f));
    }

    /// <summary>The North Gate passage: a covered way bent twice, four arched doors in a row: a choke point between the plaza and the north-east.</summary>
    private static void NorthGate()
    {
        var t = Node("NorthGate", root, Vector3.zero, 0);
        float y = Upper;
        const float h = 4.5f, th = 0.6f;
        // seg 1: x 31.5..46, z 84.5..87.5;  seg 2: x 28.5..31.5, z 68.5..87.5;  seg 3: x 12..31.5, z 68.5..71.5
        Box(t, "GateWall", new Vector3((28.5f + 46f) / 2f, y + h / 2f, 87.8f), new Vector3(46f - 28.5f + th, h, th), Ashlar);
        Box(t, "GateWall", new Vector3((31.5f + 46f) / 2f, y + h / 2f, 84.2f), new Vector3(46f - 31.5f, h, th), Ashlar);
        Box(t, "GateWall", new Vector3(28.2f, y + h / 2f, (71.5f + 88.1f) / 2f), new Vector3(th, h, 88.1f - 71.5f), Ashlar);
        Box(t, "GateWall", new Vector3(31.8f, y + h / 2f, (67.9f + 84.5f) / 2f), new Vector3(th, h, 84.5f - 67.9f), Ashlar);
        Box(t, "GateWall", new Vector3((12f + 28.5f) / 2f, y + h / 2f, 71.8f), new Vector3(28.5f - 12f, h, th), Ashlar);
        Box(t, "GateWall", new Vector3((12f + 32.1f) / 2f, y + h / 2f, 68.2f), new Vector3(32.1f - 12f, h, th), Ashlar);
        Box(t, "GateRoof", new Vector3((28f + 46f) / 2f, y + h + 0.2f, 86f), new Vector3(18f, 0.4f, 4.2f), Ashlar);
        Box(t, "GateRoof", new Vector3(30f, y + h + 0.2f, (68f + 84f) / 2f), new Vector3(4.2f, 0.4f, 16f), Ashlar);
        Box(t, "GateRoof", new Vector3((12f + 28f) / 2f, y + h + 0.2f, 70f), new Vector3(16f, 0.4f, 4.2f), Ashlar);
        // four doors: arched walls across the passage
        void Door(Vector3 at, float yaw) => Arch(Node("Door", t, at, yaw), 0, 3f, 2.3f, h, 0.6f, 28f, Ashlar);
        Door(new Vector3(42f, y, 86f), 90f);
        Door(new Vector3(30f, y, 80f), 0f);
        Door(new Vector3(30f, y, 74f), 0f);
        Door(new Vector3(20f, y, 70f), 90f);
        foreach (var p in new[] { new Vector3(36f, y + h - 0.6f, 86f), new Vector3(30f, y + h - 0.6f, 77f), new Vector3(24f, y + h - 0.6f, 70f) })
            NightLights.Lamp(t, p, new Vector3(0.18f, 0.26f, 0.18f), night, 2.4f);
        // the north-east: the army house (Dar al-Yund), broken walls round a courtyard with a pool
        Ruin(t, new Vector3(52f, y, 66f), new Vector3(88f, y, 66f), 2.6f, 0.7f, Ashlar, 0.8f);
        Ruin(t, new Vector3(52f, y, 66f), new Vector3(52f, y, 84f), 3f, 0.7f, Ashlar, 1f);
        Ruin(t, new Vector3(88f, y, 66f), new Vector3(88f, y, 84f), 2.8f, 0.7f, Ashlar, 1f);
        Ruin(t, new Vector3(60f, y, 84f), new Vector3(88f, y, 84f), 2.4f, 0.7f, Ashlar, 0.8f);
        Slab(t, "Court", Rect.MinMaxRect(54f, 67f, 86f, 83f), y + 0.06f, 0.1f, Paving, 2.5f);
        Pool(t, Rect.MinMaxRect(64f, 72f, 76f, 78f), y + 0.4f, 0.5f);
        foreach (float x in new[] { 58f, 82f }) CypressTree(t, new Vector3(x, y, 75f), 9f);
    }

    /// <summary>The north wall of the city, a walk on top reached by two stairs: the longest view of the map.</summary>
    private static void NorthWall()
    {
        var t = Node("NorthWall", root, Vector3.zero, 0);
        float y = Upper, top = 6f;
        Box(t, "NorthWall", new Vector3(0, y + top / 2f, 94.5f), new Vector3(Half * 2f + 10f, top, 3f), Ashlar);
        for (float x = -Half + 1f; x < Half; x += 1.6f)
            Box(t, "Merlon", new Vector3(x, y + top + 0.55f, 95.7f), new Vector3(0.8f, 1.1f, 0.6f), Ashlar, 1f);
        for (float x = -Half + 12f; x < Half; x += 30f)
            Box(t, "Tower", new Vector3(x, y + 4.5f, 95f), new Vector3(5f, 9f, 5f), Ashlar);
        foreach (float x in new[] { -42f, 40f })
            Buildings.Stairs(t, new Vector3(x, y, 92f), Vector3.right, top, 2f, Ashlar);
    }

    /// <summary>Ammunition: on every level, one on the Salón Rico roof and one at the top of the minaret's courtyard.</summary>
    private static void Supplies()
    {
        var t = Node("Supplies", root, Vector3.zero, 0);
        foreach (var (p, yaw) in new[] { (new Vector3(70f, Middle + 0.1f, 8f), 0f), (new Vector3(-30f, Middle + 0.5f, 28.5f), 90f), (new Vector3(-72f, Lower + 0.1f, -60.5f), 0f),
                                          (new Vector3(-60f, Upper + 0.1f, 64f), 0f), (new Vector3(76f, Upper + 0.1f, 80f), 90f), (new Vector3(-12f, Middle + 0.5f + 8.9f, 48f), 0f),
                                          (new Vector3(10f, Lower + 0.5f, -60.5f), 0f), (new Vector3(-44f, Middle + 0.1f, 24f), 0f) })
            Obstacles.AmmoCrate(t, p, yaw);
    }

    /// <summary>Beyond the walls: the Sierra Morena behind (hills of holm oak and pine), the valley in front with its fields and olive groves.</summary>
    private static void Surroundings()
    {
        var t = Node("Surroundings", root, Vector3.zero, 0);
        for (int i = 0; i < 12; i++)
        {
            float x = -260f + i * 47f + R(-10f, 10f);
            var hill = MeshGen.RoundBox("Hill", new Vector3(x, R(8f, 22f), R(150f, 230f)), new Vector3(R(70f, 110f), R(40f, 70f), R(70f, 110f)), 14f, Grass, 10f, t, R(0, 90), false);
            hill.transform.rotation = Quaternion.Euler(0, R(0, 90), 0);
        }
        for (int i = 0; i < 70; i++)
        {
            var p = new Vector3(R(-200f, 200f), Upper, R(104f, 150f));
            Nature.Tree(rng.NextDouble() < 0.5 ? Nature.Pines : Nature.Trees, rng, p, 6f, 11f, t);
        }
        // fields of the valley: strips of green and of ploughed earth, rows of olive trees
        for (int i = 0; i < 10; i++)
        {
            float z = -110f - i * 18f;
            for (int j = 0; j < 6; j++)
            {
                float x = -250f + j * 85f + R(-10f, 10f);
                Box(t, "Field", new Vector3(x, -2.98f, z), new Vector3(R(55f, 80f), 0.06f, 14f), (i + j) % 3 == 0 ? Earth : Grass, 6f, false);
            }
        }
        for (int i = 0; i < 60; i++)
        {
            var p = new Vector3(R(-220f, 220f), -3f, R(-240f, -106f));
            Nature.Tree(Nature.Trees, rng, p, 4f, 5.5f, t);
        }
        // the Guadalquivir far down the valley
        var river = MeshGen.TexBox("Guadalquivir", new Vector3(0, -2.9f, -280f), new Vector3(700f, 0.05f, 26f), AssetSetup.WaterMat("Water_River", new Color(0.05f, 0.16f, 0.17f), new Color(0.2f, 0.36f, 0.34f), 1f, 0.25f), 20f, t, 0f, false);
        river.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // cypresses and palms dotted over the terraces outside the gardens
        foreach (var (x, z) in new[] { (-30f, -34f), (20f, -34f), (-90f, 40f), (-90f, 0f), (-70f, 20f), (40f, 50f), (94f, 52f), (20f, 64f), (-36f, 92f), (88f, -34f) })
            CypressTree(t, new Vector3(x, Level(z), z), R(8f, 11f));
        // the west of the middle terrace: the bath suite next to the hall (broken walls round small rooms)
        Ruin(t, new Vector3(-40f, Middle, 34f), new Vector3(-24f, Middle, 34f), 2.6f, 0.6f, Ashlar, 1f);
        Ruin(t, new Vector3(-40f, Middle, 34f), new Vector3(-40f, Middle, 58f), 3f, 0.6f, Ashlar, 1.2f);
        Ruin(t, new Vector3(-32f, Middle, 34f), new Vector3(-32f, Middle, 52f), 2.2f, 0.5f, Ashlar, 0.8f);
        Ruin(t, new Vector3(-40f, Middle, 46f), new Vector3(-24f, Middle, 46f), 2.4f, 0.5f, Ashlar, 0.8f);
        Ruin(t, new Vector3(-90f, Middle, 30f), new Vector3(-66f, Middle, 30f), 1.8f, 0.6f, Ashlar, 0.6f);
        Ruin(t, new Vector3(-66f, Middle, 10f), new Vector3(-66f, Middle, 30f), 1.6f, 0.6f, Ashlar, 0.6f);
        Ruin(t, new Vector3(-90f, Middle, -10f), new Vector3(-70f, Middle, -10f), 1.4f, 0.6f, Ashlar, 0.6f);
    }

    /// <summary>Bot waypoints and spawn points on the open ground of the three terraces, the stairs and ramps linking them.</summary>
    private static void BuildNavigation(MapLayout layout)
    {
        Physics.SyncTransforms();
        var points = new List<Vector3>();
        var index = new Dictionary<Vector2Int, int>();
        const float step = 6f;
        int n = Mathf.FloorToInt((Half - 4f) / step);
        int mask = World.EnvMask | (1 << World.FenceLayer);
        for (int gx = -n; gx <= n; gx++)
            for (int gz = -n; gz <= n; gz++)
            {
                var p = new Vector3(gx * step, 0, gz * step);
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out var hit, 90f, mask)) continue;
                if (hit.collider.gameObject.layer == World.FenceLayer) continue;
                p.y = hit.point.y;
                string name = hit.collider.name;
                bool way = name == "Ramp" || name == "Step";
                if (!way && (p.y < Level(p.z) - 0.2f || p.y > Level(p.z) + 1.0f)) continue;   // roofs and walls are not for spawning
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.5f, mask)) continue;
                index[new Vector2Int(gx, gz)] = points.Count;
                points.Add(p);
            }
        var links = new List<Vector2Int>();
        foreach (var pair in index)
            foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) })
            {
                if (!index.TryGetValue(pair.Key + d, out int other)) continue;
                var a = points[pair.Value];
                var b = points[other];
                if (Mathf.Abs(a.y - b.y) > 1.6f) continue;
                if (Physics.SphereCast(a + Vector3.up * 1.2f, 0.4f, (b - a).normalized, out _, Vector3.Distance(a, b), mask)) continue;
                links.Add(new Vector2Int(pair.Value, other));
            }
        layout.Waypoints = points.ToArray();
        layout.Links = links.ToArray();
        // spawns only on the plain terrace ground (not on stairs)
        layout.Spawns = points.Where(p => Mathf.Abs(p.y - Level(p.z)) < 0.7f).ToArray();
    }
}
