using System;
using System.Collections.Generic;
using System.Linq;
using UA;
using UnityEngine;
using Body = Outfit.Body;
using Cut = Outfit.Cut;
using Drape = Outfit.Drape;
using Garment = Outfit.Garment;
using Region = Outfit.Region;
using Slot = UA.Wardrobe.Slot;

/// <summary>
/// Makes every piece of the wardrobe (Wardrobe.cs) for one character: modest, loose clothes in many
/// traditions (combat uniform, Afghan perahan tunban and chapan, Arab thobe and ghutra, a formal suit,
/// everyday clothes, abaya and hijab), head wear, and the military kit (helmets, plate carrier, chest rig,
/// belt, knee pads, boots, assault pack: "Military Character Kit 1.1" by britdawgmasterfunk, CC-BY-4.0).
/// </summary>
public static class Dresser
{
    public class Result
    {
        public readonly List<Agent.OutfitPart> Parts = new List<Agent.OutfitPart>();
        public readonly List<Renderer> Team = new List<Renderer>();
    }

    private static int M(params int[] options) => options.Aggregate(0, (m, o) => m | (1 << o));
    private static int AllBut(params int[] options) => ~M(options);

    private static Agent.OutfitPart Part(Result result, GameObject go, Slot slot, int mask)
    {
        if (go == null) return null;
        var part = new Agent.OutfitPart { Object = go, Slot = slot, Mask = mask };
        result.Parts.Add(part);
        go.SetActive(false);
        // shadows only from the big pieces (the body casts its own): cheaper on phones
        bool big = (go.name.StartsWith("Top_") && go.name.Split('_').Length == 2) || go.name.StartsWith("Vest_") || go.name.StartsWith("Bag_") || go.name.StartsWith("Head_Helmet");
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = big ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        return part;
    }

    private static void Tint(Agent.OutfitPart part, Slot slot, IEnumerable<Wardrobe.Swatch> colors)
    {
        if (part == null) return;
        part.Coloured = true;
        part.ColorSlot = slot;
        part.Colors = colors.Select(c => c.Color).ToArray();
    }

    private static void Swap(Agent.OutfitPart part, Slot slot, Material[] materials)
    {
        if (part == null) return;
        part.Coloured = true;
        part.ColorSlot = slot;
        part.Materials = materials;
    }

    // ------------------------------------------------------------------ materials

    private static Material cloth, wool, denim, stripes, check, knit, leather, rubber, black, white, tie, waistcoat, glove, hood, flag;
    private static Material[] camos;
    private static readonly Dictionary<string, Material> kitMaterials = new Dictionary<string, Material>();

    private static void Materials()
    {
        if (cloth != null) return;
        // wrinkles (horizontal creases, 30 cm tile) and a fine twill weave
        var clothNormal = Outfit.NormalTexture("Cloth_Normal", 256, (u, v) =>
            0.7f * Outfit.Noise(u, v, 3, 10) + 0.25f * Outfit.Noise(u, v, 7, 18) + 0.035f * Mathf.Sin((u + v) * Mathf.PI * 2f * 48f), 2.2f);
        var clothAlbedo = Outfit.Texture("Cloth_Albedo", 128, (u, v) =>
        {
            float g = 0.86f + 0.07f * (Outfit.Noise(u, v, 6) - 0.5f) + 0.025f * Mathf.Sin((u + v) * Mathf.PI * 2f * 24f);
            return new Color(g, g, g);
        });
        var woolAlbedo = Outfit.Texture("Wool_Albedo", 128, (u, v) =>
        {
            float g = 0.84f + 0.1f * (Outfit.Noise(u, v, 32) - 0.5f) + 0.05f * Mathf.Sin((u - v) * Mathf.PI * 2f * 40f);
            return new Color(g, g, g);
        });
        var denimAlbedo = Outfit.Texture("Denim_Albedo", 128, (u, v) =>
        {
            float twill = 0.5f + 0.5f * Mathf.Sin((u * 2f + v) * Mathf.PI * 2f * 40f);
            float g = 0.72f + 0.16f * twill + 0.12f * (Outfit.Noise(u, v, 24, 6) - 0.5f);
            return new Color(g, g, g * 1.02f);
        });
        var stripesAlbedo = Outfit.Texture("Chapan_Albedo", 256, (u, v) =>
        {
            // a chapan's woven stripes: wide bands in three shades with thin light threads between
            float x = u * 10f;
            int band = Mathf.FloorToInt(x);
            float f = x - band;
            float[] shades = { 1f, 0.62f, 0.82f, 0.5f, 0.9f };
            float g = shades[band % shades.Length];
            if (f < 0.06f || (f > 0.48f && f < 0.52f)) g = 1.25f;
            g *= 0.8f + 0.06f * (Outfit.Noise(u, v, 40) - 0.5f) + 0.03f * Mathf.Sin(v * Mathf.PI * 2f * 64f);
            return new Color(Mathf.Min(1, g * 0.8f), Mathf.Min(1, g * 0.8f), Mathf.Min(1, g * 0.8f));
        });
        var checkAlbedo = Outfit.Texture("Shemagh_Albedo", 256, (u, v) =>
        {
            // red and white shemagh: houndstooth-like checks with red lines
            int ix = Mathf.FloorToInt(u * 24f), iy = Mathf.FloorToInt(v * 24f);
            bool red = ((ix % 4) < 2) ^ ((iy % 4) < 2);
            if ((ix + iy) % 4 == 0) red = !red;
            if (ix % 12 == 0 || iy % 12 == 0) red = true;
            float n = 0.95f + 0.05f * Outfit.Noise(u, v, 48);
            return red ? new Color(0.62f * n, 0.08f * n, 0.1f * n) : new Color(0.93f * n, 0.92f * n, 0.9f * n);
        });
        var knitNormal = Outfit.NormalTexture("Knit_Normal", 128, (u, v) =>
            Mathf.Abs(Mathf.Sin((u * 32f + Mathf.Abs(Mathf.Sin(v * Mathf.PI * 32f)) * 0.5f) * Mathf.PI)) * 0.6f + 0.3f * Outfit.Noise(u, v, 16), 1.6f);
        var leatherNormal = Outfit.NormalTexture("Leather_Normal", 128, (u, v) => Outfit.Noise(u, v, 24) * 0.6f + Outfit.Noise(u, v, 48) * 0.4f, 0.8f);

        cloth = AssetSetup.Mat("Cloth_Cotton", Color.white, 0.1f, 0f, clothAlbedo, clothNormal);
        wool = AssetSetup.Mat("Cloth_Wool", Color.white, 0.16f, 0f, woolAlbedo, clothNormal);
        denim = AssetSetup.Mat("Cloth_Denim", Color.white, 0.12f, 0f, denimAlbedo, clothNormal);
        stripes = AssetSetup.Mat("Cloth_Chapan", Color.white, 0.22f, 0f, stripesAlbedo, clothNormal);
        check = AssetSetup.Mat("Cloth_Shemagh", Color.white, 0.08f, 0f, checkAlbedo, clothNormal);
        knit = AssetSetup.Mat("Cloth_Knit", Color.white, 0.06f, 0f, woolAlbedo, knitNormal);
        leather = AssetSetup.Mat("Leather_Black", new Color(0.05f, 0.05f, 0.055f), 0.62f, 0f, null, leatherNormal);
        rubber = AssetSetup.Mat("Rubber_Sole", new Color(0.08f, 0.07f, 0.06f), 0.2f);
        black = AssetSetup.Mat("Cord_Black", new Color(0.04f, 0.04f, 0.045f), 0.35f);
        white = AssetSetup.Mat("Cloth_ShirtWhite", new Color(0.92f, 0.92f, 0.9f), 0.12f, 0f, clothAlbedo, clothNormal);
        tie = AssetSetup.Mat("Cloth_Tie", new Color(0.42f, 0.07f, 0.1f), 0.45f, 0f, woolAlbedo, clothNormal);
        waistcoat = AssetSetup.Mat("Cloth_Waistcoat", new Color(0.2f, 0.19f, 0.18f), 0.18f, 0f, woolAlbedo, clothNormal);
        glove = AssetSetup.Mat("Gloves_Tactical", new Color(0.1f, 0.1f, 0.1f), 0.28f, 0f, null, knitNormal);
        // black cotton of the hood and the scarf (TEAM 1)
        hood = AssetSetup.Mat("Cloth_BlackHood", new Color(0.075f, 0.075f, 0.08f), 0.06f, 0f, clothAlbedo, clothNormal);
        // the arm patch: green, white and black with three red stars, a dark stitched edge
        flag = AssetSetup.Mat("Patch_Flag", Color.white, 0.15f, 0f, Outfit.Texture("Patch_Flag", 128, FlagPixel));

        var suits = AssetSetup.Suits();
        Material Plain(string name, Color c) => AssetSetup.Mat("Suit_" + name, c, 0.12f, 0f, clothAlbedo, clothNormal);
        camos = new[]
        {
            suits[0], suits[1], suits[2], suits[3],
            Plain("Olive", new Color(0.33f, 0.35f, 0.24f)), Plain("Sand", new Color(0.66f, 0.58f, 0.43f)),
            Plain("Navy", new Color(0.14f, 0.17f, 0.27f)),
            AssetSetup.Mat("Suit_Hex", Color.white, 0.12f, 0f, Outfit.Texture("Camo_Hex", 256, HexCamoPixel)),
        };
        foreach (var m in camos)
        {
            m.SetTexture("_BumpMap", clothNormal);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Glossiness", 0.12f);
        }
    }

