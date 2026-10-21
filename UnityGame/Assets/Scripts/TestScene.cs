using UnityEngine;

/// <summary>
/// Simple test scene: spinning cubes over a ground plane, plus an on-screen label and FPS counter.
/// Writes "[UnityTest]" markers to logcat so the CI emulator test knows the game is running.
/// </summary>
public class TestScene : MonoBehaviour
{
    private float fps;
    private int frames;
    private GUIStyle style;

    private void Start()
    {
        Application.targetFrameRate = 60;
        Debug.Log("[UnityTest] first-frame");
    }

    private void Update()
    {
        foreach (Transform child in transform)
            child.Rotate(new Vector3(15f, 40f, 0f) * Time.deltaTime);

        fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);
        if (++frames % 300 == 0)
            Debug.Log($"[UnityTest] running frames={frames} fps={fps:0}");
    }

    private void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.label) { fontSize = Screen.height / 14, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(30, 20, Screen.width, Screen.height / 8f), "Unity test build - OK", style);
        GUI.Label(new Rect(30, 20 + Screen.height / 8f, Screen.width, Screen.height / 8f), $"FPS: {fps:0}", style);
    }
}
