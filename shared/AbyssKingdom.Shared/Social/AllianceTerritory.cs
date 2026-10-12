using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.Map;

namespace AbyssKingdom.Shared.Social
{
    public enum FlagResult { Ok, InvalidSlot, BlockedPoint, Cooldown, SlotBusy, Conflict, Disconnected,
        InsufficientResources, Forbidden, NotMember, PersistenceFailed, NotFound }
    public sealed class AllianceFlag
    {
        public string Id { get; set; }
        public string PayerId { get; set; }
        public int Slot { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public long CreatedAtMs { get; set; }
        public long CompletesAtMs { get; set; }
        public bool Paid { get; set; }
        public bool Rejected { get; set; }
        public long RemovedAtMs { get; set; }
        public bool Live => !Rejected && RemovedAtMs == 0;
        public bool Active(long nowMs) => Live && Paid && nowMs >= CompletesAtMs;
    }

    public static class AllianceTerritory
    {
        public const int MaxFlags = 3, Radius = 12;
        public const long BuildMs = 300_000, RebuildCooldownMs = 86_400_000;
        public const int GatherBasisPoints = 11_000, DefenseBasisPoints = 10_500;
        public static long Cost(int slot) => slot >= 1 && slot <= MaxFlags ? slot * 10_000L : 0;
        private static readonly Lazy<HashSet<int>> Blocked = new Lazy<HashSet<int>>(BuildBlocked);
        private static HashSet<int> BuildBlocked()
        {
            var cells = new HashSet<int>();
            foreach (var site in SurfaceLogic.Sites())
                for (int dy = 0; dy < SurfaceConfig.CityFootprint; dy++)
                    for (int dx = 0; dx < SurfaceConfig.CityFootprint; dx++)
                        cells.Add(SurfaceLogic.Tile(site.x + dx, site.y + dy));
            foreach (var feature in SurfaceLogic.Features())
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        cells.Add(SurfaceLogic.Tile(feature.X + dx, feature.Y + dy));
            return cells;
        }
        public static bool AllowedPoint(int x, int y) => SurfaceLogic.Walkable(x, y) &&
            !SurfaceLogic.NearPass(x, y) && !Blocked.Value.Contains(SurfaceLogic.Tile(x, y));

        /// <summary>Four-direction shortest distance; never crosses a mountain, pass, or zone boundary.</summary>
        public static HashSet<int> Coverage(int x, int y, int radius = Radius)
        {
            var cells = new HashSet<int>();
            if (!SurfaceLogic.Walkable(x, y) || SurfaceLogic.NearPass(x, y)) return cells;
            var zone = SurfaceLogic.Zone(x, y);
            var queue = new Queue<(int x, int y, int distance)>();
            cells.Add(SurfaceLogic.Tile(x, y)); queue.Enqueue((x, y, 0));
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.distance >= radius) continue;
                foreach (var next in Neighbors(current.x, current.y))
                {
                    if (!SurfaceLogic.Walkable(next.x, next.y) || SurfaceLogic.NearPass(next.x, next.y) ||
                        SurfaceLogic.Zone(next.x, next.y) != zone) continue;
                    if (cells.Add(SurfaceLogic.Tile(next.x, next.y))) queue.Enqueue((next.x, next.y, current.distance + 1));
                }
            }
            return cells;
        }
        private static IEnumerable<(int x, int y)> Neighbors(int x, int y)
        {
            yield return (x - 1, y); yield return (x + 1, y); yield return (x, y - 1); yield return (x, y + 1);
        }
        public static bool Touches(HashSet<int> first, HashSet<int> second)
        {
            foreach (int tile in first)
            {
                if (second.Contains(tile)) return true;
                var p = SurfaceLogic.FromTile(tile);
                foreach (var n in Neighbors(p.x, p.y))
                    if (n.x >= 0 && n.y >= 0 && n.x < SurfaceConfig.Size && n.y < SurfaceConfig.Size &&
                        second.Contains(SurfaceLogic.Tile(n.x, n.y))) return true;
            }
            return false;
        }
        public static bool Connected(IReadOnlyList<HashSet<int>> regions)
        {
            if (regions.Count < 2) return true;
            var seen = new HashSet<int> { 0 }; var queue = new Queue<int>(); queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                for (int j = 0; j < regions.Count; j++)
                    if (!seen.Contains(j) && Touches(regions[i], regions[j])) { seen.Add(j); queue.Enqueue(j); }
            }
            return seen.Count == regions.Count;
        }
        public static bool Contains(IEnumerable<AllianceFlag> flags, int tile, long nowMs)
        {
            foreach (var flag in flags)
                if (flag.Active(nowMs) && Coverage(flag.X, flag.Y).Contains(tile)) return true;
            return false;
        }
    }
}
