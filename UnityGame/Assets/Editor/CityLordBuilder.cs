using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UA;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// CityLord: the industrial town of hangars, oil tanks, pipes, containers and fenced yards (scene map3,
/// file level2, root Map_v1), rebuilt exactly from its extracted data in UnityGame/Models/CityLord:
/// <list type="bullet">
/// <item>scene.json: every object with its world matrix, renderer (static batch sub-mesh range, materials,
/// shadow flags), mesh and box colliders, the materials with all their properties, the sun and the sky;</item>
/// <item>meshes/*.mesh: the mesh buffers as stored (CLM1, see LoadMesh), the three static batch meshes
/// already in world space;</item>
/// <item>textures/*.png: the textures, decoded losslessly.</item>
/// </list>
/// The shaders are Unity's own (Mobile/Diffuse, Legacy cutout, Standard, Skybox/6 Sided) with the stored
/// values. The only change: the whole map is lifted by <see cref="Lift"/> so its streets lie at the height
/// 0 of the other maps (the game takes players who fall below -15 back to a spawn point).
/// </summary>
public static partial class CityLordBuilder
{
    /// <summary>Spawn point spwan1 stands at y -12.922: it comes to 0.</summary>
    public const float Lift = 12.922f;
    private const float Cell = 40f;
    private const string MeshFolder = "Assets/Generated/Meshes/CityLord";
    private const string TexFolder = "Assets/Generated/Textures/CityLord";
    private const string MatFolder = "Assets/Generated/Materials/CityLord";

    public static string DataFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Models", "CityLord"));

    private static Transform root;
    private static Dictionary<string, object> scene;
    private static readonly Dictionary<string, RawMesh> raw = new Dictionary<string, RawMesh>();
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

