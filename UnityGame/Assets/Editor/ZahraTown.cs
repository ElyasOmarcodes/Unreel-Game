using System.Linq;
using UA;
using UnityEngine;

/// <summary>
/// The other houses of Al-Zahra's town, so that a street is not one courtyard house after another:
/// a flat-roofed house with an outside stair and a look-out room, a row of shops behind an arcade with
/// a walkable roof, and a small market square with a well. All from the town's own walls, tiles and
/// wood, nothing new to load.
/// </summary>
public static partial class ZahraBuilder
{
    /// <summary>
    /// A wall between two points of a node's own frame with openings in it (centre along the wall,
    /// width, sill and top above the wall's foot): doors (sill 0), windows one can climb through,
    /// the notches of a parapet (top = the wall's height).
    /// </summary>
    private static void Face(Transform t, Vector3 a, Vector3 b, float y0, float h, float th, Material m, params (float at, float w, float sill, float top)[] holes)
    {
        var wa = t.TransformPoint(a); var wb = t.TransformPoint(b);
        var dir = wb - wa; dir.y = 0;
        float len = dir.magnitude;
        if (len < 0.05f) return;
        var n = Node("Face", t, wa + Vector3.up * y0, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f);
        float e = th / 2f, u = -e;
        foreach (var k in holes.OrderBy(x => x.at))
        {
            float h0 = k.at - k.w / 2f, h1 = k.at + k.w / 2f;
            LBox(n, "Wall", new Vector3((u + h0) / 2f, h / 2f, 0), new Vector3(h0 - u, h, th), m);
            LBox(n, "Sill", new Vector3(k.at, k.sill / 2f, 0), new Vector3(k.w, k.sill, th), m);
            LBox(n, "Lintel", new Vector3(k.at, (k.top + h) / 2f, 0), new Vector3(k.w, h - k.top, th), m);
            u = h1;
        }
        LBox(n, "Wall", new Vector3((u + len + e) / 2f, h / 2f, 0), new Vector3(len + e - u, h, th), m);
    }

    /// <summary>
    /// The low wall round a roof terrace as the Andalusian houses have it: plain lime-washed, under a
    /// coping of tiles; one gap where a stair or a ladder arrives.
    /// </summary>
    private static void Parapet(Transform t, Vector3 a, Vector3 b, float y, Material m, float gapAt = -1f, float gapW = 1.5f, float h = 0.9f)
    {
        if (gapAt >= 0f) Face(t, a, b, y, h, 0.3f, m, (gapAt, gapW, 0f, h));
        else Face(t, a, b, y, h, 0.3f, m);
        var d = b - a;
        float len = d.magnitude;
        bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
        void Cope(float u0, float u1)
        {
            if (u1 - u0 < 0.1f) return;
            var c = a + d.normalized * ((u0 + u1) / 2f) + Vector3.up * (y + h + 0.05f);
            LBox(t, "Coping", c, alongX ? new Vector3(u1 - u0, 0.1f, 0.46f) : new Vector3(0.46f, 0.1f, u1 - u0), RoofTiles, 1f, false);
        }
        if (gapAt >= 0f) { Cope(-0.15f, gapAt - gapW / 2f); Cope(gapAt + gapW / 2f, len + 0.15f); }
        else Cope(-0.15f, len + 0.15f);
    }

    /// <summary>
    /// A horseshoe arch of red and cream voussoirs in its alfiz, set in an opening that Face left in a
    /// wall from a to b: 'along' its middle from a, 'y0' its foot (a door: the floor; a window: the sill).
    /// </summary>
    private static void ArchIn(Transform t, Vector3 a, Vector3 b, float along, float y0, float width, float spring, float top, float th, Material wall)
    {
        var wa = t.TransformPoint(a); var wb = t.TransformPoint(b);
        var dir = wb - wa; dir.y = 0; dir.Normalize();
        var n = Node("Opening", t, wa + dir * along + Vector3.up * y0, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f);
        Arch(n, 0, width, spring, top, th, 30f, wall);
    }

    /// <summary>A wall with horseshoe-arched openings: doors (sill 0) and windows wide enough to climb through.</summary>
    private static void ArchWall(Transform t, Vector3 a, Vector3 b, float y0, float h, float th, Material wall, params (float at, float w, float sill, float spring)[] arches)
    {
        Face(t, a, b, y0, h, th, wall, arches.Select(o => (o.at, o.w, o.sill, h)).ToArray());
        foreach (var o in arches) ArchIn(t, a, b, o.at, y0 + o.sill, o.w, o.spring, h - o.sill, th, wall);
    }