    /// <summary>
    /// Hexagon camo (TEAM 1): small six-sided cells in earth and olive tones, grouped in patches by a slow
    /// pattern, with darker seams between them; it repeats seamlessly over the tile.
    /// </summary>
    private static Color HexCamoPixel(float u, float v)
    {
        const int cols = 24, rows = 28;   // an even number of rows: the shifted rows meet across the seam
        Color[] tones =
        {
            new Color(0.29f, 0.27f, 0.18f), new Color(0.41f, 0.4f, 0.27f), new Color(0.55f, 0.5f, 0.36f),
            new Color(0.47f, 0.36f, 0.24f), new Color(0.66f, 0.6f, 0.45f),
        };
        float x = u * cols, y = v * rows;
        float best = float.MaxValue, second = float.MaxValue;
        int bc = 0, br = 0;
        int r0 = Mathf.FloorToInt(y), c0 = Mathf.FloorToInt(x);
        for (int r = r0 - 1; r <= r0 + 2; r++)
            for (int c = c0 - 1; c <= c0 + 2; c++)
            {
                float dx = (c + ((r & 1) == 1 ? 0.5f : 0f) - x) / cols, dy = (r - y) / rows;
                float d = dx * dx * 0.75f + dy * dy;   // the cells a little wider than tall, as on the cloth
                if (d < best) { second = best; best = d; bc = c; br = r; }
                else if (d < second) second = d;
            }
        bc = (bc % cols + cols) % cols;
        br = (br % rows + rows) % rows;
        float cu = (bc + ((br & 1) == 1 ? 0.5f : 0f)) / cols * Mathf.PI * 2f, cv = br / (float)rows * Mathf.PI * 2f;
        float n = Mathf.Sin(cu * 2f + Mathf.Sin(cv * 3f) * 1.3f) + 0.8f * Mathf.Sin(cv * 2f + Mathf.Cos(cu * 3f) * 1.3f) + 0.45f * Mathf.Sin((cu + cv) * 4f + 1.3f);
        int hash = ((bc * 73856093) ^ (br * 19349663)) & 1023;
        n += (hash / 1023f - 0.5f) * 1.1f;
        int k = Mathf.Clamp(Mathf.FloorToInt((n + 2.2f) / 4.4f * tones.Length), 0, tones.Length - 1);
        var col = tones[k];
        // the seam between two cells, and a little weave
        float seam = Mathf.Sqrt(second) - Mathf.Sqrt(best);
        if (seam < 0.004f) col *= 0.8f;
        col *= 0.95f + 0.05f * Outfit.Noise(u, v, 64);
        col.a = 1f;
        return col;
    }

    /// <summary>The arm patch (3 : 2): green over white over black, three red stars on the white, a stitched edge.</summary>
    private static Color FlagPixel(float u, float v)
    {
        if (u < 0.04f || u > 0.96f || v < 0.06f || v > 0.94f) return new Color(0.16f, 0.16f, 0.13f);
        float fy = (v - 0.06f) / 0.88f;
        var c = fy > 2f / 3f ? new Color(0.0f, 0.45f, 0.24f) : fy > 1f / 3f ? new Color(0.95f, 0.95f, 0.93f) : new Color(0.04f, 0.04f, 0.04f);
        // stars on the white band, measured in the patch's own proportions (1.5 wide for 1 high)
        var p = new Vector2((u - 0.5f) * 1.5f, fy - 0.5f);
        for (int i = -1; i <= 1; i++)
            if (InStar(p - new Vector2(i * 0.3f, 0f), 0.13f)) c = new Color(0.82f, 0.08f, 0.1f);
        return c;
    }

    private static bool InStar(Vector2 p, float outer)
    {
        // a five-pointed star, a point up: inside when on the inner side of the two edges of its sector
        float a = Mathf.Atan2(p.x, p.y);
        float sector = Mathf.PI * 2f / 5f;
        float local = Mathf.Repeat(a + sector / 2f, sector) - sector / 2f;
        float r = p.magnitude;
        float inner = outer * 0.382f;
        // the edge from the point (angle 0, outer) to the notch (angle sector / 2, inner)
        var tip = new Vector2(0f, outer);
        var notch = new Vector2(Mathf.Sin(sector / 2f) * inner, Mathf.Cos(sector / 2f) * inner);
        var q = new Vector2(Mathf.Sin(Mathf.Abs(local)) * r, Mathf.Cos(local) * r);
        var edge = notch - tip;
        return edge.x * (q.y - tip.y) - edge.y * (q.x - tip.x) <= 0f || r < inner;
    }

    /// <summary>Materials of a kit model: its own colours, multicam where the kit uses camo.</summary>
    private static Material[] KitMaterials(Outfit.KitModel kit)
    {
        return kit.Materials.Select(m =>
        {
            string key = m.name;
            if (kitMaterials.TryGetValue(key, out var found) && found != null) return found;
            string lower = key.ToLowerInvariant();
            Material mat;
            if (lower.Contains("camo"))
                mat = AssetSetup.Mat("Kit_" + key, Color.white, 0.18f, 0f, AssetSetup.Camo("Camo_Multicam", new[] { new Color(0.55f, 0.5f, 0.38f), new Color(0.42f, 0.4f, 0.3f), new Color(0.62f, 0.57f, 0.45f), new Color(0.3f, 0.27f, 0.2f) }, 1.3f));
            else
            {
                // the kit's colours are very dark in linear space: lift them a little, keep the hue
                var c = m.color.linear.maxColorComponent < 0.02f ? m.color * 1.8f : m.color;
                mat = AssetSetup.Mat("Kit_" + key, new Color(Mathf.Clamp01(c.r * 1.25f), Mathf.Clamp01(c.g * 1.25f), Mathf.Clamp01(c.b * 1.25f)), Mathf.Clamp(m.smoothness, 0.08f, 0.5f));
            }
            return kitMaterials[key] = mat;
        }).ToArray();
    }

    // ------------------------------------------------------------------ dressing

