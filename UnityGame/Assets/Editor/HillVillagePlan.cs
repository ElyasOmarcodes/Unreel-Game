using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The plan of the Hill Village, worked out before anything is built: the levels the hillside is cut
/// into, the winding lanes and dirt tracks, and where every house, courtyard compound (saray), windmill
/// and the cemetery stand. Houses are set along both sides of the lanes one after another, each of a
/// kind that suits its quarter (shop houses round the square and up the main street, old stone and
/// whitewashed houses on the terrace, flat-roofed concrete houses, cottages and sheds at the edges),
/// each its own width, depth and height, set back its own distance, joined to its neighbour or apart
/// across a passage or a walled garden. Plain arithmetic, no scene: the same code draws the plan
/// outside Unity (Tools/hillplan).
/// </summary>
public static partial class HillVillageBuilder
{
    public const float Half = 100f;            // the village, its groves and the near hill (bots, spawns)
    private const float Edge = Half + 40f;     // players can climb the slopes around it up to here
    private const float Size = 420f;           // the ground reaches this far (the view to the hills)

    private static System.Random rng;
    private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    private static bool Chance(double p) => rng.NextDouble() < p;

    /// <summary>(x, z) turned 'yaw' degrees about the vertical, as Unity turns it (yaw 0 looks along +z).</summary>
    private static Vector2 Turn(float yaw, float x, float z)
    {
        float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector2(x * c + z * s, -x * s + z * c);
    }

    private static float YawOf(Vector2 forward) => Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;

    // ------------------------------------------------------------------ the levels

    /// <summary>
    /// A level piece of ground (axis-aligned): flat, or rising along x or z between two heights (a ramp
    /// street). Each edge either blends into the ground beyond over its margin, or (margin 0) ends sharp:
    /// there a retaining wall stands over the step. Later pads win over earlier ones.
    /// </summary>
    private struct Pad
    {
        public float X0, Z0, X1, Z1, Y0, Y1, A0, A1;
        public int Axis;                // 0 flat (Y0), 1 along x, 2 along z: Y0 at A0, Y1 at A1
        public float W, E, S, N;        // margins (west -x, east +x, south -z, north +z)

        public float Height(float x, float z)
        {
            if (Axis == 0) return Y0;
            float a = Axis == 1 ? x : z;
            return Mathf.Lerp(Y0, Y1, Mathf.InverseLerp(A0, A1, a));
        }

        public float Weight(float x, float z)
        {
            float w = 1f;
            w = Mathf.Min(w, Side(X0 - x, W));
            w = Mathf.Min(w, Side(x - X1, E));
            w = Mathf.Min(w, Side(Z0 - z, S));
            w = Mathf.Min(w, Side(z - Z1, N));
            return w;
        }

        private static float Side(float outside, float margin)
        {
            if (outside <= 0f) return 1f;
            if (margin <= 0f) return 0f;
            return Mathf.SmoothStep(1f, 0f, outside / margin);
        }
    }

    private static readonly List<Pad> pads = new List<Pad>();

    private static void Flat(float x0, float z0, float x1, float z1, float y, float w, float e, float s, float n) =>
        pads.Add(new Pad { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Y0 = y, W = w, E = e, S = s, N = n });

    private static void Ramp(float x0, float z0, float x1, float z1, int axis, float a0, float y0, float a1, float y1, float w, float e, float s, float n) =>
        pads.Add(new Pad { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Axis = axis, A0 = a0, Y0 = y0, A1 = a1, Y1 = y1, W = w, E = e, S = s, N = n });

    private static float Gauss(float x, float z, float cx, float cz, float sigma) => Mathf.Exp(-((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (2f * sigma * sigma));

    /// <summary>The land before the village was cut into it: rising to the pine hill in the north.</summary>
    private static float RawHeight(float x, float z)
    {
        float h = 0.07f * z;
        h += 1.8f * Mathf.Sin(x / 29f + 0.5f) * Mathf.Cos(z / 23f) + 0.9f * Mathf.Sin((x - z) / 17f);
        h += 26f * Gauss(x, z, 10f, 128f, 46f);       // the pine hill
        h += 11f * Gauss(x, z, -122f, 40f, 34f);      // the west hill (the windmills)
        h += 9f * Gauss(x, z, 118f, 64f, 30f);        // the north east hill
        h -= 3.5f * Gauss(x, z, 30f, -112f, 40f);     // the hollow below the olive groves
        float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Half - 8f, Half + 50f, edge)) * (16f + 6f * Mathf.Sin(x / 21f + z / 27f));
        return h;
    }

    public static float GroundHeight(float x, float z)
    {
        float h = RawHeight(x, z);
        foreach (var p in pads)
        {
            float w = p.Weight(x, z);
            if (w > 0f) h = Mathf.Lerp(h, p.Height(x, z), w);
        }
        return h;
    }

    /// <summary>How much of the ground here is a levelled part of the village (0 the open land).</summary>
    private static float Built(float x, float z)
    {
        float w = 0f;
        foreach (var p in pads) w = Mathf.Max(w, p.Weight(x, z));
        return w;
    }

    /// <summary>
    /// The levels: the square in the middle; the west quarter rising gently from it; the upper terrace
    /// (mosque) 4.5 m above behind a stone wall, reached by stairs and by the east street sloping up
    /// along its wall; the lower quarter 2.5 m below the square, with its own sloping street down in the
    /// south east; the road out to the east climbing the slope.
    /// </summary>
    private static void Levels()
    {
        pads.Clear();
        Flat(-26f, -18f, 26f, 16f, 0f, 6f, 6f, 0f, 0f);                          // the square (meydan)
        Ramp(-96f, -18f, -26f, 16f, 1, -96f, 3f, -26f, 0f, 10f, 0f, 0f, 0f);     // the west quarter
        Flat(-96f, 16f, 24f, 50f, 4.5f, 10f, 0f, 0f, 12f);                       // the upper terrace
        Ramp(24f, -6f, 42f, 52f, 2, 16f, 0f, 40f, 4.5f, 0f, 10f, 6f, 10f);       // the east street up
        Flat(-96f, -56f, 26f, -18f, -2.5f, 12f, 0f, 12f, 0f);                    // the lower quarter
        Ramp(26f, -56f, 42f, -6f, 2, -40f, -2.5f, -18f, 0f, 0f, 10f, 12f, 0f);   // the street down
        Ramp(42f, -8f, 104f, 10f, 1, 42f, 0f, 104f, 7f, 0f, 0f, 6f, 6f);         // the road out east
    }

