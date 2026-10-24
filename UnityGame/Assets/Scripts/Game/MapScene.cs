using UnityEngine;

namespace UA
{
    /// <summary>
    /// Sits on the root of a map scene (each map is its own scene, loaded only while it is played, so a
    /// phone holds one map in memory at a time). Tells the world when the map has arrived.
    /// </summary>
    public class MapScene : MonoBehaviour
    {
        public int Layout;
        /// <summary>Lit windows, lamp glass and halos: shown at night only.</summary>
        public Renderer[] NightLights = new Renderer[0];
        /// <summary>Real lights of the lamps (per vertex, on the players only): on at night only.</summary>
        public Light[] Lamps = new Light[0];

        private void Start()
        {
            if (World.I != null) World.I.OnMapLoaded(this);
        }
    }
}