    public static Result Dress(Body body, string key, bool female, GameObject hair, GameObject beard, SkinnedMeshRenderer[] extras, Material teamMaterial)
    {
        Materials();
        var result = new Result();
        var hip = body.At("pelvis");
        var knee = (body.At("calf_l") + body.At("calf_r")) / 2f;
        var ankle = (body.At("foot_l") + body.At("foot_r")) / 2f;
        var neck = body.At("neck_01", "neck");
        var chest = body.At("spine_03", "spine_02");
        var spine2 = body.At("spine_02", "spine_01");
        float ground = body.Points.Min(p => p.y);
        float shoulderY = (body.At("clavicle_l").y + body.At("clavicle_r").y) / 2f;
        float legs = Mathf.Max(0.2f, hip.y - knee.y);
        Debug.Log($"[Setup] {key} rest pose: hip {hip} knee {knee} ankle {ankle} neck {neck} ground {ground:0.000}");

        // points of the extra meshes (hair, eyes) at rest: skinned with the body's rest bones
        List<Vector3> RestPoints(SkinnedMeshRenderer r)
        {
            var list = new List<Vector3>();
            if (r == null || r.sharedMesh == null) return list;
            var mesh = r.sharedMesh;
            var bindposes = mesh.bindposes;
            var matrices = r.bones.Select((bone, i) =>
            {
                int b = bone != null ? body.Bone(bone.name) : -1;
                return b >= 0 && i < bindposes.Length ? body.Bind(b) * bindposes[i] : r.transform.localToWorldMatrix;
            }).ToArray();
            var weights = mesh.boneWeights;
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (weights.Length != vertices.Length || matrices.Length == 0) { list.Add(r.transform.localToWorldMatrix.MultiplyPoint3x4(vertices[i])); continue; }
                var w = weights[i];
                var p = matrices[w.boneIndex0].MultiplyPoint3x4(vertices[i]) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(vertices[i]) * w.weight1
                        + matrices[w.boneIndex2].MultiplyPoint3x4(vertices[i]) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(vertices[i]) * w.weight3;
                list.Add(p);
            }
            return list;
        }

        // ---- bottoms: trousers made loose around the legs, the shape of the hips smoothed away
        float slim = female ? 0.93f : 1f;
        var trousers = new Garment[4];
        Cut Legs(float t, int smooth, float thigh, float calf, float tile) => new Cut
        { Regions = new[] { Region.Legs }, Thickness = t, Smooth = smooth, Thigh = thigh * slim, Calf = calf * slim, Tile = tile };
        trousers[0] = Outfit.Shell(body, Legs(0.012f, 10, 0.09f, 0.072f, 0.5f));
        trousers[1] = Outfit.Shell(body, Legs(0.014f, 14, 0.118f, 0.094f, 0.3f));
        trousers[2] = Outfit.Shell(body, Legs(0.009f, 10, 0.085f, 0.067f, 0.3f));
        trousers[3] = Outfit.Shell(body, Legs(0.007f, 6, 0.079f, 0.063f, 0.3f));
        Swap(Part(result, Outfit.Wear(body, trousers[0], "Bottom_Cargo", camos[0]), Slot.Bottom, M(0)), Slot.BottomColor, camos);
        Tint(Part(result, Outfit.Wear(body, trousers[1], "Bottom_Tunban", cloth), Slot.Bottom, M(1)), Slot.BottomColor, Wardrobe.BottomColors[1]);
        Tint(Part(result, Outfit.Wear(body, trousers[2], "Bottom_Suit", wool), Slot.Bottom, M(2)), Slot.BottomColor, Wardrobe.BottomColors[2]);
        Tint(Part(result, Outfit.Wear(body, trousers[3], "Bottom_Jeans", denim), Slot.Bottom, M(3)), Slot.BottomColor, Wardrobe.BottomColors[3]);
        // cargo pockets on the thighs
        foreach (string side in new[] { "l", "r" })
        {
            var a = body.At("thigh_" + side);
            var b = body.At("calf_" + side);
            var axis = (b - a).normalized;
            var outward = Vector3.ProjectOnPlane(new Vector3(Mathf.Sign(a.x - hip.x), 0, 0.15f), axis).normalized;
            var center = a + (b - a) * 0.5f + outward * (0.09f * slim + 0.012f + 0.016f);
            var pocket = Box(center, Quaternion.LookRotation(outward, -axis), new Vector3(0.14f, 0.17f, 0.03f));
            Swap(Part(result, Outfit.Attach(body, pocket, "thigh_" + side, "Bottom_CargoPocket_" + side, camos[0]), Slot.Bottom, M(0)), Slot.BottomColor, camos);
        }

        // what a hanging garment must clear: the body below the arms and every pair of trousers
        var under = new List<Vector3>(body.PointsIn(Region.Torso, Region.Legs, Region.Feet));
        foreach (var worn in trousers.Where(g => g != null)) under.AddRange(worn.P);

        // ---- tops: sleeves to the wrist and a cloth that hangs from the waist over the hips and thighs
        var topShells = new Garment[8];
        Garment Top(int option, string name, Cut cut, Drape drape, Material material, IEnumerable<Wardrobe.Swatch> colors)
        {
            cut.Regions = new[] { Region.Torso, Region.Arms };
            var shell = Outfit.Shell(body, cut);
            if (shell == null) return null;
            topShells[option] = shell;
            float waist = shell.P.Where(p => Mathf.Abs(p.x - hip.x) < 0.25f).Min(p => p.y);
            drape.Top = Mathf.Max(waist + 0.07f, hip.y + 0.05f);
            var clear = new List<Vector3>(under);
            clear.AddRange(shell.P.Where(p => p.y < drape.Top + 0.05f && Mathf.Abs(p.x - hip.x) < 0.3f));
            var robe = Outfit.Robe(body, drape, clear, option * 31 + 7);
            shell.Append(robe);
            var go = Outfit.Wear(body, shell, "Top_" + name, material);
            var part = Part(result, go, Slot.Top, M(option));
            if (colors != null) Tint(part, Slot.TopColor, colors);
            else Swap(part, Slot.TopColor, camos);
            // team arm bands on both sleeves
            Garment band = null;
            foreach (string side in new[] { "l", "r" })
            {
                var a = body.At("upperarm_" + side);
                var b = body.At("lowerarm_" + side);
                var g = Outfit.Derive(topShells[option], p =>
                {
                    float t = Vector3.Dot(p - a, b - a) / (b - a).sqrMagnitude;
                    return t > 0.24f && t < 0.46f && Vector3.Cross(b - a, p - a).magnitude / (b - a).magnitude < 0.16f;
                }, 0.004f);
                if (g == null) continue;
                if (band == null) band = g; else band.Append(g);
            }
            var bands = Outfit.Wear(body, band, "Top_" + name + "_TeamBands", teamMaterial);
            if (bands != null)
            {
                Part(result, bands, Slot.Top, M(option));
                result.Team.Add(bands.GetComponent<Renderer>());
            }
            return shell;
        }

