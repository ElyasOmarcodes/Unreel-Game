using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using UnityEngine;

namespace UA
{
    public enum Phase : byte { Lobby, Countdown, Playing, Ended }
    public enum Mode : byte { TeamDeathmatch, FreeForAll }

    [Serializable]
    public class MatchConfig
    {
        public string GroupName = "Group";
        public Mode Mode = Mode.TeamDeathmatch;
        public byte Map;
        public byte Minutes = 5;
        public byte MaxPlayers = 8;
        public byte Bots = 2;
        public byte BotSkill = 1;

        public static readonly string[] MapNames = { "DOWNTOWN - DAY", "DOWNTOWN - SUNSET", "DOWNTOWN - NIGHT" };
        public static readonly string[] ModeNames = { "TEAM DEATHMATCH", "FREE FOR ALL" };
        public static readonly string[] SkillNames = { "EASY", "NORMAL", "HARD" };

        public int KillLimit => Mode == Mode.FreeForAll ? 25 : 50;

        public void Write(BinaryWriter w)
        {
            w.Write(GroupName); w.Write((byte)Mode); w.Write(Map); w.Write(Minutes); w.Write(MaxPlayers); w.Write(Bots); w.Write(BotSkill);
        }

        public void Read(BinaryReader r)
        {
            GroupName = r.ReadString(); Mode = (Mode)r.ReadByte(); Map = r.ReadByte(); Minutes = r.ReadByte();
            MaxPlayers = r.ReadByte(); Bots = r.ReadByte(); BotSkill = r.ReadByte();
        }
    }

    public class PlayerState
    {
        public byte Id;
        public string Name = "";
        public byte Team;
        public bool IsBot;
        public byte Primary, Secondary = 4;
        public bool Alive;
        public float Health = 100;
        public byte Weapon;
        public Vector3 Pos;
        public float Yaw, Pitch, MoveX, MoveY;
        public bool Crouch, Grounded = true, Aiming;
        public short Kills, Deaths;
        public byte SpawnSeq;
        public ushort Hits;

        // host side only
        public IPEndPoint EndPoint;
        public int Token;
        public float LastSeen;
        public float RespawnAt;
        public int LastShotSeq;
        public float LastDamageTime;
    }

    public struct KillEvent { public ushort Seq; public byte Killer, Victim, Weapon; public bool Headshot; }
    public struct Shot { public int Seq; public byte Shooter, Weapon; public Vector3 Origin, Dir; public int Seed; }
    public struct ShotFx { public ushort Seq; public byte Shooter, Weapon; public Vector3 Origin, End; public byte HitKind; } // 0 wall, 1 player, 2 nothing

    /// <summary>
    /// LAN session: the host owns the match (lobby, health, kills, respawns, bots) and sends lobby and
    /// snapshot packets; clients own their own movement and send it with their shots. Plain UDP, no
    /// internet needed: works on a shared Wi-Fi or on one phone's hotspot.
    /// </summary>
    public class Session : MonoBehaviour
    {
        public static Session I;
        public const int GamePort = 7777;
        public const int DiscoveryPort = 47777;
        public const byte Version = 1;
        public const byte NoPlayer = 255;

        public bool Active, IsHost, Practice;
        public MatchConfig Config = new MatchConfig();
        public Phase Phase;
        public float TimeLeft;
        public readonly int[] TeamScore = new int[2];
        public readonly Dictionary<byte, PlayerState> Players = new Dictionary<byte, PlayerState>();
        public byte LocalId;
        public readonly List<KillEvent> KillFeed = new List<KillEvent>();
        public readonly Dictionary<string, GroupInfo> Groups = new Dictionary<string, GroupInfo>();
        public string Status = "";
        public string HostAddresses = "";
        public int HostPort = GamePort;
        public bool Paused; // practice only

        public event Action<KillEvent> KillHappened;
        public event Action<ShotFx> ShotSeen;
        public event Action<string> Disconnected;
        public event Action<Phase> PhaseChanged;

