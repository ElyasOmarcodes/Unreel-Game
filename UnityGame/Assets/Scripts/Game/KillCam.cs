using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>
    /// The first kill of every match, shown to everyone as a short film: the camera behind the killer, the
    /// bullet leaving the barrel and flying slowly to the victim (the camera after it), the hit and the
    /// victim falling, seen from the side. Meanwhile nobody moves or shoots (the host refuses shots and
    /// adds the time to the match clock), then the game goes on where it was.
    /// </summary>
    public class KillCam : MonoBehaviour
    {
        public const float Length = 3.8f;
        private const float Aim = 0.35f, Flight = 1.35f;

        /// <summary>The film is running (on this machine): no input, no shots.</summary>
        public static bool Active => I != null && I.playing;

        private static KillCam I;
        private static int shownMatch = -1;

        private bool playing, hit, wasFrozen;
        private float start;
        private Agent killer, victim;
        private Vector3 from, to, side;
        private WeaponDef def;
        private Transform bullet;
        private RectTransform cover;
        private Text caption;

        /// <summary>Called for every kill (Hud): plays the film for the first one of the match.</summary>
        public static void OnKill(KillEvent k)
        {
            var s = Session.I;
            if (s == null || World.I == null || s.Phase != Phase.Playing || shownMatch == s.MatchNumber) return;
            shownMatch = s.MatchNumber;
            if (k.Killer == k.Victim) return;   // a fall or one's own grenade is no first kill to show
            if (!World.I.Agents.TryGetValue(k.Killer, out var a) || a == null || !World.I.Agents.TryGetValue(k.Victim, out var b) || b == null) return;
            if (I == null) I = new GameObject("KillCam").AddComponent<KillCam>();
            I.Play(a, b, k);
            if (s.IsHost) s.Hold(Length);
        }

        private void Play(Agent a, Agent b, KillEvent k)
        {
            killer = a;
            victim = b;
            def = Weapons.Get(k.Weapon);
            bool gun = k.Weapon != Weapons.Grenade.Id;
            to = victim.Chest;
            from = gun && killer.Muzzle != null ? killer.Muzzle.position : killer.Chest;
            var dir = to - from;
            if (dir.sqrMagnitude < 0.01f) dir = killer.transform.forward;
            side = Vector3.Cross(Vector3.up, dir.normalized).normalized;
            // the victim stands still at the start of the fall until the bullet arrives
            if (victim.Anim != null) { victim.Anim.Play("Dead", 0, 0f); victim.Anim.speed = 0f; }
            hit = !gun;
            start = Time.unscaledTime - (gun ? 0f : Aim + Flight);
            if (gun) MakeBullet();
            ShowCover(k);
            wasFrozen = CameraRig.Frozen;
            CameraRig.Frozen = true;
            playing = true;
        }

        private void MakeBullet()
        {
            if (bullet == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(go.GetComponent<Collider>());
                go.name = "FilmBullet";
                go.transform.SetParent(transform);
                go.transform.localScale = new Vector3(0.022f, 0.035f, 0.022f);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.78f, 0.52f, 0.25f) };
                r.sharedMaterial.SetFloat("_Metallic", 0.9f);
                r.sharedMaterial.SetFloat("_Glossiness", 0.75f);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = Resources.Load<Material>("Materials/Tracer");
                trail.time = 0.35f;
                trail.startWidth = 0.02f;
                trail.endWidth = 0f;
                trail.startColor = new Color(1f, 0.9f, 0.7f, 0.6f);
                trail.endColor = new Color(1f, 0.9f, 0.7f, 0f);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bullet = go.transform;
            }
            bullet.position = from;
            bullet.rotation = Quaternion.LookRotation(to - from) * Quaternion.Euler(90f, 0f, 0f);   // the capsule lies along the flight
            bullet.GetComponent<TrailRenderer>().Clear();
            bullet.gameObject.SetActive(false);
        }

        private void ShowCover(KillEvent k)
        {
            if (cover == null)
            {
                var canvas = UIKit.CreateCanvas("KillCamCover", 50);
                canvas.transform.SetParent(transform);
                cover = (RectTransform)canvas.transform;
                // the black bars of a film above and below
                UIKit.Img(cover, Color.black).rectTransform.At(0.5f, 1f, 0, 0, 4000, 120, 0.5f, 1f);
                UIKit.Img(cover, Color.black).rectTransform.At(0.5f, 0f, 0, 0, 4000, 120, 0.5f, 0f);
                caption = UIKit.Txt(cover, "", 40, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
                caption.rectTransform.At(0.5f, 0f, 0, 60, 1800, 100);
            }
            var s = Session.I;
            string a = s.Players.TryGetValue(k.Killer, out var pa) ? Rtl.Shape(pa.Name) : "?";
            string b = s.Players.TryGetValue(k.Victim, out var pb) ? Rtl.Shape(pb.Name) : "?";
            caption.text = $"<color=#ffb040>FIRST BLOOD</color>   {a}  <color=#9aa4b0>[{def.Name}]</color>{(k.Headshot ? "  <color=#ff5040>HEADSHOT</color>" : "")}   {b}";
            cover.gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            if (!playing) return;
            float t = Time.unscaledTime - start;
            var cam = CameraRig.I;
            if (t >= Length || killer == null || victim == null || cam == null) { Stop(); return; }
            var dir = (to - from).normalized;
            Vector3 eye, look;
            if (t < Aim + Flight)
            {
                // over the killer's shoulder, then after the bullet
                float f = Mathf.Clamp01((t - Aim) / Flight);
                var at = Vector3.Lerp(from, to, f);
                if (t >= Aim && !bullet.gameObject.activeSelf)
                {
                    bullet.gameObject.SetActive(true);
                    Effects.MuzzleFlash(killer.Muzzle, def);
                    Sfx.PlayAt(def.Sound, from, 0.9f, def.SoundPitch * 0.8f, false);
                }
                if (bullet.gameObject.activeSelf) bullet.position = at;
                float behind = Mathf.Lerp(1.6f, 0.9f, Mathf.SmoothStep(0f, 1f, f * 4f));
                eye = at - dir * behind + Vector3.up * 0.22f + side * 0.35f;
                look = at + dir * 4f;
            }
            else
            {
                if (!hit)
                {
                    hit = true;
                    if (bullet != null) bullet.gameObject.SetActive(false);
                    Effects.Blood(to, -dir);
                    if (victim.Anim != null) victim.Anim.speed = 1f;
                }
                // from the side, a slow turn around the falling body
                float f = (t - Aim - Flight) / (Length - Aim - Flight);
                var center = victim.transform.position + Vector3.up * 0.7f;
                var around = Quaternion.AngleAxis(Mathf.Lerp(-20f, 25f, f), Vector3.up) * side;
                eye = center + around * 3.4f + Vector3.up * 0.9f;
                // not through a wall
                if (Physics.Linecast(center, eye, out var wall, World.EnvMask, QueryTriggerInteraction.Ignore)) eye = wall.point + (center - eye).normalized * 0.3f;
                look = center;
            }
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            cam.Cam.fieldOfView = 50f;
        }

        private void Stop()
        {
            playing = false;
            CameraRig.Frozen = wasFrozen;
            if (bullet != null) bullet.gameObject.SetActive(false);
            if (cover != null) cover.gameObject.SetActive(false);
            if (victim != null && victim.Anim != null) victim.Anim.speed = 1f;
        }
    }
}
