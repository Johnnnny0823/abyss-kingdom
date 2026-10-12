namespace AbyssKingdom.Shared.City
{
    /// <summary>城內擴張區。顯示名稱在客戶端 DisplayNames.cs；enum 名稱只是程式識別用。</summary>
    public enum CityZoneId
    {
        Housing = 0,   // 民居區
        Research = 1,  // 研究區
    }

    /// <summary>擴張區：格子範圍（左上角 X, Y 與寬高，可以是負座標）、主堡門檻、要開放的建築功能是否已上線。</summary>
    public sealed class CityZone
    {
        public CityZoneId Id { get; }
        public int X { get; }
        public int Y { get; }
        public int W { get; }
        public int H { get; }
        public int MinKeepLevel { get; }
        /// <summary>這區的建築功能做好了沒（沒做好之前一律蓋著霧，不提前開出空地）。</summary>
        public bool FeatureReady { get; }

        public CityZone(CityZoneId id, int x, int y, int w, int h, int minKeepLevel, bool featureReady)
        {
            Id = id; X = x; Y = y; W = w; H = h; MinKeepLevel = minKeepLevel; FeatureReady = featureReady;
        }

        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;
    }

    /// <summary>
    /// 城內區域擴張（GDD 5.9）：CityLayout 的 GridSize × GridSize 是核心區，外圍擴張區被深淵升起的霧蓋住，
    /// 主堡等級到了、而且該區的建築功能已上線才開放。北側（y &lt; 0）是往下的深淵懸崖，不會開放。
    /// </summary>
    public static class CityZones
    {
        public static readonly CityZone[] All =
        {
            new CityZone(CityZoneId.Housing, CityLayout.GridSize, 0, 12, CityLayout.GridSize, 3, false),
            new CityZone(CityZoneId.Research, -12, 0, 12, CityLayout.GridSize, 5, false),
        };

        public static bool IsOpen(CityZone zone, int keepLevel) => zone.FeatureReady && keepLevel >= zone.MinKeepLevel;

        /// <summary>格子 (x, y) 屬於哪個擴張區；核心區或擴張區以外回傳 null。</summary>
        public static CityZone ZoneAt(int x, int y)
        {
            foreach (var z in All)
                if (z.Contains(x, y)) return z;
            return null;
        }
    }
}
