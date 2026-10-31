using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// A ladder that can be climbed (the CityLord wooden ladder, put up in the maps by the editor). Its
    /// zone is the space in front of it, from the ground to a little above its top: standing in it and
    /// pushing towards the ladder climbs, pulling back climbs down, letting go holds on.
    /// </summary>
    public class Ladder : MonoBehaviour
    {
        public const float ClimbSpeed = 3.2f;

        /// <summary>World space: where a climber is on the ladder.</summary>
        public Bounds Zone;
        /// <summary>Horizontal direction from the climber into the ladder (towards the wall it leans on).</summary>
        public Vector3 Into = Vector3.forward;
        /// <summary>Height of the landing at the top (world y).</summary>
        public float Top;
        /// <summary>Where the climber's body stands at the foot (world, on the ground).</summary>
        public Vector3 Foot;
        /// <summary>A leaning ladder (as in CityLord): metres towards the wall for every metre up (0 = upright).</summary>
        public float Slope;

        /// <summary>Climbing speed straight up: a leaning ladder is climbed along its length at a steady pace.</summary>
        public float Rate => ClimbSpeed / Mathf.Sqrt(1f + Slope * Slope) * (Slope > 0.05f ? 1.25f : 1f);

        /// <summary>
        /// Horizontal velocity that keeps a climber at 'pos', moving up at 'up' m/s, on the line of the
        /// ladder (it follows the lean towards the wall), plus 'push' towards the wall.
        /// </summary>
        public Vector3 Steer(Vector3 pos, float up, float push)
        {
            var target = Foot + Into * Mathf.Max(0f, (pos.y - Foot.y) * Slope);
            var err = target - pos;
            err.y = 0;
            return err * 6f + Into * (up * Slope + push);
        }

        private static readonly List<Ladder> all = new List<Ladder>();

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        /// <summary>The ladder whose zone holds this point (the middle of a body), or null.</summary>
        public static Ladder At(Vector3 point)
        {
            foreach (var l in all)
                if (l != null && l.Zone.Contains(point)) return l;
            return null;
        }
    }
}
