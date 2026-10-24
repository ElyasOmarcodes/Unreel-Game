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

    private static Texture2D T(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{file}") ?? AssetSetup.ExtraTex(file);

    public static Material Pbr(string name, string set, Color tint, float smoothness = 0.8f, float metallic = 0f)
    {
        if (mats.TryGetValue(name, out var m)) return m;
        // the grass of every map is the rocky grass (rocky_terrain_02) where it is in the project
        if (set == "Grass004" && AssetSetup.ExtraTex("RockyTerrain02_BaseColor.jpg") != null) set = "RockyTerrain02";
        // wooden things are the planks of wood_table, each in its own colour (the tint); not tree trunks
        if ((set == "Wood049" || set == "Planks037A") && !name.Contains("Palm") && !name.Contains("Trunk") && AssetSetup.ExtraTex("WoodTable_BaseColor.jpg") != null) set = "WoodTable";
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
    // the yard is laid with the CityLord asphalt (its own seamless texture), like the streets of that town
    private static Material Asphalt => CityLordBuilder.MaterialNamed("Seamless_asphalt_v1") ?? Pbr("Asphalt", "Asphalt025C", new Color(0.85f, 0.85f, 0.88f), 0.6f);
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
            // big roller shutter door on both ends
            RollUpDoor(t, new Vector3(0, 2.4f, s * l / 2f), new Vector3(0, 0, s), Vector3.right, Mathf.Min(5f, w * 0.4f), 4.8f, s < 0);
        }
        foreach (float s in new[] { -1f, 1f })
            for (float z = -l / 2f + 3f; z < l / 2f - 2f; z += 4.5f)
                Box("Window", t.TransformPoint(new Vector3(s * (w / 2f + 0.05f), h - 3.2f, z)), new Vector3(0.1f, 1.1f, 2.6f), Glass, 1f, t, yaw, false);
        // roof vents
        for (float z = -l / 2f + 4f; z < l / 2f - 3f; z += 8f)
            MeshGen.TexCylinder("Vent", t.TransformPoint(new Vector3(0, h + rise - 0.1f, z)), 0.18f, 1.2f, DarkMetal, 1f, t, false);
    }


    // ------------------------------------------------------------------ yards, factories, doors

    private static Material Shutter => Pbr("Shutter", "CorrugatedSteel005", new Color(0.72f, 0.74f, 0.76f), 0.55f, 0.4f);
    private static Material Hazard => AssetSetup.Solid("Port_Hazard", new Color(0.95f, 0.75f, 0.1f), 0.4f);

    /// <summary>
    /// A closed roller shutter door as factories have them: steel slats in side guides, the rolled-up
    /// drum in its box above, a bottom bar with a handle, yellow and black bump posts and a small
    /// people door beside it. center = middle of the door at the wall face (local), normal outwards.
    /// </summary>
    private static void RollUpDoor(Transform t, Vector3 center, Vector3 normal, Vector3 along, float width, float height, bool personDoor = true)
    {
        Vector3 S(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        var face = center + normal * 0.08f;
        int slats = Mathf.Max(6, Mathf.RoundToInt(height / 0.16f));
        float slat = height / slats;
        for (int i = 0; i < slats; i++)
            Box("Slat", t.TransformPoint(face + Vector3.up * (-height / 2f + slat * (i + 0.5f))), S(along * width + Vector3.up * (slat - 0.025f) + normal * 0.05f), Shutter, 1f, t, t.eulerAngles.y, false);
        Box("ShutterBack", t.TransformPoint(center + normal * 0.03f), S(along * width + Vector3.up * height + normal * 0.04f), DarkMetal, 1f, t, t.eulerAngles.y, false);
        Box("BottomBar", t.TransformPoint(face + Vector3.up * (-height / 2f + 0.08f) + normal * 0.02f), S(along * width + Vector3.up * 0.16f + normal * 0.09f), DarkMetal, 1f, t, t.eulerAngles.y, false);
        Box("Handle", t.TransformPoint(face + Vector3.up * (-height / 2f + 0.35f) + normal * 0.06f), S(along * 0.35f + Vector3.up * 0.05f + normal * 0.06f), DarkMetal, 1f, t, t.eulerAngles.y, false);
        foreach (float s in new[] { -1f, 1f })
        {
            Box("Guide", t.TransformPoint(face + along * s * (width / 2f + 0.07f)), S(along * 0.14f + Vector3.up * height + normal * 0.16f), DarkMetal, 1f, t, t.eulerAngles.y, false);
            var post = face + along * s * (width / 2f + 0.45f) + normal * 0.45f + Vector3.up * (-height / 2f + 0.55f);
            MeshGen.TexCylinder("BumpPost", t.TransformPoint(post - Vector3.up * 0.55f), 0.12f, 1.1f, Hazard, 0.5f, t);
        }
        Box("DrumBox", t.TransformPoint(face + Vector3.up * (height / 2f + 0.3f) + normal * 0.1f), S(along * (width + 0.5f) + Vector3.up * 0.6f + normal * 0.45f), Shutter, 1f, t, t.eulerAngles.y, false);
        if (personDoor)
        {
            var d = center + along * (width / 2f + 1.4f) + Vector3.up * (-height / 2f + 1.05f) + normal * 0.05f;
            Box("PersonDoor", t.TransformPoint(d), S(along * 0.95f + Vector3.up * 2.1f + normal * 0.06f), DarkMetal, 1f, t, t.eulerAngles.y, false);
            Box("DoorSign", t.TransformPoint(d + Vector3.up * 1.35f), S(along * 0.6f + Vector3.up * 0.25f + normal * 0.03f), Hazard, 1f, t, t.eulerAngles.y, false);
        }
    }

    /// <summary>A wall around or between yards, with gates (from, to: metres along it from a).</summary>
    private static void YardWall(Vector3 a, Vector3 b, float height, params (float from, float to)[] gates)
    {
        var t = new GameObject("YardWall").transform;
        t.SetParent(root, false);
        var dir = (b - a).normalized;
        float length = (b - a).magnitude, cursor = 0f;
        foreach (var g in gates.OrderBy(g => g.from).Concat(new[] { (length, length) }))
        {
            if (g.Item1 - cursor > 0.5f) ConcreteWall(a + dir * cursor, a + dir * g.Item1, height, t);
            if (g.Item2 > g.Item1 && g.Item1 < length)
            {
                // gate posts with a yellow and black barrier arm left open
                foreach (float u in new[] { g.Item1, g.Item2 })
                    MeshGen.RoundBox("GatePost", a + dir * u + Vector3.up * (height + 0.6f) / 2f, new Vector3(0.8f, height + 0.6f, 0.8f), 0.08f, Concrete, 1.5f, t, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
            }
            cursor = Mathf.Max(cursor, g.Item2);
        }
    }

    /// <summary>
    /// A raised yard: concrete deck from the ground up to top, a waist-high rim along its edges (gaps
    /// where you come up), with a ramp and/or stairs. Rect in x/z.
    /// </summary>
    private static void Terrace(Rect r, float top, (char side, float at)[] stairs, (char side, float at)[] ramps, params (char side, float at)[] openings)
    {
        var t = new GameObject("Terrace").transform;
        t.SetParent(root, false);
        MeshGen.RoundBox("TerraceDeck", new Vector3(r.center.x, top / 2f, r.center.y), new Vector3(r.width, top, r.height), 0.1f, OldConcrete, 3f, t);
        var gaps = stairs.Concat(ramps).Concat(openings).ToList();
        void Rim(char side, float u0, float u1, Vector3 a, Vector3 dir)
        {
            float cursor = u0;
            foreach (var g in gaps.Where(g => g.side == side).OrderBy(g => g.at).Select(g => (g.at - 2f, g.at + 2f)).Concat(new[] { (u1, u1) }))
            {
                if (g.Item1 - cursor > 0.4f)
                {
                    var mid = a + dir * ((cursor + g.Item1) / 2f) + Vector3.up * (top + 0.45f);
                    MeshGen.RoundBox("TerraceRim", mid, Abs(dir * (g.Item1 - cursor)) + new Vector3(0.3f, 0.9f, 0.3f) - Abs(dir) * 0.3f, 0.06f, Concrete, 2f, t);
                }
                cursor = Mathf.Max(cursor, g.Item2);
            }
        }
        Rim('S', 0, r.width, new Vector3(r.xMin, 0, r.yMin + 0.15f), Vector3.right);
        Rim('N', 0, r.width, new Vector3(r.xMin, 0, r.yMax - 0.15f), Vector3.right);
        Rim('W', 0, r.height, new Vector3(r.xMin + 0.15f, 0, r.yMin), Vector3.forward);
        Rim('E', 0, r.height, new Vector3(r.xMax - 0.15f, 0, r.yMin), Vector3.forward);
        Vector3 Edge(char side, float at) => side == 'S' ? new Vector3(r.xMin + at, 0, r.yMin) : side == 'N' ? new Vector3(r.xMin + at, 0, r.yMax)
            : side == 'W' ? new Vector3(r.xMin, 0, r.yMin + at) : new Vector3(r.xMax, 0, r.yMin + at);
        Vector3 Out(char side) => side == 'S' ? Vector3.back : side == 'N' ? Vector3.forward : side == 'W' ? Vector3.left : Vector3.right;
        foreach (var (side, at) in stairs)
        {
            // the CityLord wooden ladder up the terrace face
            var e = Edge(side, at);
            CityLordBuilder.PlaceLadder(t, e, Out(side), top);
        }
        foreach (var (side, at) in ramps)
        {
            var e = Edge(side, at);
            Underground.Wedge(t, "TerraceRamp", e + Out(side) * (top * 4f), e + Vector3.up * top, 3.6f, OldConcrete);
        }
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>
    /// A row of small store rooms, high and low, that splits a yard in two without closing it: a gap to
    /// walk through every two or three rooms. From a towards b.
    /// </summary>
    private static void RoomRow(Vector3 a, Vector3 b, float depth)
    {
        var t = new GameObject("StoreRooms").transform;
        t.SetParent(root, false);
        var dir = (b - a).normalized;
        float length = (b - a).magnitude, u = 0f;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 90f;
        var side = Vector3.Cross(Vector3.up, dir);
        var walls = new[] { Plaster, Bricks, WallRust, WallTeal };
        int k = 0;
        while (u < length - 3f)
        {
            int run = 2 + rng.Next(2);
            for (int i = 0; i < run && u < length - 3f; i++, k++)
            {
                float w = R(3f, 4.2f), h = new[] { 2.6f, 3.4f, 2.2f, 4.2f }[k % 4];
                var c = a + dir * (u + w / 2f) + side * (depth / 2f);
                var room = MeshGen.TexBox("StoreRoom", c + Vector3.up * h / 2f, new Vector3(w, h, depth), walls[k % walls.Length], 2f, t, yaw);
                Box("RoomRoof", c + Vector3.up * (h + 0.08f), new Vector3(w + 0.3f, 0.16f, depth + 0.3f), Concrete, 1f, t, yaw, false);
                // its own small shutter on the yard side
                var rt = new GameObject("RoomFront").transform;
                rt.SetParent(t, false);
                rt.SetPositionAndRotation(c, Quaternion.Euler(0, yaw, 0));
                RollUpDoor(rt, new Vector3(0, 1.1f, -depth / 2f), Vector3.back, Vector3.right, Mathf.Min(2.2f, w - 0.8f), 2.2f, false);
                u += w;
            }
            u += R(2.4f, 3.2f);   // a way through
        }
    }

    /// <summary>
    /// A two-storey factory hall with a flat roof (a strong point over its yards), built so its roof is
    /// reached the hard way: a wooden stair up to a one-storey annex, crates on the annex roof up to a
    /// higher block, an air-conditioner and a crate on that roof up to a gap in the hall's parapet.
    /// The annex and the block are on the +X side (local). Front (doors) faces -Z.
    /// </summary>
    private static void Factory(Vector3 c, float yaw, float w, float l, float hallH, Material wall, Material band)
    {
        var t = new GameObject("Factory").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(c, Quaternion.Euler(0, yaw, 0));
        float hw = w / 2f, hl = l / 2f;
        void B(string name, Vector3 local, Vector3 size, Material m, float tex = 3f, bool col = true) => Box(name, t.TransformPoint(local), size, m, tex, t, yaw, col);
        // the hall: two floors of windows, a band, a flat roof with a parapet (gap where the route arrives)
        B("Hall", new Vector3(0, hallH / 2f, 0), new Vector3(w, hallH, l), wall);
        B("Plinth", new Vector3(0, 0.45f, 0), new Vector3(w + 0.2f, 0.9f, l + 0.2f), Concrete, 2f, false);
        if (band != null) B("Band", new Vector3(0, hallH * 0.5f, 0), new Vector3(w + 0.06f, 0.5f, l + 0.06f), band, 3f, false);
        B("RoofSlab", new Vector3(0, hallH + 0.12f, 0), new Vector3(w + 0.3f, 0.24f, l + 0.3f), Concrete, 3f);
        float gapZ = hl * 0.45f;
        foreach (float s in new[] { -1f, 1f })
            B("Parapet", new Vector3(0, hallH + 0.6f, s * (hl + 0.05f)), new Vector3(w + 0.3f, 0.8f, 0.2f), Concrete, 2f);
        B("Parapet", new Vector3(-hw - 0.05f, hallH + 0.6f, 0), new Vector3(0.2f, 0.8f, l + 0.3f), Concrete, 2f);
        // east parapet with the gap
        B("Parapet", new Vector3(hw + 0.05f, hallH + 0.6f, (-hl + gapZ - 1.3f) / 2f), new Vector3(0.2f, 0.8f, gapZ - 1.3f + hl), Concrete, 2f);
        B("Parapet", new Vector3(hw + 0.05f, hallH + 0.6f, (gapZ + 1.3f + hl) / 2f), new Vector3(0.2f, 0.8f, hl - gapZ - 1.3f), Concrete, 2f);
        // roof cover: skylights, vents and air-conditioners
        for (float x = -hw + 3f; x < hw - 2f; x += 5f)
            B("Skylight", new Vector3(x, hallH + 0.55f, -hl * 0.3f), new Vector3(2.2f, 0.9f, 3.2f), Glass, 1f);
        B("RoofAC", new Vector3(-hw + 2.5f, hallH + 0.75f, hl - 3f), new Vector3(1.6f, 1.3f, 2.2f), DarkMetal, 1f);
        for (int floor = 0; floor < 2; floor++)
            foreach (float s in new[] { -1f, 1f })
                for (float z = -hl + 2.5f; z < hl - 1.5f; z += 3.6f)
                    B("Window", new Vector3(s * (hw + 0.05f), 2.2f + floor * hallH * 0.5f, z), new Vector3(0.1f, 1.2f, 2.2f), Glass, 1f, false);
        RollUpDoor(t, new Vector3(-hw * 0.35f, 2.5f, -hl), Vector3.back, Vector3.right, Mathf.Min(5f, w * 0.32f), 5f);
        RollUpDoor(t, new Vector3(hw * 0.35f, 2.2f, -hl), Vector3.back, Vector3.right, Mathf.Min(4f, w * 0.25f), 4.4f, false);
        RollUpDoor(t, new Vector3(-hw * 0.2f, 2.5f, hl), Vector3.forward, Vector3.right, Mathf.Min(5f, w * 0.32f), 5f);

        // the climb: annex (3.0 m) - crates - block (5.0 m) - a.c. unit and crate - hall roof (7 m)
        const float annexH = 3.0f, blockH = 5.0f, step = 0.7f;
        float ax0 = hw, ax1 = hw + 3.6f, axc = (ax0 + ax1) / 2f;
        float annexZ0 = -hl, annexZ1 = gapZ - 4f, blockZ0 = annexZ1, blockZ1 = Mathf.Min(hl, gapZ + 3.5f);
        B("Annex", new Vector3(axc, annexH / 2f, (annexZ0 + annexZ1) / 2f), new Vector3(3.6f, annexH, annexZ1 - annexZ0), Bricks, 2.5f);
        B("AnnexRoof", new Vector3(axc, annexH + 0.08f, (annexZ0 + annexZ1) / 2f), new Vector3(3.9f, 0.16f, annexZ1 - annexZ0 + 0.3f), Concrete, 2f);
        B("Block", new Vector3(axc, blockH / 2f, (blockZ0 + blockZ1) / 2f), new Vector3(3.6f, blockH, blockZ1 - blockZ0), Plaster, 2.5f);
        B("BlockRoof", new Vector3(axc, blockH + 0.08f, (blockZ0 + blockZ1) / 2f), new Vector3(3.9f, 0.16f, blockZ1 - blockZ0 + 0.3f), Concrete, 2f);
        RollUpDoor(t, new Vector3(ax1, 1.3f, (annexZ0 + annexZ1) / 2f), Vector3.right, Vector3.forward, 2.4f, 2.6f, false);
        // a steep wooden stair along the outside of the annex, from the front corner up to its roof
        var wood = Wood;
        Buildings.Stairs(t, t.TransformPoint(new Vector3(ax1 + 0.6f, 0, annexZ0 - 0.2f + 15 * 0.36f + 0.36f)), t.TransformDirection(Vector3.back), annexH, 1.0f, wood);
        foreach (float y in new[] { 0.5f, 1.0f })
        {
            var rail = Box("StairRail", t.TransformPoint(new Vector3(ax1 + 1.15f, annexH / 2f + y, annexZ0 + 2.9f)), new Vector3(0.06f, 0.06f, 6.4f), wood, 1f, t, yaw, false);
            rail.transform.rotation = t.rotation * Quaternion.Euler(Mathf.Atan2(annexH, 5.8f) * Mathf.Rad2Deg, 0, 0);
        }
        // annex roof: two crates like steps up to the block
        B("Crate", new Vector3(axc, annexH + 0.16f + step / 2f, annexZ1 - 1.6f), new Vector3(1f, step, 1f), Planks, 1f);
        B("Crate", new Vector3(axc, annexH + 0.16f + step, annexZ1 - 0.6f), new Vector3(1f, step * 2f, 1f), Planks, 1f);
        // block roof: an a.c. unit, a crate on top of it next to the parapet gap
        B("BlockAC", new Vector3(axc, blockH + 0.16f + step / 2f, gapZ - 1.4f), new Vector3(1.4f, step, 1.1f), DarkMetal, 1f);
        // the last crate reaches 0.6 m under the hall roof (a jump), whatever the hall's height
        float lastTop = hallH + 0.24f - 0.6f, lastH = lastTop - (blockH + 0.16f);
        B("Crate", new Vector3(axc - 0.6f, blockH + 0.16f + lastH / 2f, gapZ - 0.3f), new Vector3(1f, lastH, 1f), Planks, 1f);
        // no ammunition on the roof: holding it means coming down for more
    }

    /// <summary>A CityLord oil tank (its own model) of the given radius, or a painted cylinder where it is missing.</summary>
    private static void Tank(Vector3 c, float radius, float height)
    {
        var oil = CityLordBuilder.Kit("Oil_tank_v1");
        if (oil != null)
        {
            CityLordBuilder.Place("Oil_tank_v1", root, c, R(0, 360), true, radius / Mathf.Max(0.5f, oil.Bounds.extents.x));
            return;
        }
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

    /// <summary>Height of a container (the CityLord container brought to the size of the old one).</summary>
    private static float ContainerH => CityLordBuilder.Kit("Cargo_container_v1_LD2close") is CityLordBuilder.KitPiece k ? k.Bounds.size.y * ContainerScale(k) : 2.59f;
    private static float ContainerScale(CityLordBuilder.KitPiece k) => 6.06f / Mathf.Max(1f, k.Bounds.size.x);

    private static void Container(Vector3 bottomCenter, float yaw, int color, Transform parent)
    {
        // the CityLord shipping container, long side along the yaw like the old one
        var kit = CityLordBuilder.Kit("Cargo_container_v1_LD2close");
        if (kit != null)
        {
            CityLordBuilder.Place("Cargo_container_v1_LD2close", parent, bottomCenter, yaw + 90f, true, ContainerScale(kit));
            return;
        }
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
        var along = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        for (int i = 0; i < columns; i++)
        {
            int height = 1 + rng.Next(maxHeight);
            // a wooden ladder up the end of the first column: the container roofs are a place to fight from
            if (i == 0 && c.y < 0.5f) CityLordBuilder.PlaceLadder(t, c + along * 3.03f, along, height * (ContainerH + 0.02f));
            for (int k = 0; k < height; k++)
                Container(c + right * (i * 2.6f) + Vector3.up * (k * (ContainerH + 0.02f)), yaw + (k > 0 ? R(-3, 3) : 0), rng.Next(7), t);
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
        // up by the CityLord wooden ladder (no stairs), a route for the bots too
        var edge = c + Vector3.Scale(dir, new Vector3(w / 2f, 0, l / 2f));
        CityLordBuilder.PlaceLadder(t, edge, dir, h);
        ladderChains.Add(new[] { edge + dir * 1.4f, edge - dir * 1.6f + Vector3.up * h });
    }

    // ------------------------------------------------------------------ raised yards (CityLord style)

    /// <summary>Bot routes up and down the ramps of the raised yards (joined to the grid like the pit's).</summary>
    private static readonly List<Vector3[]> rampChains = new List<Vector3[]>();
    /// <summary>Bot routes up the ladders of the platforms.</summary>
    private static readonly List<Vector3[]> ladderChains = new List<Vector3[]>();
    private static Material RaisedWall => CityLordBuilder.MaterialNamed("UNIConcrete_wall_v1") ?? OldConcrete;

    /// <summary>
    /// High ground as CityLord has it: one yard raised 2 m with a higher deck 4 m up on its back, and two
    /// smaller 2 m yards, each reached by the CityLord road ramp (20 m long, its own road texture), with
    /// concrete road blocks along the edges as cover. Each is put where the yard is free.
    /// </summary>
    private static void RaisedYards()
    {
        rampChains.Clear();
        int mask = World.EnvMask | (1 << World.FenceLayer);
        var ramp = CityLordBuilder.Kit("Road_set_v1_road_up_2m_HD");
        if (ramp == null) return;
        int built = 0;
        // every free place on a 3 m grid, in all four directions, the big yard first; smaller sizes where it does not fit
        var sizes = new List<(float lower, float wide, bool big)> { (34f, 22f, true), (20f, 16f, false), (20f, 16f, false), (14f, 12f, false) };
        var used = new List<Bounds>();
        foreach (var (lower, wide, big) in sizes)
        {
            if (built >= 3) break;
            float total = lower + 20f;
            var places = new List<(Vector3 c, Vector3 d)>();
            Physics.SyncTransforms();
            for (float x = -HalfX + 6f; x <= HalfX - 6f; x += 3f)
                for (float z = -HalfZ + 6f; z <= HalfZ - 6f; z += 3f)
                    foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
                    {
                        var c = new Vector3(x, 0, z);
                        var rot = Quaternion.LookRotation(d);
                        var half = new Vector3(wide / 2f + 1.5f, 3f, total / 2f + 1.5f);
                        var reach = rot * half;
                        if (Mathf.Abs(c.x) + Mathf.Abs(reach.x) > HalfX - 3f || Mathf.Abs(c.z) + Mathf.Abs(reach.z) > HalfZ - 3f) continue;
                        if (InPit(c, total / 2f + 3f)) continue;
                        if (used.Any(u => u.Intersects(new Bounds(c, new Vector3(Mathf.Abs(reach.x), 6f, Mathf.Abs(reach.z)) * 2f + new Vector3(8f, 0, 8f))))) continue;
                        if (Physics.CheckBox(c + Vector3.up * 3.2f, half, rot, mask, QueryTriggerInteraction.Ignore)) continue;
                        places.Add((c, d));
                    }
            Debug.Log($"[Map] Port raised yard {lower}x{wide}: {places.Count} free places");
            if (places.Count == 0) continue;
            var (center, dir) = places[rng.Next(places.Count)];
            BuildRaisedYard(center, dir, lower, wide, big);
            var r2 = Quaternion.LookRotation(dir) * new Vector3(wide / 2f, 3f, total / 2f);
            used.Add(new Bounds(center, new Vector3(Mathf.Abs(r2.x), 6f, Mathf.Abs(r2.z)) * 2f));
            built++;
        }
        Physics.SyncTransforms();
        Debug.Log($"[Map] Port: {built} raised yards");
    }

    private static void BuildRaisedYard(Vector3 c, Vector3 d, float lower, float wide, bool big)
    {
        var ramp = CityLordBuilder.Kit("Road_set_v1_road_up_2m_HD");
        float total = lower + 20f;
        var rot = Quaternion.LookRotation(d);
        var right = rot * Vector3.right;
        var back = c - d * (total / 2f);
        Vector3 L(float along, float side, float y) => back + d * along + right * side + Vector3.up * y;
        var t = new GameObject("RaisedYard").transform;
        t.SetParent(root, false);
        t.position = c;
        float yaw = rot.eulerAngles.y;
        Box("RaisedYard", L(lower / 2f, 0, 1f), new Vector3(wide, 2f, lower), RaisedWall, 3f, t, yaw);
        Box("RaisedTop", L(lower / 2f, 0, 2.02f), new Vector3(wide, 0.04f, lower), Asphalt, 5f, t, yaw, false);
        float rampSide = big ? 5.5f : 0f;
        Ramp(t, ramp, L(lower, rampSide, 0f), d, 0f, 2f);
        rampChains.Add(new[] { L(total + 1.5f, rampSide, 0f), L(lower + 10f, rampSide, 1f), L(lower - 2f, rampSide, 2f) });
        // road blocks along the sides of the deck
        for (float along = big ? 17f : 3f; along < lower - 2f; along += 6f)
            foreach (float side in new[] { -1f, 1f })
                CityLordBuilder.Place("Road_block_v1", t, L(along, side * (wide / 2f - 0.8f), 2f), yaw + 90f);
        if (big)
        {
            Box("RaisedYardUpper", L(7f, 0, 3f), new Vector3(wide, 2f, 14f), RaisedWall, 3f, t, yaw);
            Box("RaisedTop", L(7f, 0, 4.02f), new Vector3(wide, 0.04f, 14f), Asphalt, 5f, t, yaw, false);
            Ramp(t, ramp, L(14f, -5.5f, 0f), d, 2f, 2f);
            rampChains.Add(new[] { L(35.5f, -5.5f, 2f), L(24f, -5.5f, 3f), L(12f, -5.5f, 4f) });
            for (float side = 1f; side < wide / 2f - 1f; side += 3f)
                CityLordBuilder.Place("Road_block_v1", t, L(13.2f, side, 4f), yaw);
            foreach (float side in new[] { -1f, 1f })
                CityLordBuilder.Place("Road_block_v1", t, L(3f, side * (wide / 2f - 0.8f), 4f), yaw + 90f);
        }
    }

    /// <summary>A row of 'count' of a CityLord piece side by side (the silos), on free ground with room round it.</summary>
    private static bool PlaceRow(string piece, int count, Transform parent)
    {
        var kit = CityLordBuilder.Kit(piece);
        if (kit == null) return false;
        int mask = World.EnvMask | (1 << World.FenceLayer);
        var size = kit.Bounds.size;
        float step = Mathf.Max(size.x, size.z) * 1.04f;
        Physics.SyncTransforms();
        for (int attempt = 0; attempt < 400; attempt++)
        {
            float yaw = 90f * rng.Next(4);
            var rot = Quaternion.Euler(0, yaw, 0);
            var p = new Vector3(R(-HalfX + 10f, HalfX - 10f), 0, R(-HalfZ + 10f, HalfZ - 10f));
            float length = step * count;
            if (InPit(p, length / 2f + 3f)) continue;
            var half = new Vector3(length / 2f + 2f, size.y / 2f, step / 2f + 2f);
            if (Physics.CheckBox(p + Vector3.up * (size.y / 2f + 0.15f), half, rot, mask, QueryTriggerInteraction.Ignore)) continue;
            for (int i = 0; i < count; i++)
            {
                var at = p + rot * new Vector3((i - (count - 1) / 2f) * step, 0, 0) - rot * new Vector3(kit.Bounds.center.x, 0, kit.Bounds.center.z);
                CityLordBuilder.Place(piece, parent, at, yaw);
            }
            Physics.SyncTransforms();
            return true;
        }
        return false;
    }

    /// <summary>A CityLord piece on free ground of the yard (turned square to it), with room to walk around it.</summary>
    private static bool PlaceFree(string piece, Transform parent, float clearance = 1.6f)
    {
        var kit = CityLordBuilder.Kit(piece);
        if (kit == null) return false;
        int mask = World.EnvMask | (1 << World.FenceLayer);
        var size = kit.Bounds.size;
        Physics.SyncTransforms();
        for (int attempt = 0; attempt < 400; attempt++)
        {
            float yaw = 90f * rng.Next(4);
            var rot = Quaternion.Euler(0, yaw, 0);
            var p = new Vector3(R(-HalfX + 8f, HalfX - 8f), 0, R(-HalfZ + 8f, HalfZ - 8f));
            if (InPit(p, Mathf.Max(size.x, size.z) / 2f + 2f)) continue;
            var center = p + rot * new Vector3(kit.Bounds.center.x, 0, kit.Bounds.center.z) + Vector3.up * (size.y / 2f + 0.15f);
            var half = new Vector3(size.x / 2f + clearance, size.y / 2f, size.z / 2f + clearance);
            if (Physics.CheckBox(center, half, rot, mask, QueryTriggerInteraction.Ignore)) continue;
            CityLordBuilder.Place(piece, parent, p, yaw);
            Physics.SyncTransforms();
            return true;
        }
        return false;
    }

    /// <summary>The CityLord road ramp from 'top' (its high edge, on the ground line) going down along d, from baseY + rise to baseY.</summary>
    private static void Ramp(Transform parent, CityLordBuilder.KitPiece kit, Vector3 top, Vector3 d, float baseY, float rise)
    {
        float scaleY = rise / Mathf.Max(0.1f, kit.ColliderBounds.size.y);
        float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;   // the ramp climbs towards its local -X
        var go = CityLordBuilder.Place("Road_set_v1_road_up_2m_HD", parent, top + d * kit.Bounds.extents.x, yaw);
        if (go == null) return;
        go.transform.localScale = new Vector3(1f, scaleY, 1f);
        var p = go.transform.position;
        p.y = baseY + rise - kit.ColliderBounds.max.y * scaleY;
        go.transform.position = p;
    }

    private static void Pallet(Vector3 p, float yaw, int stack, Transform parent)
    {
        // CityLord pallets
        if (CityLordBuilder.Kit("Palet_v1_single") is CityLordBuilder.KitPiece kit)
        {
            for (int k = 0; k < stack; k++)
                CityLordBuilder.Place("Palet_v1_single", parent, p + Vector3.up * (k * kit.Bounds.size.y), yaw + R(-4, 4), k == stack - 1);
            var box = new GameObject("PalletCollider");
            box.transform.SetParent(parent, false);
            box.transform.SetPositionAndRotation(p + Vector3.up * (stack * kit.Bounds.size.y / 2f), Quaternion.Euler(0, yaw, 0));
            box.AddComponent<BoxCollider>().size = new Vector3(kit.Bounds.size.x, stack * kit.Bounds.size.y, kit.Bounds.size.z);
            return;
        }
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
        // CityLord barrels, two kinds of paint
        if (CityLordBuilder.Kit("Barrel_v3_single") != null && CityLordBuilder.Kit("Barrel_v1_LD1") != null)
        {
            for (int i = 0; i < count; i++)
                CityLordBuilder.Place(i % 3 == 1 ? "Barrel_v1_LD1" : "Barrel_v3_single", parent, p + new Vector3((i % 3) * 0.62f, 0, (i / 3) * 0.62f), R(0, 360));
            return;
        }
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
        ladderChains.Clear();
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
        var seaMat = AssetSetup.WaterMat("Water_Sea", new Color(0.02f, 0.11f, 0.17f), new Color(0.06f, 0.25f, 0.31f), 0.7f, 0.35f);
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
        // two-storey factories whose roofs are reached the hard way (strong points over the yards)
        Factory(new Vector3(-48, 0, 30), 0f, 20f, 26f, 7f, WallWhite, WallRed);
        Warehouse(new Vector3(18, 0, 42), 14, 40, 8, WallRed, WallWhite, RoofRust, 90);
        Factory(new Vector3(52, 0, -36), 0f, 15f, 18f, 6.6f, WallTeal, WallWhite);
        // small stores: CityLord hangars where the plain sheds stood
        if (CityLordBuilder.Place("Hangar_v1_full", root, new Vector3(-58, 0, -38), 0f) == null) Warehouse(new Vector3(-58, 0, -38), 9, 12, 5, WallRust, null, RoofRust, 90);
        if (CityLordBuilder.Place("Hangar_v3_basic", root, new Vector3(6, 0, -42), 90f) == null) Warehouse(new Vector3(6, 0, -42), 10, 14, 5.5f, WallTeal, WallRed, RoofRust, 90);
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
        if (CityLordBuilder.Place("Hangar_v1_full", root, new Vector3(82f, 0, 44f), 90f) == null) Warehouse(new Vector3(82f, 0, 44f), 10f, 12f, 5f, WallWhite, WallTeal, RoofRust, 0f);
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

        // the yard split into compounds: walls with gates, a row of store rooms, raised yards
        YardWall(new Vector3(-66f, 0, -64f), new Vector3(-66f, 0, 2f), 3f, (50f, 56f));
        YardWall(new Vector3(-92f, 0, 12f), new Vector3(-66f, 0, 12f), 3f, (12f, 18f));
        YardWall(new Vector3(42f, 0, -18f), new Vector3(70f, 0, -18f), 3f, (8f, 13f));
        YardWall(new Vector3(40f, 0, 36f), new Vector3(92f, 0, 36f), 3f, (26f, 32f));
        RoomRow(new Vector3(-29f, 0, 26f), new Vector3(10f, 0, 26f), 3.4f);
        // a long loading terrace and a higher one beside it in the south (stairs between them)
        Terrace(Rect.MinMaxRect(-60f, -63f, -30f, -50f), 2.4f, new[] { ('N', 26f) }, new (char, float)[0], ('E', 6f));
        Terrace(Rect.MinMaxRect(-30f, -63f, -8f, -52f), 4.2f, new[] { ('N', 15f) }, new[] { ('N', 6f) }, ('W', 6f));
        // from the lower terrace up to the higher one: a wooden ladder on its west face
        CityLordBuilder.PlaceLadder(root, new Vector3(-30f, 2.4f, -57f), Vector3.left, 1.8f);
        Container(new Vector3(-19f, 4.2f, -58.5f), 90f, 2, root);
        Obstacles.SandbagWall(root, new Vector3(-12f, 4.2f, -53f), 0f, 4f);

        // high and low ground as in CityLord: raised yards with road ramps on the free ground
        RaisedYards();
        // more to fight around, CityLord pieces on free ground: long concrete walls that make lanes, shorter
        // walls with lattice tops, containers open at both ends to run through, industrial pipe works
        var works = new GameObject("CityLordWorks").transform;
        works.SetParent(root, false);
        int works0 = 0;
        foreach (var (piece, count) in new[] { ("Concrete_fence_v1_wall_set_v2", 3), ("Cargo_container_v1_LD2through", 3), ("Concrete_fence_v2_S", 5),
                                               ("Industrial_pipe_v1", 2), ("Pipes_set_v1_V_set_v2", 2), ("Pipes_set_v1_H_set_v2", 2), ("Generator_v1", 2) })
            for (int i = 0; i < count; i++)
                if (PlaceFree(piece, works)) works0++;
        // and the rest of the CityLord set, as there: a row of tall silos on their legs, hangars and sheds,
        // fences and gates, pipe racks, electric boxes, barrels
        if (PlaceRow("Oil_tank_v2", 5, works)) works0 += 5;
        if (PlaceRow("Oil_tank_v2", 4, works)) works0 += 4;
        foreach (var (piece, count) in new[] { ("Hangar_v4", 1), ("Hangar_v2_basic", 1), ("Hangar_v3_outbuilding_1", 1), ("Hangar_v2_outbuilding", 1),
                                               ("Concrete_fence_v1_wall_set_v1", 2), ("Concrete_fence_v2_L", 2), ("Concrete_fence_v2_Gate", 1), ("Concrete_fence_v2_S_half", 2),
                                               ("Pipes_set_v1_H_set_v1", 1), ("Electric_box_v2", 3), ("Electric_box_v1", 2), ("Barrel_v1_LD2", 2), ("Barrel_v1_LD3", 3),
                                               ("Dumpsters_v1_garbadge", 2), ("Conditioner_v1", 2), ("Palet_v1_set", 3), ("Barrel_v3_quadro", 2), ("Barrel_v2_quadro", 2) })
            for (int i = 0; i < count; i++)
                if (PlaceFree(piece, works, piece.StartsWith("Hangar") ? 2.5f : 1.4f)) works0++;
        Debug.Log($"[Map] Port: {works0} CityLord works pieces placed");

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
        // CityLord yard furniture: generators, electric boxes, dumpsters, road blocks, barrel sets
        string[] kitProps = { "Generator_v1", "Electric_box_v1", "Dumpsters_v1_garbadge", "Road_block_v1", "Barrel_v3_quadro", "Barrel_v2_quadro", "Palet_v1_set", "Conditioner_v1" };
        for (int i = 0, kitPlaced = 0; i < 80 && kitPlaced < 16; i++)
        {
            var p = new Vector3(R(-HalfX + 6, HalfX - 6), 0, R(-HalfZ + 6, HalfZ - 6));
            if (InPit(p, 3f)) continue;
            if (Physics.CheckBox(p + Vector3.up * 1.1f, new Vector3(2.8f, 0.9f, 2.8f), Quaternion.identity, World.EnvMask | (1 << World.FenceLayer))) continue;
            if (CityLordBuilder.Place(kitProps[kitPlaced % kitProps.Length], small, p, R(0, 360)) == null) break;
            Physics.SyncTransforms();
            kitPlaced++;
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

        // the parts of the yard named on boards (Arabic and English), as in Al-Zahra
        var boards = new GameObject("Boards").transform;
        boards.SetParent(root, false);
        foreach (var (x, z, ar, en) in new[]
                 {
                     (50f, 12f, "مبنى الإدارة", "THE OFFICE BUILDING"), (-72f, 22f, "دار الجمارك", "THE CUSTOMS HOUSE"),
                     (29f, -1f, "خزانات النفط", "THE OIL TANKS"), (-9f, 22f, "صوامع الحبوب", "THE GRAIN SILOS"),
                     (67f, -22f, "حفرة التحميل والنفق", "THE LOADING PIT AND THE TUNNEL"), (14f, 56f, "الرصيف والسفينة", "THE QUAY AND THE SHIP"),
                     (-2f, -8f, "ساحة الحاويات", "THE CONTAINER YARD"), (-34f, -46f, "المصاطب الجنوبية", "THE SOUTH TERRACES"),
                 })
            Signboards.Stand(boards, new Vector3(x, 0, z), Vector3.zero, ar, en);

        var layout = new MapLayout { Name = "PORT", Root = root.gameObject, Center = Vector3.zero, Radius = 90f };
        BuildNavigation(layout);
        Buildings.Night = null;
        root.gameObject.AddComponent<MapScene>().NightLights = MapOptimizer.Merge(root, "Port", null, night).ToArray();
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
                if (p.y > 2f && !hit.collider.name.StartsWith("Raised")) continue; // on a roof or container (the raised yards are walked on)
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
        foreach (var chain in chains.Concat(rampChains).Concat(ladderChains))
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
