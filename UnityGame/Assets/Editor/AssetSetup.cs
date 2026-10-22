using System.Collections.Generic;
using System.IO;
using System.Linq;
using UA;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Turns the downloaded CC0 assets into game assets: city materials, the animated agent prefab
/// (mannequin + gear + six weapons + hitboxes + animator) and the effect materials.
/// </summary>
public static class AssetSetup
{
    public const string UalPath = "Assets/ThirdParty/UAL/UAL1.fbx";
    // the second library, same skeleton: walks in eight directions
    public const string Ual2Path = "Assets/ThirdParty/UAL/UAL2.fbx";
    public const string FemalePath = "Assets/ThirdParty/UAL/Mannequin_F.fbx";
    // Quaternius Universal Base Characters (realistic skin, face, eyes, hair), same skeleton as the UAL
    public const string UbcMale = "Assets/ThirdParty/UBC/Superhero_Male_FullBody.fbx";
    public const string UbcFemale = "Assets/ThirdParty/UBC/Superhero_Female_FullBody.fbx";
    public const string UbcHair = "Assets/ThirdParty/UBC/Hair";
    public const string UbcTextures = "Assets/ThirdParty/UBC/Textures";
    public const string GunsPath = "Assets/ThirdParty/Guns";
    public const string CityModels = "Assets/ThirdParty/City/Models";
    public const string CityTextures = "Assets/ThirdParty/City/Textures";
    public const string Generated = "Assets/Generated";

    public static readonly Dictionary<string, Material> CityMaterials = new Dictionary<string, Material>();
    private static readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();

    public static void Run()
    {
        Folder(Generated);
        Folder(Generated + "/Materials");
        Folder(Generated + "/Textures");
        Folder("Assets/Resources");
        Folder("Assets/Resources/Materials");
        CreateCityMaterials();
        RemapCityModels();
        Nature.Setup();
        SetupAnimationClips();
        var controller = BuildAnimator();
        BuildEffectMaterials();
        bool realistic = AssetDatabase.LoadAssetAtPath<GameObject>(UbcMale) != null;
        Debug.Log("[Setup] characters: " + (realistic ? "Universal Base Characters" : "mannequins"));
        BuildAgent(controller, realistic ? UbcMale : UalPath, "Agent");
        if (realistic && AssetDatabase.LoadAssetAtPath<GameObject>(UbcFemale) != null) BuildAgent(controller, UbcFemale, "Agent_F");
        else if (AssetDatabase.LoadAssetAtPath<GameObject>(FemalePath) != null) BuildAgent(controller, FemalePath, "Agent_F");
        else AssetDatabase.DeleteAsset("Assets/Resources/Agent_F.prefab");
        BuildGunLineup();
        BuildWeaponPrefabs();
        AssetDatabase.SaveAssets();
    }

    public static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ materials

    public static Material Mat(string name, Color color, float smoothness = 0.3f, float metallic = 0f, Texture2D albedo = null,
        Texture2D normal = null, Texture2D metallicSmoothness = null, Texture2D emission = null, Color? emissionColor = null)
    {
        string path = $"{Generated}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = m == null;
        if (create) m = new Material(Shader.Find("Standard"));
        m.name = name;
        m.color = color;
        m.SetFloat("_Glossiness", smoothness);
        m.SetFloat("_Metallic", metallic);
        if (albedo != null) m.mainTexture = albedo;
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
        }
        if (metallicSmoothness != null)
        {
            m.SetTexture("_MetallicGlossMap", metallicSmoothness);
            m.SetFloat("_GlossMapScale", smoothness);
            m.EnableKeyword("_METALLICGLOSSMAP");
        }
        if (emission != null || emissionColor.HasValue)
        {
            if (emission != null) m.SetTexture("_EmissionMap", emission);
            m.SetColor("_EmissionColor", emissionColor ?? Color.white);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        m.enableInstancing = true;
        if (create) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Simple colored material, cached by name (procedural props, weapons, gear).</summary>
    /// <summary>Moving water (UA/Water): deep and shallow colours, wave size (1 = sea, more for small pools).</summary>
    public static Material WaterMat(string name, Color deep, Color shallow, float scale = 1f, float strength = 0.3f)
    {
        if (materialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        string path = $"{Generated}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("UA/Water");
        if (m == null)
        {
            m = new Material(shader ?? Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        else if (shader != null) m.shader = shader;
        m.SetColor("_Deep", deep);
        m.SetColor("_Shallow", shallow);
        m.SetFloat("_Scale", scale);
        m.SetFloat("_Strength", strength);
        EditorUtility.SetDirty(m);
        return materialCache[name] = m;
    }

    public static Material Solid(string name, Color color, float smoothness = 0.3f, float metallic = 0f)
    {
        if (materialCache.TryGetValue(name, out var m) && m != null) return m;
        return materialCache[name] = Mat(name, color, smoothness, metallic);
    }

    private static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{CityTextures}/{file}.png");

    private static void CreateCityMaterials()
    {
        CityMaterials.Clear();
        void Kit(string name, string set, Color tint, float smooth = 0.8f, string albedoSet = null)
        {
            CityMaterials[name] = Mat(name, tint, smooth, 0f, Tex($"T_{albedoSet ?? set}_BaseColor"), Tex($"T_{set}_Normal"), Tex($"T_{set}_MS"));
        }
        Kit("MI_RedBrick", "RedBrick", Color.white);
        Kit("MI_RedBrick_Pale", "RedBrick", new Color(1f, 0.9f, 0.82f));
        Kit("MI_Trim", "Trim", Color.white);
        Kit("MI_Trim_MetalConcrete", "MetalConcrete", Color.white);
        Kit("MI_Trim_Dark", "Trim", new Color(0.38f, 0.38f, 0.4f));
        Kit("MI_Trim_Green", "Trim", new Color(0.42f, 0.6f, 0.46f));
        Kit("MI_InteriorWall", "Concrete", new Color(0.86f, 0.83f, 0.78f));
        Kit("MI_InteriorFloor", "MarbleFloor", Color.white);
        Kit("MI_InteriorRoof", "Concrete", Color.white);
        Kit("MI_Concrete", "Concrete", Color.white);
        Kit("MI_Asphalt", "Concrete", Color.white, 0.6f, "Concrete_Asphalt");
        Kit("MI_Dirt", "Dirt", Color.white);
        Kit("MI_Ornaments", "Ornaments", Color.white);
        Kit("MI_Roof_Slate", "RoofSlate", Color.white);
        CityMaterials["MI_StreetDecals"] = Mat("MI_StreetDecals", Color.white, 0.3f, 0f, Tex("T_Street_Decals"));
        CityMaterials["MI_Glass"] = Mat("MI_Glass", new Color(0.12f, 0.16f, 0.2f), 0.95f, 0.6f);
        string[] interiors = { "T_lit_interior_1", "T_lit_interior_2", "T_dark_interior", "T_lit_interior_1", "T_lit_interior_2" };
        string[] names = { "MI_FakeInterior", "MI_FakeInterior_1", "MI_FakeInterior_2", "MI_FakeInterior_3", "MI_FakeInterior_4" };
        for (int i = 0; i < names.Length; i++)
        {
            var t = Tex(interiors[i]);
            CityMaterials[names[i]] = Mat(names[i], new Color(0.7f, 0.7f, 0.7f), 0.9f, 0f, t, null, null, t, new Color(0.35f, 0.32f, 0.28f));
        }
    }

    private static void RemapCityModels()
    {
        if (!AssetDatabase.IsValidFolder(CityModels)) { Debug.LogWarning("[Setup] city models missing"); return; }
        int changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CityModels }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var existing = importer.GetExternalObjectMap();
            bool dirty = false;
            foreach (var pair in CityMaterials)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
                if (existing.TryGetValue(id, out var current) && current == pair.Value) continue;
                importer.AddRemap(id, pair.Value);
                dirty = true;
            }
            if (!dirty) continue;
            importer.SaveAndReimport();
            changed++;
        }
        Debug.Log($"[Setup] city models remapped: {changed}");
    }

    // ------------------------------------------------------------------ animations

    private static readonly string[] WantedClips =
    {
        "Idle_Loop", "Jog_Fwd_Loop", "Jog_Bwd_Loop", "Jog_Left_Loop", "Jog_Right_Loop", "Jog_Fwd_L_Loop", "Jog_Fwd_R_Loop",
        "Jog_Bwd_L_Loop", "Jog_Bwd_R_Loop", "Sprint_Loop", "Crouch_Idle_Loop", "Crouch_Fwd_Loop", "Crouch_Bwd_Loop",
        "Crouch_Left_Loop", "Crouch_Right_Loop", "Jump_Loop", "Death01", "Pistol_Idle_Loop", "Pistol_Aim_Down",
        "Pistol_Aim_Neutral", "Pistol_Aim_Up", "Pistol_Shoot", "Pistol_Reload", "Hit_Chest",
        "Crawl_Idle_Loop", "Crawl_Fwd_Loop", "Crawl_Bwd_Loop", "Crawl_Left_Loop", "Crawl_Right_Loop",
        "Walk_Loop",
    };

    private static readonly string[] WantedClips2 =
    {
        "Walk_Fwd_Loop", "Walk_Bwd_Loop", "Walk_L_Loop", "Walk_R_Loop", "Walk_Fwd_L_Loop", "Walk_Fwd_R_Loop", "Walk_Bwd_L_Loop", "Walk_Bwd_R_Loop",
    };

    private static void SetupAnimationClips()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(UalPath);
        if (importer == null) throw new System.Exception("UAL1.fbx is missing - the asset download step failed");
        SetupClips(importer, WantedClips);
        var second = (ModelImporter)AssetImporter.GetAtPath(Ual2Path);
        if (second != null) SetupClips(second, WantedClips2);
        else Debug.LogWarning("[Setup] UAL2.fbx is missing: no directional walks");
    }

