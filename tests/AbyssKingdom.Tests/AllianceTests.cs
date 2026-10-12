using AbyssKingdom.Shared.Social;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Game;

namespace AbyssKingdom.Tests;

public static class AllianceTests
{
    [Test]
    public static void Names_NormalizeUnicodeAndRejectMarkup()
    {
        Assert.Equal("深淵盟", AllianceRules.NormalizeName("  深淵盟  "));
        Assert.Equal("AB12", AllianceRules.NormalizeTag("ａｂ１２"));
        Assert.Equal<string?>(null, AllianceRules.NormalizeName("<b>盟</b>"));
        Assert.Equal<string?>(null, AllianceRules.NormalizeTag("盟軍"));
        Assert.Equal<string?>(null, AllianceRules.NormalizeName("A"));
        Assert.True(AllianceRules.NormalizeName(string.Concat(Enumerable.Repeat("淵", 16))) != null, "16 字名稱應可使用");
        Assert.Equal<string?>(null, AllianceRules.NormalizeName(string.Concat(Enumerable.Repeat("淵", 17))));
    }

    [Test]
    public static void Permissions_RespectThreeRanks()
    {
        Assert.True(AllianceRules.Can(AllianceRole.Officer, AlliancePermission.ReviewApplications), "幹部可審核");
        Assert.True(!AllianceRules.Can(AllianceRole.Officer, AlliancePermission.ChangeJoinMode), "幹部不可改審核模式");
        Assert.True(AllianceRules.CanKick(AllianceRole.Officer, AllianceRole.Member, false), "幹部可踢成員");
        Assert.True(!AllianceRules.CanKick(AllianceRole.Officer, AllianceRole.Officer, false), "幹部不可踢同級");
        Assert.True(!AllianceRules.CanKick(AllianceRole.Leader, AllianceRole.Member, true), "不可踢自己");
    }

    [Test]
    public static void TimeBoundaries_AreExact()
    {
        Assert.True(AllianceRules.ApplicationActive(999, 1000), "到期前仍有效");
        Assert.True(!AllianceRules.ApplicationActive(1000, 1000), "到期毫秒即失效");
        Assert.True(!AllianceRules.CooldownEnded(999, 1000), "冷卻前不可加入");
        Assert.True(AllianceRules.CooldownEnded(1000, 1000), "冷卻邊界可加入");
    }

    [Test]
    public static async Task InMemoryStore_RoundTripsIndependentSnapshots()
    {
        var store = new InMemoryAllianceStore(Json.Options);
        var directory = new AllianceDirectory();
        directory.Alliances.Add(new AllianceRecord
        {
            Id = "a1", Name = "淵契盟", Tag = "AK", JoinMode = AllianceJoinMode.Approval,
            Members = new() { new AllianceMemberRecord { PlayerId = "p1", Role = AllianceRole.Leader, Epoch = 1 } }
        });
        await store.SaveAsync(directory, CancellationToken.None);
        directory.Alliances[0].Name = "changed in caller";
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal("淵契盟", loaded.Alliances.Single().Name);
        Assert.Equal(AllianceRole.Leader, loaded.Alliances.Single().Members.Single().Role);
        loaded.Alliances.Clear();
        Assert.Equal(1, (await store.LoadAsync(CancellationToken.None)).Alliances.Count);
    }

    [Test]
    public static async Task JoinConcurrent_AllowsOnlyOneAllianceAndRespectsCapacity()
    {
        var store = new InMemoryAllianceStore(Json.Options);
        await store.SaveAsync(new AllianceDirectory
        {
            Alliances = new()
            {
                Seed("a1", "A1"), Seed("a2", "A2")
            }
        }, CancellationToken.None);
        var service = new AllianceService(store, new ManualClock(1000));
        var results = await Task.WhenAll(
            service.JoinAsync("p", "Player", 2, "a1", CancellationToken.None),
            service.JoinAsync("p", "Player", 2, "a2", CancellationToken.None));
        Assert.Equal(1, results.Count(x => x == AllianceResult.Ok));
        Assert.Equal(1, results.Count(x => x == AllianceResult.AlreadyMember));
        Assert.Equal(1, (await store.LoadAsync(CancellationToken.None)).Alliances.Sum(x => x.Members.Count(m => m.PlayerId == "p")));
    }

