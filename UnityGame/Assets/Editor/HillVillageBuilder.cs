using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The "Hill Village" map: a half-old Turkish (Aegean) hill village. Its plan (HillVillagePlan.cs) cuts
/// the hillside into levels and winds lanes through them, then sets houses of many kinds along both
/// sides of every lane, each its own size and height (HillVillageHouses.cs builds them): old stone and
/// whitewashed houses, tower houses, flat-roofed concrete houses, shop houses with iron balconies,
/// cottages, sheds, courtyard compounds, walled gardens. This file builds the ground, the terrace walls
/// and stairs, the paved square (meydan) with its platform, fountain and coffee house, the Grand
/// Hall and its garden on the upper terrace, the clock tower, the windmills, poles, lamps and props, the olive groves below and
/// the pine forest on the hill above, and the bots' waypoints. Every texture is a Poly Haven scan
/// (CC0); the trees are pictures of Poly Haven's olive, pine and plane-like trees rendered from three
/// sides and from above (impostors, see Tools/impostor.py), the palms and grass made the same way;
/// the other maps use the same trees (RealTree).
/// </summary>
public static partial class HillVillageBuilder
{
    private const string Tex = "Assets/Textures/Koy";
    private static Transform root;
    private static readonly List<Renderer> night = new List<Renderer>();


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
    private static Material Water => AssetSetup.Solid("K_Water", new Color(0.1f, 0.16f, 0.16f), 0.95f, 0f);
    private static Material Cloth => AssetSetup.Solid("K_Cloth", new Color(0.72f, 0.18f, 0.12f), 0.1f, 0f);

    // ------------------------------------------------------------------ the levels of the ground

    /// <summary>The ground under (x, z): the mesh (raycast), else the height function.</summary>
    private static float Y(float x, float z)
    {
        var hits = Physics.RaycastAll(new Vector3(x, 120f, z), Vector3.down, 300f, World.EnvMask);
        float best = float.MinValue;
        foreach (var h in hits)
            if (h.collider.name.StartsWith("Ground")) best = Mathf.Max(best, h.point.y);
        return best > float.MinValue ? best : GroundHeight(x, z);
    }

    // ------------------------------------------------------------------ the ground mesh

