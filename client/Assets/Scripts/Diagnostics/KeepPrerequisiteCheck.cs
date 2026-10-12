using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.UI;
using AbyssKingdom.Shared.City;
using UnityEngine;
using UnityEngine.UIElements;

namespace AbyssKingdom.Client.Diagnostics
{
    /// <summary>Opt-in loopback Dev UI check; never edits the city or contacts production.</summary>
    public sealed class KeepPrerequisiteCheck : MonoBehaviour
    {
        private string _output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-keepprereqcheck"), server = Array.IndexOf(args, "-server");
            if (flag < 0 || flag + 1 >= args.Length || server < 0 || server + 1 >= args.Length ||
                Array.IndexOf(args, "-devlogin") < 0 || !Uri.TryCreate(args[server + 1], UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            var go = new GameObject("LocalKeepPrerequisiteCheck"); DontDestroyOnLoad(go);
            go.AddComponent<KeepPrerequisiteCheck>()._output = Path.GetFullPath(args[flag + 1]);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output);
            float deadline = Time.realtimeSinceStartup + 90;
            while (GameClient.Instance == null || !GameClient.Instance.IsLoggedIn || FindAnyObjectByType<GameUI>() == null)
            {
                if (Time.realtimeSinceStartup > deadline) { Debug.LogError("KEEP_PREREQ_CHECK login timeout"); Application.Quit(2); yield break; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(5);
            var ui = FindAnyObjectByType<GameUI>();
            // Force a real UI Toolkit render even when Windows hides the swap chain.
            var uiTarget = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            var settings = ui.GetComponent<UIDocument>().panelSettings;
            settings.targetTexture = uiTarget;
            settings.clearColor = true; settings.colorClearValue = Color.clear;
            var focus = typeof(GameUI).GetMethod("FocusBuilding", BindingFlags.Instance | BindingFlags.NonPublic);
            focus.Invoke(ui, new object[] { BuildingType.Keep });
            yield return new WaitForSecondsRealtime(1);
            var root = ui.GetComponent<UIDocument>().rootVisualElement;
            var rows = root.Query<Button>().ToList().Where(b => b.text != null && b.text.Contains("目前 Lv.")).ToList();
            bool rowsOk = rows.Count == 2 && rows.Any(b => b.text.Contains("未達成"));
            var upgradeButton = root.Query<Button>().ToList().FirstOrDefault(b => b.text == "前置建築未達成");
            bool layoutOk = upgradeButton != null && rows.All(b => b.worldBound.yMax <= upgradeButton.worldBound.yMin + 1);
            bool blocked = CityLogic.CanUpgrade(GameClient.Instance.City, BuildingType.Keep) == UpgradeResult.KeepPrerequisiteMissing;
            focus.Invoke(ui, new object[] { BuildingType.Warehouse });
            yield return new WaitForSecondsRealtime(.4f);
            bool jumped = (BuildingType?)typeof(GameUI).GetField("_selBuilding", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui) == BuildingType.Warehouse;
            focus.Invoke(ui, new object[] { BuildingType.Keep });
            yield return new WaitForSecondsRealtime(.5f);
            yield return new WaitForEndOfFrame();
            var camera = AbyssKingdom.Client.World.WorldScene.Instance.Cam;
            var sceneTarget = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            var previousTarget = camera.targetTexture;
            camera.targetTexture = sceneTarget; camera.Render(); camera.targetTexture = previousTarget;
            var previousActive = RenderTexture.active;
            var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            RenderTexture.active = sceneTarget;
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); image.Apply();
            var scenePixels = image.GetPixels32();
            RenderTexture.active = uiTarget;
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); image.Apply();
            var uiPixels = image.GetPixels32();
            for (int i = 0; i < scenePixels.Length; i++)
            {
                var foreground = uiPixels[i]; var background = scenePixels[i]; int a = foreground.a;
                scenePixels[i] = new Color32((byte)((foreground.r * a + background.r * (255 - a)) / 255),
                    (byte)((foreground.g * a + background.g * (255 - a)) / 255),
                    (byte)((foreground.b * a + background.b * (255 - a)) / 255), 255);
            }
            image.SetPixels32(scenePixels); image.Apply();
            File.WriteAllBytes(Path.Combine(_output, "keep-prerequisites.png"), image.EncodeToPNG()); Destroy(image);
            RenderTexture.active = previousActive; settings.targetTexture = null;
            RenderTexture.ReleaseTemporary(uiTarget); RenderTexture.ReleaseTemporary(sceneTarget);
            bool ok = rowsOk && blocked && jumped && layoutOk;
            File.WriteAllText(Path.Combine(_output, "validation.json"),
                $"{{\"rowsVisible\":{rowsOk.ToString().ToLowerInvariant()},\"upgradeBlocked\":{blocked.ToString().ToLowerInvariant()},\"jumpHandlerSelectedWarehouse\":{jumped.ToString().ToLowerInvariant()},\"status\":\"{(ok ? "PASS" : "FAIL")}\"}}");
            Debug.Log($"KEEP_PREREQ_CHECK {(ok ? "PASS" : "FAIL")} rows={rows.Count} blocked={blocked} jump={jumped} noOverlap={layoutOk}");
            Application.Quit(ok ? 0 : 2);
        }
    }
}