        private NetSocket socket, discovery;
        private readonly PacketWriter pw = new PacketWriter();
        private IPEndPoint hostEndPoint;
        private float lastHostPacket, joinTimer, joinDeadline, lobbyTimer, snapshotTimer, announceTimer, inputTimer, queryTimer;
        private float phaseEnd;
        private int token;
        private bool joined;
        private ushort killSeq, fxSeq, lastKillSeen, lastFxSeen;
        private bool killInit, fxInit;
        private readonly List<ShotFx> fxRing = new List<ShotFx>();
        private readonly List<(Shot shot, float time)> pendingShots = new List<(Shot, float)>();
        private int shotSeq;

        private static readonly string[] BotNames = { "Viper", "Ghost", "Falcon", "Rook", "Blaze", "Shadow", "Hunter", "Nova", "Raven", "Titan" };

        public PlayerState Local => Players.TryGetValue(LocalId, out var p) ? p : null;
        public bool InMatch => Active && Phase != Phase.Lobby;

        private void Awake() { I = this; }

        private void OnApplicationQuit() => Stop();

        // ------------------------------------------------------------------ host / join / leave

        public bool Host(MatchConfig config, bool practice)
        {
            Stop();
            Config = config;
            IsHost = true;
            Practice = practice;
            if (!practice)
            {
                for (int port = GamePort; port < GamePort + 10 && socket == null; port++)
                {
                    try { socket = new NetSocket(port, false); HostPort = port; }
                    catch (Exception e) { App.Log("port " + port + " busy: " + e.Message); }
                }
                if (socket == null) { Status = "Could not open the network port"; return false; }
            }
            Active = true;
            Players.Clear();
            Players[0] = new PlayerState { Id = 0, Name = Settings.PlayerName, Team = 0, Primary = Settings.Primary, Secondary = Settings.Secondary };
            LocalId = 0;
            SyncBots();
            HostAddresses = string.Join("   ", LanUtil.LocalAddresses());
            if (HostPort != GamePort) HostAddresses += "  (port " + HostPort + ")";
            SetPhase(Phase.Lobby);
            App.Log($"host-started practice={practice} port={HostPort} ip={HostAddresses}");
            return true;
        }

        public void Join(IPEndPoint host)
        {
            Stop();
            IsHost = false;
            Practice = false;
            hostEndPoint = host;
            try { socket = new NetSocket(0, false); }
            catch (Exception e) { Status = "Network error: " + e.Message; return; }
            Active = true;
            token = UnityEngine.Random.Range(1, int.MaxValue);
            joined = false;
            joinTimer = 0;
            joinDeadline = Time.unscaledTime + 10f;
            Players.Clear();
            Phase = Phase.Lobby;
            Status = "Connecting to " + host.Address + " ...";
            App.Log("joining " + host);
        }

        public void Leave() => Stop();

        private void Stop()
        {
            if (socket != null)
            {
                if (IsHost)
                {
                    var w = pw.Begin(Msg.Kicked);
                    w.Write("The host closed the group");
                    for (int i = 0; i < 3; i++) SendToClients();
                }
                else if (joined)
                {
                    var w = pw.Begin(Msg.Leave);
                    w.Write(LocalId); w.Write(token);
                    for (int i = 0; i < 3; i++) socket.Send(pw.Buffer, pw.Length, hostEndPoint);
                }
                socket.Dispose();
                socket = null;
            }
            Active = false;
            joined = false;
            Paused = false;
            Players.Clear();
            KillFeed.Clear();
            fxRing.Clear();
            pendingShots.Clear();
            killInit = fxInit = false;
            TeamScore[0] = TeamScore[1] = 0;
            Phase = Phase.Lobby;
            Time.timeScale = 1f;
        }

        private void Disconnect(string reason)
        {
            App.Log("disconnected: " + reason);
            Stop();
            Disconnected?.Invoke(reason);
        }

        // ------------------------------------------------------------------ discovery

        public void StartDiscovery()
        {
            StopDiscovery();
            try { discovery = new NetSocket(DiscoveryPort, true); }
            catch (Exception) { discovery = new NetSocket(0, false); }
            Groups.Clear();
            queryTimer = 0;
        }

        public void StopDiscovery()
        {
            discovery?.Dispose();
            discovery = null;
        }