    /// <summary>The red dado (almagra) along the foot of a wall face and the stone plinth under it, as in the houses of Al-Zahra.</summary>
    private static void Dado(Transform t, Vector3 a, Vector3 b, Vector3 outward, float from = 0f, float to = -1f)
    {
        var d = b - a;
        float len = d.magnitude;
        if (to < 0f) to = len;
        if (to - from < 0.2f) return;
        bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
        var c = a + d.normalized * ((from + to) / 2f) + outward * 0.03f;
        LBox(t, "Plinth", c + Vector3.up * 0.2f, alongX ? new Vector3(to - from, 0.4f, 0.08f) : new Vector3(0.08f, 0.4f, to - from), Ashlar, 1.5f, false);
        LBox(t, "Dado", c + Vector3.up * 0.75f, alongX ? new Vector3(to - from, 0.7f, 0.05f) : new Vector3(0.05f, 0.7f, to - from), Almagra, 1.5f, false);
    }

    private static Material TownWall(System.Random r) => r.NextDouble() < 0.6 ? Plaster : LimeOchre;

    /// <summary>A four-sided tiled roof (a pyramid) over w x d with its overhang, the eaves at the node's own height.</summary>
    private static void TiledCap(Transform t, Vector3 local, float w, float d, float rise)
    {
        string key = $"cap_{K(w)}_{K(d)}_{K(rise)}";
        if (!meshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            var bld = new Builder(1.6f);
            var top = new Vector3(0, rise, 0);
            var c = new[] { new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0, -d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(-w / 2, 0, d / 2) };
            for (int i = 0; i < 4; i++)
            {
                var p0 = c[i]; var p1 = c[(i + 1) % 4];
                var n = Vector3.Cross(p1 - p0, top - p0);
                if (n.y < 0) n = -n;
                bld.Face(n, p0, p1, top);
            }
            bld.Face(Vector3.down, c[0], c[1], c[2], c[3]);
            mesh = bld.Save(key);
            meshes[key] = mesh;
        }
        var roof = MeshObject(t, "TiledCap", mesh, RoofTiles, true);
        roof.transform.localPosition = local;
    }

