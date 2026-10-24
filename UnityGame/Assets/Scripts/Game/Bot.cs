using System.Linq;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// Host-side AI, played like a person: walks the waypoints and stops now and then to look around;
    /// in a fight it mostly stands and shoots short bursts, and moves only now and then to another spot.
    /// </summary>
    public class Bot : MonoBehaviour
    {
        private Agent agent;
        private CharacterController cc;
        private int waypoint = -1, previousWaypoint = -1;
        private float verticalSpeed, nextThink, nextFire, reloadEnd, strafeUntil, strafeDir = 1f, stuckTime, reactionAt;
        private float pauseUntil, lookAround, burstLeft;
        private bool repositioning;
        private int mag, grenades;
        private float nextHeal;
        private float nextGrenade;
        private Agent target;
        private Vector3 lastPos;
        private float yaw, pitch;
        private System.Random rng;

        private float Skill => Session.I.Config.BotSkill; // 0 easy, 1 normal, 2 hard

        private void Awake()
        {
            agent = GetComponent<Agent>();
            cc = GetComponent<CharacterController>();
            rng = new System.Random(Random.Range(0, int.MaxValue));
        }

        private void Start() => OnRespawn();

        public void OnRespawn()
        {
            waypoint = -1;
            target = null;
            mag = Weapons.Get(agent.State.Primary).MagSize;
            grenades = Grenade.PerLife;
            nextGrenade = Time.time + Random.Range(4f, 10f);
            yaw = agent.State.Yaw;
            pitch = 0;
        }

        private void Update()
        {
            var s = Session.I;
            var st = agent.State;
            var world = World.I;
            if (s == null || st == null || world == null || !st.Alive || !cc.enabled) return;
            float dt = Time.deltaTime;
            bool playing = s.Phase == Phase.Playing;
            var def = Weapons.Get(st.Primary);
            st.Weapon = def.Id;
            // nobody in the sights and a badly wounded team mate next to it: blood for the team mate
            if (playing && target == null && Time.time >= nextHeal && s.Config.Mode == Mode.TeamDeathmatch)
            {
                nextHeal = Time.time + 1f;
                foreach (var a in world.Agents.Values)
                {
                    if (a == null || a == agent || a.State == null || !a.State.Alive || a.State.Team != st.Team || a.State.Health >= Session.BloodThreshold) continue;
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

            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.3f;
                var previous = target;
                target = FindTarget(s, world);
                if (target != null && target != previous)
                {
                    reactionAt = Time.time + Mathf.Lerp(1.3f, 0.55f, Skill / 2f) + Random.Range(0f, 0.3f);
                    repositioning = false;
                    strafeUntil = Time.time + Random.Range(1.5f, 3f);
                }
            }

            Vector3 move = Vector3.zero;
            if (target != null && playing)
            {
                var toTarget = target.Chest - agent.Eye;
                float distance = toTarget.magnitude;
                float wantYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                float wantPitch = Mathf.Asin(Mathf.Clamp(toTarget.y / Mathf.Max(distance, 0.01f), -1f, 1f)) * Mathf.Rad2Deg;
                float turn = Mathf.Lerp(90f, 220f, Skill / 2f) * dt;
                yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, turn);
                pitch = Mathf.MoveTowards(pitch, wantPitch, turn);
                // stand and shoot for a while, then a short walk to the side (or closer / back) and stop again
                if (Time.time > strafeUntil)
                {
                    repositioning = !repositioning;
                    strafeUntil = Time.time + (repositioning ? Random.Range(0.6f, 1.3f) : Random.Range(1.8f, 4f));
                    strafeDir = Random.value < 0.5f ? -1f : 1f;
                }
                var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                float ideal = def.Pellets > 1 ? 8f : def.Scope ? 35f : 16f;
                if (repositioning)
                    move = (right * strafeDir * 0.8f + forward * Mathf.Clamp((distance - ideal) / 6f, -0.6f, 1f)).normalized * 0.5f;
                bool aimed = Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < 5f;
                if (aimed && !repositioning && Time.time >= reactionAt && Time.time >= nextFire && reloadEnd <= 0 && mag > 0 && distance < def.Range * 0.8f)
                    Shoot(s, def, toTarget, distance);
                // now and then a grenade at a target in the middle distance
                if (grenades > 0 && Time.time >= nextGrenade && distance > 9f && distance < 24f && Time.time >= reactionAt)
                {
                    nextGrenade = Time.time + Random.Range(6f, 14f);
                    if (Random.value < 0.25f + 0.15f * Skill) ThrowAt(s, target.transform.position);
                }
                st.MoveX = Vector3.Dot(move, right);
                st.MoveY = Vector3.Dot(move, forward);
            }
            else if (world.Waypoints.Length > 0 && playing)
            {
                if (waypoint < 0) waypoint = world.NearestWaypoint(transform.position);
                var to = world.Waypoints[waypoint] - transform.position;
                to.y = 0;
                if (to.magnitude < 1.6f)
                {
                    NextWaypoint(world);
                    // now and then stop and look around
                    if (Random.value < 0.35f) { pauseUntil = Time.time + Random.Range(1.5f, 4.5f); lookAround = yaw + Random.Range(-100f, 100f); }
                }
                if (Time.time < pauseUntil)
                {
                    yaw = Mathf.MoveTowardsAngle(yaw, lookAround, 60f * dt);
                    if (Mathf.Abs(Mathf.DeltaAngle(yaw, lookAround)) < 2f) lookAround = yaw + Random.Range(-90f, 90f);
                    pitch = Mathf.MoveTowards(pitch, 0, 60f * dt);
                    st.MoveX = st.MoveY = 0;
                    stuckTime = 0;
                }
                else
                {
                    float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, 160f * dt);
                    pitch = Mathf.MoveTowards(pitch, 0, 60f * dt);
                    // walking pace, not a run
                    move = Quaternion.Euler(0, yaw, 0) * Vector3.forward * 0.5f;
                    st.MoveX = 0;
                    st.MoveY = 0.5f;
                    // stuck against something: pick another way
                    if ((transform.position - lastPos).sqrMagnitude < 0.0001f) stuckTime += dt; else stuckTime = 0;
                    if (stuckTime > 1.2f) { stuckTime = 0; NextWaypoint(world); }
                }
            }
            else
            {
                st.MoveX = st.MoveY = 0;
            }
            lastPos = transform.position;

            float speed = 4.8f * def.MoveSpeed;
            if (cc.isGrounded) verticalSpeed = -2f;
            // up a ladder towards a waypoint above (the ladder routes of the map)
            var ladder = Ladder.At(transform.position + Vector3.up * 0.9f);
            if (ladder != null && waypoint >= 0 && waypoint < world.Waypoints.Length && world.Waypoints[waypoint].y > transform.position.y + 0.4f)
            {
                verticalSpeed = ladder.Rate;
                if (transform.position.y < ladder.Top - 0.1f) move = ladder.Steer(transform.position, verticalSpeed, 0.3f) / speed;
            }
            else verticalSpeed -= 20f * dt;
            cc.Move((Vector3.ClampMagnitude(move, 1f) * speed + Vector3.up * verticalSpeed) * dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);

            st.Pos = transform.position;
            st.Yaw = yaw;
            st.Pitch = pitch;
            st.Grounded = cc.isGrounded;
            st.Crouch = false;
        }

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

        private void NextWaypoint(World world)
        {
            var options = world.Neighbours[waypoint].Where(n => n != previousWaypoint).ToList();
            if (options.Count == 0) options = world.Neighbours[waypoint];
            previousWaypoint = waypoint;
            if (options.Count > 0) waypoint = options[rng.Next(options.Count)];
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
                // bots notice what is in front of them, or anything shooting close by
                var dir = (other.transform.position - transform.position).normalized;
                float facing = Vector3.Dot(dir, transform.forward);
                if (facing < 0.2f && d > 10f) continue;
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
