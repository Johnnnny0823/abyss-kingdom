using System.Security.Cryptography;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class PlaytestFixesTests
{
    [Test]
    public static void ServerErrors_HaveChineseMessages_WithoutRawDiagnostics()
    {
        var types = typeof(CityState).Assembly.GetTypes().Where(t => t.IsEnum &&
            (t.Name.EndsWith("Result") || t.Name.EndsWith("Code")))
            .Append(typeof(AbyssKingdom.Server.Game.AllianceResult));
        foreach (var type in types)
            foreach (var name in Enum.GetNames(type).Where(n => n != "Ok"))
            {
                var text = AbyssKingdom.Client.UI.DisplayNames.Error("unrecognized", name);
                Assert.True(text != "操作未成功，請稍後再試" && !text.Contains(name), $"Missing translation: {type.Name}.{name}");
            }
        Assert.True(!AbyssKingdom.Client.UI.DisplayNames.Error("map_rejected", "future_private_detail").Contains("future"), "No raw future diagnostics");
        Assert.True(AbyssKingdom.Client.UI.DisplayNames.Error("map_rejected", "UnknownMarch").Contains("行軍"), "Unknown march has a specific explanation");
    }

    [Test]
    public static void QuestPolicy_RecoversWoundedThenReplenishesBeforeCamp()
    {
        var c = CityState.CreateNew(123);
        c.QuestIndex = 8; c.Buildings[BuildingType.Hospital] = 1;
        c.Buildings[BuildingType.Barracks] = 1; c.Wounded[TroopType.Shield] = 35;
        var heal = AbyssKingdom.Playtesting.AiPlaytestPolicy.Next(c, "quest", 1, 123);
        Assert.Equal(AbyssKingdom.Shared.Protocol.MessageTypes.CityHeal, heal.Type);
        c.Wounded.Clear(); c.Healing = new HealingJob { FinishAtMs = 600_123 };
        Assert.True(AbyssKingdom.Playtesting.AiPlaytestPolicy.Next(c, "quest", 2, 123) == null,
            "Wait for healing instead of training duplicates");
        c.Healing = null; c.Troops[TroopType.Shield] = 35;
        var train = AbyssKingdom.Playtesting.AiPlaytestPolicy.Next(c, "quest", 2, 123);
        Assert.Equal(AbyssKingdom.Shared.Protocol.MessageTypes.CityTrain, train.Type);
        c.Surface.Marches.Add(new March());
        Assert.True(AbyssKingdom.Playtesting.AiPlaytestPolicy.Next(c, "quest", 3, 123) == null, "Wait for deployed troops instead of training duplicates");
    }

    [Test]
    public static void TutorialArmy_WinsFirstCamp_WithLowDeaths_For200ServerSeeds()
    {
        long deaths = 0, wounded = 0, maximumDead = 0;
        var city = CityState.CreateNew(1_700_000_000_000);
        city.Buildings[BuildingType.Hospital] = 1;
        var army = MarchLogic.Squads(city, new March { Hero = HeroId.Hold,
            Troops = { [TroopType.Shield] = 50 } }).Select(s => s.squad).ToList();
        // Sample every Lv.1 enemy rotation, including any nearest camp assigned at spawn.
        for (int i = 0; i < 200; i++)
        {
            ulong seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); // WorldService.NextSeed
            var enemy = CampConfig.Lineup(1, i % 3).Select(ExpeditionLogic.EnemySquadInput).ToList();
            var report = BattleEngine.Run(army, enemy, seed);
            Assert.Equal(BattleOutcome.AttackerWins, report.Outcome, $"seed={seed}, rotation={i % 3}");
            var fresh = CityState.CreateNew(1_700_000_000_000);
            fresh.Buildings[BuildingType.Hospital] = 1;
            var losses = new Dictionary<TroopType, long> { [TroopType.Shield] = 50 - report.Attackers[0].TroopsLeft };
            var result = CityLogic.TakeCasualties(fresh, losses);
            long dead = result.dead.GetValueOrDefault(TroopType.Shield);
            deaths += dead; wounded += result.wounded.GetValueOrDefault(TroopType.Shield);
            maximumDead = Math.Max(maximumDead, dead);
        }
        Assert.True(deaths <= 200 * 10, $"total dead={deaths}");
        Console.WriteLine($"[FirstCamp] 200/200 wins; dead total={deaths}, average={deaths / 200.0:F2}, max={maximumDead}; wounded total={wounded}");
    }

    [Test]
    public static void HigherCamps_RetainExpeditionStats_AndHospitalPrecedesCamp()
    {
        foreach (int level in new[] { 2, 3 })
            for (int tile = 0; tile < 3; tile++)
            {
                var expected = ExpeditionConfig.Lineup(CampConfig.StageOf(level), level, tile, 100);
                var actual = CampConfig.Lineup(level, tile);
                for (int i = 0; i < actual.Count; i++)
                {
                    Assert.Equal(expected[i].Count, actual[i].Count);
                    Assert.Equal(expected[i].Attack, actual[i].Attack);
                    Assert.Equal(expected[i].Defense, actual[i].Defense);
                    Assert.Equal(expected[i].Hp, actual[i].Hp);
                }
            }
        Assert.Equal(BuildingType.Hospital, QuestConfig.Chapter1[7].Building);
        Assert.Equal(QuestGoal.BuildingLevel, QuestConfig.Chapter1[7].Goal);
        Assert.Equal(QuestGoal.CampsWon, QuestConfig.Chapter1[8].Goal);
        Assert.Equal(1000L, QuestConfig.Chapter1[7].Resources[ResourceType.Food]);
        Assert.Equal(5L, QuestConfig.Chapter1[8].Items[ItemType.HeroExpSmall]);
    }
}
