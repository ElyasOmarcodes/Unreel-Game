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
        /// <summary>The camera is so close to the head that the player's own body must be hidden.</summary>
        public bool Close { get; private set; }

        private float fov = 62f;
        private float orbit;
        private float kick;
        private float shoulder = 1f;

        private void Awake()
        {
            I = this;
            if (Cam == null) Cam = GetComponent<Camera>();
            Settings.ApplyCamera(Cam);
        }

        public Ray AimRay() => new Ray(transform.position + transform.forward * 0.3f, transform.forward);

        private float shake;
        // smoothed parts of the follow camera: body height (crouch / prone), shoulder offset (aim) and the
        // distance the walls allow (moves in at once, back out gently): no jumps
        private float height = 1.62f, distance = -1f;
        private Vector3 smoothOffset = new Vector3(0.12f, 0.5f, -3.1f);

        /// <summary>Short random camera shake (explosions), in degrees.</summary>
        public void Shake(float degrees) => shake = Mathf.Max(shake, degrees);

        /// <summary>
        /// Recoil: the barrel climbs by these degrees and stays there; the player pulls it back down
        /// (as in Battle of Agents: no drift sideways, no recovery by itself).
        /// </summary>
        public void Kick(float degrees)
        {
            Pitch = Mathf.Clamp(Pitch + degrees, -70f, 75f);
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
            // as in Battle of Agents: while shooting the camera comes right up beside the head, over
            // the right shoulder; otherwise it sits behind the back, centred and a little higher, with
            // the crosshair just above the head
            else if (Target.Firing && !dead) offset = new Vector3(0.48f * shoulder, 0.02f, -0.85f);
            else if (dead) offset = new Vector3(0f, 1.5f, -4.5f);
            else offset = new Vector3(0.12f * shoulder, 0.5f, -3.1f);
            smoothOffset = Scope ? offset : Vector3.Lerp(smoothOffset, offset, 1f - Mathf.Exp(-16f * dt));
            var desired = pivot + rotation * smoothOffset;
            var dir = desired - pivot;
            float length = dir.magnitude;
            float allowed = length;
            if (length > 0.01f && Physics.SphereCast(pivot, 0.24f, dir / length, out var hit, length, World.EnvMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0.25f, hit.distance - 0.05f);
            distance = distance < 0f || allowed < distance || Scope ? allowed : Mathf.Lerp(distance, allowed, 1f - Mathf.Exp(-7f * dt));
            if (length > 0.01f) desired = pivot + dir / length * Mathf.Min(distance, length);
            if (shake > 0.01f)
            {
                rotation *= Quaternion.Euler(Random.Range(-shake, shake), Random.Range(-shake, shake), Random.Range(-shake, shake) * 0.5f);
                shake = Mathf.MoveTowards(shake, 0f, dt * 4f);
            }
            transform.SetPositionAndRotation(desired, rotation);
            // a wall or a low ceiling pushed the camera up to the head: hide the body instead of
            // looking out through the inside of the head scarf (as in the big shooters)
            Close = !dead && (desired - pivot).magnitude < 0.75f;

            // aiming without a scope only brings the camera to the shoulder (a touch of zoom); scopes zoom
            float want = Scope ? AimFov : Aim ? 56f : 62f;
            fov = Mathf.Lerp(fov, want, 1f - Mathf.Exp(-14f * dt));
            Cam.fieldOfView = fov;
        }
    }
}
