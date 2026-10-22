using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// Frag grenade: a bouncing rigid body with a 3 second fuse. Only the host's copy (Authoritative)
    /// explodes and deals damage; everyone else shows a copy thrown with the same velocity and removes
    /// it when the host reports the explosion.
    /// </summary>
    public class Grenade : MonoBehaviour
    {
        public const float Fuse = 3f;
        public const float LethalRadius = 3f;      // 100+ damage: always a kill
        public const float Radius = 8.5f;          // no damage beyond this
        public const float ThroughWalls = 4.5f;    // closer than this, walls do not protect
        public const int PerLife = 2, Max = 5;   // a resupply fills up to five

        public static readonly List<Grenade> Live = new List<Grenade>();
        private static Material material;
        private static PhysicsMaterial bounce;

        public byte Thrower;
        public int ThrowSeq;
        public bool Authoritative;
        private float explodeAt, lastBounce;

        public static Grenade Spawn(byte thrower, int seq, Vector3 origin, Vector3 velocity, bool authoritative)
        {
            if (Find(thrower, seq) is Grenade existing) return existing;
            // the thrower's arm swings over the shoulder (everyone sees it, bots included)
            if (World.I != null && World.I.Agents.TryGetValue(thrower, out var agent) && agent != null) agent.PlayThrow();
            if (material == null) material = new Material(Shader.Find("Standard")) { color = new Color(0.22f, 0.26f, 0.16f) };
            if (bounce == null) bounce = new PhysicsMaterial("Grenade") { bounciness = 0.35f, dynamicFriction = 0.6f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Average };
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Grenade";
            go.layer = 2; // ignore raycast: shots pass through it
            go.transform.position = origin;
            go.transform.localScale = new Vector3(0.09f, 0.12f, 0.09f);
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.GetComponent<Collider>().sharedMaterial = bounce;
            var lever = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(lever.GetComponent<Collider>());
            lever.transform.SetParent(go.transform, false);
            lever.transform.localPosition = new Vector3(0.35f, 0.3f, 0);
            lever.transform.localScale = new Vector3(0.15f, 0.6f, 0.3f);
            lever.GetComponent<Renderer>().sharedMaterial = material;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.onUnitSphere * 12f;
            // the fuse ticking away until the blast, heard around the grenade
            var fuse = Resources.Load<AudioClip>("Audio/grenade_fuse");
            if (fuse != null)
            {
                var tick = go.AddComponent<AudioSource>();
                tick.clip = fuse;
                tick.spatialBlend = 1f;
                tick.rolloffMode = AudioRolloffMode.Logarithmic;
                tick.minDistance = 2f;
                tick.maxDistance = 40f;
                tick.dopplerLevel = 0f;
                tick.volume = 0.8f;
                tick.Play();
            }
            var g = go.AddComponent<Grenade>();
            g.Thrower = thrower;
            g.ThrowSeq = seq;
            g.Authoritative = authoritative;
            // copies wait for the host; if its message is lost they disappear a little later
            g.explodeAt = Time.time + Fuse + (authoritative ? 0f : 1.5f);
            Live.Add(g);
            return g;
        }

        public static Grenade Find(byte thrower, int seq)
        {
            foreach (var g in Live)
                if (g != null && g.Thrower == thrower && g.ThrowSeq == seq) return g;
            return null;
        }

        public static void ClearAll()
        {
            foreach (var g in Live.ToArray())
                if (g != null) Destroy(g.gameObject);
            Live.Clear();
        }

        /// <summary>Damage at a distance from the blast (before cover is checked).</summary>
        public static float DamageAt(float distance)
        {
            if (distance <= LethalRadius) return 130f;
            if (distance >= Radius) return 0f;
            return Mathf.Lerp(95f, 8f, (distance - LethalRadius) / (Radius - LethalRadius));
        }

        private void Update()
        {
            if (Time.time < explodeAt) return;
            if (Authoritative && Session.I != null) Session.I.HostExplode(this);
            else Destroy(gameObject);
        }

        private void OnCollisionEnter(Collision c)
        {
            float speed = c.relativeVelocity.magnitude;
            if (speed < 1.5f || Time.time - lastBounce < 0.12f) return;
            lastBounce = Time.time;
            Sfx.PlayAt("grenade_bounce", transform.position, Mathf.Clamp01(speed / 9f), Random.Range(0.9f, 1.1f));
        }

        private void OnDestroy() => Live.Remove(this);
    }
}