    private static void SetupClips(ModelImporter importer, string[] wanted)
    {
        var defaults = importer.defaultClipAnimations;
        var clips = new List<ModelImporterClipAnimation>();
        foreach (var name in wanted)
        {
            var clip = defaults.FirstOrDefault(c => c.name == name || c.takeName == name || c.name.EndsWith("|" + name));
            if (clip == null) { Debug.LogWarning("[Setup] clip not found: " + name); continue; }
            clip.name = name;
            clip.loopTime = name.EndsWith("_Loop") || name.StartsWith("Pistol_Aim");
            clip.lockRootHeightY = false;
            clips.Add(clip);
        }
        var current = importer.clipAnimations;
        if (current.Length == clips.Count && current.Select(c => c.name).SequenceEqual(clips.Select(c => c.name))) return;
        importer.clipAnimations = clips.ToArray();
        importer.SaveAndReimport();
    }

    private static AnimationClip Clip(string name)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(UalPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name)
            ?? AssetDatabase.LoadAllAssetsAtPath(Ual2Path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
        if (clip == null) Debug.LogWarning("[Setup] missing clip " + name);
        return clip;
    }

    private static AnimatorController BuildAnimator()
    {
        string path = Generated + "/Agent.controller";
        AssetDatabase.DeleteAsset(path);
        var c = AnimatorController.CreateAnimatorControllerAtPath(path);
        c.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        c.AddParameter("MoveY", AnimatorControllerParameterType.Float);
        c.AddParameter("Pitch", AnimatorControllerParameterType.Float);
        c.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
        c.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        c.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        c.AddParameter("Prone", AnimatorControllerParameterType.Bool);
        c.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Cadence", AnimatorControllerParameterType.Float);
        var parameters = c.parameters;
        foreach (var p in parameters)
        {
            if (p.name == "Grounded") p.defaultBool = true;
            if (p.name == "Cadence") p.defaultFloat = 1f;
        }
        c.parameters = parameters;

        var sm = c.layers[0].stateMachine;
        var loco = c.CreateBlendTreeInController("Locomotion", out var tree, 0);
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveY";
        void Add(BlendTree t, string clip, float x, float y) { var a = Clip(clip); if (a != null) t.AddChild(a, new Vector2(x, y)); }
        Add(tree, "Idle_Loop", 0, 0);
        // a real walk for slow steps (half the stick) in every direction, the jog for running
        if (Clip("Walk_Fwd_Loop") != null)
        {
            Add(tree, "Walk_Fwd_Loop", 0, 0.5f);
            Add(tree, "Walk_Bwd_Loop", 0, -0.5f);
            Add(tree, "Walk_L_Loop", -0.5f, 0);
            Add(tree, "Walk_R_Loop", 0.5f, 0);
            Add(tree, "Walk_Fwd_L_Loop", -0.35f, 0.35f);
            Add(tree, "Walk_Fwd_R_Loop", 0.35f, 0.35f);
            Add(tree, "Walk_Bwd_L_Loop", -0.35f, -0.35f);
            Add(tree, "Walk_Bwd_R_Loop", 0.35f, -0.35f);
        }
        else Add(tree, "Walk_Loop", 0, 0.5f);
        Add(tree, "Jog_Fwd_Loop", 0, 1);
        Add(tree, "Jog_Bwd_Loop", 0, -1);
        Add(tree, "Jog_Left_Loop", -1, 0);
        Add(tree, "Jog_Right_Loop", 1, 0);
        Add(tree, "Jog_Fwd_L_Loop", -0.7f, 0.7f);
        Add(tree, "Jog_Fwd_R_Loop", 0.7f, 0.7f);
        Add(tree, "Jog_Bwd_L_Loop", -0.7f, -0.7f);
        Add(tree, "Jog_Bwd_R_Loop", 0.7f, -0.7f);
        Add(tree, "Sprint_Loop", 0, 2);
        sm.defaultState = loco;
        // step rate set by the agent: running takes shorter, quicker steps
        loco.speedParameterActive = true;
        loco.speedParameter = "Cadence";

        var crouch = c.CreateBlendTreeInController("Crouch", out var crouchTree, 0);
        crouchTree.blendType = BlendTreeType.FreeformDirectional2D;
        crouchTree.blendParameter = "MoveX";
        crouchTree.blendParameterY = "MoveY";
        Add(crouchTree, "Crouch_Idle_Loop", 0, 0);
        Add(crouchTree, "Crouch_Fwd_Loop", 0, 1);
        Add(crouchTree, "Crouch_Bwd_Loop", 0, -1);
        Add(crouchTree, "Crouch_Left_Loop", -1, 0);
        Add(crouchTree, "Crouch_Right_Loop", 1, 0);

        var prone = c.CreateBlendTreeInController("Prone", out var proneTree, 0);
        proneTree.blendType = BlendTreeType.FreeformDirectional2D;
        proneTree.blendParameter = "MoveX";
        proneTree.blendParameterY = "MoveY";
        Add(proneTree, "Crawl_Idle_Loop", 0, 0);
        Add(proneTree, "Crawl_Fwd_Loop", 0, 1);
        Add(proneTree, "Crawl_Bwd_Loop", 0, -1);
        Add(proneTree, "Crawl_Left_Loop", -1, 0);
        Add(proneTree, "Crawl_Right_Loop", 1, 0);

        var air = sm.AddState("Air");
        air.motion = Clip("Jump_Loop");
        var dead = sm.AddState("Dead");
        dead.motion = Clip("Death01");

        void Link(AnimatorState from, AnimatorState to, string param, bool value, float duration = 0.15f)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
        }
        Link(loco, crouch, "Crouch", true);
        Link(crouch, loco, "Crouch", false);
        Link(loco, air, "Grounded", false, 0.1f);
        Link(crouch, air, "Grounded", false, 0.1f);
        Link(air, loco, "Grounded", true, 0.1f);
        Link(dead, loco, "Dead", false, 0.1f);
        Link(loco, prone, "Prone", true, 0.3f);
        Link(crouch, prone, "Prone", true, 0.3f);
        Link(prone, loco, "Prone", false, 0.3f);
        var toDead = sm.AddAnyStateTransition(dead);
        toDead.hasExitTime = false;
        toDead.duration = 0.1f;
        toDead.canTransitionToSelf = false;
        toDead.AddCondition(AnimatorConditionMode.If, 0, "Dead");

