using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Physics;

namespace ArenaRush.Combat
{
    /// <summary>
    /// The player's weapons: AK-47, M4A1 and pistol. Hitscan shooting from the camera through the crosshair,
    /// spread, recoil, magazines/reserve ammo, reload, weapon switching, muzzle flash and physics impulses.
    /// Weapon models are child entities named "Gun_&lt;Id&gt;" under the player's model.
    /// </summary>
    public class PlayerWeapon : SyncScript
    {
        public static PlayerWeapon Instance { get; private set; }

        private readonly Random random = new();
        private readonly List<HitResult> hits = new();
        private Entity camera;
        private Health health;
        private int current;
        private int[] magazine;
        private int[] reserve;
        private Entity[] gunEntities;
        private Entity[] muzzleFlashes;
        private float fireCooldown;
        private float reloadTimer = -1;
        private float flashTimer;
        private float bloom;          // extra spread while firing
        private bool triggerWasHeld;
        private float lastDamageTime = -100;
        private float time;

        public WeaponDef Current => WeaponDef.All[current];

        public override void Start()
        {
            Instance = this;
            var all = Entity.Scene.Entities.SelectMany(Descendants).ToList();
            camera = all.FirstOrDefault(e => e.Name == "Camera");
            health = Entity.Get<Health>();
            if (health != null)
            {
                health.Damaged += _ =>
                {
                    lastDamageTime = time;
                    PlayerStatus.DamageFlash = 0.35f;
                    SoundBank.Play("hurt", 0.7f);
                };
                health.Died += () =>
                {
                    if (GameFlow.State == FlowState.Playing)
                        GameFlow.Set(FlowState.Defeat);
                };
            }

            var count = WeaponDef.All.Length;
            magazine = WeaponDef.All.Select(w => w.Magazine).ToArray();
            reserve = WeaponDef.All.Select(w => w.StartReserve).ToArray();
            gunEntities = new Entity[count];
            muzzleFlashes = new Entity[count];
            for (var i = 0; i < count; i++)
            {
                gunEntities[i] = all.FirstOrDefault(e => e.Name == "Gun_" + WeaponDef.All[i].Id && IsDescendantOf(e, Entity));
                muzzleFlashes[i] = gunEntities[i]?.GetChildren().FirstOrDefault(c => c.Name.StartsWith("MuzzleFlash", StringComparison.Ordinal));
            }
            Equip(0);
        }

        public override void Cancel()
        {
            if (Instance == this) Instance = null;
        }

        public void AddAmmo(int magazines)
        {
            for (var i = 0; i < reserve.Length; i++)
                reserve[i] += WeaponDef.All[i].Magazine * magazines;
        }

        public override void Update()
        {
            var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            time += dt;
            fireCooldown -= dt;
            bloom = Math.Max(0, bloom - dt * 6f);
            PlayerStatus.HitMarker = Math.Max(0, PlayerStatus.HitMarker - dt);
            PlayerStatus.DamageFlash = Math.Max(0, PlayerStatus.DamageFlash - dt);

            if (flashTimer > 0)
            {
                flashTimer -= dt;
                if (flashTimer <= 0)
                    SetVisible(muzzleFlashes[current], false);
            }

            if (health != null)
            {
                // slow regeneration after 4 s without damage
                if (time - lastDamageTime > 4f && !health.IsDead)
                    health.Heal(6f * dt);
                PlayerStatus.Health = health.Current;
                PlayerStatus.MaxHealth = health.MaxHealth;
            }

            if (GameFlow.IsPlaying)
            {
                if (TouchState.SwapPressed)
                    Equip((current + 1) % WeaponDef.All.Length);
                if (TouchState.ReloadPressed)
                    StartReload();

                if (reloadTimer >= 0)
                {
                    reloadTimer += dt;
                    if (reloadTimer >= Current.ReloadTime)
                        FinishReload();
                }
                else
                {
                    var held = TouchState.FireHeld;
                    var wantsShot = held && (Current.Automatic || !triggerWasHeld);
                    if (wantsShot && fireCooldown <= 0)
                    {
                        if (magazine[current] > 0)
                            Fire();
                        else
                        {
                            fireCooldown = 0.3f;
                            SoundBank.Play("empty");
                            StartReload();
                        }
                    }
                    triggerWasHeld = held;
                }
            }
            else
            {
                triggerWasHeld = false;
            }

            PlayerStatus.WeaponName = Current.Name;
            PlayerStatus.Ammo = magazine[current];
            PlayerStatus.Reserve = reserve[current];
            PlayerStatus.ReloadProgress = reloadTimer >= 0 ? reloadTimer / Current.ReloadTime : -1;
            PlayerStatus.Spread = 14f + (Current.Spread + bloom) * 9f;
        }

