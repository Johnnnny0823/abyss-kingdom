using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Tests;

public static class BattleTests
{
    static SquadInput Squad(TroopType t, long count, long atk = 100, long def = 100, long hp = 1000) =>
        new SquadInput { Leader = "L", Troop = t, Count = count, LeaderAttack = atk, LeaderDefense = def, LeaderHp = hp };

    static List<SquadInput> Side(params SquadInput[] s) => s.ToList();

    [Test]
    public static void SameSeed_SameReport()
    {
        BattleResult Run() => BattleEngine.Run(
            Side(Squad(TroopType.Shield, 200), Squad(TroopType.Crossbow, 150)),
            Side(Squad(TroopType.Rider, 180), Squad(TroopType.Crossbow, 160)), seed: 42);
        var x = Run();
        var y = Run();
        Assert.Equal(x.Outcome, y.Outcome);
        Assert.Equal(x.Rounds, y.Rounds);
        Assert.Equal(x.Actions.Count, y.Actions.Count);
        for (int i = 0; i < x.Actions.Count; i++)
            Assert.Equal(x.Actions[i].Damage, y.Actions[i].Damage, "第 " + i + " 次攻擊");
    }

    [Test]
    public static void DifferentSeeds_CanDiffer()
    {
        var damages = new HashSet<long>();
        for (ulong seed = 1; seed <= 10; seed++)
            damages.Add(BattleEngine.Run(Side(Squad(TroopType.Shield, 100)), Side(Squad(TroopType.Shield, 100)), seed).Actions[0].Damage);
        Assert.True(damages.Count > 1, "不同種子應該有傷害浮動");
    }

    [Test]
    public static void MuchStrongerSide_Wins()
    {
        var r = BattleEngine.Run(Side(Squad(TroopType.Shield, 500)), Side(Squad(TroopType.Shield, 20)), seed: 7);
        Assert.Equal(BattleOutcome.AttackerWins, r.Outcome);
        Assert.True(!r.Defenders[0].LeaderStanding, "敵方領軍倒下");
        Assert.Equal(0L, r.Defenders[0].TroopsLeft);
    }

    [Test]
    public static void Counter_DecidesEvenFight()
    {
        // 同兵力：懸索騎克制鳴弦手，無論誰當攻方都應該是懸索騎贏
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var r1 = BattleEngine.Run(Side(Squad(TroopType.Rider, 200)), Side(Squad(TroopType.Crossbow, 200)), seed);
            Assert.Equal(BattleOutcome.AttackerWins, r1.Outcome, "攻方懸索騎 seed " + seed);
            var r2 = BattleEngine.Run(Side(Squad(TroopType.Crossbow, 200)), Side(Squad(TroopType.Rider, 200)), seed);
            Assert.Equal(BattleOutcome.DefenderWins, r2.Outcome, "守方懸索騎 seed " + seed);
        }
    }

    [Test]
    public static void CounterPercent_IsRecordedInReport()
    {
        var r = BattleEngine.Run(Side(Squad(TroopType.Shield, 100)), Side(Squad(TroopType.Rider, 100)), seed: 3);
        Assert.True(r.Actions.Any(a => a.Side == BattleSide.Attacker && a.CounterPercent == BattleConfig.CounterBonusPercent), "沉錨衛打懸索騎是克制");
        Assert.True(r.Actions.Any(a => a.Side == BattleSide.Defender && a.CounterPercent == BattleConfig.CounteredPercent), "懸索騎打沉錨衛是被克制");
    }

    [Test]
    public static void Squads_TargetWhatTheyCounter()
    {
        // 守方前排是沉錨衛、後排是鳴弦手：攻方懸索騎應該繞過前排，打它克制的鳴弦手
        var r = BattleEngine.Run(Side(Squad(TroopType.Rider, 100)),
            Side(Squad(TroopType.Shield, 100), Squad(TroopType.Crossbow, 100)), seed: 9);
        var first = r.Actions.First(a => a.Side == BattleSide.Attacker);
        Assert.Equal(1, first.Target, "懸索騎先打鳴弦手（index 1）");
    }

    [Test]
    public static void FasterTroops_ActFirst()
    {
        var r = BattleEngine.Run(Side(Squad(TroopType.Shield, 100)), Side(Squad(TroopType.Rider, 100)), seed: 1);
        Assert.Equal(BattleSide.Defender, r.Actions[0].Side, "懸索騎速度較快，先出手");
        Assert.Equal((long)r.Rounds, r.Defenders[0].FirstStrikes, "每回合都搶先出手");
        Assert.Equal(0L, r.Attackers[0].FirstStrikes);
    }

    [Test]
    public static void EqualFightBetweenTanks_CanDraw()
    {
        var r = BattleEngine.Run(Side(Squad(TroopType.Shield, 1, atk: 0, def: 1000, hp: 1_000_000)),
            Side(Squad(TroopType.Shield, 1, atk: 0, def: 1000, hp: 1_000_000)), seed: 5);
        Assert.Equal(BattleOutcome.Draw, r.Outcome);
        Assert.Equal(BattleConfig.MaxRounds, r.Rounds);
    }

    [Test]
    public static void DamageAccounting_IsConsistent()
    {
        var r = BattleEngine.Run(Side(Squad(TroopType.Crossbow, 300), Squad(TroopType.Shield, 200)),
            Side(Squad(TroopType.Rider, 250), Squad(TroopType.Shield, 250)), seed: 11);
        long dealtByAttackers = r.Attackers.Sum(s => s.DamageDealt);
        long takenByDefenders = r.Defenders.Sum(s => s.DamageTaken);
        Assert.Equal(dealtByAttackers, takenByDefenders, "攻方造成 = 守方承受");
        Assert.Equal(r.Actions.Where(a => a.Side == BattleSide.Attacker).Sum(a => a.Damage), dealtByAttackers, "戰報加總一致");
        foreach (var s in r.Attackers.Concat(r.Defenders))
            Assert.True(s.TroopsLeft >= 0 && s.TroopsLeft <= s.TroopsStart, "剩餘兵力合理");
    }

    [Test]
    public static void InvalidInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => BattleEngine.Run(new List<SquadInput>(), Side(Squad(TroopType.Shield, 1)), 1));
        Assert.Throws<ArgumentException>(() => BattleEngine.Run(Side(Squad(TroopType.Shield, -1)), Side(Squad(TroopType.Shield, 1)), 1));
        var six = Enumerable.Range(0, 6).Select(_ => Squad(TroopType.Shield, 1)).ToList();
        Assert.Throws<ArgumentException>(() => BattleEngine.Run(six, Side(Squad(TroopType.Shield, 1)), 1));
    }
}
