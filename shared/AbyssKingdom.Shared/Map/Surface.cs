using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>地表的分區（同心環）：越靠近深淵越肥、越危險。Pit＝深淵本身，Edge＝地圖外圍（不能走）。</summary>
    public enum SurfaceZone
    {
        Rim = 0,     // 崖緣（內環）：資源最好，深淵入口在這裡
        Middle = 1,  // 中環
        Outer = 2,   // 外環：新手區
        Pit = 3,
        Edge = 4,
    }

    /// <summary>地圖上的固定物件（資源田、怪物營地、深淵入口）。位置由地圖座標決定，不需要存檔。</summary>
    public enum FeatureType
    {
        None = 0,
        ResourceField = 1,
        MonsterCamp = 2,
        AbyssGate = 3,
    }

    public sealed class Feature
    {
        public FeatureType Type;
        public int X, Y;
        public ResourceType Resource;
        public int Level;
    }

    /// <summary>玩家在地表的狀態（存在城市存檔裡）。</summary>
    public class SurfaceState
    {
        /// <summary>主城的格子座標（佔地左上角，2×2）；-1 = 尚未分配。</summary>
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;

        public List<March> Marches { get; set; } = new List<March>();
        public long NextMarchId { get; set; }

        public bool Placed => X >= 0 && Y >= 0;
    }

    /// <summary>
    /// 地表大陸：800×800 格，深淵在正中央，往外是崖緣、中環、外環三個同心分區。
    /// 分區之間有環狀山脈，每條山脈只有 6 個關隘可以通過。沒有道路，行軍走直線（跨區時繞經關隘）。
    /// 全部用整數判斷（距離比較用平方），伺服器與客戶端結果一致。
    /// </summary>
    public static class SurfaceConfig
    {
        public const int Size = 800;
        public const int Center = Size / 2;
        public const int PitRadius = 70;
        public const int RimOuter = 160;      // 崖緣與中環之間的山脈
        public const int MiddleOuter = 280;   // 中環與外環之間的山脈
        public const int OuterRadius = 390;   // 再往外是地圖邊緣
        public const int WallHalfWidth = 3;
        public const int PassesPerBoundary = 6;
        public const int PassRadius = 8;

        /// <summary>主城佔地邊長（格）。</summary>
        public const int CityFootprint = 2;
        /// <summary>主城候選位置的間距：每 10×10 格一個（略有偏移）。空位平常不顯示。</summary>
        public const int Cell = 10;
        public const int Cells = Size / Cell;

        /// <summary>每 30 度的方向（cos, sin × 1000），整數查表取代三角函數。</summary>
        public static readonly int[,] Dir12 =
        {
            { 1000, 0 }, { 866, 500 }, { 500, 866 }, { 0, 1000 }, { -500, 866 }, { -866, 500 },
            { -1000, 0 }, { -866, -500 }, { -500, -866 }, { 0, -1000 }, { 500, -866 }, { 866, -500 },
        };

        public static readonly int[] Boundaries = { RimOuter, MiddleOuter };
    }

    public static class SurfaceLogic
    {
        // ───── 幾何 ─────

        public static long Dist2(int x1, int y1, int x2, int y2)
        {
            long dx = x1 - x2, dy = y1 - y2;
            return dx * dx + dy * dy;
        }

        /// <summary>整數平方根（無條件捨去）。</summary>
        public static long ISqrt(long n)
        {
            if (n <= 0) return 0;
            long x = n, y = (x + 1) / 2; // 牛頓法，全程整數
            while (y < x)
            {
                x = y;
                y = (x + n / x) / 2;
            }
            return x;
        }

        public static long Distance(int x1, int y1, int x2, int y2) => ISqrt(Dist2(x1, y1, x2, y2));

        public static SurfaceZone Zone(int x, int y)
        {
            long d = Dist2(x, y, SurfaceConfig.Center, SurfaceConfig.Center);
            if (d < Sq(SurfaceConfig.PitRadius)) return SurfaceZone.Pit;
            if (d < Sq(SurfaceConfig.RimOuter)) return SurfaceZone.Rim;
            if (d < Sq(SurfaceConfig.MiddleOuter)) return SurfaceZone.Middle;
            if (d < Sq(SurfaceConfig.OuterRadius)) return SurfaceZone.Outer;
            return SurfaceZone.Edge;
        }

        static long Sq(long v) => v * v;

        /// <summary>第 b 條山脈（0 = 崖緣／中環、1 = 中環／外環）上的關隘中心。兩條山脈的關隘錯開 30 度。</summary>
        public static (int x, int y) Pass(int boundary, int i)
        {
            int dir = (i * 2 + boundary) % 12;
            int r = SurfaceConfig.Boundaries[boundary];
            return (SurfaceConfig.Center + r * SurfaceConfig.Dir12[dir, 0] / 1000, SurfaceConfig.Center + r * SurfaceConfig.Dir12[dir, 1] / 1000);
        }

        public static bool NearPass(int x, int y, int extra = 0)
        {
            for (int b = 0; b < 2; b++)
                for (int i = 0; i < SurfaceConfig.PassesPerBoundary; i++)
                {
                    var p = Pass(b, i);
                    if (Dist2(x, y, p.x, p.y) <= Sq(SurfaceConfig.PassRadius + extra)) return true;
                }
            return false;
        }

        /// <summary>這格是山脈（關隘的位置除外）。</summary>
        public static bool IsWall(int x, int y)
        {
            long d = Dist2(x, y, SurfaceConfig.Center, SurfaceConfig.Center);
            foreach (var r in SurfaceConfig.Boundaries)
                if (d >= Sq(r - SurfaceConfig.WallHalfWidth) && d <= Sq(r + SurfaceConfig.WallHalfWidth))
                    return !NearPass(x, y);
            return false;
        }

        /// <summary>可以站人的格子：在地圖內、不是深淵、不是山脈。</summary>
        public static bool Walkable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= SurfaceConfig.Size || y >= SurfaceConfig.Size) return false;
            var z = Zone(x, y);
            return z != SurfaceZone.Pit && z != SurfaceZone.Edge && !IsWall(x, y);
        }

        static uint Hash(int a, int b, uint salt)
        {
            unchecked
            {
                uint h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ salt * 83492791u;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return h;
            }
        }

        static int LevelOf(SurfaceZone z) => z == SurfaceZone.Rim ? 3 : z == SurfaceZone.Middle ? 2 : 1;

        // ───── 固定物件 ─────

        static List<Feature> _features;
        static Dictionary<int, Feature> _featureAt;
        static readonly object Lock = new object();

        /// <summary>
        /// 資源田、怪物營地、深淵入口：放在主城候選格的「角落」格點上，和主城位置錯開。
        /// 約一成格點是資源田、4% 是怪物營地；等級依分區（崖緣 3、中環 2、外環 1）。
        /// </summary>
        public static List<Feature> Features()
        {
            lock (Lock)
            {
                if (_features != null) return _features;
                var list = new List<Feature>();
                for (int i = 0; i < 4; i++)
                {
                    int dir = i * 3 + 1; // 30°、120°、210°、300°
                    int r = SurfaceConfig.PitRadius + 22;
                    list.Add(new Feature
                    {
                        Type = FeatureType.AbyssGate,
                        X = SurfaceConfig.Center + r * SurfaceConfig.Dir12[dir, 0] / 1000,
                        Y = SurfaceConfig.Center + r * SurfaceConfig.Dir12[dir, 1] / 1000,
                    });
                }
                for (int cy = 1; cy < SurfaceConfig.Cells; cy++)
                    for (int cx = 1; cx < SurfaceConfig.Cells; cx++)
                    {
                        int x = cx * SurfaceConfig.Cell, y = cy * SurfaceConfig.Cell;
                        if (!Walkable(x, y) || NearPass(x, y, 2) || NearGate(list, x, y)) continue;
                        uint roll = Hash(cx, cy, 7) % 100;
                        var z = Zone(x, y);
                        if (roll < 10)
                            list.Add(new Feature { Type = FeatureType.ResourceField, X = x, Y = y, Resource = (ResourceType)(Hash(cx, cy, 11) % 4), Level = LevelOf(z) });
                        else if (roll < 14)
                            list.Add(new Feature { Type = FeatureType.MonsterCamp, X = x, Y = y, Level = LevelOf(z) });
                    }
                _featureAt = new Dictionary<int, Feature>();
                foreach (var f in list) _featureAt[Tile(f.X, f.Y)] = f;
                _features = list;
                return list;
            }
        }

        static bool NearGate(List<Feature> list, int x, int y)
        {
            foreach (var f in list)
                if (f.Type == FeatureType.AbyssGate && Dist2(x, y, f.X, f.Y) <= 36) return true;
            return false;
        }

        public static Feature FeatureAt(int x, int y)
        {
            Features();
            return _featureAt.TryGetValue(Tile(x, y), out var f) ? f : null;
        }

        public static int Tile(int x, int y) => y * SurfaceConfig.Size + x;
        public static (int x, int y) FromTile(int tile) => (tile % SurfaceConfig.Size, tile / SurfaceConfig.Size);

        // ───── 主城位置 ─────

        /// <summary>第 (cx, cy) 格的主城候選位置（佔地左上角）；不能用時回傳 null。</summary>
        public static (int x, int y)? SiteOf(int cx, int cy)
        {
            if (cx < 0 || cy < 0 || cx >= SurfaceConfig.Cells || cy >= SurfaceConfig.Cells) return null;
            int x = cx * SurfaceConfig.Cell + 4 + (int)(Hash(cx, cy, 3) % 5) - 2;
            int y = cy * SurfaceConfig.Cell + 4 + (int)(Hash(cx, cy, 5) % 5) - 2;
            for (int dy = 0; dy < SurfaceConfig.CityFootprint; dy++)
                for (int dx = 0; dx < SurfaceConfig.CityFootprint; dx++)
                    if (!Walkable(x + dx, y + dy)) return null;
            if (NearPass(x, y, 3)) return null;
            var z = Zone(x, y);
            if (z != Zone(x + 1, y + 1)) return null;
            return (x, y);
        }

        static List<(int x, int y, SurfaceZone z)> _sites;

        /// <summary>所有主城候選位置（約 4000 個）。平常不顯示，遷城時才標出可以放的空位。</summary>
        public static List<(int x, int y, SurfaceZone z)> Sites()
        {
            lock (Lock)
            {
                if (_sites != null) return _sites;
                var list = new List<(int, int, SurfaceZone)>();
                for (int cy = 0; cy < SurfaceConfig.Cells; cy++)
                    for (int cx = 0; cx < SurfaceConfig.Cells; cx++)
                    {
                        var s = SiteOf(cx, cy);
                        if (s.HasValue) list.Add((s.Value.x, s.Value.y, Zone(s.Value.x, s.Value.y)));
                    }
                _sites = list;
                return list;
            }
        }

        /// <summary>
        /// 為新玩家挑一個空的主城位置：優先外環（新手區），其次中環、崖緣。
        /// occupied = 已占用位置的 Tile(x, y)；pick 由伺服器提供（亂數）。全滿回傳 null。
        /// </summary>
        public static (int x, int y)? PickSite(ICollection<int> occupied, Func<int, int> pick)
        {
            foreach (var zone in new[] { SurfaceZone.Outer, SurfaceZone.Middle, SurfaceZone.Rim })
            {
                var free = new List<(int, int)>();
                foreach (var s in Sites())
                    if (s.z == zone && !occupied.Contains(Tile(s.x, s.y))) free.Add((s.x, s.y));
                if (free.Count > 0) return free[pick(free.Count)];
            }
            return null;
        }

        /// <summary>主城中心（行軍的起點與終點用）。</summary>
        public static (int x, int y) CityCenter(SurfaceState s) => (s.X + 1, s.Y + 1);
    }
}
