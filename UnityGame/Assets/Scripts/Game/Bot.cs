using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// Host-side AI, played like a person. It finds its way over the map's walk graph (the waypoints and
    /// their links: streets, yards, stairs, ladders and roofs) with A*, so it takes the stairs and the ways
    /// round instead of walking into walls, and it steers off a wall it brushes. It knows where the fight is:
    /// what it sees, the shots it hears and where a hit came from; it hunts an enemy it lost sight of where
    /// it was last seen, goes now and then towards where the enemies are, and runs to cover when badly hurt.
    /// In a fight it mostly stands (or kneels at range) and shoots short bursts, and moves now and then to
    /// another spot, never into a wall or off a roof.
    /// </summary>
    public class Bot : MonoBehaviour
    {
        private Agent agent;
        private CharacterController cc;
        private float verticalSpeed, nextThink, nextFire, reloadEnd, strafeUntil, strafeDir = 1f, reactionAt;
        private float pauseUntil, lookAround, burstLeft;
        private bool repositioning, kneel;
        private int mag, grenades;
        private float nextHeal;
        private float nextGrenade;
        private Agent target;
        private float yaw, pitch;
        private System.Random rng;

        private enum Mode { Patrol, Hunt, Cover }
        private Mode mode;
        private readonly List<int> path = new List<int>();
        private int pathAt, goal = -1;
        private bool run;
        // where an enemy was last seen or heard, and when
        private Vector3 lastKnown;
        private float lastKnownTime = -99f;
        private float lastHealth = 100f, coverUntil;
        // getting stuck: where it was a moment ago
        private Vector3 checkPos;
        private float checkTime;
        private int stuckCount;

        private float Skill => Session.I.Config.BotSkill; // 0 easy, 1 normal, 2 hard
        private const int WallMask = World.EnvMask | (1 << World.FenceLayer);

        private void Awake()
        {
            agent = GetComponent<Agent>();
            cc = GetComponent<CharacterController>();
            rng = new System.Random(Random.Range(0, int.MaxValue));
        }

        private void Start() => OnRespawn();

        public void OnRespawn()
        {
            target = null;
            mag = Weapons.Get(agent.State.Primary).MagSize;
            grenades = Grenade.PerLife;
            nextGrenade = Time.time + Random.Range(4f, 10f);
            yaw = agent.State.Yaw;
            pitch = 0;
            mode = Mode.Patrol;
            path.Clear();
            goal = -1;
            lastKnownTime = -99f;
            lastHealth = agent.State.Health;
            kneel = false;
            stuckCount = 0;
            checkPos = transform.position;
            checkTime = Time.time;
        }

        private void Update()
        {
            var s = Session.I;
            var st = agent.State;
            var world = World.I;
            if (s == null || st == null || world == null || !st.Alive || !cc.enabled) return;
            float dt = Time.deltaTime;
            bool playing = s.Phase == Phase.Playing && !KillCam.Active;
            var def = Weapons.Get(st.Primary);
            st.Weapon = def.Id;
            // nobody in the sights and a badly wounded team mate next to it: blood for the team mate
            if (playing && target == null && Time.time >= nextHeal && st.Team > 0)
            {
                nextHeal = Time.time + 1f;
                foreach (var a in world.Agents.Values)
                {
                    if (a == null || a == agent || a.State == null || !a.State.Alive || !MatchConfig.Mates(a.State, st) || a.State.Health >= Session.BloodThreshold) continue;
                    if ((a.transform.position - transform.position).sqrMagnitude > Session.HealRange * Session.HealRange) continue;
                    nextHeal = Time.time + Session.BloodCooldown;
                    agent.PlayReload();
                    agent.ShowSyringe(1, 2f);
                    s.BotGiveBlood(agent.Id, a.Id);
                    break;
                }
            }
            if (mag <= 0 && reloadEnd <= 0)
            {
                reloadEnd = Time.time + def.ReloadTime;
                agent.PlayReload();
                Sfx.Step(Weapons.ReloadSound(def), transform.position + Vector3.up, 0.8f, false);
            }
            if (reloadEnd > 0 && Time.time >= reloadEnd) { reloadEnd = 0; mag = def.MagSize; }

            // ---- what it knows: seen, heard, hit
            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.25f;
                var previous = target;
                target = FindTarget(s, world);
                if (target != null)
                {
                    Know(target.transform.position);
                    if (target != previous)
                    {
                        reactionAt = Time.time + Mathf.Lerp(1.3f, 0.55f, Skill / 2f) + Random.Range(0f, 0.3f);
                        repositioning = false;
                        strafeUntil = Time.time + Random.Range(1.5f, 3f);
                        if (mode != Mode.Cover) mode = Mode.Patrol;
                    }
                }
                else Listen(s, world);
            }
            if (st.Health < lastHealth - 0.5f)
            {
                // hit: it knows where from (the nearest enemy shooting), and badly hurt it runs for cover
                var shooter = NearestEnemy(s, world, 70f, true);
                if (shooter != null)
                {
                    Know(shooter.transform.position);
                    if (target == null) { lookAround = Angle(shooter.transform.position - transform.position); pauseUntil = 0f; }
                }
                if (st.Health < 40f && mode != Mode.Cover && Random.value < 0.5f + 0.2f * Skill) TakeCover(world);
            }
            lastHealth = st.Health;

            Vector3 move = Vector3.zero;   // where it walks (world, length 0.5 walking, 1 running)
            st.Crouch = false;
            if (!playing)
            {
                path.Clear();
            }
            else if (mode == Mode.Cover)
            {
                move = Follow(world, 1f);
                if (move == Vector3.zero)
                {
                    // in cover: down on a knee for a moment, facing where the enemy was, then back to it
                    st.Crouch = true;
                    yaw = Mathf.MoveTowardsAngle(yaw, Angle(lastKnown - transform.position), 200f * dt);
                    if (coverUntil <= 0f) coverUntil = Time.time + Random.Range(2.5f, 4.5f);
                    if (Time.time >= coverUntil) { mode = Mode.Hunt; coverUntil = 0f; path.Clear(); }
                }
                else Face(move, dt, 260f);
            }
            else if (target != null)
            {
                var toTarget = target.Chest - agent.Eye;
                float distance = toTarget.magnitude;
                float wantYaw = Angle(toTarget);
                float wantPitch = Mathf.Asin(Mathf.Clamp(toTarget.y / Mathf.Max(distance, 0.01f), -1f, 1f)) * Mathf.Rad2Deg;
                float turn = Mathf.Lerp(90f, 220f, Skill / 2f) * dt;
                yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, turn);
                pitch = Mathf.MoveTowards(pitch, wantPitch, turn);
                // stand (or kneel at range) and shoot for a while, then a short walk to the side and stop again
                if (Time.time > strafeUntil)
                {
                    repositioning = !repositioning;
                    strafeUntil = Time.time + (repositioning ? Random.Range(0.6f, 1.3f) : Random.Range(1.8f, 4f));
                    strafeDir = Random.value < 0.5f ? -1f : 1f;
                    kneel = !repositioning && distance > 14f && Random.value < 0.3f + 0.15f * Skill;
                }
                var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                float ideal = def.Pellets > 1 ? 8f : def.Scope ? 35f : 16f;
                if (repositioning)
                {
                    // never into a wall or off an edge: the other side then, or only nearer / further
                    var side = right * strafeDir;
                    if (Blocked(side, 1.4f)) { strafeDir = -strafeDir; side = -side; }
                    if (Blocked(side, 1.4f)) side = Vector3.zero;
                    var along = forward * Mathf.Clamp((distance - ideal) / 6f, -0.6f, 1f);
                    if (Blocked(along.normalized, 1.2f)) along = Vector3.zero;
                    var want = side * 0.8f + along;
                    move = want.sqrMagnitude > 0.01f ? want.normalized * 0.5f : Vector3.zero;
                }
                st.Crouch = kneel && !repositioning;
                bool aimed = Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < 5f;
                if (aimed && !repositioning && Time.time >= reactionAt && Time.time >= nextFire && reloadEnd <= 0 && mag > 0 && distance < def.Range * 0.8f)
                    Shoot(s, def, toTarget, distance);
                // now and then a grenade at a target in the middle distance
                if (grenades > 0 && Time.time >= nextGrenade && distance > 9f && distance < 24f && Time.time >= reactionAt)
                {
                    nextGrenade = Time.time + Random.Range(6f, 14f);
                    if (Random.value < 0.25f + 0.15f * Skill) ThrowAt(s, target.transform.position);
                }
                path.Clear();
            }
            else if (world.Waypoints.Length > 0)
            {
                // no enemy in sight: after the one last seen or heard, else round the map (towards the fight)
                bool hunting = Time.time - lastKnownTime < 12f;
                if (hunting && mode != Mode.Hunt) { mode = Mode.Hunt; path.Clear(); }
                if (!hunting && mode == Mode.Hunt) { mode = Mode.Patrol; path.Clear(); pauseUntil = Time.time + Random.Range(1f, 2.5f); lookAround = yaw + Random.Range(-120f, 120f); }
                if (path.Count == 0 && Time.time >= pauseUntil)
                {
                    int from = Nearest(world, transform.position);
                    goal = mode == Mode.Hunt ? Nearest(world, lastKnown) : PatrolGoal(s, world, from);
                    run = mode == Mode.Hunt || Random.value < 0.45f;
                    if (from >= 0 && goal >= 0) FindPath(world, from, goal);
                }
                if (Time.time < pauseUntil)
                {
                    // now and then a stop to look around
                    yaw = Mathf.MoveTowardsAngle(yaw, lookAround, 60f * dt);
                    if (Mathf.Abs(Mathf.DeltaAngle(yaw, lookAround)) < 2f) lookAround = yaw + Random.Range(-90f, 90f);
                    pitch = Mathf.MoveTowards(pitch, 0, 60f * dt);
                }
                else
                {
                    move = Follow(world, run ? 1f : 0.5f);
                    pitch = Mathf.MoveTowards(pitch, 0, 60f * dt);
                    // close to where the enemy was: look that way, not down the path
                    if (mode == Mode.Hunt && (lastKnown - transform.position).sqrMagnitude < 15f * 15f && world.LineOfSight(agent.Eye, lastKnown + Vector3.up * 1.2f))
                        yaw = Mathf.MoveTowardsAngle(yaw, Angle(lastKnown - transform.position), 220f * dt);
                    else if (move != Vector3.zero) Face(move, dt, 200f);
                    if (path.Count == 0)
                    {
                        if (mode == Mode.Hunt) lastKnownTime = -99f;   // there, and nobody: back to the round
                        if (Random.value < 0.4f) { pauseUntil = Time.time + Random.Range(1.2f, 3.5f); lookAround = yaw + Random.Range(-110f, 110f); }
                    }
                }
            }

            // stuck against something while wanting to go: another way from where it stands, then another goal
            if (move.sqrMagnitude > 0.01f)
            {
                if (Time.time - checkTime > 1.2f)
                {
                    if ((transform.position - checkPos).sqrMagnitude < 0.25f * 0.25f)
                    {
                        stuckCount++;
                        if (stuckCount >= 2) { goal = -1; stuckCount = 0; }
                        int from = Nearest(world, transform.position);
                        path.Clear();
                        if (from >= 0 && goal >= 0) FindPath(world, from, goal);
                    }
                    else stuckCount = 0;
                    checkPos = transform.position;
                    checkTime = Time.time;
                }
            }
            else { checkPos = transform.position; checkTime = Time.time; }

            float pace = Mathf.Min(1f, move.magnitude);
            var dir = move.sqrMagnitude > 1e-4f ? move.normalized : Vector3.zero;
            float speed = (st.Crouch ? LocalPlayer.CrouchSpeed : LocalPlayer.RunSpeed) * def.MoveSpeed * pace;
            if (cc.isGrounded) verticalSpeed = -2f;
            var velocity = dir * speed;
            // up a ladder towards a waypoint above (the ladder routes of the map)
            var ladder = Ladder.At(transform.position + Vector3.up * 0.9f);
            bool climbing = ladder != null && pathAt < path.Count && world.Waypoints[path[pathAt]].y > transform.position.y + 0.4f;
            if (climbing)
            {
                verticalSpeed = ladder.Rate;
                if (transform.position.y < ladder.Top - 0.1f) velocity = ladder.Steer(transform.position, verticalSpeed, 0.3f);
            }
            else verticalSpeed -= 20f * dt;
            cc.Move((velocity + Vector3.up * verticalSpeed) * dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);

            // the animation: which way it walks against where it faces, and how fast (0.5 walking, 1 running)
            var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var rgt = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            st.MoveX = Vector3.Dot(dir, rgt) * pace;
            st.MoveY = Vector3.Dot(dir, fwd) * pace;
            st.Pos = transform.position;
            st.Yaw = yaw;
            st.Pitch = pitch;
            st.Grounded = cc.isGrounded;
        }

        private static float Angle(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        private void Face(Vector3 move, float dt, float rate) => yaw = Mathf.MoveTowardsAngle(yaw, Angle(move), rate * dt);

        private void Know(Vector3 where)
        {
            lastKnown = where;
            lastKnownTime = Time.time;
        }

        /// <summary>Shots heard: an enemy firing within 35 m (60 m for a sniper) is placed there.</summary>
        private void Listen(Session s, World world)
        {
            var shooter = NearestEnemy(s, world, 45f, true);
            if (shooter == null) return;
            float d = Vector3.Distance(shooter.transform.position, transform.position);
            bool loud = shooter.State != null && Weapons.Get(shooter.State.Weapon).Scope;
            if (d < 35f || loud && d < 60f) Know(shooter.transform.position);
        }

        private Agent NearestEnemy(Session s, World world, float within, bool firing)
        {
            Agent best = null;
            float bestDistance = within;
            foreach (var other in world.Agents.Values)
            {
                if (other == null || other == agent || other.State == null || !other.State.Alive || !world.IsEnemy(agent.State, other.State, s)) continue;
                if (firing && !other.Firing) continue;
                float d = Vector3.Distance(other.transform.position, transform.position);
                if (d < bestDistance) { best = other; bestDistance = d; }
            }
            return best;
        }

        /// <summary>A wall, a fence or a drop (no ground a step ahead) that way within 'reach'.</summary>
        private bool Blocked(Vector3 dir, float reach)
        {
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.y = 0;
            dir.Normalize();
            var from = transform.position + Vector3.up * 0.9f;
            if (Physics.SphereCast(from, 0.3f, dir, out _, reach, WallMask, QueryTriggerInteraction.Ignore)) return true;
            var ahead = transform.position + dir * reach + Vector3.up * 0.6f;
            return !Physics.Raycast(ahead, Vector3.down, 1.8f, World.EnvMask, QueryTriggerInteraction.Ignore);
        }

        // ---------------------------------------------------------------- the way

        /// <summary>The nearest waypoint it can walk to straight (nothing in between), else the nearest.</summary>
        private static int Nearest(World world, Vector3 pos)
        {
            var w = world.Waypoints;
            if (w.Length == 0) return -1;
            int nearest = -1, clear = -1;
            float bestAny = float.MaxValue, bestClear = float.MaxValue;
            for (int i = 0; i < w.Length; i++)
            {
                float d = (w[i] - pos).sqrMagnitude + Mathf.Abs(w[i].y - pos.y) * 6f;
                if (d < bestAny) { bestAny = d; nearest = i; }
            }
            for (int i = 0; i < w.Length; i++)
            {
                float d = (w[i] - pos).sqrMagnitude + Mathf.Abs(w[i].y - pos.y) * 6f;
                if (d > 10f * 10f || d >= bestClear) continue;
                if (Physics.Linecast(pos + Vector3.up * 0.9f, w[i] + Vector3.up * 0.9f, WallMask, QueryTriggerInteraction.Ignore)) continue;
                bestClear = d;
                clear = i;
            }
            return clear >= 0 ? clear : nearest;
        }

        /// <summary>
        /// Where to go on the round: as often as its skill says, near where an enemy is (it knows roughly
        /// where the fight is), else somewhere else on the map, well away.
        /// </summary>
        private int PatrolGoal(Session s, World world, int from)
        {
            var w = world.Waypoints;
            if (Random.value < 0.45f + 0.15f * Skill)
            {
                var enemies = world.Agents.Values.Where(a => a != null && a != agent && a.State != null && a.State.Alive && world.IsEnemy(agent.State, a.State, s)).ToList();
                if (enemies.Count > 0)
                {
                    var e = enemies[rng.Next(enemies.Count)].transform.position;
                    var near = e + new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f));
                    return Nearest(world, near);
                }
            }
            var here = from >= 0 ? w[from] : transform.position;
            for (int tries = 0; tries < 12; tries++)
            {
                int i = rng.Next(w.Length);
                float d = Vector3.Distance(w[i], here);
                if (d > 20f && d < 80f) return i;
            }
            return rng.Next(w.Length);
        }

        /// <summary>Cover: a waypoint close by that the enemy last known cannot see, reached by the way.</summary>
        private void TakeCover(World world)
        {
            int from = Nearest(world, transform.position);
            if (from < 0) return;
            var w = world.Waypoints;
            var enemyEye = lastKnown + Vector3.up * 1.5f;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < w.Length; i++)
            {
                float d = Vector3.Distance(w[i], transform.position);
                if (d < 3f || d > 16f) continue;
                if (!Physics.Linecast(enemyEye, w[i] + Vector3.up * 1.1f, World.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                // close, and not towards the enemy
                float score = d + Mathf.Max(0f, 12f - Vector3.Distance(w[i], lastKnown));
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best < 0) return;
            FindPath(world, from, best);
            if (path.Count == 0) return;
            mode = Mode.Cover;
            coverUntil = 0f;
            target = null;
        }

        /// <summary>A* over the walk graph (length, plus a little for climbing); the path without its start.</summary>
        private void FindPath(World world, int from, int to)
        {
            path.Clear();
            pathAt = 0;
            var w = world.Waypoints;
            var nb = world.Neighbours;
            if (nb == null || from < 0 || to < 0 || from >= w.Length || to >= w.Length) return;
            if (from == to) { path.Add(to); return; }
            var cost = new Dictionary<int, float> { [from] = 0f };
            var cameFrom = new Dictionary<int, int>();
            var open = new SortedSet<(float f, int node)> { (Vector3.Distance(w[from], w[to]), from) };
            var closed = new HashSet<int>();
            int expanded = 0;
            while (open.Count > 0 && expanded < 4000)
            {
                var (_, node) = open.Min;
                open.Remove(open.Min);
                if (node == to) break;
                if (!closed.Add(node)) continue;
                expanded++;
                foreach (int n in nb[node])
                {
                    if (closed.Contains(n)) continue;
                    float c = cost[node] + Vector3.Distance(w[node], w[n]) + Mathf.Abs(w[n].y - w[node].y) * 0.5f;
                    if (cost.TryGetValue(n, out float old) && old <= c) continue;
                    if (cost.ContainsKey(n)) open.Remove((old + Vector3.Distance(w[n], w[to]), n));
                    cost[n] = c;
                    cameFrom[n] = node;
                    open.Add((c + Vector3.Distance(w[n], w[to]), n));
                }
            }
            if (!cameFrom.ContainsKey(to)) return;
            for (int n = to; n != from; n = cameFrom[n]) path.Add(n);
            path.Reverse();
        }

        /// <summary>
        /// The way along the path at that pace: on to the next point once there, past points it can already
        /// walk straight beyond (no zig-zag over the grid), and off a wall it brushes.
        /// </summary>
        private Vector3 Follow(World world, float pace)
        {
            var w = world.Waypoints;
            var pos = transform.position;
            while (pathAt < path.Count)
            {
                var p = w[path[pathAt]];
                var flat = p - pos;
                flat.y = 0;
                if (flat.magnitude < 1.1f && Mathf.Abs(p.y - pos.y) < 1.8f) { pathAt++; continue; }
                // the one after is straight on and on the same level: cut the corner
                if (pathAt + 1 < path.Count)
                {
                    var q = w[path[pathAt + 1]];
                    if (Mathf.Abs(q.y - pos.y) < 0.4f && Mathf.Abs(p.y - pos.y) < 0.4f
                        && !Physics.SphereCast(pos + Vector3.up * 0.9f, 0.35f, (q - pos).normalized, out _, Vector3.Distance(q, pos), WallMask, QueryTriggerInteraction.Ignore)
                        && Physics.Raycast((pos + q) / 2f + Vector3.up * 0.8f, Vector3.down, 1.6f, World.EnvMask, QueryTriggerInteraction.Ignore))
                    { pathAt++; continue; }
                }
                break;
            }
            if (pathAt >= path.Count) { path.Clear(); return Vector3.zero; }
            var to = w[path[pathAt]] - pos;
            to.y = 0;
            var dir = to.normalized;
            // brushing a wall: slide along it, a little away from it
            if (Physics.SphereCast(pos + Vector3.up * 0.9f, 0.3f, dir, out var hit, 0.9f, WallMask, QueryTriggerInteraction.Ignore))
            {
                var n = hit.normal;
                n.y = 0;
                if (n.sqrMagnitude > 1e-4f)
                {
                    n.Normalize();
                    var slide = Vector3.ProjectOnPlane(dir, n);
                    if (slide.sqrMagnitude > 0.01f) dir = (slide.normalized + n * 0.35f).normalized;
                }
            }
            return dir * pace;
        }

        // ---------------------------------------------------------------- fighting

        /// <summary>Ballistic throw that lands next to the target after a short flight.</summary>
        private void ThrowAt(Session s, Vector3 point)
        {
            grenades--;
            var origin = agent.Eye + transform.forward * 0.4f + Vector3.up * 0.1f;
            var delta = point + Random.insideUnitSphere * (2.5f - Skill) - origin;
            delta.y = point.y - origin.y;
            float time = Mathf.Clamp(delta.magnitude / 13f, 0.6f, 1.6f);
            var velocity = (delta - 0.5f * Physics.gravity * time * time) / time;
            s.BotThrow(agent.Id, origin, velocity);   // the arm swing is played by Grenade.Spawn
        }

        private Agent FindTarget(Session s, World world)
        {
            Agent best = null;
            float bestDistance = 60f;
            foreach (var other in world.Agents.Values)
            {
                if (other == agent || other.State == null || !other.State.Alive || !world.IsEnemy(agent.State, other.State, s)) continue;
                float d = Vector3.Distance(other.transform.position, transform.position);
                if (d >= bestDistance) continue;
                // bots notice what is in front of them, anything close, or the enemy they are after
                var dir = (other.transform.position - transform.position).normalized;
                float facing = Vector3.Dot(dir, transform.forward);
                bool expected = Time.time - lastKnownTime < 6f && Vector3.Distance(other.transform.position, lastKnown) < 12f;
                if (facing < 0.2f && d > 10f && !expected) continue;
                if (!world.LineOfSight(agent.Eye, other.Chest)) continue;
                best = other;
                bestDistance = d;
            }
            return best;
        }

        private void Shoot(Session s, WeaponDef def, Vector3 toTarget, float distance)
        {
            // short bursts with a pause between them, single shots taken with care
            if (burstLeft <= 0) burstLeft = def.Auto ? Random.Range(3, 6) : 1;
            burstLeft--;
            nextFire = Time.time + 1f / def.FireRate * (def.Auto ? 1.15f : Random.Range(1.6f, 2.6f));
            if (burstLeft <= 0) nextFire += Random.Range(0.7f, 1.5f);
            mag--;
            float error = Mathf.Lerp(5.5f, 2f, Skill / 2f) + distance * 0.025f;
            var dir = Ballistics.Spray(toTarget.normalized, error, rng);
            var origin = agent.Eye;
            s.FireBot(new Shot { Shooter = agent.Id, Weapon = def.Id, Origin = origin, Dir = dir, Seed = rng.Next(), Victim = Session.NoPlayer });
            agent.PlayFire();
            // effects for the host's own screen come from the ShotSeen event; play the sound here
        }
    }
}
