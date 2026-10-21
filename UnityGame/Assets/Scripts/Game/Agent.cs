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
        [Tooltip("Head gear options (helmet, cap, beret, mask); one is shown per look")]
        public GameObject[] Headgear = new GameObject[0];
        [Tooltip("Camo suits; the mannequin material named Suit* is replaced per look")]
        public Material[] Suits = new Material[0];
        public Renderer[] SuitRenderers = new Renderer[0];

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

        public Transform Muzzle => weapon >= 0 && Muzzles[weapon] != null ? Muzzles[weapon] : Head;
        public Vector3 Eye => transform.position + Vector3.up * (State == null ? 1.6f : State.Prone ? 0.45f : State.Crouch ? 1.15f : 1.6f);
        public Vector3 Chest => transform.position + Vector3.up * (State == null ? 1.25f : State.Prone ? 0.3f : State.Crouch ? 0.85f : 1.25f);

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

        /// <summary>Outfit for this player: head gear and camo pattern.</summary>
        public void SetLook(int look)
        {
            for (int i = 0; i < Headgear.Length; i++)
                if (Headgear[i] != null) Headgear[i].SetActive(i == look % Headgear.Length);
            if (Suits.Length == 0) return;
            var suit = Suits[(look / Mathf.Max(1, Headgear.Length)) % Suits.Length];
            foreach (var r in SuitRenderers)
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].name.StartsWith("Suit")) materials[i] = suit;
                r.sharedMaterials = materials;
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
