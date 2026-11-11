using System.Collections.Generic;
using System.IO;
using System.Linq;
using UA;
using UnityEngine;

/// <summary>
/// Steel City's new yards. They are laid out outside the game, as numbers, by
/// Tools/steelcity/layout.py (which also shows them in Blender, so that every joint is looked at
/// before a build) and written to Models/CityLord/extension.json; here exactly that list is put up:
/// pieces of the old town (by name, foot position and turn), plain boxes of the town's asphalt,
/// concrete and wood, ladders, ammunition, boards and spawn points; the old pieces taken away where
/// the yards join; and the invisible edge round the whole town, old and new.
/// </summary>
public static partial class CityLordBuilder
{
    private static Dictionary<string, object> extension;

    private static Dictionary<string, object> Extension
    {
        get
        {
            if (extension != null) return extension;
            string file = Path.Combine(DataFolder, "extension.json");
            extension = File.Exists(file) ? D(Json.Parse(File.ReadAllText(file))) : new Dictionary<string, object>();
            return extension;
        }
    }

    private static IEnumerable<Dictionary<string, object>> Items(string key) =>
        Extension.TryGetValue(key, out var l) ? L(l).Select(D) : Enumerable.Empty<Dictionary<string, object>>();

    private static float Num(Dictionary<string, object> d, string key, float fallback = 0f) => d.TryGetValue(key, out var v) && v is double x ? (float)x : fallback;

    /// <summary>The pieces of the old town taken away where the road goes on and the yards join.</summary>
    private static bool EastGate(string name, Vector3 at)
    {
        foreach (var r in Items("remove"))
        {
            float reach = Num(r, "r", 1f);
            if (name.StartsWith(S(r["name"])) && Mathf.Abs(at.x - F(r["x"])) < reach && Mathf.Abs(at.z - F(r["z"])) < reach) return true;
        }
        return false;
    }

    private static Bounds BuildEast(List<Vector3> spawns)
    {
        var e = Node("NewYards");
        var floorKit = Kit("Road_set_v1_s_floor");
        var asphalt = floorKit != null && floorKit.Materials.Length > 0 ? floorKit.Materials[0] : MaterialNamed("MI_Asphalt");
        var boxKit = Kit("Wooden_box_v1_LD2square");
        var wood = boxKit != null && boxKit.Materials.Length > 0 ? boxKit.Materials[0] : asphalt;
        var wallKit = Kit("UNIConcrete_wall_v1_W_tile_1");
        var concrete = wallKit != null && wallKit.Materials.Length > 0 ? wallKit.Materials[0] : asphalt;
        var total = new Bounds(new Vector3(120f, Lift - 10.9f, -20f), Vector3.one);
        int pieces = 0, boxes = 0, ladders = 0;

        foreach (var p in Items("pieces"))
        {
            var pos = V3(p["pos"]);
            var go = Place(S(p["name"]), e, pos, Num(p, "yaw"), !p.TryGetValue("collider", out var c) || B(c));
            if (go == null) continue;
            if (p.TryGetValue("scale", out var sc)) go.transform.localScale = V3(sc);
            total.Encapsulate(pos);
            pieces++;
        }
        foreach (var b in Items("boxes"))
        {
            var centre = V3(b["c"]);
            var size = V3(b["size"]);
            var rot = Quaternion.Euler(Num(b, "pitch"), Num(b, "yaw"), 0);
            string mat = S(b["mat"]);
            bool collider = !b.TryGetValue("collider", out var c) || B(c);
            GameObject go;
            if (mat == "none")
            {
                go = new GameObject(S(b["name"]));
                go.transform.SetParent(e, false);
            }
            else
            {
                var m = mat == "wood" ? wood : mat == "concrete" ? concrete : mat == "iron" ? RustyIron : asphalt;
                go = MeshGen.Object(S(b["name"]), MeshGen.Box(size, mat == "asphalt" ? 6f : 1.5f), m, e, centre);
            }
            go.transform.SetPositionAndRotation(centre, rot);
            if (collider) go.AddComponent<BoxCollider>().size = size;
            boxes++;
        }
        Physics.SyncTransforms();
        foreach (var l in Items("ladders"))
        {
            PlaceLadder(e, V3(l["foot"]), V3(l["out"]), F(l["height"]));
            Physics.SyncTransforms();
            ladders++;
        }
        foreach (var a in Items("ammo")) Obstacles.AmmoCrate(e, V3(a["pos"]), Num(a, "yaw"));
        foreach (var b in Items("boards")) BoardStand(Node("YardBoard"), V3(b["at"]), S(b["ps"]), S(b["en"]));
        if (Extension.TryGetValue("spawns", out var sp)) foreach (var s in L(sp)) spawns.Add(V3(s));
        foreach (var y in Yards())
        {
            total.Encapsulate(new Vector3(y.xMin, total.center.y, y.yMin));
            total.Encapsulate(new Vector3(y.xMax, total.center.y, y.yMax));
        }
        Debug.Log($"[Map] CityLord: the new yards built from extension.json ({pieces} pieces, {boxes} boxes, {ladders} ladders)");
        return total;
    }

