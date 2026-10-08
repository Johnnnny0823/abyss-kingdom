using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Tests;

public static class SurfaceTests
{
    static AbyssMap S => SurfaceConfig.Map();

    [Test]
    public static void Generation_IsDeterministic()
    {
        var a = SurfaceGenerator.Generate(SurfaceConfig.Seed);
        var b = SurfaceGenerator.Generate(SurfaceConfig.Seed);
        Assert.Equal(a.Edges.Count, b.Edges.Count);
        for (int i = 0; i < a.Edges.Count; i++)
        {
            Assert.Equal(a.Edges[i].A, b.Edges[i].A, "道路端點");
            Assert.Equal(a.Edges[i].Length, b.Edges[i].Length, "路程（整數）");
        }
        for (int i = 0; i < a.Nodes.Count; i++) Assert.Equal(a.Nodes[i].Type, b.Nodes[i].Type, "節點種類");
    }

    [Test]
    public static void Rings_AndZones()
    {
        int expected = Enumerable.Range(0, SurfaceConfig.Rings).Sum(SurfaceConfig.NodesInRing);
        Assert.Equal(expected, S.Nodes.Count, "節點數 = 各圈加總");
        Assert.Equal(4, S.Gates.Count, "崖緣 4 個垂降口");
        Assert.True(S.Gates.All(g => SurfaceLogic.Zone(S.Nodes[g]) == SurfaceZone.Rim), "垂降口都在崖緣");
        int sites = S.Nodes.Count(n => n.Type == NodeType.CitySite);
        Assert.True(sites >= 120, $"主城用地足夠（{sites}）");
        Assert.True(S.Nodes.Count(n => n.Type == NodeType.CitySite && SurfaceLogic.Zone(n) == SurfaceZone.Outer) >= 80, "外環有足夠的新手用地");
        Assert.True(S.Nodes.Where(n => n.Type == NodeType.ResourceField).All(n => n.Level >= 1 && n.Level <= 3), "資源田有等級");
        Assert.True(S.Nodes.Where(n => n.Type == NodeType.ResourceField && SurfaceLogic.Zone(n) == SurfaceZone.Rim).All(n => n.Level == 3), "崖緣資源田最高級");
    }

    [Test]
    public static void ZonesConnectOnlyThroughPasses()
    {
        foreach (var e in S.Edges)
        {
            var a = S.Nodes[e.A];
            var b = S.Nodes[e.B];
            if (SurfaceLogic.Zone(a) == SurfaceLogic.Zone(b)) continue;
            Assert.True(a.Type == NodeType.Pass && b.Type == NodeType.Pass, $"跨區道路 {a.Id}-{b.Id} 必須兩端都是關隘");
        }
        Assert.Equal(SurfaceConfig.PassesPerBoundary * 2 * 2, S.Nodes.Count(n => n.Type == NodeType.Pass), "兩條邊界 × 6 個關隘 × 兩側");
    }

    [Test]
    public static void Surface_IsFullyConnected()
    {
        Assert.Equal(S.Nodes.Count, S.Reachable(0, throughSeals: true).Count);
        Assert.True(S.Edges.All(e => e.Field == ForceField.None), "地表沒有力場");
        double avg = S.Nodes.Average(n => n.Edges.Count);
        Assert.True(avg >= 2.5 && avg <= 4.0, $"開闊路網（平均 {avg:F2} 條路）");
    }

    [Test]
    public static void PickSite_PrefersOuterAndSkipsOccupied()
    {
        var occupied = new HashSet<int>();
        int first = SurfaceLogic.PickSite(S, occupied, n => 0);
        Assert.Equal(NodeType.CitySite, S.Nodes[first].Type);
        Assert.Equal(SurfaceZone.Outer, SurfaceLogic.Zone(S.Nodes[first]), "新手在外環");
        occupied.Add(first);
        int second = SurfaceLogic.PickSite(S, occupied, n => 0);
        Assert.True(second != first, "不會分到同一塊地");

        // 外環滿了就往中環
        foreach (var n in S.Nodes.Where(n => n.Type == NodeType.CitySite && SurfaceLogic.Zone(n) == SurfaceZone.Outer)) occupied.Add(n.Id);
        Assert.Equal(SurfaceZone.Middle, SurfaceLogic.Zone(S.Nodes[SurfaceLogic.PickSite(S, occupied, n => 0)]));
    }
}
