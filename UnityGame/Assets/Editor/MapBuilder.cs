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

    // the metro under the south street (z = -26): floor 5 m down, the street slab is its ceiling
    private const float MetroFloor = -5f, MetroCeiling = -1f;
    private static readonly Rect MetroWest = Rect.MinMaxRect(-71.5f, -27f, -20f, -20.5f);
    private static readonly Rect MetroHall = Rect.MinMaxRect(-20f, -32f, 20f, -20f);
    private static readonly Rect MetroEast = Rect.MinMaxRect(20f, -27f, 71.5f, -20.5f);
    // stairwells: open holes in the street
    private static readonly Rect StairWest = Rect.MinMaxRect(-71.3f, -23.6f, -62f, -20.6f);
    private static readonly Rect StairEast = Rect.MinMaxRect(62f, -23.6f, 71.3f, -20.6f);
    private static readonly Rect StairHall = Rect.MinMaxRect(-5f, -23.2f, 4.3f, -20.3f);
    private static readonly Rect[] Holes = { StairWest, StairEast, StairHall };
    private static bool OverHole(Vector3 p, float margin = 2.5f) => Underground.InAny(Holes, p, margin);

    public static void BuildScene()
    {
        rng = new System.Random(7);
        tiled.Clear();
        carModels = null;
        waypoints.Clear();
        links.Clear();
        lamps.Clear();
        global::Buildings.Night = lamps;
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

        MeshGen.Reset();
        map = new GameObject("Map_Downtown").transform;
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
        BuildMetro();
        BuildBackdrop();
        BuildWaypoints();

        foreach (var t in map.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            if (t.gameObject.layer != 0 && t.gameObject.layer != World.FenceLayer) t.gameObject.layer = 0;
        }
        global::Buildings.Night = null;
        var downtownNight = MapOptimizer.Merge(map, "Downtown", null, lamps);

        var downtown = new MapLayout
        {
            Name = "DOWNTOWN", Root = map.gameObject, Center = Vector3.zero, Radius = Half,
            Waypoints = waypoints.ToArray(), Links = links.ToArray(), Spawns = Spawns().ToArray(),
            NightLights = downtownNight.ToArray(),
        };
        // the port is built while the downtown map is switched off so their colliders do not mix
        map.gameObject.SetActive(false);
        var port = PortBuilder.Build(null);
        port.Root.SetActive(false);
        var village = VillageBuilder.Build();
        village.Root.SetActive(false);
        var town = TownBuilder.Build();
        town.Root.SetActive(false);
        map.gameObject.SetActive(true);

        var worldGo = new GameObject("World");
        var world = worldGo.AddComponent<World>();
        world.Sun = sun;
        world.Skybox = skybox;
        world.Layouts = new[] { downtown, port, village, town };

        new GameObject("App").AddComponent<App>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"[Map] built: {map.GetComponentsInChildren<Renderer>().Length} renderers, {waypoints.Count} waypoints, {downtown.Spawns.Length} spawns");
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
        var asphalt = PortBuilder.Pbr("Road", "Asphalt025C", new Color(0.8f, 0.8f, 0.83f), 0.55f);
        foreach (var r in Underground.Cover(new Rect(-size / 2f, -size / 2f, size, size), Holes))
            MeshGen.TexBox("Asphalt", new Vector3(r.center.x, -0.5f, r.center.y), new Vector3(r.width, 1f, r.height), asphalt, 4f, map);
        var white = AssetSetup.Solid("LineWhite", new Color(0.85f, 0.85f, 0.82f), 0.2f);
        var yellow = AssetSetup.Solid("LineYellow", new Color(0.95f, 0.75f, 0.15f), 0.2f);
        // lane markings on every street line
        foreach (float c in new[] { -78f, -26f, 26f, 78f })
        {
            for (float t = -Half + 2; t < Half - 2; t += 5f)
            {
                bool nearCross = new[] { -78f, -26f, 26f, 78f }.Any(x => Mathf.Abs(t + 1.25f - x) < Street / 2f + 0.5f);
                if (nearCross) continue;
                if (OverHole(new Vector3(c, 0, t + 1.25f), 0.6f) || OverHole(new Vector3(t + 1.25f, 0, c), 0.6f)) continue;
                Box("Lane", new Vector3(c, 0.005f, t + 1.25f), new Vector3(0.15f, 0.01f, 2.5f), c == 26f || c == -26f ? yellow : white, null, 0, false);
                Box("Lane", new Vector3(t + 1.25f, 0.005f, c), new Vector3(2.5f, 0.01f, 0.15f), c == 26f || c == -26f ? yellow : white, null, 0, false);
            }
            // crosswalks at each crossing
            foreach (float d in new[] { -78f, -26f, 26f, 78f })
                for (int i = -4; i <= 4; i++)
                {
                    if (OverHole(new Vector3(c + i * 1.2f, 0, d - Street / 2f - 1.2f), 0.5f)) continue;
                    Box("Crosswalk", new Vector3(c + i * 1.2f, 0.006f, d - Street / 2f - 1.2f), new Vector3(0.6f, 0.01f, 2.2f), white, null, 0, false);
                }
        }
    }

    private static void SidewalkSlab(Vector3 center, Material material)
    {
        MeshGen.RoundBox("Sidewalk", center + new Vector3(0, 0.08f, 0), new Vector3(BlockSize - 0.2f, 0.16f, BlockSize - 0.2f), 0.06f, material, 2.5f, map);
        // granite curbs with rounded edges, and rounded corner stones where they meet
        var curb = PortBuilder.Pbr("CurbStone", "Concrete034", new Color(0.78f, 0.78f, 0.76f), 0.35f);
        float h = BlockSize / 2f;
        MeshGen.RoundBox("Curb", center + new Vector3(0, 0.1f, h), new Vector3(BlockSize - 0.6f, 0.2f, 0.3f), 0.06f, curb, 1f, map);
        MeshGen.RoundBox("Curb", center + new Vector3(0, 0.1f, -h), new Vector3(BlockSize - 0.6f, 0.2f, 0.3f), 0.06f, curb, 1f, map);
        MeshGen.RoundBox("Curb", center + new Vector3(h, 0.1f, 0), new Vector3(0.3f, 0.2f, BlockSize - 0.6f), 0.06f, curb, 1f, map);
        MeshGen.RoundBox("Curb", center + new Vector3(-h, 0.1f, 0), new Vector3(0.3f, 0.2f, BlockSize - 0.6f), 0.06f, curb, 1f, map);
        foreach (float x in new[] { -h, h })
            foreach (float z in new[] { -h, h })
                MeshGen.TexCylinder("CurbCorner", center + new Vector3(x, 0, z), 0.3f, 0.2f, curb, 1f, map);
    }

    private static readonly string[] BuildingKits = { "Building_Large_2", "Building_Medium_2_001", "Building_Small_1" };

    private static void BuildBlock(Vector3 center, int bx, int bz)
    {
        var paving = Paving;
        SidewalkSlab(center, paving);
        if ((bx == -1 && bz == 1) || (bx == 1 && bz == -1))
        {
            EnterableBlock(center, bx);
            return;
        }
        var block = new GameObject($"Block {bx},{bz}").transform;
        block.SetParent(map, false);
        float inner = BlockSize / 2f - Sidewalk;  // 17: buildings stay inside the sidewalk ring
        var occupied = new List<Rect>();
        var placed = new List<(GameObject go, Vector3 along, Vector3 outward, float start, float width)>();
        // a tall tower and medium buildings players can enter, with stairs inside up to the roof
        var (bw, bl, floors) = (bx, bz) switch
        {
            (1, 1) => (14f, 14f, 8),
            (-1, -1) => (16f, 12f, 5),
            (0, -1) => (12f, 12f, 4),
            (-1, 0) => (12f, 14f, 3),
            (1, 0) => (14f, 14f, 7),
            (0, 1) => (12f, 16f, 6),
            _ => (0f, 0f, 0),
        };
        if (floors > 0)
        {
            var pos = center + new Vector3(inner - bw / 2f, 0.16f, -inner + bl / 2f);
            occupied.Add(new Rect(pos.x - bw / 2f - 0.5f, pos.z - bl / 2f - 0.5f, bw + 1f, bl + 1f));
            // the two tallest are dark glass towers like the city's other high-rises
            var style = floors >= 7
                ? new global::Buildings.Style
                {
                    Outer = PortBuilder.Pbr("GlassSpandrel", "Metal032", new Color(0.2f, 0.23f, 0.27f), 0.8f, 0.5f),
                    Inner = PortBuilder.Pbr("PlasterInner", "Plaster001", new Color(0.93f, 0.92f, 0.88f), 0.5f),
                    Floor = PortBuilder.Pbr("Tiles", "Tiles101", Color.white, 0.6f),
                    Stairs = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f),
                    Trim = PortBuilder.Pbr("DarkAluminium", "Metal032", new Color(0.3f, 0.32f, 0.35f), 0.75f, 0.7f),
                    Furniture = PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f),
                    Glass = AssetSetup.Mat("TowerGlass", new Color(0.07f, 0.1f, 0.13f), 0.95f, 0.85f),
                }
                : floors >= 6
                ? new global::Buildings.Style
                {
                    Outer = PortBuilder.Pbr("TowerPlaster", "PaintedPlaster017", new Color(0.78f, 0.82f, 0.86f), 0.5f),
                    Inner = PortBuilder.Pbr("PlasterInner", "Plaster001", new Color(0.93f, 0.92f, 0.88f), 0.5f),
                    Floor = PortBuilder.Pbr("Tiles", "Tiles101", Color.white, 0.6f),
                    Stairs = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f),
                    Trim = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f),
                    Furniture = PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f),
                }
                : new global::Buildings.Style
                {
                    Outer = floors == 5 ? PortBuilder.Pbr("Bricks", "Bricks076A", Color.white, 0.6f) : PortBuilder.Pbr("PlasterBeige", "PaintedPlaster017", new Color(0.95f, 0.86f, 0.72f), 0.5f),
                    Inner = PortBuilder.Pbr("PlasterInner", "Plaster001", new Color(0.93f, 0.92f, 0.88f), 0.5f),
                    Floor = PortBuilder.Pbr("WoodFloor", "WoodFloor051", Color.white, 0.6f),
                    Stairs = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f),
                    Trim = PortBuilder.Pbr("OldConcrete", "Concrete042A", Color.white, 0.6f),
                    Furniture = PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f),
                };
            global::Buildings.Build(block, floors >= 7 ? "GlassTower" : floors >= 6 ? "Tower" : "MidRise", pos, 0f, bw, bl, floors, style);
        }
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
                string kit = BuildingKits[rng.Next(BuildingKits.Length)];
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
                var building = Place(kit, pos, yaw, block, 0.16f);
                if (building != null) placed.Add((building, along, outward, cursor, width));
                cursor += width + R(0f, 2.5f);
            }
        }
        // fire escapes up to the roof of one or two of the lower buildings
        var steel = PortBuilder.Pbr("EscapeSteel", "Metal032", new Color(0.25f, 0.26f, 0.27f), 0.6f);
        var plate = PortBuilder.Pbr("Walkway", "DiamondPlate009", new Color(0.8f, 0.8f, 0.8f), 0.8f);
        int escapes = 0;
        foreach (var b in placed.OrderBy(_ => rng.Next()))
        {
            if (escapes >= 1 + (bx + bz + 2) % 2 || b.width < 10f) continue;
            // only buildings low enough for a fire escape all the way to the roof
            float height = WorldBounds(b.go).max.y;
            if (height > 17f) continue;
            var facade = center + b.along * (b.start + b.width / 2f - 4.3f) + b.outward * (BlockSize / 2f - Sidewalk) + Vector3.up * 0.16f;
            global::Buildings.FireEscape(block, facade, b.along, b.outward, height - 0.16f, true, plate, steel);
            escapes++;
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

    /// <summary>
    /// The metro under the south street: a station hall with a sunken track and a parked train, two
    /// tunnels to stairs at the far crossings, and a stair from the hall up into the middle of the
    /// street. Pillars, benches, ticket barriers and crates for cover; lights and light strips.
    /// </summary>
    private static void BuildMetro()
    {
        var t = new GameObject("Metro").transform;
        t.SetParent(map, false);
        var style = new Underground.Style
        {
            Floor = PortBuilder.Pbr("MetroTiles", "Tiles101", new Color(0.85f, 0.85f, 0.82f), 0.55f),
            Wall = PortBuilder.Pbr("MetroWall", "Plaster001", new Color(0.86f, 0.88f, 0.86f), 0.4f),
            Ceiling = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f),
            Trim = PortBuilder.Pbr("OldConcrete", "Concrete042A", Color.white, 0.6f),
            Lamp = AssetSetup.Mat("MetroLamp", new Color(1f, 0.97f, 0.9f), 0.5f, 0f, null, null, null, null, new Color(2.4f, 2.3f, 2.1f)),
        };
        var concrete = style.Ceiling;
        var steps = PortBuilder.Pbr("StairConcrete", "Concrete034", new Color(0.9f, 0.89f, 0.86f), 0.5f);
        float f = MetroFloor, c = MetroCeiling;
        // tunnels (open at their hall end) and the hall (open where the tunnels come in)
        // the hall floor drops 1 m into the track bed along its south side
        var trench = Rect.MinMaxRect(-20f, -32f, 20f, -28.6f);
        var none = new Rect[0];
        Underground.Room(t, MetroWest, f, c, style, Holes, none, ('E', -27f, -20.5f));
        Underground.Room(t, MetroEast, f, c, style, Holes, none, ('W', -27f, -20.5f));
        Underground.Room(t, MetroHall, f, c, style, Holes, new[] { trench }, ('W', -27f, -20.5f), ('E', -27f, -20.5f));
        MeshGen.TexBox("TrackBed", new Vector3(trench.center.x, f - 1.15f, trench.center.y), new Vector3(trench.width, 0.3f, trench.height), style.Trim, 2f, t);
        MeshGen.TexBox("PlatformEdge", new Vector3(0, f - 0.5f, -28.45f), new Vector3(40f, 1.1f, 0.3f), concrete, 2f, t);
        MeshGen.TexBox("SafetyLine", new Vector3(0, f + 0.005f, -28.2f), new Vector3(40f, 0.01f, 0.35f), AssetSetup.Solid("SafetyYellow", new Color(0.95f, 0.78f, 0.1f), 0.3f), 1f, t, 0, false);
        foreach (float z in new[] { -31.2f, -29.4f })
            MeshGen.TexBox("Rail", new Vector3(0, f - 0.92f, z), new Vector3(40f, 0.12f, 0.1f), AssetSetup.Solid("RailSteel", new Color(0.35f, 0.33f, 0.3f), 0.6f, 0.8f), 1f, t, 0, false);
        // a train standing in the station: cover, and a wall of windows
        var trainBody = AssetSetup.Mat("TrainBody", new Color(0.78f, 0.8f, 0.82f), 0.6f, 0.3f);
        var trainStripe = AssetSetup.Solid("TrainStripe", new Color(0.1f, 0.35f, 0.7f), 0.5f);
        var glass = AssetSetup.Solid("TrainGlass", new Color(0.08f, 0.1f, 0.12f), 0.9f, 0.4f);
        MeshGen.RoundBox("Train", new Vector3(3f, f - 1f + 1.6f, -30.3f), new Vector3(24f, 2.9f, 2.9f), 0.35f, trainBody, 3f, t);
        MeshGen.TexBox("TrainStripe", new Vector3(3f, f - 1f + 1.05f, -28.83f), new Vector3(23.6f, 0.25f, 0.04f), trainStripe, 1f, t, 0, false);
        for (float x = -7f; x <= 13f; x += 2.5f)
            MeshGen.TexBox("TrainWindow", new Vector3(3f + x - 3f, f - 1f + 2.1f, -28.83f), new Vector3(1.6f, 0.8f, 0.04f), glass, 1f, t, 0, false);
        // pillars, benches, kiosk, ticket barriers, crates
        foreach (float x in new[] { -14f, -7f, 8f, 14f })
            MeshGen.RoundBox("Pillar", new Vector3(x, (f + c) / 2f, -26.5f), new Vector3(0.8f, c - f, 0.8f), 0.12f, style.Wall, 2f, t);
        var wood = PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f);
        foreach (float x in new[] { -11f, 11f })
            MeshGen.RoundBox("Bench", new Vector3(x, f + 0.45f, -27.6f), new Vector3(3f, 0.12f, 0.6f), 0.04f, wood, 1f, t);
        MeshGen.RoundBox("Kiosk", new Vector3(13f, f + 1.2f, -21.6f), new Vector3(3f, 2.4f, 2f), 0.1f, AssetSetup.Solid("Kiosk", new Color(0.7f, 0.2f, 0.15f), 0.4f), 1f, t);
        for (int i = 0; i < 4; i++)
            MeshGen.RoundBox("Barrier", new Vector3(-17f + i * 1.4f, f + 0.55f, -22f), new Vector3(0.3f, 1.1f, 1.6f), 0.06f, concrete, 1f, t);
        Obstacles.CrateStack(t, new Vector3(-44f, f, -25.3f), 90f, rng);
        Obstacles.CrateStack(t, new Vector3(46f, f, -25.3f), -90f, rng);
        Obstacles.Blocks(t, new Vector3(-30f, f, -25.8f), 0f, 2);
        Obstacles.Blocks(t, new Vector3(30f, f, -25.8f), 0f, 2);
        Obstacles.AmmoCrate(t, new Vector3(-3f, f, -27.3f), 0f);
        // stairs: from the tunnel ends up to the street at the far crossings, and from the hall up
        global::Buildings.Stairs(t, new Vector3(-62f, f, StairWest.center.y), Vector3.left, -f, StairWest.height - 0.1f, steps);
        global::Buildings.Stairs(t, new Vector3(62f, f, StairEast.center.y), Vector3.right, -f, StairEast.height - 0.1f, steps);
        global::Buildings.Stairs(t, new Vector3(-5f, f, StairHall.center.y), Vector3.right, -f, StairHall.height - 0.1f, steps);
        // the side of each stair facing the room gets a wall up to the street (no fall from the steps)
        MeshGen.TexBox("StairWall", new Vector3(StairWest.center.x, (f + c) / 2f, StairWest.yMin - 0.15f), new Vector3(StairWest.width, c - f, 0.3f), style.Wall, 2f, t);
        MeshGen.TexBox("StairWall", new Vector3(StairEast.center.x, (f + c) / 2f, StairEast.yMin - 0.15f), new Vector3(StairEast.width, c - f, 0.3f), style.Wall, 2f, t);
        MeshGen.TexBox("StairWall", new Vector3(StairHall.center.x, (f + c) / 2f, StairHall.yMin - 0.15f), new Vector3(StairHall.width, c - f, 0.3f), style.Wall, 2f, t);
        // railings around the openings in the street, open where the stairs arrive
        var rail = PortBuilder.Pbr("CurbStone", "Concrete034", new Color(0.78f, 0.78f, 0.76f), 0.35f);
        Underground.Railing(t, StairWest, 'W', rail);
        Underground.Railing(t, StairEast, 'E', rail);
        Underground.Railing(t, StairHall, 'E', rail);
        // signs over the entrances
        var sign = AssetSetup.Mat("MetroSign", new Color(0.1f, 0.3f, 0.75f), 0.5f, 0f, null, null, null, null, new Color(0.2f, 0.5f, 1.2f));
        foreach (var (x, z) in new[] { (-72.2f, -24f), (72.2f, -24f), (5.2f, -23.6f) })
        {
            MeshGen.TexCylinder("SignPost", new Vector3(x, 0, z), 0.06f, 2.6f, AssetSetup.Solid("Pole", new Color(0.15f, 0.15f, 0.16f), 0.4f, 0.6f), 1f, t, false);
            MeshGen.RoundBox("Sign", new Vector3(x, 2.75f, z), new Vector3(0.12f, 0.6f, 0.6f), 0.05f, sign, 1f, t, 0, false);
        }
        // lights: a few real ones in the hall and the tunnels
        foreach (var p in new[] { new Vector3(-12f, -2f, -25f), new Vector3(3f, -2f, -25f), new Vector3(14f, -2f, -24f), new Vector3(-45f, -2f, -23.7f), new Vector3(45f, -2f, -23.7f) })
            Underground.Lamp(t, p);
    }

    private static Material Paving => PortBuilder.Pbr("Paving", "PavingStones070", new Color(0.92f, 0.9f, 0.88f), 0.5f);
    private static Material BrickWall => PortBuilder.Pbr("Bricks", "Bricks076A", Color.white, 0.6f);

    /// <summary>
    /// A block with one large building players can walk into: four floors joined by a stairwell that
    /// continues onto the roof, and a back yard with trees, a low wall and crates.
    /// </summary>
    private static void EnterableBlock(Vector3 center, int bx)
    {
        var block = new GameObject("Block Enterable").transform;
        block.SetParent(map, false);
        var style = bx < 0
            ? new global::Buildings.Style
            {
                Outer = PortBuilder.Pbr("Bricks", "Bricks076A", Color.white, 0.6f),
                Inner = PortBuilder.Pbr("PlasterInner", "Plaster001", new Color(0.93f, 0.92f, 0.88f), 0.5f),
                Floor = PortBuilder.Pbr("WoodFloor", "WoodFloor051", Color.white, 0.6f),
                Stairs = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f),
                Trim = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f),
                Furniture = PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f),
            }
            : new global::Buildings.Style
            {
                Outer = PortBuilder.Pbr("PlasterBeige", "PaintedPlaster017", new Color(0.95f, 0.86f, 0.72f), 0.5f),
                Inner = PortBuilder.Pbr("PlasterInner", "Plaster001", new Color(0.93f, 0.92f, 0.88f), 0.5f),
                Floor = PortBuilder.Pbr("Tiles", "Tiles101", Color.white, 0.6f),
                Stairs = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f),
                Trim = PortBuilder.Pbr("OldConcrete", "Concrete042A", Color.white, 0.6f),
                Furniture = PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f),
            };
        // the front door faces the street, the back door opens onto the yard
        global::Buildings.Build(block, "Apartment", center + new Vector3(0, 0.16f, bx < 0 ? -5f : 5f), bx < 0 ? 0f : 180f, 22f, 16f, 4, style);
        float yard = bx < 0 ? 9.5f : -9.5f;
        var grass = PortBuilder.Pbr("Grass", "Grass004", new Color(0.9f, 0.95f, 0.85f), 0.25f);
        MeshGen.RoundBox("Lawn", center + new Vector3(0, 0.2f, yard + (bx < 0 ? 3.5f : -3.5f)), new Vector3(BlockSize - Sidewalk * 2, 0.08f, 6f), 0.035f, grass, 3f, block);
        foreach (float x in new[] { -13f, -4f, 5f, 13f })
        {
            var p = center + new Vector3(x + R(-1f, 1f), 0.24f, yard + (bx < 0 ? 4f : -4f));
            Nature.Tree(Nature.Trees, rng, p, 6f, 9f, block);
            Nature.Place(Nature.Bushes[rng.Next(Nature.Bushes.Length)], p + new Vector3(R(-2f, 2f), 0, R(-1f, 1f)), R(0, 360), R(0.9f, 1.4f), block);
        }
        MeshGen.RoundBox("LowWall", center + new Vector3(-6f, 0.76f, yard), new Vector3(6f, 1.2f, 0.5f), 0.07f, BrickWall, 2f, block);
        MeshGen.RoundBox("LowWall", center + new Vector3(8f, 0.76f, yard), new Vector3(5f, 1.2f, 0.5f), 0.07f, BrickWall, 2f, block);
        var crate = AssetSetup.Solid("Crate", new Color(0.5f, 0.36f, 0.2f), 0.25f);
        Box("Crate", center + new Vector3(1.5f, 0.76f, yard - (bx < 0 ? 1.5f : -1.5f)), Vector3.one * 1.2f, crate, block, R(0, 90));
        Box("Crate", center + new Vector3(-12f, 0.76f, yard - (bx < 0 ? 1.5f : -1.5f)), Vector3.one * 1.2f, crate, block, R(0, 90));
    }

    /// <summary>
    /// The park: a raised stone terrace (1.6 m) around a sunken court with the fountain, joined by
    /// concrete stairs: up from the four streets, down into the court. Parapets along the court edge
    /// give cover for fighting between the high ring and the low middle.
    /// </summary>
    private static void BuildPark(Vector3 center)
    {
        var grass = PortBuilder.Pbr("Grass", "Grass004", new Color(0.9f, 0.95f, 0.85f), 0.25f);
        var stone = PortBuilder.Pbr("ParkPath", "PavingStones070", new Color(0.85f, 0.83f, 0.78f), 0.5f);
        var concrete = PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f);
        var steps = PortBuilder.Pbr("StairConcrete", "Concrete034", new Color(0.9f, 0.89f, 0.86f), 0.5f);
        SidewalkSlab(center, Paving);
        var park = new GameObject("Park").transform;
        park.SetParent(map, false);
        const float y0 = 0.16f, rise = 1.6f, outer = 15f, inner = 9f, stairHalf = 1.8f;
        float top = y0 + rise;
        // lawn strips around the terrace
        float margin = BlockSize / 2f - Sidewalk;
        foreach (float s in new[] { -1f, 1f })
        {
            MeshGen.RoundBox("Lawn", center + new Vector3(0, y0 + 0.02f, s * (outer + margin) / 2f), new Vector3(margin * 2, 0.06f, margin - outer), 0.025f, grass, 3f, park, 0, false);
            MeshGen.RoundBox("Lawn", center + new Vector3(s * (outer + margin) / 2f, y0 + 0.02f, 0), new Vector3(margin - outer, 0.06f, outer * 2), 0.025f, grass, 3f, park, 0, false);
        }
        // the terrace ring: concrete walls with a stone top, in four pieces
        void Terrace(Vector3 c, Vector3 size)
        {
            MeshGen.RoundBox("Terrace", center + c + Vector3.up * (y0 + (rise - 0.1f) / 2f), new Vector3(size.x, rise - 0.1f, size.z), 0.08f, concrete, 2f, park);
            MeshGen.RoundBox("TerraceTop", center + c + Vector3.up * (top - 0.05f), new Vector3(size.x + 0.1f, 0.1f, size.z + 0.1f), 0.04f, stone, 2.5f, park);
        }
        float ring = outer - inner;
        foreach (float s in new[] { -1f, 1f })
        {
            Terrace(new Vector3(0, 0, s * (inner + ring / 2f)), new Vector3(outer * 2, 0, ring));
            Terrace(new Vector3(s * (inner + ring / 2f), 0, 0), new Vector3(ring, 0, inner * 2));
        }
        // stairs: up from the street side, down into the court, with cheek walls
        var dirs = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        float run = Mathf.CeilToInt(rise / 0.2f) * 0.36f;
        foreach (var d in dirs)
        {
            var side = Vector3.Cross(Vector3.up, d);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            // outside: from the lawn up onto the terrace (going towards the middle)
            global::Buildings.Stairs(park, center + d * (outer + run) + Vector3.up * y0, -d, rise, stairHalf * 2f, steps);
            // inside: from the court up onto the terrace (going outwards)
            global::Buildings.Stairs(park, center + d * (inner - run) + Vector3.up * y0, d, rise, stairHalf * 2f, steps);
            foreach (float k in new[] { -1f, 1f })
            {
                MeshGen.RoundBox("CheekWall", center + d * (inner - run / 2f) + side * (k * (stairHalf + 0.15f)) + Vector3.up * (y0 + (rise + 0.9f) / 2f),
                    new Vector3(0.3f, rise + 0.9f, run), 0.06f, concrete, 2f, park, yaw);
                MeshGen.RoundBox("CheekWall", center + d * (outer + run / 2f) + side * (k * (stairHalf + 0.15f)) + Vector3.up * (y0 + rise / 2f),
                    new Vector3(0.3f, rise, run), 0.06f, concrete, 2f, park, yaw);
                // parapet along the edge of the court, open at the stairs
                float length = inner - stairHalf - 0.3f;
                MeshGen.RoundBox("Parapet", center + d * (inner + 0.2f) + side * (k * (stairHalf + 0.3f + length / 2f)) + Vector3.up * (top + 0.45f),
                    new Vector3(length, 0.9f, 0.35f), 0.07f, concrete, 2f, park, yaw + 90f);
            }
        }
        // corner planters with trees and bushes on the terrace
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
            {
                var p = center + new Vector3(x * 11.6f, top, z * 11.6f);
                MeshGen.RoundBox("Planter", p + Vector3.up * 0.3f, new Vector3(2.6f, 0.6f, 2.6f), 0.1f, concrete, 2f, park);
                MeshGen.RoundBox("PlanterSoil", p + Vector3.up * 0.58f, new Vector3(2.3f, 0.06f, 2.3f), 0.03f, grass, 2f, park, 0, false);
                if (Nature.Tree(Nature.Trees, rng, p + Vector3.up * 0.6f, 6f, 8.5f, park) == null)
                    Cylinder("Trunk", p + Vector3.up * 2.2f, 0.22f, 3.2f, AssetSetup.Solid("Bark", new Color(0.3f, 0.22f, 0.15f), 0.1f), park);
                Nature.Place(Nature.Bushes[rng.Next(Nature.Bushes.Length)], p + new Vector3(x * 1.7f, 0, -z * 0.4f), R(0, 360), R(0.8f, 1.2f), park);
            }
        // the sunken court: stone paving, lawn corners, the fountain and benches
        MeshGen.RoundBox("Court", center + Vector3.up * (y0 + 0.02f), new Vector3(inner * 2, 0.04f, inner * 2), 0.02f, stone, 2f, park, 0, false);
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                MeshGen.RoundBox("CourtLawn", center + new Vector3(x * 6.3f, y0 + 0.06f, z * 6.3f), new Vector3(4.6f, 0.06f, 4.6f), 0.03f, grass, 3f, park, 0, false);
        var water = AssetSetup.Mat("Water", new Color(0.15f, 0.3f, 0.38f), 0.95f, 0.1f);
        MeshGen.TexCylinder("Fountain", center + Vector3.up * y0, 2.8f, 0.7f, stone, 2f, park);
        MeshGen.TexCylinder("Water", center + Vector3.up * (y0 + 0.62f), 2.55f, 0.1f, water, 2f, park, false);
        MeshGen.TexCylinder("Pillar", center + Vector3.up * (y0 + 0.7f), 0.4f, 1.6f, stone, 1f, park);
        MeshGen.TexCylinder("Bowl", center + Vector3.up * (y0 + 2.2f), 1.1f, 0.3f, stone, 1f, park);
        var bench = PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f);
        foreach (var (x, z, yaw) in new[] { (-3.7f, -3.7f, -45f), (3.7f, 3.7f, -45f), (-3.7f, 3.7f, 45f), (3.7f, -3.7f, 45f) })
        {
            MeshGen.RoundBox("Bench", center + new Vector3(x, y0 + 0.48f, z), new Vector3(0.6f, 0.1f, 2.2f), 0.04f, bench, 1f, park, yaw);
            MeshGen.RoundBox("BenchLeg", center + new Vector3(x, y0 + 0.22f, z), new Vector3(0.5f, 0.44f, 1.8f), 0.05f, concrete, 1f, park, yaw);
        }
        // grass tufts on the lawns around the terrace
        for (int i = 0; i < 40; i++)
        {
            float a = R(-margin + 0.5f, margin - 0.5f), b = R(outer + 0.5f, margin - 0.5f);
            if (Mathf.Abs(a) < stairHalf + 0.8f) continue;
            var p = (i % 4) switch { 0 => new Vector3(a, 0, b), 1 => new Vector3(a, 0, -b), 2 => new Vector3(b, 0, a), _ => new Vector3(-b, 0, a) };
            Nature.Place(Nature.Ground[rng.Next(Nature.Ground.Length)], center + p + Vector3.up * (y0 + 0.05f), R(0, 360), R(0.35f, 0.7f), park);
        }
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
                    var carPos = rng.NextDouble() < 0.5 ? new Vector3(line + side * (Street / 2f - 1.3f), 0, t) : new Vector3(t, 0, line + side * (Street / 2f - 1.3f));
                    if (OverHole(carPos, 3.5f)) continue;
                    Car(props, carPos, Mathf.Abs(carPos.x - line) < 6f ? 0f : 90f, paint);
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
            if (OverHole(p, 2.5f)) continue;
            float yaw = (vertical ? 0f : 90f) + R(-25f, 25f);
            if (rng.NextDouble() < 0.7)
            {
                var b = Box("Barrier", p + Vector3.up * 0.45f, new Vector3(0.6f, 0.9f, 3f), barrier, props, yaw);
                Box("BarrierTop", p + Vector3.up * 0.98f, new Vector3(0.3f, 0.16f, 3f), barrier, b.transform.parent, yaw);
            }
            else Box("Dumpster", p + Vector3.up * 0.75f, new Vector3(1.8f, 1.5f, 1.2f), dumpster, props, yaw);
        }
        // varied cover in the streets (keeping the middle of the street free for the bots)
        Physics.SyncTransforms();
        var cover = new GameObject("Cover").transform;
        cover.SetParent(props, false);
        var spawns = Spawns();
        foreach (float line in lines)
            for (int seg = 0; seg < 3; seg++)
                for (int k = 0; k < 2; k++)
                {
                    float mid = -52f + seg * Pitch;
                    float t = mid + R(-15f, 15f);
                    if (lines.Any(l => Mathf.Abs(t - l) < Street / 2f + 2.5f)) continue;
                    bool vertical = rng.NextDouble() < 0.5;
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    int kind = rng.Next(6);
                    float offset = side * (kind == 2 || kind == 0 ? 3.7f : kind == 3 ? 3.0f : 3.4f);
                    var p = vertical ? new Vector3(line + offset, 0, t) : new Vector3(t, 0, line + offset);
                    if (spawns.Any(sp => (sp - p).sqrMagnitude < 36f)) continue;
                    if (OverHole(p, 3f)) continue;
                    if (Physics.CheckBox(p + Vector3.up * 1.1f, new Vector3(2f, 0.85f, 2f), Quaternion.identity)) continue;
                    float streetYaw = vertical ? 0f : 90f;
                    switch (kind)
                    {
                        case 0: Obstacles.SandbagBunker(cover, p, streetYaw + 90f + (side > 0 ? 180f : 0f)); break;
                        case 1: Obstacles.CarWreck(cover, p, streetYaw + R(-12f, 12f)); break;
                        case 2: Obstacles.Pipes(cover, p, streetYaw); break;
                        case 3: Obstacles.Hedgehog(cover, p, R(0, 90)); break;
                        case 4: Obstacles.CrateStack(cover, p, streetYaw + 90f, rng); break;
                        default: Obstacles.Blocks(cover, p, streetYaw + 90f, 3); break;
                    }
                    Physics.SyncTransforms();
                }
        // sandbag positions on the corners of some crossings
        foreach (float a in lines)
            foreach (float b in lines)
            {
                if (rng.NextDouble() > 0.4) continue;
                float sx = rng.NextDouble() < 0.5 ? -1f : 1f, sz = rng.NextDouble() < 0.5 ? -1f : 1f;
                var p = new Vector3(a + sx * 4.3f, 0, b + sz * 4.3f);
                if (OverHole(p, 2.5f)) continue;
                Obstacles.SandbagBunker(cover, p, Mathf.Atan2(-sx, -sz) * Mathf.Rad2Deg);
            }

        // ammunition crates on the sidewalks around the city and one by the fountain
        foreach (var (x, z, yaw) in new[] { (-33.5f, -12f, 90f), (33.5f, 12f, 90f), (-12f, 33.5f, 0f), (12f, -33.5f, 0f),
                                            (-70.5f, -60f, 90f), (70.5f, 60f, 90f), (-60f, 70.5f, 0f), (60f, -70.5f, 0f), (-5.2f, 0.8f, 90f) })
            Obstacles.AmmoCrate(props, new Vector3(x, x == 0f ? 0.19f : 0.16f, z), yaw);

        // street lights on the sidewalk corners (lamps glow at night)
        var pole = AssetSetup.Solid("Pole", new Color(0.15f, 0.15f, 0.16f), 0.4f, 0.6f);
        var lampMat = AssetSetup.Solid("LampHousing", new Color(0.2f, 0.2f, 0.21f), 0.5f, 0.5f);
        for (int bx = -1; bx <= 1; bx++)
            for (int bz = -1; bz <= 1; bz++)
                foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    var p = new Vector3(bx * Pitch + sx * (BlockSize / 2f - 0.8f), 0, bz * Pitch + sz * (BlockSize / 2f - 0.8f));
                    Cylinder("LightPole", p + Vector3.up * 3.2f, 0.09f, 6.4f, pole, props);
                    var head = p + new Vector3(-sx * 0.6f, 6.3f, -sz * 0.6f);
                    Box("Lamp", head, new Vector3(0.5f, 0.15f, 0.5f), lampMat, props, 0, false);
                    NightLights.Lamp(props, head + Vector3.down * 0.09f, new Vector3(0.42f, 0.04f, 0.42f), lamps, 3.2f);
                    // kit props near the corners: bollards and planters
                    if (rng.NextDouble() < 0.5) Place("Prop_Planter_Single", p + new Vector3(-sx * 2.5f, 0, 0), 0, props, 0.16f);
                    if (rng.NextDouble() < 0.5) Place("Prop_Bollard", p + new Vector3(0, 0, -sz * 2.5f), 0, props, 0.16f);
                }
    }

    private static List<GameObject> carModels;

    /// <summary>A car from the CC0 car pack, 4.4 m long, standing on the road. Null if the pack is missing.</summary>
    private static GameObject CarModel(Transform parent, Vector3 pos, float yaw)
    {
        if (carModels == null)
        {
            carModels = new List<GameObject>();
            if (AssetDatabase.IsValidFolder("Assets/ThirdParty/Cars"))
                carModels = AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Cars" })
                    .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(m => m != null && m.GetComponentsInChildren<Renderer>().Length > 0).ToList();
            Debug.Log($"[Map] car models: {carModels.Count}");
        }
        if (carModels.Count == 0) return null;
        var holder = new GameObject("Car").transform;
        holder.SetParent(parent, false);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(carModels[rng.Next(carModels.Count)], holder);
        var b = WorldBounds(go);
        if (b.size.x > b.size.z) go.transform.localRotation = Quaternion.Euler(0, 90, 0) * go.transform.localRotation;
        b = WorldBounds(go);
        go.transform.localScale *= 4.4f / Mathf.Max(0.1f, b.size.z);
        b = WorldBounds(go);
        go.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
        b = WorldBounds(go);
        var box = holder.gameObject.AddComponent<BoxCollider>();
        box.center = b.center;
        box.size = b.size * 0.95f;
        holder.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw + (rng.NextDouble() < 0.5 ? 180f : 0f), 0));
        return holder.gameObject;
    }

    private static void Car(Transform parent, Vector3 pos, float yaw, Color paint)
    {
        if (CarModel(parent, pos, yaw) != null) return;
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
                Place(BuildingKits[rng.Next(BuildingKits.Length)], pos, yaw, back);
        }
        // the invisible edge runs behind the fronts of the outer buildings: players can walk up to them
        // (and a few meters into the gaps between them) but not out of the city
        float edge = ring + 2f;
        foreach (var (c, s) in new[] { (new Vector3(0, 10, edge), new Vector3(edge * 2 + 4, 20, 1)), (new Vector3(0, 10, -edge), new Vector3(edge * 2 + 4, 20, 1)),
                                       (new Vector3(edge, 10, 0), new Vector3(1, 20, edge * 2 + 4)), (new Vector3(-edge, 10, 0), new Vector3(1, 20, edge * 2 + 4)) })
        {
            var wall = new GameObject("InvisibleWall");
            wall.layer = World.FenceLayer;   // stops players, not the camera (no camera pulled into the head at the edge)
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
        // across the park: up the outer stairs, over the terrace, down into the court and around it
        var court = new List<Vector3>();
        foreach (var d in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
        {
            var street = d * 26f;
            var foot = d * 18.6f;
            var terrace = d * 12f + Vector3.up * 1.76f;
            var down = d * 5.4f + Vector3.up * 0.2f;
            Link(street, foot);
            Link(foot, terrace);
            Link(terrace, down);
            court.Add(down);
        }
        for (int i = 0; i < court.Count; i++) Link(court[i], court[(i + 1) % court.Count]);
        // the metro: down the end stairs, through the tunnels and the hall, up the hall stair
        float f = MetroFloor;
        Link(new Vector3(-78, 0, -26), new Vector3(-72.6f, 0, -22.1f));
        Link(new Vector3(-72.6f, 0, -22.1f), new Vector3(-60.5f, f, -22.1f));
        Link(new Vector3(-60.5f, f, -22.1f), new Vector3(-40f, f, -23.7f));
        Link(new Vector3(-40f, f, -23.7f), new Vector3(-16f, f, -24f));
        Link(new Vector3(-16f, f, -24f), new Vector3(0f, f, -25.5f));
        Link(new Vector3(0f, f, -25.5f), new Vector3(16f, f, -24f));
        Link(new Vector3(16f, f, -24f), new Vector3(40f, f, -23.7f));
        Link(new Vector3(40f, f, -23.7f), new Vector3(60.5f, f, -22.1f));
        Link(new Vector3(60.5f, f, -22.1f), new Vector3(72.6f, 0, -22.1f));
        Link(new Vector3(72.6f, 0, -22.1f), new Vector3(78, 0, -26));
        Link(new Vector3(-16f, f, -24f), new Vector3(-6.6f, f, -21.75f));
        Link(new Vector3(-6.6f, f, -21.75f), new Vector3(5.8f, 0, -21.75f));
        Link(new Vector3(5.8f, 0, -21.75f), new Vector3(26, 0, -26));
    }

    /// <summary>Spawn points all over the map: every crossing, the middle of every street, park corners.</summary>
    private static List<Vector3> Spawns()
    {
        var list = new List<Vector3>();
        float[] lines = { -78f, -26f, 26f, 78f };
        foreach (float a in lines)
        {
            foreach (float b in lines) list.Add(new Vector3(a, 0, b));
            foreach (float t in new[] { -60f, -44f, -8f, 8f, 44f, 60f })
            {
                list.Add(new Vector3(a, 0, t));
                list.Add(new Vector3(t, 0, a));
            }
        }
        // on the park terrace corners and in the sunken court
        foreach (var (x, z) in new[] { (-14f, -14f), (14f, -14f), (-14f, 14f), (14f, 14f) }) list.Add(new Vector3(x, 1.8f, z));
        foreach (var (x, z) in new[] { (-6.5f, -3.5f), (6.5f, 3.5f) }) list.Add(new Vector3(x, 0.25f, z));
        // in the metro
        foreach (var x in new[] { -42f, -12f, 12f, 42f }) list.Add(new Vector3(x, MetroFloor + 0.1f, -24.5f));
        return list;
    }
}
