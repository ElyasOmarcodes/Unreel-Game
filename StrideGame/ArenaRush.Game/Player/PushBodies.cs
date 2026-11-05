using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Physics;

namespace ArenaRush.Player
{
    /// <summary>
    /// Lets the (kinematic) character shove dynamic rigid bodies such as crates, barrels and tyres.
    /// </summary>
    public class PushBodies : SyncScript
    {
        public float PushStrength { get; set; } = 6f;

        public float Reach { get; set; } = 0.75f;

        private readonly List<RigidbodyComponent> bodies = new();
        private Vector3 lastPosition;

        public override void Start()
        {
            foreach (var entity in Entity.Scene.Entities)
            {
                var body = entity.Get<RigidbodyComponent>();
                if (body != null && !body.IsKinematic)
                    bodies.Add(body);
            }
            lastPosition = Entity.Transform.WorldMatrix.TranslationVector;
        }

        public override void Update()
        {
            var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            var position = Entity.Transform.WorldMatrix.TranslationVector;
            var velocity = dt > 0 ? (position - lastPosition) / dt : Vector3.Zero;
            lastPosition = position;
            velocity.Y = 0;
            if (velocity.LengthSquared() < 0.25f)
                return;

            foreach (var body in bodies)
            {
                var bodyPos = body.Entity.Transform.WorldMatrix.TranslationVector;
                var offset = bodyPos - position;
                offset.Y = 0;
                var distance = offset.Length();
                var bounds = body.Entity.Get<ModelComponent>()?.BoundingBox;
                var radius = bounds.HasValue ? Vector3.Distance(bounds.Value.Minimum, bounds.Value.Maximum) * 0.35f : 0.5f;
                if (distance > Reach + radius || distance < 0.001f)
                    continue;

                var direction = offset / distance;
                if (Vector3.Dot(direction, Vector3.Normalize(velocity)) < 0.2f)
                    continue;

                body.Activate(true);
                var impulse = direction * PushStrength * body.Mass * dt * 2.5f + Vector3.UnitY * body.Mass * dt * 0.8f;
                body.ApplyImpulse(impulse);
            }
        }
    }
}
