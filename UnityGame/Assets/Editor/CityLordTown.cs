using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// What CityLord gets on top of its own scene: the town named in four parts (a name board on a stand in
/// each, in Pashto and English, as in Al-Zahra), a number plate on every building, and doors opened into
/// five buildings that were closed shells (a doorway cut through the walls and their colliders, the
/// walls, roof and colliders given an inside, crates and barrels inside).
/// </summary>
public static partial class CityLordBuilder
{
    /// <summary>
    /// The buildings opened, at their own doors (the recessed roller doors and side doors of the models,
    /// measured from the meshes): the side (N, S, E, W), where the door runs along the wall (scene x for
    /// N and S, z for E and W) and how high it is above the foot of the building. The first is the main door.
    /// </summary>
    private static readonly (string path, (char side, float from, float to, float top)[] doors)[] Opened =
    {
        ("Map_v1/Static/Hangar_v4", new[] { ('E', -16.3f, -10.39f, 4.22f), ('E', -0.89f, 0.53f, 2.6f) }),
        ("Map_v1/Static/Hangar_v3_basic", new[] { ('E', 27.33f, 32.5f, 3.84f), ('S', 45.25f, 47.39f, 2.49f) }),
        ("Map_v1/Static/Hangar_v3_basic (2)", new[] { ('W', -32.35f, -27.17f, 3.84f), ('N', -9.6f, -7.46f, 2.49f) }),
        ("Map_v1/Static/Hangar_v1_full", new[] { ('N', 8.55f, 11.29f, 3.14f) }),
        ("Map_v1/Static/Hangar_v1_full (1)", new[] { ('W', 24.64f, 27.37f, 3.14f) }),
        ("Map_v1/Static/Hangar_v4 (6)", new[] { ('W', -20.71f, -14.8f, 4.22f), ('W', -31.63f, -30.21f, 2.6f) }),
    };

    /// <summary>The four parts of the town: letter, Pashto and English name, where the board stands.</summary>
    private static readonly (char id, string pashto, string english, Vector2 board)[] Districts =
    {
        ('A', "د کانټینرونو انګړ", "CONTAINER YARD", new Vector2(-12f, 6f)),
        ('B', "د تېلو او نلونو کارخانه", "OIL AND PIPE WORKS", new Vector2(36f, 8f)),
        ('C', "د هنګرونو کتار", "HANGAR ROW", new Vector2(80f, -2f)),
        ('D', "سویلي فابریکې", "SOUTH FACTORIES", new Vector2(22f, -30f)),
    };

    /// <summary>The middle of the yards (scene coordinates): boards and plates face it.</summary>
    private static readonly Vector3 TownMiddle = new Vector3(30f, -11f, -6f);

    /// <summary>The highest street of the town (scene y): container tops, decks and roofs are above it.</summary>
    private const float StreetTop = -9.6f;

    private static char DistrictOf(Vector3 c) => c.z < -30f && c.x < 92f ? 'D' : c.x < 18f ? 'A' : c.x < 62f ? 'B' : 'C';

    private static Vector3 Outward(char side) => side == 'N' ? Vector3.forward : side == 'S' ? Vector3.back : side == 'E' ? Vector3.right : Vector3.left;

    /// <summary>The side of a building that looks towards the middle of the town.</summary>
    private static char FacingSide(Bounds b)
    {
        var d = TownMiddle - b.center;
        if (Mathf.Abs(d.x) / Mathf.Max(1f, b.extents.x) > Mathf.Abs(d.z) / Mathf.Max(1f, b.extents.z)) return d.x > 0 ? 'E' : 'W';
        return d.z > 0 ? 'N' : 'S';
    }

    /// <summary>The highest walkable ground under a point (scene coordinates) no higher than 'below'.</summary>
    private static float Ground(Vector3 at, float below, float fallback)
    {
        var top = root.TransformPoint(new Vector3(at.x, below + 0.5f, at.z));
        float best = float.MinValue;
        foreach (var h in Physics.RaycastAll(top, Vector3.down, 40f, World.EnvMask, QueryTriggerInteraction.Ignore))
        {
            float y = root.InverseTransformPoint(h.point).y;
            if (h.normal.y > 0.7f && y <= below && y > best) best = y;
        }
        return best > float.MinValue ? best : fallback;
    }

    // ------------------------------------------------------------------ doors

