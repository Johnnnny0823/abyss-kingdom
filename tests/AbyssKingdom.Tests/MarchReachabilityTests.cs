using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

/// <summary>整張地圖的行軍可達性（靠近山脈的主城、站在關隘缺口裡的部隊都要走得到、走得回）。</summary>
public static class MarchReachabilityTests
{
    [Test]
    public static void EveryOuterCity_CanReachNearbyTiles_AndComeBack()
    {
        int bad = 0;
        foreach (var s in SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer))
        {
            int hx = s.x + 1, hy = s.y + 1;
            var dest = Enumerable.Range(-6, 13).SelectMany(dx => Enumerable.Range(-6, 13).Select(dy => (x: hx + dx, y: hy + dy)))
                .First(p => Math.Abs(p.x - hx) + Math.Abs(p.y - hy) >= 5 && SurfaceLogic.Walkable(p.x, p.y));
            if (MarchLogic.FindPath(hx, hy, dest.x, dest.y) == null || MarchLogic.FindPath(dest.x, dest.y, hx, hy) == null) bad++;
        }
        Assert.Equal(0, bad, "走不到附近格子的主城數");
    }

    [Test]
    public static void PassGaps_AreReachable()
    {
        int bad = 0;
        for (int b = 0; b < 2; b++)
            for (int i = 0; i < SurfaceConfig.PassesPerBoundary; i++)
            {
                var p = SurfaceLogic.Pass(b, i);
                for (int dx = -7; dx <= 7; dx++)
                    for (int dy = -7; dy <= 7; dy++)
                        if (SurfaceLogic.Walkable(p.x + dx, p.y + dy) && MarchLogic.FindPath(100, 400, p.x + dx, p.y + dy) == null) bad++;
            }
        Assert.Equal(0, bad, "關隘缺口裡走不到的格子數");
    }
}
