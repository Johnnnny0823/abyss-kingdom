using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class GatherTests
{
    const long T0 = 1_700_000_000_000;
    const long Min = 60_000;

    /// <summary>城市＋離它最近的一塊外環資源田（同分區、直線可達）。</summary>
    static (CityState city, Feature field) Setup()
    {
        foreach (var f in SurfaceLogic.Features().Where(f => f.Type == FeatureType.ResourceField && f.Level == 1))
        {
            var site = SurfaceLogic.Sites().FirstOrDefault(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, f.X, f.Y) < 15 * 15);
            if (site == default) continue;
            var c = CityState.CreateNew(T0);
            c.Surface.X = site.x;
            c.Surface.Y = site.y;
            c.Troops[TroopType.Shield] = 300;
            return (c, f);
        }
        throw new Exception("找不到測試用的資源田");
    }

    static Dictionary<TroopType, long> Shields(long n) => new() { [TroopType.Shield] = n };

    [Test]
    public static void Arrive_StartsGathering_CapByLoad()
    {
        var (c, f) = Setup();
        var reg = new SimpleFieldRegistry();
        Assert.Equal(MarchResult.Ok, MarchLogic.Start(c, HeroId.Hold, Shields(50), f.X, f.Y, T0, reg, "me"));
        var m = c.Surface.Marches[0];
        var ev = MarchLogic.Advance(c, m.ArriveAt, reg, "me");
        Assert.True(ev.Single().Gathering, "抵達資源田就開始採集");
        Assert.Equal(m.ArriveAt, m.GatherStartMs, "從抵達時間起算");
        Assert.Equal(50 * GatherConfig.LoadPerTroop(TroopType.Shield), m.GatherCap, "上限 = 載重（存量更多）");
        Assert.Equal(f.Resource, m.GatherResource);
        Assert.Equal(GatherConfig.RatePerMinute(1) * 3, MarchLogic.Gathered(m, m.ArriveAt + 3 * Min), "每分鐘固定採集量");
    }

    [Test]
    public static void Full_AutoReturns_AndResourcesArriveHome()
    {
        var (c, f) = Setup();
        var reg = new SimpleFieldRegistry();
        MarchLogic.Start(c, HeroId.Hold, Shields(50), f.X, f.Y, T0, reg, "me");
        var m = c.Surface.Marches[0];
        MarchLogic.Advance(c, m.ArriveAt, reg, "me");
        long before = c.GetResource(f.Resource);
        long end = m.GatherEndMs;

        var ev = MarchLogic.Advance(c, end, reg, "me");
        Assert.True(ev.Any(e => e.GatherDone), "採滿自動回城");
        Assert.True(m.Returning, "回程中");
        Assert.Equal(m.GatherCap, m.Carry[f.Resource], "背著採到的資源");
        Assert.Equal(GatherConfig.FieldStock(1) - m.Carry[f.Resource], reg.Stock(SurfaceLogic.Tile(f.X, f.Y)), "資源田存量扣掉");

        var back = MarchLogic.Advance(c, m.ArriveAt, reg, "me");
        Assert.True(back.Single().Disbanded, "回城解散");
        Assert.Equal(before + 600, c.GetResource(f.Resource), "資源入庫（50 × 12）");
        Assert.Equal(300L, c.GetTroops(TroopType.Shield), "部隊歸營");
    }

    [Test]
    public static void Recall_WhileGathering_BringsPartial()
    {
        var (c, f) = Setup();
        var reg = new SimpleFieldRegistry();
        MarchLogic.Start(c, HeroId.Hold, Shields(100), f.X, f.Y, T0, reg, "me");
        var m = c.Surface.Marches[0];
        MarchLogic.Advance(c, m.ArriveAt, reg, "me");
        long t = m.ArriveAt + 5 * Min;
        Assert.Equal(MarchResult.Ok, MarchLogic.Recall(c, m.Id, t, reg, "me"));
        Assert.Equal(GatherConfig.RatePerMinute(1) * 5, m.Carry[f.Resource], "只帶回 5 分鐘的量");
        Assert.True(!m.Gathering && m.Returning, "停止採集、回程");
        // 放開之後別人可以採
        Assert.True(reg.Occupy(SurfaceLogic.Tile(f.X, f.Y), "other", 1, t) > 0, "資源田已放開");
    }

    [Test]
    public static void Occupied_Field_JustStations()
    {
        var (c, f) = Setup();
        var reg = new SimpleFieldRegistry();
        reg.Occupy(SurfaceLogic.Tile(f.X, f.Y), "someone-else", 9, T0);
        MarchLogic.Start(c, HeroId.Hold, Shields(10), f.X, f.Y, T0, reg, "me");
        var m = c.Surface.Marches[0];
        var ev = MarchLogic.Advance(c, m.ArriveAt, reg, "me");
        Assert.True(ev.Single().Occupied, "已被佔用");
        Assert.True(m.Stationed && !m.Gathering, "只能駐紮");
    }

    [Test]
    public static void Offline_LongTime_GathersAndComesHome()
    {
        var (c, f) = Setup();
        var reg = new SimpleFieldRegistry();
        MarchLogic.Start(c, HeroId.Hold, Shields(20), f.X, f.Y, T0, reg, "me");
        long before = c.GetResource(f.Resource);
        var ev = MarchLogic.Advance(c, T0 + 24 * 60 * Min, reg, "me");
        Assert.True(ev.Any(e => e.Gathering) && ev.Any(e => e.GatherDone) && ev.Any(e => e.Disbanded), "一次推進：抵達、採滿、回城");
        Assert.Equal(0, c.Surface.Marches.Count);
        Assert.Equal(before + 20 * 12, c.GetResource(f.Resource), "資源入庫");
    }

    [Test]
    public static void Field_Stock_LimitsCap()
    {
        var (c, f) = Setup();
        var reg = new SimpleFieldRegistry();
        int tile = SurfaceLogic.Tile(f.X, f.Y);
        reg.Occupy(tile, "x", 1, T0);
        reg.Release(tile, "x", 1, GatherConfig.FieldStock(1) - 100, T0); // 只剩 100
        MarchLogic.Start(c, HeroId.Hold, Shields(100), f.X, f.Y, T0, reg, "me");
        var m = c.Surface.Marches[0];
        MarchLogic.Advance(c, m.ArriveAt, reg, "me");
        Assert.Equal(100L, m.GatherCap, "存量比載重少：上限 = 存量");
    }
}
