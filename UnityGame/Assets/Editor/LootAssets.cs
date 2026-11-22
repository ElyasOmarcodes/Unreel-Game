using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The things of the ADVANCED game, made into Resources/Loot: the yellow fuel drum with its skull sign,
/// the C4 pack (lying as loot, and planted with its blinking lamp), the supply crate, the military crate of
/// the hot spots, the supply drop's crate with its parachute and the cargo plane that brings it, a pack of
/// grenades and the three vests. Their textures are drawn by Tools/loot/textures.py (Assets/Textures/Loot).
/// </summary>
public static class LootAssets
{
    private const string Out = "Assets/Resources/Loot";
    private const string Tex = "Assets/Textures/Loot";
    private const string MeshDir = AssetSetup.Generated + "/Loot";

    public static void Build()
    {
        AssetSetup.Folder(Out);
        AssetSetup.Folder(MeshDir);
        Drum();
        C4(false);
        C4(true);
        Crate();
        MilitaryCrate();
        AirdropCrate();
        Parachute();
        Plane();
        GrenadePack();
        Vest(false);
        Vest(true);
        Vest(true, true);
        AssetDatabase.SaveAssets();
        Debug.Log("[Setup] loot: drum, C4, crates, supply drop, parachute, plane, grenades, vests");
    }