    /// <summary>
    /// Grid lines along one axis: 1 m apart near the retaining walls (the step between two levels falls
    /// inside the 2.1 m wall), 2 m over the village, 4 m and then 8 m further out. Fewer triangles than a
    /// fine grid everywhere (the phone draws the ground in fewer vertices); rectilinear, so no cracks.
    /// </summary>
    private static List<float> GridLines(params float[] walls)
    {
        var lines = new List<float>();
        float x = -Size / 2f;
        while (x < Size / 2f)
        {
            lines.Add(x);
            float a = Mathf.Abs(x);
            bool nearWall = walls.Any(w => Mathf.Abs(x - w) < 4f);
            x += nearWall ? 1f : a < Half + 12f ? 2f : a < Edge + 10f ? 4f : 8f;
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
        // the distance to the nearest lane edge, segment by segment over the pixels near each
        var dist = new float[size * size];
        for (int i = 0; i < dist.Length; i++) dist[i] = 9f;
        float cell = area.width / size;
        foreach (var (line, width) in cobbles)
            for (int k = 0; k + 1 < line.Length; k++)
            {
                var a = line[k]; var ab = line[k + 1] - a;
                float reach = width / 2f + 1f;
                int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, a.x + ab.x) - reach - area.xMin) / cell));
                int i1 = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.x, a.x + ab.x) + reach - area.xMin) / cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, a.y + ab.y) - reach - area.yMin) / cell));
                int j1 = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.y, a.y + ab.y) + reach - area.yMin) / cell));
                float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        var p = new Vector2(area.xMin + (i + 0.5f) * cell, area.yMin + (j + 0.5f) * cell);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        float d = Vector2.Distance(p, a + ab * t) - width / 2f;
                        if (d < dist[j * size + i]) dist[j * size + i] = d;
                    }
            }
        var px = new Color32[size * size];
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = area.xMin + (i + 0.5f) * cell, z = area.yMin + (j + 0.5f) * cell;
                float ragged = 0.35f * Mathf.PerlinNoise(x * 0.4f + 3f, z * 0.4f + 9f);
                float m = Mathf.InverseLerp(0.5f + ragged, -0.2f + ragged, dist[j * size + i]);
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
        // the retaining walls run along x = 24 and 26 and along z = 16 and -18
        var xs = GridLines(24f, 26f);
        var zs = GridLines(16f, -18f);
        int nx = xs.Count, nz = zs.Count;
        var heights = new float[nx, nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
                heights[i, j] = GroundHeight(xs[i], zs[j]);
        var mat = GroundMaterial();
        var parent = new GameObject("Ground").transform;
        parent.SetParent(root, false);
        const int chunk = 96;
        int made = 0;
        for (int cj = 0; cj < nz - 1; cj += chunk)
            for (int ci = 0; ci < nx - 1; ci += chunk)
            {
                int ni = Mathf.Min(chunk, nx - 1 - ci) + 1, nj = Mathf.Min(chunk, nz - 1 - cj) + 1;
                var v = new Vector3[ni * nj];
                var c = new Color[ni * nj];
                for (int j = 0; j < nj; j++)
                    for (int i = 0; i < ni; i++)
                    {
                        int gi = ci + i, gj = cj + j;
                        float x = xs[gi], z = zs[gj];
                        v[j * ni + i] = new Vector3(x, heights[gi, gj], z);
                        float dx = xs[Mathf.Min(nx - 1, gi + 1)] - xs[Mathf.Max(0, gi - 1)];
                        float dz = zs[Mathf.Min(nz - 1, gj + 1)] - zs[Mathf.Max(0, gj - 1)];
                        float hx = (heights[Mathf.Min(nx - 1, gi + 1), gj] - heights[Mathf.Max(0, gi - 1), gj]) / dx;
                        float hz = (heights[gi, Mathf.Min(nz - 1, gj + 1)] - heights[gi, Mathf.Max(0, gj - 1)]) / dz;
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
        Debug.Log($"[HillVillage] ground: {nx}x{nz} grid in {made} chunks");
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
        float forest = Mathf.Clamp01(Mathf.InverseLerp(70f, 95f, z) * Mathf.InverseLerp(0.2f, 0.6f, noise + 0.3f) + Mathf.InverseLerp(Half + 4f, Half + 30f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z))) * 0.7f);
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

    /// <summary>A cone (the windmills' caps), its own mesh.</summary>
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

    private static bool treesReady;

    /// <summary>The tree pictures, made once: the other maps use them too, and making them again would orphan the trees already placed.</summary>
    private static void EnsureTrees()
    {
        if (treesReady && broadleaf != null && broadleaf.Mesh != null && broadleaf.Mat != null) return;
        LoadTrees();
        treesReady = true;
    }

    /// <summary>
    /// A realistic tree for another map (the same pictures as here): "olive", "pine", "palm",
    /// "broadleaf", "bush" (a young olive) or "grass", between minH and maxH metres tall, at p (world)
    /// under 'parent'. Three draws from 'random' (kind, height, turn), as Nature.Tree takes.
    /// </summary>
    internal static GameObject RealTree(string kind, System.Random random, Vector3 p, float minH, float maxH, Transform parent)
    {
        EnsureTrees();
        var set = kind == "olive" ? olives : kind == "pine" ? pines : kind == "palm" ? palms : kind == "bush" ? new[] { olives[1], olives[0] } : kind == "grass" ? new[] { grass } : new[] { broadleaf };
        var k = set[random.Next(set.Length)];
        float h = minH + (float)random.NextDouble() * (maxH - minH);
        float yaw = (float)random.NextDouble() * 360f;
        float scale = h / Mathf.Max(0.1f, k.Info.height);
        bool small = k.Grass || kind == "bush";
        var go = new GameObject(k.Grass ? "Grass" : kind == "bush" ? "Bush" : "Tree_" + k.Name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(p - Vector3.up * 0.08f * scale, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = k.Mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = k.Mat;
        r.shadowCastingMode = small ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        if (!small)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = Mathf.Clamp(k.Info.trunkRadius * 0.55f, 0.12f, 0.32f);
            c.height = Mathf.Min(3.2f, k.Info.height * 0.5f);
            c.center = new Vector3(0, c.height / 2f, 0);
        }
        return go;
    }

    /// <summary>A tree (or tuft of grass) standing at p, turned at random, its trunk stopping players and bullets.</summary>
    private static GameObject TreeAt(TreeKind kind, Vector3 p, float scale, bool scenery = false)
    {
        var go = new GameObject(kind.Grass ? "Grass" : "Tree_" + kind.Name);
        go.transform.SetParent(nature, false);
        // the forest and the groves round the village: drawn only so far on the weaker phones
        if (scenery) go.layer = Settings.SceneryLayer;
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

    // ------------------------------------------------------------------ the grand hall and the clock tower

    /// <summary>
    /// The Grand Hall on the upper terrace, the biggest house of the village, looking down on the square:
    /// three storeys of white plaster between stone pilasters under a heavy cornice, tall windows, a
    /// flat roof behind its parapet with a pediment over the middle; before its door a portico of four
    /// stone columns on a stepped base carrying a balustraded terrace; flag poles either side. Open
    /// inside with a stair; a ladder at its side up to the roof. Its garden: a pool, cypresses, plane
    /// trees, a pavilion, benches.
    /// </summary>
    private static void Hall(Vector3 c)
    {
        var hs = new House { X = c.x, Z = c.z, Yaw = 180f, W = 17f, D = 12f, Floors = 3, Kind = Kind.Classic, Style = 0, Colour = 0, Paint = 3, Roof = 2, Open = true, Plain = true, Ladder = true };
        var h = BuildHouse(hs, root);
        h.name = "GrandHall";
        float w = hs.W, d = hs.D, top = 3f * Storey;
        // pilasters at the corners and between the bays, a plinth course, the cornice under the parapet
        foreach (int side in new[] { 0, 1, 2, 3 })
        {
            var (o, u, n, len) = Side(side, w, d);
            float full = side < 2 ? len : d;
            var face = (side < 2 ? o : new Vector3(o.x, 0, 0)) + n * (Thick / 2f);
            int bays = Mathf.RoundToInt(full / 3.4f);
            for (int i = 0; i <= bays; i++)
            {
                float at = -full / 2f + 0.3f + (full - 0.6f) * i / bays;
                if (side == 0 && Mathf.Abs(at) < 4.6f) continue;   // the portico stands there
                Box("Pilaster", h, face + u * at + n * 0.06f + Vector3.up * (top / 2f), SideSize(u, 0.55f, top, 0.12f), Coping, false, 2f);
                Box("PilasterCap", h, face + u * at + n * 0.09f + Vector3.up * (top - 0.5f), SideSize(u, 0.75f, 0.22f, 0.18f), Coping, false, 2f);
                Box("PilasterBase", h, face + u * at + n * 0.09f + Vector3.up * 0.3f, SideSize(u, 0.75f, 0.6f, 0.18f), Masonry, false, 3f);
            }
            Box("Cornice", h, face + n * 0.2f + Vector3.up * (top + 0.02f), SideSize(u, full + 0.8f, 0.3f, 0.5f), Coping, false, 2f);
            Box("CorniceLip", h, face + n * 0.3f + Vector3.up * (top + 0.24f), SideSize(u, full + 1.0f, 0.14f, 0.7f), Coping, false, 2f);
        }
        // the portico: a stepped base, four columns, the beam and the terrace they carry
        const float pw = 9.6f, pd = 3.3f;
        float pz = d / 2f + pd / 2f, colH = 2f * Storey - 0.55f;
        Box("PorticoBase", h, new Vector3(0, 0.02f, pz), new Vector3(pw, 0.3f, pd), Paving, true, 3f);
        Box("PorticoStep", h, new Vector3(0, -0.1f, pz + 0.3f), new Vector3(pw + 0.7f, 0.2f, pd + 0.4f), Masonry, true, 3f);
        foreach (float x in new[] { -4.1f, -1.5f, 1.5f, 4.1f })
        {
            var foot = h.TransformPoint(new Vector3(x, 0.17f, d / 2f + pd - 0.55f));
            Box("ColumnBase", h, new Vector3(x, 0.32f, d / 2f + pd - 0.55f), new Vector3(0.95f, 0.3f, 0.95f), Coping, true, 2f);
            Cylinder("Column", h, foot + Vector3.up * 0.3f, 0.36f, colH - 0.6f, Coping, true, 16);
            Box("ColumnCap", h, new Vector3(x, colH - 0.02f, d / 2f + pd - 0.55f), new Vector3(0.95f, 0.3f, 0.95f), Coping, false, 2f);
        }
        Box("PorticoBeam", h, new Vector3(0, colH + 0.33f, pz), new Vector3(pw + 0.2f, 0.4f, pd + 0.2f), Coping, true, 2f);
        Box("PorticoTiles", h, new Vector3(0, colH + 0.54f, pz), new Vector3(pw, 0.03f, pd), Tiles, false, 1.5f);
        float ty = colH + 0.55f;
        BarRailing(h, new Vector3(-pw / 2f + 0.1f, ty, d / 2f + pd - 0.1f), new Vector3(pw / 2f - 0.1f, ty, d / 2f + pd - 0.1f), Iron);
        BarRailing(h, new Vector3(-pw / 2f + 0.1f, ty, d / 2f + 0.1f), new Vector3(-pw / 2f + 0.1f, ty, d / 2f + pd - 0.1f), Iron);
        BarRailing(h, new Vector3(pw / 2f - 0.1f, ty, d / 2f + 0.1f), new Vector3(pw / 2f - 0.1f, ty, d / 2f + pd - 0.1f), Iron);
        // the pediment over the middle of the front, on the parapet's line
        float py = top + 0.24f;
        var ped = MeshGen.Object("Pediment", MeshGen.Gable(pw, 2.1f, 2.5f), Plaster, h, h.TransformPoint(new Vector3(0, py + 0.9f, d / 2f - 0.05f)), h.eulerAngles.y);
        ped.transform.localScale = new Vector3(1f, 1f, 3f);
        Box("PedimentBase", h, new Vector3(0, py + 0.45f, d / 2f - 0.05f), new Vector3(pw + 0.3f, 0.9f, 0.34f), Plaster, true, 2.5f);
        Box("PedimentCornice", h, new Vector3(0, py + 0.92f, d / 2f + 0.05f), new Vector3(pw + 0.6f, 0.12f, 0.5f), Coping, false, 2f);
        foreach (float sx in new[] { -1f, 1f })
            Beam("PedimentRake", h, h.TransformPoint(new Vector3(sx * (pw / 2f + 0.15f), py + 0.95f, d / 2f + 0.08f)), h.TransformPoint(new Vector3(0, py + 3.1f, d / 2f + 0.08f)), 0.2f, Coping);
        var eye = Cylinder("PedimentEye", h, h.TransformPoint(new Vector3(0, py + 1.75f, d / 2f + 0.14f)), 0.5f, 0.06f, Glass, false, 16);
        eye.transform.rotation = h.rotation * Quaternion.Euler(90f, 0, 0);
        // flag poles either side of the portico: long plain pennants
        foreach (float sx in new[] { -1f, 1f })
        {
            var pole = h.TransformPoint(new Vector3(sx * (pw / 2f + 1.4f), 0f, d / 2f + pd));
            Cylinder("FlagPole", root, pole, 0.06f, 9f, Aluminium, true, 8);
            Box("Pennant", h, new Vector3(sx * (pw / 2f + 1.4f) + 0.55f, 7.9f, d / 2f + pd), new Vector3(1f, 1.9f, 0.03f), Cloth, false, 1f);
        }
        // the garden before the hall: the walk to the door, the pool, cypresses, the pavilion, benches
        float gy = 4.5f;
        MeshGen.TexBox("GardenWalk", new Vector3(-6f, gy + 0.02f, 24.3f), new Vector3(2.8f, 0.08f, 5.6f), Paving, 3f, root);
        MeshGen.TexBox("PoolRim", new Vector3(-12.4f, gy + 0.25f, 24.3f), new Vector3(6.2f, 0.5f, 3.2f), Masonry, 3f, root);
        MeshGen.TexBox("PoolWater", new Vector3(-12.4f, gy + 0.46f, 24.3f), new Vector3(5.6f, 0.06f, 2.6f), Water, 2f, root, 0f, false);
        Cylinder("PoolJet", root, new Vector3(-12.4f, gy + 0.45f, 24.3f), 0.12f, 0.9f, Coping, false, 8);
        Pergola(new Vector3(2.4f, gy + 0.03f, 25f), 4f, 4.6f);
        foreach (var (x, z) in new[] { (-16f, 28.6f), (5f, 29f), (-16f, 46.4f), (5f, 46.4f), (-9.4f, 22.6f), (-2.6f, 22.6f) })
            ZahraBuilder.CypressFor(root, new Vector3(x, gy, z), R(7f, 9f), rng);
        Bench(new Vector3(-12.4f, gy + 0.02f, 26.7f), 0f);
    }

    /// <summary>
    /// The clock tower at the north side of the square, the village's high point: a square shaft of
    /// stone 25 m high against the terrace wall. No door on the square: the way in is from the terrace
    /// above, through the wall's parapet; inside, stone steps wind up round the walls past slit windows
    /// to the look-out under the clock, open on all four sides over the whole village. Above it the
    /// clock's four faces (lit at night) and a pointed tiled roof.
    /// </summary>
    private static void ClockTower(Vector3 c, float terrace)
    {
        var t = new GameObject("ClockTower").transform;
        t.SetParent(root, false);
        t.position = c;
        const float w = 5.2f, wall = 0.55f, look = 15.3f, lookH = 3.1f;
        float inner = w - 2f * wall;
        // the foot: solid stone up to the terrace's level, a moulding round its top
        Box("TowerFoot", t, new Vector3(0, terrace / 2f - 0.25f, 0), new Vector3(w + 0.5f, terrace + 0.5f, w + 0.5f), Masonry, true, 3f);
        Box("TowerMoulding", t, new Vector3(0, terrace + 0.08f, 0), new Vector3(w + 0.8f, 0.2f, w + 0.8f), Coping, false, 2f);
        Box("TowerFloor", t, new Vector3(0, terrace + 0.1f, 0), new Vector3(inner, 0.2f, inner), Paving, true, 3f);
        // the shaft: four walls from the terrace to the look-out; the door in the north wall, slits in the others
        float y0 = terrace + 0.2f, y1 = look;
        for (int s = 0; s < 4; s++)
        {
            var n = Quaternion.Euler(0, s * 90f, 0) * Vector3.forward;       // 0 north, 1 east, 2 south, 3 west
            var u = Quaternion.Euler(0, s * 90f, 0) * Vector3.right;
            var mid = n * (w / 2f - wall / 2f);
            float len = s % 2 == 0 ? w : w - 2f * wall;
            void Piece(float a, float b, float lo, float hi)
            {
                if (b - a < 0.02f || hi - lo < 0.02f) return;
                Box("TowerWall", t, mid + u * ((a + b) / 2f) + Vector3.up * ((lo + hi) / 2f), SideSize(u, b - a, hi - lo, wall), Stone, true, 2.6f);
            }
            if (s == 0)
            {
                // the doorway from the terrace
                Piece(-len / 2f, -0.6f, y0, y1); Piece(0.6f, len / 2f, y0, y1); Piece(-0.6f, 0.6f, y0 + 2.2f, y1);
                Box("TowerLintel", t, mid + n * (wall / 2f + 0.03f) + Vector3.up * (y0 + 2.35f), SideSize(u, 1.8f, 0.3f, 0.1f), Coping, false, 2f);
            }
            else
            {
                // slit windows one above another
                float at = y0;
                for (float sy = y0 + 2.2f; sy + 1.2f < y1 - 0.6f; sy += 3.3f)
                {
                    Piece(-len / 2f, len / 2f, at, sy);
                    Piece(-len / 2f, -0.22f, sy, sy + 1.2f); Piece(0.22f, len / 2f, sy, sy + 1.2f);
                    at = sy + 1.2f;
                }
                Piece(-len / 2f, len / 2f, at, y1);
            }
            // stone bands round the shaft
            foreach (float by in new[] { y0 + 3.6f, y0 + 7.2f })
                Box("TowerBand", t, n * (w / 2f + 0.04f) + Vector3.up * by, SideSize(u, w + 0.2f, 0.22f, 0.1f), Coping, false, 2f);
        }
        // the steps: flights round the walls, a landing in each corner, up to the look-out's floor
        const float sw = 1.0f;
        float run = inner - 2f * sw, rise = y1 - y0;
        const int perFlight = 6;
        int flights = Mathf.CeilToInt(rise / (0.18f * (perFlight + 1)));
        float stepH = rise / (flights * (perFlight + 1));
        float y = y0;
        var stone = Masonry;
        for (int f = 0; f < flights; f++)
        {
            // clockwise seen from above, the first flight along the east wall (the door is in the north one)
            var along = Quaternion.Euler(0, ((f + 1) % 4) * 90f + 90f, 0) * Vector3.forward;
            var outward = Quaternion.Euler(0, ((f + 1) % 4) * 90f, 0) * Vector3.forward;
            var start = outward * (inner / 2f - sw / 2f) - along * (run / 2f);
            for (int k = 0; k < perFlight; k++)
            {
                y += stepH;
                var p = start + along * (run * (k + 0.5f) / perFlight);
                Box("TowerStep", t, p + Vector3.up * (y - 0.1f), SideSize(along, run / perFlight + 0.02f, 0.2f, sw), stone, true, 2f);
            }
            y += stepH;
            var corner = outward * (inner / 2f - sw / 2f) + along * (inner / 2f - sw / 2f);
            Box("TowerLanding", t, corner + Vector3.up * (y - 0.1f), new Vector3(sw, 0.2f, sw), stone, true, 2f);
            // a rail on the open side of the flight
            Beam("TowerRail", t, t.TransformPoint(start - outward * (sw / 2f - 0.04f) + Vector3.up * (y - stepH * perFlight + 0.9f)), t.TransformPoint(start + along * run - outward * (sw / 2f - 0.04f) + Vector3.up * (y - stepH + 0.9f)), 0.05f, Iron, true);
        }
        // the look-out: its floor (open where the steps come up), corner piers, a parapet, the beam over
        int last = flights % 4;
        var lastOut = Quaternion.Euler(0, last * 90f, 0) * Vector3.forward;
        var lastAlong = Quaternion.Euler(0, last * 90f + 90f, 0) * Vector3.forward;
        // the floor in two parts: all but the strip along the last flight's wall
        Box("LookoutFloor", t, -lastOut * (sw / 2f) + Vector3.up * (look - 0.1f), SideSize(lastAlong, inner, 0.2f, inner - sw), Paving, true, 3f);
        for (int s = 0; s < 4; s++)
        {
            var n = Quaternion.Euler(0, s * 90f, 0) * Vector3.forward;
            var u = Quaternion.Euler(0, s * 90f, 0) * Vector3.right;
            Box("LookoutLedge", t, n * (w / 2f + 0.2f) + Vector3.up * (look - 0.05f), SideSize(u, w + 0.8f, 0.24f, 0.4f), Coping, false, 2f);
            Box("LookoutParapet", t, n * (w / 2f - wall / 2f) + Vector3.up * (look + 0.5f), SideSize(u, w, 1.0f, wall), Stone, true, 2.6f);
            Box("LookoutCope", t, n * (w / 2f - wall / 2f) + Vector3.up * (look + 1.03f), SideSize(u, w + 0.1f, 0.08f, wall + 0.12f), Coping, false, 2f);
            Box("LookoutPier", t, n * (w / 2f - wall / 2f) + u * (w / 2f - wall / 2f) + Vector3.up * (look + lookH / 2f), new Vector3(wall + 0.1f, lookH, wall + 0.1f), Stone, true, 2.6f);
            Box("LookoutBeam", t, n * (w / 2f - wall / 2f) + Vector3.up * (look + lookH - 0.25f), SideSize(u, w, 0.5f, wall), Stone, true, 2.6f);
        }
        // the clock: a block with a face on each side, lit at night
        float cy = look + lookH;
        Box("ClockSlab", t, new Vector3(0, cy + 0.1f, 0), new Vector3(w + 0.7f, 0.22f, w + 0.7f), Coping, true, 2f);
        Box("ClockBlock", t, new Vector3(0, cy + 1.75f, 0), new Vector3(w - 0.3f, 3.1f, w - 0.3f), Plaster, true, 2.5f);
        for (int s = 0; s < 4; s++)
        {
            var rot = Quaternion.Euler(0, s * 90f, 0);
            var n = rot * Vector3.forward;
            var face = t.TransformPoint(n * ((w - 0.3f) / 2f + 0.02f) + Vector3.up * (cy + 1.8f));
            var ring = Cylinder("ClockRing", t, face, 1.25f, 0.06f, Iron, false, 24);
            ring.transform.rotation = rot * Quaternion.Euler(90f, 0, 0);
            var dial = Cylinder("ClockDial", t, face + n * 0.03f, 1.12f, 0.05f, GlobeDay, false, 24);
            dial.transform.rotation = rot * Quaternion.Euler(90f, 0, 0);
            var lit = Cylinder("ClockDialLit", t, face + n * 0.035f, 1.12f, 0.05f, GlobeNight, false, 24);
            lit.transform.rotation = rot * Quaternion.Euler(90f, 0, 0);
            var lr = lit.GetComponent<Renderer>();
            lr.enabled = false; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            night.Add(lr);
            // the hours' marks and the two hands (ten past ten)
            for (int m = 0; m < 12; m++)
            {
                var mark = MeshGen.Object("ClockMark", MeshGen.Box(new Vector3(0.07f, m % 3 == 0 ? 0.26f : 0.14f, 0.02f), 1f), Iron, t, face + n * 0.1f + rot * (Quaternion.Euler(0, 0, m * 30f) * Vector3.up * 0.93f));
                mark.transform.rotation = rot * Quaternion.Euler(0, 0, m * 30f);
            }
            foreach (var (angle, length, thick) in new[] { (55f, 0.62f, 0.09f), (-60f, 0.9f, 0.06f) })
            {
                var hand = MeshGen.Object("ClockHand", MeshGen.Box(new Vector3(thick, length, 0.02f), 1f), Iron, t, face + n * 0.12f + rot * (Quaternion.Euler(0, 0, angle) * Vector3.up * (length / 2f - 0.08f)));
                hand.transform.rotation = rot * Quaternion.Euler(0, 0, angle);
            }
        }
        Box("ClockCornice", t, new Vector3(0, cy + 3.38f, 0), new Vector3(w + 0.5f, 0.2f, w + 0.5f), Coping, false, 2f);
        var roofMesh = HipRoof(w + 0.9f, w + 0.9f, 4.2f, 2.2f);
        var roof = MeshGen.Object("ClockRoof", roofMesh, Roof, t, t.TransformPoint(new Vector3(0, cy + 3.48f, 0)));
        Cylinder("TowerFinial", t, t.TransformPoint(new Vector3(0, cy + 7.5f, 0)), 0.05f, 1.6f, Iron, false, 6);
        Sphere("TowerBall", t, t.TransformPoint(new Vector3(0, cy + 7.75f, 0)), 0.18f, Iron);
    }

    /// <summary>A market stall on the square: posts, a counter with crates of fruit, a striped cloth roof.</summary>
    private static void Stall(Vector3 p, float yaw, int colour)
    {
        var t = new GameObject("Stall").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        foreach (var (x, z) in new[] { (-1.4f, -0.9f), (1.4f, -0.9f), (-1.4f, 0.9f), (1.4f, 0.9f) })
            Box("StallPost", t, new Vector3(x, 1.15f, z), new Vector3(0.09f, 2.3f, 0.09f), Wood, false, 1.5f);
        Box("StallCounter", t, new Vector3(0, 0.45f, 0.5f), new Vector3(2.9f, 0.9f, 0.9f), GreyWood, true, 1.5f);
        var cloth = AssetSetup.Solid("K_Awning" + colour % 3, new[] { new Color(0.62f, 0.14f, 0.1f), new Color(0.14f, 0.36f, 0.22f), new Color(0.75f, 0.6f, 0.3f) }[colour % 3], 0.1f, 0f);
        Tilted("StallRoof", t, new Vector3(0, 2.3f, 0.1f), new Vector3(3.3f, 0.04f, 2.5f), 9f, cloth, false, 1f);
        for (int i = 0; i < 3; i++)
        {
            Box("StallCrate", t, new Vector3(-0.95f + i * 0.95f, 0.99f, 0.5f), new Vector3(0.8f, 0.18f, 0.6f), Wood, false, 1f);
            Box("StallFruit", t, new Vector3(-0.95f + i * 0.95f, 1.1f, 0.5f), new Vector3(0.7f, 0.1f, 0.5f), i == 1 ? Leaves : i == 0 ? Flowers : Terracotta, false, 1f);
        }
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

    public static MapLayout Build()
    {
        rng = new System.Random(46);
        ladderLinks.Clear();
        night.Clear(); mats.Clear(); roofMeshes.Clear();
        rockMeshes = null; sphereMesh = null; millTower = null; millCap = null;
        root = new GameObject("Map_HillVillage").transform;
        nature = new GameObject("Nature").transform;
        nature.SetParent(root, false);
        EnsureTrees();
        Plan();
        BuildGround();

        // the terrace walls between the levels, and the stairs against them
        var walls = new GameObject("TerraceWalls").transform;
        walls.SetParent(root, false);
        TerraceWall(walls, true, 16f, -132f, 24f, -1f, (-7.5f, -4.5f), (-61.2f, -58.8f), (-101.2f, -98.8f), (11.9f, 14.1f));
        TerraceWall(walls, false, 24f, 16f, 42f, 1f);
        TerraceWall(walls, true, -18f, -132f, 26f, -1f, (-7.5f, -4.5f), (-60.2f, -57.8f), (-100.2f, -97.8f));
        TerraceWall(walls, false, 26f, -42f, -18f, -1f);
        // up from the square to the mosque: two flights against the wall meeting at a landing on its axis
        Steps(walls, new Vector3(-15.0f, 0f, 13.85f), Vector3.right, 4.5f, 2.2f, Vector3.back);
        Steps(walls, new Vector3(3.0f, 0f, 13.85f), Vector3.left, 4.5f, 2.2f, Vector3.back);
        MeshGen.TexBox("Landing", new Vector3(-6f, 2.1f, 13.85f), new Vector3(3f, 4.8f, 2.2f), Masonry, 3f, walls);
        // down from the square to the lower quarter, the same way
        Steps(walls, new Vector3(-11.7f, -2.5f, -20.15f), Vector3.right, 2.5f, 2.2f, Vector3.back);
        Steps(walls, new Vector3(-0.3f, -2.5f, -20.15f), Vector3.left, 2.5f, 2.2f, Vector3.back);
        MeshGen.TexBox("Landing", new Vector3(-6f, -1.4f, -20.15f), new Vector3(3f, 2.8f, 2.2f), Masonry, 3f, walls);
        // the west quarter's stairs up to the terrace and down to the lower quarter, twice along it
        foreach (float sx in new[] { -60f, -100f })
        {
            float westUp = GroundHeight(sx - 6f, 12f), westDown = GroundHeight(sx + 1f, -15f);
            Steps(walls, new Vector3(sx - 6.2f, westUp, 13.85f), Vector3.right, 4.5f - westUp, 2.0f, Vector3.back);
            MeshGen.TexBox("Landing", new Vector3(sx, (4.5f + westUp) / 2f - 0.2f, 13.85f), new Vector3(2.4f, 4.5f - westUp + 0.4f, 2.2f), Masonry, 3f, walls);
            int downCount = Mathf.CeilToInt((westDown + 2.5f) / 0.18f);
            Steps(walls, new Vector3(sx + 1.2f - downCount * 0.3f - 1.2f, -2.5f, -20.15f), Vector3.right, westDown + 2.5f, 2.0f, Vector3.back);
            MeshGen.TexBox("Landing", new Vector3(sx + 1f, (westDown - 2.5f) / 2f - 0.2f, -20.15f), new Vector3(2.4f, westDown + 2.9f, 2.2f), Masonry, 3f, walls);
        }

        // the square: paved, its platform, planters with palms and the plane tree, the fountain, the coffee house's terrace
        MeshGen.TexBox("SquarePaving", new Vector3(0f, -0.1f, -1f), new Vector3(52f, 0.26f, 31.9f), Paving, 4f, root);
        Platform(new Vector3(-6f, 0.03f, -5f));
        Planter(new Vector3(-21f, 0.03f, -12f), palms[0], 0.85f);
        Planter(new Vector3(21f, 0.03f, -12f), palms[1], 0.75f);
        Planter(new Vector3(-21f, 0.03f, 8.5f), palms[1], 0.8f);
        Planter(new Vector3(4f, 0.03f, 8.5f), broadleaf, 1.8f, 1.7f);
        Fountain(new Vector3(18f, 0.03f, 14.95f), 180f);
        Pergola(new Vector3(20f, 0.03f, 3f), 8f, 10f);
        foreach (var (x, z, yaw) in new[] { (-21f, 13.4f, 180f), (8f, 13.4f, 180f), (-24.5f, -2f, 90f), (-24.5f, -9f, 90f), (-20f, -16f, 0f), (10f, -16f, 0f), (17f, -16f, 0f), (-80f, 16.3f, 180f), (-54f, 16.3f, 180f), (-30f, 16.3f, 180f) })
            Bench(new Vector3(x, GroundHeight(x, z) + (Mathf.Abs(z - 16.3f) < 0.1f ? 0.03f : 0.03f), z), yaw);

        // the Grand Hall and its walled garden (the gate towards the stairs); the clock tower against the
        // terrace wall, entered from the terrace; market stalls on the square
        Hall(new Vector3(-6f, 4.5f, 37f));
        ClockTower(new Vector3(13f, 0f, 12.35f), 4.5f);
        Stall(new Vector3(-17f, 0.03f, 1.5f), 90f, 0);
        Stall(new Vector3(-17f, 0.03f, -3.5f), 90f, 1);
        Stall(new Vector3(13f, 0.03f, -11f), 180f, 2);
        foreach (var (a, b) in new[] { (new Vector2(-17f, 21.5f), new Vector2(-8f, 21.5f)), (new Vector2(-4f, 21.5f), new Vector2(6f, 21.5f)), (new Vector2(6f, 21.5f), new Vector2(6f, 47.5f)), (new Vector2(6f, 47.5f), new Vector2(-17f, 47.5f)), (new Vector2(-17f, 47.5f), new Vector2(-17f, 21.5f)) })
        {
            var p = (a + b) / 2f; float len = Vector2.Distance(a, b);
            float yaw = Mathf.Abs(a.y - b.y) < 0.1f ? 90f : 0f;
            MeshGen.TexBox("CourtyardWall", new Vector3(p.x, 4.5f + 0.5f, p.y), new Vector3(0.5f, 1.4f, len + 0.5f), Masonry, 3f, root, yaw);
            MeshGen.TexBox("CourtyardCoping", new Vector3(p.x, 4.5f + 1.24f, p.y), new Vector3(0.6f, 0.1f, len + 0.6f), Coping, 2f, root, yaw, false);
        }
        foreach (var x in new[] { -8f, -4f })
            Cylinder("GatePier", root, new Vector3(x, 4.5f, 21.5f), 0.35f, 2.2f, Masonry, true, 8);
        TreeAt(broadleaf, new Vector3(-13f, 4.5f, 44.5f), 1.7f);
        TreeAt(broadleaf, new Vector3(2.5f, 4.5f, 44f), 1.6f);

        // the houses, the compounds' walls and gates, the garden walls along the lanes
        var hp = new GameObject("Houses").transform;
        hp.SetParent(root, false);
        foreach (var hs in houses) { BuildHouse(hs, hp); Physics.SyncTransforms(); }
        foreach (var c in compounds) CompoundWalls(c);
        foreach (var g in gardens) GardenWall(g);
        foreach (var pl in plots) PlotWalls(pl);
        Physics.SyncTransforms();
        // the windmills on the west hill turned to the village, the cemetery and the dusty square before it
        for (int i = 0; i < windmills.Count; i++) Windmill(windmills[i], R(78f, 102f), i);
        Cemetery(cemetery);
        DustySquare();
        // pavements along the main street and the road
        foreach (var lane in lanes.Where(l => l.Kerb)) { Pavement(lane, 1); Pavement(lane, 2); }
        Physics.SyncTransforms();

        // the electricity poles and their wires along the main lanes
        var poleSpots = new List<Vector2>();
        foreach (var lane in lanes.Where(l => l.Main))
        {
            Vector3[] prev = null;
            Vector2 prevAt = default;
            foreach (var q in AlongLane(lane, 26f, 2, 0.3f, 5f))
            {
                var (_, t) = lane.At(Project(lane, q));
                var ends = Pole(new Vector3(q.x, Y(q.x, q.y), q.y), YawOf(t));
                bool clear = Enumerable.Range(1, 9).All(i => LaneDistance(Vector2.Lerp(prevAt, q, i / 10f)) < 0.6f);
                if (prev != null && (q - prevAt).magnitude < 34f && clear)
                    for (int i = 0; i < 3; i++) Wire(prev[i], ends[i], R(0.35f, 0.7f));
                prev = ends; prevAt = q;
                poleSpots.Add(q);
            }
        }
        // street lamps: the square's, then along the lanes on alternate sides
        foreach (var (x, z, two) in new[] { (-24.5f, -15.5f, true), (24.5f, -15.5f, true), (-24.5f, 12.8f, true), (24.5f, 12.8f, false), (-14.5f, -10.5f, true), (2.5f, -10.5f, true) })
            GlobeLamp(new Vector3(x, Y(x, z), z), two);
        var lampSpots = new List<Vector2>();
        foreach (var lane in lanes.Where(l => l.Width >= 3.5f))
            foreach (var q in AlongLane(lane, lane.Main ? 24f : 28f, 0, 0.35f, 9f))
            {
                if (poleSpots.Any(p => (p - q).sqrMagnitude < 9f) || lampSpots.Any(p => (p - q).sqrMagnitude < 100f) || Mathf.Abs(q.x) < 27f && q.y > -19f && q.y < 17f) continue;
                GlobeLamp(new Vector3(q.x, Y(q.x, q.y), q.y), false);
                lampSpots.Add(q);
            }
        StreetProps();

        Vegetation();
        Farmland();

        // ammunition crates: in the square, along the lanes of every quarter, in the groves and on the hill
        var supply = new GameObject("AmmoCrates").transform;
        supply.SetParent(root, false);
        var crates = new List<Vector2> { new Vector2(-20f, -14.5f) };
        foreach (var (name, frac, side) in new[] { ("TerraceLane", 0.3f, 1), ("UpperLane", 0.55f, 2), ("EastStreet", 0.65f, 2), ("Road", 0.4f, 1), ("WestLane", 0.45f, 1), ("UnderWall", 0.6f, 1), ("MiddleLane", 0.2f, 2), ("StreetDown", 0.75f, 1),
                                                     ("SouthLane", 0.5f, 1), ("HillRoad", 0.5f, 1), ("HighLane", 0.4f, 2), ("SlopeLane", 0.5f, 2), ("WestLane", 0.9f, 2), ("UnderWall", 0.9f, 1) })
        {
            var lane = LaneNamed(name);
            crates.AddRange(AlongLane(lane, 999f, side, 0.7f, lane.Length * frac));
        }
        foreach (var q in crates) Obstacles.AmmoCrate(supply, new Vector3(q.x, Y(q.x, q.y), q.y), R(0f, 360f));
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
        Vector3 Beside(string lane, float frac, int side)
        {
            var l = LaneNamed(lane);
            var q = AlongLane(l, 999f, side, 0.45f, l.Length * frac).DefaultIfEmpty(l.At(l.Length * frac).p).First();
            return new Vector3(q.x, 0f, q.y);
        }
        Vector3 On(string lane, float frac) { var l = LaneNamed(lane); var q = l.At(l.Length * frac).p; return new Vector3(q.x, 0f, q.y); }
        foreach (var (near, readers, ar, en) in new[]
        {
            (new Vector3(-17f, 0f, -14.5f), new Vector3(-6f, 0f, -5f), "الساحة", "THE SQUARE"),
            (new Vector3(-12.5f, 4.5f, 20.9f), new Vector3(-6f, 4.5f, 30f), "القاعة الكبرى", "THE GRAND HALL"),
            (new Vector3(16.6f, 4.5f, 18.4f), new Vector3(13f, 4.5f, 19f), "برج الساعة", "THE CLOCK TOWER"),
            (Beside("UnderWall", 0.55f, 1), On("UnderWall", 0.55f), "الحي السفلي", "THE LOWER QUARTER"),
            (Beside("WestLane", 0.3f, 2), On("WestLane", 0.3f), "الحي الغربي", "THE WEST QUARTER"),
            (new Vector3(14f, 0f, -77f), new Vector3(14f, 0f, -69f), "بساتين الزيتون", "THE OLIVE GROVES"),
            (Beside("HillRoad", 0.18f, 2), On("HillRoad", 0.18f), "تل الصنوبر", "THE PINE HILL"),
            (Beside("Road", 0.22f, 1), On("Road", 0.22f), "الطريق الشرقي", "THE EAST ROAD"),
            (Beside("WestLane", 0.985f, 2), On("WestLane", 0.97f), "طواحين الهواء", "THE WINDMILLS"),
            (new Vector3(59.5f, 0f, -12.5f), new Vector3(64f, 0f, -6f), "المقبرة", "THE CEMETERY"),
        })
            Signboards.Stand(boards, new Vector3(near.x, 0, near.z), readers, ar, en);
        Navigation(layout);
        root.gameObject.AddComponent<MapScene>().NightLights = MapOptimizer.Merge(root, "HillVillage", null, night, 60f).ToArray();
        Debug.Log($"[HillVillage] built: {houses.Count} houses ({string.Join(", ", houses.GroupBy(h => h.Kind).Select(g => g.Key + " " + g.Count()))}), {compounds.Count} compounds, {gardens.Count} gardens, {plots.Count} plots, {ladderLinks.Count} ladders, {root.GetComponentsInChildren<Renderer>().Length} renderers, {layout.Waypoints.Length} waypoints, {layout.Spawns.Length} spawns");
        return layout;
    }

    /// <summary>The dusty square before the cemetery: a row of broad trees along its wall, benches, barrels.</summary>
    private static void DustySquare()
    {
        for (int i = 0; i < 5; i++)
        {
            var p = new Vector3(56.5f + i * 3.9f + R(-0.4f, 0.4f), 0, -26f + R(-0.4f, 0.4f));
            p.y = Y(p.x, p.z);
            TreeAt(broadleaf, p, R(1.3f, 1.7f));
        }
        Bench(new Vector3(58.5f, Y(58.5f, -23.2f), -23.2f), 180f);
        Bench(new Vector3(70f, Y(70f, -23.2f), -23.2f), 180f);
        for (int i = 0; i < 3; i++) Barrel(new Vector3(56.2f + i * 0.62f, Y(56.2f + i * 0.62f, -14f), -14f), i != 1);
    }

    /// <summary>Is there room for a small thing here (a sphere above the ground clear of walls)?</summary>
    private static bool Room(Vector2 q, float radius)
    {
        var p = new Vector3(q.x, Y(q.x, q.y), q.y);
        return !Physics.CheckSphere(p + Vector3.up * (radius + 0.15f), radius, World.EnvMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// The things by the houses along the lanes: barrels, firewood stacked against a side wall, pots of
    /// flowers by a door, a bench against a front, pallets by the sheds and on the road; shipping
    /// containers at the edges of the village.
    /// </summary>
    private static void StreetProps()
    {
        Physics.SyncTransforms();
        foreach (var h in houses)
        {
            if (h.Lane < 0 || h.Side == 0 || h.Kind == Kind.Wing || h.Ruin) continue;
            double r = rng.NextDouble();
            float sx = Chance(0.5) ? -1f : 1f;
            if (r < 0.13) { }   // the front of the house kept clear
            else if (r < 0.21)
            {
                int n = rng.Next(1, 4);
                for (int i = 0; i < n; i++)
                {
                    var q = h.ToWorld(sx * (h.W / 2f + 0.4f), h.D / 2f - 0.6f - i * 0.65f);
                    if (Room(q, 0.32f)) Barrel(new Vector3(q.x, Y(q.x, q.y), q.y), Chance(0.6));
                }
            }
            else if (r < 0.3 && (h.Kind == Kind.Classic || h.Kind == Kind.Cottage || h.Kind == Kind.Tower) && h.D > 4.5f)
            {
                var q = h.ToWorld(sx * (h.W / 2f + 0.7f), 0f);
                if (Room(q, 0.4f))
                {
                    var spot = h.ToWorld(sx * (h.W / 2f + 0.02f), 0f);
                    var t = new GameObject("Woodpile").transform;
                    t.SetParent(root, false);
                    t.SetPositionAndRotation(new Vector3(spot.x, Y(spot.x, spot.y), spot.y), Quaternion.Euler(0, h.Yaw + sx * 90f, 0));
                    Firewood(t, new Vector3(0, 0, 0.31f), Mathf.Min(2.4f, h.D - 1.6f));
                }
            }
            else if (r < 0.37 && (QuarterOf(h) == RoadQ || QuarterOf(h) == FringeQ || h.Kind == Kind.Shed))
            {
                var q = h.ToWorld(sx * (h.W / 2f + 1f), h.D / 2f - 1f);
                if (Room(q, 0.7f)) Pallets(new Vector3(q.x, Y(q.x, q.y), q.y), h.Yaw + R(-20f, 20f));
            }
            else if (r < 0.47 && h.Kind != Kind.Shed && h.Kind != Kind.Shop && !h.OutsideStair)
            {
                foreach (float px in new[] { -0.95f, 0.95f })
                {
                    var q = h.ToWorld(px, h.D / 2f + 0.3f);
                    var p = new Vector3(q.x, Y(q.x, q.y), q.y);
                    Cylinder("DoorPot", root, p, 0.2f, 0.42f, Terracotta, false, 10);
                    var leaves = Sphere("DoorPlant", root, p + Vector3.up * 0.62f, 0.3f, Chance(0.4) ? Flowers : Leaves);
                    leaves.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);
                }
            }
            else if (r < 0.52 && h.Kind != Kind.Shed)
            {
                var q = h.ToWorld(sx * (h.W / 2f - 1.6f), h.D / 2f + 0.45f);
                if (Room(q, 0.45f)) Bench(new Vector3(q.x, Y(q.x, q.y), q.y), h.Yaw + 180f);
            }
        }
        // shipping containers at the edges of the village, red and blue, as in the pictures
        foreach (var (x, z, yaw, red) in new[] { (51f, -75.5f, 20f, true), (54f, -84.5f, 105f, false), (97f, -13f, 90f, false), (-46f, -61f, 5f, true), (92f, 22f, 75f, true) })
        {
            var c = new Vector3(x, Y(x, z), z);
            if (!Physics.CheckBox(c + Vector3.up * 1.4f, new Vector3(1.3f, 1.2f, 3.1f), Quaternion.Euler(0, yaw, 0), World.EnvMask, QueryTriggerInteraction.Ignore))
                ShippingContainer(c - Vector3.up * 0.05f, yaw, red);
        }
    }

    /// <summary>
    /// Pines over the hill and round the edges, olive groves in rows on the slopes below the village
    /// and on the east slope, trees by the houses, rocks on the steep ground, and dry grass everywhere
    /// the ground is not paved or trodden.
    /// </summary>
    private static void Vegetation()
    {
        Physics.SyncTransforms();
        int pinesPlaced = 0;
        for (int i = 0; i < 5000 && pinesPlaced < 460; i++)
        {
            float x = R(-Edge + 4f, Edge - 4f), z = R(-Edge + 4f, Edge - 4f);
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float want = z > 66f ? Mathf.InverseLerp(66f, 96f, z) * 0.9f : edge > Half - 12f ? Mathf.InverseLerp(Half - 12f, Half + 10f, edge) * 0.85f : 0f;
            if (!Chance(want) || Built(x, z) > 0.25f || LineDistance(tracks, x, z) < 2.5f || LaneDistance(new Vector2(x, z)) < 3f) continue;
            var p = new Vector3(x, Y(x, z), z);
            if (!Free(p, 2.2f)) continue;
            TreeAt(pines[rng.Next(pines.Length)], p, R(0.7f, 1.05f), Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > Edge);
            Physics.SyncTransforms();
            pinesPlaced++;
        }
        int olivesPlaced = 0;
        // the groves below the village: rows across the slope
        for (float z = -80f; z > -Half - 2f; z -= 7f)
            for (float x = -122f; x < 122f; x += 7.5f)
            {
                float px = x + R(-1.2f, 1.2f) + (Mathf.RoundToInt(z) % 2 == 0 ? 3.7f : 0f), pz = z + R(-1f, 1f);
                if (Chance(0.18) || Built(px, pz) > 0.3f || LineDistance(tracks, px, pz) < 3f) continue;
                var p = new Vector3(px, Y(px, pz), pz);
                if (!Free(p, 1.8f)) continue;
                TreeAt(olives[rng.Next(olives.Length)], p, R(0.85f, 1.2f), true);
                olivesPlaced++;
            }
        // and on the east slope above the road
        for (float z = 18f; z < 54f; z += 8f)
            for (float x = 48f; x < 122f; x += 8f)
            {
                float px = x + R(-1.5f, 1.5f), pz = z + R(-1.5f, 1.5f);
                if (Chance(0.35) || Built(px, pz) > 0.3f) continue;
                var p = new Vector3(px, Y(px, pz), pz);
                if (!Free(p, 1.8f)) continue;
                TreeAt(olives[rng.Next(olives.Length)], p, R(0.8f, 1.15f), true);
                olivesPlaced++;
            }
        Physics.SyncTransforms();
        // a few broad trees and palms near the houses at the edges of the village
        for (int i = 0, placed = 0; i < 400 && placed < 24; i++)
        {
            var hs = houses[rng.Next(houses.Count)];
            var p = new Vector3(hs.X + R(-9f, 9f), 0, hs.Z + R(-9f, 9f));
            if (LaneDistance(new Vector2(p.x, p.z)) < 1.5f || Mathf.Abs(p.x) < 27f && p.z > -17f && p.z < 15f) continue;
            p.y = Y(p.x, p.z);
            if (!Free(p, 1.4f)) continue;
            TreeAt(Chance(0.3) ? palms[rng.Next(palms.Length)] : Chance(0.5) ? broadleaf : olives[rng.Next(olives.Length)], p, R(0.8f, 1.3f));
            Physics.SyncTransforms();
            placed++;
        }
        // outcrops of rock on the slopes and in the forest (as on the hills in the picture): a big block
        // with smaller ones round it, good cover out of the village
        for (int i = 0, placed = 0; i < 1400 && placed < 60; i++)
        {
            float x = R(-Edge + 4f, Edge - 4f), z = R(-Edge + 4f, Edge - 4f);
            if (Built(x, z) > 0.2f || LaneDistance(new Vector2(x, z)) < 3f || LineDistance(tracks, x, z) < 2f) continue;
            float slope = Mathf.Abs(GroundHeight(x + 1f, z) - GroundHeight(x - 1f, z)) + Mathf.Abs(GroundHeight(x, z + 1f) - GroundHeight(x, z - 1f));
            if (slope < 0.45f && !(z > 75f && Chance(0.3))) continue;
            var p = new Vector3(x, Y(x, z), z);
            if (!Free(p, 2f)) continue;
            Rock(p, R(1.4f, 3.2f));
            int around = rng.Next(1, 5);
            for (int k = 0; k < around; k++)
            {
                var q = p + Quaternion.Euler(0, R(0f, 360f), 0) * Vector3.forward * R(1.6f, 3.5f);
                q.y = Y(q.x, q.z);
                Rock(q, R(0.5f, 1.4f));
            }
            Physics.SyncTransforms();
            placed++;
        }
        // dry grass: tufts in the open, fewer in the forest, some along the foot of the walls in the village
        for (int i = 0, placed = 0; i < 9000 && placed < 1300; i++)
        {
            float x = R(-Half - 25f, Half + 25f), z = R(-Half - 25f, Half + 25f);
            if (LaneDistance(new Vector2(x, z)) < 0.6f || (Mathf.Abs(x) < 26.5f && z > -17.5f && z < 15.5f)) continue;
            float built = Built(x, z);
            if (built > 0.6f && !Chance(0.12)) continue;
            if (z > 75f && !Chance(0.35)) continue;
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
        foreach (float z0 in new[] { -82f, -95f, -108f })
            for (float x = -120f; x < 120f;)
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
        Physics.SyncTransforms();
        foreach (var (x, z) in new[] { (84f, -46f), (87.5f, -46.5f), (91f, -45.5f), (-18f, -102f), (-14.5f, -102.5f), (-11f, -102f), (-42f, -86f) })
        {
            float gy = Y(x, z);
            if (Physics.CheckSphere(new Vector3(x, gy + 0.9f, z), 0.8f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
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
        // up the ladders: from the ground before each to the roof it reaches
        int Closest(Vector3 at, float reach, float rise)
        {
            int best = -1; float bestD = reach * reach;
            for (int i = 0; i < points.Count; i++)
            {
                var d = points[i] - at;
                if (Mathf.Abs(d.y) > rise) continue;
                d.y = 0;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = i; }
            }
            return best;
        }
        int climbs = 0;
        foreach (var (bottom, topAt) in ladderLinks)
        {
            int a = Closest(bottom, 3.2f, 1.2f), b = Closest(topAt, 2.6f, 0.8f);
            if (a >= 0 && b >= 0 && a != b) { links.Add(new Vector2Int(a, b)); climbs++; }
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
        var spots = new List<Vector2> { new Vector2(-18f, -12f) };
        foreach (var (name, frac) in new[] { ("TerraceLane", 0.12f), ("UpperLane", 0.6f), ("EastStreet", 0.85f), ("Road", 0.72f), ("StreetDown", 0.8f), ("MiddleLane", 0.3f), ("MiddleLane", 0.85f), ("WestLane", 0.85f), ("MansionLane", 0.5f),
                                              ("HillRoad", 0.55f), ("HighLane", 0.3f), ("SouthLane", 0.6f), ("SlopeLane", 0.45f) })
        {
            var lane = LaneNamed(name);
            spots.Add(lane.At(lane.Length * frac).p);
        }
        foreach (var q in spots)
        {
            Vector3 best = new Vector3(q.x, Y(q.x, q.y), q.y);
            bool ok = false;
            foreach (var (dx, dz) in new[] { (0f, 0f), (1.5f, 0f), (-1.5f, 0f), (0f, 1.5f), (0f, -1.5f), (3f, 0f), (-3f, 0f), (0f, 3f), (0f, -3f) })
            {
                var p = new Vector3(q.x + dx, Y(q.x + dx, q.y + dz), q.y + dz);
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.4f, mask, QueryTriggerInteraction.Ignore)) continue;
                best = p; ok = true;
                break;
            }
            if (!ok) Debug.LogWarning($"[HillVillage] spawn point {q.x},{q.y} is blocked");
            spawns.Add(best);
        }
        layout.Spawns = spawns.ToArray();
        Debug.Log($"[HillVillage] navigation: {kept.Count} waypoints of {points.Count}, {layout.Links.Length} links ({climbs} up ladders)");
    }
}
