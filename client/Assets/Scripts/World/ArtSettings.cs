using System;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 美術風格開關。建築優先用新版 3D 模型（Resources/Models/City3D），沒有模型的建築自動用 2D 建築圖。
    /// 要整個換回 2D：把 City3D 改成 false，或啟動參數加 -city2d。
    /// （2026-10-09 使用者：「先用 3D，大地圖上的建築也是，要換回 2D 隨時可以」）
    /// </summary>
    public static class ArtSettings
    {
        public static readonly bool City3D = Array.IndexOf(Environment.GetCommandLineArgs(), "-city2d") < 0;

        /// <summary>建築等級對應的外觀：1 = Lv.1～3、2 = Lv.4～6、3 = Lv.7～10（城內與大地圖主城共用）。</summary>
        public static int TierOf(int level) => level <= 3 ? 1 : level <= 6 ? 2 : 3;
    }
}
