using UnityEngine;

namespace UA
{
    /// <summary>
    /// Over-the-shoulder third person camera after Battle Warfare's (CameraController): over the right
    /// shoulder, 2 m behind it (0.8 m while shooting), on the shoulder to aim, lowered 0.4 m crouched,
    /// stopped 0.2 m short of a wall behind and brought up over the shoulder beside the head as the wall
    /// comes closer (never into the arm or the head), at least 0.55 m behind it so the body stays in the
    /// picture; over the left shoulder when the right is walled; slowly orbits the city in the menus.
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
        private float shoulder = 1f;   // over the right shoulder (1) or the left (-1) when a wall is on the right
        private float sideWanted = 1f;
        private float followY, closeK, reach;
        private bool followSet, reachSet;

        /// <summary>
        /// A sphere 0.12 m wide sent along 'way': the distance to the first wall in it. Thin things (posts,
        /// lamps, poles, railings under 0.35 m across) do not count: the camera goes past them instead of
        /// jumping in front of them.
        /// </summary>
        private static bool Cast(Vector3 from, Vector3 way, out float distance)
        {
            distance = way.magnitude;
            if (distance < 1e-4f) return false;
            bool found = false;
            foreach (var h in Physics.SphereCastAll(from, 0.12f, way / distance, distance, World.EnvMask, QueryTriggerInteraction.Ignore))
            {
                var size = h.collider.bounds.size;
                if (Mathf.Max(size.x, size.z) < 0.35f) continue;
                if (h.distance < distance || !found) { distance = Mathf.Min(distance, h.distance); found = true; }
            }
            return found;
        }

        // the camera of Battle Warfare (CameraController): it looks at a point over the right shoulder
        // from behind, turned 10 degrees down and 6 to the right of the body's aim
        private static readonly Vector3 ShoulderPoint = new Vector3(0.488f, 1.367f, 0.062f);
        private const float LookDown = 10f, LookRight = 6f;
        // with the back against a wall: over the right shoulder beside the head (above the arm that holds the
        // gun, out of the head and the headdress), reached in proportion as the wall comes closer
        private static readonly Vector3 CloseShoulderPoint = new Vector3(0.3f, 1.75f, -0.05f);
        private const float CloseFrom = 1f;   // the wall nearer than this behind the shoulder point starts it
        // and the camera keeps at least this far behind that point, even through the wall at the back: a
        // camera level with the head cannot see the body at all (it only looks forward, so nothing
        // behind the wall is shown)
        private const float MinBehind = 0.55f;
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
                rotationSet = followSet = reachSet = false;
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

