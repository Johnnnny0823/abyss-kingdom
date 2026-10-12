using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

public static class CityZoneTests
{
    [Test]
    public static void Zones_DoNotOverlapCoreOrEachOther()
    {
        foreach (var z in CityZones.All)
        {
            for (int y = z.Y; y < z.Y + z.H; y++)
                for (int x = z.X; x < z.X + z.W; x++)
                {
                    bool inCore = x >= 0 && y >= 0 && x < CityLayout.GridSize && y < CityLayout.GridSize;
                    Assert.True(!inCore, $"{z.Id} 不可蓋到核心區 ({x},{y})");
                    Assert.Equal(z.Id, CityZones.ZoneAt(x, y)!.Id, $"({x},{y}) 只屬於 {z.Id}");
                }
            Assert.True(z.Y >= 0, $"{z.Id} 不可伸進北側懸崖");
        }
    }

    [Test]
    public static void ZoneAt_CoreAndCliff_ReturnsNull()
    {
        Assert.True(CityZones.ZoneAt(0, 0) == null, "核心區不是擴張區");
        Assert.True(CityZones.ZoneAt(CityLayout.GridSize - 1, CityLayout.GridSize - 1) == null, "核心區角落");
        Assert.True(CityZones.ZoneAt(5, -1) == null, "北側懸崖");
    }

    [Test]
    public static void Zones_UnlockOrder_HousingBeforeResearch()
    {
        var housing = System.Array.Find(CityZones.All, z => z.Id == CityZoneId.Housing)!;
        var research = System.Array.Find(CityZones.All, z => z.Id == CityZoneId.Research)!;
        Assert.Equal(3, housing.MinKeepLevel, "民居區主堡 Lv.3");
        Assert.Equal(5, research.MinKeepLevel, "研究區主堡 Lv.5");
    }

    [Test]
    public static void IsOpen_NeedsFeatureAndKeepLevel()
    {
        var notReady = new CityZone(CityZoneId.Housing, 24, 0, 12, 24, 3, false);
        Assert.True(!CityZones.IsOpen(notReady, 10), "功能沒上線，主堡再高也不開");
        var ready = new CityZone(CityZoneId.Housing, 24, 0, 12, 24, 3, true);
        Assert.True(!CityZones.IsOpen(ready, 2), "主堡等級不夠");
        Assert.True(CityZones.IsOpen(ready, 3), "剛好達到門檻");
        foreach (var z in CityZones.All)
            Assert.True(!CityZones.IsOpen(z, 10), $"{z.Id} 的建築功能尚未上線，目前不開放");
    }
}