    /// <summary>A level spot for a house standing on the open slope (its own pad, blended in).</summary>
    private static float HousePad(Vector3 c, float w, float d, float yaw, float margin = 6f)
    {
        var a = Turn(yaw, w, d); var b = Turn(yaw, w, -d);
        float ex = Mathf.Max(Mathf.Abs(a.x), Mathf.Abs(b.x)) / 2f + 2f, ez = Mathf.Max(Mathf.Abs(a.y), Mathf.Abs(b.y)) / 2f + 2f;
        float y = GroundHeight(c.x, c.z);
        Flat(c.x - ex, c.z - ez, c.x + ex, c.z + ez, y, margin, margin, margin, margin);
        return y;
    }

    // ------------------------------------------------------------------ lanes and tracks

    // the streets and lanes paved with cobbles, and the dirt tracks out to the hill and the groves
    private static readonly List<(Vector2[] line, float width)> cobbles = new List<(Vector2[], float)>();
    private static readonly List<(Vector2[] line, float width)> tracks = new List<(Vector2[], float)>();

    private static float LineDistance(List<(Vector2[] line, float width)> lines, float x, float z)
    {
        float best = 1e9f;
        var p = new Vector2(x, z);
        foreach (var (line, width) in lines)
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i]; var b = line[i + 1];
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t) - width / 2f);
            }
        return best;
    }

    private const int SquareQ = 0, TerraceQ = 1, WestQ = 2, LowerQ = 3, StreetQ = 4, RoadQ = 5, FringeQ = 6;

    /// <summary>A lane: its centre line (a smooth curve through the points it was given), width and quarter.</summary>
    private class Lane
    {
        public string Name;
        public Vector2[] P;
        public float[] S;
        public float Width;
        public int Quarter, Sides;   // which sides get houses: 1 the left, 2 the right, 3 both
        public bool Kerb, Main, Paved = true;
        public Vector2 Min, Max;
        public float Length => S[S.Length - 1];

        /// <summary>The point at distance s along the lane and the direction there.</summary>
        public (Vector2 p, Vector2 t) At(float s)
        {
            s = Mathf.Clamp(s, 0f, Length);
            int i = 0;
            while (i < S.Length - 2 && S[i + 1] < s) i++;
            float f = Mathf.InverseLerp(S[i], S[i + 1], s);
            var t = (P[i + 1] - P[i]).normalized;
            return (Vector2.Lerp(P[i], P[i + 1], f), t);
        }
    }

    private static readonly List<Lane> lanes = new List<Lane>();

    /// <summary>A Catmull-Rom curve through the points (x, z pairs), a point every 'spacing' metres.</summary>
    private static Vector2[] Curve(float[] c, float spacing = 1.5f)
    {
        int n = c.Length / 2;
        var k = new Vector2[n];
        for (int i = 0; i < n; i++) k[i] = new Vector2(c[i * 2], c[i * 2 + 1]);
        var pts = new List<Vector2> { k[0] };
        for (int i = 0; i + 1 < n; i++)
        {
            Vector2 p0 = k[Mathf.Max(0, i - 1)], p1 = k[i], p2 = k[i + 1], p3 = k[Mathf.Min(n - 1, i + 2)];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / spacing));
            for (int j = 1; j <= steps; j++)
            {
                float t = j / (float)steps, t2 = t * t, t3 = t2 * t;
                pts.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
            }
        }
        return pts.ToArray();
    }

    private static Lane AddLane(string name, float width, int quarter, int sides, bool main, params float[] c)
    {
        var p = Curve(c);
        var s = new float[p.Length];
        for (int i = 1; i < p.Length; i++) s[i] = s[i - 1] + Vector2.Distance(p[i - 1], p[i]);
        var lane = new Lane { Name = name, P = p, S = s, Width = width, Quarter = quarter, Sides = sides, Main = main };
        lane.Min = new Vector2(p.Min(q => q.x), p.Min(q => q.y)) - Vector2.one * (width / 2f + 1f);
        lane.Max = new Vector2(p.Max(q => q.x), p.Max(q => q.y)) + Vector2.one * (width / 2f + 1f);
        lanes.Add(lane);
        cobbles.Add((p, width));
        return lane;
    }

    private static void AddTrack(float width, params float[] c) => tracks.Add((Curve(c, 2f), width));

    /// <summary>How far p is from the edge of the nearest lane (negative on it).</summary>
    private static float LaneDistance(Vector2 p)
    {
        float best = 1e9f;
        foreach (var lane in lanes)
        {
            if (p.x < lane.Min.x - 3f || p.y < lane.Min.y - 3f || p.x > lane.Max.x + 3f || p.y > lane.Max.y + 3f) continue;
            for (int i = 0; i + 1 < lane.P.Length; i++)
            {
                var a = lane.P[i]; var ab = lane.P[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t) - lane.Width / 2f);
            }
        }
        return best;
    }

    /// <summary>
    /// The lanes: on the upper terrace one along the wall and one winding behind, alleys between them,
    /// a lane east of the mosque up to the east street; in the west quarter its winding main lane with a
    /// small square, a lane along the foot of the terrace wall and alleys to the stairs; in the lower
    /// quarter a lane under the wall, a winding middle lane and a lane at its southern edge, alleys
    /// across; the east street up, the street down and the road out east. The dirt tracks lead out to the
    /// groves, the pine hill, the windmills on the west hill, the cemetery and the farms.
    /// </summary>
    private static void Lanes()
    {
        lanes.Clear(); cobbles.Clear(); tracks.Clear();
        // main streets first: their frontages fill first
        AddLane("EastStreet", 8.6f, StreetQ, 2, true, 29.2f, -7f, 30.6f, 5f, 29.4f, 17f, 30.9f, 29f, 29.8f, 41f, 30.4f, 53f).Kerb = true;
        AddLane("Road", 7f, RoadQ, 3, true, 33f, 1f, 44f, 1.6f, 58f, -0.6f, 72f, 1.8f, 88f, 0.2f, 104f, 1.4f).Kerb = true;
        AddLane("StreetDown", 7.2f, StreetQ, 3, true, 30f, -7f, 31.4f, -19f, 29.6f, -31f, 31f, -44f, 30f, -57f);
        AddLane("WestLane", 5.6f, WestQ, 3, true, -25f, 0.5f, -34f, 2f, -44f, -0.4f, -54f, 2.4f, -64f, 0.2f, -72f, 1.6f, -80f, 1.2f, -88f, -0.6f, -99f, 0.6f);
        AddLane("WestPlaza", 11f, WestQ, 3, false, -82f, 1f, -76.5f, 1.4f, -71f, 1.2f);
        AddLane("TerraceLane", 4.6f, TerraceQ, 1, true, -99f, 20f, -84f, 20.8f, -70f, 19.8f, -56f, 21.2f, -42f, 19.9f, -28f, 20.7f, -16f, 19.6f, -6f, 18.6f, 6f, 19.2f, 16f, 19.8f, 22f, 19.4f);
        AddLane("UpperLane", 4f, TerraceQ, 3, true, -99f, 40f, -88f, 37.5f, -76f, 39.8f, -64f, 37f, -52f, 40.2f, -40f, 37.6f, -29f, 39.6f, -21.5f, 37.5f, -19.8f, 44f, -20.6f, 51f, -23f, 58f);
        AddLane("MosqueLane", 3.6f, TerraceQ, 2, false, 7.5f, 19.5f, 10.5f, 28f, 11f, 38f, 14f, 46f, 22f, 48.5f, 31f, 48f);
        AddLane("UnderWall", 4.4f, LowerQ, 1, true, 27f, -22f, 16f, -22.4f, 4f, -23.6f, -6f, -24f, -16f, -23.4f, -30f, -22.2f, -44f, -23f, -56f, -23.8f, -67f, -24.2f, -78f, -22.6f, -90f, -23f, -99f, -22.2f);
        AddLane("MiddleLane", 5.4f, LowerQ, 3, true, 27f, -37.5f, 15f, -40f, 2f, -35.6f, -12f, -39f, -26f, -35.4f, -40f, -40.2f, -54f, -36.2f, -68f, -40.4f, -80f, -38.6f, -99f, -37.8f);
        AddLane("SouthLane", 3.6f, LowerQ, 3, false, 25f, -51f, 10f, -49.5f, -6f, -52.5f, -22f, -50f, -38f, -52.6f, -54f, -50.6f, -70f, -52f);
        AddLane("WallFoot", 2.2f, WestQ, 0, false, -27f, 13.3f, -44f, 13.2f, -58f, 13.3f, -69f, 13.3f, -82f, 13.2f, -96f, 13.3f);
        // alleys
        AddLane("AlleyT1", 2.6f, TerraceQ, 0, false, -80f, 22f, -81.5f, 30f, -79.5f, 37.5f);
        AddLane("AlleyT2", 2.4f, TerraceQ, 0, false, -46f, 21.5f, -44.5f, 29f, -46.5f, 37.5f);
        AddLane("AlleyW1", 2.8f, WestQ, 0, false, -60f, 3f, -64f, 8f, -68.5f, 12.8f);
        AddLane("AlleyW2", 2.8f, WestQ, 0, false, -56f, -1f, -57.5f, -8f, -59.5f, -15.5f);
        AddLane("AlleyW3", 2.2f, WestQ, 0, false, -36f, -2f, -37f, -9f, -35f, -15f);
        AddLane("AlleyL1", 2.4f, LowerQ, 0, false, -19f, -25f, -20.5f, -31f, -18.5f, -37f);
        AddLane("AlleyL2", 2.6f, LowerQ, 0, false, -49f, -25.5f, -47.5f, -31f, -49.5f, -37.5f);
        AddLane("AlleyL3", 2.6f, LowerQ, 0, false, 11f, -24.5f, 9.5f, -31f, 11f, -38f);
        AddLane("AlleyL4", 2.4f, LowerQ, 0, false, -31f, -41.5f, -30f, -46f, -31.5f, -50.5f);
        AddLane("AlleyL5", 2.4f, LowerQ, 0, false, -62f, -41.5f, -61f, -46f, -62.5f, -50.5f);

        AddTrack(2.6f, 30f, -57f, 24f, -68f, 8f, -78f, -12f, -86f, -32f, -84f);
        AddTrack(2.4f, 22f, -71f, 33f, -79f, 44f, -86f);
        AddTrack(2.2f, -6f, -54f, -9f, -64f, -10f, -75f);
        AddTrack(2.4f, 30.4f, 53f, 37f, 66f, 30f, 77f);
        AddTrack(2.4f, -99f, 40f, -92f, 52f, -74f, 61f, -52f, 64f, -40f, 64f);
        AddTrack(2.2f, -23f, 58f, -20f, 70f, -10f, 80f);
        AddTrack(3f, 104f, 1.4f, 118f, 4f, 132f, 6f);
        AddTrack(2.6f, -99f, 0.6f, -106f, -2f, -110f, 8f, -112f, 20f, -116f, 32f, -117f, 44f);
        AddTrack(2.6f, 62f, 4.5f, 61.5f, 14f, 62f, 23f);
        AddTrack(3f, 64f, -3.5f, 63.5f, -10f, 64f, -14f);
        AddTrack(12f, 56f, -19f, 72f, -19f);        // the dusty square before the cemetery
        AddTrack(2.4f, -70f, -52f, -72f, -60f, -80f, -67f, -90f, -70f);
    }

    // ------------------------------------------------------------------ houses

    private enum Kind { Classic, Tower, Flat, Shop, Cottage, Shed, Wing }

    private class House
    {
        public float X, Z, Yaw, W, D;
        public int Floors = 2;
        public int Style;            // 0 whitewash with stone corners, 1 stone below and whitewash above, 2 all field stone, 3 old flaking plaster
        public Kind Kind = Kind.Classic;
        public int Colour;           // the plaster: 0 white, 1 cream, 2 ochre, 3 pale pink, 4 grey concrete, 5 pale blue
        public int Paint;            // shutters, doors, railings: 0 blue, 1 green, 2 brown, 3 grey, 4 white
        public int Roof;             // 0 hip, 1 gable, 2 flat behind a parapet, 3 lean-to
        public bool Balcony, OutsideStair, Chimney, Open, Ruin, Yard, Gallery, RoofStair, Porch, Unfinished, Dish, Aircon;
        public float YardDepth = 5f;
        public int Lane = -1, Side;  // the lane it fronts and on which side
        public float S0, S1;         // its frontage along the lane

        public House() { }

        public House(float x, float z, float yaw, float w, float d, int floors, int style, string extras = "")
        {
            X = x; Z = z; Yaw = yaw; W = w; D = d; Floors = floors; Style = style;
            Roof = extras.Contains("gable") ? 1 : 0;
            Balcony = extras.Contains("balcony"); OutsideStair = extras.Contains("stair"); Chimney = extras.Contains("chimney");
            Open = extras.Contains("open"); Ruin = extras.Contains("ruin"); Yard = extras.Contains("yard");
        }

        public Vector2 C => new Vector2(X, Z);
        public Vector2 ToLocal(Vector2 p) => Turn(-Yaw, p.x - X, p.y - Z);
        public Vector2 ToWorld(float x, float z) => C + Turn(Yaw, x, z);

        /// <summary>How far p is from the footprint (0 on or inside it).</summary>
        public float Distance(Vector2 p)
        {
            var l = ToLocal(p);
            float dx = Mathf.Max(0f, Mathf.Abs(l.x) - W / 2f), dz = Mathf.Max(0f, Mathf.Abs(l.y) - D / 2f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public bool Inside(Vector2 p, float grow = 0f)
        {
            var l = ToLocal(p);
            return Mathf.Abs(l.x) < W / 2f + grow && Mathf.Abs(l.y) < D / 2f + grow;
        }

        /// <summary>Points round the edge of the footprint, about 'step' apart.</summary>
        public IEnumerable<Vector2> Outline(float step)
        {
            int nw = Mathf.Max(1, Mathf.CeilToInt(W / step)), nd = Mathf.Max(1, Mathf.CeilToInt(D / step));
            for (int i = 0; i <= nw; i++) { float x = -W / 2f + W * i / nw; yield return ToWorld(x, -D / 2f); yield return ToWorld(x, D / 2f); }
            for (int i = 1; i < nd; i++) { float z = -D / 2f + D * i / nd; yield return ToWorld(-W / 2f, z); yield return ToWorld(W / 2f, z); }
        }
    }

    private static readonly List<House> houses = new List<House>();

    /// <summary>Ground kept clear of houses: the square, the courtyard, the terrace walls and stairs, the landmarks.</summary>
    private static readonly List<(float x0, float z0, float x1, float z1)> keepOut = new List<(float, float, float, float)>();

    private static bool KeptOut(Vector2 p)
    {
        foreach (var k in keepOut)
            if (p.x > k.x0 && p.x < k.x1 && p.y > k.z0 && p.y < k.z1) return true;
        return false;
    }

    /// <summary>A walled garden or yard between two houses along a lane: the wall along the lane, its gate.</summary>
    private class Garden { public Vector2 A, B, Tree; public bool Gate; public float Height; }
    private static readonly List<Garden> gardens = new List<Garden>();

    /// <summary>
    /// Can a house stand here: in the village, off the lanes, tracks and kept ground, on one level (or
    /// on open land not too steep, where it gets its own levelled spot), and either joined wall to wall
    /// to a neighbour standing square with it or clear of every other house by a passage a man can walk.
    /// </summary>
    private static bool Fits(House h)
    {
        float reach = Half - 3f;
        if (QuarterOf(h) == FringeQ) reach = Half + 4f;
        float hi = float.MinValue, lo = float.MaxValue, built = 0f;
        foreach (var p in h.Outline(0.9f).Concat(Interior(h)))
        {
            if (Mathf.Abs(p.x) > reach || Mathf.Abs(p.y) > reach) return false;
            if (KeptOut(p)) return false;
            if (LaneDistance(p) < 0.3f) return false;
            if (LineDistance(tracks, p.x, p.y) < 0.8f) return false;
            float y = GroundHeight(p.x, p.y);
            hi = Mathf.Max(hi, y); lo = Mathf.Min(lo, y);
            built = Mathf.Max(built, Built(p.x, p.y));
        }
        // on a level or a sloping street the foundation takes up the fall (a shop's floor must stay near
        // the street); on open land the house gets its own level spot
        if (hi - lo > StepTolerance(h.Kind) && !(built < 0.03f && hi - lo < 3.5f)) return false;
        float reachSq = (h.W + h.D) * (h.W + h.D);
        foreach (var o in houses)
        {
            if ((o.C - h.C).sqrMagnitude > reachSq + (o.W + o.D) * (o.W + o.D) + 30f) continue;
            float gap = float.MaxValue;
            foreach (var p in h.Outline(0.4f)) { if (o.Inside(p, -0.01f)) return false; gap = Mathf.Min(gap, o.Distance(p)); }
            foreach (var p in o.Outline(0.4f)) { if (h.Inside(p, -0.01f)) return false; gap = Mathf.Min(gap, h.Distance(p)); }
            if (o.Inside(h.C) || h.Inside(o.C)) return false;
            if (gap < 0.03f) return false;
            if (gap < 0.16f)
            {
                // joined: only square with each other
                float turn = Mathf.Abs(Mathf.DeltaAngle(h.Yaw, o.Yaw)) % 90f;
                if (Mathf.Min(turn, 90f - turn) > 2.5f) return false;
            }
            else if (gap < 1.2f) return false;
        }
        return true;
    }

    /// <summary>How far the ground may fall across a house's footprint (its foundation shows that much).</summary>
    private static float StepTolerance(Kind k) => k == Kind.Shop ? 0.85f : 1.3f;

    private static IEnumerable<Vector2> Interior(House h)
    {
        for (int i = -1; i <= 1; i++)
            for (int k = -1; k <= 1; k++)
                yield return h.ToWorld(i * h.W / 3f, k * h.D / 3f);
    }

    private static int QuarterOf(House h) => h.Lane >= 0 ? lanes[h.Lane].Quarter : FringeQ;

    /// <summary>What kind of house to build here, by the quarter.</summary>
    private static Kind PickKind(int quarter, Vector2 p)
    {
        double r = rng.NextDouble();
        switch (quarter)
        {
            case SquareQ: return r < 0.75 ? Kind.Shop : Kind.Classic;
            case TerraceQ: return r < 0.56 ? Kind.Classic : r < 0.69 ? Kind.Tower : r < 0.82 ? Kind.Cottage : Kind.Flat;
            case WestQ:
                if (p.x > -56f && r < 0.6) return Kind.Shop;
                return r < 0.48 ? Kind.Classic : r < 0.78 ? Kind.Flat : r < 0.9 ? Kind.Cottage : Kind.Tower;
            case LowerQ: return r < 0.38 ? Kind.Classic : r < 0.7 ? Kind.Flat : r < 0.84 ? Kind.Cottage : r < 0.92 ? Kind.Shed : Kind.Tower;
            case StreetQ:
                if (p.y < 26f && p.y > -24f && r < 0.7) return Kind.Shop;
                return r < 0.55 ? Kind.Classic : r < 0.9 ? Kind.Flat : Kind.Tower;
            case RoadQ: return r < 0.42 ? Kind.Flat : r < 0.62 ? Kind.Classic : r < 0.8 ? Kind.Shed : Kind.Shop;
            default: return r < 0.3 ? Kind.Cottage : r < 0.55 ? Kind.Shed : r < 0.85 ? Kind.Classic : Kind.Flat;
        }
    }

    /// <summary>Width (along the lane), depth and storeys of a house of this kind.</summary>
    private static (float w, float d, int floors) Dims(Kind k, int quarter)
    {
        switch (k)
        {
            case Kind.Shop: return (R(6.5f, 10.5f), R(7.5f, 10f), Chance(0.35) ? 3 : 2);
            case Kind.Tower: return (R(4.8f, 6f), R(4.8f, 6.2f), 3);
            case Kind.Flat: return (R(7f, 12.5f), R(7f, 10.5f), quarter == RoadQ && Chance(0.12) ? 4 : Chance(0.22) ? 3 : Chance(0.55) ? 2 : 1);
            case Kind.Cottage: return (R(4.2f, 6.5f), R(4f, 5.6f), 1);
            case Kind.Shed: return (R(4.5f, 7.5f), R(3.6f, 5.2f), 1);
            default: return (R(6.5f, 11.5f), R(6.5f, 9.5f), Chance(0.15) ? 3 : Chance(0.75) ? 2 : 1);
        }
    }

    /// <summary>The space left to the next house: none (joined), a passage, or a garden behind a wall.</summary>
    private static float Gap(int quarter, Kind k)
    {
        double r = rng.NextDouble();
        float joined = quarter == SquareQ || quarter == StreetQ ? 0.6f : quarter == TerraceQ || quarter == WestQ ? 0.5f : quarter == LowerQ ? 0.42f : 0.15f;
        if (k == Kind.Shed || k == Kind.Cottage) joined *= 0.5f;
        if (r < joined) return 0.08f;
        if (r < joined + 0.3f) return R(1.3f, 2.6f);
        return R(4f, quarter == FringeQ || quarter == RoadQ ? 12f : 8f);
    }

    /// <summary>The look of a house: its plaster, paint, roof and what it has (balcony, stairs, chimney...).</summary>
    private static void Dress(House h)
    {
        int Weighted(params float[] w)
        {
            float t = (float)rng.NextDouble() * w.Sum();
            for (int i = 0; i < w.Length; i++) { t -= w[i]; if (t <= 0f) return i; }
            return w.Length - 1;
        }
        h.Paint = Weighted(0.4f, 0.24f, 0.2f, 0.1f, 0.06f);
        switch (h.Kind)
        {
            case Kind.Classic:
            case Kind.Tower:
            case Kind.Wing:
                h.Style = h.Kind == Kind.Tower ? (Chance(0.5) ? 2 : 0) : Weighted(0.42f, 0.2f, 0.22f, 0.16f);
                h.Colour = Weighted(0.55f, 0.22f, 0.1f, 0.08f, 0f, 0.05f);
                h.Roof = h.Kind == Kind.Tower || Chance(0.6) ? 0 : 1;
                h.Balcony = h.Floors > 1 && Chance(0.32);
                h.OutsideStair = h.Floors == 2 && h.W >= 6.8f && h.Kind != Kind.Wing && Chance(0.28);
                h.Chimney = Chance(h.Kind == Kind.Tower ? 0.5 : 0.35);
                h.Open = Chance(h.Kind == Kind.Wing ? 0.7 : 0.42);
                h.Dish = Chance(0.12);
                break;
            case Kind.Flat:
                h.Style = 0;
                h.Colour = Weighted(0.24f, 0.14f, 0.1f, 0.14f, 0.32f, 0.06f);
                h.Roof = 2;
                h.Balcony = h.Floors > 1 && Chance(0.5);
                h.RoofStair = h.Floors == 1 && h.D >= 6.2f && Chance(0.6);
                h.OutsideStair = h.Floors == 2 && h.W >= 6.8f && Chance(0.3);
                h.Porch = h.Floors <= 2 && !h.Balcony && !h.OutsideStair && Chance(0.35);
                h.Unfinished = Chance(0.3);
                h.Open = Chance(0.45);
                h.Dish = Chance(0.55);
                h.Aircon = Chance(0.5);
                break;
            case Kind.Shop:
                h.Style = 0;
                h.Colour = Weighted(0.4f, 0.28f, 0.14f, 0.12f, 0f, 0.06f);
                h.Roof = Chance(0.6) ? 0 : 2;
                h.Balcony = Chance(0.75);
                h.Open = Chance(0.5);
                h.Dish = Chance(0.3);
                h.Aircon = Chance(0.35);
                break;
            case Kind.Cottage:
                h.Style = Weighted(0.25f, 0.1f, 0.4f, 0.25f);
                h.Colour = Weighted(0.6f, 0.25f, 0.1f, 0.05f);
                h.Roof = Chance(0.35) ? 3 : 1;
                h.Chimney = Chance(0.4);
                h.Open = Chance(0.55);
                break;
            case Kind.Shed:
                h.Style = Chance(0.6) ? 2 : 3;
                h.Roof = 3;
                h.Open = true;
                break;
        }
    }

    /// <summary>
    /// Houses along one side of a lane, one after another: each of the quarter's kinds, its own size,
    /// set back its own distance, its front square to the lane where it stands.
    /// </summary>
    private static void Frontage(int laneIndex, int side)
    {
        var lane = lanes[laneIndex];
        float s = R(0.5f, 4f);
        House last = null;
        float lastGap = 0f;
        while (s < lane.Length - 3f)
        {
            var (p0, _) = lane.At(s);
            var kind = PickKind(lane.Quarter, p0);
            var (w, d, floors) = Dims(kind, lane.Quarter);
            // on a sloping street the houses are narrower, each on its own step down the slope
            var (pa, _) = lane.At(Mathf.Max(0f, s - 2f)); var (pb, _) = lane.At(s + 2f);
            float slope = Mathf.Abs(GroundHeight(pb.x, pb.y) - GroundHeight(pa.x, pa.y)) / Mathf.Max(0.5f, Vector2.Distance(pa, pb));
            if (slope > 0.04f) w = Mathf.Min(w, Mathf.Max(4.5f, 0.85f * StepTolerance(kind) / slope));
            float setback = kind == Kind.Shop || lane.Quarter == SquareQ || lane.Quarter == StreetQ && Chance(0.7) ? R(0.25f, 0.8f)
                : kind != Kind.Shed && Chance(0.16) ? R(3.8f, 6f) : R(0.3f, 2.2f);
            House placed = null;
            // joined to the house before: square with it, wall to wall, its front a little in or out
            if (last != null && lastGap < 0.2f)
            {
                var (_, t0) = lane.At(last.S1);
                float sign = Vector2.Dot(Turn(last.Yaw, 1f, 0f), t0) >= 0f ? 1f : -1f;
                for (int attempt = 0; attempt < 3 && placed == null; attempt++)
                {
                    float dd = Mathf.Max(4f, d * (1f - 0.14f * attempt)), ww = w * (1f - 0.1f * attempt);
                    var c = last.ToWorld(sign * (last.W / 2f + 0.08f + ww / 2f), last.D / 2f + R(-0.9f, 0.6f) - dd / 2f);
                    var h = new House { X = c.x, Z = c.y, Yaw = last.Yaw, W = ww, D = dd, Floors = floors, Kind = kind, Lane = laneIndex, Side = side };
                    float sc = Project(lane, c);
                    h.S0 = sc - ww / 2f; h.S1 = sc + ww / 2f;
                    if (Fits(h)) { placed = h; setback = 0f; }
                }
            }
            for (int attempt = 0; attempt < 6 && placed == null; attempt++)
            {
                float ww = w * (1f - 0.08f * attempt), dd = Mathf.Max(3.6f, d * (attempt < 2 ? 1.2f - 0.2f * attempt : 1.1f - 0.13f * attempt));
                if (attempt >= 4) setback = R(0.3f, 1f);
                var (p, t) = lane.At(s + ww / 2f);
                var normal = side == 1 ? new Vector2(-t.y, t.x) : new Vector2(t.y, -t.x);
                var c = p + normal * (lane.Width / 2f + setback + dd / 2f);
                var h = new House { X = c.x, Z = c.y, Yaw = YawOf(-normal), W = ww, D = dd, Floors = floors, Kind = kind, Lane = laneIndex, Side = side, S0 = s, S1 = s + ww };
                // where the lane bends towards the front corners, the house steps back from it
                float need = h.Outline(0.9f).Where(q => Vector2.Dot(q - h.C, -normal) > 0f).Max(q => 0.35f - LaneDistance(q));
                if (need > 0f && need < 2.5f) { h.X += normal.x * need; h.Z += normal.y * need; }
                if (Fits(h)) placed = h;
            }
            if (placed == null) { s += 0.8f; last = null; continue; }
            Dress(placed);
            if (setback > 3.5f && placed.Kind != Kind.Shed) { placed.Yard = true; placed.YardDepth = setback - 0.2f; }
            houses.Add(placed);
            // a walled garden in the gap to the house before, its wall along the lane
            if (last != null && lastGap > 3.5f && placed.S0 - last.S1 < 13f && !placed.Yard && !last.Yard) GardenBetween(last, placed, lane, side);
            float gap = Gap(lane.Quarter, kind);
            last = placed; lastGap = gap;
            s = placed.S1 + gap;
        }
    }

    /// <summary>How far p is from the edge of this lane (negative on it).</summary>
    private static float EdgeDistance(Lane lane, Vector2 p)
    {
        float best = 1e9f;
        for (int i = 0; i + 1 < lane.P.Length; i++)
        {
            var a = lane.P[i]; var ab = lane.P[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best - lane.Width / 2f;
    }

    /// <summary>Where along the lane the point p lies (the distance to its nearest point).</summary>
    private static float Project(Lane lane, Vector2 p)
    {
        float best = float.MaxValue, at = 0f;
        for (int i = 0; i + 1 < lane.P.Length; i++)
        {
            var a = lane.P[i]; var ab = lane.P[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            float dist = (p - (a + ab * t)).sqrMagnitude;
            if (dist < best) { best = dist; at = lane.S[i] + t * (lane.S[i + 1] - lane.S[i]); }
        }
        return at;
    }

    /// <summary>
    /// Houses in the ground left behind the lanes' houses (the inside of the blocks): each facing the
    /// nearest lane, square with its nearest neighbour when nearly so, reached between the houses in front.
    /// </summary>
    private static void FillBlocks()
    {
        var regions = new[]
        {
            (-96f, 18f, 22f, 50f, TerraceQ), (-96f, -17f, -27f, 12f, WestQ), (-96f, -56f, 26f, -21f, LowerQ),
            (34f, -56f, 48f, 52f, StreetQ), (42f, -16f, 104f, 18f, RoadQ),
        };
        foreach (var (x0, z0, x1, z1, q) in regions)
            for (int i = 0; i < 1500; i++)
            {
                var p = new Vector2(R(x0, x1), R(z0, z1));
                if (LaneDistance(p) < 2f || KeptOut(p) || houses.Any(o => o.Distance(p) < 2.2f)) continue;
                // the nearest lane point: face it
                Vector2 near = p; float best = float.MaxValue; int laneAt = -1;
                for (int l = 0; l < lanes.Count; l++)
                    foreach (var lp in lanes[l].P)
                    {
                        float dd = (lp - p).sqrMagnitude;
                        if (dd < best) { best = dd; near = lp; laneAt = l; }
                    }
                float yaw = YawOf((near - p).normalized);
                var neighbour = houses.OrderBy(o => (o.C - p).sqrMagnitude).FirstOrDefault();
                if (neighbour != null)
                {
                    float rel = Mathf.DeltaAngle(neighbour.Yaw, yaw);
                    float snap = Mathf.Round(rel / 90f) * 90f;
                    if (Mathf.Abs(rel - snap) < 20f) yaw = neighbour.Yaw + snap;
                }
                double r = rng.NextDouble();
                var kind = q == TerraceQ ? (r < 0.55 ? Kind.Classic : r < 0.75 ? Kind.Cottage : Kind.Tower)
                    : r < 0.35 ? Kind.Classic : r < 0.6 ? Kind.Flat : r < 0.8 ? Kind.Cottage : Kind.Shed;
                var (w, d, floors) = Dims(kind, q);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var h = new House { X = p.x, Z = p.y, Yaw = yaw, W = w * (1f - 0.15f * attempt), D = Mathf.Max(3.6f, d * (1f - 0.15f * attempt)), Floors = floors, Kind = kind, Lane = laneAt, Side = 0 };
                    if (!Fits(h)) continue;
                    Dress(h);
                    houses.Add(h);
                    break;
                }
            }
    }

    private static void GardenBetween(House a, House b, Lane lane, int side)
    {
        // the front corners facing each other
        Vector2 Corner(House h, House other)
        {
            var l = h.ToWorld(-h.W / 2f, h.D / 2f); var r = h.ToWorld(h.W / 2f, h.D / 2f);
            return (l - other.C).sqrMagnitude < (r - other.C).sqrMagnitude ? l : r;
        }
        var ca = Corner(a, b); var cb = Corner(b, a);
        float len = Vector2.Distance(ca, cb);
        if (len < 3f || len > 14f) return;
        var dir = (cb - ca) / len;
        // the wall stands a little inside the corners' line; it must not cross a lane or a house
        for (float u = 0.4f; u < len - 0.4f; u += 0.5f)
        {
            var q = ca + dir * u;
            if (LaneDistance(q) < 0.25f || KeptOut(q)) return;
            foreach (var o in houses) if (o != a && o != b && o.Distance(q) < 0.6f) return;
        }
        var mid = (ca + cb) / 2f;
        var back = (((a.C + b.C) / 2f) - mid);
        var tree = mid + back.normalized * Mathf.Min(3.5f, back.magnitude);
        gardens.Add(new Garden { A = ca, B = cb, Tree = tree, Gate = len > 4.5f || Chance(0.5), Height = Chance(0.5) ? R(1.5f, 1.9f) : R(2f, 2.4f) });
    }

    /// <summary>A courtyard compound (saray): its outer wall, the gate in the front, the wings inside.</summary>
    private class Compound { public float X, Z, Yaw, W, D, Gate; public List<House> Wings = new List<House>(); }
    private static readonly List<Compound> compounds = new List<Compound>();

    /// <summary>
    /// A saray: a high wall round a courtyard, a wide wooden gate in the middle of its front; a long
    /// two-storey wing across the back with a wooden gallery along its upper floor, a lower wing down one
    /// side (both sides on the big ones), all facing the courtyard. Axis-aligned (yaw 0 or 180).
    /// </summary>
    private static void AddCompound(float x, float z, float yaw, float w, float d, bool twoSides)
    {
        var c = new Compound { X = x, Z = z, Yaw = yaw, W = w, D = d, Gate = R(-1.5f, 1.5f) };
        float pad = HousePad(new Vector3(x, 0, z), w, d, yaw, 7f);
        // the back wing: across the whole back, the gallery in front of it
        float backD = R(6.5f, 7.5f);
        var back = new House { Kind = Kind.Wing, W = w - 0.9f, D = backD, Floors = 2, Gallery = true };
        Place(back, c, 0f, -d / 2f + 0.45f + backD / 2f, 0f);
        var sides = twoSides ? new[] { -1f, 1f } : new[] { Chance(0.5) ? -1f : 1f };
        foreach (float sx in sides)
        {
            float sideD = R(5f, 6.2f), sideW = d - backD - 0.9f - 2.6f - R(0f, 2.5f);
            var wing = new House { Kind = Kind.Wing, W = sideW, D = sideD, Floors = Chance(0.5) ? 2 : 1 };
            // its front faces the courtyard's middle: turned 90 degrees, standing against the side wall
            float lz = -d / 2f + 0.45f + backD + 2.6f + sideW / 2f;
            Place(wing, c, sx * (w / 2f - 0.45f - sideD / 2f), lz, sx > 0 ? -90f : 90f);
        }
        compounds.Add(c);
        keepOut.Add((x - w / 2f - 0.6f, z - d / 2f - 0.6f, x + w / 2f + 0.6f, z + d / 2f + 0.6f));
    }

    private static void Place(House h, Compound c, float lx, float lz, float turn)
    {
        var p = new Vector2(c.X, c.Z) + Turn(c.Yaw, lx, lz);
        h.X = p.x; h.Z = p.y; h.Yaw = c.Yaw + turn;
        Dress(h);
        h.Roof = h.Gallery ? 0 : Chance(0.5) ? 0 : 1;
        h.Open = true;
        h.OutsideStair = false;
        h.Balcony = false;
        c.Wings.Add(h);
        houses.Add(h);
    }

    // the windmills on the west hill, the cemetery and its dusty square, the farms out in the groves
    private static readonly List<Vector3> windmills = new List<Vector3>();
    private static Vector3 cemetery;
    private const float CemeteryW = 22f, CemeteryD = 14f;

    /// <summary>
    /// The whole plan: levels, lanes, the compounds, windmills and cemetery, then the houses along the
    /// lanes (main streets first), the farms out on the land, and the levelled spots of the houses that
    /// stand on open ground.
    /// </summary>
    private static void Plan()
    {
        houses.Clear(); gardens.Clear(); compounds.Clear(); windmills.Clear(); keepOut.Clear();
        Levels();
        Lanes();
        keepOut.Add((-27f, -19f, 27f, 17f));             // the square
        keepOut.Add((-18f, 20.5f, 7f, 48.5f));            // the mosque's courtyard
        keepOut.Add((-100f, 14.6f, 24.5f, 18f));          // the terrace wall
        keepOut.Add((-100f, -20.2f, 27f, -15.6f));        // the lower wall
        keepOut.Add((22.4f, 15f, 25.8f, 43f));            // the terrace wall along the east street
        keepOut.Add((24.4f, -43f, 27.8f, -17f));          // the lower wall along the street down
        keepOut.Add((-69f, 12f, -57f, 18f));              // the west stairs up
        keepOut.Add((-69f, -22.5f, -56.5f, -15.6f));      // the west stairs down
        keepOut.Add((-13.5f, -22.6f, 1.5f, -15.6f));      // the square's stairs down
        // the dusty square and the cemetery behind it, south of the road
        cemetery = new Vector3(64f, 0f, -37f);
        Flat(54f, -25f, 74f, -12f, 0.4f, 8f, 8f, 4f, 6f);
        cemetery.y = -1.4f;
        Flat(64f - CemeteryW / 2f - 1.5f, -37f - CemeteryD / 2f - 1.5f, 64f + CemeteryW / 2f + 1.5f, -37f + CemeteryD / 2f + 1.5f, cemetery.y, 8f, 8f, 8f, 4f);
        keepOut.Add((55f, -25f, 73f, -4.5f));
        keepOut.Add((64f - CemeteryW / 2f - 1f, -37f - CemeteryD / 2f - 1f, 64f + CemeteryW / 2f + 1f, -37f + CemeteryD / 2f + 1f));
        // three windmills along the ridge of the west hill
        foreach (var (x, z) in new[] { (-118f, 30f), (-122f, 43f), (-117f, 56f) })
        {
            float y = GroundHeight(x, z);
            Flat(x - 4f, z - 4f, x + 4f, z + 4f, y, 4f, 4f, 4f, 4f);
            windmills.Add(new Vector3(x, y, z));
        }
        // the compounds: behind the upper lane, in the lower quarter's west corner, on the east slope
        AddCompound(-62f, 49.6f, 180f, 20f, 14f, false);
        AddCompound(-85f, -50f, 0f, 19f, 13f, false);
        AddCompound(62f, 32f, 180f, 22f, 16f, true);
        // houses along the lanes, main streets first
        var order = Enumerable.Range(0, lanes.Count).Where(i => lanes[i].Sides != 0).OrderBy(i => lanes[i].Main ? 0 : 1).ToList();
        foreach (int i in order)
        {
            if ((lanes[i].Sides & 1) != 0) Frontage(i, 1);
            if ((lanes[i].Sides & 2) != 0) Frontage(i, 2);
        }
        FillBlocks();
        // the farms and old houses out on the land
        House Farm(House h, Kind k)
        {
            h.Kind = k; Dress(h);
            if (k == Kind.Classic) h.Style = 2;
            if (!Fits(h)) return null;
            houses.Add(h);
            return h;
        }
        Farm(new House(-30f, -78f, 0f, 10f, 8f, 2, 2), Kind.Classic);
        Farm(new House(-15.5f, -80f, 0f, 9f, 5.5f, 1, 2), Kind.Shed);
        var white = Farm(new House(44f, -80f, 20f, 9f, 7f, 1, 0), Kind.Flat);
        if (white != null) { white.Porch = true; white.Colour = 0; white.Unfinished = false; }
        Farm(new House(30f, 78f, 200f, 6f, 5f, 1, 2), Kind.Cottage);
        var ruin = Farm(new House(-40f, 66f, 160f, 8f, 7f, 1, 2), Kind.Classic);
        if (ruin != null) { ruin.Ruin = true; ruin.Open = true; }
        Farm(new House(84f, 30f, 230f, 9f, 7f, 2, 0), Kind.Classic);
        Farm(new House(-70f, 64f, 170f, 6f, 5f, 1, 3), Kind.Cottage);
        Farm(new House(86f, -36f, 10f, 7f, 5f, 1, 2), Kind.Shed);
        CheckExtras();
        // the houses standing on open ground get their own levelled spot
        foreach (var h in houses)
            if (Built(h.X, h.Z) < 0.95f && !compounds.Any(c => c.Wings.Contains(h))) HousePad(new Vector3(h.X, 0, h.Z), h.W, h.D, h.Yaw);
    }

    /// <summary>
    /// What stands outside a house's walls must have room: the outside stair and the porch before its
    /// front, the stair to the roof along its right side. Where a neighbour or the lane is in the way,
    /// the house does without.
    /// </summary>
    private static void CheckExtras()
    {
        bool Clear(House h, float x0, float x1, float z0, float z1, float laneSlack)
        {
            for (float x = x0; x <= x1 + 0.01f; x += 0.5f)
                for (float z = z0; z <= z1 + 0.01f; z += 0.5f)
                {
                    var p = h.ToWorld(x, z);
                    if (KeptOut(p) || LaneDistance(p) < -laneSlack || LineDistance(tracks, p.x, p.y) < 0.3f) return false;
                    foreach (var o in houses) if (o != h && o.Distance(p) < 0.25f) return false;
                    foreach (var g in gardens) if (SegmentDistance(p, g.A, g.B) < 0.4f) return false;
                }
            return true;
        }
        foreach (var h in houses)
        {
            float w = h.W, d = h.D;
            if (h.OutsideStair && !Clear(h, -w / 2f, -w / 2f + 1.3f + 17 * 0.3f, d / 2f, d / 2f + 1.3f, 0.8f)) h.OutsideStair = false;
            if (h.Porch && !Clear(h, -2.8f, 2.8f, d / 2f, d / 2f + 2.5f, 0.3f)) h.Porch = false;
            if (h.RoofStair && !Clear(h, w / 2f, w / 2f + 1.3f, -d / 2f, d / 2f, 0.6f)) h.RoofStair = false;
            if (h.Yard && (!Clear(h, -w / 2f - 0.6f, w / 2f + 0.6f, d / 2f + h.YardDepth - 0.3f, d / 2f + h.YardDepth + 0.3f, -0.05f)
                || !Clear(h, -w / 2f - 0.6f, -w / 2f - 0.6f, d / 2f + 0.3f, d / 2f + h.YardDepth, -0.05f) || !Clear(h, w / 2f + 0.6f, w / 2f + 0.6f, d / 2f + 0.3f, d / 2f + h.YardDepth, -0.05f))) h.Yard = false;
        }
    }

    // ------------------------------------------------------------------ poles, lamps and spawn spots along the lanes

    /// <summary>
    /// Places along a lane's edge, about 'spacing' apart, on the side given (1 left, 2 right; 0
    /// alternating), clear of the houses: for the wooden electricity poles and the street lamps.
    /// </summary>
    private static List<Vector2> AlongLane(Lane lane, float spacing, int side, float inset, float start)
    {
        var spots = new List<Vector2>();
        int k = 0;
        for (float s = start; s < lane.Length - 2f; s += spacing, k++)
        {
            for (int tryAt = 0; tryAt < 5; tryAt++)
            {
                float ss = s + (tryAt % 2 == 0 ? 1f : -1f) * tryAt * 1.2f;
                var (p, t) = lane.At(ss);
                int sd = side == 0 ? (k % 2 == 0 ? 1 : 2) : side;
                var n = sd == 1 ? new Vector2(-t.y, t.x) : new Vector2(t.y, -t.x);
                var q = p + n * (lane.Width / 2f - inset);
                if (KeptOut(q)) continue;
                if (houses.Any(h => h.Distance(q) < 0.7f)) continue;
                if (gardens.Any(g => SegmentDistance(q, g.A, g.B) < 0.7f)) continue;
                spots.Add(q);
                break;
            }
        }
        return spots;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
        return Vector2.Distance(p, a + ab * t);
    }

    private static Lane LaneNamed(string name) => lanes.First(l => l.Name == name);
}
