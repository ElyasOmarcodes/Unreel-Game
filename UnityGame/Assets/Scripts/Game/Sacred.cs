using UnityEngine;

namespace UA
{
    /// <summary>
    /// A board that is not to be shot at (the name of Madinat al-Zahra over its gate, the story of the
    /// city): a trigger round it; a bullet that reaches it stops there and its damage goes to the shooter.
    /// </summary>
    public class Sacred : MonoBehaviour
    {
        /// <summary>Puts the trigger round a board (local centre and size in 'parent').</summary>
        public static void Guard(Transform parent, Vector3 center, Vector3 size)
        {
            var go = new GameObject("Sacred");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            go.AddComponent<Sacred>();
        }
    }
}
