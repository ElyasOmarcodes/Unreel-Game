using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEngine;

/// <summary>
/// Steel City grown by two small yards, made the way the old town is made: of its own pieces, on its
/// own 10 m grid, each yard an outline of several joined rectangles (never one square), and the
/// town's one road carried on through them with its bends. The East Yard: the road turns south at
/// the old east edge, through the gate, east, south and east again to a closed gate. The South Yard:
/// the road goes on south from its old west end and turns east. Pipes stand as in the old town: a
/// riser against its tank, a run of pipes ending in its riser (copied with their own distances). Ways
/// up: ladders up hangars, and from one roof a plank and a last ladder to a steel look-out tower.
/// </summary>
public static partial class CityLordBuilder
{
    private static bool Near(Vector3 at, float x, float z) => Mathf.Abs(at.x - x) < 1f && Mathf.Abs(at.z - z) < 1f;

    /// <summary>The pieces of the old town taken away where the road goes on and the yards join.</summary>
    private static bool EastGate(string name, Vector3 at) =>
        name.StartsWith("Oil_tank_v2") && at.x > 94f && at.z > -9f && at.z < 5f
        || name.StartsWith("Concrete_fence_v1_wall_set_v2") && Near(at, 99.3f, -3.3f)
        || name.StartsWith("Road_set_v1_s_bend_1") && Near(at, 87f, 11.41f)
        || name.StartsWith("Road_set_v1_s_tile_2") && Near(at, 87f, 26.41f)
        || name.StartsWith("Road_set_v1_b_floor") && Near(at, 92f, -3.69f)
        || name.StartsWith("Cargo_container_v1_LD2close") && Near(at, 83.71f, -4.47f)
        || name.StartsWith("Concrete_fence_v1_wall_set_v1") && at.z < -52f && at.x < -15f;

