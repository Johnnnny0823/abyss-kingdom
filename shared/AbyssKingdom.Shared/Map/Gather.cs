using System.Collections.Generic;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>採集的數值（測試用，之後統一調平衡）。</summary>
    public static class GatherConfig
    {
        /// <summary>每名士兵能背多少資源。</summary>
        public static long LoadPerTroop(TroopType t) =>
            t == TroopType.Shield ? 12 : t == TroopType.Crossbow ? 10 : 8;

        /// <summary>每分鐘採集量：Lv.1 300、Lv.2 450、Lv.3 600（測試用的快速值）。</summary>
        public static long RatePerMinute(int level) => 150 + 150 * level;

        /// <summary>資源田的存量：Lv.1 10,000、Lv.2 20,000、Lv.3 40,000。</summary>
        public static long FieldStock(int level) => 10_000L << (level - 1);

        /// <summary>採空之後多久重新長滿。</summary>
        public const long RespawnMs = 30 * 60_000;

        public static long Load(Dictionary<TroopType, long> troops)
        {
            long n = 0;
            foreach (var kv in troops) n += kv.Value * LoadPerTroop(kv.Key);
            return n;
        }
    }

    /// <summary>
    /// 全服的世界狀態：資源田（誰在採、剩多少）、怪物營地（是否被打掉）、戰鬥種子。
    /// 伺服器的 WorldService 實作；測試可以用 SimpleFieldRegistry。
    /// </summary>
    public interface IFieldRegistry
    {
        /// <summary>怪物營地此刻是否存在（被打掉後要等重生）。</summary>
        bool CampAvailable(int tile, long nowMs);

        /// <summary>營地被擊敗：開始重生倒數。</summary>
        void CampDefeated(int tile, long nowMs);

        /// <summary>戰鬥用的亂數種子（由伺服器決定，客戶端無法預知）。</summary>
        ulong NextSeed();

        /// <summary>這一格是哪位領主的主城（2×2 佔地內任一格都算）；沒有回傳 null。</summary>
        string CityOwnerAt(int tile);

        /// <summary>嘗試佔用資源田。成功回傳目前存量（&gt; 0）；已被別人佔用回傳 -1；採空了回傳 0。</summary>
        long Occupy(int tile, string owner, long marchId, long nowMs);

        /// <summary>採集結束（採滿或召回）：放開資源田並扣掉採走的量。</summary>
        void Release(int tile, string owner, long marchId, long gathered, long nowMs);
    }

    /// <summary>單機用的世界狀態（測試用）：存量一開始全滿、沒有其他玩家。</summary>
    public sealed class SimpleFieldRegistry : IFieldRegistry
    {
        private readonly Dictionary<int, long> _stock = new Dictionary<int, long>();
        private readonly Dictionary<int, (string owner, long march)> _taken = new Dictionary<int, (string, long)>();
        private readonly Dictionary<int, long> _campBack = new Dictionary<int, long>();
        private ulong _seed;

        public SimpleFieldRegistry(ulong seed = 1) { _seed = seed; }

        public bool CampAvailable(int tile, long nowMs) => !_campBack.TryGetValue(tile, out var back) || nowMs >= back;
        public void CampDefeated(int tile, long nowMs) => _campBack[tile] = nowMs + CampConfig.RespawnMs;
        public ulong NextSeed() => _seed++;

        private readonly Dictionary<int, string> _cities = new Dictionary<int, string>();

        /// <summary>登記一座主城（佔地左上角）。</summary>
        public void SetCity(int x, int y, string owner)
        {
            for (int dy = 0; dy < SurfaceConfig.CityFootprint; dy++)
                for (int dx = 0; dx < SurfaceConfig.CityFootprint; dx++)
                    _cities[SurfaceLogic.Tile(x + dx, y + dy)] = owner;
        }

        public string CityOwnerAt(int tile) => _cities.TryGetValue(tile, out var o) ? o : null;

        public long Stock(int tile)
        {
            if (_stock.TryGetValue(tile, out var s)) return s;
            var (x, y) = SurfaceLogic.FromTile(tile);
            var f = SurfaceLogic.FeatureAt(x, y);
            return f != null && f.Type == FeatureType.ResourceField ? GatherConfig.FieldStock(f.Level) : 0;
        }

        public long Occupy(int tile, string owner, long marchId, long nowMs)
        {
            if (_taken.TryGetValue(tile, out var t) && (t.owner != owner || t.march != marchId)) return -1;
            long s = Stock(tile);
            if (s <= 0) return 0;
            _taken[tile] = (owner, marchId);
            return s;
        }

        public void Release(int tile, string owner, long marchId, long gathered, long nowMs)
        {
            if (_taken.TryGetValue(tile, out var t) && t.owner == owner && t.march == marchId) _taken.Remove(tile);
            _stock[tile] = System.Math.Max(0, Stock(tile) - gathered);
        }
    }
}
