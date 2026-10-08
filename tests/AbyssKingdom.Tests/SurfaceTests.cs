using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class SurfaceTests
{
    const int C = SurfaceConfig.Center;

    [Test]
    public static void Zones_AreConcentric()
    {
        Assert.Equal(SurfaceZone.Pit, SurfaceLogic.Zone(C, C), "正中央是深淵");
        Assert.Equal(SurfaceZone.Rim, SurfaceLogic.Zone(C + 100, C));
        Assert.Equal(SurfaceZone.Middle, SurfaceLogic.Zone(C, C + 220));
        Assert.Equal(SurfaceZone.Outer, SurfaceLogic.Zone(C - 330, C));
        Assert.Equal(SurfaceZone.Edge, SurfaceLogic.Zone(0, 0), "角落是地圖外圍");
        Assert.True(!SurfaceLogic.Walkable(C, C) && !SurfaceLogic.Walkable(0, 0), "深淵與外圍不能站");
    }

    [Test]
    public static void Walls_HaveSixPassesEach()
    {
        for (int b = 0; b < 2; b++)
            for (int i = 0; i < SurfaceConfig.PassesPerBoundary; i++)
            {
                var p = SurfaceLogic.Pass(b, i);
                Assert.True(SurfaceLogic.Walkable(p.x, p.y), $"關隘 {b}-{i} 可以通過");
            }
        // 崖緣外圈的關隘在 0/60/120… 度，90 度方向是山脈；中環外圈的關隘在 30/90/150… 度，0 度方向是山脈
        Assert.True(SurfaceLogic.IsWall(C, C + SurfaceConfig.RimOuter), "崖緣外圈有山脈");
        Assert.True(SurfaceLogic.IsWall(C + SurfaceConfig.MiddleOuter, C), "中環外圈有山脈");
    }

    [Test]
    public static void ISqrt_IsExact()
    {
        foreach (var n in new long[] { 0, 1, 2, 3, 4, 15, 16, 17, 99, 100, 640000, 1_279_999, 1_280_000 })
        {
            long r = SurfaceLogic.ISqrt(n);
            Assert.True(r * r <= n && (r + 1) * (r + 1) > n, $"isqrt({n}) = {r}");
        }
    }

    [Test]
    public static void Sites_AreSpacedAndPlentiful()
    {
        var sites = SurfaceLogic.Sites();
        Assert.True(sites.Count >= 3000, $"主城候選位置足夠（{sites.Count}）");
        Assert.True(sites.Count(s => s.z == SurfaceZone.Outer) >= 1500, "外環有足夠的新手位置");
        Assert.True(sites.All(s => s.z != SurfaceZone.Pit && s.z != SurfaceZone.Edge), "不在深淵或外圍");
        // 候選位置之間至少隔 6 格（不會擠在一起）
        var set = sites.Select(s => (s.x, s.y)).ToList();
        for (int i = 0; i < 200; i++)
        {
            var a = set[i * 7 % set.Count];
            Assert.True(set.All(b => b == a || SurfaceLogic.Dist2(a.x, a.y, b.x, b.y) >= 36), "主城間距");
        }
    }

    [Test]
    public static void Features_HaveLevelsByZone()
    {
        var f = SurfaceLogic.Features();
        Assert.Equal(4, f.Count(x => x.Type == FeatureType.AbyssGate), "4 個深淵入口");
        Assert.True(f.Where(x => x.Type == FeatureType.AbyssGate).All(x => SurfaceLogic.Zone(x.X, x.Y) == SurfaceZone.Rim), "深淵入口在崖緣");
        var fields = f.Where(x => x.Type == FeatureType.ResourceField).ToList();
        Assert.True(fields.Count >= 300, $"資源田數量（{fields.Count}）");
        Assert.True(f.Count(x => x.Type == FeatureType.MonsterCamp) >= 100, "怪物營地數量");
        foreach (var x in fields)
        {
            var z = SurfaceLogic.Zone(x.X, x.Y);
            Assert.Equal(z == SurfaceZone.Rim ? 3 : z == SurfaceZone.Middle ? 2 : 1, x.Level, "等級依分區");
        }
        Assert.True(f.All(x => SurfaceLogic.Walkable(x.X, x.Y)), "都在能站的格子上");
        foreach (var s in SurfaceLogic.Sites().Take(500))
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    Assert.True(SurfaceLogic.FeatureAt(s.x + dx, s.y + dy) == null, "不和主城位置重疊");
    }

    [Test]
    public static void PickSite_PrefersOuterAndSkipsOccupied()
    {
        var occupied = new HashSet<int>();
        var first = SurfaceLogic.PickSite(occupied, n => 0)!.Value;
        Assert.Equal(SurfaceZone.Outer, SurfaceLogic.Zone(first.x, first.y), "新手在外環");
        occupied.Add(SurfaceLogic.Tile(first.x, first.y));
        var second = SurfaceLogic.PickSite(occupied, n => 0)!.Value;
        Assert.True(second != first, "不會分到同一塊地");

        foreach (var s in SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer)) occupied.Add(SurfaceLogic.Tile(s.x, s.y));
        var third = SurfaceLogic.PickSite(occupied, n => 0)!.Value;
        Assert.Equal(SurfaceZone.Middle, SurfaceLogic.Zone(third.x, third.y), "外環滿了往中環");
    }
}
