using System;
using System.Collections;
using System.IO;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;
using AbyssKingdom.Shared.City;
using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>Opt-in local Dev art checks: -devlogin -server ws://127.0.0.1:5090/ws -artcapture folder.</summary>
    public sealed class WorldModelCapture : MonoBehaviour
    {
        private string _output;

        private void CaptureFrame(string name)
        {
            // Opt-in offscreen capture remains usable when Windows hides the player swap chain.
            bool offscreen = Array.IndexOf(Environment.GetCommandLineArgs(), "-artcapture-offscreen") >= 0;
            var camera = WorldScene.Instance.Cam;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            RenderTexture target = null;
            if (offscreen)
            {
                target = new RenderTexture(Screen.width, Screen.height, 24);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
            }
            var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(_output, name), image.EncodeToPNG());
            Destroy(image);
            if (offscreen)
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Destroy(target);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-artcapture");
            int s = Array.IndexOf(args, "-server");
            if (i < 0 || i + 1 >= args.Length || Array.IndexOf(args, "-devlogin") < 0 ||
                s < 0 || s + 1 >= args.Length || !Uri.TryCreate(args[s + 1], UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            var go = new GameObject("LocalArtCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<WorldModelCapture>()._output = Path.GetFullPath(args[i + 1]);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output);
            float deadline = Time.realtimeSinceStartup + 60;
            while (GameClient.Instance == null || !GameClient.Instance.IsLoggedIn || WorldScene.Instance == null)
            {
                if (Time.realtimeSinceStartup > deadline) { Debug.LogError("ART_CHECK Dev login timed out"); Application.Quit(2); yield break; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(5);
            var world = WorldScene.Instance;
            world.EndCinematic();
            world.SetVisible(true);
            world.SetMode(WorldScene.Mode.Surface);
            var city = GameClient.Instance.City;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-artcapture-offscreen") >= 0)
            {
                // Hidden windows may pause UI Toolkit repainting; apply the real server snapshot explicitly.
                var pins = GameClient.Instance.Cities;
                if (pins.Count == 0) pins = new List<CityPin> { new CityPin
                {
                    X = city.Surface.X, Y = city.Surface.Y, Name = GameClient.Instance.PlayerName, Mine = true,
                    KeepLevel = city.GetLevel(BuildingType.Keep), Shielded = PvpLogic.IsShielded(city, GameClient.Instance.ServerNowMs)
                } };
                world.Surface.SetCities(pins, null, false);
                world.Surface.SetMarches(city.Surface.Marches, GameClient.Instance.ServerNowMs);
            }
            var own = SurfaceView.CityPos(city.Surface.X, city.Surface.Y);
            var camera = world.GetComponentInChildren<Camera>();
            world.FocusOn(WorldScene.Mode.Surface, own);
            yield return new WaitForSecondsRealtime(1);
            camera.transform.position = own - camera.transform.forward * 3.3f;
            yield return new WaitForEndOfFrame();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-artcapture-offscreen") >= 0)
            {
                world.EndCinematic();
                world.SetMode(WorldScene.Mode.Surface);
                world.Surface.Cull(own, 3.3f, force: true);
                camera.transform.rotation = Quaternion.Euler(52, 0, 0);
                camera.transform.position = own - camera.transform.forward * 3.3f;
            }
            CaptureFrame("world-city.png");
            yield return null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-artcapture-offscreen") >= 0)
            {
                world.Surface.SetTerritories(GameClient.Instance.TerritoryFlags, GameClient.Instance.Alliance?.MyAllianceId,
                    GameClient.Instance.ServerNowMs);
                world.Surface.Cull(own, 3.3f, force: true);
                camera.transform.rotation = Quaternion.Euler(52, 0, 0);
                camera.transform.position = own - camera.transform.forward * 3.3f;
                CaptureFrame("world-territory.png");
                // Real authority-provided city pins, including allied and foreign fixture accounts.
                var pins = GameClient.Instance.Cities;
                if (pins.Count > 1)
                {
                    var visible = pins.Where(p => SurfaceLogic.Dist2(p.X, p.Y, city.Surface.X, city.Surface.Y) < 2000).ToList();
                    if (visible.Count > 1)
                    {
                        var center = Vector3.zero;
                        foreach (var pin in visible) center += SurfaceView.CityPos(pin.X, pin.Y);
                        center /= visible.Count;
                        float distance = Mathf.Max(3.3f, visible.Max(p => Vector3.Distance(center, SurfaceView.CityPos(p.X, p.Y))) * 2.5f);
                        world.Surface.SetCities(pins, null, false);
                        world.FocusOn(WorldScene.Mode.Surface, center);
                        yield return new WaitForSecondsRealtime(1);
                        world.EndCinematic(); world.SetMode(WorldScene.Mode.Surface);
                        world.Surface.Cull(center, distance, force: true);
                        camera.transform.rotation = Quaternion.Euler(52, 0, 0);
                        camera.transform.position = center - camera.transform.forward * distance;
                        CaptureFrame("world-alliance-colors.png");
                        foreach (var pin in visible)
                            Debug.Log($"S6_CITY_COLOR name={pin.Name} mine={pin.Mine} allied={pin.Allied} color={ColorUtility.ToHtmlStringRGB(SurfaceView.CityColor(pin))}");
                    }
                }
                var rallyMarch = city.Surface.Marches.FirstOrDefault(m => m.RallyId != null && m.RallyTotalTroops > 0 && !m.Returning);
                if (rallyMarch != null)
                {
                    var tile = MarchLogic.CurrentTile(rallyMarch, GameClient.Instance.ServerNowMs);
                    var rallyPos = SurfaceView.TilePos(tile.x, tile.y);
                    world.FocusOn(WorldScene.Mode.Surface, rallyPos);
                    yield return new WaitForSecondsRealtime(1);
                    world.EndCinematic(); world.SetMode(WorldScene.Mode.Surface);
                    world.Surface.SetMarches(city.Surface.Marches, GameClient.Instance.ServerNowMs);
                    world.Surface.Cull(rallyPos, 3.3f, force: true);
                    camera.transform.rotation = Quaternion.Euler(52, 0, 0);
                    camera.transform.position = rallyPos - camera.transform.forward * 3.3f;
                    CaptureFrame("world-rally.png");
                }
            }

            // Real map features at the production tile scale, after normal chunk culling.
            foreach (var type in new[] { FeatureType.ResourceField, FeatureType.MonsterCamp, FeatureType.AbyssGate })
            {
                var feature = SurfaceLogic.Features().Where(f => f.Type == type)
                    .OrderBy(f => SurfaceLogic.Dist2(city.Surface.X, city.Surface.Y, f.X, f.Y)).First();
                var p = SurfaceView.TilePos(feature.X, feature.Y);
                world.FocusOn(WorldScene.Mode.Surface, p);
                yield return new WaitForSecondsRealtime(.6f);
                camera.transform.position = p - camera.transform.forward * (type == FeatureType.AbyssGate ? 5f : 3.3f);
                yield return new WaitForEndOfFrame();
                CaptureFrame("world-" + type + ".png");
                yield return null;
            }
            var pass = SurfaceLogic.Pass(1, 0);
            var passPos = SurfaceView.TilePos(pass.Item1, pass.Item2);
            world.FocusOn(WorldScene.Mode.Surface, passPos);
            yield return new WaitForSecondsRealtime(.6f);
            camera.transform.position = passPos - camera.transform.forward * 7f;
            yield return new WaitForEndOfFrame();
            CaptureFrame("world-pass.png");
            yield return null;

            // Imported FBX close-ups through the same Spawn() code as SurfaceView.
            var names = new[] { "City", "Tree_A", "Tree_B", "Mountain_A", "Mountain_B", "Field_Wood", "Field_Stone", "Field_Food", "Field_Crystal", "MonsterCamp", "PassTower", "AbyssGate", "Troop" };
            var sizes = new[] { new Vector3(2,2.5f,2), new Vector3(1.2f,1.85f,1.2f), new Vector3(1.2f,1.65f,1.2f), new Vector3(5,4.6f,5), new Vector3(5,5.7f,5), new Vector3(2,1.45f,2), new Vector3(2,.95f,2), new Vector3(2,.78f,2), new Vector3(2,1.45f,2), new Vector3(2,1.45f,2), new Vector3(1.5f,4,1.5f), new Vector3(6,3,6), Vector3.one };
            var limits = new[] {3000,300,300,300,300,1500,1500,1500,1500,1500,1500,3000,800};
            var stage = new GameObject("ArtInspectionStage");
            stage.transform.position = new Vector3(1000, 0, 1000);
            var inspection = new GameObject("ArtInspectionCamera").AddComponent<Camera>();
            inspection.cullingMask = 1 << WorldScene.Layer;
            inspection.backgroundColor = new Color(.065f, .085f, .11f);
            inspection.clearFlags = CameraClearFlags.SolidColor;
            inspection.orthographic = true;
            inspection.transform.rotation = Quaternion.Euler(35, -25, 0);
            var rt = new RenderTexture(960, 960, 24);
            inspection.targetTexture = rt;
            int checkedCount = 0, failed = 0;
            for (int n = 0; n < names.Length; n++)
            {
                var go = ModelLibrary.Spawn("World/" + names[n], stage.transform, Vector3.zero, .2f);
                if (go == null) continue; // Allows incremental art batches.
                var renderers = go.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                int totalTris = go.GetComponentsInChildren<MeshFilter>().Sum(m => Enumerable.Range(0, m.sharedMesh.subMeshCount).Sum(k => (int)m.sharedMesh.GetIndexCount(k) / 3));
                int materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().Count();
                bool ok = (bounds.size - sizes[n] * .2f).magnitude < .003f && Math.Abs(bounds.min.y) < .002f &&
                    Math.Abs(bounds.center.x - 1000) < .002f && Math.Abs(bounds.center.z - 1000) < .002f && totalTris <= limits[n] && materials <= 4 &&
                    renderers.All(r => r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported));
                if (!ok) failed++;
                Debug.Log($"ART_CHECK {names[n]} {(ok ? "PASS" : "FAIL")} size={bounds.size:F4} bottom={bounds.min.y:F4} triangles={totalTris} materials={materials}");
                inspection.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * .79f;
                inspection.transform.position = bounds.center - inspection.transform.forward * 10;
                inspection.Render();
                RenderTexture.active = rt;
                var texture = new Texture2D(960, 960, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 960, 960), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(_output, names[n] + ".png"), texture.EncodeToPNG());
                RenderTexture.active = null;
                Destroy(texture);
                go.SetActive(false);
                Destroy(go);
                checkedCount++;
                yield return null;
            }
            // City-view buildings: 1 m = 1 city cell (0.5 world units). Grounded, centered, within budget.
            var cityNames = new System.Collections.Generic.List<string> { "Site" };
            foreach (var b in new[] { "Keep", "Sawmill", "Quarry", "Farm", "CrystalMine", "Warehouse", "Barracks", "Hospital" })
                for (int tier = 1; tier <= 3; tier++) cityNames.Add(b + "_" + tier);
            foreach (var cn in cityNames)
            {
                var go = ModelLibrary.Spawn("City/" + cn, stage.transform, Vector3.zero, .5f);
                if (go == null) continue;
                var renderers = go.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                int totalTris = go.GetComponentsInChildren<MeshFilter>().Sum(m => Enumerable.Range(0, m.sharedMesh.subMeshCount).Sum(k => (int)m.sharedMesh.GetIndexCount(k) / 3));
                float width = cn.StartsWith("Keep") ? 1.95f : 1.45f;
                bool ok = Math.Abs(bounds.min.y) < .002f && Math.Abs(bounds.center.x - 1000) < .003f && Math.Abs(bounds.center.z - 1000) < .003f &&
                    Math.Abs(bounds.size.x - width) < .003f && totalTris <= 2000;
                if (!ok) failed++;
                Debug.Log($"ART_CHECK City/{cn} {(ok ? "PASS" : "FAIL")} size={bounds.size:F4} bottom={bounds.min.y:F4} triangles={totalTris}");
                inspection.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * .79f;
                inspection.transform.position = bounds.center - inspection.transform.forward * 10;
                inspection.Render();
                RenderTexture.active = rt;
                var texture = new Texture2D(960, 960, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 960, 960), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(_output, "City_" + cn + ".png"), texture.EncodeToPNG());
                RenderTexture.active = null;
                Destroy(texture);
                go.SetActive(false);
                Destroy(go);
                checkedCount++;
                yield return null;
            }

            // The real city view through the normal camera.
            world.SetMode(WorldScene.Mode.City);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return new WaitForEndOfFrame();
            CaptureFrame("city-view.png");
            yield return null;

            Debug.Log($"ART_CHECK COMPLETE models={checkedCount} failures={failed}");
            inspection.targetTexture = null;
            rt.Release();
            Destroy(rt);
            Destroy(stage);
            Destroy(inspection.gameObject);
            yield return new WaitForSecondsRealtime(1);
            Application.Quit(failed == 0 ? 0 : 2);
        }
    }
}
