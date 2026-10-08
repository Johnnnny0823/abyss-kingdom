using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class PvpTests
{
    const long T0 = 1_700_000_000_000;

    /// <summary>兩座相鄰的外環主城：攻方（有兵）與守方（護盾已過期）。</summary>
    static (CityState atk, CityState def, SimpleFieldRegistry world) Setup()
    {
        var sites = SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer).ToList();
        var a = sites[0];
        var d = sites.First(s => s != a && SurfaceLogic.Dist2(s.x, s.y, a.x, a.y) < 20 * 20);
        var atk = CityState.CreateNew(T0);
        atk.Surface.X = a.x; atk.Surface.Y = a.y;
        atk.Troops[TroopType.Shield] = 1000;
        var def = CityState.CreateNew(T0);
        def.Surface.X = d.x; def.Surface.Y = d.y;
        def.ShieldUntilMs = 0;
        var w = new SimpleFieldRegistry();
        w.SetCity(a.x, a.y, "atk");
        w.SetCity(d.x, d.y, "def");
        return (atk, def, w);
    }

    /// <summary>出征 → 抵達敵城 → 守方結算 → 攻方套用。回傳 (抵達事件, 結果, 攻方事件, 行軍)。</summary>
    static (MarchEvent arrive, PvpOutcome o, MarchEvent done, March m) Attack(CityState atk, CityState def, SimpleFieldRegistry w, long shields)
    {
        Assert.Equal(MarchResult.Ok, MarchLogic.Start(atk, HeroId.Hold, new() { [TroopType.Shield] = shields }, def.Surface.X, def.Surface.Y, T0, w, "atk"));
        var m = atk.Surface.Marches[0];
        var arrive = MarchLogic.Advance(atk, m.ArriveAt, w, "atk").Single();
        var o = PvpLogic.ResolveOnDefender(def, arrive.AttackSquads, 42, m.ArriveAt);
        var done = PvpLogic.FinishAttack(atk, m.Id, o, m.ArriveAt);
        return (arrive, o, done, m);
    }

    [Test]
    public static void NewCity_HasNewbieShield_OnlyOnce()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(7 * 24 * 3_600_000L, PvpConfig.NewbieShieldMs, "新手護盾 7 天");
        Assert.True(PvpLogic.IsShielded(c, T0 + PvpConfig.NewbieShieldMs - 1), "7 天內有護盾");
        Assert.True(!PvpLogic.IsShielded(c, T0 + PvpConfig.NewbieShieldMs), "之後就沒了");
        Assert.True(!PvpLogic.GrantNewbieShieldOnce(c, T0 + 999_999_999), "只發一次");
    }

    [Test]
    public static void OldShield_StillActive_IsExtendedToSevenDays_Once()
    {
        var c = CityState.CreateNew(T0);
        c.NewbieShieldVersion = 0;
        c.ShieldUntilMs = T0 + PvpConfig.OldNewbieShieldMs; // 舊版：24 小時
        Assert.True(PvpLogic.GrantNewbieShieldOnce(c, T0 + 1000), "補發");
        Assert.Equal(T0 + PvpConfig.NewbieShieldMs, c.ShieldUntilMs, "補足到 7 天");
        Assert.True(!PvpLogic.GrantNewbieShieldOnce(c, T0 + 2000), "只補一次");

        var attacked = CityState.CreateNew(T0);
        attacked.NewbieShieldVersion = 0;
        attacked.ShieldUntilMs = 0; // 攻打過別人，護盾已經解除
        PvpLogic.GrantNewbieShieldOnce(attacked, T0 + 1000);
        Assert.Equal(0L, attacked.ShieldUntilMs, "自己解除的護盾不會補回來");
    }

    [Test]
    public static void ArrivingAtEnemyCity_WaitsForServerResolution()
    {
        var (atk, def, w) = Setup();
        MarchLogic.Start(atk, HeroId.Hold, new() { [TroopType.Shield] = 100 }, def.Surface.X + 1, def.Surface.Y + 1, T0, w, "atk");
        var m = atk.Surface.Marches[0];
        var ev = MarchLogic.Advance(atk, m.ArriveAt, w, "atk").Single();
        Assert.Equal("def", ev.AttackTarget, "2×2 佔地任一格都算攻城");
        Assert.True(m.Sieging && m.Stationed, "在城下等結算");
        Assert.Equal(1, ev.AttackSquads.Count, "帶上抵達當下的陣容");
    }

    [Test]
    public static void Attacking_BreaksOwnShield()
    {
        var (atk, def, w) = Setup();
        Assert.True(PvpLogic.IsShielded(atk, T0), "攻方本來有新手護盾");
        MarchLogic.Start(atk, HeroId.Hold, new() { [TroopType.Shield] = 10 }, def.Surface.X, def.Surface.Y, T0, w, "atk");
        Assert.True(!PvpLogic.IsShielded(atk, T0), "出兵攻城就解除護盾");
    }

    [Test]
    public static void ShieldedTarget_NoFight_TroopsComeBack()
    {
        var (atk, def, w) = Setup();
        def.ShieldUntilMs = T0 + 10 * 3_600_000L;
        var (_, o, done, m) = Attack(atk, def, w, 100);
        Assert.True(o.Shielded && done.Shielded, "對方有護盾：沒有開打");
        Assert.True(m.Returning, "撤回");
        Assert.Equal(100L, m.Troops[TroopType.Shield], "沒有損失");
    }

    [Test]
    public static void Win_PlundersAboveProtection_LimitedByLoad()
    {
        var (atk, def, w) = Setup();
        foreach (var r in CityConfig.AllResources) def.Resources[r] = 5000;
        def.Troops[TroopType.Shield] = 10;
        var (_, o, done, m) = Attack(atk, def, w, 250);
        Assert.True(o.AttackerWon && done.BattleWon, "250 兵打 10 兵");
        long load = GatherConfig.Load(o.AttackerSurvivors);
        long total = o.Plunder.Values.Sum();
        long avail = (5000 - PvpConfig.Protected(0)) * PvpConfig.PlunderPercent / 100;
        Assert.True(total <= load, "不超過載重");
        Assert.True(o.Plunder.Values.All(v => v <= avail), "每種最多拿超過保護量部分的 20%");
        foreach (var kv in o.Plunder) Assert.Equal(5000 - kv.Value, def.GetResource(kv.Key), "守方被扣掉");
        Assert.True(m.Returning && m.Carry.Values.Sum() == total, "攻方帶著掠奪品回城");
        Assert.True(def.GetTroops(TroopType.Shield) < 10, "守方部隊有損失");
    }

    [Test]
    public static void EmptyCity_OnlyWallDefends()
    {
        var (atk, def, w) = Setup();
        var squads = PvpLogic.DefenseSquads(def);
        Assert.Equal(1, squads.Count, "沒有部隊只剩城防");
        var (_, o, _, _) = Attack(atk, def, w, 250);
        Assert.True(o.AttackerWon, "打穿城防");
    }

    [Test]
    public static void StrongDefense_AttackerLoses_BothSidesGetWounded()
    {
        var (atk, def, w) = Setup();
        def.Troops[TroopType.Crossbow] = 600;
        var squads = PvpLogic.DefenseSquads(def);
        Assert.True(squads[0].squad.LeaderHp > 100, "人數最多的兵種由主將帶");
        var (_, o, done, m) = Attack(atk, def, w, 30);
        Assert.True(!o.AttackerWon && done.BattleLost, "30 兵打不贏 600 兵");
        Assert.Equal(0, o.Plunder.Count, "打輸沒有掠奪");
        long atkLost = 30 - m.Troops[TroopType.Shield];
        Assert.Equal(atkLost * CityConfig.WoundedPercent / 100, atk.Wounded.GetValueOrDefault(TroopType.Shield), "攻方傷兵進自己的醫館");
    }

    [Test]
    public static void RecalledBeforeResolution_IsIgnored()
    {
        var (atk, def, w) = Setup();
        MarchLogic.Start(atk, HeroId.Hold, new() { [TroopType.Shield] = 50 }, def.Surface.X, def.Surface.Y, T0, w, "atk");
        var m = atk.Surface.Marches[0];
        var arrive = MarchLogic.Advance(atk, m.ArriveAt, w, "atk").Single();
        MarchLogic.Recall(atk, m.Id, m.ArriveAt, w, "atk");
        var o = PvpLogic.ResolveOnDefender(def, arrive.AttackSquads, 1, m.ArriveAt);
        Assert.True(PvpLogic.FinishAttack(atk, m.Id, o, m.ArriveAt) == null, "已經召回就不套用");
    }
}
