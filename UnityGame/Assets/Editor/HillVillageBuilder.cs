using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The "Hill Village" map: a half-old Turkish (Aegean) hill village. Two-storey houses of rough stone
/// and whitewashed plaster with blue window frames and shutters, wooden balconies and outside stairs,
/// terracotta hip and gable roofs; a paved square (meydan) with a raised wooden-railed platform, globe
/// street lamps, palms, a fountain against the terrace wall and a coffee house under a tiled pergola;
/// a stone mosque with its minaret on the upper terrace; sloping cobbled streets along the retaining
/// walls up to it and down to the lower quarter; olive groves on the slopes below and a pine forest on
/// the hill above. Three levels joined by stairs and ramps. Every texture is a Poly Haven scan (CC0);
/// the trees are pictures of Poly Haven's olive, pine and plane-like trees rendered from three sides
/// and from above (impostors, see Tools/impostor.py), the palms and grass made the same way.
/// </summary>
public static class HillVillageBuilder
{
    public const float Half = 100f;            // the village, its groves and the near hill (bots, spawns)
    private const float Edge = Half + 40f;     // players can climb the slopes around it up to here
    private const float Size = 420f;           // the ground reaches this far (the view to the hills)
    private const string Tex = "Assets/Textures/Koy";

    private static Transform root;
    private static System.Random rng;
    private static readonly List<Renderer> night = new List<Renderer>();

    private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    private static bool Chance(double p) => rng.NextDouble() < p;

    // ------------------------------------------------------------------ materials

