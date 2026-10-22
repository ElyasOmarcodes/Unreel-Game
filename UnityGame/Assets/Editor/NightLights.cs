using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Night lighting without real lights (each real light draws everything it touches again, slow on
/// phones): lamp glass and windows that glow by themselves, and a soft halo around each lamp. They are
/// collected in a list the world switches on at night (MapOptimizer merges the glowing glass).
/// </summary>
public static class NightLights
{
    private static Mesh quad;
    private static readonly Dictionary<string, Material> halos = new Dictionary<string, Material>();

    public static Material WarmGlass => AssetSetup.Mat("Night_LampGlass", new Color(1f, 0.86f, 0.62f), 0.5f, 0f, null, null, null, null, new Color(3.2f, 2.2f, 1.1f));
    public static Material WindowGlow => AssetSetup.Mat("Night_Window", new Color(1f, 0.8f, 0.5f), 0.4f, 0f, null, null, null, null, new Color(1.9f, 1.25f, 0.6f));

    private static Material HaloMaterial(Color color, float size)
    {
        string key = ColorUtility.ToHtmlStringRGB(color) + "_" + Mathf.RoundToInt(size * 10);
        if (halos.TryGetValue(key, out var m) && m != null) return m;
        string path = $"{AssetSetup.Generated}/Materials/Night_Halo_{key}.mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("UA/Glow") ?? Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_Color", color);
        m.SetFloat("_Size", size);
        return halos[key] = m;
    }

    private static Mesh Quad()
    {
        if (quad != null && AssetDatabase.Contains(quad)) return quad;
        quad = new Mesh { name = "HaloQuad" };
        quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
        quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        // the halo grows around its centre in the shader: bounds big enough not to be culled early
        quad.bounds = new Bounds(Vector3.zero, Vector3.one * 6f);
        AssetSetup.Folder(AssetSetup.Generated + "/Meshes");
        AssetDatabase.CreateAsset(quad, $"{AssetSetup.Generated}/Meshes/HaloQuad.asset");
        return quad;
    }

    /// <summary>A soft halo (size in metres) around a point, shown only at night.</summary>
    public static Renderer Halo(Transform parent, Vector3 position, float size, Color color, List<Renderer> night)
    {
        var go = new GameObject("Halo");
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = HaloMaterial(color, size);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;
        night.Add(r);
        return r;
    }

    /// <summary>Lamp glass that lights up at night (inside a dark housing), with its halo.</summary>
    public static void Lamp(Transform parent, Vector3 center, Vector3 size, List<Renderer> night, float halo = 2.2f)
    {
        var glass = MeshGen.TexBox("LampGlow", center, size, WarmGlass, 1f, parent, 0f, false);
        var r = glass.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.enabled = false;
        night.Add(r);
        if (halo > 0f) Halo(parent, center, halo, new Color(0.55f, 0.36f, 0.16f), night);
        // the warm pool of light on the ground under it
        if (halo > 0f)
        {
            Physics.SyncTransforms();
            if (Physics.Raycast(center + Vector3.down * 0.3f, Vector3.down, out var hit, 14f, UA.World.EnvMask, QueryTriggerInteraction.Ignore))
                Pool(parent, hit.point + hit.normal * 0.03f, hit.normal, Mathf.Clamp((center.y - hit.point.y) * 1.3f, 2.2f, 7f), night);
        }
    }

    private static Material poolMaterial;

    /// <summary>Warm lamp light lying on a surface (ground or wall, given its normal), radius in metres; night only.</summary>
    public static void Pool(Transform parent, Vector3 center, Vector3 normal, float radius, List<Renderer> night, float strength = 1f)
    {
        if (poolMaterial == null)
        {
            string path = $"{AssetSetup.Generated}/Materials/Night_Pool.mat";
            poolMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (poolMaterial == null)
            {
                poolMaterial = new Material(Shader.Find("UA/GlowPool") ?? Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(poolMaterial, path);
            }
            poolMaterial.SetColor("_Color", new Color(0.62f, 0.33f, 0.12f));
        }
        var go = new GameObject("LightPool");
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.rotation = Quaternion.LookRotation(-normal);
        go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f) * strength;
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = poolMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;
        night.Add(r);
    }

    private static Mesh pane;

    /// <summary>
    /// A lit window seen from outside at night: a one-sided pane facing outward (from inside the room
    /// it is not there, so nobody inside is blinded or blocked).
    /// </summary>
    public static void Window(Transform parent, Vector3 center, Vector3 outward, float width, float height, List<Renderer> night)
    {
        if (pane == null || !AssetDatabase.Contains(pane))
        {
            pane = new Mesh { name = "WindowPane" };
            pane.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            pane.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            pane.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            pane.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            pane.RecalculateBounds();
            AssetSetup.Folder(AssetSetup.Generated + "/Meshes");
            AssetDatabase.CreateAsset(pane, $"{AssetSetup.Generated}/Meshes/WindowPane.asset");
        }
        var go = new GameObject("WindowGlow");
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.rotation = Quaternion.LookRotation(-outward);
        go.transform.localScale = new Vector3(width, height, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = pane;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = WindowGlow;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.enabled = false;
        night.Add(r);
    }
}