        private void PollDiscovery(float now)
        {
            while (discovery != null && discovery.Receive(out var r, out var from))
            {
                try
                {
                    if ((Msg)r.ReadByte() != Msg.Announce) continue;
                    var g = GroupInfo.Read(r);
                    g.Address = new IPEndPoint(from.Address, g.Port);
                    g.LastSeen = now;
                    bool isNew = !Groups.ContainsKey(g.Address.ToString());
                    Groups[g.Address.ToString()] = g;
                    if (isNew) App.Log("group-found " + g.Name + " at " + g.Address);
                }
                catch (Exception) { }
            }
            if ((queryTimer -= Time.unscaledDeltaTime) <= 0)
            {
                queryTimer = 1f;
                var w = pw.Begin(Msg.Query);
                w.Write(Version);
                foreach (var target in LanUtil.BroadcastTargets(GamePort)) discovery?.Send(pw.Buffer, pw.Length, target);
            }
            foreach (var key in Groups.Where(g => now - g.Value.LastSeen > 4f).Select(g => g.Key).ToList())
                Groups.Remove(key);
        }

        private void Announce(IPEndPoint to)
        {
            var info = new GroupInfo
            {
                Name = Config.GroupName, HostName = Settings.PlayerName, Port = HostPort, Mode = Config.Mode, Map = Config.Map,
                Players = (byte)Players.Values.Count(p => !p.IsBot), MaxPlayers = Config.MaxPlayers, Phase = Phase,
            };
            var w = pw.Begin(Msg.Announce);
            info.Write(w);
            if (to != null) socket.Send(pw.Buffer, pw.Length, to);
            else foreach (var target in LanUtil.BroadcastTargets(DiscoveryPort)) socket.Send(pw.Buffer, pw.Length, target);
        }

        // ------------------------------------------------------------------ main loop

        private void Update()
        {
            float now = Time.unscaledTime;
            if (discovery != null) PollDiscovery(now);
            if (!Active) return;
            while (socket != null && socket.Receive(out var reader, out var from))
            {
                try { Handle(reader, from, now); }
                catch (Exception e) { App.Log("bad packet from " + from + ": " + e.Message); }
            }
            if (!Active) return;
            if (IsHost) HostTick(now);
            else ClientTick(now);
        }

        private void Handle(BinaryReader r, IPEndPoint from, float now)
        {
            var type = (Msg)r.ReadByte();
            if (IsHost)
            {
                switch (type)
                {
                    case Msg.Query: if (!Practice) Announce(from); break;
                    case Msg.Join: HostOnJoin(r, from, now); break;
                    case Msg.Input: HostOnInput(r, from, now); break;
                    case Msg.Leave:
                    {
                        byte id = r.ReadByte();
                        int tok = r.ReadInt32();
                        if (id != 0 && Players.TryGetValue(id, out var p) && !p.IsBot && p.Token == tok)
                        {
                            Players.Remove(id);
                            App.Log("client-left id=" + id);
                        }
                        break;
                    }
                }
                return;
            }
            if (!from.Address.Equals(hostEndPoint.Address) || from.Port != hostEndPoint.Port) return;
            lastHostPacket = now;
            switch (type)
            {
                case Msg.Welcome:
                    if (r.ReadInt32() == token && !joined)
                    {
                        LocalId = r.ReadByte();
                        joined = true;
                        Status = "";
                        App.Log("joined id=" + LocalId);
                    }
                    break;
                case Msg.Lobby: if (joined) ClientOnLobby(r); break;
                case Msg.Snapshot: if (joined) ClientOnSnapshot(r); break;
                case Msg.Kicked: Disconnect(r.ReadString()); break;
            }
        }

        // ------------------------------------------------------------------ host

