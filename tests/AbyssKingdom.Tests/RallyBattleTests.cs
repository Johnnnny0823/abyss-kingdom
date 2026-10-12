using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.Heroes;
using System.Text.Json;

namespace AbyssKingdom.Tests;

public static class RallyBattleTests
{
    [Test]
    public static void Rewards_ConservePoolAndBreakTiesByStableIdWithoutOverflow()
    {
        var shares = RallyBattle.Split(5, new Dictionary<string, long> { ["c"] = long.MaxValue, ["a"] = long.MaxValue, ["b"] = long.MaxValue });
        Assert.Equal(2L, shares["a"]); Assert.Equal(2L, shares["b"]); Assert.Equal(1L, shares["c"]);
        Assert.Equal(5L, shares.Values.Sum());
        Assert.Throws<ArgumentException>(() => RallyBattle.Split(1, new Dictionary<string, long> { ["a"] = -1 }));
    }
    [Test]
    public static void FiveMixedOwners_UseFifteenSquadsAndOneRewardPool()
    {
        var participants = Enumerable.Range(0, 5).Select(i => new CampParticipantInput { OwnerId = "p" + i, MarchId = i,
            Squads = CityConfig.AllTroops.Select((t, j) => new SquadInput { Leader = j == 0 ? "Hero" : "Captain",
                Troop = t, Count = 100, LeaderHp = j == 0 ? 100_000 : 1, LeaderAttack = j == 0 ? 10_000 : 0 }).ToList() }).ToList();
        var outcome = RallyBattle.Resolve(participants, 2, 1234, 42, true);
        Assert.True(outcome.Won, "五人混兵完成營地戰鬥");
        foreach (var reward in CampConfig.ResourceLoot(2))
            Assert.Equal(reward.Value, outcome.Participants.Sum(p => p.Resources.GetValueOrDefault(reward.Key)));
        foreach (var reward in CampConfig.ItemLoot(2, 1234))
            Assert.Equal(reward.Value, outcome.Participants.Sum(p => p.Items.GetValueOrDefault(reward.Key)));
        Assert.Equal(15, outcome.Participants.Sum(p => p.Report.Attackers.Count));
        Assert.Equal(5, participants.Sum(p => p.Squads.Count(s => s.Leader == "Hero")), "五位主將，不複製給十五隊");
        var gone = RallyBattle.Resolve(participants, 2, 1234, 42, true, true);
        Assert.True(gone.CampGone && gone.Participants.All(p => p.Losses.Values.All(n => n == 0) && p.Resources.Count == 0), "營地已消失不扣兵或發獎勵");
    }
    [Test]
    public static void MixedTie_IsIndependentOfJsonDictionaryOrder()
    {
        var city = CityState.CreateNew(1_700_000_000_000);
        var a = new March { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 100, [TroopType.Crossbow] = 100, [TroopType.Rider] = 100 } };
        var b = new March { Hero = HeroId.Hold, Troops = new() { [TroopType.Rider] = 100, [TroopType.Crossbow] = 100, [TroopType.Shield] = 100 } };
        Assert.Equal(JsonSerializer.Serialize(MarchLogic.Squads(city, a).Select(s => s.squad)),
            JsonSerializer.Serialize(MarchLogic.Squads(city, b).Select(s => s.squad)));
    }
    [Test]
    public static void FiveMixedVersusFiveSingle_MeasureBalanceWithoutExtraHeroStats()
    {
        foreach (int level in new[] { 2, 3 })
            foreach (bool mixed in new[] { false, true })
            {
                var inputs = Enumerable.Range(0, 5).Select(i =>
                {
                    var city = CityState.CreateNew(1_700_000_000_000);
                    var m = new March { Hero = HeroId.Hold, Troops = mixed
                        ? CityConfig.AllTroops.ToDictionary(t => t, _ => 100L)
                        : new Dictionary<TroopType, long> { [TroopType.Shield] = 300 } };
                    return new CampParticipantInput { OwnerId = "p" + i, MarchId = i, Hero = m.Hero,
                        Squads = MarchLogic.Squads(city, m).Select(s => s.squad).ToList() };
                }).ToList();
                Assert.Equal(5, inputs.SelectMany(p => p.Squads).Count(s => s.Leader != "Captain"));
                var outcome = RallyBattle.Resolve(inputs, level, 1234, 42, true);
                long losses = outcome.Participants.Sum(p => p.Losses.Values.Sum());
                Assert.True(losses >= 0 && losses <= 1_500, "原擁有者的實兵損失有界");
                Console.WriteLine("RALLY_BALANCE " + JsonSerializer.Serialize(new { level, mixed, outcome.Won,
                    rounds = outcome.Participants[0].Report.Rounds, squads = inputs.Sum(p => p.Squads.Count), heroInputs = 5,
                    losses, contribution = outcome.Participants.Sum(p => p.Contribution) }));
            }
    }
}
