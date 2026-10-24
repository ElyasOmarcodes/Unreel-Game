using UnityEngine;

namespace UA
{
    /// <summary>
    /// The character a player builds: body, every piece of clothing and gear and their colours, packed
    /// into one int ("look") that travels with the player over the network. The editor dresses the agent
    /// prefabs with all options (Outfit.cs); Agent.SetLook shows the chosen ones.
    /// </summary>
    public static class Wardrobe
    {
        public enum Slot { Body, Top, Bottom, Head, Vest, Feet, Gloves, Kit, Beard, Bag, TopColor, BottomColor, HeadColor }

        // bits per slot, in the order of the enum
        private static readonly int[] Bits = { 2, 3, 2, 4, 2, 2, 1, 1, 1, 1, 3, 3, 2 };

        public static readonly string[][] Names =
        {
            new[] { "MAN - LIGHT", "MAN - DARK", "WOMAN - LIGHT", "WOMAN - DARK" },
            new[] { "COMBAT JACKET", "AFGHAN PERAHAN", "ARAB THOBE", "FORMAL SUIT", "LONG SHIRT", "AFGHAN CHAPAN", "ABAYA", "LONG COAT" },
            new[] { "CARGO TROUSERS", "AFGHAN TUNBAN", "SUIT TROUSERS", "JEANS" },
            new[] { "NONE", "COMBAT HELMET", "HELMET + HEADSET", "HELMET + NIGHT VISION", "PAKOL", "TURBAN", "GHUTRA + AGAL", "SHEMAGH + AGAL", "HIJAB", "PRAYER CAP", "BERET", "BALACLAVA", "FIELD CAP" },
            new[] { "NONE", "PLATE CARRIER", "CHEST RIG", "WAISTCOAT" },
            new[] { "COMBAT BOOTS", "DESERT BOOTS", "FORMAL SHOES", "SANDALS" },
            new[] { "NONE", "TACTICAL GLOVES" },
            new[] { "NONE", "BELT + KNEE PADS" },
            new[] { "NONE", "BEARD" },
            new[] { "NONE", "ASSAULT PACK" },
            null, null, null,
        };

        public static readonly string[] SlotNames = { "AGENT", "TOP", "BOTTOM", "HEAD", "VEST", "SHOES", "GLOVES", "BELT", "BEARD", "BAG", "TOP COLOUR", "BOTTOM COLOUR", "HEAD COLOUR" };

        public struct Swatch
        {
            public string Name; public Color Color;
            public Swatch(string name, Color color) { Name = name; Color = color; }
        }

        private static Swatch S(string name, float r, float g, float b) => new Swatch(name, new Color(r, g, b));

        // the combat jacket and cargo trousers use camo patterns (materials) instead of colours, in this order
        public static readonly string[] Camos = { "URBAN", "DESERT", "WOODLAND", "BLACK", "OLIVE", "SAND", "NAVY", "GREY" };