        private void HostTick(float now)
        {
            float dt = Time.unscaledDeltaTime;
            if (Paused) { phaseEnd += dt; return; }
            foreach (var p in Players.Values.Where(p => !p.IsBot && p.Id != 0 && now - p.LastSeen > 6f).ToList())
            {
                Players.Remove(p.Id);
                App.Log("client-timeout id=" + p.Id);
            }
            switch (Phase)
            {
                case Phase.Lobby:
                    SyncBots();
                    TimeLeft = Config.Minutes * 60;
                    break;
                case Phase.Countdown:
                    TimeLeft = Mathf.Max(0, phaseEnd - now);
                    if (now >= phaseEnd)
                    {
                        phaseEnd = now + Config.Minutes * 60f;
                        SetPhase(Phase.Playing);
                    }
                    break;
                case Phase.Playing:
                    TimeLeft = Mathf.Max(0, phaseEnd - now);
                    foreach (var p in Players.Values)
                    {
                        if (!p.Alive && now >= p.RespawnAt) Respawn(p);
                        if (p.Alive && p.Health < 100 && now - p.LastDamageTime > 5f) p.Health = Mathf.Min(100, p.Health + 12f * dt);
                    }
                    if (TimeLeft <= 0 || LimitReached()) EndMatch();
                    break;
                case Phase.Ended:
                    TimeLeft = Mathf.Max(0, phaseEnd - now);
                    if (now >= phaseEnd) BackToLobby();
                    break;
            }
            if (socket == null) return;
            if ((lobbyTimer -= dt) <= 0) { lobbyTimer = Phase == Phase.Lobby ? 0.25f : 1f; SendLobby(); }
            if (Phase != Phase.Lobby && (snapshotTimer -= dt) <= 0) { snapshotTimer = 0.05f; SendSnapshot(); }
            if (!Practice && (announceTimer -= dt) <= 0) { announceTimer = 1f; Announce(null); }
        }

        private bool LimitReached()
        {
            if (Config.Mode == Mode.TeamDeathmatch) return TeamScore[0] >= Config.KillLimit || TeamScore[1] >= Config.KillLimit;
            return Players.Values.Any(p => p.Kills >= Config.KillLimit);
        }

        private byte SmallerTeam()
        {
            int a = Players.Values.Count(p => p.Team == 0), b = Players.Values.Count(p => p.Team == 1);
            return (byte)(a <= b ? 0 : 1);
        }

        private void SyncBots()
        {
            var bots = Players.Values.Where(p => p.IsBot).OrderBy(p => p.Id).ToList();
            while (bots.Count > Config.Bots)
            {
                Players.Remove(bots[bots.Count - 1].Id);
                bots.RemoveAt(bots.Count - 1);
            }
            byte id = 100;
            while (bots.Count < Config.Bots)
            {
                while (Players.ContainsKey(id)) id++;
                var bot = new PlayerState
                {
                    Id = id, IsBot = true, Name = BotNames[(id - 100) % BotNames.Length] + " [BOT]", Team = SmallerTeam(),
                    Primary = (byte)UnityEngine.Random.Range(0, 4), Secondary = (byte)UnityEngine.Random.Range(4, 6),
                };
                Players[id] = bot;
                bots.Add(bot);
            }
        }

        public void SetTeam(byte team)
        {
            var me = Local;
            if (me == null || Phase != Phase.Lobby || Config.Mode != Mode.TeamDeathmatch) return;
            me.Team = (byte)(team & 1);
        }

        public void SetLoadout(byte primary, byte secondary)
        {
            Settings.Primary = primary;
            Settings.Secondary = secondary;
            var me = Local;
            if (me == null) return;
            me.Primary = primary;
            me.Secondary = secondary;
        }

        public void StartMatch()
        {
            if (!IsHost || Phase != Phase.Lobby) return;
            TeamScore[0] = TeamScore[1] = 0;
            KillFeed.Clear();
            foreach (var p in Players.Values)
            {
                p.Kills = p.Deaths = 0;
                if (Config.Mode == Mode.FreeForAll) p.Team = (byte)(p.Id % 2);
                Respawn(p);
            }
            phaseEnd = Time.unscaledTime + 3.5f;
            SetPhase(Phase.Countdown);
        }

        public void EndMatch()
        {
            if (!IsHost || Phase == Phase.Lobby || Phase == Phase.Ended) return;
            phaseEnd = Time.unscaledTime + 15f;
            SetPhase(Phase.Ended);
        }

        public void BackToLobby()
        {
            if (!IsHost) return;
            foreach (var p in Players.Values) p.Alive = false;
            SetPhase(Phase.Lobby);
        }

