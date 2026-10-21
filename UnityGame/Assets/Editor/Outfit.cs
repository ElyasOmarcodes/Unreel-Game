using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dresses the mannequin: clothes are made from the character's own skinned mesh (the triangles of a
/// body region pushed out along the normals), so a shirt, trousers, gloves, boots and a plate carrier
/// bend with every animation. Also a simple face and head gear fitted to the measured head.
/// </summary>
public static class Outfit
{
    public enum Region { None, Head, Torso, Arms, Hands, Legs, Feet }

    private const string Folder = "Assets/Generated/Outfit";

    public static Region Of(string bone)
    {
        string b = bone.ToLowerInvariant();
        if (b.Contains("hand") || b.Contains("thumb") || b.Contains("index") || b.Contains("middle") || b.Contains("ring") || b.Contains("pinky")) return Region.Hands;
        if (b.Contains("upperarm") || b.Contains("lowerarm") || b.Contains("arm")) return Region.Arms;
        if (b.Contains("head") || b.Contains("neck")) return Region.Head;
        if (b.Contains("foot") || b.Contains("ball") || b.Contains("toe")) return Region.Feet;
        if (b.Contains("thigh") || b.Contains("calf") || b.Contains("pelvis") || b.Contains("leg")) return Region.Legs;
        if (b.Contains("spine") || b.Contains("clavicle") || b.Contains("chest")) return Region.Torso;
        return Region.None;
    }

    /// <summary>Region of every vertex (from its strongest bone).</summary>
    private static Region[] VertexRegions(SkinnedMeshRenderer s)
    {
        var weights = s.sharedMesh.boneWeights;
        var bones = s.bones;
        var regions = new Region[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var w = weights[i];
            int best = w.boneIndex0;
            float max = w.weight0;
            if (w.weight1 > max) { best = w.boneIndex1; max = w.weight1; }
            if (w.weight2 > max) { best = w.boneIndex2; max = w.weight2; }
            if (w.weight3 > max) best = w.boneIndex3;
            regions[i] = best >= 0 && best < bones.Length && bones[best] != null ? Of(bones[best].name) : Region.None;
        }
        return regions;
    }

    /// <summary>World size of one mesh unit for this renderer (meshes can be in cm or m).</summary>
    private static float MeshToWorld(SkinnedMeshRenderer s)
    {
        var baked = new Mesh();
        s.BakeMesh(baked);
        float ratio = baked.bounds.size.y / Mathf.Max(1e-5f, s.sharedMesh.bounds.size.y);
        UnityEngine.Object.DestroyImmediate(baked);
        return ratio * s.transform.lossyScale.y;
    }

    /// <summary>
    /// A garment: every triangle touching one of the regions, pushed out by thickness (meters).
    /// Returns null if the mesh data cannot be read.
    /// </summary>
    public static SkinnedMeshRenderer Shell(SkinnedMeshRenderer src, string key, string name, Region[] regions, float thickness, Material material, Func<Vector3, bool> keepVertex = null)
    {
        try
        {
            var shared = src.sharedMesh;
            var vertexRegions = VertexRegions(src);
            var vertices = shared.vertices;
            var normals = shared.normals;
            float unit = MeshToWorld(src);
            // vertex positions in world space (current pose) for the optional filter
            Vector3[] posed = null;
            if (keepVertex != null)
            {
                var baked = new Mesh();
                src.BakeMesh(baked);
                var m = Matrix4x4.TRS(src.transform.position, src.transform.rotation, Vector3.one);
                posed = baked.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
                UnityEngine.Object.DestroyImmediate(baked);
            }
            var keep = new HashSet<Region>(regions);
            var triangles = new List<int>();
            for (int sub = 0; sub < shared.subMeshCount; sub++)
            {
                var t = shared.GetTriangles(sub);
                for (int i = 0; i < t.Length; i += 3)
                {
                    bool any = keep.Contains(vertexRegions[t[i]]) || keep.Contains(vertexRegions[t[i + 1]]) || keep.Contains(vertexRegions[t[i + 2]]);
                    if (!any) continue;
                    if (posed != null && !(keepVertex(posed[t[i]]) && keepVertex(posed[t[i + 1]]) && keepVertex(posed[t[i + 2]]))) continue;
                    triangles.Add(t[i]); triangles.Add(t[i + 1]); triangles.Add(t[i + 2]);
                }
            }
            if (triangles.Count == 0) return null;
            var mesh = UnityEngine.Object.Instantiate(shared);
            mesh.name = $"{key}_{name}";
            var pushed = new Vector3[vertices.Length];
            float offset = thickness / Mathf.Max(1e-5f, unit);
            for (int i = 0; i < vertices.Length; i++) pushed[i] = vertices[i] + normals[i] * offset;
            mesh.vertices = pushed;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            AssetSetup.Folder(Folder);
            string path = $"{Folder}/{key}_{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject(name);
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition;
            go.transform.localRotation = src.transform.localRotation;
            go.transform.localScale = src.transform.localScale;
            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = mesh;
            r.bones = src.bones;
            r.rootBone = src.rootBone;
            r.sharedMaterial = material;
            var bounds = src.localBounds;
            bounds.Expand(0.2f / Mathf.Max(1e-5f, unit));
            r.localBounds = bounds;
            r.updateWhenOffscreen = false;
            r.quality = SkinQuality.Bone4;
            return r;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Setup] outfit {name}: {e.Message}");
            return null;
        }
    }

    /// <summary>Bounds of the head (vertices skinned to the head bone) in the frame's space, in the current pose.</summary>
    public static Bounds? HeadBounds(SkinnedMeshRenderer[] skinned, Transform head, Transform frame)
    {
        try
        {
            Bounds? result = null;
            foreach (var s in skinned)
            {
                int headIndex = Array.IndexOf(s.bones, head);
                if (headIndex < 0) continue;
                var weights = s.sharedMesh.boneWeights;
                var baked = new Mesh();
                s.BakeMesh(baked);
                var toWorld = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);
                var verts = baked.vertices;
                for (int i = 0; i < verts.Length && i < weights.Length; i++)
                {
                    var w = weights[i];
                    float onHead = (w.boneIndex0 == headIndex ? w.weight0 : 0) + (w.boneIndex1 == headIndex ? w.weight1 : 0)
                                   + (w.boneIndex2 == headIndex ? w.weight2 : 0) + (w.boneIndex3 == headIndex ? w.weight3 : 0);
                    if (onHead < 0.6f) continue;
                    var p = frame.InverseTransformPoint(toWorld.MultiplyPoint3x4(verts[i]));
                    if (result == null) result = new Bounds(p, Vector3.zero);
                    else { var b = result.Value; b.Encapsulate(p); result = b; }
                }
                UnityEngine.Object.DestroyImmediate(baked);
            }
            return result;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Setup] head bounds: " + e.Message);
            return null;
        }
    }
}
