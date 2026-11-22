using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// What lies on the ground in an ADVANCED match: a weapon, a vest, C4 charges, grenades, or a closed
    /// container (a supply crate, a military crate at a hot spot, a supply drop from the sky).
    /// </summary>
    public enum LootKind : byte { Weapon, Armor, C4, Crate, Military, Airdrop, Grenade }

    public class LootItem
    {
        public ushort Id;
        public LootKind Kind;
        public byte Item;          // weapon id, vest points, number of charges or grenades (a container: nothing)
        public Vector3 Pos;
        // host only
        public float Until = float.MaxValue;   // a dropped thing goes after a while; the map's own stay
        public int Spot = -1;                  // the place on the map it belongs to (comes back there when taken)
    }

    /// <summary>
    /// The five grades of everything on the map, in the colours every game of the kind uses (grey, green,
    /// blue, purple, gold): the better the grade, the rarer, the further from the start and the more fought over.
    /// </summary>
    public static class Tiers
    {
        public const int Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4;
        public static readonly string[] Names = { "COMMON", "UNCOMMON", "RARE", "EPIC", "LEGENDARY" };
        public static readonly Color[] Colors =
        {
            new Color(0.86f, 0.88f, 0.9f), new Color(0.35f, 0.92f, 0.42f), new Color(0.3f, 0.62f, 1f),
            new Color(0.74f, 0.42f, 1f), new Color(1f, 0.76f, 0.2f),
        };
        /// <summary>The weapons of each grade: the pistols one starts with; sub-machine guns and the shotgun;
        /// assault rifles; the marksman rifles; the sniper and the machine gun (only from the sky or a hot spot).</summary>
        public static readonly byte[][] WeaponsOf =
        {
            new byte[] { 4, 5 }, new byte[] { 6, 10, 2 }, new byte[] { 0, 1, 9 }, new byte[] { 7, 11 }, new byte[] { 3, 8 },
        };

        public static int Weapon(int id)
        {
            for (int t = 0; t < WeaponsOf.Length; t++)
                if (System.Array.IndexOf(WeaponsOf[t], (byte)id) >= 0) return t;
            return Common;
        }

        public static int Armor(int points) => points > 100 ? Legendary : points > 50 ? Epic : Uncommon;

        public static int Of(LootItem l)
        {
            switch (l.Kind)
            {
                case LootKind.Weapon: return Weapon(l.Item);
                case LootKind.Armor: return Armor(l.Item);
                case LootKind.C4: return Rare;
                case LootKind.Grenade: return Uncommon;
                case LootKind.Crate: return Rare;
                case LootKind.Military: return Epic;
                case LootKind.Airdrop: return Legendary;
            }
            return Common;
        }

        public static bool Container(LootKind k) => k == LootKind.Crate || k == LootKind.Military || k == LootKind.Airdrop;

        /// <summary>How long a container takes to open (the player stays by it): a moment of risk before the reward.</summary>
        public static float OpenTime(LootKind k) => k == LootKind.Crate ? 1.2f : k == LootKind.Military ? 2.2f : k == LootKind.Airdrop ? 3f : 0f;

        /// <summary>The colour of a thing's ring and light (a supply crate: orange).</summary>
        public static Color Tint(LootItem l) => l.Kind == LootKind.Crate ? new Color(1f, 0.6f, 0.15f) : Colors[Of(l)];
    }

    /// <summary>A yellow fuel drum with the skull sign: shot (or caught by a blast) it explodes.</summary>
    public class BarrelState
    {
        public Vector3 Pos;
        public bool Alive = true;
        // host only
        public float Health = Session.BarrelHealth, RespawnAt, BlowAt = -1f;
        public byte Hitter = Session.NoPlayer;
    }

    /// <summary>A planted remote charge (C4): it goes off when its owner presses DETONATE.</summary>
    public class Charge
    {
        public ushort Id;
        public byte Owner;
        public Vector3 Pos, Normal;
    }

    /// <summary>
    /// A supply drop: announced to everyone, a cargo plane passes over the place, the crate comes down on a
    /// parachute and lands with red smoke; inside, the best of the game (a gold weapon, the heaviest vest).
    /// </summary>
    public class AirDrop
    {
        public ushort Id;
        public Vector3 Pos;
        /// <summary>When it lands, on this machine's clock (the host sends the seconds left; each machine keeps its own time).</summary>
        public float LandAt;
        public float Heading;      // the plane's course, degrees
        public bool Opened;        // the crate has been opened (its smoke goes on a little)
        // host only
        public ushort CrateId;
        public float OpenedAt = -1f;
    }

    /// <summary>A player's request to the host: pick up or open (1), plant a charge (2), set off one's charges (3).</summary>
    public struct ActionRequest { public int Seq; public byte Type; public ushort Id; public Vector3 A, B; }

    /// <summary>
    /// The ADVANCED game (after the way PUBG, Apex and Warzone lay out their loot, scaled to the number of
    /// players in the match):
    ///  - everyone starts with two pistols (grey); a player who dies twice without a kill comes back with a
    ///    sub-machine gun as well, so nobody is left behind;
    ///  - caches on the ground (one weapon, green to purple, and often a vest, grenades or a charge beside it)
    ///    spread over the map, at least one near every start point;
    ///  - supply crates (orange): opened in a moment, a better weapon inside (blue or purple; a player who
    ///    found little in two crates is sure of a blue one in the next) and something more;
    ///  - military crates at the hot spots (the highest places of the map, purple light): long to open, a
    ///    purple weapon for sure (sometimes gold) and a heavy vest;
    ///  - supply drops from the sky every couple of minutes (gold): a gold weapon, the heaviest vest;
    ///  - whoever dies leaves their weapons, vest and charges for a minute; the yellow fuel drums explode.
    /// The host decides everything and sends it several times a second; the players only ask.
    /// </summary>
    public partial class Session
    {
        public const byte StartPistol = 4, StartPistol2 = 5;           // GLOCK 17 and DESERT EAGLE
        public const byte ComebackWeapon = 6;                          // MP5K for a player down on their luck
        public const float BarrelHealth = 40f, BarrelRadius = 7.5f, BarrelLethal = 3.5f, BarrelRespawn = 50f;
        public const float C4Radius = 8.5f, C4Lethal = 4.2f;
        public const int MaxC4 = 3, MaxCharges = 3;
        /// <summary>A vest takes this share of every hit on the body (not the head) out of its own points instead of the health.</summary>
        public const float ArmorShare = 0.4f;
        public const float PickReach = 2.6f;
        /// <summary>A supply drop: announced this long before it lands; the crate falls for the last DropFall seconds from DropHeight.</summary>
        public const float DropWarning = 15f, DropFall = 9f, DropHeight = 95f;
        private const float DropLife = 60f, ContentLife = 90f, AirdropLife = 150f;
        private const int MaxLoot = 90;

        public readonly List<LootItem> Loot = new List<LootItem>();
        public readonly List<BarrelState> Barrels = new List<BarrelState>();
        public readonly List<Charge> Charges = new List<Charge>();
        public readonly List<AirDrop> Drops = new List<AirDrop>();
        /// <summary>Goes up whenever what lies on the map changes (the views and the bots follow it).</summary>
        public int LootVersion;
        /// <summary>A supply drop was announced (on every machine): the banner, the alarm.</summary>
        public event System.Action<AirDrop> DropAnnounced;

        public bool Advanced => Config.Kind == 1;

        private enum SpotKind : byte { Weapon, Extra, Supply, Military }
        private class Spot { public Vector3 Pos; public SpotKind Kind; public float FreeAt; }

        private ushort lootSeq, chargeSeq, dropSeq;
        private readonly List<Spot> spots = new List<Spot>();
        private bool worldDirty;
        private float worldTimer, nextDropAt = -1f;
        private readonly List<(ActionRequest a, float time)> pendingActions = new List<(ActionRequest, float)>();
        private int actionSeq;
        private readonly System.Random lootRng = new System.Random();

        private byte WeaponOf(int tier)
        {
            var list = Tiers.WeaponsOf[Mathf.Clamp(tier, 0, Tiers.WeaponsOf.Length - 1)];
            return list[lootRng.Next(list.Length)];
        }

        // ------------------------------------------------------------------ host: the map's loot

        private void ClearLoot()
        {
            Loot.Clear();
            Barrels.Clear();
            Charges.Clear();
            Drops.Clear();
            spots.Clear();
            pendingActions.Clear();
            nextDropAt = -1f;
            LootVersion++;
            worldDirty = true;
        }

        /// <summary>
        /// Host, at the countdown of an ADVANCED match: the hot spots, the caches, the supply crates and the
        /// drums, as many as the players in the match want (n players):
        ///   caches 1.5 n + 4 (6..24), supply crates 0.8 n + 2 (3..14), hot spots 1 / 2 / 3 (up to 4 / 10 / more players).
        /// </summary>
        private void SetupLoot()
        {
            ClearLoot();
            if (!Advanced || World.I == null) return;
            var w = World.I.Waypoints;
            var sp = World.I.Spawns;
            if (w.Length == 0) return;
            int n = Mathf.Max(2, Players.Count);
            int caches = Mathf.Clamp(Mathf.RoundToInt(1.5f * n + 4), 6, 24);
            int supply = Mathf.Clamp(Mathf.RoundToInt(0.8f * n + 2), 3, 14);
            int hot = n <= 4 ? 1 : n <= 10 ? 2 : 3;
            const int drums = 10;
            var order = Enumerable.Range(0, w.Length).OrderBy(_ => lootRng.Next()).ToList();
            var taken = new List<Vector3>();
            bool NearSpawn(Vector3 p, float d) => sp.Any(s => (s - p).sqrMagnitude < d * d);
            bool Free(Vector3 p, float apart) => taken.All(t => (t - p).sqrMagnitude > apart * apart) && !NearSpawn(p, 4f);
            void Add(Vector3 p, SpotKind kind) { spots.Add(new Spot { Pos = p, Kind = kind }); taken.Add(p); }
            Vector3 Jitter() => new Vector3((float)lootRng.NextDouble() - 0.5f, 0f, (float)lootRng.NextDouble() - 0.5f);

            // the hot spots first: the highest places of the map (towers, roofs), far apart
            int placed = 0;
            foreach (int i in order.OrderByDescending(i => w[i].y))
            {
                if (placed >= hot) break;
                var p = Ground(w[i]);
                if (p == null || !Free(p.Value, 24f) || NearSpawn(p.Value, 12f)) continue;
                Add(p.Value, SpotKind.Military);
                placed++;
            }
            // the caches: a weapon, and often something beside it
            placed = 0;
            foreach (int i in order)
            {
                if (placed >= caches) break;
                var p = Ground(w[i] + Jitter());
                if (p == null || !Free(p.Value, 14f)) continue;
                Add(p.Value, SpotKind.Weapon);
                placed++;
                if (lootRng.NextDouble() < 0.6) AddBeside(p.Value, SpotKind.Extra);
            }
            // nobody starts far from a weapon: a cache within 22 m of every start point
            foreach (var s in sp)
            {
                if (spots.Any(x => x.Kind == SpotKind.Weapon && (x.Pos - s).sqrMagnitude < 25f * 25f)) continue;
                foreach (int i in order.OrderBy(i => (w[i] - s).sqrMagnitude))
                {
                    float d = Vector3.Distance(w[i], s);
                    if (d < 5f) continue;
                    if (d > 22f) break;
                    var p = Ground(w[i]);
                    if (p == null || taken.Any(t => (t - p.Value).sqrMagnitude < 36f)) continue;
                    Add(p.Value, SpotKind.Weapon);
                    break;
                }
            }
            // the supply crates, well spread
            placed = 0;
            foreach (int i in order)
            {
                if (placed >= supply) break;
                var p = Ground(w[i] + Jitter());
                if (p == null || !Free(p.Value, 12f)) continue;
                Add(p.Value, SpotKind.Supply);
                placed++;
            }
            for (int i = 0; i < spots.Count; i++) Restock(i);
            // the drums stand against a wall (cover, and out of the way)
            foreach (int i in order)
            {
                if (Barrels.Count >= drums) break;
                var at = AgainstWall(w[i]);
                if (at == null || !Free(at.Value, 7f) || Barrels.Any(b => (b.Pos - at.Value).sqrMagnitude < 64f)) continue;
                taken.Add(at.Value);
                Barrels.Add(new BarrelState { Pos = at.Value });
            }
            App.Log($"loot: {n} players, {spots.Count(s => s.Kind == SpotKind.Weapon)} caches (+{spots.Count(s => s.Kind == SpotKind.Extra)} beside), " +
                    $"{spots.Count(s => s.Kind == SpotKind.Supply)} supply crates, {spots.Count(s => s.Kind == SpotKind.Military)} hot spots, {Barrels.Count} drums");
            LootVersion++;
            worldDirty = true;

            void AddBeside(Vector3 at, SpotKind kind)
            {
                for (int k = 0; k < 6; k++)
                {
                    var dir = Quaternion.Euler(0, lootRng.Next(360), 0) * Vector3.forward;
                    var q = Ground(at + dir * (1.4f + (float)lootRng.NextDouble()));
                    if (q == null || Mathf.Abs(q.Value.y - at.y) > 0.5f || NearSpawn(q.Value, 3f)) continue;
                    if (Physics.Linecast(at + Vector3.up * 0.4f, q.Value + Vector3.up * 0.4f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    Add(q.Value, kind);
                    return;
                }
            }
        }

        private static Vector3? Ground(Vector3 p)
        {
            if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 3f, World.EnvMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.8f) return null;
            // room above it (not under a stair or a table)
            if (Physics.CheckSphere(hit.point + Vector3.up * 0.9f, 0.4f, World.EnvMask, QueryTriggerInteraction.Ignore)) return null;
            return hit.point;
        }

        private static Vector3? AgainstWall(Vector3 p)
        {
            var g = Ground(p);
            if (g == null) return null;
            var from = g.Value + Vector3.up * 0.5f;
            float best = 3f;
            Vector3? at = null;
            for (int k = 0; k < 8; k++)
            {
                var dir = Quaternion.Euler(0, k * 45f, 0) * Vector3.forward;
                if (Physics.Raycast(from, dir, out var hit, best, World.EnvMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.3f)
                {
                    best = hit.distance;
                    at = Ground(hit.point + hit.normal * 0.5f);
                }
            }
            return at;
        }

        /// <summary>A new thing on a place of the map (by the place's kind).</summary>
        private void Restock(int index)
        {
            var spot = spots[index];
            var item = new LootItem { Id = ++lootSeq, Pos = spot.Pos, Spot = index };
            switch (spot.Kind)
            {
                case SpotKind.Weapon:
                {
                    // on the ground: mostly green, often blue, now and then purple
                    double r = lootRng.NextDouble();
                    item.Kind = LootKind.Weapon;
                    item.Item = WeaponOf(r < 0.6 ? Tiers.Uncommon : r < 0.92 ? Tiers.Rare : Tiers.Epic);
                    break;
                }
                case SpotKind.Extra:
                {
                    double r = lootRng.NextDouble();
                    if (r < 0.4) { item.Kind = LootKind.Armor; item.Item = 50; }
                    else if (r < 0.75) { item.Kind = LootKind.Grenade; item.Item = 2; }
                    else if (r < 0.9) { item.Kind = LootKind.C4; item.Item = 1; }
                    else { item.Kind = LootKind.Armor; item.Item = 100; }
                    break;
                }
                case SpotKind.Supply: item.Kind = LootKind.Crate; break;
                case SpotKind.Military: item.Kind = LootKind.Military; break;
            }
            Loot.Add(item);
            spot.FreeAt = float.MaxValue;
            LootVersion++;
            worldDirty = true;
        }

        /// <summary>Host: a container opened by 'p': what it held lies round where it stood (taken one by one).</summary>
        private void OpenContainer(LootItem c, PlayerState p)
        {
            var things = new List<(LootKind kind, byte item)>();
            switch (c.Kind)
            {
                case LootKind.Crate:
                {
                    // a blue weapon most of the time; after two crates with nothing better than green, a sure one
                    double r = lootRng.NextDouble();
                    int tier = p.DryCrates >= 2 ? (r < 0.5 ? Tiers.Epic : Tiers.Rare) : r < 0.2 ? Tiers.Uncommon : r < 0.75 ? Tiers.Rare : Tiers.Epic;
                    p.DryCrates = tier >= Tiers.Rare ? 0 : p.DryCrates + 1;
                    things.Add((LootKind.Weapon, WeaponOf(tier)));
                    double b = lootRng.NextDouble();
                    if (b < 0.25) things.Add((LootKind.Armor, 100));
                    else if (b < 0.4) things.Add((LootKind.Armor, 50));
                    else if (b < 0.7) things.Add((LootKind.Grenade, 2));
                    else if (b < 0.9) things.Add((LootKind.C4, 2));
                    break;
                }
                case LootKind.Military:
                    things.Add((LootKind.Weapon, WeaponOf(lootRng.NextDouble() < 0.15 ? Tiers.Legendary : Tiers.Epic)));
                    things.Add(lootRng.NextDouble() < 0.6 ? (LootKind.Armor, (byte)100) : (LootKind.C4, (byte)2));
                    break;
                case LootKind.Airdrop:
                    things.Add((LootKind.Weapon, WeaponOf(Tiers.Legendary)));
                    things.Add((LootKind.Armor, 150));
                    things.Add(lootRng.NextDouble() < 0.5 ? (LootKind.Grenade, (byte)2) : (LootKind.C4, (byte)2));
                    break;
            }
            // laid out on the opener's side of the crate, side by side
            var toward = p.Pos - c.Pos;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.01f) toward = Vector3.forward;
            toward.Normalize();
            float now = Time.unscaledTime;
            for (int i = 0; i < things.Count; i++)
            {
                float angle = (i - (things.Count - 1) / 2f) * 50f;
                var at = c.Pos + Quaternion.Euler(0, angle, 0) * toward * (things.Count == 1 ? 0.35f : 0.8f);
                var g = Ground(at) ?? c.Pos;
                Loot.Add(new LootItem { Id = ++lootSeq, Kind = things[i].kind, Item = things[i].item, Pos = g, Until = now + ContentLife });
            }
            App.Log($"opened {c.Kind}: " + string.Join(", ", things.Select(t => t.kind == LootKind.Weapon ? Weapons.Get(t.item).Name : t.kind + " " + t.item)));
        }

        private void Drop(LootKind kind, byte item, Vector3 at, float life)
        {
            var g = Ground(at) ?? at;
            Loot.Add(new LootItem { Id = ++lootSeq, Kind = kind, Item = item, Pos = g, Until = Time.unscaledTime + life });
            // never more than MaxLoot things on the map: the oldest dropped ones go first
            while (Loot.Count > MaxLoot)
            {
                var oldest = Loot.Where(l => l.Spot < 0 && !Tiers.Container(l.Kind)).OrderBy(l => l.Until).FirstOrDefault();
                if (oldest == null) break;
                Loot.Remove(oldest);
            }
            LootVersion++;
            worldDirty = true;
        }

        /// <summary>Host, at a death in an ADVANCED match: the weapons (not the starting pistols), the vest and the charges stay where the body fell.</summary>
        private void DropOnDeath(PlayerState victim)
        {
            if (!Advanced) return;
            victim.DryDeaths++;
            var at = victim.Pos;
            int n = 0;
            foreach (var weapon in new[] { victim.Primary, victim.Secondary }.Distinct())
            {
                if (weapon == StartPistol || weapon == StartPistol2) continue;
                Drop(LootKind.Weapon, weapon, at + Quaternion.Euler(0, n++ * 120f, 0) * new Vector3(0.7f, 0f, 0f), DropLife);
            }
            if (victim.Armor >= 20) Drop(LootKind.Armor, victim.Armor, at + Quaternion.Euler(0, n++ * 120f, 0) * new Vector3(0.7f, 0f, 0f), DropLife);
            if (victim.C4 > 0) Drop(LootKind.C4, victim.C4, at + new Vector3(-0.5f, 0f, -0.5f), DropLife);
            victim.C4 = 0;
            victim.Armor = 0;
        }

        /// <summary>Host: the loadout of a new life in an ADVANCED match (two pistols, no vest, no charges; after two deaths without a kill, a sub-machine gun too).</summary>
        private void AdvancedRespawn(PlayerState p)
        {
            p.Armor = 0;
            p.C4 = 0;
            if (!Advanced) return;
            p.Primary = StartPistol;
            p.Secondary = p.DryDeaths >= 2 ? ComebackWeapon : StartPistol2;
            p.Weapon = p.Secondary == ComebackWeapon ? ComebackWeapon : StartPistol;
        }

        // ------------------------------------------------------------------ host: supply drops

        /// <summary>The first drop a fifth of the way in (35..75 s), then one every fifth of the match (75..150 s).</summary>
        private float FirstDrop => Mathf.Clamp(Config.Minutes * 60f * 0.18f, 35f, 75f);
        private float DropInterval => Mathf.Clamp(Config.Minutes * 60f / 5f, 75f, 150f);

        /// <summary>Host: one supply drop for up to 6 players, two up to 12, three for more, each where the sky is open, apart from the others.</summary>
        private void LaunchDrops()
        {
            var w = World.I != null ? World.I.Waypoints : null;
            if (w == null || w.Length == 0) return;
            int count = Players.Count <= 6 ? 1 : Players.Count <= 12 ? 2 : 3;
            var sp = World.I.Spawns;
            var chosen = new List<Vector3>();
            foreach (int i in Enumerable.Range(0, w.Length).OrderBy(_ => lootRng.Next()))
            {
                if (chosen.Count >= count) break;
                var g = Ground(w[i]);
                if (g == null) continue;
                var p = g.Value;
                if (sp.Any(s => (s - p).sqrMagnitude < 14f * 14f)) continue;
                if (Drops.Any(d => (d.Pos - p).sqrMagnitude < 30f * 30f) || chosen.Any(c => (c - p).sqrMagnitude < 30f * 30f)) continue;
                // open sky over it (the crate comes straight down), and a flat place round it
                if (Physics.SphereCast(p + Vector3.up * 1.5f, 0.8f, Vector3.up, out _, DropHeight + 20f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                bool flat = true;
                for (int k = 0; k < 4 && flat; k++)
                {
                    var q = Ground(p + Quaternion.Euler(0, k * 90f, 0) * Vector3.forward * 1.2f);
                    flat = q != null && Mathf.Abs(q.Value.y - p.y) < 0.35f;
                }
                if (!flat) continue;
                chosen.Add(p);
            }
            float now = Time.unscaledTime;
            foreach (var p in chosen)
            {
                var d = new AirDrop { Id = ++dropSeq, Pos = p, LandAt = now + DropWarning, Heading = lootRng.Next(360) };
                Drops.Add(d);
                DropAnnounced?.Invoke(d);
            }
            if (chosen.Count > 0) App.Log($"supply drop: {chosen.Count} coming down at " + string.Join(" ", chosen.Select(c => $"({c.x:0},{c.z:0})")));
            LootVersion++;
            worldDirty = true;
        }

        private void HostDropsTick(float now)
        {
            if (Phase != Phase.Playing) return;
            if (nextDropAt < 0f) nextDropAt = now + FirstDrop;
            else if (now >= nextDropAt)
            {
                nextDropAt = now + DropInterval;
                if (TimeLeft > DropWarning + 25f) LaunchDrops();
            }
            foreach (var d in Drops)
            {
                if (d.CrateId == 0 && now >= d.LandAt)
                {
                    var crate = new LootItem { Id = ++lootSeq, Kind = LootKind.Airdrop, Pos = d.Pos, Until = now + AirdropLife };
                    Loot.Add(crate);
                    d.CrateId = crate.Id;
                    LootVersion++;
                    worldDirty = true;
                }
                else if (d.CrateId != 0 && !d.Opened && !Loot.Any(l => l.Id == d.CrateId))
                {
                    d.Opened = true;
                    d.OpenedAt = now;
                    worldDirty = true;
                }
            }
            // the smoke goes on a little after the crate was opened (or left too long), then the drop is gone
            if (Drops.RemoveAll(d => d.Opened && now > d.OpenedAt + 12f) > 0) { LootVersion++; worldDirty = true; }
        }

        /// <summary>Host (auto test): a supply drop now, near a point.</summary>
        public void DropNow(Vector3 near)
        {
            if (!IsHost || !Advanced) return;
            nextDropAt = Time.unscaledTime + DropInterval;
            var w = World.I.Waypoints;
            foreach (int i in Enumerable.Range(0, w.Length).OrderBy(i => (w[i] - near).sqrMagnitude))
            {
                var g = Ground(w[i]);
                if (g == null || (w[i] - near).sqrMagnitude < 6f * 6f) continue;
                if (Physics.SphereCast(g.Value + Vector3.up * 1.5f, 0.8f, Vector3.up, out _, DropHeight + 20f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                var d = new AirDrop { Id = ++dropSeq, Pos = g.Value, LandAt = Time.unscaledTime + DropWarning, Heading = lootRng.Next(360) };
                Drops.Add(d);
                DropAnnounced?.Invoke(d);
                LootVersion++;
                worldDirty = true;
                return;
            }
        }

        private void HostLootTick(float now)
        {
            if (!Advanced) return;
            // dropped things go after their while; the map's places get a new thing a while after they were emptied
            int before = Loot.Count;
            Loot.RemoveAll(l => now > l.Until);
            if (Loot.Count != before) { LootVersion++; worldDirty = true; }
            for (int i = 0; i < spots.Count; i++)
                if (spots[i].FreeAt <= now) Restock(i);
            HostDropsTick(now);
            // drums: the ones hit hard enough (or caught by a blast) go off, the gone ones come back
            foreach (var b in Barrels)
            {
                if (b.Alive && b.BlowAt >= 0f && now >= b.BlowAt) BlowBarrel(b);
                else if (!b.Alive && now >= b.RespawnAt && !Players.Values.Any(p => p.Alive && (p.Pos - b.Pos).sqrMagnitude < 2.5f))
                {
                    b.Alive = true;
                    b.Health = BarrelHealth;
                    b.BlowAt = -1f;
                    b.Hitter = NoPlayer;
                    LootVersion++;
                    worldDirty = true;
                }
            }
            if (socket != null && (worldTimer -= Time.unscaledDeltaTime) <= 0f)
            {
                worldTimer = worldDirty ? 0.1f : 1f;
                if (worldDirty || worldTimer >= 1f) SendWorld();
                worldDirty = false;
            }
        }

        /// <summary>The place a thing came from gets a new one after a while: caches 35 s, supply crates 50 s, hot spots 80 s.</summary>
        private void FreeSpot(LootItem item)
        {
            if (item.Spot < 0 || item.Spot >= spots.Count) return;
            var kind = spots[item.Spot].Kind;
            spots[item.Spot].FreeAt = Time.unscaledTime + (kind == SpotKind.Military ? 80f : kind == SpotKind.Supply ? 50f : 35f);
        }

        /// <summary>Host: a player (or a bot) takes something lying next to them, or opens the container there.</summary>
        public bool HostPickup(PlayerState p, ushort lootId)
        {
            if (!Advanced || Phase != Phase.Playing || p == null || !p.Alive) return false;
            var item = Loot.FirstOrDefault(l => l.Id == lootId);
            if (item == null) return false;
            var d = item.Pos - p.Pos;
            if (new Vector2(d.x, d.z).magnitude > PickReach + 1f || Mathf.Abs(d.y) > 2.4f) return false;
            switch (item.Kind)
            {
                case LootKind.Crate:
                case LootKind.Military:
                case LootKind.Airdrop:
                    Loot.Remove(item);
                    FreeSpot(item);
                    OpenContainer(item, p);
                    break;
                case LootKind.Weapon:
                {
                    // the weapon in the hand is put down, the new one taken in its place
                    bool second = p.Weapon == p.Secondary && p.Secondary != p.Primary;
                    byte old = second ? p.Secondary : p.Primary;
                    if (second) p.Secondary = item.Item; else p.Primary = item.Item;
                    p.Weapon = item.Item;
                    Loot.Remove(item);
                    FreeSpot(item);
                    if (old != StartPistol && old != StartPistol2)
                        Drop(LootKind.Weapon, old, p.Pos + Quaternion.Euler(0, p.Yaw + 90f, 0) * Vector3.forward * 0.6f, DropLife);
                    p.PickSeq++;
                    p.LastPicked = item.Item;
                    break;
                }
                case LootKind.Armor:
                    if (p.Armor >= item.Item) return false;
                    // the old vest is left there (someone may want it)
                    if (p.Armor >= 20) Drop(LootKind.Armor, p.Armor, p.Pos + Quaternion.Euler(0, p.Yaw - 90f, 0) * Vector3.forward * 0.6f, DropLife);
                    p.Armor = item.Item;
                    Loot.Remove(item);
                    FreeSpot(item);
                    break;
                case LootKind.C4:
                    if (p.C4 >= MaxC4) return false;
                    p.C4 = (byte)Mathf.Min(MaxC4, p.C4 + item.Item);
                    Loot.Remove(item);
                    FreeSpot(item);
                    break;
                case LootKind.Grenade:
                    // the player's own machine counts the grenades: told by the pick count
                    p.PickSeq++;
                    p.LastPicked = Weapons.Grenade.Id;
                    Loot.Remove(item);
                    FreeSpot(item);
                    break;
            }
            LootVersion++;
            worldDirty = true;
            return true;
        }

        private void HostAction(PlayerState p, ActionRequest a)
        {
            switch (a.Type)
            {
                case 1: HostPickup(p, a.Id); break;
                case 2: HostPlant(p, a.A, a.B); break;
                case 3: HostDetonate(p); break;
            }
        }

        private void HostPlant(PlayerState p, Vector3 at, Vector3 normal)
        {
            if (!Advanced || Phase != Phase.Playing || !p.Alive || p.C4 == 0) return;
            if ((at - p.Pos).sqrMagnitude > 5f * 5f || Charges.Count(c => c.Owner == p.Id) >= MaxCharges) return;
            p.C4--;
            Charges.Add(new Charge { Id = ++chargeSeq, Owner = p.Id, Pos = at, Normal = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up });
            LootVersion++;
            worldDirty = true;
        }

        private void HostDetonate(PlayerState p)
        {
            if (Phase != Phase.Playing || !p.Alive) return;
            foreach (var c in Charges.Where(c => c.Owner == p.Id).ToList())
            {
                Charges.Remove(c);
                Blast(c.Pos + c.Normal * 0.15f, p, Weapons.C4, C4Radius, C4Lethal, 3);
            }
            LootVersion++;
            worldDirty = true;
        }

        /// <summary>Host: a shot (or a pellet) that hit something solid: a drum there takes the damage.</summary>
        private void ShotAtBarrel(Vector3 point, PlayerState shooter, WeaponDef def)
        {
            foreach (var b in Barrels)
            {
                if (!b.Alive || b.BlowAt >= 0f) continue;
                var d = point - b.Pos;
                if (new Vector2(d.x, d.z).sqrMagnitude > 0.62f * 0.62f || d.y < -0.2f || d.y > 1.25f) continue;
                b.Health -= def.Damage;
                b.Hitter = shooter.Id;
                if (b.Health <= 0f) b.BlowAt = Time.unscaledTime;
                return;
            }
        }

        private void BlowBarrel(BarrelState b)
        {
            b.Alive = false;
            b.BlowAt = -1f;
            b.RespawnAt = Time.unscaledTime + BarrelRespawn;
            Players.TryGetValue(b.Hitter, out var by);
            Blast(b.Pos + Vector3.up * 0.6f, by, Weapons.Barrel, BarrelRadius, BarrelLethal, 2);
            LootVersion++;
            worldDirty = true;
        }

        /// <summary>
        /// Host: a big blast (a drum, a charge). Everything closer than 'lethal' is killed, then the damage falls
        /// off to nothing at 'radius'; walls protect beyond 70% of 'lethal'; drums near it go off a moment later.
        /// </summary>
        private void Blast(Vector3 pos, PlayerState by, WeaponDef weapon, float radius, float lethal, byte kind)
        {
            AddNade(new NadeEvent { Thrower = by != null ? by.Id : NoPlayer, Kind = kind, A = pos });
            Effects.BigExplosion(pos, kind);
            foreach (var b in Barrels)
                if (b.Alive && b.BlowAt < 0f && (b.Pos - pos).sqrMagnitude < 5.5f * 5.5f)
                {
                    b.BlowAt = Time.unscaledTime + Random.Range(0.18f, 0.4f);
                    if (by != null) b.Hitter = by.Id;
                }
            if (Phase != Phase.Playing) return;
            foreach (var victim in Players.Values.ToList())
            {
                if (!victim.Alive) continue;
                var body = victim.Pos + Vector3.up * (victim.Prone ? 0.3f : victim.Crouch ? 0.8f : 1.1f);
                var head = victim.Pos + Vector3.up * (victim.Prone ? 0.4f : victim.Crouch ? 1.15f : 1.6f);
                float dist = Vector3.Distance(pos, body);
                if (dist >= radius) continue;
                float amount = dist <= lethal ? 220f : Mathf.Lerp(110f, 10f, (dist - lethal) / (radius - lethal));
                bool exposed = !Physics.Linecast(pos, body, World.EnvMask, QueryTriggerInteraction.Ignore) || !Physics.Linecast(pos, head, World.EnvMask, QueryTriggerInteraction.Ignore);
                if (!exposed && dist > lethal * 0.7f) continue;
                if (victim.Prone) amount *= 0.75f;
                if (victim == by) amount *= 0.7f;
                Damage(by ?? victim, victim, weapon, false, amount, true);
            }
        }

        /// <summary>Host: a grenade's blast sets off the drums close to it.</summary>
        private void GrenadeNearBarrels(Vector3 pos, byte thrower)
        {
            foreach (var b in Barrels)
                if (b.Alive && b.BlowAt < 0f && (b.Pos - pos).sqrMagnitude < 4.5f * 4.5f)
                {
                    b.BlowAt = Time.unscaledTime + 0.15f;
                    b.Hitter = thrower;
                }
        }

        // ------------------------------------------------------------------ network

        private void SendWorld()
        {
            var w = pw.Begin(Msg.WorldState);
            w.Write((ushort)LootVersion);
            int n = Mathf.Min(Loot.Count, MaxLoot);
            w.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                var l = Loot[i];
                w.Write(l.Id); w.Write((byte)l.Kind); w.Write(l.Item); w.Write(l.Pos);
            }
            w.Write((byte)Barrels.Count);
            foreach (var b in Barrels) { w.Write(b.Pos); w.Write(b.Alive); }
            w.Write((byte)Charges.Count);
            foreach (var c in Charges)
            {
                w.Write(c.Id); w.Write(c.Owner); w.Write(c.Pos);
                w.Write((sbyte)Mathf.RoundToInt(c.Normal.x * 100)); w.Write((sbyte)Mathf.RoundToInt(c.Normal.y * 100)); w.Write((sbyte)Mathf.RoundToInt(c.Normal.z * 100));
            }
            // the supply drops: where, how long until they land (each machine counts on its own clock), the plane's course
            float now = Time.unscaledTime;
            w.Write((byte)Drops.Count);
            foreach (var d in Drops)
            {
                w.Write(d.Id); w.Write(d.Pos); w.Write(d.LandAt - now);
                w.Write((byte)Mathf.RoundToInt(Mathf.Repeat(d.Heading, 360f) / 360f * 255f)); w.Write(d.Opened);
            }
            SendToClients();
        }

        private void ClientOnWorld(BinaryReader r)
        {
            r.ReadUInt16();
            Loot.Clear();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
                Loot.Add(new LootItem { Id = r.ReadUInt16(), Kind = (LootKind)r.ReadByte(), Item = r.ReadByte(), Pos = r.ReadVector3() });
            int nb = r.ReadByte();
            while (Barrels.Count > nb) Barrels.RemoveAt(Barrels.Count - 1);
            for (int i = 0; i < nb; i++)
            {
                if (i >= Barrels.Count) Barrels.Add(new BarrelState());
                Barrels[i].Pos = r.ReadVector3();
                Barrels[i].Alive = r.ReadBoolean();
            }
            Charges.Clear();
            int nc = r.ReadByte();
            for (int i = 0; i < nc; i++)
            {
                var c = new Charge { Id = r.ReadUInt16(), Owner = r.ReadByte(), Pos = r.ReadVector3() };
                c.Normal = new Vector3(r.ReadSByte(), r.ReadSByte(), r.ReadSByte()) / 100f;
                Charges.Add(c);
            }
            if (r.BaseStream.Position < r.BaseStream.Length)
            {
                float now = Time.unscaledTime;
                var seen = new HashSet<ushort>();
                int nd = r.ReadByte();
                for (int i = 0; i < nd; i++)
                {
                    ushort id = r.ReadUInt16();
                    var pos = r.ReadVector3();
                    float left = r.ReadSingle();
                    float heading = r.ReadByte() / 255f * 360f;
                    bool opened = r.ReadBoolean();
                    seen.Add(id);
                    var d = Drops.FirstOrDefault(x => x.Id == id);
                    bool fresh = d == null;
                    if (fresh) { d = new AirDrop { Id = id }; Drops.Add(d); }
                    // the landing time on this clock: only moved when it drifted (no jitter from packet to packet)
                    if (fresh || Mathf.Abs(d.LandAt - (now + left)) > 0.35f) d.LandAt = now + left;
                    d.Pos = pos;
                    d.Heading = heading;
                    d.Opened = opened;
                    if (fresh && left > 0f) DropAnnounced?.Invoke(d);
                }
                Drops.RemoveAll(x => !seen.Contains(x.Id));
            }
            LootVersion++;
        }

        /// <summary>The local player asks the host: pick up / open (1, id), plant a charge (2, where, the surface's normal), set off (3).</summary>
        public void ActionLocal(byte type, ushort id = 0, Vector3 a = default, Vector3 b = default)
        {
            var req = new ActionRequest { Seq = ++actionSeq, Type = type, Id = id, A = a, B = b };
            if (IsHost) { if (Local != null) HostAction(Local, req); }
            else pendingActions.Add((req, Time.unscaledTime));
        }

        /// <summary>Host: a bot takes the thing next to it (or opens it).</summary>
        public bool BotPickup(byte bot, ushort lootId) => IsHost && Players.TryGetValue(bot, out var p) && HostPickup(p, lootId);

        private void WriteActions(BinaryWriter w, float now)
        {
            pendingActions.RemoveAll(x => now - x.time > 1f);
            w.Write((byte)pendingActions.Count);
            foreach (var (a, _) in pendingActions)
            {
                w.Write(a.Seq); w.Write(a.Type); w.Write(a.Id); w.Write(a.A); w.Write(a.B);
            }
        }

        private void ReadActions(BinaryReader r, PlayerState p)
        {
            if (r.BaseStream.Position >= r.BaseStream.Length) return;
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                var a = new ActionRequest { Seq = r.ReadInt32(), Type = r.ReadByte(), Id = r.ReadUInt16(), A = r.ReadVector3(), B = r.ReadVector3() };
                if (a.Seq <= p.LastActionSeq) continue;
                p.LastActionSeq = a.Seq;
                HostAction(p, a);
            }
        }
    }
}
