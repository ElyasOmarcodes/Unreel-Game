using System.Linq;
using UnityEngine;

namespace UA
{
    /// <summary>Host-side AI: patrols the street waypoints, hunts visible enemies and shoots in bursts.</summary>
    public class Bot : MonoBehaviour
    {
        private Agent agent;
        private CharacterController cc;
        private int waypoint = -1, previousWaypoint = -1;
        private float verticalSpeed, nextThink, nextFire, reloadEnd, strafeUntil, strafeDir = 1f, stuckTime, reactionAt;
        private int mag;
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
            if (mag <= 0 && reloadEnd <= 0) { reloadEnd = Time.time + def.ReloadTime; agent.PlayReload(); }
            if (reloadEnd > 0 && Time.time >= reloadEnd) { reloadEnd = 0; mag = def.MagSize; }

            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.3f;
                var previous = target;
                target = FindTarget(s, world);
                if (target != null && target != previous) reactionAt = Time.time + Mathf.Lerp(0.8f, 0.25f, Skill / 2f);
            }

            Vector3 move = Vector3.zero;
            if (target != null && playing)
            {
                var toTarget = target.Chest - agent.Eye;
                float distance = toTarget.magnitude;
                float wantYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                float wantPitch = Mathf.Asin(Mathf.Clamp(toTarget.y / Mathf.Max(distance, 0.01f), -1f, 1f)) * Mathf.Rad2Deg;
                float turn = Mathf.Lerp(150f, 420f, Skill / 2f) * dt;
                yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, turn);
                pitch = Mathf.MoveTowards(pitch, wantPitch, turn);
                // keep a fighting distance and strafe
                if (Time.time > strafeUntil) { strafeUntil = Time.time + Random.Range(0.8f, 2f); strafeDir = Random.value < 0.5f ? -1f : 1f; }
                var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                float ideal = def.Pellets > 1 ? 8f : def.Scope ? 35f : 16f;
                move = right * strafeDir * 0.6f + forward * Mathf.Clamp((distance - ideal) / 6f, -0.6f, 1f);
                bool aimed = Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < 7f;
                if (aimed && Time.time >= reactionAt && Time.time >= nextFire && reloadEnd <= 0 && mag > 0 && distance < def.Range * 0.8f)
                    Shoot(s, def, toTarget, distance);
                st.MoveX = Vector3.Dot(move, right);
                st.MoveY = Vector3.Dot(move, forward);
            }
            else if (world.Waypoints.Length > 0 && playing)
            {
                if (waypoint < 0) waypoint = world.NearestWaypoint(transform.position);
                var to = world.Waypoints[waypoint] - transform.position;
                to.y = 0;
                if (to.magnitude < 1.6f) NextWaypoint(world);
                float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, 240f * dt);
                pitch = Mathf.MoveTowards(pitch, 0, 60f * dt);
                move = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                st.MoveX = 0;
                st.MoveY = 1;
                // stuck against something: pick another way
                if ((transform.position - lastPos).sqrMagnitude < 0.0004f) stuckTime += dt; else stuckTime = 0;
                if (stuckTime > 1.2f) { stuckTime = 0; NextWaypoint(world); }
            }
            else
            {
                st.MoveX = st.MoveY = 0;
            }
            lastPos = transform.position;

            float speed = 4.3f * def.MoveSpeed;
            if (cc.isGrounded) verticalSpeed = -2f;
            verticalSpeed -= 20f * dt;
            cc.Move((Vector3.ClampMagnitude(move, 1f) * speed + Vector3.up * verticalSpeed) * dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);

            st.Pos = transform.position;
            st.Yaw = yaw;
            st.Pitch = pitch;
            st.Grounded = cc.isGrounded;
            st.Crouch = false;
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
            float bestDistance = 70f;
            foreach (var other in world.Agents.Values)
            {
                if (other == agent || other.State == null || !other.State.Alive || !world.IsEnemy(agent.State, other.State, s)) continue;
                float d = Vector3.Distance(other.transform.position, transform.position);
                if (d >= bestDistance) continue;
                // bots notice what is in front of them, or anything shooting close by
                var dir = (other.transform.position - transform.position).normalized;
                float facing = Vector3.Dot(dir, transform.forward);
                if (facing < -0.2f && d > 12f) continue;
                if (!world.LineOfSight(agent.Eye, other.Chest)) continue;
                best = other;
                bestDistance = d;
            }
            return best;
        }

        private void Shoot(Session s, WeaponDef def, Vector3 toTarget, float distance)
        {
            nextFire = Time.time + 1f / def.FireRate * (def.Auto ? Random.Range(1f, 1.6f) : Random.Range(1.2f, 2f));
            mag--;
            float error = Mathf.Lerp(4.5f, 1.4f, Skill / 2f) + distance * 0.02f;
            var dir = Ballistics.Spray(toTarget.normalized, error, rng);
            var origin = agent.Eye;
            s.FireBot(new Shot { Shooter = agent.Id, Weapon = def.Id, Origin = origin, Dir = dir, Seed = rng.Next() });
            agent.PlayFire();
            // effects for the host's own screen come from the ShotSeen event; play the sound here
        }
    }
}
