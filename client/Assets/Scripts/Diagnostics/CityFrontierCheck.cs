using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.UI;
using AbyssKingdom.Client.World;
using UnityEngine;

namespace AbyssKingdom.Client.Diagnostics
{
    /// <summary>Local Dev visual check for the city fog frontier: -devlogin -cityfogcheck output-directory.</summary>
    public sealed class CityFrontierCheck : MonoBehaviour
    {
        private string _output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int output = Array.IndexOf(args, "-cityfogcheck"), server = Array.IndexOf(args, "-server");
            if (output < 0 || output + 1 >= args.Length || server < 0 || server + 1 >= args.Length ||
                Array.IndexOf(args, "-devlogin") < 0 || !Uri.TryCreate(args[server + 1], UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            var go = new GameObject("LocalCityFrontierCheck");
            DontDestroyOnLoad(go);
            go.AddComponent<CityFrontierCheck>()._output = Path.GetFullPath(args[output + 1]);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output);
            float deadline = Time.realtimeSinceStartup + 120;
            while (GameClient.Instance == null || !GameClient.Instance.IsLoggedIn || WorldScene.Instance == null || FindAnyObjectByType<GameUI>() == null)
            {
                if (Time.realtimeSinceStartup > deadline) { Debug.LogError("CITYFOG_CHECK login/intro timeout"); Application.Quit(2); yield break; }
                yield return null;
            }
            // 新帳號登入後才開始播開場：先等一下，再等開場播完
            yield return new WaitForSecondsRealtime(3);
            while (FindAnyObjectByType<IntroSequence>() != null) yield return null;
            var w = WorldScene.Instance;
            w.SetMode(WorldScene.Mode.City);
            yield return Shot(w, Vector3.zero, "city_center.png");
            yield return Shot(w, new Vector3(8, 0, 0), "zone_east.png");
            yield return Shot(w, new Vector3(0, 0, 8), "cliff_north.png");
            // 點東側擴張區：應該跳出說明視窗
            typeof(GameUI).GetMethod("OpenFrontierInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(FindAnyObjectByType<GameUI>(), new object[] { 28, 10 });
            yield return Shot(w, new Vector3(8, 0, 0), "zone_east_info.png");
            Debug.Log("CITYFOG_CHECK COMPLETE");
            Application.Quit(0);
        }

        private IEnumerator Shot(WorldScene w, Vector3 focus, string name)
        {
            w.FocusOn(WorldScene.Mode.City, focus);
            yield return new WaitForSecondsRealtime(2.5f);
            yield return new WaitForEndOfFrame();
            var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(_output, name), image.EncodeToPNG());
            Destroy(image);
        }
    }
}
