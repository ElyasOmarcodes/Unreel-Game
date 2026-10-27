using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEngine;

/// <summary>
/// The hidden ways of STEEL CITY:
///  - a trapdoor in the dark back corner of the open warehouse D-3, behind a stack of crates, down a stair
///    into a clean lit tunnel; one branch comes up inside the closed warehouse C-1 (its doors shut: the
///    tunnel is the only way in) where a steel stair climbs to a hatch on its roof; the other comes up in
///    the open behind warehouse A-4;
///  - stepped wooden crates under the C-5 plate up onto its roof, from there a narrow plank walk along the
///    outer wall of C-4 and an inclined wooden ladder up onto the roof of C-4.
/// All in scene coordinates (the map's root is lifted by Lift).
/// </summary>
public static partial class CityLordBuilder
{
    private static Vector3 W(Vector3 scene) => root.TransformPoint(scene);

    /// <summary>How far a stair of Buildings.Stairs runs for a height (0.2 m steps, 0.36 m treads).</summary>
    private static float RunOf(float height) => Mathf.CeilToInt(height / 0.2f) * 0.36f;

    private static Material TunnelFloor => Mats.Pbr("CL_TunnelFloor", "Concrete034", new Color(0.55f, 0.56f, 0.58f), 0.3f);
    private static Material TunnelWall => Mats.Pbr("CL_TunnelWall", "Concrete034", new Color(0.82f, 0.82f, 0.8f), 0.25f);
    private static Material TunnelTrim => Mats.Pbr("CL_TunnelTrim", "MetalPlates013", new Color(0.45f, 0.42f, 0.38f), 0.35f, 0.4f);
    private static Material CrateWood => Mats.Pbr("CL_CrateWood", "Wood049", new Color(0.78f, 0.62f, 0.44f), 0.3f);
    private static Material Shade => AssetSetup.Solid("CL_Shade", new Color(0.035f, 0.035f, 0.04f), 0.05f);

    /// <summary>A mesh made here lies in scene coordinates when its object sits on the root itself.</summary>
    private static bool SceneSpace(Transform t) =>
        (t.position - root.position).sqrMagnitude < 1e-6f && Quaternion.Angle(t.rotation, root.rotation) < 0.01f && (t.lossyScale - root.lossyScale).sqrMagnitude < 1e-6f;

    /// <summary>Cuts a box (scene coordinates) out of every map mesh and collider there (a hole in the ground or a roof).</summary>
    private static void CutAll(Bounds box)
    {
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            if (f.sharedMesh != null && SceneSpace(f.transform) && f.sharedMesh.bounds.Intersects(box)) f.sharedMesh = Cut(f.sharedMesh, box);
        foreach (var c in root.GetComponentsInChildren<MeshCollider>(true))
            if (c.sharedMesh != null && SceneSpace(c.transform) && c.sharedMesh.bounds.Intersects(box)) c.sharedMesh = Cut(c.sharedMesh, box);
        Physics.SyncTransforms();
    }

    /// <summary>The top of the walkable surface under a point (scene), below 'from', or NaN.</summary>
    private static float Top(Vector3 at, float from)
    {
        float y = Ground(at, from, float.NaN);
        return y;
    }