        /// <summary>Colours of every top (8 each, cloth colours a tailor would sell).</summary>
        public static readonly Swatch[][] TopColors =
        {
            null, // camo
            new[] { S("WHITE", 0.86f, 0.85f, 0.81f), S("SKY BLUE", 0.55f, 0.66f, 0.74f), S("OLIVE", 0.4f, 0.42f, 0.3f), S("SAND", 0.72f, 0.64f, 0.5f), S("GREY", 0.48f, 0.49f, 0.5f), S("BROWN", 0.42f, 0.32f, 0.24f), S("BLACK", 0.12f, 0.12f, 0.13f), S("CREAM", 0.82f, 0.77f, 0.64f) },
            new[] { S("WHITE", 0.9f, 0.9f, 0.88f), S("CREAM", 0.85f, 0.8f, 0.68f), S("LIGHT GREY", 0.7f, 0.71f, 0.72f), S("BEIGE", 0.76f, 0.68f, 0.54f), S("BROWN", 0.45f, 0.35f, 0.26f), S("NAVY", 0.18f, 0.22f, 0.32f), S("BLACK", 0.12f, 0.12f, 0.13f), S("OLIVE", 0.4f, 0.42f, 0.3f) },
            new[] { S("CHARCOAL", 0.2f, 0.21f, 0.23f), S("NAVY", 0.13f, 0.16f, 0.26f), S("BLACK", 0.08f, 0.08f, 0.09f), S("GREY", 0.42f, 0.43f, 0.45f), S("BROWN", 0.32f, 0.24f, 0.18f), S("BEIGE", 0.66f, 0.58f, 0.45f), S("OLIVE", 0.3f, 0.32f, 0.22f), S("WHITE", 0.85f, 0.85f, 0.83f) },
            new[] { S("WHITE", 0.86f, 0.86f, 0.84f), S("BLUE", 0.32f, 0.44f, 0.62f), S("BLACK", 0.12f, 0.12f, 0.13f), S("KHAKI", 0.6f, 0.54f, 0.4f), S("MAROON", 0.42f, 0.14f, 0.16f), S("GREEN", 0.24f, 0.36f, 0.26f), S("GREY", 0.5f, 0.5f, 0.52f), S("ORANGE", 0.72f, 0.42f, 0.18f) },
            new[] { S("GREEN STRIPE", 0.3f, 0.42f, 0.3f), S("BLUE STRIPE", 0.26f, 0.34f, 0.56f), S("PURPLE STRIPE", 0.4f, 0.26f, 0.42f), S("RED STRIPE", 0.56f, 0.2f, 0.2f), S("BROWN STRIPE", 0.44f, 0.32f, 0.22f), S("GOLD STRIPE", 0.62f, 0.5f, 0.24f), S("BLACK STRIPE", 0.16f, 0.16f, 0.18f), S("GREY STRIPE", 0.45f, 0.46f, 0.48f) },
            new[] { S("BLACK", 0.07f, 0.07f, 0.08f), S("NAVY", 0.12f, 0.14f, 0.24f), S("BROWN", 0.3f, 0.22f, 0.17f), S("GREY", 0.36f, 0.37f, 0.39f), S("OLIVE", 0.3f, 0.32f, 0.23f), S("MAROON", 0.32f, 0.1f, 0.13f), S("SAND", 0.62f, 0.55f, 0.42f), S("TEAL", 0.12f, 0.3f, 0.32f) },
            new[] { S("CAMEL", 0.6f, 0.47f, 0.32f), S("BLACK", 0.1f, 0.1f, 0.11f), S("NAVY", 0.14f, 0.17f, 0.28f), S("GREY", 0.44f, 0.45f, 0.47f), S("OLIVE", 0.34f, 0.36f, 0.26f), S("BURGUNDY", 0.38f, 0.12f, 0.15f), S("BEIGE", 0.72f, 0.65f, 0.52f), S("BROWN", 0.36f, 0.27f, 0.2f) },
        };

        public static readonly Swatch[][] BottomColors =
        {
            null, // camo
            new[] { S("WHITE", 0.86f, 0.85f, 0.81f), S("GREY", 0.48f, 0.49f, 0.5f), S("OLIVE", 0.4f, 0.42f, 0.3f), S("SAND", 0.72f, 0.64f, 0.5f), S("BROWN", 0.42f, 0.32f, 0.24f), S("BLACK", 0.12f, 0.12f, 0.13f), S("SKY BLUE", 0.55f, 0.66f, 0.74f), S("CREAM", 0.82f, 0.77f, 0.64f) },
            new[] { S("CHARCOAL", 0.2f, 0.21f, 0.23f), S("NAVY", 0.13f, 0.16f, 0.26f), S("BLACK", 0.08f, 0.08f, 0.09f), S("GREY", 0.42f, 0.43f, 0.45f), S("BROWN", 0.32f, 0.24f, 0.18f), S("BEIGE", 0.66f, 0.58f, 0.45f), S("OLIVE", 0.3f, 0.32f, 0.22f), S("WHITE", 0.85f, 0.85f, 0.83f) },
            new[] { S("INDIGO", 0.17f, 0.24f, 0.4f), S("STONE WASH", 0.4f, 0.5f, 0.64f), S("BLACK", 0.13f, 0.13f, 0.15f), S("GREY", 0.38f, 0.39f, 0.42f), S("KHAKI", 0.58f, 0.52f, 0.38f), S("OLIVE", 0.34f, 0.36f, 0.25f), S("BROWN", 0.36f, 0.27f, 0.2f), S("SAND", 0.7f, 0.63f, 0.48f) },
        };

