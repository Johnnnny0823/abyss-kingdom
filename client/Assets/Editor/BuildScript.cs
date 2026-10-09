using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一鍵打包。編輯器選單「Abyss Kingdom」，或命令列（Unity 必須先關閉）：
///   Unity.exe -batchmode -quit -projectPath client -executeMethod BuildScript.BuildWindows -logFile -
/// 產物放在 client/Builds/（已在 .gitignore）。
/// </summary>
public static class BuildScript
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Abyss Kingdom/Build Windows")]
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/AbyssKingdom.exe");

    [MenuItem("Abyss Kingdom/Build Android APK")]
    public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/AbyssKingdom.apk");

    private static void Build(BuildTarget target, string output)
    {
        EnsureScene();
        AbyssMaterials.Generate();
        GroundTextureBaker.Bake();

        PlayerSettings.companyName = "AbyssKingdom";
        PlayerSettings.productName = "深淵王國";
        PlayerSettings.runInBackground = true;   // 視窗失焦時仍保持連線
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 800;
        PlayerSettings.resizableWindow = true;

        if (target == BuildTarget.Android)
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.abysskingdom.client");
            PlayerSettings.Android.forceInternetPermission = true; // System.Net.WebSockets 不會被自動偵測到
            // Newtonsoft 靠反射建立物件，IL2CPP 裁剪太積極會把建構子砍掉（另見 Assets/link.xml）
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = target,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        Debug.Log($"[BuildScript] {target}: {summary.result}, {summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors -> {output}");
        if (Application.isBatchMode)
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>開發面板會在任何場景自動出現，所以只需要一個空場景當入口。</summary>
    private static void EnsureScene()
    {
        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}
