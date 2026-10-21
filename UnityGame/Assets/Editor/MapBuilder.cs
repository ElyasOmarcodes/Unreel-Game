using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the "Downtown" map: a 3 x 3 grid of city blocks from the Quaternius Downtown City kit,
/// streets with lane markings, parked cars, barriers and crates for cover, and a park with a fountain
/// in the middle. Also stores spawn points and the bot waypoint graph in the World component.
/// </summary>
public static class MapBuilder
{
    public const string ScenePath = "Assets/Scenes/Main.unity";
    private const float Pitch = 52f;          // block + street
    private const float Street = 12f;         // street width
    private const float BlockSize = Pitch - Street; // block incl. sidewalk (40)
    private const float Sidewalk = 3f;
    private const float Half = 78f + Street / 2f; // playable half size

    private static Transform map;
    private static System.Random rng;
    private static float kitScale = 1f;
    private static readonly List<Vector3> waypoints = new List<Vector3>();
    private static readonly List<Vector2Int> links = new List<Vector2Int>();
    private static readonly List<Renderer> lamps = new List<Renderer>();

    public static void BuildScene()
    {
        rng = new System.Random(7);
        tiled.Clear();
        waypoints.Clear();
        links.Clear();
        lamps.Clear();
        AssetSetup.Folder("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.shadowBias = 0.04f;
        sun.shadowNormalBias = 0.3f;
        sunGo.transform.rotation = Quaternion.Euler(48, -35, 0);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.08f;
        cam.farClipPlane = 260f;
        cam.fieldOfView = 60f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<CameraRig>().Cam = cam;
        camGo.transform.position = new Vector3(0, 30, -60);

        var skybox = new Material(Shader.Find("Skybox/Procedural"));
        skybox.SetFloat("_SunSize", 0.035f);
        skybox.SetFloat("_AtmosphereThickness", 1f);
        skybox.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.62f));
        skybox.SetColor("_GroundColor", new Color(0.35f, 0.33f, 0.3f));
        AssetDatabase.DeleteAsset("Assets/Generated/Sky.mat");
        AssetDatabase.CreateAsset(skybox, "Assets/Generated/Sky.mat");
        RenderSettings.skybox = skybox;
        RenderSettings.sun = sun;

        map = new GameObject("Map").transform;
        MeasureKit();
        BuildGround();
        for (int bx = -1; bx <= 1; bx++)
            for (int bz = -1; bz <= 1; bz++)
            {
                var center = new Vector3(bx * Pitch, 0, bz * Pitch);
                if (bx == 0 && bz == 0) BuildPark(center);
                else BuildBlock(center, bx, bz);
            }
        BuildStreetProps();
        BuildBackdrop();
        BuildWaypoints();

        foreach (var t in map.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            if (t.gameObject.layer != 0) t.gameObject.layer = 0;
        }

        var worldGo = new GameObject("World");
        var world = worldGo.AddComponent<World>();
        world.Sun = sun;
        world.Skybox = skybox;
        world.Center = Vector3.zero;
        world.Radius = Half;
        world.Waypoints = waypoints.ToArray();
        world.Links = links.ToArray();
        world.Spawns = Spawns().ToArray();
        world.NightLamps = lamps.ToArray();

