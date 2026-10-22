using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "PORT": an industrial harbour yard surrounded by the sea, in the style of classic mobile shooter maps:
/// corrugated steel warehouses with gable roofs, a brick office, oil tanks with a pipe bridge, yellow
/// silos, stacked shipping containers, concrete walls with pillars, raised platforms with stairs,
/// chain-link fences, barrels, crates and pallets. Textures are CC0 PBR sets from ambientCG and the
/// props come from Poly Haven (CC0).
/// </summary>
public static class PortBuilder
{
    public const string Textures = "Assets/ThirdParty/Port/Textures";
    public const string Props = "Assets/ThirdParty/Props";
    public const float HalfX = 92f, HalfZ = 64f;
    // the sunken loading pit in the east of the yard (x0, z0, width, depth) and its depth
    private static readonly Rect Pit = new Rect(72f, -34f, 14f, 22f);
    private const float PitDepth = 4f;
    // a service tunnel runs north from the pit under the yard to a stair up near the pump house,
    // with a pump room off its side
    private static readonly Rect Tunnel = Rect.MinMaxRect(76f, -12f, 82f, 31.6f);
    private static readonly Rect PumpRoom = Rect.MinMaxRect(82f, 4f, 90f, 14f);
    private static readonly Rect ExitHole = Rect.MinMaxRect(77.2f, 24f, 80.8f, 31.6f);
    private static bool InPit(Vector3 p, float margin = 1.5f) => Underground.InAny(new[] { Pit, ExitHole }, p, margin);

    private static Transform root;
    private static System.Random rng;
    private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    private static readonly Dictionary<string, GameObject> props = new Dictionary<string, GameObject>();

    private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ materials

    private static Texture2D T(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{file}") ;

    public static Material Pbr(string name, string set, Color tint, float smoothness = 0.8f, float metallic = 0f)
    {
        if (mats.TryGetValue(name, out var m)) return m;
        var albedo = T(set + "_BaseColor.jpg");
        if (albedo == null) Debug.LogWarning("[Port] missing texture set " + set);
        return mats[name] = AssetSetup.Mat("Port_" + name, tint, smoothness, metallic, albedo, T(set + "_Normal.jpg"), T(set + "_MS.png"));
    }

    private static Material WallWhite => Pbr("WallWhite", "CorrugatedSteel005", new Color(0.92f, 0.93f, 0.94f));
    private static Material WallTeal => Pbr("WallTeal", "CorrugatedSteel005", new Color(0.55f, 0.82f, 0.8f));
    private static Material WallRed => Pbr("WallRed", "CorrugatedSteel005", new Color(0.85f, 0.42f, 0.36f));
    private static Material WallRust => Pbr("WallRust", "CorrugatedSteel007A", Color.white);
    private static Material Roof => Pbr("Roof", "CorrugatedSteel009", new Color(0.82f, 0.86f, 0.9f));
    private static Material RoofRust => Pbr("RoofRust", "CorrugatedSteel007A", new Color(0.95f, 0.85f, 0.8f));
    private static Material Concrete => Pbr("Concrete", "Concrete034", Color.white, 0.6f);
    private static Material OldConcrete => Pbr("OldConcrete", "Concrete042A", Color.white, 0.6f);
    private static Material Asphalt => Pbr("Asphalt", "Asphalt025C", new Color(0.85f, 0.85f, 0.88f), 0.6f);
    private static Material TankPaint => Pbr("TankPaint", "Metal032", new Color(0.9f, 0.92f, 0.93f), 0.7f);
    private static Material RustPlates => Pbr("RustPlates", "MetalPlates013", Color.white, 0.7f);
    private static Material Bricks => Pbr("Bricks", "Bricks076A", Color.white, 0.6f);
    private static Material Plaster => Pbr("Plaster", "PaintedPlaster017", new Color(0.88f, 0.9f, 0.92f), 0.6f);
    private static Material Walkway => Pbr("Walkway", "DiamondPlate009", new Color(0.8f, 0.8f, 0.8f), 0.8f);
    private static Material Wood => Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f);
    private static Material Planks => Pbr("Planks", "Planks037A", Color.white, 0.5f);

    /// <summary>Container paint: five colours of worn paint and two weathered, rusty ones.</summary>
    private static Material ContainerMat(int i)
    {
        Color[] colors = { new Color(0.16f, 0.32f, 0.58f), new Color(0.6f, 0.17f, 0.13f), new Color(0.22f, 0.42f, 0.26f), new Color(0.78f, 0.45f, 0.1f), new Color(0.7f, 0.7f, 0.67f) };
        i %= colors.Length + 2;
        if (i == colors.Length) return Pbr("ContainerRust", "MetalPlates013", new Color(0.85f, 0.6f, 0.5f), 0.4f);
        if (i == colors.Length + 1) return Pbr("ContainerRustBlue", "MetalPlates013", new Color(0.5f, 0.62f, 0.75f), 0.4f);
        return Pbr("Container" + i, "Metal032", colors[i], 0.45f);
    }

    private static Material Glass => AssetSetup.Solid("Port_Glass", new Color(0.1f, 0.14f, 0.18f), 0.95f, 0.5f);
    private static Material DarkMetal => AssetSetup.Solid("Port_DarkMetal", new Color(0.22f, 0.23f, 0.24f), 0.5f, 0.6f);
    private static Material Yellow => AssetSetup.Solid("Port_Yellow", new Color(0.9f, 0.72f, 0.15f), 0.5f, 0.3f);
    private static Material BlueDrum => AssetSetup.Solid("Port_BlueDrum", new Color(0.12f, 0.35f, 0.75f), 0.6f, 0.4f);