            // the rotation follows the aim at 20 per second (evens out the uneven steps of a finger on the
            // glass); the shot leaves along the camera's own forward axis, so it always goes through the crosshair
            var wanted = Quaternion.Euler(-Pitch + LookDown, Yaw + LookRight, 0);
            smoothRotation = rotationSet && Quaternion.Angle(smoothRotation, wanted) < 45f ? Quaternion.Slerp(smoothRotation, wanted, 1f - Mathf.Exp(-20f * dt)) : wanted;
            rotationSet = true;
            var rotation = smoothRotation;
            bool dead = Target.State != null && !Target.State.Alive;
            // crouched the camera comes down 0.4 m, lying down and dead lower still
            drop = Mathf.Lerp(drop, dead ? 0.8f : Prone ? 0.95f : Crouch ? 0.4f : 0f, Mathf.Clamp01(10f * dt));
            // the body's height followed softly: a step or a stair does not jolt the picture
            var at = Target.transform.position;
            if (!followSet || Mathf.Abs(at.y - followY) > 2f) followY = at.y;
            followSet = true;
            followY = Mathf.Lerp(followY, at.y, 1f - Mathf.Exp(-14f * dt));
            at.y = followY;
            var body = Quaternion.Euler(0, Yaw, 0);
            Vector3 Side(Vector3 p, float s) => new Vector3(p.x * s, p.y, p.z);
            var head = at + Vector3.up * (1.6f - drop);
            // over the left shoulder only when the right one is clearly walled and the left is free, back
            // when the right is free again (no flicking from side to side beside a wall)
            float Room(float s)
            {
                var d = at + body * Side(ShoulderPoint, s) - Vector3.up * drop - head;
                return Cast(head, d, out float hit) ? hit / d.magnitude : 1f;
            }
            float rightRoom = Room(1f);
            if (sideWanted > 0f && rightRoom < 0.5f && Room(-1f) > rightRoom + 0.3f) sideWanted = -1f;
            else if (sideWanted < 0f && rightRoom > 0.85f) sideWanted = 1f;
            shoulder = Mathf.MoveTowards(shoulder, sideWanted, 4f * dt);
            var target = at + body * Side(ShoulderPoint, shoulder) - Vector3.up * drop;
            float behind = Scope ? 0f : Aim ? BehindAiming : Target.Firing && !dead ? Mathf.Min(Behind, BehindFiring) : Behind;
            // the shoulder point kept out of a wall at the side (from the head out to it)
            var side = target - head;
            if (side.sqrMagnitude > 1e-4f && Cast(head, side, out float sh))
                target = head + side.normalized * Mathf.Max(0f, sh - 0.05f);
            // a wall behind (as in Battle Warfare): the camera comes closer along the line back from the shoulder
            // point as close as the wall is, and moves up over the shoulder beside the head in proportion,
            // never nearer than MinBehind (the body stays in the picture)
            var back = -(rotation * Vector3.forward);
            float room = behind, k = 0f;
            if (!Scope && behind > 0f && Cast(target, back * behind, out float wall))
            {
                room = Mathf.Max(0f, wall - 0.2f);
                k = Mathf.Clamp01(1f - room / Mathf.Min(CloseFrom, behind));
            }
            // pulled in quickly (not into the wall), let out slowly (no pumping past posts and corners)
            closeK = k > closeK ? Mathf.Lerp(closeK, k, 1f - Mathf.Exp(-18f * dt)) : Mathf.MoveTowards(closeK, k, 1.5f * dt);
            float kk = closeK * closeK * (3f - 2f * closeK);
            if (kk > 0.001f)
            {
                var over = at + body * Side(CloseShoulderPoint, shoulder) - Vector3.up * drop;
                var up = Vector3.Lerp(target, over, kk) - target;
                if (up.sqrMagnitude > 1e-4f && Cast(target, up, out float roof)) up = up.normalized * Mathf.Max(0f, roof - 0.05f);
                target += up;
                if (!Scope && behind > 0f && Cast(target, back * behind, out wall)) room = Mathf.Max(0f, wall - 0.2f);
            }
            float want = Scope ? 0f : Mathf.Max(room, Mathf.Min(MinBehind, behind) * kk);
            reach = want < reach || !reachSet ? Mathf.Lerp(reach, want, reachSet ? 1f - Mathf.Exp(-25f * dt) : 1f) : Mathf.MoveTowards(reach, want, 2.5f * dt);
            if (Scope) reach = 0f;
            reachSet = true;
            var desired = target + back * reach;
            if (shake > 0.01f)
            {
                rotation *= Quaternion.Euler(Random.Range(-shake, shake), Random.Range(-shake, shake), Random.Range(-shake, shake) * 0.5f);
                shake = Mathf.MoveTowards(shake, 0f, dt * 4f);
            }
            transform.SetPositionAndRotation(desired, rotation);
            // the body is hidden only when the camera has nowhere to go but into the head
            Close = !dead && !Scope && (desired - head).magnitude < 0.25f;

            // a field of view of 60, aiming too; a scope zooms in at 500 degrees a second
            fov = Mathf.MoveTowards(fov, Scope ? AimFov : 60f, 500f * dt);
            Cam.fieldOfView = fov;
        }
    }
}
