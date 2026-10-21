using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UA
{
    public struct TraceResult
    {
        public Vector3 Point;
        public float Distance;
        public byte Kind;   // 0 wall, 1 player, 2 nothing
        public byte Victim;
        public bool Head;
    }

    /// <summary>One playable map in the scene: its root object, spawn points and bot waypoint graph.</summary>
    [System.Serializable]
    public class MapLayout
    {
        public string Name;
        public GameObject Root;
        public Vector3[] Spawns = new Vector3[0];
        public Vector3[] Waypoints = new Vector3[0];
        public Vector2Int[] Links = new Vector2Int[0];
        public Vector3 Center;
        public float Radius = 80f;
    }

    /// <summary>
    /// The map (spawn points, bot waypoints, lighting presets) and the agents of the current match.
    /// The map geometry itself is built by the editor (MapBuilder) into the scene.
    /// </summary>
    public class World : MonoBehaviour
    {
        public static World I;

        public const int HitboxLayer = 8;
        public const int AgentLayer = 10;
        public const int FenceLayer = 11;   // blocks players, not bullets or sight
        public const int EnvMask = 1 << 0;
        public const int ShotMask = EnvMask | (1 << HitboxLayer);

        [Header("Filled by MapBuilder")]
        public MapLayout[] Layouts = new MapLayout[0];
        public Light Sun;
        public Material Skybox;
        public Renderer[] NightLamps = new Renderer[0];

        public readonly Dictionary<byte, Agent> Agents = new Dictionary<byte, Agent>();
        public List<int>[] Neighbours;
        private GameObject[] agentPrefabs;
        private int appliedMap = -1;
        private MapLayout layout;

        public MapLayout Layout => layout;
        public Vector3[] Spawns => layout.Spawns;
        public Vector3[] Waypoints => layout.Waypoints;
        public Vector3 Center => layout.Center;
        public float Radius => layout.Radius;

        /// <summary>Map index (as in MatchConfig.MapNames) to layout: 0-2 downtown, 3-4 port.</summary>
        public static int LayoutOf(int map) => map >= 3 ? 1 : 0;

        private void Awake()
        {
            I = this;
            agentPrefabs = new[] { Resources.Load<GameObject>("Agent"), Resources.Load<GameObject>("Agent_F") }.Where(p => p != null).ToArray();
            ApplyLighting(0);
        }

        private void UseLayout(int index)
        {
            index = Mathf.Clamp(index, 0, Layouts.Length - 1);
            for (int i = 0; i < Layouts.Length; i++)
                if (Layouts[i].Root != null) Layouts[i].Root.SetActive(i == index);
            layout = Layouts[index];
            Neighbours = new List<int>[layout.Waypoints.Length];
            for (int i = 0; i < Neighbours.Length; i++) Neighbours[i] = new List<int>();
            foreach (var l in layout.Links)
            {
                Neighbours[l.x].Add(l.y);
                Neighbours[l.y].Add(l.x);
            }
        }

        private void Update()
        {
            var s = Session.I;
            if (s == null) return;
            if (appliedMap != s.Config.Map && s.Active) ApplyLighting(s.Config.Map);
            if (!s.InMatch)
            {
                if (Agents.Count > 0) Clear();
                return;
            }
            foreach (var p in s.Players.Values)
                if (!Agents.ContainsKey(p.Id))
                    Create(p, s);
            foreach (var id in Agents.Keys.Where(id => !s.Players.ContainsKey(id)).ToList())
            {
                Destroy(Agents[id].gameObject);
                Agents.Remove(id);
            }
            foreach (var p in s.Players.Values)
                Agents[p.Id].Sync(p, s);
        }

        private void Create(PlayerState p, Session s)
        {
            // every player gets a stable look: body (male / female mannequin), outfit and camo
            int look = p.Look % 32;
            var prefab = agentPrefabs[(look / 16) % agentPrefabs.Length];
            var go = Instantiate(prefab, p.Pos, Quaternion.Euler(0, p.Yaw, 0));
            go.name = "Agent " + p.Name;
            var agent = go.GetComponent<Agent>();
            bool local = p.Id == s.LocalId;
            bool bot = p.IsBot && s.IsHost;
            agent.Init(p, local, bot, TeamColor(p, s));
            agent.SetLook(look);
            Agents[p.Id] = agent;
        }

        public void Clear()
        {
            foreach (var a in Agents.Values)
                if (a != null) Destroy(a.gameObject);
            Agents.Clear();
            if (CameraRig.I != null) CameraRig.I.Target = null;
        }

        public static Color TeamColor(PlayerState p, Session s)
        {
            if (s.Config.Mode == Mode.TeamDeathmatch)
                return p.Team == 0 ? new Color(0.18f, 0.42f, 0.95f) : new Color(0.92f, 0.22f, 0.18f);
            var palette = new[]
            {
                new Color(0.95f, 0.75f, 0.15f), new Color(0.2f, 0.8f, 0.4f), new Color(0.6f, 0.3f, 0.9f), new Color(0.95f, 0.45f, 0.1f),
                new Color(0.15f, 0.75f, 0.85f), new Color(0.9f, 0.3f, 0.6f), new Color(0.55f, 0.55f, 0.6f), new Color(0.4f, 0.25f, 0.15f),
            };
            return palette[p.Id % palette.Length];
        }

        public bool IsEnemy(PlayerState a, PlayerState b, Session s)
        {
            if (a == b) return false;
            return s.Config.Mode == Mode.FreeForAll || a.Team != b.Team;
        }

        /// <summary>Ray against walls and agent hitboxes, ignoring the shooter.</summary>
        public TraceResult TraceShot(Vector3 origin, Vector3 dir, float range, byte shooter)
        {
            var hits = Physics.RaycastAll(origin, dir, range, ShotMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var box = h.collider.GetComponent<Hitbox>();
                if (box != null)
                {
                    if (box.Owner == null || box.Owner.Id == shooter || !box.Owner.Alive) continue;
                    return new TraceResult { Point = h.point, Distance = h.distance, Kind = 1, Victim = box.Owner.Id, Head = box.Head };
                }
                if (h.collider.isTrigger) continue;
                return new TraceResult { Point = h.point, Distance = h.distance, Kind = 0, Victim = Session.NoPlayer };
            }
            return new TraceResult { Point = origin + dir * range, Distance = range, Kind = 2, Victim = Session.NoPlayer };
        }

        public bool LineOfSight(Vector3 from, Vector3 to) => !Physics.Linecast(from, to, EnvMask, QueryTriggerInteraction.Ignore);

        /// <summary>Host: spawn point far from enemies, on the player's side of the map in team mode.</summary>
        public (Vector3, float) PickSpawn(PlayerState p, Session s)
        {
            if (Spawns.Length == 0) return (Center, 0f);
            var enemies = s.Players.Values.Where(o => o.Alive && IsEnemy(p, o, s)).Select(o => o.Pos).ToList();
            float best = float.MinValue;
            Vector3 chosen = Spawns[0];
            foreach (var sp in Spawns)
            {
                float score = Random.Range(0f, 6f);
                if (s.Config.Mode == Mode.TeamDeathmatch)
                    score += (p.Team == 0 ? -sp.x : sp.x) * 0.6f;
                float nearest = enemies.Count == 0 ? 60f : enemies.Min(e => Vector3.Distance(e, sp));
                score += Mathf.Min(nearest, 60f);
                if (score > best) { best = score; chosen = sp; }
            }
            var toCenter = Center - chosen;
            float yaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
            return (chosen + Vector3.up * 0.1f, yaw);
        }

        public int NearestWaypoint(Vector3 pos)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Waypoints.Length; i++)
            {
                float d = (Waypoints[i] - pos).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        /// <summary>Map presets: the same city in daylight, at sunset or at night.</summary>
        public void ApplyLighting(int map)
        {
            appliedMap = map;
            if (Layouts.Length > 0) UseLayout(LayoutOf(map));
            if (Sun == null) return;
            int preset = map < 3 ? map : map == 3 ? 0 : 1;
            bool night = preset == 2;
            switch (preset)
            {
                case 1:
                    Sun.transform.rotation = Quaternion.Euler(14f, -60f, 0f);
                    Sun.color = new Color(1f, 0.62f, 0.38f);
                    Sun.intensity = 1.25f;
                    RenderSettings.ambientSkyColor = new Color(0.55f, 0.45f, 0.5f);
                    RenderSettings.ambientEquatorColor = new Color(0.5f, 0.38f, 0.32f);
                    RenderSettings.ambientGroundColor = new Color(0.18f, 0.14f, 0.12f);
                    RenderSettings.fogColor = new Color(0.78f, 0.55f, 0.42f);
                    RenderSettings.fogDensity = 0.009f;
                    break;
                case 2:
                    Sun.transform.rotation = Quaternion.Euler(35f, 140f, 0f);
                    Sun.color = new Color(0.55f, 0.65f, 1f);
                    Sun.intensity = 0.35f;
                    RenderSettings.ambientSkyColor = new Color(0.12f, 0.15f, 0.26f);
                    RenderSettings.ambientEquatorColor = new Color(0.1f, 0.11f, 0.16f);
                    RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.06f);
                    RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.1f);
                    RenderSettings.fogDensity = 0.016f;
                    break;
                default:
                    Sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                    Sun.color = new Color(1f, 0.95f, 0.88f);
                    Sun.intensity = 1.2f;
                    RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.8f);
                    RenderSettings.ambientEquatorColor = new Color(0.45f, 0.47f, 0.5f);
                    RenderSettings.ambientGroundColor = new Color(0.2f, 0.19f, 0.17f);
                    RenderSettings.fogColor = new Color(0.7f, 0.76f, 0.84f);
                    RenderSettings.fogDensity = 0.006f;
                    break;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.sun = Sun;
            if (Skybox != null)
            {
                RenderSettings.skybox = Skybox;
                Skybox.SetFloat("_Exposure", night ? 0.15f : map == 1 ? 1.0f : 1.25f);
                Skybox.SetFloat("_AtmosphereThickness", map == 1 ? 1.6f : 1f);
            }
            foreach (var lamp in NightLamps)
                if (lamp != null) lamp.enabled = night;
            if (Camera.main != null) Camera.main.backgroundColor = RenderSettings.fogColor;
        }
    }
}
