using UnityEngine;
using UnityEngine.UI;

namespace UA
{
    /// <summary>
    /// The first kill of every match, shown to everyone as a short film in order: out of black the camera
    /// comes down like a crane from high over the place onto both players while the film bars close in and
    /// FIRST BLOOD is written over it; then over the killer's shoulder at the victim; then the bullet: it
    /// leaves the barrel in a flash and a puff of smoke, and the camera flies with it, round it from the side
    /// over the top, the flight quick at first, slowed nearly to a stop on the way and quick again at the end
    /// (bullet time); a real-looking jacketed bullet, spinning, a thin hot line and a pale wake of air behind
    /// it and rings of pushed air along the way; the camera lets it go the last metres into the victim; a
    /// white flash at the hit and the victim falling in slow motion with the camera turning round them;
    /// then the killer from below with their name. The film goes to black and the game comes back out of
    /// it softly, the bars opening. Every shot moves smoothly (no jumps when a wall pushes the camera in),
    /// under a soft dark edge round the picture.
    /// Meanwhile nobody moves or shoots (the host refuses shots and adds the time to the match clock),
    /// then the game goes on where it was.
    /// </summary>
    public class KillCam : MonoBehaviour
    {
        public const float Length = Intro + Aim + Flight + Fall + Hero;
        private const float Intro = 1.9f, Aim = 0.8f, Flight = 2.4f, Fall = 2.0f, Hero = 1.6f;
        private const float Out = 0.6f;   // the game coming back out of black after the film
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
        private Image fade, flash, vignette, line;
        private float stoppedAt = -10f;   // when the film ended (the way back out of black)
        private Transform vapour;          // the pale wake behind the bullet
        private readonly System.Collections.Generic.List<(Transform t, Material m, float born)> rings = new System.Collections.Generic.List<(Transform, Material, float)>();
        private float nextRing;
        private static Mesh bulletMesh, ringMesh;
        private static Material ringMaterial;
        private string killerName;
        private int shot = -1;            // which shot of the film the camera is in (smoothing starts again at each)
        private Vector3 smoothEye, smoothLook;

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

        /// <summary>A rifle bullet turned on a lathe: flat base, straight copper body, pointed nose (along +Z).</summary>
        private static Mesh BulletMesh()
        {
            if (bulletMesh != null) return bulletMesh;
            // the profile (radius, length) from the base to the tip, twice life size so it reads on a phone
            var profile = new[] { new Vector2(0f, 0f), new Vector2(0.0075f, 0f), new Vector2(0.008f, 0.004f), new Vector2(0.008f, 0.03f),
                new Vector2(0.0074f, 0.038f), new Vector2(0.0058f, 0.046f), new Vector2(0.0036f, 0.053f), new Vector2(0.0012f, 0.058f), new Vector2(0f, 0.0595f) };
            const int around = 16;
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (int k = 0; k < profile.Length; k++)
                for (int a = 0; a <= around; a++)
                {
                    float ang = a * Mathf.PI * 2f / around;
                    var radial = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                    v.Add(radial * profile[k].x + Vector3.forward * profile[k].y);
                    // the side's normal from the slope of the profile there
                    var prev = profile[Mathf.Max(0, k - 1)];
                    var next = profile[Mathf.Min(profile.Length - 1, k + 1)];
                    var slope = next - prev;
                    var nn = (radial * slope.y - Vector3.forward * slope.x).normalized;
                    n.Add(k == 0 ? Vector3.back : nn);
                }
            for (int k = 0; k < profile.Length - 1; k++)
                for (int a = 0; a < around; a++)
                {
                    int i0 = k * (around + 1) + a, i1 = i0 + 1, j0 = i0 + around + 1, j1 = j0 + 1;
                    // both windings: seen right whichever way the lathe turned
                    tris.AddRange(new[] { i0, j0, i1, i1, j0, j1, i0, i1, j0, i1, j1, j0 });
                }
            bulletMesh = new Mesh { name = "FilmBullet" };
            bulletMesh.SetVertices(v);
            bulletMesh.SetNormals(n);
            bulletMesh.SetTriangles(tris, 0);
            bulletMesh.RecalculateBounds();
            return bulletMesh;
        }

