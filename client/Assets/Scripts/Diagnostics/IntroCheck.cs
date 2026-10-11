using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.UI;
using AbyssKingdom.Client.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace AbyssKingdom.Client.Diagnostics
{
    /// <summary>Opt-in local Dev regression check: -intro -introcheck output-directory.</summary>
    public sealed class IntroCheck : MonoBehaviour
    {
        [Serializable]
        private sealed class Result
        {
            public bool introObserved, toastVisibleDuringIntro, cityStableAfterIntro, skipRequested;
            public int framesChecked;
            public string status;
        }

        private string _output;
        private static object Field(GameUI ui, string name) => typeof(GameUI)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int output = Array.IndexOf(args, "-introcheck"), server = Array.IndexOf(args, "-server");
            if (output < 0 || output + 1 >= args.Length || server < 0 || server + 1 >= args.Length ||
                Array.IndexOf(args, "-devlogin") < 0 || Array.IndexOf(args, "-intro") < 0 ||
                !Uri.TryCreate(args[server + 1], UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            var go = new GameObject("LocalIntroCheck");
            DontDestroyOnLoad(go);
            go.AddComponent<IntroCheck>()._output = Path.GetFullPath(args[output + 1]);
        }

        private void Capture(string name)
        {
            var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(_output, name), image.EncodeToPNG());
            Destroy(image);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output);
            var result = new Result { cityStableAfterIntro = true };
            float deadline = Time.realtimeSinceStartup + 120, finishedAt = -1, startedAt = -1;
            bool capturedPlaying = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                var ui = FindAnyObjectByType<GameUI>();
                if (ui == null || GameClient.Instance == null || !GameClient.Instance.IsLoggedIn) continue;
                bool playing = (bool)Field(ui, "_introPlaying");
                if (playing)
                {
                    var toasts = (VisualElement)Field(ui, "_toasts");
                    bool visible = toasts.resolvedStyle.display != DisplayStyle.None && toasts.childCount > 0;
                    result.toastVisibleDuringIntro |= visible;
                    result.framesChecked++;
                    if (!result.introObserved)
                    {
                        Capture("intro-start.png");
                        Debug.Log("INTRO_CHECK START toastVisible=" + visible);
                        startedAt = Time.realtimeSinceStartup;
                    }
                    result.introObserved = true;
                    if (!capturedPlaying && Time.realtimeSinceStartup - startedAt >= 2)
                    {
                        Capture("intro-playing.png");
                        capturedPlaying = true;
                    }
                    if (!result.skipRequested && Array.IndexOf(Environment.GetCommandLineArgs(), "-introcheckskip") >= 0 &&
                        Time.realtimeSinceStartup - startedAt >= 1)
                    {
                        var intro = FindAnyObjectByType<IntroSequence>();
                        typeof(IntroSequence).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(intro, null);
                        result.skipRequested = true;
                    }
                }
                else if (result.introObserved)
                {
                    if (finishedAt < 0) { finishedAt = Time.realtimeSinceStartup; Capture("intro-finished.png"); }
                    result.cityStableAfterIntro &= WorldScene.Instance.Current == WorldScene.Mode.City;
                    if (Time.realtimeSinceStartup - finishedAt >= 5)
                    {
                        Capture("intro-after-five-seconds.png");
                        break;
                    }
                }
            }
            bool passed = result.introObserved && !result.toastVisibleDuringIntro && finishedAt >= 0 && result.cityStableAfterIntro;
            result.status = passed ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(_output, "result.json"), JsonUtility.ToJson(result, true));
            Debug.Log("INTRO_CHECK " + result.status + " toastVisible=" + result.toastVisibleDuringIntro +
                " cityStable=" + result.cityStableAfterIntro + " frames=" + result.framesChecked);
            Application.Quit(passed ? 0 : 2);
        }
    }
}
