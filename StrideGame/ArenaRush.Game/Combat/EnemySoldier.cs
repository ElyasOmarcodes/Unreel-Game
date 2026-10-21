using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Physics;

namespace ArenaRush.Combat
{
    /// <summary>
    /// Hostile soldier: patrols around its post, chases the player when it sees them and fires bursts.
    /// Expects a CharacterComponent and a Health on its entity, and the model (with an AnimationComponent
    /// holding "Idle", "Run" and "Shoot" clips) as first child.
    /// </summary>
    public class EnemySoldier : SyncScript
    {
        public float WalkSpeed { get; set; } = 2.2f;
        public float RunSpeed { get; set; } = 4.2f;
        public float SightRange { get; set; } = 34f;
        public float AttackRange { get; set; } = 22f;
        public float Damage { get; set; } = 7f;
        public float BurstInterval { get; set; } = 1.6f;

        private enum State { Patrol, Chase, Attack, Dead }

        private static readonly Random random = new();
        private readonly List<HitResult> hits = new();
        private State state = State.Patrol;
        private CharacterComponent character;
        private Health health;
        private Entity model;
        private AnimationComponent animation;
        private Entity muzzleFlash;
        private Entity player;
        private Health playerHealth;
        private Vector3 home;
        private Vector3 patrolTarget;
        private float stateTime;
        private float burstTimer;
        private int burstLeft;
        private float shotTimer;
        private float flashTimer;
        private float yaw;
        private string currentClip;
        private float seenTime;

        public override void Start()
        {
            character = Entity.Get<CharacterComponent>();
            health = Entity.Get<Health>();
            model = Entity.GetChild(0);
            animation = model?.Get<AnimationComponent>();
            muzzleFlash = PlayerWeapon.Descendants(Entity).FirstOrDefault(e => e.Name.StartsWith("MuzzleFlash", StringComparison.Ordinal));
            SetVisible(muzzleFlash, false);
            player = Entity.Scene.Entities.FirstOrDefault(e => e.Name == "Player");
            playerHealth = player?.Get<Health>();
            home = Entity.Transform.Position;
            patrolTarget = NewPatrolTarget();
            yaw = (float)(random.NextDouble() * Math.PI * 2);
            burstTimer = (float)random.NextDouble() * BurstInterval;
            if (health != null)
            {
                health.Died += Die;
                health.Damaged += _ =>
                {
                    // getting shot reveals the player
                    if (state == State.Patrol) SetState(State.Chase);
                    seenTime = 3f;
                };
            }
            Play("Idle");
        }

        public override void Update()
        {
            var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            stateTime += dt;
            if (flashTimer > 0)
            {
                flashTimer -= dt;
                if (flashTimer <= 0) SetVisible(muzzleFlash, false);
            }

            if (state == State.Dead)
            {
                UpdateDeath(dt);
                return;
            }

            if (!GameFlow.IsPlaying || player == null)
            {
                character?.SetVelocity(Vector3.Zero);
                return;
            }

            var pos = Entity.Transform.WorldMatrix.TranslationVector;
            var playerPos = player.Transform.WorldMatrix.TranslationVector;
            var toPlayer = playerPos - pos;
            toPlayer.Y = 0;
            var distance = toPlayer.Length();
            var sees = distance < SightRange && (playerHealth == null || !playerHealth.IsDead) && CanSee(pos, playerPos);
            seenTime = sees ? 3f : Math.Max(0, seenTime - dt);

            Vector3 move = Vector3.Zero;
            switch (state)
            {
                case State.Patrol:
                    {
                        var toTarget = patrolTarget - pos;
                        toTarget.Y = 0;
                        if (toTarget.Length() < 1f || stateTime > 12f)
                        {
                            patrolTarget = NewPatrolTarget();
                            stateTime = 0;
                        }
                        else
                            move = Vector3.Normalize(toTarget) * WalkSpeed;
                        if (sees) SetState(State.Chase);
                        break;
                    }
                case State.Chase:
                    if (distance < AttackRange && sees)
                        SetState(State.Attack);
                    else if (seenTime <= 0 && stateTime > 6f)
                        SetState(State.Patrol);
                    else if (distance > 0.1f)
                        move = toPlayer / distance * RunSpeed;
                    break;
                case State.Attack:
                    if (!sees || distance > AttackRange * 1.2f)
                    {
                        SetState(State.Chase);
                        break;
                    }
                    // keep some distance, strafe slowly
                    if (distance < 7f)
                        move = -toPlayer / distance * WalkSpeed;
                    UpdateShooting(dt, distance);
                    break;
            }

            character.SetVelocity(move);

            // face movement or the player
            var face = state == State.Attack || (state == State.Chase && sees) ? toPlayer : move;
            if (face.LengthSquared() > 0.01f)
            {
                var targetYaw = (float)Math.Atan2(-face.Z, face.X) + MathUtil.PiOverTwo;
                yaw = LerpAngle(yaw, targetYaw, 1f - MathF.Exp(-dt * 10f));
            }
            if (model != null)
                model.Transform.Rotation = Quaternion.RotationY(yaw);

            if (state == State.Attack)
                Play(burstLeft > 0 ? "Shoot" : "Idle");
            else
                Play(move.LengthSquared() > 0.1f ? "Run" : "Idle");
        }