    private static Material Fence()
    {
        if (mats.TryGetValue("Fence", out var cached)) return cached;
        // chain-link pattern made here: diamond wires on a transparent background
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = Mathf.Abs(((x + y) % 32) - 16f), b = Mathf.Abs(((x - y + 512) % 32) - 16f);
                float wire = Mathf.Clamp01(2.2f - Mathf.Min(a, b));
                tex.SetPixel(x, y, new Color(0.45f, 0.42f, 0.38f, wire));
            }
        tex.Apply();
        string path = "Assets/Generated/Textures/ChainLink.png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        var m = AssetSetup.Mat("Port_Fence", Color.white, 0.6f, 0.7f, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        m.SetFloat("_Mode", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.renderQueue = 2450;
        m.SetOverrideTag("RenderType", "TransparentCutout");
        EditorUtility.SetDirty(m);
        return mats["Fence"] = m;
    }

    // ------------------------------------------------------------------ props from Poly Haven

    private static void LoadProps()
    {
        props.Clear();
        if (!AssetDatabase.IsValidFolder(Props)) return;
        AssetSetup.Folder("Assets/Generated/Props");
        foreach (var dir in AssetDatabase.GetSubFolders(Props))
        {
            string id = System.IO.Path.GetFileName(dir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{id}.fbx");
            if (model == null) continue;
            var mat = AssetSetup.Mat("Prop_" + id, Color.white, 1f, 0f,
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{id}_BaseColor.jpg"),
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{id}_Normal.jpg"),
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{id}_MS.png"));
            var go = (GameObject)Object.Instantiate(model);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            var b = Bounds(go);
            if (b.size.y > 20f || b.size.y < 0.05f) go.transform.localScale *= b.size.y > 20f ? 0.01f : 100f;
            b = Bounds(go);
            var holder = new GameObject(id);
            go.transform.SetParent(holder.transform, true);
            go.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            var box = holder.AddComponent<BoxCollider>();
            box.center = new Vector3(0, b.size.y / 2f, 0);
            box.size = b.size;
            props[id] = PrefabUtility.SaveAsPrefabAsset(holder, $"Assets/Generated/Props/{id}.prefab");
            Object.DestroyImmediate(holder);
            Debug.Log($"[Port] prop {id} size {b.size}");
        }
    }

    private static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static GameObject Prop(string id, Vector3 pos, float yaw, Transform parent)
    {
        if (!props.TryGetValue(id, out var prefab) || prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        return go;
    }

    // ------------------------------------------------------------------ building blocks

    private static GameObject Box(string name, Vector3 center, Vector3 size, Material m, float tex, Transform parent, float yaw = 0, bool collider = true)
        => MeshGen.TexBox(name, center, size, m, tex, parent, yaw, collider);

    /// <summary>Warehouse with corrugated walls, a concrete plinth, a coloured band, doors, windows and a gable roof.</summary>
    private static void Warehouse(Vector3 c, float w, float l, float h, Material wall, Material band, Material roof, float yaw)
    {
        var t = new GameObject("Warehouse").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(c, Quaternion.Euler(0, yaw, 0));
        Box("Walls", t.TransformPoint(new Vector3(0, h / 2f, 0)), new Vector3(w, h, l), wall, 3f, t, yaw);
        Box("Plinth", t.TransformPoint(new Vector3(0, 0.45f, 0)), new Vector3(w + 0.2f, 0.9f, l + 0.2f), Concrete, 2f, t, yaw, false);
        if (band != null) Box("Band", t.TransformPoint(new Vector3(0, h - 1.3f, 0)), new Vector3(w + 0.08f, 1.8f, l + 0.08f), band, 3f, t, yaw, false);
        float rise = w * 0.22f;
        MeshGen.Object("Roof", MeshGen.GableRoof(w, l, rise, 0.5f, 3f), roof, t, t.TransformPoint(new Vector3(0, h, 0)), yaw).AddComponent<MeshCollider>();
        foreach (float s in new[] { -1f, 1f })
        {
            MeshGen.Object("Gable", MeshGen.Gable(w, rise, 3f), wall, t, t.TransformPoint(new Vector3(0, h, s * l / 2f)), yaw);
            // big roller door and windows on both long ends
            Box("Door", t.TransformPoint(new Vector3(0, 2.4f, s * (l / 2f + 0.06f))), new Vector3(Mathf.Min(5f, w * 0.4f), 4.8f, 0.12f), DarkMetal, 1f, t, yaw, false);
            Box("DoorFrame", t.TransformPoint(new Vector3(0, 4.95f, s * (l / 2f + 0.1f))), new Vector3(Mathf.Min(5.4f, w * 0.44f), 0.3f, 0.2f), Concrete, 1f, t, yaw, false);
        }
        foreach (float s in new[] { -1f, 1f })
            for (float z = -l / 2f + 3f; z < l / 2f - 2f; z += 4.5f)
                Box("Window", t.TransformPoint(new Vector3(s * (w / 2f + 0.05f), h - 3.2f, z)), new Vector3(0.1f, 1.1f, 2.6f), Glass, 1f, t, yaw, false);
        // roof vents
        for (float z = -l / 2f + 4f; z < l / 2f - 3f; z += 8f)
            MeshGen.TexCylinder("Vent", t.TransformPoint(new Vector3(0, h + rise - 0.1f, z)), 0.18f, 1.2f, DarkMetal, 1f, t, false);
    }

    private static void Tank(Vector3 c, float radius, float height)
    {
        var t = new GameObject("Tank").transform;
        t.SetParent(root, false);
        t.position = c;
        MeshGen.TexCylinder("Shell", c, radius, height, TankPaint, 3f, t);
        MeshGen.TexCylinder("Rust", c, radius + 0.02f, 0.8f, RustPlates, 2f, t, false);
        MeshGen.TexCylinder("Lid", c + Vector3.up * height, radius + 0.15f, 0.25f, RustPlates, 2f, t, false);
        for (int i = 0; i < 2; i++)
            MeshGen.TexCylinder("Pipe", c + new Vector3(radius + 0.35f, 0, (i - 0.5f) * 0.9f), 0.25f, height * 0.75f, TankPaint, 1f, t, false);
        // ladder
        for (float y = 0.4f; y < height; y += 0.45f)
            Box("Rung", c + new Vector3(-radius - 0.3f, y, 0), new Vector3(0.05f, 0.05f, 0.6f), DarkMetal, 1f, t, 0, false);
    }

    private static void Pipe(Vector3 a, Vector3 b, float radius, Transform parent)
    {
        var dir = b - a;
        var go = MeshGen.Object("Pipe", MeshGen.Cylinder(radius, dir.magnitude, 2f, 16), TankPaint, parent, a);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
    }

    private static void Container(Vector3 bottomCenter, float yaw, int color, Transform parent)
    {
        var go = MeshGen.Object("Container", MeshGen.Container(), ContainerMat(color), parent, bottomCenter, yaw);
        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { ContainerMat(color), DarkMetal };
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(2.44f, 2.59f, 6.06f);
        box.center = new Vector3(0, 2.59f / 2f, 0);
    }

    private static void ContainerStack(Vector3 c, float yaw, int columns, int maxHeight)
    {
        var t = new GameObject("Containers").transform;
        t.SetParent(root, false);
        var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
        for (int i = 0; i < columns; i++)
        {
            int height = 1 + rng.Next(maxHeight);
            for (int k = 0; k < height; k++)
                Container(c + right * (i * 2.6f) + Vector3.up * (k * 2.6f), yaw + (k > 0 ? R(-3, 3) : 0), rng.Next(7), t);
        }
    }

    private static void ConcreteWall(Vector3 a, Vector3 b, float height, Transform parent)
    {
        var dir = b - a;
        float length = dir.magnitude;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        int panels = Mathf.Max(1, Mathf.RoundToInt(length / 4f));
        float panel = length / panels;
        for (int i = 0; i < panels; i++)
        {
            var mid = a + dir * ((i + 0.5f) / panels);
            MeshGen.RoundBox("Wall", mid + Vector3.up * height / 2f, new Vector3(0.3f, height, panel), 0.05f, Concrete, 2.5f, parent, yaw);
            var post = a + dir * ((float)i / panels);
            MeshGen.RoundBox("Pillar", post + Vector3.up * (height + 0.3f) / 2f, new Vector3(0.6f, height + 0.3f, 0.6f), 0.1f, OldConcrete, 1.5f, parent, yaw);
        }
        MeshGen.RoundBox("Pillar", b + Vector3.up * (height + 0.3f) / 2f, new Vector3(0.6f, height + 0.3f, 0.6f), 0.1f, OldConcrete, 1.5f, parent, yaw);
    }

    private static void FenceLine(Vector3 a, Vector3 b, Transform parent)
    {
        var dir = b - a;
        float length = dir.magnitude;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        var mid = (a + b) / 2f;
        MeshGen.RoundBox("FenceBase", mid + Vector3.up * 0.35f, new Vector3(0.35f, 0.7f, length), 0.06f, Concrete, 2f, parent, yaw);
        // the mesh does not stop bullets or sight, only players
        var fence = Box("ChainLink", mid + Vector3.up * 1.75f, new Vector3(0.02f, 2.1f, length), Fence(), 2f, parent, yaw, false);
        var blocker = new GameObject("FenceBlocker");
        blocker.layer = World.FenceLayer;
        blocker.transform.SetParent(fence.transform, false);
        blocker.AddComponent<BoxCollider>().size = new Vector3(0.3f, 1f, 1f);
        int posts = Mathf.CeilToInt(length / 3f);
        for (int i = 0; i <= posts; i++)
            Box("FencePost", a + dir * ((float)i / posts) + Vector3.up * 1.4f, new Vector3(0.08f, 2.8f, 0.08f), RustPlates, 1f, parent, yaw, false);
        Box("FenceTop", mid + Vector3.up * 2.8f, new Vector3(0.06f, 0.06f, length), RustPlates, 1f, parent, yaw, false);
    }

    /// <summary>Raised concrete platform with retaining walls and a staircase on one side.</summary>
    private static void Platform(Vector3 c, float w, float l, float h, int stairsSide)
    {
        var t = new GameObject("Platform").transform;
        t.SetParent(root, false);
        Box("Deck", c + Vector3.up * (h / 2f), new Vector3(w, h, l), OldConcrete, 3f, t);
        Box("Rim", c + Vector3.up * (h + 0.45f), new Vector3(w, 0.9f, 0.3f), Concrete, 2f, t, 0, true).transform.position += new Vector3(0, 0, l / 2f - 0.15f);
        var dir = stairsSide == 0 ? Vector3.back : stairsSide == 1 ? Vector3.right : Vector3.left;
        float steps = Mathf.Ceil(h / 0.25f);
        float run = 0.38f;
        var start = c + Vector3.Scale(dir, new Vector3(w / 2f, 0, l / 2f));
        var across = Vector3.Cross(Vector3.up, dir);
        for (int i = 0; i < steps; i++)
        {
            float stepH = h - i * 0.25f;
            var p = start + dir * (run * (i + 0.5f)) + Vector3.up * (stepH / 2f);
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Box("Step", p, new Vector3(3f, stepH, run), Concrete, 1.5f, t, yaw);
        }
        foreach (float s in new[] { -1f, 1f })
        {
            var p = start + dir * (run * steps / 2f) + across * (s * 1.65f) + Vector3.up * (h / 2f + 0.3f);
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Box("StairWall", p, new Vector3(0.3f, h + 0.6f, run * steps), Concrete, 2f, t, yaw);
        }
    }

    private static void Pallet(Vector3 p, float yaw, int stack, Transform parent)
    {
        for (int k = 0; k < stack; k++)
        {
            var c = p + Vector3.up * (k * 0.15f);
            for (int i = -2; i <= 2; i++)
                Box("Slat", c + Quaternion.Euler(0, yaw, 0) * new Vector3(i * 0.26f, 0.13f, 0), new Vector3(0.14f, 0.025f, 1.2f), Planks, 1f, parent, yaw, false);
            foreach (float z in new[] { -0.5f, 0f, 0.5f })
                Box("Block", c + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0.05f, z), new Vector3(1.2f, 0.1f, 0.12f), Planks, 1f, parent, yaw, false);
        }
        var col = new GameObject("PalletCollider");
        col.transform.SetParent(parent, false);
        col.transform.SetPositionAndRotation(p + Vector3.up * (stack * 0.075f + 0.04f), Quaternion.Euler(0, yaw, 0));
        col.AddComponent<BoxCollider>().size = new Vector3(1.2f, stack * 0.15f + 0.05f, 1.2f);
    }

    private static void Drums(Vector3 p, int count, Transform parent)
    {
        for (int i = 0; i < count; i++)
        {
            var q = p + new Vector3((i % 3) * 0.62f, 0, (i / 3) * 0.62f);
            MeshGen.TexCylinder("Drum", q, 0.29f, 0.88f, BlueDrum, 1f, parent);
            MeshGen.TexCylinder("Rim", q + Vector3.up * 0.3f, 0.3f, 0.04f, DarkMetal, 1f, parent, false);
            MeshGen.TexCylinder("Rim", q + Vector3.up * 0.6f, 0.3f, 0.04f, DarkMetal, 1f, parent, false);
        }
    }

    /// <summary>Four grain silos: painted steel with rust streaks, ring stiffeners, a conical roof, ladders and braced legs.</summary>
    private static void Silos(Vector3 c, Transform parent)
    {
        var paint = Pbr("SiloPaint", "CorrugatedSteel005", new Color(0.92f, 0.74f, 0.2f), 0.6f);
        var rust = Pbr("SiloRust", "CorrugatedSteel007A", new Color(1f, 0.85f, 0.7f), 0.5f);
        var steel = Pbr("SiloSteel", "Metal032", new Color(0.45f, 0.46f, 0.47f), 0.6f);
        for (int i = 0; i < 4; i++)
        {
            var p = c + new Vector3((i % 2) * 3.2f, 0, (i / 2) * 3.2f);
            foreach (var (x, z) in new[] { (-0.9f, -0.9f), (0.9f, -0.9f), (-0.9f, 0.9f), (0.9f, 0.9f) })
                Box("Leg", p + new Vector3(x, 1f, z), new Vector3(0.14f, 2f, 0.14f), steel, 1f, parent, 0, true);
            Box("Brace", p + new Vector3(0, 0.9f, -0.9f), new Vector3(1.8f, 0.08f, 0.08f), steel, 1f, parent, 0, false);
            Box("Brace", p + new Vector3(0, 0.9f, 0.9f), new Vector3(1.8f, 0.08f, 0.08f), steel, 1f, parent, 0, false);
            MeshGen.TexCylinder("Silo", p + Vector3.up * 2f, 1.3f, 4.5f, paint, 2.2f, parent);
            MeshGen.TexCylinder("RustBand", p + Vector3.up * 2f, 1.32f, 0.7f, rust, 2f, parent, false);
            foreach (float y in new[] { 3.2f, 4.6f, 6.0f })
                MeshGen.TexCylinder("Ring", p + Vector3.up * y, 1.34f, 0.08f, steel, 1f, parent, false);
            MeshGen.TexCylinder("Roof", p + Vector3.up * 6.5f, 1.15f, 0.25f, steel, 1f, parent, false);
            MeshGen.TexCylinder("RoofTop", p + Vector3.up * 6.75f, 0.6f, 0.3f, steel, 1f, parent, false);
            MeshGen.TexCylinder("Cone", p + Vector3.up * 1.4f, 0.5f, 0.6f, steel, 1f, parent, false);
            // ladder up the outside
            var side = (i % 2 == 0 ? -1f : 1f);
            Box("LadderRail", p + new Vector3(side * 1.4f, 4.3f, -0.22f), new Vector3(0.05f, 4.6f, 0.05f), steel, 1f, parent, 0, false);
            Box("LadderRail", p + new Vector3(side * 1.4f, 4.3f, 0.22f), new Vector3(0.05f, 4.6f, 0.05f), steel, 1f, parent, 0, false);
            for (float y = 2.2f; y < 6.5f; y += 0.35f)
                Box("Rung", p + new Vector3(side * 1.4f, y, 0), new Vector3(0.04f, 0.04f, 0.44f), steel, 1f, parent, 0, false);
        }
    }

    /// <summary>
    /// A loading pit 2.4 m below the yard: concrete walls, stairs down at the west end, a truck ramp at
    /// the south, low concrete kerbs around the edge (cover) and a few crates and a container inside.
    /// </summary>
    private static void LoadingPit()
    {
        var t = new GameObject("LoadingPit").transform;
        t.SetParent(root, false);
        float floor = -PitDepth;
        var c = new Vector3(Pit.center.x, 0, Pit.center.y);
        Box("PitFloor", new Vector3(c.x, floor - 0.25f, c.z), new Vector3(Pit.width, 0.5f, Pit.height), OldConcrete, 3f, t);
        // concrete lining on the four walls
        Box("PitWall", new Vector3(Pit.xMin + 0.15f, floor / 2f, c.z), new Vector3(0.3f, PitDepth, Pit.height), Concrete, 2.5f, t);
        Box("PitWall", new Vector3(Pit.xMax - 0.15f, floor / 2f, c.z), new Vector3(0.3f, PitDepth, Pit.height), Concrete, 2.5f, t);
        Box("PitWall", new Vector3(c.x, floor / 2f, Pit.yMin + 0.15f), new Vector3(Pit.width, PitDepth, 0.3f), Concrete, 2.5f, t);
        // north wall: the tunnel opens out of it
        Box("PitWall", new Vector3((Pit.xMin + Tunnel.xMin) / 2f, floor / 2f, Pit.yMax - 0.15f), new Vector3(Tunnel.xMin - Pit.xMin, PitDepth, 0.3f), Concrete, 2.5f, t);
        Box("PitWall", new Vector3((Tunnel.xMax + Pit.xMax) / 2f, floor / 2f, Pit.yMax - 0.15f), new Vector3(Pit.xMax - Tunnel.xMax, PitDepth, 0.3f), Concrete, 2.5f, t);
        Box("PitWall", new Vector3(Tunnel.center.x, -0.5f, Pit.yMax - 0.15f), new Vector3(Tunnel.width, 1f, 0.3f), Concrete, 2.5f, t);
        MeshGen.TexBox("TunnelSign", new Vector3(Tunnel.center.x, -0.75f, Pit.yMax - 0.32f), new Vector3(3f, 0.4f, 0.04f), Yellow, 1f, t, 0, false);
        // stairs down at the west end (rising towards the west edge)
        float run = Mathf.CeilToInt(PitDepth / 0.2f) * 0.36f;
        Buildings.Stairs(t, new Vector3(Pit.xMin + 0.3f + run, floor, c.z + 6f), Vector3.left, PitDepth, 1.8f, Concrete);
        // truck ramp from the south edge down to the floor (a solid slope)
        const float rampLength = 14f;
        Underground.Wedge(t, "Ramp", new Vector3(c.x + 3f, floor, Pit.yMin + 0.3f + rampLength), new Vector3(c.x + 3f, 0f, Pit.yMin + 0.3f), 4f, OldConcrete);
        Box("RampWall", new Vector3(c.x + 0.8f, floor / 2f + 0.3f, Pit.yMin + 0.3f + rampLength / 2f), new Vector3(0.3f, PitDepth + 0.6f, rampLength), Concrete, 2f, t);
        // kerbs around the edge, open at the stairs and the ramp
        MeshGen.RoundBox("Kerb", new Vector3(Pit.xMax + 0.3f, 0.45f, c.z), new Vector3(0.5f, 0.9f, Pit.height), 0.07f, Concrete, 2f, t);
        MeshGen.RoundBox("Kerb", new Vector3(c.x - 2f, 0.45f, Pit.yMax + 0.3f), new Vector3(Pit.width - 4f, 0.9f, 0.5f), 0.07f, Concrete, 2f, t);
        MeshGen.RoundBox("Kerb", new Vector3(Pit.xMin - 0.3f, 0.45f, c.z - 3f), new Vector3(0.5f, 0.9f, Pit.height - 9f), 0.07f, Concrete, 2f, t);
        MeshGen.RoundBox("Kerb", new Vector3(c.x - 3.5f, 0.45f, Pit.yMin - 0.3f), new Vector3(Pit.width - 7f, 0.9f, 0.5f), 0.07f, Concrete, 2f, t);
        // inside: a container and pallets of crates
        Container(new Vector3(c.x - 2.5f, floor, c.z + 2f), 0, 5, t);
        Pallet(new Vector3(c.x + 4f, floor, c.z + 6f), 20f, 3, t);
        Drums(new Vector3(c.x - 5f, floor, c.z - 8f), 4, t);
    }

    /// <summary>The service tunnel from the pit to a stair up by the pump house, and the pump room.</summary>
    private static void ServiceTunnel()
    {
        var t = new GameObject("ServiceTunnel").transform;
        t.SetParent(root, false);
        var style = new Underground.Style
        {
            Floor = OldConcrete, Wall = Concrete, Ceiling = Concrete, Trim = RustPlates,
            Lamp = AssetSetup.Mat("TunnelLamp", new Color(1f, 0.9f, 0.7f), 0.5f, 0f, null, null, null, null, new Color(2.2f, 1.9f, 1.3f)),
        };
        float f = -PitDepth, c = -1f;
        var holes = new[] { ExitHole };
        var none = new Rect[0];
        Underground.Room(t, Tunnel, f, c, style, holes, none, ('S', Tunnel.xMin, Tunnel.xMax), ('E', PumpRoom.yMin + 2f, PumpRoom.yMax - 2f));
        Underground.Room(t, PumpRoom, f, c, style, none, none, ('W', PumpRoom.yMin + 2f, PumpRoom.yMax - 2f));
        // stair up to the yard at the north end
        Buildings.Stairs(t, new Vector3(ExitHole.center.x, f, ExitHole.yMin + 0.2f), Vector3.forward, PitDepth, ExitHole.width - 0.1f, Concrete);
        foreach (float x in new[] { ExitHole.xMin - 0.15f, ExitHole.xMax + 0.15f })
            Box("StairWall", new Vector3(x, (f + c) / 2f, ExitHole.center.y), new Vector3(0.3f, c - f, ExitHole.height), Concrete, 2f, t);
        Underground.Railing(t, ExitHole, 'N', Concrete);
        // pipes along the walls, a big pump and tanks in the pump room, crates for cover
        foreach (float y in new[] { -1.6f, -2.1f })
        {
            var pipe = MeshGen.Object("Pipe", MeshGen.Cylinder(0.18f, Tunnel.height, 2f, 12), TankPaint, t, new Vector3(Tunnel.xMin + 0.35f, y, Tunnel.yMin));
            pipe.transform.rotation = Quaternion.Euler(90, 0, 0);
        }
        MeshGen.TexCylinder("Pump", new Vector3(87f, f, 9f), 1.2f, 1.8f, Yellow, 1f, t);
        MeshGen.TexCylinder("Tank", new Vector3(88.3f, f, 5.6f), 0.9f, 2.4f, TankPaint, 1f, t);
        MeshGen.TexCylinder("Tank", new Vector3(88.3f, f, 12.4f), 0.9f, 2.4f, TankPaint, 1f, t);
        Pallet(new Vector3(84f, f, 12.8f), 0, 2, t);
        Obstacles.CrateStack(t, new Vector3(79f, f, -2f), 0f, rng);
        Obstacles.Blocks(t, new Vector3(79f, f, 14f), 0f, 1);
        Obstacles.AmmoCrate(t, new Vector3(84.5f, f, 6f), 90f);
        Underground.Lamp(t, new Vector3(79f, -1.8f, 4f), 13f, 1.1f);
        Underground.Lamp(t, new Vector3(79f, -1.8f, 20f), 13f, 1.1f);
        Underground.Lamp(t, new Vector3(86f, -1.8f, 9f), 9f, 1.1f);
    }

    /// <summary>Sloped surfaces in the yard: a ramp onto the loading dock, one onto a container, a ramped block.</summary>
    private static void Slopes()
    {
        var t = new GameObject("Slopes").transform;
        t.SetParent(root, false);
        Underground.Wedge(t, "DockRamp", new Vector3(-80f, 0, -14f), new Vector3(-80f, 1.4f, -24f), 4f, OldConcrete);
        Underground.Wedge(t, "ContainerRamp", new Vector3(-84f, 0, 4f), new Vector3(-84f, 2.62f, -2.95f), 2.2f, Walkway);
        MeshGen.RoundBox("RampBlock", new Vector3(62f, 1f, 56f), new Vector3(6f, 2f, 6f), 0.1f, Concrete, 2f, t);
        Underground.Wedge(t, "BlockRamp", new Vector3(52f, 0, 56f), new Vector3(59f, 2f, 56f), 4f, OldConcrete);
        Underground.Wedge(t, "BlockRamp", new Vector3(72f, 0, 56f), new Vector3(65f, 2f, 56f), 4f, OldConcrete);
        MeshGen.RoundBox("RampBlockWall", new Vector3(62f, 2.5f, 58.7f), new Vector3(6f, 1f, 0.5f), 0.07f, Concrete, 2f, t);
    }

    /// <summary>A rail-mounted gantry crane over the north quay; a steel stair climbs to a platform on one leg.</summary>
    private static void GantryCrane(Vector3 c)
    {
        var t = new GameObject("GantryCrane").transform;
        t.SetParent(root, false);
        var paint = Pbr("CranePaint", "Metal032", new Color(0.92f, 0.62f, 0.12f), 0.5f);
        const float span = 22f, depth = 8f, height = 15f;
        foreach (float x in new[] { -span / 2f, span / 2f })
            foreach (float z in new[] { -depth / 2f, depth / 2f })
            {
                Box("Leg", c + new Vector3(x, height / 2f, z), new Vector3(0.9f, height, 0.9f), paint, 2f, t);
                Box("Bogie", c + new Vector3(x, 0.45f, z), new Vector3(1.6f, 0.9f, 2.6f), DarkMetal, 1f, t);
            }
        foreach (float z in new[] { -depth / 2f, depth / 2f })
        {
            Box("Girder", c + new Vector3(0, height + 0.7f, z), new Vector3(span + 6f, 1.4f, 1.1f), paint, 2f, t, 0, false);
            Box("Rail", c + new Vector3(0, 0.05f, z), new Vector3(70f, 0.1f, 0.25f), DarkMetal, 1f, t, 0, false);
        }
        foreach (float x in new[] { -span / 2f, span / 2f })
        {
            Box("CrossBeam", c + new Vector3(x, height + 0.7f, 0), new Vector3(1.1f, 1.4f, depth), paint, 2f, t, 0, false);
            Box("SillBeam", c + new Vector3(x, 1.4f, 0), new Vector3(0.7f, 0.7f, depth), paint, 2f, t);
        }
        // trolley and operator cabin, the hoist ropes and a hanging spreader
        Box("Trolley", c + new Vector3(3f, height + 1.8f, 0), new Vector3(3f, 1.2f, depth + 1f), paint, 2f, t, 0, false);
        Box("Cabin", c + new Vector3(3f, height - 1.4f, depth / 2f - 0.8f), new Vector3(2.2f, 2.2f, 2f), WallWhite, 2f, t, 0, false);
        Box("CabinGlass", c + new Vector3(3f, height - 1.4f, depth / 2f - 1.81f), new Vector3(1.8f, 1.2f, 0.05f), Glass, 1f, t, 0, false);
        foreach (float z in new[] { -1.2f, 1.2f })
            Box("Rope", c + new Vector3(3f, height - 4f, z), new Vector3(0.05f, 8f, 0.05f), DarkMetal, 1f, t, 0, false);
        Box("Spreader", c + new Vector3(3f, height - 8.2f, 0), new Vector3(2.4f, 0.4f, 6f), paint, 1f, t, 0, false);
        // stair up the west leg to a platform at 8 m: a lookout over the whole yard
        Buildings.FireEscape(t, c + new Vector3(-span / 2f - 0.45f, 0, -depth / 2f + 0.2f), Vector3.forward, Vector3.left, 8f, false, Walkway, RustPlates);
    }

    /// <summary>A cargo ship moored outside the north wall (to look at; it is not part of the play area).</summary>
    private static void Ship(Vector3 c)
    {
        var t = new GameObject("Ship").transform;
        t.SetParent(root, false);
        var hull = AssetSetup.Mat("Port_ShipHull", new Color(0.12f, 0.16f, 0.22f), 0.45f, 0.2f);
        var bottom = AssetSetup.Mat("Port_ShipBottom", new Color(0.55f, 0.14f, 0.1f), 0.4f, 0.1f);
        var white = AssetSetup.Mat("Port_ShipWhite", new Color(0.9f, 0.9f, 0.88f), 0.5f, 0.1f);
        MeshGen.RoundBox("HullBottom", c + new Vector3(0, -4.2f, 0), new Vector3(78f, 3f, 14f), 1.2f, bottom, 4f, t, 0f, false);
        MeshGen.RoundBox("Hull", c + new Vector3(0, 0.5f, 0), new Vector3(80f, 6.5f, 14.5f), 1.5f, hull, 4f, t, 0f, false);
        MeshGen.RoundBox("Bow", c + new Vector3(41f, 0.8f, 0), new Vector3(7f, 6.2f, 9f), 3f, hull, 4f, t, 0f, false);
        // bridge house at the stern, funnel, deck cranes and containers on deck
        MeshGen.RoundBox("Bridge", c + new Vector3(-30f, 9f, 0), new Vector3(10f, 10.5f, 13f), 0.3f, white, 3f, t, 0f, false);
        MeshGen.RoundBox("BridgeWings", c + new Vector3(-27f, 13.2f, 0), new Vector3(3f, 1.2f, 17f), 0.2f, white, 3f, t, 0f, false);
        Box("BridgeGlass", c + new Vector3(-24.95f, 12.4f, 0), new Vector3(0.1f, 1.2f, 11f), Glass, 1f, t, 0, false);
        MeshGen.TexCylinder("Funnel", c + new Vector3(-33f, 14f, 0), 1.6f, 4f, AssetSetup.Mat("Port_Funnel", new Color(0.8f, 0.2f, 0.12f), 0.5f), 2f, t, false);
        for (int i = 0; i < 4; i++)
            for (int k = 0; k < 2; k++)
            {
                var go = MeshGen.Object("DeckContainer", MeshGen.Container(), ContainerMat(i + k), t, c + new Vector3(-14f + i * 9f, 3.75f + k * 2.6f, -2f + (i % 2) * 1.5f), 90f);
                go.GetComponent<MeshRenderer>().sharedMaterials = new[] { ContainerMat(i * 2 + k), DarkMetal };
            }
        foreach (float x in new[] { -6f, 14f })
        {
            Box("CraneMast", c + new Vector3(x, 8f, 5.5f), new Vector3(0.8f, 9f, 0.8f), Yellow, 1f, t, 0, false);
            Box("CraneJib", c + new Vector3(x + 5f, 11.5f, 5.5f), new Vector3(10f, 0.5f, 0.5f), Yellow, 1f, t, 0, false).transform.rotation = Quaternion.Euler(0, 0, 20f);
        }
    }

    /// <summary>
    /// A trick route onto the small shed roof: a steel stair hidden behind the shed and the perimeter
    /// wall climbs onto two stacked containers, and a plank bridges the gap to the roof.
    /// </summary>
    private static void HiddenRoute()
    {
        var t = new GameObject("HiddenRoute").transform;
        t.SetParent(root, false);
        const float z = -46.6f;
        Container(new Vector3(-58f, 0, z), 90, 3, t);
        Container(new Vector3(-58f, 2.6f, z), 90, 1, t);
        float top = 5.2f;
        Buildings.OutdoorStairs(t, new Vector3(-44.2f, 0, z), Vector3.left, top, Walkway, RustPlates);
        // landing between the top of the stair and the container roof
        Box("Landing", new Vector3(-54.3f, top - 0.1f, z), new Vector3(1.6f, 0.2f, 1.8f), Walkway, 1f, t);
        // plank over to the shed roof (eave at about 5 m)
        Box("Plank", new Vector3(-58f, top + 0.02f, -43.9f), new Vector3(1f, 0.08f, 3.4f), Planks, 1f, t);
    }

    /// <summary>Strips of grass with trees and bushes along the perimeter and next to the office.</summary>
    private static void Greenery()
    {
        var t = new GameObject("Greenery").transform;
        t.SetParent(root, false);
        var grass = Pbr("Grass", "Grass004", new Color(0.9f, 0.95f, 0.85f), 0.25f);
        var curb = Concrete;
        foreach (var (c, size) in new[]
        {
            (new Vector3(55f, 0, 46f), new Vector3(22f, 0, 8f)),
            (new Vector3(-20f, 0, -47f), new Vector3(30f, 0, 6f)),
            (new Vector3(68f, 0, -10f), new Vector3(5f, 0, 24f)),
        })
        {
            Box("Lawn", c + Vector3.up * 0.08f, new Vector3(size.x, 0.16f, size.z), grass, 3f, t);
            Box("Curb", c + new Vector3(0, 0.12f, size.z / 2f), new Vector3(size.x, 0.24f, 0.25f), curb, 1f, t, 0, false);
            Box("Curb", c + new Vector3(0, 0.12f, -size.z / 2f), new Vector3(size.x, 0.24f, 0.25f), curb, 1f, t, 0, false);
            Box("Curb", c + new Vector3(size.x / 2f, 0.12f, 0), new Vector3(0.25f, 0.24f, size.z), curb, 1f, t, 0, false);
            Box("Curb", c + new Vector3(-size.x / 2f, 0.12f, 0), new Vector3(0.25f, 0.24f, size.z), curb, 1f, t, 0, false);
            int trees = Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(size.x, size.z) / 7f));
            for (int i = 0; i < trees; i++)
            {
                float u = (i + 0.5f) / trees - 0.5f;
                var p = c + (size.x > size.z ? new Vector3(u * size.x, 0.16f, R(-1f, 1f)) : new Vector3(R(-0.8f, 0.8f), 0.16f, u * size.z));
                Nature.Tree(rng.NextDouble() < 0.4 ? Nature.Pines : Nature.Trees, rng, p, 7f, 11f, t);
                var q = p + new Vector3(R(-2.5f, 2.5f), 0, R(-1.5f, 1.5f));
                Nature.Place(Nature.Bushes[rng.Next(Nature.Bushes.Length)], q, R(0, 360), R(1f, 1.6f), t);
            }
            for (int i = 0; i < size.x * size.z / 10f; i++)
            {
                var p = c + new Vector3(R(-0.48f, 0.48f) * size.x, 0.16f, R(-0.48f, 0.48f) * size.z);
                Nature.Place(Nature.Ground[rng.Next(Nature.Ground.Length)], p, R(0, 360), R(0.4f, 0.8f), t);
            }
        }
        // a few lone trees and rocks in the open yard corners
        foreach (var (x, z) in new[] { (-66f, -46f), (-66f, 46f), (30f, -46f), (-30f, 46f) })
        {
            Nature.Tree(Nature.Trees, rng, new Vector3(x, 0, z), 6f, 9f, t);
            Nature.Place(Nature.Rocks[rng.Next(Nature.Rocks.Length)], new Vector3(x + 2.5f, 0, z + 1f), R(0, 360), R(0.8f, 1.4f), t);
        }
    }

    // ------------------------------------------------------------------ the map

    public static MapLayout Build(Transform parent)
    {
        rng = new System.Random(11);
        mats.Clear();
        root = new GameObject("Map_Port").transform;
        root.SetParent(parent, false);
        var night = new List<Renderer>();
        Buildings.Night = night;
        LoadProps();

        // ground, quay and sea
        // the yard: a 1 m slab laid around the loading pit and the tunnel stairwell, quay walls below
        float yx = HalfX + 8f, yz = HalfZ + 8f;
        foreach (var r in Underground.Cover(Rect.MinMaxRect(-yx, -yz, yx, yz), new[] { Pit, ExitHole }))
            Box("Yard", new Vector3(r.center.x, -0.5f, r.center.y), new Vector3(r.width, 1f, r.height), Asphalt, 5f, root);
        foreach (float s in new[] { -1f, 1f })
        {
            Box("QuayWall", new Vector3(0, -2.4f, s * yz), new Vector3(yx * 2f, 2.8f, 0.6f), OldConcrete, 3f, root, 0, false);
            Box("QuayWall", new Vector3(s * yx, -2.4f, 0), new Vector3(0.6f, 2.8f, yz * 2f), OldConcrete, 3f, root, 0, false);
        }
        LoadingPit();
        ServiceTunnel();
        Slopes();
        // the sea all around the yard (not under it: the pit and the tunnel are below sea level)
        var seaMat = AssetSetup.Mat("Port_Sea", new Color(0.08f, 0.22f, 0.32f), 0.95f, 0.1f);
        foreach (var r in Underground.Cover(Rect.MinMaxRect(-800f, -800f, 800f, 800f), new[] { Rect.MinMaxRect(-yx, -yz, yx, yz) }))
        {
            var sea = MeshGen.Object("Sea", MeshGen.Box(new Vector3(r.width, 0.1f, r.height), 40f), seaMat, root, new Vector3(r.center.x, -2.6f, r.center.y));
            sea.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // perimeter wall with pillars
        var walls = new GameObject("PerimeterWall").transform;
        walls.SetParent(root, false);
        var corners = new[] { new Vector3(-HalfX, 0, -HalfZ), new Vector3(HalfX, 0, -HalfZ), new Vector3(HalfX, 0, HalfZ), new Vector3(-HalfX, 0, HalfZ) };
        for (int i = 0; i < 4; i++) ConcreteWall(corners[i], corners[(i + 1) % 4], 3.2f, walls);

        // buildings
        Warehouse(new Vector3(-46, 0, 30), 22, 30, 9, WallWhite, WallRed, Roof, 0);
        Warehouse(new Vector3(18, 0, 42), 14, 40, 8, WallRed, WallWhite, RoofRust, 90);
        Warehouse(new Vector3(54, 0, -34), 14, 18, 7, WallTeal, WallWhite, Roof, 0);
        // fire escapes up to the eaves of the big white warehouse and the teal one
        Buildings.FireEscape(root, new Vector3(-35f, 0, 33f), Vector3.forward, Vector3.right, 9.1f, true, Walkway, RustPlates);
        Buildings.FireEscape(root, new Vector3(61f, 0, -39f), Vector3.forward, Vector3.right, 7f, true, Walkway, RustPlates);
        Warehouse(new Vector3(-58, 0, -38), 9, 12, 5, WallRust, null, RoofRust, 90);
        Warehouse(new Vector3(6, 0, -42), 10, 14, 5.5f, WallTeal, WallRed, RoofRust, 90);
        // three-floor office players can enter, with stairs inside up to the roof
        var office = Buildings.Build(root, "Office", new Vector3(58, 0, 24), 0, 14, 18, 3, new Buildings.Style
        {
            Outer = Bricks, Inner = Pbr("PlasterInner", "Plaster001", new Color(0.93f, 0.92f, 0.88f), 0.5f),
            Floor = Pbr("Tiles", "Tiles101", Color.white, 0.6f), Stairs = Concrete, Trim = Concrete, Furniture = Wood,
        });
        Box("AC", office.TransformPoint(new Vector3(7.45f, 2.6f, -5f)), new Vector3(0.8f, 0.7f, 1.1f), AssetSetup.Solid("Port_AC", new Color(0.85f, 0.86f, 0.84f), 0.4f), 1f, office, 0, false);
        HiddenRoute();
        // the new parts of the yard: customs house (enterable), a raised loading dock, a pump house,
        // a gantry crane with a stair up to a platform, more containers, and a ship at the quay
        Buildings.Build(root, "Customs", new Vector3(-82f, 0, 22f), -90f, 14f, 12f, 2, new Buildings.Style
        {
            Outer = Plaster, Inner = Pbr("PlasterInner", "Plaster001", new Color(0.93f, 0.92f, 0.88f), 0.5f),
            Floor = Pbr("Tiles", "Tiles101", Color.white, 0.6f), Stairs = Concrete, Trim = Concrete, Furniture = Wood,
        });
        Platform(new Vector3(-80f, 0, -30f), 16f, 12f, 1.4f, 1);
        Warehouse(new Vector3(82f, 0, 44f), 10f, 12f, 5f, WallWhite, WallTeal, RoofRust, 0f);
        GantryCrane(new Vector3(-21f, 0, 57f));
        Ship(new Vector3(14f, 0, HalfZ + 24f));

        // oil tanks with a pipe bridge to the long warehouse, yellow silos
        Tank(new Vector3(22, 0, 10), 5.5f, 9f);
        Tank(new Vector3(36, 0, 12), 5.5f, 9f);
        Tank(new Vector3(-30, 0, -6), 4.5f, 7f);
        var pipes = new GameObject("PipeBridge").transform;
        pipes.SetParent(root, false);
        Pipe(new Vector3(28, 6.5f, 10), new Vector3(28, 6.5f, 34), 0.45f, pipes);
        for (float z = 18; z < 34; z += 5)
        {
            Box("Frame", new Vector3(26.9f, 3.2f, z), new Vector3(0.25f, 6.4f, 0.25f), RustPlates, 1f, pipes);
            Box("Frame", new Vector3(29.1f, 3.2f, z), new Vector3(0.25f, 6.4f, 0.25f), RustPlates, 1f, pipes);
            Box("Beam", new Vector3(28f, 6f, z), new Vector3(2.4f, 0.25f, 0.25f), RustPlates, 1f, pipes, 0, false);
        }
        Silos(new Vector3(-16, 0, 30), root);

        // raised concrete platforms with stairs
        Platform(new Vector3(-46, 0, -12), 18, 14, 1.4f, 1);
        Platform(new Vector3(32, 0, -28), 16, 12, 1.2f, 2);

        // shipping containers (main cover)
        ContainerStack(new Vector3(-16, 0, -14), 0, 3, 2);
        ContainerStack(new Vector3(-6, 0, 2), 90, 2, 2);
        ContainerStack(new Vector3(6, 0, -18), 0, 3, 3);
        ContainerStack(new Vector3(-36, 0, 12), 90, 2, 1);
        ContainerStack(new Vector3(44, 0, -6), 0, 2, 2);
        ContainerStack(new Vector3(-62, 0, 8), 90, 3, 2);
        ContainerStack(new Vector3(62, 0, 4), 90, 2, 1);
        ContainerStack(new Vector3(78, 0, 8), 0, 2, 2);
        ContainerStack(new Vector3(-70, 0, 50), 90, 3, 2);
        ContainerStack(new Vector3(40, 0, 56), 90, 3, 2);
        ContainerStack(new Vector3(-84, 0, -6), 0, 2, 1);
        ContainerStack(new Vector3(56, 0, -56), 0, 3, 2);

        // fences and low walls
        var fences = new GameObject("Fences").transform;
        fences.SetParent(root, false);
        FenceLine(new Vector3(-30, 0, -26), new Vector3(-6, 0, -26), fences);
        FenceLine(new Vector3(12, 0, 26), new Vector3(12, 0, 4), fences);
        FenceLine(new Vector3(40, 0, -20), new Vector3(62, 0, -20), fences);
        ConcreteWall(new Vector3(-24, 0, 20), new Vector3(-4, 0, 20), 1.3f, fences);
        ConcreteWall(new Vector3(18, 0, -6), new Vector3(30, 0, -6), 1.3f, fences);

        Greenery();

        // ammunition crates, one in each part of the yard
        var supply = new GameObject("AmmoCrates").transform;
        supply.SetParent(root, false);
        foreach (var (cx, cz) in new[] { (-50f, -25f), (-20f, -40f), (15f, -25f), (45f, -45f), (-60f, 18f), (-25f, 5f), (5f, 22f), (40f, 25f), (64f, -15f) })
        {
            Physics.SyncTransforms();
            for (int k = 0; k < 20; k++)
            {
                var p = new Vector3(cx + R(-6, 6), 0, cz + R(-6, 6));
                if (Physics.CheckBox(p + Vector3.up * 1f, new Vector3(1.4f, 0.9f, 1.4f), Quaternion.identity, World.EnvMask | (1 << World.FenceLayer))) continue;
                Obstacles.AmmoCrate(supply, p, R(0, 360));
                break;
            }
        }

        // varied cover across the yard: sandbag bunkers, wrecks, pipes, hedgehogs, crates, blocks
        Physics.SyncTransforms();
        var cover = new GameObject("Cover").transform;
        cover.SetParent(root, false);
        int placed = 0;
        for (int i = 0; i < 80 && placed < 18; i++)
        {
            var p = new Vector3(R(-HalfX + 7, HalfX - 7), 0, R(-HalfZ + 7, HalfZ - 7));
            if (InPit(p, 4f)) continue;
            if (Physics.CheckBox(p + Vector3.up * 1.2f, new Vector3(3.2f, 1f, 3.2f), Quaternion.identity, World.EnvMask | (1 << World.FenceLayer))) continue;
            Obstacles.Random(cover, p, R(0, 360), rng);
            Physics.SyncTransforms();
            placed++;
        }

        // props: pallets, drums, crates, barriers, lamps
        var small = new GameObject("Props").transform;
        small.SetParent(root, false);
        foreach (var (x, z) in new[] { (-34f, 26f), (-2f, -28f), (40f, 30f), (-52f, -2f), (14f, -32f), (50f, -14f), (-20f, 8f), (30f, -42f) })
        {
            Pallet(new Vector3(x, 0, z), R(0, 90), 1 + rng.Next(4), small);
            Drums(new Vector3(x + 2f, 0, z + 1.2f), 2 + rng.Next(5), small);
        }
        Physics.SyncTransforms();
        string[] crates = { "old_military_crate", "plastic_crate_03", "cardboard_box_01", "ammo_box", "Barrel_01", "Barrel_02", "barrel_03", "propane_tank", "small_lpg_tank", "metal_trash_can", "old_tyre", "utility_box_01", "portable_generator" };
        for (int i = 0; i < 46; i++)
        {
            var p = new Vector3(R(-HalfX + 6, HalfX - 6), 0, R(-HalfZ + 6, HalfZ - 6));
            if (InPit(p)) continue;
            if (Physics.CheckSphere(p + Vector3.up * 0.8f, 1.2f)) continue;
            Prop(crates[rng.Next(crates.Length)], p, R(0, 360), small);
        }
        foreach (var (x, z, yaw) in new[] { (-4f, 12f, 90f), (2f, 12f, 90f), (-26f, -36f, 0f), (20f, 26f, 0f), (46f, 4f, 90f), (-50f, 14f, 0f) })
            Prop(rng.NextDouble() < 0.5 ? "concrete_road_barrier" : "concrete_road_barrier_02", new Vector3(x, 0, z), yaw, small);
        foreach (var (x, z) in new[] { (-60f, -20f), (-10f, 40f), (34f, -48f), (66f, 40f), (0f, -8f) })
        {
            var lamp = Prop("street_lamp_01", new Vector3(x, 0, z), R(0, 360), small);
            if (lamp == null) continue;
            var rs = lamp.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            NightLights.Halo(small, new Vector3(b.center.x, b.max.y - 0.3f, b.center.z), 3.4f, new Color(0.55f, 0.4f, 0.2f), night);
        }
        // flood light masts over the container yard (glow only: no real lights)
        var steel = AssetSetup.Solid("Pole", new Color(0.15f, 0.15f, 0.16f), 0.4f, 0.6f);
        foreach (var (x, z) in new[] { (-70f, -48f), (70f, -48f), (-70f, 48f), (30f, 52f) })
        {
            MeshGen.TexCylinder("FloodMast", new Vector3(x, 0, z), 0.18f, 14f, steel, 1f, small);
            MeshGen.TexBox("FloodHead", new Vector3(x, 14.1f, z), new Vector3(1.6f, 0.5f, 0.6f), steel, 1f, small, 0f, false);
            NightLights.Lamp(small, new Vector3(x, 13.85f, z), new Vector3(1.4f, 0.06f, 0.5f), night, 6f);
        }

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.gameObject.layer != World.FenceLayer)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);

        var layout = new MapLayout { Name = "PORT", Root = root.gameObject, Center = Vector3.zero, Radius = 90f };
        BuildNavigation(layout);
        Buildings.Night = null;
        layout.NightLights = MapOptimizer.Merge(root, "Port", null, night).ToArray();
        Debug.Log($"[Port] built: {root.GetComponentsInChildren<Renderer>().Length} renderers, {props.Count} props, {layout.Waypoints.Length} waypoints, {layout.Spawns.Length} spawns");
        return layout;
    }

    /// <summary>Bot waypoint graph and spawn points from a grid of free positions.</summary>
    private static void BuildNavigation(MapLayout layout)
    {
        Physics.SyncTransforms();
        var points = new List<Vector3>();
        var index = new Dictionary<Vector2Int, int>();
        const float step = 7f;
        int nx = Mathf.FloorToInt(HalfX / step), nz = Mathf.FloorToInt(HalfZ / step);
        for (int gx = -nx; gx <= nx; gx++)
            for (int gz = -nz; gz <= nz; gz++)
            {
                var p = new Vector3(gx * step, 0, gz * step);
                if (Mathf.Abs(p.x) > HalfX - 3 || Mathf.Abs(p.z) > HalfZ - 3) continue;
                if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 40f, World.EnvMask)) continue;
                p.y = hit.point.y;
                if (p.y > 2f) continue; // on a roof or container
                if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.6f, World.EnvMask | (1 << World.FenceLayer))) continue;
                index[new Vector2Int(gx, gz)] = points.Count;
                points.Add(p);
            }
        var links = new List<Vector2Int>();
        foreach (var pair in index)
            foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) })
            {
                if (!index.TryGetValue(pair.Key + d, out int other)) continue;
                var a = points[pair.Value] + Vector3.up * 1f;
                var b = points[other] + Vector3.up * 1f;
                if (Mathf.Abs(a.y - b.y) > 0.6f) continue;
                if (Physics.SphereCast(a, 0.45f, (b - a).normalized, out _, Vector3.Distance(a, b), World.EnvMask | (1 << World.FenceLayer))) continue;
                links.Add(new Vector2Int(pair.Value, other));
            }
        // the loading pit: down the stairs and the ramp, joined to the nearest points above and below
        float run = Mathf.CeilToInt(PitDepth / 0.2f) * 0.36f;
        var c = new Vector3(Pit.center.x, 0, Pit.center.y);
        float f = -PitDepth;
        var chains = new[]
        {
            new[] { new Vector3(Pit.xMin - 1.2f, 0, c.z + 6f), new Vector3(Pit.xMin + run + 1f, f, c.z + 6f) },
            new[] { new Vector3(c.x + 3f, 0, Pit.yMin - 1.5f), new Vector3(c.x + 3f, f, Pit.yMin + 15.5f) },
            new[] { new Vector3(79f, f, -10f), new Vector3(79f, f, 9f), new Vector3(79f, f, 23.2f), new Vector3(79f, 0, 33f) },
            new[] { new Vector3(79f, f, 9f), new Vector3(86f, f, 9f) },
        };
        foreach (var chain in chains)
        {
            int a = points.Count, b = points.Count + chain.Length - 1;
            for (int i = 0; i < chain.Length; i++)
            {
                points.Add(chain[i]);
                if (i > 0) links.Add(new Vector2Int(a + i - 1, a + i));
            }
            foreach (int end in new[] { a, b })
            {
                var p = points[end];
                int best = -1;
                float bestDist = 11f;
                for (int i = 0; i < a; i++)
                {
                    float dist = Vector3.Distance(points[i], p);
                    if (Mathf.Abs(points[i].y - p.y) > 0.5f || dist > bestDist) continue;
                    if (Physics.SphereCast(p + Vector3.up * 1f, 0.4f, (points[i] - p).normalized, out _, dist, World.EnvMask | (1 << World.FenceLayer))) continue;
                    best = i;
                    bestDist = dist;
                }
                if (best >= 0) links.Add(new Vector2Int(end, best));
            }
        }
        layout.Waypoints = points.ToArray();
        layout.Links = links.ToArray();
        // players come back all over the yard (the host picks a safe one at random), not in the pit
        layout.Spawns = points.Where(p => p.y > -1f).ToArray();
    }
}
