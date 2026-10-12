using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Tests;

public static class GarrisonBattleTests
{
    const long Now = 1_700_000_000_000;
    private static (CityState home, CityState ally, March march, GarrisonDefenseInput input) Setup()
    {
        var sites = SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer).Take(2).ToArray();
        CityState City(int i)
        {
            var c = CityState.CreateNew(Now);
            c.ShieldUntilMs = 0;
            c.Buildings[BuildingType.Keep] = 3;
            c.Buildings[BuildingType.Hospital] = 3;
            c.Surface.X = sites[i].x; c.Surface.Y = sites[i].y;
            foreach (var t in CityConfig.AllTroops) c.Troops[t] = 100;
            return c;
        }
        var home = City(0); var ally = City(1);
        Assert.Equal(AllianceMilitaryResult.Ok, MarchLogic.StartReinforcement(ally, home, "home", HeroId.Hold,
            CityConfig.AllTroops.ToDictionary(t => t, _ => 100L), Now, out _));
        var march = ally.Surface.Marches.Single();
        MarchLogic.AdvanceReinforcement(ally, march.Id, march.ArriveAt);
        march.GarrisonSettlementId = "battle";
        var input = new GarrisonDefenseInput { OwnerId = "ally", MarchId = march.Id, ReinforcementId = "r1",
            Squads = MarchLogic.Squads(ally, march).Select(s => s.squad).ToList() };
        return (home, ally, march, input);
    }
    private static List<(SquadInput squad, TroopType troop)> Attackers() => new()
    {
        (new SquadInput { Leader = "enemy", LeaderHp = 100_000, LeaderAttack = 100_000,
            LeaderDefense = 10_000, Troop = TroopType.Rider, Count = 10_000 }, TroopType.Rider)
    };

    [Test]
    public static void SevenDefenders_AttributeSameTroopToDifferentOwners()
    {
        var (home, ally, march, input) = Setup();
        var result = PvpLogic.ResolveOnDefender(home, Attackers(), 42, Now, "battle", input);
        var garrison = Assert.NotNull(result.Garrison, "援軍戰損");
        foreach (var t in CityConfig.AllTroops)
        {
            long homeLost = 100 - home.GetTroops(t);
            Assert.Equal(homeLost, result.DefenderWounded.GetValueOrDefault(t) + result.DefenderDead.GetValueOrDefault(t));
            Assert.Equal(100L, garrison.Losses[t] + garrison.Survivors[t]);
            Assert.Equal(100L, march.Troops[t], "城主結算不直接改援軍擁有者");
        }
        Assert.True(PvpLogic.FinishGarrison(ally, garrison, "battle", Now, false), "原主人的鎖內套用戰損");
        foreach (var t in CityConfig.AllTroops)
        {
            Assert.Equal(garrison.Survivors[t], march.Troops[t]);
            Assert.Equal(garrison.Losses[t] * 70 / 100, ally.Wounded.GetValueOrDefault(t));
            Assert.Equal(0L, ally.GetTroops(t), "駐外倖存者不能進兵營");
        }
        long wounded = CityLogic.WoundedTotal(ally);
        Assert.True(PvpLogic.FinishGarrison(ally, garrison, "battle", Now + 1, false), "戰損重試成功");
        Assert.Equal(wounded, CityLogic.WoundedTotal(ally), "不重複傷兵");
    }

    [Test]
    public static void ShieldAndBattleReplay_DoNotRepeatGarrisonDamage()
    {
        var (home, ally, march, input) = Setup();
        home.ShieldUntilMs = Now + 1;
        var shielded = PvpLogic.ResolveOnDefender(home, Attackers(), 1, Now, "shield", input);
        Assert.True(shielded.Shielded && shielded.Garrison == null, "護盾擋掉整場戰鬥");
        home.ShieldUntilMs = 0;
        var first = PvpLogic.ResolveOnDefender(home, Attackers(), 1, Now, "battle", input);
        long wounded = CityLogic.WoundedTotal(home);
        var retry = PvpLogic.ResolveOnDefender(home, Attackers(), 999, Now + 1, "battle");
        Assert.True(ReferenceEquals(first, retry), "重試使用持久化結果，援軍快照不用重建");
        Assert.Equal(wounded, CityLogic.WoundedTotal(home));
        Assert.Equal("ally", retry.Garrison!.OwnerId);
        Assert.Equal("battle", march.GarrisonSettlementId);
    }

    [Test]
    public static void WrongSettlement_CannotAlterOrReleaseReinforcement()
    {
        var (home, ally, march, input) = Setup();
        var result = PvpLogic.ResolveOnDefender(home, Attackers(), 1, Now, "battle", input);
        Assert.True(!PvpLogic.FinishGarrison(ally, result.Garrison!, "forged", Now, true), "錯誤戰鬥不能套用");
        Assert.Equal("battle", march.GarrisonSettlementId);
        Assert.True(!march.Returning, "不能提前釋放");
        Assert.Equal(0L, CityLogic.WoundedTotal(ally));
    }
}