        private void SetPhase(Phase phase)
        {
            if (Phase == phase && phase != Phase.Lobby) return;
            Phase = phase;
            App.Log("phase=" + phase);
            PhaseChanged?.Invoke(phase);
        }

        private void Respawn(PlayerState p)
        {
            var (pos, yaw) = World.I != null ? World.I.PickSpawn(p, this) : (Vector3.zero, 0f);
            p.Pos = pos;
            p.Yaw = yaw;
            p.Pitch = 0;
            p.Health = 100;
            p.Alive = true;
            p.SpawnSeq++;
            p.Weapon = p.Primary;
        }

        private void HostOnJoin(BinaryReader r, IPEndPoint from, float now)
        {
            byte version = r.ReadByte();
            int tok = r.ReadInt32();
            string name = Settings.Clean(r.ReadString());
            byte primary = r.ReadByte(), secondary = r.ReadByte();
            var player = Players.Values.FirstOrDefault(p => !p.IsBot && p.Id != 0 && p.Token == tok);
            if (player == null)
            {
                if (version != Version) { SendKicked(from, "The host has a different game version"); return; }
                if (Players.Values.Count(p => !p.IsBot) >= Config.MaxPlayers) { SendKicked(from, "The group is full"); return; }
                byte id = 1;
                while (Players.ContainsKey(id)) id++;
                player = new PlayerState
                {
                    Id = id, Token = tok, Name = name, Team = SmallerTeam(), Primary = (byte)(primary % 4),
                    Secondary = (byte)(4 + secondary % 2), Weapon = primary,
                };
                if (Phase == Phase.Playing || Phase == Phase.Countdown) player.RespawnAt = now + 1f;
                Players[id] = player;
                App.Log($"client-joined id={id} name={name} from={from}");
            }
            player.EndPoint = from;
            player.LastSeen = now;
            var w = pw.Begin(Msg.Welcome);
            w.Write(tok);
            w.Write(player.Id);
            socket.Send(pw.Buffer, pw.Length, from);
            SendLobby();
        }

        private void HostOnInput(BinaryReader r, IPEndPoint from, float now)
        {
            byte id = r.ReadByte();
            int tok = r.ReadInt32();
            if (!Players.TryGetValue(id, out var p) || p.IsBot || p.Token != tok) return;
            p.LastSeen = now;
            p.EndPoint = from;
            byte team = r.ReadByte(), primary = r.ReadByte(), secondary = r.ReadByte();
            if (Phase == Phase.Lobby)
            {
                if (Config.Mode == Mode.TeamDeathmatch) p.Team = (byte)(team & 1);
                p.Primary = (byte)(primary % 4);
                p.Secondary = (byte)(4 + secondary % 2);
            }
            if (!r.ReadBoolean()) return;
            byte ack = r.ReadByte();
            var pos = r.ReadVector3();
            float yaw = r.ReadSingle(), pitch = r.ReadSingle();
            float mx = r.ReadSByte() / 50f, my = r.ReadSByte() / 50f;
            byte flags = r.ReadByte();
            byte weapon = r.ReadByte();
            if (p.Alive && ack == p.SpawnSeq)
            {
                p.Pos = pos; p.Yaw = yaw; p.Pitch = pitch; p.MoveX = mx; p.MoveY = my;
                p.Crouch = (flags & 1) != 0; p.Grounded = (flags & 2) != 0; p.Aiming = (flags & 4) != 0;
                if (weapon == p.Primary || weapon == p.Secondary) p.Weapon = weapon;
            }
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                var shot = new Shot
                {
                    Seq = r.ReadInt32(), Weapon = r.ReadByte(), Origin = r.ReadVector3(), Dir = r.ReadVector3(), Seed = r.ReadInt32(), Shooter = id,
                };
                if (shot.Seq <= p.LastShotSeq) continue;
                p.LastShotSeq = shot.Seq;
                if (shot.Weapon != p.Primary && shot.Weapon != p.Secondary) continue;
                ProcessShot(shot);
            }
        }

