using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Tests;

public static class StarTests
{
    const long T0 = 1_700_000_000_000;

    static CityState Rich()
    {
        var c = CityState.CreateNew(T0);
        c.Resources[ResourceType.Crystal] = 1_000_000;
        return c;
    }

    [Test]
    public static void Costs_GrowByTier()
    {
        Assert.Equal(10L, StarConfig.ShardCost(0), "金星");
        Assert.Equal(10L, StarConfig.ShardCost(4), "第 5 顆金星");
        Assert.Equal(20L, StarConfig.ShardCost(5), "第 1 顆藍星");
        Assert.Equal(40L, StarConfig.ShardCost(10), "紅星");
        Assert.Equal(80L, StarConfig.ShardCost(15), "銀彩星");
        Assert.Equal(20, StarConfig.MaxStars);
    }

    [Test]
    public static void StarUp_UsesOwnShardsFirstThenGeneric()
    {
        var c = Rich();
        c.Items[ItemType.HeroShardHold] = 6;
        c.Items[ItemType.HeroShardAny] = 10;
        Assert.Equal(StarResult.Ok, StarLogic.StarUp(c, HeroId.Hold));
        Assert.Equal(1, c.Heroes[HeroId.Hold].Stars);
        Assert.Equal(0L, c.GetItemCount(ItemType.HeroShardHold), "專屬 6 個先用完");
        Assert.Equal(6L, c.GetItemCount(ItemType.HeroShardAny), "通用補 4 個");
        Assert.Equal(1_000_000 - 500L, c.GetResource(ResourceType.Crystal), "金星耗 500 響晶");
    }

    [Test]
    public static void StarUp_Rejections()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(StarResult.NotEnoughShards, StarLogic.StarUp(c, HeroId.Vesper));
        c.Items[ItemType.HeroShardVesper] = 10;
        c.Resources[ResourceType.Crystal] = 100;
        Assert.Equal(StarResult.NotEnoughResources, StarLogic.StarUp(c, HeroId.Vesper));
        Assert.Equal(10L, c.GetItemCount(ItemType.HeroShardVesper), "失敗不扣信物");
        c.Heroes[HeroId.Vesper].Stars = StarConfig.MaxStars;
        Assert.Equal(StarResult.MaxStars, StarLogic.StarUp(c, HeroId.Vesper));
        Assert.Equal(StarResult.UnknownHero, StarLogic.StarUp(c, (HeroId)99));
    }

    [Test]
    public static void Stars_RaiseStatsAndPower()
    {
        var c = CityState.CreateNew(T0);
        var before = PowerLogic.TotalStats(c, HeroId.Karst);
        long powerBefore = PowerLogic.HeroPower(c, HeroId.Karst);
        c.Heroes[HeroId.Karst].Stars = 5;
        var after = PowerLogic.TotalStats(c, HeroId.Karst);
        Assert.Equal(before.Attack * 120 / 100, after.Attack, "5 星攻擊 +20%");
        Assert.Equal(before.Leadership * 110 / 100, after.Leadership, "5 星統率 +10%");
        Assert.True(PowerLogic.HeroPower(c, HeroId.Karst) > powerBefore, "戰力上升");
    }

    [Test]
    public static void Trial_NeedsMatchingStarColor()
    {
        var h = new HeroState { Id = HeroId.Hold };
        Assert.True(!DomainLogic.StarsReady(h), "0 星不能挑戰第 1 股");
        h.Stars = 5;
        Assert.True(DomainLogic.StarsReady(h), "5 金星可挑戰第 1 股");
        h.Mastered.Add(Domain.Power);
        Assert.True(!DomainLogic.StarsReady(h), "第 2 股要藍星升滿");
        h.Stars = 10;
        Assert.True(DomainLogic.StarsReady(h), "5 金 + 5 藍");
    }

    [Test]
    public static void Expedition_DropsShards()
    {
        var c = CityState.CreateNew(T0);
        ExpeditionLogic.Fight(c, 1, new List<IList<HeroId>> { new[] { HeroId.Hold, HeroId.Vesper, HeroId.Karst } }, 3, T0, out var r);
        Assert.True(r.Win, "第 1 關會贏");
        Assert.Equal(7L, c.GetItemCount(ItemType.HeroShardHold), "第 1 關：霍德信物 6+1");
        Assert.Equal(3L, c.GetItemCount(ItemType.HeroShardAny));
        Assert.Equal(30L, ExpeditionConfig.Stage(10).ItemRewards[ItemType.HeroShardAny], "守層者掉大量通用信物");
    }
}
