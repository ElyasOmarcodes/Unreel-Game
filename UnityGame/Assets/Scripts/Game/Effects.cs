using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>Pooled shot effects: tracers, muzzle flashes, impact sparks and dust, blood puffs.</summary>
    public static class Effects
    {
        private class TracerItem { public LineRenderer Line; public float Until; }

        private static readonly List<TracerItem> tracers = new List<TracerItem>();
        private static readonly List<(GameObject go, float until)> flashes = new List<(GameObject, float)>();
        private static ParticleSystem sparks, dust, blood, fire, smoke, debris;
        private static readonly List<(Light light, float start, float until)> blasts = new List<(Light, float, float)>();
        private static Material tracerMaterial, flashMaterial;
        private static Transform root;
        private static Runner runner;

        private class Runner : MonoBehaviour
        {
            private void LateUpdate() => Tick();
        }

        private static void Ensure()
        {
            if (root != null) return;
            root = new GameObject("Effects").transform;
            runner = root.gameObject.AddComponent<Runner>();
            tracers.Clear();
            flashes.Clear();
            tracerMaterial = Resources.Load<Material>("Materials/Tracer");
            flashMaterial = Resources.Load<Material>("Materials/Flash");
            sparks = MakeParticles("Sparks", Resources.Load<Material>("Materials/Spark"), 0.15f, 0.06f, 9f, 0.25f, new Color(1f, 0.8f, 0.4f), 1f);
            dust = MakeParticles("Dust", Resources.Load<Material>("Materials/Dust"), 0.6f, 0.35f, 1.6f, 0.8f, new Color(0.62f, 0.6f, 0.55f, 0.7f), -0.1f);
            fire = MakeParticles("Fire", flashMaterial, 0.45f, 1.6f, 7f, 0.6f, new Color(1f, 0.62f, 0.25f), -0.2f);
            smoke = MakeParticles("Smoke", Resources.Load<Material>("Materials/Dust"), 3.2f, 2.4f, 2.8f, 1.2f, new Color(0.18f, 0.17f, 0.16f, 0.85f), -0.05f);
            debris = MakeParticles("Debris", Resources.Load<Material>("Materials/Spark"), 1.1f, 0.08f, 16f, 0f, new Color(1f, 0.7f, 0.35f), 1.6f);
            blasts.Clear();
            blood = MakeParticles("Blood", Resources.Load<Material>("Materials/Blood"), 0.45f, 0.18f, 2.5f, 0.5f, new Color(0.55f, 0.03f, 0.02f, 0.9f), 0.8f);
        }

        private static ParticleSystem MakeParticles(string name, Material material, float life, float size, float speed, float sizeGrow, Color color, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 1f + sizeGrow * 3f));
            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            colorOverLife.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static void Emit(ParticleSystem ps, Vector3 pos, Vector3 normal, int count, float spreadAngle)
        {
            var p = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = false };
            var main = ps.main;
            for (int i = 0; i < count; i++)
            {
                var dir = Vector3.Slerp(normal, Random.onUnitSphere, spreadAngle).normalized;
                p.velocity = dir * Random.Range(main.startSpeed.constantMin, main.startSpeed.constantMax);
                ps.Emit(p, 1);
            }
        }

        public static void Tracer(Vector3 from, Vector3 to, float alpha = 1f)
        {
            Ensure();
            TracerItem item = null;
            foreach (var t in tracers)
                if (!t.Line.enabled) { item = t; break; }
            if (item == null)
            {
                if (tracers.Count >= 48) item = tracers[0];
                else
                {
                    var go = new GameObject("Tracer");
                    go.transform.SetParent(root);
                    var line = go.AddComponent<LineRenderer>();
                    line.sharedMaterial = tracerMaterial;
                    line.positionCount = 2;
                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    line.numCapVertices = 0;
                    item = new TracerItem { Line = line };
                    tracers.Add(item);
                }
            }
            // draw only the far part of the bullet path so it does not cover the screen
            var start = Vector3.Lerp(from, to, Random.Range(0.05f, 0.25f));
            item.Line.SetPosition(0, start);
            item.Line.SetPosition(1, to);
            item.Line.startWidth = 0.025f;
            item.Line.endWidth = 0.012f;
            var c = new Color(1f, 0.85f, 0.5f, 0.85f * alpha);
            item.Line.startColor = c;
            item.Line.endColor = new Color(c.r, c.g, c.b, 0.2f * alpha);
            item.Line.enabled = true;
            item.Until = Time.time + 0.05f;
        }

        public static void MuzzleFlash(Transform muzzle, WeaponDef def)
        {
            if (muzzle == null) return;
            Ensure();
            GameObject flash = null;
            for (int i = 0; i < flashes.Count; i++)
                if (!flashes[i].go.activeSelf) { flash = flashes[i].go; flashes.RemoveAt(i); break; }
            if (flash == null)
            {
                flash = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(flash.GetComponent<Collider>());
                flash.GetComponent<Renderer>().sharedMaterial = flashMaterial;
                flash.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var light = flash.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 5f;
                light.intensity = 2.5f;
                light.color = new Color(1f, 0.75f, 0.4f);
                light.shadows = LightShadows.None;
            }
            flash.transform.SetParent(root);
            flash.transform.position = muzzle.position + muzzle.forward * 0.05f;
            var cam = CameraRig.I != null ? CameraRig.I.transform : null;
            flash.transform.rotation = cam != null ? Quaternion.LookRotation(cam.forward) * Quaternion.Euler(0, 0, Random.Range(0, 360f)) : muzzle.rotation;
            float size = def.Pellets > 1 || def.Scope ? 0.55f : def.Primary ? 0.42f : 0.3f;
            flash.transform.localScale = Vector3.one * size * Random.Range(0.8f, 1.2f);
            flash.SetActive(true);
            flashes.Add((flash, Time.time + 0.045f));
        }

        public static void Impact(Vector3 pos, Vector3 normal)
        {
            Ensure();
            Emit(sparks, pos + normal * 0.02f, normal, 6, 0.6f);
            Emit(dust, pos + normal * 0.05f, normal, 3, 0.4f);
        }

        public static void Blood(Vector3 pos, Vector3 normal)
        {
            Ensure();
            Emit(blood, pos, normal, 8, 0.7f);
        }

        /// <summary>Grenade blast: fireball, sparks, smoke, a flash of light, the boom and a camera shake.</summary>
        public static void Explosion(Vector3 pos)
        {
            Ensure();
            Emit(fire, pos + Vector3.up * 0.3f, Vector3.up, 26, 1f);
            Emit(debris, pos + Vector3.up * 0.2f, Vector3.up, 40, 0.85f);
            Emit(smoke, pos + Vector3.up * 0.6f, Vector3.up, 14, 0.7f);
            Emit(dust, pos + Vector3.up * 0.1f, Vector3.up, 16, 0.9f);
            var go = new GameObject("Blast");
            go.transform.SetParent(root);
            go.transform.position = pos + Vector3.up * 0.8f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.4f);
            light.range = 16f;
            light.intensity = 9f;
            light.shadows = LightShadows.None;
            blasts.Add((light, Time.time, Time.time + 0.35f));
            Sfx.PlayAt(Sfx.Has("explosion_b5") ? "explosion_b5" : "explosion", pos, 1f, Random.Range(0.94f, 1.04f));
            if (CameraRig.I != null)
            {
                float d = Vector3.Distance(CameraRig.I.transform.position, pos);
                CameraRig.I.Shake(Mathf.Clamp01(1f - d / 30f) * 2.2f);
            }
        }

        private static void Tick()
        {
            float now = Time.time;
            for (int i = blasts.Count - 1; i >= 0; i--)
            {
                var b = blasts[i];
                if (b.light == null) { blasts.RemoveAt(i); continue; }
                if (now >= b.until) { Object.Destroy(b.light.gameObject); blasts.RemoveAt(i); continue; }
                b.light.intensity = 9f * (1f - (now - b.start) / (b.until - b.start));
            }
            foreach (var t in tracers)
                if (t.Line.enabled && now > t.Until) t.Line.enabled = false;
            for (int i = flashes.Count - 1; i >= 0; i--)
                if (flashes[i].go.activeSelf && now > flashes[i].until)
                    flashes[i].go.SetActive(false);
        }
    }
}
