using UnityEngine;

namespace UA
{
    /// <summary>Trigger collider on an agent (body or head) that shots can hit.</summary>
    public class Hitbox : MonoBehaviour
    {
        public Agent Owner;
        public bool Head;
    }
}
