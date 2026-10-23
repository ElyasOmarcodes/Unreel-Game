using System.Collections.Generic;
using UA;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the game: the main scene (sun, camera, sky, world, app) and one scene for each map (the port,
/// the village, the old town and Al-Zahra), so the game loads only the map being played. The old
/// Downtown map is kept out of the game in BackupExtra/Downtown at the top of the repository.
/// </summary>
public static class MapBuilder
{
    public const string ScenePath = "Assets/Scenes/Main.unity";

    public static void BuildScene()
    {
        AssetSetup.Folder("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.shadowBias = 0.06f;
        sun.shadowNormalBias = 0.4f;
        sunGo.transform.rotation = Quaternion.Euler(48, -35, 0);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.15f;   // more depth precision far away: no flicker where slabs meet
        cam.farClipPlane = 260f;
        cam.fieldOfView = 60f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<CameraRig>().Cam = cam;
        camGo.transform.position = new Vector3(0, 30, -60);

        var skybox = new Material(Shader.Find("Skybox/Procedural"));
        skybox.SetFloat("_SunSize", 0.035f);
        skybox.SetFloat("_AtmosphereThickness", 1f);
        skybox.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.62f));
        skybox.SetColor("_GroundColor", new Color(0.35f, 0.33f, 0.3f));
        AssetDatabase.DeleteAsset("Assets/Generated/Sky.mat");
        AssetDatabase.CreateAsset(skybox, "Assets/Generated/Sky.mat");
        RenderSettings.skybox = skybox;
        RenderSettings.sun = sun;

        MeshGen.Reset();
        // each map is built while the others are switched off so their colliders do not mix
        var port = PortBuilder.Build(null);
        port.Root.SetActive(false);
        var village = VillageBuilder.Build();
        village.Root.SetActive(false);
        var town = TownBuilder.Build();
        town.Root.SetActive(false);
        var zahra = ZahraBuilder.Build();
        zahra.Root.SetActive(false);

        var worldGo = new GameObject("World");
        var world = worldGo.AddComponent<World>();
        world.Sun = sun;
        world.Skybox = skybox;
        world.Layouts = new[] { port, village, town, zahra };

        new GameObject("App").AddComponent<App>();

        // every map into a scene of its own: the game loads only the one being played (memory)
        EditorSceneManager.SaveScene(scene, ScenePath);
        var buildScenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
        var names = new[] { "Map_Port", "Map_Village", "Map_OldTown", "Map_Zahra" };
        for (int i = 0; i < world.Layouts.Length; i++)
        {
            var layout = world.Layouts[i];
            var go = layout.Root;
            go.SetActive(true);
            go.GetComponent<MapScene>().Layout = i;
            var mapScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, mapScene);
            string path = $"Assets/Scenes/{names[i]}.unity";
            EditorSceneManager.SaveScene(mapScene, path);
            EditorSceneManager.CloseScene(mapScene, true);
            layout.Root = null;
            layout.Scene = names[i];
            buildScenes.Add(new EditorBuildSettingsScene(path, true));
        }
        Debug.Log($"[Map] built {world.Layouts.Length} maps: {string.Join(", ", names)}");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = buildScenes.ToArray();
    }
}
