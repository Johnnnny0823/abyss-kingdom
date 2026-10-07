using System.Collections.Generic;

namespace AbyssKingdom.Shared.City
{
    /// <summary>
    /// 一位玩家的城市狀態。這是會被存進資料庫（JSON）並傳給客戶端的資料。
    /// 時間一律是 Unix 毫秒（UTC）。
    /// </summary>
    public class CityState
    {
        public Dictionary<ResourceType, long> Resources { get; set; } = new Dictionary<ResourceType, long>();

        /// <summary>產量的小數餘數（單位：資源 × 毫秒 / 小時），避免每次結算捨去造成資源流失。</summary>
        public Dictionary<ResourceType, long> ProductionRemainder { get; set; } = new Dictionary<ResourceType, long>();

        public Dictionary<BuildingType, int> Buildings { get; set; } = new Dictionary<BuildingType, int>();

        /// <summary>目前的升級工程（MVP 只有一條建築隊列）；null 代表空閒。</summary>
        public UpgradeJob Upgrade { get; set; }

        /// <summary>資源最後結算到的時間。</summary>
        public long LastSettledAtMs { get; set; }

        public long GetResource(ResourceType type)
        {
            return Resources.TryGetValue(type, out var v) ? v : 0;
        }

        public int GetLevel(BuildingType type)
        {
            return Buildings.TryGetValue(type, out var v) ? v : 0;
        }

        public static CityState CreateNew(long nowMs)
        {
            var s = new CityState { LastSettledAtMs = nowMs };
            foreach (var r in CityConfig.AllResources)
            {
                s.Resources[r] = CityConfig.StartingResource;
                s.ProductionRemainder[r] = 0;
            }
            foreach (var b in CityConfig.AllBuildings)
                s.Buildings[b] = CityConfig.StartingLevel(b);
            return s;
        }
    }

    public class UpgradeJob
    {
        public BuildingType Building { get; set; }
        public int TargetLevel { get; set; }
        public long StartedAtMs { get; set; }
        public long FinishAtMs { get; set; }
    }
}
