using System.Text.Json;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AbyssKingdom.Tests;

public static partial class ServerIntegrationTests
{
    [Test]
    public static async Task Playtest_LoginQuestFeedbackErrorDisconnect_ExportsAuthoritativeSnapshot()
    {
        const string token = "playtest-test-only-admin-token";
        await using var server = new TestServer("--Admin:Token=" + token); await server.StartAsync();
        using var http = new HttpClient { BaseAddress = server.HttpUri };
        foreach (var path in new[] { "summary", "events?playerId=dev-telemetry", "errors", "feedback" })
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await http.GetAsync("/admin/playtest/" + path)).StatusCode);
        http.DefaultRequestHeaders.Add("X-Admin-Token", token);
        await using (var client = new TestClient())
        {
            await client.ConnectAsync(server.WsUri); await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:telemetry", DisplayName = "封測領主" });
            await client.ExpectAsync(MessageTypes.CityState);
            await client.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = BuildingType.Keep }); await client.ExpectAsync(MessageTypes.CityState);
            await client.SendAsync(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = QueueType.Building, Item = ItemType.SpeedupBuilding30m, Count = 1 }); await client.ExpectAsync(MessageTypes.CityState);
            await client.SendAsync(MessageTypes.QuestClaim, null); var city = await FreshCity(client);
            Assert.Equal(1, city.QuestIndex);
            server.Clock.Advance(90_000);
            await client.SendAsync(MessageTypes.FeedbackSend, new { text = "我不知道要往哪裡走", screen = "city", keepLevel = 99, totalPlaySeconds = 999 });
            Assert.True((await client.ExpectAsync(MessageTypes.FeedbackReceived)).GetProperty("data").GetProperty("accepted").GetBoolean(), "Feedback accepted");
            await client.SendAsync(MessageTypes.ClientError, new ClientErrorRequest { Message = "test fault", Stack = @"at Check in C:\Users\Someone\file.cs:line 1" });
            await client.SendAsync(MessageTypes.ClientError, new ClientErrorRequest { Message = "test fault", Stack = "again" });
            await client.SendAsync(MessageTypes.Ping, null); await client.ExpectAsync(MessageTypes.Pong);
            var summary = JsonDocument.Parse(await http.GetStringAsync("/admin/playtest/summary")).RootElement[0];
            Assert.Equal(90L, summary.GetProperty("totalPlaySeconds").GetInt64());
            Assert.Equal(2, summary.GetProperty("keepLevel").GetInt32()); Assert.Equal(1, summary.GetProperty("questIndex").GetInt32());
            var feedback = JsonDocument.Parse(await http.GetStringAsync("/admin/playtest/feedback")).RootElement[0].GetProperty("data");
            Assert.Equal(2, feedback.GetProperty("keepLevel").GetInt32()); Assert.Equal(90L, feedback.GetProperty("sessionPlaySeconds").GetInt64());
            Assert.Equal(90L, feedback.GetProperty("totalPlaySeconds").GetInt64());
            var errors = JsonDocument.Parse(await http.GetStringAsync("/admin/playtest/errors")).RootElement[0];
            Assert.Equal(1, errors.GetProperty("count").GetInt32()); Assert.Equal(1, errors.GetProperty("players").GetInt32());
        }
        for (int i = 0; i < 100; i++)
        {
            var rows = await server.App.Services.GetRequiredService<PlaytestTelemetry>().EventsAsync("dev-telemetry", default);
            if (rows.Any(e => e.Kind == "session.end")) break;
            await Task.Delay(10);
        }
        var events = await server.App.Services.GetRequiredService<PlaytestTelemetry>().EventsAsync("dev-telemetry", default);
        Assert.Equal(90L, PlaytestTelemetry.Number(events.Single(e => e.Kind == "session.end"), "playSeconds"));
        Assert.Equal(1, events.Count(e => e.Kind == "quest.claim")); Assert.Equal(1, events.Count(e => e.Kind == "building.upgrade.start"));
        Assert.True(events.All(e => !PlaytestTelemetry.Text(e, "stack").Contains("Someone")), "Server strips file path");
        server.Clock.Advance(86_400_000);
        await using var nextDay = new TestClient(); await nextDay.ConnectAsync(server.WsUri);
        await nextDay.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:telemetry" }); await nextDay.ExpectAsync(MessageTypes.CityState);
        var nextSummary = JsonDocument.Parse(await http.GetStringAsync("/admin/playtest/summary")).RootElement[0];
        Assert.Equal(2, nextSummary.GetProperty("loginDays").GetInt32());
    }

    [Test]
    public static async Task Playtest_StorageFailureCannotBlockLoginOrUpgrade()
    {
        await using var server = new TestServer(s => s.Replace(ServiceDescriptor.Singleton<IPlaytestStore>(new PlaytestTelemetryTests.BrokenStore())));
        await server.StartAsync(); await using var client = new TestClient(); await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:telemetry-outage" }); await client.ExpectAsync(MessageTypes.CityState);
        await client.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = BuildingType.Keep });
        var message = await client.ExpectAsync(MessageTypes.CityState);
        Assert.Equal(2, message.GetProperty("data").GetProperty("city").GetProperty("upgrade").GetProperty("targetLevel").GetInt32());
    }
}
