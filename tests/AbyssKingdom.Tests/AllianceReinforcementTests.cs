using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.Battle;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbyssKingdom.Tests;

public static class AllianceReinforcementTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static Dictionary<TroopType, long> Mixed() => new()
    { [TroopType.Shield] = 100, [TroopType.Crossbow] = 120, [TroopType.Rider] = 80 };

    private sealed class Fixture
    {
        public readonly ManualClock Clock = new(1_700_000_000_000);
        public readonly FlakyPlayers Players = new();
        public readonly FlakyAlliances Alliances = new();
        public AllianceService Service = null!;
        public SessionManager Sessions = null!;
        public void Restart()
        {
            var world = new WorldService(Players, NullLogger<WorldService>.Instance);
            var synchronization = new AllianceSynchronization();
            var sessions = new SessionManager(Players, Clock, Json.Options, NullLogger<SessionManager>.Instance, world, Alliances, synchronization);
            Sessions = sessions;
            Service = new AllianceService(Alliances, Clock, sessions, synchronization);
        }
        public async Task Init()
        {
            var sites = SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer).Take(3).ToArray();
            var alliance = new AllianceRecord { Id = "a", Name = "test", Tag = "AK" };
            for (int i = 0; i < 3; i++)
            {
                string id = "p" + (i + 1);
                var city = CityState.CreateNew(Clock.NowMs);
                city.Surface.X = sites[i].x; city.Surface.Y = sites[i].y;
                city.Buildings[BuildingType.Keep] = 3;
                city.Buildings[BuildingType.Hospital] = 3;
                foreach (var type in CityConfig.AllTroops) city.Troops[type] = 1_000;
                await Players.SaveAsync(new PlayerRecord { Id = id, DisplayName = id, City = city }, Ct);
                alliance.Members.Add(new AllianceMemberRecord { PlayerId = id, Epoch = 1,
                    Role = i == 0 ? AllianceRole.Leader : AllianceRole.Member });
            }
            await Alliances.SaveAsync(new AllianceDirectory { Alliances = new() { alliance } }, Ct);
            Restart();
        }
        public Task<ReinforcementActionResult> Send(string sender = "p1", string? op = null,
            Dictionary<TroopType, long>? troops = null) => Service.DispatchReinforcementAsync(sender, "p2",
                HeroId.Hold, troops ?? Mixed(), op ?? Guid.NewGuid().ToString(), Ct);
        public async Task<CityState> City(string id = "p1") => (await Players.LoadAsync(id, Ct))!.City;
        public async Task<AllianceReinforcementRecord> Record(string id) =>
            (await Alliances.LoadAsync(Ct)).Reinforcements.Single(r => r.Id == id);
        public async Task Arrive(string id)
        {
            var r = await Record(id);
            Clock.Advance(r.ArriveAtMs - Clock.NowMs);
            await Service.AdvanceReinforcementsAsync(Ct);
        }
        public async Task PrepareDefense()
        {
            var player = (await Players.LoadAsync("p2", Ct))!;
            player.City.ShieldUntilMs = 0;
            player.City.Buildings[BuildingType.Hospital] = 3;
            await Players.SaveAsync(player, Ct);
        }
        public Task<AllianceDefenseResolution> Attack(string battle = "enemy:1", ulong seed = 42) =>
            Sessions.ResolveAllianceDefenseAsync("enemy", "p2", new()
            {
                (new SquadInput { Leader = "enemy", LeaderAttack = 100_000, LeaderDefense = 10_000,
                    LeaderHp = 100_000, Troop = TroopType.Rider, Count = 10_000 }, TroopType.Rider)
            }, seed, Clock.NowMs, battle, Ct);
    }

    private sealed class FlakyPlayers : IPlayerStore
    {
        private readonly InMemoryPlayerStore _inner = new(Json.Options);
        public bool FailNext;
        public string? FailPlayer;
        public int FailPlayerCountdown = 1;
        public Task<PlayerRecord?> LoadAsync(string id, CancellationToken ct) => _inner.LoadAsync(id, ct);
        public Task<List<PlayerRecord>> ListAllAsync(CancellationToken ct) => _inner.ListAllAsync(ct);
        public Task SaveAsync(PlayerRecord r, CancellationToken ct)
        {
            if (FailNext || (FailPlayer == r.Id && --FailPlayerCountdown <= 0))
            { FailNext = false; FailPlayer = null; throw new IOException("Injected player save failure"); }
            return _inner.SaveAsync(r, ct);
        }
    }
    private sealed class FlakyAlliances : IAllianceStore
    {
        private readonly InMemoryAllianceStore _inner = new(Json.Options);
        public int Saves, FailAt;
        public Task<AllianceDirectory> LoadAsync(CancellationToken ct) => _inner.LoadAsync(ct);
        public Task SaveAsync(AllianceDirectory d, CancellationToken ct)
        {
            if (++Saves == FailAt) throw new IOException("Injected directory save failure");
            return _inner.SaveAsync(d, ct);
        }
    }

    [Test]
    public static async Task ConcurrentSenders_ReserveOneSlotBeforeTroopDeduction()
    {
        var f = new Fixture(); await f.Init();
        var results = await Task.WhenAll(f.Send(), f.Send("p3"));
        Assert.Equal(1, results.Count(r => r.Result == AllianceResult.Ok));
        Assert.Equal(1, results.Count(r => r.Result == AllianceResult.ReinforcementSlotBusy));
        Assert.Equal(1_900L, (await f.City()).GetTroops(TroopType.Shield) + (await f.City("p3")).GetTroops(TroopType.Shield));
        Assert.Equal(1, (await f.Alliances.LoadAsync(Ct)).Reinforcements.Count);
        Assert.Equal(1_000L, (await f.City("p2")).GetTroops(TroopType.Shield));
    }

    [Test]
    public static async Task DirectoryFailureAfterDeduction_RetryDoesNotSendTwice()
    {
        var f = new Fixture(); await f.Init();
        string op = Guid.NewGuid().ToString();
        f.Alliances.FailAt = f.Alliances.Saves + 2;
        Assert.Equal(AllianceResult.PersistenceFailed, (await f.Send(op: op)).Result);
        Assert.Equal(900L, (await f.City()).GetTroops(TroopType.Shield));
        f.Restart();
        var retry = await f.Send(op: op);
        Assert.Equal(AllianceResult.Ok, retry.Result);
        Assert.Equal(1, (await f.City()).Surface.Marches.Count);
        Assert.Equal(900L, (await f.City()).GetTroops(TroopType.Shield));
        await f.Arrive(retry.Id!);
        Assert.Equal(ReinforcementState.Garrisoned, (await f.Record(retry.Id!)).State);
        Assert.Equal(AllianceResult.Ok, await f.Service.ReturnReinforcementAsync("p2", retry.Id!, Ct));
        f.Restart(); await f.Arrive(retry.Id!);
        Assert.Equal(ReinforcementState.Returned, (await f.Record(retry.Id!)).State);
        Assert.Equal(1_000L, (await f.City()).GetTroops(TroopType.Shield));
        Assert.Equal(AllianceResult.Ok, (await f.Send(op: op)).Result);
        Assert.Equal(0, (await f.City()).Surface.Marches.Count);
    }

    [Test]
    public static async Task PlayerFailureAfterReservation_RestartResumesWithoutLostTroops()
    {
        var f = new Fixture(); await f.Init();
        f.Players.FailNext = true;
        Assert.Equal(AllianceResult.PersistenceFailed, (await f.Send()).Result);
        Assert.Equal(1_000L, (await f.City()).GetTroops(TroopType.Shield));
        Assert.Equal(ReinforcementState.Reserved, (await f.Alliances.LoadAsync(Ct)).Reinforcements.Single().State);
        f.Restart(); await f.Service.AdvanceReinforcementsAsync(Ct);
        Assert.Equal(900L, (await f.City()).GetTroops(TroopType.Shield));
        Assert.Equal(1, (await f.City()).Surface.Marches.Count);
    }

    [Test]
    public static async Task LeavingAlliance_ReturnsTroopsEvenWhenOffline()
    {
        var f = new Fixture(); await f.Init();
        var sent = await f.Send("p3");
        await f.Arrive(sent.Id!);
        Assert.Equal(AllianceResult.Ok, await f.Service.LeaveAsync("p3", Ct));
        f.Restart(); await f.Service.AdvanceReinforcementsAsync(Ct);
        Assert.Equal(ReinforcementState.Returning, (await f.Record(sent.Id!)).State);
        await f.Arrive(sent.Id!);
        Assert.Equal(1_000L, (await f.City("p3")).GetTroops(TroopType.Shield));
    }

    [Test]
    public static async Task AcceptanceCapacityAndAuthorization_AreServerValidated()
    {
        var f = new Fixture(); await f.Init();
        Assert.Equal(AllianceResult.Ok, await f.Service.SetReinforcementAcceptanceAsync("p2", false, Ct));
        Assert.Equal(AllianceResult.ReinforcementsClosed, (await f.Send()).Result);
        await f.Service.SetReinforcementAcceptanceAsync("p2", true, Ct);
        var excessive = Mixed(); excessive[TroopType.Rider]++;
        var rejected = await f.Send(troops: excessive);
        Assert.Equal(AllianceResult.MilitaryRejected, rejected.Result);
        Assert.Equal(AllianceMilitaryResult.OverCapacity, rejected.MilitaryResult);
        Assert.Equal(1_000L, (await f.City()).GetTroops(TroopType.Shield));
        var sent = await f.Send();
        Assert.Equal(AllianceResult.Forbidden, await f.Service.ReturnReinforcementAsync("p3", sent.Id!, Ct));
        var conflict = await f.Service.DispatchReinforcementAsync("p1", "p3",
            HeroId.Hold, Mixed(), sent.Id!.Split(':')[1], Ct);
        Assert.Equal(AllianceResult.InvalidTarget, conflict.Result);
    }

    [Test]
    public static async Task FrozenRecall_PersistsIntentAndWaitsForSettlement()
    {
        var f = new Fixture(); await f.Init();
        var sent = await f.Send(); await f.Arrive(sent.Id!);
        var player = (await f.Players.LoadAsync("p1", Ct))!;
        player.City.Surface.Marches.Single().GarrisonSettlementId = "battle";
        await f.Players.SaveAsync(player, Ct);
        Assert.Equal(AllianceResult.Ok, await f.Service.ReturnReinforcementAsync("p1", sent.Id!, Ct));
        var frozen = await f.Record(sent.Id!);
        Assert.True(frozen.ReturnRequested && frozen.State == ReinforcementState.Garrisoned, "凍結中先記錄召回意圖");
        player = (await f.Players.LoadAsync("p1", Ct))!;
        player.City.Surface.Marches.Single().GarrisonSettlementId = null;
        await f.Players.SaveAsync(player, Ct);
        var directory = await f.Alliances.LoadAsync(Ct);
        directory.Reinforcements.Single().SettlementId = null;
        await f.Alliances.SaveAsync(directory, Ct);
        f.Restart(); await f.Service.AdvanceReinforcementsAsync(Ct);
        Assert.Equal(ReinforcementState.Returning, (await f.Record(sent.Id!)).State);
    }

    [Test]
    public static async Task Defense_UsesOwnerTroopsAndReplaysWithoutDoubleCasualties()
    {
        var f = new Fixture(); await f.Init();
        var sent = await f.Send(); await f.Arrive(sent.Id!); await f.PrepareDefense();
        var first = await f.Attack();
        Assert.True(first.Saved && first.Outcome?.Garrison?.OwnerId == "p1", "援軍參戰並由原主城結算");
        long homeWounded = CityLogic.WoundedTotal(await f.City("p2"));
        long allyWounded = CityLogic.WoundedTotal(await f.City());
        Assert.True(allyWounded > 0, "援軍傷兵進原主人的醫館");
        f.Restart();
        var retry = await f.Attack(seed: 999);
        Assert.True(retry.Saved, "重啟後重試完成");
        Assert.Equal(homeWounded, CityLogic.WoundedTotal(await f.City("p2")));
        Assert.Equal(allyWounded, CityLogic.WoundedTotal(await f.City()));
        Assert.Equal(first.Outcome!.Report.Seed, retry.Outcome!.Report.Seed);
        Assert.Equal<string?>(null, (await f.Record(sent.Id!)).SettlementId);
    }

    [Test]
    public static async Task Defense_FailureBeforeDefenderSave_PreservesFrozenSnapshot()
    {
        var f = new Fixture(); await f.Init();
        var sent = await f.Send(); await f.Arrive(sent.Id!); await f.PrepareDefense();
        f.Players.FailPlayer = "p2";
        bool failed = false;
        try { await f.Attack(seed: 42); } catch (IOException) { failed = true; }
        Assert.True(failed, "守方存檔斷點已注入");
        var frozen = await f.Record(sent.Id!);
        Assert.Equal("enemy:1", frozen.SettlementId);
        Assert.Equal(42UL, frozen.BattleSeed);
        Assert.Equal(0, (await f.City("p2")).SiegeLog.Count);
        f.Restart();
        var retry = await f.Attack(seed: 999);
        Assert.True(retry.Saved, "凍結快照恢復");
        Assert.Equal(42UL, retry.Outcome!.Report.Seed);
    }

    [Test]
    public static async Task Defense_FailureAfterOwnerSave_KeepsDefenderOutcomeAndBlocksNextBattle()
    {
        var f = new Fixture(); await f.Init();
        var sent = await f.Send(); await f.Arrive(sent.Id!); await f.PrepareDefense();
        // Freeze has already succeeded when the directory's third write is attempted.
        f.Alliances.FailAt = f.Alliances.Saves + 3;
        bool failed = false;
        try { await f.Attack(); } catch (IOException) { failed = true; }
        Assert.True(failed, "結算後聯盟存檔斷點已注入");
        long wounded = CityLogic.WoundedTotal(await f.City());
        Assert.True((await f.City("p2")).SiegeLog.ContainsKey("enemy:1"), "守方結果仍保留");
        Assert.True(!(await f.Attack("enemy:2")).Saved, "前一場未收尾前不得重用援軍");
        f.Restart();
        Assert.True((await f.Attack()).Saved, "依原結果完成收尾");
        Assert.Equal(wounded, CityLogic.WoundedTotal(await f.City()), "收尾不重複套傷兵");
    }

    [Test]
    public static async Task Defense_OwnerSaveFailure_RetryAppliesCasualtiesOnce()
    {
        var f = new Fixture(); await f.Init();
        var sent = await f.Send(); await f.Arrive(sent.Id!); await f.PrepareDefense();
        f.Players.FailPlayer = "p1";
        f.Players.FailPlayerCountdown = 2; // Save freeze, then fail the casualty save.
        bool failed = false;
        try { await f.Attack(); } catch (IOException) { failed = true; }
        Assert.True(failed, "援軍傷兵存檔斷點已注入");
        Assert.Equal(0L, CityLogic.WoundedTotal(await f.City()));
        Assert.True((await f.City("p2")).SiegeLog.ContainsKey("enemy:1"), "守方結果持久化");
        f.Restart();
        Assert.True((await f.Attack()).Saved, "原結果套用到援軍主人");
        long wounded = CityLogic.WoundedTotal(await f.City());
        Assert.True(wounded > 0, "傷兵不遺失");
        await f.Attack();
        Assert.Equal(wounded, CityLogic.WoundedTotal(await f.City()));
    }

    [Test]
    public static async Task Defense_ShieldedCity_NeverFreezesOrDamagesReinforcement()
    {
        var f = new Fixture(); await f.Init();
        var sent = await f.Send(); await f.Arrive(sent.Id!);
        var defended = await f.Attack();
        Assert.True(defended.Saved && defended.Outcome!.Shielded, "盾保護整個城防");
        Assert.Equal<GarrisonOutcome?>(null, defended.Outcome!.Garrison);
        Assert.Equal<string?>(null, (await f.Record(sent.Id!)).SettlementId);
        Assert.Equal<string?>(null, (await f.City()).Surface.Marches.Single().GarrisonSettlementId);
        Assert.Equal(0L, CityLogic.WoundedTotal(await f.City()));
        Assert.Equal(100L, (await f.City()).Surface.Marches.Single().Troops[TroopType.Shield]);
    }

    [Test]
    public static async Task ReceiverOfflineUpgrade_IsAppliedAtExactCapacityBoundary()
    {
        var f = new Fixture(); await f.Init();
        var sender = (await f.Players.LoadAsync("p1", Ct))!;
        sender.City.Buildings[BuildingType.Keep] = 4;
        await f.Players.SaveAsync(sender, Ct);
        var receiver = (await f.Players.LoadAsync("p2", Ct))!;
        receiver.City.Upgrade = new UpgradeJob { Building = BuildingType.Keep, TargetLevel = 4,
            StartedAtMs = f.Clock.NowMs, FinishAtMs = f.Clock.NowMs + 1_000 };
        await f.Players.SaveAsync(receiver, Ct);
        var troops = Mixed(); troops[TroopType.Shield]++;
        Assert.Equal(AllianceMilitaryResult.OverCapacity, (await f.Send(troops: troops)).MilitaryResult);
        f.Clock.Advance(1_000);
        Assert.Equal(AllianceResult.Ok, (await f.Send(troops: troops)).Result);
        Assert.Equal(899L, (await f.City()).GetTroops(TroopType.Shield));
    }
    [Test]
    public static async Task FrozenBattle_SameAllianceChangeStillCompletesOriginalBattle()
    {
        var f = new Fixture(); await f.Init(); var sent = await f.Send(); await f.Arrive(sent.Id!); await f.PrepareDefense();
        f.Players.FailPlayer = "p2";
        try { await f.Attack(); } catch (IOException) { }
        var directory = await f.Alliances.LoadAsync(Ct);
        directory.Alliances[0].Members.Add(new AllianceMemberRecord { PlayerId = "enemy", Role = AllianceRole.Member, Epoch = 1 });
        await f.Alliances.SaveAsync(directory, Ct);
        var resolved = await f.Attack(seed: 999);
        Assert.True(resolved.Saved && !resolved.Outcome!.Shielded, "已凍結戰鬥不能藉入盟免除戰損");
        Assert.Equal(42UL, resolved.Outcome!.Report.Seed);
        Assert.Equal<string?>(null, (await f.Record(sent.Id!)).SettlementId);
    }
    [Test]
    public static async Task FrozenBattle_NewShieldReleasesReservationWithoutCasualties()
    {
        var f = new Fixture(); await f.Init(); var sent = await f.Send(); await f.Arrive(sent.Id!); await f.PrepareDefense();
        f.Players.FailPlayer = "p2";
        try { await f.Attack(); } catch (IOException) { }
        var defender = (await f.Players.LoadAsync("p2", Ct))!;
        defender.City.ShieldUntilMs = f.Clock.NowMs + 60_000; await f.Players.SaveAsync(defender, Ct);
        var resolved = await f.Attack();
        Assert.True(resolved.Saved && resolved.Outcome!.Shielded, "護盾阻擋戰鬥");
        Assert.Equal<string?>(null, (await f.Record(sent.Id!)).SettlementId);
        Assert.Equal<string?>(null, (await f.City()).Surface.Marches.Single().GarrisonSettlementId);
        Assert.Equal(0L, CityLogic.WoundedTotal(await f.City()));
    }
    [Test]
    public static async Task OfflineAttacker_GarrisonSettlementRecoversAndUnblocksCity()
    {
        var f = new Fixture(); await f.Init(); var sent = await f.Send(); await f.Arrive(sent.Id!); await f.PrepareDefense();
        var enemy = CityState.CreateNew(f.Clock.NowMs);
        var site = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer);
        enemy.Surface.X = site.x; enemy.Surface.Y = site.y;
        var target = SurfaceLogic.CityCenter((await f.City("p2")).Surface);
        enemy.Surface.Marches.Add(new March { Id = 1, Hero = HeroId.Hold, Sieging = true, Stationed = true, SiegeTarget = "p2",
            Troops = new() { [TroopType.Rider] = 10_000 }, Path = new() { SurfaceLogic.Tile(site.x, site.y), SurfaceLogic.Tile(target.x, target.y) },
            ArriveAtMs = new() { f.Clock.NowMs - 1_000, f.Clock.NowMs } });
        await f.Players.SaveAsync(new PlayerRecord { Id = "enemy", City = enemy }, Ct);
        f.Players.FailPlayer = "p1"; f.Players.FailPlayerCountdown = 2;
        try { await f.Attack(); } catch (IOException) { }
        Assert.Equal("enemy:1", (await f.Record(sent.Id!)).SettlementId);
        f.Restart(); await f.Sessions.RecoverAllianceDefensesAsync(Ct);
        Assert.Equal<string?>(null, (await f.Record(sent.Id!)).SettlementId);
        Assert.True((await f.City("enemy")).Surface.Marches.Single().Returning, "離線攻方也完成兩段結算");
        long wounded = CityLogic.WoundedTotal(await f.City());
        await f.Sessions.RecoverAllianceDefensesAsync(Ct);
        Assert.Equal(wounded, CityLogic.WoundedTotal(await f.City()));
    }
}
