using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Spaces below street level: the ground is laid as boxes around the openings (stairwells, pits),
/// and rooms and tunnels get floors, walls with openings, a ceiling under the ground slab, light
/// strips and a few real lights. Also sloped pieces (ramps, wedges) for the open maps.
/// </summary>
public static class Underground
{
    public class Style
    {
        public Material Floor, Wall, Ceiling, Trim, Lamp;
    }

    /// <summary>Rectangles that cover the area except the holes (for ground slabs and ceilings).</summary>
    public static List<Rect> Cover(Rect area, IList<Rect> holes)
    {
        var xs = new SortedSet<float> { area.xMin, area.xMax };
        var zs = new SortedSet<float> { area.yMin, area.yMax };
        foreach (var h in holes)
        {
            if (!h.Overlaps(area)) continue;
            xs.Add(Mathf.Clamp(h.xMin, area.xMin, area.xMax)); xs.Add(Mathf.Clamp(h.xMax, area.xMin, area.xMax));
            zs.Add(Mathf.Clamp(h.yMin, area.yMin, area.yMax)); zs.Add(Mathf.Clamp(h.yMax, area.yMin, area.yMax));
        }
        var x = xs.ToList();
        var z = zs.ToList();
        var result = new List<Rect>();
        for (int j = 0; j + 1 < z.Count; j++)
        {
            float start = float.NaN;
            for (int i = 0; i + 1 < x.Count; i++)
            {
                var centre = new Vector2((x[i] + x[i + 1]) / 2f, (z[j] + z[j + 1]) / 2f);
                bool open = holes.Any(h => h.Contains(centre));
                if (!open && float.IsNaN(start)) start = x[i];
                if ((open || i + 2 == x.Count) && !float.IsNaN(start))
                {
                    float end = open ? x[i] : x[i + 1];
                    if (end - start > 0.01f) result.Add(Rect.MinMaxRect(start, z[j], end, z[j + 1]));
                    start = float.NaN;
                }
            }
        }
        return result;
    }

    public static bool InAny(IEnumerable<Rect> rects, Vector3 p, float margin) =>
        rects.Any(r => p.x > r.xMin - margin && p.x < r.xMax + margin && p.z > r.yMin - margin && p.z < r.yMax + margin);

    private static GameObject Box(Transform t, string name, Vector3 centre, Vector3 size, Material m, bool collider = true, float round = 0f)
    {
        if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) return null;
        var go = round > 0f ? MeshGen.RoundBox(name, centre, size, round, m, 2f, t, 0f, collider) : MeshGen.TexBox(name, centre, size, m, 2f, t, 0f, collider);
        // under the ground slab: no sun reaches here, so nothing needs to cast a shadow
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    /// <summary>
    /// The same material glowing faintly by itself: rooms underground look lit by their lamps
    /// without real lights (each real light draws everything it touches again, slow on phones).
    /// </summary>
    public static Material Lit(Material m, float glow = 0.32f)
    {
        if (m == null || m.name.EndsWith("_Lit")) return m;
        string path = $"{AssetSetup.Generated}/Materials/{m.name}_Lit.mat";
        var lit = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (lit == null)
        {
            lit = new Material(m) { name = m.name + "_Lit" };
            AssetDatabase.CreateAsset(lit, path);
        }
        else lit.CopyPropertiesFromMaterial(m);
        lit.name = m.name + "_Lit";
        if (m.mainTexture != null) lit.SetTexture("_EmissionMap", m.mainTexture);
        lit.SetTextureScale("_EmissionMap", m.mainTextureScale);
        lit.SetColor("_EmissionColor", m.color * glow);
        lit.EnableKeyword("_EMISSION");
        lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return lit;
    }