    /// <summary>
    /// A house with a roof terrace in the manner of Al-Zahra (front towards local -Z): lime-washed walls
    /// over a stone plinth and a red dado, a horseshoe-arched door in its alfiz, arched windows one can
    /// climb through, a walled forecourt, an outside stair up to the terrace under its tiled coping, and
    /// (on most) a mirador: a small room on the roof with arched openings all round under a tiled cap.
    /// </summary>
    private static void TerraceHouse(Vector3 c, float yaw, float w, float d, int seed)
    {
        houses++;
        var t = Node("TerraceHouse", root, c, yaw);
        var r = new System.Random(seed);
        var wall = TownWall(r);
        const float th = 0.45f, h = 3.8f, yard = 3.3f;
        float hw = w / 2f, hd = d / 2f, zf = -hd + yard, top = h - 0.25f;
        float door = hw - 1.9f;
        // floors
        LBox(t, "HouseFloor", new Vector3(0, 0.05f, zf / 2f + hd / 2f), new Vector3(w, 0.1f, hd - zf), BrickFloor, 2f);
        LBox(t, "YardFloor", new Vector3(0, 0.04f, (zf - hd) / 2f), new Vector3(w, 0.08f, yard), Violet, 2f, false);
        LBox(t, "Carpet", new Vector3(-1f, 0.11f, (zf + hd) / 2f), new Vector3(3.2f, 0.02f, 2.2f), carpet, 1f, false);
        // the four walls: the arched door in the front, arched windows in the others
        Vector3 fl = new Vector3(-hw + th / 2f, 0, zf), fr = new Vector3(hw - th / 2f, 0, zf);
        Vector3 bl = new Vector3(-hw + th / 2f, 0, hd - th / 2f), br = new Vector3(hw - th / 2f, 0, hd - th / 2f);
        float side = hd - th / 2f - zf, span = w - th;
        ArchWall(t, fl, fr, 0, top, th, wall, (door + hw - th / 2f, 2.2f, 0f, 2.1f));
        ArchWall(t, bl, br, 0, top, th, wall, (span * 0.27f, 1.7f, 1.0f, 0.85f), (span * 0.73f, 1.7f, 1.0f, 0.85f));
        ArchWall(t, fl, bl, 0, top, th, wall, (side * 0.6f, 1.7f, 1.0f, 0.85f));
        ArchWall(t, fr, br, 0, top, th, wall, (side * 0.5f, 1.7f, 1.0f, 0.85f));
        // a partition with a doorway: two rooms
        Face(t, new Vector3(0.6f, 0, zf), new Vector3(0.6f, 0, hd - th / 2f), 0, top, 0.25f, wall, (side * 0.3f, 1.2f, 0f, 2.3f));
        // plinth and red dado outside
        Dado(t, bl + new Vector3(-th / 2f, 0, th / 2f), br + new Vector3(th / 2f, 0, th / 2f), Vector3.forward);
        Dado(t, fl + new Vector3(-th / 2f, 0, 0), bl + new Vector3(-th / 2f, 0, 0), Vector3.left);
        Dado(t, fr + new Vector3(th / 2f, 0, 0), br + new Vector3(th / 2f, 0, 0), Vector3.right);
        LBox(t, "DoorLeaf", new Vector3(door + 0.75f, 1.1f, zf - 0.62f), new Vector3(0.08f, 2.2f, 1f), Wood, 1f, false);
        // the roof terrace, tiles at its eaves all round
        LBox(t, "RoofSlab", new Vector3(0, h - 0.125f, (zf - th / 2f + hd) / 2f), new Vector3(w, 0.25f, hd - zf + th / 2f), Flags, 2f);
        LBox(t, "Eaves", new Vector3(0, h - 0.32f, hd + 0.3f), new Vector3(w + 1.2f, 0.12f, 0.6f), RoofTiles, 1.5f, false);
        foreach (float s in new[] { -1f, 1f })
            LBox(t, "Eaves", new Vector3(s * (hw + 0.3f), h - 0.32f, (zf + hd) / 2f), new Vector3(0.6f, 0.12f, hd - zf), RoofTiles, 1.5f, false);
        // the stair along the front: up to the roof where the parapet is open
        int steps = Mathf.CeilToInt(h / 0.2f);
        float run = steps * 0.36f, sx = -hw + 0.3f, sz = zf - th / 2f - 0.6f;
        Buildings.Stairs(t, t.TransformPoint(new Vector3(sx, 0, sz)), t.TransformDirection(Vector3.right), h, 1.2f, Flags);
        LBox(t, "StairSide", new Vector3(sx + run / 2f, 0.5f, sz - 0.66f), new Vector3(run, 1f, 0.12f), wall, 2f, false);
        float gap = sx + run - 0.7f + hw - 0.15f;   // along the front parapet, from its left end
        Parapet(t, new Vector3(-hw + 0.15f, 0, zf - 0.05f), new Vector3(hw - 0.15f, 0, zf - 0.05f), h, wall, gap, 1.5f);
        Parapet(t, new Vector3(-hw + 0.15f, 0, hd - 0.15f), new Vector3(hw - 0.15f, 0, hd - 0.15f), h, wall);
        Parapet(t, new Vector3(-hw + 0.15f, 0, zf - 0.05f), new Vector3(-hw + 0.15f, 0, hd - 0.15f), h, wall);
        Parapet(t, new Vector3(hw - 0.15f, 0, zf - 0.05f), new Vector3(hw - 0.15f, 0, hd - 0.15f), h, wall);
        // the forecourt wall under its tiles, the gate in front of the door; a pot of flowers, an orange tree
        Parapet(t, new Vector3(-hw + 0.15f, 0, -hd + 0.15f), new Vector3(hw - 0.15f, 0, -hd + 0.15f), 0, wall, door + hw - 0.15f, 1.8f, 1.6f);
        foreach (float s in new[] { -1f, 1f })
            Parapet(t, new Vector3(s * (hw - 0.15f), 0, -hd + 0.15f), new Vector3(s * (hw - 0.15f), 0, zf), 0, wall, -1f, 1.5f, 1.6f);
        var pot = t.TransformPoint(new Vector3(door - 1.5f, 0.08f, -hd + 0.75f));
        MeshGen.TexCylinder("Pot", pot, 0.28f, 0.55f, Terracotta, 1f, t, false);
        Small(Nature.Place("Bush_Common_Flowers", pot + Vector3.up * 0.5f, R(0, 360), 0.7f, t));
        NightLights.Lamp(t, t.TransformPoint(new Vector3(door - 1.5f, 2.6f, zf - 0.34f)), new Vector3(0.2f, 0.3f, 0.2f), night, houses % 3 == 0 ? 2.6f : 0f);
        if (r.NextDouble() < 0.7)
        {
            // the mirador on the roof, at the back on the left: arched openings all round, a tiled cap
            const float rw = 4.4f, rh = 2.9f;
            float x0 = -hw + 0.15f, x1 = x0 + rw, z1 = hd - 0.15f, z0 = z1 - rw;
            ArchWall(t, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), h, rh, 0.3f, wall, (rw / 2f, 1.7f, 0.95f, 0.6f));
            ArchWall(t, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), h, rh, 0.3f, wall, (rw / 2f, 1.9f, 0f, 1.6f));
            ArchWall(t, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), h, rh, 0.3f, wall, (rw / 2f, 1.7f, 0.95f, 0.6f));
            ArchWall(t, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), h, rh, 0.3f, wall, (rw / 2f, 1.7f, 0.95f, 0.6f));
            TiledCap(t, new Vector3((x0 + x1) / 2f, h + rh, (z0 + z1) / 2f), rw + 1.1f, rw + 1.1f, 1.5f);
        }
        else
        {
            // a vine shade on four posts instead
            float px = -hw + 2.6f, pz = hd - 2.6f;
            foreach (float sxp in new[] { -1.6f, 1.6f })
                foreach (float szp in new[] { -1.6f, 1.6f })
                    LBox(t, "ShadePost", new Vector3(px + sxp, h + 1.15f, pz + szp), new Vector3(0.12f, 2.3f, 0.12f), Wood, 1f, false);
            for (float u = -1.8f; u <= 1.81f; u += 0.6f)
                LBox(t, "ShadeLath", new Vector3(px + u, h + 2.34f, pz), new Vector3(0.08f, 0.08f, 3.8f), Wood, 1f, false);
            LBox(t, "ShadeCloth", new Vector3(px, h + 2.4f, pz), new Vector3(3.4f, 0.03f, 3.4f), carpet, 1f, false);
        }
    }

    /// <summary>A market stall: a wooden counter under a cloth awning on four posts, a few jars.</summary>
    private static void Stall(Transform t, Vector3 local, Material cloth, bool along)
    {
        float lx = along ? 2.4f : 1.5f, lz = along ? 1.5f : 2.4f;
        LBox(t, "StallCounter", local + new Vector3(0, 0.45f, 0), new Vector3(lx - 0.3f, 0.9f, lz - 0.6f), Wood, 1f);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                LBox(t, "StallPost", local + new Vector3(sx * (lx / 2f - 0.06f), 1.2f, sz * (lz / 2f - 0.06f)), new Vector3(0.1f, 2.4f, 0.1f), Wood, 1f, false);
        LBox(t, "StallAwning", local + new Vector3(0, 2.43f, 0), new Vector3(lx + 0.4f, 0.05f, lz + 0.4f), cloth, 1f, false);
        var jar = t.TransformPoint(local + new Vector3(along ? 0.5f : 0f, 0.9f, along ? 0f : 0.5f));
        MeshGen.TexCylinder("Jar", jar, 0.18f, 0.36f, Terracotta, 1f, t, false);
        MeshGen.TexCylinder("Jar", jar + t.TransformDirection(along ? Vector3.left : Vector3.back) * 0.6f, 0.14f, 0.28f, Terracotta, 1f, t, false);
    }

    /// <summary>
    /// A row of three shops behind an arcade on columns (front towards local -Z), stalls in the paved
    /// yard before it; the flat roof over shops and arcade is a firing gallery reached by a ladder.
    /// </summary>
    private static void ArcadeShops(Vector3 c, float yaw, float w, float d, int seed)
    {
        houses++;
        var t = Node("ArcadeShops", root, c, yaw);
        var r = new System.Random(seed);
        var wall = TownWall(r);
        const float th = 0.4f, h = 4.4f, shop = 4.2f, walk = 2.6f;
        float hw = w / 2f, hd = d / 2f, zs = hd - shop, za = zs - walk, top = h - 0.25f;
        LBox(t, "YardFloor", new Vector3(0, 0.04f, 0), new Vector3(w, 0.08f, d), Paving, 2f, false);
        LBox(t, "ShopFloor", new Vector3(0, 0.07f, (za + hd) / 2f), new Vector3(w, 0.14f, hd - za), BrickFloor, 2f);
        // the shops: three open fronts, the partitions between them, a window in the back of the middle one
        float bay = (w - th) / 3f;
        ArchWall(t, new Vector3(-hw + th / 2f, 0, zs), new Vector3(hw - th / 2f, 0, zs), 0, top, th, wall,
            (bay * 0.5f, 2.6f, 0f, 1.9f), (bay * 1.5f, 2.6f, 0f, 1.9f), (bay * 2.5f, 2.6f, 0f, 1.9f));
        ArchWall(t, new Vector3(-hw + th / 2f, 0, hd - th / 2f), new Vector3(hw - th / 2f, 0, hd - th / 2f), 0, top, th, wall, (bay * 1.5f, 1.7f, 1.0f, 0.85f));
        Dado(t, new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd), Vector3.forward);
        foreach (float s in new[] { -1f, 1f })
        {
            ArchWall(t, new Vector3(s * (hw - th / 2f), 0, za), new Vector3(s * (hw - th / 2f), 0, hd - th / 2f), 0, top, th, wall, (walk / 2f + 0.1f, 1.9f, 0f, 1.9f));
            Dado(t, new Vector3(s * hw, 0, zs + 0.3f), new Vector3(s * hw, 0, hd), s < 0 ? Vector3.left : Vector3.right);
            Face(t, new Vector3(s * bay / 2f, 0, zs), new Vector3(s * bay / 2f, 0, hd - th / 2f), 0, top, 0.25f, wall);
        }
        for (int i = 0; i < 3; i++)
        {
            float x = -hw + th / 2f + bay * (i + 0.5f);
            LBox(t, "Shelf", new Vector3(x, 1.3f, hd - th - 0.2f), new Vector3(bay - 1.2f, 0.06f, 0.4f), Wood, 1f, false);
            LBox(t, "Counter", new Vector3(x + 0.5f, 0.5f, zs + 1.5f), new Vector3(1.6f, 0.86f, 0.6f), Wood, 1f);
            if (i != 1) MeshGen.TexCylinder("Jar", t.TransformPoint(new Vector3(x - 0.9f, 0.14f, hd - th - 0.5f)), 0.3f, 0.7f, Terracotta, 1f, t);
        }
        // the arcade in front of them
        Arcade(t, t.TransformPoint(new Vector3(-hw + th / 2f, 0.14f, za)), t.TransformDirection(Vector3.right), 3, bay, 2.3f, top - 0.14f, 0.4f, 28f, wall, true);
        LBox(t, "Dado", new Vector3(0, 0.7f, zs - th / 2f - 0.03f), new Vector3(w - th * 2f, 1.1f, 0.05f), Almagra, 1.5f, false);
        // the roof: one terrace over shops and arcade, tiles at the eaves, a parapet all round
        LBox(t, "RoofSlab", new Vector3(0, h - 0.125f, (za - 0.2f + hd) / 2f), new Vector3(w, 0.25f, hd - za + 0.2f), Flags, 2f);
        LBox(t, "Eaves", new Vector3(0, h - 0.3f, za - 0.45f), new Vector3(w + 0.2f, 0.12f, 0.6f), RoofTiles, 1.5f, false);
        Parapet(t, new Vector3(-hw + 0.15f, 0, za - 0.05f), new Vector3(hw - 0.15f, 0, za - 0.05f), h, wall, hw - 0.15f, 1.4f);
        Parapet(t, new Vector3(-hw + 0.15f, 0, hd - 0.15f), new Vector3(hw - 0.15f, 0, hd - 0.15f), h, wall);
        Parapet(t, new Vector3(-hw + 0.15f, 0, za - 0.05f), new Vector3(-hw + 0.15f, 0, hd - 0.15f), h, wall);
        Parapet(t, new Vector3(hw - 0.15f, 0, za - 0.05f), new Vector3(hw - 0.15f, 0, hd - 0.15f), h, wall);
        // the wooden ladder (laid flat enough to walk up) from the yard to the middle of the roof's front
        CityLordBuilder.PlaceLadder(t, t.TransformPoint(new Vector3(0, 0.08f, za - 0.3f)), t.TransformDirection(Vector3.back), h - 0.08f);
        // the yard: two stalls, a lamp under the arcade
        var cloths = new[] { Red, Cream, MarbleBlue, Almagra };
        Stall(t, new Vector3(-hw + 2.2f, 0.08f, -hd + 1.6f), cloths[r.Next(4)], true);
        Stall(t, new Vector3(hw - 2.2f, 0.08f, -hd + 1.6f), cloths[r.Next(4)], true);
        HangingLamp(t, t.TransformPoint(new Vector3(0, top - 0.6f, (za + zs) / 2f)), 0.6f);
        NightLights.Lamp(t, t.TransformPoint(new Vector3(0, top - 1.3f, (za + zs) / 2f)), new Vector3(0.2f, 0.3f, 0.2f), night, 3f);
    }

    /// <summary>
    /// A small market square: paving, a well under its wooden frame, palms, three stalls with coloured
    /// awnings and low stone walls and benches: cover in the open between the houses.
    /// </summary>
    private static void SoukSquare(Vector3 c, float yaw, float w, float d, int seed)
    {
        var t = Node("SoukSquare", root, c, yaw);
        var r = new System.Random(seed);
        float hw = w / 2f, hd = d / 2f;
        LBox(t, "SquareFloor", new Vector3(0, 0.04f, 0), new Vector3(w, 0.08f, d), Paving, 2f, false);
        LBox(t, "SquareBorder", new Vector3(0, 0.05f, 0), new Vector3(w * 0.5f, 0.08f, d * 0.5f), Violet, 2f, false);
        // the well
        var wc = t.TransformPoint(new Vector3(0, 0.08f, 0));
        MeshGen.Object("Well", MeshGen.Cylinder(1f, 0.85f, 1f, 10), Ashlar, t, wc).AddComponent<MeshCollider>();
        var ww = MeshGen.TexCylinder("WellWater", wc + Vector3.up * 0.6f, 0.82f, 0.02f, Water, 2f, t, false);
        ww.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (float s in new[] { -1f, 1f })
            LBox(t, "WellPost", new Vector3(s * 1.05f, 1.3f, 0), new Vector3(0.14f, 2.5f, 0.14f), Wood, 1f);
        LBox(t, "WellBeam", new Vector3(0, 2.5f, 0), new Vector3(2.5f, 0.14f, 0.14f), Wood, 1f, false);
        LBox(t, "WellRoof", new Vector3(0, 2.72f, 0), new Vector3(2.9f, 0.1f, 1.5f), RoofTiles, 1f, false);
        MeshGen.TexCylinder("Bucket", t.TransformPoint(new Vector3(0.3f, 1.25f, 0)), 0.16f, 0.26f, Wood, 1f, t, false);
        LBox(t, "Rope", new Vector3(0.3f, 1.98f, 0), new Vector3(0.03f, 0.96f, 0.03f), Wood, 1f, false);
        // stalls on three sides, the fourth open to the street
        var cloths = new[] { Red, Cream, MarbleBlue, Almagra };
        int k = r.Next(4);
        Stall(t, new Vector3(-hw + 1.5f, 0.08f, 1.2f), cloths[k % 4], false);
        Stall(t, new Vector3(hw - 1.5f, 0.08f, -1.4f), cloths[(k + 1) % 4], false);
        Stall(t, new Vector3(0.8f, 0.08f, hd - 1.4f), cloths[(k + 2) % 4], true);
        // low walls of dressed stone and benches: cover at chest height
        Face(t, new Vector3(-hw + 0.3f, 0, -hd + 0.3f), new Vector3(-hw + 4.2f, 0, -hd + 0.3f), 0, 1.15f, 0.4f, Ashlar);
        Face(t, new Vector3(-hw + 0.3f, 0, -hd + 0.3f), new Vector3(-hw + 0.3f, 0, -hd + 3f), 0, 1.15f, 0.4f, Ashlar);
        Face(t, new Vector3(hw - 0.3f, 0, hd - 0.3f), new Vector3(hw - 4.2f, 0, hd - 0.3f), 0, 1.15f, 0.4f, Ashlar);
        LBox(t, "Bench", new Vector3(-2.6f, 0.3f, -2.9f), new Vector3(2.2f, 0.45f, 0.6f), Marble, 1f);
        LBox(t, "Bench", new Vector3(2.9f, 0.3f, 2.6f), new Vector3(0.6f, 0.45f, 2.2f), Marble, 1f);
        Palm(t, t.TransformPoint(new Vector3(-hw + 1.3f, 0, -hd + 1.5f)), 8.5f);
        Palm(t, t.TransformPoint(new Vector3(hw - 1.4f, 0, hd - 1.6f)), 9.5f);
        OrangeTree(t, t.TransformPoint(new Vector3(hw - 1.6f, 0.05f, -hd + 4.4f)));
        NightLights.Lamp(t, t.TransformPoint(new Vector3(-1.05f, 2.3f, 0)), new Vector3(0.2f, 0.3f, 0.2f), night, 3f);
    }
}