    private static GameObject Slab(Transform t, string name, Vector3 centre, Vector3 size, Material m, bool collider = true)
    {
        var go = MeshGen.TexBox(name, W(centre), size, m, 1.5f, t, 0f, collider);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    private static void HiddenWays()
    {
        var t = Node("HiddenWays");
        if (!numbered.TryGetValue("D-3", out var d3) || !numbered.TryGetValue("C-1", out var c1) || !numbered.TryGetValue("A-4", out var a4))
        {
            Debug.LogWarning("[Map] CityLord: no D-3 / C-1 / A-4 for the hidden tunnel");
            return;
        }
        float ground = Ground(new Vector3(d3.center.x, 0, d3.center.z), d3.min.y + 1.5f, d3.min.y);
        // the tunnel deep enough under every street on its way (its ceiling 1.4 m under the lowest of them)
        const float width = 3f, height = 2.8f;
        float hx = d3.max.x - 0.75f;                          // the trapdoor in the back corner (the door is on the north side)
        float hTop = d3.min.z + 3.15f, hBottom = d3.min.z + 0.12f;
        float stairRun = 7.6f;
        var route = new List<Vector3>
        {
            new Vector3(hx, 0, hTop - stairRun),
            new Vector3(c1.center.x, 0, hTop - stairRun),
            new Vector3(c1.center.x, 0, c1.min.z - 2f),
            new Vector3(a4.max.x + 3.2f, 0, hTop - stairRun),
        };
        float lowest = route.Select(p => Ground(p, ground + 3f, ground)).Append(ground).Min();
        float ceiling = lowest - 1.4f, floor = ceiling - height;
        float depth = ground - floor;
        stairRun = RunOf(depth);
        float lift = root.position.y;

        // ---- D-3: the trapdoor in the dark corner behind the crates, a stair down to the tunnel
        var hatch = new Bounds(new Vector3(hx, ground, (hTop + hBottom) / 2f), new Vector3(1.3f, 1.2f, hTop - hBottom));
        CutAll(hatch);
        var down = new Vector3(hx, floor, hTop - stairRun);
        Buildings.Stairs(t, W(down), Vector3.forward, depth, 1.25f, TunnelFloor);
        // shaft walls from the tunnel up to the floor of the warehouse
        foreach (float x in new[] { hx - 0.8f, hx + 0.8f })
            Slab(t, "ShaftWall", new Vector3(x, (floor + ground) / 2f, hTop - stairRun / 2f), new Vector3(0.3f, depth, stairRun + 0.4f), TunnelWall);
        Slab(t, "ShaftCeiling", new Vector3(hx, ground - 0.15f, (hTop - stairRun + hBottom) / 2f), new Vector3(1.9f, 0.3f, hBottom - (hTop - stairRun)), TunnelWall);
        // dark floor round it and two stacks of crates that hide it from the door
        Slab(t, "Shade", new Vector3(hx - 0.4f, ground + 0.012f, d3.min.z + 2.4f), new Vector3(2.6f, 0.02f, 4.6f), Shade, false);
        foreach (var (dx, dz, h) in new[] { (-1.35f, 3.75f, 1.8f), (-1.35f, 4.65f, 0.9f), (-2.25f, 3.75f, 0.9f) })
            Slab(t, "HidingCrate", new Vector3(d3.max.x + dx, ground + h / 2f, d3.min.z + dz), new Vector3(0.85f, h, 0.85f), CrateWood);

        // ---- the tunnel: east to below C-1, north into it; west to behind A-4
        var style = new Underground.Style
        {
            Floor = TunnelFloor, Wall = TunnelWall, Ceiling = TunnelWall, Trim = TunnelTrim,
            Lamp = AssetSetup.Mat("CL_TunnelLamp", new Color(1f, 0.92f, 0.75f), 0.5f, 0f, null, null, null, null, new Color(2.2f, 1.9f, 1.4f)),
        };
        float zRun = hTop - stairRun;
        float c1x = c1.max.x - 2.2f;                          // up inside C-1 along its east wall
        float c1Top = c1.min.z + 7.5f;                        // where its stair comes up (inside the walls)
        float a4x = a4.max.x + 3.2f;
        float a4Top = a4.center.z;                            // up in the open behind A-4, at its middle
        var none = new Rect[0];
        var east = Rect.MinMaxRect(hx - width / 2f, zRun - width, c1x + width / 2f, zRun);
        var north = Rect.MinMaxRect(c1x - width / 2f, zRun, c1x + width / 2f, c1Top - stairRun);
        var west = Rect.MinMaxRect(a4x - width / 2f, zRun - width, hx - width / 2f, zRun);
        var westUp = Rect.MinMaxRect(a4x - width / 2f, zRun, a4x + width / 2f, a4Top - stairRun);
        var rooms = new List<(Rect r, (char, float, float)[] open)>
        {
            (east, new[] { ('N', hx - 0.75f, hx + 0.75f), ('N', north.xMin, north.xMax), ('W', east.yMin, east.yMax) }),
            (north, new[] { ('S', north.xMin, north.xMax), ('N', c1x - 0.75f, c1x + 0.75f) }),
            (west, new[] { ('E', west.yMin, west.yMax), ('N', westUp.xMin, westUp.xMax) }),
            (westUp, new[] { ('S', westUp.xMin, westUp.xMax), ('N', a4x - 0.75f, a4x + 0.75f) }),
        };
        foreach (var (r, open) in rooms)
        {
            var sub = new GameObject("Tunnel").transform;
            sub.SetParent(t, false);
            // the rooms are made in world coordinates (the root is only lifted, not moved or turned)
            Underground.Room(sub, r, floor + lift, ceiling + lift, style, none, none, open);
            float length = Mathf.Max(r.width, r.height);
            for (float u = 4f; u < length - 2f; u += 10f)
            {
                var p = r.width >= r.height ? new Vector3(r.xMin + u, ceiling - 0.3f, r.center.y) : new Vector3(r.center.x, ceiling - 0.3f, r.yMin + u);
                Underground.Lamp(t, W(p), 12f, 1.1f);
            }
        }

        // ---- up into C-1 (shut): a stair up through its floor, then a steel stair to a hatch in its roof
        Stairway(t, new Vector3(c1x, 0, c1Top), ground, floor, "C-1");
        float roof = Ground(new Vector3(c1x, 0, c1.center.z), c1.max.y + 1f, float.NaN);
        if (!float.IsNaN(roof) && roof > ground + 3f)
            RoofStair(t, c1, c1x, c1Top + 1.5f, ground, roof);
        else Debug.LogWarning($"[Map] CityLord: no flat roof found over C-1 ({roof})");

        // ---- up behind A-4, in the open
        Stairway(t, new Vector3(a4x, 0, a4Top), ground, floor, "A-4");
        Underground.Railing(t, Rect.MinMaxRect(W(new Vector3(a4x - 0.65f, 0, a4Top - 2.9f)).x, W(new Vector3(0, 0, a4Top - 2.9f)).z,
                                                W(new Vector3(a4x + 0.65f, 0, a4Top)).x, W(new Vector3(0, 0, a4Top)).z), 'N', TunnelWall, W(new Vector3(0, ground, 0)).y);
        Debug.Log($"[Map] CityLord: hidden tunnel from D-3 ({hx:0.0}, {hTop:0.0}) at {floor:0.0}..{ceiling:0.0} (ground {ground:0.0}, lowest street {lowest:0.0}) to C-1 ({c1x:0.0}, {c1Top:0.0}) and behind A-4 ({a4x:0.0}, {a4Top:0.0})");

        ClimbToC4(t);
    }

    /// <summary>A stair up from the tunnel floor northwards, through a hole in the ground ending at 'top'.</summary>
    private static void Stairway(Transform t, Vector3 top, float ground, float floor, string name)
    {
        float depth = ground - floor;
        float run = RunOf(depth);
        var bottom = new Vector3(top.x, floor, top.z - run);
        Buildings.Stairs(t, W(bottom), Vector3.forward, depth, 1.25f, TunnelFloor);
        foreach (float x in new[] { top.x - 0.8f, top.x + 0.8f })
            Slab(t, "StairWall", new Vector3(x, (floor + ground) / 2f, top.z - run / 2f), new Vector3(0.3f, depth, run + 0.4f), TunnelWall);
        // the ceiling over the low part of the stair, open over the last 2.9 m
        Slab(t, "StairCeiling", new Vector3(top.x, ground - 0.15f, top.z - run / 2f - 1.45f), new Vector3(1.9f, 0.3f, run - 2.9f), TunnelWall);
        CutAll(new Bounds(new Vector3(top.x, ground, top.z - 1.45f), new Vector3(1.3f, 1.2f, 2.9f)));
        // the probe: nothing solid left over the opening
        if (Physics.Raycast(W(new Vector3(top.x, ground + 2f, top.z - 1f)), Vector3.down, out var h, 2.6f, World.EnvMask, QueryTriggerInteraction.Ignore))
            Debug.LogWarning($"[Map] CityLord: the stair up to {name} is covered by {h.collider.name} at {root.InverseTransformPoint(h.point)}");
    }

    /// <summary>Inside a building: a steel stair in two lanes along its east wall up to a hatch in its flat roof.</summary>
    private static void RoofStair(Transform t, Bounds b, float x, float z0, float ground, float roof)
    {
        float height = roof - ground;
        int flights = Mathf.Max(2, Mathf.RoundToInt(height / 3.2f));
        float rise = height / flights;
        const float lane = 1.3f, landing = 1.5f, stepRun = 0.36f, stepH = 0.2f;
        int steps = Mathf.CeilToInt(rise / stepH);
        float run = steps * stepRun;
        var steel = Mats.Pbr("CL_StairSteel", "MetalPlates013", new Color(0.78f, 0.76f, 0.7f), 0.35f, 0.2f);   // light enough to read in the dim hall
        float laneX(int k) => b.max.x - 0.6f - lane * (k % 2 + 0.5f);
        for (int k = 0; k < flights; k++)
        {
            bool north = k % 2 == 0;
            float zs = north ? z0 + landing : z0 + landing + run;
            Buildings.Stairs(t, W(new Vector3(laneX(k), ground + k * rise, zs)), north ? Vector3.forward : Vector3.back, rise, lane - 0.08f, steel);
            float zl = north ? z0 + landing + run + landing / 2f : z0 + landing / 2f;
            Slab(t, "Landing", new Vector3(b.max.x - 0.6f - lane, ground + (k + 1) * rise - 0.06f, zl), new Vector3(lane * 2f, 0.12f, landing), steel);
            // a rail on the open side
            var guard = Slab(t, "StairRail", new Vector3(b.max.x - 0.6f - lane * 2f - 0.05f, ground + (k + 1) * rise + 0.5f, z0 + landing + run / 2f), new Vector3(0.05f, 1f, run + landing * 2f), steel);
            guard.GetComponent<Renderer>().enabled = k > 0;
        }
        // the hatch over the last flight and its landing
        bool lastNorth = (flights - 1) % 2 == 0;
        float x0 = laneX(flights - 1) - lane / 2f - 0.1f, x1 = b.max.x - 0.5f;
        float zA = lastNorth ? z0 + landing + run - 3.2f : z0, zB = lastNorth ? z0 + landing * 2f + run : z0 + landing + 3.2f;
        CutAll(new Bounds(new Vector3((x0 + x1) / 2f, roof - 0.3f, (zA + zB) / 2f), new Vector3(x1 - x0, 2.4f, zB - zA)));
        Debug.Log($"[Map] CityLord: stair to the roof of C-1, {flights} flights to {roof:0.0}, hatch x {x0:0.0}..{x1:0.0} z {zA:0.0}..{zB:0.0}");
    }

    /// <summary>
    /// Crates stepped up to the roof of C-5 under its plate; along the outer (east) wall of C-4 a narrow
    /// plank walk at that roof's height; an inclined wooden ladder from it up onto the roof of C-4.
    /// </summary>
    private static void ClimbToC4(Transform t)
    {
        if (!numbered.TryGetValue("C-5", out var c5) || !numbered.TryGetValue("C-4", out var c4)) { Debug.LogWarning("[Map] CityLord: no C-5 / C-4"); return; }
        var plate = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Plate C-5");
        var at = plate != null ? root.InverseTransformPoint(plate.position) : new Vector3(c5.min.x, 0, c5.center.z);
        // a plate faces into its wall (it is looked at from behind its forward)
        var outward = plate != null ? -root.InverseTransformDirection(plate.forward) : Vector3.left;
        outward.y = 0;
        outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.left;
        float ground = Ground(at + outward * 1.5f, c5.min.y + 1.5f, c5.min.y);
        float roof5 = Ground(at - outward * 1.2f, c5.max.y + 1f, float.NaN);
        float roof4 = Ground(new Vector3(c4.max.x - 1.2f, 0, c4.min.z + 6f), c4.max.y + 1f, float.NaN);
        if (float.IsNaN(roof5) || float.IsNaN(roof4) || roof5 < ground + 0.5f) { Debug.LogWarning($"[Map] CityLord: C-5 roof {roof5}, C-4 roof {roof4}: no climb"); return; }
        // stepped crates against the wall under the plate, each about 0.8 m higher than the last
        var along = Vector3.Cross(Vector3.up, outward);
        int n = Mathf.Max(1, Mathf.CeilToInt((roof5 - ground) / 0.8f));
        float stepH = (roof5 - ground) / n;
        var foot = at + outward * 0.55f;
        for (int i = 0; i < n; i++)
        {
            float h = stepH * (i + 1);
            var c = foot - along * (n - 1 - i) * 0.95f;
            var crate = MeshGen.TexBox("ClimbCrate", W(new Vector3(c.x, ground + h / 2f, c.z)), new Vector3(1f, h, 1f), CrateWood, 1f, t, Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg);
            crate.isStatic = true;
        }
        // the plank walk along the east wall of C-4 at the height of C-5's roof
        float wx = Mathf.Max(c4.max.x, c5.max.x) + 0.5f;
        float z0 = c5.max.z - 1.5f, z1 = c4.min.z + 7f;
        Slab(t, "PlankWalk", new Vector3(wx, roof5 - 0.05f, (z0 + z1) / 2f), new Vector3(0.9f, 0.1f, z1 - z0), CrateWood);
        Slab(t, "PlankRail", new Vector3(wx + 0.45f, roof5 + 0.5f, (z0 + z1) / 2f), new Vector3(0.05f, 1f, z1 - z0), CrateWood);
        for (float z = z0 + 0.5f; z < z1; z += 2.5f)
            Slab(t, "Bracket", new Vector3(wx, roof5 - 0.35f, z), new Vector3(0.9f, 0.5f, 0.08f), TunnelTrim, false);
        // the inclined ladder north along the wall up to C-4's roof, a plank across onto it
        if (roof4 > roof5 + 0.4f)
        {
            float rise = roof4 - roof5, len = rise * 1.45f;
            var low = W(new Vector3(wx, roof5, z1));
            var high = W(new Vector3(wx, roof4, z1 + len));
            Underground.Wedge(t, "Ladder", low, high, 0.9f, CrateWood);
            for (float u = 0.3f; u < len; u += 0.35f)
                Slab(t, "Rung", new Vector3(wx, roof5 + rise * u / len + 0.03f, z1 + u), new Vector3(0.95f, 0.05f, 0.06f), TunnelTrim, false);
            Slab(t, "RoofPlank", new Vector3(wx - 0.9f, roof4 + 0.03f, z1 + len + 0.5f), new Vector3(2.2f, 0.08f, 1f), CrateWood);
        }
        Debug.Log($"[Map] CityLord: {n} crates to the roof of C-5 ({roof5:0.0}, ground {ground:0.0}), plank walk at x {wx:0.0} z {z0:0.0}..{z1:0.0}, ladder to C-4's roof ({roof4:0.0})");
    }
}
