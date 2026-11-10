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
        private float bodyYaw;
        private bool bodyYawSet;

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
            Controls.AmmoFull = false;
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
            bodyYawSet = false;
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
            if (vaulting) VaultStep(dt, alive);
            else if (cc.enabled)
            {
                if (cc.isGrounded)
                {
                    verticalSpeed = -2f;
                    // through a window: by walking into it for a moment, or with the jump button (which also
                    // takes a low wall)
                    bool mayVault = canMove && s.Phase == Phase.Playing && !prone;
                    pushTime = mayVault && input.y > 0.5f ? pushTime + dt : 0f;
                    var ahead = wish.sqrMagnitude > 0.1f ? wish : Quaternion.Euler(0, rig.Yaw, 0) * Vector3.forward;
                    if (mayVault && (Controls.JumpPressed && TryVault(ahead, true) || pushTime > 0.15f && TryVault(ahead, false)))
                    {
                        pushTime = 0f;
                        crouch = false;
                    }
                    else if (canMove && Controls.JumpPressed && s.Phase == Phase.Playing)
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
                var hit = cc.Move((velocity + Vector3.up * verticalSpeed) * dt);
                // walking into a wall: do not keep pushing (keeps the animation honest). Only a real wall at the
                // side, and a real stop: a slope, a kerb or a seam in the ground under the feet takes a little of
                // a step's way and must not slow the run down
                var moved = (transform.position - before) / Mathf.Max(dt, 1e-4f);
                moved.y = 0;
                if ((hit & CollisionFlags.Sides) != 0 && moved.sqrMagnitude < velocity.sqrMagnitude * 0.6f) velocity = Vector3.Lerp(velocity, moved, 0.5f);
                // the body turns after the aim as softly as the camera does (20 a second): the uneven steps of a
                // finger on the glass do not jerk it round, and it stays where the camera sees it
                if (alive)
                {
                    bodyYaw = bodyYawSet && Mathf.Abs(Mathf.DeltaAngle(bodyYaw, rig.Yaw)) < 45f ? Mathf.LerpAngle(bodyYaw, rig.Yaw, 1f - Mathf.Exp(-20f * dt)) : rig.Yaw;
                    bodyYawSet = true;
                    transform.rotation = Quaternion.Euler(0, bodyYaw, 0);
                }
                if (transform.position.y < -15f && World.I != null) agent.Teleport(World.I.Spawns.Length > 0 ? World.I.Spawns[0] : Vector3.up, rig.Yaw);
            }

            // a team mate close by below the blood threshold: the blood button shows
            bloodMate = alive && !Healing && Time.time >= nextGive ? WoundedMate(s) : null;
            Controls.HealAvailable = bloodMate != null;

            // ammo crates: the button shows while standing next to one (dimmed when nothing is missing:
            // the extra magazines from kills can make the reserve fuller than a crate fills it)
            bool atBox = alive && AmmoBox.Nearest(transform.position) != null;
            Controls.AmmoAvailable = atBox;
            Controls.AmmoFull = atBox && !NeedsSupplies;
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

            // write our state for the network and the animations; not while the host's new place for us after a
            // death is still waiting to be taken (the body still lies where it fell: that place would overwrite
            // the new one, and the player came back where they died)
            if (agent.SpawnPending) return;
            state.Pos = transform.position;
            state.Yaw = rig.Yaw;
            state.Pitch = rig.AimPitch;
            // animation follows the real speed: 1 = running, 2 = sprinting
            // the way the stick points (against the aim), at the speed the body really goes: turning while running
            // the body's speed lags a moment behind the new way, and taken alone it would swing the legs into the
            // side steps for that moment (a jerk at every turn)
            var local = Quaternion.Inverse(Quaternion.Euler(0, rig.Yaw, 0)) * velocity;
            var stick = new Vector3(input.x, 0, input.y);
            if (stick.sqrMagnitude > 0.04f && !prone) local = stick.normalized * new Vector2(velocity.x, velocity.z).magnitude;
            float norm = baseSpeed * def.MoveSpeed;
            state.MoveX = alive ? Mathf.Clamp(local.x / norm, -1f, 1f) : 0f;
            state.MoveY = !alive ? 0f : local.z > norm * 1.02f ? 1f + Mathf.Clamp01((local.z - norm) / Mathf.Max(0.1f, (SprintSpeed - RunSpeed) * def.MoveSpeed)) : Mathf.Clamp(local.z / norm, -1f, 1f);
            state.Crouch = crouch;
            state.Prone = prone;
            state.Grounded = !vaulting && (!cc.enabled || cc.isGrounded);
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
            // the shot goes along the crosshair's own line, from where that line passes level with the player's
            // eyes: what the camera sees past a corner or over a wall is what is hit, and the edge of a wall
            // beside the head or the gun never stops it (it passes through the air beside the wall, as seen)
            var origin = aim.origin + aim.direction * Mathf.Max(0f, Vector3.Dot(agent.Eye - aim.origin, aim.direction));
            var dir = aim.direction;
            // no spread: the bullet goes exactly where the crosshair is (shotgun pellets keep their pattern)
            var shot = new Shot { Weapon = def.Id, Origin = origin, Dir = dir, Seed = rng.Next(), Victim = Session.NoPlayer };

            // local feedback right away; what was hit on this screen goes with the shot to the host
            var muzzle = agent.Muzzle.position;
            // the trail starts at the barrel, or on the line itself when the barrel is behind the wall's edge
            var trail = Physics.Linecast(muzzle, origin, World.EnvMask, QueryTriggerInteraction.Ignore) ? origin : muzzle;
            foreach (var d in Ballistics.Pellets(dir, shot.Seed, def))
            {
                var hit = world.TraceShot(origin, d, def.Range, agent.Id);
                Effects.Tracer(trail, hit.Point, def.Pellets > 1 ? 0.6f : 1f);
                if (hit.Sacred) { Hud.I?.ShowCenter("DO NOT SHOOT THE BOARDS", "the shot comes back on you", 1.6f); continue; }
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
            // bolt action: the bolt is worked (its sound) with the eye kept at the scope
            if (def.BoltAction) boltAt = Time.time + 0.42f;
            if (Mag[Slot] == 0) StartReload();
        }

        /// <summary>
        /// Body height for standing / crouching / lying down. Standing up again is only refused when
        /// something solid is really above the player (then they stay down until there is room).
        /// </summary>
        // through a window or over a low wall: carried along a short path (the body is taller than a window)
        private bool vaulting;
        private Vector3 vaultFrom, vaultSill, vaultTo, vaultDir;
        private float vaultTime, vaultLength, pushTime;

        /// <summary>
        /// Is there a way through or over what stands before the player along 'dir'? A window: a sill up to
        /// 1.4 m with an opening at least 0.75 m high and wide enough over it, a lintel above, and a floor
        /// to land on beyond (walking into it goes through when the floor beyond is not far below; with the
        /// jump button also out of an upper window). A low wall (up to 1.25 m, open above, lower ground
        /// beyond): only with the jump button. Starts the vault when there is.
        /// </summary>
        private bool TryVault(Vector3 dir, bool byJump)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();
            int mask = World.EnvMask | (1 << World.FenceLayer);
            var feet = transform.position;
            // something low in the way, met squarely: the sill
            if (!Physics.Raycast(feet + Vector3.up * 0.3f, dir, out var low, 0.95f, mask, QueryTriggerInteraction.Ignore)) return false;
            var into = -low.normal;
            into.y = 0;
            if (into.sqrMagnitude < 0.5f || Vector3.Dot(into.normalized, dir) < 0.6f) return false;
            into.Normalize();
            float wall = Vector3.Dot(low.point - feet, into);
            float reach = wall + 0.75f;
            // where the opening over it begins and where it ends
            float bottom = -1f, top = -1f;
            for (float h = 0.45f; h <= 2.9f; h += 0.1f)
            {
                bool blocked = Physics.Raycast(feet + Vector3.up * h, into, reach, mask, QueryTriggerInteraction.Ignore);
                if (bottom < 0f)
                {
                    if (!blocked) bottom = h;
                    else if (h > 1.4f) return false;
                }
                else if (blocked) { top = h; break; }
            }
            if (bottom < 0f) return false;
            bool window = top > 0f;
            if ((window ? top : 3f) - bottom < 0.75f) return false;
            if (!window && (!byJump || bottom > 1.25f || bottom < 0.6f)) return false;
            // room for the body through it: straight ahead, or a little to one side
            var side = Vector3.Cross(Vector3.up, into);
            float off = float.NaN;
            foreach (float o in new[] { 0f, -0.2f, 0.2f, -0.4f, 0.4f })
            {
                var from = feet + side * o + Vector3.up * (bottom + 0.4f);
                if (Physics.CheckSphere(from, 0.25f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.SphereCast(from, 0.27f, into, out _, reach + 0.45f, mask, QueryTriggerInteraction.Ignore)) continue;
                off = o;
                break;
            }
            if (float.IsNaN(off)) return false;
            // ground to land on beyond, lower than the sill (not the top of a block, not a stair)
            var over = feet + side * off + into * (reach + 0.45f);
            float drop = byJump ? 3.6f : 1.4f;
            if (!Physics.Raycast(over + Vector3.up * (bottom + 0.4f), Vector3.down, out var ground, bottom + 0.4f + drop, mask, QueryTriggerInteraction.Ignore)) return false;
            if (ground.normal.y < 0.6f || ground.point.y > feet.y + bottom - 0.25f) return false;
            var land = ground.point;
            if (Physics.CheckCapsule(land + Vector3.up * 0.45f, land + Vector3.up * 1.3f, 0.3f, mask, QueryTriggerInteraction.Ignore)) return false;
            vaultFrom = feet;
            vaultSill = feet + side * off + into * (wall + 0.2f) + Vector3.up * (bottom + 0.05f);
            // far down beyond (out of an upper window): carried only past the wall, the fall is its own
            vaultTo = land.y < feet.y + bottom - 1.6f ? new Vector3(over.x, feet.y + bottom - 0.4f, over.z) : land;
            vaultDir = into;
            vaultTime = 0f;
            vaultLength = Mathf.Clamp((Vector3.Distance(vaultFrom, vaultSill) + Vector3.Distance(vaultSill, vaultTo)) / 5.2f, 0.42f, 0.8f);
            vaulting = true;
            Aiming = false;
            return true;
        }

        private void VaultStep(float dt, bool alive)
        {
            if (!alive) { vaulting = false; return; }
            vaultTime += dt;
            float t = Mathf.Clamp01(vaultTime / vaultLength);
            // up onto the sill, then through and down
            var p = t < 0.45f ? Vector3.Lerp(vaultFrom, vaultSill, Mathf.SmoothStep(0f, 1f, t / 0.45f))
                              : Vector3.Lerp(vaultSill, vaultTo, Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 0.55f));
            bool was = cc.enabled;
            cc.enabled = false;
            transform.position = p;
            cc.enabled = was;
            if (t < 1f) return;
            vaulting = false;
            velocity = vaultDir * RunSpeed * 0.7f;
            verticalSpeed = -1f;
        }

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
            if (s == null || World.I == null || agent.State == null || agent.State.Team == 0) return null;
            var me = agent.State;
            Agent mate = null;
            float best = Session.HealRange;
            foreach (var a in World.I.Agents.Values)
            {
                if (a == null || a == agent || a.State == null || !a.State.Alive || !MatchConfig.Mates(a.State, me) || a.State.Health >= Session.BloodThreshold) continue;
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
            // thrown a little above the crosshair (a 14 degree loft): level aim lands some 14 m off and rolls on;
            // aiming higher throws further, as a real throw does (up to ~45 degrees: some 35 m)
            float loft = Mathf.Clamp(rig.Pitch + 14f, -30f, 46f);
            var dir = Quaternion.Euler(-loft, rig.Yaw, 0f) * Vector3.forward;
            var throwVelocity = dir * (prone ? 10.5f : 17f) + velocity * 0.35f;
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
            if (!NeedsSupplies) { App.I?.Toast("AMMO IS FULL"); return; }
            reloadEnd = 0;
            // full magazines and at least the standard reserve: a crate never takes away what kills gave
            for (int i = 0; i < 2; i++)
            {
                var def = Weapons.Get(i == 0 ? agent.State.Primary : agent.State.Secondary);
                Mag[i] = def.MagSize;
                Reserve[i] = Mathf.Max(Reserve[i], def.Reserve);
            }
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
