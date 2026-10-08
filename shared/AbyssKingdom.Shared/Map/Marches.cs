using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>
    /// 深淵中的一支行軍：主將帶兵營的實際部隊，沿廊道移動。
    /// 伺服器只存路徑與每個節點的抵達時間；位置由時間推算（客戶端插值顯示）。
    /// </summary>
    public class March
    {
        public long Id { get; set; }
        public HeroId Hero { get; set; }
        public Dictionary<TroopType, long> Troops { get; set; } = new Dictionary<TroopType, long>();
        /// <summary>經過的節點（含起點與終點）。</summary>
        public List<int> Path { get; set; } = new List<int>();
        /// <summary>抵達 Path[i] 的時間（Unix 毫秒）；ArriveAtMs[0] = 出發時間。</summary>
        public List<long> ArriveAtMs { get; set; } = new List<long>();
        /// <summary>已抵達終點，駐紮中。</summary>
        public bool Stationed { get; set; }
        /// <summary>召回中：抵達垂降口後解散，部隊回兵營。</summary>
        public bool Returning { get; set; }

        public int Destination => Path[Path.Count - 1];
        public long ArriveAt => ArriveAtMs[ArriveAtMs.Count - 1];
        public long TotalTroops()
        {
            long n = 0;
            foreach (var v in Troops.Values) n += v;
            return n;
        }
    }

    public enum MarchResult
    {
        Ok = 0,
        UnknownNode = 1,
        Unreachable = 2,
        HeroNotOwned = 3,
        HeroBusy = 4,
        NoTroops = 5,
        NotEnoughTroops = 6,
        ExceedsLeadership = 7,
        TooManyMarches = 8,
        UnknownMarch = 9,
        NotStationed = 10,
        AlreadyThere = 11,
    }

    /// <summary>行軍的事件（給伺服器推播用）。</summary>
    public sealed class MarchEvent
    {
        public long MarchId;
        public HeroId Hero;
        public int Node;
        public bool Disbanded;   // 召回抵達垂降口、部隊回營
        public bool Discovered;  // 揭開了新的節點
    }

    public static class MarchConfig
    {
        public const int MaxMarches = 3;
        /// <summary>移動 1 單位廊道長度所需毫秒（速度 100% 時）。一條廊道約 9~13 單位 → 約 30 秒。</summary>
        public const long MsPerLengthUnit = 3_000;

        /// <summary>兵種行軍速度百分比：懸索騎最快。</summary>
        public static long SpeedPercent(TroopType t) => t == TroopType.Rider ? 150 : 100;
    }

    public static class MarchLogic
    {
        /// <summary>
        /// 尋路（Dijkstra）：只能走「兩端都已探索、且沒有封鎖力場」的廊道；
        /// 終點可以是已探索節點，或與已探索節點相鄰的未知節點（抵達即揭開）。找不到回傳 null。
        /// </summary>
        public static List<int> FindPath(AbyssMap map, MapState state, int from, int to)
        {
            if (from == to) return new List<int> { from };
            var explored = new HashSet<int>(state.Explored);
            if (!explored.Contains(from)) return null;
            var dist = new Dictionary<int, long> { [from] = 0 };
            var prev = new Dictionary<int, int>();
            var open = new SortedSet<(long d, int n)> { (0, from) };
            while (open.Count > 0)
            {
                var (d, n) = open.Min;
                open.Remove(open.Min);
                if (n == to) break;
                if (!explored.Contains(n)) continue; // 未知節點只能當終點，不能穿過
                foreach (var ei in map.Nodes[n].Edges)
                {
                    var e = map.Edges[ei];
                    if (e.Field == ForceField.Seal) continue;
                    int m = e.Other(n);
                    if (!explored.Contains(m) && m != to) continue;
                    long nd = d + e.Length;
                    if (dist.TryGetValue(m, out var old) && old <= nd) continue;
                    if (dist.ContainsKey(m)) open.Remove((old, m));
                    dist[m] = nd;
                    prev[m] = n;
                    open.Add((nd, m));
                }
            }
            if (!prev.ContainsKey(to)) return null;
            var path = new List<int> { to };
            while (path[path.Count - 1] != from) path.Add(prev[path[path.Count - 1]]);
            path.Reverse();
            return path;
        }

        /// <summary>這支部隊的速度（以最慢的兵種為準）。</summary>
        public static long SpeedPercent(Dictionary<TroopType, long> troops)
        {
            long speed = long.MaxValue;
            foreach (var kv in troops)
                if (kv.Value > 0) speed = Math.Min(speed, MarchConfig.SpeedPercent(kv.Key));
            return speed == long.MaxValue ? 100 : speed;
        }

        /// <summary>依路徑與速度算出每個節點的抵達時間。</summary>
        public static List<long> Schedule(AbyssMap map, List<int> path, long departMs, long speedPercent)
        {
            var times = new List<long> { departMs };
            long t = departMs;
            for (int i = 1; i < path.Count; i++)
            {
                var e = map.EdgeBetween(path[i - 1], path[i]);
                t += e.Length * MarchConfig.MsPerLengthUnit * 100 / speedPercent;
                times.Add(t);
            }
            return times;
        }

        /// <summary>行軍目前所在的位置：在 Path[segment] 與 Path[segment+1] 之間，進度 0~1000（‰）。</summary>
        public static (int segment, int permille) Position(March m, long nowMs)
        {
            if (m.Stationed || nowMs >= m.ArriveAt) return (m.Path.Count - 1, 0);
            for (int i = 0; i + 1 < m.ArriveAtMs.Count; i++)
            {
                if (nowMs >= m.ArriveAtMs[i + 1]) continue;
                long span = Math.Max(1, m.ArriveAtMs[i + 1] - m.ArriveAtMs[i]);
                return (i, (int)((nowMs - m.ArriveAtMs[i]) * 1000 / span));
            }
            return (m.Path.Count - 1, 0);
        }

        /// <summary>出征：從自己的垂降口出發，前往目標節點。部隊從兵營扣除。</summary>
        public static MarchResult Start(CityState city, HeroId hero, Dictionary<TroopType, long> troops, int destination, long nowMs)
        {
            Advance(city, nowMs);
            var map = MapConfig.Layer(city.Map.Layer);
            var state = city.Map;
            if (destination < 0 || destination >= map.Nodes.Count) return MarchResult.UnknownNode;
            if (!city.Heroes.ContainsKey(hero)) return MarchResult.HeroNotOwned;
            foreach (var m in state.Marches) if (m.Hero == hero) return MarchResult.HeroBusy;
            if (state.Marches.Count >= MarchConfig.MaxMarches) return MarchResult.TooManyMarches;
            if (destination == state.HomeGate) return MarchResult.AlreadyThere;

            long total = 0;
            foreach (var kv in troops ?? new Dictionary<TroopType, long>())
            {
                if (kv.Value < 0) return MarchResult.NoTroops;
                if (city.GetTroops(kv.Key) < kv.Value) return MarchResult.NotEnoughTroops;
                total += kv.Value;
            }
            if (total <= 0) return MarchResult.NoTroops;
            if (total > PowerLogic.TotalStats(city, hero).Leadership) return MarchResult.ExceedsLeadership;

            var path = FindPath(map, state, state.HomeGate, destination);
            if (path == null) return MarchResult.Unreachable;

            var march = new March { Id = ++state.NextMarchId, Hero = hero, Path = path };
            foreach (var kv in troops)
            {
                if (kv.Value <= 0) continue;
                march.Troops[kv.Key] = kv.Value;
                city.Troops[kv.Key] = city.GetTroops(kv.Key) - kv.Value;
            }
            march.ArriveAtMs = Schedule(map, path, nowMs, SpeedPercent(march.Troops));
            state.Marches.Add(march);
            return MarchResult.Ok;
        }

        /// <summary>召回駐紮中的行軍：走回垂降口，抵達後解散、部隊回兵營。</summary>
        public static MarchResult Recall(CityState city, long marchId, long nowMs)
        {
            Advance(city, nowMs);
            var state = city.Map;
            var march = state.Marches.Find(m => m.Id == marchId);
            if (march == null) return MarchResult.UnknownMarch;
            if (!march.Stationed) return MarchResult.NotStationed;
            var map = MapConfig.Layer(state.Layer);
            var path = FindPath(map, state, march.Destination, state.HomeGate);
            if (path == null) return MarchResult.Unreachable;
            march.Path = path;
            march.ArriveAtMs = Schedule(map, path, nowMs, SpeedPercent(march.Troops));
            march.Stationed = false;
            march.Returning = true;
            if (path.Count == 1) Advance(city, nowMs); // 本來就在垂降口：立即解散
            return MarchResult.Ok;
        }

        /// <summary>推進所有行軍到 nowMs：抵達終點就駐紮並揭開迷霧；召回抵達垂降口就解散。回傳發生的事件。</summary>
        public static List<MarchEvent> Advance(CityState city, long nowMs)
        {
            var events = new List<MarchEvent>();
            var state = city.Map;
            for (int i = state.Marches.Count - 1; i >= 0; i--)
            {
                var m = state.Marches[i];
                if (m.Stationed || nowMs < m.ArriveAt) continue;
                if (m.Returning)
                {
                    foreach (var kv in m.Troops) city.Troops[kv.Key] = city.GetTroops(kv.Key) + kv.Value;
                    state.Marches.RemoveAt(i);
                    events.Add(new MarchEvent { MarchId = m.Id, Hero = m.Hero, Node = m.Destination, Disbanded = true });
                    continue;
                }
                m.Stationed = true;
                bool discovered = !state.Explored.Contains(m.Destination);
                if (discovered) state.Explored.Add(m.Destination);
                events.Add(new MarchEvent { MarchId = m.Id, Hero = m.Hero, Node = m.Destination, Discovered = discovered });
            }
            return events;
        }
    }
}