    private static int OpenBuildings(Transform geometry, Transform colliders, Dictionary<string, List<GameObject>> parts, Dictionary<string, Bounds> outlines,
                                     Dictionary<string, (List<Vector3> v, List<int> t)> shells, List<MeshCollider> cells)
    {
        var insides = Node("Insides");
        int opened = 0;
        foreach (var (path, doorList) in Opened)
        {
            if (!outlines.TryGetValue(path, out var b)) { Debug.LogWarning("[Map] CityLord: no building " + path); continue; }
            var boxes = new List<Bounds>();
            foreach (var d in doorList)
            {
                var n = Outward(d.side);
                var face = b.center + Vector3.Scale(n, b.extents);
                float ground = Ground(face + n * 1.5f, b.min.y + 1.5f, b.min.y);
                var door = DoorBox(b, d.side, d.from, d.to, ground, b.min.y + d.top);
                boxes.Add(door);
                // the doorway through every piece there (the door leaf, the wall, their colliders)
                foreach (Transform t in geometry)
                {
                    var f = t.GetComponent<MeshFilter>();
                    if (f != null && f.sharedMesh != null && f.sharedMesh.bounds.Intersects(door)) f.sharedMesh = Cut(f.sharedMesh, door);
                }
                foreach (var cell in cells)
                    if (cell.sharedMesh.bounds.Intersects(door)) cell.sharedMesh = Cut(cell.sharedMesh, door);
            }

            // the inside: the walls, roof and their colliders seen from within
            if (parts.TryGetValue(path, out var gos))
                foreach (var go in gos)
                {
                    var inside = new GameObject(go.name + "_Inside");
                    inside.transform.SetParent(insides, false);
                    inside.AddComponent<MeshFilter>().sharedMesh = Reversed(go.GetComponent<MeshFilter>().sharedMesh);
                    var r = inside.AddComponent<MeshRenderer>();
                    r.sharedMaterials = go.GetComponent<MeshRenderer>().sharedMaterials;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.lightProbeUsage = LightProbeUsage.Off;
                }
            if (shells.TryGetValue(path, out var shell))
            {
                var mesh = new Mesh { name = "CityLord_Inside_" + opened };
                mesh.SetVertices(shell.v);
                mesh.SetTriangles(shell.t, 0);
                foreach (var door in boxes) mesh = Cut(mesh, door);
                mesh = Reversed(mesh);
                var go = new GameObject("InsideCollision");
                go.transform.SetParent(colliders, false);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            var main = doorList[0];
            Furnish(insides, b, Outward(main.side), boxes[0].min.y - 0.04f);
            opened++;
        }
        Physics.SyncTransforms();
        return opened;
    }

    /// <summary>
    /// A doorway: the door's own width and height (a few centimetres inside its frame), from the street
    /// level up, through the wall on that side from 1 m outside to 2.6 m in (the door leaf sits back in the wall).
    /// </summary>
    private static Bounds DoorBox(Bounds b, char side, float from, float to, float ground, float top)
    {
        var n = Outward(side);
        var face = b.center + Vector3.Scale(n, b.extents);
        const float outside = 1f, inside = 2.6f;
        var center = face + n * ((outside - inside) / 2f);
        float a0 = Mathf.Min(from, to) + 0.04f, a1 = Mathf.Max(from, to) - 0.04f;
        float bottom = ground + 0.04f, height = Mathf.Max(0.5f, top - 0.03f - bottom);
        center.y = bottom + height / 2f;
        bool alongZ = Mathf.Abs(n.x) > 0.5f;
        if (alongZ) center.z = (a0 + a1) / 2f; else center.x = (a0 + a1) / 2f;
        var size = alongZ ? new Vector3(outside + inside, height, a1 - a0) : new Vector3(a1 - a0, height, outside + inside);
        return new Bounds(center, size);
    }

    /// <summary>Crates against the back wall, barrels in a corner, pallets by a side wall.</summary>
    private static void Furnish(Transform parent, Bounds b, Vector3 n, float ground)
    {
        var u = new Vector3(n.z, 0, -n.x);   // along the door wall
        float depth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(n.x), 0, Mathf.Abs(n.z))));
        float wide = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(u.x), 0, Mathf.Abs(u.z))));
        float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
        Vector3 At(float back, float side) => root.TransformPoint(new Vector3(b.center.x, ground, b.center.z) - n * back + u * side);
        float inset = Mathf.Min(1.6f, depth * 0.3f);
        Place("Wooden_box_v1_LD2", parent, At(depth - inset, wide * 0.35f), yaw + 90f);
        Place("Wooden_box_v1_LD2square", parent, At(depth - inset, wide * 0.35f) + Vector3.up * 0.67f, yaw + 12f);
        Place("Wooden_box_v1_LD2square", parent, At(depth - inset, wide * 0.35f - 1.4f), yaw - 7f);
        Place("Barrel_v3_quadro", parent, At(depth - inset, -wide * 0.4f), yaw);
        if (depth > 4f) Place("Palet_v1_set", parent, At(0f, -wide + 1.6f), yaw + 90f);
    }

    // ------------------------------------------------------------------ cutting

    private struct Vtx
    {
        public Vector3 P, N;
        public Vector2 UV;
        public Vector4 T;

        public static Vtx Lerp(Vtx a, Vtx b, float t) => new Vtx
        {
            P = Vector3.Lerp(a.P, b.P, t),
            N = Vector3.Lerp(a.N, b.N, t).normalized,
            UV = Vector2.Lerp(a.UV, b.UV, t),
            T = Vector4.Lerp(a.T, b.T, t),
        };
    }

    /// <summary>A mesh with everything inside the box taken out (triangles across its faces are cut along them).</summary>
    private static Mesh Cut(Mesh mesh, Bounds box)
    {
        var p = mesh.vertices;
        var nrm = mesh.normals;
        var uv = mesh.uv;
        var tan = mesh.tangents;
        bool hasN = nrm.Length == p.Length, hasUV = uv.Length == p.Length, hasT = tan.Length == p.Length;
        var verts = new List<Vtx>(p.Length);
        for (int i = 0; i < p.Length; i++)
            verts.Add(new Vtx { P = p[i], N = hasN ? nrm[i] : Vector3.up, UV = hasUV ? uv[i] : Vector2.zero, T = hasT ? tan[i] : new Vector4(1, 0, 0, 1) });
        var subs = new List<List<int>>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            var kept = new List<int>(tris.Length);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vtx a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                var tb = new Bounds(a.P, Vector3.zero);
                tb.Encapsulate(b.P);
                tb.Encapsulate(c.P);
                if (!Overlap(tb, box)) { kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]); continue; }
                foreach (var poly in Subtract(new List<Vtx> { a, b, c }, box))
                {
                    int first = verts.Count;
                    verts.AddRange(poly);
                    for (int k = 1; k + 1 < poly.Count; k++) { kept.Add(first); kept.Add(first + k); kept.Add(first + k + 1); }
                }
            }
            subs.Add(kept);
        }
        var cut = new Mesh { name = mesh.name };
        if (verts.Count > 65535) cut.indexFormat = IndexFormat.UInt32;
        cut.SetVertices(verts.Select(v => v.P).ToList());
        if (hasN) cut.SetNormals(verts.Select(v => v.N).ToList());
        if (hasUV) cut.SetUVs(0, verts.Select(v => v.UV).ToList());
        if (hasT) cut.SetTangents(verts.Select(v => v.T).ToList());
        cut.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) cut.SetTriangles(subs[s], s, false);
        cut.RecalculateBounds();
        return cut;
    }

    private static bool Overlap(Bounds a, Bounds b) =>
        a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y && a.min.z < b.max.z && a.max.z > b.min.z;

    /// <summary>A convex polygon minus a box: the parts outside each face of the box in turn.</summary>
    private static List<List<Vtx>> Subtract(List<Vtx> poly, Bounds box)
    {
        var keep = new List<List<Vtx>>();
        var rest = poly;
        for (int axis = 0; axis < 3 && rest.Count >= 3; axis++)
            for (int s = -1; s <= 1 && rest.Count >= 3; s += 2)
            {
                float bound = s < 0 ? box.min[axis] : box.max[axis];
                var outside = new List<Vtx>();
                var inside = new List<Vtx>();
                for (int i = 0; i < rest.Count; i++)
                {
                    var a = rest[i];
                    var b = rest[(i + 1) % rest.Count];
                    float da = s * (a.P[axis] - bound), db = s * (b.P[axis] - bound);
                    if (da > 0f) outside.Add(a); else inside.Add(a);
                    if ((da > 0f) != (db > 0f))
                    {
                        var m = Vtx.Lerp(a, b, da / (da - db));
                        outside.Add(m);
                        inside.Add(m);
                    }
                }
                if (outside.Count >= 3) keep.Add(outside);
                rest = inside;
            }
        return keep;   // what is left lies inside the box
    }

    /// <summary>The same faces seen from the other side.</summary>
    private static Mesh Reversed(Mesh mesh)
    {
        var copy = new Mesh { name = mesh.name + "_Inside", indexFormat = mesh.indexFormat };
        copy.vertices = mesh.vertices;
        var normals = mesh.normals;
        for (int i = 0; i < normals.Length; i++) normals[i] = -normals[i];
        if (normals.Length > 0) copy.normals = normals;
        if (mesh.uv.Length > 0) copy.uv = mesh.uv;
        copy.subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var t = mesh.GetTriangles(s);
            for (int i = 0; i + 2 < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            copy.SetTriangles(t, s, false);
        }
        copy.RecalculateBounds();
        return copy;
    }

    // ------------------------------------------------------------------ names and numbers

    // the boards are made of the town's own old iron: the rusty steel of its ladder (Ladder material)
    private static Material RustyIron => MaterialNamed("Ladder") ?? AssetSetup.Solid("CL_SignSteel", new Color(0.3f, 0.2f, 0.14f), 0.4f, 0.6f);
    private static Material SignBlue => RustyIron;
    private static Material SignSteel => RustyIron;
    private static Material SignYellow => AssetSetup.Solid("CL_SignDark", new Color(0.12f, 0.08f, 0.06f), 0.4f, 0.7f);

    /// <summary>Name boards for the four parts of the town and a number plate on every building; returns the buildings numbered.</summary>
    private static int NameTown(Dictionary<string, Bounds> outlines)
    {
        var signs = Node("Signs");
        foreach (var (id, pashto, english, at) in Districts)
            BoardStand(signs, new Vector3(at.x, 0, at.y), pashto, $"{english}  -  {id}");

        Garden(signs);

        var buildings = outlines.Where(kv =>
        {
            string name = kv.Key.Substring(kv.Key.LastIndexOf('/') + 1);
            return (name.StartsWith("Hangar_") || name.StartsWith("Oil_tank_v1")) && kv.Value.size.x > 3f && kv.Value.size.z > 3f;
        }).ToList();
        int count = 0;
        numbered.Clear();
        foreach (var group in buildings.GroupBy(kv => DistrictOf(kv.Value.center)).OrderBy(g => g.Key))
        {
            int number = 0;
            foreach (var (path, b) in group.OrderBy(kv => Mathf.Round(kv.Value.center.x / 10f)).ThenBy(kv => kv.Value.center.z).Select(kv => (kv.Key, kv.Value)))
            {
                number++;
                var opened = Opened.FirstOrDefault(o => o.path == path);
                bool open = opened.path != null;
                var main = open ? opened.doors[0] : default;
                char side = open ? main.side : FacingSide(b);
                bool tank = path.Contains("Oil_tank");
                // on an opened building: right over its main door
                Plate(signs, path, b, side, open ? main.top + 0.7f : tank ? 3.2f : 3.6f, tank ? "د تېلو ټانک" : open ? "ګودام - خلاص" : "ګودام", $"{group.Key}-{number}",
                      open ? (main.from + main.to) / 2f : (float?)null);
                numbered[$"{group.Key}-{number}"] = b;
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// A small green garden in the town: a bed of grass (10 x 6 m) with square-cut green walls round it
    /// (an opening on one long side) and two trees, on free street ground near the middle of the yards.
    /// </summary>
    private static void Garden(Transform parent)
    {
        int mask = World.EnvMask | (1 << World.FenceLayer);
        const float w = 10f, l = 6f;
        var rng = new System.Random(5);
        float Ground(Vector3 p, float near)
        {
            return Physics.Raycast(new Vector3(p.x, near + 2.5f, p.z), Vector3.down, out var h, 8f, World.EnvMask, QueryTriggerInteraction.Ignore) ? h.point.y : near - 10f;
        }
        // a flight of steps going down from beside the bed: the ground falls in several small steps
        // (not one drop over a wall, not a slope); returns how far out it starts, or -1
        float StairOut(Vector3 edge, Vector3 outward, float y)
        {
            float last = y, drop = 0f, first = -1f;
            int steps = 0;
            for (float d = 0.15f; d <= 3.5f; d += 0.1f)
            {
                float g = Ground(edge + outward * d, y);
                float step = last - g;
                if (step > 0.45f) return steps >= 3 ? first : -1f;
                if (step > 0.07f) { steps++; drop += step; if (first < 0f) first = d; }
                else if (step < -0.07f) return -1f;
                last = g;
                if (steps >= 4 && drop > 0.5f) return first;
            }
            return steps >= 3 && drop > 0.45f ? first : -1f;
        }
        // where steps come up beside the bed: on which side (0 = the open long side, -z) and where along it
        (int side, float u, float width, float start) FindStair(Vector3 c, Quaternion rot)
        {
            var sides = new[] { (Vector3.back, Vector3.right, w, l), (Vector3.forward, Vector3.right, w, l), (Vector3.left, Vector3.forward, l, w), (Vector3.right, Vector3.forward, l, w) };
            for (int k = 0; k < 4; k++)
            {
                var (n, along, len, across) = sides[k];
                float lo = float.MaxValue, hi = float.MinValue, start = 9f;
                for (float u = -len / 2f - 2.5f; u <= len / 2f + 2.5f; u += 0.1f)
                {
                    var edge = c + rot * (n * (across / 2f) + along * u);
                    float o = StairOut(edge, rot * n, c.y);
                    if (o < 0f) continue;
                    lo = Mathf.Min(lo, u); hi = Mathf.Max(hi, u); start = Mathf.Min(start, o);
                }
                if (hi >= lo) return (k, (lo + hi) / 2f, hi - lo + 0.1f, start);
            }
            return (-1, 0f, 0f, 0f);
        }
        bool Fits(Vector3 c, Quaternion rot)
        {
            if (Physics.CheckBox(c + Vector3.up * 1.6f, new Vector3(w / 2f + 1.5f, 1.45f, l / 2f + 1.5f), rot, mask, QueryTriggerInteraction.Ignore)) return false;
            foreach (var k in new[] { new Vector3(-w / 2f, 0, -l / 2f), new Vector3(w / 2f, 0, -l / 2f), new Vector3(-w / 2f, 0, l / 2f), new Vector3(w / 2f, 0, l / 2f), Vector3.zero })
                if (Mathf.Abs(Ground(c + rot * k, c.y) - c.y) > 0.15f) return false;
            return true;
        }
        // best: square in front of steps that come up beside it; else a spot with no steps beside it at
        // all; else any free level spot
        (Vector3 c, Quaternion rot, float yaw, float gap)? clear = null, any = null;
        foreach (var (x, z) in new[] { (30f, -6f), (24f, 4f), (38f, -20f), (10f, -12f), (60f, 10f), (0f, 2f), (44f, 12f), (16f, -26f), (70f, -26f), (-10f, -14f) })
            for (int ring = 0; ring < 4; ring++)
                for (int k = 0; k < Mathf.Max(1, ring * 6); k++)
                    foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                    {
                        float a = k * Mathf.PI * 2f / Mathf.Max(1, ring * 6);
                        var probe = new Vector3(x, 0, z) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (ring * 3f);
                        var top = root.TransformPoint(new Vector3(probe.x, 25f, probe.z));
                        if (!Physics.Raycast(top, Vector3.down, out var hit, 60f, World.EnvMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.95f) continue;
                        if (root.InverseTransformPoint(hit.point).y > StreetTop) continue;
                        var rot = Quaternion.Euler(0, yaw, 0);
                        var c = hit.point;
                        if (!Fits(c, rot)) continue;
                        if (any == null) any = (c, rot, yaw, 1.6f);
                        // steps coming up beside it: only in front of the opening, square on to it, the
                        // bed standing back from the top step (the way up leads straight in through the gap)
                        var (side, u, sw, start) = FindStair(c, rot);
                        if (side < 0) { if (clear == null) clear = (c, rot, yaw, 1.6f); continue; }
                        if (side > 0) continue;
                        c += rot * new Vector3(u, 0, Mathf.Max(0f, 1.4f - start));
                        if (!Fits(c, rot)) continue;
                        var again = FindStair(c, rot);
                        if (again.side != 0 || Mathf.Abs(again.u) > 0.4f) continue;
                        // and once more for the last few centimetres: the middle of the gap exactly on the middle of the steps
                        if (Mathf.Abs(again.u) > 0.03f)
                        {
                            var nudged = c + rot * new Vector3(again.u, 0, 0);
                            (int side, float u, float width, float start) third = Fits(nudged, rot) ? FindStair(nudged, rot) : (-1, 0f, 0f, 0f);
                            if (third.side == 0 && Mathf.Abs(third.u) < Mathf.Abs(again.u)) { c = nudged; again = third; }
                        }
                        float gap = Mathf.Clamp(again.width, 1.6f, w - 3f);
                        BuildGarden(parent, c, rot, yaw, w, l, gap, rng);
                        Debug.Log($"[Map] CityLord garden at {root.InverseTransformPoint(c)} yaw {yaw}, facing steps {gap:0.0} m wide");
                        return;
                    }
        var pick = clear ?? any;
        if (pick.HasValue)
        {
            var (pc, prot, pyaw, pgap) = pick.Value;
            BuildGarden(parent, pc, prot, pyaw, w, l, pgap, rng);
            Debug.Log($"[Map] CityLord garden at {root.InverseTransformPoint(pc)} yaw {pyaw}" + (clear.HasValue ? ", no steps beside it" : ""));
            return;
        }
        Debug.LogWarning("[Map] CityLord: no room for the garden");
    }

    private static void BuildGarden(Transform parent, Vector3 at, Quaternion rot, float yaw, float w, float l, float gap, System.Random rng)
    {
        var g = new GameObject("Garden").transform;
        g.SetParent(parent, false);
        g.SetPositionAndRotation(at, rot);
        var grass = Mats.Pbr("CL_Grass", "Grass004", new Color(0.85f, 0.95f, 0.8f), 0.15f);
        var green = AssetSetup.ExtraTex("GreenWall_BaseColor.jpg");
        var hedge = AssetSetup.Mat("CL_Hedge", new Color(0.85f, 1f, 0.8f), 0.25f, 0f, green, AssetSetup.ExtraTex("GreenWall_Normal.jpg"));
        MeshGen.TexBox("GardenBed", g.TransformPoint(new Vector3(0, 0.04f, 0)), new Vector3(w, 0.08f, l), grass, 3f, g, yaw, false);
        // square-cut green walls round it, an opening in the middle of the near long side
        const float hh = 0.9f, hw = 0.6f;
        MeshGen.TexBox("Hedge", g.TransformPoint(new Vector3(0, hh / 2f, l / 2f - hw / 2f)), new Vector3(w, hh, hw), hedge, 1.1f, g, yaw);
        foreach (float s in new[] { -1f, 1f })
        {
            MeshGen.TexBox("Hedge", g.TransformPoint(new Vector3(s * (w / 2f - hw / 2f), hh / 2f, 0)), new Vector3(hw, hh, l - 2f * hw), hedge, 1.1f, g, yaw);
            float seg = (w - gap) / 2f;
            MeshGen.TexBox("Hedge", g.TransformPoint(new Vector3(s * (gap / 2f + seg / 2f), hh / 2f, -l / 2f + hw / 2f)), new Vector3(seg, hh, hw), hedge, 1.1f, g, yaw);
        }
        foreach (float s in new[] { -1f, 1f })
            Nature.Tree(Nature.Trees, rng, g.TransformPoint(new Vector3(s * 2.6f, 0.08f, 0.6f)), 4.5f, 6f, g);
        // a garden lantern on a post by the back hedge, lighting the bed and whoever stands in it at night
        var post = g.TransformPoint(new Vector3(0, 0.08f, l / 2f - 1.1f));
        MeshGen.TexBox("LanternPost", post + Vector3.up * 1.4f, new Vector3(0.1f, 2.8f, 0.1f), NightLights.LampMetal, 1f, g, yaw, true);
        MeshGen.TexBox("LanternFoot", post + Vector3.up * 0.1f, new Vector3(0.26f, 0.2f, 0.26f), NightLights.LampMetal, 1f, g, yaw, false);
        NightLights.Lamp(g, post + Vector3.up * 3f, new Vector3(0.26f, 0.36f, 0.26f), night, 3.6f);
        LampLight(g, post + Vector3.up * 2.8f, 8f);
        Physics.SyncTransforms();
    }

    /// <summary>A small blue plate with the building's kind and its number, on the wall of the given side.</summary>
    private static void Plate(Transform parent, string path, Bounds b, char side, float height, string kind, string number, float? along = null)
    {
        var n = Outward(side);
        var face = b.center + Vector3.Scale(n, b.extents);
        if (along.HasValue)
        {
            if (Mathf.Abs(n.x) > 0.5f) face.z = along.Value; else face.x = along.Value;
        }
        float ground = Ground(face + n * 1.5f, b.min.y + 1.5f, b.min.y);
        float y = Mathf.Min(ground + height, b.max.y - 0.6f);
        var local = new Vector3(face.x, y, face.z);
        // on the wall itself where it stands back under the eaves
        var from = root.TransformPoint(local + n * 2f);
        if (Physics.Raycast(from, -n, out var hit, b.size.magnitude, World.EnvMask, QueryTriggerInteraction.Ignore))
        {
            var wall = root.InverseTransformPoint(hit.point);
            var grown = b;
            grown.Expand(0.4f);
            if (grown.Contains(wall)) local = wall;
        }
        local += n * 0.06f;
        float yaw = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg;   // the plate is read from its local -Z side
        var t = new GameObject("Plate " + number).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(root.TransformPoint(local), Quaternion.Euler(0, yaw, 0));
        MeshGen.TexBox("PlateBoard", t.position, new Vector3(1.6f, 0.9f, 0.05f), SignBlue, 1f, t, yaw, false);
        MeshGen.TexBox("PlateRim", t.TransformPoint(new Vector3(0, 0.47f, 0)), new Vector3(1.66f, 0.05f, 0.07f), SignYellow, 1f, t, yaw, false);
        MeshGen.TexBox("PlateRim", t.TransformPoint(new Vector3(0, -0.47f, 0)), new Vector3(1.66f, 0.05f, 0.07f), SignYellow, 1f, t, yaw, false);
        ZahraBuilder.Writing(t, new Vector3(0, 0.22f, -0.04f), new Vector2(1.45f, 0.32f), kind, 26f, Color.white);
        ZahraBuilder.Writing(t, new Vector3(0, -0.14f, -0.04f), new Vector2(1.45f, 0.44f), number, 40f, new Color(1f, 0.85f, 0.3f));
        // a lantern on a bracket over the plate: warm light on the wall and the street at night
        MeshGen.TexBox("LampArm", t.TransformPoint(new Vector3(0, 0.98f, -0.2f)), new Vector3(0.05f, 0.05f, 0.42f), NightLights.LampMetal, 1f, t, yaw, false);
        NightLights.Lamp(t, t.TransformPoint(new Vector3(0, 0.82f, -0.42f)), new Vector3(0.22f, 0.32f, 0.22f), night, 3.8f);
        LampLight(t, t.TransformPoint(new Vector3(0, 0.7f, -0.6f)), 7f);
        NightLights.Pool(t, t.TransformPoint(new Vector3(0, 0.6f, -0.08f)), -t.forward, 1.6f, night, 0.9f);
    }

    private static readonly List<Light> lamps = new List<Light>();
    /// <summary>The numbered buildings (scene coordinates) by their number, as "D-4".</summary>
    private static readonly Dictionary<string, Bounds> numbered = new Dictionary<string, Bounds>();

    /// <summary>
    /// A second double pipe like the one that runs from the roof of the pipe works over to the oil tank
    /// D-5: from the same roof over to the roof of the warehouse D-4. It rises out of the roof, runs
    /// level on steel frames over the yard (clear of the tank) and goes down into the warehouse roof;
    /// one can walk along its top like along the other.
    /// </summary>
    private static void RoofPipe(Dictionary<string, Bounds> outlines)
    {
        if (!numbered.TryGetValue("D-4", out var d4) || !numbered.TryGetValue("D-5", out var d5)) { Debug.LogWarning("[Map] CityLord: no D-4 / D-5 for the roof pipe"); return; }
        string Name(string path) => path.Substring(path.LastIndexOf('/') + 1);
        var pipes = outlines.Where(kv => Name(kv.Key).StartsWith("Pipes_set_v1_H_set") || Name(kv.Key).StartsWith("Pipes_set_v1_V_set") || Name(kv.Key).StartsWith("Pipes_set_v1_tile"))
                            .Select(kv => kv.Value).ToList();
        var near5 = d5;
        near5.Expand(4f);
        var chain = pipes.Where(b => b.Intersects(near5)).ToList();
        if (chain.Count == 0) { Debug.LogWarning("[Map] CityLord: no pipe at D-5"); return; }
        for (bool grown = true; grown;)
        {
            grown = false;
            foreach (var b in pipes)
                if (!chain.Contains(b) && chain.Any(c => { var e = c; e.Expand(2f); return e.Intersects(b); })) { chain.Add(b); grown = true; }
        }
        var all = chain[0];
        foreach (var b in chain) all.Encapsulate(b);
        // the end of the pipe works away from the tank: there the old pipe comes out of its roof
        var c5 = new Vector3(d5.center.x, 0, d5.center.z);
        Vector3 far = all.size.z >= all.size.x
            ? new Vector3(all.center.x, 0, Mathf.Abs(all.min.z - c5.z) > Mathf.Abs(all.max.z - c5.z) ? all.min.z : all.max.z)
            : new Vector3(Mathf.Abs(all.min.x - c5.x) > Mathf.Abs(all.max.x - c5.x) ? all.min.x : all.max.x, 0, all.center.z);
        float Surface(Vector3 at, float below, float flat)
        {
            float best = float.MinValue;
            foreach (var h in Physics.RaycastAll(root.TransformPoint(new Vector3(at.x, below, at.z)), Vector3.down, 40f, World.EnvMask, QueryTriggerInteraction.Ignore))
            {
                float y = root.InverseTransformPoint(h.point).y;
                if (h.normal.y > flat && y > best) best = y;
            }
            return best;
        }
        // the start: on that roof, a little in from the end of the old pipe and beside it
        var c4 = new Vector3(d4.center.x, 0, d4.center.z);
        var toward = (c4 - far); toward.y = 0; toward.Normalize();
        Vector3 start = far, end = default;
        float roofY = float.MinValue, d4Y = float.MinValue, level = 0f;
        var side = Vector3.Cross(Vector3.up, toward);
        bool found = false;
        foreach (float off in new[] { 2.2f, -2.2f, 3.6f, -3.6f, 5f, -5f })
        {
            var s0 = far + side * off - toward * 1.5f;
            float ry = Surface(s0, all.max.y + 3f, 0.9f);
            if (ry < all.min.y - 6f || ry > all.max.y + 1f) continue;
            // the end: on the warehouse roof just inside its edge towards the start
            var e0 = new Vector3(Mathf.Clamp(s0.x, d4.min.x + 1.5f, d4.max.x - 1.5f), 0, Mathf.Clamp(s0.z, d4.min.z + 1.5f, d4.max.z - 1.5f));
            float ey = Surface(e0, d4.max.y + 2f, 0.5f);
            if (ey < d4.min.y + 1f) continue;
            float lv = Mathf.Max(ry, ey) + 0.9f;
            var a = root.TransformPoint(new Vector3(s0.x, lv, s0.z));
            var b = root.TransformPoint(new Vector3(e0.x, lv, e0.z));
            var dir = b - a;
            // clear all the way (the tank, the old pipe, other roofs)
            if (Physics.SphereCast(a - dir.normalized * 0.2f, 0.55f, dir.normalized, out _, dir.magnitude - 1.2f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
            start = s0; end = e0; roofY = ry; d4Y = ey; level = lv; found = true;
            break;
        }
        if (!found) { Debug.LogWarning("[Map] CityLord: no clear way for the roof pipe to D-4"); return; }
        var t = new GameObject("RoofPipe").transform;
        t.SetParent(root, false);
        var A = new Vector3(start.x, level, start.z);
        var B = new Vector3(end.x, level, end.z);
        var run = B - A;
        float length = run.magnitude;
        var along = run / length;
        var across = Vector3.Cross(Vector3.up, along);
        t.localPosition = (A + B) / 2f;
        t.localRotation = Quaternion.LookRotation(along);
        var piece = Kit("Pipes_set_v1_H_set_v2") ?? Kit("Pipes_set_v1_H_set_v1");
        var pipeMat = piece != null && piece.Materials.Length > 0 && piece.Materials[0] != null ? piece.Materials[0] : AssetSetup.RustyPost;
        var steel = AssetSetup.RustyPost;
        const float r = 0.28f, gap = 0.36f;
        GameObject Tube(string name, Vector3 from, Vector3 to, float radius, Material m)
        {
            var d = to - from;
            var go = MeshGen.Object(name, MeshGen.Cylinder(radius, d.magnitude, 2f, 14), m, t, root.TransformPoint(from));
            go.transform.rotation = root.rotation * Quaternion.FromToRotation(Vector3.up, d.normalized);
            go.isStatic = true;
            return go;
        }
        foreach (float sd in new[] { -1f, 1f })
        {
            var o = across * sd * gap;
            // level run, the two bends down, and down into the roofs with a collar where they enter
            Tube("Pipe", A + o, B + o, r, pipeMat);
            Tube("PipeDown", A + o + Vector3.up * 0.0f, new Vector3(A.x, roofY - 0.1f, A.z) + o, r, pipeMat);
            Tube("PipeDown", B + o, new Vector3(B.x, d4Y - 0.3f, B.z) + o, r, pipeMat);
            Tube("Collar", new Vector3(A.x, roofY - 0.05f, A.z) + o, new Vector3(A.x, roofY + 0.18f, A.z) + o, r + 0.08f, steel);
            Tube("Collar", new Vector3(B.x, d4Y - 0.05f, B.z) + o, new Vector3(B.x, d4Y + 0.18f, B.z) + o, r + 0.08f, steel);
            foreach (var p in new[] { A, B })
                MeshGen.Object("Elbow", MeshGen.Cylinder(r + 0.04f, 0.5f, 1f, 14), pipeMat, t, root.TransformPoint(p + o - Vector3.up * 0.25f)).isStatic = true;
            // flanges along the run
            for (float u = 3f; u < length - 1f; u += 6f)
                Tube("Flange", A + o + along * u, A + o + along * (u + 0.08f), r + 0.07f, steel);
        }
        // steel frames on the ground under the run (wherever it is not over a roof), one every 6 m
        for (float u = 3f; u < length - 2f; u += 6f)
        {
            var p = A + along * u;
            float g = Surface(p, level - 0.6f, 0.7f);
            if (g == float.MinValue || level - g < 1.2f) continue;
            foreach (float sd in new[] { -1f, 1f })
            {
                var foot = new Vector3(p.x, g, p.z) + across * sd * 0.85f;
                MeshGen.TexBox("FramePost", root.TransformPoint(foot + Vector3.up * ((level - r - g) / 2f)), new Vector3(0.18f, level - r - g, 0.18f), steel, 1f, t, Quaternion.LookRotation(along).eulerAngles.y, true);
            }
            MeshGen.TexBox("FrameBeam", root.TransformPoint(p - Vector3.up * (r + 0.08f)), new Vector3(2f, 0.16f, 0.2f), steel, 1f, t, Quaternion.LookRotation(along).eulerAngles.y, false);
        }
        // the walk along the top of the two pipes (and the colliders of the pipes themselves)
        var walk = new GameObject("PipeWalk");
        walk.transform.SetParent(t, false);
        walk.transform.localPosition = Vector3.up * (r - 0.02f);
        walk.AddComponent<BoxCollider>().size = new Vector3(gap * 2f + r * 2f, 0.1f, length + 0.4f);
        var body = new GameObject("PipeBody");
        body.transform.SetParent(t, false);
        body.AddComponent<BoxCollider>().size = new Vector3(gap * 2f + r * 2f, r * 2f, length);
        Debug.Log($"[Map] CityLord: roof pipe to D-4 from {start} to {end}, {length:0.0} m at {level:0.0}");
    }

    /// <summary>
    /// The real light of a lamp: a warm point light lit per vertex and only on the players and their
    /// weapons (the agent layer), so someone walking up to a lamp is lit by it while the streets keep
    /// their painted pools of light: cheap enough for phones. Off by day.
    /// </summary>
    private static void LampLight(Transform parent, Vector3 at, float range)
    {
        var go = new GameObject("LampLight");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.72f, 0.42f);
        l.range = range;
        l.intensity = 2.4f;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForceVertex;
        l.cullingMask = 1 << World.AgentLayer;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        l.enabled = false;
        lamps.Add(l);
    }

    /// <summary>
    /// Street lamps along the streets of the town, as in the Old Town: a steel post with a lantern on an
    /// arm, a pool of warm light under it; on free street ground next to a wall or fence, 10 m apart.
    /// </summary>
    private static int StreetLamps()
    {
        var parent = Node("StreetLamps");
        int mask = World.EnvMask | (1 << World.FenceLayer);
        var placed = new List<Vector3>();
        var dirs = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        Physics.SyncTransforms();
        for (float x = -40f; x <= 110f; x += 4f)
            for (float z = -70f; z <= 60f; z += 4f)
            {
                if (placed.Count >= 60) return placed.Count;
                var top = root.TransformPoint(new Vector3(x, 25f, z));
                RaycastHit hit = default;
                bool any = false;
                foreach (var h in Physics.RaycastAll(top, Vector3.down, 60f, World.EnvMask, QueryTriggerInteraction.Ignore))
                {
                    if (h.normal.y < 0.9f || root.InverseTransformPoint(h.point).y > StreetTop) continue;
                    if (Physics.Raycast(h.point + Vector3.up * 4.6f, Vector3.down, 4.4f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;   // under a roof
                    // the street surface itself: the highest level ground below street top (not the big plane under the roads)
                    if (!any || h.point.y > hit.point.y) { hit = h; any = true; }
                }
                if (!any || placed.Any(p => (p - hit.point).sqrMagnitude < 10f * 10f)) continue;
                if (Physics.CheckCapsule(hit.point + Vector3.up * 1.0f, hit.point + Vector3.up * 4.6f, 0.7f, mask, QueryTriggerInteraction.Ignore)) continue;
                // beside a wall (1.2 to 3.5 m away): the lamp leans out over the street from it
                Vector3 wall = Vector3.zero;
                foreach (var d in dirs)
                    if (Physics.Raycast(hit.point + Vector3.up * 1.5f, d, out var w, 3.5f, mask, QueryTriggerInteraction.Ignore) && w.distance > 1.2f) { wall = d; break; }
                if (wall == Vector3.zero) continue;
                var arm = -wall;
                var t = new GameObject("StreetLamp").transform;
                t.SetParent(parent, false);
                t.position = hit.point;
                float yaw = Mathf.Atan2(arm.x, arm.z) * Mathf.Rad2Deg;
                var steel = AssetSetup.RustyPost;
                MeshGen.TexBox("LampPost", hit.point + Vector3.up * 2.3f, new Vector3(0.14f, 4.6f, 0.14f), steel, 1f, t, yaw, true);
                MeshGen.TexBox("LampFoot", hit.point + Vector3.up * 0.15f, new Vector3(0.34f, 0.3f, 0.34f), steel, 1f, t, yaw, false);
                MeshGen.TexBox("LampArm", hit.point + Vector3.up * 4.55f + arm * 0.5f, new Vector3(0.08f, 0.08f, 1.05f), steel, 1f, t, yaw, false);
                NightLights.Lamp(t, hit.point + Vector3.up * 4.25f + arm * 0.95f, new Vector3(0.26f, 0.36f, 0.26f), night, 4.6f);
                LampLight(t, hit.point + Vector3.up * 4f + arm * 0.95f, 9f);
                placed.Add(hit.point);
            }
        return placed.Count;
    }

    /// <summary>A name board on two steel posts, on free ground near 'at' (scene coordinates), facing the middle of the town.</summary>
    private static void BoardStand(Transform parent, Vector3 at, string pashto, string english)
    {
        int mask = World.EnvMask | (1 << World.FenceLayer);
        Vector3 foot = Vector3.zero;
        bool found = false;
        for (int ring = 0; ring <= 6 && !found; ring++)
            for (int k = 0; k < Mathf.Max(1, ring * 8) && !found; k++)
            {
                float a = k * Mathf.PI * 2f / Mathf.Max(1, ring * 8);
                var probe = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (ring * 1.5f);
                // the street itself: the lowest level surface there (not a container, a roof or a tank)
                var top = root.TransformPoint(new Vector3(probe.x, 25f, probe.z));
                RaycastHit hit = default;
                bool any = false;
                foreach (var h in Physics.RaycastAll(top, Vector3.down, 60f, World.EnvMask, QueryTriggerInteraction.Ignore))
                {
                    // not the big plane under the roads either: something must not lie just above it
                    if (h.normal.y < 0.85f || Physics.Raycast(h.point + Vector3.up * 3.4f, Vector3.down, 3.1f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    // street level only (the streets lie 10.9 to 12.9 m below the scene's zero): not a container top
                    if (root.InverseTransformPoint(h.point).y > StreetTop) continue;
                    if (!any || h.point.y < hit.point.y) { hit = h; any = true; }
                }
                if (!any) continue;
                var f = TownMiddle - root.InverseTransformPoint(hit.point);
                f.y = 0;
                var rot = Quaternion.LookRotation(f.sqrMagnitude > 1f ? -f.normalized : Vector3.back);
                if (Physics.CheckBox(hit.point + Vector3.up * 2.3f, new Vector3(1.9f, 2.1f, 0.5f), rot, mask, QueryTriggerInteraction.Ignore)) continue;
                foot = hit.point;
                found = true;
            }
        if (!found) { Debug.LogWarning("[Map] CityLord: no room for the board " + english); return; }
        var face = TownMiddle - root.InverseTransformPoint(foot);
        face.y = 0;
        if (face.sqrMagnitude < 1f) face = Vector3.back;
        face.Normalize();
        float yaw = Mathf.Atan2(-face.x, -face.z) * Mathf.Rad2Deg;
        var t = new GameObject("Board " + english).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(foot, Quaternion.Euler(0, yaw, 0));
        foreach (float x in new[] { -1.55f, 1.55f })
            MeshGen.TexBox("Post", t.TransformPoint(new Vector3(x, 1.25f, 0.05f)), new Vector3(0.12f, 2.5f, 0.12f), AssetSetup.RustyPost, 1f, t, yaw, true);
        // a plate of old white-painted iron in a dark iron frame, dark writing
        MeshGen.TexBox("Board", t.TransformPoint(new Vector3(0, 2.05f, 0)), new Vector3(3.4f, 1.25f, 0.08f), AssetSetup.WhiteIron, 1f, t, yaw, false);
        MeshGen.TexBox("Rim", t.TransformPoint(new Vector3(0, 2.7f, 0)), new Vector3(3.5f, 0.07f, 0.1f), AssetSetup.RustyPost, 1f, t, yaw, false);
        MeshGen.TexBox("Rim", t.TransformPoint(new Vector3(0, 1.4f, 0)), new Vector3(3.5f, 0.07f, 0.1f), AssetSetup.RustyPost, 1f, t, yaw, false);
        foreach (float x in new[] { -1.72f, 1.72f })
            MeshGen.TexBox("Rim", t.TransformPoint(new Vector3(x, 2.05f, 0)), new Vector3(0.07f, 1.37f, 0.1f), AssetSetup.RustyPost, 1f, t, yaw, false);
        ZahraBuilder.Writing(t, new Vector3(0, 2.28f, -0.05f), new Vector2(3.15f, 0.62f), pashto, 42f, new Color(0.08f, 0.1f, 0.14f));
        ZahraBuilder.Writing(t, new Vector3(0, 1.75f, -0.05f), new Vector2(3.15f, 0.42f), english, 24f, new Color(0.42f, 0.1f, 0.06f));
    }
}
