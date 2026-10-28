using System.Linq;
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
        /// <summary>The body itself (skin, eyes, brows): hidden under a ready-made character of the pack.</summary>
        public GameObject[] BaseBody = new GameObject[0];

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
        private Transform chestBone, handBone, upperArmBone, lowerArmBone, thighBone, kneeBone;
        // the left arm throws the grenades (the gun stays in the right hand)
        private Transform throwUpper, throwLower, throwHand, throwFingers;
        private Transform[] spineBones, upperArms, lowerArms;
        // every bone as the agent was made (standing at rest): the carried guns are placed on the body in it
        private readonly System.Collections.Generic.Dictionary<Transform, (Vector3 pos, Quaternion rot)> rest =
            new System.Collections.Generic.Dictionary<Transform, (Vector3, Quaternion)>();
        // both weapons of the loadout, carried while not in the hand: a rifle slung across the back, a pistol
        // in a holster on the right thigh; where the wrist holds it and how the hand turns to take it
        private readonly System.Collections.Generic.Dictionary<int, (GameObject copy, Vector3 wrist, Quaternion hand)> carried =
            new System.Collections.Generic.Dictionary<int, (GameObject, Vector3, Quaternion)>();
        private bool bagOn;   // an assault pack on the back: the slung rifle goes outside it
        private float swapStart = -10f;
        private int swapFrom = -1;
        private float throwStart = -10f, stepTimer;
        private int stepIndex;
        private static readonly int CadenceId = Animator.StringToHash("Cadence");

        public Transform Muzzle => weapon >= 0 && Muzzles[weapon] != null ? Muzzles[weapon] : Head;
        public Vector3 Eye => transform.position + Vector3.up * (State == null ? 1.6f : State.Prone ? 0.45f : State.Crouch ? 1.15f : 1.6f);
        public Vector3 Chest => transform.position + Vector3.up * (State == null ? 1.25f : State.Prone ? 0.3f : State.Crouch ? 0.85f : 1.25f);

        private void Awake()
        {
            var named = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                named[t.name.ToLowerInvariant()] = t;
                rest[t] = (t.localPosition, t.localRotation);
            }
            Transform Bone(string n) => named.TryGetValue(n, out var t) ? t : null;
            chestBone = Bone("spine_03");
            handBone = Bone("hand_r");
            upperArmBone = Bone("upperarm_r");
            lowerArmBone = Bone("lowerarm_r");
            thighBone = Bone("thigh_r");
            kneeBone = Bone("calf_r");
            throwUpper = Bone("upperarm_l");
            throwLower = Bone("lowerarm_l");
            throwHand = Bone("hand_l");
            throwFingers = Bone("middle_01_l");
            var spine = new[] { Bone("spine_01"), Bone("spine_02"), Bone("spine_03"), Bone("neck_01") };
            spineBones = spine.All(b => b != null) ? spine : null;
            upperArms = new[] { Bone("upperarm_l"), Bone("upperarm_r") };
            lowerArms = new[] { Bone("lowerarm_l"), Bone("lowerarm_r") };
        }

        /// <summary>
        /// After the animation: turn the chest most of the way and the right hand the rest, so the
        /// barrel points exactly at the aim point and the bullets visibly leave the muzzle towards it.
        /// </summary>
        private void LateUpdate()
        {
            Upright();
            CarryArms();
            ArmSwing();
            AimArms();
            SwapArm();
            ThrowArm();
            HoldSyringe();
            FitBodyHitbox();
        }

        /// <summary>
        /// Standing straight: the aim and run poses bend the back forwards. Each spine bone is turned back so
        /// the back leans forwards no more than a little (more when looking down or sprinting); its twist and
        /// side lean stay as the animation has them. Not crouched or lying down.
        /// </summary>
        private void Upright()
        {
            if (State == null || !Alive || State.Prone || State.Crouch || spineBones == null) return;
            // only on the feet: not while the body is still getting up from the ground or going down to it
            if (Anim != null && (Anim.IsInTransition(0) || !Anim.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"))) return;
            float allowed = 5f + Mathf.Max(0f, -State.Pitch) * 0.6f + (State.MoveY > 1.2f ? 8f : 0f);
            for (int i = 0; i + 1 < spineBones.Length; i++)
            {
                var d = spineBones[i + 1].position - spineBones[i].position;
                float lean = Mathf.Atan2(Vector3.Dot(d, transform.forward), Vector3.Dot(d, transform.up)) * Mathf.Rad2Deg;
                // a back bent far over is not a standing pose: left as it is
                if (lean > allowed && lean < 50f) spineBones[i].rotation = Quaternion.AngleAxis(allowed - lean, transform.right) * spineBones[i].rotation;
            }
        }

        /// <summary>
        /// The gun carried low while running: the elbows swing back no further than a little behind the body,
        /// so the shoulders are not thrown back with every step.
        /// </summary>
        private void ArmSwing()
        {
            if (State == null || !Alive || State.Prone || Ready > 0.99f || upperArms == null) return;
            for (int i = 0; i < upperArms.Length; i++)
            {
                if (upperArms[i] == null || lowerArms[i] == null) continue;
                var d = lowerArms[i].position - upperArms[i].position;
                if (Vector3.Dot(d, transform.up) > 0f) continue;   // a raised arm is not a swinging one
                float back = Mathf.Atan2(-Vector3.Dot(d, transform.forward), -Vector3.Dot(d, transform.up)) * Mathf.Rad2Deg;
                if (back > 12f && back < 90f) upperArms[i].rotation = Quaternion.AngleAxis((12f - back) * (1f - Ready), transform.right) * upperArms[i].rotation;
            }
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
            // in the air too: both hands stay on the gun (the jump's own arms fly wide)
            bool up = State.Aiming || Time.time - lastFire < 1.1f || State.Prone || !State.Grounded;
            Ready = Mathf.MoveTowards(Ready, up ? 1f : 0f, Time.deltaTime * (up ? 7f : 2.5f));
            if (Anim != null && Anim.layerCount > 1) Anim.SetLayerWeight(1, State.Prone ? 0f : Ready);
            if (Ready > 0.99f) return;
            var muzzle = Muzzles[weapon];
            var want = Quaternion.AngleAxis(48f, transform.right) * transform.forward;
            var turn = Limit(Quaternion.FromToRotation(muzzle.forward, want), 100f);
            handBone.rotation = Quaternion.Slerp(Quaternion.identity, turn, 1f - Ready) * handBone.rotation;
        }

        /// <summary>
        /// Grenade throw with the left hand, the gun kept in the right: the left arm winds up out to the side
        /// and behind the head (the elbow bent, the grenade by the ear, the left shoulder turned back), comes
        /// over the top at the release and follows through across the body.
        /// </summary>
        public const float ThrowRelease = 0.35f;
        private const float ThrowLength = 0.75f;
        private GameObject handGrenade;

        /// <summary>The thrower's own throw from the button: the grenade leaves at ThrowRelease.</summary>
        public void StartThrow() => throwStart = Time.time;

        /// <summary>A grenade has left this player's hand (seen by everyone): from the release on, unless already playing.</summary>
        public void PlayThrow()
        {
            if (Time.time - throwStart < ThrowLength) return;
            throwStart = Time.time - ThrowRelease;
        }

        private void ThrowArm()
        {
            float t = Time.time - throwStart;
            bool now = t >= 0f && t <= ThrowLength && throwUpper != null && throwLower != null && throwHand != null && State != null && Alive;
            if (handGrenade == null && throwHand != null) handGrenade = MakeHandGrenade();
            if (handGrenade != null) handGrenade.SetActive(now && visible && t < ThrowRelease + 0.02f);
            if (!now) return;
            // directions in the body's own frame (x out to the left side, y up, z forwards)
            Vector3 Dir(float x, float y, float z) => transform.TransformDirection(new Vector3(-x, y, z).normalized);
            Vector3 cockUpper = Dir(0.6f, 0.25f, -0.55f), cockFore = Dir(0.05f, 1f, -0.25f);
            Vector3 overUpper = Dir(0.3f, 0.75f, 0.45f), overFore = Dir(0.05f, 0.55f, 0.85f);
            Vector3 through = Dir(-0.35f, -0.45f, 0.8f);
            Vector3 upper, fore;
            float w, twist;
            const float swing = 0.12f;
            if (t < ThrowRelease)
            {
                w = Mathf.SmoothStep(0f, 1f, t / ThrowRelease);
                upper = cockUpper; fore = cockFore; twist = -28f * w;
            }
            else if (t < ThrowRelease + swing)
            {
                float u = (t - ThrowRelease) / swing;
                w = 1f;
                upper = u < 0.5f ? Vector3.Slerp(cockUpper, overUpper, u * 2f) : Vector3.Slerp(overUpper, through, u * 2f - 1f);
                fore = u < 0.5f ? Vector3.Slerp(cockFore, overFore, u * 2f) : Vector3.Slerp(overFore, through, u * 2f - 1f);
                twist = Mathf.Lerp(-28f, 20f, u);
            }
            else
            {
                w = 1f - Mathf.SmoothStep(0f, 1f, (t - ThrowRelease - swing) / (ThrowLength - ThrowRelease - swing));
                upper = through; fore = through; twist = 20f * w;
            }
            // the body turns into the throw first (the arm moves with it), then the arm takes its line
            if (chestBone != null && !State.Prone) chestBone.rotation = Quaternion.AngleAxis(twist, transform.up) * chestBone.rotation;
            Point(throwUpper, throwLower.position - throwUpper.position, upper, w);
            Point(throwLower, throwHand.position - throwLower.position, fore, w);
        }

        /// <summary>A bone turned (by w) so that the line it now has points along 'want'.</summary>
        private static void Point(Transform bone, Vector3 now, Vector3 want, float w)
        {
            if (w <= 0f || now.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(now, want), w) * bone.rotation;
        }

        private GameObject MakeHandGrenade()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "HandGrenade";
            g.layer = World.AgentLayer;
            g.transform.SetParent(throwHand, false);
            float k = 1f / Mathf.Max(0.01f, throwHand.lossyScale.x);
            g.transform.localScale = new Vector3(0.08f, 0.1f, 0.08f) * k;
            // in the palm, from the hand's own bones (the same whatever the pose now): towards the knuckles
            var along = throwFingers != null ? throwFingers.position - throwHand.position
                : (throwHand.position - throwLower.position).normalized * 0.09f;
            g.transform.position = throwHand.position + along * 0.7f;
            g.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.22f, 0.26f, 0.16f) };
            g.SetActive(false);
            return g;
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
            // the body and the weapons on the agent layer (not the hitboxes): the street lamps of the
            // maps light only that layer, so a player walking up to a lamp is lit by it
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.GetComponent<Collider>() == null) r.gameObject.layer = World.AgentLayer;
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
            foreach (var drape in GetComponentsInChildren<ClothDrape>()) drape.Settle();
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
            bool bag = Wardrobe.Get(look, Wardrobe.Slot.Bag) == 1 && Wardrobe.Get(look, Wardrobe.Slot.Character) == 0;
            if (bag != bagOn) { bagOn = bag; DropCarried(); ShowWeapons(); }
            // a ready-made character of the pack (when this body has it) in place of the body and its clothes
            int character = Wardrobe.Get(look, Wardrobe.Slot.Character);
            bool ready = false;
            foreach (var p in Parts)
                ready |= character > 0 && p.Object != null && p.Slot == Wardrobe.Slot.Character && ((p.Mask >> character) & 1) == 1;
            foreach (var go in BaseBody)
                if (go != null) go.SetActive(!ready);
            foreach (var part in Parts)
            {
                if (part.Object == null) continue;
                bool on = ((part.Mask >> Wardrobe.Get(look, part.Slot)) & 1) == 1
                          && (!part.HasSecond || ((part.Mask2 >> Wardrobe.Get(look, part.Slot2)) & 1) == 1)
                          && (part.Slot == Wardrobe.Slot.Character ? ready : !ready);
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
            id = Mathf.Clamp(id, 0, Weapons.Count - 1);
            // a change of weapon while alive: the hand puts the gun away (on the back, in the holster) and draws the other
            if (id != weapon && weapon >= 0 && State != null && Alive && Time.time - swapStart > 0.3f)
            {
                swapStart = Time.time;
                swapFrom = weapon;
            }
            weapon = id;
            ShowWeapons();
        }

        /// <summary>
        /// Weapon change: the right hand goes to the gun's place on the body (the holster on the right thigh,
        /// the sling across the back) and leaves it there, moves to the other gun and draws it, and comes up
        /// with it. The wrist goes exactly where it holds the gun, so the gun passes from hand to body in place.
        /// </summary>
        private const float SwapLength = 0.6f, PutAway = 0.22f, Draw = 0.32f;
        private int carriedPrimary = -1, carriedSecondary = -1, shownHand = -2;

        /// <summary>The gun in the hand now: during a change the old one until it is put away, then none, then the new one.</summary>
        private int InHand()
        {
            float t = Time.time - swapStart;
            if (t < 0f || t >= Draw || swapFrom < 0) return weapon;
            return t < PutAway ? swapFrom : -1;
        }

        /// <summary>The gun in the hand, and the loadout's others on the body, as the moment of a change has them.</summary>
        private void ShowWeapons()
        {
            int hand = shownHand = InHand();
            for (int i = 0; i < WeaponModels.Length; i++)
                if (WeaponModels[i] != null) WeaponModels[i].SetActive(i == hand && visible);
            if (State != null && (State.Primary != carriedPrimary || State.Secondary != carriedSecondary))
            {
                DropCarried();
                carriedPrimary = State.Primary;
                carriedSecondary = State.Secondary;
                foreach (int id in new[] { carriedPrimary, carriedSecondary })
                    if (!carried.ContainsKey(id) && id < WeaponModels.Length && WeaponModels[id] != null && id < Muzzles.Length && Muzzles[id] != null)
                        carried[id] = MakeCarried(id);
            }
            foreach (var c in carried)
                if (c.Value.copy != null) c.Value.copy.SetActive(visible && c.Key != hand);
        }

        private void DropCarried()
        {
            foreach (var c in carried.Values)
                if (c.copy != null) Destroy(c.copy);
            carried.Clear();
            carriedPrimary = carriedSecondary = -1;
        }

        /// <summary>
        /// A copy of a gun of the loadout on the body: pistols in a holster on the right thigh, rifles slung
        /// across the back. Placed as the body stands at rest, whatever the pose now (crouched, lying down,
        /// mid-step), then it moves with its bone and stays where it belongs.
        /// </summary>
        private (GameObject, Vector3, Quaternion) MakeCarried(int id)
        {
            var src = WeaponModels[id];
            bool pistol = !Weapons.Get(id).Primary;
            // a rifle rides on the middle of the back: the chest bends with the aim (up, down) and would
            // swing a rifle on it into the hips and the back
            var back = spineBones != null ? spineBones[1] : chestBone;
            var bone = pistol ? (thighBone != null ? thighBone : transform) : (back != null ? back : transform);
            bool was = src.activeSelf;
            src.SetActive(true);
            var copy = Instantiate(src, src.transform.position, src.transform.rotation, src.transform.parent);
            src.SetActive(was);
            copy.name = "Carried_" + id;
            // only the look of it: no scripts, flashes, sounds or colliders
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
                if (c != null && !(c is Transform || c is MeshFilter || c is MeshRenderer)) Destroy(c);
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = World.AgentLayer;
                if (r.name.ToLowerInvariant().Contains("flash")) r.enabled = false;
            }
            // which way its barrel points, where its middle is and where the wrist holds it, in the gun's own space
            var muzzleDir = src.transform.InverseTransformDirection(Muzzles[id].forward);
            var bounds = new Bounds(Muzzles[id].position, Vector3.zero);
            foreach (var r in src.GetComponentsInChildren<MeshRenderer>(true)) bounds.Encapsulate(r.bounds);
            var middle = src.transform.InverseTransformPoint(bounds.center);
            var wrist = handBone != null ? src.transform.InverseTransformPoint(handBone.position) : middle;
            var hand = handBone != null ? Quaternion.Inverse(src.transform.rotation) * handBone.rotation : Quaternion.identity;
            var gunUp = Vector3.ProjectOnPlane(Vector3.up, muzzleDir).normalized;
            var fromGun = Quaternion.Inverse(Quaternion.LookRotation(muzzleDir, gunUp));
            var posed = AtRest(bone);
            Quaternion want;
            Vector3 at;
            if (pistol)
            {
                // in the holster on the outside of the right thigh: the muzzle down along the thigh, the grip
                // up and back by the hip, ready for the right hand
                var hip = bone.position;
                var axis = kneeBone != null ? (kneeBone.position - hip).normalized : -transform.up;
                var outward = Vector3.ProjectOnPlane(transform.right, axis).normalized;
                want = Quaternion.LookRotation(axis, Vector3.ProjectOnPlane(transform.forward, axis)) * fromGun;
                at = hip + axis * 0.1f + outward * 0.125f - transform.forward * 0.015f;
            }
            else
            {
                // slung as a sling holds it: diagonally across the back, the muzzle up over the left shoulder,
                // the butt at the right hip, lying flat with its side on the back (the magazine along the
                // back, not sticking out), close behind the spine, or outside the pack when one is worn
                var dir = (transform.up * 0.8f - transform.right * 0.6f).normalized;
                want = Quaternion.LookRotation(dir, Vector3.Cross(dir, -transform.forward)) * fromGun;
                at = (chestBone != null ? chestBone.position : transform.position + transform.up * 1.3f) - transform.up * 0.12f
                     - transform.forward * (bagOn ? 0.31f : 0.19f);
            }
            copy.transform.SetParent(bone, true);
            copy.transform.rotation = want;
            copy.transform.position += at - copy.transform.TransformPoint(middle);
            foreach (var (t, pos, rot) in posed) { t.localPosition = pos; t.localRotation = rot; }
            return (copy, wrist, hand);
        }

        /// <summary>The bone and those above it put back as the agent was made (standing at rest); returns their pose now, to put back after.</summary>
        private System.Collections.Generic.List<(Transform, Vector3, Quaternion)> AtRest(Transform bone)
        {
            var now = new System.Collections.Generic.List<(Transform, Vector3, Quaternion)>();
            for (var t = bone; t != null && t != transform; t = t.parent)
            {
                if (!rest.TryGetValue(t, out var r)) continue;
                now.Add((t, t.localPosition, t.localRotation));
                t.localPosition = r.pos;
                t.localRotation = r.rot;
            }
            return now;
        }

        private void SwapArm()
        {
            if (InHand() != shownHand || (State != null && (State.Primary != carriedPrimary || State.Secondary != carriedSecondary))) ShowWeapons();
            float t = Time.time - swapStart;
            if (t < 0f || t > SwapLength || upperArmBone == null || lowerArmBone == null || handBone == null || State == null || !Alive) return;
            Spot(swapFrom, out var fromAt, out var fromTurn);
            Spot(weapon, out var toAt, out var toTurn);
            Vector3 at;
            Quaternion turn;
            float w;
            if (t < PutAway)
            {
                w = Mathf.SmoothStep(0f, 1f, t / PutAway);
                at = fromAt; turn = fromTurn;
            }
            else if (t < Draw)
            {
                float u = Mathf.SmoothStep(0f, 1f, (t - PutAway) / (Draw - PutAway));
                w = 1f;
                at = Vector3.Lerp(fromAt, toAt, u); turn = Quaternion.Slerp(fromTurn, toTurn, u);
            }
            else
            {
                w = 1f - Mathf.SmoothStep(0f, 1f, (t - Draw) / (SwapLength - Draw));
                at = toAt; turn = toTurn;
            }
            Reach(at, w);
            handBone.rotation = Quaternion.Slerp(handBone.rotation, turn, w);
        }

        /// <summary>Where the wrist goes to take a gun from the body and how the hand turns: from its carried copy, else low by the right hip.</summary>
        private void Spot(int id, out Vector3 at, out Quaternion turn)
        {
            if (carried.TryGetValue(id, out var c) && c.copy != null)
            {
                at = c.copy.transform.TransformPoint(c.wrist);
                turn = c.copy.transform.rotation * c.hand;
                return;
            }
            at = transform.position + transform.up * 0.95f + transform.right * 0.28f + transform.forward * 0.05f;
            turn = handBone.rotation;
        }

        /// <summary>The right arm reaching (by w) so the wrist comes to 'target', the elbow out to the side and back.</summary>
        private void Reach(Vector3 target, float w)
        {
            if (w <= 0f) return;
            Vector3 a = upperArmBone.position, b = lowerArmBone.position, c = handBone.position;
            float ab = (b - a).magnitude, bc = (c - b).magnitude;
            if (ab < 1e-4f || bc < 1e-4f) return;
            var dir = (target - a).normalized;
            float d = Mathf.Clamp((target - a).magnitude, Mathf.Abs(ab - bc) + 0.01f, (ab + bc) * 0.999f);
            float cos = Mathf.Clamp((ab * ab + d * d - bc * bc) / (2f * ab * d), -1f, 1f);
            var pole = Vector3.ProjectOnPlane(transform.right * 0.6f - transform.forward * 0.8f, dir).normalized;
            var elbow = a + (dir * cos + pole * Mathf.Sqrt(1f - cos * cos)) * ab;
            Point(upperArmBone, b - a, elbow - a, w);
            var wrist = Vector3.Lerp(c, a + dir * d, w);
            Point(lowerArmBone, handBone.position - lowerArmBone.position, wrist - lowerArmBone.position, 1f);
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
            // a real walk at half the stick (eight directions); running takes short quick steps (the jog played
            // faster for the same speed over the ground: shorter strides), sprinting a little longer ones
            Anim.SetFloat(CadenceId, s.Crouch || s.Prone ? 1f : animMoveY > 1.2f ? 1.25f : 1.05f + 0.37f * Mathf.Clamp01((move - 0.55f) / 0.45f));
            Steps(s, move);
            Anim.SetBool(CrouchId, s.Crouch);
            Anim.SetBool(ProneId, s.Prone);
            // never left in the crawl once up again (a transition missed): straight back to the feet
            if (!s.Prone && s.Alive && !Anim.IsInTransition(0) && Anim.GetCurrentAnimatorStateInfo(0).IsName("Prone")) Anim.CrossFade("Locomotion", 0.25f, 0);
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

        private float lastHealth = 100f, nextPain, nextBreath;

        /// <summary>A cry when hit and when killed (Michel Baradari, CC-BY 3.0), heard around the body.</summary>
        private void Voice(PlayerState s)
        {
            if (s.Alive && s.Health < lastHealth - 0.5f && Time.time >= nextPain && Sfx.Count("pain_") > 0)
            {
                nextPain = Time.time + 0.6f;
                Sfx.PlayAt("pain_" + Random.Range(0, Sfx.Count("pain_")), Chest, IsLocal ? 0.55f : 0.8f, 1f);
            }
            if (!s.Alive && Alive && Sfx.Count("death_") > 0)
                Sfx.PlayAt("death_" + Random.Range(0, Sfx.Count("death_")), Chest, 0.9f, 1f);
            // badly hurt: one's own heavy breathing until healed (or dead)
            if (IsLocal && s.Alive && s.Health < 30f && Time.time >= nextBreath && Sfx.Has("breath_0"))
            {
                nextBreath = Time.time + 5.4f;
                Sfx.Play("breath_0", 0.4f, 1f);
            }
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
