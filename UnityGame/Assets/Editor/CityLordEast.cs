using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEngine;

/// <summary>
/// The East Depot: Steel City grown east of its old edge, out of the town's own pieces (hangars, oil
/// tanks, containers, boxes, pallets, barrels, fences, the wooden ladder) on the same street level.
/// Laid out the way the old town is: things stand so that one leads up to the next. A ladder up a
/// hangar, a plank from roof to roof, and from the last roof a plank and a ladder to the top of the
/// steel look-out tower, the highest point of the map, over both the depot and the old town.
/// </summary>
public static partial class CityLordBuilder
{
    /// <summary>The pieces of the old east edge taken away for the gate into the depot (four small tanks and a fence).</summary>
    private static bool EastGate(string name, Vector3 at) =>
        name.StartsWith("Oil_tank_v2") && at.x > 94f && at.z > -9f && at.z < 5f
        || name.StartsWith("Concrete_fence_v1_wall_set_v2") && Mathf.Abs(at.x - 99.3f) < 1f && Mathf.Abs(at.z + 3.3f) < 1f;

    private static Bounds BuildEast(List<Vector3> spawns)
    {
        var e = Node("EastDepot");
        Physics.SyncTransforms();
        int mask = World.EnvMask;
        // the street's level at the old east edge
        float level = Physics.Raycast(new Vector3(92f, Lift + 30f, -3.7f), Vector3.down, out var street, 80f, mask, QueryTriggerInteraction.Ignore) ? street.point.y : Lift - 10.9f;
        const float x0 = 99.6f, x1 = 174f, z0 = -54f, z1 = 46f;
        var total = new Bounds(new Vector3((x0 + x1) / 2f, level, (z0 + z1) / 2f), new Vector3(x1 - x0, 1f, z1 - z0));
        var road = Kit("Road_set_v1_b_floor");
        var asphalt = road != null && road.Materials.Length > 0 ? road.Materials[0] : MaterialNamed("MI_Asphalt");
        var fenceKit = Kit("Concrete_fence_v1_wall_set_v1");
        var concrete = fenceKit != null && fenceKit.Materials.Length > 0 ? fenceKit.Materials[0] : asphalt;
        var boxKit = Kit("Wooden_box_v1_LD2square");
        var wood = boxKit != null && boxKit.Materials.Length > 0 ? boxKit.Materials[0] : concrete;
        var iron = RustyIron;
        // the ground: one slab on the street's level, its sides walled down to the plain
        MeshGen.TexBox("EastGround", new Vector3((x0 + x1) / 2f, level - 2.02f, (z0 + z1) / 2f), new Vector3(x1 - x0, 4f, z1 - z0), asphalt, 6f, e);

        // a piece of the town standing with the middle of its footprint at (x, z)
        GameObject Put(string name, float x, float z, float yaw, float up = 0f)
        {
            var kit = Kit(name);
            if (kit == null) return null;
            var c = Quaternion.Euler(0, yaw, 0) * new Vector3(kit.Bounds.center.x, 0, kit.Bounds.center.z);
            var go = Place(name, e, new Vector3(x - c.x, level + up, z - c.z), yaw);
            return go;
        }
        Vector3 Size(string name) => Kit(name)?.Bounds.size ?? new Vector3(10f, 10f, 10f);
        // how high the roof stands at (x, z)
        float Roof(float x, float z)
        {
            Physics.SyncTransforms();
            return Physics.Raycast(new Vector3(x, level + 40f, z), Vector3.down, out var h, 60f, mask, QueryTriggerInteraction.Ignore) ? h.point.y : level;
        }

        // the hangars: two side by side in the north east, one long in the north west, one each in the south
        var h4 = Size("Hangar_v4");
        bool longZ = h4.z >= h4.x;
        float h4w = Mathf.Min(h4.x, h4.z), h4l = Mathf.Max(h4.x, h4.z);
        float yawNS = longZ ? 0f : 90f;
        float ax = 148f, bx = ax + h4w + 4.2f, nz = 12f + h4l / 2f;
        Put("Hangar_v4", ax, nz, yawNS);
        Put("Hangar_v4", bx, nz, yawNS);
        float cx = 147f, cz = -16f - h4l / 2f;
        Put("Hangar_v4", cx, cz, yawNS);
        var h2 = Size("Hangar_v2_basic");
        Put("Hangar_v2_basic", 116f, 14f + Mathf.Min(h2.x, h2.z) / 2f, h2.x >= h2.z ? 0f : 90f);
        var h3 = Size("Hangar_v3_basic");
        Put("Hangar_v3_basic", 113f, -14f - Mathf.Max(h3.x, h3.z) / 2f, h3.z >= h3.x ? 0f : 90f);
        Put("Hangar_v1_full", 110f, 38f, 0f);
        Put("Oil_tank_v1", 122.5f, 38.5f, 0f);
        Put("Oil_tank_v1", 121f, -45f, 0f);
        Put("Oil_tank_v1", 108f, -46f, 0f);
        Put("Pipes_set_v1_H_set_v2", 114.5f, -41f, 90f);
        Physics.SyncTransforms();

        // the ways up. North: a ladder up the first hangar, a plank over to its twin
        float roofA = Roof(ax, 12f + 1.2f), roofB = Roof(bx, 12f + 1.2f);
        PlaceLadder(e, new Vector3(ax, level, 12f - 0.05f), Vector3.back, roofA - level);
        float edgeA = Roof(ax + h4w / 2f - 0.6f, nz), edgeB = Roof(bx - h4w / 2f + 0.6f, nz);
        Plank(e, new Vector3(ax + h4w / 2f - 0.8f, edgeA + 0.05f, nz), new Vector3(bx - h4w / 2f + 0.8f, edgeB + 0.05f, nz), 1.5f, wood);
        // South: boxes and a container as steps to a ladder up the third hangar; from its roof a plank to
        // the look-out tower and a ladder to its top
        float roofC = Roof(cx, -16f - 1.2f);
        var box = Size("Wooden_box_v1_LD2square");
        Put("Wooden_box_v1_LD2square", cx - 7.5f, -13.4f, 8f);
        Put("Wooden_box_v1_LD2square", cx - 5.6f, -13.2f, -4f);
        Put("Wooden_box_v1_LD2square", cx - 5.7f, -13.3f, 12f, box.y);
        PlaceLadder(e, new Vector3(cx, level, -16f + 0.05f), Vector3.forward, roofC - level);
        float towerX = cx + h4w / 2f + 7.5f, towerZ = cz;
        float edgeC = Roof(cx + h4w / 2f - 0.6f, cz);
        float deck = edgeC + 0.05f, topDeck = deck + 5.2f;
        const float tw = 4.6f;
        // the tower: four legs, cross braces, a lower deck on the plank's level, the look-out on top under a roof
        foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var foot = new Vector3(towerX + sx * tw / 2f, level, towerZ + sz * tw / 2f);
            MeshGen.TexBox("TowerLeg", foot + Vector3.up * ((topDeck + 2.4f - level) / 2f), new Vector3(0.34f, topDeck + 2.4f - level, 0.34f), iron, 1.5f, e);
        }
        for (float y = level + 0.6f; y + 4.4f < topDeck; y += 4.6f)
            for (int s = 0; s < 4; s++)
            {
                var a = Quaternion.Euler(0, s * 90f, 0) * new Vector3(-tw / 2f, 0, tw / 2f);
                var b = Quaternion.Euler(0, s * 90f, 0) * new Vector3(tw / 2f, 0, tw / 2f);
                var c = new Vector3(towerX, 0, towerZ);
                Strut(e, c + a + Vector3.up * y, c + b + Vector3.up * (y + 4.4f), 0.12f, iron);
                Strut(e, c + b + Vector3.up * y, c + a + Vector3.up * (y + 4.4f), 0.12f, iron);
            }
        MeshGen.TexBox("TowerDeck", new Vector3(towerX, deck - 0.1f, towerZ), new Vector3(tw + 0.5f, 0.2f, tw + 0.5f), wood, 2f, e);
        Plank(e, new Vector3(cx + h4w / 2f - 0.8f, deck, towerZ), new Vector3(towerX - tw / 2f, deck, towerZ), 1.6f, wood);
        // the look-out: a deck over the east half and the middle, open to the west where the ladder comes up
        float tx0 = towerX - 0.4f, tx1 = towerX + tw / 2f + 0.25f;
        MeshGen.TexBox("LookoutDeck", new Vector3((tx0 + tx1) / 2f, topDeck - 0.1f, towerZ), new Vector3(tx1 - tx0, 0.2f, tw + 0.5f), wood, 2f, e);
        foreach (var (px, pz, sxz, lz) in new[] { (tx1 - 0.06f, towerZ, 0.1f, tw + 0.5f), ((tx0 + tx1) / 2f, towerZ + tw / 2f + 0.19f, tx1 - tx0, 0.1f), ((tx0 + tx1) / 2f, towerZ - tw / 2f - 0.19f, tx1 - tx0, 0.1f) })
        {
            MeshGen.TexBox("LookoutRail", new Vector3(px, topDeck + 0.5f, pz), new Vector3(sxz, 1.0f, lz), iron, 1.5f, e);
        }
        MeshGen.TexBox("LookoutRoof", new Vector3(towerX, topDeck + 2.5f, towerZ), new Vector3(tw + 1.2f, 0.12f, tw + 1.2f), iron, 2f, e, 0f, false);
        Physics.SyncTransforms();
        PlaceLadder(e, new Vector3(tx0, deck, towerZ), Vector3.left, topDeck - deck);