    private static Texture2D T(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/{name}.png");

    private static Material M(string name, Color color, string tex, float smooth = 0.35f, float metal = 0f) =>
        AssetSetup.Mat("Loot_" + name, color, smooth, metal, tex != null ? T(tex) : null);

    private static Mesh Save(Mesh m)
    {
        string path = $"{MeshDir}/{m.name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material m, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    private static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float thick, Material m, PrimitiveType type = PrimitiveType.Cylinder)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (a + b) / 2f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        go.transform.localScale = new Vector3(thick, (b - a).magnitude / 2f, thick);
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    private static void Prefab(GameObject root, string name)
    {
        PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    // ------------------------------------------------------------------ the fuel drum

    /// <summary>A drum of radius r and height h, its side wrapped once by the texture (u round, v up), with a lid.</summary>
    private static Mesh DrumMesh(float r, float h, int seg)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            v.Add(d * r); n.Add(d); uv.Add(new Vector2(i / (float)seg, 0));
            v.Add(d * r + Vector3.up * h); n.Add(d); uv.Add(new Vector2(i / (float)seg, 1));
        }
        for (int i = 0; i < seg; i++)
        {
            int k = i * 2;
            t.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
        }
        var mesh = new Mesh { name = "FuelDrumSide" };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return Save(mesh);
    }

    private static Mesh Disc(string name, float r, int seg, bool up)
    {
        var v = new List<Vector3> { Vector3.zero }; var n = new List<Vector3> { up ? Vector3.up : Vector3.down }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
        var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r));
            n.Add(up ? Vector3.up : Vector3.down);
            uv.Add(new Vector2(0.5f + Mathf.Sin(a) * 0.5f, 0.5f + Mathf.Cos(a) * 0.5f));
        }
        for (int i = 1; i <= seg; i++)
            if (up) t.AddRange(new[] { 0, i, i + 1 }); else t.AddRange(new[] { 0, i + 1, i });
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return Save(mesh);
    }

    private static GameObject Part(Transform parent, string name, Mesh mesh, Material m, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    private static void Drum()
    {
        const float r = 0.29f, h = 0.88f;
        var root = new GameObject("Barrel");
        var body = M("DrumBody", Color.white, "DrumBody", 0.45f, 0.25f);
        var lid = M("DrumLid", Color.white, "DrumLid", 0.4f, 0.3f);
        var rim = M("DrumRim", new Color(0.62f, 0.47f, 0.05f), null, 0.4f, 0.4f);
        Part(root.transform, "Side", DrumMesh(r, h, 40), body, Vector3.zero);
        Part(root.transform, "Lid", Disc("FuelDrumLid", r, 40, true), lid, Vector3.up * h);
        Part(root.transform, "Bottom", Disc("FuelDrumBottom", r, 24, false), rim, Vector3.up * 0.002f);
        // the rolling hoops and the rims stand out of the side a little
        foreach (float y in new[] { 0.29f, 0.59f })
        {
            var hoop = Rod(root.transform, "Hoop", new Vector3(0, y - 0.012f, 0), new Vector3(0, y + 0.012f, 0), r * 2f + 0.02f, rim);
        }
        Rod(root.transform, "TopRim", new Vector3(0, h - 0.006f, 0), new Vector3(0, h + 0.022f, 0), r * 2f + 0.016f, rim);
        // the lid's inside showing at the rim
        var cap = root.AddComponent<CapsuleCollider>();
        cap.radius = r + 0.02f; cap.height = h + 0.04f; cap.center = new Vector3(0, h / 2f, 0);
        Prefab(root, "Barrel");
    }

    // ------------------------------------------------------------------ C4

    private static void C4(bool planted)
    {
        var root = new GameObject(planted ? "C4Planted" : "C4Pack");
        var wrap = M("C4", Color.white, "C4", 0.25f);
        var black = AssetSetup.Solid("Loot_C4Box", new Color(0.06f, 0.06f, 0.06f), 0.45f, 0.2f);
        var steel = AssetSetup.Solid("Loot_C4Antenna", new Color(0.35f, 0.35f, 0.36f), 0.6f, 0.8f);
        var red = AssetSetup.Solid("Loot_WireRed", new Color(0.75f, 0.06f, 0.04f), 0.4f);
        var blue = AssetSetup.Solid("Loot_WireBlue", new Color(0.08f, 0.2f, 0.7f), 0.4f);
        var screen = AssetSetup.Mat("Loot_C4Screen", new Color(0.05f, 0.2f, 0.08f), 0.8f, 0f, null, null, null, null, new Color(0.15f, 0.9f, 0.3f) * 0.6f);
        var ledOn = AssetSetup.Mat("Loot_C4Led", new Color(1f, 0.1f, 0.05f), 0.8f, 0f, null, null, null, null, new Color(1f, 0.12f, 0.05f) * 3f);
        var ledOff = AssetSetup.Solid("Loot_C4LedOff", new Color(0.25f, 0.03f, 0.02f), 0.8f);
        // two blocks side by side (1.3 times their real size: seen on a phone), the detonator on them
        const float k = 1.3f;
        var bx = new Vector3(0.28f, 0.052f, 0.075f) * k;
        foreach (float s in new[] { -1f, 1f })
            Box(root.transform, "Block", new Vector3(0, bx.y / 2f, s * (bx.z / 2f + 0.002f)), bx, wrap);
        var det = new Vector3(0.085f, 0.034f, 0.06f) * k;
        Box(root.transform, "Detonator", new Vector3(0.02f, bx.y + det.y / 2f, 0), det, black);
        Box(root.transform, "Screen", new Vector3(0.0f, bx.y + det.y + 0.001f, 0), new Vector3(0.045f, 0.002f, 0.03f) * k, screen);
        Rod(root.transform, "Antenna", new Vector3(0.06f * k, bx.y + det.y * 0.8f, 0.02f * k), new Vector3(0.11f * k, bx.y + det.y + 0.13f * k, 0.035f * k), 0.006f, steel);
        Rod(root.transform, "WireRed", new Vector3(-0.025f * k, bx.y + 0.01f, 0.03f * k), new Vector3(-0.09f * k, bx.y + 0.002f, 0.06f * k), 0.008f, red);
        Rod(root.transform, "WireBlue", new Vector3(-0.025f * k, bx.y + 0.01f, -0.03f * k), new Vector3(-0.09f * k, bx.y + 0.002f, -0.06f * k), 0.008f, blue);
        var led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(led.GetComponent<Collider>());
        led.name = "Led";
        led.transform.SetParent(root.transform, false);
        led.transform.localPosition = new Vector3(0.045f * k, bx.y + det.y + 0.003f, -0.018f * k);
        led.transform.localScale = Vector3.one * 0.014f;
        led.GetComponent<Renderer>().sharedMaterial = ledOff;
        if (planted)
        {
            var on = Object.Instantiate(led, root.transform);
            on.name = "LedOn";
            on.transform.localScale = Vector3.one * 0.02f;
            on.GetComponent<Renderer>().sharedMaterial = ledOn;
            on.SetActive(false);
        }
        Prefab(root, planted ? "C4Planted" : "C4Pack");
    }

    // ------------------------------------------------------------------ crate

    private static void Crate()
    {
        var root = new GameObject("Crate");
        var wood = M("Crate", Color.white, "Crate", 0.25f);
        var iron = AssetSetup.Solid("Loot_CrateIron", new Color(0.16f, 0.16f, 0.15f), 0.4f, 0.6f);
        var size = new Vector3(0.95f, 0.55f, 0.6f);
        Box(root.transform, "Body", Vector3.up * size.y / 2f, size, wood);
        // handles on the ends, a band of steel round the lid
        foreach (float s in new[] { -1f, 1f })
            Box(root.transform, "Handle", new Vector3(s * (size.x / 2f + 0.02f), size.y * 0.62f, 0), new Vector3(0.04f, 0.05f, 0.24f), iron);
        Box(root.transform, "LidBand", new Vector3(0, size.y - 0.04f, 0), new Vector3(size.x + 0.01f, 0.05f, size.z + 0.01f), iron);
        Box(root.transform, "Latch", new Vector3(0, size.y - 0.1f, -size.z / 2f - 0.012f), new Vector3(0.09f, 0.1f, 0.025f), iron);
        Prefab(root, "Crate");
    }

    /// <summary>The military crate of the hot spots: a dark hard case with ribs, corner guards, four latches and handles.</summary>
    private static void MilitaryCrate()
    {
        var root = new GameObject("CrateMilitary");
        var shell = M("CrateMilitary", Color.white, "MilitaryCrate", 0.38f, 0.1f);
        var black = AssetSetup.Solid("Loot_CaseBlack", new Color(0.05f, 0.05f, 0.055f), 0.35f, 0.1f);
        var steel = AssetSetup.Solid("Loot_CaseSteel", new Color(0.5f, 0.5f, 0.48f), 0.6f, 0.85f);
        var size = new Vector3(1.15f, 0.62f, 0.72f);
        Box(root.transform, "Body", Vector3.up * size.y / 2f, size, shell);
        // the lid's seam and the ribs round it
        Box(root.transform, "Seam", new Vector3(0, size.y * 0.7f, 0), new Vector3(size.x + 0.012f, 0.03f, size.z + 0.012f), black);
        foreach (float x in new[] { -0.38f, 0.38f })
            Box(root.transform, "Rib", new Vector3(x, size.y / 2f, 0), new Vector3(0.05f, size.y + 0.01f, size.z + 0.02f), black);
        // corner guards
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                foreach (float y in new[] { 0.03f, size.y - 0.03f })
                    Box(root.transform, "Corner", new Vector3(x * (size.x / 2f - 0.03f), y, z * (size.z / 2f - 0.03f)), new Vector3(0.09f, 0.08f, 0.09f), black);
        // latches on the front and the back, handles on the ends
        foreach (float z in new[] { -1f, 1f })
            foreach (float x in new[] { -0.2f, 0.2f })
                Box(root.transform, "Latch", new Vector3(x, size.y * 0.7f, z * (size.z / 2f + 0.015f)), new Vector3(0.08f, 0.1f, 0.025f), steel);
        foreach (float x in new[] { -1f, 1f })
            Box(root.transform, "Handle", new Vector3(x * (size.x / 2f + 0.025f), size.y * 0.6f, 0), new Vector3(0.04f, 0.04f, 0.26f), black);
        Prefab(root, "CrateMilitary");
    }

    /// <summary>
    /// The supply drop's crate: a steel frame round stencilled panels on a wooden pallet, the harness the
    /// parachute's lines hold, a red smoke canister and the strobe's housing on the lid.
    /// </summary>
    private static void AirdropCrate()
    {
        var root = new GameObject("AirdropCrate");
        var panel = M("AirdropPanel", Color.white, "Airdrop", 0.32f, 0.15f);
        var frame = AssetSetup.Solid("Loot_DropFrame", new Color(0.2f, 0.21f, 0.2f), 0.45f, 0.7f);
        var wood = AssetSetup.Solid("Loot_DropPallet", new Color(0.42f, 0.32f, 0.2f), 0.15f);
        var strap = AssetSetup.Solid("Loot_DropStrap", new Color(0.62f, 0.52f, 0.2f), 0.2f);
        var red = AssetSetup.Mat("Loot_DropCanister", new Color(0.8f, 0.1f, 0.06f), 0.4f, 0.2f);
        const float s = 1.3f, h = 1.0f, pallet = 0.12f;
        // the pallet
        foreach (float z in new[] { -0.5f, 0f, 0.5f })
            Box(root.transform, "Skid", new Vector3(0, pallet * 0.35f, z * s * 0.9f), new Vector3(s, pallet * 0.7f, 0.12f), wood);
        Box(root.transform, "Deck", new Vector3(0, pallet * 0.85f, 0), new Vector3(s + 0.04f, pallet * 0.3f, s + 0.04f), wood);
        // the box and its steel frame (twelve edges)
        float y0 = pallet;
        Box(root.transform, "Body", new Vector3(0, y0 + h / 2f, 0), new Vector3(s, h, s), panel);
        const float e = 0.07f;
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                Box(root.transform, "Post", new Vector3(x * s / 2f, y0 + h / 2f, z * s / 2f), new Vector3(e, h + e, e), frame);
        foreach (float y in new[] { y0, y0 + h })
            foreach (float k in new[] { -1f, 1f })
            {
                Box(root.transform, "Edge", new Vector3(0, y, k * s / 2f), new Vector3(s + e, e, e), frame);
                Box(root.transform, "Edge", new Vector3(k * s / 2f, y, 0), new Vector3(e, e, s + e), frame);
            }
        // the harness: two straps over the lid, crossing
        Box(root.transform, "Strap", new Vector3(0, y0 + h + 0.012f, 0), new Vector3(0.1f, 0.02f, s + 0.02f), strap);
        Box(root.transform, "Strap", new Vector3(0, y0 + h + 0.014f, 0), new Vector3(s + 0.02f, 0.02f, 0.1f), strap);
        foreach (float x in new[] { -1f, 1f })
        {
            Box(root.transform, "StrapSide", new Vector3(x * (s / 2f + 0.012f), y0 + h / 2f, 0), new Vector3(0.02f, h, 0.1f), strap);
            Box(root.transform, "StrapSide", new Vector3(0, y0 + h / 2f, x * (s / 2f + 0.012f)), new Vector3(0.1f, h, 0.02f), strap);
        }
        // the smoke canister in a corner of the lid, the strobe's housing in the middle
        Rod(root.transform, "Canister", new Vector3(0.45f, y0 + h, 0.45f), new Vector3(0.45f, y0 + h + 0.24f, 0.45f), 0.09f, red);
        Box(root.transform, "StrobeBase", new Vector3(0, y0 + h + 0.04f, 0), new Vector3(0.12f, 0.06f, 0.12f), frame);
        Prefab(root, "AirdropCrate");
    }

    /// <summary>
    /// A cargo parachute over the crate (its pivot at the crate's foot): a canopy of sixteen gores, orange and
    /// white, its lines gathered to a ring over the crate and four risers down to the harness.
    /// </summary>
    private static void Parachute()
    {
        var root = new GameObject("Parachute");
        var cloth = M("Parachute", Color.white, "Parachute", 0.12f);
        var cord = AssetSetup.Solid("Loot_ChuteCord", new Color(0.85f, 0.82f, 0.72f), 0.2f);
        const int gores = 16, rings = 7;
        const float radius = 3.6f, rise = 2.3f, baseY = 6.6f;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float a = r / (float)rings * Mathf.PI / 2f;            // from the rim (0) to the top (pi/2)
            float ring = Mathf.Cos(a) * radius, y = baseY + Mathf.Sin(a) * rise;
            for (int g = 0; g <= gores; g++)
            {
                float b = g / (float)gores * Mathf.PI * 2f;
                // the cloth bulges between the lines a little
                float bulge = 1f + 0.035f * Mathf.Sin(b * gores) * Mathf.Sin(b * gores) * Mathf.Cos(a);
                var p = new Vector3(Mathf.Sin(b) * ring * bulge, y, Mathf.Cos(b) * ring * bulge);
                v.Add(p);
                n.Add(new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Sin(a) * radius / rise, Mathf.Cos(b) * Mathf.Cos(a)).normalized);
                uv.Add(new Vector2(g / (float)gores * 4f, r / (float)rings));
            }
        }
        int row = gores + 1;
        for (int r = 0; r < rings; r++)
            for (int g = 0; g < gores; g++)
            {
                int i0 = r * row + g, i1 = i0 + 1, j0 = i0 + row, j1 = j0 + 1;
                t.AddRange(new[] { i0, j0, i1, i1, j0, j1, i0, i1, j0, i1, j1, j0 });   // both faces: seen from below too
            }
        var mesh = new Mesh { name = "ParachuteCanopy" };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        Part(root.transform, "Canopy", Save(mesh), cloth, Vector3.zero);
        // the lines from the rim to a ring over the crate, and four risers on to the harness's corners
        var gather = new Vector3(0, 2.9f, 0);
        for (int g = 0; g < gores; g += 2)
        {
            float b = g / (float)gores * Mathf.PI * 2f;
            Rod(root.transform, "Line", new Vector3(Mathf.Sin(b) * radius, baseY, Mathf.Cos(b) * radius), gather, 0.012f, cord);
        }
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                Rod(root.transform, "Riser", gather, new Vector3(x * 0.6f, 1.14f, z * 0.6f), 0.02f, cord);
        Prefab(root, "Parachute");
    }

    /// <summary>
    /// The cargo plane of the supply drop (seen high up and far, so made of simple shapes): a long body with a
    /// rounded nose and a tail that sweeps up, a high wing with four engines and their propellers (named Prop,
    /// turned by the game), a tall fin and a tailplane; grey paint, dark windows.
    /// </summary>
    private static void Plane()
    {
        var root = new GameObject("Plane");
        var paint = AssetSetup.Solid("Loot_PlaneGrey", new Color(0.43f, 0.45f, 0.47f), 0.45f, 0.35f);
        var dark = AssetSetup.Solid("Loot_PlaneDark", new Color(0.08f, 0.09f, 0.1f), 0.8f, 0.2f);
        var prop = AssetSetup.Solid("Loot_PlaneProp", new Color(0.12f, 0.12f, 0.12f), 0.3f, 0.3f);
        // the body along +Z (the way it flies)
        var body = Rod(root.transform, "Body", new Vector3(0, 0, -9f), new Vector3(0, 0, 10f), 4f, paint, PrimitiveType.Capsule);
        body.transform.localScale = new Vector3(4f, 12.5f, 4.2f);
        var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        nose.name = "Nose";
        nose.transform.SetParent(root.transform, false);
        nose.transform.localPosition = new Vector3(0, -0.2f, 11.2f);
        nose.transform.localScale = new Vector3(3.6f, 3.4f, 4.6f);
        nose.GetComponent<Renderer>().sharedMaterial = paint;
        // the cockpit windows
        Box(root.transform, "Windows", new Vector3(0, 0.9f, 12.4f), new Vector3(2.2f, 0.45f, 0.9f), dark, new Vector3(-22f, 0, 0));
        // the tail sweeping up to the fin
        Box(root.transform, "Tail", new Vector3(0, 1.0f, -13.5f), new Vector3(2.6f, 2.2f, 7f), paint, new Vector3(-12f, 0, 0));
        Box(root.transform, "Fin", new Vector3(0, 4.6f, -15.2f), new Vector3(0.35f, 6.2f, 4.6f), paint, new Vector3(-14f, 0, 0));
        Box(root.transform, "Tailplane", new Vector3(0, 2.1f, -15.6f), new Vector3(15f, 0.28f, 3.4f), paint);
        // the high wing and the four engines under it
        Box(root.transform, "Wing", new Vector3(0, 2.2f, 1.5f), new Vector3(40f, 0.5f, 4.4f), paint);
        foreach (float x in new[] { -11.5f, -6f, 6f, 11.5f })
        {
            Rod(root.transform, "Engine", new Vector3(x, 1.6f, 0.2f), new Vector3(x, 1.6f, 4.8f), 1.15f, paint);
            var hub = new GameObject("Prop");
            hub.transform.SetParent(root.transform, false);
            hub.transform.localPosition = new Vector3(x, 1.6f, 5.0f);
            for (int b = 0; b < 4; b++)
            {
                var blade = Box(hub.transform, "Blade", Vector3.zero, new Vector3(0.28f, 4.2f, 0.06f), prop);
                blade.transform.localRotation = Quaternion.Euler(0, 0, b * 45f + 22.5f);
                blade.transform.localPosition = Vector3.zero;
            }
            Rod(hub.transform, "Spinner", new Vector3(0, 0, -0.1f), new Vector3(0, 0, 0.55f), 0.5f, dark);
        }
        Prefab(root, "Plane");
    }

    /// <summary>Two frag grenades on an olive pouch.</summary>
    private static void GrenadePack()
    {
        var root = new GameObject("GrenadePack");
        var pouch = AssetSetup.Solid("Loot_GrenadePouch", new Color(0.26f, 0.27f, 0.17f), 0.2f);
        var body = AssetSetup.Solid("Loot_GrenadeBody", new Color(0.24f, 0.28f, 0.17f), 0.4f, 0.2f);
        var steel = AssetSetup.Solid("Loot_GrenadeSpoon", new Color(0.55f, 0.55f, 0.52f), 0.6f, 0.8f);
        Box(root.transform, "Pouch", new Vector3(0, 0.03f, 0), new Vector3(0.3f, 0.06f, 0.2f), pouch);
        foreach (float x in new[] { -0.07f, 0.07f })
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = "Grenade";
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = new Vector3(x, 0.12f, 0);
            g.transform.localScale = new Vector3(0.11f, 0.13f, 0.11f);
            g.GetComponent<Renderer>().sharedMaterial = body;
            Box(root.transform, "Fuze", new Vector3(x, 0.2f, 0), new Vector3(0.035f, 0.035f, 0.035f), steel);
            Box(root.transform, "Spoon", new Vector3(x + 0.025f, 0.16f, 0), new Vector3(0.012f, 0.09f, 0.025f), steel, new Vector3(0, 0, -12f));
        }
        Prefab(root, "GrenadePack");
    }

    // ------------------------------------------------------------------ vests

    /// <summary>
    /// A plate carrier laid open on the ground: front and back panels, shoulder straps, pouches (the heavy one:
    /// more, and its plates thicker; the military one from the supply drops: coyote brown, side plates and a collar).
    /// </summary>
    private static void Vest(bool heavy, bool military = false)
    {
        string name = military ? "VestMilitary" : heavy ? "VestHeavy" : "VestLight";
        var root = new GameObject(name);
        var cloth = M(name, Color.white, name, 0.15f);
        var webbing = AssetSetup.Solid(heavy ? "Loot_WebbingDark" : "Loot_Webbing", heavy ? new Color(0.06f, 0.06f, 0.07f) : new Color(0.26f, 0.25f, 0.17f), 0.2f);
        float t = heavy ? 0.07f : 0.045f;
        var panel = new Vector3(0.36f, t, 0.46f);
        foreach (float s in new[] { -1f, 1f })
        {
            Box(root.transform, "Panel", new Vector3(s * 0.2f, t / 2f, 0), panel, cloth, new Vector3(0, s * 4f, 0));
            // the straps over the shoulders join the two halves
            Box(root.transform, "Strap", new Vector3(0, t * 0.6f, s * 0.15f), new Vector3(0.12f, 0.018f, 0.06f), webbing);
            // MOLLE rows across each panel
            for (int k = 0; k < 4; k++)
                Box(root.transform, "Molle", new Vector3(s * 0.2f, t + 0.003f, -0.15f + k * 0.08f), new Vector3(0.3f, 0.006f, 0.022f), webbing);
        }
        int pouches = heavy ? 3 : 2;
        for (int k = 0; k < pouches; k++)
            Box(root.transform, "Pouch", new Vector3(-0.28f + k * 0.12f + (heavy ? 0f : 0.12f), t + 0.035f, -0.12f), new Vector3(0.09f, 0.07f, 0.13f), cloth);
        if (heavy)
            Box(root.transform, "Plate", new Vector3(0.2f, t + 0.012f, 0.08f), new Vector3(0.25f, 0.02f, 0.28f), webbing);
        if (military)
        {
            // side plates and a collar
            foreach (float s in new[] { -1f, 1f })
                Box(root.transform, "SidePlate", new Vector3(s * 0.42f, t * 0.6f, 0.02f), new Vector3(0.1f, t * 1.2f, 0.26f), cloth, new Vector3(0, 0, s * 18f));
            Box(root.transform, "Collar", new Vector3(0, t + 0.03f, 0.24f), new Vector3(0.3f, 0.06f, 0.07f), cloth);
        }
        Prefab(root, name);
    }
}
