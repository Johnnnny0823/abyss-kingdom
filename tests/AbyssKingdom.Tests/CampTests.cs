using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class CampTests
{
    const long T0 = 1_700_000_000_000;

    /// <summary>城市＋附近一座指定等級的怪物營地（同分區）。</summary>
    static (CityState city, Feature camp) Setup(int level)
    {
        foreach (var f in SurfaceLogic.Features().Where(f => f.Type == FeatureType.MonsterCamp && f.Level == level))
        {
            var z = SurfaceLogic.Zone(f.X, f.Y);
            var site = SurfaceLogic.Sites().FirstOrDefault(s => s.z == z && SurfaceLogic.Dist2(s.x, s.y, f.X, f.Y) < 20 * 20);
            if (site == default) continue;
            var c = CityState.CreateNew(T0);
            c.Surface.X = site.x;
            c.Surface.Y = site.y;
            c.Troops[TroopType.Shield] = 1000;
            c.Troops[TroopType.Crossbow] = 1000;
            c.Troops[TroopType.Rider] = 1000;
            return (c, f);
        }
        throw new Exception($"找不到 Lv.{level} 的測試營地");
    }

    static (MarchEvent ev, March m) Attack(CityState c, Feature camp, HeroId hero, Dictionary<TroopType, long> troops, SimpleFieldRegistry w)
    {
        Assert.Equal(MarchResult.Ok, MarchLogic.Start(c, hero, troops, camp.X, camp.Y, T0, w, "me"));
        var m = c.Surface.Marches.Single(x => x.Hero == hero);
        var ev = MarchLogic.Advance(c, m.ArriveAt, w, "me").Single(e => e.MarchId == m.Id);
        return (ev, m);
    }

    [Test]
    public static void FullTeam_BeatsLevel1Camp_BringsLootHome()
    {
        var (c, camp) = Setup(1);
        var w = new SimpleFieldRegistry();
        long woodBefore = c.GetResource(ResourceType.Wood);
        long books = c.GetItemCount(ItemType.HeroExpSmall);
        var (ev, m) = Attack(c, camp, HeroId.Hold, new() { [TroopType.Shield] = 250 }, w);
        Assert.True(ev.BattleWon, "滿編主將打得贏 Lv.1 營地");
        Assert.True(m.Returning, "打完自動回城");
        Assert.True(!w.CampAvailable(SurfaceLogic.Tile(camp.X, camp.Y), m.ArriveAt - 1), "營地被打掉");

        var back = MarchLogic.Advance(c, m.ArriveAt, w, "me");
        Assert.True(back.Single().Disbanded, "回城");
        Assert.Equal(woodBefore + 300, c.GetResource(ResourceType.Wood), "戰利品資源入庫");
        Assert.Equal(books + 2, c.GetItemCount(ItemType.HeroExpSmall), "戰利品道具入背包");
    }

    [Test]
    public static void TinyTeam_Loses_CasualtiesSplitIntoWoundedAndDead()
    {
        var (c, camp) = Setup(2); // Lv.1 now deliberately supports the tutorial's small army.
        var w = new SimpleFieldRegistry();
        var (ev, m) = Attack(c, camp, HeroId.Hold, new() { [TroopType.Shield] = 20 }, w);
        Assert.True(ev.BattleLost, "20 兵打不贏");
        long lost = 20 - m.Troops[TroopType.Shield];
        Assert.True(lost > 0, "有損失");
        long wounded = ev.Wounded.GetValueOrDefault(TroopType.Shield);
        long dead = ev.Dead.GetValueOrDefault(TroopType.Shield);
        Assert.Equal(lost, wounded + dead, "倒下的 = 傷兵 + 陣亡");
        Assert.Equal(lost * CityConfig.WoundedPercent / 100, wounded, "七成變傷兵");
        Assert.Equal(wounded, c.Wounded[TroopType.Shield], "傷兵進醫館");
        Assert.True(w.CampAvailable(SurfaceLogic.Tile(camp.X, camp.Y), m.ArriveAt), "營地還在");
    }

    [Test]
    public static void Level3Camp_IsTooHardForAStarterHero()
    {
        var (c, camp) = Setup(3);
        var (ev, _) = Attack(c, camp, HeroId.Hold, new() { [TroopType.Shield] = 250 }, new SimpleFieldRegistry());
        Assert.True(ev.BattleLost, "新手主將打不過 Lv.3（崖緣）營地");
    }

    [Test]
    public static void MixedTroops_FightAsSeparateSquads()
    {
        var (c, camp) = Setup(1);
        var m = new March { Hero = HeroId.Hold, Troops = { [TroopType.Shield] = 150, [TroopType.Rider] = 60, [TroopType.Crossbow] = 40 } };
        var squads = MarchLogic.Squads(c, m);
        Assert.Equal(3, squads.Count, "三個兵種三隊");
        Assert.Equal(TroopType.Shield, squads[0].troop, "人數最多的兵種由主將帶");
        Assert.True(squads[0].squad.LeaderHp > 100 && squads[1].squad.LeaderHp == 1, "其他隊是隊長帶領");
        var (ev, _) = Attack(c, camp, HeroId.Hold, new() { [TroopType.Shield] = 150, [TroopType.Rider] = 60, [TroopType.Crossbow] = 40 }, new SimpleFieldRegistry());
        Assert.True(ev.BattleWon || ev.BattleLost, "混編可以開打");
    }

    [Test]
    public static void DefeatedCamp_IsGone_UntilRespawn()
    {
        var (c, camp) = Setup(1);
        var w = new SimpleFieldRegistry();
        var (first, m1) = Attack(c, camp, HeroId.Hold, new() { [TroopType.Shield] = 250 }, w);
        Assert.True(first.BattleWon, "第一次打贏");
        var (second, m2) = Attack(c, camp, HeroId.Vesper, new() { [TroopType.Crossbow] = 10 }, w);
        Assert.True(second.CampGone && m2.Stationed, "營地被打掉了，只能駐紮");
        Assert.True(w.CampAvailable(SurfaceLogic.Tile(camp.X, camp.Y), m1.ArriveAt + CampConfig.RespawnMs), "時間到就重生");
    }

    /// <summary>模擬交錯：我方檢查「營地還在」之後、結算之前，另一位玩家先把營地打掉了。</summary>
    sealed class RivalWinsFirst : IFieldRegistry
    {
        readonly SimpleFieldRegistry _inner = new();
        public bool CampAvailable(int tile, long nowMs)
        {
            bool ok = _inner.CampAvailable(tile, nowMs);
            _inner.TryDefeatCamp(tile, nowMs); // 對手搶先擊敗
            return ok;
        }
        public bool TryDefeatCamp(int tile, long nowMs) => _inner.TryDefeatCamp(tile, nowMs);
        public ulong NextSeed() => _inner.NextSeed();
        public string CityOwnerAt(int tile) => _inner.CityOwnerAt(tile);
        public long Occupy(int tile, string owner, long marchId, long nowMs) => _inner.Occupy(tile, owner, marchId, nowMs);
        public void Release(int tile, string owner, long marchId, long gathered, long nowMs) => _inner.Release(tile, owner, marchId, gathered, nowMs);
    }

    [Test]
    public static void TwoPlayersSameCamp_OnlyOneGetsTheReward()
    {
        var (c, camp) = Setup(1);
        var w = new RivalWinsFirst();
        long books = c.GetItemCount(ItemType.HeroExpSmall);
        Assert.Equal(MarchResult.Ok, MarchLogic.Start(c, HeroId.Hold, new() { [TroopType.Shield] = 250 }, camp.X, camp.Y, T0, w, "me"));
        var m = c.Surface.Marches.Single();
        var ev = MarchLogic.Advance(c, m.ArriveAt, w, "me").Single();
        Assert.True(ev.CampGone && !ev.BattleWon, "被搶先擊敗：當作營地已經不在");
        Assert.Equal(250L, m.Troops[TroopType.Shield], "沒有算這場戰鬥的損失");
        Assert.True(m.Carry.Count == 0 && m.LootItems.Count == 0, "沒有戰利品");
        Assert.Equal(books, c.GetItemCount(ItemType.HeroExpSmall), "道具沒有增加");
        Assert.True(m.Stationed, "駐紮在原地");
    }

    [Test]
    public static void Hospital_Capacity_LimitsWounded()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(100L, CityLogic.HospitalFree(c), "0 級醫館也收 100 名");
        var (wounded, dead) = CityLogic.TakeCasualties(c, new() { [TroopType.Shield] = 1000 });
        Assert.Equal(100L, wounded[TroopType.Shield], "只收得下 100");
        Assert.Equal(900L, dead[TroopType.Shield], "其餘陣亡");
        Assert.Equal(0L, CityLogic.HospitalFree(c));
        c.Buildings[BuildingType.Hospital] = 2;
        Assert.Equal(400L, CityLogic.HospitalFree(c), "每級 +200");
    }
}
