using UnityEngine;

namespace UA
{
    /// <summary>Movement, aiming, shooting and reloading for the player on this device.</summary>
    public class LocalPlayer : MonoBehaviour
    {
        public static LocalPlayer I;

        public int Slot;                         // 0 primary, 1 secondary
        public readonly int[] Mag = new int[2];
        public readonly int[] Reserve = new int[2];
        public bool Aiming;
        public int Grenades = Grenade.PerLife;
        private float nextThrow;
        public bool Reloading => reloadEnd > 0;
        public float ReloadProgress => Reloading ? 1f - (reloadEnd - Time.time) / Current.ReloadTime : 0f;
        public float Spread { get; private set; }
        public float LastShotTime { get; private set; } = -10f;

        private Agent agent;
        private CharacterController cc;
        private float verticalSpeed;
        private float nextFire;
        private float reloadEnd;
        private float bloom;
        private bool crouch, prone;
        private Vector3 velocity;

        // speeds in m/s (the same for everyone)
        public const float RunSpeed = 5.2f, SprintSpeed = 7.4f, CrouchSpeed = 2.8f, ProneSpeed = 1.5f;
        private const float StandHeight = 1.8f, ProneHeight = 0.7f;
        private System.Random rng;

        public WeaponDef Current => Weapons.Get(Slot == 0 ? agent.State.Primary : agent.State.Secondary);
        public WeaponDef Other => Weapons.Get(Slot == 0 ? agent.State.Secondary : agent.State.Primary);

        private void Awake()
        {
            I = this;
            agent = GetComponent<Agent>();
            cc = GetComponent<CharacterController>();
            rng = new System.Random(Random.Range(0, int.MaxValue));
            Refill();
        }

        private void OnDestroy()
        {
            Controls.AmmoAvailable = false;
            if (I == this) I = null;
        }

        public void OnRespawn()
        {
            Refill();
            Grenades = Grenade.PerLife;
            Slot = 0;
            Aiming = false;
            crouch = false;
            prone = false;
            velocity = Vector3.zero;
            cc.height = StandHeight;
            cc.center = new Vector3(0, StandHeight / 2f + 0.02f, 0);
            reloadEnd = 0;
            verticalSpeed = 0;
            CameraRig.I.Yaw = agent.State.Yaw;
            CameraRig.I.Pitch = 0;
        }

        private void Refill()
        {
            if (agent == null || agent.State == null) return;
            for (int i = 0; i < 2; i++)
            {
                var def = Weapons.Get(i == 0 ? agent.State.Primary : agent.State.Secondary);
                Mag[i] = def.MagSize;
                Reserve[i] = def.Reserve;
            }
        }

