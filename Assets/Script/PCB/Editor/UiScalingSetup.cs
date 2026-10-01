using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > PCB > Set Up UI Scaling (1920x1080): every Canvas in every scene of the build scales with the
/// screen from a 1920x1080 design size (instead of staying the same size in pixels), and the build's
/// default resolution becomes 1920x1080 full screen. Safe to run again, e.g. after adding a scene.
/// </summary>
static class UiScalingSetup
{
    public static readonly Vector2 Reference = new Vector2(1920f, 1080f);

    [MenuItem("Tools/PCB/Set Up UI Scaling (1920x1080)")]
    static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string startScene = EditorSceneManager.GetActiveScene().path;
        int canvases = 0, scenes = 0;

        foreach (var entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled || string.IsNullOrEmpty(entry.path)) continue;
            var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var scaler in root.GetComponentsInChildren<CanvasScaler>(true))
                {
                    if (!scaler.GetComponent<Canvas>() || !scaler.GetComponent<Canvas>().isRootCanvas) continue;
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = Reference;
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    scaler.matchWidthOrHeight = 0.5f; // exact on 16:9; a fair compromise on other shapes
                    EditorUtility.SetDirty(scaler);
                    canvases++;
                }
            EditorSceneManager.SaveScene(scene);
            scenes++;
        }

        PlayerSettings.defaultScreenWidth = (int)Reference.x;
        PlayerSettings.defaultScreenHeight = (int)Reference.y;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

        if (!string.IsNullOrEmpty(startScene)) EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
        Debug.Log($"[PCB] UI scaling set to 1920x1080 on {canvases} canvas(es) in {scenes} scene(s); build default resolution 1920x1080 full screen.");
    }
}
