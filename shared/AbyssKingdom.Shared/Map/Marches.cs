using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Shared.Map
{
    /// <summary>
    /// 地表上的一支行軍：主將帶兵營的實際部隊，從主城走直線到目標格子（跨分區時繞經關隘）。
    /// 伺服器只存路徑點與每個點的抵達時間；位置由時間推算（客戶端插值顯示）。
    /// </summary>
    public class March
    {
        public long Id { get; set; }
        public HeroId Hero { get; set; }
        public Dictionary<TroopType, long> Troops { get; set; } = new Dictionary<TroopType, long>();
        /// <summary>路徑點（SurfaceLogic.Tile 編碼，含起點與終點）。</summary>
        public List<int> Path { get; set; } = new List<int>();
        /// <summary>抵達 Path[i] 的時間（Unix 毫秒）；ArriveAtMs[0] = 出發時間。</summary>
        public List<long> ArriveAtMs { get; set; } = new List<long>();
        /// <summary>已抵達終點，駐紮中。</summary>
        public bool Stationed { get; set; }
        /// <summary>召回中：回到主城後解散，部隊回兵營。</summary>
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
        UnknownNode = 1,     // 目標不在地圖上或不能站（深淵、山脈）
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
        NoCity = 12,
    }

    /// <summary>行軍的事件（給伺服器推播用）。</summary>
    public sealed class MarchEvent
    {
        public long MarchId;
        public HeroId Hero;
        public int Tile;
        public bool Disbanded;   // 召回抵達主城、部隊回營
    }

    public static class MarchConfig
    {
        public const int MaxMarches = 3;
        /// <summary>移動 1 格所需毫秒（速度 100% 時）。</summary>
        public const long MsPerTile = 3_000;

        /// <summary>兵種行軍速度百分比：裂蹄騎最快。</summary>
        public static long SpeedPercent(TroopType t) => t == TroopType.Rider ? 150 : 100;
    }

    public static class MarchLogic
    {
        // ───── 尋路：直線，跨分區時繞經關隘；同分區內若直線會切過內側山脈或深淵，就沿環上的轉折點繞過去 ─────

        private struct Pt
        {
            public int X, Y;
            public SurfaceZone Z;
            public int Pair; // 關隘兩側的一對點（>= 0 時，同 Pair 的兩點直接相通）
            public Pt(int x, int y, SurfaceZone z, int pair = -1) { X = x; Y = y; Z = z; Pair = pair; }
        }

        /// <summary>分區內側不能穿過的圓（深淵或山脈外緣，+1 讓剛好擦邊也算碰到）。</summary>
        static int InnerRadius(SurfaceZone z) =>
            z == SurfaceZone.Rim ? SurfaceConfig.PitRadius
            : z == SurfaceZone.Middle ? SurfaceConfig.RimOuter + SurfaceConfig.WallHalfWidth + 1
            : SurfaceConfig.MiddleOuter + SurfaceConfig.WallHalfWidth + 1;

        static int MidRadius(SurfaceZone z) =>
            z == SurfaceZone.Rim ? (SurfaceConfig.PitRadius + SurfaceConfig.RimOuter) / 2 + 8
            : z == SurfaceZone.Middle ? (SurfaceConfig.RimOuter + SurfaceConfig.MiddleOuter) / 2 + 10
            : (SurfaceConfig.MiddleOuter + SurfaceConfig.OuterRadius) / 2 + 10;

        /// <summary>線段 P→Q 是否穿過以地圖中心為圓心、半徑 r 的圓（整數運算）。</summary>
        static bool CrossesCircle(int px, int py, int qx, int qy, int r)
        {
            long cx = SurfaceConfig.Center, cy = SurfaceConfig.Center;
            long dx = qx - px, dy = qy - py;
            long len2 = dx * dx + dy * dy;
            long r2 = (long)r * r;
            if (len2 == 0) return SurfaceLogic.Dist2(px, py, (int)cx, (int)cy) < r2;
            long dot = (cx - px) * dx + (cy - py) * dy;
            if (dot <= 0) return SurfaceLogic.Dist2(px, py, (int)cx, (int)cy) < r2;
            if (dot >= len2) return SurfaceLogic.Dist2(qx, qy, (int)cx, (int)cy) < r2;
            long cross = dx * (cy - py) - dy * (cx - px);
            // 垂足距離² = cross² / len2 < r²  ⇔  cross² < r² × len2（地圖 800 格內不會溢位）
            return cross * cross < r2 * len2;
        }

        /// <summary>
        /// 從 (fx, fy) 到 (tx, ty) 的路徑點（含起終點）。轉折點只有關隘兩側各一點與各分區環上每 30 度一個點，
        /// 在這張小圖上跑 Dijkstra。到不了回傳 null。
        /// </summary>
        public static List<(int x, int y)> FindPath(int fx, int fy, int tx, int ty)
        {
            if (!SurfaceLogic.Walkable(fx, fy) || !SurfaceLogic.Walkable(tx, ty)) return null;
            var pts = new List<Pt>
            {
                new Pt(fx, fy, SurfaceLogic.Zone(fx, fy)),
                new Pt(tx, ty, SurfaceLogic.Zone(tx, ty)),
            };
            for (int b = 0; b < 2; b++)
                for (int i = 0; i < SurfaceConfig.PassesPerBoundary; i++)
                {
                    int pair = b * SurfaceConfig.PassesPerBoundary + i;
                    int off = SurfaceConfig.WallHalfWidth + 2;
                    pts.Add(PassSide(b, i, -off, b == 0 ? SurfaceZone.Rim : SurfaceZone.Middle, pair));
                    pts.Add(PassSide(b, i, off, b == 0 ? SurfaceZone.Middle : SurfaceZone.Outer, pair));
                }
            foreach (var z in new[] { SurfaceZone.Rim, SurfaceZone.Middle, SurfaceZone.Outer })
            {
                int r = MidRadius(z);
                for (int d = 0; d < 12; d++)
                    pts.Add(new Pt(SurfaceConfig.Center + r * SurfaceConfig.Dir12[d, 0] / 1000, SurfaceConfig.Center + r * SurfaceConfig.Dir12[d, 1] / 1000, z));
            }

            int n = pts.Count;
            var dist = new long[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = long.MaxValue; prev[i] = -1; }
            dist[0] = 0;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] != long.MaxValue && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || u == 1) break;
                done[u] = true;
                for (int v = 0; v < n; v++)
                {
                    if (done[v] || !Linked(pts[u], pts[v])) continue;
                    long nd = dist[u] + SurfaceLogic.Distance(pts[u].X, pts[u].Y, pts[v].X, pts[v].Y);
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                }
            }
            if (dist[1] == long.MaxValue) return null;
            var path = new List<(int, int)>();
            for (int v = 1; v >= 0; v = prev[v]) path.Add((pts[v].X, pts[v].Y));
            path.Reverse();
            return path;
        }

        /// <summary>關隘的內側或外側一點（沿半徑方向離山脈中心 offset 格）。</summary>
        static Pt PassSide(int boundary, int i, int offset, SurfaceZone zone, int pair)
        {
            int dir = (i * 2 + boundary) % 12;
            int r = SurfaceConfig.Boundaries[boundary] + offset;
            return new Pt(SurfaceConfig.Center + r * SurfaceConfig.Dir12[dir, 0] / 1000, SurfaceConfig.Center + r * SurfaceConfig.Dir12[dir, 1] / 1000, zone, pair);
        }

        /// <summary>兩個點可以直線相連：同一個關隘的兩側；或同在一個分區內、直線不穿過該分區內側的山脈或深淵。</summary>
        static bool Linked(Pt a, Pt b)
        {
            if (a.Pair >= 0 && a.Pair == b.Pair) return true;
            if (a.Z != b.Z || a.Z == SurfaceZone.Pit || a.Z == SurfaceZone.Edge) return false;
            return !CrossesCircle(a.X, a.Y, b.X, b.Y, InnerRadius(a.Z));
        }

        /// <summary>這支部隊的速度（以最慢的兵種為準）。</summary>
        public static long SpeedPercent(Dictionary<TroopType, long> troops)
        {
            long speed = long.MaxValue;
            foreach (var kv in troops)
                if (kv.Value > 0) speed = Math.Min(speed, MarchConfig.SpeedPercent(kv.Key));
            return speed == long.MaxValue ? 100 : speed;
        }

        /// <summary>依路徑與速度算出每個路徑點的抵達時間。</summary>
        public static List<long> Schedule(List<int> path, long departMs, long speedPercent)
        {
            var times = new List<long> { departMs };
            long t = departMs;
            for (int i = 1; i < path.Count; i++)
            {
                var a = SurfaceLogic.FromTile(path[i - 1]);
                var b = SurfaceLogic.FromTile(path[i]);
                long d = Math.Max(1, SurfaceLogic.Distance(a.x, a.y, b.x, b.y));
                t += d * MarchConfig.MsPerTile * 100 / speedPercent;
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

        static List<int> Encode(List<(int x, int y)> path)
        {
            var list = new List<int>(path.Count);
            foreach (var p in path) list.Add(SurfaceLogic.Tile(p.x, p.y));
            return list;
        }

        /// <summary>出征：從主城出發，前往目標格子。部隊從兵營扣除。</summary>
        public static MarchResult Start(CityState city, HeroId hero, Dictionary<TroopType, long> troops, int x, int y, long nowMs)
        {
            Advance(city, nowMs);
            var state = city.Surface;
            if (!state.Placed) return MarchResult.NoCity;
            if (!SurfaceLogic.Walkable(x, y)) return MarchResult.UnknownNode;
            if (!city.Heroes.ContainsKey(hero)) return MarchResult.HeroNotOwned;
            foreach (var m in state.Marches) if (m.Hero == hero) return MarchResult.HeroBusy;
            if (state.Marches.Count >= MarchConfig.MaxMarches) return MarchResult.TooManyMarches;
            if (x >= state.X && x < state.X + SurfaceConfig.CityFootprint && y >= state.Y && y < state.Y + SurfaceConfig.CityFootprint)
                return MarchResult.AlreadyThere;

            long total = 0;
            foreach (var kv in troops ?? new Dictionary<TroopType, long>())
            {
                if (kv.Value < 0) return MarchResult.NoTroops;
                if (city.GetTroops(kv.Key) < kv.Value) return MarchResult.NotEnoughTroops;
                total += kv.Value;
            }
            if (total <= 0) return MarchResult.NoTroops;
            if (total > PowerLogic.TotalStats(city, hero).Leadership) return MarchResult.ExceedsLeadership;

            var home = SurfaceLogic.CityCenter(state);
            var path = FindPath(home.x, home.y, x, y);
            if (path == null) return MarchResult.Unreachable;

            var march = new March { Id = ++state.NextMarchId, Hero = hero, Path = Encode(path) };
            foreach (var kv in troops)
            {
                if (kv.Value <= 0) continue;
                march.Troops[kv.Key] = kv.Value;
                city.Troops[kv.Key] = city.GetTroops(kv.Key) - kv.Value;
            }
            march.ArriveAtMs = Schedule(march.Path, nowMs, SpeedPercent(march.Troops));
            state.Marches.Add(march);
            return MarchResult.Ok;
        }

        /// <summary>召回：駐紮中或還在路上的行軍都可以召回，從目前位置走回主城，抵達後解散、部隊回兵營。</summary>
        public static MarchResult Recall(CityState city, long marchId, long nowMs)
        {
            Advance(city, nowMs);
            var state = city.Surface;
            var march = state.Marches.Find(m => m.Id == marchId);
            if (march == null) return MarchResult.UnknownMarch;
            if (march.Returning) return MarchResult.Ok;
            var here = CurrentTile(march, nowMs);
            var home = SurfaceLogic.CityCenter(state);
            var path = FindPath(here.x, here.y, home.x, home.y) ?? new List<(int, int)> { here, home };
            march.Path = Encode(path);
            march.ArriveAtMs = Schedule(march.Path, nowMs, SpeedPercent(march.Troops));
            march.Stationed = false;
            march.Returning = true;
            return MarchResult.Ok;
        }

        /// <summary>行軍此刻所在的格子（路上就取兩點之間按比例的位置）。</summary>
        public static (int x, int y) CurrentTile(March m, long nowMs)
        {
            var (seg, pm) = Position(m, nowMs);
            var a = SurfaceLogic.FromTile(m.Path[seg]);
            if (seg >= m.Path.Count - 1) return a;
            var b = SurfaceLogic.FromTile(m.Path[seg + 1]);
            var p = (a.x + (b.x - a.x) * pm / 1000, a.y + (b.y - a.y) * pm / 1000);
            return SurfaceLogic.Walkable(p.Item1, p.Item2) ? p : a;
        }

        /// <summary>推進所有行軍到 nowMs：抵達終點就駐紮；召回抵達主城就解散。回傳發生的事件。</summary>
        public static List<MarchEvent> Advance(CityState city, long nowMs)
        {
            var events = new List<MarchEvent>();
            var state = city.Surface;
            for (int i = state.Marches.Count - 1; i >= 0; i--)
            {
                var m = state.Marches[i];
                if (m.Stationed || nowMs < m.ArriveAt) continue;
                if (m.Returning)
                {
                    foreach (var kv in m.Troops) city.Troops[kv.Key] = city.GetTroops(kv.Key) + kv.Value;
                    state.Marches.RemoveAt(i);
                    events.Add(new MarchEvent { MarchId = m.Id, Hero = m.Hero, Tile = m.Destination, Disbanded = true });
                    continue;
                }
                m.Stationed = true;
                events.Add(new MarchEvent { MarchId = m.Id, Hero = m.Hero, Tile = m.Destination });
            }
            return events;
        }
    }
}
