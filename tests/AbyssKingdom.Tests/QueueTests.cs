using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

/// <summary>取消、免費加速、加速道具。</summary>
public static class QueueTests
{
    const long T0 = 1_700_000_000_000;
    const long Min = ItemConfig.MsPerMinute;
    const long Rich = 100_000;

    static CityState City()
    {
        var c = CityState.CreateNew(T0);
        c.Buildings[BuildingType.Keep] = 5;
        c.Buildings[BuildingType.Barracks] = 5;
        foreach (var r in CityConfig.AllResources) c.Resources[r] = Rich;
        return c;
    }

    // ───── B. 取消 ─────

    [Test]
    public static void CancelUpgrade_RefundsHalf_LevelUnchanged()
    {
        var c = City();
        CityLogic.StartUpgrade(c, BuildingType.Keep, T0); // Keep 6：木 400×1.5^5
        long cost = CityConfig.UpgradeCost(BuildingType.Keep, 6, ResourceType.Wood);
        long before = Rich - cost;
        Assert.Equal(before, c.GetResource(ResourceType.Wood));

        Assert.Equal(CancelResult.Ok, CityLogic.Cancel(c, QueueType.Building, T0));
        Assert.Equal(before + cost / 2, c.GetResource(ResourceType.Wood), "退一半");
        Assert.Equal(5, c.GetLevel(BuildingType.Keep), "等級不變");
        Assert.True(c.Upgrade == null, "隊列清空");
    }

    [Test]
    public static void Cancel_EmptyQueue()
    {
        var c = City();
        Assert.Equal(CancelResult.NothingToCancel, CityLogic.Cancel(c, QueueType.Building, T0));
        Assert.Equal(CancelResult.NothingToCancel, CityLogic.Cancel(c, QueueType.Training, T0));
        Assert.Equal(CancelResult.UnknownQueue, CityLogic.Cancel(c, (QueueType)9, T0));
    }

    [Test]
    public static void CancelTraining_KeepsFinishedUnits_RefundsHalfOfRest()
    {
        var c = City();
        CityLogic.StartTraining(c, TroopType.Shield, 10, T0); // 每名 2 秒、糧 20
        long foodAfterStart = c.GetResource(ResourceType.Food);

        // 過了 5 秒：第 1、2 名完成（4 秒），第 3 名還在練 → 完成 2 名，退 8 名的一半
        Assert.Equal(CancelResult.Ok, CityLogic.Cancel(c, QueueType.Training, T0 + 5000));
        Assert.Equal(2L, c.GetTroops(TroopType.Shield), "已完成的入營");
        Assert.Equal(foodAfterStart + 8 * 20 / 2, c.GetResource(ResourceType.Food), "未完成的退一半");
        Assert.True(c.Training == null, "隊列清空");
    }

    [Test]
    public static void TrainedSoFar_IsCorrectAfterSpeedup()
    {
        var job = new TrainingJob { Troop = TroopType.Shield, Count = 10, StartedAtMs = T0, FinishAtMs = T0 + 20_000 };
        Assert.Equal(0L, CityLogic.TrainedSoFar(job, T0));
        Assert.Equal(0L, CityLogic.TrainedSoFar(job, T0 + 1999));
        Assert.Equal(1L, CityLogic.TrainedSoFar(job, T0 + 2000));
        job.FinishAtMs -= 10_000; // 加速 10 秒 → 剩 5 名 → 已完成 5 名
        Assert.Equal(5L, CityLogic.TrainedSoFar(job, T0));
    }

    // ───── C. 免費加速 ─────

    [Test]
    public static void FreeSpeedup_OnlyWithinFiveMinutes()
    {
        var c = City();
        CityLogic.StartUpgrade(c, BuildingType.Keep, T0);
        long finish = c.Upgrade.FinishAtMs;
        Assert.True(finish - T0 > CityConfig.FreeSpeedupMs, "主堡 6 級工期要超過 5 分鐘，測試才有意義");

        Assert.Equal(SpeedupResult.NotFreeYet, CityLogic.SpeedupFree(c, QueueType.Building, T0));
        long almost = finish - CityConfig.FreeSpeedupMs;
        Assert.Equal(SpeedupResult.Ok, CityLogic.SpeedupFree(c, QueueType.Building, almost));
        Assert.Equal(almost, c.Upgrade.FinishAtMs, "完成時間設為現在");

        Assert.NotNull(CityLogic.Advance(c, almost), "下一次推進就完成");
        Assert.Equal(6, c.GetLevel(BuildingType.Keep));
    }

    [Test]
    public static void FreeSpeedup_EmptyQueue()
    {
        Assert.Equal(SpeedupResult.NothingInQueue, CityLogic.SpeedupFree(City(), QueueType.Training, T0));
    }

    // ───── C. 加速道具 ─────

