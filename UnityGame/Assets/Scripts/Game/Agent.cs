using UnityEngine;

namespace UA
{
    /// <summary>
    /// One player in the match: the animated mannequin, its weapons and hitboxes. Driven by the local
    /// player controller, by a bot (on the host) or by the network snapshots (everyone else).
    /// </summary>
    public class Agent : MonoBehaviour
    {
        [Header("Set up by the editor")]
        public Animator Anim;
        public Transform Model;
        public Transform Head;
        public GameObject[] WeaponModels = new GameObject[Weapons.Count];
        public Transform[] Muzzles = new Transform[Weapons.Count];
        public Renderer[] TeamRenderers = new Renderer[0];
        public Renderer[] AllRenderers = new Renderer[0];
        public Hitbox[] Hitboxes = new Hitbox[0];
        public CharacterController Controller;
        [Tooltip("Realistic characters: light and dark skin (material named Skin_*) on these renderers")]
        public Material[] Skins = new Material[0];
        public Renderer[] SkinRenderers = new Renderer[0];
        [Tooltip("Every piece of clothing and gear; SetLook shows the ones the look picks")]
        public OutfitPart[] Parts = new OutfitPart[0];

        /// <summary>One wardrobe piece: shown when the look's value in Slot is one of Mask (and in Slot2 one of Mask2).</summary>
        [System.Serializable]
        public class OutfitPart
        {
            public GameObject Object;
            public Wardrobe.Slot Slot;
            public int Mask;
            public bool HasSecond;
            public Wardrobe.Slot Slot2;
            public int Mask2;
            [Tooltip("Colour chosen by this slot of the look: a material per colour (camo) or a tint")]
            public bool Coloured;
            public Wardrobe.Slot ColorSlot;
            public Material[] Materials = new Material[0];
            public Color[] Colors = new Color[0];
        }

        public byte Id { get; private set; }
        public bool Alive { get; private set; } = true;
        public PlayerState State { get; private set; }
        public bool IsLocal { get; private set; }

        private byte spawnSeq;
        private int weapon = -1;
        private bool visible = true;
        private float animMoveX, animMoveY;
        private static readonly int MoveXId = Animator.StringToHash("MoveX");
        private static readonly int MoveYId = Animator.StringToHash("MoveY");
        private static readonly int CrouchId = Animator.StringToHash("Crouch");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int DeadId = Animator.StringToHash("Dead");
        private static readonly int PitchId = Animator.StringToHash("Pitch");
        private static readonly int ProneId = Animator.StringToHash("Prone");
        private bool proneBox;
        private static readonly int FireId = Animator.StringToHash("Fire");
        private static readonly int ReloadId = Animator.StringToHash("Reload");

        /// <summary>Where the local player's crosshair points this frame (others aim along their yaw / pitch).</summary>
        [System.NonSerialized] public Vector3? AimPoint;
        private Transform chestBone, handBone, upperArmBone, lowerArmBone;
        private float throwStart = -10f, stepTimer;
        private int stepIndex;
        private static readonly int CadenceId = Animator.StringToHash("Cadence");

        public Transform Muzzle => weapon >= 0 && Muzzles[weapon] != null ? Muzzles[weapon] : Head;
        public Vector3 Eye => transform.position + Vector3.up * (State == null ? 1.6f : State.Prone ? 0.45f : State.Crouch ? 1.15f : 1.6f);
        public Vector3 Chest => transform.position + Vector3.up * (State == null ? 1.25f : State.Prone ? 0.3f : State.Crouch ? 0.85f : 1.25f);

