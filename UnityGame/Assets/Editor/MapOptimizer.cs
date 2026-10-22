using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fewer draw calls for phones: once a map is built, its static pieces (thousands of boxes, steps,
/// props) are merged into a few large meshes, one per material in each 40 m square of the map. The
/// game objects and their colliders stay where they are, only their renderers go, so the map plays
/// and looks exactly the same. Small pieces near the ground are merged apart, on a layer the camera
/// stops drawing at a distance (see CameraRig).
/// </summary>
public static class MapOptimizer
{
    public const int DetailLayer = 12;
    private const float Cell = 40f;
    private const int MaxVertices = 65000;
    private const string Folder = "Assets/Generated/Merged";

    /// <summary>
    /// Merges the map under root. Pieces listed in nightOnly (lit windows, lamp glass) are merged
    /// apart; the merged renderers are returned so the world switches them on at night.
    /// </summary>
    public static List<Renderer> Merge(Transform root, string name, ICollection<Renderer> keep = null, ICollection<Renderer> nightOnly = null)
    {
        // glows face the camera around their own centre: never batched, never merged
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == "UA/Glow")
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
        var night = new List<Renderer>();
        var nightSet = new HashSet<Renderer>(nightOnly ?? new Renderer[0]);
        AssetSetup.Folder(Folder);
        string path = $"{Folder}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        var toLocal = root.worldToLocalMatrix;
        var groups = new Dictionary<(Material mat, int x, int z, ShadowCastingMode shadows, bool detail, bool night), List<CombineInstance>>();
        var merged = new List<MeshRenderer>();
        int before = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            before++;
            bool isNight = nightSet.Contains(r);
            if ((!r.enabled && !isNight) || (keep != null && keep.Contains(r)) || !ActiveUnder(r.transform, root)) continue;
            if (r.GetComponentInParent<LODGroup>(true) != null) continue;
            if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            var mats = r.sharedMaterials;
            if (mesh == null || mats.Length == 0 || mats.Length > mesh.subMeshCount) continue;
            if (mats.Any(m => m == null || m.renderQueue > 2500)) continue;
            var matrix = toLocal * r.localToWorldMatrix;
            if (matrix.determinant <= 0f) continue;   // mirrored pieces keep their own renderer
            var b = r.bounds;
            if (Mathf.Max(b.size.x, b.size.z) > Cell) continue;   // ground and other big pieces gain nothing
            bool detail = !isNight && Mathf.Max(b.size.x, b.size.y, b.size.z) < 1.6f && b.min.y < 2.5f;
            int cx = Mathf.FloorToInt(b.center.x / Cell), cz = Mathf.FloorToInt(b.center.z / Cell);
            for (int s = 0; s < mats.Length; s++)
            {
                var key = (mats[s], isNight ? 0 : cx, isNight ? 0 : cz, r.shadowCastingMode, detail, isNight);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = Part(mesh, s), subMeshIndex = 0, transform = matrix });
            }
            merged.Add(r);
        }

        var parent = new GameObject("Merged").transform;
        parent.SetParent(root, false);
        int count = 0;
        bool firstAsset = true;
        foreach (var g in groups)
        {
            // split groups that would go over 16-bit indices
            var chunk = new List<CombineInstance>();
            int vertices = 0;
            void Flush()
            {
                if (chunk.Count == 0) return;
                var mesh = new Mesh { name = $"{name}_{count}" };
                if (vertices > 65535) mesh.indexFormat = IndexFormat.UInt32;   // one very big piece
                mesh.CombineMeshes(chunk.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                Slim(mesh, g.Key.mat);
                if (firstAsset) { AssetDatabase.CreateAsset(mesh, path); firstAsset = false; }
                else AssetDatabase.AddObjectToAsset(mesh, path);
                var go = new GameObject($"Merged_{g.Key.mat.name}");
                go.transform.SetParent(parent, false);
                go.layer = g.Key.detail ? DetailLayer : 0;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = g.Key.mat;
                r.shadowCastingMode = g.Key.shadows;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (g.Key.night) { r.enabled = false; night.Add(r); }
                count++;
                chunk.Clear();
                vertices = 0;
            }
            foreach (var c in g.Value)
            {
                int n = c.mesh.vertexCount;   // a sub-mesh brings the whole vertex list of its mesh
                if (vertices + n > MaxVertices) Flush();
                chunk.Add(c);
                vertices += n;
            }
            Flush();
        }
        foreach (var r in merged)
        {
            var filter = r.GetComponent<MeshFilter>();
            Object.DestroyImmediate(r);
            if (filter != null) Object.DestroyImmediate(filter);
        }
        Debug.Log($"[Map] {name}: {merged.Count} of {before} renderers merged into {count} ({night.Count} for the night)");
        // night pieces that could not be merged still go on and off with the night
        night.AddRange(nightSet.Where(r => r != null));
        return night;
    }

    /// <summary>
    /// Smaller in the APK and in memory: only the vertex data the material reads (tangents only for
    /// normal maps, no colours or extra UV sets), quantised, and no copy kept for scripts.
    /// </summary>
    private static void Slim(Mesh mesh, Material m)
    {
        if (!m.IsKeywordEnabled("_NORMALMAP")) mesh.tangents = new Vector4[0];
        if (m.shader == null || !m.shader.name.StartsWith("UA/Ground")) mesh.colors = new Color[0];
        for (int i = 1; i < 8; i++) mesh.SetUVs(i, new List<Vector2>());
        MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
        var so = new SerializedObject(mesh);
        var readable = so.FindProperty("m_IsReadable");
        if (readable != null) { readable.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
    }

    private static readonly Dictionary<(Mesh, int), Mesh> parts = new Dictionary<(Mesh, int), Mesh>();

    /// <summary>One sub-mesh as a mesh of its own, with only the vertices it uses (merging a sub-mesh copies all of them).</summary>
    private static Mesh Part(Mesh mesh, int sub)
    {
        if (mesh.subMeshCount == 1) return mesh;
        if (parts.TryGetValue((mesh, sub), out var cached) && cached != null) return cached;
        var indices = mesh.GetIndices(sub);
        var map = new Dictionary<int, int>();
        var used = new List<int>();
        var remapped = new int[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            if (!map.TryGetValue(indices[i], out int n)) { n = used.Count; map[indices[i]] = n; used.Add(indices[i]); }
            remapped[i] = n;
        }
        List<T> Pick<T>(T[] all) => all.Length == mesh.vertexCount ? used.Select(i => all[i]).ToList() : null;
        var part = new Mesh { name = $"{mesh.name}_{sub}" };
        part.SetVertices(Pick(mesh.vertices));
        var normals = Pick(mesh.normals); if (normals != null) part.SetNormals(normals);
        var tangents = Pick(mesh.tangents); if (tangents != null) part.SetTangents(tangents);
        var uv = Pick(mesh.uv); if (uv != null) part.SetUVs(0, uv);
        var colors = Pick(mesh.colors); if (colors != null) part.SetColors(colors);
        part.SetIndices(remapped, mesh.GetTopology(sub), 0);
        parts[(mesh, sub)] = part;
        return part;
    }

    private static bool ActiveUnder(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (!t.gameObject.activeSelf) return false;
        return true;
    }
}
