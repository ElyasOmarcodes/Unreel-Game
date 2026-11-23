using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// The supply drops as every machine sees them (from Session.Drops and the landing time in each): a red
    /// circle pulsing on the ground where it will come down; a four-engined cargo plane crossing the sky over
    /// the place, its engines droning; the crate leaving it, falling free a moment, the parachute snapping open
    /// and the crate swinging down under it; the landing in a burst of dust with a heavy thud, the chute
    /// collapsing beside it; then red smoke climbing from the crate and a strobe blinking on it until it is
    /// opened. The crate on the ground itself is the loot (LootView), opened like any other.
    /// </summary>
    public class AirDropView : MonoBehaviour
    {
        private class View
        {
            public GameObject Plane, Crate, Chute, Marker, Strobe;
            public Transform[] Props;
            public ParticleSystem Smoke;
            public AudioSource Engine;
            public bool Landed, ChuteOpen, SmokeOn;
            public float LandedAt;
        }

        private readonly Dictionary<ushort, View> views = new Dictionary<ushort, View>();
        /// <summary>The plane's speed (m/s) and how far before the spot the crate leaves it (it drifts on to the spot).</summary>
        private const float PlaneSpeed = 62f, Lead = 22f, FreeFall = 0.8f;
        private static Material markerMat, smokeMat, strobeMat;
        private static Mesh markerMesh;

        /// <summary>Where the plane of a drop is at a time (on this machine's clock).</summary>
        public static Vector3 PlaneAt(AirDrop d, float now)
        {
            var dir = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
            float t = now - (d.LandAt - Session.DropFall);
            return d.Pos + Vector3.up * (Session.DropHeight + 4f) - dir * Lead + dir * PlaneSpeed * t;
        }

        /// <summary>Where a drop's crate is at a time: in the plane, falling, on its parachute, landed.</summary>
        public static Vector3 CrateAt(AirDrop d, float now)
        {
            var dir = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
            float t = now - (d.LandAt - Session.DropFall), fall = Session.DropFall;
            if (t <= 0f) return PlaneAt(d, now);
            if (t >= fall) return d.Pos;
            float h0 = Session.DropHeight - 4.9f * FreeFall * FreeFall;
            float h = t < FreeFall ? Session.DropHeight - 4.9f * t * t : h0 * (1f - (t - FreeFall) / (fall - FreeFall));
            float drift = Lead * (1f - Mathf.Exp(-2.5f * t)) / (1f - Mathf.Exp(-2.5f * fall));
            return d.Pos - dir * Lead + dir * drift + Vector3.up * Mathf.Max(0f, h);
        }

        private void Update()
        {
            var s = Session.I;
            if (s == null) return;
            float now = Time.unscaledTime;
            if (views.Count > 0)
                foreach (var id in new List<ushort>(views.Keys))
                    if (!s.Drops.Exists(d => d.Id == id)) { Remove(views[id]); views.Remove(id); }
            foreach (var d in s.Drops)
            {
                if (!views.TryGetValue(d.Id, out var v)) { v = Make(d); views[d.Id] = v; }
                Animate(s, d, v, now);
            }
        }

        private void OnDisable()
        {
            foreach (var v in views.Values) Remove(v);
            views.Clear();
        }

        private static GameObject Load(string name)
        {
            var prefab = Resources.Load<GameObject>("Loot/" + name);
            GameObject go;
            if (prefab != null) go = Instantiate(prefab);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = Vector3.one * (name == "Plane" ? 8f : 1f);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            return go;
        }

        private View Make(AirDrop d)
        {
            var v = new View();
            v.Plane = Load("Plane");
            v.Plane.transform.SetParent(transform, false);
            v.Plane.SetActive(false);
            var props = new List<Transform>();
            foreach (var t in v.Plane.GetComponentsInChildren<Transform>(true)) if (t.name == "Prop") props.Add(t);
            v.Props = props.ToArray();
            foreach (var r in v.Plane.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var clip = Resources.Load<AudioClip>("Audio/plane");
            if (clip != null)
            {
                v.Engine = v.Plane.AddComponent<AudioSource>();
                v.Engine.clip = clip;
                v.Engine.loop = true;
                v.Engine.spatialBlend = 1f;
                v.Engine.rolloffMode = AudioRolloffMode.Linear;
                v.Engine.minDistance = 40f;
                v.Engine.maxDistance = 520f;
                v.Engine.dopplerLevel = 0.6f;
                v.Engine.volume = 0.9f;
            }
            v.Crate = Load("AirdropCrate");
            v.Crate.transform.SetParent(transform, false);
            v.Crate.SetActive(false);
            v.Chute = Load("Parachute");
            v.Chute.transform.SetParent(transform, false);
            v.Chute.SetActive(false);
            v.Marker = Marker(d.Pos);
            v.Marker.transform.SetParent(transform, true);
            v.Strobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(v.Strobe.GetComponent<Collider>());
            v.Strobe.name = "Strobe";
            v.Strobe.transform.SetParent(transform, false);
            v.Strobe.transform.localScale = Vector3.one * 0.12f;
            if (strobeMat == null)
            {
                strobeMat = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.1f, 0.05f) };
                strobeMat.EnableKeyword("_EMISSION");
                strobeMat.SetColor("_EmissionColor", new Color(1f, 0.15f, 0.05f) * 4f);
            }
            v.Strobe.GetComponent<Renderer>().sharedMaterial = strobeMat;
            v.Strobe.SetActive(false);
            v.Smoke = Smoke(transform);
            return v;
        }

        private static void Remove(View v)
        {
            foreach (var go in new[] { v.Plane, v.Crate, v.Chute, v.Marker, v.Strobe })
                if (go != null) Destroy(go);
            if (v.Smoke != null) Destroy(v.Smoke.gameObject, 0.1f);
        }

        private void Animate(Session s, AirDrop d, View v, float now)
        {
            var dir = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
            float t = now - (d.LandAt - Session.DropFall);   // since the crate left the plane
            float fall = Session.DropFall;

            // the plane: from far behind, over the place, on and away
            bool planeOn = t > -8.5f && t < 7.5f;
            if (v.Plane.activeSelf != planeOn) v.Plane.SetActive(planeOn);
            if (planeOn)
            {
                var p = PlaneAt(d, now);
                // a gentle bank and a little rise and fall: it flies, it is not pulled along a rail
                var roll = Mathf.Sin(t * 0.35f) * 4f;
                v.Plane.transform.SetPositionAndRotation(p + Vector3.up * Mathf.Sin(t * 0.5f) * 0.8f, Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, roll));
                foreach (var prop in v.Props) if (prop != null) prop.localRotation = Quaternion.Euler(0f, 0f, now * 1500f % 360f);
                if (v.Engine != null)
                {
                    if (!v.Engine.isPlaying) v.Engine.Play();
                    v.Engine.volume = 0.9f * Mathf.Clamp01((t + 8.5f) / 2f) * Mathf.Clamp01((7.5f - t) / 2f);
                }
            }

            // the red circle where it will come down (until it lands)
            bool marked = t < fall && !d.Opened;
            if (v.Marker.activeSelf != marked) v.Marker.SetActive(marked);
            if (marked)
            {
                float pulse = 1f + Mathf.Sin(now * 4f) * 0.06f;
                v.Marker.transform.localScale = new Vector3(pulse, 1f, pulse);
            }

            bool realCrate = s.Loot.Exists(l => l.Kind == LootKind.Airdrop && (l.Pos - d.Pos).sqrMagnitude < 1.5f);
            if (t < 0f)
            {
                if (v.Crate.activeSelf) v.Crate.SetActive(false);
                if (v.Chute.activeSelf) v.Chute.SetActive(false);
            }
            else if (t < fall)
            {
                // free fall a moment, then the chute: down at a steady speed to the spot, swinging under it
                var pos = CrateAt(d, now);
                float sway = t > FreeFall ? Mathf.Sin((t - FreeFall) * 1.7f) * 8f * Mathf.Clamp01((t - FreeFall) / 1.2f) * Mathf.Clamp01((fall - t) / 1.2f) : 0f;
                var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(sway, 0f, sway * 0.6f);
                if (!v.Crate.activeSelf) v.Crate.SetActive(true);
                v.Crate.transform.SetPositionAndRotation(pos, rot);
                bool chute = t > FreeFall;
                if (v.Chute.activeSelf != chute) v.Chute.SetActive(chute);
                if (chute)
                {
                    if (!v.ChuteOpen)
                    {
                        v.ChuteOpen = true;
                        Sfx.PlayAt(Sfx.Has("chute_open") ? "chute_open" : "click", pos, 1f, 1f, false);
                    }
                    // snapping open: wide and flat first, then round
                    float open = Mathf.SmoothStep(0f, 1f, (t - FreeFall) / 0.7f);
                    float breathe = 1f + Mathf.Sin(t * 3.1f) * 0.02f;
                    v.Chute.transform.SetPositionAndRotation(pos, rot);
                    v.Chute.transform.localScale = new Vector3(Mathf.Lerp(0.15f, 1f, open) * breathe, Mathf.Lerp(0.55f, 1f, open), Mathf.Lerp(0.15f, 1f, open) * breathe);
                }
            }
            else
            {
                // down: a burst of dust and a thud once, the chute falling over beside it
                if (!v.Landed)
                {
                    v.Landed = true;
                    v.LandedAt = now;
                    if (now - d.LandAt < 3f)
                    {
                        Effects.LandingDust(d.Pos);
                        Sfx.PlayAt(Sfx.Has("drop_land") ? "drop_land" : "grenade_bounce", d.Pos, 1f, 0.8f, false);
                    }
                }
                // the falling crate stands in until the real one (the loot) is there
                bool keep = !realCrate && !d.Opened && now - d.LandAt < 2f;
                if (keep)
                {
                    if (!v.Crate.activeSelf) v.Crate.SetActive(true);
                    v.Crate.transform.SetPositionAndRotation(d.Pos, Quaternion.LookRotation(dir));
                }
                else if (v.Crate.activeSelf) v.Crate.SetActive(false);
                float c = (now - v.LandedAt) / 3f;
                if (c < 1f && v.ChuteOpen)
                {
                    if (!v.Chute.activeSelf) v.Chute.SetActive(true);
                    // it tips over downwind, sinking and settling on the ground
                    float k = Mathf.SmoothStep(0f, 1f, c);
                    var side = Quaternion.Euler(0f, 90f, 0f) * dir;
                    v.Chute.transform.SetPositionAndRotation(d.Pos + dir * (2.5f * k) + side * (0.8f * k),
                        Quaternion.LookRotation(dir) * Quaternion.Euler(Mathf.Lerp(0f, 84f, k), 0f, 0f));
                    v.Chute.transform.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.25f, k), 1f);
                }
                else if (v.Chute.activeSelf) v.Chute.SetActive(false);
                // red smoke and a strobe until it is opened (the smoke dies down after that)
                if (!v.SmokeOn && !d.Opened)
                {
                    v.SmokeOn = true;
                    v.Smoke.transform.position = d.Pos + Vector3.up * 1.05f;
                    v.Smoke.Play();
                }
                if (d.Opened && v.Smoke.isEmitting) v.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                bool blink = !d.Opened && Mathf.Repeat(now, 1.1f) < 0.12f;
                if (v.Strobe.activeSelf != blink) v.Strobe.SetActive(blink);
                if (blink) v.Strobe.transform.position = d.Pos + Vector3.up * 1.18f;
            }
        }

        /// <summary>A soft red circle on the ground, 6 m across, where the drop will come down.</summary>
        private static GameObject Marker(Vector3 at)
        {
            if (markerMat == null)
            {
                markerMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float r = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                        // a bright rim, a faint inside, and four ticks pointing in
                        float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) / 0.06f) + Mathf.Clamp01(1f - r) * 0.18f;
                        float ang = Mathf.Atan2(y - size / 2f, x - size / 2f) * Mathf.Rad2Deg;
                        if (r > 0.62f && r < 0.84f && Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / 90f) * 90f)) < 3f) a = 0.9f;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                    }
                tex.Apply();
                markerMat.mainTexture = tex;
                markerMat.SetColor("_TintColor", new Color(0.6f, 0.08f, 0.05f, 0.55f));
                markerMesh = new Mesh { name = "DropMarker" };
                markerMesh.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) };
                markerMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                markerMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                markerMesh.RecalculateBounds();
            }
            var go = new GameObject("DropMarker");
            go.transform.position = at + Vector3.up * 0.04f;
            var disc = new GameObject("Disc");
            disc.transform.SetParent(go.transform, false);
            disc.transform.localScale = Vector3.one * 3f;
            disc.AddComponent<MeshFilter>().sharedMesh = markerMesh;
            var mr = disc.AddComponent<MeshRenderer>();
            mr.sharedMaterial = markerMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>Thick red smoke rising from the crate and leaning with the wind (frames of real smoke, tinted).</summary>
        private static ParticleSystem Smoke(Transform parent)
        {
            var go = new GameObject("RedSmoke");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // the cone points up
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.86f, 0.14f, 0.09f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.015f;
            main.maxParticles = 70;
            var emission = ps.emission;
            emission.rateOverTime = 9f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.08f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(3.6f, new AnimationCurve(new Keyframe(0f, 0.22f), new Keyframe(0.35f, 0.6f), new Keyframe(1f, 1f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.55f, 0.52f), 0.6f), new GradientColorKey(new Color(0.55f, 0.5f, 0.5f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.08f), new GradientAlphaKey(0.55f, 0.55f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var wind = ps.velocityOverLifetime;
            wind.enabled = true;
            wind.space = ParticleSystemSimulationSpace.World;
            wind.x = new ParticleSystem.MinMaxCurve(0.8f);
            wind.y = new ParticleSystem.MinMaxCurve(0f);
            wind.z = new ParticleSystem.MinMaxCurve(0.35f);
            var turn = ps.rotationOverLifetime;
            turn.enabled = true;
            turn.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (smokeMat == null)
            {
                var tex = Resources.Load<Texture2D>("Effects/Smoke");
                smokeMat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { mainTexture = tex, name = "DropSmoke" };
                smokeMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            if (smokeMat.mainTexture != null)
            {
                var sheet = ps.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = 4;
                sheet.numTilesY = 4;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            }
            renderer.sharedMaterial = smokeMat;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }
    }
}
