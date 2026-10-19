using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.UI;
using AbyssKingdom.Client.World;
using AbyssKingdom.Shared.City;
using UnityEngine;
using UnityEngine.UIElements;

namespace AbyssKingdom.Client.Diagnostics
{
    /// <summary>
    /// Local Dev capture for the README: -devlogin -readmecapture output-directory.
    /// With -intro it records every intro frame (JPG + intro time) instead of taking screenshots.
    /// </summary>
    public sealed class ReadmeCapture : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private string _output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int output = Array.IndexOf(args, "-readmecapture"), server = Array.IndexOf(args, "-server");
            if (output < 0 || output + 1 >= args.Length || server < 0 || server + 1 >= args.Length ||
                Array.IndexOf(args, "-devlogin") < 0 || !Uri.TryCreate(args[server + 1], UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            var go = new GameObject("LocalReadmeCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<ReadmeCapture>()._output = Path.GetFullPath(args[output + 1]);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output);
            float deadline = Time.realtimeSinceStartup + 120;
            while (GameClient.Instance == null || !GameClient.Instance.IsLoggedIn || FindAnyObjectByType<GameUI>() == null)
            {
                if (Time.realtimeSinceStartup > deadline) { Debug.LogError("README_CAPTURE login timeout"); Application.Quit(2); yield break; }
                yield return null;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-intro") >= 0) yield return RecordIntro();
            else yield return Screenshots();
            Debug.Log("README_CAPTURE COMPLETE");
            Application.Quit(0);
        }

        // ───── 開場：每一格都存，附上開場內部時間，之後再重新取樣成固定幀率 ─────

        private IEnumerator RecordIntro()
        {
            IntroSequence intro = null;
            float wait = Time.realtimeSinceStartup + 20;
            while ((intro = FindAnyObjectByType<IntroSequence>()) == null)
            {
                if (Time.realtimeSinceStartup > wait) { Debug.LogError("README_CAPTURE intro not started"); yield break; }
                yield return null;
            }
            var frames = Path.Combine(_output, "frames");
            Directory.CreateDirectory(frames);
            var tField = typeof(IntroSequence).GetField("_t", Private);
            var starts = (float[])typeof(IntroSequence).GetField("_starts", Private).GetValue(intro);
            var durations = (float[])typeof(IntroSequence).GetField("_durations", Private).GetValue(intro);
            var csv = new StringBuilder("index,t\n");
            int n = 0;
            while (intro != null)
            {
                yield return new WaitForEndOfFrame();
                if (intro == null) break;
                float t = (float)tField.GetValue(intro);
                var image = Grab();
                File.WriteAllBytes(Path.Combine(frames, n.ToString("D5") + ".jpg"), image.EncodeToJPG(92));
                Destroy(image);
                csv.Append(n).Append(',').Append(t.ToString("F4", CultureInfo.InvariantCulture)).Append('\n');
                n++;
            }
            File.WriteAllText(Path.Combine(_output, "frames.csv"), csv.ToString());
            var json = new StringBuilder("{\"starts\":[");
            for (int i = 0; i < starts.Length; i++) json.Append(i > 0 ? "," : "").Append(starts[i].ToString("F4", CultureInfo.InvariantCulture));
            json.Append("],\"durations\":[");
            for (int i = 0; i < durations.Length; i++) json.Append(i > 0 ? "," : "").Append(durations[i].ToString("F4", CultureInfo.InvariantCulture));
            json.Append("]}");
            File.WriteAllText(Path.Combine(_output, "intro.json"), json.ToString());
            Debug.Log("README_CAPTURE intro frames=" + n);
            yield return new WaitForSecondsRealtime(3);
            yield return Shot("intro-landing.png");
        }

        // ───── 遊戲畫面 ─────

        private IEnumerator Screenshots()
        {
            var ui = FindAnyObjectByType<GameUI>();
            var w = WorldScene.Instance;
            // 開場提示消失後再拍；仍保險把提示區藏起來
            yield return new WaitForSecondsRealtime(6);
            ((VisualElement)typeof(GameUI).GetField("_toasts", Private).GetValue(ui)).style.display = DisplayStyle.None;

            w.SetMode(WorldScene.Mode.City);
            w.FocusOn(WorldScene.Mode.City, Vector3.zero);
            yield return Shot("city.png");

            Invoke(ui, "FocusBuilding", BuildingType.Keep);
            yield return Shot("keep-card.png");
            typeof(GameUI).GetField("_selBuilding", Private).SetValue(ui, null);

            w.FocusOn(WorldScene.Mode.City, new Vector3(7.5f, 0, 2f));
            yield return Shot("city-fog.png");
            w.FocusOn(WorldScene.Mode.City, Vector3.zero);

            foreach (var (page, name) in new[] { (0, "heroes.png"), (1, "expedition.png"), (3, "bag.png"), (6, "alliance.png") })
            {
                Invoke(ui, "OpenPage", page);
                yield return Shot(name);
                Invoke(ui, "ClosePage");
            }

            Invoke(ui, "ToggleHome");
            yield return Shot("world.png", 4f);
        }

        private static void Invoke(GameUI ui, string method, params object[] args) =>
            typeof(GameUI).GetMethod(method, Private).Invoke(ui, args);

        private IEnumerator Shot(string name, float settle = 2.5f)
        {
            yield return new WaitForSecondsRealtime(settle);
            yield return new WaitForEndOfFrame();
            var image = Grab();
            File.WriteAllBytes(Path.Combine(_output, name), image.EncodeToPNG());
            Destroy(image);
        }

        private static Texture2D Grab()
        {
            var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            image.Apply();
            return image;
        }
    }
}
