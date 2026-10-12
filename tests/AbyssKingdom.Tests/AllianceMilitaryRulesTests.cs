using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Tests;

public static class AllianceMilitaryRulesTests
{
    private static CityState City(int keep, long troops = 1_000)
    {
        var city = CityState.CreateNew(1_700_000_000_000);
        city.Buildings[BuildingType.Keep] = keep;
        foreach (var type in Enum.GetValues<TroopType>()) city.Troops[type] = troops;
        return city;
    }

    [Test]
    public static void Reinforcement_MixedTroopsUseLowerKeepCapacity()
    {
        var sender = City(4);
        var receiver = City(3);
        var mixed = new Dictionary<TroopType, long>
        {
            [TroopType.Shield] = 100, [TroopType.Crossbow] = 120, [TroopType.Rider] = 80
        };
        Assert.Equal(300L, AllianceMilitaryRules.PersonalCapacity(3));
        Assert.Equal(AllianceMilitaryResult.Ok,
            AllianceMilitaryRules.ValidateReinforcement(sender, receiver, mixed));
        mixed[TroopType.Rider] = 81;
        Assert.Equal(AllianceMilitaryResult.OverCapacity,
            AllianceMilitaryRules.ValidateReinforcement(sender, receiver, mixed));
        receiver.Buildings[BuildingType.Keep] = 4;
        Assert.Equal(AllianceMilitaryResult.Ok,
            AllianceMilitaryRules.ValidateReinforcement(sender, receiver, mixed));
        sender.Buildings[BuildingType.Keep] = 2;
        Assert.Equal(AllianceMilitaryResult.KeepLocked,
            AllianceMilitaryRules.ValidateReinforcement(sender, receiver, mixed));
    }

    [Test]
    public static void Capacity_RejectsFakeOrUnavailableTroopsWithoutOverflow()
    {
        var sender = City(3, 100);
        var receiver = City(3);
        Assert.Equal(AllianceMilitaryResult.EmptyTroops,
            AllianceMilitaryRules.ValidateReinforcement(sender, receiver, new Dictionary<TroopType, long>()));
        Assert.Equal(AllianceMilitaryResult.InvalidTroops,
            AllianceMilitaryRules.ValidateReinforcement(sender, receiver,
                new Dictionary<TroopType, long> { [(TroopType)999] = 1 }));
        Assert.Equal(AllianceMilitaryResult.InvalidTroops,
            AllianceMilitaryRules.ValidateReinforcement(sender, receiver,
                new Dictionary<TroopType, long> { [TroopType.Shield] = -1 }));
        Assert.Equal(AllianceMilitaryResult.NotEnoughTroops,
            AllianceMilitaryRules.ValidateReinforcement(sender, receiver,
                new Dictionary<TroopType, long> { [TroopType.Shield] = long.MaxValue }));
        Assert.Equal(AllianceMilitaryResult.Ok,
            AllianceMilitaryRules.ValidateRallyParticipant(sender,
                new Dictionary<TroopType, long> { [TroopType.Shield] = 100,
                    [TroopType.Crossbow] = 100, [TroopType.Rider] = 100 }));
        Assert.Equal(1_500L, AllianceMilitaryRules.RallyTotalCapacity(3));
    }
}
