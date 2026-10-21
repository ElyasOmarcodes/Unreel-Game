using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace ArenaRush
{
    /// <summary>Shared game progress shown by the HUD.</summary>
    public static class GameState
    {
        public static int Collected;
        public static int Total;
        public static float Elapsed;
        public static bool Finished;
        public static string Message = "";
        public static float MessageTime;

        public static void Show(string message, float seconds = 2.5f)
        {
            Message = message;
            MessageTime = seconds;
        }
    }

    /// <summary>
    /// First gameplay goal: collect every glowing energy cell spread around the factory.
    /// </summary>
    public class CellHunt : SyncScript
    {
        public float PickupRadius { get; set; } = 1.4f;

        private readonly List<(Entity Entity, Vector3 Base, float Phase)> cells = new();
        private Entity player;
        private float time;

        public override void Start()
        {
            var scene = Entity.Scene;
            player = scene.Entities.First(e => e.Name == "Player");
            var i = 0;
            foreach (var e in scene.Entities.Where(e => e.Name.StartsWith("EnergyCell", StringComparison.Ordinal)))
                cells.Add((e, e.Transform.Position, i++ * 0.7f));

            GameState.Collected = 0;
            GameState.Total = cells.Count;
            GameState.Elapsed = 0;
            GameState.Finished = false;
            GameState.Show("Find the glowing energy cells!", 4f);
        }

        public override void Update()
        {
            var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            time += dt;
            if (!GameState.Finished)
                GameState.Elapsed += dt;

            var playerPos = player.Transform.WorldMatrix.TranslationVector;
            for (var i = cells.Count - 1; i >= 0; i--)
            {
                var (entity, basePos, phase) = cells[i];
                entity.Transform.Position = basePos + new Vector3(0, MathF.Sin(time * 2f + phase) * 0.15f, 0);
                entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(time * 1.5f + phase, 0.25f, 0);

                var delta = basePos - playerPos;
                var horizontal = new Vector2(delta.X, delta.Z).Length();
                if (horizontal < PickupRadius && delta.Y > -0.5f && delta.Y < 2.5f)
                {
                    Entity.Scene.Entities.Remove(entity);
                    cells.RemoveAt(i);
                    GameState.Collected++;
                    if (GameState.Collected >= GameState.Total)
                    {
                        GameState.Finished = true;
                        GameState.Show($"FACTORY ONLINE!  Time {FormatTime(GameState.Elapsed)}", 8f);
                    }
                    else
                    {
                        GameState.Show($"Energy cell {GameState.Collected}/{GameState.Total}", 1.5f);
                    }
                }
            }

            GameState.MessageTime = Math.Max(0, GameState.MessageTime - dt);
        }

        public static string FormatTime(float seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }
    }
}