        new GameObject("App").AddComponent<App>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"[Map] built: {map.GetComponentsInChildren<Renderer>().Length} renderers, {waypoints.Count} waypoints, {world.Spawns.Length} spawns");
    }

    // ------------------------------------------------------------------ helpers

    private static GameObject Kit(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetSetup.CityModels}/{name}.fbx");

    private static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    private static void MeasureKit()
    {
        var probe = Kit("Building_Small_1");
        if (probe == null) { Debug.LogWarning("[Map] kit missing, using fallback boxes"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(probe);
        var b = WorldBounds(go);
        Object.DestroyImmediate(go);
        Debug.Log($"[Map] Building_Small_1 size {b.size}");
        if (b.size.y < 1f) kitScale = 100f;
        else if (b.size.y > 200f) kitScale = 0.01f;
    }

    /// <summary>Instantiate a kit model so that its bounds are centered at (x, z) and sit on the ground.</summary>
    private static GameObject Place(string kitName, Vector3 center, float yaw, Transform parent = null, float y = 0f)
    {
        var prefab = Kit(kitName);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent ?? map);
        go.transform.localScale *= kitScale;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        go.transform.position = Vector3.zero;
        var b = WorldBounds(go);
        go.transform.position = new Vector3(center.x - b.center.x, y - b.min.y, center.z - b.center.z);
        return go;
    }

    private static Vector3 FootprintSize(string kitName, float yaw)
    {
        var go = Place(kitName, Vector3.zero, yaw);
        if (go == null) return Vector3.zero;
        var size = WorldBounds(go).size;
        Object.DestroyImmediate(go);
        return size;
    }

    private static GameObject Box(string name, Vector3 center, Vector3 size, Material material, Transform parent = null, float yaw = 0f, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent ?? map, false);
        go.transform.position = center;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static GameObject Cylinder(string name, Vector3 center, float radius, float height, Material material, Transform parent = null, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent ?? map, false);
        go.transform.position = center;
        go.transform.localScale = new Vector3(radius * 2, height / 2, radius * 2);
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);

    private static readonly Dictionary<string, Material> tiled = new Dictionary<string, Material>();

    private static Material Tiled(string name, string sourceMaterial, Vector2 tiling, Color? tint = null)
    {
        // created once per build: re-creating the asset would leave earlier users with a missing (pink) material
        if (tiled.TryGetValue(name, out var cached) && cached != null) return cached;
        AssetSetup.CityMaterials.TryGetValue(sourceMaterial, out var source);
        string path = $"Assets/Generated/Materials/{name}.mat";
        AssetDatabase.DeleteAsset(path);
        var m = source != null ? new Material(source) : new Material(Shader.Find("Standard"));
        m.name = name;
        m.mainTextureScale = tiling;
        if (tint.HasValue) m.color = tint.Value;
        AssetDatabase.CreateAsset(m, path);
        tiled[name] = m;
        return m;
    }

    // ------------------------------------------------------------------ ground and blocks

    private static void BuildGround()
    {
        float size = Half * 2 + 120;
        var asphalt = Tiled("Ground_Asphalt", "MI_Asphalt", new Vector2(size / 6f, size / 6f), new Color(0.78f, 0.78f, 0.8f));
        var ground = Box("Asphalt", new Vector3(0, -0.5f, 0), new Vector3(size, 1f, size), asphalt);
        ground.GetComponent<Renderer>().receiveShadows = true;
        var white = AssetSetup.Solid("LineWhite", new Color(0.85f, 0.85f, 0.82f), 0.2f);
        var yellow = AssetSetup.Solid("LineYellow", new Color(0.95f, 0.75f, 0.15f), 0.2f);
        // lane markings on every street line
        foreach (float c in new[] { -78f, -26f, 26f, 78f })
        {
            for (float t = -Half + 2; t < Half - 2; t += 5f)
            {
                bool nearCross = new[] { -78f, -26f, 26f, 78f }.Any(x => Mathf.Abs(t + 1.25f - x) < Street / 2f + 0.5f);
                if (nearCross) continue;
                Box("Lane", new Vector3(c, 0.005f, t + 1.25f), new Vector3(0.15f, 0.01f, 2.5f), c == 26f || c == -26f ? yellow : white, null, 0, false);
                Box("Lane", new Vector3(t + 1.25f, 0.005f, c), new Vector3(2.5f, 0.01f, 0.15f), c == 26f || c == -26f ? yellow : white, null, 0, false);
            }
            // crosswalks at each crossing
            foreach (float d in new[] { -78f, -26f, 26f, 78f })
                for (int i = -4; i <= 4; i++)
                {
                    Box("Crosswalk", new Vector3(c + i * 1.2f, 0.006f, d - Street / 2f - 1.2f), new Vector3(0.6f, 0.01f, 2.2f), white, null, 0, false);
                }
        }
    }

    private static void SidewalkSlab(Vector3 center, Material material)
    {
        Box("Sidewalk", center + new Vector3(0, 0.08f, 0), new Vector3(BlockSize, 0.16f, BlockSize), material);
        var curb = AssetSetup.Solid("Curb", new Color(0.55f, 0.55f, 0.53f), 0.2f);
        float h = BlockSize / 2f;
        Box("Curb", center + new Vector3(0, 0.1f, h), new Vector3(BlockSize, 0.2f, 0.3f), curb);
        Box("Curb", center + new Vector3(0, 0.1f, -h), new Vector3(BlockSize, 0.2f, 0.3f), curb);
        Box("Curb", center + new Vector3(h, 0.1f, 0), new Vector3(0.3f, 0.2f, BlockSize), curb);
        Box("Curb", center + new Vector3(-h, 0.1f, 0), new Vector3(0.3f, 0.2f, BlockSize), curb);
    }

    private static readonly string[] Buildings = { "Building_Large_2", "Building_Medium_2_001", "Building_Small_1" };

    private static void BuildBlock(Vector3 center, int bx, int bz)
    {
        var concrete = Tiled("Sidewalk_Concrete", "MI_Concrete", new Vector2(10, 10));
        SidewalkSlab(center, concrete);
        var block = new GameObject($"Block {bx},{bz}").transform;
        block.SetParent(map, false);
        float inner = BlockSize / 2f - Sidewalk;  // 17: buildings stay inside the sidewalk ring
        var occupied = new List<Rect>();
        // walk around the four edges, buildings facing the street
        for (int edge = 0; edge < 4; edge++)
        {
            float yaw = edge * 90f;
            var along = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            var outward = Quaternion.Euler(0, yaw, 0) * Vector3.back;
            float cursor = -inner;
            int guard = 0;
            while (cursor < inner - 4f && guard++ < 10)
            {
                string kit = Buildings[rng.Next(Buildings.Length)];
                var size = FootprintSize(kit, yaw);
                if (size == Vector3.zero) break;
                float width = Mathf.Abs(Vector3.Dot(size, along));
                float depth = Mathf.Abs(Vector3.Dot(size, outward));
                if (depth > inner * 1.15f || width > inner * 2f) { cursor += 3f; continue; }
                if (cursor + width > inner) { cursor += 3f; continue; }
                var pos = center + along * (cursor + width / 2f) + outward * (inner - depth / 2f) + Vector3.up * 0.16f;
                var rect = new Rect(pos.x - size.x / 2f + 0.3f, pos.z - size.z / 2f + 0.3f, size.x - 0.6f, size.z - 0.6f);
                if (occupied.Any(o => o.Overlaps(rect))) { cursor += 3f; continue; }
                occupied.Add(rect);
                Place(kit, pos, yaw, block, 0.16f);
                cursor += width + R(0f, 2.5f);
            }
        }
        // what is left in the middle of the block becomes a small yard with cover
        var crate = AssetSetup.Solid("Crate", new Color(0.5f, 0.36f, 0.2f), 0.25f);
        for (int i = 0; i < 3; i++)
        {
            var p = center + new Vector3(R(-6, 6), 0, R(-6, 6));
            if (occupied.Any(o => o.Contains(new Vector2(p.x, p.z)))) continue;
            Box("Crate", p + Vector3.up * 0.76f, Vector3.one * 1.2f, crate, block, R(0, 90));
        }
    }

    private static void BuildPark(Vector3 center)
    {
        var grass = AssetSetup.Mat("Grass", new Color(0.32f, 0.45f, 0.2f), 0.15f);
        var stone = Tiled("Park_Stone", "MI_Concrete", new Vector2(6, 6), new Color(0.82f, 0.8f, 0.75f));
        var concrete = Tiled("Sidewalk_Concrete2", "MI_Concrete", new Vector2(10, 10));
        SidewalkSlab(center, concrete);
        var park = new GameObject("Park").transform;
        park.SetParent(map, false);
        Box("Lawn", center + new Vector3(0, 0.17f, 0), new Vector3(BlockSize - Sidewalk * 2, 0.04f, BlockSize - Sidewalk * 2), grass, park);
        // paths in a cross
        Box("Path", center + new Vector3(0, 0.19f, 0), new Vector3(4f, 0.02f, BlockSize - Sidewalk * 2), stone, park, 0, false);
        Box("Path", center + new Vector3(0, 0.19f, 0), new Vector3(BlockSize - Sidewalk * 2, 0.02f, 4f), stone, park, 0, false);
        // fountain
        var water = AssetSetup.Mat("Water", new Color(0.15f, 0.3f, 0.38f), 0.95f, 0.1f);
        Cylinder("Fountain", center + new Vector3(0, 0.55f, 0), 4.2f, 0.8f, stone, park);
        Cylinder("Water", center + new Vector3(0, 0.96f, 0), 3.8f, 0.02f, water, park, false);
        Cylinder("Pillar", center + new Vector3(0, 1.9f, 0), 0.5f, 2.2f, stone, park);
        Cylinder("Bowl", center + new Vector3(0, 3.0f, 0), 1.3f, 0.3f, stone, park);
        // low walls and planters for cover, trees for the look
        var wall = Tiled("Wall_Brick", "MI_RedBrick", new Vector2(2, 1));
        foreach (var (x, z, yaw) in new[] { (-9f, 9f, 45f), (9f, 9f, -45f), (-9f, -9f, -45f), (9f, -9f, 45f) })
            Box("LowWall", center + new Vector3(x, 0.75f, z), new Vector3(5f, 1.2f, 0.5f), wall, park, yaw);
        var bark = AssetSetup.Solid("Bark", new Color(0.3f, 0.22f, 0.15f), 0.1f);
        var leaves = AssetSetup.Solid("Leaves", new Color(0.22f, 0.4f, 0.15f), 0.15f);
        foreach (var (x, z) in new[] { (-13f, -5f), (-13f, 6f), (13f, -6f), (13f, 5f), (-5f, 13f), (6f, 13f), (-6f, -13f), (5f, -13f) })
        {
            var p = center + new Vector3(x, 0, z);
            Cylinder("Trunk", p + Vector3.up * 1.6f, 0.22f, 3.2f, bark, park);
            for (int k = 0; k < 3; k++)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(leaf.GetComponent<Collider>());
                leaf.name = "Leaves";
                leaf.transform.SetParent(park, false);
                leaf.transform.position = p + new Vector3(R(-0.8f, 0.8f), 3.6f + k * 0.7f, R(-0.8f, 0.8f));
                leaf.transform.localScale = Vector3.one * R(2.2f, 3.2f) * (1f - k * 0.18f);
                leaf.GetComponent<Renderer>().sharedMaterial = leaves;
            }
        }
        var bench = AssetSetup.Solid("BenchWood", new Color(0.42f, 0.28f, 0.16f), 0.3f);
        foreach (var (x, z, yaw) in new[] { (-2.6f, 10f, 90f), (2.6f, -10f, 90f), (10f, 2.6f, 0f), (-10f, -2.6f, 0f) })
            Box("Bench", center + new Vector3(x, 0.55f, z), new Vector3(2.2f, 0.12f, 0.6f), bench, park, yaw);
    }

    // ------------------------------------------------------------------ street props

    private static void BuildStreetProps()
    {
        var props = new GameObject("StreetProps").transform;
        props.SetParent(map, false);
        Color[] paints = { new Color(0.6f, 0.08f, 0.06f), new Color(0.1f, 0.18f, 0.35f), new Color(0.75f, 0.75f, 0.72f), new Color(0.1f, 0.1f, 0.11f), new Color(0.85f, 0.65f, 0.1f), new Color(0.2f, 0.35f, 0.25f) };
        float[] lines = { -78f, -26f, 26f, 78f };
        // parked cars along the curbs
        foreach (float line in lines)
            for (int seg = 0; seg < 3; seg++)
            {
                float start = -52f + seg * Pitch;
                for (int k = 0; k < 2; k++)
                {
                    if (rng.NextDouble() < 0.35) continue;
                    float t = start + R(-12f, 12f);
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    var paint = paints[rng.Next(paints.Length)];
                    if (rng.NextDouble() < 0.5) Car(props, new Vector3(line + side * (Street / 2f - 1.3f), 0, t), 0f, paint);
                    else Car(props, new Vector3(t, 0, line + side * (Street / 2f - 1.3f)), 90f, paint);
                }
            }
        // concrete barriers and dumpsters in the streets
        var barrier = Tiled("Barrier_Concrete", "MI_Concrete", new Vector2(1, 1), new Color(0.85f, 0.84f, 0.8f));
        var dumpster = AssetSetup.Solid("Dumpster", new Color(0.12f, 0.3f, 0.2f), 0.35f, 0.3f);
        for (int i = 0; i < 26; i++)
        {
            float line = lines[rng.Next(lines.Length)];
            float t = R(-70f, 70f);
            if (lines.Any(l => Mathf.Abs(t - l) < Street)) continue;
            bool vertical = rng.NextDouble() < 0.5;
            float offset = (rng.NextDouble() < 0.5 ? -1f : 1f) * R(2.4f, 3.6f);   // keep the middle of the street free for the bots
            var p = vertical ? new Vector3(line + offset, 0, t) : new Vector3(t, 0, line + offset);
            if (Spawns().Any(sp => (sp - p).sqrMagnitude < 25f)) continue;
            float yaw = (vertical ? 0f : 90f) + R(-25f, 25f);
            if (rng.NextDouble() < 0.7)
            {
                var b = Box("Barrier", p + Vector3.up * 0.45f, new Vector3(0.6f, 0.9f, 3f), barrier, props, yaw);
                Box("BarrierTop", p + Vector3.up * 0.98f, new Vector3(0.3f, 0.16f, 3f), barrier, b.transform.parent, yaw);
            }
            else Box("Dumpster", p + Vector3.up * 0.75f, new Vector3(1.8f, 1.5f, 1.2f), dumpster, props, yaw);
        }
        // street lights on the sidewalk corners (lamps glow at night)
        var pole = AssetSetup.Solid("Pole", new Color(0.15f, 0.15f, 0.16f), 0.4f, 0.6f);
        var lampMat = AssetSetup.Mat("Lamp", new Color(1f, 0.9f, 0.7f), 0.5f, 0f, null, null, null, null, new Color(2.2f, 1.8f, 1.2f));
        for (int bx = -1; bx <= 1; bx++)
            for (int bz = -1; bz <= 1; bz++)
                foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    var p = new Vector3(bx * Pitch + sx * (BlockSize / 2f - 0.8f), 0, bz * Pitch + sz * (BlockSize / 2f - 0.8f));
                    Cylinder("LightPole", p + Vector3.up * 3.2f, 0.09f, 6.4f, pole, props);
                    var lamp = Box("Lamp", p + new Vector3(-sx * 0.6f, 6.3f, -sz * 0.6f), new Vector3(0.5f, 0.15f, 0.5f), lampMat, props, 0, false);
                    lamps.Add(lamp.GetComponent<Renderer>());
                    // kit props near the corners: bollards and planters
                    if (rng.NextDouble() < 0.5) Place("Prop_Planter_Single", p + new Vector3(-sx * 2.5f, 0, 0), 0, props, 0.16f);
                    if (rng.NextDouble() < 0.5) Place("Prop_Bollard", p + new Vector3(0, 0, -sz * 2.5f), 0, props, 0.16f);
                }
    }

    private static void Car(Transform parent, Vector3 pos, float yaw, Color paint)
    {
        var car = new GameObject("Car").transform;
        car.SetParent(parent, false);
        var body = AssetSetup.Solid("CarPaint_" + ColorUtility.ToHtmlStringRGB(paint), paint, 0.82f, 0.45f);
        var glass = AssetSetup.Solid("CarGlass", new Color(0.06f, 0.08f, 0.1f), 0.95f, 0.3f);
        var tyre = AssetSetup.Solid("Tyre", new Color(0.05f, 0.05f, 0.05f), 0.2f);
        var chrome = AssetSetup.Solid("Chrome", new Color(0.75f, 0.75f, 0.75f), 0.9f, 1f);
        var light = AssetSetup.Mat("CarLight", new Color(1f, 0.95f, 0.85f), 0.9f, 0f, null, null, null, null, new Color(0.6f, 0.55f, 0.45f));
        var tail = AssetSetup.Mat("CarTail", new Color(0.8f, 0.05f, 0.05f), 0.9f, 0f, null, null, null, null, new Color(0.4f, 0.02f, 0.02f));
        Box("Body", new Vector3(0, 0.62f, 0), new Vector3(1.8f, 0.6f, 4.4f), body, car);
        Box("Cabin", new Vector3(0, 1.18f, -0.2f), new Vector3(1.6f, 0.55f, 2.3f), glass, car);
        Box("Roof", new Vector3(0, 1.47f, -0.25f), new Vector3(1.55f, 0.06f, 1.9f), body, car, 0, false);
        Box("Bumper", new Vector3(0, 0.42f, 2.22f), new Vector3(1.82f, 0.18f, 0.12f), chrome, car, 0, false);
        Box("Bumper", new Vector3(0, 0.42f, -2.22f), new Vector3(1.82f, 0.18f, 0.12f), chrome, car, 0, false);
        Box("Headlight", new Vector3(0.6f, 0.68f, 2.2f), new Vector3(0.35f, 0.12f, 0.04f), light, car, 0, false);
        Box("Headlight", new Vector3(-0.6f, 0.68f, 2.2f), new Vector3(0.35f, 0.12f, 0.04f), light, car, 0, false);
        Box("Taillight", new Vector3(0.65f, 0.7f, -2.21f), new Vector3(0.3f, 0.12f, 0.04f), tail, car, 0, false);
        Box("Taillight", new Vector3(-0.65f, 0.7f, -2.21f), new Vector3(0.3f, 0.12f, 0.04f), tail, car, 0, false);
        foreach (var (x, z) in new[] { (0.85f, 1.4f), (-0.85f, 1.4f), (0.85f, -1.4f), (-0.85f, -1.4f) })
        {
            var wheel = Cylinder("Wheel", Vector3.zero, 0.36f, 0.28f, tyre, car, false);
            wheel.transform.localPosition = new Vector3(x, 0.36f, z);
            wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
        }
        car.position = pos;
        car.rotation = Quaternion.Euler(0, yaw + (rng.NextDouble() < 0.5 ? 180f : 0f), 0);
    }

    /// <summary>A ring of buildings outside the playable area and invisible walls at the edge.</summary>
    private static void BuildBackdrop()
    {
        var back = new GameObject("Backdrop").transform;
        back.SetParent(map, false);
        float ring = Half + 14f;
        for (float t = -ring; t <= ring; t += 24f)
        {
            foreach (var (pos, yaw) in new[] { (new Vector3(t, 0, ring), 180f), (new Vector3(t, 0, -ring), 0f), (new Vector3(ring, 0, t), 270f), (new Vector3(-ring, 0, t), 90f) })
                Place(Buildings[rng.Next(Buildings.Length)], pos, yaw, back);
        }
        foreach (var (c, s) in new[] { (new Vector3(0, 10, Half + 1), new Vector3(Half * 2 + 4, 20, 1)), (new Vector3(0, 10, -Half - 1), new Vector3(Half * 2 + 4, 20, 1)),
                                       (new Vector3(Half + 1, 10, 0), new Vector3(1, 20, Half * 2 + 4)), (new Vector3(-Half - 1, 10, 0), new Vector3(1, 20, Half * 2 + 4)) })
        {
            var wall = new GameObject("InvisibleWall");
            wall.transform.SetParent(back, false);
            wall.transform.position = c;
            wall.AddComponent<BoxCollider>().size = s;
        }
    }

    // ------------------------------------------------------------------ bots and spawns

    private static int Node(Vector3 p)
    {
        for (int i = 0; i < waypoints.Count; i++)
            if ((waypoints[i] - p).sqrMagnitude < 0.5f) return i;
        waypoints.Add(p);
        return waypoints.Count - 1;
    }

    private static void Link(Vector3 a, Vector3 b) => links.Add(new Vector2Int(Node(a), Node(b)));

    private static void BuildWaypoints()
    {
        float[] lines = { -78f, -26f, 26f, 78f };
        foreach (float line in lines)
            for (int i = 0; i < lines.Length - 1; i++)
            {
                float a = lines[i], b = lines[i + 1], mid = (a + b) / 2f;
                Link(new Vector3(line, 0, a), new Vector3(line, 0, mid));
                Link(new Vector3(line, 0, mid), new Vector3(line, 0, b));
                Link(new Vector3(a, 0, line), new Vector3(mid, 0, line));
                Link(new Vector3(mid, 0, line), new Vector3(b, 0, line));
            }
        // across the park
        Link(new Vector3(-26, 0, 0), new Vector3(0, 0, 26));
        Link(new Vector3(0, 0, 26), new Vector3(26, 0, 0));
        Link(new Vector3(26, 0, 0), new Vector3(0, 0, -26));
        Link(new Vector3(0, 0, -26), new Vector3(-26, 0, 0));
    }

    private static List<Vector3> Spawns()
    {
        var list = new List<Vector3>();
        foreach (float t in new[] { -52f, 0f, 52f })
        {
            list.Add(new Vector3(-78, 0, t));
            list.Add(new Vector3(78, 0, t));
            list.Add(new Vector3(t, 0, -78));
            list.Add(new Vector3(t, 0, 78));
        }
        foreach (float x in new[] { -78f, 78f })
            foreach (float z in new[] { -78f, 78f })
                list.Add(new Vector3(x, 0, z));
        return list;
    }
}
