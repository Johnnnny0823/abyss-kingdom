using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Tests;

public static class AllianceChatTests
{
    private static async Task<(AllianceService Service, InMemoryAllianceStore Store, ManualClock Clock)> Setup()
    {
        var store = new InMemoryAllianceStore(Json.Options);
        await store.SaveAsync(new AllianceDirectory { Alliances = new()
        {
            new AllianceRecord { Id = "a", Members = new()
            {
                new AllianceMemberRecord { PlayerId = "leader", DisplayName = "Leader", Role = AllianceRole.Leader },
                new AllianceMemberRecord { PlayerId = "officer", DisplayName = "Officer", Role = AllianceRole.Officer },
                new AllianceMemberRecord { PlayerId = "member", DisplayName = "Member", Role = AllianceRole.Member }
            } }
        } }, CancellationToken.None);
        var clock = new ManualClock(1_000_000);
        return (new AllianceService(store, clock), store, clock);
    }

    [Test]
    public static async Task Post_DeduplicatesPersistsAndScopesHistory()
    {
        var (service, store, clock) = await Setup();
        string op = Guid.NewGuid().ToString();
        var first = await service.PostChatAsync("member", op, " hello ", CancellationToken.None);
        Assert.Equal(AllianceResult.Ok, first.Result);
        Assert.Equal(1L, first.Message!.Sequence);
        Assert.Equal("hello", first.Message.Text);
        clock.Advance(2_000);
        var retry = await service.PostChatAsync("member", op, "changed", CancellationToken.None);
        Assert.Equal(1L, retry.Message!.Sequence);
        Assert.Equal(1, (await store.LoadAsync(CancellationToken.None)).Alliances.Single().Chat.Count);
        Assert.Equal(AllianceResult.NotMember, (await service.ReadChatAsync("outsider", 0, CancellationToken.None)).Result);
        var history = await service.ReadChatAsync("officer", 0, CancellationToken.None);
        Assert.Equal(1, history.Messages.Count);
        Assert.Equal(0, (await service.ReadChatAsync("officer", 1, CancellationToken.None)).Messages.Count);
        Assert.Equal(AllianceResult.InvalidMessage,
            (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "\u0001", CancellationToken.None)).Result);
    }

    [Test]
    public static async Task Post_EnforcesTwoSecondsAndTwentyPerMinute()
    {
        var (service, _, clock) = await Setup();
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(AllianceResult.Ok,
                (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "m" + i, CancellationToken.None)).Result);
            Assert.Equal(AllianceResult.RateLimited,
                (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "too fast", CancellationToken.None)).Result);
            clock.Advance(2_000);
        }
        Assert.Equal(AllianceResult.RateLimited,
            (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "minute cap", CancellationToken.None)).Result);
        clock.Advance(20_000);
        Assert.Equal(AllianceResult.Ok,
            (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "after minute", CancellationToken.None)).Result);
    }

    [Test]
    public static async Task RateLimit_SurvivesHistoryEvictionByOtherMembers()
    {
        var (service, store, clock) = await Setup();
        Assert.Equal(AllianceResult.Ok,
            (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "first", CancellationToken.None)).Result);
        var directory = await store.LoadAsync(CancellationToken.None);
        directory.Alliances.Single().Chat.Clear(); // simulates the 100-message history window evicting this post
        await store.SaveAsync(directory, CancellationToken.None);
        clock.Advance(1_999);
        Assert.Equal(AllianceResult.RateLimited,
            (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "second", CancellationToken.None)).Result);
        clock.Advance(1);
        Assert.Equal(AllianceResult.Ok,
            (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "second", CancellationToken.None)).Result);
    }

    [Test]
    public static async Task Moderation_RespectsRankAndMuteBoundary()
    {
        var (service, store, clock) = await Setup();
        var post = await service.PostChatAsync("officer", Guid.NewGuid().ToString(), "notice", CancellationToken.None);
        Assert.Equal(AllianceResult.Forbidden, await service.HideChatAsync("officer", post.Message!.Sequence, CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.HideChatAsync("leader", post.Message.Sequence, CancellationToken.None));
        Assert.True(AllianceService.ToView((await service.ReadChatAsync("member", 0, CancellationToken.None)).Messages.Single()).Hidden, "hidden message must not be visible");
        Assert.Equal("", AllianceService.ToView((await service.ReadChatAsync("member", 0, CancellationToken.None)).Messages.Single()).Text);
        Assert.Equal(AllianceResult.Forbidden, await service.MuteChatAsync("officer", "leader", 1, CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.MuteChatAsync("leader", "officer", 1, CancellationToken.None));
        Assert.Equal(AllianceResult.Muted,
            (await service.PostChatAsync("officer", Guid.NewGuid().ToString(), "x", CancellationToken.None)).Result);
        clock.Advance(3_600_000);
        Assert.Equal(AllianceResult.Ok,
            (await service.PostChatAsync("officer", Guid.NewGuid().ToString(), "x", CancellationToken.None)).Result);
        Assert.Equal(AllianceResult.Ok, await service.ReportChatAsync("member", post.Message.Sequence, CancellationToken.None));
        Assert.Equal(AllianceResult.InvalidTarget, await service.ReportChatAsync("member", post.Message.Sequence, CancellationToken.None));
        Assert.True((await store.LoadAsync(CancellationToken.None)).Alliances.Single().ChatAudit.Count >= 3, "moderation actions must be audited");
    }

    [Test]
    public static async Task History_RetainsSevenDaysAndHundredMessages()
    {
        var (service, store, clock) = await Setup();
        for (int i = 0; i < 101; i++)
        {
            Assert.Equal(AllianceResult.Ok,
                (await service.PostChatAsync("member", Guid.NewGuid().ToString(), "m" + i, CancellationToken.None)).Result);
            clock.Advance(60_000);
        }
        var history = await service.ReadChatAsync("member", 0, CancellationToken.None);
        Assert.Equal(100, history.Messages.Count);
        Assert.Equal(2L, history.Messages[0].Sequence);
        Assert.Equal(100, (await store.LoadAsync(CancellationToken.None)).Alliances.Single().Chat.Count);
        clock.Advance(7L * 24 * 60 * 60 * 1000);
        Assert.Equal(0, (await service.ReadChatAsync("member", 0, CancellationToken.None)).Messages.Count);
    }
}
