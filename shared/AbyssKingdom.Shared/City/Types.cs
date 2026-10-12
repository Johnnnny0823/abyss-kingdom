namespace AbyssKingdom.Shared.City
{
    /// <summary>基礎資源。數值一律用 long 整數，避免浮點誤差。</summary>
    public enum ResourceType
    {
        Wood = 0,     // 根材
        Stone = 1,    // 層岩
        Food = 2,     // 菌糧
        Crystal = 3,  // 響晶
    }

    /// <summary>建築：主堡 + 7 種。</summary>
    public enum BuildingType
    {
        Keep = 0,         // 主堡：其他建築等級不可超過主堡
        Sawmill = 1,      // 掘根場：產根材
        Quarry = 2,       // 層岩坑：產層岩
        Farm = 3,         // 農田：產菌糧
        CrystalMine = 4,  // 響響晶脈：產響晶
        Warehouse = 5,    // 倉庫：提高資源上限
        Barracks = 6,     // 兵營：之後訓練部隊用
        Hospital = 7,     // 醫館：收容並治療傷兵
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
        KeepPrerequisiteMissing = 6,
    }

    /// <summary>
    /// 兵種。顯示名稱在客戶端 DisplayNames.cs（沉錨衛／鳴弦手／懸索騎）；enum 名稱只是程式識別用。
    /// 克制：沉錨衛(Shield) → 懸索騎(Rider) → 鳴弦手(Crossbow) → 沉錨衛。
    /// </summary>
    public enum TroopType
    {
        Shield = 0,    // 沉錨衛：前排坦克
        Crossbow = 1,  // 鳴弦手：遠程輸出
        Rider = 2,     // 懸索騎：機動突擊
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
