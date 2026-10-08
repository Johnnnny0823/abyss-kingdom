using System.Collections.Generic;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>2026-10-08 以前的深淵狀態（深淵改成大陸後停用）。只讀出行軍中的部隊，好還給兵營。</summary>
    public class LegacyMapState
    {
        public List<LegacyMarch> Marches { get; set; } = new List<LegacyMarch>();
    }

    public class LegacyMarch
    {
        public Dictionary<TroopType, long> Troops { get; set; } = new Dictionary<TroopType, long>();
    }

    public static class LegacyMigration
    {
        /// <summary>舊深淵行軍的部隊還給兵營，然後清掉舊狀態。有改動時回傳 true。</summary>
        public static bool Run(CityState city)
        {
            if (city.Map == null) return false;
            foreach (var m in city.Map.Marches ?? new List<LegacyMarch>())
                foreach (var kv in m.Troops ?? new Dictionary<TroopType, long>())
                    city.Troops[kv.Key] = city.GetTroops(kv.Key) + kv.Value;
            city.Map = null;
            return true;
        }
    }
}