        private void Fire()
        {
            var weapon = Current;
            fireCooldown = weapon.FireInterval;
            magazine[current]--;
            GameFlow.ShotsFired++;

            flashTimer = 0.05f;
            SetVisible(muzzleFlashes[current], true);
            SoundBank.Play(weapon.Sound, 0.9f);
            Camera.ThirdPersonCamera.AddRecoil(weapon.Recoil * (0.6f + (float)random.NextDouble() * 0.6f), (float)(random.NextDouble() - 0.5) * weapon.Recoil * 0.6f);

            if (camera == null) return;
            var world = camera.Transform.WorldMatrix;
            var origin = world.TranslationVector;
            var spreadRad = MathUtil.DegreesToRadians(weapon.Spread + bloom) * 0.5f;
            var dir = Vector3.Normalize(world.Forward
                                        + world.Right * (float)((random.NextDouble() * 2 - 1) * Math.Tan(spreadRad))
                                        + world.Up * (float)((random.NextDouble() * 2 - 1) * Math.Tan(spreadRad)));
            bloom = Math.Min(bloom + weapon.Recoil * 0.6f, 4f);

            hits.Clear();
            this.GetSimulation().RaycastPenetrating(origin, origin + dir * weapon.Range, hits);
            foreach (var hit in hits.OrderBy(h => h.HitFraction))
            {
                var hitEntity = hit.Collider?.Entity;
                if (hitEntity == null || hitEntity == Entity)
                    continue;

                var targetHealth = FindHealth(hitEntity);
                if (targetHealth != null && targetHealth != health)
                {
                    var headshot = hit.Point.Y > hitEntity.Transform.WorldMatrix.TranslationVector.Y + 1.45f;
                    targetHealth.Damage(weapon.Damage * (headshot ? 2.2f : 1f));
                    GameFlow.ShotsHit++;
                    PlayerStatus.HitMarker = 0.18f;
                    PlayerStatus.LastHitHeadshot = headshot;
                    SoundBank.Play("hit", 0.6f);
                }
                else if (hit.Collider is RigidbodyComponent body && !body.IsKinematic)
                {
                    body.Activate(true);
                    var local = hit.Point - hitEntity.Transform.WorldMatrix.TranslationVector;
                    body.ApplyImpulse(dir * weapon.Damage * 0.35f, local);
                }
                break; // first solid hit stops the bullet
            }
        }

        private void StartReload()
        {
            if (reloadTimer >= 0 || magazine[current] >= Current.Magazine || reserve[current] <= 0)
                return;
            reloadTimer = 0;
            SoundBank.Play("reload", 0.9f);
        }

        private void FinishReload()
        {
            var needed = Current.Magazine - magazine[current];
            var taken = Math.Min(needed, reserve[current]);
            magazine[current] += taken;
            reserve[current] -= taken;
            reloadTimer = -1;
        }

        private void Equip(int index)
        {
            reloadTimer = -1;
            current = index;
            for (var i = 0; i < gunEntities.Length; i++)
            {
                SetVisible(gunEntities[i], i == current);
                SetVisible(muzzleFlashes[i], false);
            }
            fireCooldown = 0.25f;
            SoundBank.Play("click", 0.8f);
        }

        private static Health FindHealth(Entity entity)
        {
            for (var e = entity; e != null; e = e.GetParent())
            {
                var h = e.Get<Health>();
                if (h != null) return h;
            }
            return null;
        }

        private static void SetVisible(Entity entity, bool visible)
        {
            if (entity == null) return;
            foreach (var e in Descendants(entity))
            {
                var model = e.Get<ModelComponent>();
                if (model != null)
                    model.Enabled = visible;
            }
        }

        private static bool IsDescendantOf(Entity entity, Entity ancestor)
        {
            for (var e = entity.GetParent(); e != null; e = e.GetParent())
                if (e == ancestor) return true;
            return false;
        }

        internal static IEnumerable<Entity> Descendants(Entity e)
        {
            yield return e;
            foreach (var c in e.GetChildren())
                foreach (var d in Descendants(c))
                    yield return d;
        }
    }
}
