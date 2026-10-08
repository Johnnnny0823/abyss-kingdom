using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>深淵地圖的節點種類（設定見 docs/design/GDD.md 5.3）。</summary>
    public enum NodeType
    {
        Gate = 0,          // 垂降口：從裂口邊緣（地表）下來的入口
        Cavern = 1,        // 資源洞室
        MonsterNest = 2,   // 深淵小怪巢
        Fissure = 3,       // 地脈裂隙（屬於一條淵脈）
        ShaftHub = 4,      // 升降井樞紐
        GuardianLair = 5,  // 守層者巢穴
    }

    /// <summary>廊道上的力場：Veil 遮蔽視野（看不到另一頭，但可以通過），Seal 封鎖通行（之後可破解）。</summary>
    public enum ForceField
    {
        None = 0,
        Veil = 1,
        Seal = 2,
    }

    public sealed class MapNode
    {
        public int Id;
        public NodeType Type;
        public int Column, Row;   // 生成用的格位（Row 越大越深）
        public int X, Y;          // 顯示座標
        public Element Element;   // 只有地脈裂隙有
        public readonly List<int> Edges = new List<int>();
    }

    public sealed class MapEdge
    {
        public int Id, A, B;
        public int Length;        // 移動距離（之後行軍用）
        public ForceField Field;
        public int Other(int node) => node == A ? B : A;
    }

    /// <summary>
    /// 一層深淵的「層網狀」地圖：節點＋狹窄廊道，不是棋盤格。
    /// 由種子確定性生成（只用整數亂數），伺服器與客戶端各自生成同一張圖，不必傳整張地圖。
    /// </summary>
    public sealed class AbyssMap
    {
        public int Layer;
        public ulong Seed;
        public readonly List<MapNode> Nodes = new List<MapNode>();
        public readonly List<MapEdge> Edges = new List<MapEdge>();
        public readonly List<int> Gates = new List<int>();
        public int Width, Height;

        public MapEdge EdgeBetween(int a, int b)
        {
            foreach (var e in Nodes[a].Edges)
                if (Edges[e].Other(a) == b) return Edges[e];
            return null;
        }

        /// <summary>從 start 出發、不經過 Seal 的可達節點（用於驗證連通）。</summary>
        public HashSet<int> Reachable(int start, bool throughSeals)
        {
            var seen = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                foreach (var ei in Nodes[n].Edges)
                {
                    var e = Edges[ei];
                    if (!throughSeals && e.Field == ForceField.Seal) continue;
                    int m = e.Other(n);
                    if (seen.Add(m)) queue.Enqueue(m);
                }
            }
            return seen;
        }
    }

    public static class MapConfig
    {
        public const int Columns = 12;
        public const int Rows = 18;
        public const int SpacingX = 100;
        public const int SpacingY = 90;
        public const int Margin = 60;

        /// <summary>每層的種子。之後改成每個伺服器／賽季不同。</summary>
        public static ulong LayerSeed(int layer) => 0xA6155_0000UL + (ulong)layer;

        static readonly Dictionary<int, AbyssMap> Cache = new Dictionary<int, AbyssMap>();

        /// <summary>取得某一層的地圖（同一程序內快取）。</summary>
        public static AbyssMap Layer(int layer)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(layer, out var map))
                {
                    map = MapGenerator.Generate(layer, LayerSeed(layer));
                    Cache[layer] = map;
                }
                return map;
            }
        }
    }

    public static class MapGenerator
    {
        public static AbyssMap Generate(int layer, ulong seed)
        {
            var rng = new DeterministicRandom(seed);
            int C = MapConfig.Columns, R = MapConfig.Rows;
            var map = new AbyssMap { Layer = layer, Seed = seed };
            map.Width = MapConfig.Margin * 2 + (C - 1) * MapConfig.SpacingX;
            map.Height = MapConfig.Margin * 2 + (R - 1) * MapConfig.SpacingY;
            var grid = new int[C, R];
            for (int c = 0; c < C; c++) for (int r = 0; r < R; r++) grid[c, r] = -1;

            int[] gateCols = { 1, 4, 7, 10 };
            const int lairCol = 6;

            // 1. 節點：每一欄是一條垂直管道，大部分格位有洞室，缺口處由較長的廊道跨過
            for (int c = 0; c < C; c++)
                for (int r = 0; r < R; r++)
                {
                    bool force = (r == 0 && Array.IndexOf(gateCols, c) >= 0) || (r == R - 1 && c == lairCol);
                    if (r == 0 && !force) continue;            // 第 0 列只有垂降口
                    if (!force && rng.Range(0, 99) >= 80) continue;
                    grid[c, r] = AddNode(map, rng, c, r);
                }

            // 2. 垂直廊道：同一欄上下相鄰（含跨過缺口）的節點相連
            for (int c = 0; c < C; c++)
            {
                int prev = -1;
                for (int r = 0; r < R; r++)
                {
                    if (grid[c, r] < 0) continue;
                    if (prev >= 0) Connect(map, prev, grid[c, r]);
                    prev = grid[c, r];
                }
            }

            // 3. 少量橫向／斜向連結，讓管道之間形成網路（但不會變成棋盤）
            for (int r = 1; r < R; r++)
                for (int c = 0; c + 1 < C; c++)
                {
                    if (grid[c, r] >= 0 && grid[c + 1, r] >= 0 && rng.Range(0, 99) < 16)
                        Connect(map, grid[c, r], grid[c + 1, r]);
                    else if (r + 1 < R && grid[c, r] >= 0 && grid[c + 1, r + 1] >= 0 && rng.Range(0, 99) < 6)
                        Connect(map, grid[c, r], grid[c + 1, r + 1]);
                }

            // 4. 保證全圖連通：把孤立的區塊接到最近的已連通節點
            EnsureConnected(map);

            // 5. 指定節點種類
            AssignTypes(map, rng, grid, gateCols, lairCol);

            // 6. 力場：遮蔽（不碰垂降口）與封鎖（不能切斷任何節點）
            foreach (var e in map.Edges)
            {
                if (map.Nodes[e.A].Type == NodeType.Gate || map.Nodes[e.B].Type == NodeType.Gate) continue;
                int roll = (int)rng.Range(0, 99);
                if (roll < 14) e.Field = ForceField.Veil;
                else if (roll < 20)
                {
                    e.Field = ForceField.Seal;
                    if (map.Reachable(map.Gates[0], throughSeals: false).Count != map.Nodes.Count) e.Field = ForceField.None;
                }
            }
            return map;
        }

        static int AddNode(AbyssMap map, DeterministicRandom rng, int c, int r)
        {
            var n = new MapNode
            {
                Id = map.Nodes.Count, Column = c, Row = r, Type = NodeType.Cavern,
                X = MapConfig.Margin + c * MapConfig.SpacingX + (int)rng.Range(-28, 28),
                Y = MapConfig.Margin + r * MapConfig.SpacingY + (int)rng.Range(-22, 22),
            };
            map.Nodes.Add(n);
            return n.Id;
        }

        static void Connect(AbyssMap map, int a, int b)
        {
            if (a == b || map.EdgeBetween(a, b) != null) return;
            var na = map.Nodes[a];
            var nb = map.Nodes[b];
            var e = new MapEdge
            {
                Id = map.Edges.Count, A = a, B = b,
                Length = Math.Max(1, (Math.Abs(na.X - nb.X) + Math.Abs(na.Y - nb.Y)) / 10),
            };
            map.Edges.Add(e);
            na.Edges.Add(e.Id);
            nb.Edges.Add(e.Id);
        }

        static void EnsureConnected(AbyssMap map)
        {
            while (true)
            {
                var reached = map.Reachable(0, throughSeals: true);
                if (reached.Count == map.Nodes.Count) return;
                // 找一個未連通節點與已連通節點中「格位距離最近」的一對，接起來
                int bestA = -1, bestB = -1, best = int.MaxValue;
                foreach (var n in map.Nodes)
                {
                    if (reached.Contains(n.Id)) continue;
                    foreach (var m in map.Nodes)
                    {
                        if (!reached.Contains(m.Id)) continue;
                        int d = Math.Abs(n.Column - m.Column) * 3 + Math.Abs(n.Row - m.Row);
                        if (d < best) { best = d; bestA = n.Id; bestB = m.Id; }
                    }
                }
                Connect(map, bestA, bestB);
            }
        }

        static void AssignTypes(AbyssMap map, DeterministicRandom rng, int[,] grid, int[] gateCols, int lairCol)
        {
            foreach (var c in gateCols)
            {
                map.Nodes[grid[c, 0]].Type = NodeType.Gate;
                map.Gates.Add(grid[c, 0]);
            }
            map.Nodes[grid[lairCol, MapConfig.Rows - 1]].Type = NodeType.GuardianLair;

            // 升降井樞紐：在幾個深度帶各挑一個連結最多的節點
            foreach (var band in new[] { (4, 6), (8, 10), (12, 14) })
            {
                MapNode best = null;
                foreach (var n in map.Nodes)
                    if (n.Type == NodeType.Cavern && n.Row >= band.Item1 && n.Row <= band.Item2 &&
                        (best == null || n.Edges.Count > best.Edges.Count || (n.Edges.Count == best.Edges.Count && Math.Abs(n.Column - 6) < Math.Abs(best.Column - 6))))
                        best = n;
                if (best != null) best.Type = NodeType.ShaftHub;
            }

            // 地脈裂隙：10 個，五條淵脈各 2 個，分布在第 3 列以下
            var candidates = new List<MapNode>();
            foreach (var n in map.Nodes) if (n.Type == NodeType.Cavern && n.Row >= 3) candidates.Add(n);
            for (int i = 0; i < 10 && candidates.Count > 0; i++)
            {
                int pick = (int)rng.Range(0, candidates.Count - 1);
                var n = candidates[pick];
                candidates.RemoveAt(pick);
                n.Type = NodeType.Fissure;
                n.Element = Elements.All[i % Elements.All.Length];
            }

            // 小怪巢：剩下的洞室約三成
            foreach (var n in map.Nodes)
                if (n.Type == NodeType.Cavern && n.Row >= 1 && rng.Range(0, 99) < 30)
                    n.Type = NodeType.MonsterNest;
        }
    }

    /// <summary>玩家在深淵中的探索狀態（存在城市存檔裡）。</summary>
    public class MapState
    {
        public int Layer { get; set; } = 1;
        /// <summary>已探索的節點。</summary>
        public List<int> Explored { get; set; } = new List<int>();
        /// <summary>自己的垂降口（行軍出發與召回的地方）；-1 = 尚未進入深淵。</summary>
        public int HomeGate { get; set; } = -1;
        /// <summary>進行中與駐紮中的行軍。</summary>
        public List<March> Marches { get; set; } = new List<March>();
        public long NextMarchId { get; set; }
    }

    public static class MapLogic
    {
        /// <summary>第一次進入深淵時，依玩家 ID 分配一個垂降口（分散玩家，避免全擠在同一處）。</summary>
        public static bool EnsureStarted(CityState city, string playerId)
        {
            var state = city.Map;
            if (state.HomeGate >= 0) return false;
            if (state.Explored.Count > 0)
            {
                state.HomeGate = state.Explored[0]; // 舊存檔：第一個探索的節點就是垂降口
                return true;
            }
            var map = MapConfig.Layer(state.Layer);
            state.HomeGate = map.Gates[(int)(StableHash(playerId) % (uint)map.Gates.Count)];
            state.Explored.Add(state.HomeGate);
            return true;
        }

        /// <summary>看得到的節點：探索過的，加上它們經由「無遮蔽」廊道相鄰的節點。</summary>
        public static HashSet<int> Visible(AbyssMap map, MapState state)
        {
            var vis = new HashSet<int>(state.Explored);
            foreach (var n in state.Explored)
                foreach (var ei in map.Nodes[n].Edges)
                {
                    var e = map.Edges[ei];
                    if (e.Field != ForceField.Veil) vis.Add(e.Other(n));
                }
            return vis;
        }

        /// <summary>穩定的字串雜湊（FNV-1a）；string.GetHashCode 每次執行不同，不能用。</summary>
        public static uint StableHash(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s ?? "") { h ^= ch; h *= 16777619; }
            return h;
        }
    }
}
