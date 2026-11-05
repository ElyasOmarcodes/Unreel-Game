using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The buildings of the Hill Village, from the plan (HillVillagePlan.cs): old stone and whitewashed
/// houses with shutters, wooden balconies and outside stairs under tiled roofs; narrow three-storey
/// tower houses; flat-roofed houses of plaster or bare concrete with roller shutters, concrete balconies
/// with painted bar railings, porches on pillars, water tanks, dishes, aerials and the rods of the next
/// storey sticking out of the roof; shop houses with glass fronts, roller shutters, signs and awnings
/// below and a balcony of tiles and iron railings across the whole front above; cottages under gable or
/// lean-to roofs; sheds and barns; courtyard compounds with a gallery; walled gardens; the windmills on
/// the west hill and the cemetery with its cypresses.
/// </summary>
public static partial class HillVillageBuilder
{
    // ------------------------------------------------------------------ the colours of the village

    private static Material OchrePlaster => Scan("OchrePlaster", "WhitePlaster", new Color(1.32f, 1.12f, 0.8f), 0.12f, 2.5f, 0.5f);
    private static Material PinkPlaster => Scan("PinkPlaster", "WhitePlaster", new Color(1.34f, 1.1f, 1.04f), 0.12f, 2.5f, 0.5f);
    private static Material BluePlaster => Scan("BluePlaster", "WhitePlaster", new Color(1.1f, 1.2f, 1.32f), 0.12f, 2.5f, 0.5f);
    private static Material Concrete => Scan("Concrete", "RoughConcrete", new Color(0.95f, 0.95f, 0.93f), 0.1f, 3f, 0.8f);
    private static Material GreenPaint => Scan("GreenPaint", "GreenPlanks", new Color(1.05f, 1.1f, 1.05f), 0.3f, 1.4f);
    private static Material BrownPaint => Scan("BrownPaint", "PaintedPlanks", new Color(0.62f, 0.42f, 0.3f), 0.3f, 1.4f);
    private static Material GreyPaint => Scan("GreyPaint", "PaintedPlanks", new Color(1.05f, 1.06f, 1.08f), 0.3f, 1.4f);
    private static Material WhitePaint => Scan("WhitePaint", "PaintedPlanks", new Color(1.45f, 1.45f, 1.4f), 0.3f, 1.4f);
    private static Material Thatch => Scan("Thatch", "Thatch", new Color(1.3f, 1.12f, 0.82f), 0.05f, 2.5f);
    private static Material Corrugated => Scan("Corrugated", "Corrugated", new Color(1f, 0.95f, 0.9f), 0.35f, 2f);
    private static Material RollerShutter => Scan("RollerShutter", "RollerShutter", new Color(1f, 1f, 1f), 0.35f, 2f);
    private static Material Tiles => Scan("Tiles", "TerracottaTiles", new Color(1.05f, 0.95f, 0.9f), 0.35f, 1.5f);
    private static Material Bark => Scan("Bark", "PineBark", new Color(0.95f, 0.9f, 0.85f), 0.1f, 1.2f);
    private static Material Aluminium => AssetSetup.Solid("K_Aluminium", new Color(0.62f, 0.64f, 0.66f), 0.6f, 0.6f);
    private static Material Sail => AssetSetup.Solid("K_Sail", new Color(0.9f, 0.87f, 0.8f), 0.1f, 0f);
    private static Material WhitePlastic => AssetSetup.Solid("K_WhitePlastic", new Color(0.86f, 0.86f, 0.84f), 0.35f, 0f);
    private static Material BlackPlastic => AssetSetup.Solid("K_BlackPlastic", new Color(0.04f, 0.04f, 0.05f), 0.65f, 0f);
    private static Material BluePlastic => AssetSetup.Solid("K_BluePlastic", new Color(0.08f, 0.2f, 0.45f), 0.55f, 0f);
    private static Material GreenPlastic => AssetSetup.Solid("K_GreenPlastic", new Color(0.12f, 0.3f, 0.14f), 0.55f, 0f);
    private static Material Solar => AssetSetup.Solid("K_Solar", new Color(0.05f, 0.08f, 0.18f), 0.9f, 0.3f);
    private static Material Marble => AssetSetup.Solid("K_Marble", new Color(0.86f, 0.85f, 0.82f), 0.35f, 0f);
    private static Material Terracotta => AssetSetup.Solid("K_Pot", new Color(0.62f, 0.3f, 0.18f), 0.2f, 0f);
    private static Material Leaves => AssetSetup.Solid("K_Leaves", new Color(0.16f, 0.32f, 0.12f), 0.2f, 0f);
    private static Material Flowers => AssetSetup.Solid("K_Flowers", new Color(0.78f, 0.12f, 0.2f), 0.2f, 0f);

    private static Material WallOf(int colour)
    {
        switch (colour)
        {
            case 1: return CreamPlaster;
            case 2: return OchrePlaster;
            case 3: return PinkPlaster;
            case 4: return Concrete;
            case 5: return BluePlaster;
            default: return Plaster;
        }
    }

    private static Material PaintOf(int paint)
    {
        switch (paint)
        {
            case 1: return GreenPaint;
            case 2: return BrownPaint;
            case 3: return GreyPaint;
            case 4: return WhitePaint;
            default: return Shutter;
        }
    }

    private static readonly Color[] railColours =
    {
        new Color(0.16f, 0.32f, 0.56f), new Color(0.18f, 0.38f, 0.26f), new Color(0.32f, 0.2f, 0.13f),
        new Color(0.42f, 0.44f, 0.45f), new Color(0.86f, 0.86f, 0.82f), new Color(0.82f, 0.52f, 0.56f),
    };

    /// <summary>Painted iron (railings): the house's paint, or the pink of the concrete balconies (5).</summary>
    private static Material RailOf(int paint) => AssetSetup.Solid("K_Rail" + paint, railColours[Mathf.Clamp(paint, 0, railColours.Length - 1)], 0.45f, 0.45f);

    private static readonly Color[] signColours =
    {
        new Color(0.62f, 0.1f, 0.08f), new Color(0.1f, 0.34f, 0.18f), new Color(0.1f, 0.2f, 0.48f), new Color(0.85f, 0.62f, 0.12f), new Color(0.9f, 0.9f, 0.88f),
    };

    private static Material SignOf(int i) => AssetSetup.Solid("K_Sign" + i, signColours[i % signColours.Length], 0.4f, 0f);

    // ------------------------------------------------------------------ walls and openings

    private struct Opening
    {
        public float U, Width, Bottom, Top;   // along the wall from its middle; bottom and top above the floor
        public int Kind;                      // 0 window, 1 door, 2 balcony door, 3 shop window, 4 glass door
    }

    private const float Storey = 3.0f, Thick = 0.4f;

    /// <summary>The four walls of a house: origin (middle of the wall's thickness), direction along it, outward, length.</summary>
    private static (Vector3 o, Vector3 u, Vector3 n, float len) Side(int side, float w, float d)
    {
        switch (side)
        {
            case 0: return (new Vector3(0, 0, d / 2f - Thick / 2f), Vector3.right, Vector3.forward, w);
            case 1: return (new Vector3(0, 0, -d / 2f + Thick / 2f), Vector3.left, Vector3.back, w);
            case 2: return (new Vector3(-w / 2f + Thick / 2f, 0, 0), Vector3.forward, Vector3.left, d - 2f * Thick);
            default: return (new Vector3(w / 2f - Thick / 2f, 0, 0), Vector3.back, Vector3.right, d - 2f * Thick);
        }
    }

    private static Vector3 SideSize(Vector3 u, float along, float height, float across) =>
        Mathf.Abs(u.x) > 0.5f ? new Vector3(along, height, across) : new Vector3(across, height, along);

    /// <summary>One storey of one wall, with its openings cut out (open houses) or closed behind glass.</summary>
    private static void WallStorey(Transform h, int side, float w, float d, float y0, float y1, Material m, List<Opening> ops, bool holes)
    {
        var (o, u, n, len) = Side(side, w, d);
        void Piece(float a, float b, float c, float e)
        {
            if (b - a < 0.02f || e - c < 0.02f) return;
            Box("Wall", h, o + u * ((a + b) / 2f) + Vector3.up * ((c + e) / 2f), SideSize(u, b - a, e - c, Thick), m, true, 2.5f);
        }
        if (!holes || ops.Count == 0) { Piece(-len / 2f, len / 2f, y0, y1); return; }
        float at = -len / 2f;
        foreach (var op in ops.OrderBy(q => q.U))
        {
            float l = op.U - op.Width / 2f, r = op.U + op.Width / 2f;
            Piece(at, l, y0, y1);
            Piece(l, r, y0, y0 + op.Bottom);
            Piece(l, r, y0 + op.Top, y1);
            at = r;
        }
        Piece(at, len / 2f, y0, y1);
    }

    /// <summary>
    /// What makes an opening look lived in: a painted frame and a sill, shutters (wooden ones open
    /// against the wall or closed, or a roller shutter in its box), glass behind glazing bars when the
    /// house is closed, an iron grille on some ground floor windows, a pot of flowers on some sills; a
    /// door in a door. Shop windows and glass doors have aluminium frames and roller shutters.
    /// </summary>
    private static void DressOpening(Transform h, int side, float w, float d, float y0, Opening op, bool holes, bool stoneWall, bool ground, bool old, Material paint, int shutters, bool closedShop)
    {
        var (o, u, n, _) = Side(side, w, d);
        var face = o + n * (Thick / 2f);
        var c = face + u * op.U + Vector3.up * (y0 + (op.Bottom + op.Top) / 2f);
        float hgt = op.Top - op.Bottom, wid = op.Width;
        if (op.Kind >= 3) { ShopOpening(h, c, u, n, op, holes, closedShop); return; }
        const float bar = 0.08f;
        var frame = shutters == 1 ? Aluminium : paint;
        Box("Frame", h, c + u * (wid / 2f + bar / 2f) + n * 0.03f, SideSize(u, bar, hgt + bar * 2f, 0.1f), frame, false, 1.4f);
        Box("Frame", h, c - u * (wid / 2f + bar / 2f) + n * 0.03f, SideSize(u, bar, hgt + bar * 2f, 0.1f), frame, false, 1.4f);
        Box("Frame", h, c + Vector3.up * (hgt / 2f + bar / 2f) + n * 0.03f, SideSize(u, wid, bar, 0.1f), frame, false, 1.4f);
        if (op.Kind == 0)
        {
            Box("Sill", h, c - Vector3.up * (hgt / 2f + 0.04f) + n * 0.1f, SideSize(u, wid + 0.3f, 0.07f, 0.24f), Coping, false, 1.5f);
            if (!holes)
            {
                Box("Glass", h, c - n * 0.005f, SideSize(u, wid, hgt, 0.02f), Glass, false);
                Box("Bar", h, c + n * 0.02f, SideSize(u, 0.05f, hgt, 0.04f), frame, false, 1.4f);
                if (shutters != 1) Box("Bar", h, c + Vector3.up * (hgt * 0.12f) + n * 0.02f, SideSize(u, wid, 0.05f, 0.04f), frame, false, 1.4f);
                if (Chance(0.3)) NightLights.Window(h, h.TransformPoint(c + n * 0.03f), h.TransformDirection(n), wid * 0.9f, hgt * 0.9f, night);
            }
            if (ground && Chance(0.35))
                for (int k = 1; k <= 4; k++)
                    Box("Grille", h, c + u * (-wid / 2f + wid * k / 5f) + n * 0.07f, SideSize(u, 0.025f, hgt, 0.025f), Iron, false);
            if (shutters == 1)
            {
                // a roller shutter: its box over the window, the slats let down part of the way
                Box("ShutterBox", h, c + Vector3.up * (hgt / 2f + bar + 0.13f) + n * 0.09f, SideSize(u, wid + 0.2f, 0.26f, 0.2f), WhitePlastic, false, 1.5f);
                float down = Chance(0.3) ? R(0.55f, 1f) : R(0f, 0.4f);
                if (holes) down = Mathf.Min(down, 0.4f);
                if (down > 0.05f) Box("Roller", h, c + Vector3.up * (hgt / 2f - hgt * down / 2f) + n * 0.06f, SideSize(u, wid + 0.04f, hgt * down, 0.03f), GreyPaint, false, 1f);
            }
            else
            {
                // wooden shutters: open flat against the wall either side, closed over the window, or one gone
                int state = Chance(0.2) && !holes ? 1 : Chance(old ? 0.25 : 0.06) ? 2 : 0;
                float sw = wid / 2f;
                for (int s = -1; s <= 1; s += 2)
                {
                    if (state == 2 && s > 0) continue;
                    var at = state == 1 ? c + u * (s * sw / 2f) + n * 0.07f : c + u * (s * (wid / 2f + bar + sw / 2f + 0.02f)) + n * 0.04f;
                    Box("Shutter", h, at, SideSize(u, sw - 0.02f, hgt, 0.035f), paint, false, 0.7f);
                }
            }
            if (!ground && Chance(0.14))
            {
                // a pot of red flowers on the sill
                var pot = c - Vector3.up * (hgt / 2f - 0.1f) + n * 0.16f;
                Box("Pot", h, pot, SideSize(u, Mathf.Min(0.7f, wid * 0.7f), 0.2f, 0.18f), Terracotta, false);
                Box("PotLeaves", h, pot + Vector3.up * 0.16f, SideSize(u, Mathf.Min(0.66f, wid * 0.66f), 0.14f, 0.2f), Chance(0.5) ? Flowers : Leaves, false);
            }
        }
        else
        {
            // the door: a lintel of stone or wood over it, the leaf shut, or swung open inside
            Box("Lintel", h, c + Vector3.up * (hgt / 2f + bar + 0.1f) + n * 0.02f, SideSize(u, wid + 0.5f, 0.2f, 0.06f), stoneWall ? Stone : shutters == 1 ? Coping : Wood, false);
            var leafMat = shutters == 1 ? (Chance(0.5) ? GreyPaint : paint) : Chance(0.5) ? paint : DoorWood;
            if (!holes) Box("Door", h, c - n * 0.01f, SideSize(u, wid, hgt, 0.06f), leafMat, false, 1.4f);
            else Box("Door", h, c - u * (wid / 2f - 0.04f) - n * (Thick + wid / 2f), SideSize(u, 0.06f, hgt, wid), leafMat, false, 1.4f);
            if (op.Kind == 1)
                Box("DoorStep", h, face + u * op.U + Vector3.up * (y0 - 0.06f) + n * 0.25f, SideSize(u, wid + 0.4f, 0.12f, 0.5f), Coping, true, 1.5f);
        }
    }

