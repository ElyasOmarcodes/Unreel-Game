using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The "Old Town" map: a small European market town of half-timbered houses (stone ground floor,
/// plastered upper floors framed with dark oak beams, steep tiled roofs with the gable to the street,
/// chimneys, shutters, flower boxes and wrought-iron lanterns), cobbled streets around a market
/// square with a well and stalls, a tavern, a church whose tower can be climbed, a canal with stone
/// bridges, gardens with picket fences, a barn and a windmill. Lanterns and lit windows glow at night
/// without real lights (NightLights). Textures: ambientCG (CC0).
/// </summary>
public static class TownBuilder
{
    public const float Half = 74f;
    private const float FloorH = 3.1f, Wall = 0.3f;
    private const float CanalX0 = 55f, CanalX1 = 61f;

    private static Transform root;
    private static System.Random rng;
    private static readonly List<Renderer> night = new List<Renderer>();
    private static readonly List<Rect> taken = new List<Rect>();
    private static Mesh spire;

    private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ materials

    private static readonly Color[] PlasterTints =
    {
        new Color(0.97f, 0.96f, 0.93f), new Color(0.9f, 0.88f, 0.99f), new Color(0.99f, 0.93f, 0.82f),
        new Color(0.99f, 0.88f, 0.86f), new Color(0.88f, 0.94f, 0.9f),
    };
    private static readonly Color[] RoofTints =
    {
        new Color(0.36f, 0.46f, 0.98f), new Color(0.82f, 0.38f, 0.3f), new Color(0.56f, 0.39f, 0.31f), new Color(0.42f, 0.44f, 0.54f),
    };
    private static Material Cobbles => PortBuilder.Pbr("T_Cobbles", "PavingStones070", new Color(0.8f, 0.77f, 0.88f), 0.35f);
    private static Material Grass => PortBuilder.Pbr("T_Grass", "Grass004", new Color(0.82f, 0.95f, 0.78f), 0.2f);
    private static Material Stone => PortBuilder.Pbr("T_Stone", "PavingStones070", new Color(0.74f, 0.72f, 0.8f), 0.25f);
    private static Material Timber => PortBuilder.Pbr("T_Timber", "Wood049", new Color(0.45f, 0.27f, 0.18f), 0.35f);
    private static Material DoorWood => PortBuilder.Pbr("T_Door", "Planks037A", new Color(0.62f, 0.36f, 0.22f), 0.35f);
    private static Material Boards => PortBuilder.Pbr("WoodFloor", "WoodFloor051", Color.white, 0.6f);
    private static Material Barn => PortBuilder.Pbr("T_Barn", "Planks037A", new Color(0.7f, 0.42f, 0.3f), 0.3f);
    private static Material Hay => PortBuilder.Pbr("V_Hay", "Ground104", new Color(1f, 0.86f, 0.5f), 0.05f);
    private static Material Plaster(int i) => PortBuilder.Pbr("T_Plaster" + i, "Plaster001", PlasterTints[i % PlasterTints.Length], 0.3f);
    private static Material Roof(int i) => PortBuilder.Pbr("T_Roof" + i, "RoofingTiles006", RoofTints[i % RoofTints.Length], 0.4f);
    private static Material Iron => AssetSetup.Solid("T_Iron", new Color(0.07f, 0.07f, 0.08f), 0.45f, 0.6f);
    private static Material Glass => AssetSetup.Mat("T_WindowGlass", new Color(0.16f, 0.26f, 0.32f), 0.93f, 0.3f);
    private static Material Water => AssetSetup.Mat("T_Water", new Color(0.12f, 0.24f, 0.3f), 0.96f, 0.1f);
    private static Material Flower(int i) => AssetSetup.Solid("T_Flower" + i,
        new[] { new Color(0.45f, 0.35f, 0.95f), new Color(0.75f, 0.35f, 0.85f), new Color(0.95f, 0.45f, 0.6f), new Color(0.95f, 0.85f, 0.4f) }[i % 4], 0.2f);
    private static Material Cloth(int i) => AssetSetup.Solid("T_Cloth" + i,
        new[] { new Color(0.78f, 0.2f, 0.2f), new Color(0.2f, 0.45f, 0.75f), new Color(0.9f, 0.75f, 0.3f), new Color(0.3f, 0.6f, 0.35f) }[i % 4], 0.2f);

    // ------------------------------------------------------------------ helpers (local to a transform)

    private static GameObject B(Transform t, string name, Vector3 local, Vector3 size, Material m, float tex = 2f, bool collider = true)
    {
        if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) return null;
        return MeshGen.TexBox(name, t.TransformPoint(local), size, m, tex, t, t.eulerAngles.y, collider);
    }

    /// <summary>A thin beam from a to b (local points) lying in the wall plane, sticking out along the normal.</summary>
    private static void Beam(Transform t, Vector3 a, Vector3 b, Vector3 normal, float width = 0.18f)
    {
        var go = B(t, "Beam", (a + b) / 2f + normal * 0.04f, new Vector3(width, (b - a).magnitude + width * 0.5f, 0.08f), Timber, 1f, false);
        if (go == null) return;
        go.transform.rotation = t.rotation * Quaternion.LookRotation(-normal, (b - a).normalized);
    }

    private static Transform Node(string name, Transform parent, Vector3 pos, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        return t;
    }

    /// <summary>One wall from u0 to u1 along local X (alongX) or Z, with openings (centre, width, sill, top).</summary>
    private static void WallStrip(Transform t, bool alongX, float fixedCoord, float y0, float h, float u0, float u1, List<(float c, float w, float b, float top)> openings, Material m)
    {
        float cursor = u0;
        Vector3 Pos(float u, float y) => alongX ? new Vector3(u, y, fixedCoord) : new Vector3(fixedCoord, y, u);
        Vector3 Size(float length, float height) => alongX ? new Vector3(length, height, Wall) : new Vector3(Wall, height, length);
        foreach (var o in openings.OrderBy(o => o.c))
        {
            float a = o.c - o.w / 2f, b = o.c + o.w / 2f;
            if (a > cursor) B(t, "Wall", Pos((cursor + a) / 2f, y0 + h / 2f), Size(a - cursor, h), m);
            if (o.b > 0.01f) B(t, "Sill", Pos(o.c, y0 + o.b / 2f), Size(o.w, o.b), m);
            if (o.top < h - 0.01f) B(t, "Lintel", Pos(o.c, y0 + (o.top + h) / 2f), Size(o.w, h - o.top), m);
            cursor = b;
        }
        if (u1 > cursor) B(t, "Wall", Pos((cursor + u1) / 2f, y0 + h / 2f), Size(u1 - cursor, h), m);
    }

    private static List<(float c, float w, float b, float top)> Openings(float length, float? door, float doorAt, float windowW = 1.0f)
    {
        var list = new List<(float, float, float, float)>();
        int count = Mathf.Max(1, Mathf.FloorToInt((length - 1.2f) / 2.6f));
        float spacing = (length - 1.2f) / count;
        for (int i = 0; i < count; i++)
        {
            float c = -length / 2f + 0.6f + spacing * (i + 0.5f);
            if (door.HasValue && Mathf.Abs(c - doorAt) < door.Value / 2f + windowW / 2f + 0.3f) continue;
            list.Add((c, windowW, 0.95f, 2.25f));
        }
        if (door.HasValue) list.Add((doorAt, door.Value, 0f, 2.3f));
        return list;
    }

    /// <summary>Window dressing on a wall face: frame, cross bars, glass (closed windows), shutters, flower box, night light.</summary>
    private static void Window(Transform t, Vector3 center, Vector3 normal, Vector3 along, float w, float h, bool glass, bool shutters, bool flowers)
    {
        var face = center + normal * 0.02f;
        float f = 0.09f;
        B(t, "Frame", face + Vector3.up * (h / 2f), Abs(along * (w + f * 2) + Vector3.up * f + normal * 0.07f), Timber, 1f, false);
        B(t, "Frame", face - Vector3.up * (h / 2f) + normal * 0.03f, Abs(along * (w + f * 2.6f) + Vector3.up * f + normal * 0.13f), Timber, 1f, false);
        B(t, "Frame", face + along * (w / 2f), Abs(along * f + Vector3.up * h + normal * 0.07f), Timber, 1f, false);
        B(t, "Frame", face - along * (w / 2f), Abs(along * f + Vector3.up * h + normal * 0.07f), Timber, 1f, false);
        if (glass)
        {
            B(t, "Glass", center + normal * 0.01f, Abs(along * w + Vector3.up * h + normal * 0.01f), Glass, 1f, false);
            B(t, "Mullion", face, Abs(along * 0.05f + Vector3.up * h + normal * 0.05f), Timber, 1f, false);
            B(t, "Mullion", face + Vector3.up * (h * 0.1f), Abs(along * w + Vector3.up * 0.05f + normal * 0.05f), Timber, 1f, false);
        }
        if (shutters)
            foreach (float s in new[] { -1f, 1f })
                B(t, "Shutter", face + along * s * (w / 2f + f + w * 0.27f), Abs(along * (w * 0.5f) + Vector3.up * h + normal * 0.05f), DoorWood, 1f, false);
        if (flowers)
        {
            var box = face - Vector3.up * (h / 2f + 0.16f) + normal * 0.16f;
            B(t, "FlowerBox", box, Abs(along * (w + 0.15f) + Vector3.up * 0.2f + normal * 0.24f), Timber, 1f, false);
            int k = rng.Next(4);
            for (float u = -w / 2f + 0.06f; u <= w / 2f; u += 0.16f)
                B(t, "Flowers", box + along * u + Vector3.up * R(0.12f, 0.2f), Vector3.one * R(0.12f, 0.18f), Flower(k + (rng.NextDouble() < 0.3 ? 1 : 0)), 1f, false);
        }
        if (rng.NextDouble() < 0.55)
            NightLights.Window(t, t.TransformPoint(center + normal * 0.022f), t.TransformDirection(normal), w, h, night);
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>Wrought-iron lantern on a bracket beside a door (lit at night, with its halo).</summary>
    private static void WallLantern(Transform t, Vector3 at, Vector3 normal)
    {
        B(t, "Bracket", at + normal * 0.25f + Vector3.up * 0.25f, Abs(normal * 0.5f) + new Vector3(0.05f, 0.05f, 0.05f), Iron, 1f, false);
        var c = at + normal * 0.45f;
        B(t, "LanternTop", c + Vector3.up * 0.2f, new Vector3(0.26f, 0.06f, 0.26f), Iron, 1f, false);
        B(t, "LanternBase", c - Vector3.up * 0.17f, new Vector3(0.2f, 0.04f, 0.2f), Iron, 1f, false);
        NightLights.Lamp(t, t.TransformPoint(c), new Vector3(0.16f, 0.3f, 0.16f), night, 2.6f);
        // the warm glow it throws on the wall around it
        NightLights.Pool(t, t.TransformPoint(at + normal * 0.03f), t.TransformDirection(normal), 1.8f, night);
    }

    // ------------------------------------------------------------------ houses

    /// <summary>
    /// A half-timbered house at pos, its front (gable, door) towards -Z turned by yaw, w wide and d deep.
    /// Enterable houses have open doors, a wooden floor, a staircase along the right wall and an upper
    /// floor with windows; the others are closed with dressed windows and doors. Jettied houses lean
    /// their upper floors out over the street.
    /// </summary>
    private static void House(Vector3 pos, float yaw, float w, float d, int floors, int plaster, int roof, bool enterable, bool jetty, string name = "House")
    {
        var t = Node(name, root, pos, yaw);
        float hw = w / 2f, hd = d / 2f;
        float plinth = enterable ? 0.45f : 0.25f;
        var wallMat = Plaster(plaster);
        taken.Add(Footprint(pos, yaw, w + 2f, d + 2f));
        B(t, "Plinth", new Vector3(0, plinth / 2f, 0), new Vector3(w + 0.24f, plinth, d + 0.24f), Stone, 2f);
        float y = plinth;
        float doorAt = R(-0.2f, 0.2f) * w;
        // front steps up to the door
        int steps = enterable ? 2 : 1;
        for (int s = 0; s < steps; s++)
        {
            float stepTop = plinth * (steps - s) / (steps + 1f);
            B(t, "Step", new Vector3(doorAt, stepTop / 2f, -hd - 0.12f - 0.35f * s - 0.175f), new Vector3(1.6f + s * 0.3f, stepTop, 0.35f), Stone, 1.5f);
        }

        // ---- ground floor: stone
        if (enterable)
        {
            WallStrip(t, true, -hd + Wall / 2f, y, FloorH, -hw, hw, Openings(w, 1.3f, doorAt), Stone);
            WallStrip(t, true, hd - Wall / 2f, y, FloorH, -hw, hw, Openings(w, 1.2f, -doorAt), Stone);
            WallStrip(t, false, -hw + Wall / 2f, y, FloorH, -hd + Wall, hd - Wall, Openings(d - 2 * Wall, null, 0f), Stone);
            WallStrip(t, false, hw - Wall / 2f, y, FloorH, -hd + Wall, hd - Wall, Openings(d - 2 * Wall, null, 0f), Stone);
            B(t, "Floor", new Vector3(0, y + 0.02f, 0), new Vector3(w - 2 * Wall, 0.04f, d - 2 * Wall), Boards, 2f);
            // an open door leaf against the wall inside
            B(t, "DoorLeaf", new Vector3(doorAt + 0.62f, y + 1.15f, -hd + Wall + 0.62f), new Vector3(0.06f, 2.3f, 1.2f), DoorWood, 1f, false);
            // stairs along the right wall from the front to the upper floor
            float sx = hw - Wall - 0.6f, sz = -hd + Wall + 0.9f;
            Buildings.Stairs(t, t.TransformPoint(new Vector3(sx, y, sz)), t.TransformDirection(Vector3.forward), FloorH, 1.1f, Timber);
            float run = Mathf.CeilToInt(FloorH / 0.2f) * 0.36f;
            var hole = Rect.MinMaxRect(hw - Wall - 1.25f, sz + 0.6f, hw - Wall, sz + run + 0.1f);
            float uy = y + FloorH;
            Slab(t, uy, -hw + Wall, hw - Wall, -hd + Wall, hd - Wall, hole, Boards);
            B(t, "StairRail", new Vector3(hole.xMin - 0.04f, uy + 0.5f, hole.center.y), new Vector3(0.08f, 1f, hole.height), Timber, 1f);
            // furniture: a table, a cupboard, barrels (cover inside)
            B(t, "Table", new Vector3(-hw + 1.6f, y + 0.4f, 0), new Vector3(1.6f, 0.8f, 0.9f), DoorWood, 1f);
            B(t, "Cupboard", new Vector3(-hw + 0.6f, uy + 1f, hd - 1.4f), new Vector3(0.6f, 2f, 1.4f), DoorWood, 1f);
            // upper floor: plaster walls with windows
            WallStrip(t, true, -hd + Wall / 2f, uy, FloorH, -hw, hw, Openings(w, null, 0f), wallMat);
            WallStrip(t, true, hd - Wall / 2f, uy, FloorH, -hw, hw, Openings(w, null, 0f), wallMat);
            WallStrip(t, false, -hw + Wall / 2f, uy, FloorH, -hd + Wall, hd - Wall, Openings(d - 2 * Wall, null, 0f), wallMat);
            WallStrip(t, false, hw - Wall / 2f, uy, FloorH, -hd + Wall, hd - Wall, Openings(d - 2 * Wall, null, 0f), wallMat);
            B(t, "Ceiling", new Vector3(0, uy + FloorH - 0.06f, 0), new Vector3(w, 0.12f, d), Boards, 2f);
            DressFloor(t, y, FloorH, w, d, Stone, Openings(w, 1.3f, doorAt), Openings(w, 1.2f, -doorAt), Openings(d - 2 * Wall, null, 0f), false, false);
            DressFloor(t, uy, FloorH, w, d, wallMat, Openings(w, null, 0f), Openings(w, null, 0f), Openings(d - 2 * Wall, null, 0f), true, false);
            floors = 2;
        }
        else
        {
            B(t, "GroundFloor", new Vector3(0, y + FloorH / 2f, 0), new Vector3(w, FloorH, d), Stone, 2f);
            var front = Openings(w, 1.2f, doorAt);
            DressFloor(t, y, FloorH, w, d, Stone, front, Openings(w, null, 0f), Openings(d, null, 0f), false, true);
            // the closed front door
            B(t, "Door", new Vector3(doorAt, y + 1.15f, -hd - 0.03f), new Vector3(1.2f, 2.3f, 0.08f), DoorWood, 1f, false);
            B(t, "DoorArch", new Vector3(doorAt, y + 2.4f, -hd - 0.05f), new Vector3(1.5f, 0.2f, 0.12f), Timber, 1f, false);
            float over = 0f;
            for (int k = 1; k < floors; k++)
            {
                float fy = y + FloorH * k;
                float lean = jetty ? 0.35f * k : 0f;
                // jettied upper floors: oak brackets under the overhang
                if (jetty)
                    for (float u = -hw + 0.4f; u <= hw - 0.3f; u += (w - 0.8f) / 3f)
                        Beam(t, new Vector3(u, fy - 0.7f, -hd - over), new Vector3(u, fy - 0.05f, -hd - lean), Vector3.right, 0.16f);
                B(t, "UpperFloor", new Vector3(0, fy + FloorH / 2f, -lean / 2f), new Vector3(w, FloorH, d + lean), wallMat, 2f);
                DressFloorAt(t, fy, FloorH, w, d + lean, -lean / 2f, wallMat, k);
                over = lean;
            }
        }

        // ---- roof: steep, gable to the street
        float top = y + FloorH * floors;
        float lastLean = !enterable && jetty ? 0.35f * (floors - 1) : 0f;
        float rise = w * 0.72f;
        float roofDepth = d + lastLean;
        var roofGo = MeshGen.Object("Roof", MeshGen.GableRoof(w, roofDepth, rise, 0.45f, 2f), Roof(roof), t, t.TransformPoint(new Vector3(0, top, -lastLean / 2f)), t.eulerAngles.y);
        roofGo.AddComponent<MeshCollider>().sharedMesh = roofGo.GetComponent<MeshFilter>().sharedMesh;
        foreach (float s in new[] { -1f, 1f })
        {
            float gz = s < 0 ? -hd - lastLean + 0.02f : hd - 0.02f;
            var gable = MeshGen.Object("Gable", MeshGen.Gable(w, rise, 2f), wallMat, t, t.TransformPoint(new Vector3(0, top, gz)), t.eulerAngles.y);
            var n = new Vector3(0, 0, s);
            // the gable's timbers: king post, two struts and a collar beam
            Beam(t, new Vector3(0, top, gz), new Vector3(0, top + rise - 0.15f, gz), n);
            Beam(t, new Vector3(-w * 0.32f, top, gz), new Vector3(0, top + rise * 0.55f, gz), n, 0.16f);
            Beam(t, new Vector3(w * 0.32f, top, gz), new Vector3(0, top + rise * 0.55f, gz), n, 0.16f);
            Beam(t, new Vector3(-w * 0.27f, top + rise * 0.45f, gz), new Vector3(w * 0.27f, top + rise * 0.45f, gz), n, 0.16f);
            // an attic window in the front gable
            if (s < 0) Window(t, new Vector3(0, top + rise * 0.28f, gz - 0.05f), n, Vector3.right, 0.7f, 0.8f, true, false, false);
        }
        // chimney
        float cx = hw * R(-0.5f, 0.5f);
        B(t, "Chimney", new Vector3(cx, top + rise * 0.7f, hd * 0.35f), new Vector3(0.7f, rise * 0.9f + 1.2f, 0.7f), Stone, 1.5f);
        B(t, "ChimneyCap", new Vector3(cx, top + rise * 1.15f + 0.6f, hd * 0.35f), new Vector3(0.85f, 0.12f, 0.85f), Stone, 1f, false);
        WallLantern(t, new Vector3(doorAt + 1.05f, y + 2.1f, -hd), Vector3.back);
    }

    private static Rect Footprint(Vector3 pos, float yaw, float w, float d)
    {
        var e = Quaternion.Euler(0, yaw, 0) * new Vector3(w, 0, d);
        float ex = Mathf.Abs(e.x) / 2f, ez = Mathf.Abs(e.z) / 2f;
        return Rect.MinMaxRect(pos.x - ex, pos.z - ez, pos.x + ex, pos.z + ez);
    }

    private static void Slab(Transform t, float y, float x0, float x1, float z0, float z1, Rect hole, Material m)
    {
        B(t, "Slab", new Vector3((x0 + hole.xMin) / 2f, y - 0.1f, (z0 + z1) / 2f), new Vector3(hole.xMin - x0, 0.2f, z1 - z0), m, 2f);
        B(t, "Slab", new Vector3((hole.xMax + x1) / 2f, y - 0.1f, (z0 + z1) / 2f), new Vector3(x1 - hole.xMax, 0.2f, z1 - z0), m, 2f);
        B(t, "Slab", new Vector3(hole.center.x, y - 0.1f, (z0 + hole.yMin) / 2f), new Vector3(hole.width, 0.2f, hole.yMin - z0), m, 2f);
        B(t, "Slab", new Vector3(hole.center.x, y - 0.1f, (hole.yMax + z1) / 2f), new Vector3(hole.width, 0.2f, z1 - hole.yMax), m, 2f);
    }

    /// <summary>Frames, timbers and dressing on the four faces of one floor (openings as cut in the walls).</summary>
    private static void DressFloor(Transform t, float y, float h, float w, float d, Material m,
        List<(float c, float w, float b, float top)> front, List<(float c, float w, float b, float top)> back, List<(float c, float w, float b, float top)> sides, bool timbered, bool closed)
    {
        float hw = w / 2f, hd = d / 2f;
        foreach (var (normal, along, list, half) in new[] { (Vector3.back, Vector3.right, front, hd), (Vector3.forward, Vector3.right, back, hd), (Vector3.left, Vector3.forward, sides, hw), (Vector3.right, Vector3.forward, sides, hw) })
        {
            float length = along == Vector3.right ? w : d;
            foreach (var o in list)
            {
                if (o.b < 0.01f) continue;   // doors
                var c = normal * half + along * o.c + Vector3.up * (y + (o.b + o.top) / 2f);
                Window(t, c, normal, along, o.w, o.top - o.b, closed, closed && rng.NextDouble() < 0.5, rng.NextDouble() < 0.35);
            }
            if (timbered) Timbers(t, normal * half, normal, along, length, y, h, list);
        }
    }

    private static void DressFloorAt(Transform t, float y, float h, float w, float d, float zShift, Material m, int k)
    {
        var front = Openings(w, null, 0f);
        var sides = Openings(d, null, 0f);
        float hw = w / 2f, hd = d / 2f;
        foreach (var (normal, along, list, half) in new[] { (Vector3.back, Vector3.right, front, hd), (Vector3.forward, Vector3.right, front, hd), (Vector3.left, Vector3.forward, sides, hw), (Vector3.right, Vector3.forward, sides, hw) })
        {
            var origin = normal * half + Vector3.forward * zShift;
            float length = along == Vector3.right ? w : d;
            foreach (var o in list)
                Window(t, origin + along * o.c + Vector3.up * (y + (o.b + o.top) / 2f), normal, along, o.w, o.top - o.b, true, rng.NextDouble() < 0.45, k == 1 && rng.NextDouble() < 0.45);
            Timbers(t, origin, normal, along, length, y, h, list);
        }
    }

    /// <summary>Half-timbering on one face: sill and head rails, corner and middle posts, braces in the plain panels.</summary>
    private static void Timbers(Transform t, Vector3 faceCenter, Vector3 normal, Vector3 along, float length, float y, float h, List<(float c, float w, float b, float top)> openings)
    {
        var o0 = faceCenter + Vector3.up * y;
        Beam(t, o0 - along * (length / 2f) + Vector3.up * 0.1f, o0 + along * (length / 2f) + Vector3.up * 0.1f, normal, 0.2f);
        Beam(t, o0 - along * (length / 2f) + Vector3.up * (h - 0.1f), o0 + along * (length / 2f) + Vector3.up * (h - 0.1f), normal, 0.2f);
        // posts on each side of every window and at the corners
        var posts = new List<float> { -length / 2f + 0.1f, length / 2f - 0.1f };
        foreach (var o in openings) { posts.Add(o.c - o.w / 2f - 0.12f); posts.Add(o.c + o.w / 2f + 0.12f); }
        posts.Sort();
        foreach (float u in posts) Beam(t, o0 + along * u, o0 + along * u + Vector3.up * h, normal);
        // braces in the panels with no window, a rail under the windows
        for (int i = 0; i + 1 < posts.Count; i++)
        {
            float a = posts[i], b = posts[i + 1];
            if (b - a < 0.6f) continue;
            bool window = openings.Any(o => o.c > a && o.c < b);
            if (window)
                Beam(t, o0 + along * a + Vector3.up * 0.9f, o0 + along * b + Vector3.up * 0.9f, normal, 0.16f);
            else if (b - a > 0.9f)
            {
                bool flip = (i % 2) == 0;
                Beam(t, o0 + along * (flip ? a : b) + Vector3.up * 0.2f, o0 + along * (flip ? b : a) + Vector3.up * (h - 0.25f), normal, 0.16f);
            }
        }
    }

    // ------------------------------------------------------------------ town furniture

    private static void StreetLamp(Vector3 p)
    {
        var t = Node("StreetLamp", root, p, 0);
        MeshGen.TexCylinder("LampPost", p, 0.07f, 3.4f, Iron, 1f, t);
        B(t, "LampFoot", new Vector3(0, 0.15f, 0), new Vector3(0.3f, 0.3f, 0.3f), Iron, 1f);
        B(t, "LampTop", new Vector3(0, 3.78f, 0), new Vector3(0.36f, 0.08f, 0.36f), Iron, 1f, false);
        B(t, "LampBase", new Vector3(0, 3.42f, 0), new Vector3(0.26f, 0.05f, 0.26f), Iron, 1f, false);
        NightLights.Lamp(t, p + Vector3.up * 3.6f, new Vector3(0.22f, 0.32f, 0.22f), night, 3.2f);
    }

    private static void Barrel(Transform t, Vector3 p) => MeshGen.TexCylinder("Barrel", p, 0.32f, 0.9f, DoorWood, 1f, t);

    private static void Cart(Vector3 p, float yaw)
    {
        var t = Node("Cart", root, p, yaw);
        B(t, "CartBed", new Vector3(0, 0.75f, 0), new Vector3(1.4f, 0.12f, 2.6f), DoorWood, 1f);
        B(t, "CartSide", new Vector3(-0.68f, 1.05f, 0), new Vector3(0.06f, 0.5f, 2.6f), DoorWood, 1f);
        B(t, "CartSide", new Vector3(0.68f, 1.05f, 0), new Vector3(0.06f, 0.5f, 2.6f), DoorWood, 1f);
        foreach (float s in new[] { -1f, 1f })
        {
            var wheel = MeshGen.Object("Wheel", MeshGen.Cylinder(0.5f, 0.1f, 1f, 16), Timber, t, t.TransformPoint(new Vector3(s * 0.78f, 0.5f, 0.2f)));
            wheel.transform.rotation = t.rotation * Quaternion.Euler(0, 0, 90);
        }
        Beam(t, new Vector3(0, 0.75f, -1.3f), new Vector3(0, 0.45f, -2.6f), Vector3.up, 0.1f);
        B(t, "HayLoad", new Vector3(0, 1.1f, 0.3f), new Vector3(1.2f, 0.6f, 1.6f), Hay, 1f);
    }

    private static void Stall(Vector3 p, float yaw, int color)
    {
        var t = Node("Stall", root, p, yaw);
        foreach (var (x, z) in new[] { (-1.4f, -0.9f), (1.4f, -0.9f), (-1.4f, 0.9f), (1.4f, 0.9f) })
            B(t, "Post", new Vector3(x, 1.2f, z), new Vector3(0.12f, 2.4f, 0.12f), Timber, 1f);
        B(t, "Counter", new Vector3(0, 0.5f, -0.6f), new Vector3(2.9f, 1f, 0.8f), DoorWood, 1f);
        var canopy = B(t, "Canopy", new Vector3(0, 2.5f, 0), new Vector3(3.2f, 0.08f, 2.4f), Cloth(color), 1f, false);
        canopy.transform.rotation = t.rotation * Quaternion.Euler(-12f, 0, 0);
        for (int i = 0; i < 4; i++)
            B(t, "Goods", new Vector3(-1f + i * 0.65f, 1.1f, -0.65f), new Vector3(0.45f, 0.2f, 0.4f), Flower(i + color), 1f, false);
        Barrel(t, t.TransformPoint(new Vector3(1.9f, 0, 0.6f)));
    }

    private static void Fence(Vector3 a, Vector3 b)
    {
        var t = Node("Fence", root, a, 0);
        var dir = b - a;
        float len = dir.magnitude;
        var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        t.rotation = Quaternion.Euler(0, yaw, 0);
        for (float u = 0; u <= len; u += 0.5f)
            B(t, "Picket", new Vector3(0, 0.5f, u), new Vector3(0.08f, 1f, 0.06f), DoorWood, 1f, u % 2f < 0.01f);
        B(t, "Rail", new Vector3(0, 0.3f, len / 2f), new Vector3(0.05f, 0.08f, len), DoorWood, 1f);
        B(t, "Rail", new Vector3(0, 0.75f, len / 2f), new Vector3(0.05f, 0.08f, len), DoorWood, 1f);
    }

    private static Mesh Spire()
    {
        if (spire != null && AssetDatabase.Contains(spire)) return spire;
        var v = new List<Vector3>();
        var tri = new List<int>();
        var tip = new Vector3(0, 1, 0);
        var c = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
        for (int i = 0; i < 4; i++)
        {
            int k = v.Count;
            v.Add(c[i]); v.Add(tip); v.Add(c[(i + 1) % 4]);
            tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
        }
        spire = new Mesh { name = "Spire" };
        spire.SetVertices(v);
        spire.SetUVs(0, v.Select(p => new Vector2(p.x + p.z, p.y) * 2f).ToList());
        spire.SetTriangles(tri, 0);
        spire.RecalculateNormals();
        spire.RecalculateBounds();
        AssetDatabase.CreateAsset(spire, $"{AssetSetup.Generated}/Meshes/Spire.asset");
        return spire;
    }

    /// <summary>The church: a stone nave with a steep roof and a tower whose stairs lead to the bell chamber lookout.</summary>
    private static void Church(Vector3 pos)
    {
        var t = Node("Church", root, pos, 0);
        taken.Add(Rect.MinMaxRect(pos.x - 9f, pos.z - 6f, pos.x + 9f, pos.z + 30f));
        B(t, "Nave", new Vector3(0, 4.5f, 16f), new Vector3(12f, 9f, 22f), Stone, 2.5f);
        var roof = MeshGen.Object("NaveRoof", MeshGen.GableRoof(12f, 22f, 7f, 0.5f, 2f), Roof(1), t, t.TransformPoint(new Vector3(0, 9f, 16f)));
        roof.AddComponent<MeshCollider>().sharedMesh = roof.GetComponent<MeshFilter>().sharedMesh;
        MeshGen.Object("NaveGable", MeshGen.Gable(12f, 7f, 2f), Stone, t, t.TransformPoint(new Vector3(0, 9f, 27f)));
        // tall arched windows on the long sides, lit at night
        foreach (float s in new[] { -1f, 1f })
            for (float z = 8f; z <= 24f; z += 4f)
            {
                B(t, "ChurchWindow", new Vector3(s * 6.02f, 4.6f, z), new Vector3(0.06f, 3.4f, 1.1f), Glass, 1f, false);
                NightLights.Window(t, t.TransformPoint(new Vector3(s * 6.06f, 4.6f, z)), t.TransformDirection(new Vector3(s, 0, 0)), 1.1f, 3.4f, night);
            }
        // the tower: a climbable building with a lookout under a spire
        var style = new Buildings.Style { Outer = Stone, Inner = Plaster(0), Floor = Boards, Stairs = Stone, Trim = Stone, Furniture = DoorWood };
        var tower = Buildings.Build(t, "ChurchTower", pos + new Vector3(0, 0, 0f), 0f, 7.6f, 7.6f, 5, style);
        float roofY = 5 * 3.4f;
        foreach (var (x, z) in new[] { (-3.4f, -3.4f), (3.4f, -3.4f), (-3.4f, 3.4f), (3.4f, 3.4f) })
            B(t, "BelfryPillar", new Vector3(x, roofY + 1.6f, z), new Vector3(0.6f, 3.2f, 0.6f), Stone, 1f);
        B(t, "BelfryRoof", new Vector3(0, roofY + 3.3f, 0), new Vector3(8.2f, 0.25f, 8.2f), Stone, 2f);
        var sp = MeshGen.Object("Spire", Spire(), Roof(0), t, t.TransformPoint(new Vector3(0, roofY + 3.42f, 0)));
        sp.transform.localScale = new Vector3(8.4f, 9f, 8.4f);
        MeshGen.TexCylinder("Bell", t.TransformPoint(new Vector3(-2f, roofY + 2f, 2f)), 0.45f, 0.8f, AssetSetup.Solid("T_Bronze", new Color(0.55f, 0.4f, 0.2f), 0.7f, 0.9f), 1f, t, false);
        WallLantern(t, new Vector3(-1.6f, 2.4f, -3.8f), Vector3.back);
        WallLantern(t, new Vector3(1.6f, 2.4f, -3.8f), Vector3.back);
    }

    private static void Well(Vector3 p)
    {
        var t = Node("Well", root, p, 0);
        MeshGen.TexCylinder("WellRing", p, 1.2f, 0.9f, Stone, 1.5f, t);
        MeshGen.TexCylinder("WellWater", p + Vector3.up * 0.7f, 1.0f, 0.22f, Water, 2f, t, false);
        foreach (float s in new[] { -1f, 1f })
            B(t, "WellPost", new Vector3(s * 1.05f, 1.6f, 0), new Vector3(0.14f, 1.6f, 0.14f), Timber, 1f);
        MeshGen.Object("WellRoof", MeshGen.GableRoof(2.6f, 1.6f, 1f, 0.15f, 1f), Roof(0), t, p + Vector3.up * 2.4f, 90f);
        B(t, "WellBeam", new Vector3(0, 2.25f, 0), new Vector3(2.2f, 0.12f, 0.12f), Timber, 1f, false);
        StreetLamp(p + new Vector3(2.4f, 0, 2.4f));
    }

    private static void Windmill(Vector3 p)
    {
        var t = Node("Windmill", root, p, 30f);
        taken.Add(Rect.MinMaxRect(p.x - 5f, p.z - 5f, p.x + 5f, p.z + 5f));
        MeshGen.TexCylinder("MillTower", p, 3f, 9f, Plaster(2), 2f, t);
        var cap = MeshGen.Object("MillCap", Spire(), Roof(2), t, p + Vector3.up * 9f);
        cap.transform.localScale = new Vector3(6.6f, 3f, 6.6f);
        var hub = t.TransformPoint(new Vector3(0, 9.4f, -3.4f));
        for (int i = 0; i < 4; i++)
        {
            var sail = MeshGen.TexBox("Sail", hub, new Vector3(1.4f, 6.5f, 0.06f), Cloth(i % 2 == 0 ? 2 : 0), 1f, t, 0f, false);
            sail.transform.rotation = t.rotation * Quaternion.Euler(0, 0, i * 90f + 20f);
            sail.transform.position = hub + sail.transform.up * 3.4f;
        }
        B(t, "MillDoor", new Vector3(0, 1.1f, -3.02f), new Vector3(1.1f, 2.2f, 0.1f), DoorWood, 1f, false);
        WallLantern(t, new Vector3(0.9f, 2.2f, -3.02f), Vector3.back);
    }

    private static void BarnAndFields(Vector3 p)
    {
        var t = Node("Barn", root, p, 90f);
        taken.Add(Rect.MinMaxRect(p.x - 8f, p.z - 7f, p.x + 8f, p.z + 7f));
        const float w = 10f, d = 12f, h = 4.5f;
        // three plank walls, open to the yard, hay bales and a loft
        B(t, "BarnWall", new Vector3(0, h / 2f, d / 2f), new Vector3(w, h, 0.2f), Barn, 2f);
        B(t, "BarnWall", new Vector3(-w / 2f, h / 2f, 0), new Vector3(0.2f, h, d), Barn, 2f);
        B(t, "BarnWall", new Vector3(w / 2f, h / 2f, 0), new Vector3(0.2f, h, d), Barn, 2f);
        B(t, "BarnWallFront", new Vector3(-3.5f, h / 2f, -d / 2f), new Vector3(3f, h, 0.2f), Barn, 2f);
        B(t, "BarnWallFront", new Vector3(3.5f, h / 2f, -d / 2f), new Vector3(3f, h, 0.2f), Barn, 2f);
        var roof = MeshGen.Object("BarnRoof", MeshGen.GableRoof(w, d, 4f, 0.5f, 2f), Roof(2), t, t.TransformPoint(new Vector3(0, h, 0)), t.eulerAngles.y);
        roof.AddComponent<MeshCollider>().sharedMesh = roof.GetComponent<MeshFilter>().sharedMesh;
        foreach (float s in new[] { -1f, 1f })
            MeshGen.Object("BarnGable", MeshGen.Gable(w, 4f, 2f), Barn, t, t.TransformPoint(new Vector3(0, h, s * (d / 2f - 0.05f))), t.eulerAngles.y);
        B(t, "Loft", new Vector3(0, 2.6f, 3f), new Vector3(w - 0.4f, 0.15f, 5.6f), Boards, 2f);
        for (int i = 0; i < 6; i++)
            B(t, "HayBale", new Vector3(R(-3.8f, 3.8f), 0.45f, R(-4f, 0f)), new Vector3(1.2f, 0.9f, 0.8f), Hay, 1f);
        WallLantern(t, new Vector3(-1.8f, 3f, -d / 2f - 0.1f), Vector3.back);
        // fenced paddock and wheat field next to it
        var a = p + new Vector3(-8f, 0, -16f);
        Fence(a, a + new Vector3(16f, 0, 0));
        Fence(a, a + new Vector3(0, 0, 8f));
        Fence(a + new Vector3(16f, 0, 0), a + new Vector3(16f, 0, 8f));
        for (int i = 0; i < 40; i++)
            Nature.Place(Nature.Ground[i % 2], a + new Vector3(R(1f, 15f), 0, R(1f, 7f)), R(0, 360), R(0.8f, 1.2f), root);
    }

    // ------------------------------------------------------------------ the town

    public static MapLayout Build()
    {
        rng = new System.Random(31);
        night.Clear();
        Buildings.Night = night;
        taken.Clear();
        root = new GameObject("Map_OldTown").transform;

        // ground with the canal cut out of it, cobbled streets, the square
        var canal = Rect.MinMaxRect(CanalX0, -260f, CanalX1, 260f);
        foreach (var r in Underground.Cover(Rect.MinMaxRect(-260f, -260f, 260f, 260f), new[] { canal }))
            MeshGen.TexBox("Ground", new Vector3(r.center.x, -0.5f, r.center.y), new Vector3(r.width, 1f, r.height), Grass, 4f, root);
        MeshGen.TexBox("CanalBed", new Vector3((CanalX0 + CanalX1) / 2f, -1.1f, 0), new Vector3(CanalX1 - CanalX0, 0.4f, 520f), Stone, 2f, root);
        var water = MeshGen.TexBox("CanalWater", new Vector3((CanalX0 + CanalX1) / 2f, -0.55f, 0), new Vector3(CanalX1 - CanalX0, 0.02f, 520f), Water, 6f, root, 0f, false);
        water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (float x in new[] { CanalX0 - 0.25f, CanalX1 + 0.25f })
            MeshGen.TexBox("CanalWall", new Vector3(x, -0.4f, 0), new Vector3(0.5f, 1.2f, 520f), Stone, 2f, root, 0f, false);
        // steps down into the canal (cover in the water)
        foreach (float z in new[] { -22f, 26f })
        {
            Underground.Wedge(root, "CanalSteps", new Vector3(CanalX0 + 0.8f, -0.9f, z - 3f), new Vector3(CanalX0 + 0.8f, 0f, z), 1.4f, Stone);
            Underground.Wedge(root, "CanalSteps", new Vector3(CanalX1 - 0.8f, -0.9f, z + 3f), new Vector3(CanalX1 - 0.8f, 0f, z), 1.4f, Stone);
        }
        // stone bridges with low parapets
        foreach (float z in new[] { -42f, 0f, 42f })
        {
            MeshGen.RoundBox("Bridge", new Vector3((CanalX0 + CanalX1) / 2f, 0.1f, z), new Vector3(CanalX1 - CanalX0 + 2f, 0.4f, 5f), 0.08f, Stone, 2f, root);
            foreach (float s in new[] { -1f, 1f })
                MeshGen.RoundBox("BridgeParapet", new Vector3((CanalX0 + CanalX1) / 2f, 0.75f, z + s * 2.3f), new Vector3(CanalX1 - CanalX0 + 2f, 0.9f, 0.4f), 0.08f, Stone, 2f, root);
        }

        var streets = new[]
        {
            Rect.MinMaxRect(-Half - 6f, -4.5f, Half + 6f, 4.5f),      // main street, west to east over the canal
            Rect.MinMaxRect(-4f, -Half - 6f, 4f, 30f),                 // north-south street up to the church
            Rect.MinMaxRect(-18f, -14f, 18f, 14f),                     // the market square
            Rect.MinMaxRect(-44f, -60f, -38f, 60f), Rect.MinMaxRect(38f, -60f, 44f, 60f),
            Rect.MinMaxRect(-60f, -43f, 60f, -37f), Rect.MinMaxRect(-60f, 37f, 60f, 43f),
            Rect.MinMaxRect(-6f, 30f, 6f, 36f),                        // the churchyard
        };
        foreach (var s in streets)
            foreach (var r in Underground.Cover(s, new[] { canal }))
                MeshGen.TexBox("Street", new Vector3(r.center.x, 0.02f, r.center.y), new Vector3(r.width, 0.04f, r.height), Cobbles, 2.5f, root);
        foreach (var s in streets) taken.Add(s);
        taken.Add(canal);

        // landmarks
        Church(new Vector3(0, 0, 40f));
        Well(Vector3.zero);
        for (int i = 0; i < 6; i++)
        {
            float x = -12f + (i % 3) * 12f, z = i < 3 ? -9f : 9f;
            Stall(new Vector3(x, 0, z), i < 3 ? 0f : 180f, i);
        }
        Windmill(new Vector3(-62f, 0, 58f));
        BarnAndFields(new Vector3(-58f, 0, -60f));

        // the tavern on the square (enterable, with a sign, tables and barrels outside)
        House(new Vector3(-27f, 0, 9f), 90f, 10f, 12f, 2, 2, 1, true, false, "Tavern");
        var tav = Node("TavernTerrace", root, new Vector3(-20.5f, 0, 9f), 0);
        for (int i = 0; i < 3; i++)
        {
            B(tav, "Table", new Vector3(0, 0.4f, -4f + i * 4f), new Vector3(1.2f, 0.8f, 1.2f), DoorWood, 1f);
            foreach (float s in new[] { -1f, 1f }) B(tav, "Bench", new Vector3(s * 1.1f, 0.25f, -4f + i * 4f), new Vector3(0.4f, 0.5f, 1.4f), DoorWood, 1f);
        }
        Barrel(tav, new Vector3(-20.5f, 0, 16f));
        Barrel(tav, new Vector3(-19.8f, 0, 16.6f));
        var sign = Node("TavernSign", root, new Vector3(-21.6f, 3.3f, 13f), 90f);
        B(sign, "SignArm", new Vector3(0, 0.2f, -0.6f), new Vector3(0.06f, 0.06f, 1.2f), Iron, 1f, false);
        B(sign, "Sign", new Vector3(0, -0.3f, -0.9f), new Vector3(0.06f, 0.7f, 0.9f), AssetSetup.Solid("T_SignGold", new Color(0.85f, 0.65f, 0.25f), 0.6f, 0.5f), 1f, false);

        // houses along the streets: front to the street, gable up, in many colours and heights
        int index = 0;
        void Row(float from, float to, float lineZ, float facing, bool alongX)
        {
            float u = from;
            while (u < to - 6f)
            {
                float w = R(7.5f, 10.5f), d = R(8.5f, 11f);
                float c = u + w / 2f;
                Vector3 pos = alongX ? new Vector3(c, 0, lineZ + facing * (d / 2f)) : new Vector3(lineZ + facing * (d / 2f), 0, c);
                float yaw = alongX ? (facing > 0 ? 0f : 180f) : (facing > 0 ? 90f : -90f);
                var fp = Footprint(pos, yaw, w, d);
                bool clash = taken.Any(r => r.Overlaps(Rect.MinMaxRect(fp.xMin + 0.3f, fp.yMin + 0.3f, fp.xMax - 0.3f, fp.yMax - 0.3f)));
                if (!clash && Mathf.Abs(pos.x) < Half - 4f && Mathf.Abs(pos.z) < Half - 4f)
                {
                    int floors = rng.NextDouble() < 0.3 ? 3 : 2;
                    bool enter = index % 3 == 0;
                    House(pos, yaw, w, d, floors, rng.Next(PlasterTints.Length), rng.Next(RoofTints.Length), enter, !enter && rng.NextDouble() < 0.4);
                    index++;
                    u += w + R(0.6f, 3.5f);
                }
                else u += 2f;
            }
        }
        // main street: both sides, west and east of the square
        Row(-Half + 4f, -20f, 4.5f + 1.2f, 1f, true);
        Row(-Half + 4f, -20f, -4.5f - 1.2f, -1f, true);
        Row(20f, CanalX0 - 2f, 4.5f + 1.2f, 1f, true);
        Row(20f, CanalX0 - 2f, -4.5f - 1.2f, -1f, true);
        // the north-south street south of the square
        Row(-Half + 4f, -16f, 4f + 1.2f, 1f, false);
        Row(-Half + 4f, -16f, -4f - 1.2f, -1f, false);
        // the ring lanes
        Row(-36f, 36f, -43f - 1f, -1f, true);
        Row(-36f, 36f, -37f + 1f, 1f, true);
        Row(-36f, 36f, 43f + 1f, 1f, true);
        Row(-36f, 36f, 37f - 1f, -1f, true);
        Row(-36f, 36f, -44f - 1f, -1f, false);
        Row(-36f, 36f, -38f + 1f, 1f, false);
        Row(-36f, 36f, 38f - 1f, -1f, false);
        Row(-36f, 36f, 44f + 1f, 1f, false);
        // across the canal: a few houses by the water
        Row(-Half + 6f, -8f, CanalX1 + 3f, 1f, false);
        Row(8f, Half - 6f, CanalX1 + 3f, 1f, false);

        // lamps along the streets, carts and barrels for cover, gardens with trees and fences
        bool Dry(float x) => x < CanalX0 - 1.5f || x > CanalX1 + 1.5f;
        for (float x = -Half + 6f; x <= Half - 4f; x += 15f)
        {
            if (Dry(x)) StreetLamp(new Vector3(x, 0, -4f));
            if (Dry(x + 7.5f) && x + 7.5f <= Half - 4f) StreetLamp(new Vector3(x + 7.5f, 0, 4f));
        }
        for (float z = -Half + 6f; z <= -16f; z += 15f) StreetLamp(new Vector3(3.6f, 0, z));
        foreach (var (x, z) in new[] { (-14f, 0f), (14f, 0f), (0f, 12f), (0f, -12f) }) StreetLamp(new Vector3(x, 0, z));
        foreach (var (x, z) in new[] { (-41f, -20f), (41f, 20f), (-20f, 40f), (20f, -40f) }) StreetLamp(new Vector3(x, 0, z));
        Cart(new Vector3(16f, 0, -2f), 80f);
        Cart(new Vector3(-30f, 0, -2.5f), -100f);
        Cart(new Vector3(40.5f, 0, 30f), 5f);
        Cart(new Vector3(-41f, 0, -30f), 180f);
        var cover = Node("Cover", root, Vector3.zero, 0);
        foreach (var (x, z) in new[] { (-6f, 6f), (7f, -6f), (-40f, 8f), (41f, -12f), (-12f, 38f), (12f, -38f), (-3f, -30f), (2.5f, -52f) })
        {
            Barrel(cover, new Vector3(x, 0, z));
            Barrel(cover, new Vector3(x + 0.7f, 0, z + 0.3f));
            Obstacles.CrateStack(cover, new Vector3(x + 1.6f, 0, z - 0.6f), R(0, 90), rng);
        }
        Obstacles.AmmoCrate(cover, new Vector3(-3f, 0, 18f), 0f);
        Obstacles.AmmoCrate(cover, new Vector3(30f, 0, -3.2f), 90f);
        Obstacles.AmmoCrate(cover, new Vector3(-50f, 0, 3.4f), 90f);
        Obstacles.AmmoCrate(cover, new Vector3(64f, 0, -10f), 0f);
        // gardens: in the free spaces between the houses, trees, bushes and flowers behind picket fences
        int gardens = 0;
        for (float gx = -Half + 10f; gx < Half - 8f; gx += 11f)
            for (float gz = -Half + 10f; gz < Half - 8f; gz += 11f)
            {
                var spot = new Rect(gx - 4f, gz - 4f, 8f, 8f);
                if (taken.Any(r => r.Overlaps(spot))) continue;
                taken.Add(spot);
                var c = new Vector3(gx, 0, gz);
                if (gardens++ % 2 == 0)
                {
                    Fence(c + new Vector3(-3.5f, 0, -3.5f), c + new Vector3(3.5f, 0, -3.5f));
                    Fence(c + new Vector3(-3.5f, 0, -3.5f), c + new Vector3(-3.5f, 0, 3.5f));
                }
                Nature.Tree(Nature.Trees, rng, c + new Vector3(R(-1.5f, 1.5f), 0, R(-1.5f, 1.5f)), 6f, 9f, root);
                for (int i = 0; i < 3; i++)
                    Nature.Place(Nature.Bushes[rng.Next(Nature.Bushes.Length)], c + new Vector3(R(-3f, 3f), 0, R(-3f, 3f)), R(0, 360), R(0.8f, 1.5f), root);
            }
        // a forest and hills around the town (outside the playing area, for the horizon)
        for (int i = 0; i < 110; i++)
        {
            float a = i / 110f * Mathf.PI * 2f, rad = R(Half + 12f, Half + 60f);
            Nature.Tree(rng.NextDouble() < 0.6 ? Nature.Pines : Nature.Trees, rng, new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad), 8f, 14f, root);
        }
        for (int i = 0; i < 14; i++)
        {
            float a = i / 14f * Mathf.PI * 2f, rad = R(Half + 70f, Half + 110f);
            MeshGen.RoundBox("Hill", new Vector3(Mathf.Cos(a) * rad, R(4f, 12f), Mathf.Sin(a) * rad), new Vector3(R(40f, 70f), R(16f, 30f), R(40f, 70f)), 6f, Grass, 8f, root, R(0, 90), false);
        }
        // edges of the playing area: they stop players, not bullets or the camera
        float edge = Half + 6f;
        foreach (var (c, s) in new[] { (new Vector3(0, 15, edge), new Vector3(edge * 2 + 4, 30, 1)), (new Vector3(0, 15, -edge), new Vector3(edge * 2 + 4, 30, 1)),
                                       (new Vector3(edge, 15, 0), new Vector3(1, 30, edge * 2 + 4)), (new Vector3(-edge, 15, 0), new Vector3(1, 30, edge * 2 + 4)) })
        {
            var wall = new GameObject("Edge");
            wall.layer = World.FenceLayer;
            wall.transform.SetParent(root, false);
            wall.transform.position = c;
            wall.AddComponent<BoxCollider>().size = s;
        }

        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            if (tr.gameObject.layer != World.FenceLayer)
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);

        Buildings.Night = null;
        var layout = new MapLayout { Name = "OLD TOWN", Root = root.gameObject, Center = Vector3.zero, Radius = Half };
        BuildNavigation(layout);
        root.gameObject.AddComponent<MapScene>().NightLights = MapOptimizer.Merge(root, "OldTown", null, night).ToArray();
        Debug.Log($"[Town] built: {root.GetComponentsInChildren<Renderer>().Length} renderers, {index} houses, {layout.Waypoints.Length} waypoints, {layout.Spawns.Length} spawns");
        return layout;
    }

    /// <summary>Bot waypoints and spawn points on a grid over the streets, gardens and ground floors.</summary>
    private static void BuildNavigation(MapLayout layout)
    {
        Physics.SyncTransforms();
        var points = new List<Vector3>();
        var index = new Dictionary<Vector2Int, int>();
        const float step = 6f;
        int n = Mathf.FloorToInt((Half - 3f) / step);
        int mask = World.EnvMask | (1 << World.FenceLayer);
        for (int gx = -n; gx <= n; gx++)
            for (int gz = -n; gz <= n; gz++)
            {
                var p = new Vector3(gx * step, 0, gz * step);
                if (!Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var hit, 80f, World.EnvMask)) continue;
                p.y = hit.point.y;
                if (p.y > 0.6f || p.y < -0.2f) continue;   // the ground, streets and ground floors only
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.5f, mask)) continue;
                index[new Vector2Int(gx, gz)] = points.Count;
                points.Add(p);
            }
        var links = new List<Vector2Int>();
        foreach (var pair in index)
            foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) })
            {
                if (!index.TryGetValue(pair.Key + d, out int other)) continue;
                var a = points[pair.Value];
                var b = points[other];
                if (Physics.SphereCast(a + Vector3.up * 1.2f, 0.4f, (b - a).normalized, out _, Vector3.Distance(a, b), mask)) continue;
                links.Add(new Vector2Int(pair.Value, other));
            }
        layout.Waypoints = points.ToArray();
        layout.Links = links.ToArray();
        layout.Spawns = points.ToArray();
    }
}