        // the yards: containers (some two high), boxes, pallets, barrels, road blocks; ammunition
        var cont = Size("Cargo_container_v1_LD2close");
        foreach (var (x, z, yaw, two) in new[] { (136f, 30f, 0f, true), (131f, 20f, 90f, false), (160f, -6f, 90f, true), (128f, -30f, 0f, false), (136f, -44f, 90f, true), (166f, -40f, 0f, false), (106f, 2f, 90f, false) })
        {
            Put("Cargo_container_v1_LD2close", x, z, yaw);
            if (two) Put("Cargo_container_v1_LD2close", x, z, yaw + 4f, cont.y);
        }
        foreach (var (x, z) in new[] { (126f, 6f), (141f, -8f), (158f, 6.5f), (170f, 2f), (120f, -9f), (133f, 42f), (168f, -20f), (104f, -30f), (150f, 40f) })
        {
            Put("Wooden_box_v1_LD2square", x, z, x * 7f % 40f);
            Put("Wooden_box_v1_LD1square", x + box.x * 0.9f, z + 0.3f, z * 5f % 30f);
        }
        foreach (var (x, z) in new[] { (129f, -12f), (144f, 6f), (164f, 40f), (112f, 8f), (156f, -46f) }) Put("Palet_v1_set", x, z, x * 3f % 90f);
        foreach (var (x, z) in new[] { (139f, 12f), (125f, -20f), (171f, -10f), (103f, 30f) }) Put("Barrel_v3_quadro", x, z, 0f);
        foreach (var (x, z, yaw) in new[] { (118f, -2f, 90f), (152f, 0f, 90f) }) Put("Road_block_v1", x, z, yaw);
        Physics.SyncTransforms();
        Obstacles.AmmoCrate(e, new Vector3(140.5f, level, 36f), 20f);
        Obstacles.AmmoCrate(e, new Vector3(131f, level, -47f), -70f);