    /// <summary>A shop window or glass door: aluminium frame, glass, the roller shutter's box over it, the shutter part way down.</summary>
    private static void ShopOpening(Transform h, Vector3 c, Vector3 u, Vector3 n, Opening op, bool holes, bool closed)
    {
        float hgt = op.Top - op.Bottom, wid = op.Width;
        Box("ShopFrame", h, c + u * (wid / 2f + 0.04f), SideSize(u, 0.08f, hgt + 0.16f, 0.14f), Aluminium, false);
        Box("ShopFrame", h, c - u * (wid / 2f + 0.04f), SideSize(u, 0.08f, hgt + 0.16f, 0.14f), Aluminium, false);
        Box("ShopFrame", h, c + Vector3.up * (hgt / 2f + 0.04f), SideSize(u, wid + 0.16f, 0.08f, 0.14f), Aluminium, false);
        if (op.Kind == 3) Box("ShopFrame", h, c - Vector3.up * (hgt / 2f + 0.04f), SideSize(u, wid + 0.16f, 0.08f, 0.14f), Aluminium, false);
        if (!holes)
        {
            Box("ShopGlass", h, c - n * 0.02f, SideSize(u, wid, hgt, 0.02f), Glass, false);
            if (op.Kind == 3 && wid > 1.6f) Box("Mullion", h, c, SideSize(u, 0.06f, hgt, 0.08f), Aluminium, false);
            if (op.Kind == 4) Box("DoorBar", h, c + n * 0.04f, SideSize(u, wid * 0.7f, 0.05f, 0.05f), Aluminium, false);
            if (Chance(0.45)) NightLights.Window(h, h.TransformPoint(c + n * 0.03f), h.TransformDirection(n), wid * 0.92f, hgt * 0.9f, night);
        }
        else if (op.Kind == 4)
            Box("ShopDoor", h, c - u * (wid / 2f - 0.04f) - n * (Thick + wid / 2f), SideSize(u, 0.05f, hgt, wid), Glass, false);
        Box("ShutterBox", h, c + Vector3.up * (hgt / 2f + 0.23f) + n * 0.12f, SideSize(u, wid + 0.2f, 0.3f, 0.26f), Aluminium, false);
        float down = closed ? 1f : Chance(0.3) ? R(0.1f, 0.35f) : 0f;
        if (down > 0.02f)
            Box("Roller", h, c + Vector3.up * (hgt / 2f - hgt * down / 2f) + n * 0.09f, SideSize(u, wid + 0.08f, hgt * down, 0.04f), RollerShutter, closed, 1.2f);
    }

    /// <summary>Openings of one storey of one wall: windows spaced along it, a door or balcony door at given places.</summary>
    private static List<Opening> Openings(float len, float? door, int doorKind, float avoidFrom = 99f, float avoidTo = -99f, float winW = 0.9f, float winBottom = 0.95f)
    {
        var ops = new List<Opening>();
        float pitch = winW + 1.5f;
        int nWin = Mathf.Max(1, Mathf.FloorToInt((len - 1.2f) / pitch));
        for (int i = 0; i < nWin; i++)
        {
            float u = -len / 2f + len * (i + 1) / (nWin + 1);
            if (door.HasValue && Mathf.Abs(u - door.Value) < winW / 2f + 0.95f) continue;
            if (u > avoidFrom && u < avoidTo) continue;
            ops.Add(new Opening { U = u, Width = winW, Bottom = winBottom, Top = 2.25f, Kind = 0 });
        }
        if (door.HasValue)
            ops.Add(new Opening { U = door.Value, Width = doorKind == 1 ? 1.05f : 1.0f, Bottom = 0f, Top = doorKind == 1 ? 2.2f : 2.3f, Kind = doorKind });
        return ops;
    }

    /// <summary>The ground floor front of a shop: a glass door and wide windows, as many bays as the front takes.</summary>
    private static List<Opening> ShopOpenings(float len)
    {
        var ops = new List<Opening>();
        int bays = Mathf.Max(1, Mathf.FloorToInt((len - 0.6f) / 3.3f));
        float bay = len / bays;
        for (int i = 0; i < bays; i++)
        {
            float u = -len / 2f + bay * (i + 0.5f);
            bool door = i == bays / 2 || bays > 2 && Chance(0.25);
            ops.Add(door ? new Opening { U = u, Width = 1.5f, Bottom = 0f, Top = 2.7f, Kind = 4 }
                         : new Opening { U = u, Width = Mathf.Min(2.6f, bay - 0.7f), Bottom = 0.45f, Top = 2.7f, Kind = 3 });
        }
        return ops;
    }

    /// <summary>The ground under a footprint: highest and lowest point.</summary>
    private static (float hi, float lo) Under(Vector3 c, Quaternion rot, float w, float d)
    {
        float hi = float.MinValue, lo = float.MaxValue;
        for (int i = -2; i <= 2; i++)
            for (int k = -2; k <= 2; k++)
            {
                var p = c + rot * new Vector3(i * w / 4f, 0, k * d / 4f);
                float y = Y(p.x, p.z);
                hi = Mathf.Max(hi, y); lo = Mathf.Min(lo, y);
            }
        return (hi, lo);
    }

