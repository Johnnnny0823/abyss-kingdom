using System;
using System.Collections.Generic;

namespace AbyssKingdom.Shared.City
{
    /// <summary>
    /// 城建數值設定（MVP 先寫在程式裡，之後改成從 Excel 匯出的設定表）。
    /// 所有公式都是整數運算，伺服器與客戶端算出的結果必定一致。
    /// </summary>
    public static class CityConfig
    {
        public const int MaxLevel = 10;

        /// <summary>新帳號的初始資源。</summary>
        public const long StartingResource = 8000;

        /// <summary>倉庫 0 級時的每種資源上限，以及每級增加量。</summary>
        public const long BaseCapacity = 10000;
        public const long CapacityPerWarehouseLevel = 5000;

        /// <summary>生產建築每級每小時產量。</summary>
        public const long ProductionPerLevelPerHour = 1200;

        public const long MsPerHour = 3_600_000;

        static readonly Dictionary<BuildingType, ResourceType> Producers = new Dictionary<BuildingType, ResourceType>
        {
            { BuildingType.Sawmill, ResourceType.Wood },
            { BuildingType.Quarry, ResourceType.Stone },
            { BuildingType.Farm, ResourceType.Food },
            { BuildingType.CrystalMine, ResourceType.Crystal },
        };

        public static readonly BuildingType[] AllBuildings = (BuildingType[])Enum.GetValues(typeof(BuildingType));
        public static readonly ResourceType[] AllResources = (ResourceType[])Enum.GetValues(typeof(ResourceType));

        /// <summary>新帳號的初始建築等級：主堡與四種生產建築 1 級，其餘 0 級（未建造）。</summary>
        public static int StartingLevel(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Keep:
                case BuildingType.Sawmill:
                case BuildingType.Quarry:
                case BuildingType.Farm:
                case BuildingType.CrystalMine:
                    return 1;
                default:
                    return 0;
            }
        }

        public static bool TryGetProducedResource(BuildingType type, out ResourceType resource)
        {
            return Producers.TryGetValue(type, out resource);
        }

        /// <summary>每小時產量。</summary>
        public static long ProductionPerHour(int level)
        {
            return level <= 0 ? 0 : level * ProductionPerLevelPerHour;
        }

        public static long Capacity(int warehouseLevel)
        {
            return BaseCapacity + Math.Max(0, warehouseLevel) * CapacityPerWarehouseLevel;
        }

        /// <summary>
        /// 升到 targetLevel 所需的某種資源。基礎值 × 1.5^(目標等級-1)，以整數計算（×3/2 反覆）。
        /// 響晶只有主堡與響響晶脈以外的高階建築才需要，MVP 先簡化為主堡需要響晶。
        /// </summary>
        public static long UpgradeCost(BuildingType type, int targetLevel, ResourceType resource)
        {
            long baseCost = BaseCost(type, resource);
            if (baseCost == 0 || targetLevel <= 0) return 0;
            long cost = baseCost;
            for (int i = 1; i < targetLevel; i++) cost = cost * 3 / 2;
            return cost;
        }

        /// <summary>升到 targetLevel 所需時間（毫秒）。基礎秒數（主堡 240、其他 120）× 1.4^(目標等級-1)。2026-10-09 使用者定案的正式節奏（見 tools/NewbieSim）。</summary>
        public static long UpgradeDurationMs(BuildingType type, int targetLevel)
        {
            long seconds = type == BuildingType.Keep ? 240 : 120;
            for (int i = 1; i < targetLevel; i++) seconds = seconds * 7 / 5;
            return seconds * 1000;
        }

        // ───── 隊列 ─────

        /// <summary>取消建築／訓練時退還的資源比例（%）。</summary>
        public const long CancelRefundPercent = 50;

        /// <summary>剩餘時間在這之內可免費立即完成（5 分鐘）。</summary>
        public const long FreeSpeedupMs = 5 * 60_000;

        // ───── 傷兵與醫館（數值為測試用） ─────

        /// <summary>戰鬥倒下的士兵中，送進醫館的比例（%）；其餘陣亡。</summary>
        public const long WoundedPercent = 70;

        /// <summary>醫館容量：0 級也有 100（新手保護），每級 +200。</summary>
        public static long HospitalCapacity(int level) => 100 + 200L * Math.Max(0, level);

        /// <summary>治療每名傷兵的成本：訓練成本的一半。</summary>
        public static long HealCost(TroopType troop, ResourceType resource) => TroopCost(troop, resource) / 2;

        /// <summary>治療每名傷兵的時間：訓練時間的一半。</summary>
        public static long HealMsPerUnit(TroopType troop) => TroopTrainMsPerUnit(troop) / 2;

        // ───── 部隊（數值為測試用，之後統一調平衡） ─────

        public static readonly TroopType[] AllTroops = (TroopType[])Enum.GetValues(typeof(TroopType));

        /// <summary>每級兵營可增加的單批訓練上限。</summary>
        public const long TroopBatchPerBarracksLevel = 50;

        /// <summary>單批最多可訓練幾名（兵營 0 級 = 不能訓練）。</summary>
        public static long MaxTrainBatch(int barracksLevel)
        {
            return Math.Max(0, barracksLevel) * TroopBatchPerBarracksLevel;
        }

        /// <summary>每名士兵的訓練成本。</summary>
        public static long TroopCost(TroopType troop, ResourceType resource)
        {
            switch (troop)
            {
                case TroopType.Shield:
                    return resource == ResourceType.Food ? 20 : resource == ResourceType.Crystal ? 0 : 10;
                case TroopType.Crossbow:
                    return resource == ResourceType.Food || resource == ResourceType.Wood ? 20 : 0;
                case TroopType.Rider:
                    switch (resource)
                    {
                        case ResourceType.Food: return 30;
                        case ResourceType.Stone: return 10;
                        case ResourceType.Crystal: return 5;
                        default: return 0;
                    }
                default:
                    return 0;
            }
        }

        /// <summary>每名士兵的訓練時間（毫秒）。</summary>
        public static long TroopTrainMsPerUnit(TroopType troop)
        {
            return troop == TroopType.Rider ? 3000 : 2000;
        }

        static long BaseCost(BuildingType type, ResourceType resource)
        {
            switch (type)
            {
                case BuildingType.Keep:
                    return resource == ResourceType.Crystal ? 100 : 400;
                case BuildingType.Warehouse:
                    return resource == ResourceType.Wood || resource == ResourceType.Stone ? 300 : 0;
                case BuildingType.Barracks:
                    return resource == ResourceType.Crystal ? 0 : 250;
                case BuildingType.Sawmill:
                    return resource == ResourceType.Stone || resource == ResourceType.Food ? 150 : 0;
                case BuildingType.Quarry:
                    return resource == ResourceType.Wood || resource == ResourceType.Food ? 150 : 0;
                case BuildingType.Farm:
                    return resource == ResourceType.Wood || resource == ResourceType.Stone ? 150 : 0;
                case BuildingType.CrystalMine:
                    return resource == ResourceType.Crystal ? 0 : 200;
                case BuildingType.Hospital:
                    return resource == ResourceType.Crystal ? 0 : 200;
                default:
                    return 0;
            }
        }
    }
}
