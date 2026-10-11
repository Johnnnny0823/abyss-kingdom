using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class QuestTests
{
    const long T0 = 1_700_000_000_000;

    [Test]
    public static void NewCity_StartsAtFirstQuest_UpgradeKeep()
    {
        var c = CityState.CreateNew(T0);
        var q = QuestLogic.Current(c);
        Assert.NotNull(q, "有第一個任務");
        Assert.Equal(QuestGoal.BuildingLevel, q!.Goal);
        Assert.Equal(BuildingType.Keep, q.Building, "先升主堡");
        Assert.Equal(1L, QuestLogic.Progress(c, q), "主堡現在 1 級");
        Assert.Equal(QuestResult.NotDone, QuestLogic.Claim(c), "還沒完成不能領");
        Assert.Equal(0, c.QuestIndex, "任務沒有前進");
    }

    [Test]
    public static void Claim_GivesRewards_AndAdvances()
    {
        var c = CityState.CreateNew(T0);
        var q = QuestLogic.Current(c)!;
        c.Buildings[BuildingType.Keep] = 2;
        long wood = c.GetResource(ResourceType.Wood);
        long speedups = c.GetItemCount(ItemType.SpeedupBuilding5m);
        Assert.Equal(QuestResult.Ok, QuestLogic.Claim(c));
        Assert.Equal(wood + q.Resources[ResourceType.Wood], c.GetResource(ResourceType.Wood), "拿到資源");
        Assert.Equal(speedups + q.Items[ItemType.SpeedupBuilding5m], c.GetItemCount(ItemType.SpeedupBuilding5m), "拿到道具");
        Assert.Equal(1, c.QuestIndex, "換下一個任務");
        Assert.Equal(BuildingType.Barracks, QuestLogic.Current(c)!.Building, "第二個是蓋兵營");
    }

    [Test]
    public static void Rewards_IgnoreWarehouseCap_LikeExpeditionRewards()
    {
        var c = CityState.CreateNew(T0);
        c.Buildings[BuildingType.Keep] = 2;
        foreach (var r in CityConfig.AllResources) c.Resources[r] = CityConfig.Capacity(0);
        QuestLogic.Claim(c);
        Assert.True(c.GetResource(ResourceType.Wood) > CityConfig.Capacity(0), "獎勵直接入庫");
    }

    [Test]
    public static void AllQuestsClaimed_NoQuest()
    {
        var c = CityState.CreateNew(T0);
        c.QuestIndex = QuestConfig.Chapter1.Length;
        Assert.True(QuestLogic.Current(c) == null, "這一章做完了");
        Assert.Equal(QuestResult.NoQuest, QuestLogic.Claim(c));
    }

    [Test]
    public static void Quests_HaveUniqueIds_AndRewards()
    {
        var ids = QuestConfig.Chapter1.Select(q => q.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count(), "編號不重複");
        Assert.True(QuestConfig.Chapter1.All(q => q.Target > 0 && (q.Resources.Count > 0 || q.Items.Count > 0)), "每個任務都有目標與獎勵");
    }

    [Test]
    public static void TrainingCompletion_CountsTowardTroopsTrained()
    {
        var c = CityState.CreateNew(T0);
        c.Buildings[BuildingType.Barracks] = 1;
        Assert.Equal(TrainResult.Ok, CityLogic.StartTraining(c, TroopType.Shield, 30, T0));
        Assert.Equal(0L, c.Stats.TroopsTrained, "訓練完成前不算");
        CityLogic.Advance(c, T0 + CityConfig.TroopTrainMsPerUnit(TroopType.Shield) * 30);
        Assert.Equal(30L, c.Stats.TroopsTrained, "完成才累計");
    }

    [Test]
    public static void CampWin_And_Gather_AreCounted()
    {
        // 打營地：照 CampTests 的設定，滿編主將打贏 Lv.1
        var camp = SurfaceLogic.Features().First(f => f.Type == FeatureType.MonsterCamp && f.Level == 1
            && SurfaceLogic.Sites().Any(s => s.z == SurfaceLogic.Zone(f.X, f.Y) && SurfaceLogic.Dist2(s.x, s.y, f.X, f.Y) < 400));
        var site = SurfaceLogic.Sites().First(s => s.z == SurfaceLogic.Zone(camp.X, camp.Y) && SurfaceLogic.Dist2(s.x, s.y, camp.X, camp.Y) < 400);
        var c = CityState.CreateNew(T0);
        c.Surface.X = site.x; c.Surface.Y = site.y;
        c.Troops[TroopType.Shield] = 500;
        var w = new SimpleFieldRegistry();
        MarchLogic.Start(c, HeroId.Hold, new() { [TroopType.Shield] = 250 }, camp.X, camp.Y, T0, w, "me");
        var m = c.Surface.Marches[0];
        Assert.True(MarchLogic.Advance(c, m.ArriveAt, w, "me").Single().BattleWon, "打贏");
        Assert.Equal(1L, c.Stats.CampsWon, "擊敗營地累計 1");

        // 採集：到資源田、採滿自動回城
        var field = SurfaceLogic.Features().First(f => f.Type == FeatureType.ResourceField && SurfaceLogic.Zone(f.X, f.Y) == SurfaceLogic.Zone(site.x, site.y));
        MarchLogic.Start(c, HeroId.Vesper, new() { [TroopType.Shield] = 10 }, field.X, field.Y, T0, w, "me");
        var g = c.Surface.Marches.Single(x => x.Hero == HeroId.Vesper);
        MarchLogic.Advance(c, g.ArriveAt, w, "me");
        Assert.True(g.Gathering, "開始採集");
        Assert.Equal(0L, c.Stats.GatherTrips, "還沒採完不算");
        MarchLogic.Advance(c, g.GatherEndMs, w, "me");
        Assert.Equal(1L, c.Stats.GatherTrips, "採滿算一次");
    }

    [Test]
    public static void RecallWhileGathering_CountsOnlyIfSomethingWasGathered()
    {
        var site = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer);
        var field = SurfaceLogic.Features().OrderBy(f => SurfaceLogic.Dist2(f.X, f.Y, site.x, site.y)).First(f => f.Type == FeatureType.ResourceField);
        var c = CityState.CreateNew(T0);
        c.Surface.X = site.x; c.Surface.Y = site.y;
        c.Troops[TroopType.Shield] = 100;
        var w = new SimpleFieldRegistry();
        MarchLogic.Start(c, HeroId.Hold, new() { [TroopType.Shield] = 50 }, field.X, field.Y, T0, w, "me");
        var m = c.Surface.Marches[0];
        MarchLogic.Advance(c, m.ArriveAt, w, "me");
        MarchLogic.Recall(c, m.Id, m.ArriveAt, w, "me");
        Assert.Equal(0L, c.Stats.GatherTrips, "一抵達就召回、什麼都沒採到：不算");

        MarchLogic.Start(c, HeroId.Vesper, new() { [TroopType.Shield] = 50 }, field.X, field.Y, T0, w, "me");
        var m2 = c.Surface.Marches.Single(x => x.Hero == HeroId.Vesper);
        MarchLogic.Advance(c, m2.ArriveAt, w, "me");
        MarchLogic.Recall(c, m2.Id, m2.ArriveAt + 60_000, w, "me");
        Assert.Equal(1L, c.Stats.GatherTrips, "採了一分鐘才召回：算一次");
    }
}