        // upper body: aim pose blended by pitch, shoot and reload on top of the legs
        c.AddLayer("Arms");
        var layers = c.layers;
        layers[1].avatarMask = BuildUpperBodyMask();
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        c.layers = layers;
        var aim = c.CreateBlendTreeInController("Aim", out var aimTree, 1);
        aimTree.blendType = BlendTreeType.Simple1D;
        aimTree.blendParameter = "Pitch";
        aimTree.useAutomaticThresholds = false;
        var down = Clip("Pistol_Aim_Down"); var neutral = Clip("Pistol_Aim_Neutral"); var up = Clip("Pistol_Aim_Up");
        if (down != null) aimTree.AddChild(down, -1f);
        if (neutral != null) aimTree.AddChild(neutral, 0f);
        if (up != null) aimTree.AddChild(up, 1f);
        var armSm = c.layers[1].stateMachine;
        armSm.defaultState = aim;
        var shoot = armSm.AddState("Shoot");
        shoot.motion = Clip("Pistol_Shoot");
        shoot.speed = 1.6f;
        var reload = armSm.AddState("Reload");
        reload.motion = Clip("Pistol_Reload");
        var anyShoot = armSm.AddAnyStateTransition(shoot);
        anyShoot.hasExitTime = false;
        anyShoot.duration = 0.03f;
        anyShoot.canTransitionToSelf = true;
        anyShoot.AddCondition(AnimatorConditionMode.If, 0, "Fire");
        var anyReload = armSm.AddAnyStateTransition(reload);
        anyReload.hasExitTime = false;
        anyReload.duration = 0.1f;
        anyReload.AddCondition(AnimatorConditionMode.If, 0, "Reload");
        foreach (var s in new[] { shoot, reload })
        {
            var back = s.AddTransition(aim);
            back.hasExitTime = true;
            back.exitTime = s == shoot ? 0.6f : 0.9f;
            back.duration = 0.15f;
        }
        EditorUtility.SetDirty(c);
        return c;
    }

    private static AvatarMask BuildUpperBodyMask()
    {
        string path = Generated + "/UpperBody.mask";
        AssetDatabase.DeleteAsset(path);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(UalPath);
        var mask = new AvatarMask();
        mask.AddTransformPath(model.transform, true);
        for (int i = 0; i < mask.transformCount; i++)
        {
            string p = mask.GetTransformPath(i);
            mask.SetTransformActive(i, p.Contains("spine_02") || p.Contains("spine_03"));
        }
        AssetDatabase.CreateAsset(mask, path);
        return mask;
    }

    // ------------------------------------------------------------------ effects

    private static Texture2D SaveTexture(string name, int size, System.Func<float, float, Color> pixel)
    {
        string path = $"{Generated}/Textures/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, pixel((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material EffectMaterial(string name, string shader, Texture2D texture, Color tint)
    {
        string path = $"Assets/Resources/Materials/{name}.mat";
        AssetDatabase.DeleteAsset(path);
        var s = Shader.Find(shader) ?? Shader.Find("Sprites/Default");
        var m = new Material(s) { mainTexture = texture };
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
        if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static void BuildEffectMaterials()
    {
        var dot = SaveTexture("SoftDot", 64, (x, y) => { float d = Mathf.Sqrt(x * x + y * y); float a = Mathf.Clamp01(1f - d); return new Color(1, 1, 1, a * a); });
        var star = SaveTexture("Flash", 128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float angle = Mathf.Atan2(y, x);
            float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 3f)), 8f) * Mathf.Clamp01(1f - d) * 0.9f;
            float core = Mathf.Clamp01(1f - d * 2.2f);
            float a = Mathf.Clamp01(core + rays);
            return new Color(1f, 0.85f + 0.15f * core, 0.55f + 0.45f * core, a);
        });
        var white = SaveTexture("White", 8, (x, y) => Color.white);
        EffectMaterial("Tracer", "Legacy Shaders/Particles/Additive", white, new Color(0.5f, 0.45f, 0.35f, 0.5f));
        EffectMaterial("Flash", "Legacy Shaders/Particles/Additive", star, new Color(0.5f, 0.45f, 0.4f, 0.5f));
        EffectMaterial("Spark", "Legacy Shaders/Particles/Additive", dot, new Color(0.5f, 0.45f, 0.35f, 0.5f));
        EffectMaterial("Dust", "Legacy Shaders/Particles/Alpha Blended", dot, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EffectMaterial("Blood", "Legacy Shaders/Particles/Alpha Blended", dot, new Color(0.5f, 0.5f, 0.5f, 0.5f));
    }

    // ------------------------------------------------------------------ agent prefab

    private static Transform Find(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase));

    private static GameObject Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 size, Material material, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    /// <summary>A cylinder along Z (length, radius) for barrels, tubes and scopes.</summary>
    private static GameObject Tube(Transform parent, Vector3 center, float length, float radius, Material material) =>
        Part(parent, PrimitiveType.Cylinder, center, new Vector3(radius * 2, length / 2, radius * 2), material, new Vector3(90, 0, 0));

    // Quaternius Ultimate Gun Pack model used for each weapon, its length in meters and where the hand
    // holds it (fraction of the length from the back, fraction of the height from the bottom)
    // (several candidates per weapon: the first model found in the pack is used)
    public static readonly string[][] GunModels =
    {
        new[] { "AssaultRifle_2" }, new[] { "AssaultRifle2_1" }, new[] { "Shotgun_1" }, new[] { "SniperRifle_1" },
        new[] { "Pistol_1" }, new[] { "Revolver_1" }, new[] { "SubmachineGun_2", "SubmachineGun_1" }, new[] { "SniperRifle_4", "SniperRifle_2", "SniperRifle_1" },
        new[] { "Assets/Weapons/PKM/PKM.fbx" },   // PKM machine gun model brought by the player (barrel already along +Z)
    };
    private static readonly float[] GunLengths = { 0.88f, 0.84f, 0.98f, 1.15f, 0.2f, 0.27f, 0.55f, 1.12f, 1.19f };
    private static readonly Vector2[] GunGrips =
    {
        new Vector2(0.36f, 0.35f), new Vector2(0.36f, 0.35f), new Vector2(0.32f, 0.35f), new Vector2(0.3f, 0.35f),
        new Vector2(0.2f, 0.45f), new Vector2(0.2f, 0.45f), new Vector2(0.4f, 0.35f), new Vector2(0.3f, 0.35f),
        new Vector2(0.34f, 0.3f),
    };

    private static readonly Dictionary<string, Material> gunMaterials = new Dictionary<string, Material>();

    /// <summary>Gun steel or wood with real (ambientCG) detail textures, keeping the model's own colour.</summary>
    private static Material GunMaterial(Material source)
    {
        var c = source != null && source.HasProperty("_Color") ? source.color : new Color(0.15f, 0.15f, 0.15f);
        string sourceName = source != null ? source.name : "";
        // models that name their materials: wood without a colour of its own, copper / brass cartridges
        if (sourceName.Contains("Wood") && Mathf.Abs(c.r - c.b) < 0.05f) c = new Color(0.5f, 0.28f, 0.15f);
        bool brass = sourceName.Contains("Copper") || sourceName.Contains("Brass");
        if (brass) c = new Color(0.8f, 0.55f, 0.25f);
        bool wood = !brass && c.r > c.g && c.g > c.b && c.r - c.b > 0.12f;
        string key = (wood ? "Wood_" : "Metal_") + ColorUtility.ToHtmlStringRGB(c);
        if (gunMaterials.TryGetValue(key, out var m) && m != null) return m;
        string set = wood ? "Wood049" : "Metal032";
        Texture2D T(string kind) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortBuilder.Textures}/{set}_{kind}");
        var tint = wood ? new Color(Mathf.Max(c.r, 0.35f) * 1.3f, Mathf.Max(c.g, 0.2f) * 1.3f, Mathf.Max(c.b, 0.12f) * 1.3f) : new Color(c.r * 0.9f + 0.03f, c.g * 0.9f + 0.03f, c.b * 0.9f + 0.03f);
        return gunMaterials[key] = Mat("Gun_" + key, tint, wood ? 0.45f : 0.75f, wood ? 0f : 0.8f, T("BaseColor.jpg"), T("Normal.jpg"), wood ? null : T("MS.png"));
    }

    /// <summary>
    /// The pack's guns have no texture coordinates worth using: give every mesh box-projected UVs in
    /// meters (from the vertex normals) so the detail textures keep the same scale on every gun.
    /// </summary>
    private static void TextureGun(GameObject holder, string key)
    {
        Folder(Generated + "/GunMeshes");
        foreach (var filter in holder.GetComponentsInChildren<MeshFilter>())
        {
            var src = filter.sharedMesh;
            if (src == null) continue;
            // only safe characters in the file name (some models name their parts like "762*54r.001")
            // (with a short checksum of the real name, so parts named in other scripts stay apart)
            string original = $"{key}_{filter.name}_{src.name}";
            uint sum = 2166136261;
            foreach (char ch in original) sum = (sum ^ ch) * 16777619;
            string file = System.Text.RegularExpressions.Regex.Replace(original, @"[^A-Za-z0-9_.-]", "_") + "_" + sum.ToString("x8");
            string path = $"{Generated}/GunMeshes/{file}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                Vector3[] vertices, normals;
                try { vertices = src.vertices; normals = src.normals; }
                catch (System.Exception e) { Debug.LogWarning($"[Setup] gun mesh {src.name} not readable: {e.Message}"); continue; }
                if (normals == null || normals.Length != vertices.Length) { src.RecalculateNormals(); normals = src.normals; }
                var toHolder = holder.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var uv = new Vector2[vertices.Length];
                const float tile = 0.18f; // meters per texture repeat
                for (int i = 0; i < vertices.Length; i++)
                {
                    var p = toHolder.MultiplyPoint3x4(vertices[i]);
                    var n = toHolder.MultiplyVector(normals[i]);
                    n = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                    uv[i] = (n.x >= n.y && n.x >= n.z ? new Vector2(p.z, p.y) : n.y >= n.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y)) / tile;
                }
                mesh = Object.Instantiate(src);
                mesh.uv = uv;
                mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, path);
            }
            filter.sharedMesh = mesh;
            var r = filter.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterials = r.sharedMaterials.Select(GunMaterial).ToArray();
        }
    }

    /// <summary>ACOG style sight on a rail, for rifles whose model has no optic.</summary>
    private static void AddScope(Transform gun, float top, float z)
    {
        var black = Solid("ScopeBody", new Color(0.06f, 0.06f, 0.065f), 0.55f, 0.4f);
        var lens = Solid("ScopeLens", new Color(0.15f, 0.32f, 0.38f), 0.97f, 0.6f);
        Part(gun, PrimitiveType.Cube, new Vector3(0, top + 0.012f, z), new Vector3(0.03f, 0.024f, 0.09f), black);
        Tube(gun, new Vector3(0, top + 0.05f, z), 0.15f, 0.022f, black);
        Tube(gun, new Vector3(0, top + 0.05f, z + 0.085f), 0.03f, 0.027f, black);
        Tube(gun, new Vector3(0, top + 0.05f, z - 0.08f), 0.03f, 0.024f, black);
        Part(gun, PrimitiveType.Cylinder, new Vector3(0, top + 0.05f, z + 0.101f), new Vector3(0.048f, 0.002f, 0.048f), lens, new Vector3(90, 0, 0));
        Part(gun, PrimitiveType.Cylinder, new Vector3(0, top + 0.05f, z - 0.096f), new Vector3(0.04f, 0.002f, 0.04f), lens, new Vector3(90, 0, 0));
    }

    /// <summary>Instance of a gun model from the pack, barrel along +Z, scaled to the given length.</summary>
    public static GameObject GunModel(string name, float length, bool textured = false)
    {
        // a full asset path for models outside the pack (their barrel already points to +Z)
        bool own = name.EndsWith(".fbx");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(own ? name : $"{GunsPath}/{name}.fbx");
        if (model == null) return null;
        if (own) name = System.IO.Path.GetFileNameWithoutExtension(name);
        var holder = new GameObject(name);
        var go = (GameObject)Object.Instantiate(model, holder.transform);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        var b = RendererBounds(go);
        // longest side becomes the barrel axis
        // longest side becomes the barrel axis; in this pack the barrel then points to -Z, so turn it round
        if (b.size.x >= b.size.z && b.size.x >= b.size.y) go.transform.localRotation = Quaternion.Euler(0, 90, 0) * go.transform.localRotation;
        else if (b.size.y > b.size.z) go.transform.localRotation = Quaternion.Euler(90, 0, 0) * go.transform.localRotation;
        else if (!own) go.transform.localRotation = Quaternion.Euler(0, 180, 0) * go.transform.localRotation;
        b = RendererBounds(go);
        float scale = length / Mathf.Max(0.001f, b.size.z);
        go.transform.localScale *= scale;
        b = RendererBounds(go);
        go.transform.localPosition -= b.center;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (var m in r.sharedMaterials)
                if (m != null && m.HasProperty("_Glossiness")) { m.SetFloat("_Glossiness", 0.55f); m.SetFloat("_Metallic", 0.35f); }
        }
        if (textured) TextureGun(holder, name);
        return holder;
    }

    private static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static GameObject BuildGun(int id, Transform parent)
    {
        GameObject fromPack = null;
        foreach (var candidate in GunModels[id])
            if ((fromPack = GunModel(candidate, GunLengths[id], true)) != null) break;
        if (fromPack != null)
        {
            fromPack.name = "W_" + Weapons.Get(id).Name;
            fromPack.transform.SetParent(parent, false);
            var size = RendererBounds(fromPack).size;
            // move so the hand (origin) sits on the grip
            var grip = GunGrips[id];
            foreach (Transform child in fromPack.transform)
                child.localPosition += new Vector3(0, size.y * (0.5f - grip.y), size.z * (0.5f - grip.x));
            var tip = new GameObject("Muzzle").transform;
            tip.SetParent(fromPack.transform, false);
            tip.localPosition = new Vector3(0, size.y * (0.5f - grip.y) + size.y * 0.25f, size.z * (1f - grip.x));
            if (id == 1)
            {
                // sit the sight on the top of the receiver (highest point of the gun just ahead of the grip)
                float z = size.z * 0.08f, top = float.MinValue;
                foreach (var filter in fromPack.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                    foreach (var v in filter.sharedMesh.vertices)
                    {
                        var p = fromPack.transform.InverseTransformPoint(filter.transform.TransformPoint(v));
                        if (Mathf.Abs(p.z - z) < size.z * 0.12f && p.y > top) top = p.y;
                    }
                }
                if (top == float.MinValue) top = size.y * (1f - grip.y);
                AddScope(fromPack.transform, top - 0.005f, z);
            }
            return fromPack;
        }
        var metal = Solid("GunMetal", new Color(0.13f, 0.13f, 0.14f), 0.55f, 0.75f);
        var polymer = Solid("GunPolymer", new Color(0.08f, 0.08f, 0.085f), 0.3f, 0f);
        var wood = Solid("GunWood", new Color(0.45f, 0.24f, 0.11f), 0.45f, 0f);
        var silver = Solid("GunSilver", new Color(0.72f, 0.72f, 0.74f), 0.75f, 0.9f);
        var green = Solid("GunOlive", new Color(0.24f, 0.28f, 0.18f), 0.35f, 0f);
        var glass = Solid("ScopeGlass", new Color(0.1f, 0.25f, 0.35f), 0.95f, 0.5f);
        var root = new GameObject("W_" + Weapons.Get(id).Name).transform;
        root.SetParent(parent, false);
        float muzzle = 0.6f;
        switch (id)
        {
            case 0: // AK-47
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.03f, 0.08f), new Vector3(0.055f, 0.075f, 0.3f), metal);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.02f, 0.33f), new Vector3(0.06f, 0.06f, 0.2f), wood);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.065f, 0.33f), new Vector3(0.04f, 0.025f, 0.2f), wood);
                Tube(root, new Vector3(0, 0.035f, 0.52f), 0.36f, 0.012f, metal);
                Tube(root, new Vector3(0, 0.068f, 0.5f), 0.16f, 0.01f, metal);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.08f, 0.67f), new Vector3(0.01f, 0.04f, 0.01f), metal);
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.01f, -0.2f), new Vector3(0.045f, 0.09f, 0.3f), wood, new Vector3(-6, 0, 0));
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.06f, 0.0f), new Vector3(0.035f, 0.1f, 0.045f), wood, new Vector3(-20, 0, 0));
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.075f, 0.13f), new Vector3(0.032f, 0.13f, 0.065f), metal, new Vector3(18, 0, 0));
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.15f, 0.17f), new Vector3(0.032f, 0.07f, 0.06f), metal, new Vector3(35, 0, 0));
                muzzle = 0.72f;
                break;
            case 1: // M4A1
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.03f, 0.07f), new Vector3(0.05f, 0.08f, 0.28f), polymer);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.03f, 0.33f), new Vector3(0.058f, 0.06f, 0.24f), polymer);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.08f, 0.2f), new Vector3(0.025f, 0.02f, 0.5f), metal);
                Tube(root, new Vector3(0, 0.035f, 0.55f), 0.24f, 0.011f, metal);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.1f, 0.0f), new Vector3(0.03f, 0.04f, 0.06f), polymer);
                Tube(root, new Vector3(0, 0.035f, -0.14f), 0.14f, 0.02f, polymer);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.0f, -0.27f), new Vector3(0.042f, 0.08f, 0.12f), polymer);
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.06f, 0.0f), new Vector3(0.033f, 0.1f, 0.045f), polymer, new Vector3(-18, 0, 0));
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.085f, 0.12f), new Vector3(0.03f, 0.13f, 0.065f), metal, new Vector3(8, 0, 0));
                muzzle = 0.69f;
                break;
            case 2: // SPAS-12
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.03f, 0.08f), new Vector3(0.055f, 0.085f, 0.28f), polymer);
                Tube(root, new Vector3(0, 0.05f, 0.5f), 0.56f, 0.016f, metal);
                Tube(root, new Vector3(0, 0.015f, 0.45f), 0.46f, 0.016f, metal);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.015f, 0.38f), new Vector3(0.05f, 0.05f, 0.17f), polymer);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.0f, -0.22f), new Vector3(0.045f, 0.085f, 0.3f), polymer, new Vector3(-5, 0, 0));
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.06f, 0.0f), new Vector3(0.034f, 0.1f, 0.045f), polymer, new Vector3(-18, 0, 0));
                muzzle = 0.79f;
                break;
            case 3: // L96 sniper
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.02f, 0.05f), new Vector3(0.06f, 0.09f, 0.6f), green);
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.02f, -0.3f), new Vector3(0.05f, 0.11f, 0.26f), green);
                Tube(root, new Vector3(0, 0.05f, 0.66f), 0.62f, 0.013f, metal);
                Tube(root, new Vector3(0, 0.12f, 0.08f), 0.34f, 0.022f, metal);
                Tube(root, new Vector3(0, 0.12f, 0.26f), 0.05f, 0.03f, metal);
                Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.12f, 0.29f), new Vector3(0.05f, 0.002f, 0.05f), glass, new Vector3(90, 0, 0));
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.08f, 0.08f), new Vector3(0.02f, 0.04f, 0.04f), metal);
                Part(root, PrimitiveType.Cube, new Vector3(0, -0.07f, 0.06f), new Vector3(0.03f, 0.08f, 0.05f), metal);
                Part(root, PrimitiveType.Cube, new Vector3(0.025f, -0.04f, 0.5f), new Vector3(0.01f, 0.12f, 0.01f), metal, new Vector3(20, 0, 15));
                Part(root, PrimitiveType.Cube, new Vector3(-0.025f, -0.04f, 0.5f), new Vector3(0.01f, 0.12f, 0.01f), metal, new Vector3(20, 0, -15));
                muzzle = 0.98f;
                break;
            case 4: // Glock 17
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.05f, 0.05f), new Vector3(0.03f, 0.035f, 0.19f), polymer);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.0f, 0.0f), new Vector3(0.028f, 0.11f, 0.05f), polymer, new Vector3(-15, 0, 0));
                muzzle = 0.15f;
                break;
            case 6: // MP5 (fallback: short M4)
            case 7: // SVD (fallback: L96 shape)
                return BuildGunFallback(id, root);
            default: // Desert Eagle
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.055f, 0.07f), new Vector3(0.036f, 0.045f, 0.25f), silver);
                Part(root, PrimitiveType.Cube, new Vector3(0, 0.0f, 0.0f), new Vector3(0.032f, 0.12f, 0.055f), polymer, new Vector3(-15, 0, 0));
                muzzle = 0.21f;
                break;
        }
        var m = new GameObject("Muzzle").transform;
        m.SetParent(root, false);
        m.localPosition = new Vector3(0, id == 2 ? 0.05f : id >= 4 ? 0.055f : 0.035f, muzzle);
        return root.gameObject;
    }

    private static GameObject BuildGunFallback(int id, Transform root)
    {
        var parent = root.parent;
        Object.DestroyImmediate(root.gameObject);
        var gun = BuildGun(id == 6 ? 1 : 3, parent);
        gun.name = "W_" + Weapons.Get(id).Name;
        if (id == 6) gun.transform.localScale = new Vector3(1f, 1f, 0.7f);
        return gun;
    }

    /// <summary>One prefab per weapon for the 3D preview in the profile screen.</summary>
    private static void BuildWeaponPrefabs()
    {
        Folder("Assets/Resources/Weapons");
        for (int i = 0; i < Weapons.Count; i++)
        {
            var holder = new GameObject("W" + i);
            BuildGun(i, holder.transform);
            PrefabUtility.SaveAsPrefabAsset(holder, $"Assets/Resources/Weapons/W{i}.prefab");
            Object.DestroyImmediate(holder);
        }
    }

    /// <summary>All guns of the pack in a grid with labels, photographed by the auto test to pick models.</summary>
    private static void BuildGunLineup()
    {
        Folder("Assets/Resources/Debug");
        var root = new GameObject("GunLineup");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var names = AssetDatabase.FindAssets("t:Model", new[] { GunsPath }).Select(AssetDatabase.GUIDToAssetPath)
            .Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
        for (int i = 0; i < names.Count; i++)
        {
            var gun = GunModel(names[i], names[i].Contains("Pistol") || names[i].Contains("Revolver") ? 0.35f : 1f);
            if (gun == null) continue;
            gun.transform.SetParent(root.transform, false);
            gun.transform.localPosition = new Vector3(0, (i / 6) * 0.55f, (i % 6) * 1.35f);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(gun.transform, false);
            marker.transform.localPosition = new Vector3(0, 0, 0.6f);
            marker.transform.localScale = Vector3.one * 0.04f;
            marker.GetComponent<Renderer>().sharedMaterial = Solid("MarkerRed", Color.red, 0.2f);
            var label = new GameObject("Label");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = gun.transform.localPosition + new Vector3(0, -0.2f, 0);
            label.transform.localRotation = Quaternion.Euler(0, -90, 0);
            var text = label.AddComponent<TextMesh>();
            text.text = names[i];
            text.font = font;
            text.fontSize = 48;
            text.characterSize = 0.012f;
            text.anchor = TextAnchor.MiddleCenter;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Debug/GunLineup.prefab");
        Object.DestroyImmediate(root);
        Debug.Log($"[Setup] gun lineup: {names.Count} models");
    }

    public static Texture2D Camo(string name, Color[] tones, float scale)
    {
        string path = $"{Generated}/Textures/{name}.png";
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // tileable blobs: layered sine noise on a torus
                float u = x / (float)size * Mathf.PI * 2, v = y / (float)size * Mathf.PI * 2;
                float n = Mathf.Sin(u * 2 + Mathf.Sin(v * 3) * scale) + Mathf.Sin(v * 2 + Mathf.Cos(u * 3) * scale) * 0.8f
                          + Mathf.Sin((u + v) * 4 + 1.3f) * 0.45f;
                int k = n < -0.6f ? 0 : n < 0.2f ? 1 : n < 0.9f ? 2 : 3;
                tex.SetPixel(x, y, tones[Mathf.Min(k, tones.Length - 1)]);
            }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material[] suits;

    public static Material[] Suits()
    {
        if (suits != null && suits.All(m => m != null)) return suits;
        suits = new[]
        {
            Mat("Suit_Urban", Color.white, 0.3f, 0f, Camo("Camo_Urban", new[] { new Color(0.32f, 0.33f, 0.35f), new Color(0.5f, 0.51f, 0.53f), new Color(0.7f, 0.7f, 0.71f), new Color(0.18f, 0.18f, 0.2f) }, 1.4f)),
            Mat("Suit_Desert", Color.white, 0.3f, 0f, Camo("Camo_Desert", new[] { new Color(0.62f, 0.52f, 0.36f), new Color(0.76f, 0.66f, 0.48f), new Color(0.5f, 0.4f, 0.28f), new Color(0.84f, 0.76f, 0.6f) }, 1.1f)),
            Mat("Suit_Woodland", Color.white, 0.3f, 0f, Camo("Camo_Woodland", new[] { new Color(0.2f, 0.27f, 0.16f), new Color(0.33f, 0.4f, 0.22f), new Color(0.38f, 0.3f, 0.2f), new Color(0.1f, 0.12f, 0.08f) }, 1.7f)),
            Mat("Suit_Black", new Color(0.16f, 0.17f, 0.19f), 0.35f),
        };
        return suits;
    }

    private static void BuildAgent(AnimatorController controller, string modelPath, string prefabName)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var root = new GameObject("Agent");
        root.layer = World.AgentLayer;
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.33f;
        cc.center = new Vector3(0, 0.92f, 0);
        cc.stepOffset = 0.45f;
        cc.slopeLimit = 50f;
        cc.skinWidth = 0.04f;

        var body = (GameObject)Object.Instantiate(model);
        body.name = "Model";
        body.transform.SetParent(root.transform, false);
        foreach (var cam in body.GetComponentsInChildren<Camera>()) Object.DestroyImmediate(cam.gameObject);
        foreach (var light in body.GetComponentsInChildren<Light>()) Object.DestroyImmediate(light.gameObject);

        // size: the mannequin should be about 1.8 m tall
        var skinned = body.GetComponentsInChildren<SkinnedMeshRenderer>();
        var bounds = skinned.Length > 0 ? skinned[0].bounds : new Bounds(Vector3.up, Vector3.one * 1.8f);
        foreach (var s in skinned) bounds.Encapsulate(s.bounds);
        Debug.Log($"[Setup] mannequin bounds {bounds.size} at {bounds.center}");
        if (bounds.size.y < 1.2f || bounds.size.y > 2.6f)
            body.transform.localScale *= 1.8f / Mathf.Max(0.01f, bounds.size.y);

        var anim = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        // which way does the model face? sample the aim pose and look where the right hand is
        var hand = Find(body.transform, "hand_r");
        var head = Find(body.transform, "head");
        var spine = Find(body.transform, "spine_03") ?? Find(body.transform, "spine_02");
        var aimClip = Clip("Pistol_Aim_Neutral") ?? Clip("Pistol_Idle_Loop");
        if (aimClip != null && hand != null)
        {
            aimClip.SampleAnimation(body, 0f);
            var local = root.transform.InverseTransformPoint(hand.position);
            Debug.Log($"[Setup] aim pose: right hand at {local}");
            if (local.z < 0)
            {
                body.transform.localRotation = Quaternion.Euler(0, 180, 0);
                Debug.Log("[Setup] model faces -Z, turned around");
            }
        }

        // materials: skin on the body (light or dark per look), eyes, hair; the clothes come from the Dresser
        var skin = Mat("Skin", new Color(0.76f, 0.58f, 0.46f), 0.32f);
        bool realistic = modelPath.Contains("/UBC/");
        string sex = modelPath.Contains("Female") ? "Female" : "Male";
        Texture2D U(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{UbcTextures}/{file}.png");
        var skins = realistic
            ? new[]
            {
                Mat($"Skin_{sex}_Light", Color.white, 0.55f, 0f, U($"T_{sex}_Light_BaseColor"), U($"T_{sex}_Normal"), U($"T_{sex}_MS")),
                Mat($"Skin_{sex}_Dark", Color.white, 0.55f, 0f, U($"T_{sex}_Dark_BaseColor"), U($"T_{sex}_Normal"), U($"T_{sex}_MS")),
            }
            : new Material[0];
        var eyes = Mat("Eyes", Color.white, 0.9f, 0f, U("T_Eye_BaseColor"), U("T_Eye_Normal"));
        // the hair textures are light grey (meant to be tinted): black hair and beard
        var black = new Color(0.075f, 0.065f, 0.06f);
        var hair1 = Mat("Hair_1", black, 0.35f, 0f, U("T_Hair_1_BaseColor"), U("T_Hair_1_Normal"));
        var hair2 = Mat("Hair_2", black, 0.35f, 0f, U("T_Hair_2_BaseColor"), U("T_Hair_2_Normal"));
        Material ForSlot(Material m)
        {
            string n = m != null ? m.name : "";
            if (!realistic) return skin;
            if (n.Contains("Eye")) return eyes;
            if (n.Contains("Hair_2")) return hair2;
            if (n.Contains("Hair")) return hair1;
            return skins[0];
        }
        // the body is the biggest skinned mesh (the others are eyes and eyebrows)
        var bodyRenderer = skinned.OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).FirstOrDefault();
        var vestMat = Mat("TeamVest", new Color(0.45f, 0.46f, 0.48f), 0.25f);
        foreach (var s in skinned)
            s.sharedMaterials = s.sharedMaterials.Select(ForSlot).ToArray();
        GameObject hair = null, beard = null;
        if (realistic && bodyRenderer != null)
        {
            var bones = body.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            // hair modelled on the same skeleton: re-bind its bones to this body
            SkinnedMeshRenderer Attach(string file, Material m)
            {
                var hairModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{UbcHair}/{file}.fbx");
                var src = hairModel != null ? hairModel.GetComponentInChildren<SkinnedMeshRenderer>() : null;
                if (src == null) { Debug.LogWarning("[Setup] hair missing: " + file); return null; }
                var go = new GameObject(file);
                go.transform.SetParent(bodyRenderer.transform.parent, false);
                var hairRenderer = go.AddComponent<SkinnedMeshRenderer>();
                hairRenderer.sharedMesh = src.sharedMesh;
                hairRenderer.bones = src.bones.Select(bone => bone != null && bones.TryGetValue(bone.name, out var mapped) ? mapped : null).ToArray();
                hairRenderer.rootBone = src.rootBone != null && bones.TryGetValue(src.rootBone.name, out var rootBone) ? rootBone : bodyRenderer.rootBone;
                hairRenderer.sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, src.sharedMaterials.Length)).ToArray();
                hairRenderer.localBounds = bodyRenderer.localBounds;
                return hairRenderer;
            }
            var hairRenderer = Attach(sex == "Male" ? "Hair_Buzzed" : "Hair_Buns", sex == "Male" ? hair1 : hair2);
            if (hairRenderer != null) hair = hairRenderer.gameObject;
            if (sex == "Male")
            {
                var beardRenderer = Attach("Hair_Beard", hair1);
                if (beardRenderer != null) beard = beardRenderer.gameObject;
            }
        }
        // the whole wardrobe, cut for this body at rest and skinned to its bones (Dresser, Outfit)
        var teamRenderers = new List<Renderer>();
        var parts = new List<Agent.OutfitPart>();
        if (bodyRenderer != null)
        {
            var dressed = Dresser.Dress(new Outfit.Body(bodyRenderer, prefabName), prefabName, sex == "Female", hair, beard,
                skinned.Where(r => r != bodyRenderer).ToArray(), vestMat);
            parts = dressed.Parts;
            teamRenderers = dressed.Team;
        }

        // weapons in the right hand, aligned with the character's forward direction in the aim pose
        var weapons = new GameObject[Weapons.Count];
        var muzzles = new Transform[Weapons.Count];
        var weaponRoot = new GameObject("WeaponRoot").transform;
        weaponRoot.SetPositionAndRotation(hand != null ? hand.position : root.transform.position + new Vector3(0.2f, 1.3f, 0.4f), root.transform.rotation);
        weaponRoot.position += root.transform.TransformDirection(new Vector3(-0.02f, -0.02f, 0.02f));
        weaponRoot.SetParent(hand != null ? hand : root.transform, true);
        for (int i = 0; i < Weapons.Count; i++)
        {
            weapons[i] = BuildGun(i, weaponRoot);
            muzzles[i] = weapons[i].transform.Find("Muzzle");
            weapons[i].SetActive(i == 0);
        }

        // hitboxes (triggers on their own layer)
        var bodyBox = new GameObject("BodyHitbox");
        bodyBox.layer = World.HitboxLayer;
        bodyBox.transform.SetParent(root.transform, false);
        var capsule = bodyBox.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.center = new Vector3(0, 0.74f, 0);
        capsule.height = 1.48f;
        capsule.radius = 0.3f;
        var bodyHit = bodyBox.AddComponent<Hitbox>();
        var hitboxes = new List<Hitbox> { bodyHit };
        if (head != null)
        {
            var headBox = new GameObject("HeadHitbox");
            headBox.layer = World.HitboxLayer;
            headBox.transform.position = head.position + Vector3.up * 0.08f;
            headBox.transform.SetParent(head, true);
            var sphere = headBox.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.15f / Mathf.Max(0.01f, headBox.transform.lossyScale.x);
            var headHit = headBox.AddComponent<Hitbox>();
            headHit.Head = true;
            hitboxes.Add(headHit);
        }

        // back to the idle pose for the saved prefab
        var idle = Clip("Idle_Loop");
        if (idle != null) idle.SampleAnimation(body, 0f);

        var agent = root.AddComponent<Agent>();
        agent.Anim = anim;
        agent.Model = body.transform;
        agent.Head = head;
        agent.WeaponModels = weapons;
        agent.Muzzles = muzzles;
        agent.TeamRenderers = teamRenderers.ToArray();
        agent.AllRenderers = root.GetComponentsInChildren<Renderer>(true);
        agent.Hitboxes = hitboxes.ToArray();
        agent.Controller = cc;
        agent.Skins = skins;
        agent.SkinRenderers = realistic && bodyRenderer != null ? new Renderer[] { bodyRenderer } : new Renderer[0];
        agent.Parts = parts.ToArray();

        PrefabUtility.SaveAsPrefabAsset(root, $"Assets/Resources/{prefabName}.prefab");
        Object.DestroyImmediate(root);
        Debug.Log($"[Setup] agent prefab {prefabName} saved");
    }
}
