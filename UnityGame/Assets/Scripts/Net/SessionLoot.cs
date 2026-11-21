using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UA
{
    /// <summary>What lies on the ground in an ADVANCED match: a weapon, a vest, C4 charges, or a closed crate.</summary>
    public enum LootKind : byte { Weapon, Armor, C4, Crate }

    public class LootItem
    {
        public ushort Id;
        public LootKind Kind;
        public byte Item;          // weapon id, armour points, number of charges (a crate: nothing)
        public Vector3 Pos;
        // host only
        public float Until = float.MaxValue;   // a dropped thing goes after a while; the map's own stay
        public int Spot = -1;                  // the place on the map it belongs to (comes back there when taken)
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

    /// <summary>A player's request to the host: pick something up (1), plant a charge (2), set off one's charges (3).</summary>
    public struct ActionRequest { public int Seq; public byte Type; public ushort Id; public Vector3 A, B; }

    /// <summary>
    /// The ADVANCED game: everyone starts with two pistols; weapons lie on the map and in crates, vests and
    /// C4 charges too; whoever dies leaves their weapons where they fell for a minute; yellow fuel drums
    /// explode when shot. The host decides everything (what lies where, who took it, what blew up) and
    /// sends it to the others several times a second; the players only ask.
    /// </summary>
    public partial class Session
    {
        public const byte StartPistol = 4, StartPistol2 = 5;           // GLOCK 17 and DESERT EAGLE
        public const float BarrelHealth = 40f, BarrelRadius = 7.5f, BarrelLethal = 3.5f, BarrelRespawn = 50f;
        public const float C4Radius = 8.5f, C4Lethal = 4.2f;
        public const int MaxC4 = 3, MaxCharges = 3;
        /// <summary>A vest takes this share of every hit on the body (not the head) out of its own points instead of the health.</summary>
        public const float ArmorShare = 0.4f;
        public const float PickReach = 2.6f;
        private const float DropLife = 60f, SpotComeBack = 40f;

        public readonly List<LootItem> Loot = new List<LootItem>();
        public readonly List<BarrelState> Barrels = new List<BarrelState>();
        public readonly List<Charge> Charges = new List<Charge>();
        /// <summary>Goes up whenever what lies on the map changes (the views and the bots follow it).</summary>
        public int LootVersion;

        public bool Advanced => Config.Kind == 1;

        private ushort lootSeq, chargeSeq;
        private readonly List<(Vector3 pos, LootKind kind)> spots = new List<(Vector3, LootKind)>();
        private readonly List<float> spotFreeAt = new List<float>();
        private bool worldDirty;
        private float worldTimer;
        private readonly List<(ActionRequest a, float time)> pendingActions = new List<(ActionRequest, float)>();
        private int actionSeq;

        // weapons on the ground (common ones more often) and in crates (the rare ones)
        private static readonly (byte id, int weight)[] GroundWeapons =
            { (0, 14), (1, 14), (9, 12), (10, 12), (6, 12), (2, 10), (7, 6), (8, 5), (11, 5), (3, 4) };
        private static readonly (byte id, int weight)[] CrateWeapons =
            { (7, 10), (8, 9), (3, 8), (11, 8), (9, 6), (1, 5), (0, 5) };

        private static byte Pick((byte id, int weight)[] table, System.Random rng)
        {
            int total = table.Sum(t => t.weight), roll = rng.Next(total);
            foreach (var t in table) { if (roll < t.weight) return t.id; roll -= t.weight; }
            return table[0].id;
        }

        private readonly System.Random lootRng = new System.Random();

        // ------------------------------------------------------------------ host: the map's loot

        private void ClearLoot()
        {
            Loot.Clear();
            Barrels.Clear();
            Charges.Clear();
            spots.Clear();
            spotFreeAt.Clear();
            pendingActions.Clear();
            LootVersion++;
            worldDirty = true;
        }

        /// <summary>Host, at the countdown of an ADVANCED match: places, drums and the first things on them.</summary>
        private void SetupLoot()
        {
            ClearLoot();
            if (!Advanced || World.I == null) return;
            var w = World.I.Waypoints;
            var sp = World.I.Spawns;
            if (w.Length == 0) return;
            var order = Enumerable.Range(0, w.Length).OrderBy(_ => lootRng.Next()).ToList();
            int weapons = Mathf.Clamp(w.Length / 5, 10, 24), crates = 5, vests = 5, drums = 10;
            var taken = new List<Vector3>();
            bool Free(Vector3 p, float apart) => taken.All(t => (t - p).sqrMagnitude > apart * apart) && sp.All(s => (s - p).sqrMagnitude > 16f);
            void Place(LootKind kind, int count)
            {
                foreach (int i in order)
                {
                    if (count <= 0) return;
                    var p = Ground(w[i] + new Vector3((float)lootRng.NextDouble() - 0.5f, 0f, (float)lootRng.NextDouble() - 0.5f));
                    if (p == null || !Free(p.Value, 9f)) continue;
                    taken.Add(p.Value);
                    spots.Add((p.Value, kind));
                    spotFreeAt.Add(0f);
                    count--;
                }
            }
            Place(LootKind.Crate, crates);
            Place(LootKind.Armor, vests);
            Place(LootKind.Weapon, weapons);
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
            App.Log($"loot: {spots.Count} places, {Barrels.Count} drums");
            LootVersion++;
            worldDirty = true;
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
        private void Restock(int spot)
        {
            var (pos, kind) = spots[spot];
            var item = new LootItem { Id = ++lootSeq, Kind = kind, Pos = pos, Spot = spot };
            switch (kind)
            {
                case LootKind.Weapon: item.Item = Pick(GroundWeapons, lootRng); break;
                case LootKind.Armor: item.Item = (byte)(lootRng.NextDouble() < 0.35 ? 100 : 50); break;
                case LootKind.Crate: break;
            }
            Loot.Add(item);
            spotFreeAt[spot] = float.MaxValue;
            LootVersion++;
            worldDirty = true;
        }

        /// <summary>What a crate holds, decided when it is opened.</summary>
        private LootItem CrateContent(LootItem crate)
        {
            var c = new LootItem { Id = ++lootSeq, Pos = crate.Pos, Spot = crate.Spot };
            double roll = lootRng.NextDouble();
            if (roll < 0.42) { c.Kind = LootKind.Weapon; c.Item = Pick(CrateWeapons, lootRng); }
            else if (roll < 0.66) { c.Kind = LootKind.Armor; c.Item = 100; }
            else if (roll < 0.86) { c.Kind = LootKind.C4; c.Item = 2; }
            else { c.Kind = LootKind.Armor; c.Item = 50; }
            return c;
        }

        private void Drop(LootKind kind, byte item, Vector3 at, float life)
        {
            var g = Ground(at) ?? at;
            Loot.Add(new LootItem { Id = ++lootSeq, Kind = kind, Item = item, Pos = g, Until = Time.unscaledTime + life });
            // never more than 48 things on the map: the oldest dropped ones go first
            while (Loot.Count > 48)
            {
                var oldest = Loot.Where(l => l.Spot < 0).OrderBy(l => l.Until).FirstOrDefault();
                if (oldest == null) break;
                Loot.Remove(oldest);
            }
            LootVersion++;
            worldDirty = true;
        }

        /// <summary>Host, at a death in an ADVANCED match: the weapons (not the two starting pistols) and the charges stay where the body fell.</summary>
        private void DropOnDeath(PlayerState victim)
        {
            if (!Advanced) return;
            var at = victim.Pos;
            int n = 0;
            foreach (var weapon in new[] { victim.Primary, victim.Secondary }.Distinct())
            {
                if (weapon == StartPistol || weapon == StartPistol2) continue;
                Drop(LootKind.Weapon, weapon, at + Quaternion.Euler(0, n++ * 120f, 0) * new Vector3(0.7f, 0f, 0f), DropLife);
            }
            if (victim.C4 > 0) Drop(LootKind.C4, victim.C4, at + new Vector3(-0.5f, 0f, -0.5f), DropLife);
            victim.C4 = 0;
            victim.Armor = 0;
        }

        /// <summary>Host: the loadout of a new life in an ADVANCED match (two pistols, no vest, no charges).</summary>
        private void AdvancedRespawn(PlayerState p)
        {
            p.Armor = 0;
            p.C4 = 0;
            if (!Advanced) return;
            p.Primary = StartPistol;
            p.Secondary = StartPistol2;
            p.Weapon = StartPistol;
        }

        private void HostLootTick(float now)
        {
            if (!Advanced) return;
            // dropped things go after their minute; the map's places get a new thing a while after they were emptied
            int before = Loot.Count;
            Loot.RemoveAll(l => now > l.Until);
            if (Loot.Count != before) { LootVersion++; worldDirty = true; }
            for (int i = 0; i < spots.Count; i++)
                if (spotFreeAt[i] <= now) Restock(i);
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

        private void FreeSpot(LootItem item)
        {
            if (item.Spot >= 0 && item.Spot < spotFreeAt.Count) spotFreeAt[item.Spot] = Time.unscaledTime + SpotComeBack;
        }

        /// <summary>Host: a player (or a bot) takes something lying next to them.</summary>
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
                {
                    // opened: what was in it lies in its place (taken with a second press)
                    Loot.Remove(item);
                    var content = CrateContent(item);
                    Loot.Add(content);
                    break;
                }
                case LootKind.Weapon:
                {
                    // the weapon in the hand is put down, the new one taken in its place
                    bool second = p.Weapon == p.Secondary && p.Secondary != p.Primary;
                    byte old = second ? p.Secondary : p.Primary;
                    if (second) p.Secondary = item.Item; else p.Primary = item.Item;
                    p.Weapon = item.Item;
                    Loot.Remove(item);
                    FreeSpot(item);
                    Drop(LootKind.Weapon, old, p.Pos + Quaternion.Euler(0, p.Yaw + 90f, 0) * Vector3.forward * 0.6f, DropLife);
                    p.PickSeq++;
                    p.LastPicked = item.Item;
                    break;
                }
                case LootKind.Armor:
                    if (p.Armor >= item.Item) return false;
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
            int n = Mathf.Min(Loot.Count, 60);
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
            LootVersion++;
        }

        /// <summary>The local player asks the host: pick up (1, id), plant a charge (2, where, the surface's normal), set off (3).</summary>
        public void ActionLocal(byte type, ushort id = 0, Vector3 a = default, Vector3 b = default)
        {
            var req = new ActionRequest { Seq = ++actionSeq, Type = type, Id = id, A = a, B = b };
            if (IsHost) { if (Local != null) HostAction(Local, req); }
            else pendingActions.Add((req, Time.unscaledTime));
        }

        /// <summary>Host: a bot takes the thing next to it.</summary>
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