    public static MapLayout Build()
    {
        Load(false);
        root = new GameObject("Map_CityLord").transform;
        root.position = new Vector3(0, Lift, 0);
        var geometry = Node("Geometry");
        var colliders = Node("Colliders");
        var parts = new Dictionary<string, List<GameObject>>();   // renderers made for each object path
        var outlines = new Dictionary<string, Bounds>();           // collider bounds of each object path
        var shells = new Dictionary<string, (List<Vector3> v, List<int> t)>();   // collider triangles of the buildings opened

        var collision = new Dictionary<Vector2Int, (List<Vector3> v, List<int> t)>();
        var play = new Bounds();
        bool playSet = false;
        void Play(Bounds b)
        {
            if (Mathf.Max(b.size.x, b.size.z) > 150f) return;   // the ground reaching far beyond the fences
            if (playSet) play.Encapsulate(b);
            else { play = b; playSet = true; }
        }
        void Outline(string path, IEnumerable<Vector3> v)
        {
            foreach (var p in v)
            {
                if (outlines.TryGetValue(path, out var b)) { b.Encapsulate(p); outlines[path] = b; }
                else outlines[path] = new Bounds(p, Vector3.zero);
            }
        }
        void Collide(Vector3[] v, int[] t, Vector3 at)
        {
            var key = new Vector2Int(Mathf.FloorToInt(at.x / Cell), Mathf.FloorToInt(at.z / Cell));
            if (!collision.TryGetValue(key, out var c)) collision[key] = c = (new List<Vector3>(), new List<int>());
            int first = c.v.Count;
            c.v.AddRange(v);
            c.t.AddRange(t.Select(i => i + first));
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            Play(b);
        }

        int renderers = 0, meshColliders = 0, boxes = 0, ammo = 0;
        foreach (var o in objects)
        {
            string path = S(o["path"]);
            if (!path.StartsWith("Map_v1") || !ActiveInHierarchy(path)) continue;
            var world = M(o["world_matrix"]);
            var comps = L(o["components"]).Select(D).ToList();
            foreach (var c in comps)
            {
                string type = S(c["type"]);
                bool enabled = !c.ContainsKey("enabled") || B(c["enabled"]);
                if (!enabled) continue;
                if (type == "MeshRenderer")
                {
                    var mats = L(c["materials"]).Select(m => m == null ? null : Mat(S(m))).ToArray();
                    int count = I(c["static_batch_submesh_count"]);
                    var src = LoadMesh(S(c["mesh"]));
                    if (src == null || mats.Length == 0) continue;
                    // a statically batched renderer draws its own sub-meshes of the combined mesh (already in
                    // world space); the others draw their mesh through their world matrix
                    int first = count > 0 ? I(c["static_batch_first_submesh"]) : 0;
                    int subs = count > 0 ? count : mats.Length;
                    var matrix = count > 0 ? Matrix4x4.identity : world;
                    var go = new GameObject(S(o["name"]));
                    go.transform.SetParent(geometry, false);
                    go.AddComponent<MeshFilter>().sharedMesh = Piece(S(o["name"]), src, Enumerable.Range(first, subs).ToList(), matrix);
                    var r = go.AddComponent<MeshRenderer>();
                    // more sub-meshes than materials: the last material again; more materials: the last sub-mesh again (Unity)
                    r.sharedMaterials = Enumerable.Range(0, subs).Select(i => mats[Mathf.Min(i, mats.Length - 1)]).ToArray();
                    r.shadowCastingMode = (ShadowCastingMode)I(c["cast_shadows"]);
                    r.receiveShadows = I(c["receive_shadows"]) != 0;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    if (!parts.TryGetValue(path, out var list)) parts[path] = list = new List<GameObject>();
                    list.Add(go);
                    renderers++;
                }
                else if (type == "MeshCollider")
                {
                    var src = LoadMesh(S(c["mesh"]));
                    if (src == null) continue;
                    var v = src.P.Select(p => world.MultiplyPoint3x4(p)).ToArray();
                    var t = src.Sub.SelectMany(s => s).ToArray();
                    Collide(v, t, world.GetColumn(3));
                    Outline(path, v);
                    if (Opened.Any(d => d.path == path)) shells[path] = (v.ToList(), t.ToList());
                    meshColliders++;
                }
                else if (type == "BoxCollider")
                {
                    if (B(c["is_trigger"])) continue;   // the ammo pick-up zones: the game finds the boxes by distance
                    var center = V3(c["center"]);
                    var size = V3(c["size"]);
                    if (Orthogonal(world))
                    {
                        var go = new GameObject(S(o["name"]));
                        go.transform.SetParent(colliders, false);
                        go.transform.localPosition = world.GetColumn(3);
                        go.transform.localRotation = world.rotation;
                        go.transform.localScale = world.lossyScale;
                        var box = go.AddComponent<BoxCollider>();
                        box.center = center;
                        box.size = size;
                        var bounds = new Bounds(world.MultiplyPoint3x4(center), Vector3.Scale(size, Abs(world.lossyScale)));
                        Play(bounds);
                        Outline(path, new[] { bounds.min, bounds.max });
                    }
                    else
                    {
                        var v = new Vector3[8];
                        for (int i = 0; i < 8; i++)
                            v[i] = world.MultiplyPoint3x4(center + Vector3.Scale(size / 2f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        Collide(v, BoxTriangles, world.GetColumn(3));
                        Outline(path, v);
                    }
                    boxes++;
                }
                else if (type == "MonoBehaviour" && c.TryGetValue("script", out var script) && S(script) == "Ammo_box")
                {
                    var go = new GameObject("AmmoBox");
                    go.transform.SetParent(root, false);
                    go.transform.localPosition = world.GetColumn(3);
                    go.AddComponent<AmmoBox>();
                    ammo++;
                }
            }
        }

        // the mesh colliders, one mesh for each 40 m square of the map
        var cells = new List<MeshCollider>();
        foreach (var c in collision)
        {
            var mesh = new Mesh { name = $"CityLord_Collision_{c.Key.x}_{c.Key.y}" };
            if (c.Value.v.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(c.Value.v);
            mesh.SetTriangles(c.Value.t, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("Collision");
            go.transform.SetParent(colliders, false);
            cells.Add(go.AddComponent<MeshCollider>());
            cells[cells.Count - 1].sharedMesh = mesh;
        }

        // doors opened into some of the buildings, with their insides
        Physics.SyncTransforms();
        int doors = OpenBuildings(geometry, colliders, parts, outlines, shells, cells);

        // invisible fences just outside the fenced yards
        var lift = Vector3.up * Lift;
        play.center += lift;
        foreach (var (c, sz) in new[]
                 {
                     (new Vector3(play.center.x, play.center.y, play.max.z + 1.5f), new Vector3(play.size.x + 6f, 80, 1)),
                     (new Vector3(play.center.x, play.center.y, play.min.z - 1.5f), new Vector3(play.size.x + 6f, 80, 1)),
                     (new Vector3(play.max.x + 1.5f, play.center.y, play.center.z), new Vector3(1, 80, play.size.z + 6f)),
                     (new Vector3(play.min.x - 1.5f, play.center.y, play.center.z), new Vector3(1, 80, play.size.z + 6f)),
                 })
        {
            var wall = new GameObject("Edge");
            wall.layer = World.FenceLayer;
            wall.transform.SetParent(root, false);
            wall.transform.position = c;
            wall.AddComponent<BoxCollider>().size = sz;
        }
        // the four parts of the town: a name board in each, a number on every building
        Physics.SyncTransforms();
        int numbered = NameTown(outlines);
        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            if (tr.gameObject.layer != World.FenceLayer)
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);

        var spawns = objects.Where(o => S(o["path"]).StartsWith("Element/Network_Manager/spwan")).Select(o => (Vector3)M(o["world_matrix"]).GetColumn(3) + lift).ToList();
        var layout = new MapLayout
        {
            Name = "CITYLORD",
            Root = root.gameObject,
            Center = new Vector3(play.center.x, spawns.Count > 0 ? spawns.Average(s => s.y) : Lift, play.center.z),
            Radius = Mathf.Max(play.extents.x, play.extents.z),
            Sky = Mat(S(D(D(scene["settings"])["render"])["skybox"])),
        };
        BuildNavigation(layout, play, spawns);
        root.gameObject.AddComponent<MapScene>().NightLights = MapOptimizer.Merge(root, "CityLord").ToArray();
        SaveMeshes();
        Debug.Log($"[Map] CityLord built: {renderers} renderers ({root.GetComponentsInChildren<Renderer>().Length} after merging), {meshColliders} mesh and {boxes} box colliders, " +
                  $"{ammo} ammo boxes, {doors} doors opened, {numbered} buildings numbered, {materials.Count} materials, {textures.Count} textures, {layout.Waypoints.Length} waypoints, {layout.Spawns.Length} spawns, play area {play.size}");
        return layout;
    }

    private static Transform Node(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    private static readonly int[] BoxTriangles =
    {
        0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5,
    };

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>Rotation and scale only (no shear from a scaled parent): a transform can hold it.</summary>
    private static bool Orthogonal(Matrix4x4 m)
    {
        Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
        if (m.determinant <= 0f) return false;
        float e = 1e-3f * Mathf.Max(x.sqrMagnitude, y.sqrMagnitude, z.sqrMagnitude);
        return Mathf.Abs(Vector3.Dot(x, y)) < e && Mathf.Abs(Vector3.Dot(y, z)) < e && Mathf.Abs(Vector3.Dot(x, z)) < e;
    }

    /// <summary>Every mesh made here (pieces not merged, colliders) into one asset, so the map scene can hold them.</summary>
    private static void SaveMeshes()
    {
        string path = $"{MeshFolder}/CityLord.asset";
        bool first = true;
        var done = new HashSet<Mesh>();
        void Save(Mesh mesh)
        {
            if (mesh == null || AssetDatabase.Contains(mesh) || !done.Add(mesh)) return;
            if (first) { AssetDatabase.CreateAsset(mesh, path); first = false; }
            else AssetDatabase.AddObjectToAsset(mesh, path);
        }
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true)) Save(f.sharedMesh);
        foreach (var c in root.GetComponentsInChildren<MeshCollider>(true)) Save(c.sharedMesh);
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------ meshes

    private class RawMesh
    {
        public Vector3[] P, N;
        public Vector2[] UV;
        public Vector4[] T;
        public int[][] Sub;
    }

    /// <summary>
    /// CLM1 (little endian): "CLM1", uint32 vertex count, sub-mesh count, flags; float positions[3n];
    /// flags 1: normals[3n]; 2: uv0[2n]; 4: uv1[2n]; 8: colours[4n]; 16: tangents[4n]; then for each
    /// sub-mesh uint32 index count and the indices.
    /// </summary>
    private static RawMesh LoadMesh(string key)
    {
        if (raw.TryGetValue(key, out var cached)) return cached;
        if (!D(scene["meshes"]).TryGetValue(key, out var info) || info == null) { Debug.LogWarning("[Map] CityLord: no mesh " + key); return raw[key] = null; }
        string file = Path.Combine(DataFolder, S(D(info)["file"]));
        if (!File.Exists(file)) { Debug.LogWarning("[Map] CityLord: mesh file missing " + file); return raw[key] = null; }
        using (var r = new BinaryReader(File.OpenRead(file)))
        {
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "CLM1") throw new InvalidDataException("[Map] not a CLM1 mesh: " + file);
            int n = (int)r.ReadUInt32(), subs = (int)r.ReadUInt32(), flags = (int)r.ReadUInt32();
            Vector3[] V3s() { var a = new Vector3[n]; for (int i = 0; i < n; i++) a[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); return a; }
            Vector2[] V2s() { var a = new Vector2[n]; for (int i = 0; i < n; i++) a[i] = new Vector2(r.ReadSingle(), r.ReadSingle()); return a; }
            Vector4[] V4s() { var a = new Vector4[n]; for (int i = 0; i < n; i++) a[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); return a; }
            var m = new RawMesh { P = V3s() };
            if ((flags & 1) != 0) m.N = V3s();
            if ((flags & 2) != 0) m.UV = V2s();
            if ((flags & 4) != 0) V2s();
            if ((flags & 8) != 0) V4s();
            if ((flags & 16) != 0) m.T = V4s();
            m.Sub = new int[subs][];
            for (int s = 0; s < subs; s++)
            {
                int count = (int)r.ReadUInt32();
                m.Sub[s] = new int[count];
                for (int i = 0; i < count; i++) m.Sub[s][i] = (int)r.ReadUInt32();
            }
            return raw[key] = m;
        }
    }

    /// <summary>The given sub-meshes of a mesh as a mesh of its own (only the vertices they use), moved by matrix.</summary>
    private static Mesh Piece(string name, RawMesh src, List<int> subs, Matrix4x4 matrix)
    {
        var map = new Dictionary<int, int>();
        var used = new List<int>();
        var tris = new List<int[]>();
        foreach (int s in subs)
        {
            var idx = src.Sub[Mathf.Clamp(s, 0, src.Sub.Length - 1)];
            var t = new int[idx.Length];
            for (int i = 0; i < idx.Length; i++)
            {
                if (!map.TryGetValue(idx[i], out int k)) { k = used.Count; map[idx[i]] = k; used.Add(idx[i]); }
                t[i] = k;
            }
            tris.Add(t);
        }
        var normalMatrix = matrix.inverse.transpose;
        var mesh = new Mesh { name = name };
        if (used.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(used.Select(i => matrix.MultiplyPoint3x4(src.P[i])).ToList());
        if (src.N != null) mesh.SetNormals(used.Select(i => normalMatrix.MultiplyVector(src.N[i]).normalized).ToList());
        if (src.UV != null) mesh.SetUVs(0, used.Select(i => src.UV[i]).ToList());
        if (src.T != null)
            mesh.SetTangents(used.Select(i =>
            {
                var t = src.T[i];
                var d = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                return new Vector4(d.x, d.y, d.z, t.w);
            }).ToList());
        mesh.subMeshCount = tris.Count;
        for (int s = 0; s < tris.Count; s++) mesh.SetTriangles(tris[s], s, false);
        mesh.RecalculateBounds();
        if (src.N == null) mesh.RecalculateNormals();
        return mesh;
    }

    // ------------------------------------------------------------------ materials and textures

    private static Material Mat(string key)
    {
        if (materials.TryGetValue(key, out var cached)) return cached;
        if (!D(scene["materials"]).TryGetValue(key, out var found) || found == null) { Debug.LogWarning("[Map] CityLord: no material " + key); return materials[key] = null; }
        var info = D(found);
        string shaderName = S(info["shader"]);
        var shader = Shader.Find(shaderName);
        if (shader == null) { Debug.LogWarning($"[Map] CityLord: no shader {shaderName}, Standard instead"); shader = Shader.Find("Standard"); }
        var m = new Material(shader) { name = S(info["name"]) };
        foreach (var kv in D(info["floats"]))
            if (m.HasProperty(kv.Key)) m.SetFloat(kv.Key, F(kv.Value));
        foreach (var kv in D(info["colors"]))
            if (m.HasProperty(kv.Key)) m.SetColor(kv.Key, C(kv.Value));
        float cutoff = D(info["floats"]).TryGetValue("_Cutoff", out var cut) ? F(cut) : 0.5f;
        foreach (var kv in D(info["textures"]))
        {
            var slot = D(kv.Value);
            if (!slot.TryGetValue("texture", out var tex) || tex == null || !m.HasProperty(kv.Key)) continue;
            bool cutout = shaderName.Contains("Cutout");
            m.SetTexture(kv.Key, LoadTexture(D(tex), kv.Key, cutout ? cutoff : -1f));
            m.SetTextureScale(kv.Key, V2(slot["scale"]));
            m.SetTextureOffset(kv.Key, V2(slot["offset"]));
        }
        foreach (var k in S(info["keywords"]).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) m.EnableKeyword(k);
        int queue = I(info["render_queue"]);
        if (queue >= 0) m.renderQueue = queue;   // -1: the shader's own
        if (shaderName == "Standard" && m.IsKeywordEnabled("_EMISSION")) m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.enableInstancing = !shaderName.StartsWith("Skybox");
        string file = string.Concat(m.name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_'));
        string path = $"{MatFolder}/{file}.mat";
        for (int i = 1; AssetDatabase.LoadMainAssetAtPath(path) != null; i++) path = $"{MatFolder}/{file}_{i}.mat";
        AssetDatabase.CreateAsset(m, path);
        return materials[key] = m;
    }

    /// <summary>A decoded texture into the project, imported with the settings it had in the game.</summary>
    private static Texture2D LoadTexture(Dictionary<string, object> info, string slot, float alphaTest)
    {
        string key = S(info["key"]);
        if (textures.TryGetValue(key, out var cached)) return cached;
        string path = $"{TexFolder}/{S(info["name"])}.png";
        File.Copy(Path.Combine(DataFolder, S(info["file"])), path, true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = slot == "_BumpMap" || slot == "_DetailNormalMap" ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = I(info["color_space"]) == 1 && slot != "_MetallicGlossMap" && slot != "_OcclusionMap";
        importer.wrapMode = I(info["wrap_u"]) == 1 ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.filterMode = (FilterMode)Mathf.Clamp(I(info["filter"]), 0, 2);
        importer.anisoLevel = I(info["aniso"]);
        importer.mipmapEnabled = I(info["mip_count"]) > 1;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = Mathf.Max(I(info["width"]), I(info["height"]));
        importer.textureCompression = TextureImporterCompression.Compressed;
        if (alphaTest >= 0f)
        {
            importer.alphaIsTransparency = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = alphaTest;
        }
        var android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = importer.maxTextureSize;
        android.format = slot == "_BumpMap" || slot == "_MetallicGlossMap" ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
        android.compressionQuality = 50;
        importer.SetPlatformTextureSettings(android);
        importer.SaveAndReimport();
        return textures[key] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ bots and spawn points

    /// <summary>
    /// Bot waypoints on a 3 m grid over every floor of the map (streets, yards, inside the hangars, on
    /// walkways), linked where a player can walk between them; only the ones that can be walked to from
    /// the map's own spawn points are kept (no roofs nobody can reach). The spawn points are the map's
    /// seven and the waypoints.
    /// </summary>
    private static void BuildNavigation(MapLayout layout, Bounds play, List<Vector3> spawns)
    {
        Physics.SyncTransforms();
        const float step = 3f;
        int mask = World.EnvMask | (1 << World.FenceLayer);
        var points = new List<Vector3>();
        var columns = new Dictionary<Vector2Int, List<int>>();
        int nx = Mathf.FloorToInt(play.size.x / step), nz = Mathf.FloorToInt(play.size.z / step);
        for (int gx = 0; gx <= nx; gx++)
            for (int gz = 0; gz <= nz; gz++)
            {
                var top = new Vector3(play.min.x + gx * step, play.max.y + 10f, play.min.z + gz * step);
                var hits = Physics.RaycastAll(top, Vector3.down, play.size.y + 30f, World.EnvMask, QueryTriggerInteraction.Ignore);
                foreach (var h in hits.OrderByDescending(x => x.point.y))
                {
                    if (h.normal.y < 0.7f) continue;
                    var p = h.point;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.45f, mask, QueryTriggerInteraction.Ignore)) continue;
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
                        var a = points[ia];
                        var b = points[ib];
                        float dist = Vector3.Distance(a, b);
                        if (Mathf.Abs(a.y - b.y) > dist * 0.55f) continue;
                        // ground all the way (no gap between two roofs) and the way free
                        var mid = (a + b) / 2f;
                        if (!Physics.Raycast(new Vector3(mid.x, Mathf.Max(a.y, b.y) + 0.8f, mid.z), Vector3.down, out var ground, Mathf.Abs(a.y - b.y) + 1.6f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                        if (ground.point.y < Mathf.Min(a.y, b.y) - 0.6f) continue;
                        if (Physics.SphereCast(a + Vector3.up * 1.2f, 0.4f, (b - a).normalized, out _, dist, mask, QueryTriggerInteraction.Ignore)) continue;
                        links.Add(new Vector2Int(ia, ib));
                    }
            }

        // keep what can be walked to from the spawn points
        var neighbours = new List<int>[points.Count];
        for (int i = 0; i < points.Count; i++) neighbours[i] = new List<int>();
        foreach (var l in links) { neighbours[l.x].Add(l.y); neighbours[l.y].Add(l.x); }
        var reached = new bool[points.Count];
        var queue = new Queue<int>();
        foreach (var s in spawns)
        {
            int best = -1;
            float bestDistance = 25f;
            for (int i = 0; i < points.Count; i++)
            {
                float dd = (points[i] - s).sqrMagnitude;
                if (dd < bestDistance) { bestDistance = dd; best = i; }
            }
            if (best >= 0 && !reached[best]) { reached[best] = true; queue.Enqueue(best); }
        }
        while (queue.Count > 0)
            foreach (int n in neighbours[queue.Dequeue()])
                if (!reached[n]) { reached[n] = true; queue.Enqueue(n); }
        if (reached.Count(x => x) < 30)
        {
            Debug.LogWarning("[Map] CityLord: few waypoints reached from the spawn points, keeping all");
            for (int i = 0; i < reached.Length; i++) reached[i] = true;
        }
        var index = new int[points.Count];
        var kept = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            index[i] = reached[i] ? kept.Count : -1;
            if (reached[i]) kept.Add(points[i]);
        }
        layout.Waypoints = kept.ToArray();
        layout.Links = links.Where(l => reached[l.x] && reached[l.y]).Select(l => new Vector2Int(index[l.x], index[l.y])).ToArray();
        // the map's own spawn points, on the ground below them, then every waypoint
        var spawnList = new List<Vector3>();
        foreach (var s in spawns)
            spawnList.Add(Physics.Raycast(s + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, World.EnvMask, QueryTriggerInteraction.Ignore) ? hit.point : s);
        spawnList.AddRange(kept);
        layout.Spawns = spawnList.ToArray();
    }

    // ------------------------------------------------------------------ scene.json values

    private static Dictionary<string, object> D(object o) => (Dictionary<string, object>)o;
    private static List<object> L(object o) => (List<object>)o;
    private static string S(object o) => o as string ?? "";
    private static bool B(object o) => o is bool b && b;
    private static float F(object o) => o is double d ? (float)d : 0f;
    private static int I(object o) => o is double d ? (int)d : 0;
    private static Vector2 V2(object o) { var l = L(o); return new Vector2(F(l[0]), F(l[1])); }
    private static Vector3 V3(object o) { var l = L(o); return new Vector3(F(l[0]), F(l[1]), F(l[2])); }
    private static Color C(object o) { var l = L(o); return new Color(F(l[0]), F(l[1]), F(l[2]), l.Count > 3 ? F(l[3]) : 1f); }

    /// <summary>A world matrix as stored: four rows of four.</summary>
    private static Matrix4x4 M(object o)
    {
        var rows = L(o);
        var m = new Matrix4x4();
        for (int r = 0; r < 4; r++)
        {
            var row = L(rows[r]);
            for (int c = 0; c < 4; c++) m[r, c] = F(row[c]);
        }
        return m;
    }

    /// <summary>A small JSON reader: objects, arrays, strings, numbers (as double), true, false, null.</summary>
    private static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            return Value(s, ref i);
        }

        private static void Space(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object Value(string s, ref int i)
        {
            Space(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                i++;
                var d = new Dictionary<string, object>();
                Space(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Space(s, ref i);
                    string key = Text(s, ref i);
                    Space(s, ref i);
                    i++;   // ':'
                    d[key] = Value(s, ref i);
                    Space(s, ref i);
                    if (s[i++] == '}') return d;
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                Space(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Space(s, ref i);
                    if (s[i++] == ']') return l;
                }
            }
            if (c == '"') return Text(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Text(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;   // '"'
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }
}
