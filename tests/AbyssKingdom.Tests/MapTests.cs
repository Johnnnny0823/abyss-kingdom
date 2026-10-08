using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class MapTests
{
    const long T0 = 1_700_000_000_000;
    static AbyssMap L1 => MapConfig.Layer(1);

    [Test]
    public static void Generation_IsDeterministic()
    {
        var a = MapGenerator.Generate(1, 12345);
        var b = MapGenerator.Generate(1, 12345);
        Assert.Equal(a.Nodes.Count, b.Nodes.Count);
        Assert.Equal(a.Edges.Count, b.Edges.Count);
        for (int i = 0; i < a.Nodes.Count; i++)
        {
            Assert.Equal(a.Nodes[i].X, b.Nodes[i].X, "節點座標");
            Assert.Equal(a.Nodes[i].Type, b.Nodes[i].Type, "節點種類");
        }
        Assert.True(MapGenerator.Generate(1, 999).Edges.Count != a.Edges.Count || MapGenerator.Generate(1, 999).Nodes[5].X != a.Nodes[5].X, "不同種子地圖不同");
    }

    [Test]
    public static void Layer1_HasExpectedFeatures()
    {
        var m = L1;
        Assert.True(m.Nodes.Count >= 150 && m.Nodes.Count <= 220, $"節點數合理（{m.Nodes.Count}）");
        Assert.Equal(4, m.Gates.Count, "4 個垂降口");
        Assert.Equal(1, m.Nodes.Count(n => n.Type == NodeType.GuardianLair), "1 個守層者巢穴");
        Assert.Equal(3, m.Nodes.Count(n => n.Type == NodeType.ShaftHub), "3 個升降井樞紐");
        Assert.Equal(10, m.Nodes.Count(n => n.Type == NodeType.Fissure), "10 個地脈裂隙");
        Assert.True(m.Nodes.Where(n => n.Type == NodeType.Fissure).All(n => n.Element != AbyssKingdom.Shared.Battle.Element.None), "裂隙都有淵脈");
        Assert.True(m.Edges.Any(e => e.Field == ForceField.Veil) && m.Edges.Any(e => e.Field == ForceField.Seal), "有遮蔽與封鎖力場");
    }

    [Test]
    public static void Layer1_IsTubularNotAGrid()
    {
        // 層網狀：平均每個節點只連 2~3 條廊道（棋盤格會接近 4）
        double avg = L1.Nodes.Average(n => n.Edges.Count);
        Assert.True(avg >= 2.0 && avg <= 3.0, $"平均連結數 {avg:F2}");
        int vertical = L1.Edges.Count(e => L1.Nodes[e.A].Column == L1.Nodes[e.B].Column);
        Assert.True(vertical * 2 > L1.Edges.Count, "多數廊道是垂直的");
    }

    [Test]
    public static void Layer1_AllReachableWithoutPassingSeals()
    {
        foreach (var g in L1.Gates)
            Assert.Equal(L1.Nodes.Count, L1.Reachable(g, throughSeals: false).Count, $"從垂降口 {g} 不經封鎖力場也能到達全圖");
    }

    [Test]
    public static void Start_AssignsAGate_Stably()
    {
        var a = CityState.CreateNew(T0);
        Assert.True(MapLogic.EnsureStarted(a, "player-1"), "第一次進入");
        Assert.Equal(1, a.Map.Explored.Count);
        Assert.True(L1.Gates.Contains(a.Map.Explored[0]), "起點是垂降口");
        Assert.True(!MapLogic.EnsureStarted(a, "player-1"), "不會重複");
        var b = CityState.CreateNew(T0);
        MapLogic.EnsureStarted(b, "player-1");
        Assert.Equal(a.Map.Explored[0], b.Map.Explored[0], "同一玩家永遠分到同一個垂降口");
        var gates = Enumerable.Range(0, 40).Select(i => { var c = CityState.CreateNew(T0); MapLogic.EnsureStarted(c, "p" + i); return c.Map.Explored[0]; }).Distinct().Count();
        Assert.True(gates >= 3, "玩家會分散到不同垂降口");
    }

    [Test]
    public static void Fog_ShowsNeighborsExceptBehindVeils()
    {
        var m = L1;
        var veil = m.Edges.First(e => e.Field == ForceField.Veil);
        var state = new MapState { Explored = { veil.A } };
        var vis = MapLogic.Visible(m, state);
        Assert.True(!vis.Contains(veil.B) || m.Nodes[veil.A].Edges.Any(ei => m.Edges[ei].Field != ForceField.Veil && m.Edges[ei].Other(veil.A) == veil.B), "遮蔽力場後方看不到");
        foreach (var ei in m.Nodes[veil.A].Edges)
            if (m.Edges[ei].Field != ForceField.Veil) Assert.True(vis.Contains(m.Edges[ei].Other(veil.A)), "無遮蔽的鄰居看得到");
    }
}