        float hemThigh(float f) => hip.y - f * legs;
        Top(0, "CombatJacket", new Cut { Thickness = 0.014f, Smooth = 12, UpperArm = 0.066f * slim, LowerArm = 0.055f * slim, Tile = 0.5f },
            new Drape { Hem = hemThigh(0.45f), FrontOpen = true, OpenFrom = 0.1f, Ease = 0.012f, Flare = 0.015f, Folds = 0.006f, Tile = 0.5f }, camos[0], null);
        Top(1, "Perahan", new Cut { Thickness = 0.016f, Smooth = 16, UpperArm = 0.076f * slim, LowerArm = 0.067f * slim },
            new Drape { Hem = knee.y - 0.03f, SideSlits = true, OpenFrom = 0.2f, Ease = 0.026f, Flare = 0.05f, Folds = 0.013f }, cloth, Wardrobe.TopColors[1]);
        Top(2, "Thobe", new Cut { Thickness = 0.014f, Smooth = 16, UpperArm = 0.071f * slim, LowerArm = 0.063f * slim },
            new Drape { Hem = ankle.y + 0.05f, Ease = 0.045f, Flare = 0.025f, Folds = 0.012f, Follow = 0.95f }, cloth, Wardrobe.TopColors[2]);
        Top(3, "Suit", new Cut { Thickness = 0.016f, Smooth = 12, UpperArm = 0.062f * slim, LowerArm = 0.052f * slim, Tile = 0.25f },
            new Drape { Hem = hemThigh(0.4f), FrontOpen = true, OpenFrom = 0.03f, Ease = 0.01f, Flare = 0.012f, Folds = 0.004f, Tile = 0.25f }, wool, Wardrobe.TopColors[3]);
        Top(4, "LongShirt", new Cut { Thickness = 0.012f, Smooth = 12, UpperArm = 0.062f * slim, LowerArm = 0.054f * slim },
            new Drape { Hem = hemThigh(0.58f), SideSlits = true, OpenFrom = 0.13f, Ease = 0.016f, Flare = 0.022f, Folds = 0.008f }, cloth, Wardrobe.TopColors[4]);
        Top(5, "Chapan", new Cut { Thickness = 0.022f, Smooth = 16, UpperArm = 0.084f * slim, LowerArm = 0.08f * slim, Tile = 0.35f },
            new Drape { Hem = knee.y - 0.16f, FrontOpen = true, OpenFrom = 0.02f, Ease = 0.036f, Flare = 0.07f, Folds = 0.016f, Tile = 0.35f }, stripes, Wardrobe.TopColors[5]);
        Top(6, "Abaya", new Cut { Thickness = 0.016f, Smooth = 16, UpperArm = 0.075f * slim, LowerArm = 0.071f * slim },
            new Drape { Hem = ankle.y + 0.03f, Ease = 0.05f, Flare = 0.045f, Folds = 0.015f, Follow = 0.95f }, cloth, Wardrobe.TopColors[6]);
        Top(7, "LongCoat", new Cut { Thickness = 0.016f, Smooth = 12, UpperArm = 0.066f * slim, LowerArm = 0.058f * slim, Tile = 0.25f },
            new Drape { Hem = knee.y - 0.06f, FrontOpen = true, SideSlits = true, OpenFrom = 0.14f, Ease = 0.02f, Flare = 0.045f, Folds = 0.01f, Tile = 0.25f }, wool, Wardrobe.TopColors[7]);

        // the TEAM 1 arm patches on the combat jacket's sleeves, just under the shoulders (with the hood)
        if (topShells[0] != null)
            foreach (string side in new[] { "l", "r" })
            {
                var a = body.At("upperarm_" + side);
                var b = body.At("lowerarm_" + side);
                var axis = (b - a).normalized;
                var outward = Vector3.ProjectOnPlane(new Vector3(Mathf.Sign(a.x - hip.x), 0, 0.12f), axis).normalized;
                var at = a + (b - a) * 0.16f;
                // on the sleeve: the farthest point of the jacket out from the arm there
                float reach = topShells[0].P.Where(q => Mathf.Abs(Vector3.Dot(q - at, axis)) < 0.03f && Vector3.Dot(q - at, outward) > 0f)
                    .Select(q => Vector3.Dot(q - at, outward)).DefaultIfEmpty(0.08f).Max();
                var patch = Box(at + outward * (reach + 0.002f), Quaternion.LookRotation(outward, -axis), new Vector3(0.075f, 0.05f, 0.004f));
                var patchPart = Part(result, Outfit.Attach(body, patch, "upperarm_" + side, "Top_FlagPatch_" + side, flag), Slot.Head, M(13));
                if (patchPart == null) continue;
                patchPart.HasSecond = true;
                patchPart.Slot2 = Slot.Top;
                patchPart.Mask2 = M(0);
            }

        // front details: a button placket on the shirts, collar, white shirt front and tie on the suit
        float FrontZ(Garment g, float y) => g.P.Where(p => Mathf.Abs(p.y - y) < 0.02f && Mathf.Abs(p.x - neck.x) < 0.03f).Select(p => p.z).DefaultIfEmpty(chest.z + 0.12f).Max();
        foreach (int option in new[] { 1, 2, 4 })
        {
            if (topShells[option] == null) continue;
            var placket = Outfit.Derive(topShells[option], p => Mathf.Abs(p.x - neck.x) < 0.017f && p.z > neck.z && p.y < neck.y - 0.03f && p.y > neck.y - (option == 2 ? 0.3f : 0.24f), 0.003f);
            var collar = Outfit.Derive(topShells[option], p => p.y > neck.y - 0.035f && Mathf.Abs(p.x - neck.x) < 0.12f, 0.004f);
            if (placket != null && collar != null) placket.Append(collar);
            Tint(Part(result, Outfit.Wear(body, placket ?? collar, "Top_Placket" + option, cloth), Slot.Top, M(option)), Slot.TopColor, Wardrobe.TopColors[option]);
        }
        if (topShells[3] != null)
        {
            // a V of white shirt between the lapels, the collar, and a tie down the middle
            float vBottom = spine2.y - 0.02f, vTop = neck.y - 0.02f;
            var shirt = Outfit.Derive(topShells[3], p => p.z > neck.z && p.y > vBottom && p.y < vTop + 0.05f
                && Mathf.Abs(p.x - neck.x) < 0.012f + 0.075f * Mathf.Clamp01((p.y - vBottom) / (vTop - vBottom)), 0.002f);
            var shirtCollar = Outfit.Derive(topShells[3], p => p.y > neck.y - 0.03f && Mathf.Abs(p.x - neck.x) < 0.12f, 0.005f);
            if (shirt != null && shirtCollar != null) shirt.Append(shirtCollar);
            Part(result, Outfit.Wear(body, shirt ?? shirtCollar, "Top_SuitShirt", white), Slot.Top, M(3));
            var path = new List<Vector3>();
            var width = new List<float>();
            var normals = new List<Vector3>();
            int steps = 12;
            for (int i = 0; i <= steps; i++)
            {
                float f = i / (float)steps;
                float y = Mathf.Lerp(neck.y - 0.035f, vBottom - 0.06f, f);
                path.Add(new Vector3(neck.x, y, FrontZ(topShells[3], y) + 0.006f));
                width.Add(i == 0 ? 0.03f : i == steps ? 0.005f : Mathf.Lerp(0.034f, 0.078f, f) * (i == steps - 1 ? 0.6f : 1f));
                normals.Add(Vector3.forward);
            }
            var tieStrip = Outfit.Strip(path, width, normals, 0.006f);
            for (int i = 0; i < tieStrip.P.Count; i++) tieStrip.W[i] = body.Nearest(tieStrip.P[i], Region.Torso);
            Part(result, Outfit.Wear(body, tieStrip, "Top_SuitTie", tie), Slot.Top, M(3));
        }