        private void Update()
        {
            var s = Session.I;
            var state = agent.State;
            if (s == null || state == null) return;
            var rig = CameraRig.I;
            bool alive = state.Alive;
            bool canMove = alive && (s.Phase == Phase.Playing || s.Phase == Phase.Countdown) && Controls.Enabled;
            bool canShoot = alive && s.Phase == Phase.Playing && Controls.Enabled;
            float dt = Time.deltaTime;

            // look
            if (Controls.Enabled)
            {
                // aiming: the turn follows the zoom (the picture moves as fast under the finger at any zoom)
                float zoom = Mathf.Tan(rig.Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(31f * Mathf.Deg2Rad);
                float turnSpeed = !Aiming ? 1f : Mathf.Pow(Mathf.Clamp01(zoom), 0.8f) * (Current.Scope ? Settings.ScopeSensitivity : Settings.AimSensitivity);
                var look = Controls.Look * turnSpeed;
                if (Settings.AimAssist && alive && canShoot) look = AimAssist(rig, look, dt);
                rig.Yaw += look.x;
                rig.Pitch = Mathf.Clamp(rig.Pitch + look.y, prone ? -25f : -70f, 75f);
            }

            // weapon actions
            if (canShoot)
            {
                if (Controls.SwapPressed) Swap();
                if (Controls.ReloadPressed) StartReload();
                if (Controls.AimPressed) Aiming = !Aiming;
                if (Controls.CrouchPressed) { crouch = !crouch; prone = false; }
                if (Controls.PronePressed) { prone = !prone; crouch = false; Aiming = false; }
                if (Controls.GrenadePressed && Grenades > 0 && Time.time >= nextThrow) ThrowGrenade();
            }
            if (!alive) { Aiming = false; reloadEnd = 0; }
            var def = Current;
            if (Reloading && Time.time >= reloadEnd) FinishReload();
            bool trigger = def.Auto ? Controls.FireHeld : Controls.FirePressed;
            if (canShoot && trigger && !Reloading && Time.time >= nextFire)
            {
                if (Mag[Slot] > 0) Fire(def);
                else
                {
                    nextFire = Time.time + 0.3f;
                    Sfx.Play("empty", 0.6f);
                    StartReload();
                }
            }
            bloom = Mathf.MoveTowards(bloom, 0f, (Aiming ? 10f : 6f) * dt);

            // movement: smooth acceleration towards the wanted speed (no sudden starts and stops)
            var input = canMove ? Controls.Move : Vector2.zero;
            if (s.Phase == Phase.Countdown) input = Vector2.zero;
            bool firing = Time.time - LastShotTime < 0.25f;
            bool sprint = input.y > 0.8f && Mathf.Abs(input.x) < 0.5f && !firing && !Aiming && !crouch && !prone && !Reloading;
            float baseSpeed = prone ? ProneSpeed : crouch ? CrouchSpeed : RunSpeed;
            float maxSpeed = (sprint ? SprintSpeed : baseSpeed) * def.MoveSpeed * (Aiming ? 0.72f : 1f);
            var wish = Quaternion.Euler(0, rig.Yaw, 0) * Vector3.ClampMagnitude(new Vector3(input.x, 0, input.y), 1f) * maxSpeed;
            float accel = cc.isGrounded ? (wish.sqrMagnitude >= velocity.sqrMagnitude ? 30f : 40f) : 8f;
            velocity = Vector3.MoveTowards(velocity, wish, accel * dt);
            if (!alive) velocity = Vector3.zero;
            UpdateStance();
            if (cc.enabled)
            {
                if (cc.isGrounded)
                {
                    verticalSpeed = -2f;
                    if (canMove && Controls.JumpPressed && s.Phase == Phase.Playing)
                    {
                        if (crouch || prone) { crouch = false; prone = false; }
                        else verticalSpeed = 6.4f;
                    }
                }
                verticalSpeed -= 20f * dt;
                var before = transform.position;
                cc.Move((velocity + Vector3.up * verticalSpeed) * dt);
                // walking into a wall: do not keep pushing (keeps the animation honest)
                var moved = (transform.position - before) / Mathf.Max(dt, 1e-4f);
                moved.y = 0;
                if (moved.sqrMagnitude < velocity.sqrMagnitude) velocity = Vector3.Lerp(velocity, moved, 0.5f);
                if (alive) transform.rotation = Quaternion.Euler(0, rig.Yaw, 0);
                if (transform.position.y < -15f && World.I != null) agent.Teleport(World.I.Spawns.Length > 0 ? World.I.Spawns[0] : Vector3.up, rig.Yaw);
            }

            // ammo crates: the button shows while standing next to one and something is missing
            bool atBox = alive && AmmoBox.Nearest(transform.position) != null;
            Controls.AmmoAvailable = atBox && NeedsSupplies;
            if (atBox && Controls.AmmoPressed) Resupply();

            // spread for the crosshair and the next shot
            float moving = Mathf.Clamp01(velocity.magnitude / RunSpeed);
            Spread = (Aiming ? def.AimSpread : def.Spread) + bloom + moving * def.Spread * 0.6f + (cc.isGrounded ? 0f : def.Spread);
            if (crouch) Spread *= 0.75f;
            if (prone) Spread *= 0.5f;

            // scope: first person view for the sniper
            rig.Aim = Aiming && alive;
            rig.Scope = Aiming && def.Scope && alive;
            rig.Crouch = crouch;
            rig.Prone = prone;
            rig.AimFov = def.AimFov;
            agent.SetVisible(!rig.Scope);

            // the barrel follows the crosshair exactly
            if (alive && !rig.Scope)
            {
                var ray = rig.AimRay();
                agent.AimPoint = World.I.TraceShot(ray.origin, ray.direction, Current.Range, agent.Id).Point;
            }
            else agent.AimPoint = null;

            // write our state for the network and the animations
            state.Pos = transform.position;
            state.Yaw = rig.Yaw;
            state.Pitch = rig.Pitch;
            // animation follows the real speed: 1 = running, 2 = sprinting
            var local = Quaternion.Inverse(transform.rotation) * velocity;
            float norm = baseSpeed * def.MoveSpeed;
            state.MoveX = alive ? Mathf.Clamp(local.x / norm, -1f, 1f) : 0f;
            state.MoveY = !alive ? 0f : local.z > norm * 1.02f ? 1f + Mathf.Clamp01((local.z - norm) / Mathf.Max(0.1f, (SprintSpeed - RunSpeed) * def.MoveSpeed)) : Mathf.Clamp(local.z / norm, -1f, 1f);
            state.Crouch = crouch;
            state.Prone = prone;
            state.Grounded = !cc.enabled || cc.isGrounded;
            state.Aiming = Aiming;
            state.Weapon = def.Id;
        }

        private void Fire(WeaponDef def)
        {
            nextFire = Time.time + 1f / def.FireRate;
            LastShotTime = Time.time;
            Mag[Slot]--;
            var rig = CameraRig.I;
            var aim = rig.AimRay();
            var world = World.I;
            // what is under the crosshair decides the direction; the shot itself starts at the eyes
            var under = world.TraceShot(aim.origin, aim.direction, def.Range, agent.Id);
            var origin = agent.Eye;
            var dir = (under.Point - origin).normalized;
            if (Vector3.Dot(dir, aim.direction) < 0.5f) dir = aim.direction;
            if (Settings.AimAssist && under.Kind != 1 && def.Pellets <= 1) dir = Magnetism(origin, dir, aim, def.Range);
            if (def.Pellets <= 1) dir = Ballistics.Spray(dir, Spread, rng);
            var shot = new Shot { Weapon = def.Id, Origin = origin, Dir = dir, Seed = rng.Next() };
            Session.I.FireLocal(shot);

            // local feedback right away
            var muzzle = agent.Muzzle.position;
            foreach (var d in Ballistics.Pellets(dir, shot.Seed, def))
            {
                var hit = world.TraceShot(origin, d, def.Range, agent.Id, def.DropVelocity);
                Effects.Tracer(muzzle, hit.Point, def.Pellets > 1 ? 0.6f : 1f);
                if (hit.Kind == 0) Effects.Impact(hit.Point, -d);
                else if (hit.Kind == 1) Effects.Blood(hit.Point, -d);
            }
            Effects.MuzzleFlash(agent.Muzzle, def);
            Sfx.PlayAt(def.Sound, muzzle, 1f, def.SoundPitch, false);
            agent.PlayFire();
            bloom = Mathf.Min(bloom + def.Recoil * 0.5f, 6f);
            rig.Kick(def.Recoil * (Aiming ? 0.6f : 1f));
            if (def.BoltAction) Aiming = false;   // bolt action: re-aim after every shot
            if (Mag[Slot] == 0) StartReload();
        }

        /// <summary>
        /// Body height for standing / crouching / lying down. Standing up again is only refused when
        /// something solid is really above the player (then they stay down until there is room).
        /// </summary>
        private void UpdateStance()
        {
            float want = prone ? ProneHeight : StandHeight;
            if (Mathf.Abs(cc.height - want) < 0.01f) return;
            if (!prone)
            {
                var bottom = transform.position + Vector3.up * (ProneHeight + 0.35f);
                var top = transform.position + Vector3.up * (StandHeight - 0.3f);
                if (Physics.CheckCapsule(bottom, top, 0.26f, World.EnvMask, QueryTriggerInteraction.Ignore))
                {
                    prone = true;
                    return;
                }
            }
            cc.height = want;
            cc.center = new Vector3(0, want / 2f + 0.02f, 0);
        }

        /// <summary>Throw a frag grenade along the crosshair, a little upwards, from the right shoulder.</summary>
        private void ThrowGrenade()
        {
            Grenades--;
            nextThrow = Time.time + 1f;
            Aiming = false;
            var rig = CameraRig.I;
            var aim = rig.AimRay().direction;
            var origin = agent.Eye + Quaternion.Euler(0, rig.Yaw, 0) * new Vector3(0.3f, 0.15f, 0.45f);
            if (Physics.Linecast(agent.Eye, origin, World.EnvMask, QueryTriggerInteraction.Ignore)) origin = agent.Eye;
            var throwVelocity = (aim + Vector3.up * 0.22f).normalized * (prone ? 9f : 15f) + velocity * 0.5f;
            Session.I.ThrowLocal(origin, throwVelocity);
            Sfx.Play("grenade_pin", 0.7f);
            agent.PlayReload();
        }

        // ---------------------------------------------------------------- aim assist
        // As in the big mobile shooters: the look slows down over an enemy (friction), follows one that
        // moves (tracking), aiming down the sights next to one turns onto it (snap), and while shooting
        // or aiming the crosshair is pulled gently to the body. Far away a shot that just misses the
        // edge of a body bends onto it (bullet magnetism), so small far targets can be hit.

        private Agent assistTarget;
        private Vector2 assistLast;
        private float snapUntil;
        private bool wasAiming;

        private static Vector2 Angles(Vector3 d) =>
            new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);