        private void MakeBullet()
        {
            if (bullet == null)
            {
                var go = new GameObject("FilmBullet");
                go.transform.SetParent(transform);
                go.AddComponent<MeshFilter>().sharedMesh = BulletMesh();
                var r = go.AddComponent<MeshRenderer>();
                // a copper jacket: warm, metallic, polished
                r.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.8f, 0.48f, 0.26f) };
                r.sharedMaterial.SetFloat("_Metallic", 0.92f);
                r.sharedMaterial.SetFloat("_Glossiness", 0.82f);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // the hot line right behind it
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = Resources.Load<Material>("Materials/Tracer");
                trail.time = 0.25f;
                trail.startWidth = 0.012f;
                trail.endWidth = 0f;
                trail.startColor = new Color(1f, 0.85f, 0.6f, 0.75f);
                trail.endColor = new Color(1f, 0.6f, 0.3f, 0f);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.numCapVertices = 2;
                bullet = go.transform;
                // the pale wake of pushed air, wider and slower to fade
                var wake = new GameObject("FilmBulletWake");
                wake.transform.SetParent(transform);
                var air = wake.AddComponent<TrailRenderer>();
                air.sharedMaterial = Resources.Load<Material>("Materials/Dust");
                air.time = 1.1f;
                air.widthCurve = new AnimationCurve(new Keyframe(0f, 0.018f), new Keyframe(0.3f, 0.05f), new Keyframe(1f, 0.11f));
                air.startColor = new Color(0.92f, 0.94f, 0.97f, 0.42f);
                air.endColor = new Color(0.92f, 0.94f, 0.97f, 0f);
                air.minVertexDistance = 0.02f;
                air.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                vapour = wake.transform;
            }
            bullet.position = from;
            bullet.rotation = Quaternion.LookRotation(to - from);
            bullet.GetComponent<TrailRenderer>().Clear();
            bullet.gameObject.SetActive(false);
            vapour.position = from;
            vapour.GetComponent<TrailRenderer>().Clear();
            vapour.gameObject.SetActive(false);
            ClearRings();
        }

        /// <summary>A ring of pushed air round the bullet's line, widening and fading (a soft ring texture).</summary>
        private void Ring(Vector3 at, Vector3 dir)
        {
            if (ringMaterial == null)
            {
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float r = new Vector2(x - size / 2f + 0.5f, y - size / 2f + 0.5f).magnitude / (size / 2f);
                        float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.78f) / 0.16f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                tex.Apply();
                ringMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
                ringMesh = new Mesh { name = "FilmRing" };
                ringMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
                ringMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                ringMesh.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 };   // both faces
                ringMesh.RecalculateBounds();
            }
            var go = new GameObject("FilmRing");
            go.transform.SetParent(transform);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir));
            go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var mr = go.AddComponent<MeshRenderer>();
            var m = new Material(ringMaterial);
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rings.Add((go.transform, m, Time.unscaledTime));
        }

        private void UpdateRings()
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var (t, m, born) = rings[i];
                float age = (Time.unscaledTime - born) / 0.7f;
                if (t == null || age >= 1f) { if (t != null) Destroy(t.gameObject); if (m != null) Destroy(m); rings.RemoveAt(i); continue; }
                t.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.42f, Mathf.Sqrt(age));
                m.color = new Color(0.9f, 0.93f, 0.97f, 0.38f * (1f - age));
            }
        }

        private void ClearRings()
        {
            foreach (var (t, m, _) in rings) { if (t != null) Destroy(t.gameObject); if (m != null) Destroy(m); }
            rings.Clear();
        }

        private void ShowCover(KillEvent k)
        {
            if (cover == null)
            {
                var canvas = UIKit.CreateCanvas("KillCamCover", 50);
                canvas.transform.SetParent(transform);
                cover = (RectTransform)canvas.transform;
                // out of black (and back into it at the end), the bars of a film above and below, the title
                // a soft dark edge round the picture, as through a lens
                vignette = UIKit.Img(cover, new Color(0f, 0f, 0f, 0.55f), VignetteSprite());
                vignette.rectTransform.Fill();
                vignette.raycastTarget = false;
                fade = UIKit.Img(cover, Color.black);
                fade.rectTransform.Fill();
                barTop = UIKit.Img(cover, Color.black).rectTransform;
                barBottom = UIKit.Img(cover, Color.black).rectTransform;
                title = UIKit.Txt(cover, "FIRST BLOOD", 110, TextAnchor.MiddleCenter, new Color(1f, 0.69f, 0.25f)).Shadowed();
                title.fontStyle = FontStyle.Bold;
                title.rectTransform.At(0.5f, 0.5f, 0, 60, 1800, 160);
                // a thin gold line drawn out under the title
                line = UIKit.Img(cover, new Color(1f, 0.69f, 0.25f, 0.9f));
                caption = UIKit.Txt(cover, "", 40, TextAnchor.MiddleCenter, UIKit.TextColor).Shadowed();
                caption.rectTransform.At(0.5f, 0f, 0, 60, 1800, 100);
                flash = UIKit.Img(cover, new Color(1f, 1f, 1f, 0f));
                flash.rectTransform.Fill();
                flash.raycastTarget = false;
            }
            fade.color = Color.black;
            stoppedAt = -10f;
            Cinema(0f);
            var s = Session.I;
            string a = s.Players.TryGetValue(k.Killer, out var pa) ? Rtl.Shape(pa.Name) : "?";
            killerName = a;
            title.text = Loc.T("FIRST BLOOD");
            shot = -1;
            string b = s.Players.TryGetValue(k.Victim, out var pb) ? Rtl.Shape(pb.Name) : "?";
            caption.text = $"{a}  <color=#9aa4b0>[{def.Name}]</color>{(k.Headshot ? "  <color=#ff5040>HEADSHOT</color>" : "")}   {b}";
            cover.gameObject.SetActive(true);
        }

        private static Sprite vignetteSprite;

        /// <summary>Clear in the middle, dark towards the corners.</summary>
        private static Sprite VignetteSprite()
        {
            if (vignetteSprite != null) return vignetteSprite;
            const int w = 128, h = 72;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 0.9f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, (r - 0.55f) / 0.75f)));
                }
            tex.Apply();
            vignetteSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return vignetteSprite;
        }

        /// <summary>The overlay at time t: the fade from and to black, the bars, the title, the names.</summary>
        private void Cinema(float t)
        {
            float bars = Bars * Mathf.SmoothStep(0f, 1f, t / 0.7f);
            barTop.At(0.5f, 1f, 0, 0, 4000, bars, 0.5f, 1f);
            barBottom.At(0.5f, 0f, 0, 0, 4000, bars, 0.5f, 0f);
            // out of black slowly at the start, down into black over the last half second
            float dark = Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, t / 0.8f), Mathf.SmoothStep(0f, 1f, (t - (Length - 0.5f)) / 0.5f));
            fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(dark));
            vignette.color = new Color(0f, 0f, 0f, 0.55f * Mathf.Clamp01(t / 0.6f));
            // the title grows into place over the crane shot and goes as the story starts
            float showTitle = Mathf.Clamp01((t - 0.45f) / 0.4f) * (1f - Mathf.Clamp01((t - Intro + 0.2f) / 0.3f));
            float grow = Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 0.7f);
            // at the end the killer's name in its place, over their picture
            float heroAt = Length - Hero;
            if (t > heroAt)
            {
                title.text = killerName;
                showTitle = Mathf.Clamp01((t - heroAt - 0.15f) / 0.35f) * (1f - Mathf.Clamp01((t - (Length - 0.45f)) / 0.3f));
                grow = Mathf.SmoothStep(0f, 1f, (t - heroAt - 0.15f) / 0.6f);
            }
            // the white of the hit
            float sinceHit = t - (Intro + Aim + Flight);
            flash.color = new Color(1f, 1f, 1f, sinceHit >= 0f && sinceHit < 0.3f ? 0.6f * (1f - sinceHit / 0.3f) : 0f);
            title.color = new Color(title.color.r, title.color.g, title.color.b, showTitle);
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.18f, 1f, grow);
            // the gold line drawn out from the middle under the title
            line.rectTransform.At(0.5f, 0.5f, 0, -30, 760f * grow, 4);
            line.color = new Color(1f, 0.69f, 0.25f, 0.9f * showTitle);
            caption.color = new Color(caption.color.r, caption.color.g, caption.color.b, Mathf.Clamp01((t - Intro) / 0.3f) * (1f - Mathf.Clamp01((t - heroAt) / 0.3f)));
        }

        /// <summary>After the film: the game's own picture comes up out of black and the bars open.</summary>
        private void Afterwards()
        {
            float u = (Time.unscaledTime - stoppedAt) / Out;
            if (u >= 1f) { stoppedAt = -10f; if (cover != null) cover.gameObject.SetActive(false); return; }
            float k = 1f - Mathf.SmoothStep(0f, 1f, u);
            fade.color = new Color(0f, 0f, 0f, k);
            vignette.color = new Color(0f, 0f, 0f, 0.55f * k);
            barTop.At(0.5f, 1f, 0, 0, 4000, Bars * k, 0.5f, 1f);
            barBottom.At(0.5f, 0f, 0, 0, 4000, Bars * k, 0.5f, 0f);
            title.color = new Color(title.color.r, title.color.g, title.color.b, 0f);
            caption.color = new Color(caption.color.r, caption.color.g, caption.color.b, 0f);
            line.color = new Color(0f, 0f, 0f, 0f);
            flash.color = new Color(1f, 1f, 1f, 0f);
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

        /// <summary>How far along its way the bullet is at f (0..1 of the flight): quick out of the barrel,
        /// slowed almost to a stop in the middle, quick again at the end (bullet time).</summary>
        private static float Along(float f) => f + 0.78f * Mathf.Sin(2f * Mathf.PI * f) / (2f * Mathf.PI);

        private void LateUpdate()
        {
            UpdateRings();
            if (!playing) { if (stoppedAt > 0f) Afterwards(); return; }
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
            float fov = 50f, roll = 0f;
            bool steady = false;   // this shot is worked out smooth already (no extra easing after it)
            if (t < Intro)
            {
                // the crane: from high over the place and well back, down in one long curve onto the killer's
                // shoulder with the victim ahead, slowing as it lands
                float f = Mathf.SmoothStep(0f, 1f, t / Intro);
                var head = killer.transform.position + Vector3.up * 1.7f;
                var ahead = Vector3.Lerp(killer.transform.position, victim.transform.position, 0.45f) + Vector3.up * 1f;
                var high = head - flat * 9f + right * 5f + Vector3.up * 9f;
                var low = head - flat * 2.9f + right * 1.1f + Vector3.up * 1.9f;
                var swing = Quaternion.AngleAxis(Mathf.Lerp(-35f, 0f, f), Vector3.up);
                eye = Best(head, head + swing * (Vector3.Lerp(high, low, f) - head), head + swing * (Vector3.Lerp(high, low, f) - head - right * 2.2f * f),
                    head - flat * 1.4f + Vector3.up * 3.5f);
                look = Vector3.Lerp(Vector3.Lerp(killer.transform.position, victim.transform.position, 0.5f), ahead, f);
                fov = Mathf.Lerp(60f, 52f, f);
            }
            else if (t < Intro + Aim)
            {
                // over the killer's shoulder at the victim, pushing in a little
                float f = (t - Intro) / Aim;
                var head = killer.transform.position + Vector3.up * 1.7f;
                look = to;
                float pull = Mathf.Lerp(1.5f, 1.1f, Mathf.SmoothStep(0f, 1f, f));
                // over the right shoulder, or the left one when a wall is on the right
                eye = Best(head, head - flat * pull + right * 0.55f + Vector3.up * 0.1f, head - flat * pull - right * 0.55f + Vector3.up * 0.1f,
                    head - flat * 1.2f + Vector3.up * 0.45f);
                fov = Mathf.Lerp(48f, 40f, f);
            }
            else if (t < Intro + Aim + Flight)
            {
                // with the bullet: the camera rides beside it and turns round it from the side over the top to
                // behind, close in while it is slow; the last metres it stays and lets the bullet go into the victim
                float f = Mathf.Clamp01((t - Intro - Aim) / Flight);
                float u = Along(f);
                var at = Vector3.Lerp(from, to, u);
                if (!hit && bullet != null && !bullet.gameObject.activeSelf)
                {
                    bullet.gameObject.SetActive(true);
                    vapour.gameObject.SetActive(true);
                    Effects.MuzzleFlash(killer.Muzzle, def);
                    Effects.Puff(from + dir * 0.05f, dir);   // the puff of smoke at the muzzle
                    Sfx.PlayAt(def.Sound, from, 0.9f, def.SoundPitch * 0.75f, false);
                    nextRing = 0.06f;
                }
                if (bullet != null && bullet.gameObject.activeSelf)
                {
                    bullet.position = at;
                    // spinning in the rifling's twist, a turn every few frames even in the slow part
                    bullet.rotation = Quaternion.LookRotation(dir) * Quaternion.AngleAxis(t * 1440f, Vector3.forward);
                    vapour.position = at - dir * 0.03f;
                }
                // a ring of pushed air now and then along the way (more of them where it flies slowly)
                while (u >= nextRing && nextRing < 0.97f) { Ring(Vector3.Lerp(from, to, nextRing), dir); nextRing += 0.11f; }
                const float release = 0.84f;
                float uc = Mathf.Min(u, Along(release));
                var camAt = Vector3.Lerp(from, to, uc);
                // round the line: side (90) over the top (0) to a little past it, close while slow
                float turn = Mathf.Lerp(95f, -25f, Mathf.SmoothStep(0f, 1f, Mathf.Min(f, release) / release));
                var round = Quaternion.AngleAxis(turn, dir) * Vector3.ProjectOnPlane(Vector3.up, dir).normalized;
                float near = Mathf.Lerp(0.95f, 0.45f, Mathf.Sin(Mathf.Clamp01(f / release) * Mathf.PI));
                var place = camAt - dir * Mathf.Lerp(0.3f, 0.75f, f) + round * near;
                eye = Clear(camAt, place);
                look = f < release ? at + dir * 0.6f : Vector3.Lerp(at, to, 0.5f);
                fov = Mathf.Lerp(46f, 38f, Mathf.Sin(f * Mathf.PI));
                roll = Mathf.Lerp(10f, -6f, f);
                steady = true;
            }
            else if (t < Intro + Aim + Flight + Fall)
            {
                if (!hit)
                {
                    hit = true;
                    if (bullet != null) bullet.gameObject.SetActive(false);
                    Effects.Blood(to, -dir);
                    Effects.Blood(to + dir * 0.15f, dir);   // and out through the back
                }
                // the fall in slow motion
                if (victim.Anim != null) victim.Anim.speed = 0.42f;
                // from the side, a slow turn around the falling body
                float f = (t - Intro - Aim - Flight) / Fall;
                var center = victim.transform.position + Vector3.up * 0.7f;
                var around = Quaternion.AngleAxis(Mathf.Lerp(-25f, 30f, Mathf.SmoothStep(0f, 1f, f)), Vector3.up) * right;
                look = center;
                float dist = Mathf.Lerp(3.2f, 2.6f, f);
                eye = Best(center, center + around * dist + Vector3.up * 0.9f, center - around * dist + Vector3.up * 0.9f, center + Vector3.up * dist);
                fov = Mathf.Lerp(48f, 40f, f);
            }
            else
            {
                // the killer from in front and below, the camera turning slowly round them
                if (victim.Anim != null) victim.Anim.speed = 1f;
                float f = (t - (Length - Hero)) / Hero;
                var face = killer.transform.position + Vector3.up * 1.45f;
                var front = Quaternion.AngleAxis(Mathf.Lerp(-30f, -6f, Mathf.SmoothStep(0f, 1f, f)), Vector3.up) * flat;
                look = face + Vector3.up * 0.05f;
                eye = Best(face, face + front * 2.2f - Vector3.up * 0.55f, face + Quaternion.AngleAxis(56f, Vector3.up) * front * 2.2f - Vector3.up * 0.55f, face - flat * 2f + Vector3.up * 0.3f);
                fov = Mathf.Lerp(40f, 33f, f);
            }
            // each shot moves smoothly (a wall pushing the camera in does not make it jump); the bullet's shot is
            // smooth of itself and follows it exactly
            int now = t < Intro ? 0 : t < Intro + Aim ? 1 : t < Intro + Aim + Flight ? 2 : t < Length - Hero ? 3 : 4;
            if (now != shot || steady) { shot = now; smoothEye = eye; smoothLook = look; }
            float k = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
            smoothEye = Vector3.Lerp(smoothEye, eye, k);
            smoothLook = Vector3.Lerp(smoothLook, look, k);
            eye = smoothEye;
            look = smoothLook;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye) * Quaternion.Euler(0f, 0f, roll));
            cam.Cam.fieldOfView = fov;
            cam.Cam.ResetProjectionMatrix();
            cam.Cam.nearClipPlane = now == 2 ? 0.08f : CameraRig.NearClip;   // close to the bullet
        }

        private void Stop()
        {
            playing = false;
            CameraRig.Frozen = wasFrozen;
            if (bullet != null) bullet.gameObject.SetActive(false);
            if (vapour != null) vapour.gameObject.SetActive(false);
            if (CameraRig.I != null) CameraRig.I.Cam.nearClipPlane = CameraRig.NearClip;
            // the game comes back out of black (Afterwards), the bars opening
            if (cover != null && cover.gameObject.activeSelf) stoppedAt = Time.unscaledTime;
            if (killer != null) killer.SetVisible(true);
            if (victim != null && victim.Anim != null) victim.Anim.speed = 1f;
        }
    }
}
