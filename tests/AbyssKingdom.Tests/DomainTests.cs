using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Tests;

public static class DomainTests
{
    const long T0 = 1_700_000_000_000;

    /// <summary>主將在 lv 級、已通達 mastered 個境時，挑戰 domain 試煉的 20 種子勝場。</summary>
    static int TrialWins(HeroId hero, int lv, Domain domain, int mastered)
    {
        int wins = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var c = CityState.CreateNew(T0);
            var h = c.Heroes[hero];
            h.Level = lv;
            foreach (var d in DomainConfig.All.Where(d => d != domain).Take(mastered)) h.Mastered.Add(d);
            h.DomainXp[domain] = DomainConfig.XpToMaster(mastered);
            DomainLogic.Trial(c, hero, domain, seed, out var r);
            if (r.Success) wins++;
        }
        return wins;
    }

    // ───── 試煉難度曲線 ─────

    [Test]
    public static void Curve_FirstTrialsAroundLevel5()
    {
        foreach (var hero in new[] { HeroId.Hold, HeroId.Vesper })
        {
            Assert.Equal(0, TrialWins(hero, 1, Domain.Technique, 0), hero + " Lv1 過不了巧境試煉");
            Assert.Equal(20, TrialWins(hero, 8, Domain.Technique, 0), hero + " Lv8 穩過第一次巧境試煉");
            Assert.Equal(20, TrialWins(hero, 8, Domain.Speed, 0), hero + " Lv8 穩過第一次迅境試煉");
        }
    }

    [Test]
    public static void Curve_LaterTrialsNeedMoreLevels()
    {
        Assert.Equal(0, TrialWins(HeroId.Hold, 14, Domain.Speed, 3), "第四境的試煉在 Lv14 過不了");
        Assert.Equal(20, TrialWins(HeroId.Hold, 30, Domain.Speed, 3), "滿級可以過");
    }

    [Test]
    public static void Curve_HeroesHaveNaturalStrengths()
    {
        Assert.True(TrialWins(HeroId.Vesper, 1, Domain.Power, 0) > TrialWins(HeroId.Hold, 1, Domain.Power, 0), "射手的震境比較容易");
        Assert.True(TrialWins(HeroId.Hold, 1, Domain.Durability, 0) > TrialWins(HeroId.Vesper, 1, Domain.Durability, 0), "盾衛的磐境比較容易");
    }

    // ───── 規則 ─────

    [Test]
    public static void Tier_ByMasteredCount()
    {
        Assert.Equal(DomainTier.Unbound, DomainConfig.Tier(0));
        Assert.Equal(DomainTier.Roped, DomainConfig.Tier(1));
        Assert.Equal(DomainTier.Delver, DomainConfig.Tier(2));
        Assert.Equal(DomainTier.Deepwalker, DomainConfig.Tier(3));
        Assert.Equal(DomainTier.AbyssReacher, DomainConfig.Tier(4));
    }

    [Test]
    public static void Xp_CapsAtThreshold_AndStopsAfterMastery()
    {
        var h = new HeroState { Id = HeroId.Hold };
        DomainLogic.AddXp(h, Domain.Power, 5000);
        Assert.Equal(DomainConfig.XpToMaster(0), DomainLogic.Xp(h, Domain.Power), "最多累積到門檻");
        Assert.True(DomainLogic.ReadyForTrial(h, Domain.Power), "滿了可以挑戰");
        h.Mastered.Add(Domain.Power);
        DomainLogic.AddXp(h, Domain.Power, 100);
        Assert.True(!DomainLogic.ReadyForTrial(h, Domain.Power), "通達後不再需要試煉");
        Assert.Equal(2000L, DomainConfig.XpToMaster(h.Mastered.Count), "下一境門檻變高");
    }

    [Test]
    public static void Trial_RequiresFullXp_AndRecordsOrder()
    {
        var c = CityState.CreateNew(T0);
        var h = c.Heroes[HeroId.Hold];
        Assert.Equal(DomainResult.NotReady, DomainLogic.Trial(c, HeroId.Hold, Domain.Durability, 1, out _));
        h.Level = 20;
        h.DomainXp[Domain.Durability] = DomainConfig.XpToMaster(0);
        Assert.Equal(DomainResult.Ok, DomainLogic.Trial(c, HeroId.Hold, Domain.Durability, 1, out var r));
        Assert.True(r.Success, "Lv20 霍德過磐境試煉");
        Assert.Equal(Domain.Durability, h.Mastered[0], "記錄通達順序");
        Assert.Equal(DomainResult.AlreadyMastered, DomainLogic.Trial(c, HeroId.Hold, Domain.Durability, 1, out _));
    }

    [Test]
    public static void FailedTrial_KeepsXp()
    {
        var c = CityState.CreateNew(T0);
        var h = c.Heroes[HeroId.Hold];
        h.DomainXp[Domain.Technique] = DomainConfig.XpToMaster(0);
        DomainLogic.Trial(c, HeroId.Hold, Domain.Technique, 1, out var r);
        Assert.True(!r.Success, "Lv1 會失敗");
        Assert.True(DomainLogic.ReadyForTrial(h, Domain.Technique), "失敗不扣修練值，可以再挑戰");
    }

    [Test]
    public static void Mastery_AppliesBattleBonuses()
    {
        var c = CityState.CreateNew(T0);
        var h = c.Heroes[HeroId.Karst];
        var before = DomainLogic.HeroSquad(c, HeroId.Karst);
        h.Mastered.AddRange(DomainConfig.All);
        var after = DomainLogic.HeroSquad(c, HeroId.Karst);
        Assert.Equal(before.AttackPercent * DomainConfig.PowerAttackPercent / 100, after.AttackPercent, "震境");
        Assert.Equal(DomainConfig.DurabilityDamageTakenPercent, after.DamageTakenPercent, "磐境");
        Assert.Equal(DomainConfig.SpeedBonus, after.SpeedBonus, "迅境");
        Assert.Equal(DomainConfig.TechniqueCounterBonus, after.CounterBonus, "巧境");
    }

    [Test]
    public static void Expedition_AwardsDomainXpByPerformance()
    {
        var c = CityState.CreateNew(T0);
        ExpeditionLogic.Fight(c, 1, new List<IList<HeroId>> { new[] { HeroId.Hold, HeroId.Vesper, HeroId.Karst } }, 3, T0, out var r);
        Assert.True(r.Win, "第 1 關會贏");
        long totalPower = c.Heroes.Values.Sum(h => DomainLogic.Xp(h, Domain.Power));
        Assert.True(totalPower > 0, "有人打出傷害 → 震境修練值增加");
        Assert.True(c.Heroes.Values.All(h => DomainLogic.Xp(h, Domain.Power) <= DomainConfig.MaxXpPerBattle), "單場有上限");
    }

    [Test]
    public static void Scroll_AddsXpToItsDomain()
    {
        var c = CityState.CreateNew(T0);
        c.Items[ItemType.DomainScrollSpeed] = 3;
        Assert.Equal(DomainResult.Ok, DomainLogic.UseScroll(c, HeroId.Vesper, ItemType.DomainScrollSpeed, 3));
        Assert.Equal(300L, DomainLogic.Xp(c.Heroes[HeroId.Vesper], Domain.Speed));
        Assert.Equal(0L, c.GetItemCount(ItemType.DomainScrollSpeed));
        Assert.Equal(DomainResult.NotScroll, DomainLogic.UseScroll(c, HeroId.Vesper, ItemType.HeroExpSmall, 1));
        Assert.Equal(DomainResult.NotEnoughItems, DomainLogic.UseScroll(c, HeroId.Vesper, ItemType.DomainScrollPower, 1));
    }
}
