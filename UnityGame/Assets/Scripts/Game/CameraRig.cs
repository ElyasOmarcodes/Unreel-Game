using UnityEngine;

namespace UA
{
    /// <summary>Over-the-shoulder third person camera; slowly orbits the city while in the menus.</summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I;
        public static bool Frozen;   // the auto test positions the camera itself

        public Camera Cam;
        public Agent Target;
        public float Yaw, Pitch;
        public bool Aim, Scope, Crouch, Prone;
        public float AimFov = 45f;

        private float fov = 62f;
        private float orbit;
        private float kick;
        private float shoulder = 1f;

        private void Awake()
        {
            I = this;
            if (Cam == null) Cam = GetComponent<Camera>();
        }

        public Ray AimRay() => new Ray(transform.position + transform.forward * 0.3f, transform.forward);

        private float shake;
        // smoothed parts of the follow camera: body height (crouch / prone), shoulder offset (aim) and the
        // distance the walls allow (moves in at once, back out gently): no jumps
        private float height = 1.62f, distance = -1f;
        private Vector3 smoothOffset = new Vector3(0.62f, 0.18f, -2.7f);

        /// <summary>Short random camera shake (explosions), in degrees.</summary>
        public void Shake(float degrees) => shake = Mathf.Max(shake, degrees);

        public void Kick(float degrees)
        {
            Pitch += degrees;
            kick += degrees * 0.6f;   // part of the recoil recovers by itself
            Yaw += Random.Range(-0.35f, 0.35f) * degrees;
        }

        private void LateUpdate()
        {
            if (Frozen) return;
            float dt = Time.unscaledDeltaTime;
            var world = World.I;
            if (Target == null)
            {
                // menu: slow orbit around the middle of the city
                orbit += dt * 3.5f;
                var center = world != null ? world.Center : Vector3.zero;
                float radius = world != null ? world.Radius * 0.55f : 40f;
                var pos = center + Quaternion.Euler(0, orbit, 0) * new Vector3(0, 0, -radius) + Vector3.up * 22f;
                transform.position = pos;
                transform.rotation = Quaternion.LookRotation(center + Vector3.up * 4f - pos);
                fov = 55f;
                Cam.fieldOfView = fov;
                return;
            }

            float recover = Mathf.Min(kick, kick * 6f * dt + 0.5f * dt);
            Pitch -= recover;
            kick -= recover;

            var rotation = Quaternion.Euler(-Pitch, Yaw, 0);
            bool dead = Target.State != null && !Target.State.Alive;
            float wantHeight = dead ? 0.8f : Prone ? 0.75f : Crouch ? 1.25f : 1.62f;
            height = Mathf.Lerp(height, wantHeight, 1f - Mathf.Exp(-10f * dt));
            var pivot = Target.transform.position + Vector3.up * height;
            Vector3 offset;
            if (Scope) offset = new Vector3(0f, 0f, 0.25f);
            else if (Aim) offset = new Vector3(0.55f * shoulder, 0.05f, -1.35f);
            else if (dead) offset = new Vector3(0f, 1.5f, -4.5f);
            else offset = new Vector3(0.62f * shoulder, 0.18f, -2.7f);
            smoothOffset = Scope ? offset : Vector3.Lerp(smoothOffset, offset, 1f - Mathf.Exp(-16f * dt));
            var desired = pivot + rotation * smoothOffset;
            var dir = desired - pivot;
            float length = dir.magnitude;
            float allowed = length;
            if (length > 0.01f && Physics.SphereCast(pivot, 0.18f, dir / length, out var hit, length, World.EnvMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0.25f, hit.distance - 0.05f);
            distance = distance < 0f || allowed < distance || Scope ? allowed : Mathf.Lerp(distance, allowed, 1f - Mathf.Exp(-7f * dt));
            if (length > 0.01f) desired = pivot + dir / length * Mathf.Min(distance, length);
            if (shake > 0.01f)
            {
                rotation *= Quaternion.Euler(Random.Range(-shake, shake), Random.Range(-shake, shake), Random.Range(-shake, shake) * 0.5f);
                shake = Mathf.MoveTowards(shake, 0f, dt * 4f);
            }
            transform.SetPositionAndRotation(desired, rotation);

            float want = Scope ? AimFov : Aim ? Mathf.Max(AimFov, 40f) : 62f;
            fov = Mathf.Lerp(fov, want, 1f - Mathf.Exp(-14f * dt));
            Cam.fieldOfView = fov;
        }
    }
}
