using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbyssKingdom.Tests;

public static class AllianceFlagServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private sealed class Store : IAllianceStore
    {
        public readonly InMemoryAllianceStore Inner = new(Json.Options);
        public int Saves, FailAt;
        public Task<AllianceDirectory> LoadAsync(CancellationToken ct) => Inner.LoadAsync(ct);
        public Task SaveAsync(AllianceDirectory d, CancellationToken ct)
        {
            if (++Saves == FailAt) throw new IOException("Injected directory failure");
            return Inner.SaveAsync(d, ct);
        }
    }
    private sealed class PlayerStore : IPlayerStore
    {
        private readonly InMemoryPlayerStore _inner = new(Json.Options);
        public bool FailNext;
        public Func<Task>? DuringSave;
        public Task<PlayerRecord?> LoadAsync(string id, CancellationToken ct) => _inner.LoadAsync(id, ct);
        public Task<List<PlayerRecord>> ListAllAsync(CancellationToken ct) => _inner.ListAllAsync(ct);
        public async Task SaveAsync(PlayerRecord r, CancellationToken ct)
        {
            if (FailNext) { FailNext = false; throw new IOException("Injected player save failure"); }
            if (DuringSave != null) { var callback = DuringSave; DuringSave = null; await callback(); }
            await _inner.SaveAsync(r, ct);
        }
    }
    private sealed class Fixture
    {
        public readonly ManualClock Clock = new(1_700_000_000_000);
        public readonly PlayerStore Players = new();
        public readonly Store Alliances = new();
        public AllianceService Service = null!;
        public WorldService World = null!;
        public (int x, int y) Point;
        public async Task Init()
        {
            var sites = SurfaceLogic.Sites();
            var s = sites.First(s => s.z == SurfaceZone.Outer && s.x > 730 && Math.Abs(s.y - 400) < 10);
            var neighbor = sites.Where(n => n.z == s.z && n != s).OrderBy(n => SurfaceLogic.Dist2(s.x, s.y, n.x, n.y)).First();
            foreach (var entry in new[] { ("p1", s), ("p2", s), ("p3", neighbor) })
            {
                var city = CityState.CreateNew(Clock.NowMs);
                foreach (var b in CityConfig.AllBuildings) city.Buildings[b] = 0;
                city.Buildings[BuildingType.Keep] = 3;
                city.Surface.X = entry.Item2.x; city.Surface.Y = entry.Item2.y;
                city.Resources[ResourceType.Wood] = 100_000; city.Resources[ResourceType.Stone] = 100_000;
                await Players.SaveAsync(new PlayerRecord { Id = entry.Item1, City = city }, Ct);
            }
            await Alliances.SaveAsync(new AllianceDirectory { Alliances = new()
            {
                new AllianceRecord { Id = "a", Members = new() { new() { PlayerId = "p1", Role = AllianceRole.Leader },
                    new() { PlayerId = "p2", Role = AllianceRole.Member } } },
                new AllianceRecord { Id = "b", Members = new() { new() { PlayerId = "p3", Role = AllianceRole.Leader } } }
            } }, Ct);
            var center = SurfaceLogic.CityCenter((await Players.LoadAsync("p1", Ct))!.City.Surface);
            var other = SurfaceLogic.CityCenter((await Players.LoadAsync("p3", Ct))!.City.Surface);
            var common = AllianceTerritory.Coverage(center.x, center.y, 10);
            common.IntersectWith(AllianceTerritory.Coverage(other.x, other.y, 10));
            Point = common.Select(SurfaceLogic.FromTile).First(p => AllianceTerritory.AllowedPoint(p.x, p.y));
            Restart();
        }
        public void Restart()
        {
            var sync = new AllianceSynchronization();
            World = new WorldService(Players, NullLogger<WorldService>.Instance, Alliances);
            var sessions = new SessionManager(Players, Clock, Json.Options, NullLogger<SessionManager>.Instance, World, Alliances, sync);
            Service = new AllianceService(Alliances, Clock, sessions, sync, World);
        }
        public Task<FlagResult> Place(int slot = 1, string actor = "p1", string? op = null, (int x, int y)? point = null) =>
            Service.PlaceFlagAsync(actor, slot, (point ?? Point).x, (point ?? Point).y, op ?? Guid.NewGuid().ToString(), Ct);
        public async Task<long> Wood(string id = "p1") => (await Players.LoadAsync(id, Ct))!.City.GetResource(ResourceType.Wood);
    }

    [Test]
    public static async Task Placement_PaysFixedSlotCostOnceAndEnforcesRoleAndCooldown()
    {
        var f = new Fixture(); await f.Init(); string op = Guid.NewGuid().ToString();
        Assert.Equal(FlagResult.Forbidden, await f.Place(actor: "p2"));
        Assert.Equal(FlagResult.Ok, await f.Place(slot: 3, op: op));
        Assert.Equal(FlagResult.Ok, await f.Place(slot: 3, op: op));
        Assert.Equal(70_000L, await f.Wood());
        Assert.Equal(FlagResult.Disconnected, await f.Place(slot: 1), "施工不能當連接根");
        Assert.Equal(FlagResult.Ok, await f.Service.RemoveFlagAsync("p1", 3, Ct));
        Assert.Equal(70_000L, await f.Wood(), "取消不退款");
        Assert.Equal(FlagResult.Cooldown, await f.Place(slot: 3));
        f.Clock.Advance(AllianceTerritory.RebuildCooldownMs);
        Assert.Equal(FlagResult.Ok, await f.Place(slot: 3));
        Assert.Equal(40_000L, await f.Wood());
    }
    [Test]
    public static async Task ConcurrentEnemyPlacement_ReservesCoverageBeforeCharging()
    {
        var f = new Fixture(); await f.Init();
        var results = await Task.WhenAll(f.Place(), f.Place(actor: "p3"));
        Assert.Equal(1, results.Count(r => r == FlagResult.Ok));
        Assert.Equal(1, results.Count(r => r == FlagResult.Conflict));
        Assert.Equal(190_000L, await f.Wood() + await f.Wood("p3"));
    }
    [Test]
    public static async Task PaymentDirectoryFailure_RestartReplaysWithoutDoubleCharge()
    {
        var f = new Fixture(); await f.Init(); string op = Guid.NewGuid().ToString();
        f.Alliances.FailAt = f.Alliances.Saves + 2;
        Assert.Equal(FlagResult.PersistenceFailed, await f.Place(op: op));
        Assert.Equal(90_000L, await f.Wood());
        f.Restart(); await f.Service.RecoverFlagPaymentsAsync(Ct);
        Assert.Equal(FlagResult.Ok, await f.Place(op: op));
        Assert.Equal(90_000L, await f.Wood());
        var flag = (await f.Alliances.LoadAsync(Ct)).Alliances.First().Flags.Single();
        Assert.True(flag.Paid && !flag.Active(f.Clock.NowMs), "重啟仍在施工");
        f.Clock.Advance(AllianceTerritory.BuildMs);
        Assert.True(flag.Active(f.Clock.NowMs), "精確完工");
    }
    [Test]
    public static async Task TerritoryGatherBonus_IsSnapshottedAndDoesNotStack()
    {
        var f = new Fixture(); await f.Init();
        var field = SurfaceLogic.Features().First(f => f.Type == FeatureType.ResourceField && SurfaceLogic.Zone(f.X, f.Y) == SurfaceZone.Outer);
        var d = await f.Alliances.LoadAsync(Ct);
        d.Alliances[0].Flags.Add(new AllianceFlag { Paid = true, X = field.X + 2, Y = field.Y, CompletesAtMs = f.Clock.NowMs });
        d.Alliances[0].Flags.Add(new AllianceFlag { Paid = true, X = field.X + 3, Y = field.Y, CompletesAtMs = f.Clock.NowMs });
        f.World.UpdateTerritory(d);
        var city = (await f.Players.LoadAsync("p1", Ct))!.City;
        city.Troops[TroopType.Shield] = 50;
        MarchLogic.Start(city, HeroId.Hold, new() { [TroopType.Shield] = 50 }, field.X, field.Y, f.Clock.NowMs, f.World, "p1");
        var m = city.Surface.Marches.Single();
        MarchLogic.Advance(city, m.ArriveAt, f.World, "p1");
        Assert.Equal(11_000, m.GatherBonusBasisPoints);
        Assert.Equal(GatherConfig.RatePerMinute(field.Level) * 11_000 / 10_000, m.GatherRatePerMin);
        f.World.UpdateTerritory(new AllianceDirectory());
        MarchLogic.Advance(city, m.ArriveAt + 1_000, f.World, "p1");
        Assert.Equal(11_000, m.GatherBonusBasisPoints, "撤旗後已開始的採集倍率不重算");
        Assert.Equal(10_000, f.World.GatherBonusBasisPoints("p1", m.Destination, m.ArriveAt + 1_000));
    }
    [Test]
    public static void DefenseBonus_IncreasesDefenseWithoutMutatingFrozenInput()
    {
        var attack = new SquadInput { Leader = "A", LeaderHp = 10_000, LeaderAttack = 500, Troop = TroopType.Shield, Count = 100 };
        var defense = new SquadInput { Leader = "D", LeaderHp = 10_000, LeaderDefense = 1_000, Troop = TroopType.Shield, Count = 100 };
        var normal = BattleEngine.Run(new[] { attack }, new[] { defense }, 1);
        var boosted = BattleEngine.Run(new[] { attack }, new[] { defense.WithDefenseBonus(10_500) }, 1);
        Assert.Equal(10_000, defense.DefenseBasisPoints);
        Assert.True(boosted.Actions.First().Damage < normal.Actions.First().Damage, "包含兵種與主將防禦的 5% 加成");
    }
    [Test]
    public static async Task Removal_RejectsBridgeAndConstructionDependents()
    {
        var f = new Fixture(); await f.Init();
        var d = await f.Alliances.LoadAsync(Ct);
        d.Alliances[0].Flags = new()
        {
            new AllianceFlag { Id = "one", Slot = 1, X = 730, Y = 400, Paid = true },
            new AllianceFlag { Id = "two", Slot = 2, X = 710, Y = 400, Paid = true },
            new AllianceFlag { Id = "three", Slot = 3, X = 690, Y = 400, Paid = true }
        };
        await f.Alliances.SaveAsync(d, Ct);
        Assert.Equal(FlagResult.Disconnected, await f.Service.RemoveFlagAsync("p1", 2, Ct));
        Assert.Equal(FlagResult.Ok, await f.Service.RemoveFlagAsync("p1", 3, Ct));
        d = await f.Alliances.LoadAsync(Ct);
        d.Alliances[0].Flags[1].CompletesAtMs = f.Clock.NowMs + 1_000;
        await f.Alliances.SaveAsync(d, Ct);
        Assert.Equal(FlagResult.Disconnected, await f.Service.RemoveFlagAsync("p1", 1, Ct));
        Assert.Equal(FlagResult.Ok, await f.Service.RemoveFlagAsync("p1", 2, Ct));
        Assert.Equal(FlagResult.Ok, await f.Service.RemoveFlagAsync("p1", 1, Ct));
    }
    [Test]
    public static async Task Dissolve_RequiresRemovingLiveFlagsFirst()
    {
        var f = new Fixture(); await f.Init();
        await f.Place(); await f.Service.LeaveAsync("p2", Ct);
        Assert.Equal(AllianceResult.PendingOperations, await f.Service.DissolveAsync("p1", Ct));
        await f.Service.RemoveFlagAsync("p1", 1, Ct);
        Assert.Equal(AllianceResult.Ok, await f.Service.DissolveAsync("p1", Ct));
    }
    [Test]
    public static async Task PlayerPaymentSaveFailure_RecoveryKeepsReservationAndPaysOnce()
    {
        var f = new Fixture(); await f.Init(); f.Players.FailNext = true;
        Assert.Equal(FlagResult.PersistenceFailed, await f.Place());
        Assert.Equal(100_000L, await f.Wood());
        var pending = (await f.Alliances.LoadAsync(Ct)).Alliances.First().Flags.Single();
        Assert.True(pending.Live && !pending.Paid, "預留不被誤當作完成");
        f.Restart(); await f.Service.RecoverFlagPaymentsAsync(Ct);
        Assert.Equal(90_000L, await f.Wood());
        await f.Service.RecoverFlagPaymentsAsync(Ct);
        Assert.Equal(90_000L, await f.Wood());
    }
    [Test]
    public static async Task MetadataSnapshot_RemainsReadableDuringPlayerSave()
    {
        var f = new Fixture(); await f.Init(); bool read = false;
        f.Players.DuringSave = async () =>
        {
            var snapshot = await Task.Run(() => f.Service.SnapshotAsync(Ct)).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(snapshot.Alliances.First().Flags.Single().Live && !snapshot.Alliances.First().Flags.Single().Paid,
                "讀者看到持久化預留，沒有被玩家存檔占住聯盟目錄鎖");
            read = true;
        };
        Assert.Equal(FlagResult.Ok, await f.Place());
        Assert.True(read, "並行讀取完成");
    }
}
