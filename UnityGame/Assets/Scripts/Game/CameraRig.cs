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
        private Quaternion smoothRotation = Quaternion.identity;
        private bool rotationSet;
        // smoothed parts of the follow camera: body height (crouch / prone), shoulder offset (aim) and the
        // distance the walls allow (moves in at once, back out gently): no jumps
        private float height = 1.62f, distance = -1f;
        // 0 = the usual place behind the back, 1 = close over the right shoulder: a wall behind the
        // player brings the camera round to the shoulder bit by bit (as in Battle Warfare) instead of
        // pushing it into the head
        private float closeIn;
        private Vector3 smoothOffset = new Vector3(0.12f, 0.5f, -3.1f);

        /// <summary>
        /// How far back (or forward) the camera can go from the shoulder point of offset 'local': first
        /// sideways from the pivot to the shoulder, then along the view axis, a sphere of 'radius'.
        /// </summary>
        private static float Reach(Vector3 pivot, Quaternion rotation, Vector3 local, float radius)
        {
            var sideways = rotation * new Vector3(local.x, local.y, 0f);
            float sideLen = sideways.magnitude;
            var from = pivot;
            if (sideLen > 0.01f)
            {
                if (Physics.SphereCast(pivot, radius, sideways / sideLen, out var sh, sideLen, World.EnvMask, QueryTriggerInteraction.Ignore)) sideLen = Mathf.Max(0f, sh.distance - 0.05f);
                from = pivot + sideways.normalized * sideLen;
            }
            float back = Mathf.Abs(local.z);
            if (back < 0.01f) return back;
            var dir = rotation * new Vector3(0f, 0f, Mathf.Sign(local.z));
            return Physics.SphereCast(from, radius, dir, out var hit, back, World.EnvMask, QueryTriggerInteraction.Ignore) ? Mathf.Max(0f, hit.distance - 0.05f) : back;
        }

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

            // as in Battle of Agents (CameraController.Update): the camera turns towards the aim at 30 per
            // second, which steadies the small shakes of the finger; the shot leaves along the camera's own
            // forward axis, so it always goes exactly through the crosshair
            var wanted = Quaternion.Euler(-Pitch, Yaw, 0);
            smoothRotation = rotationSet && Quaternion.Angle(smoothRotation, wanted) < 45f ? Quaternion.Slerp(smoothRotation, wanted, 1f - Mathf.Exp(-30f * dt)) : wanted;
            rotationSet = true;
            var rotation = smoothRotation;
            bool dead = Target.State != null && !Target.State.Alive;
            float wantHeight = dead ? 0.8f : Prone ? 0.75f : Crouch ? 1.25f : 1.62f;
            height = Mathf.Lerp(height, wantHeight, 1f - Mathf.Exp(-10f * dt));
            var pivot = Target.transform.position + Vector3.up * height;
            Vector3 offset;
            if (Scope) offset = new Vector3(0f, 0f, 0.25f);
            // aiming puts the camera close over the right shoulder (Battle of Agents: aimdis 0, the camera on
            // the shoulder point): what the crosshair sees is what the gun can hit, no wall edge in between
            else if (Aim) offset = new Vector3(0.45f * shoulder, 0.02f, -0.62f);
            // as in Battle of Agents: while shooting the camera comes right up beside the head, over
            // the right shoulder; otherwise it sits behind the back, centred and a little higher, with
            // the crosshair just above the head
            else if (Target.Firing && !dead) offset = new Vector3(0.48f * shoulder, 0.02f, -0.85f);
            else if (dead) offset = new Vector3(0f, 1.5f, -4.5f);
            else offset = new Vector3(0.12f * shoulder, 0.5f, -3.1f);
            smoothOffset = Scope ? offset : Vector3.Lerp(smoothOffset, offset, 1f - Mathf.Exp(-16f * dt));
            // the camera goes out sideways to the shoulder first, then back: a wall behind the player
            // stops only the second leg, so the camera stays beside the shoulder and the body in sight
            var near = new Vector3(0.75f * shoulder, 0.25f, -0.55f);
            if (!Scope)
            {
                float wantIn = 1f;
                for (int i = 0; i <= 8; i++)
                {
                    float t = i / 8f;
                    var l = Vector3.Lerp(smoothOffset, near, t);
                    if (Reach(pivot, rotation, l, 0.15f) >= Mathf.Abs(l.z) - 0.02f) { wantIn = t; break; }
                }
                closeIn = wantIn > closeIn ? Mathf.Lerp(closeIn, wantIn, 1f - Mathf.Exp(-25f * dt)) : Mathf.Lerp(closeIn, wantIn, 1f - Mathf.Exp(-5f * dt));
            }
            else closeIn = 0f;
            var local = Scope ? smoothOffset : Vector3.Lerp(smoothOffset, near, closeIn);
            // the sideways leg (to the shoulder), then the leg back as far as the walls allow
            var sideways = rotation * new Vector3(local.x, local.y, 0f);
            float sideLen = sideways.magnitude;
            var shoulderPoint = pivot;
            if (sideLen > 0.01f)
            {
                float free = Physics.SphereCast(pivot, 0.15f, sideways / sideLen, out var sh, sideLen, World.EnvMask, QueryTriggerInteraction.Ignore) ? Mathf.Max(0f, sh.distance - 0.05f) : sideLen;
                shoulderPoint = pivot + sideways / sideLen * free;
            }
            float allowed = Reach(pivot, rotation, local, 0.15f);
            distance = distance < 0f || allowed < distance || Scope ? allowed : Mathf.Lerp(distance, allowed, 1f - Mathf.Exp(-7f * dt));
            var desired = shoulderPoint + rotation * new Vector3(0f, 0f, Mathf.Sign(local.z) * Mathf.Min(distance, Mathf.Abs(local.z)));
            if (shake > 0.01f)
            {
                rotation *= Quaternion.Euler(Random.Range(-shake, shake), Random.Range(-shake, shake), Random.Range(-shake, shake) * 0.5f);
                shake = Mathf.MoveTowards(shake, 0f, dt * 4f);
            }
            transform.SetPositionAndRotation(desired, rotation);
            // a wall or a low ceiling pushed the camera up to the head: hide the body instead of
            // looking out through the inside of the head scarf (as in the big shooters)
            Close = !dead && !Scope && (desired - pivot).magnitude < 0.35f;

            // aiming without a scope only brings the camera to the shoulder (a touch of zoom); scopes zoom
            float want = Scope ? AimFov : Aim ? 56f : 62f;
            fov = Mathf.Lerp(fov, want, 1f - Mathf.Exp(-14f * dt));
            Cam.fieldOfView = fov;
        }
    }
}
