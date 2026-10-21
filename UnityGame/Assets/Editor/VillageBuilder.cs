using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The "Village" map: an Afghan farming village on rolling green land. Mud-walled compounds (qala)
/// with gates and corner towers around one- and two-storey mud houses (flat roofs on wooden beams,
/// outside stairs to the roof), a small bazaar on a raised platform around a big plane tree, a dry
/// river bed (khwar) with a wooden footbridge and a ford, terraced wheat fields and orchards on the
/// hills, and an old round watchtower (burj) on the highest hill. Ground is a height field mesh painted
/// with grass, dirt, gravel and rock (ambientCG, CC0).
/// </summary>
public static class VillageBuilder
{
    public const float Half = 86f;              // playable half size
    private const float Size = 340f;

    private static Transform root;
    private static System.Random rng;
    private static readonly List<Rect> flats = new List<Rect>();

    private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ materials

    // kahgel: mud and straw plaster, light beige brown (the hand smoothed clay texture)
    private static Material Mud => PortBuilder.Pbr("V_Mud", "Clay007", new Color(0.88f, 0.78f, 0.64f), 0.12f);
    private static Material MudPlaster => PortBuilder.Pbr("V_MudPlaster", "Clay007", new Color(0.97f, 0.9f, 0.78f), 0.1f);
    private static Material MudDark => PortBuilder.Pbr("V_MudDark", "Clay007", new Color(0.68f, 0.58f, 0.47f), 0.1f);
    private static Material Dirt => PortBuilder.Pbr("V_Dirt", "Ground104", Color.white, 0.1f);
    private static Material Wood => PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f);
    private static Material OldWood => PortBuilder.Pbr("V_OldWood", "Planks037A", new Color(0.72f, 0.62f, 0.5f), 0.3f);
    private static Material Hay => PortBuilder.Pbr("V_Hay", "Ground104", new Color(1f, 0.86f, 0.5f), 0.05f);
    private static Material Stone => PortBuilder.Pbr("V_Stone", "Rock055", Color.white, 0.2f);

    // ------------------------------------------------------------------ ground

    /// <summary>River bed centre line (z) for a given x.</summary>
    private static float RiverZ(float x) => -47f + 9f * Mathf.Sin(x / 37f) + 4f * Mathf.Sin(x / 13f + 1f);

    private static float Gauss(float x, float z, float cx, float cz, float sigma) => Mathf.Exp(-((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (2f * sigma * sigma));

    /// <summary>Ground height before the flat spots are levelled.</summary>
    private static float RawHeight(float x, float z)
    {
        float h = 0f;
        // gentle waves everywhere, real hills around the edge (outside the playable area they frame the view)
        h += 0.6f * Mathf.Sin(x / 23f) * Mathf.Cos(z / 19f) + 0.4f * Mathf.Sin((x + z) / 31f);
        float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Half - 4f, Half + 50f, edge)) * (20f + 8f * Mathf.Sin(x / 17f + z / 23f));
        // the watchtower hill (north east) and a lower hill with terraced fields (north west)
        h += 9.5f * Gauss(x, z, 58f, 56f, 15f);
        float terraces = 6f * Gauss(x, z, -52f, 60f, 20f);
        h += Mathf.Floor(terraces / 0.9f) * 0.9f + Mathf.Clamp01((terraces % 0.9f) / 0.9f - 0.85f) * 6f;
        // the dry river bed: 3 m deep, 7 m wide bottom with sloping banks
        float d = Mathf.Abs(z - RiverZ(x));
        h -= 3.2f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, 3.5f, d));
        return h;
    }

    private static float FlatWeight(float x, float z)
    {
        float w = 0f;
        foreach (var r in flats)
        {
            float dx = Mathf.Max(r.xMin - x, 0f, x - r.xMax), dz = Mathf.Max(r.yMin - z, 0f, z - r.yMax);
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            w = Mathf.Max(w, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 0f, dist)));
        }
        return w;
    }

    public static float GroundHeight(float x, float z) => Mathf.Lerp(RawHeight(x, z), 0f, FlatWeight(x, z));

    private static Texture2D T(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortBuilder.Textures}/{file}");

    /// <summary>
    /// The ground: a height field mesh (2 m cells) with a mesh collider, painted with grass, dirt,
    /// gravel and rock through its vertex colours (shader UA/Ground). A plain mesh rather than a Unity
    /// terrain: cheaper on phones and its shader always ends up in the build.
    /// </summary>
    private static void BuildGround()
    {
        const int cells = 170;
        float cell = Size / cells;
        int n = cells + 1;
        var vertices = new Vector3[n * n];
        var colors = new Color[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -Size / 2f + i * cell, z = -Size / 2f + j * cell;
                vertices[j * n + i] = new Vector3(x, GroundHeight(x, z), z);
            }
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var v = vertices[j * n + i];
                float hx = vertices[j * n + Mathf.Min(n - 1, i + 1)].y - vertices[j * n + Mathf.Max(0, i - 1)].y;
                float hz = vertices[Mathf.Min(n - 1, j + 1) * n + i].y - vertices[Mathf.Max(0, j - 1) * n + i].y;
                var normal = new Vector3(-hx, 2f * cell, -hz).normalized;
                float x = v.x, z = v.z;
                float noise = Mathf.PerlinNoise(x * 0.05f + 3f, z * 0.05f + 7f);
                float steep = Mathf.InverseLerp(0.9f, 0.72f, normal.y);
                float river = Mathf.InverseLerp(6f, 3f, Mathf.Abs(z - RiverZ(x)));
                // the village itself is trodden dirt, paths link it to the hills and the river
                float village = Mathf.Max(FlatWeight(x, z), Mathf.InverseLerp(48f, 30f, new Vector2(x * 0.9f, z).magnitude));
                float dirt = Mathf.Clamp01(Mathf.Max(village * (0.75f + 0.25f * noise), PathWeight(x, z)));
                float rock = Mathf.Clamp01(steep * (1f - river));
                float gravel = river;
                float grass = Mathf.Clamp01(1f - dirt - rock - gravel);
                float sum = Mathf.Max(0.001f, grass + dirt + rock + gravel);
                colors[j * n + i] = new Color(dirt / sum, grass / sum, gravel / sum, rock / sum);
            }
        var triangles = new int[cells * cells * 6];
        int t = 0;
        for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                // clockwise seen from above (Unity's front faces)
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
        var mesh = new Mesh { name = "VillageGround" };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.DeleteAsset($"{AssetSetup.Generated}/Village_Ground.asset");
        AssetDatabase.CreateAsset(mesh, $"{AssetSetup.Generated}/Village_Ground.asset");

        string matPath = $"{AssetSetup.Generated}/Materials/Village_Ground.mat";
        AssetDatabase.DeleteAsset(matPath);
        var shader = Shader.Find("UA/Ground");
        if (shader == null) Debug.LogWarning("[Map] village: ground shader missing");
        var mat = new Material(shader ?? Shader.Find("Standard")) { name = "Village_Ground" };
        mat.SetTexture("_Grass", T("Grass004_BaseColor.jpg"));
        mat.SetTexture("_Dirt", T("Ground104_BaseColor.jpg"));
        mat.SetTexture("_Gravel", T("Gravel036L_BaseColor.jpg"));
        mat.SetTexture("_Rock", T("Rock055_BaseColor.jpg"));
        mat.SetColor("_GrassTint", new Color(0.9f, 1f, 0.85f));
        mat.SetVector("_Tiles", new Vector4(5f, 4f, 3f, 8f));
        AssetDatabase.CreateAsset(mat, matPath);

        var go = new GameObject("Ground");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        Physics.SyncTransforms();
    }

    // the paths: from the bazaar to the bridge, the ford, the watchtower and the terraces
    private static readonly Vector2[][] Paths =
    {
        new[] { new Vector2(0, 0), new Vector2(6, -20), new Vector2(10, -38), new Vector2(12, -60), new Vector2(14, -80) },
        new[] { new Vector2(0, 0), new Vector2(-18, -18), new Vector2(-40, -36), new Vector2(-46, -60), new Vector2(-50, -80) },
        new[] { new Vector2(0, 0), new Vector2(22, 18), new Vector2(40, 36), new Vector2(52, 50) },
        new[] { new Vector2(0, 0), new Vector2(-22, 22), new Vector2(-40, 44), new Vector2(-50, 62) },
        new[] { new Vector2(0, 0), new Vector2(40, -2), new Vector2(80, 4) },
        new[] { new Vector2(0, 0), new Vector2(-40, 2), new Vector2(-80, -4) },
    };

    private static float PathWeight(float x, float z)
    {
        float best = 99f;
        var p = new Vector2(x, z);
        foreach (var path in Paths)
            for (int i = 0; i + 1 < path.Length; i++)
            {
                var a = path[i]; var b = path[i + 1];
                float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + (b - a) * t));
            }
        return Mathf.InverseLerp(2.6f, 1.2f, best);
    }

    /// <summary>Ground height under (x, z): the mesh surface (raycast), else the height function.</summary>
    private static float Y(float x, float z)
    {
        var hits = Physics.RaycastAll(new Vector3(x, 80f, z), Vector3.down, 200f, World.EnvMask);
        float best = float.MinValue;
        foreach (var h in hits)
            if (h.collider.name == "Ground") best = Mathf.Max(best, h.point.y);
        return best > float.MinValue ? best : GroundHeight(x, z);
    }

    // ------------------------------------------------------------------ pieces

    /// <summary>A mud block with softly rounded edges (walls, parapets, platforms).</summary>
    private static GameObject Block(string name, Transform t, Vector3 local, Vector3 size, Material m = null, float round = 0.1f, bool collider = true)
    {
        if (size.x < 0.02f || size.y < 0.02f || size.z < 0.02f) return null;
        return MeshGen.RoundBox(name, t.TransformPoint(local), size, round, m ?? Mud, 2.5f, t, t.eulerAngles.y, collider);
    }

    /// <summary>
    /// A wall along local X from x0 to x1 at local z, base y, with openings (centre x, width, bottom,
    /// top): wooden lintels over doors and windows.
    /// </summary>
    private static void Wall(Transform t, float x0, float x1, float z, float y, float height, float thick, List<(float c, float w, float b, float top)> openings, Material m = null, bool alongZ = false)
    {
        Vector3 P(float u, float v) => alongZ ? new Vector3(z, v, u) : new Vector3(u, v, z);
        Vector3 S(float len, float h) => alongZ ? new Vector3(thick, h, len) : new Vector3(len, h, thick);
        float cursor = x0;
        foreach (var o in openings.OrderBy(o => o.c))
        {
            float a = o.c - o.w / 2f, b = o.c + o.w / 2f;
            if (a > cursor) Block("Wall", t, P((cursor + a) / 2f, y + height / 2f), S(a - cursor + 0.02f, height), m);
            if (o.b > 0.01f) Block("Sill", t, P(o.c, y + o.b / 2f), S(o.w + 0.02f, o.b), m);
            if (o.top < height - 0.01f)
            {
                Block("Above", t, P(o.c, y + (o.top + height) / 2f), S(o.w + 0.02f, height - o.top), m);
                Block("Lintel", t, P(o.c, y + o.top + 0.06f), S(o.w + 0.5f, 0.12f) + (alongZ ? new Vector3(0.06f, 0, 0) : new Vector3(0, 0, 0.06f)), Wood, 0.03f, false);
            }
            cursor = b;
        }
        if (x1 > cursor) Block("Wall", t, P((cursor + x1) / 2f, y + height / 2f), S(x1 - cursor, height), m);
    }

    private static List<(float, float, float, float)> Openings(params (float c, float w, float b, float top)[] o) => o.ToList();
    private static (float, float, float, float) Door(float c) => (c, 1.1f, 0f, 2.15f);
    private static (float, float, float, float) Window(float c) => (c, 0.7f, 1.1f, 1.9f);

    /// <summary>Flat mud roof on wooden beams with a low parapet; the beam ends show on the front and back.</summary>
    private static void Roof(Transform t, float w, float l, float y, bool parapet, float stairGap = 0f, float gapSide = 1f)
    {
        Block("Roof", t, new Vector3(0, y + 0.15f, 0), new Vector3(w + 0.3f, 0.3f, l + 0.3f), MudPlaster, 0.1f);
        for (float x = -w / 2f + 0.4f; x < w / 2f - 0.2f; x += 0.75f)
            foreach (float s in new[] { -1f, 1f })
                Block("Beam", t, new Vector3(x, y - 0.08f, s * (l / 2f + 0.3f)), new Vector3(0.13f, 0.13f, 0.45f), OldWood, 0.03f, false);
        if (!parapet) return;
        float ph = 0.55f, py = y + 0.3f + ph / 2f;
        Block("Parapet", t, new Vector3(0, py, -l / 2f), new Vector3(w + 0.3f, ph, 0.3f), MudPlaster, 0.12f);
        Block("Parapet", t, new Vector3(0, py, l / 2f), new Vector3(w + 0.3f, ph, 0.3f), MudPlaster, 0.12f);
        foreach (float s in new[] { -1f, 1f })
        {
            // the side where the outside stair arrives has an opening at its back end
            if (stairGap > 0f && Mathf.Approximately(s, gapSide))
            {
                float len = l - stairGap;
                Block("Parapet", t, new Vector3(s * w / 2f, py, -l / 2f + len / 2f), new Vector3(0.3f, ph, len), MudPlaster, 0.12f);
            }
            else Block("Parapet", t, new Vector3(s * w / 2f, py, 0), new Vector3(0.3f, ph, l + 0.3f), MudPlaster, 0.12f);
        }
        // a wooden spout to drain the roof
        Block("Spout", t, new Vector3(w / 4f, y + 0.25f, -l / 2f - 0.45f), new Vector3(0.14f, 0.1f, 0.7f), OldWood, 0.02f, false);
    }

    /// <summary>
    /// A mud house (front at local -Z): rooms with a partition, doors and small windows, a flat roof,
    /// optionally an upper room (balakhana) with a wooden balcony. Returns the roof height.
    /// </summary>
    private static float House(Transform parent, Vector3 pos, float yaw, float w, float l, bool upper, int stairSide)
    {
        var t = new GameObject("House").transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        const float h = 3.1f, thick = 0.45f;
        Block("Floor", t, new Vector3(0, 0.05f, 0), new Vector3(w, 0.1f, l), Dirt, 0.04f);
        Wall(t, -w / 2f, w / 2f, -l / 2f + thick / 2f, 0, h, thick, Openings(Door(-w * 0.22f), Door(w * 0.25f), Window(-w * 0.4f), Window(w * 0.02f), Window(w * 0.42f)));
        Wall(t, -w / 2f, w / 2f, l / 2f - thick / 2f, 0, h, thick, Openings(Window(-w * 0.25f), Window(w * 0.25f)));
        Wall(t, -l / 2f + thick, l / 2f - thick, -w / 2f + thick / 2f, 0, h, thick, Openings(Window(0)), null, true);
        Wall(t, -l / 2f + thick, l / 2f - thick, w / 2f - thick / 2f, 0, h, thick, Openings(Window(0)), null, true);
        // partition between the two rooms with a doorway
        Wall(t, -l / 2f + thick, l / 2f - thick, 0, 0, h, 0.3f, Openings((-0.4f, 1f, 0f, 2.1f)), MudDark, true);
        // the guest room has a raised sitting platform (toshak place) along the back wall: low cover
        Block("Platform", t, new Vector3(-w / 4f, 0.25f, l / 2f - 1f), new Vector3(w / 2f - 1f, 0.4f, 1.1f), MudDark, 0.12f);
        Roof(t, w, l, h, true, 1.6f, stairSide);
        // outside stair along one side, from the front up onto the roof (Buildings.Stairs: 0.2 m steps, 0.36 m deep)
        float run = Mathf.CeilToInt((h + 0.3f) / 0.2f) * 0.36f;
        var stairStart = t.TransformPoint(new Vector3(stairSide * (w / 2f + 0.75f), 0, l / 2f - 0.3f - run));
        global::Buildings.Stairs(parent, stairStart, t.forward, h + 0.3f, 1.2f, Mud);
        Block("StairWall", t, new Vector3(stairSide * (w / 2f + 1.45f), (h + 0.3f) / 2f + 0.3f, l / 2f - 0.3f - run / 2f), new Vector3(0.25f, h + 0.9f, run), MudPlaster, 0.1f);
        float top = h + 0.3f;
        if (upper)
        {
            // the upper room on the half away from the stair, its door towards the open roof, a balcony at the front
            var u = new GameObject("UpperRoom").transform;
            u.SetParent(t, false);
            float uw = w * 0.45f, ul = l - 0.6f;
            u.localPosition = new Vector3(-stairSide * (w / 2f - uw / 2f - 0.15f), top, 0.15f);
            Wall(u, -uw / 2f, uw / 2f, -ul / 2f + 0.2f, 0, 2.8f, 0.4f, Openings(Window(-uw * 0.2f), Window(uw * 0.25f)));
            Wall(u, -uw / 2f, uw / 2f, ul / 2f - 0.2f, 0, 2.8f, 0.4f, Openings(Window(0)));
            Wall(u, -ul / 2f + 0.4f, ul / 2f - 0.4f, -stairSide * (uw / 2f - 0.2f), 0, 2.8f, 0.4f, Openings(Window(0.6f)), null, true);
            Wall(u, -ul / 2f + 0.4f, ul / 2f - 0.4f, stairSide * (uw / 2f - 0.2f), 0, 2.8f, 0.4f, Openings(Door(-0.4f)), null, true);
            Roof(u, uw, ul, 2.8f, false);
            // wooden balcony on carved posts
            Block("Balcony", u, new Vector3(0, 0.0f, -ul / 2f - 0.75f), new Vector3(uw * 0.7f, 0.12f, 1.1f), OldWood, 0.03f);
            Block("BalconyRail", u, new Vector3(0, 0.55f, -ul / 2f - 1.27f), new Vector3(uw * 0.7f, 0.9f, 0.08f), OldWood, 0.03f);
        }
        return top;
    }

    /// <summary>A compound: mud walls with a gate (and a breach), corner towers, a house against the back wall and a yard.</summary>
    private static void Compound(Vector3 c, float w, float l, float yaw, bool upper, bool towers, int seed)
    {
        var r = new System.Random(seed);
        var t = new GameObject("Qala").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(c, Quaternion.Euler(0, yaw, 0));
        const float wallH = 2.9f, thick = 0.6f;
        float gate = (float)(r.NextDouble() - 0.5) * (w - 8f);
        // front wall with the gate, back wall with a breach, solid sides with a low gap
        Wall(t, -w / 2f, w / 2f, -l / 2f, 0, wallH, thick, Openings((gate, 2.6f, 0f, 2.5f)));
        Block("GateBeam", t, new Vector3(gate, 2.62f, -l / 2f), new Vector3(3.2f, 0.2f, 0.75f), OldWood, 0.04f, false);
        foreach (float s in new[] { -1f, 1f })
        {
            // half-open wooden gate leaves
            var leaf = Block("GateLeaf", t, new Vector3(gate + s * 1.6f, 1.15f, -l / 2f - 0.35f - 0.4f), new Vector3(0.1f, 2.2f, 1.2f), OldWood, 0.02f);
            if (leaf != null) leaf.transform.RotateAround(t.TransformPoint(new Vector3(gate + s * 1.3f, 0, -l / 2f - 0.3f)), Vector3.up, s * 20f);
        }
        float breach = (float)(r.NextDouble() - 0.5) * (w - 6f);
        Wall(t, -w / 2f, w / 2f, l / 2f, 0, wallH, thick, Openings((breach, 1.5f, 0f, wallH)));
        foreach (float s in new[] { -1f, 1f })
            Wall(t, -l / 2f + thick / 2f, l / 2f - thick / 2f, s * w / 2f, 0, wallH, thick, Openings((s * l * 0.15f, 1.3f, 0.95f, wallH)), null, true);
        // soft rounded wall tops
        foreach (float s in new[] { -1f, 1f })
        {
            Block("Cap", t, new Vector3(0, wallH + 0.08f, s * l / 2f), new Vector3(w + 0.1f, 0.16f, thick + 0.12f), MudPlaster, 0.08f, false);
            Block("Cap", t, new Vector3(s * w / 2f, wallH + 0.08f, 0), new Vector3(thick + 0.12f, 0.16f, l), MudPlaster, 0.08f, false);
        }
        if (towers)
            foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                {
                    var p = t.TransformPoint(new Vector3(x * w / 2f, 0, z * l / 2f));
                    MeshGen.TexCylinder("Tower", p, 1.5f, 4.2f, Mud, 2.5f, t);
                    MeshGen.TexCylinder("TowerTop", p + Vector3.up * 4.2f, 1.62f, 0.3f, MudPlaster, 2.5f, t, false);
                }
        // the house along the back wall, its stair on the side with room for it
        float hw = Mathf.Min(w - 5.2f, 13f), hl = 6f;
        int stairSide = r.NextDouble() < 0.5 ? -1 : 1;
        float hx = -stairSide * (w / 2f - thick - 0.3f - hw / 2f);
        House(t, t.TransformPoint(new Vector3(hx, 0, l / 2f - thick / 2f - 0.2f - hl / 2f)), yaw, hw, hl, upper, stairSide);
        // the yard: tandoor oven, hay stack, firewood, a tree, sacks and a water jar
        Yard(t, w, l, gate, r);
    }

    private static void Yard(Transform t, float w, float l, float gate, System.Random r)
    {
        float yardZ = -l / 2f + (l - 6f) / 2f;
        var tandoor = t.TransformPoint(new Vector3(w / 2f - 2f, 0, -l / 2f + 2f));
        MeshGen.TexCylinder("Tandoor", tandoor, 0.55f, 0.9f, MudDark, 1.5f, t);
        var hay = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hay.name = "HayStack";
        hay.transform.SetParent(t, false);
        hay.transform.localPosition = new Vector3(-w / 2f + 2.4f, 0.6f, yardZ);
        hay.transform.localScale = new Vector3(2.6f, 2.2f, 2.6f);
        hay.GetComponent<Renderer>().sharedMaterial = Hay;
        Block("Firewood", t, new Vector3(-w / 2f + 1.2f, 0.45f, -l / 2f + 1.8f), new Vector3(1.8f, 0.9f, 1.1f), OldWood, 0.08f);
        Block("Sacks", t, new Vector3(gate + (gate > 0 ? -3.5f : 3.5f), 0.35f, yardZ + 1f), new Vector3(1.6f, 0.7f, 0.9f), Hay, 0.25f);
        // a charpai (rope bed) as low cover
        Block("Charpai", t, new Vector3(w * 0.15f, 0.45f, yardZ - 1.5f), new Vector3(1.9f, 0.12f, 0.95f), OldWood, 0.03f);
        foreach (float x in new[] { -0.85f, 0.85f })
            foreach (float z in new[] { -0.4f, 0.4f })
                Block("Leg", t, new Vector3(w * 0.15f + x, 0.2f, yardZ - 1.5f + z), new Vector3(0.08f, 0.4f, 0.08f), OldWood, 0.02f, false);
        if (r.NextDouble() < 0.7)
            Nature.Tree(Nature.Trees, rng, t.TransformPoint(new Vector3(w * 0.25f, 0, yardZ + 0.5f)), 6f, 8f, t);
    }

    /// <summary>The bazaar: shops on a raised platform with steps, around a big plane tree on a round seat.</summary>
    private static void Bazaar()
    {
        var t = new GameObject("Bazaar").transform;
        t.SetParent(root, false);
        // the chinar on its round raised seat
        MeshGen.TexCylinder("TreeSeat", Vector3.zero, 4.2f, 0.9f, Mud, 2.5f, t);
        MeshGen.TexCylinder("TreeSeatTop", Vector3.up * 0.9f, 4.35f, 0.12f, MudPlaster, 2.5f, t, false);
        global::Buildings.Stairs(t, new Vector3(0, 0, -5.9f), Vector3.forward, 0.9f, 1.6f, MudPlaster);
        global::Buildings.Stairs(t, new Vector3(0, 0, 5.9f), Vector3.back, 0.9f, 1.6f, MudPlaster);
        Nature.Tree(Nature.Trees, rng, Vector3.up * 1f, 11f, 13f, t);
        // two rows of shops facing the square
        foreach (float side in new[] { -1f, 1f })
        {
            var row = new GameObject("Shops").transform;
            row.SetParent(t, false);
            row.SetPositionAndRotation(new Vector3(0, 0, side * 15f), Quaternion.Euler(0, side > 0 ? 0f : 180f, 0));
            Block("ShopPlatform", row, new Vector3(0, 0.35f, 0.4f), new Vector3(26f, 0.7f, 6.4f), MudPlaster, 0.12f);
            // steps up to the platform edge (front at local z = -2.8; 4 steps of 0.36 m)
            global::Buildings.Stairs(t, row.TransformPoint(new Vector3(-8f, 0, -4.24f)), row.forward, 0.7f, 2.2f, MudPlaster);
            global::Buildings.Stairs(t, row.TransformPoint(new Vector3(8f, 0, -4.24f)), row.forward, 0.7f, 2.2f, MudPlaster);
            for (int i = 0; i < 5; i++)
            {
                float x = -10f + i * 5f;
                var shop = new GameObject("Shop").transform;
                shop.SetParent(row, false);
                shop.localPosition = new Vector3(x, 0.7f, 1.2f);
                // open front shop: back and side walls, roof with an awning on poles
                Block("Back", shop, new Vector3(0, 1.4f, 1.8f), new Vector3(4.9f, 2.8f, 0.4f));
                Block("Side", shop, new Vector3(-2.45f, 1.4f, 0), new Vector3(0.35f, 2.8f, 4f));
                if (i == 4) Block("Side", shop, new Vector3(2.45f, 1.4f, 0), new Vector3(0.35f, 2.8f, 4f));
                Block("Roof", shop, new Vector3(0, 2.95f, 0), new Vector3(5.1f, 0.3f, 4.2f), MudPlaster, 0.1f);
                Block("Awning", shop, new Vector3(0, 2.5f, -2.8f), new Vector3(4.9f, 0.06f, 1.6f), OldWood, 0.02f, false).transform.localRotation = Quaternion.Euler(-12, 0, 0);
                foreach (float px in new[] { -2.2f, 2.2f })
                    Block("Pole", shop, new Vector3(px, 1.2f, -3.45f), new Vector3(0.12f, 2.4f, 0.12f), OldWood, 0.03f);
                // goods: sacks, crates, a counter
                Block("Counter", shop, new Vector3(0.6f, 0.45f, -1.3f), new Vector3(2.4f, 0.9f, 0.7f), OldWood, 0.04f);
                Block("Sacks", shop, new Vector3(-1.4f, 0.3f, 0.6f), new Vector3(1.2f, 0.6f, 0.8f), Hay, 0.2f);
            }
        }
        // a well and a cart in the square
        MeshGen.TexCylinder("Well", new Vector3(-9f, 0, -5f), 1f, 0.9f, Stone, 1.5f, t);
        Block("WellFrame", t, new Vector3(-9f, 1.9f, -5f), new Vector3(2.4f, 0.12f, 0.12f), OldWood, 0.03f, false);
        foreach (float s in new[] { -1f, 1f })
            Block("WellPost", t, new Vector3(-9f + s * 1.1f, 1.2f, -5f), new Vector3(0.12f, 1.6f, 0.12f), OldWood, 0.03f, false);
        Cart(t, new Vector3(9f, 0, 4f), 30f);
        Cart(t, new Vector3(-12f, 0, 6f), -60f);
    }

    private static void Cart(Transform parent, Vector3 pos, float yaw)
    {
        var t = new GameObject("Cart").transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        Block("Bed", t, new Vector3(0, 0.75f, 0), new Vector3(1.4f, 0.12f, 2.4f), OldWood, 0.03f);
        foreach (float s in new[] { -1f, 1f })
        {
            Block("Side", t, new Vector3(s * 0.66f, 1f, 0), new Vector3(0.08f, 0.4f, 2.4f), OldWood, 0.02f, false);
            var wheel = MeshGen.Object("Wheel", MeshGen.Cylinder(0.55f, 0.1f, 1f, 16), OldWood, t, t.TransformPoint(new Vector3(s * 0.8f, 0.55f, -0.2f)));
            wheel.transform.rotation = t.rotation * Quaternion.Euler(0, 0, 90);
        }
        Block("Shaft", t, new Vector3(0, 0.6f, 1.9f), new Vector3(0.1f, 0.1f, 1.6f), OldWood, 0.02f, false);
        var c = new GameObject("CartCollider");
        c.transform.SetParent(t, false);
        c.transform.localPosition = new Vector3(0, 0.7f, 0);
        c.AddComponent<BoxCollider>().size = new Vector3(1.6f, 1.2f, 2.4f);
    }

    /// <summary>The old round watchtower: thick mud walls with loopholes, a stair up the outside to the top.</summary>
    private static void Watchtower(Vector3 pos)
    {
        var t = new GameObject("Burj").transform;
        t.SetParent(root, false);
        t.position = pos;
        const float radius = 3.2f, height = 5.6f;
        int segments = 14;
        for (int i = 0; i < segments; i++)
        {
            float a = (i + 0.5f) / segments * 360f;
            if (Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < 13f) continue;   // the doorway (east)
            var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
            MeshGen.RoundBox("TowerWall", pos + dir * radius + Vector3.up * height / 2f, new Vector3(1.55f, height, 0.7f), 0.15f, Mud, 2.5f, t, a);
            // battlements on top, every other segment, open where the outside stair arrives (south)
            if (i % 2 == 0 && Mathf.Abs(Mathf.DeltaAngle(a, 180f)) > 30f)
                MeshGen.RoundBox("Merlon", pos + dir * radius + Vector3.up * (height + 0.45f), new Vector3(1.2f, 0.9f, 0.7f), 0.15f, MudPlaster, 2.5f, t, a);
        }
        // wooden floor at the top, reached by the outside stair on the south side (going east)
        MeshGen.TexCylinder("TopFloor", pos + Vector3.up * (height - 0.2f), radius - 0.3f, 0.2f, OldWood, 1.5f, t);
        var landing = pos + Vector3.back * (radius + 1.05f) + Vector3.up * (height - 0.1f);
        MeshGen.RoundBox("Landing", landing, new Vector3(1.8f, 0.2f, 1.5f), 0.05f, OldWood, 1f, t);
        var top = landing + Vector3.left * 0.9f;
        // the hill falls away under the stair: find where a stair from the ground reaches the landing
        var start = top + Vector3.left * 8f;
        for (int k = 0; k < 4; k++)
        {
            start.y = Y(start.x, start.z);
            float run = Mathf.CeilToInt((top.y + 0.1f - start.y) / 0.2f) * 0.36f;
            start = new Vector3(top.x - run, start.y, top.z);
        }
        start.y = Y(start.x, start.z);
        global::Buildings.Stairs(t, start, Vector3.right, top.y + 0.1f - start.y, 1.4f, Mud);
    }

    /// <summary>A plank footbridge across the river bed on posts, with rails.</summary>
    private static void Bridge(float x)
    {
        var t = new GameObject("Footbridge").transform;
        t.SetParent(root, false);
        float z = RiverZ(x);
        const float span = 22f;
        t.position = new Vector3(x, Mathf.Max(Y(x, z - span / 2f), Y(x, z + span / 2f)) + 0.12f, z);
        MeshGen.RoundBox("Deck", t.position, new Vector3(2.2f, 0.18f, span), 0.04f, OldWood, 1.5f, t);
        foreach (float s in new[] { -1f, 1f })
        {
            MeshGen.RoundBox("Rail", t.position + new Vector3(s * 1.05f, 0.95f, 0), new Vector3(0.08f, 0.08f, span), 0.02f, OldWood, 1f, t, 0, false);
            var guard = new GameObject("RailCollider");
            guard.transform.SetParent(t, false);
            guard.transform.position = t.position + new Vector3(s * 1.08f, 0.6f, 0);
            guard.AddComponent<BoxCollider>().size = new Vector3(0.08f, 1.2f, span);
            for (float k = -span / 2f; k <= span / 2f; k += 2.75f)
            {
                float ground = Y(x + s, z + k), railTop = t.position.y + 0.95f;
                MeshGen.RoundBox("Post", new Vector3(x + s * 1.05f, (ground + railTop) / 2f, z + k), new Vector3(0.14f, railTop - ground + 0.1f, 0.14f), 0.03f, OldWood, 1f, t, 0, false);
            }
        }
    }

    // ------------------------------------------------------------------ the map

    public static MapLayout Build()
    {
        rng = new System.Random(23);
        root = new GameObject("Map_Village").transform;
        flats.Clear();
        // where the compounds stand the ground is levelled (with a few metres of margin)
        var compounds = new (Vector3 c, float w, float l, float yaw, bool upper, bool towers)[]
        {
            (new Vector3(-34f, 0, 12f), 22f, 18f, 90f, true, true),
            (new Vector3(-30f, 0, -22f), 18f, 16f, 0f, false, false),
            (new Vector3(32f, 0, 16f), 20f, 18f, -90f, true, false),
            (new Vector3(34f, 0, -20f), 18f, 16f, 180f, false, true),
            (new Vector3(-6f, 0, 38f), 20f, 16f, 180f, true, false),
            (new Vector3(-62f, 0, -12f), 16f, 16f, 90f, false, false),
            (new Vector3(62f, 0, -6f), 18f, 16f, -90f, true, true),
            (new Vector3(20f, 0, -68f), 16f, 14f, 180f, false, false),
            (new Vector3(-18f, 0, -36f), 14f, 14f, 0f, false, false),
            (new Vector3(66f, 0, 26f), 16f, 14f, -90f, false, false),
        };
        foreach (var k in compounds)
        {
            var ext = Quaternion.Euler(0, k.yaw, 0) * new Vector3(k.w, 0, k.l);
            float ex = Mathf.Abs(ext.x) / 2f + 2.5f, ez = Mathf.Abs(ext.z) / 2f + 2.5f;
            flats.Add(new Rect(k.c.x - ex, k.c.z - ez, ex * 2f, ez * 2f));
        }
        flats.Add(new Rect(-16f, -21f, 32f, 42f));   // the bazaar square
        BuildGround();
        int seed = 5;
        foreach (var k in compounds) Compound(k.c, k.w, k.l, k.yaw, k.upper, k.towers, seed++);
        Bazaar();

        // the watchtower on its hill, the bridge and a ford over the river bed
        var hill = new Vector3(58f, 0, 56f);
        hill.y = Y(hill.x, hill.z);
        Watchtower(hill);
        Bridge(12f);
        for (int i = 0; i < 7; i++)
        {
            float x = -48f + (i % 2 == 0 ? 0.6f : -0.6f), z = RiverZ(-48f) - 6f + i * 2f;
            MeshGen.RoundBox("SteppingStone", new Vector3(x, Y(x, z) + 0.2f, z), new Vector3(1.3f, 0.5f, 1.1f), 0.2f, Stone, 1f, root, R(0, 90));
        }
        // the river bed: rocks and poplars along the banks
        var nature = new GameObject("Nature").transform;
        nature.SetParent(root, false);
        for (float x = -Half + 4f; x < Half - 4f; x += R(7f, 12f))
        {
            float z = RiverZ(x);
            Nature.Place(Nature.Rocks[rng.Next(Nature.Rocks.Length)], new Vector3(x, Y(x, z), z + R(-2.5f, 2.5f)), R(0, 360), R(0.8f, 1.8f), nature);
            foreach (float s in new[] { -1f, 1f })
            {
                float bz = z + s * R(10f, 13f);
                if (Mathf.Abs(x - 12f) < 6f || Mathf.Abs(x + 48f) < 5f || rng.NextDouble() < 0.35) continue;
                Nature.Tree(rng.NextDouble() < 0.5 ? Nature.Pines : Nature.Trees, rng, new Vector3(x, Y(x, bz), bz), 9f, 13f, nature);
            }
        }
        // orchards on the north west, trees scattered over the fields
        for (int i = 0; i < 5; i++)
            for (int k = 0; k < 4; k++)
            {
                float x = -76f + i * 6.5f + R(-0.8f, 0.8f), z = 22f + k * 6.5f + R(-0.8f, 0.8f);
                if (FlatWeight(x, z) > 0.2f) continue;
                Nature.Tree(Nature.Trees, rng, new Vector3(x, Y(x, z), z), 4.5f, 6f, nature);
            }
        for (int i = 0; i < 26; i++)
        {
            float x = R(-Half + 6f, Half - 6f), z = R(-Half + 6f, Half - 6f);
            if (FlatWeight(x, z) > 0.1f || PathWeight(x, z) > 0.1f || Mathf.Abs(z - RiverZ(x)) < 10f || new Vector2(x, z).magnitude < 24f) continue;
            if (Physics.CheckSphere(new Vector3(x, Y(x, z) + 1f, z), 3f)) continue;
            Nature.Tree(rng.NextDouble() < 0.25 ? Nature.Pines : Nature.Trees, rng, new Vector3(x, Y(x, z), z), 7f, 11f, nature);
            Nature.Place(Nature.Bushes[rng.Next(Nature.Bushes.Length)], new Vector3(x + R(-2f, 2f), Y(x, z), z + R(-2f, 2f)), R(0, 360), R(0.9f, 1.5f), nature);
        }
        // grass and flowers on the fields (few: they are drawn one by one)
        for (int i = 0; i < 130; i++)
        {
            float x = R(-Half, Half), z = R(-Half, Half);
            if (FlatWeight(x, z) > 0.05f || PathWeight(x, z) > 0.1f || Mathf.Abs(z - RiverZ(x)) < 7f) continue;
            Nature.Place(Nature.Ground[rng.Next(Nature.Ground.Length)], new Vector3(x, Y(x, z), z), R(0, 360), R(0.4f, 0.8f), nature);
        }
        // mud walls on the field terraces: cover on the north west hill
        for (int i = 0; i < 6; i++)
        {
            float z = 48f + i * 5.5f, x = -52f + R(-6f, 6f);
            var p = new Vector3(x, Y(x, z), z);
            MeshGen.RoundBox("FieldWall", p + Vector3.up * 0.4f, new Vector3(R(6f, 10f), 0.9f, 0.5f), 0.2f, Mud, 2.5f, nature, R(-8f, 8f));
        }
        // hay stacks and sandbag posts in the open
        Physics.SyncTransforms();
        var cover = new GameObject("Cover").transform;
        cover.SetParent(root, false);
        int placed = 0;
        for (int i = 0; i < 90 && placed < 14; i++)
        {
            float x = R(-Half + 8f, Half - 8f), z = R(-Half + 8f, Half - 8f);
            if (FlatWeight(x, z) > 0.05f || Mathf.Abs(z - RiverZ(x)) < 8f) continue;
            var p = new Vector3(x, Y(x, z), z);
            if (Physics.CheckBox(p + Vector3.up * 1.2f, new Vector3(2.5f, 1f, 2.5f), Quaternion.identity, World.EnvMask)) continue;
            if (placed % 3 == 0) Obstacles.SandbagBunker(cover, p, R(0, 360));
            else
            {
                var hay = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hay.name = "HayStack";
                hay.transform.SetParent(cover, false);
                hay.transform.position = p + Vector3.up * 0.5f;
                hay.transform.localScale = new Vector3(R(2.4f, 3.2f), R(2f, 2.6f), R(2.4f, 3.2f));
                hay.GetComponent<Renderer>().sharedMaterial = Hay;
            }
            Physics.SyncTransforms();
            placed++;
        }
        // ammunition crates: in the bazaar, by the bridge, at the tower hill and in three compounds
        var supply = new GameObject("AmmoCrates").transform;
        supply.SetParent(root, false);
        foreach (var (x, z) in new[] { (6f, 7f), (16f, RiverZ(16f) + 13f), (50f, 46f), (-34f, 6f), (32f, 10f), (-30f, -26f), (-50f, 40f) })
            Obstacles.AmmoCrate(supply, new Vector3(x, Y(x, z), z), R(0, 360));
        // invisible walls just outside the playable area
        foreach (var (c, s) in new[]
        {
            (new Vector3(0, 10f, Half + 3f), new Vector3(Half * 2f + 10f, 40f, 1f)), (new Vector3(0, 10f, -Half - 3f), new Vector3(Half * 2f + 10f, 40f, 1f)),
            (new Vector3(Half + 3f, 10f, 0), new Vector3(1f, 40f, Half * 2f + 10f)), (new Vector3(-Half - 3f, 10f, 0), new Vector3(1f, 40f, Half * 2f + 10f)),
        })
        {
            var wall = new GameObject("Edge");
            wall.transform.SetParent(root, false);
            wall.transform.position = c;
            wall.AddComponent<BoxCollider>().size = s;
        }

        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);

        var layout = new MapLayout { Name = "VILLAGE", Root = root.gameObject, Center = Vector3.zero, Radius = Half };
        BuildNavigation(layout);
        Debug.Log($"[Village] built: {root.GetComponentsInChildren<Renderer>().Length} renderers, {layout.Waypoints.Length} waypoints, {layout.Spawns.Length} spawns");
        return layout;
    }

    /// <summary>Bot waypoints and spawn points on a grid over the open ground (not inside walls or on roofs).</summary>
    private static void BuildNavigation(MapLayout layout)
    {
        Physics.SyncTransforms();
        var points = new List<Vector3>();
        var index = new Dictionary<Vector2Int, int>();
        const float step = 8f;
        int n = Mathf.FloorToInt((Half - 4f) / step);
        for (int gx = -n; gx <= n; gx++)
            for (int gz = -n; gz <= n; gz++)
            {
                var p = new Vector3(gx * step, 0, gz * step);
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out var hit, 120f, World.EnvMask)) continue;
                p.y = hit.point.y;
                // only the ground or a floor close to it (inside houses), not roofs and tower tops
                if (Mathf.Abs(p.y - Y(p.x, p.z)) > 0.4f) continue;
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.55f, World.EnvMask | (1 << World.FenceLayer))) continue;
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
                if (Mathf.Abs(a.y - b.y) > Vector3.Distance(a, b) * 0.55f) continue;
                // the way between must be free and the ground in between not much higher (hill tops)
                var mid = (a + b) / 2f;
                if (Y(mid.x, mid.z) > Mathf.Max(a.y, b.y) + 0.8f) continue;
                if (Physics.SphereCast(a + Vector3.up * 1.2f, 0.4f, (b - a).normalized, out _, Vector3.Distance(a, b), World.EnvMask | (1 << World.FenceLayer))) continue;
                links.Add(new Vector2Int(pair.Value, other));
            }
        layout.Waypoints = points.ToArray();
        layout.Links = links.ToArray();
        layout.Spawns = points.Where(p => Mathf.Abs(p.z - RiverZ(p.x)) > 5f).ToArray();
    }
}
