using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

public static class CityLayoutTests
{
    const long T0 = 1_700_000_000_000;

    static void AssertValid(CityState c)
    {
        var all = CityConfig.AllBuildings;
        foreach (var a in all)
        {
            var ca = c.Layout[a];
            Assert.True(CityLayout.InBounds(a, ca.X, ca.Y), $"{a} 在格子內");
            foreach (var b in all)
            {
                if (a >= b) continue;
                var cb = c.Layout[b];
                Assert.True(!(CityLayout.Overlaps(a, ca.X, ca.Y, b, cb.X, cb.Y)), $"{a} 和 {b} 不重疊");
            }
        }
    }

    [Test]
    public static void NewCity_HasValidDefaultLayout()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(CityConfig.AllBuildings.Length, c.Layout.Count, "每棟建築都有位置（含未建造的）");
        AssertValid(c);
        Assert.True(!(CityLayout.EnsureLayout(c)), "新城市不需要修正");
    }

    [Test]
    public static void Move_ToFreeSpot_Succeeds()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(MoveResult.Ok, CityLayout.Move(c, BuildingType.Barracks, 0, 21));
        Assert.Equal(0, c.Layout[BuildingType.Barracks].X);
        Assert.Equal(21, c.Layout[BuildingType.Barracks].Y);
        // 和自己原本的位置部分重疊也可以（往旁邊挪一格）
        Assert.Equal(MoveResult.Ok, CityLayout.Move(c, BuildingType.Barracks, 1, 21));
        AssertValid(c);
    }

    [Test]
    public static void Move_Rejects_OverlapAndOutOfBounds()
    {
        var c = CityState.CreateNew(T0);
        var keep = c.Layout[BuildingType.Keep];
        Assert.Equal(MoveResult.Overlaps, CityLayout.Move(c, BuildingType.Farm, keep.X + 1, keep.Y + 1));
        Assert.Equal(MoveResult.OutOfBounds, CityLayout.Move(c, BuildingType.Farm, CityLayout.GridSize - 2, 0), "3×3 超出右邊界");
        Assert.Equal(MoveResult.OutOfBounds, CityLayout.Move(c, BuildingType.Farm, -1, 0));
        Assert.Equal(MoveResult.UnknownBuilding, CityLayout.Move(c, (BuildingType)99, 0, 0));
        Assert.Equal(CityLayout.DefaultCell(BuildingType.Farm).X, c.Layout[BuildingType.Farm].X, "失敗時位置不變");
    }

    [Test]
    public static void EnsureLayout_FixesOldAndBrokenSaves()
    {
        var c = CityState.CreateNew(T0);
        c.Layout = null!; // 舊存檔沒有這個欄位
        Assert.True(CityLayout.EnsureLayout(c), "補上位置");
        AssertValid(c);

        // 壞資料：兩棟疊在一起、一棟超出邊界 → 自動挪開
        c.Layout[BuildingType.Farm] = new Cell(c.Layout[BuildingType.Sawmill].X, c.Layout[BuildingType.Sawmill].Y);
        c.Layout[BuildingType.Barracks] = new Cell(CityLayout.GridSize, 0);
        Assert.True(CityLayout.EnsureLayout(c), "需要修正");
        AssertValid(c);
    }

    [Test]
    public static void EnsureLayout_DefaultTaken_FindsAnotherSpot()
    {
        var c = CityState.CreateNew(T0);
        // 把主堡移到兵營預設位置旁邊，再刪掉兵營的位置 → 兵營要另外找地方
        c.Layout.Remove(BuildingType.Barracks);
        var d = CityLayout.DefaultCell(BuildingType.Barracks);
        c.Layout[BuildingType.Keep] = new Cell(d.X, d.Y);
        Assert.True(CityLayout.EnsureLayout(c), "需要修正");
        AssertValid(c);
        var b = c.Layout[BuildingType.Barracks];
        Assert.True(!(b.X == d.X && b.Y == d.Y), "預設位置被佔，換別處");
    }
}
