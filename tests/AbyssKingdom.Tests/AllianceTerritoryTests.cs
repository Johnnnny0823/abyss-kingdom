using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Tests;

public static class AllianceTerritoryTests
{
    [Test]
    public static void Coverage_UsesPathDistanceAndStaysWithinZone()
    {
        var point = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer);
        var cells = AllianceTerritory.Coverage(point.x, point.y);
        Assert.True(cells.Count > 100 && cells.Count <= 313, "12 格四向領地有界");
        foreach (int tile in cells)
        {
            var p = SurfaceLogic.FromTile(tile);
            Assert.Equal(SurfaceZone.Outer, SurfaceLogic.Zone(p.x, p.y));
            Assert.True(SurfaceLogic.Walkable(p.x, p.y) && !SurfaceLogic.NearPass(p.x, p.y), "不越山脈或關隘");
            Assert.True(Math.Abs(p.x - point.x) + Math.Abs(p.y - point.y) <= 12, "四向最短路徑半徑");
        }
        var repeated = AllianceTerritory.Coverage(point.x, point.y);
        Assert.True(cells.SetEquals(repeated), "相同輸入確定性");
    }
    [Test]
    public static void FlagPoint_ExcludesEveryCityAndFixedFeature()
    {
        foreach (var s in SurfaceLogic.Sites()) Assert.True(!AllianceTerritory.AllowedPoint(s.x, s.y), "保留主城候選位置");
        foreach (var f in SurfaceLogic.Features()) Assert.True(!AllianceTerritory.AllowedPoint(f.X, f.Y), "保留固定玩法物件");
        var pass = SurfaceLogic.Pass(0, 0);
        Assert.True(!AllianceTerritory.AllowedPoint(pass.x, pass.y), "關隘不能蓋旗");
        Assert.True(!AllianceTerritory.AllowedPoint(400, 400), "深淵不能蓋旗");
    }
    [Test]
    public static void Connectivity_RemovingMiddleBreaksChain()
    {
        var a = new HashSet<int> { SurfaceLogic.Tile(10, 10) };
        var b = new HashSet<int> { SurfaceLogic.Tile(11, 10) };
        var c = new HashSet<int> { SurfaceLogic.Tile(12, 10) };
        Assert.True(AllianceTerritory.Connected(new[] { a, b, c }), "四向相鄰可連接");
        Assert.True(!AllianceTerritory.Connected(new[] { a, c }), "拆中間不能斷開");
        Assert.True(!AllianceTerritory.Touches(a, new() { SurfaceLogic.Tile(11, 11) }), "斜角不算相鄰");
    }
    [Test]
    public static void Bonus_RequiresPaymentAndExactCompletionBoundary()
    {
        var s = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer);
        var flag = new AllianceFlag { X = s.x, Y = s.y, CompletesAtMs = 1_000 };
        int tile = SurfaceLogic.Tile(s.x, s.y);
        Assert.True(!AllianceTerritory.Contains(new[] { flag }, tile, 1_000), "未付款不啟用");
        flag.Paid = true;
        Assert.True(!AllianceTerritory.Contains(new[] { flag }, tile, 999), "施工沒有加成");
        Assert.True(AllianceTerritory.Contains(new[] { flag }, tile, 1_000), "完工邊界啟用");
        flag.RemovedAtMs = 1_000;
        Assert.True(!AllianceTerritory.Contains(new[] { flag }, tile, 1_000), "撤旗即失效");
        Assert.Equal(30_000L, AllianceTerritory.Cost(3));
    }
}
