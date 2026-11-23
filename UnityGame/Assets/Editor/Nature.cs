using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Trees, bushes, grass and rocks from the Quaternius Stylized Nature kit (CC0): textured bark with
/// normal maps, two-sided alpha-tested leaves, and trunk colliders so trees stop players and bullets.
/// </summary>
public static class Nature
{
    public const string Folder = "Assets/ThirdParty/Nature";
    private const string PrefabFolder = "Assets/Generated/Nature";

    private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, float> heights = new Dictionary<string, float>();
    private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    private static bool ready;

    private static Texture2D T(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{file}.png");

    private static Material Foliage(string name, string texture, Color tint)
    {
        if (mats.TryGetValue(name, out var cached) && cached != null) return cached;
        string path = $"{AssetSetup.Generated}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("UA/Foliage") ?? Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse");
        bool create = m == null;
        if (create) m = new Material(shader);
        m.shader = shader;
        m.mainTexture = T(texture);
        m.color = tint;
        m.SetFloat("_Cutoff", 0.45f);
        m.enableInstancing = true;
        if (create) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        return mats[name] = m;
    }

    /// <summary>The material for one of the kit's material names.</summary>
    private static Material For(string source)
    {
        if (source.StartsWith("Bark"))
        {
            var albedo = T(source) != null ? source : "Bark_NormalTree";
            return mats.TryGetValue(source, out var m) ? m : mats[source] =
                AssetSetup.Mat("Nature_" + source, new Color(0.92f, 0.88f, 0.84f), 0.12f, 0f, T(albedo), T(albedo + "_Normal") ?? T("Bark_NormalTree_Normal"));
        }
        if (source == "Leaves_Pine" || source.StartsWith("Leaf_Pine")) return Foliage("Nature_LeavesPine", "Leaf_Pine_C", new Color(0.82f, 0.9f, 0.8f));
        if (source.StartsWith("Leaves"))
        {
            string tex = T(source + "_C") != null ? source + "_C" : "Leaves_NormalTree_C";
            return Foliage("Nature_" + source, tex, new Color(0.86f, 0.92f, 0.8f));
        }
        if (source.StartsWith("Grass")) return Foliage("Nature_Grass", "Grass", new Color(0.8f, 0.9f, 0.72f));
        if (source.StartsWith("Flower")) return Foliage("Nature_Flowers", "Flowers", Color.white);
        if (source.StartsWith("Rock"))
            return mats.TryGetValue("Rocks", out var r) ? r : mats["Rocks"] = AssetSetup.Mat("Nature_Rocks", new Color(0.8f, 0.8f, 0.78f), 0.2f, 0f, T("Rocks_Diffuse"));
        // unnamed material on the bushes: the twigs inside the leaves
        return mats.TryGetValue("Twigs", out var t) ? t : mats["Twigs"] = AssetSetup.Mat("Nature_Twigs", new Color(0.3f, 0.24f, 0.17f), 0.1f);
    }

    /// <summary>Remaps the kit materials and makes one prefab per model (with colliders for trees and rocks).</summary>
    public static void Setup()
    {
        ready = true;
        prefabs.Clear();
        heights.Clear();
        mats.Clear();
        if (!AssetDatabase.IsValidFolder(Folder)) { Debug.LogWarning("[Setup] nature kit missing"); return; }
        AssetSetup.Folder(PrefabFolder);
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var map = importer.GetExternalObjectMap();
            bool dirty = false;
            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name).ToList();
            names.AddRange(map.Keys.Where(k => k.type == typeof(Material)).Select(k => k.name));
            foreach (var name in names.Distinct())
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                var target = For(name);
                if (map.TryGetValue(id, out var current) && current == target) continue;
                importer.AddRemap(id, target);
                dirty = true;
            }
            if (dirty) importer.SaveAndReimport();

            string key = System.IO.Path.GetFileNameWithoutExtension(path);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.transform.position = Vector3.zero;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Object.DestroyImmediate(go); continue; }
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            bool tree = key.StartsWith("CommonTree") || key.StartsWith("Pine");
            bool small = key.StartsWith("Grass") || key.StartsWith("Fern") || key.StartsWith("Plant");
            foreach (var r in renderers)
            {
                r.shadowCastingMode = small ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = !small;
            }
            if (tree)
            {
                // a capsule around the lower trunk: blocks players and bullets, leaves stay see-through
                var trunk = new GameObject("Trunk");
                trunk.transform.SetParent(go.transform, false);
                var c = trunk.AddComponent<CapsuleCollider>();
                c.radius = Mathf.Clamp(b.size.y * 0.035f, 0.18f, 0.5f);
                c.height = b.size.y * 0.55f;
                c.center = new Vector3(b.center.x, b.min.y + c.height / 2f, b.center.z);
            }
            else if (key.StartsWith("Rock"))
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size * 0.85f;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{key}.prefab");
            Object.DestroyImmediate(go);
            prefabs[key] = prefab;
            heights[key] = Mathf.Max(0.01f, b.size.y);
        }
        Debug.Log($"[Setup] nature prefabs: {prefabs.Count} ({string.Join(", ", heights.Select(p => $"{p.Key} {p.Value:0.0}m"))})");
    }

    /// <summary>Places a kit model scaled to the given height. Returns null when the kit is missing.</summary>
    public static GameObject Place(string key, Vector3 position, float yaw, float height, Transform parent)
    {
        if (!ready) Setup();
        if (!prefabs.TryGetValue(key, out var prefab) || prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one * (height / heights[key]);
        return go;
    }

    public static readonly string[] Trees = { "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5" };
    public static readonly string[] Pines = { "Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5" };
    public static readonly string[] Bushes = { "Bush_Common", "Bush_Common_Flowers", "Bush_Large", "Bush_Long_1", "Bush_Long_2" };
    public static readonly string[] Ground = { "Grass_Common_Tall", "Grass_Wispy_Tall", "Fern_1", "Plant_1_Big" };
    public static readonly string[] Rocks = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3", "Rock_Medium_4" };

    /// <summary>A tree from the list (common or pine), with a random height in the given range; falls back to nothing.</summary>
    public static GameObject Tree(string[] kinds, System.Random rng, Vector3 position, float minHeight, float maxHeight, Transform parent)
    {
        string key = kinds[rng.Next(kinds.Length)];
        float h = minHeight + (float)rng.NextDouble() * (maxHeight - minHeight);
        return Place(key, position, (float)rng.NextDouble() * 360f, h, parent);
    }
}
