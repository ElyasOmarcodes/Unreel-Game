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