        private bool Enemy(Agent other) =>
            other != agent && other.State != null && other.State.Alive && World.I.IsEnemy(agent.State, other.State, Session.I);

        private Agent FindAssistTarget(Vector3 eye, Vector3 forward, out float angle, out float radius)
        {
            Agent best = null;
            angle = radius = 0f;
            float bestScore = 1f;
            foreach (var other in World.I.Agents.Values)
            {
                if (!Enemy(other)) continue;
                var to = other.Chest - eye;
                float d = to.magnitude;
                if (d < 1f || d > Current.Range) continue;
                // the cone is about a body's width around it, never smaller than 1.5 degrees
                float r = Mathf.Clamp(Mathf.Atan2(0.9f, d) * Mathf.Rad2Deg, 1.5f, 9f) * (Aiming ? 1.25f : 1f);
                float a = Vector3.Angle(forward, to);
                if (a / r >= bestScore) continue;
                if (!World.I.LineOfSight(eye, other.Chest)) continue;
                best = other;
                bestScore = a / r;
                angle = a;
                radius = r;
            }
            return best;
        }

        private Vector2 AimAssist(CameraRig rig, Vector2 look, float dt)
        {
            var cam = rig.transform;
            var target = FindAssistTarget(cam.position, cam.forward, out float angle, out float radius);
            if (Aiming && !wasAiming) snapUntil = Time.time + 0.14f;
            wasAiming = Aiming;
            if (target == null) { assistTarget = null; return look; }
            var want = Angles(target.Chest - cam.position);
            bool firing = Time.time - LastShotTime < 0.2f;
            // friction: up to half speed over the middle of the body
            var result = look * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(angle / radius));
            // tracking: keep up with the target's movement across the view while the player aims at it
            if (assistTarget == target && (look.sqrMagnitude > 1e-4f || firing || Aiming))
            {
                float limit = 60f * dt;
                result.x += 0.55f * Mathf.Clamp(Mathf.DeltaAngle(assistLast.x, want.x), -limit, limit);
                result.y += 0.55f * Mathf.Clamp(want.y - assistLast.y, -limit, limit);
            }
            // pull: a quick turn just after aiming down the sights, a gentle one while shooting or aiming
            bool snap = Time.time < snapUntil;
            if (snap || firing || Aiming)
            {
                float pull = (snap ? 70f : Aiming ? 7f : 4f) * dt;
                result.x += Mathf.Clamp(Mathf.DeltaAngle(rig.Yaw + result.x, want.x), -pull, pull);
                result.y += Mathf.Clamp(want.y - (rig.Pitch + result.y), -pull, pull);
            }
            assistTarget = target;
            assistLast = want;
            return result;
        }