        // ---- vests: kit plate carrier and chest rig over any top, or an Afghan waistcoat
        var shells = topShells.Where(t => t != null).ToList();
        var band2 = shells.SelectMany(s => s.P).Where(p => p.y > spine2.y - 0.05f && p.y < shoulderY - 0.03f && Mathf.Abs(p.x - neck.x) < 0.3f).ToList();
        if (band2.Count == 0) band2 = body.PointsIn(Region.Torso).ToList();
        float torsoW = band2.Max(p => p.x) - band2.Min(p => p.x), torsoD = band2.Max(p => p.z) - band2.Min(p => p.z);
        var torsoC = new Vector3((band2.Max(p => p.x) + band2.Min(p => p.x)) / 2f, chest.y, (band2.Max(p => p.z) + band2.Min(p => p.z)) / 2f);
        var vestPieces = new List<Garment>();
        foreach (var (option, item) in new[] { (1, "PlateCarrier"), (2, "ChestRig") })
        {
            var kit = Outfit.LoadKit(item);
            if (kit == null) continue;
            var kb = kit.Bounds;
            var scale = new Vector3((torsoW + 0.03f) / kb.size.x, 0, (torsoD + 0.05f) / kb.size.z);
            scale.y = (scale.x + scale.z) / 2f;
            var placed = Outfit.Place(kit, new Vector3(kb.center.x, kb.max.y, kb.center.z), new Vector3(torsoC.x, shoulderY + 0.06f, torsoC.z), scale);
            vestPieces.Add(placed);
            Part(result, Outfit.Attach(body, placed, "spine_03", "Vest_" + item, KitMaterials(kit)), Slot.Vest, M(option));
        }
        float vBottomW = spine2.y - 0.04f;
        var vest = Outfit.Shell(body, new Cut
        {
            Regions = new[] { Region.Torso }, Thickness = 0.033f, Smooth = 16, AllCorners = true, Tile = 0.25f,
            // open V at the front and a narrow opening down to the hem
            Keep = (p, n) => !(p.z > neck.z && n.z > 0.1f && p.y > vBottomW && Mathf.Abs(p.x - neck.x) < 0.014f + 0.07f * Mathf.Clamp01((p.y - vBottomW) / (neck.y - vBottomW)))
                && !(p.z > neck.z && n.z > 0.3f && Mathf.Abs(p.x - neck.x) < 0.01f),
        });
        Part(result, Outfit.Wear(body, vest, "Vest_Waistcoat", waistcoat), Slot.Vest, M(3));

        // ---- head wear, fitted to the head and hair at rest
        var hb = Outfit.HeadBounds(body);
        var headPts = Outfit.HeadPoints(body);
        var hairPts = hair != null && !female ? RestPoints(hair.GetComponent<SkinnedMeshRenderer>()) : new List<Vector3>();
        headPts.AddRange(hairPts);
        float top = headPts.Max(p => p.y);
        var eyePts = extras.Where(r => r != null && r.sharedMaterials.Any(m => m != null && m.name.Contains("Eye"))).SelectMany(RestPoints).ToList();
        float eyeY = eyePts.Count > 0 ? eyePts.Average(p => p.y) : hb.center.y + 0.02f;
        float brow = eyeY + 0.03f, chin = hb.min.y + 0.012f;
        float hx = hb.center.x, hz = hb.center.z;
        Debug.Log($"[Setup] {key} head {hb.center} {hb.size} top {top:0.000} eyes {eyeY:0.000} ({eyePts.Count} pts)");
        float[] Sec(float y)
        {
            var s = Outfit.Support(headPts, Mathf.Min(y, top - 0.008f), 0.008f, hx, hz);
            return s ?? Enumerable.Repeat(hb.size.x * 0.5f, 32).ToArray();
        }
        float[] R(float[] s, float scale, float extra) => s.Select(r => r * scale + extra).ToArray();
        int headBone = body.Bone("head"), neckBone = body.Bone("neck_01"), chestBone = body.Bone("spine_03");
        var headWeight = Outfit.Weight((headBone, 1f));

        // 1-3: kit helmets
        var helmetKit = Outfit.LoadKit("Helmet");
        float helmetScale = 1f;
        if (helmetKit != null)
        {
            var dome = helmetKit.Mesh.P.Where(p => p.y > helmetKit.Bounds.max.y - helmetKit.Bounds.size.y * 0.45f).ToList();
            helmetScale = (hb.size.x + 0.075f) / (dome.Max(p => p.x) - dome.Min(p => p.x));
        }
        foreach (var (option, item) in new[] { (1, "Helmet"), (2, "HelmetHeadset"), (3, "HelmetNVG") })
        {
            var kit = item == "Helmet" ? helmetKit : Outfit.LoadKit(item);
            if (kit == null) continue;
            var dome = kit.Mesh.P.Where(p => p.y > kit.Bounds.max.y - kit.Bounds.size.y * 0.45f).ToList();
            var from = new Vector3((dome.Max(p => p.x) + dome.Min(p => p.x)) / 2f, kit.Bounds.max.y, (dome.Max(p => p.z) + dome.Min(p => p.z)) / 2f);
            var placed = Outfit.Place(kit, from, new Vector3(hx, top + 0.022f, hz - 0.006f), Vector3.one * helmetScale);
            Part(result, Outfit.Attach(body, placed, "head", "Head_" + item, KitMaterials(kit)), Slot.Head, M(option));
        }

        // 4: pakol, the rolled wool cap
        {
            var s = Sec(top - 0.06f);
            var rings = new List<(float, float[])>
            {
                (top - 0.083f, R(s, 1, 0.002f)), (top - 0.087f, R(s, 1, 0.014f)), (top - 0.082f, R(s, 1, 0.028f)),
                (top - 0.068f, R(s, 1, 0.036f)), (top - 0.05f, R(s, 1, 0.036f)), (top - 0.036f, R(s, 1, 0.029f)),
                (top - 0.027f, R(s, 1, 0.018f)), (top - 0.023f, R(s, 1, 0.009f)), (top - 0.014f, R(s, 0.99f, 0.007f)),
                (top + 0.004f, R(s, 0.94f, 0.006f)), (top + 0.011f, R(s, 0.75f, 0f)), (top + 0.014f, R(s, 0.4f, 0f)),
            };
            var pakol = Outfit.Lathe(rings, hx, hz, 0.25f, true);
            Tint(Part(result, Outfit.Attach(body, pakol, "head", "Head_Pakol", wool), Slot.Head, M(4)), Slot.HeadColor, Wardrobe.HeadColors);
        }

        // 5: turban (lungee): diagonal wraps around the head, a tail down the back
        {
            float yb = brow + 0.01f, yt = top + 0.03f;
            var rings = new List<(float, float[])> { (yb - 0.004f, R(Sec(yb), 1, 0.004f)) };
            int steps = 14;
            for (int i = 0; i <= steps; i++)
            {
                float f = i / (float)steps, y = Mathf.Lerp(yb, yt, f);
                var s = Sec(y);
                var r = new float[32];
                for (int k = 0; k < 32; k++)
                {
                    float a = (k + 0.5f) / 32f * Mathf.PI * 2f;
                    float wrap = Mathf.Max(0, Mathf.Sin((f * 5.5f + a / (Mathf.PI * 2f)) * Mathf.PI * 2f));
                    r[k] = s[k] * (y > top - 0.01f ? 1f - (y - top + 0.01f) * 3f : 1f) + 0.024f + 0.012f * Mathf.Sin(f * Mathf.PI) + 0.007f * wrap;
                }
                rings.Add((y, r));
            }
            var cap = Sec(top - 0.01f);
            rings.Add((yt + 0.012f, R(cap, 0.72f, 0.02f)));
            rings.Add((yt + 0.018f, R(cap, 0.4f, 0.01f)));
            var turban = Outfit.Lathe(rings, hx, hz, 0.2f, true);
            turban.SetWeights(headWeight);
            // the tail (shamla) hangs from the back of the turban to the shoulder blades
            var path = new List<Vector3>();
            var width = new List<float>();
            var normals = new List<Vector3>();
            float backZ = hz - Sec(eyeY)[24] - 0.03f;
            for (int i = 0; i <= 6; i++)
            {
                float f = i / 6f;
                path.Add(new Vector3(hx + 0.03f, Mathf.Lerp(eyeY + 0.03f, shoulderY - 0.1f, f), backZ - 0.012f - 0.02f * f));
                width.Add(Mathf.Lerp(0.06f, 0.1f, f));
                normals.Add(Vector3.back);
            }
            var tail = Outfit.Strip(path, width, normals, 0.004f);
            for (int i = 0; i < tail.P.Count; i++)
            {
                float f = Mathf.Clamp01((eyeY - tail.P[i].y) / Mathf.Max(0.05f, eyeY - shoulderY + 0.1f));
                tail.W[i] = Outfit.Weight((headBone, 1 - f), (chestBone, f));
            }
            turban.Append(tail);
            Tint(Part(result, Outfit.Wear(body, turban, "Head_Turban", cloth), Slot.Head, M(5)), Slot.HeadColor, Wardrobe.HeadColors);
        }

