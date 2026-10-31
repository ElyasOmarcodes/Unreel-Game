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
        public bool Sacred;   // stopped by a board not to be shot at (Sacred)
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
        /// <summary>The scene the map lives in (loaded while it is played; empty: Root is in this scene).</summary>
        public string Scene = "";
        /// <summary>The map's own sky by day (null: the procedural sky).</summary>
        public Material Sky;
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

        /// <summary>Map index (as in MatchConfig.MapNames) to layout: three each (day, sunset, night) for CityLord (STEEL CITY), Al-Zahra, the village and the old town.</summary>
        public static int LayoutOf(int map) => map / 3;
        public const int CityLordLayout = 0, ZahraLayout = 1;

        private void Awake()
        {
            I = this;
            agentPrefabs = new[] { Resources.Load<GameObject>("Agent") };
            ApplyLighting(0);
        }

        private MapScene loadedMap;
        private int wantedLayout = -1;
        private bool night;

        /// <summary>The map being played is in memory and ready (ground, walls, spawns).</summary>
        public bool MapReady => wantedLayout >= 0 && (string.IsNullOrEmpty(Layouts[wantedLayout].Scene) || (loadedMap != null && loadedMap.Layout == wantedLayout));

        /// <summary>
        /// One map in memory at a time: the other map scenes are unloaded (with what only they used)
        /// and the wanted one is loaded in the background.
        /// </summary>
        private void LoadMap(int index)
        {
            // phones with little memory let the old map go at once; the others keep showing it (no empty
            // background in Create Group) until the new one is there
            if (Settings.DeviceClass == 0 || Session.I == null || Session.I.InMatch) UnloadOthers(index);
            else if (loadedMap != null && loadedMap.Layout != index) loadedMap = null;
            string wanted = Layouts[index].Scene;
            if (string.IsNullOrEmpty(wanted)) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(wanted);
            if (!scene.IsValid())   // neither loaded nor on its way
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(wanted, UnityEngine.SceneManagement.LoadSceneMode.Additive);
        }

        private void UnloadOthers(int index)
        {
            for (int i = 0; i < Layouts.Length; i++)
            {
                if (i == index || string.IsNullOrEmpty(Layouts[i].Scene)) continue;
                var other = UnityEngine.SceneManagement.SceneManager.GetSceneByName(Layouts[i].Scene);
                if (!other.isLoaded) continue;
                if (loadedMap != null && loadedMap.Layout == i) loadedMap = null;
                foreach (var go in other.GetRootGameObjects()) go.SetActive(false);   // gone from view this frame
                var op = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(other);
                if (op != null) op.completed += _ => Resources.UnloadUnusedAssets();
            }
        }

        public void OnMapLoaded(MapScene map)
        {
            if (map.Layout != wantedLayout)
            {
                // arrived after the players chose another map: out again
                UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(map.gameObject.scene);
                return;
            }
            UnloadOthers(map.Layout);   // the old map stayed on screen until now
            loadedMap = map;
            foreach (var light in map.NightLights)
                if (light != null) light.enabled = night;
            foreach (var lamp in map.Lamps)
                if (lamp != null) lamp.enabled = night;
        }

        private void UseLayout(int index)
        {
            index = Mathf.Clamp(index, 0, Layouts.Length - 1);
            for (int i = 0; i < Layouts.Length; i++)
                if (Layouts[i].Root != null) Layouts[i].Root.SetActive(i == index);
            if (index != wantedLayout)
            {
                wantedLayout = index;
                LoadMap(index);
            }
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
            if (!MapReady) return;   // the map is still loading: nobody stands on air
            // a body appears only once the host has placed it in this match: never dead at 0,0,0 first
            foreach (var p in s.Players.Values)
                if (!Agents.ContainsKey(p.Id) && p.Spawned)
                    Create(p, s);
            foreach (var id in Agents.Keys.Where(id => !s.Players.ContainsKey(id)).ToList())
            {
                Destroy(Agents[id].gameObject);
                Agents.Remove(id);
            }
            foreach (var p in s.Players.Values)
                if (Agents.TryGetValue(p.Id, out var agent)) agent.Sync(p, s);
        }

        private void Create(PlayerState p, Session s)
        {
            // every player is dressed as they built their character (Wardrobe)
            int look = p.Look;
            var prefab = agentPrefabs[0];
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
            if (p.Team > 0) return MatchConfig.TeamColor(p.Team);
            var palette = new[]
            {
                new Color(0.95f, 0.75f, 0.15f), new Color(0.2f, 0.8f, 0.4f), new Color(0.6f, 0.3f, 0.9f), new Color(0.95f, 0.45f, 0.1f),
                new Color(0.15f, 0.75f, 0.85f), new Color(0.9f, 0.3f, 0.6f), new Color(0.55f, 0.55f, 0.6f), new Color(0.4f, 0.25f, 0.15f),
            };
            return palette[p.Id % palette.Length];
        }

        public bool IsEnemy(PlayerState a, PlayerState b, Session s)
        {
            return a != b && !MatchConfig.Mates(a, b);
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
                // a board that is not to be shot at: the bullet stops on it (the host turns it on the shooter)
                if (h.collider.isTrigger && h.collider.GetComponent<Sacred>() != null)
                    return new TraceResult { Point = h.point, Distance = h.distance, Kind = 0, Victim = Session.NoPlayer, Sacred = true };
                if (h.collider.isTrigger) continue;
                return new TraceResult { Point = h.point, Distance = h.distance, Kind = 0, Victim = Session.NoPlayer };
            }
            return new TraceResult { Point = origin + dir * range, Distance = range, Kind = 2, Victim = Session.NoPlayer };
        }

        public bool LineOfSight(Vector3 from, Vector3 to) => !Physics.Linecast(from, to, EnvMask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// Host: one of the map's spawn points (Steel City: its seven), picked fully at random; only a point
        /// with an enemy right beside it is left out while another is free. Two players never come in on top
        /// of each other: a point already taken gives a free place a step or two beside it.
        /// </summary>
        public (Vector3, float) PickSpawn(PlayerState p, Session s)
        {
            if (Spawns.Length == 0) return (Center, 0f);
            var others = s.Players.Values.Where(o => o != p && o.Alive && o.Spawned).ToList();
            var free = Spawns.Where(sp => !others.Any(o => IsEnemy(p, o, s) && Vector3.Distance(o.Pos, sp) < 10f)).ToList();
            if (free.Count == 0) free = Spawns.ToList();
            // the least crowded first among equals: fully random, but a point nobody stands on wins over a taken one
            int Crowd(Vector3 sp) => others.Count(o => Vector3.Distance(o.Pos, sp) < 2.5f);
            int least = free.Min(Crowd);
            var pick = free.Where(sp => Crowd(sp) == least).ToList();
            var chosen = pick[Random.Range(0, pick.Count)];
            chosen = FreeSpot(chosen, others);
            var toCenter = Center - chosen;
            float yaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg + Random.Range(-40f, 40f);
            return (chosen + Vector3.up * 0.1f, yaw);
        }

        /// <summary>The point itself, or the nearest place around it (rings of 1.2 m) clear of players and walls.</summary>
        private static Vector3 FreeSpot(Vector3 point, List<PlayerState> others)
        {
            bool Clear(Vector3 at) =>
                !others.Any(o => Vector3.Distance(new Vector3(o.Pos.x, 0, o.Pos.z), new Vector3(at.x, 0, at.z)) < 1.1f && Mathf.Abs(o.Pos.y - at.y) < 2f)
                && !Physics.CheckCapsule(at + Vector3.up * 0.5f, at + Vector3.up * 1.5f, 0.35f, EnvMask, QueryTriggerInteraction.Ignore);
            if (Clear(point)) return point;
            for (int ring = 1; ring <= 3; ring++)
                for (int k = 0; k < 8 * ring; k++)
                {
                    float a = (k + 0.5f * ring) / (8f * ring) * Mathf.PI * 2f;
                    var at = point + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (1.2f * ring);
                    // on the same ground: down onto it from a little above
                    if (Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out var hit, 2.5f, EnvMask, QueryTriggerInteraction.Ignore)) at = hit.point;
                    else continue;
                    if (Mathf.Abs(at.y - point.y) < 0.6f && Clear(at)) return at;
                }
            return point;
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
            int preset = map % 3;
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
                    // moonlit night as in the reference: lavender walls, a deep blue-violet sky, warm lamps and windows
                    Sun.transform.rotation = Quaternion.Euler(42f, 140f, 0f);
                    Sun.color = new Color(0.66f, 0.6f, 1f);
                    Sun.intensity = 0.85f;
                    RenderSettings.ambientSkyColor = new Color(0.56f, 0.5f, 0.92f);
                    RenderSettings.ambientEquatorColor = new Color(0.5f, 0.44f, 0.86f);
                    RenderSettings.ambientGroundColor = new Color(0.27f, 0.23f, 0.42f);
                    RenderSettings.fogColor = new Color(0.1f, 0.09f, 0.26f);
                    RenderSettings.fogDensity = 0.0065f;
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
                    if (LayoutOf(map) == ZahraLayout)
                    {
                        // Al-Zahra in spring: warm, soft Andalusian light, a clear sky and a light haze over the valley
                        Sun.transform.rotation = Quaternion.Euler(52f, -150f, 0f);
                        Sun.color = new Color(1f, 0.93f, 0.8f);
                        Sun.intensity = 1.25f;
                        RenderSettings.ambientSkyColor = new Color(0.6f, 0.7f, 0.86f);
                        RenderSettings.ambientEquatorColor = new Color(0.56f, 0.53f, 0.48f);
                        RenderSettings.ambientGroundColor = new Color(0.3f, 0.26f, 0.2f);
                        RenderSettings.fogColor = new Color(0.8f, 0.84f, 0.9f);
                        RenderSettings.fogDensity = 0.0045f;
                    }
                    break;
            }
            // CityLord by day as in its own scene: its sun, a flat ambient light, no fog, the six-sided sky
            bool cityLord = LayoutOf(map) == CityLordLayout;
            bool ownSky = cityLord && preset == 0;
            if (ownSky)
            {
                Sun.transform.rotation = new Quaternion(0.4082179f, -0.2345697f, 0.1093816f, 0.8754261f);
                Sun.color = new Color(1f, 0.9568627f, 0.8392157f);
                Sun.intensity = 1.2f;
                RenderSettings.ambientSkyColor = new Color(0.7311321f, 0.8207548f, 1f);
            }
            Sun.shadowStrength = ownSky ? 1f : 0.85f;
            RenderSettings.ambientMode = ownSky ? UnityEngine.Rendering.AmbientMode.Flat : UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = !ownSky;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.sun = Sun;
            if (ownSky && layout != null && layout.Sky != null) RenderSettings.skybox = layout.Sky;
            else if (Skybox != null)
            {
                RenderSettings.skybox = Skybox;
                Skybox.SetFloat("_Exposure", night ? 0.3f : preset == 1 ? 1.0f : 1.25f);
                Skybox.SetFloat("_AtmosphereThickness", preset == 1 ? 1.6f : 1f);
                Skybox.SetColor("_SkyTint", night ? new Color(0.36f, 0.3f, 0.8f) : new Color(0.5f, 0.55f, 0.62f));
            }
            this.night = night;
            if (loadedMap != null)
            {
                foreach (var light in loadedMap.NightLights)
                    if (light != null) light.enabled = night;
                foreach (var lamp in loadedMap.Lamps)
                    if (lamp != null) lamp.enabled = night;
            }
            if (Camera.main != null)
            {
                Camera.main.backgroundColor = RenderSettings.fogColor;
                // the CityLord ground reaches 250 m around the town: seen to its end without fog
                Camera.main.farClipPlane = cityLord ? 450f : 260f;
            }
        }
    }
}
