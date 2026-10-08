using System.Collections.Generic;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

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

        /// <summary>城內畫面的建築位置（格子左上角），見 CityLayout。</summary>
        public Dictionary<BuildingType, Cell> Layout { get; set; } = new Dictionary<BuildingType, Cell>();

        /// <summary>目前的升級工程（MVP 只有一條建築隊列）；null 代表空閒。</summary>
        public UpgradeJob Upgrade { get; set; }

        /// <summary>已訓練完成的部隊數量。</summary>
        public Dictionary<TroopType, long> Troops { get; set; } = new Dictionary<TroopType, long>();

        /// <summary>兵營訓練隊列（一條，與建築隊列分開）；null 代表空閒。</summary>
        public TrainingJob Training { get; set; }

        /// <summary>背包道具數量（加速道具等）。</summary>
        public Dictionary<ItemType, long> Items { get; set; } = new Dictionary<ItemType, long>();

        /// <summary>擁有的主將（MVP 先跟城市存在一起）。</summary>
        public Dictionary<HeroId, HeroState> Heroes { get; set; } = new Dictionary<HeroId, HeroState>();

        /// <summary>背包中的裝備（含穿在主將身上的）。</summary>
        public List<GearItem> Gear { get; set; } = new List<GearItem>();

        /// <summary>地表：主城位置。</summary>
        public SurfaceState Surface { get; set; } = new SurfaceState();

        /// <summary>舊存檔的深淵狀態（已停用）；登入時把行軍中的部隊還給兵營後清成 null。</summary>
        public LegacyMapState Map { get; set; }

        /// <summary>遠征進度與掛機收益。</summary>
        public ExpeditionState Expedition { get; set; } = new ExpeditionState();

        /// <summary>下一件裝備的流水號。</summary>
        public long NextGearUid { get; set; }

        public bool StarterGearGranted { get; set; }

        /// <summary>是否已發過新手道具（避免舊存檔重複補發）。</summary>
        public bool StarterItemsGranted { get; set; }

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

        public long GetTroops(TroopType type)
        {
            return Troops.TryGetValue(type, out var v) ? v : 0;
        }

        public long GetItemCount(ItemType type)
        {
            return Items.TryGetValue(type, out var v) ? v : 0;
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
            foreach (var t in CityConfig.AllTroops)
                s.Troops[t] = 0;
            CityLayout.EnsureLayout(s);
            CityLogic.GrantStarterItemsOnce(s);
            HeroLogic.EnsureStarterHeroes(s);
            GearLogic.EnsureStarterGear(s);
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

    public class TrainingJob
    {
        public TroopType Troop { get; set; }
        public long Count { get; set; }
        public long StartedAtMs { get; set; }
        public long FinishAtMs { get; set; }
    }
}
