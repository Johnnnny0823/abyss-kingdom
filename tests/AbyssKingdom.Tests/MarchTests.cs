using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class MarchTests
{
    const long T0 = 1_700_000_000_000;
    static AbyssMap L1 => MapConfig.Layer(1);

    static CityState City(string player = "marcher")
    {
        var c = CityState.CreateNew(T0);
        MapLogic.EnsureStarted(c, player);
        c.Troops[TroopType.Shield] = 300;
        c.Troops[TroopType.Rider] = 300;
        return c;
    }

    static Dictionary<TroopType, long> T(TroopType t, long n) => new() { [t] = n };

    /// <summary>從垂降口出發，第一個可以走到（不經封鎖）的未知鄰居。</summary>
    static int Frontier(CityState c)
    {
        int home = c.Map.HomeGate;
        foreach (var ei in L1.Nodes[home].Edges)
        {
            var e = L1.Edges[ei];
            if (e.Field != ForceField.Seal && !c.Map.Explored.Contains(e.Other(home))) return e.Other(home);
        }
        throw new Exception("垂降口沒有可走的鄰居");
    }

    [Test]
    public static void Start_DeductsTroops_AndSchedules()
    {
        var c = City();
        int dest = Frontier(c);
        Assert.Equal(MarchResult.Ok, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 100), dest, T0));
        Assert.Equal(200L, c.GetTroops(TroopType.Shield), "部隊從兵營扣除");
        var m = c.Map.Marches[0];
        Assert.Equal(c.Map.HomeGate, m.Path[0]);
        Assert.Equal(dest, m.Destination);
        long expect = L1.EdgeBetween(m.Path[0], dest).Length * MarchConfig.MsPerLengthUnit;
        Assert.Equal(T0 + expect, m.ArriveAt, "抵達時間＝廊道長度 × 每單位毫秒");
    }

    [Test]
    public static void RidersAreFaster()
    {
        var a = City("a");
        var b = City("a");
        int dest = Frontier(a);
        MarchLogic.Start(a, HeroId.Hold, T(TroopType.Shield, 50), dest, T0);
        MarchLogic.Start(b, HeroId.Hold, T(TroopType.Rider, 50), dest, T0);
        Assert.True(b.Map.Marches[0].ArriveAt < a.Map.Marches[0].ArriveAt, "懸索騎先到");
        var mixed = City("a");
        MarchLogic.Start(mixed, HeroId.Hold, new() { [TroopType.Rider] = 50, [TroopType.Shield] = 1 }, dest, T0);
        Assert.Equal(a.Map.Marches[0].ArriveAt, mixed.Map.Marches[0].ArriveAt, "混編以最慢的為準");
    }

    [Test]
    public static void Arrival_StationsAndRevealsFog()
    {
        var c = City();
        int dest = Frontier(c);
        MarchLogic.Start(c, HeroId.Karst, T(TroopType.Rider, 50), dest, T0);
        var m = c.Map.Marches[0];
        Assert.Equal(0, MarchLogic.Advance(c, m.ArriveAt - 1).Count, "還沒到");
        Assert.True(!c.Map.Explored.Contains(dest), "抵達前仍是迷霧");
        var events = MarchLogic.Advance(c, m.ArriveAt);
        Assert.True(events.Count == 1 && events[0].Discovered, "抵達並揭開");
        Assert.True(m.Stationed && c.Map.Explored.Contains(dest), "駐紮、迷霧揭開");
    }

    [Test]
    public static void Position_InterpolatesAlongPath()
    {
        var c = City();
        MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 10), Frontier(c), T0);
        var m = c.Map.Marches[0];
        long mid = (m.ArriveAtMs[0] + m.ArriveAtMs[1]) / 2;
        var (seg, pm) = MarchLogic.Position(m, mid);
        Assert.Equal(0, seg);
        Assert.True(pm >= 490 && pm <= 510, $"走到一半（{pm}‰）");
    }

    [Test]
    public static void Recall_ReturnsTroopsAtTheGate()
    {
        var c = City();
        MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 100), Frontier(c), T0);
        var m = c.Map.Marches[0];
        Assert.Equal(MarchResult.NotStationed, MarchLogic.Recall(c, m.Id, T0 + 1), "還在路上不能召回");
        MarchLogic.Advance(c, m.ArriveAt);
        Assert.Equal(MarchResult.Ok, MarchLogic.Recall(c, m.Id, m.ArriveAt));
        Assert.True(m.Returning, "召回中");
        var back = m.ArriveAt;
        var events = MarchLogic.Advance(c, back);
        Assert.True(events.Count == 1 && events[0].Disbanded, "回到垂降口解散");
        Assert.Equal(300L, c.GetTroops(TroopType.Shield), "部隊歸營");
        Assert.Equal(0, c.Map.Marches.Count);
    }

    [Test]
    public static void Path_OnlyThroughExploredAndNotSealed()
    {
        var c = City();
        var far = L1.Nodes.First(n => n.Row == MapConfig.Rows - 1).Id;
        Assert.True(MarchLogic.FindPath(L1, c.Map, c.Map.HomeGate, far) == null, "不能穿過迷霧走到遠方");
        Assert.Equal(MarchResult.Unreachable, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 1), far, T0));

        // 全部探索後，路徑一定避開封鎖力場
        var all = new MapState { Explored = L1.Nodes.Select(n => n.Id).ToList(), HomeGate = c.Map.HomeGate };
        var path = MarchLogic.FindPath(L1, all, all.HomeGate, far);
        Assert.NotNull(path, "全圖探索後走得到");
        for (int i = 1; i < path!.Count; i++)
            Assert.True(L1.EdgeBetween(path[i - 1], path[i]).Field != ForceField.Seal, "不經封鎖力場");
    }

    [Test]
    public static void Start_Validation()
    {
        var c = City();
        int dest = Frontier(c);
        Assert.Equal(MarchResult.NoTroops, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 0), dest, T0));
        Assert.Equal(MarchResult.NotEnoughTroops, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Crossbow, 1), dest, T0), "沒有鳴弦手");
        Assert.Equal(MarchResult.ExceedsLeadership, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 300), dest, T0), "超過統率");
        Assert.Equal(MarchResult.AlreadyThere, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 1), c.Map.HomeGate, T0));
        Assert.Equal(MarchResult.UnknownNode, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 1), 99999, T0));
        Assert.Equal(MarchResult.Ok, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 10), dest, T0));
        Assert.Equal(MarchResult.HeroBusy, MarchLogic.Start(c, HeroId.Hold, T(TroopType.Shield, 10), dest, T0), "主將已在行軍");
        MarchLogic.Start(c, HeroId.Vesper, T(TroopType.Shield, 10), dest, T0);
        MarchLogic.Start(c, HeroId.Karst, T(TroopType.Shield, 10), dest, T0);
        c.Heroes[(HeroId)4] = new HeroState { Id = (HeroId)4 };
        Assert.Equal(MarchResult.TooManyMarches, MarchLogic.Start(c, (HeroId)4, T(TroopType.Shield, 1), dest, T0), "最多 3 支");
    }

    [Test]
    public static void OldSave_GetsHomeGateFromFirstExplored()
    {
        var c = CityState.CreateNew(T0);
        c.Map.Explored.Add(L1.Gates[2]);
        c.Map.HomeGate = -1;
        Assert.True(MapLogic.EnsureStarted(c, "whoever"), "補上垂降口");
        Assert.Equal(L1.Gates[2], c.Map.HomeGate);
    }
}