    [Test]
    public static async Task ApprovalRolesAndCooldown_EnforcedAtBoundary()
    {
        var store = new InMemoryAllianceStore(Json.Options);
        var alliance = Seed("a1", "A1");
        alliance.JoinMode = AllianceJoinMode.Approval;
        alliance.Members.Add(new AllianceMemberRecord { PlayerId = "leader", Role = AllianceRole.Leader });
        alliance.Members.Add(new AllianceMemberRecord { PlayerId = "member", Role = AllianceRole.Member });
        await store.SaveAsync(new AllianceDirectory { Alliances = new() { alliance } }, CancellationToken.None);
        var clock = new ManualClock(1000);
        var service = new AllianceService(store, clock);
        Assert.Equal(AllianceResult.Ok, await service.ApplyAsync("candidate", "Candidate", 2, "a1", CancellationToken.None));
        Assert.Equal(AllianceResult.Forbidden, await service.ReviewAsync("member", "candidate", true, CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.ReviewAsync("leader", "candidate", true, CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.LeaveAsync("candidate", CancellationToken.None));
        Assert.Equal(AllianceResult.Cooldown, await service.ApplyAsync("candidate", "Candidate", 2, "a1", CancellationToken.None));
        clock.Advance(AllianceConfig.RejoinCooldownMs);
        Assert.Equal(AllianceResult.Ok, await service.ApplyAsync("candidate", "Candidate", 2, "a1", CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.ReviewAsync("leader", "candidate", true, CancellationToken.None));
        var current = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(2L, current.Alliances.Single().Members.Single(m => m.PlayerId == "candidate").Epoch);
    }

    [Test]
    public static async Task Leadership_OnlyLeaderMayTransferAndCannotLeave()
    {
        var store = new InMemoryAllianceStore(Json.Options);
        var alliance = Seed("a1", "A1");
        alliance.Members.Add(new AllianceMemberRecord { PlayerId = "leader", Role = AllianceRole.Leader });
        alliance.Members.Add(new AllianceMemberRecord { PlayerId = "member", Role = AllianceRole.Member });
        await store.SaveAsync(new AllianceDirectory { Alliances = new() { alliance } }, CancellationToken.None);
        var service = new AllianceService(store, new ManualClock(1000));
        Assert.Equal(AllianceResult.LeaderMustTransfer, await service.LeaveAsync("leader", CancellationToken.None));
        Assert.Equal(AllianceResult.Forbidden, await service.SetOfficerAsync("member", "leader", true, CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.TransferAsync("leader", "member", CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.TransferAsync("leader", "member", CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.LeaveAsync("leader", CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.DissolveAsync("member", CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.DissolveAsync("member", CancellationToken.None));
        Assert.Equal(0, (await store.LoadAsync(CancellationToken.None)).Alliances.Count);
    }

    [Test]
    public static async Task ConcurrentJoin_StopsAtMemberCapAndOfficerLimitIsFour()
    {
        var store = new InMemoryAllianceStore(Json.Options);
        var alliance = Seed("a1", "A1");
        alliance.Members.Add(new AllianceMemberRecord { PlayerId = "leader", Role = AllianceRole.Leader });
        await store.SaveAsync(new AllianceDirectory { Alliances = new() { alliance } }, CancellationToken.None);
        var service = new AllianceService(store, new ManualClock(1000));
        var results = await Task.WhenAll(Enumerable.Range(0, AllianceConfig.MaxMembers + 5).Select(i =>
            service.JoinAsync("p" + i, "P" + i, 2, "a1", CancellationToken.None)));
        Assert.Equal(AllianceConfig.MaxMembers - 1, results.Count(x => x == AllianceResult.Ok));
        Assert.Equal(6, results.Count(x => x == AllianceResult.Full));
        Assert.Equal(AllianceConfig.MaxMembers, (await store.LoadAsync(CancellationToken.None)).Alliances.Single().Members.Count);
        for (int i = 0; i < 4; i++)
            Assert.Equal(AllianceResult.Ok, await service.SetOfficerAsync("leader", "p" + i, true, CancellationToken.None));
        Assert.Equal(AllianceResult.OfficerLimit, await service.SetOfficerAsync("leader", "p4", true, CancellationToken.None));
    }

    private static AllianceRecord Seed(string id, string tag) => new() { Id = id, Name = "Alliance " + tag, Tag = tag };
}
