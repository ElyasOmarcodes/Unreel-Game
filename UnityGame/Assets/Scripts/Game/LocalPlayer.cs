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
        /// <summary>Giving blood to a badly wounded team mate close by (the button shows only then).</summary>
        public bool Healing => healEnd > 0;
        private float healEnd, nextGive;
        private byte healTarget;
        private Agent bloodMate;
        private float nextThrow;
        public bool Reloading => reloadEnd > 0;
        public float ReloadProgress => Reloading ? 1f - (reloadEnd - Time.time) / Current.ReloadTime : 0f;
        public float Spread { get; private set; }
        public float LastShotTime { get; private set; } = -10f;

        private Agent agent;
        private CharacterController cc;
        private float verticalSpeed;
        private bool sprinting;
        private float nextFire;
        private float reloadEnd;
        private float boltAt;   // when the bolt is worked after a bolt-action shot
        private float bloom;
        private bool crouch, prone;
        private Vector3 velocity;

        // speeds in m/s (the same for everyone)
        public const float RunSpeed = 5.6f, SprintSpeed = 7.8f, CrouchSpeed = 3f, ProneSpeed = 1.6f;
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
            Controls.HealAvailable = false;
            if (I == this) I = null;
        }

        public void OnRespawn()
        {
            Refill();
            Grenades = Grenade.PerLife;
            healEnd = 0;
            nextGive = 0;
            // the weapon in the hand is kept over a death (Slot stays as it was)
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
            bool canMove = alive && (s.Phase == Phase.Playing || s.Phase == Phase.Countdown) && Controls.Enabled && !KillCam.Active;
            bool canShoot = alive && s.Phase == Phase.Playing && Controls.Enabled && !KillCam.Active;
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
                if (Controls.HealPressed && !Healing && bloodMate != null) StartHeal();
            }
            if (!alive) { Aiming = false; reloadEnd = 0; healEnd = 0; }
            if (KillCam.Active) Aiming = false;
            if (Healing && Time.time >= healEnd) FinishHeal(s);
            var def = Current;
            if (Reloading && Time.time >= reloadEnd) FinishReload();
            if (boltAt > 0 && Time.time >= boltAt)
            {
                boltAt = 0;
                if (alive && !Reloading && Sfx.Has("bolt_0")) Sfx.Play("bolt_" + rng.Next(Sfx.Count("bolt_")), 0.85f);
            }
            bool trigger = def.Auto ? Controls.FireHeld : Controls.FirePressed;
            if (canShoot && trigger && !Reloading && !Healing && Time.time >= nextFire)
            {
                if (Mag[Slot] > 0) Fire(def);
                else
                {
                    nextFire = Time.time + 0.3f;
                    Sfx.Play(Sfx.Has("dry_0") ? "dry_" + rng.Next(Sfx.Count("dry_")) : "empty", 0.7f);
                    StartReload();
                }
            }
            bloom = Mathf.MoveTowards(bloom, 0f, (Aiming ? 10f : 6f) * dt);

            // movement: smooth acceleration towards the wanted speed (no sudden starts and stops)
            var input = canMove ? Controls.Move : Vector2.zero;
            if (s.Phase == Phase.Countdown) input = Vector2.zero;
            bool firing = Time.time - LastShotTime < 0.25f;
            // sprint with the stick pushed well forward; once sprinting a thumb that drifts a little back or
            // to the side keeps it (no sudden drop to the run speed and the slower steps in the middle of a run)
            bool forward = sprinting ? input.y > 0.45f && Mathf.Abs(input.x) < Mathf.Max(0.6f, input.y * 0.9f) : input.y > 0.8f && Mathf.Abs(input.x) < 0.5f;
            bool sprint = sprinting = forward && !firing && !Aiming && !crouch && !prone && !Reloading;
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
                        // a running jump goes a little higher and further than one from standing
                        else verticalSpeed = 6.4f + 0.8f * Mathf.Clamp01(new Vector2(velocity.x, velocity.z).magnitude / SprintSpeed);
                    }
                }
                // ladders: push towards the ladder to climb, pull back to climb down, let go to hold on
                var ladder = alive && canMove ? Ladder.At(transform.position + Vector3.up * 0.9f) : null;
                bool climbing = false;
                if (ladder != null)
                {
                    var look = Quaternion.Euler(0, rig.Yaw, 0) * Vector3.forward;
                    bool towards = input.y > 0.2f && Vector3.Dot(look, ladder.Into) > 0.2f;
                    if (towards) { verticalSpeed = ladder.Rate; climbing = true; }
                    else if (!cc.isGrounded) { verticalSpeed = input.y < -0.2f ? -ladder.Rate : 0f; climbing = true; }
                    // on the ladder the body stays on it (along its lean); over the top the push carries it onto the landing
                    if (climbing && transform.position.y < ladder.Top - 0.1f) velocity = ladder.Steer(transform.position, verticalSpeed, towards ? 1.2f : 0.3f);
                }
                if (!climbing) verticalSpeed -= 20f * dt;
                var before = transform.position;
                cc.Move((velocity + Vector3.up * verticalSpeed) * dt);
                // walking into a wall: do not keep pushing (keeps the animation honest)
                var moved = (transform.position - before) / Mathf.Max(dt, 1e-4f);
                moved.y = 0;
                if (moved.sqrMagnitude < velocity.sqrMagnitude) velocity = Vector3.Lerp(velocity, moved, 0.5f);
                if (alive) transform.rotation = Quaternion.Euler(0, rig.Yaw, 0);
                if (transform.position.y < -15f && World.I != null) agent.Teleport(World.I.Spawns.Length > 0 ? World.I.Spawns[0] : Vector3.up, rig.Yaw);
            }

            // a team mate close by below the blood threshold: the blood button shows
            bloodMate = alive && !Healing && Time.time >= nextGive ? WoundedMate(s) : null;
            Controls.HealAvailable = bloodMate != null;

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
            agent.SetVisible(!rig.Scope && !rig.Close);

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
            // no spread: the bullet goes exactly where the crosshair is (shotgun pellets keep their pattern)
            var shot = new Shot { Weapon = def.Id, Origin = origin, Dir = dir, Seed = rng.Next(), Victim = Session.NoPlayer };

            // local feedback right away; what was hit on this screen goes with the shot to the host
            var muzzle = agent.Muzzle.position;
            foreach (var d in Ballistics.Pellets(dir, shot.Seed, def))
            {
                var hit = world.TraceShot(origin, d, def.Range, agent.Id);
                Effects.Tracer(muzzle, hit.Point, def.Pellets > 1 ? 0.6f : 1f);
                if (hit.Kind == 0) Effects.Impact(hit.Point, -d);
                else if (hit.Kind == 1)
                {
                    Effects.Blood(hit.Point, -d);
                    if (shot.Victim == Session.NoPlayer) { shot.Victim = hit.Victim; shot.Head = hit.Head; }
                    if (hit.Victim == shot.Victim && shot.Pellets < 255) shot.Pellets++;
                }
            }
            Session.I.FireLocal(shot);
            Effects.MuzzleFlash(agent.Muzzle, def);
            Sfx.PlayAt(def.Sound, muzzle, 1f, def.SoundPitch, false);
            agent.PlayFire();
            bloom = Mathf.Min(bloom + def.Recoil * 0.5f, 6f);
            // the barrel climbs a fixed amount per shot, half as much with the crosshair on an enemy (aim assist)
            rig.Kick(def.Climb * (onEnemy && Settings.AimAssist ? 0.5f : 1f));
            if (def.BoltAction)
            {
                Aiming = false;   // bolt action: re-aim after every shot
                boltAt = Time.time + 0.42f;
            }
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

        /// <summary>The nearest living team mate within reach whose health is below the blood threshold.</summary>
        private Agent WoundedMate(Session s)
        {
            if (s == null || s.Config.Mode != Mode.TeamDeathmatch || World.I == null) return null;
            var me = agent.State;
            Agent mate = null;
            float best = Session.HealRange;
            foreach (var a in World.I.Agents.Values)
            {
                if (a == null || a == agent || a.State == null || !a.State.Alive || a.State.Team != me.Team || a.State.Health >= Session.BloodThreshold) continue;
                float d = Vector3.Distance(a.transform.position, transform.position);
                if (d < best) { best = d; mate = a; }
            }
            return mate;
        }

        /// <summary>The blood button: a transfusion to the wounded team mate close by (40 health).</summary>
        private void StartHeal()
        {
            healTarget = bloodMate.Id;
            healEnd = Time.time + 2f;
            Aiming = false;
            reloadEnd = 0;
            agent.PlayReload();
            agent.ShowSyringe(1, 2f);
            Sfx.Play(Sfx.Has("dry_1") ? "dry_1" : "click", 0.7f, 1.3f);
        }

        private void FinishHeal(Session s)
        {
            healEnd = 0;
            if (!agent.State.Alive) return;
            // the team mate must still be close and alive
            if (World.I == null || !World.I.Agents.TryGetValue(healTarget, out var mate) || mate == null || mate.State == null || !mate.State.Alive
                || Vector3.Distance(mate.transform.position, transform.position) > Session.HealRange + 0.5f)
            {
                App.I?.Toast("TOO FAR AWAY");
                return;
            }
            nextGive = Time.time + Session.BloodCooldown;
            s.HealLocal(healTarget, 1);
        }

        /// <summary>Throw a frag grenade along the crosshair, a little upwards, from the right shoulder.</summary>
        private void ThrowGrenade()
        {
            Grenades--;
            nextThrow = Time.time + 1f;
            Aiming = false;
            var rig = CameraRig.I;
            var origin = agent.Eye + Quaternion.Euler(0, rig.Yaw, 0) * new Vector3(0.3f, 0.15f, 0.45f);
            if (Physics.Linecast(agent.Eye, origin, World.EnvMask, QueryTriggerInteraction.Ignore)) origin = agent.Eye;
            // thrown a little above the crosshair (a 14 degree loft): level aim lands some 11 m off (4 m more
            // than before) and rolls on; aiming higher throws further, as a real throw does (up to ~45 degrees)
            float loft = Mathf.Clamp(rig.Pitch + 14f, -30f, 46f);
            var dir = Quaternion.Euler(-loft, rig.Yaw, 0f) * Vector3.forward;
            var throwVelocity = dir * (prone ? 9f : 15f) + velocity * 0.35f;
            Session.I.ThrowLocal(origin, throwVelocity);
            // the fuse on the grenade itself is the throw sound now, and the arm swing is played there (Grenade.Spawn)
        }

        // ---------------------------------------------------------------- aim assist
        // As in Battle of Agents: while the crosshair is on an enemy (a ray from the camera hits one within
        // 100 m) the look turns at half speed and the recoil climbs half as much, so the finger can stay on
        // target. Nothing bends the shots and nothing pulls the crosshair: the bullet goes where it points.

        private bool onEnemy;

        private bool Enemy(Agent other) =>
            other != agent && other.State != null && other.State.Alive && World.I.IsEnemy(agent.State, other.State, Session.I);

        private Vector2 AimAssist(CameraRig rig, Vector2 look, float dt)
        {
            var ray = rig.AimRay();
            var hit = World.I.TraceShot(ray.origin, ray.direction, 100f, agent.Id);
            onEnemy = hit.Kind == 1 && World.I.Agents.TryGetValue(hit.Victim, out var other) && other != null && Enemy(other);
            return onEnemy ? look * 0.5f : look;
        }

        public void Swap()
        {
            Slot = 1 - Slot;
            reloadEnd = 0;
            Aiming = false;
            nextFire = Time.time + 0.45f;
            Sfx.Play(Sfx.Has("dry_1") ? "dry_1" : "reload", 0.5f, Sfx.Has("dry_1") ? 0.9f : 1.4f);
        }

        public void StartReload()
        {
            var def = Current;
            if (Reloading || Mag[Slot] >= def.MagSize || Reserve[Slot] <= 0) return;
            reloadEnd = Time.time + def.ReloadTime;
            Aiming = false;
            agent.PlayReload();
            Sfx.Play(Weapons.ReloadSound(def), 0.85f);
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
            Sfx.Play(Weapons.ReloadSound(Current), 0.9f, 1.1f);
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
