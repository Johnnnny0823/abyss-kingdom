using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Tests;

public static class ElementTests
{
    static List<(Element, Polarity)> Team(params (Element, Polarity)[] m) => m.ToList();
    const Polarity Yang = Polarity.Yang, Yin = Polarity.Yin;

    [Test]
    public static void Overcome_Cycle()
    {
        Assert.True(Elements.Overcomes(Element.Wood, Element.Earth), "木剋土");
        Assert.True(Elements.Overcomes(Element.Earth, Element.Water), "土剋水");
        Assert.True(Elements.Overcomes(Element.Water, Element.Fire), "水剋火");
        Assert.True(Elements.Overcomes(Element.Fire, Element.Metal), "火剋金");
        Assert.True(Elements.Overcomes(Element.Metal, Element.Wood), "金剋木");
        foreach (var a in Elements.All)
            Assert.Equal(1, Elements.All.Count(b => Elements.Overcomes(a, b)), a + " 恰好剋一種");
    }

    [Test]
    public static void Percent_DependsOnPolarity()
    {
        Assert.Equal(125, Elements.Percent(Element.Water, Yang, Element.Fire, Yang), "同性相剋（七殺）");
        Assert.Equal(115, Elements.Percent(Element.Water, Yang, Element.Fire, Yin), "異性相剋（正官）");
        Assert.Equal(85, Elements.Percent(Element.Fire, Yang, Element.Water, Yang), "被剋");
        Assert.Equal(100, Elements.Percent(Element.Wood, Yang, Element.Fire, Yang), "相生不影響傷害");
        Assert.Equal(100, Elements.Percent(Element.None, Polarity.None, Element.Fire, Yang), "無屬性");
    }

    [Test]
    public static void Battle_ElementChangesDamage()
    {
        SquadInput S(Element e) => new SquadInput { Leader = "L", Troop = TroopType.Shield, Count = 100, LeaderAttack = 100, LeaderDefense = 100, LeaderHp = 1000, Element = e, Polarity = Yang };
        long Hit(Element a, Element d) => BattleEngine.Run(new[] { S(a) }.ToList(), new[] { S(d) }.ToList(), 9)
            .Actions.First(x => x.Side == BattleSide.Attacker).Damage;
        long neutral = Hit(Element.Wood, Element.Fire);
        Assert.True(Hit(Element.Water, Element.Fire) > neutral, "剋制傷害較高");
        Assert.True(Hit(Element.Fire, Element.Water) < neutral, "被剋傷害較低");
        var r = BattleEngine.Run(new[] { S(Element.Water) }.ToList(), new[] { S(Element.Fire) }.ToList(), 9);
        Assert.Equal(125, r.Actions.First(x => x.Side == BattleSide.Attacker).ElementPercent, "戰報記錄五行倍率");
    }

    [Test]
    public static void Synergy_SameElement()
    {
        var s3 = Elements.Synergies(Team((Element.Fire, Yang), (Element.Fire, Yin), (Element.Fire, Yang)));
        Assert.True(s3.Contains(Synergy.Triad), "三位同屬 → 三合");
        var s5 = Elements.Synergies(Enumerable.Repeat((Element.Water, Yin), 5).ToList());
        Assert.True(s5.Contains(Synergy.Quintet) && !s5.Contains(Synergy.Triad), "五同取代三合");
        Assert.True(s5.Contains(Synergy.PureYin), "五位都陰 → 純陰");
    }

    [Test]
    public static void Synergy_AllDifferent()
    {
        var s3 = Elements.Synergies(Team((Element.Wood, Yang), (Element.Metal, Yin), (Element.Water, Yang)));
        Assert.True(s3.Contains(Synergy.ThreeTalents), "三位全不同 → 三才");
        var s5 = Elements.Synergies(Team((Element.Wood, Yang), (Element.Fire, Yin), (Element.Earth, Yang), (Element.Metal, Yin), (Element.Water, Yang)));
        Assert.True(s5.Contains(Synergy.FiveComplete), "五行俱全");
        Assert.True(s5.Contains(Synergy.Chain5), "五行到齊必然相生五環");
        Assert.True(s5.Contains(Synergy.Harmony), "三陽二陰 → 陰陽調和");
    }

    [Test]
    public static void Synergy_MixedChain()
    {
        // 混搭：木、火、土（相生三環）＋ 兩位重複的木
        var s = Elements.Synergies(Team((Element.Wood, Yang), (Element.Fire, Yang), (Element.Earth, Yang), (Element.Wood, Yang)));
        Assert.True(s.Contains(Synergy.Chain3), "木生火生土 → 相生三環");
        Assert.True(s.Contains(Synergy.PureYang), "四位都陽 → 純陽");
        Assert.True(!s.Contains(Synergy.ThreeTalents), "有重複就不是三才");
        Assert.Equal(4, Elements.LongestChain(new[] { Element.Water, Element.Wood, Element.Fire, Element.Earth }), "水生木生火生土");
        Assert.Equal(1, Elements.LongestChain(new[] { Element.Wood, Element.Earth }), "木與土不相生");
    }

    [Test]
    public static void Synergy_AppliesToSquads()
    {
        var team = Enumerable.Range(0, 3).Select(_ => new SquadInput { Element = Element.Fire, Polarity = Yang }).ToList();
        var got = Elements.ApplyTeamSynergy(team);
        Assert.True(got.Contains(Synergy.Triad) && got.Contains(Synergy.PureYang), "三合＋純陽");
        Assert.Equal(118L, team[0].AttackPercent, "攻擊 +8% +10%");
    }

    [Test]
    public static void StarterHeroes_HaveDistinctElements()
    {
        var els = HeroConfig.All.Select(h => h.Element).ToList();
        Assert.True(els.All(e => e != Element.None), "每位主將都有五行");
        Assert.Equal(els.Count, els.Distinct().Count(), "初始三位五行不重複 → 可觸發三才");
    }

    [Test]
    public static void Expedition_ReportsSynergies()
    {
        var c = CityState.CreateNew(1_700_000_000_000);
        ExpeditionLogic.Fight(c, 1, new List<IList<HeroId>> { new[] { HeroId.Hold, HeroId.Vesper, HeroId.Karst } }, 3, 0, out var r);
        Assert.True(r.OurSynergies[0].Contains(Synergy.ThreeTalents), "土、金、木 → 三才");
        Assert.True(r.OurSynergies[0].Contains(Synergy.Harmony), "二陽一陰 → 陰陽調和");
    }
}
