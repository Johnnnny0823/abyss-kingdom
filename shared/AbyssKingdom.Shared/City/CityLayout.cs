using System.Collections.Generic;

namespace AbyssKingdom.Shared.City
{
    /// <summary>城內格子座標（建築佔地的左上角）。</summary>
    public class Cell
    {
        public int X { get; set; }
        public int Y { get; set; }

        public Cell() { }
        public Cell(int x, int y) { X = x; Y = y; }
    }

    /// <summary>移動建築的結果碼。</summary>
    public enum MoveResult
    {
        Ok = 0,
        OutOfBounds = 1,
        Overlaps = 2,
        UnknownBuilding = 3,
    }

    /// <summary>
    /// 城內畫面的建築擺放：GridSize × GridSize 的格子，每棟建築佔一塊正方形，不可重疊、不可超出邊界。
    /// 每種建築一棟，未建造（0 級）的建築也有預留位置（顯示成空地基）。
    /// </summary>
    public static class CityLayout
    {
        public const int GridSize = 24;

        /// <summary>建築佔地邊長（格）。</summary>
        public static int Footprint(BuildingType type) => type == BuildingType.Keep ? 4 : 3;

        static readonly Dictionary<BuildingType, Cell> Defaults = new Dictionary<BuildingType, Cell>
        {
            { BuildingType.Keep, new Cell(10, 10) },
            { BuildingType.Sawmill, new Cell(4, 4) },
            { BuildingType.Quarry, new Cell(17, 4) },
            { BuildingType.Farm, new Cell(4, 17) },
            { BuildingType.CrystalMine, new Cell(17, 17) },
            { BuildingType.Warehouse, new Cell(10, 4) },
            { BuildingType.Barracks, new Cell(10, 17) },
            { BuildingType.Hospital, new Cell(17, 10) },
        };

        public static Cell DefaultCell(BuildingType type) =>
            Defaults.TryGetValue(type, out var c) ? new Cell(c.X, c.Y) : new Cell(0, 0);

        public static bool InBounds(BuildingType type, int x, int y)
        {
            int f = Footprint(type);
            return x >= 0 && y >= 0 && x + f <= GridSize && y + f <= GridSize;
        }

        public static bool Overlaps(BuildingType a, int ax, int ay, BuildingType b, int bx, int by)
        {
            int fa = Footprint(a), fb = Footprint(b);
            return ax < bx + fb && bx < ax + fa && ay < by + fb && by < ay + fa;
        }

        /// <summary>這棟建築目前的位置（舊存檔沒有時回傳預設位置）。</summary>
        public static Cell CellOf(CityState s, BuildingType type) =>
            s.Layout != null && s.Layout.TryGetValue(type, out var c) ? c : DefaultCell(type);

        public static MoveResult CanMove(CityState s, BuildingType type, int x, int y)
        {
            if (!Defaults.ContainsKey(type)) return MoveResult.UnknownBuilding;
            if (!InBounds(type, x, y)) return MoveResult.OutOfBounds;
            foreach (var other in CityConfig.AllBuildings)
            {
                if (other == type) continue;
                var c = CellOf(s, other);
                if (Overlaps(type, x, y, other, c.X, c.Y)) return MoveResult.Overlaps;
            }
            return MoveResult.Ok;
        }

        public static MoveResult Move(CityState s, BuildingType type, int x, int y)
        {
            var r = CanMove(s, type, x, y);
            if (r != MoveResult.Ok) return r;
            EnsureLayout(s);
            s.Layout[type] = new Cell(x, y);
            return MoveResult.Ok;
        }

        /// <summary>
        /// 補齊／修正擺放：缺少、超出邊界或和前面建築重疊的，放回預設位置；預設位置被佔就找第一塊空地。
        /// 有改動時回傳 true（舊存檔登入時呼叫）。
        /// </summary>
        public static bool EnsureLayout(CityState s)
        {
            if (s.Layout == null) s.Layout = new Dictionary<BuildingType, Cell>();
            bool changed = false;
            var placed = new List<BuildingType>();
            foreach (var type in CityConfig.AllBuildings)
            {
                if (!(s.Layout.TryGetValue(type, out var c) && c != null && Fits(s, placed, type, c.X, c.Y)))
                {
                    s.Layout[type] = FindSpot(s, placed, type);
                    changed = true;
                }
                placed.Add(type);
            }
            return changed;
        }

        static bool Fits(CityState s, List<BuildingType> placed, BuildingType type, int x, int y)
        {
            if (!InBounds(type, x, y)) return false;
            foreach (var other in placed)
            {
                var c = s.Layout[other];
                if (Overlaps(type, x, y, other, c.X, c.Y)) return false;
            }
            return true;
        }

        static Cell FindSpot(CityState s, List<BuildingType> placed, BuildingType type)
        {
            var d = DefaultCell(type);
            if (Fits(s, placed, type, d.X, d.Y)) return d;
            for (int y = 0; y < GridSize; y++)
                for (int x = 0; x < GridSize; x++)
                    if (Fits(s, placed, type, x, y)) return new Cell(x, y);
            return d; // 格子塞滿（不會發生：7 棟建築遠小於 24×24）
        }
    }
}
