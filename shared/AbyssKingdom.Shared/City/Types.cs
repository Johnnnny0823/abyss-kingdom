namespace AbyssKingdom.Shared.City
{
    /// <summary>基礎資源。數值一律用 long 整數，避免浮點誤差。</summary>
    public enum ResourceType
    {
        Wood = 0,     // 木材
        Stone = 1,    // 石材
        Food = 2,     // 糧食
        Crystal = 3,  // 晶礦
    }

    /// <summary>MVP 的 7 種建築：主堡 + 6 種。</summary>
    public enum BuildingType
    {
        Keep = 0,         // 主堡：其他建築等級不可超過主堡
        Sawmill = 1,      // 伐木場：產木材
        Quarry = 2,       // 採石場：產石材
        Farm = 3,         // 農田：產糧食
        CrystalMine = 4,  // 晶礦場：產晶礦
        Warehouse = 5,    // 倉庫：提高資源上限
        Barracks = 6,     // 兵營：之後訓練部隊用
    }

    /// <summary>開始升級的結果碼，客戶端依此顯示錯誤訊息。</summary>
    public enum UpgradeResult
    {
        Ok = 0,
        AlreadyMaxLevel = 1,
        QueueBusy = 2,
        KeepLevelTooLow = 3,
        NotEnoughResources = 4,
        UnknownBuilding = 5,
    }

    /// <summary>
    /// 兵種。名稱暫定，之後會依深淵世界觀改名——enum 名稱只是程式識別用，改名只需改顯示文字。
    /// 克制（第 8–9 週戰鬥用）：盾衛 → 獵騎 → 弩手 → 盾衛。
    /// </summary>
    public enum TroopType
    {
        Shield = 0,    // 盾衛：前排坦克
        Crossbow = 1,  // 弩手：遠程輸出
        Rider = 2,     // 獵騎：機動突擊
    }

    /// <summary>開始訓練的結果碼。</summary>
    public enum TrainResult
    {
        Ok = 0,
        BarracksRequired = 1,
        QueueBusy = 2,
        InvalidCount = 3,
        ExceedsBatchLimit = 4,
        NotEnoughResources = 5,
        UnknownTroop = 6,
    }
}