    private static Bounds BuildEast(List<Vector3> spawns)
    {
        var e = Node("NewYards");
        Physics.SyncTransforms();
        int mask = World.EnvMask;
        float east = Lift - 10.9f, south = Lift - 12.9f;   // the street levels of the old east and west ends
        var floorKit = Kit("Road_set_v1_s_floor");
        var asphalt = floorKit != null && floorKit.Materials.Length > 0 ? floorKit.Materials[0] : MaterialNamed("MI_Asphalt");
        var boxKit = Kit("Wooden_box_v1_LD2square");
        var wood = boxKit != null && boxKit.Materials.Length > 0 ? boxKit.Materials[0] : asphalt;
        var iron = RustyIron;
        var total = new Bounds(new Vector3(120f, east, -20f), Vector3.one);

        // a piece with the middle of its footprint at (x, z): standing on 'ground', or (top) its top on that level
        GameObject Put(string name, float x, float z, float yaw, float ground, bool top = false)
        {
            var kit = Kit(name);
            if (kit == null) return null;
            var c = Quaternion.Euler(0, yaw, 0) * new Vector3(kit.Bounds.center.x, 0, kit.Bounds.center.z);
            var go = Place(name, e, new Vector3(x - c.x, ground - (top ? kit.Bounds.size.y : 0f), z - c.z), yaw);
            total.Encapsulate(new Vector3(x, ground, z));
            return go;
        }
        // a piece of the old town again, moved (its own turn kept): chains of pipes keep their joints
        void Copy(string name, float px, float minY, float pz, float yaw, float dx, float dy, float dz) =>
            Place(name, e, new Vector3(px + dx, minY + Lift + dy, pz + dz), yaw);
        float Roof(float x, float z, float level)
        {
            Physics.SyncTransforms();
            return Physics.Raycast(new Vector3(x, level + 40f, z), Vector3.down, out var h, 60f, mask, QueryTriggerInteraction.Ignore) ? h.point.y : level;
        }
        void Floor(float level, params (float x, float z)[] cells) { foreach (var (x, z) in cells) Put("Road_set_v1_s_floor", x, z, 0f, level, true); }
        void Straight(float x, float z, bool alongX, bool twenty, float level) => Put(twenty ? "Road_set_v1_s_tile_2" : "Road_set_v1_s_tile_1", x, z, alongX ? 0f : 90f, level, true);
        // a bend: 0 joins north and east, 90 east and south, 180 south and west, -90 west and north
        void Bend(float x, float z, float yaw, float level) { Put("Road_set_v1_s_bend_1", x, z, yaw, level, true); Put("Road_set_v1_s_bend_1f", x, z, yaw, level, true); }
        void Ground(float x0, float z0, float x1, float z1, float level) =>
            MeshGen.TexBox("YardBase", new Vector3((x0 + x1) / 2f, level - 2.3f, (z0 + z1) / 2f), new Vector3(x1 - x0, 4f, z1 - z0), asphalt, 6f, e);
        // a fence along a side of the grid (10 m panels between columns), 'gate' a closed gate in the middle of it
        void Fence(float ax, float az, float bx, float bz, float level, bool gate = false)
        {
            bool alongX = Mathf.Abs(bx - ax) > Mathf.Abs(bz - az);
            float len = alongX ? Mathf.Abs(bx - ax) : Mathf.Abs(bz - az);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 10f));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(new Vector2(ax, az), new Vector2(bx, bz), i / (float)n);
                Put("Concrete_fence_v1_column", p.x, p.y, 0f, level - 0.05f);
                if (i == n) break;
                var m = Vector2.Lerp(new Vector2(ax, az), new Vector2(bx, bz), (i + 0.5f) / n);
                if (gate)
                {
                    Put("Concrete_fence_v2_Gate", m.x, m.y, alongX ? -90f : 0f, level - 0.05f);
                    Put("Concrete_fence_v2_Gate_lattice", m.x, m.y, alongX ? -90f : 0f, level + 1.02f);
                    foreach (float s in new[] { -1f, 1f })
                        Put("Concrete_fence_v2_S_half", m.x + (alongX ? s * 3.9f : 0f), m.y + (alongX ? 0f : s * 3.9f), alongX ? 90f : 0f, level - 0.05f);
                }
                else Put("Concrete_fence_v1_wall_set_v1", m.x, m.y, alongX ? 90f : 0f, level - 0.05f);
            }
        }
        void Stack(float x, float z, float yaw, float level, bool two)
        {
            Put("Cargo_container_v1_LD2close", x, z, yaw, level);
            if (two) Put("Cargo_container_v1_LD2close", x, z, yaw + 5f, level + 2.6f);
        }

        // ---------------------------------------------------------------- the road on through the old east gate
        Bend(87f, 11.41f, 180f, east);
        Bend(87f, 1.41f, 0f, east);
        Straight(97f, 1.41f, true, false, east);
        Floor(east, (87f, -8.59f), (97f, -8.59f), (87f, 21.41f), (87f, 31.41f));
        MeshGen.TexBox("FloorJoint", new Vector3(92f, east - 0.05f, -13.64f), new Vector3(20f, 0.1f, 0.14f), asphalt, 6f, e, 0f, false);

        // ---------------------------------------------------------------- the East Yard
        // its outline: a court at the gate (x 102-122), a wider east part (122-142), a south wing and its corner
        Ground(102f, -53.59f, 122f, 16.41f, east);
        Ground(122f, -43.59f, 142f, 6.41f, east);
        Ground(122f, -53.59f, 132f, -43.59f, east);
        Straight(112f, 1.41f, true, true, east);
        Bend(127f, 1.41f, 180f, east);
        Straight(127f, -13.59f, false, true, east);
        Bend(127f, -28.59f, 0f, east);
        Straight(137f, -28.59f, true, false, east);
        Floor(east, (107f, 11.41f), (117f, 11.41f), (107f, -8.59f), (117f, -8.59f), (107f, -18.59f), (117f, -18.59f), (107f, -28.59f), (117f, -28.59f),
            (107f, -38.59f), (117f, -38.59f), (107f, -48.59f), (117f, -48.59f), (127f, -38.59f), (127f, -48.59f),
            (137f, 1.41f), (137f, -8.59f), (137f, -18.59f), (137f, -38.59f));
        // the buildings: a long hangar in the west part, a smaller one east of the road, a workshop in the court
        Put("Hangar_v4", 111.5f, -26f, 0f, east);
        Put("Hangar_v3_basic", 136f, -9.4f, 0f, east);
        Put("Hangar_v1_full", 110f, 11.6f, 0f, east);
        // an oil tank with its riser (as the tank of the old yard), and a run of pipes ending in its riser
        Copy("Oil_tank_v1", 75.4f, -10.91f, -32.52f, 0f, 33.6f, 0f, -15f);
        Copy("Pipes_set_v1_V_set_v2", 75.69f, -10.92f, -26.87f, 90f, 33.6f, 0f, -15f);
        Copy("Pipes_set_v1_H_set_v1", 79.64f, -10.94f, -10.23f, -80.3f, 30.2f, 0f, 1.5f);
        Copy("Pipes_set_v1_H_set_v2", 88.68f, -10.94f, -11.78f, -80.3f, 30.2f, 0f, 1.5f);
        // the riser stands on the end of the run (in the old town the two miss each other by a metre): the pipe runs into its side
        Copy("Pipes_set_v1_V_set_v2", 91.36f, -10.94f, -10.41f, 90f, 30.2f, 0f, 0f);
        Put("Industrial_pipe_v1", 127.6f, -49.4f, 0f, east);
        Physics.SyncTransforms();
        // the ways up: a ladder up the long hangar from the court; a ladder up the east hangar, from its roof
        // a plank to the look-out tower and a ladder to its top
        PlaceLadder(e, new Vector3(111.5f - 5.84f + 0.05f, east, -30f), Vector3.left, Roof(111.5f - 4.6f, -30f, east) - east);
        float h3s = -9.4f - 6.75f, h3n = -9.4f + 6.75f;
        PlaceLadder(e, new Vector3(136f, east, h3n - 0.05f), Vector3.forward, Roof(136f, h3n - 1.2f, east) - east);
        float deck = Roof(136f, h3s + 0.7f, east) + 0.05f, topDeck = deck + 5.2f;
        const float tw = 4.4f;
        float tz = h3s - 1.6f - tw / 2f;
        foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            MeshGen.TexBox("TowerLeg", new Vector3(136f + sx * tw / 2f, (topDeck + 2.4f + east) / 2f, tz + sz * tw / 2f), new Vector3(0.34f, topDeck + 2.4f - east, 0.34f), iron, 1.5f, e);
        for (float y = east + 0.5f; y + 4.2f < topDeck; y += 4.4f)
            for (int s = 0; s < 4; s++)
            {
                var a = Quaternion.Euler(0, s * 90f, 0) * new Vector3(-tw / 2f, 0, tw / 2f);
                var b = Quaternion.Euler(0, s * 90f, 0) * new Vector3(tw / 2f, 0, tw / 2f);
                var c = new Vector3(136f, 0, tz);
                Strut(e, c + a + Vector3.up * y, c + b + Vector3.up * (y + 4.2f), 0.12f, iron);
                Strut(e, c + b + Vector3.up * y, c + a + Vector3.up * (y + 4.2f), 0.12f, iron);
            }
        MeshGen.TexBox("TowerDeck", new Vector3(136f, deck - 0.1f, tz), new Vector3(tw + 0.5f, 0.2f, tw + 0.5f), wood, 2f, e);
        Plank(e, new Vector3(136f, deck, h3s + 0.9f), new Vector3(136f, deck, tz + tw / 2f), 1.6f, wood);
        // the look-out: a deck over the far half, open towards the hangar where the ladder comes up
        float lz0 = tz + 0.4f, lz1 = tz - tw / 2f - 0.25f;
        MeshGen.TexBox("LookoutDeck", new Vector3(136f, topDeck - 0.1f, (lz0 + lz1) / 2f), new Vector3(tw + 0.5f, 0.2f, lz0 - lz1), wood, 2f, e);
        MeshGen.TexBox("LookoutRail", new Vector3(136f, topDeck + 0.5f, lz1 + 0.06f), new Vector3(tw + 0.5f, 1.0f, 0.1f), iron, 1.5f, e);
        foreach (float sx in new[] { -1f, 1f })
            MeshGen.TexBox("LookoutRail", new Vector3(136f + sx * (tw / 2f + 0.19f), topDeck + 0.5f, (lz0 + lz1) / 2f), new Vector3(0.1f, 1.0f, lz0 - lz1), iron, 1.5f, e);
        MeshGen.TexBox("LookoutRoof", new Vector3(136f, topDeck + 2.5f, tz), new Vector3(tw + 1.2f, 0.12f, tw + 1.2f), iron, 2f, e, 0f, false);
        Physics.SyncTransforms();
        PlaceLadder(e, new Vector3(136f, deck, lz0), Vector3.forward, topDeck - deck);
        // the yard's things
        Stack(118.5f, -45.5f, 90f, east, true);
        Stack(134f, -39f, 90f, east, false);
        Stack(118.5f, 12.5f, 90f, east, false);
        foreach (var (x, z) in new[] { (119.5f, -5.5f), (131.5f, 3.5f), (119f, -37f), (121f, -20f), (133f, -24f), (104f, 13f) })
        {
            Put("Wooden_box_v1_LD2square", x, z, x * 7f % 40f, east);
            Put("Wooden_box_v1_LD1square", x + 1.5f, z + 0.3f, z * 5f % 30f, east);
        }
        foreach (var (x, z) in new[] { (120f, -33f), (140f, -19.5f), (118f, -15.5f) }) Put("Palet_v1_set", x, z, x * 3f % 90f, east);
        foreach (var (x, z) in new[] { (104f, -36f), (140.5f, 4.5f), (131f, -41.5f) }) Put("Barrel_v3_quadro", x, z, 0f, east);
        Physics.SyncTransforms();
        Obstacles.AmmoCrate(e, new Vector3(139.5f, east, -1f), 20f);
        // its fence, corner by corner; the road ends at a closed gate
        Fence(102f, 16.41f, 122f, 16.41f, east);
        Fence(122f, 6.41f, 122f, 16.41f, east);
        Fence(122f, 6.41f, 142f, 6.41f, east);
        Fence(142f, -23.59f, 142f, 6.41f, east);
        Fence(142f, -33.59f, 142f, -23.59f, east, true);
        Fence(142f, -43.59f, 142f, -33.59f, east);
        Fence(132f, -43.59f, 142f, -43.59f, east);
        Fence(132f, -53.59f, 132f, -43.59f, east);
        Fence(102f, -53.59f, 132f, -53.59f, east);
        BoardStand(Node("EastBoard"), new Vector3(104f, -10.9f, 8f), "ختیځ انګړ", "EAST YARD");
        spawns.Add(new Vector3(116f, east + 0.1f, -44f));
        total.Encapsulate(new Vector3(143f, topDeck + 3f, 17f));
        total.Encapsulate(new Vector3(101f, east, -54.2f));

        // ---------------------------------------------------------------- the South Yard
        Ground(-28f, -83.59f, 12f, -53.59f, south);
        Ground(12f, -73.59f, 22f, -53.59f, south);
        Straight(-23f, -63.59f, false, true, south);
        Bend(-23f, -78.59f, 0f, south);
        Straight(-8f, -78.59f, true, true, south);
        Straight(7f, -78.59f, true, false, south);
        Floor(south, (-13f, -58.59f), (-3f, -58.59f), (7f, -58.59f), (17f, -58.59f), (-13f, -68.59f), (-3f, -68.59f), (7f, -68.59f), (17f, -68.59f));
        Put("Hangar_v1_full", 17f, -63.4f, 90f, south);
        Copy("Oil_tank_v1", 75.4f, -10.91f, -32.52f, 0f, -73.4f, -2f, -31.3f);
        Copy("Pipes_set_v1_V_set_v2", 75.69f, -10.92f, -26.87f, 90f, -73.4f, -2f, -31.3f);
        Physics.SyncTransforms();
        // a ladder up the south end of the old hangar that stands into the yard: its roof leads back over the town
        PlaceLadder(e, new Vector3(-12.8f, south, -65.78f - 0.05f), Vector3.back, Roof(-12.8f, -64.6f, south) - south);
        Stack(-14f, -71.2f, 90f, south, true);
        Stack(8.5f, -57.2f, 90f, south, false);
        foreach (var (x, z) in new[] { (-20f, -57f), (-6.5f, -72f), (9.5f, -71.5f) })
        {
            Put("Wooden_box_v1_LD2square", x, z, x * 7f % 40f, south);
            Put("Wooden_box_v1_LD1square", x + 1.5f, z + 0.3f, z * 5f % 30f, south);
        }
        Put("Palet_v1_set", -24.5f, -70f, 20f, south);
        Put("Barrel_v3_quadro", 10.5f, -81.5f, 0f, south);
        Physics.SyncTransforms();
        Obstacles.AmmoCrate(e, new Vector3(13.5f, south, -70.5f), -70f);
        Fence(-28f, -83.59f, -28f, -63.59f, south);
        Fence(-28f, -83.59f, 12f, -83.59f, south);
        Fence(12f, -83.59f, 12f, -73.59f, south, true);
        Fence(12f, -73.59f, 22f, -73.59f, south);
        Fence(22f, -73.59f, 22f, -53.59f, south);
        Fence(-7f, -53.59f, 22f, -53.59f, south);
        BoardStand(Node("SouthBoard"), new Vector3(-16f, -12.9f, -76f), "جنوبي انګړ", "SOUTH YARD");
        spawns.Add(new Vector3(-3f, south + 0.1f, -73f));
        total.Encapsulate(new Vector3(-28.5f, south, -84.2f));
        total.Encapsulate(new Vector3(22.5f, south, -53f));
        Debug.Log($"[Map] CityLord: the East Yard and the South Yard built (tower top {topDeck - east:0.0} m over the street)");
        return total;
    }

    /// <summary>A plank bridge of boards between two edges, a rail on each side.</summary>
    private static void Plank(Transform parent, Vector3 a, Vector3 b, float width, Material wood)
    {
        float len = Vector3.Distance(a, b);
        var go = MeshGen.Object("Plank", MeshGen.Box(new Vector3(width, 0.14f, len + 0.6f), 1.5f), wood, parent, (a + b) / 2f - Vector3.up * 0.07f);
        go.transform.rotation = Quaternion.LookRotation(b - a);
        go.AddComponent<BoxCollider>().size = new Vector3(width, 0.14f, len + 0.6f);
        foreach (float s in new[] { -1f, 1f })
        {
            var side = go.transform.right * (s * (width / 2f - 0.04f));
            Strut(parent, a + side + Vector3.up * 0.9f, b + side + Vector3.up * 0.9f, 0.06f, RustyIron, true);
            Strut(parent, a + side, a + side + Vector3.up * 0.9f, 0.06f, RustyIron);
            Strut(parent, b + side, b + side + Vector3.up * 0.9f, 0.06f, RustyIron);
        }
    }

    private static void Strut(Transform parent, Vector3 a, Vector3 b, float thick, Material m, bool collider = false)
    {
        float len = Vector3.Distance(a, b);
        var go = MeshGen.Object("Strut", MeshGen.Box(new Vector3(thick, thick, len), 1.5f), m, parent, (a + b) / 2f);
        go.transform.rotation = Quaternion.LookRotation(b - a);
        if (collider) go.AddComponent<BoxCollider>().size = new Vector3(thick, thick, len);
    }
}