        /// <summary>Bends a shot that passes just beside an enemy's body onto it (more allowance further away).</summary>
        private Vector3 Magnetism(Vector3 origin, Vector3 dir, Ray aim, float range)
        {
            var best = dir;
            float bestMiss = float.MaxValue;
            foreach (var other in World.I.Agents.Values)
            {
                if (!Enemy(other)) continue;
                var st = other.State;
                // the body as a line from the hips to the top of the head
                var bottom = other.transform.position + Vector3.up * (st.Prone ? 0.2f : st.Crouch ? 0.6f : 0.9f);
                var top = other.Chest + Vector3.up * (st.Prone ? 0.1f : 0.4f);
                var p = ClosestOnSegment(aim, bottom, top);
                float along = Vector3.Dot(p - aim.origin, aim.direction);
                if (along < 2f || along > range) continue;
                float miss = Vector3.Distance(aim.origin + aim.direction * along, p);
                float allowance = 0.2f + Mathf.Clamp(along * 0.004f, 0f, 0.4f);
                if (miss > allowance || miss >= bestMiss) continue;
                if (!World.I.LineOfSight(origin, p)) continue;
                bestMiss = miss;
                best = (p - origin).normalized;
            }
            return best;
        }

        private static Vector3 ClosestOnSegment(Ray ray, Vector3 a, Vector3 b)
        {
            var u = b - a;
            var w = a - ray.origin;
            float uu = Vector3.Dot(u, u), uv = Vector3.Dot(u, ray.direction), vv = Vector3.Dot(ray.direction, ray.direction);
            float uw = Vector3.Dot(u, w), vw = Vector3.Dot(ray.direction, w);
            float den = uu * vv - uv * uv;
            float t = den > 1e-6f ? (uv * vw - vv * uw) / den : 0f;
            return a + u * Mathf.Clamp01(t);
        }

