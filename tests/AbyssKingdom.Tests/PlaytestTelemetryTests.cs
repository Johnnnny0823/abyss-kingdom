using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbyssKingdom.Tests;

public static class PlaytestTelemetryTests
{
    [Test]
    public static async Task Milestones_AreUniqueAfterRestart_AndMemoryStoreCopiesValues()
    {
        var clock = new ManualClock(1_700_000_000_000); var store = new InMemoryPlaytestStore(Json.Options);
        var city = CityState.CreateNew(clock.NowMs); city.Stats.TroopsTrained = 1; city.Stats.GatherTrips = 1; city.Stats.CampsWon = 1;
        using (var service = new PlaytestTelemetry(store, clock, NullLogger<PlaytestTelemetry>.Instance))
        {
            await service.StartAsync(default); service.Start("p", "領主", "s1");
            service.Observe("p", "領主", city); service.Observe("p", "領主", city);
            service.Record("p", "領主", "first.alliance"); service.Record("p", "領主", "first.alliance");
            clock.Advance(12_000); service.End("p", "s1"); await service.StopAsync(default);
        }
        var before = await store.ListAsync("p", default);
        Assert.Equal(4, before.Count(e => e.Kind.StartsWith("first.")));
        before[0].Data["displayName"] = "changed";
        Assert.True((await store.ListAsync("p", default)).All(e => PlaytestTelemetry.Text(e, "displayName") != "changed"), "Store owns copied events");
        using (var restarted = new PlaytestTelemetry(store, clock, NullLogger<PlaytestTelemetry>.Instance))
        {
            await restarted.StartAsync(default); restarted.Start("p", "領主", "s2");
            restarted.Observe("p", "領主", city); clock.Advance(3_000);
            Assert.Equal(15L, restarted.TotalSeconds("p"));
            restarted.End("p", "s1"); Assert.Equal(3L, restarted.SessionSeconds("p"));
            restarted.End("p", "s2"); await restarted.StopAsync(default);
        }
        Assert.Equal(4, (await store.ListAsync("p", default)).Count(e => e.Kind.StartsWith("first.")));
    }

    [Test]
    public static async Task Feedback_ValidationSlidingWindowAndReconnect_ErrorLimitsAndPrivacy()
    {
        var clock = new ManualClock(1_700_000_000_000); var store = new InMemoryPlaytestStore(Json.Options);
        using var service = new PlaytestTelemetry(store, clock, NullLogger<PlaytestTelemetry>.Instance);
        await service.StartAsync(default); service.Start("p", "領主", "s");
        Assert.Equal("InvalidFeedback", service.ValidateFeedback("p", " ", "city"));
        Assert.Equal("InvalidFeedback", service.ValidateFeedback("p", new string('x', 1001), "city"));
        Assert.Equal("InvalidFeedback", service.ValidateFeedback("p", "ok", "bogus"));
        for (int i = 0; i < 3; i++) Assert.True(service.ValidateFeedback("p", "ok", "city") == null, "Three accepted per minute");
        service.Start("p", "領主", "next");
        Assert.Equal("FeedbackRateLimited", service.ValidateFeedback("p", "ok", "world"));
        clock.Advance(60_000); Assert.True(service.ValidateFeedback("p", "ok", "world") == null, "Boundary releases quota");
        service.ClientError("p", "領主", "problem", @"C:\Users\Private\file.cs:123");
        service.ClientError("p", "領主", "problem", "same");
        clock.Advance(60_000); service.ClientError("p", "領主", "problem", "later");
        for (int i = 0; i < 30; i++) service.ClientError("p", "領主", "error " + i, "");
        await service.StopAsync(default);
        var errors = (await store.ListAsync("p", default)).Where(e => e.Kind == "client.error").ToList();
        Assert.Equal(20, errors.Count); Assert.Equal(2, errors.Count(e => PlaytestTelemetry.Text(e, "message") == "problem"));
        Assert.True(!PlaytestTelemetry.Text(errors[0], "stack").Contains("Private"), "No path in saved stack");
        string cleaned = PlaytestReports.Sanitize("token=secret\n10.2.3.4 mail@example.com\n/home/user/private.cs", 2000);
        Assert.True(!cleaned.Contains("secret\n") && !cleaned.Contains("10.2.3.4") && !cleaned.Contains("example.com") && !cleaned.Contains("/home/user"), "Privacy scrub");
    }

    public sealed class BrokenStore : IPlaytestStore
    {
        public Task AppendAsync(PlaytestEvent item, CancellationToken ct) => throw new IOException("simulated telemetry outage");
        public Task<List<PlaytestEvent>> ListAsync(string? playerId, CancellationToken ct) => Task.FromResult(new List<PlaytestEvent>());
    }
}
