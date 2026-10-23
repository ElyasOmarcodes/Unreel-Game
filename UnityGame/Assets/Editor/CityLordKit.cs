using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The CityLord data loaded once for the whole build, and its pieces (ammo box, wooden boxes, barrels,
/// pallets, containers, oil tanks, hangars, fences, the road ramp...) as meshes of their own with their
/// own materials and colliders, so every map can use them.
/// </summary>
public static partial class CityLordBuilder
{
    private static bool loaded;
    private static List<Dictionary<string, object>> objects;
    private static HashSet<string> inactive;
    private static readonly Dictionary<string, KitPiece> kit = new Dictionary<string, KitPiece>();
    private const string KitAsset = MeshFolder + "/Kit.asset";
    private static bool kitAssetMade;

    /// <summary>
    /// Reads the scene and starts the generated folders afresh. MapBuilder calls it with fresh = true
    /// before the first map is built; later calls (the maps, the kit) only read what is there.
    /// </summary>
    public static void Load(bool fresh)
    {
        if (loaded && !fresh) return;
        raw.Clear();
        materials.Clear();
        textures.Clear();
        kit.Clear();
        kitAssetMade = false;
        string file = Path.Combine(DataFolder, "scene.json");
        if (!File.Exists(file)) throw new FileNotFoundException("[Map] CityLord data missing", file);
        scene = D(Json.Parse(File.ReadAllText(file)));
        objects = L(scene["objects"]).Select(D).ToList();
        // paths are not unique (siblings with the same name); every object of the map with a switched-off path is off itself
        inactive = new HashSet<string>(objects.Where(o => !B(o["active"])).Select(o => S(o["path"])));
        AssetDatabase.DeleteAsset(MeshFolder);
        AssetDatabase.DeleteAsset(MatFolder);
        AssetSetup.Folder(MeshFolder);
        AssetSetup.Folder(TexFolder);
        AssetSetup.Folder(MatFolder);
        loaded = true;
    }

    private static bool ActiveInHierarchy(string path)
    {
        for (string p = path; ; p = p.Substring(0, p.LastIndexOf('/')))
        {
            if (inactive.Contains(p)) return false;
            if (p.IndexOf('/') < 0) return true;
        }
    }

    /// <summary>A material of the map by its name (as "Seamless_asphalt_v1").</summary>
    public static Material MaterialNamed(string name)
    {
        Load(false);
        foreach (var kv in D(scene["materials"]))
            if (kv.Value != null && S(D(kv.Value)["name"]) == name) return Mat(kv.Key);
        Debug.LogWarning("[Map] CityLord: no material named " + name);
        return null;
    }

    public class KitPiece
    {
        public Mesh Mesh;
        public Material[] Materials;
        public ShadowCastingMode Shadows;
        /// <summary>Mesh collider (null: a box collider of BoxSize at BoxCenter).</summary>
        public Mesh Collider;
        public Vector3 BoxCenter, BoxSize;
        /// <summary>Where it stands: the bounds of the mesh, its foot at y 0.</summary>
        public Bounds Bounds;
        /// <summary>The bounds of its collider.</summary>
        public Bounds ColliderBounds;
    }

    /// <summary>
    /// One piece of the map as a mesh of its own: the first object of that name (as "AmmoBox",
    /// "Wooden_box_v1_LD2square"), turned and moved back to its own origin with its scale kept, its foot
    /// at y 0.
    /// </summary>
    public static KitPiece Kit(string name)
    {
        Load(false);
        if (kit.TryGetValue(name, out var cached)) return cached;
        var o = objects.FirstOrDefault(x => (S(x["name"]) == name || S(x["name"]).StartsWith(name + " (")) && S(x["path"]).StartsWith("Map_v1") && ActiveInHierarchy(S(x["path"]))
                                            && L(x["components"]).Select(D).Any(c => S(c["type"]) == "MeshRenderer"));
        if (o == null) { Debug.LogWarning("[Map] CityLord: no piece named " + name); return kit[name] = null; }
        var world = M(o["world_matrix"]);
        var pose = Matrix4x4.TRS(world.GetColumn(3), world.rotation, Vector3.one).inverse;
        var comps = L(o["components"]).Select(D).ToList();
        var r = comps.First(c => S(c["type"]) == "MeshRenderer");
        var mats = L(r["materials"]).Select(m => m == null ? null : Mat(S(m))).ToArray();
        int count = I(r["static_batch_submesh_count"]);
        var src = LoadMesh(S(r["mesh"]));
        if (src == null || mats.Length == 0) return kit[name] = null;
        int first = count > 0 ? I(r["static_batch_first_submesh"]) : 0;
        int subs = count > 0 ? count : mats.Length;
        var mesh = Piece("CL_" + name, src, Enumerable.Range(first, subs).ToList(), count > 0 ? pose : pose * world);
        float foot = mesh.bounds.min.y;
        var lift = Matrix4x4.Translate(new Vector3(0, -foot, 0));
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i].y -= foot;
        mesh.vertices = verts;
        mesh.RecalculateBounds();
        var piece = new KitPiece
        {
            Mesh = mesh,
            Materials = Enumerable.Range(0, subs).Select(i => mats[Mathf.Min(i, mats.Length - 1)]).ToArray(),
            Shadows = (ShadowCastingMode)I(r["cast_shadows"]),
            Bounds = mesh.bounds,
        };
        var toKit = lift * pose * world;
        foreach (var c in comps)
        {
            string type = S(c["type"]);
            if (type == "MeshCollider" && LoadMesh(S(c["mesh"])) is RawMesh cm)
            {
                var col = new Mesh { name = "CL_" + name + "_Collider" };
                col.SetVertices(cm.P.Select(p => toKit.MultiplyPoint3x4(p)).ToList());
                col.SetTriangles(cm.Sub.SelectMany(s => s).ToArray(), 0);
                col.RecalculateBounds();
                piece.Collider = col;
                piece.ColliderBounds = col.bounds;
                break;
            }
            if (type == "BoxCollider" && !B(c["is_trigger"]))
            {
                piece.BoxCenter = toKit.MultiplyPoint3x4(V3(c["center"]));
                // the box's own axes are the piece's axes (pieces are not turned against their collider)
                piece.BoxSize = Vector3.Scale(V3(c["size"]), Abs(world.lossyScale));
                piece.ColliderBounds = new Bounds(piece.BoxCenter, piece.BoxSize);
                break;
            }
        }
        if (piece.Collider == null && piece.BoxSize == Vector3.zero)
        {
            piece.BoxCenter = piece.Bounds.center;
            piece.BoxSize = piece.Bounds.size;
            piece.ColliderBounds = piece.Bounds;
        }
        SaveKitMesh(piece.Mesh);
        SaveKitMesh(piece.Collider);
        return kit[name] = piece;
    }

    private static void SaveKitMesh(Mesh mesh)
    {
        if (mesh == null || AssetDatabase.Contains(mesh)) return;
        if (!kitAssetMade) { AssetDatabase.DeleteAsset(KitAsset); AssetDatabase.CreateAsset(mesh, KitAsset); kitAssetMade = true; }
        else AssetDatabase.AddObjectToAsset(mesh, KitAsset);
    }

    /// <summary>
    /// The CityLord wooden ladder against a wall: its foot at 'foot' (world, on the ground at the wall
    /// face), the wall facing 'outward', reaching 'height' up, with the zone players and bots climb in.
    /// </summary>
    public static GameObject PlaceLadder(Transform parent, Vector3 foot, Vector3 outward, float height)
    {
        outward.y = 0;
        outward.Normalize();
        var piece = Kit("TallLadder");
        float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
        var go = piece != null ? Place("TallLadder", parent, foot + outward * 0.2f, yaw, false) : null;
        if (go == null)
        {
            go = new GameObject("Ladder");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(foot + outward * 0.2f, Quaternion.Euler(0, yaw, 0));
        }
        else
        {
            // stretched to the height, its rungs a little closer or further apart; narrower on short climbs
            float sy = (height + 0.9f) / Mathf.Max(0.5f, piece.Bounds.size.y);
            go.transform.localScale = new Vector3(Mathf.Clamp(sy * 1.4f, 0.7f, 1f), sy, 1f);
        }
        var ladder = go.AddComponent<UA.Ladder>();
        ladder.Into = -outward;
        ladder.Top = foot.y + height;
        ladder.Zone = new Bounds(foot + outward * 0.55f + Vector3.up * ((height + 1.4f) / 2f),
            new Vector3(Mathf.Abs(outward.z) * 1.4f + Mathf.Abs(outward.x) * 1.2f, height + 1.4f, Mathf.Abs(outward.x) * 1.4f + Mathf.Abs(outward.z) * 1.2f));
        return go;
    }

    /// <summary>
    /// Places a piece of the town: its foot at 'position' (world), turned by yaw, scaled; with its
    /// collider unless told not to. Returns null when the piece is missing.
    /// </summary>
    public static GameObject Place(string name, Transform parent, Vector3 position, float yaw, bool collider = true, float scale = 1f)
    {
        var piece = Kit(name);
        if (piece == null) return null;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = piece.Materials;
        r.shadowCastingMode = piece.Shadows;
        r.lightProbeUsage = LightProbeUsage.Off;
        if (collider)
        {
            if (piece.Collider != null) go.AddComponent<MeshCollider>().sharedMesh = piece.Collider;
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = piece.BoxCenter;
                box.size = piece.BoxSize;
            }
        }
        return go;
    }
}
