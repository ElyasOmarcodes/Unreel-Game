using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Command line build used by GitHub Actions: -executeMethod CiBuild.BuildAll</summary>
public static class CiBuild
{
    public static void BuildAll()
    {
        int code = 1;
        try
        {
            ConfigurePlayer();
            AssetSetup.Run();
            MapBuilder.BuildScene();
            bool ok = true;
            if (Environment.GetEnvironmentVariable("BUILD_LINUX") == "1") ok &= BuildLinux();
            ok &= BuildAndroid();
            code = ok ? 0 : 1;
        }
        catch (Exception e)
        {
            Debug.LogError("[CiBuild] failed: " + e);
        }
        EditorApplication.Exit(code);
    }

    private static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "ElyasOmar";
        PlayerSettings.productName = "Urban Agents";
        PlayerSettings.bundleVersion = "1.0";
        if (int.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"), out int run)) PlayerSettings.Android.bundleVersionCode = run;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.elyasomar.urbanagents");
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.elyasomar.urbanagents");
        PlayerSettings.colorSpace = ColorSpace.Linear;

        // Android
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.forceInternetPermission = true;
        // smaller APK: strip unused managed and engine code, optimise the IL2CPP output for size
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.Android.renderOutsideSafeArea = true;
        // Android frame pacing (Swappy): frames shown at even intervals, no stutter at 30 or 60 fps
        PlayerSettings.Android.optimizedFramePacing = true;
        // smaller build: vertex data no shader reads is left out of the meshes
        PlayerSettings.stripUnusedMeshComponents = true;
        PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

        // desktop test build
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.OpenGLCore });

        // the game uses the classic input manager
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settings.Length > 0)
        {
            var so = new SerializedObject(settings[0]);
            var input = so.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 0; so.ApplyModifiedProperties(); }
        }
        QualitySettings.shadowDistance = 35;
        // skin the characters and their clothes on the GPU (many skinned meshes per agent)
        var deformation = typeof(PlayerSettings).GetProperty("meshDeformation");
        if (deformation != null && deformation.CanWrite) deformation.SetValue(null, Enum.Parse(deformation.PropertyType, "GPU"));
        else typeof(PlayerSettings).GetProperty("gpuSkinning")?.SetValue(null, true);
        AssetDatabase.SaveAssets();
    }

    private static bool Build(BuildTarget target, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(e => e.enabled).Select(e => e.path).ToArray(),
            locationPathName = output,
            target = target,
            options = BuildOptions.None,
        });
        var s = report.summary;
        // what fills the build: the biggest kinds of asset and the biggest assets
        var packed = report.packedAssets.SelectMany(p => p.contents).ToList();
        foreach (var g in packed.GroupBy(a => a.type.Name).OrderByDescending(g => g.Sum(a => (long)a.packedSize)).Take(8))
            Debug.Log($"[CiBuild] {target} size by type: {g.Key} {g.Sum(a => (long)a.packedSize) / 1024} KB ({g.Count()})");
        foreach (var a in packed.OrderByDescending(a => a.packedSize).Take(12))
            Debug.Log($"[CiBuild] {target} big asset: {a.sourceAssetPath} {a.type.Name} {a.packedSize / 1024} KB");
        Debug.Log($"[CiBuild] {target}: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime}");
        return s.result == BuildResult.Succeeded;
    }

    private static bool BuildAndroid()
    {
        // the gun lineup is only for the screenshots of the desktop test build: keep it out of the APK
        AssetDatabase.DeleteAsset("Assets/Resources/Debug");
        AssetDatabase.SaveAssets();
        return Build(BuildTarget.Android, "Builds/Android/UrbanAgents.apk");
    }

    private static bool BuildLinux()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
        {
            Debug.LogWarning("[CiBuild] Linux build support is not installed, skipping the desktop test build");
            return true;
        }
        return Build(BuildTarget.StandaloneLinux64, "Builds/Linux/UrbanAgents.x86_64");
    }
}