        /// <summary>Host: hit test a shot against the world and the players and apply its damage.</summary>
        private void ProcessShot(Shot shot)
        {
            if (Phase != Phase.Playing || !Players.TryGetValue(shot.Shooter, out var shooter) || !shooter.Alive || World.I == null) return;
            var def = Weapons.Get(shot.Weapon);
            var dirs = Ballistics.Pellets(shot.Dir.normalized, shot.Seed, def);
            var fx = new ShotFx { Shooter = shot.Shooter, Weapon = shot.Weapon, Origin = shot.Origin, HitKind = 2, End = shot.Origin + shot.Dir * def.Range };
            for (int i = 0; i < dirs.Length; i++)
            {
                var hit = World.I.TraceShot(shot.Origin, dirs[i], def.Range, shot.Shooter);
                if (i == 0) { fx.End = hit.Point; fx.HitKind = hit.Kind; }
                if (hit.Victim != NoPlayer && Players.TryGetValue(hit.Victim, out var victim))
                {
                    float falloff = def.Pellets > 1 || !def.Primary ? Mathf.Lerp(1f, 0.55f, hit.Distance / def.Range) : 1f;
                    Damage(shooter, victim, def, hit.Head, def.Damage * falloff);
                }
            }
            fx.Seq = ++fxSeq;
            fxRing.Add(fx);
            if (fxRing.Count > 16) fxRing.RemoveAt(0);
            if (fx.Shooter != LocalId) ShotSeen?.Invoke(fx);
        }

        private void Damage(PlayerState shooter, PlayerState victim, WeaponDef def, bool head, float amount)
        {
            if (!victim.Alive) return;
            if (Config.Mode == Mode.TeamDeathmatch && victim.Team == shooter.Team && victim != shooter) return;
            victim.Health -= amount * (head ? def.HeadMultiplier : 1f);
            victim.LastDamageTime = Time.unscaledTime;
            if (victim != shooter) shooter.Hits++;
            if (victim.Health > 0) return;
            victim.Health = 0;
            victim.Alive = false;
            victim.Deaths++;
            victim.RespawnAt = Time.unscaledTime + 4f;
            if (victim != shooter)
            {
                shooter.Kills++;
                if (Config.Mode == Mode.TeamDeathmatch) TeamScore[shooter.Team & 1]++;
            }
            var kill = new KillEvent { Seq = ++killSeq, Killer = shooter.Id, Victim = victim.Id, Weapon = def.Id, Headshot = head };
            KillFeed.Add(kill);
            if (KillFeed.Count > 8) KillFeed.RemoveAt(0);
            App.Log($"kill {shooter.Name} -> {victim.Name} with {def.Name}{(head ? " (headshot)" : "")}");
            KillHappened?.Invoke(kill);
        }

        private void SendLobby()
        {
            var w = pw.Begin(Msg.Lobby);
            Config.Write(w);
            w.Write((byte)Phase);
            w.Write(TimeLeft);
            w.Write((byte)Players.Count);
            foreach (var p in Players.Values)
            {
                w.Write(p.Id); w.Write(p.Name); w.Write(p.Team); w.Write(p.IsBot); w.Write(p.Primary); w.Write(p.Secondary);
            }
            SendToClients();
        }

        private void SendSnapshot()
        {
            var w = pw.Begin(Msg.Snapshot);
            w.Write((byte)Phase);
            w.Write(TimeLeft);
            w.Write((short)TeamScore[0]);
            w.Write((short)TeamScore[1]);
            w.Write((byte)Players.Count);
            foreach (var p in Players.Values)
            {
                byte flags = (byte)((p.Alive ? 1 : 0) | (p.Crouch ? 2 : 0) | (p.Grounded ? 4 : 0) | (p.Aiming ? 8 : 0));
                w.Write(p.Id); w.Write(flags); w.Write((byte)Mathf.Clamp(Mathf.CeilToInt(p.Health), 0, 255)); w.Write(p.Weapon);
                w.Write(p.Pos); w.Write(p.Yaw); w.Write(p.Pitch);
                w.Write((sbyte)Mathf.Clamp(Mathf.RoundToInt(p.MoveX * 50), -127, 127));
                w.Write((sbyte)Mathf.Clamp(Mathf.RoundToInt(p.MoveY * 50), -127, 127));
                w.Write(p.Kills); w.Write(p.Deaths); w.Write(p.SpawnSeq); w.Write(p.Hits);
            }
            int kills = Mathf.Min(KillFeed.Count, 6);
            w.Write((byte)kills);
            for (int i = KillFeed.Count - kills; i < KillFeed.Count; i++)
            {
                var k = KillFeed[i];
                w.Write(k.Seq); w.Write(k.Killer); w.Write(k.Victim); w.Write(k.Weapon); w.Write(k.Headshot);
            }
            w.Write((byte)fxRing.Count);
            foreach (var fx in fxRing)
            {
                w.Write(fx.Seq); w.Write(fx.Shooter); w.Write(fx.Weapon); w.Write(fx.Origin); w.Write(fx.End); w.Write(fx.HitKind);
            }
            SendToClients();
        }

