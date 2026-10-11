using System;
using System.Collections;
using System.IO;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace AbyssKingdom.Client.Diagnostics
{
    /// <summary>Opt-in local Dev art verification; never runs for ordinary players.</summary>
    public sealed class BuildingPolishCheck : MonoBehaviour
    {
        private string _output;
        private int _failures, _checked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-buildingpolishcheck"), s = Array.IndexOf(args, "-server");
            if (i < 0 || i + 1 >= args.Length || s < 0 || s + 1 >= args.Length ||
                Array.IndexOf(args, "-devlogin") < 0 ||
                !Uri.TryCreate(args[s + 1], UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            var go = new GameObject("BuildingPolishCheck");
            DontDestroyOnLoad(go);
            go.AddComponent<BuildingPolishCheck>()._output = Path.GetFullPath(args[i + 1]);
        }

        private void Capture(string name)
        {
            var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG()); Destroy(image);
        }

        private void Check(GameObject model, string name, bool expectSmoke)
        {
            _checked++;
            bool ok = model != null;
            if (ok)
            {
                var meshes = model.GetComponentsInChildren<MeshRenderer>();
                ok = meshes.Length > 0 && meshes.All(r => r.sharedMaterial != null &&
                    r.sharedMaterial.mainTexture != null && r.sharedMaterial.shader.name == "AbyssKingdom/Building3D");
                foreach (var r in meshes.Where(r => r.name.StartsWith("FlagCloth_", StringComparison.Ordinal)))
                    ok &= r.sharedMaterial.GetFloat("_Cloth") > 0;
                foreach (var r in meshes.Where(r => !r.name.StartsWith("FlagCloth_", StringComparison.Ordinal)))
                    ok &= r.sharedMaterial.GetFloat("_Cloth") == 0;
                var sockets = model.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("SmokeSocket_", StringComparison.Ordinal)).ToArray();
                ok &= (sockets.Length > 0) == expectSmoke;
                if (expectSmoke)
                {
                    var smoke = model.transform.parent.GetComponentsInChildren<ParticleSystem>();
                    ok &= sockets.All(s => smoke.Any(p => Vector3.Distance(s.position, p.transform.position) < .001f));
                }
                Debug.Log($"POLISH_CHECK {name} {(ok ? "PASS" : "FAIL")} meshes={meshes.Length} sockets={sockets.Length} scale={model.transform.lossyScale}");
            }
            if (!ok) { _failures++; Debug.LogError("POLISH_CHECK failed " + name); }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output);
            float deadline = Time.realtimeSinceStartup + 90;
            while (GameClient.Instance == null || !GameClient.Instance.IsLoggedIn || WorldScene.Instance == null)
            {
                if (Time.realtimeSinceStartup > deadline) { Debug.LogError("POLISH_CHECK login timeout"); Application.Quit(2); yield break; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(5);
            var world = WorldScene.Instance;
            world.SetMode(WorldScene.Mode.City);
            yield return new WaitForSecondsRealtime(2);
            yield return new WaitForEndOfFrame(); Capture("city-live");
            world.SetMode(WorldScene.Mode.Surface);
            var city = GameClient.Instance.City;
            var own = SurfaceView.CityPos(city.Surface.X, city.Surface.Y);
            world.FocusOn(WorldScene.Mode.Surface, own);
            yield return new WaitForSecondsRealtime(1);
            world.Cam.transform.position = own - world.Cam.transform.forward * 3.3f;
            yield return new WaitForEndOfFrame(); Capture("world-city-live");

            // The real-game shots keep the HUD; comparison plates must expose all three tiers.
            foreach (var document in UnityEngine.Object.FindObjectsByType<UIDocument>())
                document.rootVisualElement.style.display = DisplayStyle.None;

            var stage = new GameObject("PolishStage"); stage.transform.position = new Vector3(1000, 0, 1000);
            var camera = new GameObject("PolishCamera").AddComponent<Camera>();
            camera.cullingMask = 1 << WorldScene.Layer; camera.depth = 20;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.10f,.13f,.17f);
            camera.orthographic = true; camera.orthographicSize = 1.45f;
            camera.transform.rotation = Quaternion.Euler(52, 0, 0);
            camera.transform.position = stage.transform.position + Vector3.up * .6f - camera.transform.forward * 8;
            foreach (var family in new[] { "Keep", "Sawmill", "Farm", "Warehouse", "Barracks", "Hospital", "CrystalMine", "CityWall" })
            {
                var batch = new GameObject(family); batch.transform.SetParent(stage.transform, false);
                for (int tier = 1; tier <= 3; tier++)
                {
                    string name = family + "_" + tier;
                    var holder = new GameObject(name); holder.transform.SetParent(batch.transform, false);
                    holder.transform.localPosition = new Vector3((tier-2)*1.6f,0,0);
                    bool wall = family == "CityWall";
                    var model = ModelLibrary.Spawn((wall ? "World3D/" : "City3D/") + name, holder.transform, Vector3.zero, wall ? .45f : 1f);
                    if (model != null) LivingModel.Attach(model, name, tier * 13);
                    Check(model, name, !wall && family != "Warehouse" && family != "CrystalMine");
                    if (wall)
                    {
                        var keep = ModelLibrary.Spawn("City3D/Keep_" + tier, holder.transform, Vector3.zero, .9f);
                        if (keep != null) LivingModel.Attach(keep, "Keep_" + tier, tier * 13);
                    }
                }
                yield return new WaitForSecondsRealtime(3);
                yield return new WaitForEndOfFrame(); Capture(family + "-a");
                yield return new WaitForSecondsRealtime(.35f);
                yield return new WaitForEndOfFrame(); Capture(family + "-b");
                Destroy(batch); yield return null;
            }
            // Isolate one flag: a pixel change now proves actual GPU cloth motion, not smoke or UI animation.
            var flagModel = ModelLibrary.Spawn("City3D/Keep_1", stage.transform, Vector3.zero, 1f);
            LivingModel.Attach(flagModel, "Keep_1", 0);
            var flagRenderer = flagModel.GetComponentsInChildren<MeshRenderer>()
                .Where(r => r.name.StartsWith("FlagCloth_", StringComparison.Ordinal)).OrderByDescending(r => r.bounds.center.y).First();
            foreach (var r in stage.GetComponentsInChildren<Renderer>()) r.enabled = r == flagRenderer;
            camera.orthographicSize = .11f;
            camera.transform.position = flagRenderer.bounds.center - camera.transform.forward * 2;
            yield return new WaitForEndOfFrame(); Capture("flag-motion-a");
            var first = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            first.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0); first.Apply();
            var pixels = first.GetPixels32(); Destroy(first);
            yield return new WaitForSecondsRealtime(.31f);
            yield return new WaitForEndOfFrame(); Capture("flag-motion-b");
            var second = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            second.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0); second.Apply();
            var after = second.GetPixels32(); Destroy(second);
            int changed = 0;
            for (int p=0;p<pixels.Length;p++)
                if (Math.Abs(pixels[p].r-after[p].r)+Math.Abs(pixels[p].g-after[p].g)+Math.Abs(pixels[p].b-after[p].b)>12) changed++;
            if (changed < 100) _failures++;
            Debug.Log($"POLISH_CHECK flag_motion changed_pixels={changed} {(changed>=100 ? "PASS" : "FAIL")}");
            File.WriteAllText(Path.Combine(_output,"result.json"), $"{{\"status\":\"{(_failures==0 ? "PASS" : "FAIL")}\",\"models\":{_checked},\"failures\":{_failures}}}");
            Debug.Log($"POLISH_CHECK COMPLETE models={_checked} failures={_failures}");
            Application.Quit(_failures == 0 ? 0 : 2);
        }
    }
}
