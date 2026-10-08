using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Heroes
{
    /// <summary>裝備欄位：每位主將 6 格。</summary>
    public enum EquipSlot
    {
        Weapon = 0,
        Head = 1,
        Body = 2,
        Hands = 3,
        Accessory = 4,
        Relic = 5,
    }

    /// <summary>裝備種類（顯示名稱在客戶端 DisplayNames.cs）。</summary>
    public enum GearId
    {
        RustAnchorBlade = 1,   // 鏽錨刃
        RopeHood = 2,          // 繩編兜帽
        StrataPlate = 3,       // 層岩胸甲
        EchoGloves = 4,        // 鳴指手套
        LanternCharm = 5,      // 燈魅護符
        BoneBellRelic = 6,     // 骨鈴遺物（遠征掉落）
        BoneBellSpear = 7,     // 骨鈴長矛（遠征掉落）
        HundredEyeRib = 8,     // 百眼傘骨（第一章守層者掉落）
    }

    public enum GearResult
    {
        Ok = 0,
        UnknownGear = 1,
        HeroNotOwned = 2,
        NotEquipped = 3,
    }

    public sealed class GearDef
    {
        public GearId Id;
        public EquipSlot Slot;
        public RopeGrade Grade;
        public long Attack, Defense, Hp, Leadership;
    }

    /// <summary>背包中的一件裝備（同種裝備可以有好幾件，用 Uid 區分）。</summary>
    public class GearItem
    {
        public long Uid { get; set; }
        public GearId Def { get; set; }
        /// <summary>穿在哪位主將身上；null = 在背包裡。</summary>
        public HeroId? EquippedBy { get; set; }
    }

    public static class GearConfig
    {
        public static readonly GearDef[] All =
        {
            new GearDef { Id = GearId.RustAnchorBlade, Slot = EquipSlot.Weapon, Grade = RopeGrade.Common, Attack = 30 },
            new GearDef { Id = GearId.RopeHood, Slot = EquipSlot.Head, Grade = RopeGrade.Common, Defense = 15, Hp = 150 },
            new GearDef { Id = GearId.StrataPlate, Slot = EquipSlot.Body, Grade = RopeGrade.Uncommon, Defense = 40, Hp = 300 },
            new GearDef { Id = GearId.EchoGloves, Slot = EquipSlot.Hands, Grade = RopeGrade.Uncommon, Attack = 25, Defense = 10 },
            new GearDef { Id = GearId.LanternCharm, Slot = EquipSlot.Accessory, Grade = RopeGrade.Uncommon, Hp = 200, Leadership = 20 },
            new GearDef { Id = GearId.BoneBellRelic, Slot = EquipSlot.Relic, Grade = RopeGrade.Rare, Attack = 30, Leadership = 40 },
            new GearDef { Id = GearId.BoneBellSpear, Slot = EquipSlot.Weapon, Grade = RopeGrade.Rare, Attack = 70 },
            new GearDef { Id = GearId.HundredEyeRib, Slot = EquipSlot.Relic, Grade = RopeGrade.Epic, Attack = 60, Defense = 30, Hp = 400, Leadership = 60 },
        };

        /// <summary>新帳號的起始裝備（每種一件）。</summary>
        public static readonly GearId[] Starter =
            { GearId.RustAnchorBlade, GearId.RopeHood, GearId.StrataPlate, GearId.EchoGloves, GearId.LanternCharm };

        static readonly Dictionary<GearId, GearDef> ById = new Dictionary<GearId, GearDef>();

        static GearConfig()
        {
            foreach (var d in All) ById[d.Id] = d;
        }

        public static bool TryGet(GearId id, out GearDef def) => ById.TryGetValue(id, out def);
    }

    public static class GearLogic
    {
        public static GearItem Grant(CityState city, GearId id)
        {
            var item = new GearItem { Uid = ++city.NextGearUid, Def = id };
            city.Gear.Add(item);
            return item;
        }

        public static bool EnsureStarterGear(CityState city)
        {
            if (city.StarterGearGranted) return false;
            foreach (var id in GearConfig.Starter) Grant(city, id);
            city.StarterGearGranted = true;
            return true;
        }

        public static GearItem Find(CityState city, long uid)
        {
            foreach (var g in city.Gear) if (g.Uid == uid) return g;
            return null;
        }

        /// <summary>穿上裝備：同欄位原本的裝備自動卸下；若這件穿在別的主將身上，會換過來。</summary>
        public static GearResult Equip(CityState city, long uid, HeroId hero)
        {
            var item = Find(city, uid);
            if (item == null || !GearConfig.TryGet(item.Def, out var def)) return GearResult.UnknownGear;
            if (!city.Heroes.ContainsKey(hero)) return GearResult.HeroNotOwned;

            foreach (var other in city.Gear)
            {
                if (other.EquippedBy != hero || other.Uid == uid) continue;
                if (GearConfig.TryGet(other.Def, out var od) && od.Slot == def.Slot) other.EquippedBy = null;
            }
            item.EquippedBy = hero;
            return GearResult.Ok;
        }

        public static GearResult Unequip(CityState city, long uid)
        {
            var item = Find(city, uid);
            if (item == null) return GearResult.UnknownGear;
            if (item.EquippedBy == null) return GearResult.NotEquipped;
            item.EquippedBy = null;
            return GearResult.Ok;
        }

        /// <summary>主將在某欄位穿的裝備（沒有則 null）。</summary>
        public static GearItem Equipped(CityState city, HeroId hero, EquipSlot slot)
        {
            foreach (var g in city.Gear)
                if (g.EquippedBy == hero && GearConfig.TryGet(g.Def, out var d) && d.Slot == slot) return g;
            return null;
        }
    }

    public static class PowerLogic
    {
        /// <summary>主將的總屬性 = 等級屬性 + 身上裝備。</summary>
        public static HeroStats TotalStats(CityState city, HeroId id)
        {
            if (!HeroConfig.TryGet(id, out var def) || !city.Heroes.TryGetValue(id, out var hero)) return default;
            var s = HeroConfig.Stats(def, hero.Level);
            foreach (var g in city.Gear)
            {
                if (g.EquippedBy != id || !GearConfig.TryGet(g.Def, out var gd)) continue;
                s.Attack += gd.Attack;
                s.Defense += gd.Defense;
                s.Hp += gd.Hp;
                s.Leadership += gd.Leadership;
            }
            return s;
        }

        /// <summary>戰力：給玩家看的單一數字，升級、換裝都會反映出來。</summary>
        public static long Power(HeroStats s) => s.Attack * 5 + s.Defense * 4 + s.Hp / 4 + s.Leadership * 2;

        public static long HeroPower(CityState city, HeroId id) => Power(TotalStats(city, id));

        public static long TotalPower(CityState city)
        {
            long sum = 0;
            foreach (var id in city.Heroes.Keys) sum += HeroPower(city, id);
            return sum;
        }
    }
}