        private void SendToClients()
        {
            foreach (var p in Players.Values)
                if (!p.IsBot && p.Id != 0 && p.EndPoint != null) socket.Send(pw.Buffer, pw.Length, p.EndPoint);
        }

        private void SendKicked(IPEndPoint to, string reason)
        {
            var w = pw.Begin(Msg.Kicked);
            w.Write(reason);
            socket.Send(pw.Buffer, pw.Length, to);
        }

        // ------------------------------------------------------------------ client

        private void ClientTick(float now)
        {
            float dt = Time.unscaledDeltaTime;
            if (!joined)
            {
                if ((joinTimer -= dt) <= 0)
                {
                    joinTimer = 0.5f;
                    var w = pw.Begin(Msg.Join);
                    w.Write(Version); w.Write(token); w.Write(Settings.PlayerName); w.Write(Settings.Primary); w.Write(Settings.Secondary);
                    socket.Send(pw.Buffer, pw.Length, hostEndPoint);
                }
                if (now > joinDeadline) Disconnect("Could not reach the group. Check that you are on the same Wi-Fi or hotspot.");
                return;
            }
            if (now - lastHostPacket > 8f) { Disconnect("The connection to the host was lost"); return; }
            if ((inputTimer -= dt) <= 0)
            {
                inputTimer = InMatch ? 1f / 30f : 0.2f;
                SendInput(now);
            }
        }

        private void SendInput(float now)
        {
            var me = Local;
            if (me == null) return;
            var w = pw.Begin(Msg.Input);
            w.Write(LocalId); w.Write(token); w.Write(me.Team); w.Write(me.Primary); w.Write(me.Secondary);
            bool inMatch = Phase == Phase.Playing || Phase == Phase.Countdown;
            w.Write(inMatch);
            if (inMatch)
            {
                byte flags = (byte)((me.Crouch ? 1 : 0) | (me.Grounded ? 2 : 0) | (me.Aiming ? 4 : 0));
                w.Write(me.SpawnSeq); w.Write(me.Pos); w.Write(me.Yaw); w.Write(me.Pitch);
                w.Write((sbyte)Mathf.Clamp(Mathf.RoundToInt(me.MoveX * 50), -127, 127));
                w.Write((sbyte)Mathf.Clamp(Mathf.RoundToInt(me.MoveY * 50), -127, 127));
                w.Write(flags); w.Write(me.Weapon);
                pendingShots.RemoveAll(s => now - s.time > 1f);
                int count = Mathf.Min(pendingShots.Count, 8);
                w.Write((byte)count);
                for (int i = pendingShots.Count - count; i < pendingShots.Count; i++)
                {
                    var s = pendingShots[i].shot;
                    w.Write(s.Seq); w.Write(s.Weapon); w.Write(s.Origin); w.Write(s.Dir); w.Write(s.Seed);
                }
            }
            socket.Send(pw.Buffer, pw.Length, hostEndPoint);
        }

