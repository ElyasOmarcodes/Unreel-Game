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
        // the explosions: a fireball and smoke from frames rendered in Blender (a real volume), hot embers in streaks
        private static ParticleSystem fireball, smokeCloud, embers, flames;
        private static readonly List<(Vector3 pos, float until, float next)> burning = new List<(Vector3, float, float)>();
        private static readonly List<(Transform t, Renderer r, float born)> scorches = new List<(Transform, Renderer, float)>();
        private static Material scorchMaterial;
        private static Mesh scorchMesh;
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
            holes.Clear();
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
            burning.Clear();
            scorches.Clear();
            var fireTex = Resources.Load<Texture2D>("Effects/Fireball");
            var smokeTex = Resources.Load<Texture2D>("Effects/Smoke");
            if (fireTex != null)
            {
                fireball = Sheet("Fireball", fireTex, 1.7f, 3.4f, new Color(1f, 1f, 1f, 1f), 1.6f, -0.02f);
                flames = Sheet("Flames", fireTex, 0.9f, 0.9f, new Color(1f, 0.95f, 0.9f, 0.9f), 1.4f, -0.05f);
            }
            if (smokeTex != null) smokeCloud = Sheet("SmokeCloud", smokeTex, 5.5f, 3.6f, new Color(0.3f, 0.29f, 0.28f, 0.85f), 2.4f, -0.015f);
            embers = MakeParticles("Embers", Resources.Load<Material>("Materials/Spark"), 1.6f, 0.07f, 14f, -0.3f, new Color(1f, 0.62f, 0.25f), 1.1f);
            var er = embers.GetComponent<ParticleSystemRenderer>();
            er.renderMode = ParticleSystemRenderMode.Stretch;
            er.velocityScale = 0.06f;
            er.lengthScale = 1f;
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

        /// <summary>Particles drawn from a 4 x 4 sheet of frames played once over their life, turned at random, growing.</summary>
        private static ParticleSystem Sheet(string name, Texture2D tex, float life, float size, Color tint, float grow, float gravity)
        {
            var mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { mainTexture = tex, name = name };
            mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            var ps = MakeParticles(name, mat, life, size, 1f, 0f, tint, gravity);
            var main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.15f);
            main.maxParticles = 200;
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1, grow)));
            // the frames fade by themselves: only the last moment is faded more
            var colorOverLife = ps.colorOverLifetime;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            colorOverLife.color = gradient;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortMode = ParticleSystemSortMode.Distance;
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

        /// <summary>A puff of smoke (no mark): a muzzle's breath in the air.</summary>
        public static void Puff(Vector3 pos, Vector3 dir)
        {
            Ensure();
            Emit(dust, pos, dir, 5, 0.5f);
        }

        public static void Impact(Vector3 pos, Vector3 normal)
        {
            Ensure();
            Emit(sparks, pos + normal * 0.02f, normal, 6, 0.6f);
            Emit(dust, pos + normal * 0.05f, normal, 3, 0.4f);
            BulletHole(pos, normal);
        }

        // ---------------------------------------------------------------- bullet holes
        // A mark stays where a bullet hit a wall, the ground or a crate, and fades away after a while.

        private const int MaxHoles = 64;
        private const float HoleLife = 15f;
        private static readonly List<(Transform t, Renderer r, float born)> holes = new List<(Transform, Renderer, float)>();
        private static int nextHole;
        private static Material holeMaterial;
        private static Mesh holeMesh;
        private static MaterialPropertyBlock holeBlock;

        private static void BulletHole(Vector3 pos, Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.01f) return;
            if (holeMaterial == null)
            {
                holeMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = HoleTexture(), renderQueue = 2450 };
                holeMesh = new Mesh { name = "BulletHole" };
                holeMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
                holeMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                holeMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                holeMesh.RecalculateBounds();
                holeBlock = new MaterialPropertyBlock();
            }
            (Transform t, Renderer r, float born) hole;
            if (holes.Count < MaxHoles)
            {
                var go = new GameObject("BulletHole");
                go.transform.SetParent(root);
                go.AddComponent<MeshFilter>().sharedMesh = holeMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = holeMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                hole = (go.transform, mr, 0f);
                holes.Add(hole);
                nextHole = holes.Count - 1;
            }
            else
            {
                nextHole = (nextHole + 1) % MaxHoles;   // the oldest one moves here
                hole = holes[nextHole];
            }
            hole.t.SetPositionAndRotation(pos + normal * 0.012f, Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
            hole.t.localScale = Vector3.one * Random.Range(0.11f, 0.16f);
            hole.r.enabled = true;
            holeBlock.SetColor("_Color", Color.white);
            hole.r.SetPropertyBlock(holeBlock);
            holes[nextHole] = (hole.t, hole.r, Time.time);
        }

        private static void FadeHoles(float now)
        {
            for (int i = 0; i < holes.Count; i++)
            {
                var (t, r, born) = holes[i];
                if (!r.enabled) continue;
                float age = now - born;
                if (age > HoleLife) { r.enabled = false; continue; }
                if (age < HoleLife - 4f) continue;
                holeBlock.SetColor("_Color", new Color(1, 1, 1, (HoleLife - age) / 4f));
                r.SetPropertyBlock(holeBlock);
            }
        }

        /// <summary>A dark hole with a broken grey rim and short cracks, drawn once.</summary>
        private static Texture2D HoleTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "BulletHole" };
            var rng = new System.Random(7);
            var cracks = new float[6];
            for (int i = 0; i < cracks.Length; i++) cracks[i] = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float noise = Mathf.PerlinNoise(x * 0.35f, y * 0.35f);
                    Color c;
                    if (d < 0.13f + noise * 0.03f) c = new Color(0.03f, 0.03f, 0.03f, 1f);                      // the hole
                    else if (d < 0.26f + noise * 0.05f) c = new Color(0.16f, 0.15f, 0.14f, 0.85f - (d - 0.13f) * 2f); // scorched rim
                    else
                    {
                        float crack = 0f;
                        foreach (float ca in cracks) crack = Mathf.Max(crack, 1f - Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, ca * Mathf.Rad2Deg)) / 6f);
                        float alpha = Mathf.Clamp01(crack) * Mathf.Clamp01((0.48f - d) * 6f) * 0.7f + Mathf.Clamp01((0.36f - d) * 4f) * 0.25f;
                        c = new Color(0.22f, 0.21f, 0.2f, alpha);
                    }
                    tex.SetPixel(x, y, c);
                }
            tex.Apply(true, true);
            return tex;
        }

        public static void Blood(Vector3 pos, Vector3 normal)
        {
            Ensure();
            Emit(blood, pos, normal, 8, 0.7f);
        }

        /// <summary>Grenade blast: fireball, hot embers, smoke, dust along the ground, a flash, a scorch mark, the boom and a shake.</summary>
        public static void Explosion(Vector3 pos) => Blast(pos, 1f, false);

        /// <summary>A fuel drum (kind 2: bigger, and it burns on a while) or a C4 charge (kind 3).</summary>
        public static void BigExplosion(Vector3 pos, int kind) => Blast(pos, kind == 2 ? 1.7f : 1.5f, kind == 2);

        private static void Blast(Vector3 pos, float scale, bool burns)
        {
            Ensure();
            if (fireball != null)
            {
                // several balls of fire at once, a little apart, each turned its own way: no two blasts alike
                int balls = Mathf.RoundToInt(4 * scale);
                var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                for (int i = 0; i < balls; i++)
                {
                    p.position = pos + Vector3.up * (0.6f * scale) + Random.insideUnitSphere * 0.7f * scale;
                    p.velocity = (Vector3.up * Random.Range(0.6f, 1.6f) + Random.insideUnitSphere * 1.2f) * scale;
                    p.startSize = Random.Range(2.4f, 3.6f) * scale;
                    fireball.Emit(p, 1);
                }
            }
            else Emit(fire, pos + Vector3.up * 0.3f, Vector3.up, 26, 1f);
            if (smokeCloud != null)
            {
                var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                for (int i = 0; i < Mathf.RoundToInt(7 * scale); i++)
                {
                    p.position = pos + Vector3.up * (0.9f * scale) + Random.insideUnitSphere * 1.1f * scale;
                    p.velocity = Vector3.up * Random.Range(0.8f, 1.8f) + Random.insideUnitSphere * 0.6f;
                    p.startSize = Random.Range(2.8f, 4.2f) * scale;
                    smokeCloud.Emit(p, 1);
                }
            }
            else Emit(smoke, pos + Vector3.up * 0.6f, Vector3.up, 14, 0.7f);
            Emit(embers, pos + Vector3.up * 0.3f, Vector3.up, Mathf.RoundToInt(45 * scale), 0.9f);
            Emit(debris, pos + Vector3.up * 0.2f, Vector3.up, Mathf.RoundToInt(30 * scale), 0.85f);
            // the blast throws the dust of the ground outwards along it
            var dp = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < Mathf.RoundToInt(22 * scale); i++)
            {
                var dir = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
                dp.position = pos + Vector3.up * 0.15f + dir * 0.4f;
                dp.velocity = (dir * Random.Range(4f, 9f) + Vector3.up * Random.Range(0.2f, 1.2f)) * scale;
                dp.startSize = Random.Range(0.8f, 1.6f) * scale;
                dust.Emit(dp, 1);
            }
            Scorch(pos, 2.6f * scale);
            if (burns) burning.Add((pos, Time.time + 7f, 0f));
            var go = new GameObject("Blast");
            go.transform.SetParent(root);
            go.transform.position = pos + Vector3.up * 1.2f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.66f, 0.35f);
            light.range = 16f * scale;
            light.intensity = 9f;
            light.shadows = LightShadows.None;
            blasts.Add((light, Time.time, Time.time + 0.45f * scale));
            Sfx.PlayAt(Sfx.Has("explosion_b5") ? "explosion_b5" : "explosion", pos, 1f, scale > 1f ? Random.Range(0.8f, 0.88f) : Random.Range(0.94f, 1.04f));
            if (CameraRig.I != null)
            {
                float d = Vector3.Distance(CameraRig.I.transform.position, pos);
                CameraRig.I.Shake(Mathf.Clamp01(1f - d / (30f * scale)) * 2.2f * scale);
            }
        }

        /// <summary>A black burn on the ground under a blast (it fades after half a minute).</summary>
        private static void Scorch(Vector3 pos, float size)
        {
            if (!Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, World.EnvMask, QueryTriggerInteraction.Ignore)) return;
            if (scorchMaterial == null)
            {
                var tex = Resources.Load<Texture2D>("Effects/Scorch");
                if (tex == null) return;
                scorchMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, renderQueue = 2451 };
            }
            if (scorchMesh == null)
            {
                scorchMesh = new Mesh { name = "Scorch" };
                scorchMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
                scorchMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                scorchMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                scorchMesh.RecalculateBounds();
            }
            if (holeBlock == null) holeBlock = new MaterialPropertyBlock();
            (Transform t, Renderer r, float born) mark;
            if (scorches.Count < 8)
            {
                var go = new GameObject("Scorch");
                go.transform.SetParent(root);
                go.AddComponent<MeshFilter>().sharedMesh = scorchMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = scorchMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mark = (go.transform, mr, 0f);
                scorches.Add(mark);
            }
            else mark = scorches[0];
            scorches.Remove(mark);
            mark.t.SetPositionAndRotation(hit.point + hit.normal * 0.015f, Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
            mark.t.localScale = Vector3.one * size;
            mark.r.enabled = true;
            scorches.Add((mark.t, mark.r, Time.time));
        }

        private static void Tick()
        {
            float now = Time.time;
            FadeHoles(now);
            // a drum burns on for a while: small flames licking up and a thin column of smoke
            for (int i = burning.Count - 1; i >= 0; i--)
            {
                var (pos, until, next) = burning[i];
                if (now > until) { burning.RemoveAt(i); continue; }
                if (now < next) continue;
                burning[i] = (pos, until, now + 0.12f);
                if (flames != null)
                {
                    var p = new ParticleSystem.EmitParams { applyShapeToPosition = false, position = pos + new Vector3(Random.Range(-0.5f, 0.5f), 0.1f, Random.Range(-0.5f, 0.5f)), velocity = Vector3.up * Random.Range(0.8f, 1.6f), startSize = Random.Range(0.7f, 1.2f) * Mathf.Clamp01((until - now) / 2f + 0.3f) };
                    flames.Emit(p, 1);
                }
                if (smokeCloud != null && Random.value < 0.35f)
                {
                    var p = new ParticleSystem.EmitParams { applyShapeToPosition = false, position = pos + Vector3.up * 1.2f, velocity = Vector3.up * Random.Range(1f, 1.8f), startSize = Random.Range(1.2f, 2f) };
                    smokeCloud.Emit(p, 1);
                }
            }
            for (int i = 0; i < scorches.Count && holeBlock != null; i++)
            {
                var (t, r, born) = scorches[i];
                if (!r.enabled) continue;
                float age = now - born;
                if (age > 30f) r.enabled = false;
                else if (age > 24f) { holeBlock.SetColor("_Color", new Color(1, 1, 1, (30f - age) / 6f)); r.SetPropertyBlock(holeBlock); }
                else { holeBlock.SetColor("_Color", Color.white); r.SetPropertyBlock(holeBlock); }
            }
            for (int i = blasts.Count - 1; i >= 0; i--)
            {
                var b = blasts[i];
                if (b.light == null) { blasts.RemoveAt(i); continue; }
                if (now >= b.until) { Object.Destroy(b.light.gameObject); blasts.RemoveAt(i); continue; }
                float k = (now - b.start) / (b.until - b.start);
                b.light.intensity = 9f * (1f - k) * (1f - k) * (0.85f + 0.15f * Mathf.PerlinNoise(now * 30f, 0f));
            }
            foreach (var t in tracers)
                if (t.Line.enabled && now > t.Until) t.Line.enabled = false;
            for (int i = flashes.Count - 1; i >= 0; i--)
                if (flashes[i].go.activeSelf && now > flashes[i].until)
                    flashes[i].go.SetActive(false);
        }
    }
}