        /// <summary>Colours of cloth head wear (pakol, turban, hijab, prayer cap, beret).</summary>
        public static readonly Swatch[] HeadColors = { S("NATURAL", 0.62f, 0.55f, 0.44f), S("BLACK", 0.1f, 0.1f, 0.11f), S("WHITE", 0.88f, 0.88f, 0.86f), S("OLIVE", 0.36f, 0.38f, 0.27f) };

        /// <summary>A whole outfit in one step (the parts can be changed afterwards).</summary>
        public struct Style
        {
            public string Name;
            public int Top, Bottom, Head, Vest, Feet, Gloves, Kit, Beard, Bag;
        }

        public static readonly Style[] Styles =
        {
            new Style { Name = "SOLDIER", Top = 0, Bottom = 0, Head = 1, Vest = 1, Feet = 0, Gloves = 1, Kit = 1, Bag = 1 },
            new Style { Name = "SPECIAL FORCES", Top = 0, Bottom = 0, Head = 3, Vest = 2, Feet = 1, Gloves = 1, Kit = 1, Beard = 1 },
            new Style { Name = "AFGHAN", Top = 1, Bottom = 1, Head = 4, Vest = 3, Feet = 3, Beard = 1 },
            new Style { Name = "AFGHAN ELDER", Top = 5, Bottom = 1, Head = 5, Vest = 0, Feet = 3, Beard = 1 },
            new Style { Name = "ARAB", Top = 2, Bottom = 1, Head = 6, Vest = 0, Feet = 3, Beard = 1 },
            new Style { Name = "STATESMAN", Top = 3, Bottom = 2, Head = 0, Vest = 0, Feet = 2 },
            new Style { Name = "EVERYDAY", Top = 4, Bottom = 3, Head = 9, Vest = 0, Feet = 0, Beard = 1 },
            new Style { Name = "AFGHAN GUARD", Top = 1, Bottom = 1, Head = 5, Vest = 2, Feet = 1, Beard = 1, Bag = 1 },
            new Style { Name = "WOMAN - ABAYA", Top = 6, Bottom = 1, Head = 8, Vest = 0, Feet = 2 },
            new Style { Name = "WOMAN - SOLDIER", Top = 7, Bottom = 0, Head = 8, Vest = 1, Feet = 0, Gloves = 1, Kit = 1 },
        };

        public static int Count(Slot slot)
        {
            switch (slot)
            {
                case Slot.TopColor: return 8;
                case Slot.BottomColor: return 8;
                case Slot.HeadColor: return HeadColors.Length;
                default: return Names[(int)slot].Length;
            }
        }

        private static int Shift(Slot slot)
        {
            int shift = 0;
            for (int i = 0; i < (int)slot; i++) shift += Bits[i];
            return shift;
        }

        public static int Get(int look, Slot slot) => Mathf.Min((look >> Shift(slot)) & ((1 << Bits[(int)slot]) - 1), Count(slot) - 1);

        public static int Set(int look, Slot slot, int value)
        {
            int shift = Shift(slot), mask = ((1 << Bits[(int)slot]) - 1) << shift;
            value = ((value % Count(slot)) + Count(slot)) % Count(slot);
            return (look & ~mask) | (value << shift);
        }

        public static bool Female(int look) => Get(look, Slot.Body) >= 2;

        /// <summary>Text for one slot of a look (colour slots name the colour of the chosen garment).</summary>
        public static string Label(int look, Slot slot)
        {
            int v = Get(look, slot);
            switch (slot)
            {
                case Slot.TopColor:
                    var top = TopColors[Get(look, Slot.Top)];
                    return top == null ? Camos[v] + " CAMO" : top[v].Name;
                case Slot.BottomColor:
                    var bottom = BottomColors[Get(look, Slot.Bottom)];
                    return bottom == null ? Camos[v] + " CAMO" : bottom[v].Name;
                case Slot.HeadColor: return HeadColors[v].Name;
                default: return Names[(int)slot][v];
            }
        }

        public static int Apply(int look, Style s)
        {
            look = Set(look, Slot.Top, s.Top);
            look = Set(look, Slot.Bottom, s.Bottom);
            look = Set(look, Slot.Head, s.Head);
            look = Set(look, Slot.Vest, s.Vest);
            look = Set(look, Slot.Feet, s.Feet);
            look = Set(look, Slot.Gloves, s.Gloves);
            look = Set(look, Slot.Kit, s.Kit);
            look = Set(look, Slot.Beard, s.Beard);
            return Set(look, Slot.Bag, s.Bag);
        }

