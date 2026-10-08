using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

public static class HealingTests
{
    const long T0 = 1_700_000_000_000;

    static CityState City()
    {
        var c = CityState.CreateNew(T0);
        c.Wounded[TroopType.Shield] = 60;
        c.Wounded[TroopType.Rider] = 20;
        return c;
    }

    [Test]
    public static void Heal_CostsHalf_TakesHalfTime_ThenTroopsReturn()
    {
        var c = City();
        long food = c.GetResource(ResourceType.Food);
        var troops = new Dictionary<TroopType, long> { [TroopType.Shield] = 40, [TroopType.Rider] = 20 };
        Assert.Equal(HealResult.Ok, CityLogic.StartHealing(c, troops, T0));
        Assert.Equal(food - (40 * 20 / 2 + 20 * 30 / 2), c.GetResource(ResourceType.Food), "菌糧 = 訓練成本的一半");
        Assert.Equal(20L, c.Wounded[TroopType.Shield], "治療中的傷兵離開醫館名單");
        long ms = 40 * 1000 + 20 * 1500;
        Assert.Equal(T0 + ms, c.Healing.FinishAtMs, "時間 = 訓練時間的一半");

        CityLogic.Advance(c, T0 + ms - 1, out _, out var notYet);
        Assert.True(notYet == null, "還沒好");
        CityLogic.Advance(c, T0 + ms, out _, out var done);
        Assert.True(done != null && c.Healing == null, "治療完成");
        Assert.Equal(40L, c.GetTroops(TroopType.Shield), "回到兵營");
        Assert.Equal(20L, c.GetTroops(TroopType.Rider));
    }

    [Test]
    public static void Heal_Validates()
    {
        var c = City();
        Assert.Equal(HealResult.NothingToHeal, CityLogic.StartHealing(c, new() { [TroopType.Shield] = 0 }, T0));
        Assert.Equal(HealResult.NotEnoughWounded, CityLogic.StartHealing(c, new() { [TroopType.Crossbow] = 1 }, T0));
        Assert.Equal(HealResult.Ok, CityLogic.StartHealing(c, new() { [TroopType.Shield] = 1 }, T0));
        Assert.Equal(HealResult.QueueBusy, CityLogic.StartHealing(c, new() { [TroopType.Shield] = 1 }, T0), "同時只能一批");
        var poor = City();
        foreach (var r in CityConfig.AllResources) poor.Resources[r] = 0;
        Assert.Equal(HealResult.NotEnoughResources, CityLogic.StartHealing(poor, new() { [TroopType.Shield] = 10 }, T0));
    }

    [Test]
    public static void Cancel_ReturnsWounded_AndRefundsHalf()
    {
        var c = City();
        long food = c.GetResource(ResourceType.Food);
        CityLogic.StartHealing(c, new() { [TroopType.Shield] = 60 }, T0);
        Assert.Equal(CancelResult.Ok, CityLogic.Cancel(c, QueueType.Healing, T0 + 1));
        Assert.Equal(60L, c.Wounded[TroopType.Shield], "傷兵回到醫館");
        Assert.Equal(food - 600 + 300, c.GetResource(ResourceType.Food), "退還一半");
    }

    [Test]
    public static void Speedup_Free_WorksOnHealing()
    {
        var c = City();
        CityLogic.StartHealing(c, new() { [TroopType.Shield] = 10 }, T0);
        Assert.Equal(SpeedupResult.Ok, CityLogic.SpeedupFree(c, QueueType.Healing, T0 + 1), "5 分鐘內免費完成");
        CityLogic.Advance(c, T0 + 1, out _, out var done);
        Assert.True(done != null, "立即完成");
    }
}
