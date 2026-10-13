using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.UI;
using AbyssKingdom.Client.World;
using AbyssKingdom.Shared.City;
using UnityEngine;
using UnityEngine.UIElements;

namespace AbyssKingdom.Client.Diagnostics
{
    /// <summary>Opt-in loopback Dev check. Sends a real feedback intent and one harmless sample error.</summary>
    public sealed class PlaytestCheck : MonoBehaviour
    {
        private string _output;
        private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-playtestcheck"), server = Array.IndexOf(args, "-server");
            if (flag < 0 || flag + 1 >= args.Length || server < 0 || server + 1 >= args.Length ||
                Array.IndexOf(args, "-devlogin") < 0 || !Uri.TryCreate(args[server + 1], UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            var go = new GameObject("LocalPlaytestCheck"); DontDestroyOnLoad(go);
            go.AddComponent<PlaytestCheck>()._output = Path.GetFullPath(args[flag + 1]);
        }
        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output); float deadline = Time.realtimeSinceStartup + 90;
            while (GameClient.Instance == null || !GameClient.Instance.IsLoggedIn || FindAnyObjectByType<GameUI>() == null)
            {
                if (Time.realtimeSinceStartup > deadline) { Debug.LogError("PLAYTEST_CHECK login timeout"); Application.Quit(2); yield break; }
                yield return null;
            }
            var game = GameClient.Instance;
            yield return new WaitForSecondsRealtime(5);
            if (game.City.GetLevel(BuildingType.Keep) == 1)
            {
                game.Upgrade(BuildingType.Keep); yield return new WaitForSecondsRealtime(2);
                game.Speedup(QueueType.Building, ItemType.SpeedupBuilding30m); yield return new WaitForSecondsRealtime(2);
                game.ClaimQuest(); yield return new WaitForSecondsRealtime(2);
            }
            var ui = FindAnyObjectByType<GameUI>();
            var intro = FindAnyObjectByType<IntroSequence>();
            if (intro != null) typeof(IntroSequence).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(intro, null);
            yield return new WaitForSecondsRealtime(.5f);
            WorldScene.Instance.EndCinematic(); WorldScene.Instance.SetMode(WorldScene.Mode.City);
            typeof(GameUI).GetMethod("OpenFeedback", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
            yield return new WaitForSecondsRealtime(.5f);
            var feedback = Field(ui, "_feedback");
            var field = (TextField)Field(feedback, "_text");
            var tree = new System.Text.StringBuilder();
            foreach (var element in field.Query<VisualElement>().ToList())
                tree.AppendLine(element.GetType().Name + " name=" + element.name + " classes=" + string.Join(",", element.GetClasses()) +
                    " bg=" + element.resolvedStyle.backgroundColor + " text=" + element.resolvedStyle.color + " image=" + element.resolvedStyle.backgroundImage);
            File.WriteAllText(Path.Combine(_output, "input-tree.txt"), tree.ToString());
            ((TextField)Field(feedback, "_text")).value = "封測測試：升級前置清楚，希望採集目標更容易找到。";
            yield return Capture(ui, "feedback-draft.png");
            feedback.GetType().GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(feedback, null);
            Debug.LogError("PLAYTEST_SAMPLE: harmless local error for reporting verification");
            yield return new WaitForSecondsRealtime(2);
            bool ok = ((Label)Field(feedback, "_status")).text == DisplayNames.FeedbackThanks;
            yield return Capture(ui, "feedback.png");
            File.WriteAllText(Path.Combine(_output, "validation.json"), "{\"feedbackAcknowledged\":" + ok.ToString().ToLowerInvariant() + "}");
            Debug.Log("PLAYTEST_CHECK " + (ok ? "PASS" : "FAIL")); Application.Quit(ok ? 0 : 2);
        }
        private IEnumerator Capture(GameUI ui, string fileName)
        {
            var panel = ui.GetComponent<UIDocument>().panelSettings;
            // Multiline input clips with stencil; a color-only target produces an opaque mask quad.
            var uiTarget = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            panel.targetTexture = uiTarget; panel.clearColor = true; panel.colorClearValue = Color.clear;
            yield return new WaitForSecondsRealtime(.5f); yield return new WaitForEndOfFrame();
            var camera = WorldScene.Instance.Cam; var priorTarget = camera.targetTexture;
            var sceneTarget = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            camera.targetTexture = sceneTarget; camera.Render(); camera.targetTexture = priorTarget;
            var priorActive = RenderTexture.active; var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            RenderTexture.active = sceneTarget; image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); image.Apply();
            var background = image.GetPixels32();
            RenderTexture.active = uiTarget; image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); image.Apply();
            var foreground = image.GetPixels32();
            for (int i = 0; i < background.Length; i++)
            {
                var f = foreground[i]; var b = background[i]; int a = f.a;
                background[i] = new Color32((byte)((f.r * a + b.r * (255 - a)) / 255),
                    (byte)((f.g * a + b.g * (255 - a)) / 255), (byte)((f.b * a + b.b * (255 - a)) / 255), 255);
            }
            image.SetPixels32(background); image.Apply();
            File.WriteAllBytes(Path.Combine(_output, fileName), image.EncodeToPNG()); Destroy(image);
            RenderTexture.active = priorActive; panel.targetTexture = null;
            RenderTexture.ReleaseTemporary(sceneTarget); RenderTexture.ReleaseTemporary(uiTarget);
        }
    }
}