    [Test]
    public static void NewCity_HasStarterItems()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(10L, c.GetItemCount(ItemType.SpeedupGeneral1m));
        Assert.Equal(3L, c.GetItemCount(ItemType.SpeedupBuilding30m));
        Assert.Equal(0L, c.GetItemCount(ItemType.SpeedupMagic60m));
    }

    [Test]
    public static void OldSave_GetsStarterItemsExactlyOnce()
    {
        var c = CityState.CreateNew(T0);
        c.Items.Clear();
        c.StarterItemsGranted = false; // 模擬加入道具前的舊存檔
        Assert.True(CityLogic.GrantStarterItemsOnce(c), "第一次補發");
        Assert.Equal(10L, c.GetItemCount(ItemType.SpeedupGeneral1m));
        c.Items[ItemType.SpeedupGeneral1m] = 0; // 用光了
        Assert.True(!CityLogic.GrantStarterItemsOnce(c), "不會重複補發");
        Assert.Equal(0L, c.GetItemCount(ItemType.SpeedupGeneral1m));
    }

    [Test]
    public static void ItemIds_EncodeCategoryAndMinutes()
    {
        Assert.True(ItemConfig.TryGetSpeedup(ItemType.SpeedupBuilding30m, out var cat, out var ms), "是加速道具");
        Assert.Equal(ItemCategory.Building, cat);
        Assert.Equal(30 * Min, ms);
        Assert.True(ItemConfig.TryGetSpeedup(ItemType.SpeedupTraining60m, out cat, out ms), "訓練 60 分");
        Assert.Equal(ItemCategory.Training, cat);
        Assert.Equal(60 * Min, ms);
        // 每個加速道具都要能解析，且分鐘數是 1/3/5/30/60
        foreach (var item in ItemConfig.AllItems.Where(i => (int)i < 1000))
        {
            Assert.True(ItemConfig.TryGetSpeedup(item, out _, out var m), item.ToString());
            Assert.True(m == Min || m == 3 * Min || m == 5 * Min || m == 30 * Min || m == 60 * Min, item + " 分鐘數");
        }
    }

    [Test]
    public static void UseItem_ReducesTimeAndConsumes()
    {
        var c = City();
        CityLogic.StartUpgrade(c, BuildingType.Keep, T0);
        long finish = c.Upgrade.FinishAtMs;

        Assert.Equal(SpeedupResult.Ok, CityLogic.UseSpeedupItem(c, QueueType.Building, ItemType.SpeedupGeneral1m, 3, T0));
        Assert.Equal(finish - 3 * Min, c.Upgrade.FinishAtMs, "減 3 分鐘");
        Assert.Equal(7L, c.GetItemCount(ItemType.SpeedupGeneral1m), "消耗 3 個");
    }

    [Test]
    public static void UseItem_NeverGoesBeforeNow()
    {
        var c = City();
        CityLogic.StartTraining(c, TroopType.Shield, 5, T0); // 10 秒
        Assert.Equal(SpeedupResult.Ok, CityLogic.UseSpeedupItem(c, QueueType.Training, ItemType.SpeedupTraining30m, 1, T0));
        Assert.Equal(T0, c.Training.FinishAtMs);
        CityLogic.Advance(c, T0, out var done);
        Assert.Equal(5L, Assert.NotNull(done, "立即完成").Count);
    }

    [Test]
    public static void UseItem_Rejections()
    {
        var c = City();
        Assert.Equal(SpeedupResult.NothingInQueue, CityLogic.UseSpeedupItem(c, QueueType.Building, ItemType.SpeedupGeneral1m, 1, T0));
        CityLogic.StartUpgrade(c, BuildingType.Keep, T0);
        Assert.Equal(SpeedupResult.ItemNotApplicable, CityLogic.UseSpeedupItem(c, QueueType.Building, ItemType.SpeedupTraining30m, 1, T0), "訓練道具不能用在建築");
        Assert.Equal(SpeedupResult.ItemNotApplicable, CityLogic.UseSpeedupItem(c, QueueType.Building, ItemType.SpeedupResearch5m, 1, T0), "科研道具目前無處可用");
        Assert.Equal(SpeedupResult.NotEnoughItems, CityLogic.UseSpeedupItem(c, QueueType.Building, ItemType.SpeedupGeneral60m, 2, T0), "只有 1 個");
        Assert.Equal(SpeedupResult.InvalidCount, CityLogic.UseSpeedupItem(c, QueueType.Building, ItemType.SpeedupGeneral1m, 0, T0));
        Assert.Equal(SpeedupResult.UnknownItem, CityLogic.UseSpeedupItem(c, QueueType.Building, (ItemType)999, 1, T0));
        Assert.Equal(1L, c.GetItemCount(ItemType.SpeedupGeneral60m), "被拒絕不應扣道具");
    }
}