        private void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n == "spine_03") chestBone = t;
                else if (n == "hand_r") handBone = t;
                else if (n == "upperarm_r") upperArmBone = t;
                else if (n == "lowerarm_r") lowerArmBone = t;
            }
        }

        /// <summary>
        /// After the animation: turn the chest most of the way and the right hand the rest, so the
        /// barrel points exactly at the aim point and the bullets visibly leave the muzzle towards it.
        /// </summary>
        private void LateUpdate()
        {
            CarryArms();
            AimArms();
            ThrowArm();
            HoldSyringe();
            FitBodyHitbox();
        }

        /// <summary>
        /// The body hitbox ends just under the head as the animation holds it (standing, crouched, leaning
        /// over the gun): a crouched player's head is a head shot, and a shot passing over it misses.
        /// </summary>
        private void FitBodyHitbox()
        {
            if (proneBox || Head == null || Hitboxes.Length == 0 || !(Hitboxes[0].GetComponent<CapsuleCollider>() is CapsuleCollider body)) return;
            var head = transform.InverseTransformPoint(Head.position);
            float top = Mathf.Clamp(head.y - 0.1f, 0.7f, 1.5f);
            body.height = top;
            body.center = new Vector3(head.x * 0.5f, top / 2f, head.z * 0.5f);
        }

        /// <summary>
        /// 1 while the gun is up at the shoulder (aiming, or shot within the last second), 0 when it is
        /// carried low in the right hand and the arms swing with the steps (as in Battle of Agents).
        /// </summary>
        public float Ready { get; private set; } = 1f;
        public bool Firing => Time.time - lastFire < 0.8f;
        private float lastFire = -10f;

        /// <summary>Low carry: the right hand turns the barrel forwards and down, along the body.</summary>
        private void CarryArms()
        {
            if (State == null || !Alive || weapon < 0 || handBone == null || weapon >= Muzzles.Length || Muzzles[weapon] == null) return;
            bool up = State.Aiming || Time.time - lastFire < 1.1f || State.Prone;
            Ready = Mathf.MoveTowards(Ready, up ? 1f : 0f, Time.deltaTime * (up ? 7f : 2.5f));
            if (Anim != null && Anim.layerCount > 1) Anim.SetLayerWeight(1, State.Prone ? 0f : Ready);
            if (Ready > 0.99f) return;
            var muzzle = Muzzles[weapon];
            var want = Quaternion.AngleAxis(48f, transform.right) * transform.forward;
            var turn = Limit(Quaternion.FromToRotation(muzzle.forward, want), 100f);
            handBone.rotation = Quaternion.Slerp(Quaternion.identity, turn, 1f - Ready) * handBone.rotation;
        }

        /// <summary>Grenade throw: the right arm winds up behind the head, swings over and follows through.</summary>
        public void PlayThrow() => throwStart = Time.time;

        private void ThrowArm()
        {
            const float length = 0.65f;
            float t = Time.time - throwStart;
            if (t < 0f || t > length || upperArmBone == null || State == null || !Alive) return;
            float angle = t < 0.25f ? Mathf.SmoothStep(0f, -150f, t / 0.25f)
                : t < 0.4f ? Mathf.Lerp(-150f, 40f, (t - 0.25f) / 0.15f)
                : Mathf.SmoothStep(40f, 0f, (t - 0.4f) / (length - 0.4f));
            var right = transform.right;
            // the body turns into the throw: away while winding up, towards the target on release
            if (chestBone != null && !State.Prone) chestBone.rotation = Quaternion.AngleAxis(angle / 150f * 28f, Vector3.up) * chestBone.rotation;
            upperArmBone.rotation = Quaternion.AngleAxis(angle, right) * upperArmBone.rotation;
            if (lowerArmBone != null) lowerArmBone.rotation = Quaternion.AngleAxis(Mathf.Min(0f, angle) * 0.35f, right) * lowerArmBone.rotation;
        }

        private void AimArms()
        {
            if (State == null || !Alive || weapon < 0 || handBone == null || weapon >= Muzzles.Length || Muzzles[weapon] == null) return;
            if (State.MoveY > 1.2f) return;   // sprinting: the gun is carried, not aimed
            if (Ready < 0.5f) return;   // carried low
            var muzzle = Muzzles[weapon];
            var forward = Quaternion.Euler(-State.Pitch, State.Yaw, 0) * Vector3.forward;
            var target = Eye + forward * 60f;
            // a point right in front of the face (a wall, an obstacle while running) would twist the
            // arms: then aim along the view direction instead
            if (AimPoint.HasValue)
            {
                var toPoint = AimPoint.Value - Eye;
                if (toPoint.magnitude > 4f && Vector3.Dot(toPoint.normalized, forward) > 0.9f) target = AimPoint.Value;
            }
            if (chestBone != null && !State.Prone)
            {
                var turn = Quaternion.FromToRotation(muzzle.forward, (target - muzzle.position).normalized);
                chestBone.rotation = Limit(Quaternion.Slerp(Quaternion.identity, turn, 0.75f), 30f) * chestBone.rotation;
            }
            for (int i = 0; i < 2; i++)
                handBone.rotation = Limit(Quaternion.FromToRotation(muzzle.forward, (target - muzzle.position).normalized), 20f) * handBone.rotation;
        }

        private static Quaternion Limit(Quaternion q, float maxDegrees)
        {
            q.ToAngleAxis(out float angle, out var axis);
            if (angle > 180f) angle -= 360f;
            return Mathf.Abs(angle) <= maxDegrees || float.IsNaN(axis.x) ? q : Quaternion.AngleAxis(Mathf.Sign(angle) * maxDegrees, axis);
        }

        public void Init(PlayerState state, bool local, bool bot, Color teamColor)
        {
            State = state;
            Id = state.Id;
            IsLocal = local;
            spawnSeq = state.SpawnSeq;
            foreach (var box in Hitboxes) box.Owner = this;
            SetTeamColor(teamColor);
            Controller.enabled = local || bot;
            if (local)
            {
                gameObject.AddComponent<LocalPlayer>();
                CameraRig.I.Target = this;
                CameraRig.I.Yaw = state.Yaw;
                CameraRig.I.Pitch = 0;
            }
            else if (bot)
            {
                gameObject.AddComponent<Bot>();
            }
            SetWeapon(state.Weapon);
            Alive = state.Alive;
        }

        /// <summary>Called by World every frame with the latest state.</summary>
        public void Sync(PlayerState state, Session s)
        {
            State = state;
            bool driven = IsLocal || (state.IsBot && s.IsHost);
            if (state.SpawnSeq != spawnSeq)
            {
                spawnSeq = state.SpawnSeq;
                Teleport(state.Pos, state.Yaw);
                if (IsLocal) GetComponent<LocalPlayer>()?.OnRespawn();
                if (driven && !IsLocal) GetComponent<Bot>()?.OnRespawn();
            }
            if (!driven)
            {
                // smooth towards the network position
                float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
                var target = state.Pos;
                if ((target - transform.position).sqrMagnitude > 25f) transform.position = target;
                else transform.position = Vector3.Lerp(transform.position, target, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, state.Yaw, 0), k);
            }
            Voice(state);
            if (state.Alive != Alive)
            {
                Alive = state.Alive;
                foreach (var box in Hitboxes) box.gameObject.SetActive(Alive);
                if (!Alive && Controller.enabled) Controller.enabled = false;
                if (Alive && driven) Controller.enabled = true;
            }
            if (weapon != state.Weapon) SetWeapon(state.Weapon);
            Animate(state);
        }

        public void Teleport(Vector3 pos, float yaw)
        {
            bool cc = Controller.enabled;
            Controller.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Controller.enabled = cc;
        }

        /// <summary>Tint only the materials named "Team..." (vest, helmet), not the suit, skin or weapons.</summary>
        public void SetTeamColor(Color teamColor)
        {
            foreach (var r in TeamRenderers)
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !materials[i].name.StartsWith("Team")) continue;
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", materials[i].color * teamColor * 1.6f);
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        /// <summary>Dress the agent as the look says (see Wardrobe).</summary>
        public void SetLook(int look)
        {
            female = Wardrobe.Female(look);
            foreach (var part in Parts)
            {
                if (part.Object == null) continue;
                bool on = ((part.Mask >> Wardrobe.Get(look, part.Slot)) & 1) == 1
                          && (!part.HasSecond || ((part.Mask2 >> Wardrobe.Get(look, part.Slot2)) & 1) == 1);
                part.Object.SetActive(on);
                if (!on || !part.Coloured) continue;
                int c = Wardrobe.Get(look, part.ColorSlot);
                foreach (var r in part.Object.GetComponentsInChildren<Renderer>(true))
                {
                    if (part.Materials.Length > 0) r.sharedMaterial = part.Materials[c % part.Materials.Length];
                    else if (part.Colors.Length > 0)
                    {
                        var block = new MaterialPropertyBlock();
                        block.SetColor("_Color", part.Colors[c % part.Colors.Length]);
                        r.SetPropertyBlock(block);
                    }
                }
            }
            if (Skins.Length > 0)
            {
                var tone = Skins[Wardrobe.Get(look, Wardrobe.Slot.Body) % Skins.Length];
                foreach (var r in SkinRenderers)
                {
                    if (r == null) continue;
                    var materials = r.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null && materials[i].name.StartsWith("Skin_")) materials[i] = tone;
                    r.sharedMaterials = materials;
                }
            }
        }

        public void SetWeapon(int id)
        {
            weapon = Mathf.Clamp(id, 0, Weapons.Count - 1);
            for (int i = 0; i < WeaponModels.Length; i++)
                if (WeaponModels[i] != null) WeaponModels[i].SetActive(i == weapon && visible);
        }

        public void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            foreach (var r in AllRenderers)
                if (r != null) r.enabled = value;
            SetWeapon(weapon);
        }

        private void Animate(PlayerState s)
        {
            if (Anim == null) return;
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            animMoveX = Mathf.Lerp(animMoveX, s.MoveX, k);
            animMoveY = Mathf.Lerp(animMoveY, s.MoveY, k);
            Anim.SetFloat(MoveXId, animMoveX);
            Anim.SetFloat(MoveYId, animMoveY);
            // shorter, quicker steps when running (same speed over the ground)
            float move = new Vector2(animMoveX, Mathf.Min(1f, animMoveY)).magnitude;
            // a real walk at half the stick (eight directions); running and sprinting take a little longer strides
            Anim.SetFloat(CadenceId, s.Crouch || s.Prone ? 1f : animMoveY > 1.2f ? 0.96f : move < 0.7f ? 1.05f : 1.0f);
            Steps(s, move);
            Anim.SetBool(CrouchId, s.Crouch);
            Anim.SetBool(ProneId, s.Prone);
            if (s.Prone != proneBox && Hitboxes.Length > 0 && Hitboxes[0].GetComponent<CapsuleCollider>() is CapsuleCollider body)
            {
                // body hitbox lies flat while prone
                proneBox = s.Prone;
                body.direction = proneBox ? 2 : 1;
                body.center = proneBox ? new Vector3(0, 0.25f, 0.1f) : new Vector3(0, 0.74f, 0);
                body.height = proneBox ? 1.6f : 1.48f;
                body.radius = proneBox ? 0.25f : 0.3f;
            }
            Anim.SetBool(GroundedId, s.Grounded);
            Anim.SetBool(DeadId, !s.Alive);
            Anim.SetFloat(PitchId, Mathf.Clamp(s.Pitch / 70f, -1f, 1f));
            if (Anim.layerCount > 1 && (!s.Alive || s.Prone)) Anim.SetLayerWeight(1, 0f);
        }

        /// <summary>Footsteps (walking, running, sprinting, crouching) and the rustle of crawling.</summary>
        private void Steps(PlayerState s, float move)
        {
            if (!s.Alive || !s.Grounded || move < 0.25f) { stepTimer = Mathf.Min(stepTimer, 0.06f); return; }
            stepTimer -= Time.deltaTime;
            if (stepTimer > 0f) return;
            bool sprint = animMoveY > 1.2f;
            stepTimer = s.Prone ? 0.8f : s.Crouch ? 0.45f : sprint ? 0.26f : move < 0.6f ? 0.42f : 0.3f;
            bool running = !s.Crouch && (sprint || move > 0.8f);
            // the same loudness for everyone: Sfx.Step lowers the others' steps with their distance
            float volume = 0.42f * (s.Crouch ? 0.45f : 1f) * (sprint ? 1.15f : 1f);
            // recorded steps (Fantozzi, CC0): soft ground in the village, stone and concrete elsewhere;
            // the made-up ones only when the recordings are missing
            // the player's own footstep recording first, then the recorded ones by ground
            string surface = Sfx.Count("step_b2_") > 0 ? "step_b2_"
                : World.I != null && World.I.Layout != null && World.I.Layout.Name == "VILLAGE" ? "fs_sand_" : "fs_stone_";
            int recorded = Sfx.Count(surface);
            string clip = s.Prone ? "crawl_" + (stepIndex++ % 2)
                : recorded > 0 ? surface + (stepIndex++ % recorded)
                : running ? "step_run_" + (stepIndex++ % 3)
                : "step_" + (stepIndex++ % 4);
            if (recorded > 0 && !s.Prone) volume *= running ? 0.9f : 0.75f;
            Sfx.Step(clip, transform.position, s.Prone ? volume * 0.8f : volume, IsLocal);
        }

        private float lastHealth = 100f, nextPain;
        private bool female;

        /// <summary>A cry when hit and when killed (Michel Baradari, CC-BY 3.0), heard around the body.</summary>
        private void Voice(PlayerState s)
        {
            if (s.Alive && s.Health < lastHealth - 0.5f && Time.time >= nextPain && Sfx.Count("pain_") > 0)
            {
                nextPain = Time.time + 0.6f;
                Sfx.PlayAt("pain_" + Random.Range(0, Sfx.Count("pain_")), Chest, IsLocal ? 0.55f : 0.8f, female ? 1.22f : 1f);
            }
            if (!s.Alive && Alive && Sfx.Count("death_") > 0)
                Sfx.PlayAt("death_" + Random.Range(0, Sfx.Count("death_")), Chest, 0.9f, female ? 1.22f : 1f);
            lastHealth = s.Health;
        }

        public void PlayFire()
        {
            lastFire = Time.time;
            if (Anim != null) Anim.SetTrigger(FireId);
        }
        public void PlayReload() { if (Anim != null) Anim.SetTrigger(ReloadId); }

        private GameObject syringe;
        private float syringeUntil;
        private static Material barrelMat, plungerMat, needleMat;
        private static readonly Material[] liquidMats = new Material[2];

        /// <summary>
        /// A syringe in the right hand for a moment: green adrenaline (kind 0) or dark red blood given to a
        /// team mate (kind 1). Built from a few small cylinders, no model file.
        /// </summary>
        public void ShowSyringe(byte kind, float seconds)
        {
            if (syringe != null) Destroy(syringe);
            Material M(ref Material m, Color c, float smooth, float metal)
            {
                if (m == null) { m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", metal); }
                return m;
            }
            barrelMat = M(ref barrelMat, new Color(0.86f, 0.9f, 0.92f), 0.85f, 0f);
            plungerMat = M(ref plungerMat, new Color(0.15f, 0.35f, 0.75f), 0.4f, 0f);
            needleMat = M(ref needleMat, new Color(0.8f, 0.8f, 0.82f), 0.9f, 1f);
            liquidMats[kind & 1] = M(ref liquidMats[kind & 1], kind == 0 ? new Color(0.35f, 0.95f, 0.45f) : new Color(0.5f, 0.02f, 0.03f), 0.9f, 0f);
            syringe = new GameObject("Syringe");
            void Part(Vector3 local, float radius, float length, Material m)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(syringe.transform, false);
                c.transform.localPosition = local;
                c.transform.localRotation = Quaternion.Euler(90, 0, 0);   // along +Z
                c.transform.localScale = new Vector3(radius * 2f, length / 2f, radius * 2f);
                var r = c.GetComponent<Renderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Part(new Vector3(0, 0, 0), 0.016f, 0.11f, barrelMat);
            Part(new Vector3(0, 0, 0.012f), 0.012f, 0.07f, liquidMats[kind & 1]);
            Part(new Vector3(0, 0, -0.075f), 0.005f, 0.05f, plungerMat);
            Part(new Vector3(0, 0, -0.1f), 0.02f, 0.008f, plungerMat);
            Part(new Vector3(0, 0, 0.075f), 0.0015f, 0.04f, needleMat);
            syringeUntil = Time.time + seconds;
        }

        private void HoldSyringe()
        {
            if (syringe == null) return;
            if (Time.time > syringeUntil || !Alive) { Destroy(syringe); syringe = null; return; }
            // held in the right hand, the needle forward and a little down
            var hand = handBone != null ? handBone.position : transform.position + transform.up * 1.2f + transform.right * 0.25f;
            syringe.transform.SetPositionAndRotation(hand + transform.forward * 0.05f, Quaternion.LookRotation(transform.forward * 0.8f - transform.up * 0.5f + transform.right * 0.2f));
        }
    }
}