    private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    private static Texture2D T(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/{file}");

    /// <summary>A scanned material of the village (albedo + normal), world-mapped in 'metres' (no seams).</summary>
    private static Material Scan(string name, string set, Color tint, float smooth, float metres, float bump = 1f)
    {
        if (mats.TryGetValue(name, out var m) && m != null) return m;
        m = AssetSetup.Mat("K_" + name, tint, smooth, 0f, T(set + "_BaseColor.jpg"), T(set + "_Normal.jpg"));
        m.SetFloat("_BumpScale", bump);
        EditorUtility.SetDirty(m);
        return mats[name] = AssetSetup.Stone(m, metres);
    }

    // whitewash over the stone, well kept; the same plaster flaking off the stones (the older houses)
    private static Material Plaster => Scan("Plaster", "WhitePlaster", new Color(1.32f, 1.32f, 1.3f), 0.12f, 2.5f, 0.5f);
    private static Material OldPlaster => Scan("OldPlaster", "PlasterStone", new Color(1.22f, 1.2f, 1.15f), 0.1f, 2.8f);
    private static Material CreamPlaster => Scan("CreamPlaster", "WhitePlaster", new Color(1.3f, 1.24f, 1.1f), 0.12f, 2.5f, 0.5f);
    // rough field stone (house walls, garden walls) and the dressed stones in mortar (terrace walls)
    private static Material Stone => Scan("Stone", "OldStone", new Color(1f, 0.96f, 0.9f), 0.12f, 2.6f, 1.2f);
    private static Material Masonry => Scan("Masonry", "MortarStone", new Color(1f, 0.97f, 0.92f), 0.12f, 3.2f, 1.2f);
    private static Material Roof => Scan("Roof", "ClayRoof", new Color(1f, 0.92f, 0.86f), 0.25f, 2.2f, 1.2f);
    private static Material Paving => Scan("Paving", "PlazaConcrete", new Color(1.02f, 0.98f, 0.92f), 0.15f, 4f);
    private static Material Cobble => Scan("Cobble", "Cobble", new Color(0.98f, 0.94f, 0.88f), 0.2f, 2.2f, 1.2f);
    private static Material Shutter => Scan("Shutter", "BluePlanks", new Color(0.62f, 0.85f, 1.15f), 0.3f, 1.4f);
    private static Material Coping => Scan("Coping", "PlazaConcrete", new Color(0.9f, 0.88f, 0.84f), 0.15f, 2f);
    private static Material RockMat => Scan("Rock", "RockFace", new Color(0.95f, 0.9f, 0.84f), 0.1f, 4f);
    private static Material Wood => Mats.Pbr("K_Wood", "Planks037A", new Color(0.62f, 0.5f, 0.38f), 0.3f);
    private static Material GreyWood => Mats.Pbr("K_GreyWood", "Planks037A", new Color(0.78f, 0.74f, 0.68f), 0.2f);
    private static Material DoorWood => Mats.Pbr("K_DoorWood", "Planks037A", new Color(0.5f, 0.32f, 0.2f), 0.35f);
    private static Material Glass => AssetSetup.Solid("K_Glass", new Color(0.07f, 0.09f, 0.11f), 0.92f, 0.1f);
    private static Material Iron => AssetSetup.Solid("K_Iron", new Color(0.06f, 0.07f, 0.07f), 0.5f, 0.7f);
    private static Material LampGreen => AssetSetup.Solid("K_LampGreen", new Color(0.13f, 0.24f, 0.18f), 0.55f, 0.5f);
    private static Material GlobeDay => AssetSetup.Solid("K_Globe", new Color(0.95f, 0.95f, 0.93f), 0.85f, 0f);
    private static Material GlobeNight => AssetSetup.Mat("K_GlobeNight", new Color(1f, 0.92f, 0.75f), 0.5f, 0f, null, null, null, null, new Color(3.4f, 2.8f, 1.8f));
    private static Material Lead => AssetSetup.Solid("K_Lead", new Color(0.42f, 0.44f, 0.46f), 0.5f, 0.6f);
    private static Material Carpet => AssetSetup.Solid("K_Carpet", new Color(0.45f, 0.08f, 0.07f), 0.05f, 0f);
    private static Material Water => AssetSetup.Solid("K_Water", new Color(0.1f, 0.16f, 0.16f), 0.95f, 0f);
    private static Material Cloth => AssetSetup.Solid("K_Cloth", new Color(0.72f, 0.18f, 0.12f), 0.1f, 0f);

    // ------------------------------------------------------------------ the levels of the ground

    /// <summary>
    /// A level piece of ground (axis-aligned): flat, or rising along x or z between two heights (a ramp
    /// street). Each edge either blends into the ground beyond over its margin, or (margin 0) ends sharp:
    /// there a retaining wall stands over the step. Later pads win over earlier ones.
    /// </summary>
    private struct Pad
    {
        public float X0, Z0, X1, Z1, Y0, Y1, A0, A1;
        public int Axis;                // 0 flat (Y0), 1 along x, 2 along z: Y0 at A0, Y1 at A1
        public float W, E, S, N;        // margins (west -x, east +x, south -z, north +z)

        public float Height(float x, float z)
        {
            if (Axis == 0) return Y0;
            float a = Axis == 1 ? x : z;
            return Mathf.Lerp(Y0, Y1, Mathf.InverseLerp(A0, A1, a));
        }

        public float Weight(float x, float z)
        {
            float w = 1f;
            w = Mathf.Min(w, Side(X0 - x, W));
            w = Mathf.Min(w, Side(x - X1, E));
            w = Mathf.Min(w, Side(Z0 - z, S));
            w = Mathf.Min(w, Side(z - Z1, N));
            return w;
        }

        private static float Side(float outside, float margin)
        {
            if (outside <= 0f) return 1f;
            if (margin <= 0f) return 0f;
            return Mathf.SmoothStep(1f, 0f, outside / margin);
        }
    }

    private static readonly List<Pad> pads = new List<Pad>();

    private static void Flat(float x0, float z0, float x1, float z1, float y, float w, float e, float s, float n) =>
        pads.Add(new Pad { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Y0 = y, W = w, E = e, S = s, N = n });

    private static void Ramp(float x0, float z0, float x1, float z1, int axis, float a0, float y0, float a1, float y1, float w, float e, float s, float n) =>
        pads.Add(new Pad { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Axis = axis, A0 = a0, Y0 = y0, A1 = a1, Y1 = y1, W = w, E = e, S = s, N = n });

    private static float Gauss(float x, float z, float cx, float cz, float sigma) => Mathf.Exp(-((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (2f * sigma * sigma));

    /// <summary>The land before the village was cut into it: rising to the pine hill in the north.</summary>
    private static float RawHeight(float x, float z)
    {
        float h = 0.07f * z;
        h += 1.8f * Mathf.Sin(x / 29f + 0.5f) * Mathf.Cos(z / 23f) + 0.9f * Mathf.Sin((x - z) / 17f);
        h += 26f * Gauss(x, z, 10f, 128f, 46f);       // the pine hill
        h += 11f * Gauss(x, z, -122f, 40f, 34f);      // the west hill
        h += 9f * Gauss(x, z, 118f, 64f, 30f);        // the north east hill
        h -= 3.5f * Gauss(x, z, 30f, -112f, 40f);     // the hollow below the olive groves
        float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Half - 8f, Half + 50f, edge)) * (16f + 6f * Mathf.Sin(x / 21f + z / 27f));
        return h;
    }

    public static float GroundHeight(float x, float z)
    {
        float h = RawHeight(x, z);
        foreach (var p in pads)
        {
            float w = p.Weight(x, z);
            if (w > 0f) h = Mathf.Lerp(h, p.Height(x, z), w);
        }
        return h;
    }

    /// <summary>How much of the ground here is a levelled part of the village (0 the open land).</summary>
    private static float Built(float x, float z)
    {
        float w = 0f;
        foreach (var p in pads) w = Mathf.Max(w, p.Weight(x, z));
        return w;
    }

    /// <summary>The ground under (x, z): the mesh (raycast), else the height function.</summary>
    private static float Y(float x, float z)
    {
        var hits = Physics.RaycastAll(new Vector3(x, 120f, z), Vector3.down, 300f, World.EnvMask);
        float best = float.MinValue;
        foreach (var h in hits)
            if (h.collider.name.StartsWith("Ground")) best = Mathf.Max(best, h.point.y);
        return best > float.MinValue ? best : GroundHeight(x, z);
    }

    /// <summary>
    /// The levels: the square in the middle; the west quarter rising gently from it; the upper terrace
    /// (mosque) 4.5 m above behind a stone wall, reached by stairs and by the east street sloping up
    /// along its wall; the lower quarter 2.5 m below the square, with its own sloping street down in the
    /// south east; the road out to the east climbing the slope.
    /// </summary>
    private static void Levels()
    {
        pads.Clear();
        Flat(-26f, -18f, 26f, 16f, 0f, 6f, 6f, 0f, 0f);                          // the square (meydan)
        Ramp(-96f, -18f, -26f, 16f, 1, -96f, 3f, -26f, 0f, 10f, 0f, 0f, 0f);     // the west quarter
        Flat(-96f, 16f, 24f, 50f, 4.5f, 10f, 0f, 0f, 12f);                       // the upper terrace
        Ramp(24f, -6f, 42f, 52f, 2, 16f, 0f, 40f, 4.5f, 0f, 10f, 6f, 10f);       // the east street up
        Flat(-96f, -56f, 26f, -18f, -2.5f, 12f, 0f, 12f, 0f);                    // the lower quarter
        Ramp(26f, -56f, 42f, -6f, 2, -40f, -2.5f, -18f, 0f, 0f, 10f, 12f, 0f);   // the street down
        Ramp(42f, -8f, 104f, 10f, 1, 42f, 0f, 104f, 7f, 0f, 0f, 6f, 6f);         // the road out east
    }

    /// <summary>A level spot for a house standing on the open slope (its own pad, blended in).</summary>
    private static float HousePad(Vector3 c, float w, float d, float yaw)
    {
        var ext = Quaternion.Euler(0, yaw, 0) * new Vector3(w, 0, d);
        float ex = Mathf.Abs(ext.x) / 2f + 2f, ez = Mathf.Abs(ext.z) / 2f + 2f;
        float y = GroundHeight(c.x, c.z);
        Flat(c.x - ex, c.z - ez, c.x + ex, c.z + ez, y, 6f, 6f, 6f, 6f);
        return y;
    }

    // ------------------------------------------------------------------ the ground mesh

    // the streets and lanes paved with cobbles, and the dirt tracks out to the hill and the groves
    private static readonly List<(Vector2[] line, float width)> cobbles = new List<(Vector2[], float)>();
    private static readonly List<(Vector2[] line, float width)> tracks = new List<(Vector2[], float)>();

    private static float LineDistance(List<(Vector2[] line, float width)> lines, float x, float z)
    {
        float best = 1e9f;
        var p = new Vector2(x, z);
        foreach (var (line, width) in lines)
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i]; var b = line[i + 1];
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t) - width / 2f);
            }
        return best;
    }

    /// <summary>
    /// Grid lines: 1 m apart over the village (the steps between the levels fall inside the terrace
    /// walls), 2 m and then 6 m further out. The same lines both ways: a rectilinear grid, no cracks.
    /// </summary>
    private static List<float> GridLines()
    {
        var lines = new List<float>();
        float x = -Size / 2f;
        while (x < Size / 2f)
        {
            lines.Add(x);
            float a = Mathf.Abs(x);
            x += a < 104f ? 1f : a < 140f ? 2f : 6f;
        }
        lines.Add(Size / 2f);
        return lines;
    }

    private static Material GroundMaterial()
    {
        string matPath = $"{AssetSetup.Generated}/Materials/HillVillage_Ground.mat";
        AssetDatabase.DeleteAsset(matPath);
        var shader = Shader.Find("UA/Ground");
        var mat = new Material(shader ?? Shader.Find("Standard")) { name = "HillVillage_Ground" };
        // dry summer grass on the land, the dusty earth of the village, the pine needles of the
        // forest floor, bare rock on the steep banks; the lanes paved with cobbles
        mat.SetTexture("_Grass", T("DryGrass_BaseColor.jpg"));
        mat.SetTexture("_Dirt", T("DryGround_BaseColor.jpg"));
        mat.SetTexture("_Gravel", T("PineFloor_BaseColor.jpg"));
        mat.SetTexture("_Rock", T("RockFace_BaseColor.jpg"));
        mat.SetColor("_GrassTint", new Color(0.86f, 0.96f, 0.74f));
        mat.SetVector("_Tiles", new Vector4(7f, 5f, 4f, 7f));
        var area = new Rect(-Half - 10f, -Half - 10f, (Half + 10f) * 2f, (Half + 10f) * 2f);
        mat.SetTexture("_Path", T("Cobble_BaseColor.jpg"));
        mat.SetTexture("_PathMask", CobbleMask(area, 1024));
        mat.SetVector("_PathArea", new Vector4(area.xMin, area.yMin, area.width, 2.2f));
        mat.SetColor("_PathTint", new Color(1.35f, 1.3f, 1.22f));
        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    /// <summary>Where the ground is cobbled: the lanes, with a ragged edge where the stones end.</summary>
    private static Texture2D CobbleMask(Rect area, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.R8, false, true) { name = "HillVillage_Cobbles", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = area.xMin + (i + 0.5f) * area.width / size, z = area.yMin + (j + 0.5f) * area.height / size;
                float d = LineDistance(cobbles, x, z);
                float ragged = 0.35f * Mathf.PerlinNoise(x * 0.4f + 3f, z * 0.4f + 9f);
                float m = Mathf.InverseLerp(0.5f + ragged, -0.2f + ragged, d);
                px[j * size + i] = new Color32((byte)(Mathf.Clamp01(m) * 255f), 0, 0, 255);
            }
        tex.SetPixels32(px);
        tex.Apply();
        string path = $"{AssetSetup.Generated}/HillVillage_Cobbles.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    /// <summary>
    /// The ground: a height field on the grid above, in chunks (each under 65k vertices, culled on its
    /// own), with a mesh collider each, painted through its vertex colours (shader UA/Ground).
    /// </summary>
    private static void BuildGround()
    {
        var lines = GridLines();
        int n = lines.Count;
        var heights = new float[n, n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                heights[i, j] = GroundHeight(lines[i], lines[j]);
        var mat = GroundMaterial();
        var parent = new GameObject("Ground").transform;
        parent.SetParent(root, false);
        const int chunk = 96;
        int made = 0;
        for (int cj = 0; cj < n - 1; cj += chunk)
            for (int ci = 0; ci < n - 1; ci += chunk)
            {
                int ni = Mathf.Min(chunk, n - 1 - ci) + 1, nj = Mathf.Min(chunk, n - 1 - cj) + 1;
                var v = new Vector3[ni * nj];
                var c = new Color[ni * nj];
                for (int j = 0; j < nj; j++)
                    for (int i = 0; i < ni; i++)
                    {
                        int gi = ci + i, gj = cj + j;
                        float x = lines[gi], z = lines[gj];
                        v[j * ni + i] = new Vector3(x, heights[gi, gj], z);
                        float dx = lines[Mathf.Min(n - 1, gi + 1)] - lines[Mathf.Max(0, gi - 1)];
                        float dz = lines[Mathf.Min(n - 1, gj + 1)] - lines[Mathf.Max(0, gj - 1)];
                        float hx = (heights[Mathf.Min(n - 1, gi + 1), gj] - heights[Mathf.Max(0, gi - 1), gj]) / dx;
                        float hz = (heights[gi, Mathf.Min(n - 1, gj + 1)] - heights[gi, Mathf.Max(0, gj - 1)]) / dz;
                        float up = 1f / Mathf.Sqrt(1f + hx * hx + hz * hz);
                        c[j * ni + i] = Splat(x, z, up, heights[gi, gj]);
                    }
                var t = new int[(ni - 1) * (nj - 1) * 6];
                int k = 0;
                for (int j = 0; j < nj - 1; j++)
                    for (int i = 0; i < ni - 1; i++)
                    {
                        int a = j * ni + i, b = a + 1, cc = a + ni, d = cc + 1;
                        t[k++] = a; t[k++] = cc; t[k++] = b;
                        t[k++] = b; t[k++] = cc; t[k++] = d;
                    }
                var mesh = new Mesh { name = $"HillVillageGround_{ci}_{cj}" };
                if (v.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v);
                mesh.SetColors(c);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                string path = $"{AssetSetup.Generated}/HillVillage_Ground_{made}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject("Ground");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                made++;
            }
        Physics.SyncTransforms();
        Debug.Log($"[HillVillage] ground: {n}x{n} grid in {made} chunks");
    }

    /// <summary>Vertex colour of the ground: r dusty earth, g dry grass, b pine needles, a rock.</summary>
    private static Color Splat(float x, float z, float up, float y)
    {
        float noise = Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 7f);
        float fine = Mathf.PerlinNoise(x * 0.31f + 11f, z * 0.31f + 2f);
        float steep = Mathf.InverseLerp(0.86f, 0.66f, up);
        // the village's ground is trodden earth, with grass coming back at its edges
        float village = Built(x, z) * Mathf.InverseLerp(Half + 4f, Half - 10f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
        float track = Mathf.InverseLerp(1.6f, 0f, LineDistance(tracks, x, z));
        float dirt = Mathf.Clamp01(Mathf.Max(village * (0.55f + 0.5f * noise) - 0.15f * fine, track));
        // the pine forest on the hill and its edges: a floor of needles
        float forest = Mathf.Clamp01(Mathf.InverseLerp(52f, 74f, z) * Mathf.InverseLerp(0.2f, 0.6f, noise + 0.3f) + Mathf.InverseLerp(Half + 4f, Half + 30f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z))) * 0.7f);
        forest *= 1f - dirt;
        float rock = Mathf.Clamp01(steep * 1.3f) * (1f - village);
        float grass = Mathf.Clamp01(1f - dirt - forest - rock);
        float sum = Mathf.Max(0.001f, grass + dirt + forest + rock);
        return new Color(dirt / sum, grass / sum, forest / sum, rock / sum);
    }

    // ------------------------------------------------------------------ pieces

    /// <summary>A box placed in a parent's space (centre, size), with its collider.</summary>
    private static GameObject Box(string name, Transform t, Vector3 local, Vector3 size, Material m, bool collider = true, float tex = 2f)
    {
        if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) return null;
        var go = MeshGen.TexBox(name, t.TransformPoint(local), size, m, tex, t, t.eulerAngles.y, collider);
        // the small trim (frames, sills, shutters, bands) casts no shadow: cheaper, and not missed
        if (!collider && Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < 0.3f)
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    /// <summary>A box turned freely (braces, beams across).</summary>
    private static GameObject Beam(string name, Transform parent, Vector3 a, Vector3 b, float thick, Material m, bool collider = false)
    {
        var go = MeshGen.Object(name, MeshGen.Box(new Vector3(thick, thick, Vector3.Distance(a, b)), 1.5f), m, parent, (a + b) / 2f);
        go.transform.rotation = Quaternion.LookRotation(b - a);
        if (collider) go.AddComponent<BoxCollider>().size = new Vector3(thick, thick, Vector3.Distance(a, b));
        return go;
    }

    private static GameObject Cylinder(string name, Transform parent, Vector3 bottom, float radius, float height, Material m, bool collider = true, int segments = 14)
    {
        var go = MeshGen.Object(name, MeshGen.Cylinder(radius, height, 1.2f, segments), m, parent, bottom);
        if (collider)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = radius; c.height = Mathf.Max(height, radius * 2f); c.center = new Vector3(0, height / 2f, 0);
        }
        return go;
    }

    private static Mesh sphereMesh;
    private static GameObject Sphere(string name, Transform parent, Vector3 centre, float radius, Material m)
    {
        if (sphereMesh == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
        }
        var go = MeshGen.Object(name, sphereMesh, m, parent, centre);
        go.transform.localScale = Vector3.one * radius * 2f;
        return go;
    }

    /// <summary>A cone (the minaret's cap), its own mesh.</summary>
    private static Mesh Cone(float radius, float height, int segments)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius)); uv.Add(new Vector2(i / (float)segments * 3f, 0));
            v.Add(new Vector3(0, height, 0)); uv.Add(new Vector2(i / (float)segments * 3f, 2f));
        }
        for (int i = 0; i < segments; i++) { int a = i * 2; t.Add(a); t.Add(a + 1); t.Add(a + 2); t.Add(a + 2); t.Add(a + 1); t.Add(a + 3); }
        var m = new Mesh { name = $"HV_Cone_{radius}_{height}" };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, $"{AssetSetup.Generated}/HV_Cone_{Mathf.RoundToInt(radius * 100)}_{Mathf.RoundToInt(height * 100)}.asset");
        return m;
    }

    // ------------------------------------------------------------------ terrace walls and stairs

    /// <summary>
    /// A retaining wall over a sharp edge between two levels, along x (at z = c) or along z (at x = c),
    /// from a to b: 2.1 m thick over the step of the ground, its top flush with the higher side and a
    /// parapet on the outer edge where the drop is more than a metre. Built in 2 m pieces (the ground
    /// beside it may slope). 'gaps' leave the parapet open (stair heads).
    /// </summary>
    private static void TerraceWall(Transform t, bool alongX, float c, float a, float b, float outerSign, params (float from, float to)[] gaps)
    {
        for (float s = a; s < b - 0.01f; s += 2f)
        {
            float e = Mathf.Min(b, s + 2f), m = (s + e) / 2f;
            float hi = float.MinValue, lo = float.MaxValue;
            foreach (float d in new[] { -2f, -1.5f, 1.5f, 2f })
                foreach (float u in new[] { s + 0.2f, m, e - 0.2f })
                {
                    float y = alongX ? GroundHeight(u, c + d) : GroundHeight(c + d, u);
                    hi = Mathf.Max(hi, y); lo = Mathf.Min(lo, y);
                }
            if (hi - lo < 0.25f) continue;
            float bottom = lo - 0.4f, top = hi + 0.03f;
            var size = alongX ? new Vector3(e - s, top - bottom, 2.1f) : new Vector3(2.1f, top - bottom, e - s);
            var centre = alongX ? new Vector3(m, (top + bottom) / 2f, c) : new Vector3(c, (top + bottom) / 2f, m);
            MeshGen.TexBox("TerraceWall", centre, size, Masonry, 3f, t);
            // the coping: a band of dressed stone along the top edge
            var copeSize = alongX ? new Vector3(e - s, 0.12f, 0.5f) : new Vector3(0.5f, 0.12f, e - s);
            var outerEdge = (1.05f - 0.22f) * outerSign;
            bool open = gaps.Any(g => m > g.from && m < g.to);
            if (hi - lo > 1.0f && !open)
            {
                var pc = alongX ? new Vector3(m, top + 0.45f, c + outerEdge) : new Vector3(c + outerEdge, top + 0.45f, m);
                MeshGen.TexBox("Parapet", pc, alongX ? new Vector3(e - s, 0.9f, 0.44f) : new Vector3(0.44f, 0.9f, e - s), Masonry, 3f, t);
                MeshGen.TexBox("Coping", pc + Vector3.up * 0.51f, alongX ? new Vector3(e - s + 0.02f, 0.12f, 0.54f) : new Vector3(0.54f, 0.12f, e - s + 0.02f), Coping, 2f, t, 0f, false);
            }
            else MeshGen.TexBox("Coping", (alongX ? new Vector3(m, top + 0.06f, c + outerEdge) : new Vector3(c + outerEdge, top + 0.06f, m)), copeSize, Coping, 2f, t, 0f, false);
        }
    }

    /// <summary>
    /// A straight flight of stone steps from 'start' (its foot) along 'dir' up 'rise' metres, 'width'
    /// wide: each step a block down to below the ground, a low wall on the open side.
    /// </summary>
    private static void Steps(Transform t, Vector3 foot, Vector3 dir, float rise, float width, Vector3 wallSide, Material m = null)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(rise) / 0.18f));
        float stepH = rise / count, run = 0.3f;
        dir.y = 0; dir.Normalize();
        float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        for (int i = 0; i < count; i++)
        {
            var p = foot + dir * (run * (i + 0.5f));
            float topY = foot.y + stepH * (i + 1);
            float bottom = Mathf.Min(foot.y, foot.y + rise) - 0.5f;
            MeshGen.TexBox("Step", new Vector3(p.x, (topY + bottom) / 2f, p.z), new Vector3(width, topY - bottom, run + 0.02f), m ?? Masonry, 2f, t, yaw);
        }
        if (wallSide != Vector3.zero)
        {
            float len = run * count;
            float bottom = Mathf.Min(foot.y, foot.y + rise) - 0.3f;
            // the low wall beside the steps follows them down in three parts
            for (int k = 0; k < 3; k++)
            {
                float f0 = k / 3f, f1 = (k + 1) / 3f;
                var pm = foot + dir * (len * (f0 + f1) / 2f) + wallSide.normalized * (width / 2f + 0.2f);
                float yTop = foot.y + rise * f1 + 0.85f;
                MeshGen.TexBox("StairWall", new Vector3(pm.x, (yTop + bottom) / 2f, pm.z), new Vector3(0.4f, yTop - bottom, len / 3f + 0.02f), Masonry, 3f, t, yaw);
            }
        }
    }

    // ------------------------------------------------------------------ roofs

    private static readonly Dictionary<string, Mesh> roofMeshes = new Dictionary<string, Mesh>();

    /// <summary>
    /// A hip roof over W x D (overhang included), ridge along the longer side, 'rise' high: two
    /// trapezoid slopes, two triangles and the soffit underneath. UVs in metres along the eaves (u)
    /// and down the slope (v): the barrel tiles run down the roof.
    /// </summary>
    private static Mesh HipRoof(float W, float D, float rise, float texScale)
    {
        string key = $"hip_{Mathf.RoundToInt(W * 100)}_{Mathf.RoundToInt(D * 100)}_{Mathf.RoundToInt(rise * 100)}";
        if (roofMeshes.TryGetValue(key, out var cached)) return cached;
        bool swap = W > D;
        float w = swap ? D : W, d = swap ? W : D;
        float r = Mathf.Max(0f, (d - w) / 2f);
        var faces = new List<Vector3[]>
        {
            new[] { new Vector3(w / 2, 0, -d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(0, rise, r), new Vector3(0, rise, -r) },
            new[] { new Vector3(-w / 2, 0, d / 2), new Vector3(-w / 2, 0, -d / 2), new Vector3(0, rise, -r), new Vector3(0, rise, r) },
            new[] { new Vector3(w / 2, 0, d / 2), new Vector3(-w / 2, 0, d / 2), new Vector3(0, rise, r) },
            new[] { new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0, -d / 2), new Vector3(0, rise, -r) },
        };
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var n = new List<Vector3>();
        foreach (var f0 in faces)
        {
            var f = swap ? f0.Select(p => new Vector3(p.z, p.y, -p.x)).ToArray() : f0;
            var normal = Vector3.Cross(f[1] - f[0], f[2] - f[0]).normalized;
            if (normal.y < 0) { System.Array.Reverse(f); normal = -normal; }
            var u = Vector3.Cross(Vector3.up, normal).normalized;
            var down = Vector3.Cross(normal, u).normalized;
            int start = v.Count;
            foreach (var p in f) { v.Add(p); n.Add(normal); uv.Add(new Vector2(Vector3.Dot(p, u), -Vector3.Dot(p, down)) / texScale); }
            // wound so the face looks up (Unity: clockwise seen from the front)
            for (int i = 1; i + 1 < f.Length; i++)
            {
                var cross = Vector3.Cross(f[i] - f[0], f[i + 1] - f[0]);
                if (cross.y > 0) { t.Add(start); t.Add(start + i); t.Add(start + i + 1); }
                else { t.Add(start); t.Add(start + i + 1); t.Add(start + i); }
            }
        }
        int b = v.Count;
        v.Add(new Vector3(-W / 2, 0, -D / 2)); v.Add(new Vector3(W / 2, 0, -D / 2)); v.Add(new Vector3(W / 2, 0, D / 2)); v.Add(new Vector3(-W / 2, 0, D / 2));
        for (int k = 0; k < 4; k++) { n.Add(Vector3.down); uv.Add(new Vector2(v[b + k].x, v[b + k].z) / texScale); }
        t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        var mesh = new Mesh { name = "HV_" + key };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{AssetSetup.Generated}/HV_{key}.asset");
        return roofMeshes[key] = mesh;
    }

    /// <summary>
    /// The roof of a house at height y (its own space): hip or gable, the ridge and hip lines capped with
    /// round tiles, a painted fascia board along the eaves; returns the height of the ridge.
    /// </summary>
    private static float RoofOver(Transform h, float w, float d, float y, bool hip, Material wall)
    {
        const float over = 0.5f;
        float W = w + 2f * over, D = d + 2f * over;
        float rise = 0.42f * Mathf.Min(W, D) / 2f + 0.3f;
        if (hip)
        {
            var go = MeshGen.Object("Roof", HipRoof(W, D, rise, 2.2f), Roof, h, h.TransformPoint(new Vector3(0, y, 0)), h.eulerAngles.y);
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            float r = Mathf.Abs(D - W) / 2f;
            var a = W > D ? new Vector3(-r, y + rise, 0) : new Vector3(0, y + rise, -r);
            var b = W > D ? new Vector3(r, y + rise, 0) : new Vector3(0, y + rise, r);
            if (r > 0.05f) Beam("RidgeCap", h, h.TransformPoint(a), h.TransformPoint(b), 0.2f, Roof);
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                var corner = new Vector3(sx * W / 2f, y, sz * D / 2f);
                var top = W > D ? (sx < 0 ? a : b) : (sz < 0 ? a : b);
                Beam("HipCap", h, h.TransformPoint(corner), h.TransformPoint(top), 0.17f, Roof);
            }
        }
        else
        {
            // the ridge along the longer side; the gable ends are walls of the house's own material
            bool alongX = w > d;
            float span = alongX ? D : W, length = alongX ? W : D;
            var go = MeshGen.Object("Roof", MeshGen.GableRoof(span - 2f * over, length - 2f * over, rise, over, 2.2f), Roof, h,
                h.TransformPoint(new Vector3(0, y, 0)), h.eulerAngles.y + (alongX ? 90f : 0f));
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            float wallSpan = alongX ? d : w;
            // under the roof at the end wall: a strip as high as the roof stands over the wall's corners,
            // the triangle of the gable on it up to the ridge
            float gableRise = rise * wallSpan / span, strip = rise - gableRise;
            foreach (float s in new[] { -1f, 1f })
            {
                var local = alongX ? new Vector3(s * (w / 2f - 0.2f), y + strip, 0) : new Vector3(0, y + strip, s * (d / 2f - 0.2f));
                Box("GableStrip", h, local - Vector3.up * (strip / 2f), alongX ? new Vector3(Thick, strip, wallSpan) : new Vector3(wallSpan, strip, Thick), wall, true, 2.5f);
                var gable = MeshGen.Object("Gable", MeshGen.Gable(wallSpan, gableRise, 2.5f), wall, h, h.TransformPoint(local), h.eulerAngles.y + (alongX ? 90f : 0f));
                gable.transform.localScale = new Vector3(1f, 1f, 4f);   // 0.4 m thick
                gable.AddComponent<BoxCollider>().size = new Vector3(wallSpan * 0.7f, gableRise, 0.1f);
                gable.GetComponent<BoxCollider>().center = new Vector3(0, gableRise * 0.35f, 0);
            }
            var r0 = alongX ? new Vector3(-length / 2f, y + rise, 0) : new Vector3(0, y + rise, -length / 2f);
            var r1 = alongX ? new Vector3(length / 2f, y + rise, 0) : new Vector3(0, y + rise, length / 2f);
            Beam("RidgeCap", h, h.TransformPoint(r0), h.TransformPoint(r1), 0.2f, Roof);
        }
        // the fascia board along the eaves, painted like the shutters on some houses, wood on others
        var fascia = Chance(0.4) ? Shutter : Wood;
        foreach (var (p, s) in new[]
        {
            (new Vector3(0, y - 0.06f, D / 2f), new Vector3(W, 0.16f, 0.05f)), (new Vector3(0, y - 0.06f, -D / 2f), new Vector3(W, 0.16f, 0.05f)),
            (new Vector3(W / 2f, y - 0.06f, 0), new Vector3(0.05f, 0.16f, D)), (new Vector3(-W / 2f, y - 0.06f, 0), new Vector3(0.05f, 0.16f, D)),
        })
            if (hip || (w > d ? Mathf.Abs(p.z) > 0.1f : Mathf.Abs(p.x) > 0.1f)) Box("Fascia", h, p, s, fascia, false, 1.4f);
        return y + rise;
    }

    // ------------------------------------------------------------------ houses

    private class House
    {
        public float X, Z, Yaw, W, D;
        public int Floors = 2;
        public int Style;            // 0 whitewash with stone corners, 1 stone below and whitewash above, 2 all field stone, 3 old flaking plaster
        public bool Hip = true, Balcony, OutsideStair, Chimney, Open, Ruin, Yard;
        public House(float x, float z, float yaw, float w, float d, int floors, int style, string extras = "")
        {
            X = x; Z = z; Yaw = yaw; W = w; D = d; Floors = floors; Style = style;
            Hip = !extras.Contains("gable");
            Balcony = extras.Contains("balcony"); OutsideStair = extras.Contains("stair"); Chimney = extras.Contains("chimney");
            Open = extras.Contains("open"); Ruin = extras.Contains("ruin"); Yard = extras.Contains("yard");
        }
    }

    private struct Opening
    {
        public float U, Width, Bottom, Top;   // along the wall from its middle; bottom and top above the floor
        public int Kind;                      // 0 window, 1 door, 2 balcony door
    }

    private const float Storey = 3.0f, Thick = 0.4f;

    /// <summary>The four walls of a house: origin (middle of the wall's thickness), direction along it, outward, length.</summary>
    private static (Vector3 o, Vector3 u, Vector3 n, float len) Side(int side, float w, float d)
    {
        switch (side)
        {
            case 0: return (new Vector3(0, 0, d / 2f - Thick / 2f), Vector3.right, Vector3.forward, w);
            case 1: return (new Vector3(0, 0, -d / 2f + Thick / 2f), Vector3.left, Vector3.back, w);
            case 2: return (new Vector3(-w / 2f + Thick / 2f, 0, 0), Vector3.forward, Vector3.left, d - 2f * Thick);
            default: return (new Vector3(w / 2f - Thick / 2f, 0, 0), Vector3.back, Vector3.right, d - 2f * Thick);
        }
    }

    private static Vector3 SideSize(Vector3 u, float along, float height, float across) =>
        Mathf.Abs(u.x) > 0.5f ? new Vector3(along, height, across) : new Vector3(across, height, along);

    /// <summary>One storey of one wall, with its openings cut out (open houses) or closed behind glass.</summary>
    private static void WallStorey(Transform h, int side, float w, float d, float y0, float y1, Material m, List<Opening> ops, bool holes)
    {
        var (o, u, n, len) = Side(side, w, d);
        void Piece(float a, float b, float c, float e)
        {
            if (b - a < 0.02f || e - c < 0.02f) return;
            Box("Wall", h, o + u * ((a + b) / 2f) + Vector3.up * ((c + e) / 2f), SideSize(u, b - a, e - c, Thick), m, true, 2.5f);
        }
        if (!holes || ops.Count == 0) { Piece(-len / 2f, len / 2f, y0, y1); return; }
        float at = -len / 2f;
        foreach (var op in ops.OrderBy(q => q.U))
        {
            float l = op.U - op.Width / 2f, r = op.U + op.Width / 2f;
            Piece(at, l, y0, y1);
            Piece(l, r, y0, y0 + op.Bottom);
            Piece(l, r, y0 + op.Top, y1);
            at = r;
        }
        Piece(at, len / 2f, y0, y1);
    }

    /// <summary>
    /// What makes an opening look lived in: a blue painted frame, a sill, a stone lintel on the stone
    /// houses, shutters (open against the wall, or closed), glass behind a cross of glazing bars when
    /// the house is closed, an iron grille on some ground floor windows; a wooden door in a door.
    /// </summary>
    private static void Dress(Transform h, int side, float w, float d, float y0, Opening op, bool holes, bool stoneWall, bool ground, bool old)
    {
        var (o, u, n, _) = Side(side, w, d);
        var face = o + n * (Thick / 2f);
        var c = face + u * op.U + Vector3.up * (y0 + (op.Bottom + op.Top) / 2f);
        float hgt = op.Top - op.Bottom, wid = op.Width;
        const float bar = 0.08f;
        // the frame
        Box("Frame", h, c + u * (wid / 2f + bar / 2f) + n * 0.03f, SideSize(u, bar, hgt + bar * 2f, 0.1f), Shutter, false, 1.4f);
        Box("Frame", h, c - u * (wid / 2f + bar / 2f) + n * 0.03f, SideSize(u, bar, hgt + bar * 2f, 0.1f), Shutter, false, 1.4f);
        Box("Frame", h, c + Vector3.up * (hgt / 2f + bar / 2f) + n * 0.03f, SideSize(u, wid, bar, 0.1f), Shutter, false, 1.4f);
        if (op.Kind == 0)
        {
            Box("Sill", h, c - Vector3.up * (hgt / 2f + 0.04f) + n * 0.1f, SideSize(u, wid + 0.3f, 0.07f, 0.24f), Coping, false, 1.5f);
            if (!holes)
            {
                Box("Glass", h, c - n * 0.005f, SideSize(u, wid, hgt, 0.02f), Glass, false);
                Box("Bar", h, c + n * 0.02f, SideSize(u, 0.05f, hgt, 0.04f), Shutter, false, 1.4f);
                Box("Bar", h, c + Vector3.up * (hgt * 0.12f) + n * 0.02f, SideSize(u, wid, 0.05f, 0.04f), Shutter, false, 1.4f);
                if (Chance(0.3)) NightLights.Window(h, h.TransformPoint(c + n * 0.03f), h.TransformDirection(n), wid * 0.9f, hgt * 0.9f, night);
            }
            if (ground && Chance(0.35))
                for (int k = 1; k <= 4; k++)
                    Box("Grille", h, c + u * (-wid / 2f + wid * k / 5f) + n * 0.07f, SideSize(u, 0.025f, hgt, 0.025f), Iron, false);
            // shutters: open flat against the wall either side, closed over the window, or one gone
            int state = Chance(0.2) && !holes ? 1 : Chance(old ? 0.25 : 0.06) ? 2 : 0;
            float sw = wid / 2f;
            for (int s = -1; s <= 1; s += 2)
            {
                if (state == 2 && s > 0) continue;
                var at = state == 1 ? c + u * (s * sw / 2f) + n * 0.07f : c + u * (s * (wid / 2f + bar + sw / 2f + 0.02f)) + n * 0.04f;
                Box("Shutter", h, at, SideSize(u, sw - 0.02f, hgt, 0.035f), Shutter, false, 0.7f);
            }
        }
        else
        {
            // the door: a lintel of stone or wood over it, the leaf shut, or swung open inside
            Box("Lintel", h, c + Vector3.up * (hgt / 2f + bar + 0.1f) + n * 0.02f, SideSize(u, wid + 0.5f, 0.2f, 0.06f), stoneWall ? Stone : Wood, false);
            var leafMat = Chance(0.5) ? Shutter : DoorWood;
            if (!holes) Box("Door", h, c - n * 0.01f, SideSize(u, wid, hgt, 0.06f), leafMat, false, 1.4f);
            else
            {
                // swung open inside, square to the wall from its hinge at one jamb
                Box("Door", h, c - u * (wid / 2f - 0.04f) - n * (Thick + wid / 2f), SideSize(u, 0.06f, hgt, wid), leafMat, false, 1.4f);
            }
            if (op.Kind == 1)
                Box("DoorStep", h, face + u * op.U + Vector3.up * (y0 - 0.06f) + n * 0.25f, SideSize(u, wid + 0.4f, 0.12f, 0.5f), Coping, true, 1.5f);
        }
    }

    /// <summary>Openings of one storey of one wall: windows spaced along it, a door or balcony door at given places.</summary>
    private static List<Opening> Openings(float len, float? door, int doorKind, float avoidFrom = 99f, float avoidTo = -99f)
    {
        var ops = new List<Opening>();
        int nWin = Mathf.Max(1, Mathf.FloorToInt((len - 1.2f) / 2.4f));
        for (int i = 0; i < nWin; i++)
        {
            float u = -len / 2f + len * (i + 1) / (nWin + 1);
            if (door.HasValue && Mathf.Abs(u - door.Value) < 1.4f) continue;
            if (u > avoidFrom && u < avoidTo) continue;
            ops.Add(new Opening { U = u, Width = 0.9f, Bottom = 0.95f, Top = 2.25f, Kind = 0 });
        }
        if (door.HasValue)
            ops.Add(new Opening { U = door.Value, Width = doorKind == 1 ? 1.05f : 1.0f, Bottom = 0f, Top = doorKind == 1 ? 2.2f : 2.3f, Kind = doorKind });
        return ops;
    }

    /// <summary>The ground under a footprint: highest and lowest point.</summary>
    private static (float hi, float lo) Under(Vector3 c, Quaternion rot, float w, float d)
    {
        float hi = float.MinValue, lo = float.MaxValue;
        for (int i = -2; i <= 2; i++)
            for (int k = -2; k <= 2; k++)
            {
                var p = c + rot * new Vector3(i * w / 4f, 0, k * d / 4f);
                float y = Y(p.x, p.z);
                hi = Mathf.Max(hi, y); lo = Mathf.Min(lo, y);
            }
        return (hi, lo);
    }

    private static void BuildHouse(House hs, Transform parent)
    {
        // not quite in line with the street: each house a little turned and set forward or back
        var rot = Quaternion.Euler(0, hs.Yaw + R(-3.5f, 3.5f), 0);
        var centre = new Vector3(hs.X, 0, hs.Z) + rot * Vector3.forward * R(-0.6f, 0.4f);
        var (hi, lo) = Under(centre, rot, hs.W + 1f, hs.D + 1f);
        float baseY = hi + 0.15f;
        var h = new GameObject("House").transform;
        h.SetParent(parent, false);
        h.SetPositionAndRotation(new Vector3(centre.x, baseY, centre.z), rot);
        float w = hs.W, d = hs.D;
        // half the old houses freshly whitewashed: the village is white with stone between, as in the picture
        if (hs.Style >= 2 && !hs.Ruin && Chance(0.45)) hs.Style = 0;
        bool old = hs.Style >= 2;
        var upper = hs.Style == 0 ? (Chance(0.3) ? CreamPlaster : Plaster) : hs.Style == 1 ? Plaster : hs.Style == 2 ? Stone : OldPlaster;
        var lower = hs.Style == 1 || hs.Style == 2 ? Stone : upper;
        float drop = baseY - lo + 0.4f;
        // the foundation down into the slope, and a band of field stone round the foot of the walls
        Box("Foundation", h, new Vector3(0, -drop / 2f, 0), new Vector3(w, drop, d), Stone, true, 2.6f);
        foreach (int s in new[] { 0, 1, 2, 3 })
        {
            var (o, u, n, len) = Side(s, w, d);
            float bandLen = s < 2 ? len + 0.12f : d + 0.12f;
            var bc = (s < 2 ? o : new Vector3(o.x, 0, 0)) + n * (Thick / 2f + 0.03f) + Vector3.up * ((0.55f - drop) / 2f);
            Box("StoneBand", h, bc, SideSize(u, bandLen, 0.55f + drop, 0.06f), Stone, false, 2.6f);
        }
        if (hs.Ruin) { Ruin(h, hs, lower); return; }

        // where the doors go: the front door in the middle of the front, or on the right side when an
        // outside stair runs up the left half of the front to the upper door
        float stairRun = 17 * 0.3f;
        bool stair = hs.OutsideStair && hs.Floors > 1 && w >= 6.8f;
        float balconyW = Mathf.Min(4.2f, w * 0.5f);
        float balconyU = stair ? w / 2f - balconyW / 2f - 0.5f : 0f;
        bool balcony = hs.Balcony && hs.Floors > 1 && (!stair || w >= 8.5f);
        float avoidFrom = stair ? -w / 2f - 1f : 99f, avoidTo = stair ? -w / 2f + 1.3f + stairRun + 0.4f : -99f;
        for (int f = 0; f < hs.Floors; f++)
        {
            float y0 = f * Storey, y1 = y0 + Storey + (f == hs.Floors - 1 ? 0.3f : 0f);
            var m = f == 0 ? lower : upper;
            for (int s = 0; s < 4; s++)
            {
                var (_, _, _, len) = Side(s, w, d);
                List<Opening> ops;
                if (s == 0 && f == 0) ops = stair ? Openings(len, null, 0, avoidFrom, avoidTo) : Openings(len, 0f, 1);
                else if (s == 0 && f == 1 && (stair || balcony))
                {
                    ops = Openings(len, balcony ? balconyU : (float?)null, 2, stair ? -w / 2f - 1f : 99f, stair ? -w / 2f + 1.5f : -99f);
                    if (stair) ops.Add(new Opening { U = -w / 2f + 0.75f, Width = 0.95f, Bottom = 0f, Top = 2.2f, Kind = 2 });
                }
                else if (s == 3 && f == 0 && stair) ops = Openings(len, 0f, 1);
                else ops = len < 4f ? new List<Opening> { new Opening { U = 0f, Width = 0.8f, Bottom = 0.95f, Top = 2.15f } } : Openings(len, null, 0);
                WallStorey(h, s, w, d, y0, y1, m, ops, hs.Open);
                foreach (var op in ops) Dress(h, s, w, d, y0, op, hs.Open, m == Stone, f == 0, old);
            }
        }
        // floors inside (open houses): a board floor, the ceiling and upper floor over it
        if (hs.Open)
        {
            Box("Floor", h, new Vector3(0, 0.03f, 0), new Vector3(w - 2f * Thick, 0.06f, d - 2f * Thick), Wood, true, 1.5f);
            if (hs.Floors > 1) Box("UpperFloor", h, new Vector3(0, Storey - 0.1f, 0), new Vector3(w - 2f * Thick, 0.22f, d - 2f * Thick), Wood, true, 1.5f);
        }
        float top = hs.Floors * Storey + 0.3f;
        // whitewash bands: under the eaves and between the storeys
        if (hs.Style == 0 || hs.Style == 3 || hs.Style == 1)
        {
            foreach (int s in new[] { 0, 1, 2, 3 })
            {
                var (o, u, n, len) = Side(s, w, d);
                float bandLen = (s < 2 ? len : d) + 0.14f;
                var bo = (s < 2 ? o : new Vector3(o.x, 0, 0)) + n * (Thick / 2f + 0.035f);
                Box("EaveBand", h, bo + Vector3.up * (top - 0.12f), SideSize(u, bandLen, 0.24f, 0.07f), Coping, false, 2f);
                if (hs.Floors > 1) Box("FloorBand", h, bo + Vector3.up * Storey, SideSize(u, bandLen, 0.16f, 0.07f), hs.Style == 1 ? Stone : Coping, false, 2f);
            }
        }
        // dressed stone quoins at the corners of the whitewashed houses
        if (hs.Style == 0 || hs.Style == 1)
        {
            float from = hs.Style == 1 ? Storey + 0.1f : 0.55f;
            int k = 0;
            for (float y = from; y + 0.36f < top - 0.25f; y += 0.38f, k++)
                foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    float a = k % 2 == 0 ? 0.55f : 0.32f, b = k % 2 == 0 ? 0.32f : 0.55f;
                    Box("Quoin", h, new Vector3(sx * (w / 2f - a / 2f + 0.035f), y + 0.17f, sz * (d / 2f + 0.035f)), new Vector3(a + 0.07f, 0.34f, 0.07f), Stone, false, 2.6f);
                    Box("Quoin", h, new Vector3(sx * (w / 2f + 0.035f), y + 0.17f, sz * (d / 2f - b / 2f)), new Vector3(0.07f, 0.34f, b), Stone, false, 2.6f);
                }
        }
        // the balcony: a slab on wooden brackets, a wooden railing round it
        if (balcony)
        {
            float bz = d / 2f + 0.6f, by = Storey - 0.02f;
            Box("Balcony", h, new Vector3(balconyU, by - 0.08f, bz), new Vector3(balconyW, 0.16f, 1.2f), Coping, true, 2f);
            for (int i = -1; i <= 1; i++)
                Box("Bracket", h, new Vector3(balconyU + i * (balconyW / 2f - 0.3f), by - 0.42f, d / 2f + 0.45f), new Vector3(0.12f, 0.5f, 0.9f), Wood, false, 1.5f);
            Railing(h, new Vector3(balconyU - balconyW / 2f + 0.05f, by, d / 2f + 1.15f), new Vector3(balconyU + balconyW / 2f - 0.05f, by, d / 2f + 1.15f), Wood);
            Railing(h, new Vector3(balconyU - balconyW / 2f + 0.05f, by, d / 2f + 0.05f), new Vector3(balconyU - balconyW / 2f + 0.05f, by, d / 2f + 1.15f), Wood);
            Railing(h, new Vector3(balconyU + balconyW / 2f - 0.05f, by, d / 2f + 0.05f), new Vector3(balconyU + balconyW / 2f - 0.05f, by, d / 2f + 1.15f), Wood);
        }
        // the outside stair up the front to the upper door, with a landing and a low wall
        if (stair)
        {
            float rise = Storey + 0.02f;
            var foot = h.TransformPoint(new Vector3(-w / 2f + 1.3f + stairRun, 0f, d / 2f + 0.62f));
            StepsDown(h, foot, h.TransformDirection(Vector3.left), rise, 1.15f, h.TransformDirection(Vector3.forward), drop, old ? Stone : Masonry);
            Box("Landing", h, new Vector3(-w / 2f + 0.65f, (rise - drop) / 2f, d / 2f + 0.62f), new Vector3(1.3f, rise + drop, 1.24f), old ? Stone : Masonry, true, 2.6f);
            Railing(h, new Vector3(-w / 2f + 0.05f, rise, d / 2f + 1.2f), new Vector3(-w / 2f + 1.25f, rise, d / 2f + 1.2f), Iron);
        }
        float ridge = RoofOver(h, w, d, top, hs.Hip, upper);
        if (hs.Chimney)
        {
            var cp = new Vector3(w * 0.22f, 0, -d * 0.12f);
            float cb = top + 0.4f, ct = ridge + 0.7f;
            Box("Chimney", h, cp + Vector3.up * ((cb + ct) / 2f), new Vector3(0.6f, ct - cb, 0.6f), hs.Style == 2 ? Stone : upper, false, 2.5f);
            Box("ChimneyCap", h, cp + Vector3.up * (ct + 0.05f), new Vector3(0.8f, 0.1f, 0.8f), Coping, false);
            Box("ChimneyHat", h, cp + Vector3.up * (ct + 0.32f), new Vector3(0.7f, 0.08f, 0.7f), Roof, false);
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, 1f), (1f, -1f), (-1f, 1f) })
                Box("ChimneyPost", h, cp + new Vector3(sx * 0.28f, ct + 0.18f, sz * 0.28f), new Vector3(0.08f, 0.26f, 0.08f), Stone, false);
        }
        // front steps when the door is well above the ground
        if (!stair && baseY - Y(h.TransformPoint(new Vector3(0, 0, d / 2f + 1.2f)).x, h.TransformPoint(new Vector3(0, 0, d / 2f + 1.2f)).z) > 0.35f)
        {
            var g = h.TransformPoint(new Vector3(0, 0, d / 2f + 0.3f));
            float down = baseY - Y(g.x, g.z + 0f);
            StepsDown(h, h.TransformPoint(new Vector3(0, -down, d / 2f + 0.3f + Mathf.CeilToInt(down / 0.18f) * 0.3f)), h.TransformDirection(Vector3.back), down, 1.6f, Vector3.zero, 0.3f, Masonry);
        }
        if (hs.Yard) Yard(h, hs, w, d);
    }

    /// <summary>A flight of steps (see Steps) whose blocks reach 'below' metres under its foot.</summary>
    private static void StepsDown(Transform t, Vector3 foot, Vector3 dir, float rise, float width, Vector3 wallSide, float below, Material m)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(rise) / 0.18f));
        float stepH = rise / count, run = 0.3f;
        dir.y = 0; dir.Normalize();
        float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        float bottom = foot.y - below;
        for (int i = 0; i < count; i++)
        {
            var p = foot + dir * (run * (i + 0.5f));
            float topY = foot.y + stepH * (i + 1);
            MeshGen.TexBox("Step", new Vector3(p.x, (topY + bottom) / 2f, p.z), new Vector3(width, topY - bottom, run + 0.02f), m, 2f, t, yaw);
        }
        if (wallSide != Vector3.zero)
            Railing(null, foot + wallSide.normalized * (width / 2f - 0.05f) + Vector3.up * 0.05f, foot + dir * (run * count) + wallSide.normalized * (width / 2f - 0.05f) + Vector3.up * (rise + 0.05f), Iron, t);
    }

    /// <summary>
    /// A railing from a to b (posts every ~0.9 m, a top and a middle rail, 1 m high). In the house's own
    /// space when 'h' is given, else in world space under 'worldParent'. Sloping when a and b differ in height.
    /// </summary>
    private static void Railing(Transform h, Vector3 a, Vector3 b, Material m, Transform worldParent = null)
    {
        var A = h != null ? h.TransformPoint(a) : a;
        var B = h != null ? h.TransformPoint(b) : b;
        var parent = h ?? worldParent;
        float len = Vector3.Distance(new Vector3(A.x, 0, A.z), new Vector3(B.x, 0, B.z));
        int posts = Mathf.Max(1, Mathf.CeilToInt(len / 0.9f));
        bool iron = m == Iron;
        float post = iron ? 0.04f : 0.09f;
        for (int i = 0; i <= posts; i++)
        {
            var p = Vector3.Lerp(A, B, i / (float)posts);
            var go = MeshGen.Object("RailPost", MeshGen.Box(new Vector3(post, 1.0f, post), 1.5f), m, parent, p + Vector3.up * 0.5f);
            go.AddComponent<BoxCollider>().size = new Vector3(post, 1.0f, post);
        }
        Beam("Rail", parent, A + Vector3.up * 1.0f, B + Vector3.up * 1.0f, iron ? 0.05f : 0.1f, m, true);
        Beam("Rail", parent, A + Vector3.up * 0.5f, B + Vector3.up * 0.5f, iron ? 0.03f : 0.07f, m, true);
    }

    /// <summary>A ruined house: walls broken down to ragged tops, no roof, rubble and a fallen beam.</summary>
    private static void Ruin(Transform h, House hs, Material m)
    {
        float w = hs.W, d = hs.D, full = hs.Floors * Storey;
        for (int s = 0; s < 4; s++)
        {
            var (o, u, _, len) = Side(s, w, d);
            float top = R(1.2f, full);
            for (float a = -len / 2f; a < len / 2f - 0.01f; a += 1f)
            {
                float b = Mathf.Min(len / 2f, a + 1f);
                top = Mathf.Clamp(Mathf.Lerp(top, R(0.6f, full), 0.55f), 0.5f, full);
                // a window gap now and then in the standing parts
                bool gap = top > 2.6f && Chance(0.2);
                if (gap)
                {
                    Box("RuinWall", h, o + u * ((a + b) / 2f) + Vector3.up * 0.45f, SideSize(u, b - a, 0.9f, Thick), m, true, 2.5f);
                    Box("RuinWall", h, o + u * ((a + b) / 2f) + Vector3.up * ((2.2f + top) / 2f), SideSize(u, b - a, top - 2.2f, Thick), m, true, 2.5f);
                }
                else Box("RuinWall", h, o + u * ((a + b) / 2f) + Vector3.up * (top / 2f), SideSize(u, b - a, top, Thick), m, true, 2.5f);
            }
        }
        for (int i = 0; i < 9; i++)
        {
            var p = new Vector3(R(-w / 2f + 0.8f, w / 2f - 0.8f), 0, R(-d / 2f + 0.8f, d / 2f - 0.8f));
            var go = Box("Rubble", h, p + Vector3.up * 0.2f, new Vector3(R(0.4f, 1.2f), R(0.3f, 0.7f), R(0.4f, 1.0f)), Stone, true, 2.6f);
            if (go != null) go.transform.rotation = Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f));
        }
        Beam("FallenBeam", h, h.TransformPoint(new Vector3(-w / 2f + 0.6f, 0.1f, 0)), h.TransformPoint(new Vector3(w / 2f - 1f, 2.4f, d * 0.2f)), 0.22f, Wood, true);
    }

    /// <summary>A walled yard in front of a house: field stone walls following the ground, a wooden gate, a tree.</summary>
    private static void Yard(Transform h, House hs, float w, float d)
    {
        float depth = R(4.5f, 6.5f), half = w / 2f + 1.2f;
        var corners = new[] { new Vector3(-half, 0, d / 2f), new Vector3(-half, 0, d / 2f + depth), new Vector3(half, 0, d / 2f + depth), new Vector3(half, 0, d / 2f) };
        for (int s = 0; s < 3; s++)
        {
            var a = h.TransformPoint(corners[s]); var b = h.TransformPoint(corners[s + 1]);
            float len = Vector3.Distance(a, b);
            var dir = (b - a).normalized;
            for (float t = 0f; t < len - 0.05f; t += 2f)
            {
                float t1 = Mathf.Min(len, t + 2f);
                // the gate in the middle of the front wall
                if (s == 1 && (t + t1) / 2f > len / 2f - 1f && (t + t1) / 2f < len / 2f + 1f) continue;
                var p = a + dir * ((t + t1) / 2f);
                float gy = Y(p.x, p.z);
                MeshGen.TexBox("YardWall", new Vector3(p.x, gy + 0.55f, p.z), new Vector3(0.45f, 1.7f, t1 - t + 0.45f), Stone, 2.6f, root, Quaternion.LookRotation(dir).eulerAngles.y);
                MeshGen.TexBox("YardCoping", new Vector3(p.x, gy + 1.44f, p.z), new Vector3(0.55f, 0.08f, t1 - t + 0.5f), Coping, 2f, root, Quaternion.LookRotation(dir).eulerAngles.y, false);
            }
            if (s == 1)
            {
                // the gate: two wooden leaves, one ajar
                var g = a + dir * (len / 2f);
                float gy = Y(g.x, g.z);
                var leaf = MeshGen.TexBox("Gate", g - dir * 0.5f + Vector3.up * (gy + 0.7f), new Vector3(0.06f, 1.35f, 0.95f), Shutter, 1.4f, root, Quaternion.LookRotation(dir).eulerAngles.y);
                var other = MeshGen.TexBox("Gate", g + dir * 0.5f + Vector3.up * (gy + 0.7f), new Vector3(0.06f, 1.35f, 0.95f), Shutter, 1.4f, root, Quaternion.LookRotation(dir).eulerAngles.y + 35f);
            }
        }
        // the tree of the yard
        var spot = h.TransformPoint(new Vector3(R(-w / 2f + 1f, w / 2f - 1f) * 0.6f, 0, d / 2f + depth * 0.6f));
        TreeAt(Chance(0.35) ? palms[rng.Next(palms.Length)] : Chance(0.5) ? olives[rng.Next(olives.Length)] : broadleaf, new Vector3(spot.x, Y(spot.x, spot.z), spot.z), R(0.7f, 1f));
    }

    // ------------------------------------------------------------------ trees and grass (impostors)

    [System.Serializable]
    private class TreeInfo { public float height, halfWidth, side, top, trunkRadius, leavesFrom; }

    private class TreeKind
    {
        public string Name;
        public Mesh Mesh;
        public Material Mat;
        public TreeInfo Info;
        public bool Grass;
    }

    private static TreeKind[] olives, pines, palms;
    private static TreeKind broadleaf, grass;

    /// <summary>
    /// A tree from its picture (Textures/Koy/Trees/name_Atlas.png, sizes in name.json): three upright cards
    /// through the trunk 60 degrees apart, each showing the tree from its side, and a level card with the
    /// crown seen from above at 'crown' of the height (none for grass).
    /// </summary>
    private static TreeKind LoadTree(string name, float crown, bool isGrass, Color tint)
    {
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Tex}/Trees/{name}.json");
        var info = json != null ? JsonUtility.FromJson<TreeInfo>(json.text) : new TreeInfo { height = 5, halfWidth = 2.5f, side = 5, top = 5, trunkRadius = 0.2f };
        var atlas = T($"Trees/{name}_Atlas.png");
        if (atlas == null) Debug.LogWarning("[HillVillage] tree picture missing: " + name);
        string matPath = $"{AssetSetup.Generated}/Materials/HV_Tree_{name}.mat";
        AssetDatabase.DeleteAsset(matPath);
        var mat = new Material(Shader.Find("UA/Impostor") ?? Shader.Find("UA/Foliage")) { name = "HV_Tree_" + name };
        mat.mainTexture = atlas;
        mat.color = tint;
        mat.SetFloat("_Cutoff", isGrass ? 0.4f : 0.45f);
        mat.SetFloat("_Wrap", isGrass ? 0.7f : 0.45f);
        mat.enableInstancing = true;
        AssetDatabase.CreateAsset(mat, matPath);
        float S = info.side, Sw = info.top;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        var tiles = new[] { new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f) };
        for (int k = 0; k < 3; k++)
        {
            float a = k * 60f * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (S / 2f);
            int i = v.Count;
            v.Add(-dir); v.Add(dir); v.Add(dir + Vector3.up * S); v.Add(-dir + Vector3.up * S);
            var o = tiles[k];
            uv.Add(o); uv.Add(o + new Vector2(0.5f, 0f)); uv.Add(o + new Vector2(0.5f, 0.5f)); uv.Add(o + new Vector2(0f, 0.5f));
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
        if (!isGrass)
        {
            int i = v.Count;
            float y = info.height * crown;
            v.Add(new Vector3(-Sw / 2f, y, -Sw / 2f)); v.Add(new Vector3(Sw / 2f, y, -Sw / 2f)); v.Add(new Vector3(Sw / 2f, y, Sw / 2f)); v.Add(new Vector3(-Sw / 2f, y, Sw / 2f));
            uv.Add(new Vector2(0.5f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f));
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
        var mesh = new Mesh { name = "HV_Tree_" + name };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.normals = v.Select(p => Vector3.up).ToArray();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{AssetSetup.Generated}/HV_Tree_{name}.asset");
        return new TreeKind { Name = name, Mesh = mesh, Mat = mat, Info = info, Grass = isGrass };
    }

    private static void LoadTrees()
    {
        olives = new[] { LoadTree("Olive1", 0.62f, false, new Color(1f, 1f, 0.95f)), LoadTree("Olive2", 0.6f, false, new Color(1f, 1f, 0.95f)) };
        pines = new[] { LoadTree("PineA", 0.72f, false, new Color(1.05f, 1.05f, 0.95f)), LoadTree("PineB", 0.7f, false, new Color(1.05f, 1.05f, 0.95f)), LoadTree("PineC", 0.72f, false, new Color(1.05f, 1.05f, 0.95f)) };
        palms = new[] { LoadTree("Palm1", 0.86f, false, new Color(1.08f, 1.05f, 0.9f)), LoadTree("Palm2", 0.88f, false, new Color(1.08f, 1.05f, 0.9f)) };
        broadleaf = LoadTree("Broadleaf", 0.7f, false, new Color(1f, 1f, 0.92f));
        grass = LoadTree("GrassTuft", 0f, true, new Color(1.1f, 1.05f, 0.92f));
    }

    private static Transform nature;

    /// <summary>A tree (or tuft of grass) standing at p, turned at random, its trunk stopping players and bullets.</summary>
    private static GameObject TreeAt(TreeKind kind, Vector3 p, float scale)
    {
        var go = new GameObject(kind.Grass ? "Grass" : "Tree_" + kind.Name);
        go.transform.SetParent(nature, false);
        go.transform.SetPositionAndRotation(p - Vector3.up * 0.08f * scale, Quaternion.Euler(0, R(0f, 360f), 0));
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = kind.Mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = kind.Mat;
        r.shadowCastingMode = kind.Grass ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        if (!kind.Grass)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = Mathf.Clamp(kind.Info.trunkRadius * 0.55f, 0.12f, 0.32f);
            c.height = Mathf.Min(3.2f, kind.Info.height * 0.5f);
            c.center = new Vector3(0, c.height / 2f, 0);
        }
        return go;
    }

    /// <summary>Is there room for a tree here (no wall, house or other tree within 'radius')?</summary>
    private static bool Free(Vector3 p, float radius) =>
        !Physics.CheckCapsule(p + Vector3.up * (radius + 0.5f), p + Vector3.up * (radius + 3f), radius, World.EnvMask, QueryTriggerInteraction.Ignore);

    // ------------------------------------------------------------------ rocks

    private static Mesh[] rockMeshes;

    private static Mesh RockMesh(int seed)
    {
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var src = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmp);
        var v = src.vertices;
        var map = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < v.Length; i++)
        {
            var key = new Vector3(Mathf.Round(v[i].x * 1000f), Mathf.Round(v[i].y * 1000f), Mathf.Round(v[i].z * 1000f));
            if (!map.TryGetValue(key, out var moved))
            {
                var d = v[i].normalized;
                float n = Mathf.PerlinNoise(d.x * 1.7f + seed * 3.1f, d.z * 1.7f + d.y * 1.3f) * 0.55f + Mathf.PerlinNoise(d.x * 5f + seed, d.y * 5f - seed) * 0.15f;
                moved = d * (0.35f + n);
                moved.y *= 0.62f;
                if (moved.y < -0.05f) moved.y *= 0.4f;   // a flatter underside, sitting in the ground
                map[key] = moved;
            }
            v[i] = moved;
        }
        var mesh = new Mesh { name = "HV_Rock" + seed };
        mesh.SetVertices(v); mesh.SetTriangles(src.triangles, 0); mesh.SetUVs(0, src.uv);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{AssetSetup.Generated}/HV_Rock{seed}.asset");
        return mesh;
    }

    private static void Rock(Vector3 p, float size)
    {
        if (rockMeshes == null) rockMeshes = new[] { RockMesh(1), RockMesh(2), RockMesh(3) };
        var mesh = rockMeshes[rng.Next(rockMeshes.Length)];
        var go = MeshGen.Object("Rock", mesh, RockMat, nature, p - Vector3.up * size * 0.15f, R(0f, 360f));
        go.transform.localScale = new Vector3(size * R(0.9f, 1.4f), size, size * R(0.9f, 1.3f));
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    // ------------------------------------------------------------------ the mosque

    /// <summary>
    /// The village mosque on the upper terrace: a prayer hall of dressed stone under a tiled hip roof,
    /// two rows of windows, a porch on wooden posts in front; inside a carpet, the mihrab and the minbar;
    /// the minaret at its corner with a balcony and a pointed lead cap. A walled courtyard with a plane tree.
    /// </summary>
    private static void Mosque(Vector3 c)
    {
        var t = new GameObject("Mosque").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(new Vector3(c.x, c.y + 0.45f, c.z), Quaternion.Euler(0, 180f, 0));
        const float w = 12f, d = 13f, hgt = 7.2f;
        Box("Plinth", t, new Vector3(0, -0.4f, 0), new Vector3(w + 0.4f, 0.9f, d + 0.4f), Masonry, true, 3f);
        for (int s = 0; s < 4; s++)
        {
            var (_, _, _, len) = Side(s, w, d);
            var ops = new List<Opening>();
            if (s == 0) ops.Add(new Opening { U = 0f, Width = 1.8f, Bottom = 0f, Top = 3.0f, Kind = 1 });
            int nWin = s < 2 ? 3 : 4;
            for (int i = 0; i < nWin; i++)
            {
                float u = -len / 2f + len * (i + 1) / (nWin + 1);
                if (s == 0 && Mathf.Abs(u) < 1.6f) continue;
                ops.Add(new Opening { U = u, Width = 1.0f, Bottom = 1.1f, Top = 3.0f, Kind = 0 });
            }
            WallStorey(t, s, w, d, 0f, hgt, Masonry, ops.Where(o => o.Kind == 1).ToList(), true);
            foreach (var op in ops)
            {
                if (op.Kind == 1) { Dress(t, s, w, d, 0f, op, true, true, true, false); continue; }
                var (o, u2, n, _) = Side(s, w, d);
                var face = o + n * (Thick / 2f);
                var wc = face + u2 * op.U + Vector3.up * 2.05f;
                // tall lower windows behind iron grilles in a stone frame, small arched ones above
                Box("Glass", t, wc + n * 0.005f, SideSize(u2, 1.0f, 1.9f, 0.02f), Glass, false);
                Box("StoneFrame", t, wc + n * 0.04f + Vector3.up * 1.05f, SideSize(u2, 1.4f, 0.24f, 0.1f), Coping, false);
                Box("StoneFrame", t, wc + n * 0.04f - Vector3.up * 1.0f, SideSize(u2, 1.4f, 0.12f, 0.2f), Coping, false);
                for (int k = 1; k <= 5; k++)
                    Box("Grille", t, wc + u2 * (-0.5f + k / 6f) + n * 0.06f, SideSize(u2, 0.025f, 1.9f, 0.025f), Iron, false);
                var up = face + u2 * op.U + Vector3.up * 4.9f;
                Box("Glass", t, up + n * 0.005f, SideSize(u2, 0.7f, 1.0f, 0.02f), Glass, false);
                Sphere("Arch", t, t.TransformPoint(up + Vector3.up * 0.5f + n * 0.005f), 0.36f, Glass).transform.localScale = new Vector3(0.72f, 0.72f, 0.05f);
                Box("StoneFrame", t, up + n * 0.04f - Vector3.up * 0.55f, SideSize(u2, 1.0f, 0.1f, 0.18f), Coping, false);
                if (Chance(0.5)) NightLights.Window(t, t.TransformPoint(wc + n * 0.03f), t.TransformDirection(n), 0.9f, 1.7f, night);
            }
        }
        // a cornice under the eaves
        foreach (int s in new[] { 0, 1, 2, 3 })
        {
            var (o, u, n, len) = Side(s, w, d);
            Box("Cornice", t, (s < 2 ? o : new Vector3(o.x, 0, 0)) + n * (Thick / 2f + 0.06f) + Vector3.up * (hgt - 0.15f), SideSize(u, (s < 2 ? len : d) + 0.24f, 0.3f, 0.12f), Coping, false);
        }
        RoofOver(t, w, d, hgt, true, Masonry);
        // inside: the carpet, the mihrab niche in the qibla wall (the back), the minbar beside it
        Box("Carpet", t, new Vector3(0, 0.03f, 0), new Vector3(w - 1f, 0.04f, d - 1f), Carpet, true);
        Box("Mihrab", t, new Vector3(0, 1.8f, -d / 2f + 0.5f), new Vector3(1.6f, 3.4f, 0.2f), Coping, false);
        Box("MihrabNiche", t, new Vector3(0, 1.6f, -d / 2f + 0.62f), new Vector3(1.0f, 2.6f, 0.06f), Glass, false);
        for (int i = 0; i < 8; i++)
            Box("Minbar", t, new Vector3(2.4f, 0.18f + i * 0.2f, -d / 2f + 1.2f + i * 0.35f), new Vector3(0.9f, 0.36f + i * 0.4f, 0.35f), Wood, true);
        // the porch: a raised stone floor, four posts, a tiled roof
        Box("PorchFloor", t, new Vector3(0, -0.2f, d / 2f + 1.9f), new Vector3(w + 0.4f, 0.5f, 3.8f), Masonry, true, 3f);
        for (int i = 0; i < 4; i++)
        {
            float x = -w / 2f + 0.5f + i * (w - 1f) / 3f;
            Box("PorchPost", t, new Vector3(x, 1.75f, d / 2f + 3.4f), new Vector3(0.3f, 3.5f, 0.3f), Wood, true, 1.5f);
        }
        Box("PorchBeam", t, new Vector3(0, 3.55f, d / 2f + 3.4f), new Vector3(w + 0.6f, 0.3f, 0.3f), Wood, false, 1.5f);
        var porchRoof = MeshGen.Object("PorchRoof", HipRoof(w + 1.2f, 4.6f, 0.9f, 2.2f), Roof, t, t.TransformPoint(new Vector3(0, 3.7f, d / 2f + 1.9f)), t.eulerAngles.y);
        porchRoof.AddComponent<MeshCollider>().sharedMesh = porchRoof.GetComponent<MeshFilter>().sharedMesh;
        // the minaret: square stone base, round shaft, a balcony, the upper shaft, the lead cone
        var m = t.TransformPoint(new Vector3(-w / 2f - 1.6f, 0, d / 2f - 1.4f));
        float gy = c.y;
        MeshGen.TexBox("MinaretBase", new Vector3(m.x, gy + 1.6f, m.z), new Vector3(2.8f, 4.0f, 2.8f), Masonry, 3f, t, t.eulerAngles.y);
        Cylinder("MinaretFoot", t, new Vector3(m.x, gy + 3.6f, m.z), 1.2f, 0.8f, Coping, false, 8);
        Cylinder("MinaretShaft", t, new Vector3(m.x, gy + 4.4f, m.z), 0.95f, 11.2f, Masonry, true, 12);
        Cylinder("Serefe", t, new Vector3(m.x, gy + 15.3f, m.z), 1.55f, 0.25f, Coping, false, 16);
        Cylinder("SerefeUnder", t, new Vector3(m.x, gy + 14.8f, m.z), 1.15f, 0.5f, Coping, false, 12);
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16f;
            MeshGen.TexBox("SerefeRail", new Vector3(m.x + Mathf.Cos(a) * 1.45f, gy + 16.0f, m.z + Mathf.Sin(a) * 1.45f), new Vector3(0.06f, 0.95f, 0.06f), Iron, 1f, t, 0f, false);
        }
        Cylinder("SerefeRing", t, new Vector3(m.x, gy + 16.45f, m.z), 1.48f, 0.06f, Iron, false, 16);
        Cylinder("MinaretUpper", t, new Vector3(m.x, gy + 15.55f, m.z), 0.8f, 3.0f, Plaster, false, 12);
        var cap = MeshGen.Object("MinaretCap", Cone(0.98f, 3.6f, 14), Lead, t, new Vector3(m.x, gy + 18.5f, m.z));
        Cylinder("Finial", t, new Vector3(m.x, gy + 22.0f, m.z), 0.05f, 1.2f, Lead, false, 6);
        Sphere("FinialBall", t, new Vector3(m.x, gy + 22.5f, m.z), 0.13f, Lead);
        Sphere("FinialBall", t, new Vector3(m.x, gy + 22.85f, m.z), 0.09f, Lead);
        NightLights.Halo(t, new Vector3(m.x, gy + 16.2f, m.z), 3.2f, new Color(0.4f, 0.75f, 0.5f), night);
    }

    // ------------------------------------------------------------------ the square

    /// <summary>
    /// A street lamp as in the village squares: a dark green cast pole, one or two curved arms with
    /// white glass globes (lit at night, a warm pool under them).
    /// </summary>
    private static void GlobeLamp(Vector3 p, bool two)
    {
        var t = new GameObject("GlobeLamp").transform;
        t.SetParent(root, false);
        t.position = p;
        Cylinder("LampBase", t, p, 0.14f, 0.6f, LampGreen, true, 10);
        Cylinder("LampPole", t, p + Vector3.up * 0.6f, 0.065f, 3.6f, LampGreen, false, 8);
        Sphere("LampKnob", t, p + Vector3.up * 4.25f, 0.09f, LampGreen);
        float yaw = R(0f, 180f);
        var dirs = two ? new[] { Quaternion.Euler(0, yaw, 0) * Vector3.right, Quaternion.Euler(0, yaw, 0) * Vector3.left } : new[] { Quaternion.Euler(0, yaw, 0) * Vector3.right };
        foreach (var dir in dirs)
        {
            var a = p + Vector3.up * 3.9f;
            var b = a + dir * 0.55f + Vector3.up * 0.15f;
            Beam("LampArm", t, a, b, 0.05f, LampGreen);
            var g = b + Vector3.down * 0.05f;
            Sphere("Globe", t, g + Vector3.down * 0.22f, 0.22f, GlobeDay);
            var glow = Sphere("GlobeLit", t, g + Vector3.down * 0.22f, 0.23f, GlobeNight);
            var r = glow.GetComponent<Renderer>();
            r.enabled = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            night.Add(r);
            NightLights.Halo(t, g + Vector3.down * 0.22f, 2.2f, new Color(0.85f, 0.7f, 0.45f), night);
        }
        Physics.SyncTransforms();
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, World.EnvMask, QueryTriggerInteraction.Ignore))
            NightLights.Pool(t, hit.point + hit.normal * 0.03f, hit.normal, 5f, night);
    }

    private static void Bench(Vector3 p, float yaw)
    {
        var t = new GameObject("Bench").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        foreach (float x in new[] { -0.75f, 0.75f })
            Box("BenchFoot", t, new Vector3(x, 0.22f, 0), new Vector3(0.18f, 0.44f, 0.45f), Masonry, true, 2f);
        for (int i = 0; i < 3; i++)
            Box("BenchSlat", t, new Vector3(0, 0.47f, -0.15f + i * 0.15f), new Vector3(1.9f, 0.05f, 0.12f), Wood, false, 1.5f);
        Box("BenchBack", t, new Vector3(0, 0.8f, 0.24f), new Vector3(1.9f, 0.4f, 0.05f), Wood, false, 1.5f);
    }

    /// <summary>
    /// The platform in the middle of the square (as in the reference): a stone base a metre high with a
    /// paved top, a wooden railing of posts and crossed braces round it, two wooden frames standing on it,
    /// steps up at the front and the side.
    /// </summary>
    private static void Platform(Vector3 c)
    {
        var t = new GameObject("Platform").transform;
        t.SetParent(root, false);
        t.position = c;
        const float w = 13f, d = 8f, hgt = 1.1f;
        Box("PlatformBase", t, new Vector3(0, hgt / 2f - 0.3f, 0), new Vector3(w, hgt + 0.6f, d), Masonry, true, 3f);
        Box("PlatformTop", t, new Vector3(0, hgt + 0.05f, 0), new Vector3(w + 0.2f, 0.12f, d + 0.2f), Paving, true, 3f);
        // steps: front (south) in the middle, and the east end
        StepsDown(t, c + new Vector3(0, 0f, -d / 2f - 0.1f - 6 * 0.3f), Vector3.forward, hgt + 0.1f, 2.6f, Vector3.zero, 0.3f, Masonry);
        StepsDown(t, c + new Vector3(w / 2f + 0.1f + 6 * 0.3f, 0f, 0f), Vector3.left, hgt + 0.1f, 2.2f, Vector3.zero, 0.3f, Masonry);
        // the railing: posts, a top rail and crossed braces in each bay, open at the two stair heads
        float y = c.y + hgt + 0.11f;
        void RailSide(Vector3 a, Vector3 b, float gapAt)
        {
            float len = Vector3.Distance(a, b);
            int bays = Mathf.RoundToInt(len / 1.6f);
            var dir = (b - a).normalized;
            for (int i = 0; i < bays; i++)
            {
                float s0 = len * i / bays, s1 = len * (i + 1) / bays;
                if (gapAt >= 0f && (s0 + s1) / 2f > gapAt - 1.4f && (s0 + s1) / 2f < gapAt + 1.4f) continue;
                var p0 = a + dir * s0; var p1 = a + dir * s1;
                foreach (var q in new[] { p0, p1 })
                {
                    var post = MeshGen.TexBox("RailPost", new Vector3(q.x, y + 0.5f, q.z), new Vector3(0.13f, 1.0f, 0.13f), GreyWood, 1.5f, t);
                }
                Beam("TopRail", t, new Vector3(p0.x, y + 0.98f, p0.z), new Vector3(p1.x, y + 0.98f, p1.z), 0.12f, GreyWood, true);
                Beam("Brace", t, new Vector3(p0.x, y + 0.1f, p0.z), new Vector3(p1.x, y + 0.9f, p1.z), 0.07f, GreyWood, true);
                Beam("Brace", t, new Vector3(p0.x, y + 0.9f, p0.z), new Vector3(p1.x, y + 0.1f, p1.z), 0.07f, GreyWood, true);
            }
        }
        var c0 = new Vector3(c.x - w / 2f + 0.1f, y, c.z - d / 2f + 0.1f);
        var c1 = new Vector3(c.x + w / 2f - 0.1f, y, c.z - d / 2f + 0.1f);
        var c2 = new Vector3(c.x + w / 2f - 0.1f, y, c.z + d / 2f - 0.1f);
        var c3 = new Vector3(c.x - w / 2f + 0.1f, y, c.z + d / 2f - 0.1f);
        RailSide(c0, c1, w / 2f);
        RailSide(c1, c2, d / 2f);
        RailSide(c2, c3, -1f);
        RailSide(c3, c0, -1f);
        // the two wooden frames (like goal posts) across the platform, a beam joining their tops
        foreach (float x in new[] { -3.5f, 3.5f })
        {
            var a = new Vector3(c.x + x, y, c.z - 2.6f); var b = new Vector3(c.x + x, y, c.z + 2.6f);
            foreach (var q in new[] { a, b })
                MeshGen.TexBox("FramePost", q + Vector3.up * 1.7f, new Vector3(0.22f, 3.4f, 0.22f), GreyWood, 1.5f, t);
            Beam("FrameBeam", t, a + Vector3.up * 3.35f, b + Vector3.up * 3.35f, 0.25f, GreyWood, true);
            Beam("FrameKnee", t, a + Vector3.up * 2.6f, a + Vector3.up * 3.3f + Vector3.forward * 0.7f, 0.12f, GreyWood);
            Beam("FrameKnee", t, b + Vector3.up * 2.6f, b + Vector3.up * 3.3f + Vector3.back * 0.7f, 0.12f, GreyWood);
        }
        Beam("FrameTie", t, new Vector3(c.x - 3.6f, y + 3.5f, c.z), new Vector3(c.x + 3.6f, y + 3.5f, c.z), 0.18f, GreyWood, true);
    }

    /// <summary>The fountain (cesme) against the terrace wall: a stone front with an arched niche, two taps and a trough.</summary>
    private static void Fountain(Vector3 wallFoot, float yaw)
    {
        var t = new GameObject("Fountain").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(wallFoot, Quaternion.Euler(0, yaw, 0));
        Box("FountainFront", t, new Vector3(0, 1.45f, 0.3f), new Vector3(2.8f, 2.9f, 0.6f), Masonry, true, 3f);
        Box("FountainCap", t, new Vector3(0, 2.95f, 0.32f), new Vector3(3.1f, 0.14f, 0.72f), Coping, false);
        Box("Niche", t, new Vector3(0, 1.5f, 0.61f), new Vector3(1.5f, 1.7f, 0.04f), Coping, false);
        Box("NicheBack", t, new Vector3(0, 1.4f, 0.63f), new Vector3(1.1f, 1.3f, 0.02f), Masonry, false, 2f);
        Sphere("NicheArch", t, t.TransformPoint(new Vector3(0, 2.35f, 0.62f)), 0.75f, Coping).transform.localScale = new Vector3(1.5f, 0.6f, 0.04f);
        foreach (float x in new[] { -0.3f, 0.3f })
        {
            var tap = t.TransformPoint(new Vector3(x, 1.25f, 0.62f));
            Beam("Tap", t, tap, tap + t.forward * 0.18f, 0.05f, Iron);
        }
        Box("Trough", t, new Vector3(0, 0.35f, 1.0f), new Vector3(2.4f, 0.7f, 0.8f), Masonry, true, 2f);
        Box("TroughWater", t, new Vector3(0, 0.66f, 1.0f), new Vector3(2.1f, 0.04f, 0.55f), Water, false);
    }

    /// <summary>The coffee house's terrace: wooden posts and beams under a low tiled roof, tables and chairs below.</summary>
    private static void Pergola(Vector3 c, float w, float d)
    {
        var t = new GameObject("Pergola").transform;
        t.SetParent(root, false);
        t.position = c;
        const float hgt = 2.8f;
        for (int i = 0; i < 3; i++)
            for (int k = 0; k < 2; k++)
            {
                var p = c + new Vector3(-w / 2f + 0.2f + i * (w - 0.4f) / 2f, 0, -d / 2f + 0.2f + k * (d - 0.4f));
                MeshGen.TexBox("PergolaPost", p + Vector3.up * hgt / 2f, new Vector3(0.2f, hgt, 0.2f), Wood, 1.5f, t);
            }
        foreach (float z in new[] { -d / 2f + 0.2f, d / 2f - 0.2f })
            MeshGen.TexBox("PergolaBeam", c + new Vector3(0, hgt + 0.1f, z), new Vector3(w + 0.4f, 0.22f, 0.2f), Wood, 1.5f, t, 0f, false);
        var roof = MeshGen.Object("PergolaRoof", HipRoof(w + 0.9f, d + 0.9f, 0.9f, 2.2f), Roof, t, c + Vector3.up * (hgt + 0.2f));
        roof.AddComponent<MeshCollider>().sharedMesh = roof.GetComponent<MeshFilter>().sharedMesh;
        for (int i = 0; i < 6; i++)
        {
            var p = c + new Vector3(-w / 2f + 1.3f + (i % 3) * (w - 2.6f) / 2f, 0, i < 3 ? -d / 4f : d / 4f);
            Cylinder("TableLeg", t, p, 0.04f, 0.72f, Iron, false, 6);
            Cylinder("TableTop", t, p + Vector3.up * 0.72f, 0.42f, 0.04f, Coping, true, 14);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f + R(-15f, 15f);
                var cp = p + Quaternion.Euler(0, a, 0) * Vector3.forward * 0.7f;
                var chair = new GameObject("Chair").transform;
                chair.SetParent(t, false);
                chair.SetPositionAndRotation(cp, Quaternion.Euler(0, a + 180f, 0));
                Box("ChairSeat", chair, new Vector3(0, 0.45f, 0), new Vector3(0.42f, 0.05f, 0.42f), Wood, false, 1.5f);
                Box("ChairBack", chair, new Vector3(0, 0.75f, -0.19f), new Vector3(0.42f, 0.5f, 0.04f), Wood, false, 1.5f);
                foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                    Box("ChairLeg", chair, new Vector3(sx * 0.18f, 0.22f, sz * 0.18f), new Vector3(0.04f, 0.45f, 0.04f), Wood, false, 1.5f);
            }
        }
    }

    /// <summary>A round planter of dressed stone with a tree in it (the square's palms and its plane tree).</summary>
    private static void Planter(Vector3 p, TreeKind tree, float scale, float radius = 1.3f)
    {
        Cylinder("Planter", root, p - Vector3.up * 0.2f, radius, 0.65f, Masonry, true, 16);
        Cylinder("PlanterSoil", root, p + Vector3.up * 0.4f, radius - 0.15f, 0.06f, Mats.Pbr("K_Soil", "Ground104", new Color(0.7f, 0.6f, 0.5f), 0.05f), false, 14);
        TreeAt(tree, p + Vector3.up * 0.45f, scale);
        for (int i = 0; i < 3; i++)
            TreeAt(grass, p + Vector3.up * 0.45f + new Vector3(R(-0.6f, 0.6f), 0, R(-0.6f, 0.6f)), R(0.5f, 0.8f));
    }

    // ------------------------------------------------------------------ the village

    /// <summary>Every house: position, facing (the front looks along yaw), size, storeys, style and extras.</summary>
    private static List<House> Houses() => new List<House>
    {
        // the upper terrace, first row facing the square
        new House(-88f, 25f, 180f, 9f, 7f, 2, 1, "balcony"),
        new House(-75f, 25f, 180f, 8f, 7f, 2, 0, "stair open"),
        new House(-62f, 26f, 180f, 10f, 8f, 2, 3, "gable balcony chimney"),
        new House(-49f, 25f, 180f, 7f, 6f, 1, 2, "gable open"),
        new House(-36f, 25f, 180f, 9f, 7f, 2, 0, "chimney"),
        new House(-24f, 26f, 180f, 8f, 7f, 2, 1, "stair open"),
        new House(12f, 25f, 180f, 8f, 7f, 2, 3, "balcony"),
        // the upper terrace, second row
        new House(-86f, 43f, 180f, 8f, 7f, 2, 0, "chimney"),
        new House(-72f, 44f, 180f, 9f, 7f, 2, 3, "gable"),
        new House(-57f, 43f, 180f, 8f, 7f, 1, 2, "gable open"),
        new House(-43f, 44f, 180f, 10f, 8f, 2, 0, "balcony stair open"),
        new House(-28f, 43f, 180f, 8f, 7f, 2, 1, "chimney"),
        new House(13f, 42f, 180f, 9f, 8f, 2, 0, "yard"),
        // the west quarter, both sides of its lane
        new House(-40f, 8f, 180f, 8f, 7f, 2, 0, "balcony open"),
        new House(-54f, 9f, 180f, 9f, 7f, 2, 3, "stair"),
        new House(-68f, 8f, 180f, 8f, 6f, 1, 2, "gable open"),
        new House(-82f, 9f, 180f, 10f, 7f, 2, 1, "balcony chimney"),
        new House(-40f, -9f, 0f, 8f, 8f, 2, 1, "open"),
        new House(-55f, -10f, 0f, 9f, 7f, 2, 0, "chimney"),
        new House(-69f, -10f, 0f, 9f, 7f, 2, 3, "ruin"),
        new House(-84f, -9f, 0f, 8f, 7f, 2, 0, "stair open"),
        // the coffee house at the foot of the east street, and the houses up the street
        new House(39f, 2f, 270f, 8f, 10f, 1, 0, "open"),
        new House(38.5f, 24f, 270f, 8f, 7f, 2, 3, "gable"),
        new House(38.5f, 37f, 270f, 9f, 7f, 2, 0, "stair open"),
        new House(38.5f, 49f, 270f, 8f, 7f, 1, 2, "gable open"),
        // along the road out east
        new House(54f, 16f, 180f, 9f, 7f, 2, 0, "balcony"),
        new House(69f, 17f, 180f, 8f, 7f, 1, 2, "gable open"),
        new House(84f, 16f, 180f, 10f, 8f, 2, 1, "chimney yard"),
        new House(55f, -14f, 0f, 8f, 7f, 2, 3, "gable chimney"),
        new House(71f, -15f, 0f, 9f, 7f, 2, 0, "stair open"),
        new House(88f, -16f, 0f, 12f, 8f, 1, 2, "gable"),
        // the lower quarter, first row facing the square's wall
        new House(-86f, -27f, 0f, 9f, 7f, 2, 1, ""),
        new House(-72f, -27f, 0f, 8f, 7f, 2, 0, "balcony open"),
        new House(-58f, -27f, 0f, 10f, 8f, 2, 3, "gable"),
        new House(-44f, -27f, 0f, 8f, 7f, 1, 2, "gable"),
        new House(-30f, -27f, 0f, 9f, 7f, 2, 0, "stair open"),
        new House(-16f, -27f, 0f, 8f, 7f, 2, 1, "chimney"),
        new House(4f, -27f, 0f, 8f, 7f, 2, 0, "balcony"),
        new House(16f, -27f, 0f, 8f, 7f, 2, 3, "open"),
        // the lower quarter, second row facing the groves
        new House(-80f, -45f, 180f, 8f, 7f, 1, 2, "gable yard"),
        new House(-64f, -45f, 180f, 9f, 7f, 2, 0, "open"),
        new House(-48f, -46f, 180f, 8f, 7f, 2, 3, "gable balcony"),
        new House(-32f, -45f, 180f, 10f, 8f, 2, 1, "yard"),
        new House(-16f, -45f, 180f, 8f, 7f, 1, 2, "gable open"),
        new House(0f, -45f, 180f, 9f, 7f, 2, 0, "chimney"),
        new House(15f, -45f, 180f, 8f, 7f, 2, 1, "stair open"),
        // down the south east street
        new House(38.5f, -30f, 270f, 8f, 7f, 2, 0, "balcony"),
        new House(38.5f, -46f, 270f, 8f, 7f, 1, 2, "gable open"),
        // farms and the old houses out on the hill
        new House(-30f, -78f, 0f, 10f, 8f, 2, 2, "gable chimney"),
        new House(-13f, -81f, 0f, 12f, 8f, 1, 2, "gable"),
        new House(44f, -80f, 20f, 9f, 7f, 1, 3, "gable open"),
        new House(-40f, 66f, 160f, 8f, 7f, 1, 2, "ruin"),
        new House(30f, 78f, 200f, 6f, 5f, 1, 2, "gable"),
        new House(68f, 44f, 230f, 9f, 7f, 2, 0, "chimney"),
    };

    /// <summary>The cobbled streets and lanes, and the dirt tracks out to the farms and up the hill.</summary>
    private static void Streets()
    {
        cobbles.Clear(); tracks.Clear();
        Vector2[] L(params float[] p) { var a = new Vector2[p.Length / 2]; for (int i = 0; i < a.Length; i++) a[i] = new Vector2(p[i * 2], p[i * 2 + 1]); return a; }
        cobbles.Add((L(29.5f, -6f, 29.5f, 53f), 9f));         // the east street up to the terrace
        cobbles.Add((L(30f, -6f, 30f, -57f), 8f));            // the street down to the lower quarter
        cobbles.Add((L(30f, 1f, 105f, 1f), 7f));              // the road out east
        cobbles.Add((L(-26f, 0f, -99f, 0f), 6f));             // the west quarter's lane
        cobbles.Add((L(-99f, 19.3f, 24f, 19.3f), 4.4f));      // the terrace lane along the wall
        cobbles.Add((L(-97f, 34.5f, -17f, 34.5f), 5f));       // the terrace's back lane
        cobbles.Add((L(-6f, 21f, -6f, 29f), 19f));            // the mosque's courtyard
        cobbles.Add((L(-99f, -21.6f, 27f, -21.6f), 4.6f));    // the lower lane under the wall
        cobbles.Add((L(-99f, -36.2f, 27f, -36.2f), 9f));      // the lower quarter's middle lane
        tracks.Add((L(30f, -57f, 22f, -72f, -12f, -86f, -32f, -84f), 2.6f));
        tracks.Add((L(22f, -72f, 44f, -86f), 2.4f));
        tracks.Add((L(30f, 53f, 37f, 66f, 30f, 77f), 2.4f));
        tracks.Add((L(-97f, 22f, -82f, 50f, -58f, 62f, -40f, 64f), 2.4f));
        tracks.Add((L(-6f, 48f, -9f, 62f, -2f, 78f), 2.2f));
        tracks.Add((L(105f, 1f, 130f, 6f), 3f));
    }

    public static MapLayout Build()
    {
        rng = new System.Random(41);
        night.Clear(); mats.Clear(); roofMeshes.Clear();
        rockMeshes = null; sphereMesh = null;
        root = new GameObject("Map_HillVillage").transform;
        nature = new GameObject("Nature").transform;
        nature.SetParent(root, false);
        LoadTrees();
        Levels();
        var houses = Houses();
        foreach (var hs in houses)
            if (Built(hs.X, hs.Z) < 0.95f) HousePad(new Vector3(hs.X, 0, hs.Z), hs.W, hs.D, hs.Yaw);
        Streets();
        BuildGround();

        // the terrace walls between the levels, and the stairs against them
        var walls = new GameObject("TerraceWalls").transform;
        walls.SetParent(root, false);
        TerraceWall(walls, true, 16f, -100f, 24f, -1f, (-7.5f, -4.5f), (-61.2f, -58.8f));
        TerraceWall(walls, false, 24f, 16f, 42f, 1f);
        TerraceWall(walls, true, -18f, -100f, 26f, -1f, (-7.5f, -4.5f), (-60.2f, -57.8f));
        TerraceWall(walls, false, 26f, -42f, -18f, -1f);
        // up from the square to the mosque: two flights against the wall meeting at a landing on its axis
        Steps(walls, new Vector3(-15.0f, 0f, 13.85f), Vector3.right, 4.5f, 2.2f, Vector3.back);
        Steps(walls, new Vector3(3.0f, 0f, 13.85f), Vector3.left, 4.5f, 2.2f, Vector3.back);
        MeshGen.TexBox("Landing", new Vector3(-6f, 2.1f, 13.85f), new Vector3(3f, 4.8f, 2.2f), Masonry, 3f, walls);
        // down from the square to the lower quarter, the same way
        Steps(walls, new Vector3(-11.7f, -2.5f, -20.15f), Vector3.right, 2.5f, 2.2f, Vector3.back);
        Steps(walls, new Vector3(-0.3f, -2.5f, -20.15f), Vector3.left, 2.5f, 2.2f, Vector3.back);
        MeshGen.TexBox("Landing", new Vector3(-6f, -1.4f, -20.15f), new Vector3(3f, 2.8f, 2.2f), Masonry, 3f, walls);
        // the west quarter's stairs up to the terrace and down to the lower quarter
        float westUp = GroundHeight(-66f, 12f), westDown = GroundHeight(-59f, -15f);
        Steps(walls, new Vector3(-66.2f, westUp, 13.85f), Vector3.right, 4.5f - westUp, 2.0f, Vector3.back);
        MeshGen.TexBox("Landing", new Vector3(-60f, (4.5f + westUp) / 2f - 0.2f, 13.85f), new Vector3(2.4f, 4.5f - westUp + 0.4f, 2.2f), Masonry, 3f, walls);
        int downCount = Mathf.CeilToInt((westDown + 2.5f) / 0.18f);
        Steps(walls, new Vector3(-58.8f - downCount * 0.3f - 1.2f, -2.5f, -20.15f), Vector3.right, westDown + 2.5f, 2.0f, Vector3.back);
        MeshGen.TexBox("Landing", new Vector3(-59f, (westDown - 2.5f) / 2f - 0.2f, -20.15f), new Vector3(2.4f, westDown + 2.9f, 2.2f), Masonry, 3f, walls);

        // the square: paved, its platform, planters with palms and the plane tree, the fountain, the coffee house's terrace
        MeshGen.TexBox("SquarePaving", new Vector3(0f, -0.1f, -1f), new Vector3(52f, 0.26f, 31.9f), Paving, 4f, root);
        Platform(new Vector3(-6f, 0.03f, -5f));
        Planter(new Vector3(-21f, 0.03f, -12f), palms[0], 0.85f);
        Planter(new Vector3(21f, 0.03f, -12f), palms[1], 0.75f);
        Planter(new Vector3(-21f, 0.03f, 8.5f), palms[1], 0.8f);
        Planter(new Vector3(9f, 0.03f, 8.5f), broadleaf, 1.8f, 1.7f);
        Fountain(new Vector3(18f, 0.03f, 14.95f), 180f);
        Pergola(new Vector3(20f, 0.03f, 3f), 8f, 10f);
        foreach (var (x, z, yaw) in new[] { (-21f, 13.4f, 180f), (8f, 13.4f, 180f), (-24.5f, -2f, 90f), (-24.5f, -9f, 90f), (-20f, -16f, 0f), (10f, -16f, 0f), (17f, -16f, 0f), (-80f, 16.3f, 180f), (-54f, 16.3f, 180f), (-30f, 16.3f, 180f) })
            Bench(new Vector3(x, GroundHeight(x, z) + (Mathf.Abs(z - 16.3f) < 0.1f ? 0.03f : 0.03f), z), yaw);

        // the mosque and its courtyard (a low wall round it, the gate towards the stairs), two plane trees
        Mosque(new Vector3(-6f, 4.5f, 36.5f));
        foreach (var (a, b) in new[] { (new Vector2(-17f, 21.5f), new Vector2(-8f, 21.5f)), (new Vector2(-4f, 21.5f), new Vector2(6f, 21.5f)), (new Vector2(6f, 21.5f), new Vector2(6f, 47.5f)), (new Vector2(6f, 47.5f), new Vector2(-17f, 47.5f)), (new Vector2(-17f, 47.5f), new Vector2(-17f, 21.5f)) })
        {
            var p = (a + b) / 2f; float len = Vector2.Distance(a, b);
            float yaw = Mathf.Abs(a.y - b.y) < 0.1f ? 90f : 0f;
            MeshGen.TexBox("CourtyardWall", new Vector3(p.x, 4.5f + 0.5f, p.y), new Vector3(0.5f, 1.4f, len + 0.5f), Masonry, 3f, root, yaw);
            MeshGen.TexBox("CourtyardCoping", new Vector3(p.x, 4.5f + 1.24f, p.y), new Vector3(0.6f, 0.1f, len + 0.6f), Coping, 2f, root, yaw, false);
        }
        foreach (var x in new[] { -8f, -4f })
            Cylinder("GatePier", root, new Vector3(x, 4.5f, 21.5f), 0.35f, 2.2f, Masonry, true, 8);
        TreeAt(broadleaf, new Vector3(-13f, 4.5f, 25f), 1.9f);
        TreeAt(broadleaf, new Vector3(2.5f, 4.5f, 44f), 1.6f);

        // the houses
        var hp = new GameObject("Houses").transform;
        hp.SetParent(root, false);
        foreach (var hs in houses) { BuildHouse(hs, hp); Physics.SyncTransforms(); }

        // street lamps: the square, the terrace wall, the streets and lanes
        foreach (var (x, z, two) in new[]
        {
            (-24.5f, -15.5f, true), (24.5f, -15.5f, true), (-24.5f, 12.8f, true), (24.5f, 12.8f, false), (-14.5f, -10.5f, true), (2.5f, -10.5f, true),
            (-90f, 16.4f, false), (-66f, 16.4f, false), (-42f, 16.4f, false), (-18f, 16.4f, false), (12f, 16.4f, false),
            (34.6f, 10f, false), (34.6f, 22f, false), (34.6f, 34f, false), (34.6f, 46f, false),
            (34.6f, -12f, false), (34.6f, -26f, false), (34.6f, -40f, false), (34.6f, -52f, false),
            (50f, 5.2f, false), (66f, 5.2f, false), (82f, 5.2f, false), (98f, 5.2f, false),
            (-34f, 3.6f, false), (-48f, 3.6f, false), (-62f, 3.6f, false), (-76f, 3.6f, false), (-92f, 3.6f, false),
            (-88f, -23.4f, false), (-64f, -23.4f, false), (-40f, -23.4f, false), (-22f, -23.4f, false), (10f, -23.4f, false),
            (-70f, -31.8f, false), (-38f, -31.8f, false), (-6f, -31.8f, false),
        })
            GlobeLamp(new Vector3(x, Y(x, z), z), two);

        Vegetation(houses);
        Farmland();

        // ammunition crates: in the square, by the mosque, up the streets, in the lanes, the groves and on the hill
        var supply = new GameObject("AmmoCrates").transform;
        supply.SetParent(root, false);
        foreach (var (x, z) in new[] { (-20f, -14.5f), (8f, 23f), (36f, 20f), (60f, 3f), (-60f, -22.5f), (-70f, 2.5f), (18f, -62f), (-30f, 72f), (33f, -38f) })
            Obstacles.AmmoCrate(supply, new Vector3(x, Y(x, z), z), R(0f, 360f));
        // invisible walls just outside the playable area
        foreach (var (c, s) in new[]
        {
            (new Vector3(0, 30f, Edge), new Vector3(Edge * 2f, 100f, 1f)), (new Vector3(0, 30f, -Edge), new Vector3(Edge * 2f, 100f, 1f)),
            (new Vector3(Edge, 30f, 0), new Vector3(1f, 100f, Edge * 2f)), (new Vector3(-Edge, 30f, 0), new Vector3(1f, 100f, Edge * 2f)),
        })
        {
            var wall = new GameObject("Edge");
            wall.layer = World.FenceLayer;
            wall.transform.SetParent(root, false);
            wall.transform.position = c;
            wall.AddComponent<BoxCollider>().size = s;
        }
        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            if (tr.gameObject.layer != World.FenceLayer)
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);

        var layout = new MapLayout { Name = "HILL VILLAGE", Root = root.gameObject, Center = Vector3.zero, Radius = Half };
        var boards = new GameObject("Boards").transform;
        boards.SetParent(root, false);
        foreach (var (near, readers, ar, en) in new[]
        {
            (new Vector3(-17f, 0f, -14.5f), new Vector3(-6f, 0f, -5f), "الساحة", "THE SQUARE"),
            (new Vector3(-12f, 4.5f, 19.6f), new Vector3(-6f, 4.5f, 30f), "المسجد", "THE MOSQUE"),
            (new Vector3(-40f, -2.5f, -34f), new Vector3(-40f, -2.5f, -40f), "الحي السفلي", "THE LOWER QUARTER"),
            (new Vector3(12f, 0f, -63f), new Vector3(12f, 0f, -55f), "بساتين الزيتون", "THE OLIVE GROVES"),
            (new Vector3(-12f, 0f, 55f), new Vector3(-12f, 0f, 45f), "تل الصنوبر", "THE PINE HILL"),
            (new Vector3(58f, 0f, 6.5f), new Vector3(40f, 0f, 1f), "الطريق الشرقي", "THE EAST ROAD"),
        })
            Signboards.Stand(boards, new Vector3(near.x, 0, near.z), readers, ar, en);
        Navigation(layout);
        root.gameObject.AddComponent<MapScene>().NightLights = MapOptimizer.Merge(root, "HillVillage", null, night).ToArray();
        Debug.Log($"[HillVillage] built: {houses.Count} houses, {root.GetComponentsInChildren<Renderer>().Length} renderers, {layout.Waypoints.Length} waypoints, {layout.Spawns.Length} spawns");
        return layout;
    }

    /// <summary>
    /// Pines over the hill and round the edges, olive groves in rows on the slopes below the village
    /// and on the east slope, trees by the houses, rocks on the steep ground, and dry grass everywhere
    /// the ground is not paved or trodden.
    /// </summary>
    private static void Vegetation(List<House> houses)
    {
        Physics.SyncTransforms();
        int pinesPlaced = 0;
        for (int i = 0; i < 5000 && pinesPlaced < 460; i++)
        {
            float x = R(-Edge + 4f, Edge - 4f), z = R(-Edge + 4f, Edge - 4f);
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float want = z > 50f ? Mathf.InverseLerp(50f, 80f, z) * 0.9f : edge > Half - 12f ? Mathf.InverseLerp(Half - 12f, Half + 10f, edge) * 0.85f : 0f;
            if (!Chance(want) || Built(x, z) > 0.25f || LineDistance(tracks, x, z) < 2.5f) continue;
            var p = new Vector3(x, Y(x, z), z);
            if (!Free(p, 2.2f)) continue;
            TreeAt(pines[rng.Next(pines.Length)], p, R(0.7f, 1.05f));
            Physics.SyncTransforms();
            pinesPlaced++;
        }
        int olivesPlaced = 0;
        // the groves below the village: rows across the slope
        for (float z = -62f; z > -Half - 2f; z -= 7f)
            for (float x = -96f; x < 96f; x += 7.5f)
            {
                float px = x + R(-1.2f, 1.2f) + (Mathf.RoundToInt(z) % 2 == 0 ? 3.7f : 0f), pz = z + R(-1f, 1f);
                if (Chance(0.18) || Built(px, pz) > 0.3f || LineDistance(tracks, px, pz) < 3f) continue;
                var p = new Vector3(px, Y(px, pz), pz);
                if (!Free(p, 1.8f)) continue;
                TreeAt(olives[rng.Next(olives.Length)], p, R(0.85f, 1.2f));
                olivesPlaced++;
            }
        // and on the east slope above the road
        for (float z = 24f; z < 66f; z += 8f)
            for (float x = 48f; x < 98f; x += 8f)
            {
                float px = x + R(-1.5f, 1.5f), pz = z + R(-1.5f, 1.5f);
                if (Chance(0.35) || Built(px, pz) > 0.3f) continue;
                var p = new Vector3(px, Y(px, pz), pz);
                if (!Free(p, 1.8f)) continue;
                TreeAt(olives[rng.Next(olives.Length)], p, R(0.8f, 1.15f));
                olivesPlaced++;
            }
        Physics.SyncTransforms();
        // a few broad trees and palms near the houses at the edges of the village
        for (int i = 0, placed = 0; i < 300 && placed < 16; i++)
        {
            var hs = houses[rng.Next(houses.Count)];
            var p = new Vector3(hs.X + R(-9f, 9f), 0, hs.Z + R(-9f, 9f));
            if (LineDistance(cobbles, p.x, p.z) < 1.5f || Mathf.Abs(p.x) < 27f && p.z > -17f && p.z < 15f) continue;
            p.y = Y(p.x, p.z);
            if (!Free(p, 1.4f)) continue;
            TreeAt(Chance(0.3) ? palms[rng.Next(palms.Length)] : Chance(0.5) ? broadleaf : olives[rng.Next(olives.Length)], p, R(0.8f, 1.3f));
            Physics.SyncTransforms();
            placed++;
        }
        // rocks on the steep banks and in the forest
        for (int i = 0, placed = 0; i < 900 && placed < 70; i++)
        {
            float x = R(-Edge + 4f, Edge - 4f), z = R(-Edge + 4f, Edge - 4f);
            if (Built(x, z) > 0.2f) continue;
            float slope = Mathf.Abs(GroundHeight(x + 1f, z) - GroundHeight(x - 1f, z)) + Mathf.Abs(GroundHeight(x, z + 1f) - GroundHeight(x, z - 1f));
            if (slope < 0.7f && !(z > 55f && Chance(0.3))) continue;
            var p = new Vector3(x, Y(x, z), z);
            if (!Free(p, 1.2f)) continue;
            Rock(p, R(0.6f, 1.8f));
            placed++;
        }
        // dry grass: tufts in the open, fewer in the forest, some along the foot of the walls in the village
        for (int i = 0, placed = 0; i < 9000 && placed < 1700; i++)
        {
            float x = R(-Half - 25f, Half + 25f), z = R(-Half - 25f, Half + 25f);
            if (LineDistance(cobbles, x, z) < 0.6f || (Mathf.Abs(x) < 26.5f && z > -17.5f && z < 15.5f)) continue;
            float built = Built(x, z);
            if (built > 0.6f && !Chance(0.12)) continue;
            if (z > 60f && !Chance(0.35)) continue;
            var p = new Vector3(x, Y(x, z), z);
            if (Physics.CheckSphere(p + Vector3.up * 0.5f, 0.35f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
            TreeAt(grass, p, R(0.6f, 1.25f));
            placed++;
        }
        Debug.Log($"[HillVillage] {pinesPlaced} pines, {olivesPlaced} olive trees");
    }

    /// <summary>Dry stone walls across the groves (cover in the open), round hay bales by the barns.</summary>
    private static void Farmland()
    {
        foreach (float z0 in new[] { -66f, -79f, -92f })
            for (float x = -94f; x < 94f;)
            {
                float len = R(6f, 14f);
                if (Chance(0.3)) { x += len; continue; }
                for (float s = 0f; s < len; s += 2f)
                {
                    float px = x + s + 1f, pz = z0 + Mathf.Sin(px / 9f) * 1.2f;
                    if (LineDistance(tracks, px, pz) < 2f || Built(px, pz) > 0.4f) continue;
                    float gy = Y(px, pz);
                    MeshGen.TexBox("FieldWall", new Vector3(px, gy + 0.3f, pz), new Vector3(2.05f, 1.1f, 0.6f), Stone, 2.6f, root, R(-3f, 3f));
                }
                x += len + R(2f, 5f);
            }
        var hayMat = Mats.Pbr("K_Hay", "Ground104", new Color(1f, 0.86f, 0.52f), 0.05f);
        foreach (var (x, z) in new[] { (86f, -23f), (89.5f, -23.5f), (93f, -22.5f), (-18f, -88f), (-14.5f, -88.5f), (-11f, -88f) })
        {
            float gy = Y(x, z);
            var bale = MeshGen.Object("HayBale", MeshGen.Cylinder(0.7f, 1.3f, 1.5f, 16), hayMat, root, new Vector3(x - 0.65f, gy + 0.68f, z));
            bale.transform.rotation = Quaternion.Euler(0, 0, 90f);
            var c = bale.AddComponent<CapsuleCollider>();
            c.radius = 0.7f; c.height = 1.4f; c.center = new Vector3(0, 0.65f, 0);
        }
    }

    /// <summary>
    /// Bot waypoints on a 3 m grid over every walkable level (the ground, the terrace walls' tops, the
    /// platform, inside the houses) and the links between them that can be walked; the spawn points.
    /// </summary>
    private static void Navigation(MapLayout layout)
    {
        Physics.SyncTransforms();
        const float step = 3f;
        int mask = World.EnvMask | (1 << World.FenceLayer);
        var points = new List<Vector3>();
        var columns = new Dictionary<Vector2Int, List<int>>();
        int n = Mathf.FloorToInt((Half + 6f) / step);
        for (int gx = -n; gx <= n; gx++)
            for (int gz = -n; gz <= n; gz++)
            {
                var top = new Vector3(gx * step, 90f, gz * step);
                foreach (var h in Physics.RaycastAll(top, Vector3.down, 200f, World.EnvMask, QueryTriggerInteraction.Ignore).OrderByDescending(x => x.point.y))
                {
                    if (h.normal.y < 0.72f) continue;
                    var p = h.point;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.55f, p + Vector3.up * 1.6f, 0.42f, mask, QueryTriggerInteraction.Ignore)) continue;
                    var key = new Vector2Int(gx, gz);
                    if (!columns.TryGetValue(key, out var list)) columns[key] = list = new List<int>();
                    list.Add(points.Count);
                    points.Add(p);
                }
            }
        var links = new List<Vector2Int>();
        foreach (var pair in columns)
            foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) })
            {
                if (!columns.TryGetValue(pair.Key + d, out var others)) continue;
                foreach (int ia in pair.Value)
                    foreach (int ib in others)
                    {
                        var a = points[ia]; var b = points[ib];
                        float dist = Vector3.Distance(a, b);
                        if (Mathf.Abs(a.y - b.y) > dist * 0.6f) continue;
                        var mid = (a + b) / 2f;
                        if (!Physics.Raycast(new Vector3(mid.x, Mathf.Max(a.y, b.y) + 0.8f, mid.z), Vector3.down, out var g, Mathf.Abs(a.y - b.y) + 1.6f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                        if (g.point.y < Mathf.Min(a.y, b.y) - 0.6f) continue;
                        if (Physics.SphereCast(a + Vector3.up * 1.2f, 0.38f, (b - a).normalized, out _, dist, mask, QueryTriggerInteraction.Ignore)) continue;
                        links.Add(new Vector2Int(ia, ib));
                    }
            }
        // keep what can be walked to from the square
        var nb = new List<int>[points.Count];
        for (int i = 0; i < nb.Length; i++) nb[i] = new List<int>();
        foreach (var l in links) { nb[l.x].Add(l.y); nb[l.y].Add(l.x); }
        var reached = new bool[points.Count];
        var queue = new Queue<int>();
        int start = Enumerable.Range(0, points.Count).OrderBy(i => (points[i] - new Vector3(-16f, 0f, -12f)).sqrMagnitude).FirstOrDefault();
        if (points.Count > 0) { reached[start] = true; queue.Enqueue(start); }
        while (queue.Count > 0)
            foreach (int k in nb[queue.Dequeue()])
                if (!reached[k]) { reached[k] = true; queue.Enqueue(k); }
        if (reached.Count(r => r) < 50) for (int i = 0; i < reached.Length; i++) reached[i] = true;
        var index = new int[points.Count];
        var kept = new List<Vector3>();
        for (int i = 0; i < points.Count; i++) { index[i] = reached[i] ? kept.Count : -1; if (reached[i]) kept.Add(points[i]); }
        layout.Waypoints = kept.ToArray();
        layout.Links = links.Where(l => reached[l.x] && reached[l.y]).Select(l => new Vector2Int(index[l.x], index[l.y])).ToArray();
        // the spawn points: spread over the levels, each on open ground
        var spawns = new List<Vector3>();
        foreach (var (x, z) in new[] { (-82f, 19.5f), (18f, 19.5f), (31f, 47f), (70f, 2f), (32f, -50f), (-40f, -36f), (-88f, 0.5f), (-6f, 58f), (-18f, -12f) })
        {
            var p = new Vector3(x, Y(x, z), z);
            if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.4f, mask, QueryTriggerInteraction.Ignore))
                Debug.LogWarning($"[HillVillage] spawn point {x},{z} is blocked");
            spawns.Add(p);
        }
        layout.Spawns = spawns.ToArray();
        Debug.Log($"[HillVillage] navigation: {kept.Count} waypoints of {points.Count}, {layout.Links.Length} links");
    }
}
