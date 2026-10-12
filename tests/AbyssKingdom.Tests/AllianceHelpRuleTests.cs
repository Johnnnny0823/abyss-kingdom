using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

public static class AllianceHelpRuleTests
{
    private const long Now = 1_700_000_000_000;

    [Test]
    public static void UpgradeHelp_ShortensByOnePercentAndNeverTwice()
    {
        var city = CityState.CreateNew(Now);
        city.Upgrade = new UpgradeJob
        {
            Building = BuildingType.Keep, TargetLevel = 2, StartedAtMs = Now, FinishAtMs = Now + 600_000,
            Help = new AllianceHelpProgress { QueueId = 7, OriginalDurationMs = 600_000, MaxCount = 10 }
        };
        Assert.Equal(AllianceHelpResult.Ok,
            CityLogic.RequestAllianceHelp(city, QueueType.Building, "a", 4, Now, out long id));
        Assert.Equal(7L, id);
        Assert.Equal(AllianceHelpResult.SelfHelp,
            CityLogic.ApplyAllianceHelp(city, QueueType.Building, id, "owner", "owner", "a", 4, Now, out _));
        Assert.Equal(AllianceHelpResult.Ok,
            CityLogic.ApplyAllianceHelp(city, QueueType.Building, id, "owner", "helper", "a", 4, Now, out long reduced));
        Assert.Equal(6_000L, reduced);
        Assert.Equal(Now + 594_000, city.Upgrade.FinishAtMs);
        Assert.Equal(AllianceHelpResult.AlreadyHelped,
            CityLogic.ApplyAllianceHelp(city, QueueType.Building, id, "owner", "helper", "a", 4, Now, out _));
        Assert.Equal(AllianceHelpResult.WrongQueue,
            CityLogic.ApplyAllianceHelp(city, QueueType.Building, id + 1, "owner", "other", "a", 4, Now, out _));
    }

    [Test]
    public static void Help_CapsAtTwentyPercentAndCompletesAtExactBoundary()
    {
        var city = CityState.CreateNew(Now);
        city.Healing = new HealingJob
        {
            StartedAtMs = Now, FinishAtMs = Now + 10_000,
            Help = new AllianceHelpProgress { QueueId = 5, OriginalDurationMs = 10_000, MaxCount = 10 }
        };
        Assert.Equal(AllianceHelpResult.Ok,
            CityLogic.RequestAllianceHelp(city, QueueType.Healing, "a", 2, Now, out long id));
        for (int i = 0; i < 2; i++)
            Assert.Equal(AllianceHelpResult.Ok,
                CityLogic.ApplyAllianceHelp(city, QueueType.Healing, id, "owner", "h" + i, "a", 2, Now, out _));
        Assert.Equal(2_000L, city.Healing.Help.ReducedMs);
        Assert.Equal(AllianceHelpResult.HelpLimit,
            CityLogic.ApplyAllianceHelp(city, QueueType.Healing, id, "owner", "h3", "a", 2, Now, out _));

        city.Healing.FinishAtMs = Now + 500; // an item has already shortened the same queue
        Assert.Equal(AllianceHelpResult.HelpLimit,
            CityLogic.ApplyAllianceHelp(city, QueueType.Healing, id, "owner", "h4", "a", 2, Now, out _));
        city.Healing.Help.ReducedMs = 0;
        Assert.Equal(AllianceHelpResult.Ok,
            CityLogic.ApplyAllianceHelp(city, QueueType.Healing, id, "owner", "h4", "a", 2, Now, out long final));
        Assert.Equal(500L, final);
        Assert.Equal(Now, city.Healing.FinishAtMs);
        Assert.Equal(AllianceHelpResult.Finished,
            CityLogic.ApplyAllianceHelp(city, QueueType.Healing, id, "owner", "h5", "a", 2, Now, out _));
    }

    [Test]
    public static void NewQueues_GetStableIdsAndKeepOriginalDuration()
    {
        var city = CityState.CreateNew(Now);
        Assert.Equal(UpgradeResult.Ok, CityLogic.StartUpgrade(city, BuildingType.Keep, Now));
        long first = city.Upgrade.Help.QueueId;
        long original = city.Upgrade.Help.OriginalDurationMs;
        Assert.Equal(city.Upgrade.FinishAtMs - Now, original);
        Assert.Equal(AllianceHelpResult.Ok,
            CityLogic.RequestAllianceHelp(city, QueueType.Building, "a", 1, Now, out long requested));
        Assert.Equal(first, requested);
        city.Upgrade.FinishAtMs -= 1_000; // item speedup does not change the request ID or original duration
        Assert.Equal(first, city.Upgrade.Help.QueueId);
        Assert.Equal(original, city.Upgrade.Help.OriginalDurationMs);
        city.Upgrade = null;
        Assert.Equal(UpgradeResult.Ok, CityLogic.StartUpgrade(city, BuildingType.Keep, Now));
        Assert.Equal(first + 1, city.Upgrade.Help.QueueId);
    }
}
