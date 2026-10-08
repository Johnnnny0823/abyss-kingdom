using System;
using System.Collections.Generic;

namespace AbyssKingdom.Shared.City
{
    /// <summary>城市裡的兩條隊列。</summary>
    public enum QueueType
    {
        Building = 0,
        Training = 1,
    }

    public enum CancelResult
    {
        Ok = 0,
        NothingToCancel = 1,
        UnknownQueue = 2,
    }

    public enum SpeedupResult
    {
        Ok = 0,
        NothingInQueue = 1,
        NotFreeYet = 2,
        NotEnoughItems = 3,
        ItemNotApplicable = 4,
        InvalidCount = 5,
        UnknownItem = 6,
        UnknownQueue = 7,
    }

    /// <summary>加速道具的類別。「科研」「魔導」系統尚未實作，先預留類別（目前沒有可套用的隊列）。</summary>
    public enum ItemCategory
    {
        General = 0,   // 通用：任何隊列
        Building = 1,  // 建築
        Research = 2,  // 科研（預留）
        Magic = 3,     // 魔導（預留）
        Training = 4,  // 訓練
    }

    /// <summary>
    /// 道具。加速道具的編號規則：類別 × 100 + 分鐘數（例：SpeedupBuilding30m = 1×100 + 30 = 130）。
    /// 之後新增其他道具請用 1000 以上的編號，避免被當成加速道具。
    /// </summary>
    public enum ItemType
    {
        SpeedupGeneral1m = 1, SpeedupGeneral3m = 3, SpeedupGeneral5m = 5, SpeedupGeneral30m = 30, SpeedupGeneral60m = 60,
        SpeedupBuilding1m = 101, SpeedupBuilding3m = 103, SpeedupBuilding5m = 105, SpeedupBuilding30m = 130, SpeedupBuilding60m = 160,
        SpeedupResearch1m = 201, SpeedupResearch3m = 203, SpeedupResearch5m = 205, SpeedupResearch30m = 230, SpeedupResearch60m = 260,
        SpeedupMagic1m = 301, SpeedupMagic3m = 303, SpeedupMagic5m = 305, SpeedupMagic30m = 330, SpeedupMagic60m = 360,
        SpeedupTraining1m = 401, SpeedupTraining3m = 403, SpeedupTraining5m = 405, SpeedupTraining30m = 430, SpeedupTraining60m = 460,

        // 主將經驗書（1000 以上：非加速道具）
        HeroExpSmall = 1001, HeroExpMedium = 1002, HeroExpLarge = 1003,

        // 索譜：定向補四股索修練值（材料為輔）
        DomainScrollPower = 1101, DomainScrollTechnique = 1102, DomainScrollSpeed = 1103, DomainScrollDurability = 1104,

        // 主將信物：升星材料。1200 = 通用；1200 + HeroId = 該主將專屬
        HeroShardAny = 1200, HeroShardHold = 1201, HeroShardVesper = 1202, HeroShardKarst = 1203,
    }

    public static class ItemConfig
    {
        public const long MsPerMinute = 60_000;

        public static readonly ItemType[] AllItems = (ItemType[])Enum.GetValues(typeof(ItemType));

        /// <summary>是否為加速道具；是的話回傳類別與可減少的毫秒數。</summary>
        public static bool TryGetSpeedup(ItemType item, out ItemCategory category, out long ms)
        {
            int id = (int)item;
            category = (ItemCategory)(id / 100);
            ms = (id % 100) * MsPerMinute;
            return Enum.IsDefined(typeof(ItemType), item) && id < 1000;
        }

        /// <summary>是否為主將經驗書；是的話回傳每本給多少經驗。</summary>
        public static bool TryGetHeroExp(ItemType item, out long exp)
        {
            switch (item)
            {
                case ItemType.HeroExpSmall: exp = 100; return true;
                case ItemType.HeroExpMedium: exp = 1_000; return true;
                case ItemType.HeroExpLarge: exp = 10_000; return true;
                default: exp = 0; return false;
            }
        }

        /// <summary>是否為索譜；是的話回傳對應的境與每本修練值。</summary>
        public static bool TryGetDomainScroll(ItemType item, out Heroes.Domain domain, out long xp)
        {
            xp = 100;
            switch (item)
            {
                case ItemType.DomainScrollPower: domain = Heroes.Domain.Power; return true;
                case ItemType.DomainScrollTechnique: domain = Heroes.Domain.Technique; return true;
                case ItemType.DomainScrollSpeed: domain = Heroes.Domain.Speed; return true;
                case ItemType.DomainScrollDurability: domain = Heroes.Domain.Durability; return true;
                default: domain = default; xp = 0; return false;
            }
        }

        /// <summary>某位主將的專屬信物。</summary>
        public static ItemType ShardFor(Heroes.HeroId hero) => (ItemType)(1200 + (int)hero);

        /// <summary>某類加速道具能不能用在某條隊列。</summary>
        public static bool CanApply(ItemCategory category, QueueType queue)
        {
            switch (category)
            {
                case ItemCategory.General: return true;
                case ItemCategory.Building: return queue == QueueType.Building;
                case ItemCategory.Training: return queue == QueueType.Training;
                default: return false; // 科研、魔導：對應系統尚未實作
            }
        }

        /// <summary>新帳號的新手道具（之後主要來源會是任務／活動獎勵）。</summary>
        public static readonly KeyValuePair<ItemType, long>[] StarterItems =
        {
            new KeyValuePair<ItemType, long>(ItemType.SpeedupGeneral1m, 10),
            new KeyValuePair<ItemType, long>(ItemType.SpeedupGeneral5m, 5),
            new KeyValuePair<ItemType, long>(ItemType.SpeedupGeneral30m, 2),
            new KeyValuePair<ItemType, long>(ItemType.SpeedupGeneral60m, 1),
            new KeyValuePair<ItemType, long>(ItemType.SpeedupBuilding30m, 3),
            new KeyValuePair<ItemType, long>(ItemType.SpeedupTraining30m, 3),
        };
    }
}