    private static List<Rect> Yards()
    {
        var list = new List<Rect>();
        if (!Extension.TryGetValue("yards", out var ys)) return list;
        foreach (var y in L(ys))
        {
            var v = L(y);
            list.Add(Rect.MinMaxRect(F(v[0]), F(v[1]), F(v[2]), F(v[3])));
        }
        return list;
    }

    /// <summary>
    /// The invisible edge of the town: round the old town's own rectangle joined with the new yards,
    /// following every corner of that outline, so that no yard is cut off and no side is left open.
    /// </summary>
    private static void TownEdge(Transform parent, Bounds old)
    {
        var rects = new List<Rect> { Rect.MinMaxRect(old.min.x - 1.5f, old.min.z - 1.5f, old.max.x + 1.5f, old.max.z + 1.5f) };
        rects.AddRange(Yards().Select(y => Rect.MinMaxRect(y.xMin - 0.9f, y.yMin - 0.9f, y.xMax + 0.9f, y.yMax + 0.9f)));
        var xs = rects.SelectMany(r => new[] { r.xMin, r.xMax }).Distinct().OrderBy(v => v).ToList();
        var zs = rects.SelectMany(r => new[] { r.yMin, r.yMax }).Distinct().OrderBy(v => v).ToList();
        bool In(int i, int k)
        {
            if (i < 0 || k < 0 || i >= xs.Count - 1 || k >= zs.Count - 1) return false;
            var c = new Vector2((xs[i] + xs[i + 1]) / 2f, (zs[k] + zs[k + 1]) / 2f);
            return rects.Any(r => r.Contains(c));
        }
        int walls = 0;
        void Wall(Vector3 c, Vector3 size)
        {
            var wall = new GameObject("Edge");
            wall.layer = World.FenceLayer;
            wall.transform.SetParent(parent, false);
            wall.transform.position = c;
            wall.AddComponent<BoxCollider>().size = size;
            walls++;
        }
        float y = old.center.y;
        for (int i = 0; i < xs.Count - 1; i++)
            for (int k = 0; k < zs.Count - 1; k++)
            {
                if (!In(i, k)) continue;
                float cx = (xs[i] + xs[i + 1]) / 2f, cz = (zs[k] + zs[k + 1]) / 2f, w = xs[i + 1] - xs[i], d = zs[k + 1] - zs[k];
                if (!In(i - 1, k)) Wall(new Vector3(xs[i] - 0.5f, y, cz), new Vector3(1, 80, d + 2f));
                if (!In(i + 1, k)) Wall(new Vector3(xs[i + 1] + 0.5f, y, cz), new Vector3(1, 80, d + 2f));
                if (!In(i, k - 1)) Wall(new Vector3(cx, y, zs[k] - 0.5f), new Vector3(w + 2f, 80, 1));
                if (!In(i, k + 1)) Wall(new Vector3(cx, y, zs[k + 1] + 0.5f), new Vector3(w + 2f, 80, 1));
            }
        Debug.Log($"[Map] CityLord: the town's edge, {walls} invisible walls round {rects.Count} rectangles");
    }
}
