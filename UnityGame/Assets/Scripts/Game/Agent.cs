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
        private Transform chestBone, handBone;

        public Transform Muzzle => weapon >= 0 && Muzzles[weapon] != null ? Muzzles[weapon] : Head;
        public Vector3 Eye => transform.position + Vector3.up * (State == null ? 1.6f : State.Prone ? 0.45f : State.Crouch ? 1.15f : 1.6f);
        public Vector3 Chest => transform.position + Vector3.up * (State == null ? 1.25f : State.Prone ? 0.3f : State.Crouch ? 0.85f : 1.25f);

        private void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "spine_03") chestBone = t;
                else if (t.name == "hand_r") handBone = t;
            }
        }

        /// <summary>
        /// After the animation: turn the chest most of the way and the right hand the rest, so the
        /// barrel points exactly at the aim point and the bullets visibly leave the muzzle towards it.
        /// </summary>
        private void LateUpdate()
        {
            if (State == null || !Alive || weapon < 0 || handBone == null || weapon >= Muzzles.Length || Muzzles[weapon] == null) return;
            if (State.MoveY > 1.2f) return;   // sprinting: the gun is carried, not aimed
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
            if (Anim.layerCount > 1) Anim.SetLayerWeight(1, s.Alive && !s.Prone ? 1f : 0f);
        }

        public void PlayFire() { if (Anim != null) Anim.SetTrigger(FireId); }
        public void PlayReload() { if (Anim != null) Anim.SetTrigger(ReloadId); }
    }
}
