using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Tests;

public static class GearTests
{
    const long T0 = 1_700_000_000_000;

    static GearItem Gear(CityState c, GearId id) => c.Gear.First(g => g.Def == id);

    [Test]
    public static void NewCity_HasStarterGearInBag()
    {
        var c = CityState.CreateNew(T0);
        Assert.Equal(GearConfig.Starter.Length, c.Gear.Count);
        Assert.True(c.Gear.All(g => g.EquippedBy == null), "起始裝備在背包裡");
        Assert.Equal(c.Gear.Count, c.Gear.Select(g => g.Uid).Distinct().Count(), "Uid 不重複");
    }

    [Test]
    public static void Equip_RaisesStatsAndPower()
    {
        var c = CityState.CreateNew(T0);
        long before = PowerLogic.HeroPower(c, HeroId.Hold);
        var blade = Gear(c, GearId.RustAnchorBlade);
        Assert.Equal(GearResult.Ok, GearLogic.Equip(c, blade.Uid, HeroId.Hold));
        Assert.Equal(HeroConfig.Stats(HeroConfig.All[0], 1).Attack + 30, PowerLogic.TotalStats(c, HeroId.Hold).Attack, "攻擊 +30");
        Assert.Equal(before + 30 * 5, PowerLogic.HeroPower(c, HeroId.Hold), "戰力 +150");
    }

    [Test]
    public static void Equip_SameSlotReplacesOld()
    {
        var c = CityState.CreateNew(T0);
        var blade = Gear(c, GearId.RustAnchorBlade);
        var spear = GearLogic.Grant(c, GearId.BoneBellSpear);
        GearLogic.Equip(c, blade.Uid, HeroId.Hold);
        GearLogic.Equip(c, spear.Uid, HeroId.Hold);
        Assert.True(blade.EquippedBy == null, "舊武器回到背包");
        Assert.Equal(spear.Uid, Assert.NotNull(GearLogic.Equipped(c, HeroId.Hold, EquipSlot.Weapon), "武器欄").Uid);
    }

    [Test]
    public static void Equip_MovesGearBetweenHeroes()
    {
        var c = CityState.CreateNew(T0);
        var hood = Gear(c, GearId.RopeHood);
        GearLogic.Equip(c, hood.Uid, HeroId.Hold);
        GearLogic.Equip(c, hood.Uid, HeroId.Karst);
        Assert.True(GearLogic.Equipped(c, HeroId.Hold, EquipSlot.Head) == null, "霍德不再穿");
        Assert.Equal(HeroId.Karst, hood.EquippedBy!.Value);
    }

    [Test]
    public static void Unequip_AndRejections()
    {
        var c = CityState.CreateNew(T0);
        var charm = Gear(c, GearId.LanternCharm);
        Assert.Equal(GearResult.NotEquipped, GearLogic.Unequip(c, charm.Uid));
        GearLogic.Equip(c, charm.Uid, HeroId.Vesper);
        Assert.Equal(GearResult.Ok, GearLogic.Unequip(c, charm.Uid));
        Assert.Equal(GearResult.UnknownGear, GearLogic.Equip(c, 999, HeroId.Vesper));
        c.Heroes.Remove(HeroId.Vesper);
        Assert.Equal(GearResult.HeroNotOwned, GearLogic.Equip(c, charm.Uid, HeroId.Vesper));
    }

    [Test]
    public static void LevelUp_RaisesPower()
    {
        var c = CityState.CreateNew(T0);
        long before = PowerLogic.HeroPower(c, HeroId.Karst);
        HeroLogic.UseExpItem(c, HeroId.Karst, ItemType.HeroExpSmall, 1); // 100 經驗 → Lv2
        Assert.True(PowerLogic.HeroPower(c, HeroId.Karst) > before, "升級後戰力上升");
    }

    [Test]
    public static void TotalPower_CountsHeroesTroopsAndBuildings()
    {
        var c = CityState.CreateNew(T0);
        long before = PowerLogic.TotalPower(c);
        Assert.Equal(PowerLogic.HeroesPower(c) + PowerLogic.TroopsPower(c) + PowerLogic.BuildingsPower(c), before);

        c.Troops[TroopType.Rider] = 100;
        Assert.Equal(before + 100 * PowerLogic.TroopUnitPower(TroopType.Rider), PowerLogic.TotalPower(c), "訓練完成的部隊計入總戰力");

        long beforeBuild = PowerLogic.TotalPower(c);
        c.Buildings[BuildingType.Keep] = 2; // 50×1² → 50×2²
        Assert.Equal(beforeBuild + 150, PowerLogic.TotalPower(c), "建築升級計入總戰力");
    }

    [Test]
    public static void TroopUnitPower_IsPositiveForAllTypes()
    {
        foreach (var t in CityConfig.AllTroops) Assert.True(PowerLogic.TroopUnitPower(t) > 0, t.ToString());
    }

    [Test]
    public static void OldSave_GetsStarterGearOnce()
    {
        var c = CityState.CreateNew(T0);
        c.Gear.Clear();
        c.StarterGearGranted = false;
        Assert.True(GearLogic.EnsureStarterGear(c), "補發");
        Assert.True(!GearLogic.EnsureStarterGear(c), "不重複");
        Assert.Equal(GearConfig.Starter.Length, c.Gear.Count);
    }
}
