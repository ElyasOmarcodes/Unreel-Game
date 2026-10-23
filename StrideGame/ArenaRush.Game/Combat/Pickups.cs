using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace ArenaRush.Combat
{
    /// <summary>
    /// Glowing supply cells around the factory: +40 health and one magazine for every weapon.
    /// They respawn after a while. Also counts the hostiles at start.
    /// </summary>
    public class Pickups : SyncScript
    {
        public float PickupRadius { get; set; } = 1.4f;
        public float RespawnTime { get; set; } = 30f;

        private readonly List<(Entity Entity, Vector3 Base, float Phase)> cells = new();
        private readonly Dictionary<Entity, float> hidden = new();
        private Entity player;
        private Health playerHealth;
        private float time;

        public override void Start()
        {
            var scene = Entity.Scene;
            player = scene.Entities.First(e => e.Name == "Player");
            playerHealth = player.Get<Health>();
            var i = 0;
            foreach (var e in scene.Entities.Where(e => e.Name.StartsWith("EnergyCell", StringComparison.Ordinal)))
                cells.Add((e, e.Transform.Position, i++ * 0.7f));
            GameFlow.EnemiesTotal = scene.Entities.Count(e => e.Get<EnemySoldier>() != null);
        }

        public override void Update()
        {
            var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            time += dt;
            if (GameFlow.IsPlaying)
                GameFlow.MatchTime += dt;
            GameFlow.MessageTime = Math.Max(0, GameFlow.MessageTime - dt);

            var playerPos = player.Transform.WorldMatrix.TranslationVector;
            foreach (var (entity, basePos, phase) in cells)
            {
                if (hidden.TryGetValue(entity, out var hiddenAt))
                {
                    if (time - hiddenAt > RespawnTime)
                    {
                        hidden.Remove(entity);
                        SetVisible(entity, true);
                    }
                    continue;
                }

                entity.Transform.Position = basePos + new Vector3(0, MathF.Sin(time * 2f + phase) * 0.15f, 0);
                entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(time * 1.5f + phase, 0.25f, 0);

                if (!GameFlow.IsPlaying) continue;
                var delta = basePos - playerPos;
                if (new Vector2(delta.X, delta.Z).Length() < PickupRadius && delta.Y > -0.5f && delta.Y < 2.5f)
                {
                    hidden[entity] = time;
                    SetVisible(entity, false);
                    playerHealth?.Heal(40f);
                    PlayerWeapon.Instance?.AddAmmo(1);
                    SoundBank.Play("pickup");
                    GameFlow.Show("+40 HEALTH  +AMMO", 1.5f);
                }
            }
        }

        private static void SetVisible(Entity entity, bool visible)
        {
            foreach (var e in PlayerWeapon.Descendants(entity))
            {
                var m = e.Get<ModelComponent>();
                if (m != null) m.Enabled = visible;
            }
        }
    }
}
