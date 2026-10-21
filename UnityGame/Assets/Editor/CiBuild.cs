using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Command line build used by GitHub Actions: -executeMethod CiBuild.BuildAndroid</summary>
public static class CiBuild
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    public static void BuildAndroid()
    {
        CreateScene();

        PlayerSettings.companyName = "ElyasOmar";
        PlayerSettings.productName = "Unity Test";
        PlayerSettings.bundleVersion = "0.1";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.elyasomar.unitytest");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        EditorUserBuildSettings.buildAppBundle = false;

        var output = "Builds/Android/UnityTest.apk";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });

        Debug.Log($"[CiBuild] result={report.summary.result} size={report.summary.totalSize} errors={report.summary.totalErrors}");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var camera = Camera.main;
        camera.transform.SetPositionAndRotation(new Vector3(0, 3, -8), Quaternion.Euler(15, 0, 0));
        camera.clearFlags = CameraClearFlags.Skybox;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4, 1, 4);
        ground.GetComponent<Renderer>().sharedMaterial = Material(new Color(0.76f, 0.66f, 0.45f), "Ground");

        var root = new GameObject("Cubes");
        root.AddComponent<TestScene>();
        var colors = new[] { new Color(0.9f, 0.3f, 0.2f), new Color(0.2f, 0.6f, 0.9f), new Color(0.3f, 0.8f, 0.3f) };
        for (var i = 0; i < 3; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cube" + i;
            cube.transform.SetParent(root.transform);
            cube.transform.position = new Vector3((i - 1) * 2.5f, 1.2f, 0);
            cube.GetComponent<Renderer>().sharedMaterial = Material(colors[i], "Cube" + i);
        }

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    private static Material Material(Color color, string name)
    {
        var material = new Material(Shader.Find("Standard")) { color = color };
        Directory.CreateDirectory("Assets/Materials");
        AssetDatabase.CreateAsset(material, $"Assets/Materials/{name}.mat");
        return material;
    }
}
