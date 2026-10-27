using UnityEngine;

namespace UA
{
    /// <summary>
    /// Over-the-shoulder third person camera after Battle Warfare's (CameraController): over the right
    /// shoulder, 2 m behind it (0.8 m while shooting), on the shoulder to aim, lowered 0.4 m crouched,
    /// stopped 0.2 m short of a wall behind; slowly orbits the city while in the menus.
    /// </summary>
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

        private void Awake()
        {
            I = this;
            if (Cam == null) Cam = GetComponent<Camera>();
            Settings.ApplyCamera(Cam);
        }

        public Ray AimRay() => new Ray(transform.position + transform.forward * 0.3f, transform.forward);

        private float shake;
        private Quaternion smoothRotation = Quaternion.identity;
        private bool rotationSet;
        private float drop;   // how far the shoulder point is lowered (crouched, lying, dead)

        // the camera of Battle Warfare (CameraController): it looks at a point over the right shoulder
        // from behind, turned 10 degrees down and 6 to the right of the body's aim
        private static readonly Vector3 ShoulderPoint = new Vector3(0.488f, 1.367f, 0.062f);
        private const float LookDown = 10f, LookRight = 6f;
        /// <summary>Where the crosshair really points (the camera's pitch): the agent's arms and the network.</summary>
        public float AimPitch => Pitch - LookDown;
        // distance behind the shoulder point: normally, while shooting, while aiming (Battle Warfare
        // puts it on the shoulder with arms of their own; here the agent's own body holds the gun, so it
        // stays a little behind it), with a scope
        private const float Behind = 2f, BehindFiring = 0.8f, BehindAiming = 0.55f;

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
                rotationSet = false;
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

            // the rotation follows the aim at 30 per second (steadies the small shakes of the finger); the
            // shot leaves along the camera's own forward axis, so it always goes through the crosshair
            var wanted = Quaternion.Euler(-Pitch + LookDown, Yaw + LookRight, 0);
            smoothRotation = rotationSet && Quaternion.Angle(smoothRotation, wanted) < 45f ? Quaternion.Slerp(smoothRotation, wanted, Mathf.Clamp01(30f * dt)) : wanted;
            rotationSet = true;
            var rotation = smoothRotation;
            bool dead = Target.State != null && !Target.State.Alive;
            // crouched the camera comes down 0.4 m, lying down and dead lower still
            drop = Mathf.Lerp(drop, dead ? 0.8f : Prone ? 0.95f : Crouch ? 0.4f : 0f, Mathf.Clamp01(10f * dt));
            var body = Quaternion.Euler(0, Target.transform.eulerAngles.y, 0);
            var target = Target.transform.position + body * ShoulderPoint - Vector3.up * drop;
            float behind = Scope ? 0f : Aim ? BehindAiming : Target.Firing && !dead ? Mathf.Min(Behind, BehindFiring) : Behind;
            var desired = target - rotation * Vector3.forward * behind;
            // a wall behind: from the head towards that point, the camera stops 0.2 m short of the wall
            var head = Target.transform.position + Vector3.up * (1.6f - drop);
            var toCam = desired - head;
            if (!Scope && Physics.SphereCast(head, 0.1f, toCam.normalized, out var wall, toCam.magnitude, World.EnvMask, QueryTriggerInteraction.Ignore))
                desired = head + toCam.normalized * Mathf.Max(0f, wall.distance - 0.2f);
            desired = Vector3.Lerp(transform.position, desired, Mathf.Clamp01(50f * dt));
            if (shake > 0.01f)
            {
                rotation *= Quaternion.Euler(Random.Range(-shake, shake), Random.Range(-shake, shake), Random.Range(-shake, shake) * 0.5f);
                shake = Mathf.MoveTowards(shake, 0f, dt * 4f);
            }
            transform.SetPositionAndRotation(desired, rotation);
            // a wall pushed the camera up to the head: hide the body instead of looking out through it
            Close = !dead && !Scope && (desired - head).magnitude < 0.35f;

            // a field of view of 60, aiming too; a scope zooms in at 500 degrees a second
            fov = Mathf.MoveTowards(fov, Scope ? AimFov : 60f, 500f * dt);
            Cam.fieldOfView = fov;
        }
    }
}
