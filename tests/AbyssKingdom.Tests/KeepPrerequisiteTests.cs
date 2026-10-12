using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

public static class KeepPrerequisiteTests
{
    const long T0 = 1_700_000_000_000;

    [Test]
    public static void EveryLevel_ExactRequirementsPass_EachMissingBuildingRejectsWithoutMutation()
    {
        Assert.Equal(6, (int)UpgradeResult.KeepPrerequisiteMissing);
        for (int target = 2; target <= 10; target++)
        {
            var c = CityState.CreateNew(T0);
            c.Buildings[BuildingType.Keep] = target - 1;
            foreach (var r in CityConfig.AllResources) c.Resources[r] = 100_000;
            var requirements = CityConfig.KeepPrerequisites(target);
            Assert.Equal(target == 2 ? 0 : 2, requirements.Count);
            foreach (var (building, level) in requirements)
            {
                Assert.True(level < target, "No circular Keep dependency");
                c.Buildings[building] = level;
            }
            Assert.Equal(UpgradeResult.Ok, CityLogic.CanUpgrade(c, BuildingType.Keep));
            foreach (var (building, level) in requirements)
            {
                c.Buildings[building] = level - 1;
                var resources = CityConfig.AllResources.ToDictionary(r => r, c.GetResource);
                Assert.Equal(UpgradeResult.KeepPrerequisiteMissing, CityLogic.StartUpgrade(c, BuildingType.Keep, T0));
                Assert.True(c.Upgrade == null, "Rejected Keep leaves queue empty");
                Assert.Equal(target - 1, c.GetLevel(BuildingType.Keep));
                foreach (var r in CityConfig.AllResources) Assert.Equal(resources[r], c.GetResource(r));
                c.Buildings[building] = level;
            }
            Assert.Equal(UpgradeResult.Ok, CityLogic.StartUpgrade(c, BuildingType.Keep, T0));
            Assert.Equal(target, Assert.NotNull(c.Upgrade, "Upgrade job").TargetLevel);
        }
    }

    [Test]
    public static void NewCity_CanLegallyReachKeepTen_AndExistingQueuedUpgradeStillCompletes()
    {
        var c = CityState.CreateNew(T0); long now = T0;
        void Upgrade(BuildingType building)
        {
            foreach (var r in CityConfig.AllResources) c.Resources[r] = 100_000;
            Assert.Equal(UpgradeResult.Ok, CityLogic.StartUpgrade(c, building, now));
            now = c.Upgrade!.FinishAtMs; CityLogic.Advance(c, now);
        }
        for (int target = 2; target <= 10; target++)
        {
            foreach (var (building, level) in CityConfig.KeepPrerequisites(target))
                while (c.GetLevel(building) < level) Upgrade(building);
            Upgrade(BuildingType.Keep);
            Assert.Equal(target, c.GetLevel(BuildingType.Keep));
        }
        var old = CityState.CreateNew(T0);
        old.Buildings[BuildingType.Keep] = 8;
        old.Upgrade = new UpgradeJob { Building = BuildingType.Keep, TargetLevel = 9, StartedAtMs = T0, FinishAtMs = T0 + 1000 };
        CityLogic.Advance(old, T0 + 1000);
        Assert.Equal(9, old.GetLevel(BuildingType.Keep));
        Assert.Equal(UpgradeResult.Ok, CityLogic.CanUpgrade(old, BuildingType.Sawmill));
    }

    [Test]
    public static void ChapterOne_IntroducesPrerequisitesBeforeKeepTargets()
    {
        var established = CityConfig.AllBuildings.ToDictionary(b => b, CityConfig.StartingLevel);
        foreach (var quest in QuestConfig.Chapter1)
        {
            if (quest.Goal == QuestGoal.BuildingLevel)
            {
                if (quest.Building == BuildingType.Keep)
                    foreach (var (building, level) in CityConfig.KeepPrerequisites((int)quest.Target))
                        Assert.True(established[building] >= level, "Quest introduces prerequisite before Keep");
                established[quest.Building] = Math.Max(established[quest.Building], (int)quest.Target);
            }
            if (quest.Goal == QuestGoal.ProducersLevel)
                foreach (var b in CityConfig.AllBuildings.Where(b => CityConfig.TryGetProducedResource(b, out _)))
                    established[b] = (int)quest.Target;
        }
    }
}