        // 6, 7: ghutra (white) and shemagh (red checks) with the black agal: cloth over the head that
        // falls to the shoulders, the face open
        // around the neck and the top of the shoulders only (the arms stand out sideways at rest)
        var neckPts = body.PointsIn(Region.Torso, Region.Head).Where(p => Mathf.Abs(p.x - hx) < 0.2f).ToList();
        neckPts.AddRange(shells.SelectMany(s => s.P).Where(p => p.y > shoulderY - 0.15f && Mathf.Abs(p.x - hx) < 0.2f));
        // dome: the height under the crown over which a loose hood rounds off (0: the cloth lies on the head)
        Garment Headcloth(float headRoom, float hang, float faceHalfAngle, bool closedBelowChin, float dome = 0f)
        {
            var rings = new List<(float y, float[] r)>();
            float[] widest = null;
            float yTop = top + headRoom;
            // over the head, down to the chin: never narrower than above (the cloth hangs)
            for (float y = yTop; y > chin; y -= 0.012f)
            {
                var s = y >= top - 0.004f ? R(Sec(top - 0.004f), Mathf.Clamp01(1f - (y - top + 0.004f) * 9f), 0) : Sec(y);
                var r = R(s, 1, headRoom);
                if (dome > 0f && y > top - dome)
                {
                    // a round crown: an ellipse from the widest ring under it up to the top of the cloth
                    float t = Mathf.Clamp01((y - (top - dome)) / (dome + headRoom));
                    r = R(Sec(top - dome), Mathf.Sqrt(1f - t * t), headRoom * Mathf.Sqrt(1f - t * t));
                }
                if (widest != null && y < eyeY + 0.02f) r = r.Select((v, k) => Mathf.Max(v, widest[k] + 0.002f)).ToArray();
                if (y < eyeY + 0.04f) widest = r;
                rings.Add((y, r));
            }
            rings.Reverse();
            // below the chin: around the neck and out over the shoulders
            float shoulder = shoulderY - hang;
            var below = new List<(float y, float[] r)>();
            for (float y = chin - 0.012f; y > shoulder; y -= 0.014f)
            {
                var s = Outfit.Support(neckPts, y, 0.012f, hx, hz) ?? widest;
                var r = s.Select((v, k) => v + 0.02f).ToArray();
                var prev = below.Count > 0 ? below[below.Count - 1].r : widest;
                if (!closedBelowChin) r = r.Select((v, k) => Mathf.Max(v, prev[k] + 0.004f)).ToArray();
                else r = r.Select((v, k) => Mathf.Max(v, Mathf.Min(prev[k], v + 0.01f))).ToArray();
                below.Add((y, r));
            }
            below.Reverse();
            var all = below.Concat(rings).ToList();   // bottom to top
            int faceFrom = below.Count;
            var g = Outfit.Lathe(all, hx, hz, 0.12f, true, (i, k) =>
            {
                float y = all[i].y;
                if (i >= faceFrom && (y > brow || y < chin - 0.004f)) return false;
                if (i < faceFrom && closedBelowChin) return false;
                float a = ((k + 0.5f) / 32f * 360f + 360f) % 360f;
                float half = i < faceFrom ? 30f : faceHalfAngle * (0.7f + 0.3f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((brow - y) / (brow - chin))));
                return Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < half;
            }, true);
            for (int i = 0; i < g.P.Count; i++)
            {
                float f = Mathf.Clamp01((chin - g.P[i].y) / Mathf.Max(0.05f, chin - shoulder));
                g.W[i] = Outfit.Weight((headBone, 1 - f), (neckBone, f * (1 - f)), (chestBone, f * f));
            }
            return g;
        }
        var agalRing = Sec(top - 0.035f);
        var agal = Outfit.Torus(new Vector3(hx, top - 0.028f, hz), R(agalRing, 1, 0.026f), 0.0065f);
        agal.Append(Outfit.Torus(new Vector3(hx, top - 0.043f, hz), R(Sec(top - 0.045f), 1, 0.027f), 0.0065f));
        var ghutra = Headcloth(0.012f, 0.03f, 52f, false);
        Part(result, Outfit.Wear(body, ghutra, "Head_Ghutra", white), Slot.Head, M(6));
        Part(result, Outfit.Wear(body, ghutra, "Head_Shemagh", check), Slot.Head, M(7));
        Part(result, Outfit.Attach(body, agal, "head", "Head_Agal", black), Slot.Head, M(6, 7));

        // 8: hijab: close around the face, covering hair, ears, neck and shoulders
        var hijab = Headcloth(0.009f, 0.09f, 48f, true);
        Tint(Part(result, Outfit.Wear(body, hijab, "Head_Hijab", cloth), Slot.Head, M(8)), Slot.HeadColor, Wardrobe.HeadColors);

        // 9: prayer cap (taqiyah)
        {
            float yb = top - 0.062f;
            var rings = new List<(float, float[])> { (yb - 0.002f, R(Sec(yb), 1, 0.003f)) };
            for (float y = yb; y < top - 0.006f; y += 0.01f) rings.Add((y, R(Sec(y), 1, 0.006f)));
            rings.Add((top + 0.004f, R(Sec(top - 0.01f), 0.75f, 0.004f)));
            rings.Add((top + 0.007f, R(Sec(top - 0.01f), 0.35f, 0f)));
            Tint(Part(result, Outfit.Attach(body, Outfit.Lathe(rings, hx, hz, 0.06f, true), "head", "Head_PrayerCap", knit), Slot.Head, M(9)), Slot.HeadColor, Wardrobe.HeadColors);
        }

        // 10: beret, pulled to the right
        {
            float yb = top - 0.052f;
            var lean = Enumerable.Range(0, 32).Select(k => Mathf.Cos((k + 0.5f) / 32f * Mathf.PI * 2f)).ToArray();
            float[] Tilt(float[] r, float amount) => r.Select((v, k) => v + lean[k] * amount).ToArray();
            var s = Sec(yb);
            var rings = new List<(float, float[])>
            {
                (yb - 0.003f, R(s, 1, 0.003f)), (yb + 0.006f, R(s, 1, 0.007f)), (yb + 0.02f, Tilt(R(s, 1.05f, 0.028f), 0.012f)),
                (yb + 0.037f, Tilt(R(s, 1.08f, 0.04f), 0.024f)), (top + 0.012f, Tilt(R(s, 0.98f, 0.022f), 0.03f)),
                (top + 0.022f, Tilt(R(s, 0.6f, 0.0f), 0.024f)), (top + 0.026f, Tilt(R(s, 0.15f, 0.0f), 0.012f)),
            };
            Tint(Part(result, Outfit.Attach(body, Outfit.Lathe(rings, hx, hz, 0.08f, true), "head", "Head_Beret", wool), Slot.Head, M(10)), Slot.HeadColor, Wardrobe.HeadColors);
        }

        // 11: balaclava: the head and neck covered, an opening for the eyes
        var eyeZ = eyePts.Count > 0 ? eyePts.Max(p => p.z) : hb.max.z;
        var balaclava = Outfit.Shell(body, new Cut
        {
            Regions = new[] { Region.Head }, Thickness = 0.008f, Smooth = 3, Tile = 0.06f,
            Keep = (p, n) => !(p.z > hz && Mathf.Abs(p.y - eyeY) < 0.021f && Mathf.Abs(p.x - hx) < 0.068f) && p.y > shoulderY - 0.02f,
        });
        Part(result, Outfit.Wear(body, balaclava, "Head_Balaclava", AssetSetup.Mat("Knit_Black", new Color(0.07f, 0.07f, 0.075f), 0.05f, 0f, null, knit.GetTexture("_BumpMap") as Texture2D)), Slot.Head, M(11));

        // 12: field cap with a peak
        {
            float yb = brow + 0.012f;
            var rings = new List<(float, float[])> { (yb - 0.002f, R(Sec(yb), 1, 0.005f)) };
            for (float y = yb; y < top - 0.006f; y += 0.012f) rings.Add((y, R(Sec(y), 1, 0.008f + 0.012f * Mathf.Clamp01((y - yb) / (top - yb)))));
            rings.Add((top + 0.01f, R(Sec(top - 0.01f), 0.85f, 0.016f)));
            rings.Add((top + 0.016f, R(Sec(top - 0.01f), 0.4f, 0f)));
            var cap = Outfit.Lathe(rings, hx, hz, 0.08f, true);
            // the peak: a curved visor at the front
            var s = Sec(yb);
            var visor = new Garment();
            int arc = 10;
            var ids = new int[arc + 1, 2];
            for (int i = 0; i <= arc; i++)
            {
                float a = Mathf.Lerp(40f, 140f, i / (float)arc) * Mathf.Deg2Rad;
                int k = Mathf.Clamp(Mathf.RoundToInt(a / (Mathf.PI * 2f) * 32f - 0.5f), 0, 31);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float reach = 0.07f * Mathf.Sin(Mathf.Lerp(0.15f, 0.85f, i / (float)arc) * Mathf.PI);
                var inner = new Vector3(hx, yb, hz) + dir * (s[k] + 0.004f);
                var outer = inner + dir * reach + Vector3.down * reach * 0.25f;
                ids[i, 0] = visor.Add(inner, Vector3.up, new Vector2(i * 0.1f, 0));
                ids[i, 1] = visor.Add(outer, Vector3.up, new Vector2(i * 0.1f, 1));
            }
            for (int i = 0; i < arc; i++)
            {
                Outfit.Face(visor, ids[i, 0], ids[i + 1, 0], ids[i, 1], ids[i + 1, 1], Vector3.up);
                Outfit.Face(visor, ids[i + 1, 0], ids[i, 0], ids[i + 1, 1], ids[i, 1], Vector3.down);
            }
            cap.Append(visor);
            Tint(Part(result, Outfit.Attach(body, cap, "head", "Head_FieldCap", cloth), Slot.Head, M(12)), Slot.HeadColor, Wardrobe.HeadColors);
        }

        // 13: hood and scarf (TEAM 1): a black mask over the face and neck (only the eyes free), a loose
        // hood over it falling to the shoulders, and a scarf wound twice round the neck, its ends on the chest
        {
            var mask = Outfit.Shell(body, new Cut
            {
                Regions = new[] { Region.Head }, Thickness = 0.009f, Smooth = 4, Tile = 0.06f,
                Keep = (p, n) => !(p.z > hz && Mathf.Abs(p.y - eyeY) < 0.015f && Mathf.Abs(p.x - hx) < 0.062f) && p.y > shoulderY - 0.02f,
            });
            var hooded = Headcloth(0.022f, 0.06f, 30f, false, 0.075f);
            if (mask != null) hooded.Append(mask);
            // the scarf: two thick rolls round the neck, then the two ends down the chest
            int neckBoneId = neckBone, chestId = chestBone, midId = body.Bone("spine_02");
            var scarf = new Garment();
            int fold = 0;
            foreach (var (y, extra, tube) in new[] { (chin - 0.022f, 0.022f, 0.014f), (chin - 0.048f, 0.03f, 0.016f), (chin - 0.074f, 0.036f, 0.015f) })
            {
                var around = Outfit.Support(neckPts, y, 0.014f, hx, hz);
                if (around == null) continue;
                // loosely wound: each turn a little wavy, sagging to the front
                int turn = fold++;
                var radius = around.Select((v, k) =>
                {
                    float a = (k + 0.5f) / 32f * Mathf.PI * 2f;
                    return v + extra + 0.008f * Mathf.Sin(a * 3f + turn * 1.7f) + 0.006f * Mathf.Max(0f, Mathf.Sin(a));
                }).ToArray();
                var roll = Outfit.Torus(new Vector3(hx, y, hz), radius, tube, 8);
                roll.SetWeights(Outfit.Weight((neckBoneId, 0.6f), (chestId, 0.4f)));
                scarf.Append(roll);
            }
            var front = shells.SelectMany(g => g.P).Concat(body.PointsIn(Region.Torso)).ToList();
            float FrontAt(float x, float y) => front.Where(q => Mathf.Abs(q.y - y) < 0.03f && Mathf.Abs(q.x - x) < 0.04f).Select(q => q.z).DefaultIfEmpty(chest.z + 0.12f).Max();
            foreach (var (side, length) in new[] { (-1f, 0.42f), (1f, 0.3f) })
            {
                var path = new List<Vector3>();
                var width = new List<float>();
                var normals = new List<Vector3>();
                float x = hx + side * 0.065f;
                for (int i = 0; i <= 8; i++)
                {
                    float f = i / 8f, y = chin - 0.07f - length * f;
                    path.Add(new Vector3(x + side * 0.01f * f, y, FrontAt(x, y) + 0.016f));
                    width.Add(Mathf.Lerp(0.085f, 0.11f, f));
                    normals.Add(Vector3.forward);
                }
                var end = Outfit.Strip(path, width, normals, 0.008f);
                for (int i = 0; i < end.P.Count; i++)
                {
                    float f = Mathf.Clamp01((chin - 0.07f - end.P[i].y) / length);
                    end.W[i] = Outfit.Weight((neckBoneId, (1 - f) * 0.4f), (chestId, 0.6f + 0.2f * f), (midId, 0.4f * f));
                }
                scarf.Append(end);
            }
            hooded.Append(scarf);
            Part(result, Outfit.Wear(body, hooded, "Head_HoodScarf", hood), Slot.Head, M(13));
        }

        // hair under the head wear, beard unless the face is covered
        if (hair != null) Part(result, hair, Slot.Head, female ? M(0) : M(0, 1, 2, 3, 4, 9, 10, 12));
        if (beard != null)
        {
            var part = Part(result, beard, Slot.Beard, M(1));
            part.HasSecond = true;
            part.Slot2 = Slot.Head;
            part.Mask2 = AllBut(11, 13);
        }

        // ---- shoes: kit boots (skinned to foot, toes and ankle), formal shoes, Kabuli sandals
        foreach (var (option, item) in new[] { (0, "Boot"), (1, "BootBrown") })
        {
            var kit = Outfit.LoadKit(item);
            if (kit == null) continue;
            Garment pair = null;
            foreach (string side in new[] { "l", "r" })
            {
                var foot = body.At("foot_" + side);
                var ball = body.At("ball_" + side, "foot_" + side);
                float sign = Mathf.Sign(foot.x - hip.x);
                var pts = body.PointsIn(Region.Feet).Where(p => Mathf.Sign(p.x - hip.x) == sign).ToList();
                if (pts.Count == 0) continue;
                var fb = new Bounds(pts[0], Vector3.zero);
                foreach (var p in pts) fb.Encapsulate(p);
                var kb = kit.Bounds;
                float len = (fb.size.z * 1.12f + 0.04f) / kb.size.z, wid = (fb.size.x + 0.03f) / kb.size.x;
                // measured on the files: the toe points to +Z like the body (the shaft stands over the
                // heel at min z); "Boot" is a right boot (big toe bulge at -X), "BootBrown" a left one.
                // The heel goes to the back of the foot, and the boot is mirrored only for the other foot.
                bool rightBoot = item == "Boot", rightFoot = sign > 0;
                var placed = Outfit.Place(kit, new Vector3(kb.center.x, kb.min.y, kb.min.z), new Vector3(fb.center.x, ground - 0.004f, fb.min.z - 0.013f),
                    new Vector3(wid, Mathf.Max(len, wid), len), 0f, rightBoot != rightFoot);
                int footBone = body.Bone("foot_" + side), ballBone = body.Bone("ball_" + side), calfBone = body.Bone("calf_" + side);
                for (int i = 0; i < placed.P.Count; i++)
                {
                    var p = placed.P[i];
                    float toes = ballBone >= 0 ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(ball.z - 0.02f, ball.z + 0.03f, p.z)) : 0;
                    float shin = 0.8f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(foot.y + 0.03f, foot.y + 0.13f, p.y));
                    placed.W[i] = Outfit.Weight((footBone, (1 - toes) * (1 - shin)), (ballBone, toes * (1 - shin)), (calfBone, shin));
                }
                if (pair == null) pair = placed; else pair.Append(placed);
            }
            Part(result, Outfit.Wear(body, pair, "Feet_" + item, KitMaterials(kit)), Slot.Feet, M(option));
        }
        // a leather lining under the kit boots: no bare toes when the boot shape and the foot differ
        var lining = Outfit.Shell(body, new Cut { Regions = new[] { Region.Feet }, Thickness = 0.006f, Smooth = 2, Tile = 0.1f });
        Part(result, Outfit.Wear(body, lining, "Feet_BootLining", AssetSetup.Mat("Leather_Boot", new Color(0.09f, 0.08f, 0.07f), 0.35f)), Slot.Feet, M(0, 1));
        var shoes = Outfit.Shell(body, new Cut { Regions = new[] { Region.Feet }, Thickness = 0.007f, Smooth = 3, Tile = 0.1f });
        var sole = shoes != null ? Outfit.Derive(shoes, p => p.y < ground + 0.018f, 0.003f) : null;
        Part(result, Outfit.Wear(body, shoes, "Feet_Shoes", leather), Slot.Feet, M(2));
        Part(result, Outfit.Wear(body, sole, "Feet_ShoeSoles", rubber), Slot.Feet, M(2));
        var sandalLeather = AssetSetup.Mat("Leather_Brown", new Color(0.3f, 0.19f, 0.11f), 0.35f, 0f, null, leather.GetTexture("_BumpMap") as Texture2D);
        var sandal = Outfit.Shell(body, new Cut { Regions = new[] { Region.Feet }, Thickness = 0.008f, Tile = 0.1f, Keep = (p, n) => p.y < ground + 0.024f });
        // a wide strap over the instep (the toes stay free, as on a Kabuli chappal)
        var footR = body.At("foot_r");
        var footL = body.At("foot_l");
        var straps = Outfit.Shell(body, new Cut
        {
            Regions = new[] { Region.Feet }, Thickness = 0.005f, Tile = 0.1f,
            Keep = (p, n) => p.y > ground + 0.012f && Mathf.Abs(p.z - ((Mathf.Abs(p.x - footR.x) < Mathf.Abs(p.x - footL.x) ? footR : footL).z + 0.065f)) < 0.028f,
        });
        if (sandal != null && straps != null) sandal.Append(straps);
        Part(result, Outfit.Wear(body, sandal, "Feet_Sandals", sandalLeather), Slot.Feet, M(3));

        // ---- gloves
        var gloves = Outfit.Shell(body, new Cut { Regions = new[] { Region.Hands }, Thickness = 0.004f, Tile = 0.06f });
        Part(result, Outfit.Wear(body, gloves, "Gloves", glove), Slot.Gloves, M(1));

        // ---- belt and knee pads (kit)
        var beltKit = Outfit.LoadKit("Belt");
        if (beltKit != null)
        {
            float beltY = hip.y + 0.06f;
            var around = new List<Vector3>(under.Where(p => Mathf.Abs(p.y - beltY) < 0.03f));
            if (topShells[0] != null) around.AddRange(topShells[0].P.Where(p => Mathf.Abs(p.y - beltY) < 0.03f && Mathf.Abs(p.x - hip.x) < 0.3f));
            float w = around.Max(p => p.x) - around.Min(p => p.x), d = around.Max(p => p.z) - around.Min(p => p.z);
            var c = new Vector3((around.Max(p => p.x) + around.Min(p => p.x)) / 2f, beltY, (around.Max(p => p.z) + around.Min(p => p.z)) / 2f);
            var kb = beltKit.Bounds;
            var placed = Outfit.Place(beltKit, kb.center, c, new Vector3((w + 0.05f) / kb.size.x, 0.9f, (d + 0.05f) / kb.size.z));
            Part(result, Outfit.Attach(body, placed, "pelvis", "Kit_Belt", KitMaterials(beltKit)), Slot.Kit, M(1));
        }
        var padKit = Outfit.LoadKit("KneePad");
        if (padKit != null)
        {
            foreach (string side in new[] { "l", "r" })
            {
                var k = body.At("calf_" + side);
                var around = trousers.Where(t => t != null).SelectMany(t => t.P).Where(p => Mathf.Abs(p.y - k.y) < 0.03f && Mathf.Abs(p.x - k.x) < 0.14f).ToList();
                if (around.Count == 0) continue;
                float w = around.Max(p => p.x) - around.Min(p => p.x), d = around.Max(p => p.z) - around.Min(p => p.z);
                var c = new Vector3((around.Max(p => p.x) + around.Min(p => p.x)) / 2f, k.y - 0.01f, (around.Max(p => p.z) + around.Min(p => p.z)) / 2f);
                var kb = padKit.Bounds;
                float s = (w + 0.025f) / kb.size.x;
                var placed = Outfit.Place(padKit, kb.center, c + Vector3.forward * 0.01f, new Vector3(s, s, (d + 0.03f) / kb.size.z), 0, side == "l");
                Part(result, Outfit.Attach(body, placed, "calf_" + side, "Kit_KneePad_" + side, KitMaterials(padKit)), Slot.Kit, M(1));
            }
        }

        // ---- assault pack on the back, behind any vest
        var packKit = Outfit.LoadKit("Backpack");
        if (packKit != null)
        {
            var back = shells.SelectMany(s => s.P).Concat(vestPieces.SelectMany(v => v.P))
                .Where(p => Mathf.Abs(p.y - (chest.y - 0.04f)) < 0.08f && Mathf.Abs(p.x - neck.x) < 0.12f).ToList();
            float backZ = back.Count > 0 ? back.Min(p => p.z) : chest.z - 0.16f;
            var turned = Outfit.Place(packKit, packKit.Bounds.center, Vector3.zero, Vector3.one * 1.15f, 180f);
            var tb = new Bounds(turned.P[0], Vector3.zero);
            foreach (var p in turned.P) tb.Encapsulate(p);
            var shift = new Vector3(neck.x - tb.center.x, chest.y - 0.05f - tb.center.y, backZ - 0.006f - tb.max.z);
            for (int i = 0; i < turned.P.Count; i++) turned.P[i] += shift;
            Part(result, Outfit.Attach(body, turned, "spine_03", "Bag_AssaultPack", KitMaterials(packKit)), Slot.Bag, M(1));
        }
        Debug.Log($"[Setup] {key}: {result.Parts.Count} wardrobe pieces");
        return result;
    }

    /// <summary>A box (pockets, pouches) in tailor space.</summary>
    private static Garment Box(Vector3 center, Quaternion rotation, Vector3 size)
    {
        var g = new Garment();
        var h = size / 2f;
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var d in dirs)
        {
            var u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
            var v = Vector3.Cross(d, u);
            var c = Vector3.Scale(d, h);
            var hu = Vector3.Scale(u, h);
            var hv = Vector3.Scale(v, h);
            var n = rotation * d;
            int a = g.Add(center + rotation * (c - hu - hv), n, new Vector2(0, 0));
            int b = g.Add(center + rotation * (c - hu + hv), n, new Vector2(1, 0));
            int e = g.Add(center + rotation * (c + hu - hv), n, new Vector2(0, 1));
            int f = g.Add(center + rotation * (c + hu + hv), n, new Vector2(1, 1));
            Outfit.Face(g, e, f, a, b, n);
        }
        return g;
    }
}