        private void ClientOnLobby(BinaryReader r)
        {
            Config.Read(r);
            var phase = (Phase)r.ReadByte();
            TimeLeft = r.ReadSingle();
            int count = r.ReadByte();
            var seen = new HashSet<byte>();
            for (int i = 0; i < count; i++)
            {
                byte id = r.ReadByte();
                bool isNew = !Players.TryGetValue(id, out var p);
                if (isNew) Players[id] = p = new PlayerState { Id = id };
                p.Name = r.ReadString(); byte team = r.ReadByte(); p.IsBot = r.ReadBoolean();
                byte primary = r.ReadByte(), secondary = r.ReadByte();
                // our own team / loadout are decided locally while in the lobby and sent with the input
                if (id != LocalId || phase != Phase.Lobby || isNew) { p.Team = team; p.Primary = primary; p.Secondary = secondary; }
                seen.Add(id);
            }
            foreach (var id in Players.Keys.Where(k => !seen.Contains(k)).ToList()) Players.Remove(id);
            if (!Players.ContainsKey(LocalId)) { Disconnect("You were removed from the group"); return; }
            if (phase != Phase) SetPhase(phase);
        }

        private void ClientOnSnapshot(BinaryReader r)
        {
            var phase = (Phase)r.ReadByte();
            TimeLeft = r.ReadSingle();
            TeamScore[0] = r.ReadInt16();
            TeamScore[1] = r.ReadInt16();
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                byte id = r.ReadByte();
                if (!Players.TryGetValue(id, out var p)) Players[id] = p = new PlayerState { Id = id };
                byte flags = r.ReadByte();
                p.Health = r.ReadByte();
                byte weapon = r.ReadByte();
                var pos = r.ReadVector3();
                float yaw = r.ReadSingle(), pitch = r.ReadSingle();
                float mx = r.ReadSByte() / 50f, my = r.ReadSByte() / 50f;
                p.Kills = r.ReadInt16(); p.Deaths = r.ReadInt16();
                byte spawnSeq = r.ReadByte();
                p.Hits = r.ReadUInt16();
                p.Alive = (flags & 1) != 0;
                bool respawned = spawnSeq != p.SpawnSeq;
                p.SpawnSeq = spawnSeq;
                if (id != LocalId || respawned)
                {
                    p.Pos = pos; p.Yaw = yaw; p.Pitch = pitch;
                }
                if (id != LocalId)
                {
                    p.MoveX = mx; p.MoveY = my; p.Weapon = weapon;
                    p.Crouch = (flags & 2) != 0; p.Grounded = (flags & 4) != 0; p.Aiming = (flags & 8) != 0;
                }
            }
            int kills = r.ReadByte();
            for (int i = 0; i < kills; i++)
            {
                var k = new KillEvent { Seq = r.ReadUInt16(), Killer = r.ReadByte(), Victim = r.ReadByte(), Weapon = r.ReadByte(), Headshot = r.ReadBoolean() };
                if (!killInit) { lastKillSeen = k.Seq; continue; }
                if ((short)(k.Seq - lastKillSeen) <= 0) continue;
                lastKillSeen = k.Seq;
                KillFeed.Add(k);
                if (KillFeed.Count > 8) KillFeed.RemoveAt(0);
                KillHappened?.Invoke(k);
            }
            killInit = true;
            int fxCount = r.ReadByte();
            for (int i = 0; i < fxCount; i++)
            {
                var fx = new ShotFx { Seq = r.ReadUInt16(), Shooter = r.ReadByte(), Weapon = r.ReadByte(), Origin = r.ReadVector3(), End = r.ReadVector3(), HitKind = r.ReadByte() };
                if (!fxInit) { lastFxSeen = fx.Seq; continue; }
                if ((short)(fx.Seq - lastFxSeen) <= 0) continue;
                lastFxSeen = fx.Seq;
                if (fx.Shooter != LocalId) ShotSeen?.Invoke(fx);
            }
            fxInit = true;
            if (phase != Phase) SetPhase(phase);
        }

        // ------------------------------------------------------------------ shooting

        /// <summary>Called by the local player for every trigger pull.</summary>
        public void FireLocal(Shot shot)
        {
            shot.Shooter = LocalId;
            shot.Seq = ++shotSeq;
            if (IsHost) ProcessShot(shot);
            else pendingShots.Add((shot, Time.unscaledTime));
        }

        /// <summary>Host only: bots shoot through here.</summary>
        public void FireBot(Shot shot)
        {
            if (IsHost) ProcessShot(shot);
        }
    }
}