        private void UpdateShooting(float dt, float distance)
        {
            if (burstLeft <= 0)
            {
                burstTimer -= dt;
                if (burstTimer <= 0)
                {
                    burstLeft = 3 + random.Next(3);
                    shotTimer = 0;
                    burstTimer = BurstInterval * (0.7f + (float)random.NextDouble() * 0.6f);
                }
                return;
            }

            shotTimer -= dt;
            if (shotTimer > 0) return;
            shotTimer = 0.11f;
            burstLeft--;

            flashTimer = 0.05f;
            SetVisible(muzzleFlash, true);
            SoundBank.Play("enemy", MathUtil.Clamp(1.2f - distance / 40f, 0.15f, 0.8f));

            // hit chance falls with distance; moving targets are harder to hit
            var moving = TouchState.Stick.LengthSquared() > 0.1f;
            var chance = MathUtil.Clamp(0.65f - distance * 0.018f, 0.12f, 0.65f) * (moving ? 0.7f : 1f);
            if (random.NextDouble() < chance)
                playerHealth?.Damage(Damage);
        }

        private bool CanSee(Vector3 from, Vector3 to)
        {
            var eye = from + new Vector3(0, 1.6f, 0);
            var target = to + new Vector3(0, 1.3f, 0);
            hits.Clear();
            this.GetSimulation().RaycastPenetrating(eye, target, hits);
            foreach (var hit in hits.OrderBy(h => h.HitFraction))
            {
                var e = hit.Collider?.Entity;
                if (e == null || e == Entity) continue;
                if (e == player) return true;
                if (hit.Collider is RigidbodyComponent) continue;   // small props don't block sight
                if (e.Get<EnemySoldier>() != null) continue;        // nor other soldiers
                return false;
            }
            return true;
        }

        private void Die()
        {
            state = State.Dead;
            stateTime = 0;
            character?.SetVelocity(Vector3.Zero);
            if (character != null)
                Entity.Remove(character);
            SetVisible(muzzleFlash, false);
            Play("Idle");
            GameFlow.EnemiesKilled++;
            var left = GameFlow.EnemiesTotal - GameFlow.EnemiesKilled;
            if (left <= 0)
            {
                GameFlow.Show("FACTORY CLEARED!", 4f);
                GameFlow.Set(FlowState.Victory);
            }
            else
                GameFlow.Show(left == 1 ? "1 hostile left" : $"{left} hostiles left", 1.6f);
        }

        private void UpdateDeath(float dt)
        {
            if (model == null) return;
            // fall backwards over half a second, then sink into the ground and disappear
            var fall = MathUtil.Clamp(stateTime / 0.5f, 0, 1);
            model.Transform.Rotation = Quaternion.RotationY(yaw) * Quaternion.RotationX(-MathUtil.PiOverTwo * fall);
            model.Transform.Position = new Vector3(0, 0.25f * fall - Math.Max(0, stateTime - 6f) * 0.3f, 0);
            if (stateTime > 9f)
                Entity.Scene?.Entities.Remove(Entity);
        }

        private void SetState(State newState)
        {
            state = newState;
            stateTime = 0;
        }

        private Vector3 NewPatrolTarget()
        {
            var a = random.NextDouble() * Math.PI * 2;
            var r = 3 + random.NextDouble() * 7;
            return home + new Vector3((float)(Math.Cos(a) * r), 0, (float)(Math.Sin(a) * r));
        }

        private void Play(string clip)
        {
            if (animation == null || clip == currentClip || !animation.Animations.ContainsKey(clip))
                return;
            currentClip = clip;
            animation.Crossfade(clip, TimeSpan.FromSeconds(0.2));
        }

        private static float LerpAngle(float a, float b, float t)
        {
            var d = (b - a) % MathUtil.TwoPi;
            if (d > MathUtil.Pi) d -= MathUtil.TwoPi;
            if (d < -MathUtil.Pi) d += MathUtil.TwoPi;
            return a + d * t;
        }

        private static void SetVisible(Entity entity, bool visible)
        {
            if (entity == null) return;
            foreach (var e in PlayerWeapon.Descendants(entity))
            {
                var m = e.Get<ModelComponent>();
                if (m != null) m.Enabled = visible;
            }
        }
    }
}
