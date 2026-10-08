using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Heroes
{
    /// <summary>主將。顯示名稱與介紹在客戶端 DisplayNames.cs；這裡只是程式識別碼。</summary>
    public enum HeroId
    {
        Hold = 1,    // 鐵錘同盟的老盾衛隊長（沉錨衛專精）
        Vesper = 2,  // 銀繩公會的探路射手（鳴弦手專精）
        Karst = 3,   // 自由探窟團的索騎（懸索騎專精）
    }

    /// <summary>
    /// 繩級：主將品質（2026-10-08 使用者定案 6 階）。初始主將都是罕見（綠繩）。
    /// 白繩 普通 → 綠繩 罕見 → 藍繩 稀有 → 紫繩 史詩 → 金繩 傳說 → 黑繩 神秘。
    /// </summary>
    public enum RopeGrade
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4,
        Mythic = 5,
    }

    public enum HeroResult
    {
        Ok = 0,
        UnknownHero = 1,
        HeroNotOwned = 2,
        NotExpItem = 3,
        InvalidCount = 4,
        NotEnoughItems = 5,
        MaxLevel = 6,
    }

    /// <summary>主將定義（固定資料）。數值為整數；成長 = 每升一級增加多少。</summary>
    public sealed class HeroDef
    {
        public HeroId Id;
        public RopeGrade Grade;
        public TroopType Specialty;
        public long BaseAttack, BaseDefense, BaseHp;
        public long AttackGrowth, DefenseGrowth, HpGrowth;
    }

    /// <summary>某位主將在某個等級的屬性。</summary>
    public struct HeroStats
    {
        public long Attack, Defense, Hp, Leadership;
    }

    /// <summary>玩家擁有的一位主將（存在城市存檔裡）。</summary>
    public class HeroState
    {
        public HeroId Id { get; set; }
        public int Level { get; set; } = 1;
        /// <summary>目前等級已累積的經驗（升級後歸零重算）。</summary>
        public long Exp { get; set; }
    }

    public static class HeroConfig
    {
        public const int MaxLevel = 30;

        public static readonly HeroDef[] All =
        {
            new HeroDef { Id = HeroId.Hold, Grade = RopeGrade.Uncommon, Specialty = TroopType.Shield,
                BaseAttack = 80, BaseDefense = 140, BaseHp = 1600, AttackGrowth = 8, DefenseGrowth = 14, HpGrowth = 160 },
            new HeroDef { Id = HeroId.Vesper, Grade = RopeGrade.Uncommon, Specialty = TroopType.Crossbow,
                BaseAttack = 150, BaseDefense = 70, BaseHp = 1100, AttackGrowth = 16, DefenseGrowth = 7, HpGrowth = 110 },
            new HeroDef { Id = HeroId.Karst, Grade = RopeGrade.Uncommon, Specialty = TroopType.Rider,
                BaseAttack = 120, BaseDefense = 100, BaseHp = 1300, AttackGrowth = 12, DefenseGrowth = 10, HpGrowth = 130 },
        };

        static readonly Dictionary<HeroId, HeroDef> ById = new Dictionary<HeroId, HeroDef>();

        static HeroConfig()
        {
            foreach (var d in All) ById[d.Id] = d;
        }

        public static bool TryGet(HeroId id, out HeroDef def) => ById.TryGetValue(id, out def);

        /// <summary>升到下一級所需經驗：50 × 等級²（1→2 要 50，10→11 要 5,000）。數值待封測調整。</summary>
        public static long ExpToNext(int level) => 50L * level * level;

        /// <summary>統率：一支行軍可帶的兵力。基礎 200，每級 +20，品質每階 +50。</summary>
        public static long Leadership(RopeGrade grade, int level) => 200 + 20L * (level - 1) + 50L * (int)grade;

        public static HeroStats Stats(HeroDef def, int level)
        {
            long n = Math.Max(0, level - 1);
            return new HeroStats
            {
                Attack = def.BaseAttack + def.AttackGrowth * n,
                Defense = def.BaseDefense + def.DefenseGrowth * n,
                Hp = def.BaseHp + def.HpGrowth * n,
                Leadership = Leadership(def.Grade, level),
            };
        }
    }

    public static class HeroLogic
    {
        /// <summary>新帳號（或加入主將前的舊存檔）發放 3 位初始主將與經驗書。回傳這次是否有發放。</summary>
        public static bool EnsureStarterHeroes(CityState city)
        {
            if (city.Heroes.Count > 0) return false;
            foreach (var def in HeroConfig.All)
                city.Heroes[def.Id] = new HeroState { Id = def.Id };
            city.Items[ItemType.HeroExpSmall] = city.GetItemCount(ItemType.HeroExpSmall) + 20;
            city.Items[ItemType.HeroExpMedium] = city.GetItemCount(ItemType.HeroExpMedium) + 5;
            return true;
        }

        /// <summary>加經驗並自動升級；滿級後多餘經驗捨去。</summary>
        public static void AddExp(HeroState hero, long exp)
        {
            hero.Exp += exp;
            while (hero.Level < HeroConfig.MaxLevel && hero.Exp >= HeroConfig.ExpToNext(hero.Level))
            {
                hero.Exp -= HeroConfig.ExpToNext(hero.Level);
                hero.Level++;
            }
            if (hero.Level >= HeroConfig.MaxLevel) hero.Exp = 0;
        }

        /// <summary>使用經驗書。滿級時拒絕，避免浪費道具。</summary>
        public static HeroResult UseExpItem(CityState city, HeroId id, ItemType item, long count)
        {
            if (!HeroConfig.TryGet(id, out _)) return HeroResult.UnknownHero;
            if (!city.Heroes.TryGetValue(id, out var hero)) return HeroResult.HeroNotOwned;
            if (!ItemConfig.TryGetHeroExp(item, out var expEach)) return HeroResult.NotExpItem;
            if (count <= 0 || count > 1000) return HeroResult.InvalidCount;
            if (city.GetItemCount(item) < count) return HeroResult.NotEnoughItems;
            if (hero.Level >= HeroConfig.MaxLevel) return HeroResult.MaxLevel;

            city.Items[item] = city.GetItemCount(item) - count;
            AddExp(hero, expEach * count);
            return HeroResult.Ok;
        }
    }
}
