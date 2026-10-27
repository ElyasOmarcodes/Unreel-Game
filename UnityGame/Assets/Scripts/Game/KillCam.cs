using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>
    /// The first kill of every match, shown to everyone as a short film in order: out of black the film
    /// bars close in over a wide shot of both players under a FIRST BLOOD title, then over the killer's
    /// shoulder at the victim, the bullet leaving the barrel and flying slowly to the victim (the camera
    /// after it), the hit and the victim falling with the camera turning round them, and a dip to black.
    /// Meanwhile nobody moves or shoots (the host refuses shots and adds the time to the match clock),
    /// then the game goes on where it was.
    /// </summary>
    public class KillCam : MonoBehaviour
    {
        public const float Length = Intro + Aim + Flight + Fall;
        private const float Intro = 1f, Aim = 0.6f, Flight = 1.4f, Fall = 1.8f;
        private const float Bars = 120f;

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
        private RectTransform cover, barTop, barBottom;
        private Text caption, title;
        private Image fade;

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
            start = Time.unscaledTime;
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
                // out of black (and back into it at the end), the bars of a film above and below, the title
                fade = UIKit.Img(cover, Color.black);
                fade.rectTransform.Fill();
                barTop = UIKit.Img(cover, Color.black).rectTransform;
                barBottom = UIKit.Img(cover, Color.black).rectTransform;
                title = UIKit.Txt(cover, "FIRST BLOOD", 110, TextAnchor.MiddleCenter, new Color(1f, 0.69f, 0.25f)).Shadowed();
                title.fontStyle = FontStyle.Bold;
                title.rectTransform.At(0.5f, 0.5f, 0, 60, 1800, 160);
                caption = UIKit.Txt(cover, "", 40, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
                caption.rectTransform.At(0.5f, 0f, 0, 60, 1800, 100);
            }
            fade.color = Color.black;
            Cinema(0f);
            var s = Session.I;
            string a = s.Players.TryGetValue(k.Killer, out var pa) ? Rtl.Shape(pa.Name) : "?";
            string b = s.Players.TryGetValue(k.Victim, out var pb) ? Rtl.Shape(pb.Name) : "?";
            caption.text = $"{a}  <color=#9aa4b0>[{def.Name}]</color>{(k.Headshot ? "  <color=#ff5040>HEADSHOT</color>" : "")}   {b}";
            cover.gameObject.SetActive(true);
        }

        /// <summary>The overlay at time t: the fade from and to black, the bars, the title, the names.</summary>
        private void Cinema(float t)
        {
            float bars = Bars * Mathf.SmoothStep(0f, 1f, t / 0.5f);
            barTop.At(0.5f, 1f, 0, 0, 4000, bars, 0.5f, 1f);
            barBottom.At(0.5f, 0f, 0, 0, 4000, bars, 0.5f, 0f);
            float dark = Mathf.Max(1f - t / 0.45f, (t - (Length - 0.3f)) / 0.3f);
            fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(dark));
            // the title grows into place over the wide shot and goes as the story starts
            float showTitle = Mathf.Clamp01((t - 0.15f) / 0.3f) * (1f - Mathf.Clamp01((t - Intro + 0.15f) / 0.25f));
            title.color = new Color(title.color.r, title.color.g, title.color.b, showTitle);
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.5f));
            caption.color = new Color(caption.color.r, caption.color.g, caption.color.b, Mathf.Clamp01((t - Intro) / 0.3f));
        }

        /// <summary>A camera place looking at 'look' that is not behind a wall: 'eye', or as near it as is clear.</summary>
        private static Vector3 Clear(Vector3 look, Vector3 eye)
        {
            if (Physics.SphereCast(look, 0.15f, (eye - look).normalized, out var wall, (eye - look).magnitude, World.EnvMask, QueryTriggerInteraction.Ignore))
                return look + (eye - look).normalized * Mathf.Max(0.4f, wall.distance - 0.1f);
            return eye;
        }

        /// <summary>Of several camera places, the one that keeps most of its distance once walls are in the way.</summary>
        private static Vector3 Best(Vector3 look, params Vector3[] eyes)
        {
            Vector3 best = eyes[0];
            float score = -1f;
            foreach (var e in eyes)
            {
                var c = Clear(look, e);
                float kept = (c - look).magnitude / Mathf.Max(0.01f, (e - look).magnitude);
                if (kept > score + 0.05f) { score = kept; best = c; }
                if (kept > 0.98f) break;
            }
            return best;
        }

        private void LateUpdate()
        {
            if (!playing) return;
            float t = Time.unscaledTime - start;
            var cam = CameraRig.I;
            if (t >= Length || killer == null || victim == null || cam == null) { Stop(); return; }
            Cinema(t);
            // both bodies in the picture, whatever the camera of the local player hid
            killer.SetVisible(true);
            victim.SetVisible(true);
            var dir = (to - from).normalized;
            var flat = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = killer.transform.forward;
            var right = Vector3.Cross(Vector3.up, flat).normalized;
            Vector3 eye, look;
            float fov = 50f;
            if (t < Intro)
            {
                // the wide shot: from behind and above the killer, the victim ahead, the camera drifting slowly in
                float f = t / Intro;
                var head = killer.transform.position + Vector3.up * 1.7f;
                float pull = Mathf.Lerp(3.4f, 2.8f, f);
                look = Vector3.Lerp(killer.transform.position, victim.transform.position, 0.45f) + Vector3.up * 1f;
                eye = Best(head, head - flat * pull + right * 1.1f + Vector3.up * 2.2f, head - flat * pull - right * 1.1f + Vector3.up * 2.2f,
                    head - flat * 1.4f + Vector3.up * 3.5f);
                fov = 55f;
            }
            else if (t < Intro + Aim)
            {
                // over the killer's shoulder at the victim, pushing in a little
                float f = (t - Intro) / Aim;
                var head = killer.transform.position + Vector3.up * 1.7f;
                look = to;
                float pull = Mathf.Lerp(1.5f, 1.2f, f);
                // over the right shoulder, or the left one when a wall is on the right
                eye = Best(head, head - flat * pull + right * 0.55f + Vector3.up * 0.1f, head - flat * pull - right * 0.55f + Vector3.up * 0.1f,
                    head - flat * 1.2f + Vector3.up * 0.45f);
                fov = Mathf.Lerp(50f, 44f, f);
            }
            else if (t < Intro + Aim + Flight)
            {
                // after the bullet, slowly
                float f = Mathf.Clamp01((t - Intro - Aim) / Flight);
                var at = Vector3.Lerp(from, to, f);
                if (!hit && bullet != null && !bullet.gameObject.activeSelf)
                {
                    bullet.gameObject.SetActive(true);
                    Effects.MuzzleFlash(killer.Muzzle, def);
                    Sfx.PlayAt(def.Sound, from, 0.9f, def.SoundPitch * 0.8f, false);
                }
                if (bullet != null && bullet.gameObject.activeSelf) bullet.position = at;
                float behind = Mathf.Lerp(1.6f, 0.9f, Mathf.SmoothStep(0f, 1f, f * 4f));
                eye = Clear(at, at - dir * behind + Vector3.up * 0.22f + right * 0.35f);
                look = at + dir * 4f;
            }
            else
            {
                if (!hit)
                {
                    hit = true;
                    if (bullet != null) bullet.gameObject.SetActive(false);
                    Effects.Blood(to, -dir);
                }
                if (victim.Anim != null) victim.Anim.speed = 1f;
                // from the side, a slow turn around the falling body
                float f = (t - Intro - Aim - Flight) / Fall;
                var center = victim.transform.position + Vector3.up * 0.7f;
                var around = Quaternion.AngleAxis(Mathf.Lerp(-20f, 25f, f), Vector3.up) * right;
                look = center;
                float dist = Mathf.Lerp(3.6f, 3f, f);
                eye = Best(center, center + around * dist + Vector3.up * 0.9f, center - around * dist + Vector3.up * 0.9f, center + Vector3.up * dist);
            }
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            cam.Cam.fieldOfView = fov;
        }

        private void Stop()
        {
            playing = false;
            CameraRig.Frozen = wasFrozen;
            if (bullet != null) bullet.gameObject.SetActive(false);
            if (cover != null) cover.gameObject.SetActive(false);
            if (killer != null) killer.SetVisible(true);
            if (victim != null && victim.Anim != null) victim.Anim.speed = 1f;
        }
    }
}
