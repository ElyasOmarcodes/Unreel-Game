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

    public static void Merge(Transform root, string name, ICollection<Renderer> keep = null)
    {
        AssetSetup.Folder(Folder);
        string path = $"{Folder}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        var toLocal = root.worldToLocalMatrix;
        var groups = new Dictionary<(Material mat, int x, int z, ShadowCastingMode shadows, bool detail), List<CombineInstance>>();
        var merged = new List<MeshRenderer>();
        int before = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            before++;
            if (!r.enabled || (keep != null && keep.Contains(r)) || !ActiveUnder(r.transform, root)) continue;
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
            bool detail = Mathf.Max(b.size.x, b.size.y, b.size.z) < 1.6f && b.min.y < 2.5f;
            int cx = Mathf.FloorToInt(b.center.x / Cell), cz = Mathf.FloorToInt(b.center.z / Cell);
            for (int s = 0; s < mats.Length; s++)
            {
                var key = (mats[s], cx, cz, r.shadowCastingMode, detail);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = matrix });
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
                mesh.CombineMeshes(chunk.ToArray(), true, true, false);
                mesh.RecalculateBounds();
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
                count++;
                chunk.Clear();
                vertices = 0;
            }
            foreach (var c in g.Value)
            {
                int n = c.mesh.GetSubMesh(c.subMeshIndex).vertexCount;
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
        Debug.Log($"[Map] {name}: {merged.Count} of {before} renderers merged into {count}");
    }

    private static bool ActiveUnder(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (!t.gameObject.activeSelf) return false;
        return true;
    }
}
