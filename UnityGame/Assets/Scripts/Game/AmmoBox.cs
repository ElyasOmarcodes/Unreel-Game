using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// Supply crate placed on the maps: standing next to one shows the AMMO button, which fills both
    /// magazines, the reserve ammunition and the grenades again.
    /// </summary>
    public class AmmoBox : MonoBehaviour
    {
        public const float Reach = 2.3f;
        private static readonly List<AmmoBox> all = new List<AmmoBox>();

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        public static AmmoBox Nearest(Vector3 position)
        {
            AmmoBox best = null;
            float bestDistance = Reach * Reach;
            foreach (var box in all)
            {
                var d = box.transform.position - position;
                d.y *= 0.5f;
                if (d.sqrMagnitude < bestDistance) { bestDistance = d.sqrMagnitude; best = box; }
            }
            return best;
        }
    }
}