    /// <summary>A box in a house's space turned about its own x axis (sloping roofs, awnings), with its collider.</summary>
    private static GameObject Tilted(string name, Transform h, Vector3 local, Vector3 size, float pitch, Material m, bool collider, float tex = 2f)
    {
        var go = MeshGen.Object(name, MeshGen.Box(size, tex), m, h, h.TransformPoint(local));
        go.transform.rotation = h.rotation * Quaternion.Euler(pitch, 0f, 0f);
        if (collider) go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    // ------------------------------------------------------------------ houses

    /// <summary>
    /// A house of the plan: its foundation into the slope, its storeys with their openings (holes and
    /// floors inside when it is open, a stair inside up to the first floor), bands and corner stones on
    /// the old ones, balcony, outside stair, roof (hip, gable, flat behind a parapet or lean-to) and what
    /// stands on it, porch, chimney, the front steps when the door is above the street, and its yard.
    /// </summary>
    private static void BuildHouse(House hs, Transform parent)
    {
        var rot = Quaternion.Euler(0, hs.Yaw, 0);
        var centre = new Vector3(hs.X, 0, hs.Z);
        var (hi, lo) = Under(centre, rot, hs.W + 1f, hs.D + 1f);
        float baseY = hi + 0.15f;
        var h = new GameObject(hs.Kind.ToString()).transform;
        h.SetParent(parent, false);
        h.SetPositionAndRotation(new Vector3(centre.x, baseY, centre.z), rot);
        float w = hs.W, d = hs.D;
        float drop = baseY - lo + 0.4f;
        if (hs.Kind == Kind.Shed) { Shed(h, hs, drop); return; }
        bool flat = hs.Roof == 2, shop = hs.Kind == Kind.Shop, old = hs.Style >= 2;
        var plaster = WallOf(hs.Colour);
        var upper = hs.Style == 0 || hs.Style == 1 ? plaster : hs.Style == 2 ? Stone : OldPlaster;
        var lower = hs.Style == 1 || hs.Style == 2 ? Stone : upper;
        if (flat) { upper = lower = plaster; }
        var paint = PaintOf(hs.Paint);
        int shutters = hs.Kind == Kind.Flat && (Mathf.Abs(Mathf.RoundToInt(hs.X * 7f + hs.Z * 13f)) % 3 != 0) ? 1 : 0;
        if (hs.Kind == Kind.Flat && shutters == 0) paint = GreyPaint;
        // the foundation down into the slope, a band of field stone (a plain plinth on the newer houses) round the foot
        Box("Foundation", h, new Vector3(0, -drop / 2f, 0), new Vector3(w, drop, d), flat ? Coping : Stone, true, 2.6f);
        float band = flat ? 0.35f : 0.55f;
        foreach (int s in new[] { 0, 1, 2, 3 })
        {
            var (o, u, n, len) = Side(s, w, d);
            float bandLen = s < 2 ? len + 0.12f : d + 0.12f;
            var bc = (s < 2 ? o : new Vector3(o.x, 0, 0)) + n * (Thick / 2f + 0.03f) + Vector3.up * ((band - drop) / 2f);
            Box("StoneBand", h, bc, SideSize(u, bandLen, band + drop, 0.06f), flat ? Coping : Stone, false, 2.6f);
        }
        if (hs.Ruin) { Ruin(h, hs, lower); return; }

        float groundH = shop ? 3.6f : Storey;
        float FloorY(int f) => f == 0 ? 0f : groundH + (f - 1) * Storey;
        float top = FloorY(hs.Floors) + (flat ? 0f : 0.3f);
        bool closedShop = shop && !hs.Open && Chance(0.3);
        // where the doors go: the front door in the middle of the front, or on the right side when an
        // outside stair runs up the left half of the front to the upper door
        float stairRun = Mathf.CeilToInt(groundH / 0.18f) * 0.3f;
        bool stair = hs.OutsideStair && hs.Floors > 1 && w >= 6.8f && !shop;
        float balconyW = Mathf.Min(4.2f, w * 0.5f);
        float balconyU = stair ? w / 2f - balconyW / 2f - 0.5f : 0f;
        bool woodBalcony = hs.Balcony && hs.Floors > 1 && !flat && !shop && (!stair || w >= 8.5f);
        bool concreteBalcony = hs.Balcony && hs.Floors > 1 && flat && !shop && !hs.Porch;
        bool ironBalcony = hs.Balcony && shop;
        var ironAt = new bool[hs.Floors + 1];
        for (int f = 1; f < hs.Floors; f++) ironAt[f] = ironBalcony && (f == 1 || Chance(0.5));
        float cbW = Mathf.Min(w - 1f, R(3.2f, 5.5f)), cbU = Mathf.Clamp(R(-1.5f, 1.5f), -(w - cbW) / 2f + 0.3f, (w - cbW) / 2f - 0.3f);
        bool innerStair = hs.Open && hs.Floors > 1 && w - 2f * Thick >= stairRun + 1.3f && d >= 5.2f && !hs.Gallery;
        float avoidFrom = stair ? -w / 2f - 1f : 99f, avoidTo = stair ? -w / 2f + 1.3f + stairRun + 0.4f : -99f;
        float winW = flat ? 1.2f : 0.9f, winBottom = flat ? 0.9f : 0.95f;
        for (int f = 0; f < hs.Floors; f++)
        {
            float y0 = FloorY(f), y1 = FloorY(f + 1) + (f == hs.Floors - 1 && !flat ? 0.3f : 0f);
            var m = f == 0 ? lower : upper;
            for (int s = 0; s < 4; s++)
            {
                var (_, _, _, len) = Side(s, w, d);
                List<Opening> ops;
                if (s == 0 && f == 0 && shop) ops = ShopOpenings(len);
                else if (s == 0 && f == 0) ops = stair ? Openings(len, null, 0, avoidFrom, avoidTo, winW, winBottom) : Openings(len, hs.Kind == Kind.Flat ? R(-len / 4f, len / 4f) : 0f, 1, 99f, -99f, winW, winBottom);
                else if (s == 0 && f >= 1 && hs.Gallery) ops = GalleryOpenings(len);
                else if (s == 0 && f >= 1 && ironAt[f]) ops = GalleryOpenings(len);
                else if (s == 0 && f == 1 && hs.Porch) ops = Openings(len, 0f, 2, 99f, -99f, winW, winBottom);
                else if (s == 0 && f >= 1 && concreteBalcony) ops = Openings(len, cbU, 2, cbU - cbW / 2f, cbU + cbW / 2f, winW, winBottom);
                else if (s == 0 && f == 1 && (stair || woodBalcony))
                {
                    ops = Openings(len, woodBalcony ? balconyU : (float?)null, 2, stair ? -w / 2f - 1f : 99f, stair ? -w / 2f + 1.5f : -99f, winW, winBottom);
                    if (stair) ops.Add(new Opening { U = -w / 2f + 0.75f, Width = 0.95f, Bottom = 0f, Top = 2.2f, Kind = 2 });
                }
                else if (s == 3 && f == 0 && stair) ops = Openings(len, 0f, 1, 99f, -99f, winW, winBottom);
                else if (s == 1 && f == 0 && innerStair) ops = new List<Opening>();
                else ops = len < 4f ? new List<Opening> { new Opening { U = 0f, Width = 0.8f, Bottom = 0.95f, Top = 2.15f } } : Openings(len, null, 0, 99f, -99f, winW, winBottom);
                if (s == 0 && f == 0 && shop) ops = ops.Select(q => { q.Top = Mathf.Min(q.Top, groundH - 0.9f); return q; }).ToList();
                WallStorey(h, s, w, d, y0, y1, m, ops, hs.Open);
                foreach (var op in ops) DressOpening(h, s, w, d, y0, op, hs.Open, m == Stone, f == 0, old, paint, shutters, closedShop);
            }
        }
        // floors inside (open houses): boards or tiles, the floors above with the stair's well
        if (hs.Open)
        {
            Box("Floor", h, new Vector3(0, 0.03f, 0), new Vector3(w - 2f * Thick, 0.06f, d - 2f * Thick), flat || shop ? Tiles : Wood, true, 1.5f);
            for (int f = 1; f < hs.Floors; f++)
                UpperFloor(h, w, d, FloorY(f), f == 1 && innerStair ? stairRun : 0f, flat || shop ? Concrete : Wood);
            if (innerStair) InnerStair(h, w, d, groundH, flat || shop ? Coping : Wood);
        }
        // the old houses: whitewash bands under the eaves and between the storeys, dressed stone quoins
        if (!flat && !shop && (hs.Style == 0 || hs.Style == 3 || hs.Style == 1))
        {
            foreach (int s in new[] { 0, 1, 2, 3 })
            {
                var (o, u, n, len) = Side(s, w, d);
                float bandLen = (s < 2 ? len : d) + 0.14f;
                var bo = (s < 2 ? o : new Vector3(o.x, 0, 0)) + n * (Thick / 2f + 0.035f);
                Box("EaveBand", h, bo + Vector3.up * (top - 0.12f), SideSize(u, bandLen, 0.24f, 0.07f), Coping, false, 2f);
                for (int f = 1; f < hs.Floors; f++)
                    Box("FloorBand", h, bo + Vector3.up * FloorY(f), SideSize(u, bandLen, 0.16f, 0.07f), hs.Style == 1 ? Stone : Coping, false, 2f);
            }
        }
        if (!flat && (hs.Style == 0 || hs.Style == 1) || shop) Quoins(h, w, d, hs.Style == 1 ? Storey + 0.1f : 0.55f, top);
        if (flat) foreach (int f in Enumerable.Range(1, hs.Floors - 1))
            {
                // the concrete floor slabs show as a band round the newer houses
                foreach (int s in new[] { 0, 1, 2, 3 })
                {
                    var (o, u, n, len) = Side(s, w, d);
                    var bo = (s < 2 ? o : new Vector3(o.x, 0, 0)) + n * (Thick / 2f + 0.03f);
                    Box("SlabBand", h, bo + Vector3.up * (FloorY(f) - 0.05f), SideSize(u, (s < 2 ? len : d) + 0.1f, 0.22f, 0.06f), Coping, false, 2f);
                }
            }

        // balconies
        if (woodBalcony)
        {
            float bz = d / 2f + 0.6f, by = Storey - 0.02f;
            Box("Balcony", h, new Vector3(balconyU, by - 0.08f, bz), new Vector3(balconyW, 0.16f, 1.2f), Coping, true, 2f);
            for (int i = -1; i <= 1; i++)
                Box("Bracket", h, new Vector3(balconyU + i * (balconyW / 2f - 0.3f), by - 0.42f, d / 2f + 0.45f), new Vector3(0.12f, 0.5f, 0.9f), Wood, false, 1.5f);
            Railing(h, new Vector3(balconyU - balconyW / 2f + 0.05f, by, d / 2f + 1.15f), new Vector3(balconyU + balconyW / 2f - 0.05f, by, d / 2f + 1.15f), Wood);
            Railing(h, new Vector3(balconyU - balconyW / 2f + 0.05f, by, d / 2f + 0.05f), new Vector3(balconyU - balconyW / 2f + 0.05f, by, d / 2f + 1.15f), Wood);
            Railing(h, new Vector3(balconyU + balconyW / 2f - 0.05f, by, d / 2f + 0.05f), new Vector3(balconyU + balconyW / 2f - 0.05f, by, d / 2f + 1.15f), Wood);
        }
        if (concreteBalcony)
        {
            var rail = RailOf(Chance(0.45) ? 5 : hs.Paint);
            bool bars = Chance(0.65);
            for (int f = 1; f < hs.Floors; f++) ConcreteBalcony(h, cbU, cbW, d, FloorY(f), rail, bars);
        }
        if (ironBalcony)
        {
            var rail = Chance(0.6) ? Iron : RailOf(hs.Paint);
            for (int f = 1; f < hs.Floors; f++)
                if (ironAt[f]) IronBalcony(h, w, d, FloorY(f), rail);
        }
        if (shop) ShopFront(h, w, d, groundH, ironBalcony);

        // the outside stair up the front to the upper door, with a landing and an iron railing
        if (stair)
        {
            float rise = groundH + 0.02f;
            var foot = h.TransformPoint(new Vector3(-w / 2f + 1.3f + stairRun, 0f, d / 2f + 0.62f));
            StepsDown(h, foot, h.TransformDirection(Vector3.left), rise, 1.15f, h.TransformDirection(Vector3.forward), drop, old ? Stone : flat ? Coping : Masonry);
            Box("Landing", h, new Vector3(-w / 2f + 0.65f, (rise - drop) / 2f, d / 2f + 0.62f), new Vector3(1.3f, rise + drop, 1.24f), old ? Stone : flat ? Coping : Masonry, true, 2.6f);
            Railing(h, new Vector3(-w / 2f + 0.05f, rise, d / 2f + 1.2f), new Vector3(-w / 2f + 1.25f, rise, d / 2f + 1.2f), Iron);
        }
        if (hs.Gallery) Gallery(h, w, d);
        if (hs.Porch) Porch(h, hs, w, d, groundH, plaster);

        // the roof
        float ridge = top;
        if (hs.Roof == 0 || hs.Roof == 1) ridge = RoofOver(h, w, d, top, hs.Roof == 0, upper);
        else if (hs.Roof == 2) FlatRoof(h, hs, w, d, top, plaster);
        else ridge = LeanTo(h, w, d, top, upper, hs.Kind == Kind.Cottage && Chance(0.5) ? Roof : Corrugated);
        if (hs.Chimney && hs.Roof != 2)
        {
            var cp = new Vector3(w * 0.22f, 0, -d * 0.12f);
            float cb = top + 0.4f, ct = ridge + 0.7f;
            Box("Chimney", h, cp + Vector3.up * ((cb + ct) / 2f), new Vector3(0.6f, ct - cb, 0.6f), hs.Style == 2 ? Stone : upper, false, 2.5f);
            Box("ChimneyCap", h, cp + Vector3.up * (ct + 0.05f), new Vector3(0.8f, 0.1f, 0.8f), Coping, false);
            Box("ChimneyHat", h, cp + Vector3.up * (ct + 0.32f), new Vector3(0.7f, 0.08f, 0.7f), Roof, false);
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, 1f), (1f, -1f), (-1f, 1f) })
                Box("ChimneyPost", h, cp + new Vector3(sx * 0.28f, ct + 0.18f, sz * 0.28f), new Vector3(0.08f, 0.26f, 0.08f), Stone, false);
        }
        if (hs.Dish && hs.Roof != 2)
        {
            // a satellite dish on a bracket high on a side wall
            float sx = Chance(0.5) ? -1f : 1f;
            Dish(h, h.TransformPoint(new Vector3(sx * (w / 2f + 0.35f), top - 0.6f, d / 4f)), hs.Yaw + sx * 90f);
        }
        if (hs.Aircon)
            for (int f = 0; f < hs.Floors; f++)
                if (Chance(0.6))
                {
                    float sx = Chance(0.5) ? -1f : 1f;
                    var local = new Vector3(sx * (w / 2f + 0.15f), FloorY(f) + 2.3f, R(-d / 4f, d / 4f));
                    Aircon(h, local, new Vector3(sx, 0, 0));
                }
        // front steps when the door is well above the street
        var fp = h.TransformPoint(new Vector3(0, 0, d / 2f + 1.2f));
        if (!stair && !shop && baseY - Y(fp.x, fp.z) > 0.35f)
        {
            var g = h.TransformPoint(new Vector3(0, 0, d / 2f + 0.3f));
            float down = baseY - Y(g.x, g.z);
            StepsDown(h, h.TransformPoint(new Vector3(0, -down, d / 2f + 0.3f + Mathf.CeilToInt(down / 0.18f) * 0.3f)), h.TransformDirection(Vector3.back), down, 1.6f, Vector3.zero, 0.3f, Masonry);
        }
        else if (shop && baseY - Y(fp.x, fp.z) > 0.2f)
        {
            // a stepped pavement along the shop front where the street falls away
            var g = h.TransformPoint(new Vector3(0, 0, d / 2f + 0.6f));
            float down = baseY - Y(g.x, g.z);
            Box("ShopPavement", h, new Vector3(0, -down / 2f - 0.02f, d / 2f + 0.6f), new Vector3(w + 0.2f, down + 0.04f, 1.2f), Paving, true, 3f);
        }
        if (hs.Yard) Yard(h, hs, w, d);
    }

    /// <summary>Dressed stone quoins at the corners, alternately long and short, from 'from' to 'top'.</summary>
    private static void Quoins(Transform h, float w, float d, float from, float top)
    {
        int k = 0;
        for (float y = from; y + 0.36f < top - 0.25f; y += 0.38f, k++)
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                float a = k % 2 == 0 ? 0.55f : 0.32f, b = k % 2 == 0 ? 0.32f : 0.55f;
                Box("Quoin", h, new Vector3(sx * (w / 2f - a / 2f + 0.035f), y + 0.17f, sz * (d / 2f + 0.035f)), new Vector3(a + 0.07f, 0.34f, 0.07f), Stone, false, 2.6f);
                Box("Quoin", h, new Vector3(sx * (w / 2f + 0.035f), y + 0.17f, sz * (d / 2f - b / 2f)), new Vector3(0.07f, 0.34f, b), Stone, false, 2.6f);
            }
    }

    /// <summary>Balcony doors along an upper floor's front (onto a gallery or a balcony across the front), windows between.</summary>
    private static List<Opening> GalleryOpenings(float len)
    {
        var ops = new List<Opening>();
        int n = Mathf.Max(1, Mathf.FloorToInt((len - 0.8f) / 2.6f));
        for (int i = 0; i < n; i++)
        {
            float u = -len / 2f + len * (i + 0.5f) / n;
            ops.Add(i % 2 == 0 ? new Opening { U = u, Width = 1.0f, Bottom = 0f, Top = 2.3f, Kind = 2 } : new Opening { U = u, Width = 0.9f, Bottom = 0.95f, Top = 2.25f, Kind = 0 });
        }
        return ops;
    }

    /// <summary>An upper floor inside, the well of the stair cut out of it along the back wall.</summary>
    private static void UpperFloor(Transform h, float w, float d, float y, float well, Material m)
    {
        float iw = w - 2f * Thick, id = d - 2f * Thick;
        if (well <= 0f) { Box("UpperFloor", h, new Vector3(0, y - 0.1f, 0), new Vector3(iw, 0.22f, id), m, true, 1.5f); return; }
        float wellD = 1.15f, x0 = -w / 2f + Thick + 0.25f, x1 = x0 + well;
        float zBack = -d / 2f + Thick, zWell = zBack + wellD;
        Box("UpperFloor", h, new Vector3(0, y - 0.1f, (zWell + d / 2f - Thick) / 2f), new Vector3(iw, 0.22f, d / 2f - Thick - zWell), m, true, 1.5f);
        Box("UpperFloor", h, new Vector3((x1 + w / 2f - Thick) / 2f, y - 0.1f, (zBack + zWell) / 2f), new Vector3(w / 2f - Thick - x1, 0.22f, wellD), m, true, 1.5f);
        Box("UpperFloor", h, new Vector3((-w / 2f + Thick + x0) / 2f, y - 0.1f, (zBack + zWell) / 2f), new Vector3(x0 + w / 2f - Thick, 0.22f, wellD), m, true, 1.5f);
        Railing(h, new Vector3(x0, y, zWell + 0.04f), new Vector3(x1 - 0.9f, y, zWell + 0.04f), Iron);
    }

    /// <summary>A straight stair inside along the back wall, up to the first floor (its top at 'rise').</summary>
    private static void InnerStair(Transform h, float w, float d, float rise, Material m)
    {
        int count = Mathf.CeilToInt(rise / 0.18f);
        float stepH = rise / count, run = 0.3f;
        float x0 = -w / 2f + Thick + 0.25f, z = -d / 2f + Thick + 0.575f;
        for (int i = 0; i < count; i++)
        {
            float top = stepH * (i + 1);
            Box("Step", h, new Vector3(x0 + run * (i + 0.5f), top / 2f, z), new Vector3(run + 0.02f, top, 1.1f), m, true, 1.5f);
        }
    }

    /// <summary>A flight of steps (see Steps) whose blocks reach 'below' metres under its foot.</summary>
    private static void StepsDown(Transform t, Vector3 foot, Vector3 dir, float rise, float width, Vector3 wallSide, float below, Material m)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(rise) / 0.18f));
        float stepH = rise / count, run = 0.3f;
        dir.y = 0; dir.Normalize();
        float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        float bottom = foot.y - below;
        for (int i = 0; i < count; i++)
        {
            var p = foot + dir * (run * (i + 0.5f));
            float topY = foot.y + stepH * (i + 1);
            MeshGen.TexBox("Step", new Vector3(p.x, (topY + bottom) / 2f, p.z), new Vector3(width, topY - bottom, run + 0.02f), m, 2f, t, yaw);
        }
        if (wallSide != Vector3.zero)
            Railing(null, foot + wallSide.normalized * (width / 2f - 0.05f) + Vector3.up * 0.05f, foot + dir * (run * count) + wallSide.normalized * (width / 2f - 0.05f) + Vector3.up * (rise + 0.05f), Iron, t);
    }

    /// <summary>
    /// A railing from a to b (posts every ~0.9 m, a top and a middle rail, 1 m high). In the house's own
    /// space when 'h' is given, else in world space under 'worldParent'. Sloping when a and b differ in height.
    /// </summary>
    private static void Railing(Transform h, Vector3 a, Vector3 b, Material m, Transform worldParent = null)
    {
        var A = h != null ? h.TransformPoint(a) : a;
        var B = h != null ? h.TransformPoint(b) : b;
        var parent = h ?? worldParent;
        float len = Vector3.Distance(new Vector3(A.x, 0, A.z), new Vector3(B.x, 0, B.z));
        int posts = Mathf.Max(1, Mathf.CeilToInt(len / 0.9f));
        bool iron = m == Iron;
        float post = iron ? 0.04f : 0.09f;
        for (int i = 0; i <= posts; i++)
        {
            var p = Vector3.Lerp(A, B, i / (float)posts);
            var go = MeshGen.Object("RailPost", MeshGen.Box(new Vector3(post, 1.0f, post), 1.5f), m, parent, p + Vector3.up * 0.5f);
            go.AddComponent<BoxCollider>().size = new Vector3(post, 1.0f, post);
        }
        Beam("Rail", parent, A + Vector3.up * 1.0f, B + Vector3.up * 1.0f, iron ? 0.05f : 0.1f, m, true);
        Beam("Rail", parent, A + Vector3.up * 0.5f, B + Vector3.up * 0.5f, iron ? 0.03f : 0.07f, m, true);
    }

    /// <summary>
    /// An iron railing of close vertical bars (house space, a to b on the floor): a top rail, a foot rail,
    /// a band near the top with a diamond in every third bay; one thin collider along it.
    /// </summary>
    private static void BarRailing(Transform h, Vector3 a, Vector3 b, Material m, float height = 1.0f, bool diamonds = true)
    {
        var A = h.TransformPoint(a); var B = h.TransformPoint(b);
        float len = Vector3.Distance(A, B);
        if (len < 0.1f) return;
        Beam("RailTop", h, A + Vector3.up * height, B + Vector3.up * height, 0.05f, m);
        Beam("RailFoot", h, A + Vector3.up * 0.08f, B + Vector3.up * 0.08f, 0.04f, m);
        Beam("RailBand", h, A + Vector3.up * (height - 0.2f), B + Vector3.up * (height - 0.2f), 0.03f, m);
        int bars = Mathf.Max(2, Mathf.CeilToInt(len / 0.13f));
        var barMesh = MeshGen.Box(new Vector3(0.02f, height - 0.08f, 0.02f), 1f);
        var turn = Quaternion.LookRotation((B - A).normalized);
        for (int i = 0; i <= bars; i++)
        {
            var p = Vector3.Lerp(A, B, i / (float)bars);
            var bar = MeshGen.Object("RailBar", barMesh, m, h, p + Vector3.up * (height / 2f + 0.04f));
            bar.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (diamonds && i % 3 == 1 && i < bars)
            {
                var q = Vector3.Lerp(A, B, (i + 0.5f) / bars) + Vector3.up * (height - 0.1f);
                var dia = MeshGen.Object("RailScroll", MeshGen.Box(new Vector3(0.02f, 0.11f, 0.11f), 1f), m, h, q);
                dia.transform.rotation = turn * Quaternion.Euler(45f, 0f, 0f);
                dia.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        var col = new GameObject("RailCollider");
        col.transform.SetParent(h, false);
        col.transform.SetPositionAndRotation((A + B) / 2f + Vector3.up * (height / 2f), turn);
        col.AddComponent<BoxCollider>().size = new Vector3(0.06f, height, len);
    }

    /// <summary>The balcony across a shop's whole front: a slab tiled on top on the floor's level, an iron railing round it.</summary>
    private static void IronBalcony(Transform h, float w, float d, float y, Material rail)
    {
        const float depth = 1.15f;
        Box("BalconySlab", h, new Vector3(0, y - 0.11f, d / 2f + depth / 2f), new Vector3(w + 0.3f, 0.2f, depth), Coping, true, 2f);
        Box("BalconyTiles", h, new Vector3(0, y + 0.005f, d / 2f + depth / 2f - 0.01f), new Vector3(w + 0.26f, 0.02f, depth - 0.04f), Tiles, false, 1.5f);
        float x = w / 2f + 0.1f, z = d / 2f + depth - 0.06f;
        BarRailing(h, new Vector3(-x, y, z), new Vector3(x, y, z), rail);
        BarRailing(h, new Vector3(-x, y, d / 2f + 0.05f), new Vector3(-x, y, z), rail);
        BarRailing(h, new Vector3(x, y, d / 2f + 0.05f), new Vector3(x, y, z), rail);
    }

    /// <summary>A concrete balcony: a slab out from the floor, a painted bar railing or a low concrete wall round it.</summary>
    private static void ConcreteBalcony(Transform h, float u, float bw, float d, float y, Material rail, bool bars)
    {
        const float depth = 1.3f;
        Box("BalconySlab", h, new Vector3(u, y - 0.11f, d / 2f + depth / 2f), new Vector3(bw, 0.22f, depth), Concrete, true, 2.5f);
        float x0 = u - bw / 2f + 0.06f, x1 = u + bw / 2f - 0.06f, z = d / 2f + depth - 0.06f;
        if (bars)
        {
            BarRailing(h, new Vector3(x0, y, z), new Vector3(x1, y, z), rail, 1.0f, false);
            BarRailing(h, new Vector3(x0, y, d / 2f + 0.05f), new Vector3(x0, y, z), rail, 1.0f, false);
            BarRailing(h, new Vector3(x1, y, d / 2f + 0.05f), new Vector3(x1, y, z), rail, 1.0f, false);
        }
        else
        {
            Box("BalconyWall", h, new Vector3(u, y + 0.47f, z), new Vector3(bw, 0.95f, 0.12f), Concrete, true, 2.5f);
            Box("BalconyWall", h, new Vector3(x0, y + 0.47f, d / 2f + depth / 2f), new Vector3(0.12f, 0.95f, depth - 0.1f), Concrete, true, 2.5f);
            Box("BalconyWall", h, new Vector3(x1, y + 0.47f, d / 2f + depth / 2f), new Vector3(0.12f, 0.95f, depth - 0.1f), Concrete, true, 2.5f);
            Beam("BalconyRail", h, h.TransformPoint(new Vector3(x0, y + 1.0f, z)), h.TransformPoint(new Vector3(x1, y + 1.0f, z)), 0.05f, rail);
        }
    }

    /// <summary>The shop's sign over the windows (its lettering a row of pale blocks), wall lamps, an awning on some.</summary>
    private static void ShopFront(Transform h, float w, float d, float groundH, bool balcony)
    {
        float y = groundH - 0.48f;
        var sign = SignOf(rng.Next(signColours.Length));
        Box("Sign", h, new Vector3(0, y, d / 2f + 0.07f), new Vector3(w - 0.9f, 0.5f, 0.08f), sign, false, 1f);
        Box("SignEdge", h, new Vector3(0, y + 0.27f, d / 2f + 0.08f), new Vector3(w - 0.8f, 0.05f, 0.11f), Aluminium, false);
        Box("SignEdge", h, new Vector3(0, y - 0.27f, d / 2f + 0.08f), new Vector3(w - 0.8f, 0.05f, 0.11f), Aluminium, false);
        var letters = SignOf(sign == SignOf(4) || sign == SignOf(3) ? 0 : 4);
        float x = -R(1.2f, (w - 2f) / 2f);
        int count = rng.Next(4, 9);
        for (int i = 0; i < count && x < (w - 1.4f) / 2f; i++)
        {
            float lw = R(0.14f, 0.32f);
            Box("Letter", h, new Vector3(x + lw / 2f, y, d / 2f + 0.115f), new Vector3(lw, 0.26f, 0.01f), letters, false, 1f);
            x += lw + R(0.06f, 0.1f);
        }
        foreach (float sx in new[] { -1f, 1f })
        {
            var lamp = new Vector3(sx * (w / 2f - 0.35f), groundH - 1.15f, d / 2f + 0.3f);
            Box("LampArm", h, lamp + new Vector3(0, 0.22f, -0.15f), new Vector3(0.04f, 0.04f, 0.3f), Iron, false);
            NightLights.Lamp(h, h.TransformPoint(lamp), new Vector3(0.18f, 0.28f, 0.18f), night, 1.6f);
        }
        if (!balcony && Chance(0.6))
        {
            var cloth = AssetSetup.Solid("K_Awning" + rng.Next(3), new[] { new Color(0.62f, 0.14f, 0.1f), new Color(0.14f, 0.36f, 0.22f), new Color(0.75f, 0.6f, 0.3f) }[rng.Next(3)], 0.1f, 0f);
            Tilted("Awning", h, new Vector3(0, groundH - 0.2f, d / 2f + 0.65f), new Vector3(w - 0.6f, 0.03f, 1.3f), 16f, cloth, false, 1f);
        }
    }

    /// <summary>The wooden gallery along a compound wing's upper floor: posts, a board floor, a railing, a lean-to roof, a stair up.</summary>
    private static void Gallery(Transform h, float w, float d)
    {
        const float depth = 2.2f;
        float y = Storey;
        int bays = Mathf.Max(2, Mathf.RoundToInt(w / 2.6f));
        for (int i = 0; i <= bays; i++)
        {
            float x = -w / 2f + 0.15f + (w - 0.3f) * i / bays;
            Box("GalleryPost", h, new Vector3(x, (2f * Storey - 0.25f) / 2f, d / 2f + depth - 0.1f), new Vector3(0.18f, 2f * Storey - 0.25f, 0.18f), Wood, true, 1.5f);
        }
        Box("GalleryFloor", h, new Vector3(0, y - 0.08f, d / 2f + depth / 2f), new Vector3(w, 0.16f, depth), Wood, true, 1.5f);
        Box("GalleryBeam", h, new Vector3(0, y - 0.3f, d / 2f + depth - 0.1f), new Vector3(w, 0.3f, 0.2f), Wood, false, 1.5f);
        // the stair from the courtyard, along the outer edge, arriving at a gap in the railing
        float run = Mathf.CeilToInt(y / 0.18f) * 0.3f, sx = -w / 4f;
        var foot = h.TransformPoint(new Vector3(sx - run / 2f, 0f, d / 2f + depth + 0.6f));
        StepsDown(h, foot, h.TransformDirection(Vector3.right), y, 1.1f, h.TransformDirection(Vector3.forward), 0.4f, Wood);
        float gap0 = sx + run / 2f - 1.3f, gap1 = sx + run / 2f + 0.2f;
        Railing(h, new Vector3(-w / 2f + 0.1f, y, d / 2f + depth - 0.1f), new Vector3(gap0, y, d / 2f + depth - 0.1f), Wood);
        Railing(h, new Vector3(gap1, y, d / 2f + depth - 0.1f), new Vector3(w / 2f - 0.1f, y, d / 2f + depth - 0.1f), Wood);
        Tilted("GalleryRoof", h, new Vector3(0, 2f * Storey + 0.25f, d / 2f + depth / 2f + 0.1f), new Vector3(w + 0.4f, 0.12f, depth + 0.7f), 14f, Roof, true, 2.2f);
    }

    /// <summary>A porch on concrete pillars before a flat house's door, its roof a terrace with a railing when there is a floor above.</summary>
    private static void Porch(Transform h, House hs, float w, float d, float groundH, Material wall)
    {
        const float depth = 2.4f;
        float pw = Mathf.Min(w - 0.4f, R(3.5f, 6f));
        float y = groundH;
        Box("PorchSlab", h, new Vector3(0, y - 0.12f, d / 2f + depth / 2f), new Vector3(pw, 0.24f, depth), Concrete, true, 2.5f);
        foreach (float sx in new[] { -1f, 1f })
            Box("PorchPillar", h, new Vector3(sx * (pw / 2f - 0.2f), (y - 0.24f) / 2f, d / 2f + depth - 0.2f), new Vector3(0.3f, y - 0.24f, 0.3f), wall, true, 2.5f);
        Box("PorchFloor", h, new Vector3(0, 0.05f, d / 2f + depth / 2f), new Vector3(pw, 0.1f, depth), Tiles, true, 1.5f);
        if (hs.Floors > 1)
        {
            var rail = RailOf(Chance(0.4) ? 5 : hs.Paint);
            BarRailing(h, new Vector3(-pw / 2f + 0.06f, y, d / 2f + depth - 0.06f), new Vector3(pw / 2f - 0.06f, y, d / 2f + depth - 0.06f), rail, 1.0f, false);
            BarRailing(h, new Vector3(-pw / 2f + 0.06f, y, d / 2f + 0.05f), new Vector3(-pw / 2f + 0.06f, y, d / 2f + depth - 0.06f), rail, 1.0f, false);
            BarRailing(h, new Vector3(pw / 2f - 0.06f, y, d / 2f + 0.05f), new Vector3(pw / 2f - 0.06f, y, d / 2f + depth - 0.06f), rail, 1.0f, false);
        }
    }

    /// <summary>
    /// The flat roof: a concrete slab, a parapet round it (open where the outside stair comes up), and
    /// what stands on it: the columns and rods of a storey yet to be built, a water tank, solar panels,
    /// a satellite dish, an aerial.
    /// </summary>
    private static void FlatRoof(Transform h, House hs, float w, float d, float top, Material wall)
    {
        Box("RoofSlab", h, new Vector3(0, top + 0.12f, 0), new Vector3(w + 0.16f, 0.24f, d + 0.16f), Concrete, true, 2.5f);
        float py = top + 0.24f, ph = Chance(0.3) ? 0.45f : 0.9f;
        float stairZ = d / 2f - 0.3f - Mathf.CeilToInt((py) / 0.18f) * 0.3f;
        foreach (int s in new[] { 0, 1, 2, 3 })
        {
            bool alongX = s < 2;
            float len = alongX ? w + 0.16f : d - 0.2f;
            var c = s == 0 ? new Vector3(0, 0, d / 2f - 0.01f) : s == 1 ? new Vector3(0, 0, -d / 2f + 0.01f) : s == 2 ? new Vector3(-w / 2f - 0.01f, 0, 0) : new Vector3(w / 2f - 0.01f, 0, 0);
            void Part(float a, float b)
            {
                if (b - a < 0.05f) return;
                var p = c + (alongX ? Vector3.right : Vector3.forward) * ((a + b) / 2f);
                Box("Parapet", h, p + Vector3.up * (py + ph / 2f), alongX ? new Vector3(b - a, ph, 0.18f) : new Vector3(0.18f, ph, b - a), wall, true, 2.5f);
                Box("ParapetCap", h, p + Vector3.up * (py + ph + 0.03f), alongX ? new Vector3(b - a + 0.04f, 0.06f, 0.26f) : new Vector3(0.26f, 0.06f, b - a + 0.04f), Coping, false, 2f);
            }
            if (s == 3 && hs.RoofStair) { Part(-len / 2f, stairZ - 0.7f); Part(stairZ + 0.7f, len / 2f); }
            else Part(-len / 2f, len / 2f);
        }
        if (hs.RoofStair)
        {
            float rise = py;
            int count = Mathf.CeilToInt(rise / 0.18f);
            var foot = h.TransformPoint(new Vector3(w / 2f + 0.62f, 0f, stairZ + count * 0.3f));
            StepsDown(h, foot, h.TransformDirection(Vector3.back), rise, 1.1f, h.TransformDirection(Vector3.right), 0.6f, Coping);
        }
        var spots = new List<Vector3>();
        Vector3 Spot(float margin)
        {
            for (int i = 0; i < 12; i++)
            {
                var p = new Vector3(R(-w / 2f + margin, w / 2f - margin), py, R(-d / 2f + margin, d / 2f - margin));
                if (spots.All(q => (q - p).magnitude > margin * 1.6f + 0.4f) && !(hs.RoofStair && p.x > w / 2f - 1.6f && Mathf.Abs(p.z - stairZ) < 1.4f)) { spots.Add(p); return p; }
            }
            return new Vector3(99f, 0, 0);
        }
        if (hs.Unfinished)
        {
            // the columns of the next storey with their rods sticking out: the house will grow
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                var p = new Vector3(sx * (w / 2f - 0.25f), py, sz * (d / 2f - 0.25f));
                float ch = R(0.4f, 1.3f);
                Box("Column", h, p + Vector3.up * (ch / 2f), new Vector3(0.32f, ch, 0.32f), Concrete, true, 2.5f);
                for (int k = 0; k < 4; k++)
                    Box("Rebar", h, p + new Vector3((k % 2 - 0.5f) * 0.18f, ch + 0.45f, (k / 2 - 0.5f) * 0.18f), new Vector3(0.022f, 0.9f, 0.022f), AssetSetup.RustyPost, false);
                spots.Add(p);
            }
        }
        if (Chance(0.6))
        {
            var p = Spot(1.1f);
            if (p.x < 50f)
            {
                var tankMat = Chance(0.5) ? WhitePlastic : BlackPlastic;
                Box("TankStand", h, p + Vector3.up * 0.2f, new Vector3(1.2f, 0.4f, 1.2f), Concrete, true, 2f);
                Cylinder("Tank", h, h.TransformPoint(p + Vector3.up * 0.4f), 0.55f, 1.15f, tankMat, true, 14);
            }
        }
        if (Chance(0.4))
        {
            var p = Spot(1.3f);
            if (p.x < 50f)
            {
                var panel = MeshGen.Object("SolarPanel", MeshGen.Box(new Vector3(1.0f, 0.05f, 1.7f), 1f), Solar, h, h.TransformPoint(p + Vector3.up * 0.75f));
                panel.transform.rotation = Quaternion.Euler(-32f, 180f, 0f);
                Box("SolarLeg", h, p + new Vector3(0, 0.35f, 0.5f), new Vector3(0.9f, 0.7f, 0.05f), Aluminium, false);
            }
        }
        if (hs.Dish) { var p = Spot(0.6f); if (p.x < 50f) Dish(h, h.TransformPoint(p + Vector3.up * 0.1f), 160f); }
        if (Chance(0.5)) { var p = Spot(0.5f); if (p.x < 50f) Aerial(h, h.TransformPoint(p)); }
    }

    /// <summary>A lean-to roof: a wedge of the wall's material on the walls rising to the back, the roof sloping over it; returns its high edge.</summary>
    private static float LeanTo(Transform h, float w, float d, float top, Material wall, Material roof)
    {
        float rise = Mathf.Max(0.6f, d * 0.24f);
        var wedge = MeshGen.Object("Gables", MeshGen.Wedge(w, d, rise, 2.5f), wall, h, h.TransformPoint(new Vector3(0, top, d / 2f)), h.eulerAngles.y + 180f);
        wedge.AddComponent<MeshCollider>().sharedMesh = wedge.GetComponent<MeshFilter>().sharedMesh;
        float pitch = Mathf.Atan2(rise, d) * Mathf.Rad2Deg;
        float slope = Mathf.Sqrt(d * d + rise * rise);
        Tilted("Roof", h, new Vector3(0, top + rise / 2f + 0.08f, 0), new Vector3(w + 0.5f, 0.1f, slope + 0.7f), pitch, roof, true, 2.2f);
        return top + rise;
    }

    /// <summary>A satellite dish on its arm (a squashed half ball), facing 'yaw'.</summary>
    private static void Dish(Transform parent, Vector3 p, float yaw)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        Beam("DishArm", parent, p, p + Vector3.up * 0.55f, 0.04f, Aluminium);
        var dish = Sphere("Dish", parent, p + Vector3.up * 0.6f + rot * Vector3.forward * 0.12f, 0.36f, WhitePlastic);
        dish.transform.localScale = new Vector3(0.72f, 0.72f, 0.2f);
        dish.transform.rotation = rot * Quaternion.Euler(-25f, 0, 0);
        Beam("DishFeed", parent, p + Vector3.up * 0.6f + rot * Vector3.forward * 0.15f, p + Vector3.up * 0.55f + rot * Vector3.forward * 0.55f, 0.02f, Iron);
    }

    /// <summary>A television aerial: a mast with its rods across.</summary>
    private static void Aerial(Transform parent, Vector3 p)
    {
        Beam("AerialMast", parent, p, p + Vector3.up * 2.6f, 0.035f, Aluminium);
        float yaw = R(0f, 180f);
        for (int i = 0; i < 4; i++)
        {
            float half = 0.55f - i * 0.1f;
            var c = p + Vector3.up * (1.9f + i * 0.2f);
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.right * half;
            Beam("AerialRod", parent, c - dir, c + dir, 0.015f, Aluminium);
        }
        var boom = Quaternion.Euler(0, yaw + 90f, 0) * Vector3.forward * 0.5f;
        Beam("AerialBoom", parent, p + Vector3.up * 2.2f - boom, p + Vector3.up * 2.2f + boom, 0.02f, Aluminium);
    }

    /// <summary>An air conditioner's outside unit on a wall (house space, the wall's outward direction n).</summary>
    private static void Aircon(Transform h, Vector3 local, Vector3 n)
    {
        var u = Vector3.Cross(Vector3.up, n);
        Box("Aircon", h, local + n * 0.15f, SideSize(u, 0.8f, 0.55f, 0.28f), WhitePlastic, false, 1f);
        Box("AirconGrille", h, local + n * 0.3f + u * 0.12f, SideSize(u, 0.42f, 0.42f, 0.01f), BlackPlastic, false, 1f);
        Box("AirconPipe", h, local + n * 0.05f - u * 0.3f - Vector3.up * 0.6f, SideSize(u, 0.04f, 0.8f, 0.04f), WhitePlastic, false, 1f);
    }

    /// <summary>A ruined house: walls broken down to ragged tops, no roof, rubble and a fallen beam.</summary>
    private static void Ruin(Transform h, House hs, Material m)
    {
        float w = hs.W, d = hs.D, full = hs.Floors * Storey;
        for (int s = 0; s < 4; s++)
        {
            var (o, u, _, len) = Side(s, w, d);
            float top = R(1.2f, full);
            for (float a = -len / 2f; a < len / 2f - 0.01f; a += 1f)
            {
                float b = Mathf.Min(len / 2f, a + 1f);
                top = Mathf.Clamp(Mathf.Lerp(top, R(0.6f, full), 0.55f), 0.5f, full);
                bool gap = top > 2.6f && Chance(0.2);
                if (gap)
                {
                    Box("RuinWall", h, o + u * ((a + b) / 2f) + Vector3.up * 0.45f, SideSize(u, b - a, 0.9f, Thick), m, true, 2.5f);
                    Box("RuinWall", h, o + u * ((a + b) / 2f) + Vector3.up * ((2.2f + top) / 2f), SideSize(u, b - a, top - 2.2f, Thick), m, true, 2.5f);
                }
                else Box("RuinWall", h, o + u * ((a + b) / 2f) + Vector3.up * (top / 2f), SideSize(u, b - a, top, Thick), m, true, 2.5f);
            }
        }
        for (int i = 0; i < 9; i++)
        {
            var p = new Vector3(R(-w / 2f + 0.8f, w / 2f - 0.8f), 0, R(-d / 2f + 0.8f, d / 2f - 0.8f));
            var go = Box("Rubble", h, p + Vector3.up * 0.2f, new Vector3(R(0.4f, 1.2f), R(0.3f, 0.7f), R(0.4f, 1.0f)), Stone, true, 2.6f);
            if (go != null) go.transform.rotation = Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f));
        }
        Beam("FallenBeam", h, h.TransformPoint(new Vector3(-w / 2f + 0.6f, 0.1f, 0)), h.TransformPoint(new Vector3(w / 2f - 1f, 2.4f, d * 0.2f)), 0.22f, Wood, true);
    }

    /// <summary>
    /// A shed or barn: stone or board walls on three sides under a lean-to roof of rusty iron, open in
    /// front between posts or behind a wide door left half open; hay, firewood or barrels inside.
    /// </summary>
    private static void Shed(Transform h, House hs, float drop)
    {
        float w = hs.W, d = hs.D, wallH = 2.6f;
        var wall = hs.Style == 2 ? Stone : Chance(0.5) ? GreyWood : OldPlaster;
        Box("Foundation", h, new Vector3(0, -drop / 2f, 0), new Vector3(w, drop, d), Stone, true, 2.6f);
        Box("ShedFloor", h, new Vector3(0, 0.03f, 0), new Vector3(w - 0.5f, 0.06f, d - 0.5f), Coping, true, 2f);
        const float t = 0.3f;
        Box("ShedWall", h, new Vector3(0, wallH / 2f, -d / 2f + t / 2f), new Vector3(w, wallH, t), wall, true, 2.5f);
        foreach (float sx in new[] { -1f, 1f })
            Box("ShedWall", h, new Vector3(sx * (w / 2f - t / 2f), wallH / 2f, t / 2f), new Vector3(t, wallH, d - t), wall, true, 2.5f);
        bool door = Chance(0.45);
        if (door)
        {
            // a front wall with a wide doorway, one wooden leaf swung out
            float gw = Mathf.Min(2.8f, w - 1.4f);
            foreach (float sx in new[] { -1f, 1f })
            {
                float pw = (w - gw) / 2f;
                Box("ShedWall", h, new Vector3(sx * (w / 2f - pw / 2f), wallH / 2f, d / 2f - t / 2f), new Vector3(pw, wallH, t), wall, true, 2.5f);
            }
            Box("ShedLintel", h, new Vector3(0, wallH - 0.2f, d / 2f - t / 2f), new Vector3(gw, 0.4f, t), Wood, true, 1.5f);
            var leaf = Box("ShedDoor", h, new Vector3(-gw / 2f - 0.3f, 1.1f, d / 2f + gw / 4f), new Vector3(0.06f, 2.1f, gw / 2f), Chance(0.5) ? GreyWood : DoorWood, true, 1.4f);
        }
        else
            foreach (float sx in new[] { -1f, 1f })
                Box("ShedPost", h, new Vector3(sx * (w / 2f - 0.15f), wallH / 2f, d / 2f - 0.15f), new Vector3(0.24f, wallH, 0.24f), Wood, true, 1.5f);
        if (!door) Box("ShedBeam", h, new Vector3(0, wallH - 0.1f, d / 2f - 0.15f), new Vector3(w, 0.2f, 0.24f), Wood, false, 1.5f);
        LeanTo(h, w, d, wallH, wall, Corrugated);
        // inside: hay, firewood or barrels
        double r = rng.NextDouble();
        if (r < 0.35) HayBale(h.TransformPoint(new Vector3(R(-w / 4f, w / 4f), 0f, -d / 2f + 1.1f)), hs.Yaw + 90f);
        else if (r < 0.7) Firewood(h, new Vector3(0, 0, -d / 2f + t + 0.35f), Mathf.Min(w - 1f, 3f));
        else for (int i = 0; i < 3; i++) Barrel(h.TransformPoint(new Vector3(-w / 2f + 0.8f + i * 0.65f, 0f, -d / 2f + 0.7f)), i % 2 == 0);
    }

    /// <summary>A walled yard in front of a house: field stone walls following the ground, a wooden gate, a tree.</summary>
    private static void Yard(Transform h, House hs, float w, float d)
    {
        float depth = hs.YardDepth, half = w / 2f + 0.6f;
        var corners = new[] { new Vector3(-half, 0, d / 2f), new Vector3(-half, 0, d / 2f + depth), new Vector3(half, 0, d / 2f + depth), new Vector3(half, 0, d / 2f) };
        var wall = Chance(0.5) ? Stone : WallOf(hs.Colour);
        var gatePaint = PaintOf(hs.Paint);
        for (int s = 0; s < 3; s++)
        {
            var a = h.TransformPoint(corners[s]); var b = h.TransformPoint(corners[s + 1]);
            float len = Vector3.Distance(a, b);
            var dir = (b - a).normalized;
            float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
            for (float t = 0f; t < len - 0.05f; t += 2f)
            {
                float t1 = Mathf.Min(len, t + 2f);
                if (s == 1 && (t + t1) / 2f > len / 2f - 1f && (t + t1) / 2f < len / 2f + 1f) continue;
                var p = a + dir * ((t + t1) / 2f);
                float gy = Y(p.x, p.z);
                MeshGen.TexBox("YardWall", new Vector3(p.x, gy + 0.55f, p.z), new Vector3(0.45f, 1.7f, t1 - t + 0.45f), wall, 2.6f, root, yaw);
                MeshGen.TexBox("YardCoping", new Vector3(p.x, gy + 1.44f, p.z), new Vector3(0.55f, 0.08f, t1 - t + 0.5f), Coping, 2f, root, yaw, false);
            }
            if (s == 1)
            {
                var g = a + dir * (len / 2f);
                float gy = Y(g.x, g.z);
                MeshGen.TexBox("Gate", g - dir * 0.5f + Vector3.up * (gy + 0.7f), new Vector3(0.06f, 1.35f, 0.95f), gatePaint, 1.4f, root, yaw);
                MeshGen.TexBox("Gate", g + dir * 0.5f + Vector3.up * (gy + 0.7f), new Vector3(0.06f, 1.35f, 0.95f), gatePaint, 1.4f, root, yaw + 35f);
            }
        }
        var spot = h.TransformPoint(new Vector3(R(-w / 2f + 1f, w / 2f - 1f) * 0.6f, 0, d / 2f + depth * 0.6f));
        TreeAt(Chance(0.35) ? palms[rng.Next(palms.Length)] : Chance(0.5) ? olives[rng.Next(olives.Length)] : broadleaf, new Vector3(spot.x, Y(spot.x, spot.z), spot.z), R(0.7f, 1f));
    }

    /// <summary>A garden wall along the lane between two houses (from the plan), its gate, the garden's tree behind it.</summary>
    private static void GardenWall(Garden g)
    {
        float len = Vector2.Distance(g.A, g.B);
        var dir2 = (g.B - g.A) / len;
        var dir = new Vector3(dir2.x, 0, dir2.y);
        float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        var wall = Chance(0.5) ? Stone : Chance(0.6) ? Plaster : CreamPlaster;
        float gate0 = len / 2f - 0.75f, gate1 = len / 2f + 0.75f;
        for (float t = 0f; t < len - 0.05f; t += 1.5f)
        {
            float t1 = Mathf.Min(len, t + 1.5f);
            float a = t, b = t1;
            if (g.Gate && b > gate0 && a < gate1)
            {
                if (a < gate0) b = gate0; else if (b > gate1) a = gate1; else continue;
            }
            if (b - a < 0.1f) continue;
            var p2 = g.A + dir2 * ((a + b) / 2f);
            float gy = Y(p2.x, p2.y);
            MeshGen.TexBox("GardenWall", new Vector3(p2.x, gy + g.Height / 2f - 0.3f, p2.y), new Vector3(0.4f, g.Height + 0.6f, b - a + 0.02f), wall, 2.6f, root, yaw);
            MeshGen.TexBox("GardenCoping", new Vector3(p2.x, gy + g.Height + 0.04f, p2.y), new Vector3(0.5f, 0.08f, b - a + 0.06f), wall == Stone ? Coping : Roof, 2f, root, yaw, false);
        }
        if (g.Gate)
        {
            var gp = g.A + dir2 * (len / 2f);
            float gy = Y(gp.x, gp.y);
            var leaf = MeshGen.TexBox("GardenGate", new Vector3(gp.x, gy + 0.75f, gp.y) - dir * 0.4f, new Vector3(0.05f, 1.4f, 0.75f), Chance(0.5) ? Shutter : GreenPaint, 1.4f, root, yaw + 50f);
        }
        var tp = new Vector3(g.Tree.x, 0, g.Tree.y);
        tp.y = Y(tp.x, tp.z);
        Physics.SyncTransforms();
        if (Free(tp, 1.2f)) TreeAt(Chance(0.4) ? olives[rng.Next(olives.Length)] : Chance(0.5) ? broadleaf : palms[rng.Next(palms.Length)], tp, R(0.65f, 0.95f));
    }

    /// <summary>A compound's outer wall with its gate (wooden leaves open inwards, a little tiled roof over it), a well and a tree inside.</summary>
    private static void CompoundWalls(Compound c)
    {
        var t = new GameObject("Saray").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(new Vector3(c.X, 0, c.Z), Quaternion.Euler(0, c.Yaw, 0));
        var wall = Chance(0.5) ? Stone : Plaster;
        const float hgt = 2.8f, th = 0.5f;
        float W = c.W, D = c.D;
        void Run(Vector3 a, Vector3 b, float gapFrom, float gapTo)
        {
            float len = Vector3.Distance(a, b);
            var dir = (b - a) / len;
            for (float s = 0f; s < len - 0.05f; s += 2f)
            {
                float s0 = s, s1 = Mathf.Min(len, s + 2f);
                if (s1 > gapFrom && s0 < gapTo) { if (s0 < gapFrom) s1 = gapFrom; else if (s1 > gapTo) s0 = gapTo; else continue; }
                if (s1 - s0 < 0.1f) continue;
                var p = t.TransformPoint(a + dir * ((s0 + s1) / 2f));
                float gy = Y(p.x, p.z);
                float yaw = Quaternion.LookRotation(t.TransformDirection(dir)).eulerAngles.y;
                MeshGen.TexBox("SarayWall", new Vector3(p.x, gy + hgt / 2f - 0.4f, p.z), new Vector3(th, hgt + 0.8f, s1 - s0 + 0.02f), wall, 2.6f, root, yaw);
                MeshGen.TexBox("SarayCoping", new Vector3(p.x, gy + hgt + 0.05f, p.z), new Vector3(th + 0.12f, 0.1f, s1 - s0 + 0.06f), Roof, 2f, root, yaw, false);
            }
        }
        var c0 = new Vector3(-W / 2f + th / 2f, 0, -D / 2f + th / 2f); var c1 = new Vector3(W / 2f - th / 2f, 0, -D / 2f + th / 2f);
        var c2 = new Vector3(W / 2f - th / 2f, 0, D / 2f - th / 2f); var c3 = new Vector3(-W / 2f + th / 2f, 0, D / 2f - th / 2f);
        float gateW = 3.2f, gateAt = (W - th) / 2f + c.Gate;
        Run(c0, c1, -1f, -1f);
        Run(c1, c2, -1f, -1f);
        Run(c3, c2, gateAt - gateW / 2f, gateAt + gateW / 2f);
        Run(c0, c3, -1f, -1f);
        // the gate: stone piers, a beam across, a little tiled roof, two leaves swung in
        var g = new Vector3(c.Gate, 0, D / 2f - th / 2f);
        var gw = t.TransformPoint(g);
        float gy0 = Y(gw.x, gw.z) - t.position.y;
        foreach (float sx in new[] { -1f, 1f })
            Box("GatePier", t, g + new Vector3(sx * (gateW / 2f + 0.3f), gy0 + 1.6f, 0), new Vector3(0.6f, 3.6f, 0.75f), Masonry, true, 3f);
        Box("GateBeam", t, g + new Vector3(0, gy0 + 3.2f, 0), new Vector3(gateW + 0.2f, 0.35f, 0.6f), Wood, true, 1.5f);
        var gr = MeshGen.Object("GateRoof", HipRoof(gateW + 2f, 1.6f, 0.5f, 2.2f), Roof, t, t.TransformPoint(g + new Vector3(0, gy0 + 3.4f, 0)), c.Yaw);
        foreach (float sx in new[] { -1f, 1f })
        {
            var leaf = Box("GateLeaf", t, g + new Vector3(sx * (gateW / 2f - 0.15f), gy0 + 1.4f, -0.55f), new Vector3(0.08f, 2.7f, 1.4f), DoorWood, true, 1.4f);
            if (leaf != null) leaf.transform.rotation = t.rotation * Quaternion.Euler(0, sx * 18f, 0);
        }
        // the courtyard: a well, a tree, a bench by the wall
        var well = t.TransformPoint(new Vector3(c.Gate + R(2.5f, 4f) * (Chance(0.5) ? 1f : -1f), 0, D / 2f - 3.5f));
        well.y = Y(well.x, well.z);
        Cylinder("Well", root, well, 0.75f, 0.85f, Masonry, true, 14);
        Cylinder("WellWater", root, well + Vector3.up * 0.8f, 0.6f, 0.04f, Water, false, 12);
        Beam("WellPost", root, well + new Vector3(-0.7f, 0, 0), well + new Vector3(-0.7f, 2f, 0), 0.1f, Wood);
        Beam("WellPost", root, well + new Vector3(0.7f, 0, 0), well + new Vector3(0.7f, 2f, 0), 0.1f, Wood);
        Beam("WellBar", root, well + new Vector3(-0.75f, 1.9f, 0), well + new Vector3(0.75f, 1.9f, 0), 0.08f, Wood);
        var tree = t.TransformPoint(new Vector3(-c.Gate * 0.5f + (Chance(0.5) ? 4f : -4f), 0, D / 2f - 4.2f));
        tree.y = Y(tree.x, tree.z);
        Physics.SyncTransforms();
        if (Free(tree, 1f)) TreeAt(Chance(0.5) ? broadleaf : olives[rng.Next(olives.Length)], tree, R(0.9f, 1.2f));
    }

    // ------------------------------------------------------------------ the windmills and the cemetery

    private static Mesh millTower, millCap;

    private static Mesh Frustum(float r0, float r1, float height, int segments)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            float u = i / (float)segments * 2f * Mathf.PI * r0 / 2.5f;
            v.Add(dir * r0); uv.Add(new Vector2(u, 0));
            v.Add(dir * r1 + Vector3.up * height); uv.Add(new Vector2(u, height / 2.5f));
        }
        for (int i = 0; i < segments; i++) { int a = i * 2; t.Add(a); t.Add(a + 1); t.Add(a + 2); t.Add(a + 2); t.Add(a + 1); t.Add(a + 3); }
        int c = v.Count;
        v.Add(Vector3.up * height); uv.Add(Vector2.zero);
        for (int i = 0; i < segments; i++) { t.Add(c); t.Add(i * 2 + 3); t.Add(i * 2 + 1); }
        var m = new Mesh { name = $"HV_Frustum_{r0}_{r1}_{height}" };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, $"{AssetSetup.Generated}/HV_Frustum_{Mathf.RoundToInt(r0 * 100)}_{Mathf.RoundToInt(r1 * 100)}_{Mathf.RoundToInt(height * 100)}.asset");
        return m;
    }

    /// <summary>
    /// A windmill on the west hill (as on the Aegean islands): a round tapering tower of whitewashed or
    /// bare stone, a door and small windows, a conical thatched cap, and the wheel of eight wooden spars
    /// with their triangular sails turned to the wind.
    /// </summary>
    private static void Windmill(Vector3 p, float yaw, int index)
    {
        var t = new GameObject("Windmill").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        const float r0 = 3.0f, r1 = 2.55f, hgt = 7.6f;
        var wallMat = index % 2 == 0 ? Plaster : Stone;
        if (millTower == null) millTower = Frustum(r0, r1, hgt, 24);
        var towerMesh = millTower;
        var tower = MeshGen.Object("WindmillTower", towerMesh, wallMat, t, p - Vector3.up * 0.6f);
        tower.AddComponent<MeshCollider>().sharedMesh = towerMesh;
        Box("WindmillDoor", t, new Vector3(0, 0.95f, -r0 + 0.12f), new Vector3(1.0f, 2.0f, 0.3f), DoorWood, false, 1.4f);
        Box("WindmillLintel", t, new Vector3(0, 2.05f, -r0 + 0.1f), new Vector3(1.4f, 0.22f, 0.32f), Stone, false);
        foreach (var (a, y) in new[] { (60f, 3.8f), (-70f, 5.4f), (180f, 4.6f) })
        {
            var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
            float r = Mathf.Lerp(r0, r1, y / hgt);
            var wp = t.TransformPoint(dir * (r - 0.05f) + Vector3.up * y);
            var win = MeshGen.TexBox("WindmillWindow", wp, new Vector3(0.5f, 0.7f, 0.2f), Glass, 1f, t, yaw + a, false);
        }
        Cylinder("WindmillRing", t, t.TransformPoint(Vector3.up * (hgt - 0.6f)), r1 + 0.12f, 0.25f, Wood, false, 20);
        if (millCap == null) millCap = Cone(r1 + 0.45f, 2.8f, 20);
        var capMesh = millCap;
        MeshGen.Object("WindmillCap", capMesh, Thatch, t, t.TransformPoint(Vector3.up * (hgt - 0.38f)));
        Beam("WindmillTail", t, t.TransformPoint(new Vector3(0, hgt + 0.3f, -r1 + 0.2f)), t.TransformPoint(new Vector3(0, 0.6f, -r0 - 3.2f)), 0.16f, Wood);
        // the wheel at the front of the cap
        var hub = t.TransformPoint(new Vector3(0, hgt + 0.25f, r1 + 0.9f));
        Beam("WindmillAxle", t, t.TransformPoint(new Vector3(0, hgt + 0.4f, 0)), hub, 0.28f, Wood);
        Sphere("WindmillHub", t, hub, 0.32f, Wood);
        float spin = index * 13f;
        for (int k = 0; k < 8; k++)
        {
            float a = spin + k * 45f;
            var dir = t.TransformDirection(Quaternion.Euler(0, 0, a) * Vector3.up);
            var tip = hub + dir * 6.4f;
            Beam("WindmillSpar", t, hub, tip, 0.11f, Wood);
            // the sail: a triangle of cloth from the hub to the spar's tip, wrapped round on some
            if (k % 2 == 0 || index == 1)
            {
                var side = t.TransformDirection(Quaternion.Euler(0, 0, a - 90f) * Vector3.up);
                var sail = MeshGen.Object("WindmillSail", MeshGen.Gable(1.6f, 5.4f, 2f), Sail, t, tip - dir * 0.2f + side * 0.75f);
                sail.transform.rotation = Quaternion.LookRotation(t.forward, -dir);
                sail.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            }
            // a rope from the tip to the front of the axle, as on the island mills
            Beam("WindmillRope", t, tip, hub + t.forward * 1.6f, 0.02f, Wood);
        }
    }

    /// <summary>
    /// The village cemetery: a stone wall round it, an iron gate, rows of graves (a low border of stone,
    /// a headstone, some tall Ottoman stones with a turban or a pointed top) and dark cypresses.
    /// </summary>
    private static void Cemetery(Vector3 c)
    {
        var t = new GameObject("Cemetery").transform;
        t.SetParent(root, false);
        t.position = c;
        float W = CemeteryW, D = CemeteryD;
        void Wall(Vector3 a, Vector3 b, float gapFrom, float gapTo)
        {
            float len = Vector3.Distance(a, b);
            var dir = (b - a) / len;
            float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
            for (float s = 0f; s < len - 0.05f; s += 2f)
            {
                float s0 = s, s1 = Mathf.Min(len, s + 2f);
                if (s1 > gapFrom && s0 < gapTo) { if (s0 < gapFrom) s1 = gapFrom; else if (s1 > gapTo) s0 = gapTo; else continue; }
                if (s1 - s0 < 0.1f) continue;
                var p = a + dir * ((s0 + s1) / 2f);
                float gy = Y(p.x, p.z);
                MeshGen.TexBox("CemeteryWall", new Vector3(p.x, gy + 0.45f, p.z), new Vector3(0.5f, 1.5f, s1 - s0 + 0.02f), Stone, 2.6f, t, yaw);
                MeshGen.TexBox("CemeteryCoping", new Vector3(p.x, gy + 1.24f, p.z), new Vector3(0.6f, 0.08f, s1 - s0 + 0.06f), Coping, 2f, t, yaw, false);
            }
        }
        var a0 = c + new Vector3(-W / 2f, 0, -D / 2f); var a1 = c + new Vector3(W / 2f, 0, -D / 2f);
        var a2 = c + new Vector3(W / 2f, 0, D / 2f); var a3 = c + new Vector3(-W / 2f, 0, D / 2f);
        Wall(a0, a1, -1f, -1f); Wall(a1, a2, -1f, -1f); Wall(a0, a3, -1f, -1f);
        Wall(a3, a2, W / 2f - 1.3f, W / 2f + 1.3f);
        foreach (float sx in new[] { -1f, 1f })
        {
            var gp = c + new Vector3(sx * 1.55f, 0, D / 2f);
            float gy = Y(gp.x, gp.z);
            MeshGen.TexBox("CemeteryPier", new Vector3(gp.x, gy + 0.9f, gp.z), new Vector3(0.6f, 1.8f, 0.6f), Masonry, 3f, t);
            var leaf = MeshGen.TexBox("CemeteryGate", new Vector3(gp.x - sx * 0.65f, gy + 0.75f, gp.z - 0.55f), new Vector3(1.25f, 1.3f, 0.04f), Iron, 1f, t, sx * 60f, false);
        }
        for (float z = -D / 2f + 1.8f; z < D / 2f - 2.2f; z += 2.4f)
            for (float x = -W / 2f + 1.4f; x < W / 2f - 1f; x += 1.55f)
            {
                if (Mathf.Abs(x) < 1.3f || Chance(0.18)) continue;
                var gp = c + new Vector3(x + R(-0.12f, 0.12f), 0, z);
                float gy = Y(gp.x, gp.z);
                float yaw = R(-4f, 4f);
                MeshGen.TexBox("Grave", new Vector3(gp.x, gy + 0.08f, gp.z), new Vector3(0.75f, 0.22f, 1.75f), Coping, 2f, t, yaw);
                Box("GraveEarth", t, new Vector3(gp.x - c.x, gy + 0.2f - c.y, gp.z - c.z), new Vector3(0.55f, 0.04f, 1.5f), RockMat, false);
                double r = rng.NextDouble();
                var head = new Vector3(gp.x, gy, gp.z) + Quaternion.Euler(0, yaw, 0) * Vector3.back * 0.8f;
                if (r < 0.55)
                    MeshGen.TexBox("Headstone", head + Vector3.up * 0.55f, new Vector3(0.5f, R(0.7f, 1.0f), 0.1f), Marble, 1f, t, yaw);
                else
                {
                    // an Ottoman stone: tall and narrow, a turban carved on top, or a pointed top
                    float sh = R(1.1f, 1.5f);
                    MeshGen.TexBox("Headstone", head + Vector3.up * (sh / 2f), new Vector3(0.3f, sh, 0.16f), Marble, 1f, t, yaw);
                    if (r < 0.8) { Cylinder("Turban", t, head + Vector3.up * sh, 0.16f, 0.2f, Marble, false, 10); Sphere("TurbanTop", t, head + Vector3.up * (sh + 0.22f), 0.15f, Marble); }
                    else { var tip = MeshGen.TexBox("StoneTip", head + Vector3.up * (sh + 0.04f), new Vector3(0.21f, 0.21f, 0.16f), Marble, 1f, t, yaw); tip.transform.rotation = Quaternion.Euler(0, yaw, 45f); }
                }
                if (Chance(0.25)) MeshGen.TexBox("Footstone", head + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 1.6f + Vector3.up * 0.3f, new Vector3(0.36f, 0.45f, 0.08f), Marble, 1f, t, yaw);
            }
        foreach (var (x, z) in new[] { (-W / 2f + 1f, D / 2f - 1f), (W / 2f - 1f, D / 2f - 1f), (-W / 2f + 1f, -D / 2f + 1f), (W / 2f - 1f, -D / 2f + 1f), (0f, -D / 2f + 1f), (-W / 4f, D / 2f - 1f) })
        {
            var p = c + new Vector3(x, 0, z);
            p.y = Y(p.x, p.z);
            ZahraBuilder.CypressFor(t, p, R(8f, 11f), rng);
        }
    }

    // ------------------------------------------------------------------ the props of the streets

    private static void HayBale(Vector3 p, float yaw)
    {
        var hayMat = Mats.Pbr("K_Hay", "Ground104", new Color(1f, 0.86f, 0.52f), 0.05f);
        var bale = MeshGen.Object("HayBale", MeshGen.Cylinder(0.7f, 1.3f, 1.5f, 16), hayMat, root, p + Vector3.up * 0.68f);
        bale.transform.rotation = Quaternion.Euler(0, yaw, 90f);
        var c = bale.AddComponent<CapsuleCollider>();
        c.radius = 0.7f; c.height = 1.4f; c.center = new Vector3(0, 0.65f, 0);
    }

    /// <summary>A stack of firewood against a wall (house space, its back against the wall at 'local').</summary>
    private static void Firewood(Transform h, Vector3 local, float len)
    {
        Box("Firewood", h, local + Vector3.up * 0.55f, new Vector3(len, 1.1f, 0.6f), Bark, true, 0.8f);
        for (int i = 0; i < 6; i++)
        {
            var end = h.TransformPoint(local + new Vector3(R(-len / 2f + 0.2f, len / 2f - 0.2f), R(0.15f, 0.95f), 0.31f));
            var log = MeshGen.Object("LogEnd", MeshGen.Cylinder(R(0.07f, 0.11f), 0.02f, 0.3f, 8), Wood, h, end);
            log.transform.rotation = h.rotation * Quaternion.Euler(90f, 0, 0);
            log.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Tilted("FirewoodCover", h, local + new Vector3(0, 1.2f, 0.1f), new Vector3(len + 0.3f, 0.04f, 0.9f), 12f, Corrugated, false, 1.5f);
    }

    private static void Barrel(Vector3 p, bool rusty)
    {
        Cylinder("Barrel", root, p, 0.29f, 0.88f, rusty ? AssetSetup.RustyPost : BluePlastic, true, 12);
        Cylinder("BarrelRim", root, p + Vector3.up * 0.3f, 0.3f, 0.04f, rusty ? AssetSetup.RustyPost : BluePlastic, false, 12);
    }

    /// <summary>Rubbish bags piled at a corner by the street (no collider: kicked aside).</summary>
    private static void Rubbish(Vector3 p)
    {
        int n = rng.Next(3, 7);
        for (int i = 0; i < n; i++)
        {
            var q = p + new Vector3(R(-0.6f, 0.6f), 0, R(-0.6f, 0.6f));
            float s = R(0.32f, 0.48f);
            var bag = Sphere("RubbishBag", root, q + Vector3.up * (s * 0.35f + (i > 3 ? 0.3f : 0f)), s, Chance(0.6) ? BlackPlastic : Chance(0.5) ? BluePlastic : GreenPlastic);
            bag.transform.localScale = new Vector3(s * 2f * R(0.9f, 1.15f), s * 1.5f, s * 2f * R(0.85f, 1.1f));
            bag.transform.rotation = Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f));
            bag.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>A stack of wooden pallets (one collider for the stack).</summary>
    private static void Pallets(Vector3 p, float yaw)
    {
        var t = new GameObject("Pallets").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        int n = rng.Next(2, 7);
        for (int i = 0; i < n; i++)
        {
            float y = i * 0.15f;
            var tilt = new Vector3(R(-0.06f, 0.06f), 0, R(-0.06f, 0.06f));
            for (int k = -2; k <= 2; k++) Box("PalletSlat", t, tilt + new Vector3(k * 0.27f, y + 0.135f, 0), new Vector3(0.15f, 0.025f, 1.2f), GreyWood, false, 1f);
            for (int k = -1; k <= 1; k++) Box("PalletRunner", t, tilt + new Vector3(k * 0.52f, y + 0.06f, 0), new Vector3(0.1f, 0.12f, 1.2f), GreyWood, false, 1f);
        }
        t.gameObject.AddComponent<BoxCollider>().center = new Vector3(0, n * 0.075f, 0);
        t.GetComponent<BoxCollider>().size = new Vector3(1.2f, n * 0.15f, 1.2f);
    }

    /// <summary>A shipping container (red or blue), as at the edges of the village in the pictures.</summary>
    private static void ShippingContainer(Vector3 p, float yaw, bool red)
    {
        var paint = Mats.Pbr(red ? "K_ContainerRed" : "K_ContainerBlue", "RustyMetal02", red ? new Color(0.78f, 0.24f, 0.17f) : new Color(0.24f, 0.36f, 0.62f), 0.35f, 0.4f);
        var go = MeshGen.Object("Container", MeshGen.Container(), paint, root, p, yaw);
        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { paint, Iron };
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(2.44f, 2.59f, 6.06f); col.center = new Vector3(0, 1.295f, 0);
    }

    /// <summary>A wooden electricity pole: its cross arm with three insulators; returns where the wires hang from.</summary>
    private static Vector3[] Pole(Vector3 p, float yaw)
    {
        var t = new GameObject("Pole").transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        Cylinder("PoleWood", t, p - Vector3.up * 0.3f, 0.13f, 8.3f, GreyWood, true, 8);
        Box("CrossArm", t, new Vector3(0, 7.45f, 0), new Vector3(1.7f, 0.12f, 0.12f), GreyWood, false, 1.5f);
        Beam("ArmBrace", t, t.TransformPoint(new Vector3(-0.55f, 7.4f, 0)), t.TransformPoint(new Vector3(0, 6.9f, 0)), 0.05f, Iron);
        Beam("ArmBrace", t, t.TransformPoint(new Vector3(0.55f, 7.4f, 0)), t.TransformPoint(new Vector3(0, 6.9f, 0)), 0.05f, Iron);
        var ends = new Vector3[3];
        for (int i = 0; i < 3; i++)
        {
            var at = new Vector3((i - 1) * 0.72f, 7.51f, 0);
            Cylinder("Insulator", t, t.TransformPoint(at), 0.05f, 0.14f, WhitePlastic, false, 6);
            ends[i] = t.TransformPoint(at + Vector3.up * 0.13f);
        }
        if (Chance(0.2)) Cylinder("Transformer", t, t.TransformPoint(new Vector3(0, 5.6f, 0.32f)), 0.28f, 0.8f, GreyPaint, false, 10);
        return ends;
    }

    /// <summary>A sagging wire between two points (no collider, no shadow).</summary>
    private static void Wire(Vector3 a, Vector3 b, float sag)
    {
        const int parts = 5;
        var prev = a;
        for (int i = 1; i <= parts; i++)
        {
            float f = i / (float)parts;
            var q = Vector3.Lerp(a, b, f) + Vector3.down * (sag * 4f * f * (1f - f));
            var wire = Beam("Wire", root, prev, q, 0.022f, Iron);
            wire.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            prev = q;
        }
    }

    /// <summary>A kerbed pavement along a lane's edge on the side given, broken where other lanes join it.</summary>
    private static void Pavement(Lane lane, int side)
    {
        for (float s = 0f; s < lane.Length - 0.5f; s += 2f)
        {
            float s1 = Mathf.Min(lane.Length, s + 2f);
            var (pa, ta) = lane.At(s); var (pb, tb) = lane.At(s1);
            Vector2 Edge(Vector2 p, Vector2 t) => p + (side == 1 ? new Vector2(-t.y, t.x) : new Vector2(t.y, -t.x)) * (lane.Width / 2f - 0.6f);
            var a = Edge(pa, ta); var b = Edge(pb, tb);
            var mid = (a + b) / 2f;
            bool joined = lanes.Any(o => o != lane && EdgeDistance(o, mid) < 0.8f);
            if (joined || KeptOut(mid) || houses.Any(h => h.Distance(mid) < 0.62f)) continue;
            var A = new Vector3(a.x, Y(a.x, a.y), a.y); var B = new Vector3(b.x, Y(b.x, b.y), b.y);
            float len = Vector3.Distance(A, B);
            var go = MeshGen.Object("Pavement", MeshGen.Box(new Vector3(1.2f, 0.3f, len + 0.04f), 3f), Paving, root, (A + B) / 2f + Vector3.up * 0.02f);
            go.transform.rotation = Quaternion.LookRotation(B - A);
            go.AddComponent<BoxCollider>().size = new Vector3(1.2f, 0.3f, len + 0.04f);
            var kerb = MeshGen.Object("Kerb", MeshGen.Box(new Vector3(0.16f, 0.32f, len + 0.04f), 2f), Coping, root, (A + B) / 2f + Vector3.up * 0.03f + go.transform.rotation * Vector3.right * (side == 1 ? 0.6f : -0.6f));
            kerb.transform.rotation = go.transform.rotation;
        }
    }
}
