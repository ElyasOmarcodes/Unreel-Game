using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Buildings players can walk into: several floors with door and window openings, a two-lane
/// switchback staircase that goes up to every floor and out onto the roof, interior cover and a
/// roof parapet. Also free-standing outdoor staircases (for secret routes onto roofs).
/// </summary>
public static class Buildings
{
    public class Style
    {
        public Material Outer, Inner, Floor, Stairs, Trim, Furniture;
        /// <summary>Dark glass tower: tall ribbon windows with tinted panes (seen from outside only, so those inside see out).</summary>
        public Material Glass;
    }

    private static Mesh paneMesh;

    /// <summary>When set, about half the windows get a warm light at night (see NightLights).</summary>
    public static List<Renderer> Night;
    private static int windowCount;

    /// <summary>A one-sided pane of glass in a window: it faces outwards and is not there from inside (no collider either).</summary>
    private static void Pane(Transform t, Vector3 localCenter, Vector3 outward, float width, float height, Material glass)
    {
        if (paneMesh == null || !UnityEditor.AssetDatabase.Contains(paneMesh))
        {
            paneMesh = new Mesh { name = "GlassPane" };
            paneMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            paneMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            paneMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            paneMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            paneMesh.RecalculateTangents();
            paneMesh.RecalculateBounds();
            AssetSetup.Folder("Assets/Generated/Meshes");
            UnityEditor.AssetDatabase.CreateAsset(paneMesh, "Assets/Generated/Meshes/GlassPane.asset");
        }
        var go = new GameObject("GlassPane");
        go.transform.SetParent(t, false);
        go.transform.localPosition = localCenter;
        // the quad shows its -Z side
        go.transform.localRotation = Quaternion.LookRotation(-outward);
        go.transform.localScale = new Vector3(width, height, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = paneMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = glass;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private const float FloorHeight = 3.4f, Wall = 0.25f, Slab = 0.25f, StepH = 0.2f, StepRun = 0.36f, Lane = 1.4f;

    private static GameObject Box(Transform t, string name, Vector3 localCenter, Vector3 size, Material m, float tex = 2f, float round = 0f)
    {
        if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) return null;
        return round > 0f
            ? MeshGen.RoundBox(name, t.TransformPoint(localCenter), size, round, m, tex, t, t.eulerAngles.y)
            : MeshGen.TexBox(name, t.TransformPoint(localCenter), size, m, tex, t, t.eulerAngles.y);
    }

    /// <summary>A rectangle (x0..x1, z0..z1) at height y with an optional rectangular hole, as up to four boxes.</summary>
    private static void SlabWithHole(Transform t, float y, float x0, float x1, float z0, float z1, Rect? hole, Material m)
    {
        if (hole == null)
        {
            Box(t, "Slab", new Vector3((x0 + x1) / 2f, y - Slab / 2f, (z0 + z1) / 2f), new Vector3(x1 - x0, Slab, z1 - z0), m, 3f);
            return;
        }
        var h = hole.Value;
        Box(t, "Slab", new Vector3((x0 + h.xMin) / 2f, y - Slab / 2f, (z0 + z1) / 2f), new Vector3(h.xMin - x0, Slab, z1 - z0), m, 3f);
        Box(t, "Slab", new Vector3((h.xMax + x1) / 2f, y - Slab / 2f, (z0 + z1) / 2f), new Vector3(x1 - h.xMax, Slab, z1 - z0), m, 3f);
        Box(t, "Slab", new Vector3(h.center.x, y - Slab / 2f, (z0 + h.yMin) / 2f), new Vector3(h.width, Slab, h.yMin - z0), m, 3f);
        Box(t, "Slab", new Vector3(h.center.x, y - Slab / 2f, (h.yMax + z1) / 2f), new Vector3(h.width, Slab, z1 - h.yMax), m, 3f);
    }

    /// <summary>One wall of one floor, from u0 to u1 along the side, with openings (center, width, bottom, top).</summary>
    private static void WallStrip(Transform t, bool alongX, float fixedCoord, float y0, float u0, float u1, List<(float c, float w, float b, float top)> openings, Material m, Material glass = null, float outward = 0f)
    {
        float cursor = u0;
        openings.Sort((a, b) => a.c.CompareTo(b.c));
        Vector3 Pos(float u, float y) => alongX ? new Vector3(u, y, fixedCoord) : new Vector3(fixedCoord, y, u);
        Vector3 Size(float length, float height) => alongX ? new Vector3(length, height, Wall) : new Vector3(Wall, height, length);
        foreach (var o in openings)
        {
            float a = o.c - o.w / 2f, b = o.c + o.w / 2f;
            if (a > cursor) Box(t, "Wall", Pos((cursor + a) / 2f, y0 + FloorHeight / 2f), Size(a - cursor, FloorHeight), m);
            if (o.b > 0.01f) Box(t, "Sill", Pos(o.c, y0 + o.b / 2f), Size(o.w, o.b), m);
            if (o.top < FloorHeight - 0.01f) Box(t, "Lintel", Pos(o.c, y0 + (o.top + FloorHeight) / 2f), Size(o.w, FloorHeight - o.top), m);
            var n = alongX ? new Vector3(0, 0, outward) : new Vector3(outward, 0, 0);
            // tinted glass in the windows (doors stay open), on the outer face of the wall
            if (glass != null && o.b > 0.01f)
                Pane(t, Pos(o.c, y0 + (o.b + o.top) / 2f) + n * (Wall / 2f - 0.03f), n, o.w, o.top - o.b, glass);
            // someone is at home behind every other window at night
            if (Night != null && o.b > 0.01f && outward != 0f && windowCount++ % 2 == 0)
                NightLights.Window(t, t.TransformPoint(Pos(o.c, y0 + (o.b + o.top) / 2f) + n * (Wall / 2f - 0.02f)), t.TransformDirection(n), o.w * 0.96f, o.top - o.b, Night);
            cursor = b;
        }
        if (u1 > cursor) Box(t, "Wall", Pos((cursor + u1) / 2f, y0 + FloorHeight / 2f), Size(u1 - cursor, FloorHeight), m);
    }

    private static List<(float, float, float, float)> Windows(float length, bool door, float doorAt = 0f, bool ribbon = false)
    {
        var list = new List<(float, float, float, float)>();
        if (ribbon)
        {
            // curtain wall: tall panes from knee height almost to the ceiling between narrow mullions
            int panes = Mathf.Max(1, Mathf.FloorToInt((length - 1f) / 2.1f));
            float pitch = (length - 1f) / panes;
            for (int i = 0; i < panes; i++)
            {
                float c = -length / 2f + 0.5f + pitch * (i + 0.5f);
                if (door && Mathf.Abs(c - doorAt) < 1.9f) continue;
                list.Add((c, pitch - 0.3f, 0.45f, 3.05f));
            }
            if (door) list.Add((doorAt, 1.8f, 0f, 2.5f));
            return list;
        }
        int count = Mathf.FloorToInt((length - 2f) / 3.6f);
        float spacing = (length - 2f) / Mathf.Max(1, count);
        for (int i = 0; i < count; i++)
        {
            float c = -length / 2f + 1f + spacing * (i + 0.5f);
            if (door && Mathf.Abs(c - doorAt) < 2.2f) continue;
            list.Add((c, 1.4f, 0.95f, 2.3f));
        }
        if (door) list.Add((doorAt, 1.8f, 0f, 2.5f));
        return list;
    }

    /// <summary>
    /// Balcony outside a door in the front (side -1) or back (side 1) wall: a slab at floor level and
    /// a solid parapet around it, good cover for shooting down into the street.
    /// </summary>
    private static void Balcony(Transform t, float x, float y0, float wallZ, float side, Style s)
    {
        const float width = 3f, depth = 1.4f, rail = 1.05f;
        float z = wallZ + side * depth / 2f;
        Box(t, "Balcony", new Vector3(x, y0 - 0.1f, z), new Vector3(width, 0.2f, depth), s.Trim, 1.5f, 0.05f);
        Box(t, "BalconyRail", new Vector3(x, y0 + rail / 2f, wallZ + side * (depth - 0.05f)), new Vector3(width, rail, 0.1f), s.Trim, 1.5f, 0.04f);
        foreach (float e in new[] { -1f, 1f })
            Box(t, "BalconyRail", new Vector3(x + e * (width / 2f - 0.05f), y0 + rail / 2f, z), new Vector3(0.1f, rail, depth), s.Trim, 1.5f, 0.04f);
        // brackets under the slab
        foreach (float e in new[] { -1f, 1f })
            Box(t, "Bracket", new Vector3(x + e * (width / 2f - 0.3f), y0 - 0.45f, wallZ + side * 0.45f), new Vector3(0.15f, 0.5f, 0.8f), s.Trim, 1f);
    }

    /// <summary>Solid steps going from start (bottom) in direction dir, rising height.</summary>
    public static void Stairs(Transform parent, Vector3 start, Vector3 dir, float height, float width, Material m)
    {
        int steps = Mathf.CeilToInt(height / StepH);
        float stepH = height / steps;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        for (int i = 0; i < steps; i++)
        {
            float top = (i + 1) * stepH;
            var p = start + dir * (StepRun * (i + 0.5f)) + Vector3.up * (top / 2f);
            MeshGen.TexBox("Step", p, new Vector3(width, top, StepRun), m, 1.5f, parent, yaw);
        }
    }

    /// <summary>
    /// Steel fire escape on the outside of a building: two lanes of stairs zig-zag up the facade with
    /// landings at both ends. It ends either on the roof (a plank over the parapet) or on a platform
    /// part of the way up, a sniper perch. facade = point on the wall at street level, along = direction
    /// along the wall, outward = away from the wall. Needs about 8.6 m of wall and 3 m in front of it.
    /// </summary>
    public static void FireEscape(Transform parent, Vector3 facade, Vector3 along, Vector3 outward, float height, bool toRoof, Material steps, Material steel)
    {
        var t = new GameObject(toRoof ? "FireEscape_Roof" : "FireEscape_Perch").transform;
        t.SetParent(parent, false);
        along.y = outward.y = 0;
        along.Normalize();
        outward.Normalize();
        float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
        const float laneWidth = 1.3f, landing = 1.4f, gap = 0.15f;
        int flights = Mathf.Max(1, Mathf.RoundToInt(height / 3.2f));
        float rise = height / flights;
        int count = Mathf.CeilToInt(rise / StepH);
        float run = count * StepRun;
        Vector3 Lane(int lane) => outward * (gap + laneWidth * (lane + 0.5f));
        Vector3 At(float a, float y, float o) => facade + along * a + outward * o + Vector3.up * y;
        float depth = gap + laneWidth * 2;
        for (int k = 0; k < flights; k++)
        {
            bool forward = k % 2 == 0;
            var start = facade + Lane(k % 2) + along * (forward ? landing : landing + run) + Vector3.up * (k * rise);
            Stairs(t, start, forward ? along : -along, rise, laneWidth - 0.08f, steps);
            // landing at the top of this flight (both lanes wide)
            float a = forward ? landing + run + landing / 2f : landing / 2f;
            MeshGen.TexBox("Landing", At(a, (k + 1) * rise - 0.06f, depth / 2f), new Vector3(landing, 0.12f, depth), steps, 1.5f, t, yaw + 90f);
        }
        // outer railing along the whole escape, and at the ends
        float length = landing * 2 + run;
        for (int k = 1; k <= flights; k++)
        {
            // two thin bars to look at, one invisible wall to stop falls
            MeshGen.TexBox("Rail", At(length / 2f, k * rise + 1.0f, depth + 0.03f), new Vector3(0.05f, 0.05f, length), steel, 1f, t, yaw, false);
            MeshGen.TexBox("Rail", At(length / 2f, k * rise + 0.5f, depth + 0.03f), new Vector3(0.04f, 0.04f, length), steel, 1f, t, yaw, false);
            var guard = new GameObject("RailCollider");
            guard.transform.SetParent(t, false);
            guard.transform.SetPositionAndRotation(At(length / 2f, k * rise + 0.5f, depth + 0.03f), Quaternion.Euler(0, yaw, 0));
            guard.AddComponent<BoxCollider>().size = new Vector3(0.06f, 1f, length);
        }
        // posts down to the street
        foreach (float a in new[] { 0.1f, length - 0.1f })
            MeshGen.TexBox("Post", At(a, flights * rise / 2f, depth), new Vector3(0.12f, flights * rise, 0.12f), steel, 1f, t, yaw);
        float top = flights * rise;
        float end = flights % 2 == 1 ? landing + run + landing / 2f : landing / 2f;
        if (toRoof)
        {
            // plank from the top landing over the parapet onto the roof
            MeshGen.TexBox("RoofPlank", At(end, top + 0.04f, -0.6f), new Vector3(1.1f, 0.08f, 2.2f), steps, 1f, t, yaw + 90f);
        }
        else
        {
            // a wider platform with a waist-high steel wall: cover for shooting down the street
            MeshGen.TexBox("Perch", At(end, top - 0.06f, depth + 0.7f), new Vector3(landing + 1.2f, 0.12f, 1.4f), steps, 1.5f, t, yaw + 90f);
            MeshGen.TexBox("PerchWall", At(end, top + 0.5f, depth + 1.37f), new Vector3(0.08f, 1f, landing + 1.2f), steel, 1f, t, yaw);
        }
    }

    /// <summary>Outdoor staircase with side walls, from a ground point up to the given height.</summary>
    public static float OutdoorStairs(Transform parent, Vector3 start, Vector3 dir, float height, Material steps, Material rails)
    {
        var t = new GameObject("OutdoorStairs").transform;
        t.SetParent(parent, false);
        dir.y = 0;
        dir.Normalize();
        Stairs(t, start, dir, height, 1.6f, steps);
        int count = Mathf.CeilToInt(height / StepH);
        float run = count * StepRun;
        var side = Vector3.Cross(Vector3.up, dir);
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        foreach (float s in new[] { -1f, 1f })
        {
            var p = start + dir * (run / 2f) + side * (s * 0.85f) + Vector3.up * (height / 2f + 0.45f);
            var rail = MeshGen.TexBox("Rail", p, new Vector3(0.06f, 0.06f, run * 1.05f), rails, 1f, t, yaw, false);
            rail.transform.rotation = Quaternion.LookRotation(dir * run + Vector3.up * height);
            rail.transform.position = start + dir * (run / 2f) + side * (s * 0.85f) + Vector3.up * (height / 2f + 0.95f);
        }
        return run;
    }

    /// <summary>
    /// Builds the building centred at center (front = -Z before yaw), size w x l, floors high.
    /// Returns the root transform.
    /// </summary>
    public static Transform Build(Transform parent, string name, Vector3 center, float yaw, float w, float l, int floors, Style s)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(center, Quaternion.Euler(0, yaw, 0));
        float hw = w / 2f, hl = l / 2f;
        int steps = Mathf.CeilToInt(FloorHeight / StepH);
        float run = steps * StepRun;
        // stair core along the +X wall, centred in Z
        float coreX1 = hw - Wall, coreX0 = coreX1 - 2 * Lane;
        float zA = -run / 2f, zB = run / 2f;
        Rect LaneRect(int lane) => new Rect(lane == 0 ? coreX0 : coreX0 + Lane, zA, Lane, run);

        // ground floor
        Box(t, "GroundFloor", new Vector3(0, 0.05f, 0), new Vector3(w, 0.1f, l), s.Floor, 3f);
        for (int k = 0; k < floors; k++)
        {
            float y0 = k * FloorHeight;
            if (k > 0) SlabWithHole(t, y0, -hw + Wall, hw - Wall, -hl + Wall, hl - Wall, LaneRect((k - 1) % 2), s.Floor);
            // outer walls with windows; doors on the ground floor front and back
            // upper floors: a door out onto a balcony, on the front or the back (alternating)
            float frontDoor = k == 0 ? -w * 0.2f : k % 2 == 1 ? -w * 0.25f : w * 0.12f;
            float backDoor = k == 0 ? w * 0.2f : k % 2 == 0 ? -w * 0.22f : w * 0.12f;
            bool frontBalcony = k > 0, backBalcony = k > 0 && k % 2 == 0;
            bool ribbon = s.Glass != null;
            WallStrip(t, true, -hl + Wall / 2f, y0, -hw, hw, Windows(w, k == 0 || frontBalcony, frontDoor, ribbon), s.Outer, s.Glass, -1f);
            WallStrip(t, true, hl - Wall / 2f, y0, -hw, hw, Windows(w, k == 0 || backBalcony, backDoor, ribbon), s.Outer, s.Glass, 1f);
            if (frontBalcony) Balcony(t, frontDoor, y0, -hl, -1f, s);
            if (backBalcony) Balcony(t, backDoor, y0, hl, 1f, s);
            if (k == 0)
                foreach (var (x, z, side) in new[] { (frontDoor, -hl, -1f), (backDoor, hl, 1f) })
                    Box(t, "Porch", new Vector3(x, 0.08f, z + side * 0.9f), new Vector3(2.6f, 0.16f, 1.6f), s.Trim, 1.5f, 0.05f);
            WallStrip(t, false, -hw + Wall / 2f, y0, -hl + Wall, hl - Wall, Windows(l - 2 * Wall, false, 0f, ribbon), s.Outer, s.Glass, -1f);
            WallStrip(t, false, hw - Wall / 2f, y0, -hl + Wall, hl - Wall, Windows(l - 2 * Wall, false, 0f, ribbon), s.Outer, s.Glass, 1f);
            // floor band on the outside of the walls (decoration, no collider)
            if (k > 0)
                foreach (float sgn in new[] { -1f, 1f })
                {
                    MeshGen.RoundBox("Band", t.TransformPoint(new Vector3(0, y0 - 0.1f, sgn * (hl + 0.05f))), new Vector3(w + 0.2f, 0.3f, 0.12f), 0.04f, s.Trim, 2f, t, t.eulerAngles.y, false);
                    MeshGen.RoundBox("Band", t.TransformPoint(new Vector3(sgn * (hw + 0.05f), y0 - 0.1f, 0)), new Vector3(0.12f, 0.3f, l), 0.04f, s.Trim, 2f, t, t.eulerAngles.y, false);
                }
            // flight from this floor to the next one (the last one reaches the roof)
            int lane = k % 2;
            var laneRect = LaneRect(lane);
            bool up = k % 2 == 0;
            var start = new Vector3(laneRect.center.x, y0, up ? zA : zB);
            Stairs(t, t.TransformPoint(start), t.TransformDirection(up ? Vector3.forward : Vector3.back), FloorHeight, Lane - 0.1f, s.Stairs);
            // low wall between the two lanes
            Box(t, "StairDivider", new Vector3(coreX0 + Lane, y0 + 0.5f, 0), new Vector3(0.12f, 1f, run), s.Trim, 1f);
            // some interior cover: a partition wall with a doorway, desks and cabinets
            if (w > 9f && l > 9f)
            {
                float partitionEnd = coreX0 - 0.4f, door = (-hw + Wall + partitionEnd) / 2f + (k % 2 == 0 ? -1.5f : 1.5f);
                WallStrip(t, true, hl * 0.35f, y0, -hw + Wall, partitionEnd, new List<(float, float, float, float)> { (door, 1.3f, 0f, 2.3f) }, s.Inner ?? s.Outer);
                Box(t, "Desk", new Vector3(-hw + 2.2f, y0 + 0.45f, -hl + 3f), new Vector3(2f, 0.9f, 0.9f), s.Furniture, 1f);
                Box(t, "Desk", new Vector3(-hw + 2.2f, y0 + 0.45f, hl - 3f), new Vector3(2f, 0.9f, 0.9f), s.Furniture, 1f);
                Box(t, "Cabinet", new Vector3(coreX0 - 1.4f, y0 + 1f, hl - 1f), new Vector3(1.6f, 2f, 0.6f), s.Furniture, 1f);
                Box(t, "Crate", new Vector3(-1f, y0 + 0.5f, 0.5f), new Vector3(1f, 1f, 1f), s.Furniture, 1f);
            }
        }
        // roof with the stair opening and a parapet
        float roofY = floors * FloorHeight;
        SlabWithHole(t, roofY, -hw, hw, -hl, hl, LaneRect((floors - 1) % 2), s.Trim);
        Box(t, "Parapet", new Vector3(0, roofY + 0.5f, -hl + 0.12f), new Vector3(w, 1f, 0.24f), s.Outer, 2f, 0.06f);
        Box(t, "Parapet", new Vector3(0, roofY + 0.5f, hl - 0.12f), new Vector3(w, 1f, 0.24f), s.Outer, 2f, 0.06f);
        Box(t, "Parapet", new Vector3(-hw + 0.12f, roofY + 0.5f, 0), new Vector3(0.24f, 1f, l), s.Outer, 2f, 0.06f);
        Box(t, "Parapet", new Vector3(hw - 0.12f, roofY + 0.5f, 0), new Vector3(0.24f, 1f, l), s.Outer, 2f, 0.06f);
        // coping stone on the parapet and rounded corner pillars up the whole building
        foreach (float sgn in new[] { -1f, 1f })
        {
            MeshGen.RoundBox("Coping", t.TransformPoint(new Vector3(0, roofY + 1.04f, sgn * (hl - 0.12f))), new Vector3(w + 0.1f, 0.1f, 0.36f), 0.04f, s.Trim, 1.5f, t, t.eulerAngles.y, false);
            MeshGen.RoundBox("Coping", t.TransformPoint(new Vector3(sgn * (hw - 0.12f), roofY + 1.04f, 0)), new Vector3(0.36f, 0.1f, l + 0.1f), 0.04f, s.Trim, 1.5f, t, t.eulerAngles.y, false);
            foreach (float sgn2 in new[] { -1f, 1f })
                MeshGen.RoundBox("CornerPillar", t.TransformPoint(new Vector3(sgn * (hw - 0.1f), (roofY + 1f) / 2f, sgn2 * (hl - 0.1f))), new Vector3(0.5f, roofY + 1f, 0.5f), 0.14f, s.Trim, 2f, t, t.eulerAngles.y, false);
        }
        // rail around the stair opening on the roof
        var top = LaneRect((floors - 1) % 2);
        Box(t, "RoofRail", new Vector3(top.xMin - 0.05f, roofY + 0.5f, top.center.y), new Vector3(0.1f, 1f, top.height), s.Trim, 1f);
        Box(t, "RoofHut", new Vector3(-hw + 2f, roofY + 0.9f, hl - 2f), new Vector3(2.4f, 1.8f, 2.4f), s.Trim, 2f);
        return t;
    }
}
