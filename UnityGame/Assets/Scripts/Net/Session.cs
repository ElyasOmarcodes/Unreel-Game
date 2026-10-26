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
        public byte MaxPlayers = 255;   // no limit: as many as want to join
        public byte Bots = 2;
        public byte BotSkill = 1;
        public string Password = "";   // host only, never sent: joiners must type it (empty = open group)

        public static readonly string[] MapNames =
        {
            "STEEL CITY - DAY", "STEEL CITY - SUNSET", "STEEL CITY - NIGHT",
            "AL-ZAHRA - DAY", "AL-ZAHRA - SUNSET", "AL-ZAHRA - NIGHT",
            "VILLAGE - DAY", "VILLAGE - SUNSET", "VILLAGE - NIGHT", "OLD TOWN - DAY", "OLD TOWN - SUNSET", "OLD TOWN - NIGHT",
        };
        public static readonly string[] ModeNames = { "TEAMS", "SOLO - NO TEAMS" };
        public const int MaxTeams = 10;
        /// <summary>The ten teams: TEAM 1 .. TEAM 10, each its own colour (the vest, the names, the tables).</summary>
        public static readonly Color[] TeamColors =
        {
            new Color(0.18f, 0.42f, 0.95f), new Color(0.92f, 0.22f, 0.18f), new Color(0.2f, 0.75f, 0.3f), new Color(0.98f, 0.78f, 0.12f), new Color(0.62f, 0.3f, 0.9f),
            new Color(0.1f, 0.8f, 0.85f), new Color(0.98f, 0.5f, 0.1f), new Color(0.95f, 0.4f, 0.7f), new Color(0.55f, 0.4f, 0.25f), new Color(0.85f, 0.88f, 0.92f),
        };
        public static string TeamName(int team) => "TEAM " + (team % MaxTeams + 1);
        public static string TeamHex(int team) => "#" + ColorUtility.ToHtmlStringRGB(TeamColors[team % MaxTeams]);
        /// <summary>The maps in the order of the menu (base layout of each; + 0 day, 1 afternoon, 2 evening).</summary>
        public static readonly int[] MapOrder = { 0, 3, 6, 9 };
        public static readonly string[] MapTitles = { "STEEL CITY", "AL-ZAHRA", "VILLAGE", "OLD TOWN" };
        public static readonly string[] TimeNames = { "DAY", "AFTERNOON", "EVENING" };
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
        public bool Crouch, Grounded = true, Aiming, Prone;
        public int Look;
        public short Kills, Deaths;
        public byte SpawnSeq;
        public ushort Hits;
        public ushort Shots, ShotHits, Heads, DamageDone;   // this match, for the results table
        public bool Spawned;   // placed in this match: only then is the body shown (never dead in the air at 0,0,0)

        // host side only
        public IPEndPoint EndPoint;
        public int Token;
        public float LastSeen;
        public float RespawnAt;
        public int LastShotSeq, LastThrowSeq, LastHealSeq;
        public float LastDamageTime;
    }

    public struct KillEvent { public ushort Seq; public byte Killer, Victim, Weapon; public bool Headshot; }
    /// <summary>
    /// A trigger pull. Victim / Head / Pellets: what the shooter saw it hit on their screen (others are drawn
    /// a little in the past there); the host checks it is believable and counts it even if, on its own
    /// newer picture, the target has already moved on.
    /// </summary>
    public struct Shot { public int Seq; public byte Shooter, Weapon; public Vector3 Origin, Dir; public int Seed; public byte Victim, Pellets; public bool Head; }
    public struct Throw { public int Seq; public byte Thrower; public Vector3 Origin, Velocity; }
    /// <summary>Grenade event in the snapshot: Kind 0 thrown (A origin, B velocity), 1 exploded (A position).</summary>
    public struct NadeEvent { public ushort Seq; public byte Thrower, Kind; public int ThrowSeq; public Vector3 A, B; }
    /// <summary>Blood given to a badly wounded team mate (Kind 1).</summary>
    public struct HealEvent { public ushort Seq; public byte Giver, Target, Kind, Amount; }
    public struct HealRequest { public int Seq; public byte Target, Kind; }
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
        public const byte Version = 12;
        public const byte NoPlayer = 255;
        public const string PasswordNeeded = "This group is locked: type its password";
        public const string WrongPassword = "Wrong password for this group";

        public bool Active, IsHost, Practice;
        public MatchConfig Config = new MatchConfig();
        public Phase Phase;
        /// <summary>Counts the matches played in this session (each countdown starts a new one).</summary>
        public int MatchNumber;
        public float TimeLeft;
        public readonly int[] TeamScore = new int[MatchConfig.MaxTeams];
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
        private string joinPassword = "";
        private bool joined;
        private ushort killSeq, fxSeq, lastKillSeen, lastFxSeen;
        private bool killInit, fxInit;
        private readonly List<ShotFx> fxRing = new List<ShotFx>();
        private readonly List<(Shot shot, float time)> pendingShots = new List<(Shot, float)>();
        private int shotSeq;
        private readonly List<(Throw t, float time)> pendingThrows = new List<(Throw, float)>();
        private readonly List<NadeEvent> nadeRing = new List<NadeEvent>();
        private int throwSeq;
        private ushort nadeSeq, lastNadeSeen;
        private bool nadeInit;
        private readonly List<(HealRequest h, float time)> pendingHeals = new List<(HealRequest, float)>();
        private readonly List<HealEvent> healRing = new List<HealEvent>();
        private int healReqSeq;
        private ushort healSeq, lastHealSeen;
        private bool healInit;

        public event Action<HealEvent> Healed;
        /// <summary>Blood: a team mate below the threshold, close by, gets 40 health; then the giver waits a little.</summary>
        public const float HealRange = 3f, BloodThreshold = 50f, BloodGift = 40f, BloodCooldown = 10f;

        private static readonly string[] BotNames = { "Viper", "Ghost", "Falcon", "Rook", "Blaze", "Shadow", "Hunter", "Nova", "Raven", "Titan" };

        public PlayerState Local => Players.TryGetValue(LocalId, out var p) ? p : null;
        public bool InMatch => Active && Phase != Phase.Lobby;

        private void Awake() { I = this; }

        private void OnApplicationQuit() => Stop();

        // ------------------------------------------------------------------ host / join / leave

        public bool Host(MatchConfig config, bool practice, byte team = 0)
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
            Players[0] = new PlayerState { Id = 0, Name = Settings.PlayerName, Team = (byte)(team % MatchConfig.MaxTeams), Primary = Settings.Primary, Secondary = Settings.Secondary, Look = Settings.Look };
            LocalId = 0;
            SyncBots();
            HostAddresses = string.Join("   ", LanUtil.LocalAddresses());
            if (HostPort != GamePort) HostAddresses += "  (port " + HostPort + ")";
            SetPhase(Phase.Lobby);
            App.Log($"host-started practice={practice} port={HostPort} ip={HostAddresses}");
            return true;
        }

        public void Join(IPEndPoint host, string password = "")
        {
            Stop();
            joinPassword = password ?? "";
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
            pendingThrows.Clear();
            nadeRing.Clear();
            pendingHeals.Clear();
            healRing.Clear();
            Grenade.ClearAll();
            killInit = fxInit = nadeInit = healInit = false;
            Array.Clear(TeamScore, 0, TeamScore.Length);
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
                Locked = !string.IsNullOrEmpty(Config.Password),
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
            if (Config.Mode == Mode.TeamDeathmatch) return TeamScore.Any(t => t >= Config.KillLimit);
            return Players.Values.Any(p => p.Kills >= Config.KillLimit);
        }

        /// <summary>
        /// The team with the fewest players among the teams the people chose (at least two teams: if
        /// everyone is in one, the next team number is the other side); bots and late comers go there.
        /// </summary>
        private byte SmallerTeam()
        {
            var teams = Players.Values.Where(p => !p.IsBot && p.Team < MatchConfig.MaxTeams).Select(p => (int)p.Team).Distinct().ToList();
            if (teams.Count == 0) teams.Add(0);
            if (teams.Count < 2) teams.Add((teams[0] + 1) % MatchConfig.MaxTeams);
            return (byte)teams.OrderBy(t => Players.Values.Count(p => p.Team == t)).ThenBy(t => t).First();
        }

        /// <summary>The bots spread over the teams the people are in, the smallest filled first.</summary>
        private void BalanceBots()
        {
            var bots = Players.Values.Where(p => p.IsBot).OrderBy(p => p.Id).ToList();
            foreach (var b in bots) b.Team = 255;
            foreach (var b in bots) b.Team = SmallerTeam();
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
                    Primary = Weapons.BotPrimaries[UnityEngine.Random.Range(0, Weapons.BotPrimaries.Length)], Secondary = (byte)UnityEngine.Random.Range(4, 6),
                    Look = Wardrobe.Random(),
                };
                Players[id] = bot;
                bots.Add(bot);
            }
        }

        public void SetTeam(byte team)
        {
            var me = Local;
            if (me == null || Phase != Phase.Lobby || Config.Mode != Mode.TeamDeathmatch) return;
            me.Team = (byte)(team % MatchConfig.MaxTeams);
            Settings.Team = me.Team;
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
            Array.Clear(TeamScore, 0, TeamScore.Length);
            KillFeed.Clear();
            if (Config.Mode == Mode.TeamDeathmatch) BalanceBots();
            foreach (var p in Players.Values)
            {
                p.Kills = p.Deaths = 0;
                p.Shots = p.ShotHits = p.Heads = p.DamageDone = 0;
                if (Config.Mode == Mode.FreeForAll) p.Team = (byte)(p.Id % 2);
                Respawn(p);
            }
            phaseEnd = Time.unscaledTime + 3.5f;
            SetPhase(Phase.Countdown);
        }

        /// <summary>Host: the match stands still for a while (the first kill film): the clock and the respawns wait.</summary>
        public void Hold(float seconds)
        {
            if (!IsHost || Phase != Phase.Playing) return;
            phaseEnd += seconds;
            foreach (var p in Players.Values)
                if (!p.Alive) p.RespawnAt += seconds;
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
            if (phase == Phase.Countdown) MatchNumber++;
            if (phase != Phase.Playing) Grenade.ClearAll();
            if (phase == Phase.Lobby) foreach (var p in Players.Values) p.Spawned = false;
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
            p.Spawned = true;
            p.SpawnSeq++;
            // back with the weapon that was in the hand (the choice is kept over a death)
            if (p.Weapon != p.Primary && p.Weapon != p.Secondary) p.Weapon = p.Primary;
        }

        private void HostOnJoin(BinaryReader r, IPEndPoint from, float now)
        {
            byte version = r.ReadByte();
            int tok = r.ReadInt32();
            string name = Settings.Clean(r.ReadString());
            byte primary = r.ReadByte(), secondary = r.ReadByte();
            int look = r.ReadInt32();
            string password = r.BaseStream.Position < r.BaseStream.Length ? r.ReadString() : "";
            byte wanted = r.BaseStream.Position < r.BaseStream.Length ? r.ReadByte() : (byte)255;
            var player = Players.Values.FirstOrDefault(p => !p.IsBot && p.Id != 0 && p.Token == tok);
            if (player == null)
            {
                if (version != Version) { SendKicked(from, "The host has a different game version"); return; }
                if (!string.IsNullOrEmpty(Config.Password) && password != Config.Password)
                {
                    SendKicked(from, password.Length == 0 ? PasswordNeeded : WrongPassword);
                    return;
                }
                if (Players.Values.Count(p => !p.IsBot) >= 250) { SendKicked(from, "The group is full"); return; }
                byte id = 1;
                while (Players.ContainsKey(id)) id++;
                player = new PlayerState
                {
                    Id = id, Token = tok, Name = name, Team = wanted < MatchConfig.MaxTeams ? wanted : SmallerTeam(), Primary = (byte)(primary % Weapons.Count),
                    Secondary = (byte)(secondary % Weapons.Count), Weapon = (byte)(primary % Weapons.Count), Look = look,
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
                if (Config.Mode == Mode.TeamDeathmatch) p.Team = (byte)(team % MatchConfig.MaxTeams);
                p.Primary = (byte)(primary % Weapons.Count);
                p.Secondary = (byte)(secondary % Weapons.Count);
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
                p.Crouch = (flags & 1) != 0; p.Grounded = (flags & 2) != 0; p.Aiming = (flags & 4) != 0; p.Prone = (flags & 8) != 0;
                if (weapon == p.Primary || weapon == p.Secondary) p.Weapon = weapon;
            }
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                var shot = new Shot
                {
                    Seq = r.ReadInt32(), Weapon = r.ReadByte(), Origin = r.ReadVector3(), Dir = r.ReadVector3(), Seed = r.ReadInt32(), Shooter = id,
                    Victim = r.ReadByte(), Head = r.ReadBoolean(), Pellets = r.ReadByte(),
                };
                if (shot.Seq <= p.LastShotSeq) continue;
                p.LastShotSeq = shot.Seq;
                if (shot.Weapon != p.Primary && shot.Weapon != p.Secondary) continue;
                ProcessShot(shot);
            }
            int throws = r.ReadByte();
            for (int i = 0; i < throws; i++)
            {
                var t = new Throw { Seq = r.ReadInt32(), Origin = r.ReadVector3(), Velocity = r.ReadVector3(), Thrower = id };
                if (t.Seq <= p.LastThrowSeq) continue;
                p.LastThrowSeq = t.Seq;
                HostThrow(t);
            }
            int heals = r.ReadByte();
            for (int i = 0; i < heals; i++)
            {
                int seq = r.ReadInt32();
                byte target = r.ReadByte(), kind = r.ReadByte();
                if (seq <= p.LastHealSeq) continue;
                p.LastHealSeq = seq;
                HostHeal(id, target, kind);
            }
        }

        /// <summary>Host: hit test a shot against the world and the players and apply its damage.</summary>
        private void ProcessShot(Shot shot)
        {
            if (Phase != Phase.Playing || KillCam.Active || !Players.TryGetValue(shot.Shooter, out var shooter) || !shooter.Alive || World.I == null) return;
            var def = Weapons.Get(shot.Weapon);
            var dirs = Ballistics.Pellets(shot.Dir.normalized, shot.Seed, def);
            shooter.Shots++;
            bool hitAny = false, headAny = false, claimMet = false;
            var fx = new ShotFx { Shooter = shot.Shooter, Weapon = shot.Weapon, Origin = shot.Origin, HitKind = 2, End = shot.Origin + shot.Dir * def.Range };
            for (int i = 0; i < dirs.Length; i++)
            {
                var hit = World.I.TraceShot(shot.Origin, dirs[i], def.Range, shot.Shooter);
                if (i == 0) { fx.End = hit.Point; fx.HitKind = hit.Kind; }
                if (hit.Victim != NoPlayer && Players.TryGetValue(hit.Victim, out var victim))
                {
                    claimMet |= hit.Victim == shot.Victim;
                    float falloff = def.Pellets > 1 || !def.Primary ? Mathf.Lerp(1f, 0.55f, hit.Distance / def.Range) : 1f;
                    if (Damage(shooter, victim, def, hit.Head, def.Damage * falloff) > 0f) { hitAny = true; headAny |= hit.Head; }
                }
            }
            // the shooter saw a hit the host's newer picture misses (the target moved on meanwhile): count it
            // when the target was really close to the line of fire and nothing solid stood in between
            if (!claimMet && shot.Victim != NoPlayer && shot.Victim != shot.Shooter && Players.TryGetValue(shot.Victim, out var claimed) && claimed.Alive)
            {
                var dir = shot.Dir.normalized;
                var chest = claimed.Pos + Vector3.up * (claimed.Prone ? 0.3f : claimed.Crouch ? 0.9f : 1.2f);
                float along = Vector3.Dot(chest - shot.Origin, dir);
                var closest = shot.Origin + dir * along;
                if (along > 0f && along < def.Range && Vector3.Distance(closest, chest) < 1.6f && World.I.LineOfSight(shot.Origin, closest))
                {
                    float falloff = def.Pellets > 1 || !def.Primary ? Mathf.Lerp(1f, 0.55f, along / def.Range) : 1f;
                    int pellets = Mathf.Clamp(shot.Pellets, 1, dirs.Length);
                    for (int i = 0; i < pellets; i++)
                        if (Damage(shooter, claimed, def, shot.Head, def.Damage * falloff) > 0f) { hitAny = true; headAny |= shot.Head; }
                    fx.End = closest; fx.HitKind = 1;
                }
            }
            if (hitAny) shooter.ShotHits++;
            if (headAny) shooter.Heads++;
            fx.Seq = ++fxSeq;
            fxRing.Add(fx);
            if (fxRing.Count > 16) fxRing.RemoveAt(0);
            if (fx.Shooter != LocalId) ShotSeen?.Invoke(fx);
        }

        /// <summary>Applies the damage; returns how much health it really took (0 when refused).</summary>
        private float Damage(PlayerState shooter, PlayerState victim, WeaponDef def, bool head, float amount)
        {
            if (!victim.Alive) return 0f;
            if (Config.Mode == Mode.TeamDeathmatch && victim.Team == shooter.Team && victim != shooter) return 0f;
            float dealt = Mathf.Min(victim.Health, amount * (head ? def.HeadMultiplier : 1f));
            victim.Health -= amount * (head ? def.HeadMultiplier : 1f);
            victim.LastDamageTime = Time.unscaledTime;
            if (victim != shooter)
            {
                shooter.Hits++;
                shooter.DamageDone = (ushort)Mathf.Min(65000, shooter.DamageDone + Mathf.RoundToInt(dealt));
            }
            if (victim.Health > 0) return dealt;
            victim.Health = 0;
            victim.Alive = false;
            victim.Deaths++;
            victim.RespawnAt = Time.unscaledTime + 4f;
            if (victim != shooter)
            {
                shooter.Kills++;
                if (Config.Mode == Mode.TeamDeathmatch) TeamScore[shooter.Team % MatchConfig.MaxTeams]++;
            }
            var kill = new KillEvent { Seq = ++killSeq, Killer = shooter.Id, Victim = victim.Id, Weapon = def.Id, Headshot = head };
            KillFeed.Add(kill);
            if (KillFeed.Count > 8) KillFeed.RemoveAt(0);
            App.Log($"kill {shooter.Name} -> {victim.Name} with {def.Name}{(head ? " (headshot)" : "")}");
            KillHappened?.Invoke(kill);
            return dealt;
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
                w.Write(p.Id); w.Write(p.Name); w.Write(p.Team); w.Write(p.IsBot); w.Write(p.Primary); w.Write(p.Secondary); w.Write(p.Look);
            }
            SendToClients();
        }

        private void SendSnapshot()
        {
            var w = pw.Begin(Msg.Snapshot);
            w.Write((byte)Phase);
            w.Write(TimeLeft);
            for (int t = 0; t < MatchConfig.MaxTeams; t++) w.Write((short)TeamScore[t]);
            w.Write((byte)Players.Count);
            foreach (var p in Players.Values)
            {
                byte flags = (byte)((p.Alive ? 1 : 0) | (p.Crouch ? 2 : 0) | (p.Grounded ? 4 : 0) | (p.Aiming ? 8 : 0) | (p.Prone ? 16 : 0));
                w.Write(p.Id); w.Write(flags); w.Write((byte)Mathf.Clamp(Mathf.CeilToInt(p.Health), 0, 255)); w.Write(p.Weapon);
                w.Write(p.Pos); w.Write(p.Yaw); w.Write(p.Pitch);
                w.Write((sbyte)Mathf.Clamp(Mathf.RoundToInt(p.MoveX * 50), -127, 127));
                w.Write((sbyte)Mathf.Clamp(Mathf.RoundToInt(p.MoveY * 50), -127, 127));
                w.Write(p.Kills); w.Write(p.Deaths); w.Write(p.SpawnSeq); w.Write(p.Hits);
                w.Write(p.Shots); w.Write(p.ShotHits); w.Write(p.Heads); w.Write(p.DamageDone);
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
            w.Write((byte)nadeRing.Count);
            foreach (var n in nadeRing)
            {
                w.Write(n.Seq); w.Write(n.Thrower); w.Write(n.Kind); w.Write(n.ThrowSeq); w.Write(n.A); w.Write(n.B);
            }
            w.Write((byte)healRing.Count);
            foreach (var h in healRing)
            {
                w.Write(h.Seq); w.Write(h.Giver); w.Write(h.Target); w.Write(h.Kind); w.Write(h.Amount);
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
                    w.Write(Version); w.Write(token); w.Write(Settings.PlayerName); w.Write(Settings.Primary); w.Write(Settings.Secondary); w.Write(Settings.Look);
                    w.Write(joinPassword);
                    w.Write(Settings.Team);
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
                byte flags = (byte)((me.Crouch ? 1 : 0) | (me.Grounded ? 2 : 0) | (me.Aiming ? 4 : 0) | (me.Prone ? 8 : 0));
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
                    w.Write(s.Victim); w.Write(s.Head); w.Write(s.Pellets);
                }
                pendingThrows.RemoveAll(t => now - t.time > 1f);
                w.Write((byte)pendingThrows.Count);
                foreach (var (t, _) in pendingThrows)
                {
                    w.Write(t.Seq); w.Write(t.Origin); w.Write(t.Velocity);
                }
                pendingHeals.RemoveAll(h => now - h.time > 1f);
                w.Write((byte)pendingHeals.Count);
                foreach (var (h, _) in pendingHeals)
                {
                    w.Write(h.Seq); w.Write(h.Target); w.Write(h.Kind);
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
                p.Look = r.ReadInt32();
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
            for (int t = 0; t < MatchConfig.MaxTeams; t++) TeamScore[t] = r.ReadInt16();
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
                p.Shots = r.ReadUInt16(); p.ShotHits = r.ReadUInt16(); p.Heads = r.ReadUInt16(); p.DamageDone = r.ReadUInt16();
                p.Alive = (flags & 1) != 0;
                bool respawned = spawnSeq != p.SpawnSeq;
                p.SpawnSeq = spawnSeq;
                if (respawned) p.Spawned = true;
                if (id != LocalId || respawned)
                {
                    p.Pos = pos; p.Yaw = yaw; p.Pitch = pitch;
                }
                if (id != LocalId)
                {
                    p.MoveX = mx; p.MoveY = my; p.Weapon = weapon;
                    p.Crouch = (flags & 2) != 0; p.Grounded = (flags & 4) != 0; p.Aiming = (flags & 8) != 0; p.Prone = (flags & 16) != 0;
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
            int nades = r.ReadByte();
            for (int i = 0; i < nades; i++)
            {
                var n = new NadeEvent { Seq = r.ReadUInt16(), Thrower = r.ReadByte(), Kind = r.ReadByte(), ThrowSeq = r.ReadInt32(), A = r.ReadVector3(), B = r.ReadVector3() };
                if (!nadeInit) { lastNadeSeen = n.Seq; continue; }
                if ((short)(n.Seq - lastNadeSeen) <= 0) continue;
                lastNadeSeen = n.Seq;
                if (n.Kind == 0)
                {
                    if (n.Thrower != LocalId) Grenade.Spawn(n.Thrower, n.ThrowSeq, n.A, n.B, false);
                }
                else
                {
                    var g = Grenade.Find(n.Thrower, n.ThrowSeq);
                    if (g != null) Destroy(g.gameObject);
                    Effects.Explosion(n.A);
                }
            }
            nadeInit = true;
            int healCount = r.ReadByte();
            for (int i = 0; i < healCount; i++)
            {
                var h = new HealEvent { Seq = r.ReadUInt16(), Giver = r.ReadByte(), Target = r.ReadByte(), Kind = r.ReadByte(), Amount = r.ReadByte() };
                if (!healInit) { lastHealSeen = h.Seq; continue; }
                if ((short)(h.Seq - lastHealSeen) <= 0) continue;
                lastHealSeen = h.Seq;
                Healed?.Invoke(h);
            }
            healInit = true;
            if (phase != Phase) SetPhase(phase);
        }

        // ------------------------------------------------------------------ shooting

        /// <summary>
        /// The local player uses a syringe (kind 0: on a team mate, or on themselves) or gives a team mate
        /// some of their own blood (kind 1). The host checks and applies it.
        /// </summary>
        public void HealLocal(byte target, byte kind)
        {
            if (IsHost) HostHeal(LocalId, target, kind);
            else pendingHeals.Add((new HealRequest { Seq = ++healReqSeq, Target = target, Kind = kind }, Time.unscaledTime));
        }

        /// <summary>Host: a bot gives blood to a wounded team mate next to it.</summary>
        public void BotGiveBlood(byte bot, byte mate) { if (IsHost) HostHeal(bot, mate, 1); }

        private void HostHeal(byte giverId, byte targetId, byte kind)
        {
            if (Phase != Phase.Playing || !Players.TryGetValue(giverId, out var giver) || !Players.TryGetValue(targetId, out var target) || !giver.Alive || !target.Alive) return;
            // only blood, only for a badly wounded team mate standing close by (a little slack for the network)
            if (kind != 1 || giver == target || Config.Mode != Mode.TeamDeathmatch || giver.Team != target.Team) return;
            if ((giver.Pos - target.Pos).sqrMagnitude > HealRange * HealRange * 2f || target.Health >= BloodThreshold + 2f) return;
            float amount = Mathf.Min(BloodGift, 100f - target.Health);
            if (amount <= 0f) return;
            target.Health += amount;
            var e = new HealEvent { Seq = ++healSeq, Giver = giverId, Target = targetId, Kind = kind, Amount = (byte)Mathf.RoundToInt(amount) };
            healRing.Add(e);
            if (healRing.Count > 8) healRing.RemoveAt(0);
            App.Log($"heal {giver.Name} -> {target.Name} kind={kind} +{e.Amount}");
            Healed?.Invoke(e);
        }

        /// <summary>Called by the local player when a grenade leaves the hand.</summary>
        public void ThrowLocal(Vector3 origin, Vector3 velocity)
        {
            var t = new Throw { Seq = ++throwSeq, Thrower = LocalId, Origin = origin, Velocity = velocity };
            if (IsHost) HostThrow(t);
            else
            {
                pendingThrows.Add((t, Time.unscaledTime));
                Grenade.Spawn(LocalId, t.Seq, origin, velocity, false);
            }
        }

        /// <summary>Host: a bot throws (seq numbers from a range of their own).</summary>
        public void BotThrow(byte bot, Vector3 origin, Vector3 velocity)
        {
            if (!IsHost || !Players.TryGetValue(bot, out var p)) return;
            HostThrow(new Throw { Seq = ++p.LastThrowSeq, Thrower = bot, Origin = origin, Velocity = velocity });
        }

        private void HostThrow(Throw t)
        {
            if (Phase != Phase.Playing || KillCam.Active || !Players.TryGetValue(t.Thrower, out var p) || !p.Alive) return;
            Grenade.Spawn(t.Thrower, t.Seq, t.Origin, Vector3.ClampMagnitude(t.Velocity, 25f), true);
            AddNade(new NadeEvent { Thrower = t.Thrower, ThrowSeq = t.Seq, Kind = 0, A = t.Origin, B = Vector3.ClampMagnitude(t.Velocity, 25f) });
        }

        private void AddNade(NadeEvent n)
        {
            n.Seq = ++nadeSeq;
            nadeRing.Add(n);
            if (nadeRing.Count > 12) nadeRing.RemoveAt(0);
        }

        /// <summary>
        /// Host: the blast. Full damage up to 3 m, falling off to nothing at 8.5 m; walls and cover
        /// between the grenade and the body (and the head) protect beyond 4.5 m; lying down takes 30% less; the
        /// thrower takes 60% of the damage from their own grenade; no team damage.
        /// </summary>
        public void HostExplode(Grenade g)
        {
            var pos = g.transform.position;
            AddNade(new NadeEvent { Thrower = g.Thrower, ThrowSeq = g.ThrowSeq, Kind = 1, A = pos });
            Effects.Explosion(pos);
            Destroy(g.gameObject);
            if (Phase != Phase.Playing || !Players.TryGetValue(g.Thrower, out var thrower)) return;
            var blast = pos + Vector3.up * 0.25f;
            foreach (var victim in Players.Values.ToList())
            {
                if (!victim.Alive) continue;
                var body = victim.Pos + Vector3.up * (victim.Prone ? 0.3f : victim.Crouch ? 0.8f : 1.1f);
                var head = victim.Pos + Vector3.up * (victim.Prone ? 0.4f : victim.Crouch ? 1.15f : 1.6f);
                float amount = Grenade.DamageAt(Vector3.Distance(blast, body));
                if (amount <= 0) continue;
                bool exposed = !Physics.Linecast(blast, body, World.EnvMask, QueryTriggerInteraction.Ignore)
                               || !Physics.Linecast(blast, head, World.EnvMask, QueryTriggerInteraction.Ignore);
                // close to the blast nothing protects (fragments and the pressure go through thin walls)
                if (!exposed && Vector3.Distance(blast, body) > Grenade.ThroughWalls) continue;
                if (victim.Prone) amount *= 0.7f;
                if (victim == thrower) amount *= 0.6f;
                Damage(thrower, victim, Weapons.Grenade, false, amount);
            }
        }

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
