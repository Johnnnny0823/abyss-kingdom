using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class MarchTests
{
    const long T0 = 1_700_000_000_000;
    const int C = SurfaceConfig.Center;

    /// <summary>主城放在外環正右方（地圖中心的東邊）的城市。</summary>
    static CityState City()
    {
        var c = CityState.CreateNew(T0);
        var site = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer && s.y > C - 15 && s.y < C + 15 && s.x > C + 320);
        c.Surface.X = site.x;
        c.Surface.Y = site.y;
        c.Troops[TroopType.Shield] = 300;
        c.Troops[TroopType.Rider] = 300;
        return c;
    }

    static Dictionary<TroopType, long> T(TroopType t, long n) => new() { [t] = n };

    static (int x, int y) Near(CityState c, int dx, int dy)
    {
        var (hx, hy) = SurfaceLogic.CityCenter(c.Surface);
        for (int r = 0; r < 10; r++)
            if (SurfaceLogic.Walkable(hx + dx + r, hy + dy)) return (hx + dx + r, hy + dy);
        throw new Exception("找不到可以站的格子");
    }

    static bool SegmentWalkable((int x, int y) a, (int x, int y) b)
    {
        long d = Math.Max(1, SurfaceLogic.Distance(a.x, a.y, b.x, b.y));
        for (long i = 0; i <= d; i++)
        {
            int x = (int)(a.x + (b.x - a.x) * i / d), y = (int)(a.y + (b.y - a.y) * i / d);
            if (SurfaceLogic.Zone(x, y) == SurfaceZone.Pit || SurfaceLogic.IsWall(x, y)) return false;
        }
        return true;
    }

    [Test]
    public static void Start_DeductsTroops_AndSchedulesByDistance()
    {
        var c = City();
        var dest = Near(c, -20, 0);
        Assert.Equal(MarchResult.Ok, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 100), dest.x, dest.y, T0));
        Assert.Equal(200L, c.GetTroops(TroopType.Shield), "部隊從兵營扣除");
        var m = c.Surface.Marches[0];
        Assert.Equal(2, m.Path.Count, "同分區、近距離 = 直線");
        var (hx, hy) = SurfaceLogic.CityCenter(c.Surface);
        long d = SurfaceLogic.Distance(hx, hy, dest.x, dest.y);
        Assert.Equal(T0 + d * MarchConfig.MsPerTile, m.ArriveAt, "時間 = 距離 × 每格毫秒");
    }

    [Test]
    public static void Riders_AreFaster_MixedUsesSlowest()
    {
        var a = City();
        var b = City();
        var mixed = City();
        var dest = Near(a, -25, 3);
        MarchLogic.Start(a, HeroId.Hold, T(TroopType.Shield, 50), dest.x, dest.y, T0);
        MarchLogic.Start(b, HeroId.Hold, T(TroopType.Rider, 50), dest.x, dest.y, T0);
        Assert.True(b.Surface.Marches[0].ArriveAt < a.Surface.Marches[0].ArriveAt, "裂蹄騎先到");
        MarchLogic.Start(mixed, HeroId.Hold, new() { [TroopType.Rider] = 50, [TroopType.Shield] = 1 }, dest.x, dest.y, T0);
        Assert.Equal(a.Surface.Marches[0].ArriveAt, mixed.Surface.Marches[0].ArriveAt, "混編以最慢的為準");
    }

    [Test]
    public static void CrossingZones_GoesThroughPasses()
    {
        var c = City();
        var (hx, hy) = SurfaceLogic.CityCenter(c.Surface);
        // 從東邊外環到西邊崖緣：要過兩道山脈，而且不能穿過深淵
        int tx = C - 110, ty = C + 5;
        Assert.Equal(SurfaceZone.Rim, SurfaceLogic.Zone(tx, ty));
        var path = MarchLogic.FindPath(hx, hy, tx, ty)!;
        Assert.True(path.Count(p => SurfaceLogic.NearPass(p.x, p.y)) >= 4, "經過兩個關隘（每個關隘內外各一點）");
        for (int i = 1; i < path.Count; i++)
            Assert.True(SegmentWalkable(path[i - 1], path[i]), $"第 {i} 段不穿過山脈或深淵");
    }

    [Test]
    public static void SameZone_FarSide_GoesAroundTheRing()
    {
        var c = City();
        var (hx, hy) = SurfaceLogic.CityCenter(c.Surface);
        var other = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer && s.x < C - 300 && Math.Abs(s.y - C) < 20);
        var path = MarchLogic.FindPath(hx, hy, other.x, other.y)!;
        Assert.True(path.Count > 2, "地圖對面要繞環走");
        for (int i = 1; i < path.Count; i++)
            Assert.True(SegmentWalkable(path[i - 1], path[i]), $"第 {i} 段不穿過內側山脈");
        // 走中環抄近路（經兩個關隘）也是合法的；只要求每段都走得通
    }

    [Test]
    public static void Arrive_Station_ThenRecall_ReturnsTroops()
    {
        var c = City();
        var dest = Near(c, -15, 4);
        MarchLogic.Start(c, HeroId.Karst, T(TroopType.Rider, 50), dest.x, dest.y, T0);
        var m = c.Surface.Marches[0];
        Assert.Equal(0, MarchLogic.Advance(c, m.ArriveAt - 1).Count, "還沒到");
        var events = MarchLogic.Advance(c, m.ArriveAt);
        Assert.Equal(1, events.Count);
        Assert.True(m.Stationed, "駐紮");
        Assert.Equal(MarchResult.Ok, MarchLogic.Recall(c, m.Id, m.ArriveAt));
        Assert.True(m.Returning, "召回中");
        var back = MarchLogic.Advance(c, m.ArriveAt);
        Assert.True(back.Single().Disbanded, "回城解散");
        Assert.Equal(300L, c.GetTroops(TroopType.Rider), "部隊歸營");
        Assert.Equal(0, c.Surface.Marches.Count);
    }

    [Test]
    public static void Recall_WhileMoving_TurnsBackFromCurrentPosition()
    {
        var c = City();
        var dest = Near(c, -30, 0);
        MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 10), dest.x, dest.y, T0);
        var m = c.Surface.Marches[0];
        long half = (m.ArriveAt - T0) / 2;
        Assert.Equal(MarchResult.Ok, MarchLogic.Recall(c, m.Id, T0 + half), "路上也能召回");
        Assert.True(m.ArriveAt - (T0 + half) <= half + MarchConfig.MsPerTile * 2, "從半路折返，回程約一半時間");
    }

    [Test]
    public static void Start_Validates()
    {
        var c = City();
        var dest = Near(c, -12, 0);
        Assert.Equal(MarchResult.NoTroops, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 0), dest.x, dest.y, T0));
        Assert.Equal(MarchResult.NotEnoughTroops, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Crossbow, 1), dest.x, dest.y, T0), "沒有鳴弦手");
        Assert.Equal(MarchResult.ExceedsLeadership, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 300), dest.x, dest.y, T0), "超過統率");
        Assert.Equal(MarchResult.AlreadyThere, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 1), c.Surface.X, c.Surface.Y, T0));
        Assert.Equal(MarchResult.UnknownNode, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 1), C, C, T0), "深淵裡不能去");
        Assert.Equal(MarchResult.Ok, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 10), dest.x, dest.y, T0));
        Assert.Equal(MarchResult.HeroBusy, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 10), dest.x, dest.y, T0), "主將已在行軍");
        MarchLogic.Start(c, HeroId.Vesper, T(TroopType.Shield, 10), dest.x, dest.y, T0);
        MarchLogic.Start(c, HeroId.Karst, T(TroopType.Shield, 10), dest.x, dest.y, T0);
        c.Heroes[(HeroId)4] = new HeroState();
        Assert.Equal(MarchResult.TooManyMarches, MarchLogic.Start(c, (HeroId)4, T(TroopType.Shield, 1), dest.x, dest.y, T0), "最多 3 支");

        var homeless = CityState.CreateNew(T0);
        Assert.Equal(MarchResult.NoCity, MarchLogic.Start(homeless, HeroId.Hold, T(TroopType.Shield, 1), dest.x, dest.y, T0), "還沒有主城位置");
    }
}
