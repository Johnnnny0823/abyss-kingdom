using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbyssKingdom.Tests;

public static class AllianceRallyTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static Dictionary<TroopType, long> Troops() => CityConfig.AllTroops.ToDictionary(t => t, _ => 100L);
    private sealed class Players : IPlayerStore
    {
        readonly InMemoryPlayerStore _inner = new(Json.Options);
        public string? FailOwner;
        public Task<PlayerRecord?> LoadAsync(string id, CancellationToken ct) => _inner.LoadAsync(id, ct);
        public Task<List<PlayerRecord>> ListAllAsync(CancellationToken ct) => _inner.ListAllAsync(ct);
        public Task SaveAsync(PlayerRecord r, CancellationToken ct)
        {
            if (r.Id == FailOwner) { FailOwner = null; throw new IOException("Injected player failure"); }
            return _inner.SaveAsync(r, ct);
        }
    }
    private sealed class Store : IAllianceStore
    {
        readonly InMemoryAllianceStore _inner = new(Json.Options);
        public int Saves, FailAt;
        public Task<AllianceDirectory> LoadAsync(CancellationToken ct) => _inner.LoadAsync(ct);
        public Task SaveAsync(AllianceDirectory d, CancellationToken ct)
        {
            if (++Saves == FailAt) throw new IOException("Injected directory failure");
            return _inner.SaveAsync(d, ct);
        }
    }
    private sealed class Fixture
    {
        public readonly ManualClock Clock = new(1_700_000_000_000);
        public readonly Players Players = new(); public readonly Store Store = new();
        public AllianceService Service = null!; public SessionManager Sessions = null!;
        public Feature Target = null!;
        public async Task Init()
        {
            var sites = SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer && s.x > 700 && Math.Abs(s.y - 400) < 25).Take(6).ToArray();
            var alliance = new AllianceRecord { Id = "a" };
            for (int i = 0; i < 6; i++)
            {
                string id = "p" + (i + 1); var city = CityState.CreateNew(Clock.NowMs);
                city.Buildings[BuildingType.Keep] = 3; city.Buildings[BuildingType.Hospital] = 10;
                city.Surface.X = sites[i].x; city.Surface.Y = sites[i].y;
                city.Heroes[HeroId.Hold].Level = 10;
                foreach (var t in CityConfig.AllTroops) city.Troops[t] = 1_000;
                await Players.SaveAsync(new PlayerRecord { Id = id, City = city }, Ct);
                alliance.Members.Add(new AllianceMemberRecord { PlayerId = id, Epoch = 1, Role = i == 0 ? AllianceRole.Leader : AllianceRole.Member });
            }
            await Store.SaveAsync(new AllianceDirectory { Alliances = new() { alliance } }, Ct);
            Target = SurfaceLogic.Features().First(f => f.Type == FeatureType.MonsterCamp && f.Level == 2);
            await Restart();
        }
        public async Task Restart()
        {
            var sync = new AllianceSynchronization(); var world = new WorldService(Players, NullLogger<WorldService>.Instance, Store);
            world.UpdateCampSnapshot(await Store.LoadAsync(Ct));
            Sessions = new SessionManager(Players, Clock, Json.Options, NullLogger<SessionManager>.Instance, world, Store, sync);
            Service = new AllianceService(Store, Clock, Sessions, sync, world);
        }
        public Task<ReinforcementActionResult> Create(string? op = null) => Service.CreateRallyAsync("p1", HeroId.Hold,
            Troops(), Target.X, Target.Y, 60, op ?? Guid.NewGuid().ToString(), Ct);
        public Task<ReinforcementActionResult> Join(string id, string owner = "p2", string? op = null) => Service.JoinRallyAsync(owner, id,
            HeroId.Hold, Troops(), op ?? Guid.NewGuid().ToString(), Ct);
        public async Task<AllianceRallyRecord> Rally(string id) => (await Store.LoadAsync(Ct)).Rallies.Single(r => r.Id == id);
        public async Task<CityState> City(string owner = "p1") => (await Players.LoadAsync(owner, Ct))!.City;
        public async Task Launch(string id)
        {
            var r = await Rally(id); Clock.Advance(r.DepartAtMs - Clock.NowMs); await Service.AdvanceRalliesAsync(Ct);
        }
        public async Task Arrive(string id)
        {
            var r = await Rally(id); Clock.Advance(r.Times[^1] - Clock.NowMs); await Service.AdvanceRalliesAsync(Ct);
        }
    }
    [Test]
    public static async Task TwoOwners_TravelTogetherSettleOnceAndBringOnePoolHome()
    {
        var f = new Fixture(); await f.Init(); var created = await f.Create();
        Assert.Equal(AllianceResult.Ok, created.Result); string id = created.Id!;
        Assert.Equal(AllianceResult.Ok, (await f.Join(id)).Result);
        await f.Launch(id);
        Assert.Equal(RallyState.Traveling, (await f.Rally(id)).State);
        var a = (await f.City()).Surface.Marches.Single(); var b = (await f.City("p2")).Surface.Marches.Single();
        Assert.True(a.Path.SequenceEqual(b.Path) && a.ArriveAtMs.SequenceEqual(b.ArriveAtMs), "共同路徑與速度");
        Assert.Equal(600L, a.RallyTotalTroops);
        Assert.Equal(AllianceResult.Forbidden, await f.Service.RecallRallyAsync("p2", id, Ct));
        await f.Arrive(id); var settled = await f.Rally(id);
        Assert.True(settled.Outcome != null, "有持久化戰報");
        if (settled.Outcome!.Won)
            foreach (var reward in CampConfig.ResourceLoot(2)) Assert.Equal(reward.Value, settled.Outcome.Participants.Sum(p => p.Resources.GetValueOrDefault(reward.Key)));
        long wounded = CityLogic.WoundedTotal(await f.City());
        await f.Restart(); await f.Service.AdvanceRalliesAsync(Ct);
        Assert.Equal(wounded, CityLogic.WoundedTotal(await f.City()));
        f.Clock.Advance(10_000_000); await f.Service.AdvanceRalliesAsync(Ct);
        Assert.Equal(RallyState.Finished, (await f.Rally(id)).State);
        Assert.Equal(0, (await f.City()).Surface.Marches.Count);
        Assert.Equal(0, (await f.City("p2")).Surface.Marches.Count);
        long total = (await f.City()).Troops.Values.Sum() + CityLogic.WoundedTotal(await f.City());
        Assert.True(total <= 3_000, "不複製士兵");
    }
    [Test]
    public static async Task ConcurrentJoiners_EnforceFiveDifferentOwnersAndCapacity()
    {
        var f = new Fixture(); await f.Init(); var created = await f.Create();
        var results = await Task.WhenAll(Enumerable.Range(2, 5).Select(i => f.Join(created.Id!, "p" + i)));
        Assert.Equal(4, results.Count(r => r.Result == AllianceResult.Ok));
        Assert.Equal(1, results.Count(r => r.Result == AllianceResult.Full));
        Assert.Equal(5, (await f.Rally(created.Id!)).Participants.Count(p => !p.Left));
    }
    [Test]
    public static async Task SoloLeader_CancelsAtDeadlineAndReturnsTroops()
    {
        var f = new Fixture(); await f.Init(); var created = await f.Create();
        await f.Launch(created.Id!); f.Clock.Advance(10_000); await f.Service.AdvanceRalliesAsync(Ct);
        var rally = await f.Rally(created.Id!);
        Assert.True(rally.Cancelled && rally.State == RallyState.Finished, "至少兩人到齊才出發");
        Assert.Equal(3_000L, (await f.City()).Troops.Values.Sum());
    }
    [Test]
    public static async Task DebitFailure_RestartDoesNotDeductTwiceAndJoinIdCannotCrossRallies()
    {
        var f = new Fixture(); await f.Init(); string op = Guid.NewGuid().ToString();
        f.Store.FailAt = f.Store.Saves + 2;
        Assert.Equal(AllianceResult.PersistenceFailed, (await f.Create(op)).Result);
        Assert.Equal(900L, (await f.City()).GetTroops(TroopType.Shield));
        await f.Restart(); var retry = await f.Create(op); Assert.Equal(AllianceResult.Ok, retry.Result);
        Assert.Equal(900L, (await f.City()).GetTroops(TroopType.Shield));
        string join = Guid.NewGuid().ToString(); Assert.Equal(AllianceResult.Ok, (await f.Join(retry.Id!, op: join)).Result);
        var another = await f.Service.CreateRallyAsync("p3", HeroId.Hold, Troops(), f.Target.X, f.Target.Y, 60, Guid.NewGuid().ToString(), Ct);
        Assert.Equal(AllianceResult.InvalidTarget, (await f.Join(another.Id!, op: join)).Result);
    }
    [Test]
    public static async Task LaunchAndCasualtyFailure_ResumeFrozenInputsWithoutDoubleRewards()
    {
        var f = new Fixture(); await f.Init(); var created = await f.Create(); string id = created.Id!;
        await f.Join(id); var rally = await f.Rally(id); f.Clock.Advance(rally.DepartAtMs - f.Clock.NowMs);
        f.Players.FailOwner = "p1"; bool failed = false;
        try { await f.Service.AdvanceRalliesAsync(Ct); } catch (IOException) { failed = true; }
        Assert.True(failed, "出發存檔斷點"); Assert.Equal(RallyState.Launching, (await f.Rally(id)).State);
        await f.Restart(); await f.Service.AdvanceRalliesAsync(Ct);
        rally = await f.Rally(id); f.Clock.Advance(rally.Times[^1] - f.Clock.NowMs);
        f.Players.FailOwner = "p2"; failed = false;
        try { await f.Service.AdvanceRalliesAsync(Ct); } catch (IOException) { failed = true; }
        Assert.True(failed, "第二位玩家戰損存檔斷點"); Assert.Equal(RallyState.Settling, (await f.Rally(id)).State);
        Assert.Equal(AllianceResult.PendingOperations, await f.Service.RecallRallyAsync("p1", id, Ct));
        var city = await f.City(); long wounded = CityLogic.WoundedTotal(city); var carry = city.Surface.Marches.Single().Carry.Values.Sum();
        await f.Restart(); await f.Service.AdvanceRalliesAsync(Ct);
        Assert.Equal(wounded, CityLogic.WoundedTotal(await f.City()));
        Assert.Equal(carry, (await f.City()).Surface.Marches.Single().Carry.Values.Sum());
        Assert.Equal(RallyState.Returning, (await f.Rally(id)).State);
    }
    [Test]
    public static async Task LateJoinAndPreparationLeave_ReturnWithoutBattleOrExtraDebits()
    {
        var f = new Fixture(); await f.Init(); var created = await f.Create(); string id = created.Id!;
        var remote = (await f.Players.LoadAsync("p2", Ct))!;
        var far = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer && s.x < 100);
        remote.City.Surface.X = far.x; remote.City.Surface.Y = far.y; await f.Players.SaveAsync(remote, Ct);
        var late = await f.Join(id);
        Assert.Equal(AllianceMilitaryResult.ArrivesLate, late.MilitaryResult);
        Assert.Equal(3_000L, (await f.City("p2")).Troops.Values.Sum());
        Assert.Equal(AllianceResult.Ok, (await f.Join(id, "p3")).Result);
        Assert.Equal(AllianceResult.Ok, await f.Service.LeaveAsync("p3", Ct));
        await f.Service.AdvanceRalliesAsync(Ct);
        Assert.True((await f.City("p3")).Surface.Marches.Single().Returning, "離盟在準備期返還");
        await f.Launch(id); f.Clock.Advance(1_000_000); await f.Service.AdvanceRalliesAsync(Ct);
        Assert.True((await f.Rally(id)).Cancelled, "到齊人數不足取消");
        Assert.Equal(3_000L, (await f.City("p3")).Troops.Values.Sum());
    }
    [Test]
    public static async Task DepartedMember_StillTakesBattleLossesAndLeaderCanRecallWholeTeam()
    {
        var f = new Fixture(); await f.Init(); var created = await f.Create(); string id = created.Id!;
        await f.Join(id); await f.Launch(id);
        Assert.Equal(AllianceResult.Ok, await f.Service.LeaveAsync("p2", Ct));
        Assert.Equal(AllianceResult.Forbidden, await f.Service.RecallRallyAsync("p2", id, Ct));
        await f.Arrive(id);
        Assert.True((await f.Rally(id)).Outcome!.Participants.Any(p => p.OwnerId == "p2"), "出發後離盟仍參戰");
        var second = new Fixture(); await second.Init(); var next = await second.Create(); await second.Join(next.Id!); await second.Launch(next.Id!);
        Assert.Equal(AllianceResult.Ok, await second.Service.RecallRallyAsync("p1", next.Id!, Ct));
        Assert.True((await second.City()).Surface.Marches.Single().Returning && (await second.City("p2")).Surface.Marches.Single().Returning, "隊長整隊召回");
        Assert.Equal<CampSettlementOutcome?>(null, (await second.Rally(next.Id!)).Outcome);
    }
    [Test]
    public static async Task SoloCampOutcome_CrashAfterOwnerSaveAndCompetingAttack_GetOnePool()
    {
        var f = new Fixture(); await f.Init();
        async Task<CampParticipantInput> Prepare(string owner)
        {
            CampParticipantInput? input = null;
            await f.Sessions.ModifyCityAsync(owner, city =>
            {
                var home = SurfaceLogic.CityCenter(city.Surface);
                var march = new March { Id = ++city.Surface.NextMarchId, Hero = HeroId.Hold, CampPending = true, Stationed = true,
                    Troops = Troops(), Path = new() { SurfaceLogic.Tile(home.x, home.y), SurfaceLogic.Tile(f.Target.X, f.Target.Y) },
                    ArriveAtMs = new() { f.Clock.NowMs - 10_000, f.Clock.NowMs } };
                foreach (var t in CityConfig.AllTroops) city.Troops[t] -= 100;
                city.Surface.Marches.Add(march);
                var squads = MarchLogic.Squads(city, march).Select(s => s.squad).ToList();
                squads[0].LeaderAttack = 100_000; squads[0].LeaderHp = 100_000;
                input = new CampParticipantInput { OwnerId = owner, MarchId = march.Id, Hero = march.Hero, Squads = squads };
            }, Ct, saveNow: true);
            return input!;
        }
        var first = await Prepare("p1"); int tile = SurfaceLogic.Tile(f.Target.X, f.Target.Y);
        f.Store.FailAt = f.Store.Saves + 3; bool failed = false;
        try { await f.Sessions.SettleCampAsync("solo-test-1", tile, 2, f.Clock.NowMs, new() { first }, false, Ct); }
        catch (IOException) { failed = true; }
        Assert.True(failed, "城市結果已存、結算收尾目錄失敗");
        long loot = (await f.City()).Surface.Marches.Single().Carry.Values.Sum(); Assert.True(loot > 0, "勝利產生一份獎勵");
        await f.Restart(); await f.Sessions.RecoverCampSettlementsAsync(Ct);
        Assert.Equal(loot, (await f.City()).Surface.Marches.Single().Carry.Values.Sum());
        var competitor = await Prepare("p3");
        var gone = await f.Sessions.SettleCampAsync("solo-test-2", tile, 2, f.Clock.NowMs, new() { competitor }, false, Ct);
        Assert.True(gone.CampGone && gone.Participants.Single().Resources.Count == 0, "搶同營地不重複領獎");
        Assert.True(gone.Participants.Single().Losses.Values.All(n => n == 0), "營地消失不產生戰損");
    }
    [Test]
    public static async Task ArrivalAtDeparture_IsIncludedButOneMillisecondLateIsRejected()
    {
        async Task<(Fixture fixture, string id)> Setup()
        {
            var f = new Fixture(); await f.Init(); var created = await f.Create();
            var leader = await f.City(); var member = (await f.Players.LoadAsync("p2", Ct))!;
            member.City.Surface.X = leader.Surface.X; member.City.Surface.Y = leader.Surface.Y + 100;
            await f.Players.SaveAsync(member, Ct); return (f, created.Id!);
        }
        var exact = await Setup(); Assert.Equal(AllianceResult.Ok, (await exact.fixture.Join(exact.id)).Result);
        Assert.Equal((await exact.fixture.Rally(exact.id)).DepartAtMs, (await exact.fixture.City("p2")).Surface.Marches.Single().ArriveAt);
        await exact.fixture.Launch(exact.id);
        Assert.Equal(2, (await exact.fixture.Rally(exact.id)).FrozenInputs.Count);
        var late = await Setup(); late.fixture.Clock.Advance(1);
        Assert.Equal(AllianceMilitaryResult.ArrivesLate, (await late.fixture.Join(late.id)).MilitaryResult);
        Assert.Equal(1, (await late.fixture.Rally(late.id)).Participants.Count, "遲到拒絕不占預留席位");
    }
    [Test]
    public static async Task RecallAfterArrival_CannotUndoTheBattleBeforeTickerRuns()
    {
        var f = new Fixture(); await f.Init(); var created = await f.Create(); string id = created.Id!;
        await f.Join(id); await f.Launch(id); var r = await f.Rally(id);
        f.Clock.Advance(r.Times[^1] - f.Clock.NowMs);
        Assert.Equal(AllianceResult.Ok, await f.Service.RecallRallyAsync("p1", id, Ct));
        r = await f.Rally(id);
        Assert.True(r.Outcome != null && !r.Cancelled, "抵達邊界先凍結／結算，不可利用 ticker 延遲取消");
    }
}
