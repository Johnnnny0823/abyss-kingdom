using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

public static class CityLogicTests
{
    const long T0 = 1_700_000_000_000;
    const long Hour = CityConfig.MsPerHour;

    [Test]
    public static void NewCity_HasStartingValues()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(1, c.GetLevel(BuildingType.Keep));
        Assert.Equal(1, c.GetLevel(BuildingType.Sawmill));
        Assert.Equal(0, c.GetLevel(BuildingType.Warehouse));
        Assert.Equal(CityConfig.StartingResource, c.GetResource(ResourceType.Wood));
        Assert.True(c.Upgrade == null, "新城市不應有升級中的工程");
    }

    [Test]
    public static void Production_OneHour_AddsRatePerLevel()
    {
        var c = CityState.CreateNew(T0);
        CityLogic.Advance(c, T0 + Hour);
        Assert.Equal(CityConfig.StartingResource + 100, c.GetResource(ResourceType.Wood), "根材");
        Assert.Equal(CityConfig.StartingResource + 100, c.GetResource(ResourceType.Crystal), "響晶");
    }

    [Test]
    public static void Production_ManySmallSteps_LosesNothing()
    {
        // 每秒結算一次、共 3600 次，結果必須等於一次結算一小時（餘數機制）
        var c = CityState.CreateNew(T0);
        for (int i = 1; i <= 3600; i++) CityLogic.Advance(c, T0 + i * 1000L);
        Assert.Equal(CityConfig.StartingResource + 100, c.GetResource(ResourceType.Food), "菌糧");
    }

    [Test]
    public static void Production_TimeGoingBackwards_DoesNothing()
    {
        var c = CityState.CreateNew(T0);
        CityLogic.Advance(c, T0 - Hour);
        Assert.Equal(CityConfig.StartingResource, c.GetResource(ResourceType.Wood));
        Assert.Equal(T0, c.LastSettledAtMs);
    }

    [Test]
    public static void Production_IsCappedByWarehouse()
    {
        var c = CityState.CreateNew(T0);
        CityLogic.Advance(c, T0 + 1000 * Hour);
        Assert.Equal(CityConfig.Capacity(0), c.GetResource(ResourceType.Stone), "層岩上限");
    }

    [Test]
    public static void UpgradeCost_GrowsByOneAndHalf()
    {
        Assert.Equal(400L, CityConfig.UpgradeCost(BuildingType.Keep, 1, ResourceType.Wood));
        Assert.Equal(600L, CityConfig.UpgradeCost(BuildingType.Keep, 2, ResourceType.Wood));
        Assert.Equal(900L, CityConfig.UpgradeCost(BuildingType.Keep, 3, ResourceType.Wood));
        Assert.Equal(0L, CityConfig.UpgradeCost(BuildingType.Sawmill, 2, ResourceType.Wood), "掘根場不需根材");
    }

    [Test]
    public static void StartUpgrade_DeductsCostAndQueues()
    {
        var c = CityState.CreateNew(T0);
        var r = CityLogic.StartUpgrade(c, BuildingType.Keep, T0);
        Assert.Equal(UpgradeResult.Ok, r);
        Assert.Equal(CityConfig.StartingResource - 600, c.GetResource(ResourceType.Wood), "根材扣除");
        Assert.Equal(CityConfig.StartingResource - 150, c.GetResource(ResourceType.Crystal), "響晶扣除");
        var job = Assert.NotNull(c.Upgrade, "升級工程");
        Assert.Equal(2, job.TargetLevel);
        Assert.Equal(T0 + CityConfig.UpgradeDurationMs(BuildingType.Keep, 2), job.FinishAtMs);
        Assert.Equal(1, c.GetLevel(BuildingType.Keep), "完成前等級不變");
    }

    [Test]
    public static void StartUpgrade_QueueBusy()
    {
        var c = CityState.CreateNew(T0);
        CityLogic.StartUpgrade(c, BuildingType.Keep, T0);
        Assert.Equal(UpgradeResult.QueueBusy, CityLogic.StartUpgrade(c, BuildingType.Barracks, T0 + 1000));
    }

    [Test]
    public static void StartUpgrade_CannotExceedKeep()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(UpgradeResult.KeepLevelTooLow, CityLogic.StartUpgrade(c, BuildingType.Sawmill, T0));
        Assert.Equal(UpgradeResult.Ok, CityLogic.StartUpgrade(c, BuildingType.Barracks, T0), "兵營 0→1 可以");
    }

    [Test]
    public static void StartUpgrade_NotEnoughResources()
    {
        var c = CityState.CreateNew(T0);
        c.Resources[ResourceType.Crystal] = 10;
        Assert.Equal(UpgradeResult.NotEnoughResources, CityLogic.StartUpgrade(c, BuildingType.Keep, T0));
        Assert.Equal(10L, c.GetResource(ResourceType.Crystal), "失敗時不扣資源");
        Assert.True(c.Upgrade == null, "失敗時不排入隊列");
    }

    [Test]
    public static void StartUpgrade_MaxLevel()
    {
        var c = CityState.CreateNew(T0);
        c.Buildings[BuildingType.Keep] = CityConfig.MaxLevel;
        Assert.Equal(UpgradeResult.AlreadyMaxLevel, CityLogic.StartUpgrade(c, BuildingType.Keep, T0));
    }

    [Test]
    public static void StartUpgrade_UnknownBuilding()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(UpgradeResult.UnknownBuilding, CityLogic.StartUpgrade(c, (BuildingType)99, T0));
    }

    [Test]
    public static void Advance_CompletesUpgrade_AndSplitsProduction()
    {
        var c = CityState.CreateNew(T0);
        c.Buildings[BuildingType.Keep] = 2;
        Assert.Equal(UpgradeResult.Ok, CityLogic.StartUpgrade(c, BuildingType.Farm, T0));
        long foodAfterCost = c.GetResource(ResourceType.Food);
        long duration = CityConfig.UpgradeDurationMs(BuildingType.Farm, 2);

        Assert.True(CityLogic.Advance(c, T0 + duration - 1) == null, "完成前不應完成");
        var done = CityLogic.Advance(c, T0 + Hour);
        Assert.NotNull(done, "升級完成");
        Assert.Equal(2, c.GetLevel(BuildingType.Farm));

        // 完成前以 1 級產量、完成後以 2 級產量（整數＋餘數計算）
        long beforeUnits = 100 * duration;
        long afterUnits = 200 * (Hour - duration) + beforeUnits % Hour;
        long expected = foodAfterCost + beforeUnits / Hour + afterUnits / Hour;
        Assert.Equal(expected, c.GetResource(ResourceType.Food), "分段產量");
    }

    [Test]
    public static void Determinism_SameInputsSameResult()
    {
        CityState Run()
        {
            var c = CityState.CreateNew(T0);
            CityLogic.StartUpgrade(c, BuildingType.Keep, T0 + 5000);
            CityLogic.Advance(c, T0 + 3 * Hour + 123);
            CityLogic.StartUpgrade(c, BuildingType.Warehouse, T0 + 3 * Hour + 500);
            CityLogic.Advance(c, T0 + 9 * Hour);
            return c;
        }
        var a = Run();
        var b = Run();
        foreach (var r in CityConfig.AllResources)
            Assert.Equal(a.GetResource(r), b.GetResource(r), r.ToString());
        Assert.Equal(a.GetLevel(BuildingType.Warehouse), b.GetLevel(BuildingType.Warehouse));
    }
}
