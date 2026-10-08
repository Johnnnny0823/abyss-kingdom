using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Tests;

public static class ExpeditionTests
{
    const long T0 = 1_700_000_000_000;
    const long Hour = CityConfig.MsPerHour;
    static readonly HeroId[] All3 = { HeroId.Hold, HeroId.Vesper, HeroId.Karst };

    static CityState City(int level = 1)
    {
        var c = CityState.CreateNew(T0);
        foreach (var h in c.Heroes.Values) h.Level = level;
        foreach (var g in c.Gear.ToList()) GearLogic.Equip(c, g.Uid, HeroId.Hold);
        return c;
    }

    static IList<IList<HeroId>> TeamsFor(int stage) => ExpeditionConfig.Stage(stage).Branches.Count == 1
        ? new List<IList<HeroId>> { All3 }
        : new List<IList<HeroId>> { new[] { HeroId.Hold, HeroId.Vesper }, new[] { HeroId.Karst } };

    /// <summary>同一隊伍在 20 個種子下打某一關的勝場數（每次都用新的城市，不互相影響）。</summary>
    static int Wins(int level, int stage)
    {
        int wins = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var c = City(level);
            c.Expedition.Cleared = stage - 1;
            ExpeditionLogic.Fight(c, stage, TeamsFor(stage), seed, T0, out var r);
            if (r.Win) wins++;
        }
        return wins;
    }

    // ───── 難度曲線（改數值時這些測試會提醒你曲線是否還合理） ─────

    [Test]
    public static void Curve_StarterTeamClearsFirstStage()
    {
        Assert.Equal(20, Wins(1, 1), "Lv1 一定打得過第 1 關");
    }

    [Test]
    public static void Curve_BossNeedsRealProgress()
    {
        Assert.Equal(0, Wins(1, 10), "Lv1 不可能打贏守層者");
        Assert.Equal(0, Wins(8, 10), "Lv8 也還打不過守層者");
        Assert.True(Wins(20, 10) >= 15, "Lv20 應該穩定打贏守層者");
    }

    [Test]
    public static void Curve_BranchStageNeedsSomeLevels()
    {
        Assert.Equal(0, Wins(1, 6), "Lv1 打不過岔路關");
        Assert.True(Wins(10, 6) >= 15, "Lv10 應該打得過岔路關");
    }

    // ───── 規則 ─────

    [Test]
    public static void Stage6_IsADualTeamBranch()
    {
        Assert.Equal(2, ExpeditionConfig.Stage(6).Branches.Count);
        Assert.True(ExpeditionConfig.Stage(10).IsBoss, "第 10 關是守層者");
    }

    [Test]
    public static void Fight_Validation()
    {
        var c = City();
        Assert.Equal(ExpeditionCode.StageLocked, ExpeditionLogic.Fight(c, 2, TeamsFor(2), 1, T0, out _), "不能跳關");
        Assert.Equal(ExpeditionCode.EmptyTeam, ExpeditionLogic.Fight(c, 1, new List<IList<HeroId>> { new HeroId[0] }, 1, T0, out _));
        Assert.Equal(ExpeditionCode.DuplicateHero, ExpeditionLogic.Fight(c, 1, new List<IList<HeroId>> { new[] { HeroId.Hold, HeroId.Hold } }, 1, T0, out _));
        Assert.Equal(ExpeditionCode.HeroNotOwned, ExpeditionLogic.Fight(c, 1, new List<IList<HeroId>> { new[] { (HeroId)99 } }, 1, T0, out _));
        Assert.Equal(ExpeditionCode.TeamTooLarge, ExpeditionLogic.Fight(c, 1,
            new List<IList<HeroId>> { Enumerable.Repeat(HeroId.Hold, 6).ToArray() }, 1, T0, out _));

        c.Expedition.Cleared = 5;
        Assert.Equal(ExpeditionCode.WrongTeamCount, ExpeditionLogic.Fight(c, 6, new List<IList<HeroId>> { All3 }, 1, T0, out _), "岔路要兩隊");
        Assert.Equal(ExpeditionCode.DuplicateHero, ExpeditionLogic.Fight(c, 6,
            new List<IList<HeroId>> { new[] { HeroId.Hold }, new[] { HeroId.Hold } }, 1, T0, out _), "同一位主將不能走兩條路");

        c.Expedition.Cleared = 10;
        Assert.Equal(ExpeditionCode.ChapterComplete, ExpeditionLogic.Fight(c, 11, TeamsFor(1), 1, T0, out _));
    }

    [Test]
    public static void Win_AdvancesAndGrantsRewards()
    {
        var c = City(20);
        c.Expedition.Cleared = 3;
        long wood = c.GetResource(ResourceType.Wood);
        int gearBefore = c.Gear.Count;
        Assert.Equal(ExpeditionCode.Ok, ExpeditionLogic.Fight(c, 4, TeamsFor(4), 7, T0, out var r));
        Assert.True(r.Win, "Lv20 打第 4 關必勝");
        Assert.Equal(4, c.Expedition.Cleared);
        Assert.Equal(wood + 1200, c.GetResource(ResourceType.Wood), "300 × 4");
        Assert.Equal(gearBefore + 1, c.Gear.Count, "第 4 關掉骨鈴長矛");
        Assert.Equal(GearId.BoneBellSpear, Assert.NotNull(r.Rewards, "獎勵").Gear!.Value);
        Assert.Equal(T0, c.Expedition.IdleSinceMs, "首次通關開始累積掛機");
    }

    [Test]
    public static void Loss_ChangesNothing()
    {
        var c = City(1);
        c.Expedition.Cleared = 9;
        long wood = c.GetResource(ResourceType.Wood);
        Assert.Equal(ExpeditionCode.Ok, ExpeditionLogic.Fight(c, 10, TeamsFor(10), 3, T0, out var r));
        Assert.True(!r.Win, "Lv1 打守層者會輸");
        Assert.Equal(9, c.Expedition.Cleared);
        Assert.Equal(wood, c.GetResource(ResourceType.Wood));
        Assert.True(r.Rewards == null, "輸了沒有獎勵");
    }

    [Test]
    public static void SameSeed_SameResult()
    {
        ExpeditionResult Run()
        {
            var c = City(5);
            c.Expedition.Cleared = 4;
            ExpeditionLogic.Fight(c, 5, TeamsFor(5), 99, T0, out var r);
            return r;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a.Win, b.Win);
        Assert.Equal(a.Reports[0].Actions.Count, b.Reports[0].Actions.Count);
    }

    // ───── 掛機收益 ─────

    [Test]
    public static void Idle_AccumulatesAndCaps()
    {
        var c = City();
        Assert.Equal(ExpeditionCode.NothingToClaim, ExpeditionLogic.ClaimIdle(c, T0 + Hour, out _), "還沒通關沒有收益");

        c.Expedition.Cleared = 4;
        c.Expedition.IdleSinceMs = T0;
        long wood = c.GetResource(ResourceType.Wood);
        long books = c.GetItemCount(ItemType.HeroExpSmall);
        Assert.Equal(ExpeditionCode.Ok, ExpeditionLogic.ClaimIdle(c, T0 + 2 * Hour, out var r));
        Assert.Equal(4 * 60 * 2L, r.Resources[ResourceType.Wood], "4 關 × 60 × 2 小時");
        Assert.Equal(wood + 480, c.GetResource(ResourceType.Wood));
        Assert.Equal(books + 4, c.GetItemCount(ItemType.HeroExpSmall), "4 ÷ 2 × 2 小時");
        Assert.Equal(T0 + 2 * Hour, c.Expedition.IdleSinceMs, "領取後重新計時");

        var capped = ExpeditionLogic.PendingIdle(c, T0 + 100 * Hour);
        Assert.Equal(4 * 60 * 12L, capped.Resources[ResourceType.Wood], "最多 12 小時");
    }
}
