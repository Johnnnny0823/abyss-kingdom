using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Tests;

public static class HeroTests
{
    const long T0 = 1_700_000_000_000;

    [Test]
    public static void NewCity_HasThreeStarterHeroesAndExpBooks()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(3, c.Heroes.Count);
        foreach (var def in HeroConfig.All) Assert.Equal(1, c.Heroes[def.Id].Level, def.Id.ToString());
        Assert.Equal(20L, c.GetItemCount(ItemType.HeroExpSmall));
        Assert.Equal(5L, c.GetItemCount(ItemType.HeroExpMedium));
    }

    [Test]
    public static void OldSave_GetsStarterHeroesOnce()
    {
        var c = CityState.CreateNew(T0);
        c.Heroes.Clear();
        c.Items.Clear();
        Assert.True(HeroLogic.EnsureStarterHeroes(c), "舊存檔補發");
        Assert.True(!HeroLogic.EnsureStarterHeroes(c), "已有主將就不再發");
        Assert.Equal(20L, c.GetItemCount(ItemType.HeroExpSmall), "經驗書只發一次");
    }

    [Test]
    public static void EachTroopTypeHasASpecialist()
    {
        foreach (var t in CityConfig.AllTroops)
            Assert.True(HeroConfig.All.Any(h => h.Specialty == t), t + " 需要至少一位專精主將");
    }

    [Test]
    public static void Stats_GrowWithLevelAndGrade()
    {
        Assert.True(HeroConfig.TryGet(HeroId.Vesper, out var vesper), "Vesper 存在");
        var lv1 = HeroConfig.Stats(vesper, 1);
        var lv10 = HeroConfig.Stats(vesper, 10);
        Assert.Equal(vesper.BaseAttack, lv1.Attack);
        Assert.Equal(vesper.BaseAttack + 9 * vesper.AttackGrowth, lv10.Attack);
        Assert.Equal(200L + 50 * (int)RopeGrade.Purple, lv1.Leadership, "紫色統率");
        Assert.Equal(lv1.Leadership + 9 * 20, lv10.Leadership, "每級 +20 統率");
    }

    [Test]
    public static void AddExp_LevelsUpAcrossMultipleLevels()
    {
        var h = new HeroState { Id = HeroId.Hold };
        HeroLogic.AddExp(h, 100 + 400 + 50); // 1→2 要 100，2→3 要 400
        Assert.Equal(3, h.Level);
        Assert.Equal(50L, h.Exp, "剩餘經驗");
    }

    [Test]
    public static void AddExp_StopsAtMaxLevel()
    {
        var h = new HeroState { Id = HeroId.Hold };
        HeroLogic.AddExp(h, long.MaxValue / 4);
        Assert.Equal(HeroConfig.MaxLevel, h.Level);
        Assert.Equal(0L, h.Exp, "滿級後經驗歸零");
    }

    [Test]
    public static void UseExpItem_ConsumesBooks()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(HeroResult.Ok, HeroLogic.UseExpItem(c, HeroId.Karst, ItemType.HeroExpMedium, 1));
        Assert.Equal(4L, c.GetItemCount(ItemType.HeroExpMedium));
        Assert.True(c.Heroes[HeroId.Karst].Level > 1, "1000 經驗應該升了好幾級");
    }

    [Test]
    public static void UseExpItem_Rejections()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(HeroResult.NotExpItem, HeroLogic.UseExpItem(c, HeroId.Hold, ItemType.SpeedupGeneral1m, 1));
        Assert.Equal(HeroResult.NotEnoughItems, HeroLogic.UseExpItem(c, HeroId.Hold, ItemType.HeroExpLarge, 1));
        Assert.Equal(HeroResult.InvalidCount, HeroLogic.UseExpItem(c, HeroId.Hold, ItemType.HeroExpSmall, 0));
        Assert.Equal(HeroResult.UnknownHero, HeroLogic.UseExpItem(c, (HeroId)99, ItemType.HeroExpSmall, 1));
        c.Heroes.Remove(HeroId.Hold);
        Assert.Equal(HeroResult.HeroNotOwned, HeroLogic.UseExpItem(c, HeroId.Hold, ItemType.HeroExpSmall, 1));
        c.Heroes[HeroId.Karst].Level = HeroConfig.MaxLevel;
        Assert.Equal(HeroResult.MaxLevel, HeroLogic.UseExpItem(c, HeroId.Karst, ItemType.HeroExpSmall, 1));
        Assert.Equal(20L, c.GetItemCount(ItemType.HeroExpSmall), "被拒絕不扣道具");
    }
}
