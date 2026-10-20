using System.Text.Json;
using AbyssKingdom.Server;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;
using AbyssKingdom.Playtesting;
using Microsoft.Extensions.DependencyInjection.Extensions;

if (args.Any(a => a == "--simulate" || a.StartsWith("--population") || a.StartsWith("--days")))
{
    await PopulationSimulation.RunAsync(PopulationOptions.Parse(args));
    return;
}

long start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var clock = LocalClock.Create("Dev", "InMemory", start);
var app = GameServer.Build(new[] { "--Auth:Mode=Dev", "--Storage:Mode=InMemory", "--Logging:LogLevel:Default=Warning" },
    s => s.Replace(ServiceDescriptor.Singleton<IClock>(clock)), "http://127.0.0.1:5090");
var repository = new DirectoryInfo(AppContext.BaseDirectory);
while (repository != null && !File.Exists(Path.Combine(repository.FullName, "CLAUDE.md"))) repository = repository.Parent;
if (repository == null) throw new InvalidOperationException("Repository root not found.");
string output = Path.Combine(repository.FullName, "artifacts", "ai-playtest"); Directory.CreateDirectory(output);
if (args.Contains("--fixes-1")) { output += "-fixes-1"; Directory.CreateDirectory(output); }
await app.StartAsync();
if (args.Contains("--host"))
{
    var accelerate = Task.Run(async () => { while (!app.Lifetime.ApplicationStopping.IsCancellationRequested) { clock.Advance(10_000); await Task.Delay(1000); } });
    await app.WaitForShutdownAsync(); return;
}
try
{
    var profiles = args.FirstOrDefault(a => a.StartsWith("--profiles="))?.Substring(11).Split(',') ?? new[] { "quest", "diligent", "random", "offline" };
    if (args.Contains("--revised")) { output += "-queues"; Directory.CreateDirectory(output); }
    if (args.Contains("--queue-check")) { output += "-offline"; Directory.CreateDirectory(output); }
    foreach (string profile in profiles)
    {
        long beginning = clock.NowMs; double? completedMinute = null; int actions = 0, idle = 0;
        var timeline = new List<object>(); var player = new SocketPlayer(); await player.LoginAsync(profile);
        for (int turn = 0; turn < 120; turn++)
        {
            int beforeActions = actions;
            if (profile == "offline" && turn == 30)
            {
                if (args.Contains("--queue-check") && player.City.Upgrade == null)
                {
                    var candidate = CityConfig.AllBuildings.FirstOrDefault(b => CityLogic.CanUpgrade(player.City, b) == UpgradeResult.Ok);
                    if (CityLogic.CanUpgrade(player.City, candidate) == UpgradeResult.Ok)
                    {
                        await player.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = candidate });
                        await player.SendAsync(MessageTypes.CityGet, null, MessageTypes.CityState);
                    }
                }
                timeline.Add(new { minute = 15, action = "before offline", city = player.City });
                await player.DisposeAsync(); clock.Advance(8 * 3_600_000); await Task.Delay(1100);
                player = new SocketPlayer(); await player.LoginAsync(profile);
                timeline.Add(new { minute = 15, action = "offline 8 hours, reconnect", city = player.City });
            }
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var intent = AiPlaytestPolicy.Next(player.City, profile == "offline" ? "diligent" : profile, turn * 6 + attempt, clock.NowMs);
                if (intent == null) break;
                var response = await player.SendAsync(intent.Type, intent.Data);
                await player.SendAsync(MessageTypes.CityGet, null, MessageTypes.CityState); actions++;
                timeline.Add(new { minute = turn / 2.0, intent.Type, intent.Data, intent.Reason, response, city = player.City });
                if (player.City.QuestIndex == QuestConfig.Chapter1.Length && completedMinute == null) completedMinute = turn / 2.0;
                if (response.GetProperty("type").GetString() == MessageTypes.Error) break;
            }
            if (actions == beforeActions) idle++;
            clock.Advance(30_000);
            await Task.Delay(1100); // Allow the real server tick to settle marches; no direct calls into game services.
            await player.SendAsync(MessageTypes.CityGet, null, MessageTypes.CityState);
            if (profile == "offline" && turn == 59) break;
        }
        await player.SendAsync(MessageTypes.FeedbackSend, new FeedbackRequest { Text = "AI protocol playtest " + profile, Screen = "world" });
        var summary = new { profile, activeMinutes = profile == "offline" ? 30 : 60, chapterCompletedMinute = completedMinute, actions, idleTurns = idle,
            buildings = player.City.Buildings, questIndex = player.City.QuestIndex, resources = player.City.Resources, troops = player.City.Troops, wounded = player.City.Wounded };
        File.WriteAllText(Path.Combine(output, profile + "-summary.json"), JsonSerializer.Serialize(summary, Json.Options));
        File.WriteAllText(Path.Combine(output, profile + "-timeline.json"), JsonSerializer.Serialize(timeline, Json.Options));
        await player.DisposeAsync(); Console.WriteLine(JsonSerializer.Serialize(summary, Json.Options));
    }
    using var http = new HttpClient();
    http.DefaultRequestHeaders.Add("X-Admin-Token", Environment.GetEnvironmentVariable("Admin__Token"));
    foreach (var endpoint in new[] { "summary", "errors", "feedback" })
        File.WriteAllText(Path.Combine(output, "gm-" + endpoint + ".json"), await http.GetStringAsync("http://127.0.0.1:5090/admin/playtest/" + endpoint));
    foreach (var profile in profiles)
        File.WriteAllText(Path.Combine(output, profile + "-events.json"), await http.GetStringAsync("http://127.0.0.1:5090/admin/playtest/events?playerId=dev-aiplay-" + profile));
}
finally { await app.StopAsync(); await app.DisposeAsync(); }