        public void Swap()
        {
            Slot = 1 - Slot;
            reloadEnd = 0;
            Aiming = false;
            nextFire = Time.time + 0.45f;
            Sfx.Play("reload", 0.4f, 1.4f);
        }

        public void StartReload()
        {
            var def = Current;
            if (Reloading || Mag[Slot] >= def.MagSize || Reserve[Slot] <= 0) return;
            reloadEnd = Time.time + def.ReloadTime;
            Aiming = false;
            agent.PlayReload();
            Sfx.Play("reload", 0.8f);
        }

        private void FinishReload()
        {
            reloadEnd = 0;
            var def = Current;
            int take = Mathf.Min(def.MagSize - Mag[Slot], Reserve[Slot]);
            Mag[Slot] += take;
            Reserve[Slot] -= take;
        }

        public bool NeedsSupplies
        {
            get
            {
                if (Grenades < Grenade.Max) return true;
                for (int i = 0; i < 2; i++)
                {
                    var def = Weapons.Get(i == 0 ? agent.State.Primary : agent.State.Secondary);
                    if (Mag[i] < def.MagSize || Reserve[i] < def.Reserve) return true;
                }
                return false;
            }
        }

        /// <summary>Everything full again: magazines, reserve ammunition and grenades.</summary>
        public void Resupply()
        {
            reloadEnd = 0;
            Refill();
            Grenades = Grenade.Max;
            Sfx.Play("reload", 0.9f, 1.15f);
            App.I?.Toast("AMMO AND GRENADES REFILLED");
        }

        /// <summary>A kill gives back one magazine for both weapons and a grenade (up to 3).</summary>
        public void OnKill()
        {
            Grenades = Mathf.Min(Grenades + 1, Grenade.Max);
            for (int i = 0; i < 2; i++)
            {
                var def = Weapons.Get(i == 0 ? agent.State.Primary : agent.State.Secondary);
                Reserve[i] = Mathf.Min(Reserve[i] + def.MagSize, def.Reserve * 2);
            }
        }
    }
}
