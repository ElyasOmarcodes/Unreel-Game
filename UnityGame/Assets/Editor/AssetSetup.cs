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
        SetupAnimationClips();
        var controller = BuildAnimator();
        BuildEffectMaterials();
        BuildAgent(controller);
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
    };

    private static void SetupAnimationClips()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(UalPath);
        if (importer == null) throw new System.Exception("UAL1.fbx is missing - the asset download step failed");
        var defaults = importer.defaultClipAnimations;
        Debug.Log($"[Setup] UAL clips in file: {defaults.Length}");
        var clips = new List<ModelImporterClipAnimation>();
        foreach (var name in WantedClips)
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
        var clip = AssetDatabase.LoadAllAssetsAtPath(UalPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
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
        c.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
        var parameters = c.parameters;
        foreach (var p in parameters) if (p.name == "Grounded") p.defaultBool = true;
        c.parameters = parameters;

        var sm = c.layers[0].stateMachine;
        var loco = c.CreateBlendTreeInController("Locomotion", out var tree, 0);
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveY";
        void Add(BlendTree t, string clip, float x, float y) { var a = Clip(clip); if (a != null) t.AddChild(a, new Vector2(x, y)); }
        Add(tree, "Idle_Loop", 0, 0);
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

        var crouch = c.CreateBlendTreeInController("Crouch", out var crouchTree, 0);
        crouchTree.blendType = BlendTreeType.FreeformDirectional2D;
        crouchTree.blendParameter = "MoveX";
        crouchTree.blendParameterY = "MoveY";
        Add(crouchTree, "Crouch_Idle_Loop", 0, 0);
        Add(crouchTree, "Crouch_Fwd_Loop", 0, 1);
        Add(crouchTree, "Crouch_Bwd_Loop", 0, -1);
        Add(crouchTree, "Crouch_Left_Loop", -1, 0);
        Add(crouchTree, "Crouch_Right_Loop", 1, 0);

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
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

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

    private static GameObject BuildGun(int id, Transform parent)
    {
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

    private static void BuildAgent(AnimatorController controller)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(UalPath);
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

        // materials: suit (team tinted), joints, gear
        var suit = Mat("TeamSuit", new Color(0.62f, 0.63f, 0.66f), 0.35f);
        var joints = Mat("Joints", new Color(0.1f, 0.1f, 0.11f), 0.45f, 0.2f);
        var vestMat = Mat("TeamVest", new Color(0.45f, 0.46f, 0.48f), 0.25f);
        var gear = Solid("Gear", new Color(0.16f, 0.17f, 0.14f), 0.25f);
        var visor = Solid("Visor", new Color(0.05f, 0.06f, 0.08f), 0.92f, 0.4f);
        foreach (var s in skinned)
            s.sharedMaterials = s.sharedMaterials.Select(m => m != null && m.name.Contains("Joint") ? joints : suit).ToArray();

        var teamRenderers = new List<Renderer>(skinned);
        if (spine != null)
        {
            // vest and backpack, placed in world space in the current pose then attached to the chest bone
            var holder = new GameObject("Gear").transform;
            holder.SetPositionAndRotation(spine.position, root.transform.rotation * body.transform.localRotation);
            teamRenderers.Add(Part(holder, PrimitiveType.Cube, new Vector3(0, -0.05f, 0.02f), new Vector3(0.34f, 0.32f, 0.22f), vestMat).GetComponent<Renderer>());
            Part(holder, PrimitiveType.Cube, new Vector3(0, -0.04f, -0.15f), new Vector3(0.24f, 0.28f, 0.1f), gear);
            Part(holder, PrimitiveType.Cube, new Vector3(-0.1f, -0.16f, 0.16f), new Vector3(0.08f, 0.09f, 0.05f), gear);
            Part(holder, PrimitiveType.Cube, new Vector3(0.1f, -0.16f, 0.16f), new Vector3(0.08f, 0.09f, 0.05f), gear);
            holder.SetParent(spine, true);
        }
        if (head != null)
        {
            var holder = new GameObject("Helmet").transform;
            holder.SetPositionAndRotation(head.position, root.transform.rotation * body.transform.localRotation);
            teamRenderers.Add(Part(holder, PrimitiveType.Sphere, new Vector3(0, 0.1f, -0.01f), new Vector3(0.27f, 0.21f, 0.29f), vestMat).GetComponent<Renderer>());
            Part(holder, PrimitiveType.Cube, new Vector3(0, 0.06f, 0.1f), new Vector3(0.2f, 0.05f, 0.08f), visor);
            holder.SetParent(head, true);
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

        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Agent.prefab");
        Object.DestroyImmediate(root);
        Debug.Log("[Setup] agent prefab saved");
    }
}
