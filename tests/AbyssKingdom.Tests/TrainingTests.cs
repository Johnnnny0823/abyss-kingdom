using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

public static class TrainingTests
{
    const long T0 = 1_700_000_000_000;

    static CityState CityWithBarracks(int level)
    {
        var c = CityState.CreateNew(T0);
        c.Buildings[BuildingType.Barracks] = level;
        return c;
    }

    [Test]
    public static void NewCity_HasNoTroops()
    {
        var c = CityState.CreateNew(T0);
        foreach (var t in CityConfig.AllTroops) Assert.Equal(0L, c.GetTroops(t), t.ToString());
        Assert.True(c.Training == null, "新城市不應有訓練中的部隊");
    }

    [Test]
    public static void Train_RequiresBarracks()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(TrainResult.BarracksRequired, CityLogic.StartTraining(c, TroopType.Shield, 10, T0));
    }

    [Test]
    public static void Train_RejectsBadCountAndUnknownTroop()
    {
        var c = CityWithBarracks(1);
        Assert.Equal(TrainResult.InvalidCount, CityLogic.StartTraining(c, TroopType.Shield, 0, T0));
        Assert.Equal(TrainResult.InvalidCount, CityLogic.StartTraining(c, TroopType.Shield, -5, T0));
        Assert.Equal(TrainResult.UnknownTroop, CityLogic.StartTraining(c, (TroopType)99, 1, T0));
    }

    [Test]
    public static void Train_BatchLimitScalesWithBarracks()
    {
        var c = CityWithBarracks(2);
        Assert.Equal(100L, CityConfig.MaxTrainBatch(2));
        Assert.Equal(TrainResult.ExceedsBatchLimit, CityLogic.StartTraining(c, TroopType.Shield, 101, T0));
        Assert.Equal(TrainResult.Ok, CityLogic.StartTraining(c, TroopType.Shield, 100, T0));
    }

    [Test]
    public static void Train_DeductsCostAndQueues()
    {
        var c = CityWithBarracks(1);
        Assert.Equal(TrainResult.Ok, CityLogic.StartTraining(c, TroopType.Rider, 10, T0));
        Assert.Equal(CityConfig.StartingResource - 300, c.GetResource(ResourceType.Food), "糧 30×10");
        Assert.Equal(CityConfig.StartingResource - 100, c.GetResource(ResourceType.Stone), "石 10×10");
        Assert.Equal(CityConfig.StartingResource - 50, c.GetResource(ResourceType.Crystal), "晶 5×10");
        Assert.Equal(CityConfig.StartingResource, c.GetResource(ResourceType.Wood), "懸索騎不花根材");
        var job = Assert.NotNull(c.Training, "訓練已排入");
        Assert.Equal(T0 + 30_000, job.FinishAtMs, "3 秒 × 10 名");
    }

    [Test]
    public static void Train_QueueBusy()
    {
        var c = CityWithBarracks(1);
        Assert.Equal(TrainResult.Ok, CityLogic.StartTraining(c, TroopType.Shield, 5, T0));
        Assert.Equal(TrainResult.QueueBusy, CityLogic.StartTraining(c, TroopType.Crossbow, 5, T0 + 1));
    }

    [Test]
    public static void Train_NotEnoughResources_ChangesNothing()
    {
        var c = CityWithBarracks(10);
        c.Resources[ResourceType.Food] = 19; // 沉錨衛每名要糧 20
        Assert.Equal(TrainResult.NotEnoughResources, CityLogic.StartTraining(c, TroopType.Shield, 1, T0));
        Assert.Equal(CityConfig.StartingResource, c.GetResource(ResourceType.Wood), "失敗不應扣資源");
        Assert.True(c.Training == null, "失敗不應排入隊列");
    }

    [Test]
    public static void Train_IndependentFromBuildingQueue()
    {
        var c = CityWithBarracks(1);
        Assert.Equal(UpgradeResult.Ok, CityLogic.StartUpgrade(c, BuildingType.Keep, T0));
        Assert.Equal(TrainResult.Ok, CityLogic.StartTraining(c, TroopType.Crossbow, 5, T0));
    }

    [Test]
    public static void Advance_CompletesTraining()
    {
        var c = CityWithBarracks(1);
        CityLogic.StartTraining(c, TroopType.Crossbow, 7, T0);
        long finish = T0 + 7 * CityConfig.TroopTrainMsPerUnit(TroopType.Crossbow);

        CityLogic.Advance(c, finish - 1, out var notYet);
        Assert.True(notYet == null, "完成前不應完成");
        Assert.Equal(0L, c.GetTroops(TroopType.Crossbow));

        CityLogic.Advance(c, finish, out var done);
        Assert.Equal(7L, Assert.NotNull(done, "訓練完成").Count);
        Assert.Equal(7L, c.GetTroops(TroopType.Crossbow), "部隊入營");
        Assert.True(c.Training == null, "隊列清空");

        // 再訓練一批會累加
        CityLogic.StartTraining(c, TroopType.Crossbow, 3, finish);
        CityLogic.Advance(c, finish + 3 * CityConfig.TroopTrainMsPerUnit(TroopType.Crossbow));
        Assert.Equal(10L, c.GetTroops(TroopType.Crossbow), "累加");
    }

    [Test]
    public static void OldSaveWithoutTroops_StillWorks()
    {
        // Supabase 裡在加入部隊功能前存的城市沒有 Troops 欄位
        var c = CityWithBarracks(1);
        c.Troops.Clear();
        Assert.Equal(0L, c.GetTroops(TroopType.Shield));
        Assert.Equal(TrainResult.Ok, CityLogic.StartTraining(c, TroopType.Shield, 1, T0));
        CityLogic.Advance(c, T0 + Hour());
        Assert.Equal(1L, c.GetTroops(TroopType.Shield));
    }

    static long Hour() => CityConfig.MsPerHour;
}