        /// <summary>Index of the style this look was made from (or the closest one by top and head).</summary>
        public static int StyleOf(int look)
        {
            int best = 0, bestScore = -1;
            for (int i = 0; i < Styles.Length; i++)
            {
                var s = Styles[i];
                int score = (Get(look, Slot.Top) == s.Top ? 4 : 0) + (Get(look, Slot.Head) == s.Head ? 2 : 0) + (Get(look, Slot.Vest) == s.Vest ? 1 : 0) + (Get(look, Slot.Bottom) == s.Bottom ? 1 : 0);
                if (score > bestScore) { best = i; bestScore = score; }
            }
            return best;
        }

        /// <summary>A ready-made character: a name, a body and a style with its colours, and pieces changed from the style.</summary>
        public struct Preset
        {
            public string Name;
            public int Body, Style, TopColor, BottomColor, HeadColor, Beard;
            public (Slot slot, int value)[] Changes;
            public Preset(string name, int body, int style, int top, int bottom, int head, int beard, params (Slot, int)[] changes)
            { Name = name; Body = body; Style = style; TopColor = top; BottomColor = bottom; HeadColor = head; Beard = beard; Changes = changes; }
        }

        /// <summary>The characters the profile page offers (each one can be changed piece by piece).</summary>
        public static readonly Preset[] Presets =
        {
            // white ghutra with a black agal, white perahan and tunban, dark waistcoat, sandals, camo pack, no beard
            new Preset("DESERT SCOUT", 0, 2, 0, 0, 2, 0, (Slot.Head, 6), (Slot.Bag, 1)),
            new Preset("ELDER", 1, 3, 1, 1, 2, 1),        // Afghan elder: blue chapan, white turban
            new Preset("SOLDIER", 0, 0, 2, 2, 3, 0),       // soldier in woodland camo
            new Preset("THOBE", 0, 4, 0, 0, 2, 1),      // Arab thobe and ghutra
            new Preset("SPECIAL FORCES", 0, 1, 3, 3, 1, 1),        // special forces, black
            new Preset("CIVILIAN", 1, 6, 1, 0, 2, 1),       // everyday: long shirt, jeans
            new Preset("GUARD", 1, 7, 4, 2, 3, 1),      // Afghan guard: grey perahan, turban, chest rig
            new Preset("BLACK TURBAN", 0, 2, 7, 7, 1, 1, (Slot.Head, 5)),   // cream perahan, black turban, waistcoat
            new Preset("STATESMAN", 0, 5, 1, 1, 1, 0),       // statesman: navy suit
            new Preset("ABAYA", 2, 8, 0, 0, 1, 0),      // abaya and hijab
            new Preset("WOMAN SOLDIER", 3, 9, 4, 4, 3, 0),       // woman soldier
        };

        public static int PresetLook(int index)
        {
            var p = Presets[((index % Presets.Length) + Presets.Length) % Presets.Length];
            int look = Apply(Set(0, Slot.Body, p.Body), Styles[p.Style]);
            look = Set(look, Slot.TopColor, p.TopColor);
            look = Set(look, Slot.BottomColor, p.BottomColor);
            look = Set(look, Slot.HeadColor, p.HeadColor);
            look = Set(look, Slot.Beard, p.Beard);
            if (p.Changes != null)
                foreach (var (slot, value) in p.Changes) look = Set(look, slot, value);
            return look;
        }

        /// <summary>A believable random outfit (bots, first start).</summary>
        public static int Random()
        {
            bool female = UnityEngine.Random.value < 0.25f;
            int look = Set(0, Slot.Body, (female ? 2 : 0) + UnityEngine.Random.Range(0, 2));
            int style = female ? UnityEngine.Random.Range(8, 10) : UnityEngine.Random.Range(0, 8);
            look = Apply(look, Styles[style]);
            look = Set(look, Slot.TopColor, UnityEngine.Random.Range(0, 8));
            look = Set(look, Slot.BottomColor, UnityEngine.Random.Range(0, 8));
            look = Set(look, Slot.HeadColor, UnityEngine.Random.Range(0, HeadColors.Length));
            if (!female) look = Set(look, Slot.Beard, UnityEngine.Random.value < 0.7f ? 1 : 0);
            if (Get(look, Slot.Top) == 0) look = Set(look, Slot.BottomColor, Get(look, Slot.TopColor));
            return look;
        }
    }
}
