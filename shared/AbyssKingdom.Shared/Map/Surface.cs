using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>地表的分區（同心環）：越靠近深淵越肥、越危險。</summary>
    public enum SurfaceZone
    {
        Rim = 0,     // 崖緣（內環）：資源最好，垂降口在這裡
        Middle = 1,  // 中環
        Outer = 2,   // 外環：新手區
    }

    /// <summary>玩家在地表的狀態（存在城市存檔裡）。</summary>
    public class SurfaceState
    {
        /// <summary>主城所在的節點（CitySite）；-1 = 尚未分配。</summary>
        public int Site { get; set; } = -1;
    }

    /// <summary>
    /// 地表大陸：深淵位於正中央，周圍 8 圈同心環。每圈有環狀道路，圈與圈之間有放射道路；
    /// 分區邊界（崖緣↔中環、中環↔外環）只有 6 個關隘可以通過。
    /// 拓撲與路程只用整數決定（伺服器與客戶端保證一致）；座標只用於繪圖。
    /// </summary>
    public static class SurfaceConfig
    {
        public const int Rings = 8;
        public const int PitRadius = 140;
        public const int FirstRingRadius = 200;
        public const int RingSpacing = 72;
        public const int PassesPerBoundary = 6;
        public const int Margin = 70;
        public const ulong Seed = 0x5_0F_ACE_0001UL;

        public static int NodesInRing(int k) => 16 + 6 * k;
        public static int RingRadius(int k) => FirstRingRadius + RingSpacing * k;
        public static SurfaceZone ZoneOfRing(int k) => k <= 1 ? SurfaceZone.Rim : k <= 4 ? SurfaceZone.Middle : SurfaceZone.Outer;
        /// <summary>這一圈與外面一圈之間是分區邊界（只有關隘相通）。</summary>
        public static bool IsBoundary(int k) => k == 1 || k == 4;

        static AbyssMap _map;
        static readonly object Lock = new object();

        public static AbyssMap Map()
        {
            lock (Lock) return _map ?? (_map = SurfaceGenerator.Generate(Seed));
        }
    }

    public static class SurfaceGenerator
    {
        public static AbyssMap Generate(ulong seed)
        {
            var rng = new DeterministicRandom(seed);
            int R = SurfaceConfig.Rings;
            int outer = SurfaceConfig.RingRadius(R - 1);
            int size = (outer + SurfaceConfig.Margin) * 2;
            var map = new AbyssMap { Layer = 0, Seed = seed, Width = size, Height = size };
            int c = size / 2;
            var ids = new int[R][];

            // 節點：每圈等角分布（略加擾動，只影響繪圖）
            for (int k = 0; k < R; k++)
            {
                int n = SurfaceConfig.NodesInRing(k);
                ids[k] = new int[n];
                double offset = k * 0.37;
                for (int i = 0; i < n; i++)
                {
                    double a = (i + offset + (rng.Range(-20, 20) / 100.0)) * Math.PI * 2 / n;
                    int r = SurfaceConfig.RingRadius(k) + (int)rng.Range(-14, 14);
                    var node = new MapNode
                    {
                        Id = map.Nodes.Count, Row = k, Column = i, Type = NodeType.CitySite,
                        X = c + (int)Math.Round(Math.Cos(a) * r),
                        Y = c + (int)Math.Round(Math.Sin(a) * r),
                    };
                    map.Nodes.Add(node);
                    ids[k][i] = node.Id;
                }
            }

            // 關隘：每條分區邊界上等距 6 個
            var passes = new HashSet<int>();
            for (int k = 0; k + 1 < R; k++)
            {
                if (!SurfaceConfig.IsBoundary(k)) continue;
                int n = SurfaceConfig.NodesInRing(k);
                for (int p = 0; p < SurfaceConfig.PassesPerBoundary; p++)
                {
                    int i = p * n / SurfaceConfig.PassesPerBoundary;
                    passes.Add(ids[k][i]);
                    passes.Add(ids[k + 1][Outward(k, i)]);
                }
            }

            // 道路：環狀（相鄰兩點）＋放射（往外一圈對應位置；分區邊界只有關隘）
            for (int k = 0; k < R; k++)
            {
                int n = SurfaceConfig.NodesInRing(k);
                int ringLen = Math.Max(1, SurfaceConfig.RingRadius(k) * 628 / 100 / n / 10);
                for (int i = 0; i < n; i++)
                    Connect(map, ids[k][i], ids[k][(i + 1) % n], ringLen);
                if (k + 1 >= R) continue;
                for (int i = 0; i < n; i++)
                {
                    if (SurfaceConfig.IsBoundary(k) && !passes.Contains(ids[k][i])) continue;
                    Connect(map, ids[k][i], ids[k + 1][Outward(k, i)], SurfaceConfig.RingSpacing / 10);
                }
            }

            // 節點種類
            int gateStride = SurfaceConfig.NodesInRing(0) / 4;
            foreach (var node in map.Nodes)
            {
                int k = node.Row;
                if (passes.Contains(node.Id)) { node.Type = NodeType.Pass; continue; }
                if (k == 0 && node.Column % gateStride == 2) { node.Type = NodeType.Gate; map.Gates.Add(node.Id); continue; }
                var zone = SurfaceConfig.ZoneOfRing(k);
                int roll = (int)rng.Range(0, 99);
                int city = zone == SurfaceZone.Rim ? 25 : zone == SurfaceZone.Middle ? 50 : 65;
                int field = zone == SurfaceZone.Rim ? 50 : zone == SurfaceZone.Middle ? 30 : 25;
                if (roll < city) node.Type = NodeType.CitySite;
                else if (roll < city + field)
                {
                    node.Type = NodeType.ResourceField;
                    node.Resource = (ResourceType)rng.Range(0, 3);
                    node.Level = zone == SurfaceZone.Rim ? 3 : zone == SurfaceZone.Middle ? 2 : 1;
                }
                else
                {
                    node.Type = NodeType.MonsterCamp;
                    node.Level = zone == SurfaceZone.Rim ? 3 : zone == SurfaceZone.Middle ? 2 : 1;
                }
            }
            return map;
        }

        /// <summary>第 k 圈第 i 個節點，往外一圈對應的索引（整數運算）。</summary>
        static int Outward(int k, int i)
        {
            int n = SurfaceConfig.NodesInRing(k), m = SurfaceConfig.NodesInRing(k + 1);
            return (i * m + n / 2) / n % m;
        }

        static void Connect(AbyssMap map, int a, int b, int length)
        {
            if (a == b || map.EdgeBetween(a, b) != null) return;
            var e = new MapEdge { Id = map.Edges.Count, A = a, B = b, Length = Math.Max(1, length) };
            map.Edges.Add(e);
            map.Nodes[a].Edges.Add(e.Id);
            map.Nodes[b].Edges.Add(e.Id);
        }
    }

    public static class SurfaceLogic
    {
        public static SurfaceZone Zone(MapNode n) => SurfaceConfig.ZoneOfRing(n.Row);

        /// <summary>
        /// 為新玩家挑一個空的主城位置：優先外環（新手區），其次中環。occupied = 已被占用的節點。
        /// pick 由伺服器提供（亂數），回傳 -1 表示已滿。
        /// </summary>
        public static int PickSite(AbyssMap map, ICollection<int> occupied, Func<int, int> pick)
        {
            foreach (var zone in new[] { SurfaceZone.Outer, SurfaceZone.Middle, SurfaceZone.Rim })
            {
                var free = new List<int>();
                foreach (var n in map.Nodes)
                    if (n.Type == NodeType.CitySite && Zone(n) == zone && !occupied.Contains(n.Id)) free.Add(n.Id);
                if (free.Count > 0) return free[pick(free.Count)];
            }
            return -1;
        }
    }
}
