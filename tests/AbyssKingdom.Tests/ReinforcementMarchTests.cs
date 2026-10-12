using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Tests;

public static class ReinforcementMarchTests
{
    private const long Now = 1_700_000_000_000;
    private static (CityState sender, CityState receiver) Cities()
    {
        var sites = SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer).Take(2).ToArray();
        CityState At(int i)
        {
            var c = CityState.CreateNew(Now);
            c.Buildings[BuildingType.Keep] = 3;
            c.Surface.X = sites[i].x;
            c.Surface.Y = sites[i].y;
            foreach (var type in CityConfig.AllTroops) c.Troops[type] = 1_000;
            return c;
        }
        return (At(0), At(1));
    }
    private static Dictionary<TroopType, long> Mixed() => new()
    {
        [TroopType.Shield] = 100, [TroopType.Crossbow] = 120, [TroopType.Rider] = 80
    };

    [Test]
    public static void Dispatch_MixedCapacityDeductsRealTroopsAndKeepsShield()
    {
        var (sender, receiver) = Cities();
        long shield = sender.ShieldUntilMs;
        Assert.Equal(AllianceMilitaryResult.Ok, MarchLogic.StartReinforcement(sender, receiver,
            "ally", HeroId.Hold, Mixed(), Now, out long id));
        Assert.Equal(1L, id);
        var m = sender.Surface.Marches.Single();
        Assert.Equal("ally", m.ReinforcementTarget);
        Assert.Equal(300L, m.TotalTroops());
        Assert.Equal(900L, sender.GetTroops(TroopType.Shield));
        Assert.Equal(880L, sender.GetTroops(TroopType.Crossbow));
        Assert.Equal(920L, sender.GetTroops(TroopType.Rider));
        Assert.Equal(shield, sender.ShieldUntilMs);
        Assert.Equal(1_000L, receiver.GetTroops(TroopType.Shield));
        Assert.Equal(AllianceMilitaryResult.HeroBusy, MarchLogic.StartReinforcement(sender, receiver,
            "ally", HeroId.Hold, Mixed(), Now, out _));
    }

    [Test]
    public static void Arrival_NeverBecomesAnAttackAndRecallReturnsOnce()
    {
        var (sender, receiver) = Cities();
        MarchLogic.StartReinforcement(sender, receiver, "ally", HeroId.Hold, Mixed(), Now, out long id);
        var m = sender.Surface.Marches.Single();
        var fields = new AlliedCity();
        var events = MarchLogic.Advance(sender, m.ArriveAt, fields, "sender");
        Assert.Equal(1, events.Count);
        Assert.Equal("ally", events[0].ReinforcementTarget);
        Assert.True(m.Stationed && !m.Sieging && events[0].AttackTarget == null, "援軍不能變成攻城");
        Assert.Equal(0, MarchLogic.Advance(sender, m.ArriveAt + 1, fields, "sender").Count);
        Assert.Equal(MarchResult.Ok, MarchLogic.Recall(sender, id, m.ArriveAt + 1, fields, "sender"));
        long back = m.ArriveAt;
        Assert.True(MarchLogic.Advance(sender, back, fields, "sender").Single().Disbanded, "回到兵營");
        Assert.Equal(1_000L, sender.GetTroops(TroopType.Shield));
        Assert.Equal(1_000L, sender.GetTroops(TroopType.Crossbow));
        Assert.Equal(1_000L, sender.GetTroops(TroopType.Rider));
        Assert.Equal(0, MarchLogic.Advance(sender, back + 1, fields, "sender").Count);
        Assert.Equal(1_000L, sender.GetTroops(TroopType.Shield));
    }

    [Test]
    public static void RejectedDispatch_DoesNotDeductOrOccupyMarch()
    {
        var (sender, receiver) = Cities();
        var tooMany = Mixed();
        tooMany[TroopType.Shield]++;
        Assert.Equal(AllianceMilitaryResult.OverCapacity, MarchLogic.StartReinforcement(sender, receiver,
            "ally", HeroId.Hold, tooMany, Now, out long id));
        Assert.Equal(0L, id);
        Assert.Equal(0, sender.Surface.Marches.Count);
        Assert.Equal(1_000L, sender.GetTroops(TroopType.Shield));
        Assert.Equal(AllianceMilitaryResult.InvalidTarget, MarchLogic.StartReinforcement(sender, sender,
            "self", HeroId.Hold, Mixed(), Now, out _));
        Assert.Equal(AllianceMilitaryResult.InvalidTarget, MarchLogic.StartReinforcement(sender, receiver,
            "", HeroId.Hold, Mixed(), Now, out _));
        Assert.Equal(AllianceMilitaryResult.HeroNotOwned, MarchLogic.StartReinforcement(sender, receiver,
            "ally", (HeroId)999, Mixed(), Now, out _));
    }

    [Test]
    public static void FrozenGarrison_CannotRecallUntilSettlementFinishes()
    {
        var (sender, receiver) = Cities();
        MarchLogic.StartReinforcement(sender, receiver, "ally", HeroId.Hold, Mixed(), Now, out long id);
        var m = sender.Surface.Marches.Single();
        MarchLogic.Advance(sender, m.ArriveAt);
        m.GarrisonSettlementId = "battle:1";
        Assert.Equal(MarchResult.Sieging, MarchLogic.Recall(sender, id, m.ArriveAt + 1));
        Assert.True(!m.Returning, "凍結時不能提前歸還部隊");
        m.GarrisonSettlementId = null;
        Assert.Equal(MarchResult.Ok, MarchLogic.Recall(sender, id, m.ArriveAt + 2));
    }

    private sealed class AlliedCity : IFieldRegistry
    {
        public string CityOwnerAt(int tile) => "ally";
        public bool CampAvailable(int tile, long nowMs) => throw new Exception("Not a camp");
        public bool TryDefeatCamp(int tile, long nowMs) => throw new Exception("Not a camp");
        public ulong NextSeed() => throw new Exception("Not a battle");
        public long Occupy(int tile, string owner, long marchId, long nowMs) => throw new Exception("Not gathering");
        public void Release(int tile, string owner, long marchId, long gathered, long nowMs) => throw new Exception("Not gathering");
    }
}
