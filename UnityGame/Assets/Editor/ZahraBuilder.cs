using System.Collections.Generic;
using System.IO;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Al-Zahra: Madinat al-Zahra, the 10th-century caliphal city below the Sierra Morena near Córdoba, as it
/// stood in its golden age (nothing broken), in spring, inside its walls, on three terraces stepping down
/// to the south:
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
    public const float Half = 128f;
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
    private static Material hedge;
    private static Material HedgeMat => hedge;
    private static Material Lawn => PortBuilder.Pbr("Z_Lawn", "Grass004", new Color(0.8f, 0.98f, 0.62f), 0.12f);
    private static Material Wheat => PortBuilder.Pbr("Z_Wheat", "Grass004", new Color(1.1f, 1.05f, 0.5f), 0.1f);
    private static Material TownGround => PortBuilder.Pbr("Z_TownGround", "Gravel036L", new Color(0.98f, 0.88f, 0.72f), 0.1f);
    private static Material Earth => PortBuilder.Pbr("Z_Earth", "Ground104", new Color(0.82f, 0.68f, 0.5f), 0.08f);
    private static Material RoofTiles => PortBuilder.Pbr("Z_RoofTiles", "RoofingTiles006", new Color(0.9f, 0.52f, 0.36f), 0.3f);
    private static Material Wood => PortBuilder.Pbr("Z_Wood", "Wood049", new Color(0.62f, 0.44f, 0.3f), 0.4f);
    private static Material PalmTrunk => PortBuilder.Pbr("Z_PalmTrunk", "Wood049", new Color(0.62f, 0.5f, 0.38f), 0.2f);
    private static Material MarblePink => AssetSetup.Solid("Z_MarblePink", new Color(0.86f, 0.64f, 0.62f), 0.75f);
    private static Material MarbleBlue => AssetSetup.Solid("Z_MarbleBlue", new Color(0.6f, 0.67f, 0.76f), 0.75f);
    private static Material Capital => AssetSetup.Solid("Z_Capital", new Color(0.94f, 0.93f, 0.88f), 0.5f);
    private static Material Leaf => AssetSetup.Solid("Z_PalmLeaf", new Color(0.3f, 0.48f, 0.18f), 0.25f);
    private static Material Cypress => PortBuilder.Pbr("Z_Cypress", "Grass001", new Color(0.3f, 0.48f, 0.24f), 0.1f);
    private static Material Orange => AssetSetup.Solid("Z_Orange", new Color(1f, 0.55f, 0.08f), 0.5f);
    private static Material Bronze => AssetSetup.Solid("Z_Bronze", new Color(0.25f, 0.18f, 0.1f), 0.5f, 0.6f);
    private static Material Water => AssetSetup.WaterMat("Water_Pool", new Color(0.04f, 0.17f, 0.18f), new Color(0.18f, 0.42f, 0.4f), 3f, 0.15f);
    private static Material LimeOchre => PortBuilder.Pbr("Z_LimeOchre", "Plaster001", new Color(0.96f, 0.86f, 0.66f), 0.3f);
    private static Material Gold => AssetSetup.Solid("Z_Gold", new Color(0.85f, 0.65f, 0.25f), 0.75f, 0.85f);
    private static Material Velvet => AssetSetup.Solid("Z_Velvet", new Color(0.5f, 0.06f, 0.08f), 0.2f);
    private static Material Terracotta => AssetSetup.Solid("Z_Terracotta", new Color(0.7f, 0.36f, 0.22f), 0.2f);
    private static Material Signboard => PortBuilder.Pbr("Z_Signboard", "Wood049", new Color(0.35f, 0.22f, 0.14f), 0.4f);
    private static Material lattice, carpet;

    /// <summary>Running water, the waves drifting the way it flows (one material per direction).</summary>
    private static Material Flowing(Vector3 dir)
    {
        dir.y = 0;
        dir.Normalize();
        string key = $"Water_Flow_{Mathf.RoundToInt(dir.x * 10)}_{Mathf.RoundToInt(dir.z * 10)}";
        return AssetSetup.WaterMat(key, new Color(0.05f, 0.18f, 0.19f), new Color(0.22f, 0.46f, 0.44f), 4f, 0.22f, new Vector3(dir.x, 1.4f, dir.z));
    }

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

    /// <summary>Wooden lattice (for the windows) and a red carpet with a border and a medallion.</summary>
    private static void MakeLatticeAndCarpet()
    {
        var lt = SaveTexture("Lattice", 128, (x, y) =>
        {
            float u = (x + 0.5f) / 16f, v = (y + 0.5f) / 16f;
            float a = Mathf.Abs((u + v) % 1f - 0.5f), b = Mathf.Abs((u - v + 8f) % 1f - 0.5f);
            bool bar = a > 0.38f || b > 0.38f;
            var c = bar ? new Color(0.38f, 0.24f, 0.14f) : new Color(0.06f, 0.05f, 0.05f);
            c.a = 1f;
            return c;
        }, false);
        lattice = AssetSetup.Mat("Z_Lattice", Color.white, 0.3f, 0f, lt);
        var ct = SaveTexture("Carpet", 256, (x, y) =>
        {
            float u = x / 256f, v = y / 256f;
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            float du = u - 0.5f, dv = v - 0.5f, r = Mathf.Sqrt(du * du + dv * dv);
            Color c = new Color(0.55f, 0.07f, 0.08f);
            if (edge < 0.08f) c = new Color(0.1f, 0.12f, 0.3f);
            else if (edge < 0.1f || Mathf.Abs(r - 0.22f) < 0.012f) c = new Color(0.85f, 0.68f, 0.3f);
            else if (r < 0.2f) c = Mathf.Sin(Mathf.Atan2(dv, du) * 8f) > 0.2f ? new Color(0.85f, 0.68f, 0.3f) : new Color(0.1f, 0.12f, 0.3f);
            else if ((Mathf.FloorToInt(u * 12f) + Mathf.FloorToInt(v * 12f)) % 2 == 0 && edge > 0.14f) c *= 0.85f;
            c.a = 1f;
            return c;
        }, false);
        carpet = AssetSetup.Mat("Z_Carpet", Color.white, 0.1f, 0f, ct);
    }

    /// <summary>Clipped myrtle: small overlapping oval leaves in dark and fresh greens, shadowed between them (0.8 m per repeat).</summary>
    private static void MakeMyrtle()
    {
        const int size = 256;
        var h = new float[size, size];
        var col = new Color[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) { h[x, y] = 0f; col[x, y] = new Color(0.05f, 0.11f, 0.04f); }
        var r = new System.Random(77);
        for (int i = 0; i < 2600; i++)
        {
            float cx = (float)r.NextDouble() * size, cy = (float)r.NextDouble() * size;
            float ang = (float)r.NextDouble() * Mathf.PI, len = 5f + (float)r.NextDouble() * 4f, wid = len * 0.45f;
            float top = 0.4f + (float)r.NextDouble() * 0.6f;
            var leaf = Color.Lerp(new Color(0.13f, 0.26f, 0.09f), new Color(0.3f, 0.46f, 0.15f), (float)r.NextDouble()) * (0.7f + 0.5f * top);
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            for (int oy = -6; oy <= 6; oy++)
                for (int ox = -6; ox <= 6; ox++)
                {
                    float u = (ox * ca + oy * sa) / (len / 2f), v = (-ox * sa + oy * ca) / (wid / 2f);
                    float q = u * u + v * v;
                    if (q > 1f) continue;
                    int px = ((int)cx + ox + size) % size, py = ((int)cy + oy + size) % size;
                    float hh = top * (1f - q * 0.6f);
                    if (hh < h[px, py]) continue;
                    h[px, py] = hh;
                    col[px, py] = leaf * (0.85f + 0.15f * (1f - Mathf.Abs(v)));
                    col[px, py].a = 1f;
                }
        }
        var tex = SaveTexture("Myrtle", size, (x, y) => col[x, y], false);
        var nrm = NormalFrom("Myrtle_N", size, h, 4f);
        hedge = AssetSetup.Mat("Z_Myrtle", Color.white, 0.25f, 0f, tex, nrm);
    }

    // ------------------------------------------------------------------ boards with writing

    private static Font font;

    /// <summary>Width of a letter of the UA font in ems (Arabic forms 0.6, capitals 0.69, small letters 0.58).</summary>
    private static float Advance(char c) => c == ' ' ? 0.3f : Rtl.IsRtl(c) ? 0.6f : char.IsUpper(c) ? 0.69f : char.IsDigit(c) ? 0.6f : char.IsLower(c) ? 0.58f : 0.38f;

    /// <summary>Words of a text in lines no wider than 'width' ems (Arabic is wrapped before it is shaped).</summary>
    private static List<string> Wrap(string text, float width)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var line = "";
            float used = 0f;
            foreach (var w in para.Split(' '))
            {
                float ww = w.Sum(Advance);
                if (line.Length > 0 && used + 0.3f + ww > width) { lines.Add(line); line = w; used = ww; }
                else { line = line.Length == 0 ? w : line + " " + w; used += (used > 0f ? 0.3f : 0f) + ww; }
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// A block of writing on the front (local -Z face) of a board: local centre, size in metres, the
    /// letters at most 'maxSize' cm and smaller until the whole text fits.
    /// </summary>
    private static void Writing(Transform board, Vector3 local, Vector2 size, string text, float maxSize, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
    {
        // every board draws its letters at one size from the font (a small font texture, made once);
        // the canvas is scaled so the letters come out at the size that fits
        const int fs = 40;
        if (font == null) font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/UAFont.ttf");
        float em = maxSize * 0.01f;   // letter size in metres
        var lines = Wrap(text, size.x / em);
        while (em > 0.02f && (lines.Count * 1.62f * em > size.y || lines.Any(l => l.Sum(Advance) * em > size.x)))
        {
            em *= 0.94f;
            lines = Wrap(text, size.x / em);
        }
        float scale = em / fs;
        float w = size.x / scale, h = size.y / scale;
        var go = new GameObject("Writing");
        go.transform.SetParent(board, false);
        go.transform.localPosition = local;
        go.transform.localScale = Vector3.one * scale;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        var t = new GameObject("Text").AddComponent<Text>();
        t.transform.SetParent(go.transform, false);
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        t.font = font;
        t.fontSize = fs;
        t.fontStyle = style;
        t.color = color;
        t.alignment = anchor;
        t.supportRichText = false;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.lineSpacing = 1f;
        t.text = string.Join("\n", lines.Select(Rtl.Shape));
    }

    /// <summary>
    /// A name board (dark wood, gilt frame) with an Arabic line over an English line, its face towards
    /// 'yaw' + 180 (it is read from the side its local -Z points to).
    /// </summary>
    private static Transform Sign(Transform parent, Vector3 pos, float yaw, string arabic, string english, float width = 3f, float height = 1.1f)
    {
        var t = Node("Sign", parent, pos, yaw);
        LBox(t, "SignBoard", Vector3.zero, new Vector3(width, height, 0.12f), Signboard, 1f, false);
        LBox(t, "SignFrame", new Vector3(0, height / 2f, -0.03f), new Vector3(width + 0.1f, 0.06f, 0.1f), Gold, 1f, false);
        LBox(t, "SignFrame", new Vector3(0, -height / 2f, -0.03f), new Vector3(width + 0.1f, 0.06f, 0.1f), Gold, 1f, false);
        foreach (float s in new[] { -1f, 1f })
            LBox(t, "SignFrame", new Vector3(s * width / 2f, 0, -0.03f), new Vector3(0.06f, height, 0.1f), Gold, 1f, false);
        var gilt = new Color(0.98f, 0.84f, 0.45f);
        Writing(t, new Vector3(0, height * 0.2f, -0.07f), new Vector2(width - 0.2f, height * 0.5f), arabic, height * 36f, gilt);
        Writing(t, new Vector3(0, -height * 0.24f, -0.07f), new Vector2(width - 0.2f, height * 0.4f), english, height * 20f, new Color(0.95f, 0.9f, 0.8f));
        return t;
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
            MeshUtility.SetMeshCompression(m, ModelImporterMeshCompression.Medium);
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

    /// <summary>A Mediterranean cypress: a pine of the nature kit drawn in tall and narrow (a spindle if the kit is missing).</summary>
    private static void CypressTree(Transform parent, Vector3 p, float height)
    {
        var pine = Nature.Place(Nature.Pines[rng.Next(Nature.Pines.Length)], p, R(0, 360), height, parent);
        if (pine != null)
        {
            pine.transform.localScale = Vector3.Scale(pine.transform.localScale, new Vector3(0.24f, 1f, 0.24f));
            return;
        }
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
        var crown = MeshObject(t, "Crown", cypressMesh, Cypress, false);
        crown.transform.localPosition = new Vector3(0, 0.5f, 0);
        crown.transform.localScale = new Vector3(height * 0.15f, height - 0.5f, height * 0.15f);
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
        if (frondMesh == null) frondMesh = FrondMesh();
        for (int i = 0; i < 14; i++)
        {
            var frond = MeshObject(t, "Frond", frondMesh, Leaf, false);
            frond.transform.SetPositionAndRotation(top, Quaternion.Euler(0, i * 360f / 14f + R(-10, 10), 0) * Quaternion.Euler(R(-25f, 5f), 0, 0));
            frond.transform.localScale = Vector3.one * R(0.85f, 1.1f);
        }
    }

    private static Mesh frondMesh;

    /// <summary>
    /// A palm frond 2.8 m long from the crown along +Z: it rises, then droops, folded in a V along its
    /// spine and narrowing to the tip; seen from both sides.
    /// </summary>
    private static Mesh FrondMesh()
    {
        var b = new Builder(1f);
        const int n = 9;
        const float len = 2.8f;
        Vector3 Spine(float u) => new Vector3(0, len * (0.32f * u - 0.78f * u * u), len * u);
        float Width(float u) => 0.04f + 0.38f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 1.1f)), 0.6f) * (1f - 0.55f * u);
        for (int i = 0; i < n; i++)
        {
            float u0 = i / (float)n, u1 = (i + 1) / (float)n;
            var s0 = Spine(u0); var s1 = Spine(u1);
            foreach (float side in new[] { -1f, 1f })
            {
                // the leaflets rise from the spine to the edges (the V fold)
                var e0 = s0 + new Vector3(side * Width(u0), Width(u0) * 0.35f, 0);
                var e1 = s1 + new Vector3(side * Width(u1), Width(u1) * 0.35f, 0);
                var normal = Vector3.Cross(e0 - s0, s1 - s0).normalized * side;
                if (normal.y < 0) normal = -normal;
                b.Face(normal, s0, e0, e1, s1);
                b.Face(-normal, s0, e0, e1, s1);
            }
        }
        return b.Save("palmfrond");
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

    /// <summary>
    /// A clipped myrtle hedge from a to b, as the gardens of al-Andalus keep them: a dense block of
    /// small leaves with rounded edges, cut flat on top.
    /// </summary>
    private static void Hedge(Transform parent, Vector3 a, Vector3 b, float height = 0.9f)
    {
        var d = b - a; d.y = 0;
        float len = d.magnitude;
        if (len < 0.3f) return;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        MeshGen.RoundBox("Myrtle", (a + b) / 2f + Vector3.up * height / 2f, new Vector3(0.7f, height, len), 0.22f, HedgeMat, 0.8f, parent, yaw);
    }

    private static void Flowers(Transform parent, Vector3 c, float rx, float rz, int count, float y)
    {
        for (int i = 0; i < (count + 1) / 2; i++)
        {
            var p = c + new Vector3(R(-rx, rx), 0, R(-rz, rz));
            p.y = y;
            Small(rng.NextDouble() < 0.6 ? Nature.Place("Bush_Common_Flowers", p, R(0, 360), R(0.7f, 1.2f), parent)
                                         : Nature.Place(Nature.Ground[rng.Next(Nature.Ground.Length)], p, R(0, 360), R(0.5f, 0.9f), parent));
        }
    }

    /// <summary>Small plants cast no shadows (half the drawing for nothing to see).</summary>
    private static GameObject Small(GameObject go)
    {
        if (go != null)
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    /// <summary>Scenery outside the walls: no shadows, on the layer the camera stops drawing far away on weaker phones.</summary>
    private static GameObject Scenery(GameObject go)
    {
        if (go == null) return null;
        foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = Settings.SceneryLayer;
        return Small(go);
    }

    /// <summary>A bronze lantern on a post: a pierced dome lamp on an arm, seen by day, lit at night.</summary>
    private static void Lantern(Transform parent, Vector3 p)
    {
        var t = Node("Lantern", parent, p, R(0, 360));
        MeshGen.TexCylinder("LanternPost", p, 0.07f, 2.9f, Bronze, 1f, t);
        MeshGen.TexCylinder("LanternFoot", p, 0.16f, 0.25f, Bronze, 1f, t, false);
        LBox(t, "LanternArm", new Vector3(0.22f, 2.85f, 0), new Vector3(0.5f, 0.05f, 0.05f), Bronze, 1f, false);
        LBox(t, "LanternChain", new Vector3(0.44f, 2.72f, 0), new Vector3(0.02f, 0.26f, 0.02f), Bronze, 1f, false);
        NightLights.Lamp(t, t.TransformPoint(new Vector3(0.44f, 2.4f, 0)), new Vector3(0.24f, 0.36f, 0.24f), night, 3f);
    }

    /// <summary>A lantern hanging on a chain from a ceiling (halls and galleries).</summary>
    private static void HangingLamp(Transform parent, Vector3 p, float chain)
    {
        MeshGen.TexBox("LampChain", p + Vector3.up * (0.2f + chain / 2f), new Vector3(0.02f, chain, 0.02f), Bronze, 1f, parent, 0f, false);
        NightLights.Lamp(parent, p, new Vector3(0.3f, 0.42f, 0.3f), night, 3.4f);
    }

    /// <summary>
    /// A stone channel of running water from a to b (level): two low rims, a dark bed, the water drifting
    /// downstream. 'top' is the height of the rims.
    /// </summary>
    private static void Channel(Transform parent, Vector3 a, Vector3 b, float width, float top, float foot = float.NaN)
    {
        var d = b - a; d.y = 0;
        float len = d.magnitude;
        if (len < 0.2f) return;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        var c = (a + b) / 2f;
        c.y = top;
        var t = Node("Channel", parent, c, yaw);
        LBox(t, "ChannelBed", new Vector3(0, -0.22f, 0), new Vector3(width, 0.06f, len), Ashlar, 2f, false);
        var water = LBox(t, "ChannelWater", new Vector3(0, -0.07f, 0), new Vector3(width, 0.02f, len), Flowing(d), 4f, false);
        water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (float s in new[] { -1f, 1f })
            LBox(t, "ChannelRim", new Vector3(s * (width / 2f + 0.09f), -0.1f, 0), new Vector3(0.18f, 0.2f, len), Cream, 1f);
        // raised above the ground: a stone base under it
        if (!float.IsNaN(foot) && top - 0.25f - foot > 0.05f)
            LBox(t, "ChannelBase", new Vector3(0, (foot - top - 0.25f) / 2f + 0.0f, 0), new Vector3(width + 0.36f, top - 0.25f - foot, len), Ashlar, 2f);
    }

    /// <summary>A cascade: water running down a stone chute between two cheeks, from 'high' to 'low'.</summary>
    private static void Cascade(Transform parent, Vector3 high, Vector3 low, float width)
    {
        Underground.Wedge(parent, "CascadeStone", low - Vector3.up * 0.15f, high - Vector3.up * 0.15f, width + 0.5f, Ashlar);
        var water = Underground.Wedge(parent, "Cascade", low, high + Vector3.up * 0.02f, width, Flowing(low - high));
        water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var d = high - low; d.y = 0;
        var side = Vector3.Cross(Vector3.up, d.normalized);
        float len = d.magnitude;
        foreach (float s in new[] { -1f, 1f })
        {
            var cheek = MeshGen.TexBox("CascadeCheek", (high + low) / 2f + side * s * (width / 2f + 0.2f) + Vector3.up * 0.15f, new Vector3(0.3f, 0.5f, Mathf.Sqrt(len * len + (high.y - low.y) * (high.y - low.y))), Cream, 1f, parent);
            cheek.transform.rotation = Quaternion.LookRotation(high - low);
        }
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
        frondMesh = null;
        houses = 0;
        AssetDatabase.DeleteAsset(MeshFolder);
        AssetSetup.Folder(MeshFolder);
        MakeAshlar();
        MakeMarble();
        MakeAtaurique();
        MakeLatticeAndCarpet();
        MakeMyrtle();
        root = new GameObject("Map_Zahra").transform;

        Terraces();
        CityWalls();
        UpperGarden();
        SalonRico();
        Hammam();
        Portico();
        PlazaDeArmas();
        RampStreet();
        CaminoDeRonda();
        LowerGarden();
        Waters();
        Palace(new Vector3(70f, Lower, -70f));
        Houses();
        DarAlMulk();
        BasilicalHall();
        NorthGate();
        Medina();
        Supplies();
        Boards();
        Surroundings();

        // invisible fences just outside the walls (in the south beyond the road down from the gate)
        foreach (var (c, sz) in new[] { (new Vector3(0, 30, 127.2f), new Vector3(260f, 60, 1)), (new Vector3(0, 30, -132f), new Vector3(260f, 60, 1)),
                                        (new Vector3(127.2f, 30, 0), new Vector3(1, 60, 264f)), (new Vector3(-127.2f, 30, 0), new Vector3(1, 60, 264f)) })
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
        Debug.Log($"[Map] Zahra built: {root.GetComponentsInChildren<Renderer>().Length} renderers, {layout.Waypoints.Length} waypoints, {layout.Spawns.Length} spawns, {houses} houses");
        return layout;
    }


    /// <summary>The three terraces, faced with ashlar where they step down, with a parapet along the edges.</summary>
    private static void Terraces()
    {
        var t = Node("Terraces", root, Vector3.zero, 0);
        const float far = 380f, south = -127f;
        Slabs(t, "Valley", Rect.MinMaxRect(-far, -far, far, south), new[] { Rect.MinMaxRect(-far, -323f, far, -277f) }, -3f, 2f, Grass, 6f);
        Slab(t, "RiverBed", Rect.MinMaxRect(-far, -323f, far, -277f), -4.4f, 1f, Earth, 6f);
        Slab(t, "LowerTerrace", Rect.MinMaxRect(-far, south, far, MidSouth), Lower, 5f, Lawn, 5f);
        Slab(t, "MiddleTerrace", Rect.MinMaxRect(-far, MidSouth, far, UpSouth), Middle, 8f, Lawn, 5f);
        Slab(t, "UpperTerrace", Rect.MinMaxRect(-far, UpSouth, far, far), Upper, 14f, Lawn, 5f);
        Box(t, "TerraceFace", new Vector3(0, (Lower - 3f) / 2f, south - 0.12f), new Vector3(2f * far, Lower + 3f + 0.02f, 0.25f), Ashlar, 2.5f);
        Box(t, "TerraceFace", new Vector3(0, (Lower + Middle) / 2f - 0.32f, MidSouth - 0.15f), new Vector3(Half * 2f + 8f, Middle - Lower + 0.6f, 0.3f), Ashlar);
        Box(t, "TerraceFace", new Vector3(0, (Middle + Upper) / 2f - 0.32f, UpSouth - 0.15f), new Vector3(Half * 2f + 8f, Upper - Middle + 0.6f, 0.3f), Ashlar);
        void Parapet(float z, float y, params (float a, float b)[] gaps)
        {
            float x = -123.5f;
            var ordered = gaps.OrderBy(g => g.a).ToList();
            ordered.Add((123.5f, 123.5f));
            foreach (var (ga, gb) in ordered)
            {
                if (ga - x > 0.1f) Box(t, "Parapet", new Vector3((x + ga) / 2f, y + 0.45f, z), new Vector3(ga - x, 0.9f, 0.45f), Ashlar, 2f);
                x = Mathf.Max(x, gb);
            }
        }
        Parapet(MidSouth + 0.23f, Middle, (-10.4f, 0.4f), (-50.2f, -45.8f), (33f, 41f));
        Parapet(UpSouth + 0.23f, Upper, (-60.2f, -55.8f), (49.8f, 54.2f), (-20.6f, 10.6f), (19.4f, 22.6f));
        // the paved streets of the lower town: along the foot of the middle terrace, from the gate north, east to west
        Slab(t, "Street", Rect.MinMaxRect(-123.5f, MidSouth - 4f, 123.5f, MidSouth), Lower + 0.04f, 0.1f, Paving, 2.5f);
        Slab(t, "Street", Rect.MinMaxRect(GateX - 3f, -123f, GateX + 3f, MidSouth - 4f), Lower + 0.05f, 0.1f, Paving, 2.5f);
        Slab(t, "Street", Rect.MinMaxRect(GateX + 3f, -91.6f, 123.5f, -88.7f), Lower + 0.05f, 0.1f, Paving, 2.5f);
        Slab(t, "Street", Rect.MinMaxRect(-123.5f, -91.6f, GateX - 3f, -88.7f), Lower + 0.05f, 0.1f, Paving, 2.5f);
    }

    private const float GateX = -53.5f;

    /// <summary>A square tower of ashlar with a band and merlons on top.</summary>
    private static void Tower(Transform t, Vector3 foot, float height, float size = 6f)
    {
        Box(t, "Tower", foot + Vector3.up * (height / 2f - 2f), new Vector3(size, height + 4f, size), Ashlar, 2.5f);
        Box(t, "TowerBand", foot + Vector3.up * (height - 0.15f), new Vector3(size + 0.3f, 0.3f, size + 0.3f), Cream, 1.5f, false);
        for (int i = 0; i < 4; i++)
            for (float u = -size / 2f + 0.5f; u <= size / 2f - 0.4f; u += 1.4f)
            {
                var off = i == 0 ? new Vector3(u, 0, size / 2f - 0.25f) : i == 1 ? new Vector3(u, 0, -size / 2f + 0.25f) : i == 2 ? new Vector3(size / 2f - 0.25f, 0, u) : new Vector3(-size / 2f + 0.25f, 0, u);
                Box(t, "Merlon", foot + off + Vector3.up * (height + 0.5f), new Vector3(0.7f, 1f, 0.7f), Ashlar, 1f);
            }
    }

    private const float WallTop = 8f;   // the walk on the city walls, above the terrace at their foot

    /// <summary>
    /// A stretch of the city wall along its axis from a to b (a.y the terrace at its foot): 3 m of ashlar
    /// with a walk on top, merlons along the outer side, a cream band under them.
    /// </summary>
    private static void WallStretch(Transform t, Vector3 a, Vector3 b, Vector3 outward)
    {
        var d = b - a; d.y = 0;
        float len = d.magnitude;
        if (len < 0.5f) return;
        var dir = d / len;
        bool alongX = Mathf.Abs(dir.x) > 0.5f;
        float top = a.y + WallTop;
        var c = (a + b) / 2f;
        Box(t, "CityWall", new Vector3(c.x, (a.y - 4f + top) / 2f, c.z), alongX ? new Vector3(len, WallTop + 4f, 3f) : new Vector3(3f, WallTop + 4f, len), Ashlar, 2.5f);
        Box(t, "WallBand", new Vector3(c.x, top - 0.6f, c.z) + outward * 1.52f, alongX ? new Vector3(len, 0.2f, 0.06f) : new Vector3(0.06f, 0.2f, len), Cream, 1.5f, false);
        for (float u = 0.7f; u < len - 0.4f; u += 1.5f)
        {
            var p = a + dir * u + outward * 1.25f;
            Box(t, "Merlon", new Vector3(p.x, top + 0.55f, p.z), alongX ? new Vector3(0.8f, 1.1f, 0.5f) : new Vector3(0.5f, 1.1f, 0.8f), Ashlar, 1f);
        }
    }

    /// <summary>A square bastion on the wall: solid up to the walk, merlons round its top, open in the middle of each side for the walk.</summary>
    private static void Bastion(Transform t, Vector3 foot, float size = 6f)
    {
        float top = foot.y + WallTop;
        Box(t, "Bastion", new Vector3(foot.x, (foot.y - 4f + top) / 2f, foot.z), new Vector3(size, WallTop + 4f, size), Ashlar, 2.5f);
        Box(t, "BastionBand", new Vector3(foot.x, top - 0.6f, foot.z), new Vector3(size + 0.12f, 0.2f, size + 0.12f), Cream, 1.5f, false);
        float h = size / 2f - 0.3f;
        for (int side = 0; side < 4; side++)
            for (float u = -h + 0.35f; u <= h - 0.3f; u += 1.3f)
            {
                if (Mathf.Abs(u) < 1.4f) continue;
                var off = side == 0 ? new Vector3(u, 0, h) : side == 1 ? new Vector3(u, 0, -h) : side == 2 ? new Vector3(h, 0, u) : new Vector3(-h, 0, u);
                Box(t, "Merlon", new Vector3(foot.x, top + 0.55f, foot.z) + off, side < 2 ? new Vector3(0.7f, 1.1f, 0.6f) : new Vector3(0.6f, 1.1f, 0.7f), Ashlar, 1f);
            }
    }

    /// <summary>A wooden crate (or a stack of them) from 'baseY' up to 'topY'.</summary>
    private static void Crates(Transform t, float x, float z, float baseY, float topY, float size = 1.1f, float yaw = 0f)
    {
        for (float y = baseY; y < topY - 0.05f;)
        {
            float h = Mathf.Min(0.8f, topY - y);
            Box(t, "Crate", new Vector3(x, y + h / 2f, z), new Vector3(size, h, size), Wood, 1f, true, yaw);
            y += h;
        }
    }

    /// <summary>A hidden way up: crates (stacks) standing on 'baseY', each 0.8 m higher than the one before.</summary>
    private static void CrateClimb(Transform t, float baseY, params (float x, float z, float top)[] steps)
    {
        foreach (var (x, z, top) in steps) Crates(t, x, z, baseY, top);
    }

    /// <summary>
    /// A row of four store rooms against the inside of the city wall, each a step higher (2.4, 4.0,
    /// 5.6, 7.2 m), with crates on the roofs between them: the hidden way up onto the wall walk (8 m).
    /// 'start' is on the inner face of the wall, the row runs along 'along', 'inward' into the city.
    /// </summary>
    private static void StoreRow(Transform t, Vector3 start, Vector3 along, Vector3 inward)
    {
        float y = start.y;
        float yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
        Vector3 P(float u, float v) => start + along * u + inward * v;
        // two crates in the street up to the first roof
        var c1 = P(-2.4f, 1.6f); Crates(t, c1.x, c1.z, y, y + 0.8f);
        var c2 = P(-1.2f, 1.6f); Crates(t, c2.x, c2.z, y, y + 1.6f);
        float[] tops = { 2.4f, 4.0f, 5.6f, 7.2f };
        for (int i = 0; i < 4; i++)
        {
            var c = P(i * 3.2f + 1.6f, 1.5f);
            float h = tops[i];
            Box(t, "StoreRoom", new Vector3(c.x, y + h / 2f, c.z), Mathf.Abs(along.x) > 0.5f ? new Vector3(3.2f, h, 3f) : new Vector3(3f, h, 3.2f), i % 2 == 0 ? LimeOchre : Plaster, 2f);
            Box(t, "StoreRoof", new Vector3(c.x, y + h + 0.04f, c.z), Mathf.Abs(along.x) > 0.5f ? new Vector3(3.3f, 0.08f, 3.1f) : new Vector3(3.1f, 0.08f, 3.3f), Wood, 2f);
            var door = P(i * 3.2f + 1.6f, 3.03f);
            Box(t, "StoreDoor", new Vector3(door.x, y + 1.05f, door.z), Mathf.Abs(along.x) > 0.5f ? new Vector3(1f, 2.1f, 0.06f) : new Vector3(0.06f, 2.1f, 1f), Wood, 1f, false);
            if (i < 3)
            {
                // a crate on this roof at the foot of the next room: 0.8 m under its roof
                var k = P(i * 3.2f + 2.6f, 1.5f);
                Crates(t, k.x, k.z, y + h + 0.08f, y + tops[i + 1] - 0.8f, 1f, yaw);
            }
        }
        NightLights.Lamp(t, P(-0.3f, 3.2f) + Vector3.up * (y + 2.4f), new Vector3(0.22f, 0.32f, 0.22f), night, 2.6f);
    }

    /// <summary>
    /// The walls round the whole city with towers, and the main gate in the south wall: a tall horseshoe
    /// arch between two towers, its name over it on both faces, and inside it a board with the story of
    /// the city in Arabic and English.
    /// </summary>
    private static void CityWalls()
    {
        var t = Node("CityWalls", root, Vector3.zero, 0);
        const float x = 125f;
        foreach (float s in new[] { -1f, 1f })
        {
            var outward = new Vector3(s, 0, 0);
            foreach (var (z0, z1, y) in new[] { (-123.5f, MidSouth, Lower), (MidSouth, UpSouth, Middle), (UpSouth, 123f, Upper) })
            {
                WallStretch(t, new Vector3(s * x, y, z0), new Vector3(s * x, y, z1), outward);
                for (float z = z0 + 14f; z < z1 - 12f; z += 24f) Bastion(t, new Vector3(s * x, y, z));
            }
            // on the walk, stairs up where the wall climbs to the next terrace
            Buildings.Stairs(t, new Vector3(s * (x - 0.25f), Lower + WallTop, MidSouth - 7.2f), Vector3.forward, Middle - Lower, 2f, Ashlar);
            Buildings.Stairs(t, new Vector3(s * (x - 0.25f), Middle + WallTop, UpSouth - 10.8f), Vector3.forward, Upper - Middle, 2f, Ashlar);
        }
        // the south wall, open for the gate
        foreach (var (a, b) in new[] { (-123.5f, GateX - 4f), (GateX + 4f, 123.5f) })
            WallStretch(t, new Vector3(a, Lower, -125f), new Vector3(b, Lower, -125f), Vector3.back);
        for (float xx = -110f; xx < 120f; xx += 26f)
            if (Mathf.Abs(xx - GateX) > 12f) Bastion(t, new Vector3(xx, Lower, -125f));
        // the north wall above the upper terrace, two stairs up to its walk
        WallStretch(t, new Vector3(-123.5f, Upper, 124.5f), new Vector3(123.5f, Upper, 124.5f), Vector3.forward);
        for (float xx = -100f; xx < 120f; xx += 30f) Bastion(t, new Vector3(xx, Upper, 124.5f));
        foreach (float xx in new[] { -36f, 54f })
            Buildings.Stairs(t, new Vector3(xx, Upper, 121.8f), Vector3.right, WallTop, 2f, Ashlar);
        foreach (var c in new[] { new Vector3(-x, Lower, -125f), new Vector3(x, Lower, -125f), new Vector3(-x, Upper, 124.5f), new Vector3(x, Upper, 124.5f) })
            Bastion(t, c, 8f);
        // no stairs on the other walls: the way up is over the roofs of the store rows built against them
        StoreRow(t, new Vector3(-123.5f, Lower, -55f), Vector3.forward, Vector3.right);
        StoreRow(t, new Vector3(123.5f, Middle, 1f), Vector3.back, Vector3.left);
        StoreRow(t, new Vector3(52f, Lower, -123.5f), Vector3.right, Vector3.forward);
        // the gate: two towers and a great horseshoe arch, wooden doors open against the inside
        foreach (float s in new[] { -1f, 1f }) Tower(t, new Vector3(GateX + s * 7f, Lower, -125f), 14f, 6f);
        var gate = Node("MainGate", t, new Vector3(GateX, Lower, -125f), 0);
        Arch(gate, 0, 8f, 4.6f, 13f, 4f, 30f, Ashlar);
        foreach (float s in new[] { -1f, 1f })
            Box(t, "GateDoor", new Vector3(GateX + s * 3.9f, 2.2f, -122.85f), new Vector3(2.6f, 4.4f, 0.16f), Wood, 1.5f);
        Sign(t, new Vector3(GateX, 11.7f, -122.85f), 180f, "مدينة الزهراء  -  سنة 325 للهجرة", "MADINAT AL-ZAHRA  -  FOUNDED 936 CE (325 AH)", 6.4f, 1.8f);
        Sign(t, new Vector3(GateX, 11.7f, -127.15f), 0f, "مدينة الزهراء  -  سنة 325 للهجرة", "MADINAT AL-ZAHRA  -  FOUNDED 936 CE (325 AH)", 6.4f, 1.8f);
        foreach (float s in new[] { -1f, 1f })
        {
            NightLights.Lamp(t, new Vector3(GateX + s * 4.6f, 5.6f, -122.7f), new Vector3(0.3f, 0.45f, 0.3f), night, 3.4f);
            NightLights.Lamp(t, new Vector3(GateX + s * 4.6f, 5.6f, -127.3f), new Vector3(0.3f, 0.45f, 0.3f), night, 3.4f);
        }
        HistoryBoard(new Vector3(GateX + 6.2f, Lower, -112f), 90f);
        // the road from the valley up to the gate
        Underground.Wedge(t, "Ramp", new Vector3(GateX, Lower - 3f, -141f), new Vector3(GateX, Lower, -127f), 8f, Paving);
    }

    private const string StoryArabic =
        "بنى الخليفة عبد الرحمن الناصر لدين الله مدينة الزهراء سنة 325 للهجرة (936 للميلاد) على سفح جبل العروس غربي قرطبة، وجعلها مقر الخلافة الأموية في الأندلس.\n" +
        "قامت على ثلاث مدرجات: في أعلاها دار الملك قصر الخليفة، وفي وسطها مجالس الحكم والمجلس الشرقي والجنان والبرك، وفي أسفلها المدينة بدورها وأسواقها وحماماتها.\n" +
        "عمل في بنائها آلاف الصناع والبنائين، وجلب إليها الرخام من أنحاء الأندلس وإفريقية، وجرى إليها الماء من الجبل في قناة عظيمة.\n" +
        "استقبل فيها الخلفاء رسل القسطنطينية وملوك أوروبا والمغرب، فكانت من عجائب زمانها، وأتم بناءها ابنه الحكم المستنصر بالله.";

    private const string StoryEnglish =
        "Caliph Abd al-Rahman III founded Madinat al-Zahra in 936 CE (325 AH) on the slopes of the Sierra Morena, west of Cordoba, as the seat of the Umayyad Caliphate of al-Andalus.\n" +
        "It rises on three terraces: the Caliph's house (Dar al-Mulk) at the top, the halls of state, the Salon Rico, the gardens and pools in the middle, and the town with its houses, markets and baths below.\n" +
        "Thousands of masons and craftsmen built it; marble came from all of al-Andalus and North Africa, and water ran down from the mountain in a great aqueduct.\n" +
        "Here the caliphs received the envoys of Constantinople and the kings of Europe and the Maghreb. His son al-Hakam II completed the city, one of the wonders of its age.";

    /// <summary>A marble stele on a plinth with the story of the city, Arabic above and English below.</summary>
    private static void HistoryBoard(Vector3 foot, float yaw)
    {
        var t = Node("HistoryBoard", root, foot, yaw);
        const float w = 6f, h = 6f, face = -0.19f;
        LBox(t, "SteleBase", new Vector3(0, 0.3f, 0), new Vector3(w + 0.6f, 0.6f, 1.1f), Ashlar, 1.5f);
        LBox(t, "Stele", new Vector3(0, 0.6f + h / 2f, 0), new Vector3(w, h, 0.35f), Marble, 2f);
        LBox(t, "SteleCrown", new Vector3(0, 0.6f + h + 0.15f, 0), new Vector3(w + 0.3f, 0.3f, 0.55f), Cream, 1f, false);
        foreach (float s in new[] { -1f, 1f })
            LBox(t, "SteleFrame", new Vector3(s * (w / 2f - 0.08f), 0.6f + h / 2f, face), new Vector3(0.1f, h - 0.1f, 0.04f), Gold, 1f, false);
        LBox(t, "StelePanel", new Vector3(0, 0.6f + h / 2f, face + 0.01f), new Vector3(w - 0.3f, h - 0.3f, 0.02f), Capital, 1f, false);
        LBox(t, "SteleRule", new Vector3(0, 0.6f + h / 2f, face), new Vector3(w - 0.6f, 0.04f, 0.04f), Gold, 1f, false);
        var ink = new Color(0.2f, 0.12f, 0.06f);
        var title = new Color(0.55f, 0.3f, 0.05f);
        float top = 0.6f + h, mid = 0.6f + h / 2f;
        Writing(t, new Vector3(0, top - 0.42f, face - 0.01f), new Vector2(w - 0.4f, 0.55f), "قصة مدينة الزهراء", 40f, title);
        Writing(t, new Vector3(0, (top - 0.75f + mid + 0.1f) / 2f, face - 0.01f), new Vector2(w - 0.5f, top - 0.75f - mid - 0.1f), StoryArabic, 20f, ink);
        Writing(t, new Vector3(0, mid - 0.3f, face - 0.01f), new Vector2(w - 0.4f, 0.42f), "THE STORY OF MADINAT AL-ZAHRA", 28f, title);
        Writing(t, new Vector3(0, (mid - 0.58f + 0.75f) / 2f, face - 0.01f), new Vector2(w - 0.5f, mid - 0.58f - 0.75f), StoryEnglish, 20f, ink);
        NightLights.Lamp(t, t.TransformPoint(new Vector3(0, 0.6f + h + 0.6f, -0.4f)), new Vector3(0.24f, 0.34f, 0.24f), night, 3f);
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
        // building stones left over from the works on the upper terrace behind it: three steps up to the roof
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


    /// <summary>The Plaza de Armas: the parade ground in front of the Portico, galleries on two sides, a great fountain, planters with myrtle for cover.</summary>
    private static void PlazaDeArmas()
    {
        var t = Node("Plaza", root, Vector3.zero, 0);
        Slab(t, "Plaza", Rect.MinMaxRect(38f, MidSouth, 101f, 44f), Middle + 0.04f, 0.1f, Paving, 3f);
        // galleries on columns along the north and the south side, roofed
        Arcade(t, new Vector3(40f, Middle, 39.5f), Vector3.right, 7, 7f, 3f, 5f, 0.5f, 28f, Ashlar, true);
        Box(t, "GalleryRoof", new Vector3(64.5f, Middle + 5.2f, 42.25f), new Vector3(49.5f, 0.4f, 5.9f), RoofTiles, 2f);
        Box(t, "GalleryBack", new Vector3(44f, Middle + 2.5f, 42.4f), new Vector3(8f, 5f, 0.4f), Ashlar);
        Arcade(t, new Vector3(44f, Middle, -24f), Vector3.right, 7, 7f, 3f, 5f, 0.5f, 28f, Ashlar, true);
        Box(t, "GalleryRoof", new Vector3(68.5f, Middle + 5.2f, -26.9f), new Vector3(49.5f, 0.4f, 6f), RoofTiles, 2f);
        Box(t, "GalleryBack", new Vector3(68.5f, Middle + 2.5f, -29.6f), new Vector3(49f, 5f, 0.4f), Ashlar);
        // the great fountain
        var fc = new Vector3(70f, Middle, 8f);
        MeshGen.Object("FountainBasin", MeshGen.Cylinder(3.2f, 0.7f, 2f, 16), Cream, t, fc).AddComponent<MeshCollider>();
        var fw = MeshGen.TexCylinder("FountainWater", fc + Vector3.up * 0.62f, 3f, 0.02f, Water, 3f, t, false);
        fw.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MeshGen.TexCylinder("FountainShaft", fc, 0.35f, 2.2f, MarblePink, 1f, t);
        MeshGen.TexCylinder("FountainBowl", fc + Vector3.up * 2.2f, 1.1f, 0.3f, Cream, 1f, t, false);
        // planters of clipped myrtle: cover across the open ground
        foreach (var (x, z, along) in new[] { (55f, -10f, true), (55f, 26f, true), (85f, -10f, true), (85f, 26f, true), (70f, -17f, true), (70f, 33f, true), (50f, 8f, false), (90f, 8f, false) })
        {
            var size = along ? new Vector3(5f, 0.8f, 1.3f) : new Vector3(1.3f, 0.8f, 5f);
            Box(t, "Planter", new Vector3(x, Middle + 0.4f, z), size, Ashlar, 1.5f);
            var a = new Vector3(x - (along ? 2.2f : 0f), Middle + 0.8f, z - (along ? 0f : 2.2f));
            var b = new Vector3(x + (along ? 2.2f : 0f), Middle + 0.8f, z + (along ? 0f : 2.2f));
            Hedge(t, a, b, 0.8f);
        }
        for (float z = -18f; z < 36f; z += 9f)
        {
            Box(t, "Planter", new Vector3(96f, Middle + 0.3f, z), new Vector3(2.4f, 0.6f, 2.4f), Ashlar, 1.5f);
            OrangeTree(t, new Vector3(96f, Middle + 0.6f, z));
        }
        foreach (var (x, z) in new[] { (44f, -20f), (44f, 10f), (44f, 36f), (62f, 8f), (78f, 8f) }) Lantern(t, new Vector3(x, Middle, z));
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
        bool Wet(Vector3 p, float margin) => pools.Any(r => p.x > r.xMin - margin && p.x < r.xMax + margin && p.z > r.yMin - margin && p.z < r.yMax + margin)
            || (p.z > -59.5f && (Mathf.Abs(p.x + 9.4f) < margin + 0.5f || Mathf.Abs(p.x + 0.6f) < margin + 0.5f));   // the channels from the cascades
        foreach (var bed in beds)
        {
            var c = new Vector3(bed.center.x, Lower, bed.center.y);
            float hx = bed.width / 2f - 0.6f, hz = bed.height / 2f - 0.6f;
            foreach (float sz in new[] { -1f, 1f })
                foreach (var (a, b) in new[] { (c + new Vector3(-hx, 0, sz * hz), c + new Vector3(-1.2f, 0, sz * hz)), (c + new Vector3(1.2f, 0, sz * hz), c + new Vector3(hx, 0, sz * hz)) })
                {
                    // hedges stop short of the channels
                    var (ha, hb) = (a, b);
                    foreach (float cx in new[] { -9.4f, -0.6f })
                        if (ha.z > -59.5f && ha.x < cx + 1.3f && hb.x > cx - 1.3f)
                        {
                            if (cx - ha.x > hb.x - cx) hb.x = cx - 1.3f;
                            else ha.x = cx + 1.3f;
                        }
                    if (!Wet(ha, 0.6f) && !Wet(hb, 0.6f) && !Wet((ha + hb) / 2f, 0.6f)) Hedge(t, ha, hb, 1f);
                }
            for (float x = bed.xMin + 5f; x < bed.xMax - 3f; x += 9f)
                for (float z = bed.yMin + 5f; z < bed.yMax - 3f; z += 9f)
                    if (rng.NextDouble() < 0.6 && !Wet(new Vector3(x, 0, z), 2.5f)) OrangeTree(t, new Vector3(x, Lower, z));
            Flowers(t, c, hx - 1f, hz - 1f, 8, Lower);
            var palm = c + new Vector3(hx - 1.5f, 0, hz - 1.5f);
            if (Wet(palm, 2f)) palm = c + new Vector3(-(hx - 1.5f), 0, hz - 1.5f);
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
    /// The Caliph's palace in the lower town: a fortified square with corner towers and a watch tower,
    /// a gate under a horseshoe arch, a courtyard with a fountain and four channels between orange
    /// trees, galleries on marble columns, side chambers, and the throne hall: three naves, a carpet
    /// to the dais, the throne under a canopy before a carved arch, hanging lamps.
    /// </summary>
    private static void Palace(Vector3 c)
    {
        var t = Node("RoyalPalace", root, c, 0);
        const float hx = 28f, hz = 26f, wallH = 8f, floor = 0.5f;
        Vector3 W(float x, float y, float z) => t.TransformPoint(new Vector3(x, y, z));
        LBox(t, "Podium", new Vector3(0, floor / 2f, 0), new Vector3(hx * 2f + 4f, floor, hz * 2f + 4f), Ashlar, 2f);
        LBox(t, "CourtFloor", new Vector3(0, floor + 0.02f, 11f), new Vector3(34f, 0.04f, 22f), Marble, 2f, false);
        LBox(t, "HallFloor", new Vector3(0, floor + 0.02f, -12.5f), new Vector3(34f, 0.04f, 25f), Marble, 2f, false);
        foreach (float s in new[] { -1f, 1f })
            LBox(t, "RoomFloor", new Vector3(s * 22.25f, floor + 0.02f, -2.5f), new Vector3(10.5f, 0.04f, 46f), BrickFloor, 2f, false);
        // outer walls, the gate in the north wall, corner towers, the watch tower in the north-west
        Wall(t, W(-hx, floor, -hz), W(hx, floor, -hz), wallH, 1f, Ashlar);
        Wall(t, W(-hx, floor, -hz), W(-hx, floor, hz), wallH, 1f, Ashlar);
        Wall(t, W(hx, floor, -hz), W(hx, floor, hz), wallH, 1f, Ashlar);
        Wall(t, W(-hx, floor, hz), W(-3f, floor, hz), wallH, 1f, Ashlar);
        Wall(t, W(3f, floor, hz), W(hx, floor, hz), wallH, 1f, Ashlar);
        var gate = Node("PalaceGate", t, W(0, floor, hz), 0);
        Arch(gate, 0, 6f, 3.6f, wallH + 1.5f, 1.8f, 30f, Ashlar);
        foreach (float s in new[] { -1f, 1f })
            LBox(t, "GateDoor", new Vector3(s * 2.6f, floor + 2f, hz - 0.95f), new Vector3(2f, 4f, 0.14f), Wood, 1.5f);
        Sign(t, W(0, floor + wallH + 0.4f, hz + 0.98f), 180f, "قصر الخلافة", "THE CALIPH'S PALACE", 5f, 1.3f);
        foreach (var (x, z) in new[] { (-hx, -hz), (hx, -hz), (hx, hz) }) Tower(t, W(x, floor, z), 11f, 5f);
        Minaret(t, new Vector3(-24.3f, floor, 22.3f), 15.6f);
        // the courtyard: a fountain, four channels, four beds of orange trees edged with myrtle
        var fc = W(0, floor, 11f);
        MeshGen.Object("CourtFountain", MeshGen.Cylinder(1.9f, 0.6f, 1f, 8), Cream, t, fc).AddComponent<MeshCollider>();
        var cw = MeshGen.TexCylinder("CourtFountainWater", fc + Vector3.up * 0.52f, 1.75f, 0.02f, Water, 2f, t, false);
        cw.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MeshGen.TexCylinder("FountainJet", fc, 0.18f, 1.3f, Cream, 1f, t, false);
        foreach (var (dx, dz) in new[] { (0f, 1f), (0f, -1f), (1f, 0f), (-1f, 0f) })
            Channel(t, fc + new Vector3(dx * 2.1f, 0, dz * 2.1f), fc + new Vector3(dx * (dz == 0 ? 15.5f : 0f), 0, dz * (dx == 0 ? 9.5f : 0f)), 0.5f, floor + 0.2f);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var bc = W(sx * 8.5f, floor, 11f + sz * 5.5f);
                Box(t, "CourtBed", bc + Vector3.up * 0.06f, new Vector3(12f, 0.1f, 7f), Grass, 2f, false);
                Hedge(t, bc + new Vector3(-5.6f, 0, -3.2f), bc + new Vector3(5.6f, 0, -3.2f), 0.7f);
                Hedge(t, bc + new Vector3(-5.6f, 0, 3.2f), bc + new Vector3(5.6f, 0, 3.2f), 0.7f);
                OrangeTree(t, bc + new Vector3(-2.5f, 0, 0));
                OrangeTree(t, bc + new Vector3(2.5f, 0, 0));
            }
        // galleries on the sides of the courtyard and in front of the gate, the chambers behind them
        foreach (float s in new[] { -1f, 1f })
        {
            Arcade(t, W(s * 17f, floor, 0f), t.TransformDirection(Vector3.forward), 5, 4.4f, 2.8f, 4.6f, 0.5f, 28f, Plaster, true);
            LBox(t, "GalleryRoof", new Vector3(s * 19f, floor + 4.8f, 11f), new Vector3(4.4f, 0.4f, 22f), RoofTiles, 2f);
            // the wall of the chambers, a door into each, its leaf standing open inside
            var bounds = new[] { -24.5f, -16f, -8f, 0f, 6.7f, 13.4f, 20f };
            for (int i = 0; i + 1 < bounds.Length; i++)
            {
                float za = bounds[i], zb = bounds[i + 1];
                Wall(t, W(s * 21f, floor, za), W(s * 21f, floor, zb), 5.2f, 0.5f, Plaster, (zb - za) / 2f, 1.2f, 2.4f);
                LBox(t, "ChamberDoor", new Vector3(s * 21.75f, floor + 1.2f, (za + zb) / 2f + 0.62f), new Vector3(1f, 2.3f, 0.08f), Wood, 1f, false);
            }
            foreach (float z in new[] { -16f, -8f, 0f, 6.7f, 13.4f })
                Wall(t, W(s * 21f, floor, z), W(s * 27.5f, floor, z), 5.2f, 0.4f, Plaster, 3.2f, 1.2f, 2.4f);
            LBox(t, "ChamberRoof", new Vector3(s * 24.3f, floor + 5.4f, -2.25f), new Vector3(7.2f, 0.4f, 45.5f), RoofTiles, 2f);
        }
        Arcade(t, W(-17f, floor, 22f), t.TransformDirection(Vector3.right), 5, 6.8f, 3f, 4.6f, 0.5f, 28f, Plaster, true);
        LBox(t, "VestibuleRoof", new Vector3(0, floor + 4.8f, 23.9f), new Vector3(34.4f, 0.4f, 4.2f), RoofTiles, 2f);
        // the throne hall: a front of five arches, two arcades, carved arch behind the dais
        Arcade(t, W(-17f, floor, 0f), t.TransformDirection(Vector3.right), 5, 6.8f, 3.8f, wallH, 1f, 28f, Ashlar, false, true, 0.9f);
        foreach (float x in new[] { -5.67f, 5.67f })
            Arcade(t, W(x, floor, -0.5f), t.TransformDirection(Vector3.back), 4, 5.9f, 3.6f, wallH, 0.6f, 25f, Plaster, true);
        foreach (float s in new[] { -1f, 1f })
        {
            Wall(t, W(s * 17f, floor, -25.5f), W(s * 17f, floor, -0.5f), wallH, 0.6f, Plaster);
            LBox(t, "Dado", new Vector3(s * 16.67f, floor + 0.6f, -13f), new Vector3(0.05f, 1.2f, 24.5f), Almagra, 1.5f, false);
            foreach (float z in new[] { -6f, -13f, -20f })
                LBox(t, "Ataurique", new Vector3(s * 16.64f, floor + 3.6f, z), new Vector3(0.04f, 3.2f, 2f), Ataurique, 1f, false);
            LBox(t, "Divan", new Vector3(s * 15.9f, floor + 0.22f, -12f), new Vector3(1f, 0.44f, 14f), Velvet, 1f);
        }
        LBox(t, "HallRoof", new Vector3(0, floor + wallH + 0.2f, -12.75f), new Vector3(35f, 0.4f, 26.5f), RoofTiles, 2.5f);
        LBox(t, "Ceiling", new Vector3(0, floor + wallH - 0.03f, -12.75f), new Vector3(33.6f, 0.06f, 25f), Wood, 2f, false);
        for (float z = -24f; z < -1f; z += 2.6f)
            LBox(t, "Beam", new Vector3(0, floor + wallH - 0.2f, z), new Vector3(33.6f, 0.26f, 0.3f), Wood, 2f, false);
        // the dais, the throne under its canopy, the carpets
        LBox(t, "Dais", new Vector3(0, floor + 0.3f, -22.5f), new Vector3(10f, 0.6f, 5.6f), Marble, 2f);
        LBox(t, "DaisStep", new Vector3(0, floor + 0.15f, -19.4f), new Vector3(6f, 0.3f, 0.6f), Marble, 1f);
        float dy = floor + 0.6f;
        LBox(t, "DaisCarpet", new Vector3(0, dy + 0.006f, -22.5f), new Vector3(8.6f, 0.012f, 4.6f), carpet, 4.6f, false);
        LBox(t, "Runner", new Vector3(0, floor + 0.05f, -9.6f), new Vector3(3f, 0.012f, 19f), carpet, 3f, false);
        LBox(t, "ThroneBase", new Vector3(0, dy + 0.3f, -23.2f), new Vector3(1.8f, 0.6f, 1.2f), Gold, 1f);
        LBox(t, "ThroneCushion", new Vector3(0, dy + 0.68f, -23.1f), new Vector3(1.5f, 0.16f, 1f), Velvet, 1f, false);
        LBox(t, "ThroneBack", new Vector3(0, dy + 1.5f, -23.75f), new Vector3(1.8f, 1.8f, 0.2f), Gold, 1f);
        LBox(t, "ThroneCarving", new Vector3(0, dy + 1.55f, -23.63f), new Vector3(1.4f, 1.4f, 0.04f), Ataurique, 0.7f, false);
        foreach (float s in new[] { -1f, 1f })
        {
            LBox(t, "ThroneArm", new Vector3(s * 0.85f, dy + 0.95f, -23.2f), new Vector3(0.14f, 0.5f, 1.1f), Gold, 1f, false);
            foreach (float z in new[] { -21.2f, -24.6f })
                MeshGen.TexCylinder("CanopyColumn", W(s * 1.9f, dy, z), 0.12f, 3.6f, MarblePink, 1f, t);
        }
        LBox(t, "Canopy", new Vector3(0, dy + 3.7f, -22.9f), new Vector3(4.2f, 0.22f, 3.8f), Gold, 1f, false);
        var cupola = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(cupola.GetComponent<Collider>());
        cupola.name = "CanopyDome";
        cupola.transform.SetParent(t, false);
        cupola.transform.position = W(0, dy + 3.8f, -22.9f);
        cupola.transform.localScale = new Vector3(2.6f, 1.6f, 2.6f);
        cupola.GetComponent<Renderer>().sharedMaterial = Gold;
        var niche = Node("ThroneArch", t, W(0, floor, -25.2f), 0);
        Arch(niche, 0, 6f, 3.4f, 7.4f, 0.4f, 30f, Ataurique);
        LBox(t, "ThroneArchBack", new Vector3(0, floor + 3.7f, -25.05f), new Vector3(4.4f, 7.4f, 0.05f), Almagra, 1f, false);
        foreach (float z in new[] { -5f, -12f, -19f })
            foreach (float x in new[] { -11f, 0f, 11f })
                HangingLamp(t, W(x, floor + 5.6f, z), wallH - 6f);
        foreach (var (x, z) in new[] { (-6f, 5f), (6f, 5f), (-6f, 17f), (6f, 17f) }) Lantern(t, W(x, floor, z));
        foreach (float s in new[] { -1f, 1f }) NightLights.Lamp(t, W(s * 4.2f, floor + 4.6f, hz + 1.2f), new Vector3(0.3f, 0.45f, 0.3f), night, 3.4f);
        // names of the chambers by the hall
        foreach (var (s, z, ar, en) in new[] { (1f, -4f, "مجلس السفراء", "HALL OF THE AMBASSADORS"), (1f, -12f, "خزانة الكتب", "THE LIBRARY"), (1f, -20.25f, "بيت المال", "THE TREASURY"),
                                               (-1f, -4f, "ديوان الرسائل", "THE CHANCERY"), (-1f, -12f, "مجلس الوزراء", "COUNCIL OF THE VIZIERS"), (-1f, -20.25f, "خزانة الكسوة", "THE WARDROBE") })
            Sign(t, W(s * 20.63f, floor + 3.05f, z), s * 90f, ar, en, 2f, 0.66f);
        Sign(t, W(0, floor + 8.1f, 0.65f), 180f, "مجلس الخلافة", "THE THRONE HALL", 3.2f, 0.6f);
        // the hidden way onto the throne hall roof: crates left by the craftsmen in the east corridor up to
        // the chamber roof, two more there and a plank across the corridor to the hall roof
        float rf = c.y + floor + 0.04f;
        foreach (var (lx, lz, top) in new[] { (18f, -24.6f, 1.3f), (19.4f, -24.6f, 2.1f), (19.4f, -23.2f, 2.9f), (18f, -23.2f, 3.7f), (18f, -21.8f, 4.5f), (19.6f, -21.8f, 5.3f) })
        {
            var p = W(lx, 0, lz);
            Crates(t, p.x, p.z, rf, c.y + top);
        }
        float chamberTop = c.y + floor + 5.6f;
        foreach (var (lx, lz, top) in new[] { (21.6f, -21.8f, 6.9f), (21.6f, -20.4f, 7.7f), (21.6f, -19f, 8.5f) })
        {
            var p = W(lx, 0, lz);
            Crates(t, p.x, p.z, chamberTop, c.y + top);
        }
        var plank = W(19.2f, 8.5f, -19f);
        Box(t, "Plank", plank, new Vector3(4f, 0.1f, 0.6f), Wood, 1f);
        // the approach from the street, a ramp up onto the podium
        Underground.Wedge(t, "Ramp", W(0, Lower, hz + 4.6f), W(0, floor, hz + 2f), 6f, Paving);
        Slab(t, "Approach", Rect.MinMaxRect(c.x - 4f, c.z + hz + 2f, c.x + 4f, MidSouth - 4f), Lower + 0.05f, 0.1f, Paving, 2.5f);
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


    private static int houses;

    /// <summary>
    /// An Andalusian courtyard house (front, with the door, towards local -Z): whitewashed or ochre lime
    /// over a stone plinth, a horseshoe-arched door in an alfiz, lattice windows, rooms round a patio
    /// with a fountain or a pool, an orange tree and pots of flowers, tiled roofs sloping into the patio.
    /// Grand houses get a gallery on columns, a second storey and a lattice balcony, and a name board.
    /// </summary>
    private static void CourtHouse(Vector3 c, float yaw, float w, float d, int floors, int seed, string arabic = null, string english = null, Material patio = null, bool pool = false)
    {
        houses++;
        var t = Node("House", root, c, yaw);
        var r = new System.Random(seed);
        float h = floors > 1 ? 7f : 4.6f;
        const float th = 0.45f;
        var wall = r.NextDouble() < 0.55 ? Plaster : LimeOchre;
        float hw = w / 2f, hd = d / 2f;
        float pw = Mathf.Max(3.6f, w * 0.42f), pd = Mathf.Max(3.6f, d * 0.42f);
        float inner = h - 0.7f;
        LBox(t, "HouseFloor", new Vector3(0, 0.05f, 0), new Vector3(w, 0.1f, d), BrickFloor, 2f);
        LBox(t, "Patio", new Vector3(0, 0.12f, 0), new Vector3(pw, 0.04f, pd), patio ?? (r.NextDouble() < 0.35 ? Violet : r.NextDouble() < 0.5 ? Paving : Marble), 2f, false);
        // outer walls: the front with the door, the others with lattice windows
        float door = (float)(r.NextDouble() - 0.5) * Mathf.Max(0f, pw - 2.6f);
        LBox(t, "Wall", new Vector3((-hw + door - 1.15f) / 2f, h / 2f, -hd + th / 2f), new Vector3(door - 1.15f + hw, h, th), wall);
        LBox(t, "Wall", new Vector3((hw + door + 1.15f) / 2f, h / 2f, -hd + th / 2f), new Vector3(hw - door - 1.15f, h, th), wall);
        var front = Node("Door", t, t.TransformPoint(new Vector3(door, 0, -hd + th / 2f)), yaw);
        Arch(front, 0, 2.3f, 2.2f, h, th, 30f, wall);
        LBox(t, "DoorLeaf", new Vector3(door - 0.55f, 1.2f, -hd + th + 0.35f), new Vector3(0.08f, 2.3f, 0.9f), Wood, 1f, false);
        LBox(t, "Wall", new Vector3(0, h / 2f, hd - th / 2f), new Vector3(w, h, th), wall);
        foreach (float s in new[] { -1f, 1f })
            LBox(t, "Wall", new Vector3(s * (hw - th / 2f), h / 2f, 0), new Vector3(th, h, d - th * 2f), wall);
        // the stone plinth outside, a band under the eaves
        LBox(t, "Plinth", new Vector3(0, 0.4f, hd + 0.03f), new Vector3(w + 0.12f, 0.8f, 0.06f), Ashlar, 1.5f, false);
        foreach (float s in new[] { -1f, 1f })
        {
            LBox(t, "Plinth", new Vector3(s * (hw + 0.03f), 0.4f, 0), new Vector3(0.06f, 0.8f, d), Ashlar, 1.5f, false);
            float a = s < 0 ? -hw : door + 1.15f, b = s < 0 ? door - 1.15f : hw;
            LBox(t, "Plinth", new Vector3((a + b) / 2f, 0.4f, -hd - 0.03f), new Vector3(b - a, 0.8f, 0.06f), Ashlar, 1.5f, false);
        }
        // lattice windows high up (and on the upper storey)
        foreach (float wy in floors > 1 ? new[] { 2.4f, 5.2f } : new[] { 2.5f })
        {
            for (float u = -hw + 1.6f; u < hw - 1.2f; u += 3.2f)
                LBox(t, "Lattice", new Vector3(u, wy, hd + 0.04f), new Vector3(0.9f, 1.1f, 0.06f), lattice, 1f, false);
            foreach (float s in new[] { -1f, 1f })
                for (float u = -hd + 1.8f; u < hd - 1.2f; u += 3.6f)
                    LBox(t, "Lattice", new Vector3(s * (hw + 0.04f), wy, u), new Vector3(0.06f, 1.1f, 0.9f), lattice, 1f, false);
            if (wy > 3f)
                for (float u = -hw + 1.4f; u < hw - 1f; u += 3f)
                    if (Mathf.Abs(u - door) > 1.6f) LBox(t, "Lattice", new Vector3(u, wy, -hd - 0.04f), new Vector3(0.9f, 1.1f, 0.06f), lattice, 1f, false);
        }
        // the patio walls (lower than the outside walls), a door in each; a gallery on columns at the back
        bool gallery = floors > 1 || r.NextDouble() < 0.5;
        foreach (float s in new[] { -1f, 1f })
        {
            Wall(t, t.TransformPoint(new Vector3(s * pw / 2f, 0, -pd / 2f)), t.TransformPoint(new Vector3(s * pw / 2f, 0, pd / 2f)), inner, 0.3f, wall, pd / 2f, 1.1f, 2.3f);
            // partitions: four rooms in the corners
            Wall(t, t.TransformPoint(new Vector3(s * pw / 2f, 0, -pd / 2f)), t.TransformPoint(new Vector3(s * pw / 2f, 0, -hd + th)), inner, 0.25f, wall, (hd - th - pd / 2f) / 2f, 1f, 2.2f);
            Wall(t, t.TransformPoint(new Vector3(s * pw / 2f, 0, pd / 2f)), t.TransformPoint(new Vector3(s * pw / 2f, 0, hd - th)), inner, 0.25f, wall);
        }
        Wall(t, t.TransformPoint(new Vector3(-pw / 2f, 0, -pd / 2f)), t.TransformPoint(new Vector3(pw / 2f, 0, -pd / 2f)), inner, 0.3f, wall, pw / 2f + door, 1.2f, 2.3f);
        if (gallery)
        {
            int bays = pw > 5.5f ? 3 : 2;
            Arcade(t, t.TransformPoint(new Vector3(-pw / 2f, 0, pd / 2f)), t.TransformDirection(Vector3.right), bays, pw / bays, 2.2f, inner, 0.36f, 28f, wall, true);
        }
        else Wall(t, t.TransformPoint(new Vector3(-pw / 2f, 0, pd / 2f)), t.TransformPoint(new Vector3(pw / 2f, 0, pd / 2f)), inner, 0.3f, wall, pw / 2f, 1.2f, 2.3f);
        // one tiled roof all round, its four slopes running down into the patio
        var roof = MeshObject(t, "Roof", CourtRoof(hw + 0.35f, hd + 0.35f, pw / 2f, pd / 2f, h + 0.2f, inner), RoofTiles, true);
        roof.transform.localPosition = Vector3.zero;
        // a lattice balcony on the front of a grand house
        if (floors > 1)
        {
            float bx = door + (door > 0 ? -3.2f : 3.2f);
            if (Mathf.Abs(bx) < hw - 1.6f)
            {
                LBox(t, "Balcony", new Vector3(bx, 3.6f, -hd - 0.55f), new Vector3(2.4f, 0.14f, 1.1f), Wood, 1f);
                LBox(t, "BalconyLattice", new Vector3(bx, 4.4f, -hd - 1.08f), new Vector3(2.4f, 1.5f, 0.06f), lattice, 1f, false);
                foreach (float s in new[] { -1f, 1f })
                    LBox(t, "BalconyLattice", new Vector3(bx + s * 1.18f, 4.4f, -hd - 0.55f), new Vector3(0.06f, 1.5f, 1.1f), lattice, 1f, false);
                LBox(t, "BalconyRoof", new Vector3(bx, 5.25f, -hd - 0.6f), new Vector3(2.7f, 0.1f, 1.3f), RoofTiles, 1f, false);
            }
        }
        // the patio: a fountain or a pool, an orange tree, pots of flowers
        var pc = t.TransformPoint(Vector3.zero);
        if (pool)
        {
            var right = t.TransformDirection(Vector3.right); var fwd = t.TransformDirection(Vector3.forward);
            var a = pc - right * (pw / 2f - 1.1f) - fwd * (pd / 2f - 1.1f);
            var b = pc + right * (pw / 2f - 1.1f) + fwd * (pd / 2f - 1.1f);
            Pool(t, Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)), c.y + 0.5f, 0.5f);
        }
        else
        {
            MeshGen.Object("PatioFountain", MeshGen.Cylinder(0.75f, 0.5f, 1f, 8), Cream, t, pc + Vector3.up * 0.1f).AddComponent<MeshCollider>();
            var fw = MeshGen.TexCylinder("PatioWater", pc + Vector3.up * 0.55f, 0.65f, 0.02f, Water, 2f, t, false);
            fw.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            OrangeTree(t, t.TransformPoint(new Vector3(pw / 2f - 1f, 0.12f, pd / 2f - 1f)));
        }
        foreach (var (px, pz) in new[] { (-pw / 2f + 0.5f, -pd / 2f + 0.5f), (pw / 2f - 0.5f, -pd / 2f + 0.5f) })
        {
            var pot = t.TransformPoint(new Vector3(px, 0.12f, pz));
            MeshGen.TexCylinder("Pot", pot, 0.26f, 0.5f, Terracotta, 1f, t, false);
            if (px < 0f) Small(Nature.Place("Bush_Common_Flowers", pot + Vector3.up * 0.45f, R(0, 360), 0.7f, t));
        }
        // a lantern by the door (its glow on the street at the grand houses and every third one), a lit window
        NightLights.Lamp(t, t.TransformPoint(new Vector3(door + 1.6f, 2.6f, -hd - 0.3f)), new Vector3(0.2f, 0.3f, 0.2f), night, arabic != null || houses % 3 == 0 ? 2.6f : 0f);
        if (r.NextDouble() < 0.6) NightLights.Window(t, t.TransformPoint(new Vector3(-hw + 1.6f, 2.5f, hd + 0.08f)), t.TransformDirection(Vector3.forward), 0.8f, 1f, night);
        if (arabic != null) Sign(t, t.TransformPoint(new Vector3(door, Mathf.Min(h - 0.35f, 4.25f), -hd - 0.1f)), yaw, arabic, english, 2.8f, 0.8f);
    }

    /// <summary>
    /// The roof of a courtyard house in its own frame: four slopes from the eaves (half sizes ox, oz at
    /// 'high') down to the patio edge (px, pz at 'low'), the tiles running along the eaves, a fascia at
    /// the eaves and the underside seen from the rooms.
    /// </summary>
    private static Mesh CourtRoof(float ox, float oz, float px, float pz, float high, float low)
    {
        string key = $"roof_{K(ox)}_{K(oz)}_{K(px)}_{K(pz)}_{K(high)}_{K(low)}";
        if (meshes.TryGetValue(key, out var cached) && cached != null) return cached;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3, Vector3 up)
        {
            var normal = Vector3.Cross(p1 - p0, p3 - p0).normalized;
            bool flip = Vector3.Dot(normal, up) < 0f;
            if (flip) normal = -normal;
            int i = v.Count;
            v.AddRange(new[] { p0, p1, p2, p3 });
            uv.AddRange(new[] { u0, u1, u2, u3 });
            for (int k = 0; k < 4; k++) n.Add(normal);
            // front faces as in Builder.Face: (p0, p1, p2) turning round the normal
            if (!flip) tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
        const float thick = 0.12f;
        // the four slopes: x along the eave, the slope from the eave to the patio
        var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        for (int side = 0; side < 4; side++)
        {
            var c0 = corners[side]; var c1 = corners[(side + 1) % 4];
            var o0 = new Vector3(c0.x * ox, high, c0.y * oz); var o1 = new Vector3(c1.x * ox, high, c1.y * oz);
            var i0 = new Vector3(c0.x * px, low, c0.y * pz); var i1 = new Vector3(c1.x * px, low, c1.y * pz);
            bool alongX = side % 2 == 0;
            float slope = Vector2.Distance(new Vector2(alongX ? o0.z : o0.x, high), new Vector2(alongX ? i0.z : i0.x, low));
            Vector2 U(Vector3 p, float across) => new Vector2((alongX ? p.x : p.z) / 2f, across / 2f);
            Quad(o0, o1, i1, i0, U(o0, 0), U(o1, 0), U(i1, slope), U(i0, slope), Vector3.up);
            var d = Vector3.down * thick;
            Quad(o0 + d, o1 + d, i1 + d, i0 + d, U(o0, 0), U(o1, 0), U(i1, slope), U(i0, slope), Vector3.down);
            // the fascia at the eave
            var outward = new Vector3(alongX ? 0 : c0.x, 0, alongX ? c0.y : 0);
            Quad(o0, o1, o1 + d, o0 + d, U(o0, 0), U(o1, 0), U(o1, 0.06f), U(o0, 0.06f), outward);
        }
        var m = new Mesh { name = key };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        m.RecalculateTangents();
        MeshUtility.SetMeshCompression(m, ModelImporterMeshCompression.Medium);
        AssetSetup.Folder(MeshFolder);
        AssetDatabase.CreateAsset(m, $"{MeshFolder}/{key}.asset");
        return meshes[key] = m;
    }

    /// <summary>
    /// The famous houses of the lower town, each with its name board: Casa de Ya'far (a triple arcade
    /// on columns over a violet stone courtyard), its service house, the Patio de la Pila, the House of
    /// the Pool.
    /// </summary>
    private static void Houses()
    {
        var t = Node("Houses", root, Vector3.zero, 0);
        const float h = 5f, th = 0.6f;
        // Casa de Ya'far: x -84..-60, z -56..-36
        float x0 = -84f, x1 = -60f, z0 = -56f, z1 = -36f;
        Slab(t, "YafarFloor", Rect.MinMaxRect(x0, z0, x1, z1), Lower + 0.05f, 0.1f, BrickFloor, 2f);
        Slab(t, "YafarCourt", Rect.MinMaxRect(-78f, -52f, -66f, -42f), Lower + 0.1f, 0.06f, Violet, 2f);
        MeshGen.Object("Basin", MeshGen.Cylinder(0.9f, 0.5f, 1f, 8), Marble, t, new Vector3(-72f, Lower + 0.1f, -47f)).AddComponent<MeshCollider>();
        var bw = MeshGen.TexCylinder("BasinWater", new Vector3(-72f, Lower + 0.56f, -47f), 0.8f, 0.02f, Water, 2f, t, false);
        bw.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Wall(t, new Vector3(x0, Lower, z1), new Vector3(x1, Lower, z1), h, th, Plaster);
        Wall(t, new Vector3(x0, Lower, z0), new Vector3(x1, Lower, z0), h, th, Plaster, 12f, 1.6f, 2.8f);
        Wall(t, new Vector3(x0, Lower, z0), new Vector3(x0, Lower, z1), h, th, Plaster);
        Wall(t, new Vector3(x1, Lower, z0), new Vector3(x1, Lower, z1), h, th, Plaster, 9f, 1.6f, 2.8f);
        Arcade(t, new Vector3(-77.4f, Lower, -42f), Vector3.right, 3, 3.6f, 2.6f, h, 0.6f, 28f, Plaster, true);
        Wall(t, new Vector3(x0, Lower, -42f), new Vector3(-77.4f, Lower, -42f), h, th, Plaster);
        Wall(t, new Vector3(-66.6f, Lower, -42f), new Vector3(x1, Lower, -42f), h, th, Plaster);
        Wall(t, new Vector3(-78f, Lower, -52f), new Vector3(-78f, Lower, -42f), h - 0.6f, 0.4f, Plaster, 5f, 1.2f, 2.4f);
        Wall(t, new Vector3(-66f, Lower, -52f), new Vector3(-66f, Lower, -42f), h - 0.6f, 0.4f, Plaster, 5f, 1.2f, 2.4f);
        Wall(t, new Vector3(-78f, Lower, -52f), new Vector3(-66f, Lower, -52f), h - 0.6f, 0.4f, Plaster, 6f, 1.4f, 2.4f);
        Box(t, "HallRoof", new Vector3((x0 + x1) / 2f, h + 0.2f, -39f), new Vector3(x1 - x0 + 0.6f, 0.4f, 6.6f), RoofTiles, 2f);
        Box(t, "WingRoof", new Vector3(-81f, h + 0.2f, -47f), new Vector3(6.6f, 0.4f, 10.6f), RoofTiles, 2f);
        Box(t, "WingRoof", new Vector3(-63f, h + 0.2f, -47f), new Vector3(6.6f, 0.4f, 10.6f), RoofTiles, 2f);
        Box(t, "WingRoof", new Vector3(-72f, h + 0.2f, -54f), new Vector3(24.6f, 0.4f, 4.6f), RoofTiles, 2f);
        Box(t, "Dado", new Vector3(-72f, Lower + 0.6f, -36.33f), new Vector3(23f, 1.2f, 0.05f), Almagra, 1.5f, false);
        OrangeTree(t, new Vector3(-76f, Lower + 0.1f, -50f));
        OrangeTree(t, new Vector3(-68f, Lower + 0.1f, -44f));
        HangingLamp(t, new Vector3(-72f, Lower + 3.6f, -39f), 1f);
        Sign(t, new Vector3(-72f, 4.25f, z0 - 0.4f), 0f, "دار جعفر", "HOUSE OF JA'FAR, CHIEF MINISTER", 3.2f, 0.9f);
        NightLights.Lamp(t, new Vector3(-70f, 2.7f, z0 - 0.45f), new Vector3(0.22f, 0.32f, 0.22f), night, 2.6f);
        // the service house behind the alley, with its bread oven; the Patio de la Pila; the House of the Pool
        CourtHouse(new Vector3(-90.5f, Lower, -46f), 90f, 19f, 9f, 1, 11, "دار الخدمة", "THE SERVICE HOUSE");
        MeshGen.TexCylinder("Oven", new Vector3(-98.2f, Lower, -40f), 1f, 1.3f, Earth, 1f, t);
        CourtHouse(new Vector3(-72f, Lower, -71f), 0f, 22f, 20f, 2, 12, "صحن الحوض", "PATIO DE LA PILA - COURT OF THE BASIN", Marble);
        CourtHouse(new Vector3(-90.5f, Lower, -72f), 90f, 24f, 9.5f, 1, 13, "دار البركة", "HOUSE OF THE POOL", Paving, true);
        CypressTree(t, new Vector3(-57f, Lower, -60f), 9f);
        CypressTree(t, new Vector3(-57f, Lower, -86f), 10f);
    }

    /// <summary>
    /// The town: courtyard houses in their dozens on free plots of all three terraces (never on the
    /// gardens, halls, streets, stairs or the palace), in rows along the streets.
    /// </summary>
    private static void Medina()
    {
        var taken = new List<Rect>
        {
            // lower terrace
            Rect.MinMaxRect(-86f, -58f, -58f, -34f), Rect.MinMaxRect(-86f, -84f, -58f, -58f), Rect.MinMaxRect(-97f, -86f, -84f, -34f),
            Rect.MinMaxRect(-42f, -92f, 32f, -36f), Rect.MinMaxRect(-52f, -80f, -44f, -28f), Rect.MinMaxRect(GateX - 4f, -127f, GateX + 4f, -28f),
            Rect.MinMaxRect(40f, -98f, 100f, -28f), Rect.MinMaxRect(33f, -40f, 41f, -28f), Rect.MinMaxRect(GateX + 3f, -118f, GateX + 10f, -106f),
            Rect.MinMaxRect(-127f, -92f, 127f, -88f), Rect.MinMaxRect(-127f, -36f, 127f, -28f), Rect.MinMaxRect(-12f, -40f, 2f, -28f),
            // middle terrace
            Rect.MinMaxRect(-44f, -30f, 34f, 40f), Rect.MinMaxRect(-44f, 30f, 12f, 62f), Rect.MinMaxRect(-62f, 46f, -54f, 62f),
            Rect.MinMaxRect(-52f, -31f, -42f, 32f), Rect.MinMaxRect(30f, -30f, 102f, 46f), Rect.MinMaxRect(46f, 44f, 93f, 62f), Rect.MinMaxRect(12f, 38f, 30f, 62f),
            // upper terrace
            Rect.MinMaxRect(-96f, 58f, -34f, 92f), Rect.MinMaxRect(-36f, 58f, 14f, 93f), Rect.MinMaxRect(10f, 62f, 48f, 90f), Rect.MinMaxRect(50f, 62f, 92f, 88f),
            Rect.MinMaxRect(-38f, 116f, -20f, 127f), Rect.MinMaxRect(52f, 116f, 70f, 127f), Rect.MinMaxRect(16f, 58f, 26f, 68f),
            // the store rows against the walls (the hidden ways up)
            Rect.MinMaxRect(-124f, -59f, -118.5f, -40f), Rect.MinMaxRect(118.5f, -13f, 124f, 5f), Rect.MinMaxRect(48f, -124f, 66f, -118.5f),
        };
        int seed = 100;
        void Plots(float[] xs, float[] zs, float y)
        {
            foreach (float x in xs)
                foreach (float z in zs)
                {
                    float w = R(10.5f, 12.5f), d = R(10.5f, 12.5f);
                    var area = Rect.MinMaxRect(x - w / 2f - 1f, z - d / 2f - 1f, x + w / 2f + 1f, z + d / 2f + 1f);
                    if (Mathf.Abs(x) + w / 2f > 122.5f || Mathf.Abs(z) + d / 2f > 122.5f) continue;
                    if (taken.Any(r => r.Overlaps(area))) continue;
                    taken.Add(area);
                    // the door towards the nearest street or the middle of the town
                    float yaw = Mathf.Abs(x - GateX) < 20f ? (x < GateX ? 90f : -90f) : z < -60f ? 180f : (rng.NextDouble() < 0.5 ? 0f : 90f * (x < 0 ? 1 : -1));
                    CourtHouse(new Vector3(x, y, z), yaw, w, d, rng.NextDouble() < 0.3 ? 2 : 1, seed++);
                }
        }
        Plots(new[] { -115f, -102f }, new[] { -116f, -102f, -88f, -74f, -60f, -46f }, Lower);
        Plots(new[] { -88f, -74f }, new[] { -116f, -101f }, Lower);
        Plots(new[] { -40f, -26f, -12f, 2f, 16f, 30f }, new[] { -115f, -100f }, Lower);
        Plots(new[] { 46f, 60f, 74f, 88f, 102f, 115f }, new[] { -114f }, Lower);
        Plots(new[] { 115f }, new[] { -98f, -84f, -70f, -56f, -42f }, Lower);
        Plots(new[] { -114f, -100f, -86f }, new[] { -20f, -6f, 8f, 22f, 36f, 50f }, Middle);
        Plots(new[] { 114f }, new[] { -18f, -4f, 10f, 24f, 38f, 52f }, Middle);
        Plots(new[] { -115f, -102f }, new[] { 70f, 86f, 102f }, Upper);
        Plots(new[] { 102f, 115f }, new[] { 70f, 86f, 102f }, Upper);
        Plots(new[] { -20f, -4f, 12f, 28f, 66f, 82f }, new[] { 106f }, Upper);
        Debug.Log($"[Map] Zahra town: {houses} courtyard houses");
    }


    /// <summary>Dar al-Mulk, the Caliph's house on the highest terrace: three halls behind arcades on a terrace with the view over the city, rooms behind them.</summary>
    private static void DarAlMulk()
    {
        var t = Node("DarAlMulk", root, Vector3.zero, 0);
        float y = Upper;
        Slab(t, "Terrace", Rect.MinMaxRect(-94f, UpSouth + 0.5f, -36f, 70f), y + 0.06f, 0.1f, Marble, 2f);
        for (int i = 0; i < 3; i++)
        {
            float x0 = -92f + i * 18f, x1 = x0 + 16f;
            Arcade(t, new Vector3(x0, y, 70f), Vector3.right, 4, 4f, 2.8f, 5f, 0.6f, 28f, Plaster, true);
            Wall(t, new Vector3(x0, y, 78f), new Vector3(x1, y, 78f), 5f, 0.6f, Plaster, 8f, 1.4f, 2.6f);
            Wall(t, new Vector3(x0, y, 70f), new Vector3(x0, y, 78f), 5f, 0.6f, Ashlar);
            Wall(t, new Vector3(x1, y, 70f), new Vector3(x1, y, 78f), 5f, 0.6f, Ashlar);
            Slab(t, "HallFloor", Rect.MinMaxRect(x0, 70f, x1, 78f), y + 0.06f, 0.1f, Marble, 2f);
            Box(t, "HallRoof", new Vector3((x0 + x1) / 2f, y + 5.2f, 74f), new Vector3(16.6f, 0.4f, 8.6f), RoofTiles, 2f);
            Box(t, "Dado", new Vector3((x0 + x1) / 2f, y + 0.6f, 77.67f), new Vector3(15.4f, 1.2f, 0.05f), Almagra, 1.5f, false);
            HangingLamp(t, new Vector3((x0 + x1) / 2f, y + 3.4f, 74f), 1.2f);
        }
        // the private rooms behind: a block with doors from the halls, roofed
        Slab(t, "RoomsFloor", Rect.MinMaxRect(-92f, 78f, -38f, 90f), y + 0.06f, 0.1f, BrickFloor, 2f);
        Wall(t, new Vector3(-92f, y, 90f), new Vector3(-38f, y, 90f), 5f, 0.6f, Ashlar);
        Wall(t, new Vector3(-92f, y, 78f), new Vector3(-92f, y, 90f), 5f, 0.6f, Ashlar);
        Wall(t, new Vector3(-38f, y, 78f), new Vector3(-38f, y, 90f), 5f, 0.6f, Ashlar, 6f, 1.4f, 2.6f);
        for (float x = -83f; x < -40f; x += 9f)
            Wall(t, new Vector3(x, y, 78f), new Vector3(x, y, 90f), 5f, 0.4f, Plaster, 6f, 1.2f, 2.4f);
        Box(t, "RoomsRoof", new Vector3(-65f, y + 5.2f, 84f), new Vector3(54.6f, 0.4f, 12.6f), RoofTiles, 2f);
        foreach (float x in new[] { -88f, -70f, -52f, -40f }) Lantern(t, new Vector3(x, y, 64f));
        CypressTree(t, new Vector3(-38f, y, 66f), 10f);
        CypressTree(t, new Vector3(-94f, y, 66f), 9f);
        BoardStand(t, new Vector3(-61f, y + 0.1f, 66f), 0f, "دار الملك", "DAR AL-MULK - THE CALIPH'S HOUSE");
        // behind the west end, out of sight: crates up to the roof of the private rooms
        CrateClimb(t, y, (-95.5f, 84f, y + 0.8f), (-95.5f, 85.4f, y + 1.6f), (-94.1f, 85.4f, y + 2.4f), (-94.1f, 86.8f, y + 3.2f), (-95.5f, 86.8f, y + 4.0f), (-93f, 88.2f, y + 4.8f));
        Buildings.Stairs(t, new Vector3(-58f, Middle, UpSouth - 10.8f), Vector3.forward, Upper - Middle, 3f, Ashlar);
    }


    /// <summary>The upper basilical hall (Dar al-Jund, the house of the army): a portico of five arches over its plaza, three naves on columns, a brick floor, a tiled roof.</summary>
    private static void BasilicalHall()
    {
        var t = Node("BasilicalHall", root, Vector3.zero, 0);
        float y = Upper;
        const float x0 = -26f, x1 = 8f, z0 = 70f, z1 = 91f, h = 6f;
        Slab(t, "Plaza", Rect.MinMaxRect(-34f, UpSouth + 0.5f, 12f, z0), y + 0.05f, 0.1f, Paving, 2.5f);
        Slab(t, "BrickFloor", Rect.MinMaxRect(x0, z0, x1, z1), y + 0.06f, 0.1f, BrickFloor, 2f);
        Arcade(t, new Vector3(x0, y, z0), Vector3.right, 5, (x1 - x0) / 5f, 3.4f, h, 0.9f, 28f, Ashlar, false, true, 0.8f);
        foreach (float x in new[] { -14.7f, -3.3f })
            Arcade(t, new Vector3(x, y, z0 + 5f), Vector3.forward, 4, (z1 - z0 - 5f) / 4f, 3f, h, 0.5f, 25f, Plaster, true);
        Wall(t, new Vector3(x0, y, z0), new Vector3(x0, y, z1), h, 0.8f, Ashlar, 12f, 1.6f, 2.8f);
        Wall(t, new Vector3(x1, y, z0), new Vector3(x1, y, z1), h, 0.8f, Ashlar, 12f, 1.6f, 2.8f);
        Wall(t, new Vector3(x0, y, z1), new Vector3(x1, y, z1), h, 0.8f, Ashlar);
        Box(t, "Roof", new Vector3((x0 + x1) / 2f, y + h + 0.2f, (z0 + z1) / 2f), new Vector3(x1 - x0 + 1f, 0.4f, z1 - z0 + 1f), RoofTiles, 2.5f);
        Box(t, "Ceiling", new Vector3((x0 + x1) / 2f, y + h - 0.03f, (z0 + z1) / 2f), new Vector3(x1 - x0 - 0.6f, 0.06f, z1 - z0 - 0.6f), Wood, 2f, false);
        var niche = Node("Niche", t, new Vector3((x0 + x1) / 2f, y, z1 - 0.65f), 0);
        Arch(niche, 0, 3.6f, 2.4f, 5f, 0.4f, 28f, Ataurique);
        foreach (float z in new[] { 76f, 84f }) HangingLamp(t, new Vector3(-9f, y + 4.3f, z), 1.2f);
        foreach (float x in new[] { -30f, 10f }) Lantern(t, new Vector3(x, y, 66f));
        BoardStand(t, new Vector3(-17f, y + 0.1f, 65f), 0f, "دار الجند", "DAR AL-JUND - HOUSE OF THE ARMY");
        // behind the hall: crates zigzagging up to its roof
        CrateClimb(t, y, (-22f, 96f, y + 0.8f), (-20.6f, 96f, y + 1.6f), (-20.6f, 94.6f, y + 2.4f), (-19.2f, 94.6f, y + 3.2f), (-19.2f, 93.2f, y + 4.0f), (-17.8f, 93.2f, y + 4.8f), (-17.8f, 92.1f, y + 5.6f));
    }


    /// <summary>The North Gate passage: a covered way bent twice, four arched doors in a row (a choke point); the House of the Viziers beside it.</summary>
    private static void NorthGate()
    {
        var t = Node("NorthGate", root, Vector3.zero, 0);
        float y = Upper;
        const float h = 4.5f, th = 0.6f;
        Box(t, "GateWall", new Vector3((28.5f + 46f) / 2f, y + h / 2f, 87.8f), new Vector3(46f - 28.5f + th, h, th), Ashlar);
        Box(t, "GateWall", new Vector3((31.5f + 46f) / 2f, y + h / 2f, 84.2f), new Vector3(46f - 31.5f, h, th), Ashlar);
        Box(t, "GateWall", new Vector3(28.2f, y + h / 2f, (71.5f + 88.1f) / 2f), new Vector3(th, h, 88.1f - 71.5f), Ashlar);
        Box(t, "GateWall", new Vector3(31.8f, y + h / 2f, (67.9f + 84.5f) / 2f), new Vector3(th, h, 84.5f - 67.9f), Ashlar);
        Box(t, "GateWall", new Vector3((12f + 28.5f) / 2f, y + h / 2f, 71.8f), new Vector3(28.5f - 12f, h, th), Ashlar);
        Box(t, "GateWall", new Vector3((12f + 32.1f) / 2f, y + h / 2f, 68.2f), new Vector3(32.1f - 12f, h, th), Ashlar);
        Box(t, "GateRoof", new Vector3((28f + 46f) / 2f, y + h + 0.2f, 86f), new Vector3(18f, 0.4f, 4.2f), Ashlar);
        Box(t, "GateRoof", new Vector3(30f, y + h + 0.2f, (68f + 84f) / 2f), new Vector3(4.2f, 0.4f, 16f), Ashlar);
        Box(t, "GateRoof", new Vector3((12f + 28f) / 2f, y + h + 0.2f, 70f), new Vector3(16f, 0.4f, 4.2f), Ashlar);
        void Door(Vector3 at, float yaw) => Arch(Node("Door", t, at, yaw), 0, 3f, 2.3f, h, 0.6f, 28f, Ashlar);
        Door(new Vector3(42f, y, 86f), 90f);
        Door(new Vector3(30f, y, 80f), 0f);
        Door(new Vector3(30f, y, 74f), 0f);
        Door(new Vector3(20f, y, 70f), 90f);
        foreach (var p in new[] { new Vector3(36f, y + h - 0.6f, 86f), new Vector3(30f, y + h - 0.6f, 77f), new Vector3(24f, y + h - 0.6f, 70f) })
            HangingLamp(t, p, 0.4f);
        Sign(t, new Vector3(46.4f, y + h - 0.7f, 86f), -90f, "باب الجبل", "THE MOUNTAIN GATE", 2.6f, 0.8f);
        CourtHouse(new Vector3(70f, Upper, 75f), 0f, 36f, 18f, 2, 7, "دار الوزراء", "DAR AL-WUZARA - HOUSE OF THE VIZIERS", Paving, true);
    }


    /// <summary>
    /// Ammunition: in the streets, the gardens and the palace gallery, never at the strong points (roofs,
    /// walls, towers, the high terrace): whoever holds one has to leave it for more.
    /// </summary>
    private static void Supplies()
    {
        var t = Node("Supplies", root, Vector3.zero, 0);
        foreach (var (p, yaw) in new[] { (new Vector3(70f, Middle + 0.1f, -4f), 0f), (new Vector3(-30f, Middle + 0.5f, 28.5f), 90f), (new Vector3(GateX, Lower + 0.1f, -60f), 90f),
                                          (new Vector3(-10f, Upper + 0.1f, 96f), 0f), (new Vector3(92f, Upper + 0.1f, 96f), 90f),
                                          (new Vector3(10f, Lower + 0.5f, -60.5f), 0f), (new Vector3(-44f, Middle + 0.1f, 24f), 0f), (new Vector3(0f, Lower + 0.1f, -90f), 0f),
                                          (new Vector3(51f, Lower + 0.55f, -65f), 0f), (new Vector3(-90f, Middle + 0.1f, 15f), 0f) })
            Obstacles.AmmoCrate(t, p, yaw);
    }


    /// <summary>A smooth height field over 'area' (nx by nz cells), texture repeating every 'tex' metres.</summary>
    private static Mesh HillMesh(string key, Rect area, int nx, int nz, System.Func<float, float, float> height, float tex)
    {
        if (meshes.TryGetValue(key, out var cached) && cached != null) return cached;
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = Mathf.Lerp(area.xMin, area.xMax, i / (float)nx), z = Mathf.Lerp(area.yMin, area.yMax, j / (float)nz);
                v.Add(new Vector3(x, height(x, z), z));
                uv.Add(new Vector2(x, z) / tex);
            }
        var tris = new List<int>();
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        var m = new Mesh { name = key };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        m.RecalculateTangents();
        MeshUtility.SetMeshCompression(m, ModelImporterMeshCompression.Medium);
        AssetSetup.Folder(MeshFolder);
        AssetDatabase.CreateAsset(m, $"{MeshFolder}/{key}.asset");
        return meshes[key] = m;
    }

    private static float Fbm(float x, float z, int seed)
    {
        float v = 0, amp = 1, f = 1f / 90f, total = 0;
        for (int o = 0; o < 4; o++) { v += amp * Mathf.PerlinNoise(x * f + seed * 13.1f, z * f + seed * 7.7f); total += amp; amp *= 0.5f; f *= 2.1f; }
        return v / total;
    }

    /// <summary>
    /// Beyond the walls: the Sierra Morena behind (hills of holm oak and pine), low hills on the sides,
    /// the valley in front with fields, olive groves and the Guadalquivir between poplars and reeds.
    /// </summary>
    private static void Surroundings()
    {
        var t = Node("Surroundings", root, Vector3.zero, 0);
        var hills = PortBuilder.Pbr("Z_Hills", "Grass004", new Color(0.62f, 0.72f, 0.48f), 0.1f);
        float North(float x, float z) => Upper - 2f + Mathf.Max(0f, z - 132f) * 0.32f * (0.55f + Fbm(x, z, 1)) + Fbm(x, z, 2) * 18f * Mathf.Clamp01((z - 132f) / 40f);
        var mesh = HillMesh("hills_north", Rect.MinMaxRect(-420f, 130f, 420f, 420f), 56, 22, North, 10f);
        var go = MeshObject(t, "SierraMorena", mesh, hills, false);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (float s in new[] { -1f, 1f })
        {
            float Side(float x, float z) => Level(z) - 1f + Mathf.Max(0f, Mathf.Abs(x) - 132f) * 0.18f * Fbm(x, z, 3) * 2f;
            var side = HillMesh(s < 0 ? "hills_west" : "hills_east", s < 0 ? Rect.MinMaxRect(-420f, -127f, -130f, 132f) : Rect.MinMaxRect(130f, -127f, 420f, 132f), 18, 16, Side, 10f);
            var sg = MeshObject(t, "SideHills", side, hills, false);
            sg.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // woods on the hills: pines and holm oaks, thicker further up
        for (int i = 0; i < 80; i++)
        {
            float x = R(-300f, 300f), z = R(136f, 280f);
            Scenery(Nature.Tree(rng.NextDouble() < 0.55 ? Nature.Pines : Nature.Trees, rng, new Vector3(x, North(x, z) - 0.3f, z), 9f, 15f, t));
        }
        for (int i = 0; i < 28; i++)
        {
            float s = rng.NextDouble() < 0.5 ? -1f : 1f, x = s * R(136f, 260f), z = R(-120f, 125f);
            Scenery(Nature.Tree(Nature.Trees, rng, new Vector3(x, Level(z) - 1f + Mathf.Max(0f, Mathf.Abs(x) - 132f) * 0.18f * Fbm(x, z, 3) * 2f - 0.3f, z), 6f, 10f, t));
        }
        // the valley: strips of green wheat and of ploughed earth, olive groves in rows
        for (int i = 0; i < 9; i++)
        {
            float z = -142f - i * 17f;
            for (int j = 0; j < 7; j++)
            {
                float x = -300f + j * 95f + R(-12f, 12f);
                // thin as paint on the valley floor: no edges to see
                Box(t, "Field", new Vector3(x, -2.985f, z), new Vector3(R(60f, 85f), 0.012f, 13f), (i + j) % 3 == 0 ? Earth : Wheat, 6f, false);
            }
        }
        for (int row = 0; row < 4; row++)
            for (float x = -240f; x < 240f; x += R(9f, 14f))
                if (rng.NextDouble() < 0.35) Scenery(Nature.Tree(Nature.Trees, rng, new Vector3(x, -3f, -150f - row * 30f + R(-3f, 3f)), 3.5f, 5f, t));
        // the Guadalquivir: flowing west between banks, poplars and reeds
        var flow = AssetSetup.WaterMat("Water_River", new Color(0.05f, 0.15f, 0.15f), new Color(0.22f, 0.36f, 0.32f), 1.2f, 0.25f, new Vector3(-1f, 1.2f, 0f));
        var river = MeshGen.TexBox("Guadalquivir", new Vector3(0, -3.3f, -300f), new Vector3(760f, 0.05f, 46f), flow, 20f, t, 0f, false);
        river.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (float s in new[] { -1f, 1f })
        {
            Box(t, "RiverBank", new Vector3(0, -2.92f, -300f + s * 24.5f), new Vector3(760f, 0.3f, 3f), Earth, 4f, false);
            for (float x = -280f; x < 280f; x += R(22f, 34f))
                Scenery(Nature.Tree(Nature.Trees, rng, new Vector3(x, -3f, -300f + s * 29f), 10f, 14f, t));
        }
    }

    /// <summary>
    /// The water of the city: from a spring pool on the upper terrace a cascade runs down the terrace
    /// face into a basin and on along a channel to the Salón Rico patio; channels on the axes of the
    /// Upper Garden run into its pools; twin cascades beside the great stair feed two channels running
    /// down the Lower Garden to its fountain.
    /// </summary>
    private static void Waters()
    {
        var t = Node("Waters", root, Vector3.zero, 0);
        const float x = 21f;
        Pool(t, Rect.MinMaxRect(x - 2.5f, UpSouth + 1.5f, x + 2.5f, UpSouth + 6f), Upper + 0.5f, 0.6f);
        Cascade(t, new Vector3(x, Upper + 0.2f, UpSouth + 1.2f), new Vector3(x, Middle + 0.3f, UpSouth - 8f), 1.6f);
        Pool(t, Rect.MinMaxRect(x - 2.5f, UpSouth - 12.5f, x + 2.5f, UpSouth - 8f), Middle + 0.5f, 0.6f);
        Channel(t, new Vector3(x, 0, UpSouth - 12.8f), new Vector3(x, 0, 32.3f), 0.7f, Middle + 0.72f, Middle);
        var bc = new Vector3(x, Middle + 0.5f, 31f);
        MeshGen.Object("ChannelBasin", MeshGen.Cylinder(1.3f, 0.45f, 1f, 8), Cream, t, bc).AddComponent<MeshCollider>();
        var bw = MeshGen.TexCylinder("BasinWater", bc + Vector3.up * 0.4f, 1.15f, 0.02f, Water, 2f, t, false);
        bw.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // the garden axes
        float walk = Middle + 0.5f;
        Channel(t, new Vector3(-5f, 0, 27f), new Vector3(-5f, 0, 17.3f), 0.5f, walk + 0.18f);
        Channel(t, new Vector3(-5f, 0, -25f), new Vector3(-5f, 0, -15.3f), 0.5f, walk + 0.18f);
        Channel(t, new Vector3(-39f, 0, 1f), new Vector3(-28.3f, 0, 1f), 0.5f, walk + 0.18f);
        Channel(t, new Vector3(29f, 0, 1f), new Vector3(18.3f, 0, 1f), 0.5f, walk + 0.18f);
        // the twin cascades beside the great stair and the Lower Garden channels
        foreach (float cx in new[] { -9.4f, -0.6f })
        {
            Cascade(t, new Vector3(cx, Middle + 0.1f, MidSouth + 0.3f), new Vector3(cx, Lower + 0.35f, MidSouth - 6.6f), 0.9f);
            Channel(t, new Vector3(cx, 0, MidSouth - 6.8f), new Vector3(cx, 0, -59f), 0.6f, Lower + 0.32f);
        }
    }

    /// <summary>The royal baths west of the Salón Rico: a cold, a warm and a hot room behind arched doors, domes with star-shaped skylights, a pool.</summary>
    private static void Hammam()
    {
        var t = Node("Hammam", root, Vector3.zero, 0);
        float y = Middle;
        const float x0 = -42f, x1 = -22.4f, z0 = 32f, z1 = 58f, h = 5f;
        Slab(t, "BathFloor", Rect.MinMaxRect(x0, z0, x1, z1), y + 0.1f, 0.1f, Marble, 2f);
        Wall(t, new Vector3(x0, y, z0), new Vector3(x1, y, z0), h, 0.6f, LimeOchre, 9.8f, 1.6f, 2.8f);
        Wall(t, new Vector3(x0, y, z1), new Vector3(x1, y, z1), h, 0.6f, LimeOchre);
        Wall(t, new Vector3(x0, y, z0), new Vector3(x0, y, z1), h, 0.6f, LimeOchre);
        Wall(t, new Vector3(x1, y, z0), new Vector3(x1, y, z1), h, 0.6f, LimeOchre, 18f, 1.4f, 2.6f);
        // partitions between the three rooms, an arched door in each
        foreach (float z in new[] { 40.7f, 49.3f })
        {
            var p = Node("BathArch", t, new Vector3((x0 + x1) / 2f, y, z), 0);
            Arch(p, 0, 2.4f, 2f, h, 0.5f, 30f, Plaster);
            Box(t, "BathWall", new Vector3((x0 + (x0 + x1) / 2f - 1.2f) / 2f, y + h / 2f, z), new Vector3((x1 - x0) / 2f - 1.2f, h, 0.5f), Plaster);
            Box(t, "BathWall", new Vector3((x1 + (x0 + x1) / 2f + 1.2f) / 2f, y + h / 2f, z), new Vector3((x1 - x0) / 2f - 1.2f, h, 0.5f), Plaster);
        }
        Box(t, "BathRoof", new Vector3((x0 + x1) / 2f, y + h + 0.2f, (z0 + z1) / 2f), new Vector3(x1 - x0 + 0.6f, 0.4f, z1 - z0 + 0.6f), LimeOchre, 2f);
        foreach (float z in new[] { 36.3f, 45f, 53.7f })
        {
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(dome.GetComponent<Collider>());
            dome.name = "Dome";
            dome.transform.SetParent(t, false);
            dome.transform.position = new Vector3((x0 + x1) / 2f, y + h + 0.4f, z);
            dome.transform.localScale = new Vector3(6f, 3.4f, 6f);
            dome.GetComponent<Renderer>().sharedMaterial = Plaster;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var star = Box2(t, "Skylight", new Vector3((x0 + x1) / 2f + Mathf.Cos(a) * 1.7f, y + h + 1.6f, z + Mathf.Sin(a) * 1.7f), new Vector3(0.3f, 0.3f, 0.3f), Cream);
                star.transform.rotation = Quaternion.Euler(45f, i * 60f, 45f);
            }
        }
        // the hot room: a pool and stone benches; the warm room: benches
        Pool(t, Rect.MinMaxRect(-36f, 51.5f, -28f, 56f), y + 0.6f, 0.5f);
        foreach (float z in new[] { 43f, 47f })
            Box(t, "Bench", new Vector3(x0 + 1.2f, y + 0.25f, z), new Vector3(1.2f, 0.5f, 3f), Marble, 1f);
        HangingLamp(t, new Vector3((x0 + x1) / 2f, y + 3.6f, 36f), 1f);
        HangingLamp(t, new Vector3((x0 + x1) / 2f, y + 3.6f, 45f), 1f);
        Sign(t, new Vector3((x0 + x1) / 2f, y + 4.2f, z0 - 0.38f), 0f, "الحمام الملكي", "THE ROYAL BATHS", 3.4f, 1f);
    }

    private static GameObject Box2(Transform t, string name, Vector3 center, Vector3 size, Material m) => MeshGen.TexBox(name, center, size, m, 1f, t, 0f, false);

    /// <summary>A name board on two wooden posts (read from the side 'yaw' + 180 looks to).</summary>
    private static void BoardStand(Transform parent, Vector3 foot, float yaw, string ar, string en)
    {
        var s = Node("BoardStand", parent, foot, yaw);
        foreach (float x in new[] { -1.3f, 1.3f }) LBox(s, "Post", new Vector3(x, 0.9f, 0), new Vector3(0.12f, 1.8f, 0.12f), Signboard, 1f);
        Sign(s, s.TransformPoint(new Vector3(0, 1.9f, -0.1f)), yaw, ar, en, 3f, 0.9f);
    }

    /// <summary>Name boards on stands for the gardens and the great buildings that have no wall to carry one.</summary>
    private static void Boards()
    {
        var t = Node("Boards", root, Vector3.zero, 0);
        void Stand(Vector3 foot, float yaw, string ar, string en) => BoardStand(t, foot, yaw, ar, en);
        Stand(new Vector3(-12f, Middle + 0.5f, 28.5f), 180f, "الجنة العليا", "THE UPPER GARDEN");
        Stand(new Vector3(-14f, Lower + 0.5f, -39.5f), 180f, "الجنة السفلى", "THE LOWER GARDEN");
        Stand(new Vector3(-30f, Middle + 0.5f, 33.5f), 0f, "المجلس الشرقي - قاعة عبد الرحمن الناصر", "SALON RICO - HALL OF ABD AL-RAHMAN III");
        Stand(new Vector3(42f, Middle, 10f), -90f, "الرواق الكبير وساحة الاستعراض", "THE GREAT PORTICO AND THE PARADE GROUND");
        Stand(new Vector3(48f, Middle, 47f), 0f, "الشارع المنحدر المقبب", "THE VAULTED RAMP STREET");
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
