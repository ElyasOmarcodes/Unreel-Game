using UnityEngine;

namespace UA
{
    /// <summary>
    /// The skirt of a long garment (a thobe, a perahan, a chapan, a long coat) as real cloth: held to the
    /// body down to the waist, freer and freer below it to the hem, swinging with the steps and pushed
    /// aside by the legs. Simulated only near the camera (a few agents at a time on a phone); far away the
    /// garment simply follows the body as before.
    /// </summary>
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class ClothDrape : MonoBehaviour
    {
        /// <summary>Waist line and hem above the feet (m), set when the garment is made.</summary>
        public float Waist = 1f, Hem = 0.4f;
        /// <summary>How far the hem may swing away from the body (m).</summary>
        public float Swing = 0.16f;

        private const float Near = 22f;
        private const int MostAtOnce = 6;
        private static int running;

        private Cloth cloth;
        private Transform agent;
        private float nextCheck;
        private bool on;

        private void OnEnable()
        {
            agent = GetComponentInParent<Agent>() != null ? GetComponentInParent<Agent>().transform : transform.root;
            nextCheck = Random.value * 0.5f;
        }

        private void OnDisable() => Switch(false);

        private void Update()
        {
            if ((nextCheck -= Time.unscaledDeltaTime) > 0f) return;
            nextCheck = 0.5f;
            var cam = CameraRig.I != null ? CameraRig.I.Cam : Camera.main;
            bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < Near * Near;
            if (near && !on && running >= MostAtOnce) near = false;
            Switch(near);
        }

        private void Switch(bool value)
        {
            if (value == on) return;
            on = value;
            running += value ? 1 : -1;
            if (value && cloth == null) Build();
            if (cloth != null)
            {
                cloth.enabled = value;
                if (value) cloth.ClearTransformMotion();
            }
        }

        /// <summary>After a jump across the map (a respawn): no flying skirt.</summary>
        public void Settle()
        {
            if (cloth != null && cloth.enabled) cloth.ClearTransformMotion();
        }

        private void Build()
        {
            cloth = gameObject.AddComponent<Cloth>();
            cloth.useGravity = true;
            cloth.damping = 0.25f;
            cloth.stretchingStiffness = 0.95f;
            cloth.bendingStiffness = 0.6f;
            cloth.worldVelocityScale = 0.35f;
            cloth.worldAccelerationScale = 0.8f;
            cloth.friction = 0.3f;
            cloth.collisionMassScale = 0.1f;
            cloth.useTethers = true;
            // held at the waist, free by the hem (the cloth works in this object's own units)
            float unit = 1f / Mathf.Max(1e-5f, transform.lossyScale.y);
            var points = cloth.vertices;
            var coefficients = new ClothSkinningCoefficient[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float height = agent.InverseTransformPoint(transform.TransformPoint(points[i])).y;
                float free = Mathf.Clamp01((Waist - 0.05f - height) / Mathf.Max(0.1f, Waist - Hem));
                coefficients[i].maxDistance = Swing * free * free * unit;
                coefficients[i].collisionSphereDistance = 0f;
            }
            cloth.coefficients = coefficients;
            // the legs push the cloth aside: as thick as the widest trousers under it (loose tunban), else
            // the cloth falls onto the leg and the trousers show through it
            var legs = new System.Collections.Generic.List<CapsuleCollider>();
            foreach (string side in new[] { "l", "r" })
            {
                Leg(legs, "thigh_" + side, "calf_" + side, 0.135f);
                Leg(legs, "calf_" + side, "foot_" + side, 0.105f);
            }
            cloth.capsuleColliders = legs.ToArray();
        }

        private void Leg(System.Collections.Generic.List<CapsuleCollider> legs, string from, string to, float radius)
        {
            Transform a = Find(agent, from), b = Find(agent, to);
            if (a == null || b == null) return;
            var go = new GameObject("ClothLeg");
            go.layer = 2;   // ignore raycasts: shots and the camera go through it
            go.transform.SetParent(a, false);
            var d = b.position - a.position;
            go.transform.position = a.position + d * 0.5f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            var c = go.AddComponent<CapsuleCollider>();
            c.isTrigger = true;
            c.direction = 1;
            float scale = Mathf.Max(1e-5f, go.transform.lossyScale.y);
            c.radius = radius / scale;
            c.height = (d.magnitude + radius * 2f) / scale;
            legs.Add(c);
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name.ToLowerInvariant() == name) return t;
            foreach (Transform c in t)
            {
                var f = Find(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