    /// <summary>
    /// A room (x/z rectangle) from floor to ceiling: floor slab, ceiling panels (not over the holes),
    /// walls outside the rectangle except where openings are (side N/S/E/W, from, to along the side),
    /// light strips along the ceiling.
    /// </summary>
    public static void Room(Transform t, Rect r, float floor, float ceiling, Style s, IList<Rect> holes, IList<Rect> floorHoles, params (char side, float from, float to)[] openings)
    {
        s = new Style { Floor = Lit(s.Floor), Wall = Lit(s.Wall), Ceiling = Lit(s.Ceiling, 0.22f), Trim = Lit(s.Trim), Lamp = s.Lamp };
        foreach (var c in Cover(r, floorHoles))
            Box(t, "Floor", new Vector3(c.center.x, floor - 0.15f, c.center.y), new Vector3(c.width, 0.3f, c.height), s.Floor);
        foreach (var c in Cover(r, holes))
            Box(t, "Ceiling", new Vector3(c.center.x, ceiling - 0.06f, c.center.y), new Vector3(c.width, 0.12f, c.height), s.Ceiling, false);
        // walls reach a little below the floor (sunken parts of the room, no gaps at the edges)
        float bottom = floor - 1.4f, h = ceiling - bottom;
        void Side(char side, float a0, float a1, float fixedCoord, bool alongX)
        {
            var gaps = openings.Where(o => o.side == side).OrderBy(o => o.from).ToList();
            float cursor = a0;
            foreach (var g in gaps.Concat(new[] { (side, a1, a1) }))
            {
                float end = Mathf.Clamp(g.Item2, a0, a1);
                if (end - cursor > 0.05f)
                {
                    float mid = (cursor + end) / 2f, len = end - cursor;
                    var centre = alongX ? new Vector3(mid, bottom + h / 2f, fixedCoord) : new Vector3(fixedCoord, bottom + h / 2f, mid);
                    var size = alongX ? new Vector3(len, h, 0.3f) : new Vector3(0.3f, h, len);
                    Box(t, "Wall", centre, size, s.Wall);
                    // a darker skirting along the floor
                    var skirt = alongX ? new Vector3(len, 0.3f, 0.34f) : new Vector3(0.34f, 0.3f, len);
                    Box(t, "Skirting", new Vector3(centre.x, floor + 0.15f, centre.z), skirt, s.Trim, false);
                }
                cursor = Mathf.Max(cursor, Mathf.Clamp(g.Item3, a0, a1));
            }
        }
        Side('S', r.xMin - 0.3f, r.xMax + 0.3f, r.yMin - 0.15f, true);
        Side('N', r.xMin - 0.3f, r.xMax + 0.3f, r.yMax + 0.15f, true);
        Side('W', r.yMin, r.yMax, r.xMin - 0.15f, false);
        Side('E', r.yMin, r.yMax, r.xMax + 0.15f, false);
        // light strips under the ceiling, along the longer side
        bool alongXStrips = r.width >= r.height;
        float length = alongXStrips ? r.width : r.height;
        for (float u = 3f; u < length - 2f; u += 6f)
        {
            var p = alongXStrips ? new Vector3(r.xMin + u, ceiling - 0.16f, r.center.y) : new Vector3(r.center.x, ceiling - 0.16f, r.yMin + u);
            if (holes.Any(hole => hole.Contains(new Vector2(p.x, p.z)))) continue;
            Box(t, "LightStrip", p, alongXStrips ? new Vector3(2.4f, 0.06f, 0.25f) : new Vector3(0.25f, 0.06f, 2.4f), s.Lamp, false);
        }
    }

    /// <summary>
    /// A point light without shadows, lit per vertex: it brightens the people and props passing by
    /// inside the base pass (no extra drawing of the room, which per-pixel lights cost).
    /// </summary>
    public static void Lamp(Transform t, Vector3 p, float range = 14f, float intensity = 1.3f)
    {
        var go = new GameObject("UndergroundLight");
        go.transform.SetParent(t, false);
        go.transform.position = p;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = new Color(1f, 0.93f, 0.8f);
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForceVertex;
    }

    /// <summary>Low concrete walls around a stairwell, open on the side where the stair arrives.</summary>
    public static void Railing(Transform t, Rect hole, char openSide, Material m, float y = 0f)
    {
        const float h = 1f, w = 0.3f;
        if (openSide != 'S') MeshGen.RoundBox("Railing", new Vector3(hole.center.x, y + h / 2f, hole.yMin - w / 2f), new Vector3(hole.width + w * 2, h, w), 0.06f, m, 2f, t);
        if (openSide != 'N') MeshGen.RoundBox("Railing", new Vector3(hole.center.x, y + h / 2f, hole.yMax + w / 2f), new Vector3(hole.width + w * 2, h, w), 0.06f, m, 2f, t);
        if (openSide != 'W') MeshGen.RoundBox("Railing", new Vector3(hole.xMin - w / 2f, y + h / 2f, hole.center.y), new Vector3(w, h, hole.height), 0.06f, m, 2f, t);
        if (openSide != 'E') MeshGen.RoundBox("Railing", new Vector3(hole.xMax + w / 2f, y + h / 2f, hole.center.y), new Vector3(w, h, hole.height), 0.06f, m, 2f, t);
    }

    /// <summary>A solid sloped wedge from 'low' (ground level) up to 'high' (the top edge), width across.</summary>
    public static GameObject Wedge(Transform t, string name, Vector3 low, Vector3 high, float width, Material m)
    {
        var flat = new Vector3(high.x - low.x, 0, high.z - low.z);
        float length = flat.magnitude, height = high.y - low.y;
        var go = MeshGen.Object(name, MeshGen.Wedge(width, length, height, 2f), m, t, low, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg);
        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        return go;
    }
}
