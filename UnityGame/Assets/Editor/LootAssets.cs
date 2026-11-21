using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The things of the ADVANCED game, made into Resources/Loot: the yellow fuel drum with its skull sign,
/// the C4 pack (lying as loot, and planted with its blinking lamp), the supply crate and the two vests.
/// Their textures are drawn by Tools/loot/textures.py (Assets/Textures/Loot).
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
        Vest(false);
        Vest(true);
        AssetDatabase.SaveAssets();
        Debug.Log("[Setup] loot: drum, C4, crate, vests");
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

    // ------------------------------------------------------------------ vests

    /// <summary>A plate carrier laid open on the ground: front and back panels, shoulder straps, pouches (the heavy one: more, and its plates thicker).</summary>
    private static void Vest(bool heavy)
    {
        var root = new GameObject(heavy ? "VestHeavy" : "VestLight");
        var cloth = M(heavy ? "VestHeavy" : "VestLight", Color.white, heavy ? "VestHeavy" : "VestLight", 0.15f);
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
        Prefab(root, heavy ? "VestHeavy" : "VestLight");
    }
}
