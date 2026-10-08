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

        /// <summary>每分鐘採集量：Lv.1 60、Lv.2 90、Lv.3 120。</summary>
        public static long RatePerMinute(int level) => 30 + 30 * level;

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
    /// 全服的資源田狀態（誰在採、剩多少）。伺服器的 WorldService 實作；測試可以用簡單的假實作。
    /// </summary>
    public interface IFieldRegistry
    {
        /// <summary>嘗試佔用資源田。成功回傳目前存量（&gt; 0）；已被別人佔用回傳 -1；採空了回傳 0。</summary>
        long Occupy(int tile, string owner, long marchId, long nowMs);

        /// <summary>採集結束（採滿或召回）：放開資源田並扣掉採走的量。</summary>
        void Release(int tile, string owner, long marchId, long gathered, long nowMs);
    }

    /// <summary>單機用的資源田登記（測試與預測用）：存量全滿、不會被別人佔用。</summary>
    public sealed class SimpleFieldRegistry : IFieldRegistry
    {
        private readonly Dictionary<int, long> _stock = new Dictionary<int, long>();
        private readonly Dictionary<int, (string owner, long march)> _taken = new Dictionary<int, (string, long)>();

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