        // the fence round the depot's three outer sides
        if (fenceKit != null)
        {
            bool alongX = fenceKit.Bounds.size.x >= fenceKit.Bounds.size.z;
            float len = Mathf.Max(fenceKit.Bounds.size.x, fenceKit.Bounds.size.z);
            for (float x = x0 + len / 2f + 3f; x + len / 2f <= x1; x += len)
            {
                Put("Concrete_fence_v1_wall_set_v1", x, z1 - 0.6f, alongX ? 0f : 90f);
                Put("Concrete_fence_v1_wall_set_v1", x, z0 + 0.6f, alongX ? 0f : 90f);
            }
            for (float z = z0 + len / 2f; z + len / 2f <= z1; z += len)
                Put("Concrete_fence_v1_wall_set_v1", x1 - 0.6f, z, alongX ? 90f : 0f);
        }
        BoardStand(Node("EastBoard"), new Vector3(106f, level - Lift, -6.5f), "ختیځ ډیپو", "EAST DEPOT");
        // two more places to come in
        spawns.Add(new Vector3(136f, level + 0.1f, -2f));
        spawns.Add(new Vector3(163f, level + 0.1f, 42f));
        total.Encapsulate(new Vector3(x1, topDeck + 3f, z1));
        Debug.Log($"[Map] CityLord: the East Depot built (street level {level:0.00}, tower top {topDeck - level:0.0} m)");
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
