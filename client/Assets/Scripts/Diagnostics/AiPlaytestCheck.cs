using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.Net;
using AbyssKingdom.Client.UI;
using AbyssKingdom.Client.World;
using AbyssKingdom.Playtesting;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace AbyssKingdom.Client.Diagnostics
{
    public sealed class AiPlaytestCheck : MonoBehaviour
    {
        private string _output;
        private static object Field(object o, string n) => o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(o);
        private static object Call(object o, string n, params object[] args) => o.GetType().GetMethod(n, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-aiplaytest"), server = Array.IndexOf(args, "-server");
            if (at < 0 || at + 1 >= args.Length || server < 0 || server + 1 >= args.Length || Array.IndexOf(args, "-devlogin") < 0 ||
                !Uri.TryCreate(args[server + 1], UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            var go = new GameObject("LocalAiPlaytest"); DontDestroyOnLoad(go);
            go.AddComponent<AiPlaytestCheck>()._output = Path.GetFullPath(args[at + 1]);
        }
        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output); float deadline = Time.realtimeSinceStartup + 90;
            while (GameClient.Instance == null || !GameClient.Instance.IsLoggedIn || GameClient.Instance.City == null)
            { if (Time.realtimeSinceStartup > deadline) { Application.Quit(2); yield break; } yield return null; }
            var game = GameClient.Instance; var ui = FindAnyObjectByType<GameUI>(); var world = WorldScene.Instance;
            yield return new WaitForSecondsRealtime(5); // Let the normal first-login UI create its intro before skipping it.
            var connection = (GameConnection)Field(game, "_conn");
            game.Notice += (text, error) => File.AppendAllText(Path.Combine(_output, "notices.jsonl"), JsonConvert.SerializeObject(new { atMs = game.ServerNowMs, text, error }) + "\n");
            var intro = FindAnyObjectByType<IntroSequence>(); if (intro != null) Call(intro, "Finish");
            yield return new WaitForSecondsRealtime(1);
            world.SetMode(WorldScene.Mode.City); yield return Capture(ui, "city");
            Call(ui, "ToggleHome"); yield return Capture(ui, "world"); Call(ui, "ToggleHome");
            string[] pages = { "heroes", "expedition", "barracks", "bag", "march", "hospital", "alliance" };
            for (int page = 0; page < pages.Length; page++)
            {
                Call(ui, "OpenPage", page); yield return Capture(ui, pages[page]);
                if (page == 3)
                {
                    int slots = ((VisualElement)Field(ui, "_itemList")).Query<VisualElement>(className: "item-slot").ToList().Count;
                    for (int n = 0; n < slots; n++)
                    {
                        // Selection rebuilds the grid, so reacquire the current visual element every time.
                        var slot = ((VisualElement)Field(ui, "_itemList")).Query<VisualElement>(className: "item-slot").ToList()[n];
                        using (var click = ClickEvent.GetPooled()) { click.target = slot; slot.SendEvent(click); }
                        yield return Capture(ui, "item-" + n);
                        File.AppendAllText(Path.Combine(_output, "icon-audit.jsonl"), JsonConvert.SerializeObject(new
                            { kind = "bag", name = ((Label)Field(ui, "_bagDetailName")).text, description = ((Label)Field(ui, "_bagDetailDesc")).text }) + "\n");
                    }
                }
                Call(ui, "ClosePage");
            }
            foreach (var building in CityConfig.AllBuildings)
            { Call(ui, "FocusBuilding", building); yield return Capture(ui, "building-" + building); }
            var auditRoot = ui.GetComponent<UIDocument>().rootVisualElement;
            int overlaysBefore = auditRoot.Query<VisualElement>(className: "overlay").ToList().Count;
            foreach (var icon in auditRoot.Query<VisualElement>(className: "icon-small").ToList())
            {
                using (var click = ClickEvent.GetPooled()) { click.target = icon; icon.SendEvent(click); }
            }
            yield return Capture(ui, "cost-icon-click");
            File.AppendAllText(Path.Combine(_output, "icon-audit.jsonl"), JsonConvert.SerializeObject(new
                { kind = "cost", modalOpened = auditRoot.Query<VisualElement>(className: "overlay").ToList().Count > overlaysBefore }) + "\n");
            foreach (var resource in CityConfig.AllResources)
            {
                Call(ui, "OpenResourceInfo", resource); yield return Capture(ui, "resource-" + resource);
                foreach (var overlay in ui.GetComponent<UIDocument>().rootVisualElement.Query<VisualElement>(className: "overlay").ToList()) overlay.RemoveFromHierarchy();
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-aiplaytest-fixes-1") >= 0)
                yield return FixesRegression(ui, game, connection, world);
            long beginning = game.ServerNowMs; int lastQuest = -1, turn = 0;
            while (Array.IndexOf(Environment.GetCommandLineArgs(), "-aiplaytest-smoke") < 0 && game.ServerNowMs - beginning < 60 * 60_000)
            {
                connection.Send(MessageTypes.CityGet); yield return new WaitForSecondsRealtime(.3f);
                var city = game.City; var quest = QuestLogic.Current(city);
                if (quest != null && city.QuestIndex != lastQuest)
                { lastQuest = city.QuestIndex; Call(ui, "GoToQuest", quest); yield return Capture(ui, "quest-" + quest.Id); }
                string profile = Array.IndexOf(Environment.GetCommandLineArgs(), "-aiplaytest-diligent") >= 0 ? "diligent" : "quest";
                var intent = AiPlaytestPolicy.Next(city, profile, turn++, game.ServerNowMs);
                if (intent != null)
                {
                    if (intent.Type == MessageTypes.CityUpgrade)
                    {
                        var type = ((UpgradeRequest)intent.Data).Building; Call(ui, "FocusBuilding", type);
                        yield return new WaitForSecondsRealtime(.3f);
                        var cards = (System.Collections.IDictionary)Field(ui, "_cards"); var button = (Button)Field(cards[type], "_button");
                        typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Invoke(button.clickable, new object[] { null, 0 });
                    }
                    else if (intent.Type == MessageTypes.ExpeditionBattle)
                    { Call(ui, "OpenPage", 1); yield return new WaitForSecondsRealtime(.3f); ((ExpeditionPage)Field(ui, "_expPage")).FightNow(); }
                    else connection.Send(intent.Type, intent.Data);
                    File.AppendAllText(Path.Combine(_output, "actions.jsonl"), JsonConvert.SerializeObject(new { minute = (game.ServerNowMs - beginning) / 60_000, intent.Type, intent.Reason, questIndex = city.QuestIndex }) + "\n");
                }
                yield return new WaitForSecondsRealtime(1);
            }
            Call(ui, "ClosePage");
            connection.Send(MessageTypes.MapRecall, new MapRecallRequest { March = long.MaxValue });
            yield return new WaitForSecondsRealtime(1); yield return Capture(ui, "error-toast");
            Call(ui, "OpenFeedback"); yield return new WaitForSecondsRealtime(.3f);
            var feedback = Field(ui, "_feedback"); ((TextField)Field(feedback, "_text")).value = "AI 試玩：請查看附帶的任務與畫面檢查報告。";
            yield return Capture(ui, "feedback-draft"); Call(feedback, "Send");
            Debug.LogError("AI_PLAYTEST_SAMPLE: harmless local error");
            yield return new WaitForSecondsRealtime(2); yield return Capture(ui, "feedback-received");
            File.WriteAllText(Path.Combine(_output, "final-city.json"), JsonConvert.SerializeObject(game.City));
            Debug.Log("AI_PLAYTEST_UI COMPLETE questIndex=" + game.City.QuestIndex); Application.Quit();
        }
        private void Check(bool passed, string name)
        {
            File.AppendAllText(Path.Combine(_output, "fixes-checks.jsonl"), JsonConvert.SerializeObject(new { name, passed }) + "\n");
            if (!passed) { Debug.LogError("PLAYTEST_FIX_CHECK FAILED: " + name); Application.Quit(2); }
        }

        private IEnumerator WaitForCity(GameConnection connection, Func<bool> ready)
        {
            float end = Time.realtimeSinceStartup + 120;
            while (!ready() && Time.realtimeSinceStartup < end)
            { connection.Send(MessageTypes.CityGet); yield return new WaitForSecondsRealtime(.5f); }
            Check(ready(), "server state reached before timeout");
        }

        private IEnumerator AuditCosts(GameUI ui, string context)
        {
            var root = ui.GetComponent<UIDocument>().rootVisualElement;
            var costs = context == "building" ? (VisualElement)Field(ui, "_cardHost") :
                (VisualElement)Field(Field(ui, context == "training" ? "_training" : "_hospital"), "Root");
            var icons = costs.Query<VisualElement>(className: "icon-small").ToList().Where(icon =>
            {
                for (var e = icon; e != null; e = e.parent) if (e.resolvedStyle.display == DisplayStyle.None) return false;
                return true;
            }).ToList();
            Check(icons.Count > 0, context + " has visible resource costs");
            foreach (var icon in icons)
            {
                using (var click = ClickEvent.GetPooled()) { click.target = icon; icon.SendEvent(click); }
                Check(root.Query<VisualElement>(className: "overlay").ToList().Count == 1, context + " opens exactly one dialog");
                Check(icon.tooltip.StartsWith(root.Q<Label>(className: "res-info-name").text), context + " names the clicked resource");
                yield return Capture(ui, "cost-" + context + "-" + icons.IndexOf(icon));
                foreach (var overlay in root.Query<VisualElement>(className: "overlay").ToList()) overlay.RemoveFromHierarchy();
            }
        }

        private IEnumerator BuildForCheck(GameClient game, GameConnection connection, BuildingType building)
        {
            connection.Send(MessageTypes.CityUpgrade, new UpgradeRequest { Building = building });
            yield return new WaitForSecondsRealtime(.5f);
            if (game.City.Upgrade != null && game.City.Upgrade.FinishAtMs > game.ServerNowMs)
                connection.Send(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = QueueType.Building });
            yield return WaitForCity(connection, () => game.City.GetLevel(building) > 0);
        }

        private IEnumerator FixesRegression(GameUI ui, GameClient game, GameConnection connection, WorldScene world)
        {
            Call(ui, "ClosePage");
            Call(ui, "FocusBuilding", BuildingType.Hospital);
            yield return new WaitForSecondsRealtime(.3f);
            Check(((Button)Field(ui, "_trainBtn")).resolvedStyle.display == DisplayStyle.None, "Hospital zero hides treatment entrance");
            yield return Capture(ui, "hospital-zero-card");
            yield return AuditCosts(ui, "building");

            world.FocusOn(WorldScene.Mode.City, new Vector3(5, 0, -5));
            typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(((Button)Field(Field(ui, "_quest"), "_go")).clickable, new object[] { null, 0 });
            yield return Capture(ui, "quest-focus");
            var position = world.Cam.WorldToViewportPoint(world.City.PositionOf(BuildingType.Keep));
            Check(position.x > .3f && position.x < .7f && position.y > .3f && position.y < .7f,
                "Quest navigation moves camera from distant pan to building");

            yield return BuildForCheck(game, connection, BuildingType.Barracks);
            Call(ui, "OpenPage", 2); yield return new WaitForSecondsRealtime(.3f);
            yield return AuditCosts(ui, "training");
            connection.Send(MessageTypes.CityTrain, new TrainRequest { Troop = TroopType.Shield, Count = 10 });
            yield return new WaitForSecondsRealtime(.5f);
            if (game.City.Training != null && game.City.Training.FinishAtMs > game.ServerNowMs)
                connection.Send(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = QueueType.Training });
            yield return WaitForCity(connection, () => game.City.GetTroops(TroopType.Shield) >= 10);
            Call(ui, "ClosePage");

            // A deliberately under-strength, legal march produces real wounded soldiers; no GM grants.
            var home = SurfaceLogic.CityCenter(game.City.Surface);
            var camp = SurfaceLogic.Features().Where(f => f.Type == FeatureType.MonsterCamp && f.Level == 2)
                .OrderBy(f => SurfaceLogic.Dist2(home.x, home.y, f.X, f.Y)).First();
            connection.Send(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, X = camp.X, Y = camp.Y,
                Troops = new Dictionary<TroopType, long> { [TroopType.Shield] = 5 } });
            yield return WaitForCity(connection, () => game.City.Surface.Marches.Count > 0);
            yield return WaitForCity(connection, () => game.City.Surface.Marches.Count == 0 && CityLogic.WoundedTotal(game.City) > 0);
            Check(game.City.Stats.CampsWon == 0, "Deliberately under-strength Lv.2 attack loses and produces real wounded");

            Call(ui, "GoToQuest", QuestConfig.Chapter1[8]);
            yield return Capture(ui, "recover-build-hospital");
            Check(((Button)Field(ui, "_hospitalBtn")).text == DisplayNames.BuildHospitalFirst, "Wounded HUD asks to build Hospital");
            Check((BuildingType?)Field(ui, "_selBuilding") == BuildingType.Hospital, "Wounded quest navigation selects Hospital plot");
            typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(((Button)Field(ui, "_hospitalBtn")).clickable, new object[] { null, 0 });
            Check((BuildingType?)Field(ui, "_selBuilding") == BuildingType.Hospital && world.Current == WorldScene.Mode.City,
                "Wounded HUD button selects the unbuilt Hospital plot");
            yield return BuildForCheck(game, connection, BuildingType.Hospital);
            Call(ui, "GoToQuest", QuestConfig.Chapter1[8]);
            yield return Capture(ui, "recover-heal");
            Check((int)Field(ui, "_page") == 5, "Wounded quest navigation opens treatment");
            foreach (var slider in ui.GetComponent<UIDocument>().rootVisualElement.Query<SliderInt>().ToList()) slider.value = slider.highValue;
            yield return new WaitForSecondsRealtime(.3f);
            yield return AuditCosts(ui, "healing");
            connection.Send(MessageTypes.CityHeal, new HealRequest { Troops = new Dictionary<TroopType, long>(game.City.Wounded) });
            yield return new WaitForSecondsRealtime(.5f);
            if (game.City.Healing != null && game.City.Healing.FinishAtMs > game.ServerNowMs)
                connection.Send(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = QueueType.Healing });
            yield return WaitForCity(connection, () => game.City.Healing == null && CityLogic.WoundedTotal(game.City) == 0);
            Call(ui, "GoToQuest", QuestConfig.Chapter1[8]);
            yield return Capture(ui, "recover-train");
            Check((int)Field(ui, "_page") == 2, "After treatment, under-fifty army navigates to training");
            connection.Send(MessageTypes.CityTrain, new TrainRequest { Troop = TroopType.Shield, Count = 50 });
            yield return new WaitForSecondsRealtime(.5f);
            yield return WaitForCity(connection, () => game.City.Training == null || game.City.Training.FinishAtMs - game.ServerNowMs <= CityConfig.FreeSpeedupMs);
            if (game.City.Training != null && game.City.Training.FinishAtMs > game.ServerNowMs)
                connection.Send(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = QueueType.Training });
            yield return WaitForCity(connection, () => game.City.GetTroops(TroopType.Shield) >= 50);
            Call(ui, "GoToQuest", QuestConfig.Chapter1[8]);
            yield return Capture(ui, "recover-ready-camp");
            Check(world.Current == WorldScene.Mode.Surface, "Recovered army navigates to camp again");
            Call(ui, "ClosePage"); Call(ui, "FocusBuilding", BuildingType.Keep);
            yield return Capture(ui, "city-nameplates");
            Call(ui, "ToggleHome"); yield return Capture(ui, "world-nameplates"); Call(ui, "ToggleHome");
        }

        private IEnumerator Capture(GameUI ui, string name)
        {
            var panel = ui.GetComponent<UIDocument>().panelSettings; var rt = RenderTexture.GetTemporary(1280, 800, 24, RenderTextureFormat.ARGB32);
            panel.targetTexture = rt; panel.clearColor = true; panel.colorClearValue = Color.clear;
            yield return new WaitForSecondsRealtime(.5f); yield return new WaitForEndOfFrame();
            var cam = WorldScene.Instance.Cam; var oldTarget = cam.targetTexture;
            var scene = RenderTexture.GetTemporary(1280, 800, 24); cam.targetTexture = scene; cam.Render(); cam.targetTexture = oldTarget;
            var prior = RenderTexture.active; var tex = new Texture2D(1280, 800, TextureFormat.RGBA32, false);
            RenderTexture.active = scene; tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); tex.Apply(); var pixels = tex.GetPixels32();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); tex.Apply(); var overlay = tex.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) { var a = overlay[i]; var b = pixels[i]; int w = a.a;
                pixels[i] = new Color32((byte)((a.r * w + b.r * (255 - w)) / 255), (byte)((a.g * w + b.g * (255 - w)) / 255), (byte)((a.b * w + b.b * (255 - w)) / 255), 255); }
            tex.SetPixels32(pixels); tex.Apply(); File.WriteAllBytes(Path.Combine(_output, name + ".png"), tex.EncodeToPNG()); Destroy(tex);
            RenderTexture.active = prior; panel.targetTexture = null; RenderTexture.ReleaseTemporary(rt); RenderTexture.ReleaseTemporary(scene);
        }
    }
}
