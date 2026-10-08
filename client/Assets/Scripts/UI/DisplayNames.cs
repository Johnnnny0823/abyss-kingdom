using System.Collections.Generic;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// 所有顯示給玩家看的名稱與說明集中在這裡。
    /// 兵種、建築等名稱目前是暫定，之後依「深淵」世界觀改名時只需要改這個檔案（程式內部用 enum，不受影響）。
    /// </summary>
    public static class DisplayNames
    {
        public static readonly Dictionary<ResourceType, string> Resources = new Dictionary<ResourceType, string>
        {
            { ResourceType.Wood, "木材" },
            { ResourceType.Stone, "石材" },
            { ResourceType.Food, "糧食" },
            { ResourceType.Crystal, "晶礦" },
        };

        /// <summary>資源圖示上的單字。</summary>
        public static readonly Dictionary<ResourceType, string> ResourceGlyphs = new Dictionary<ResourceType, string>
        {
            { ResourceType.Wood, "木" },
            { ResourceType.Stone, "石" },
            { ResourceType.Food, "糧" },
            { ResourceType.Crystal, "晶" },
        };

        public static readonly Dictionary<BuildingType, string> Buildings = new Dictionary<BuildingType, string>
        {
            { BuildingType.Keep, "主堡" },
            { BuildingType.Sawmill, "伐木場" },
            { BuildingType.Quarry, "採石場" },
            { BuildingType.Farm, "農田" },
            { BuildingType.CrystalMine, "晶礦場" },
            { BuildingType.Warehouse, "倉庫" },
            { BuildingType.Barracks, "兵營" },
        };

        public static readonly Dictionary<BuildingType, string> BuildingDescriptions = new Dictionary<BuildingType, string>
        {
            { BuildingType.Keep, "裂口邊的要塞核心。其他建築等級不可超過主堡。" },
            { BuildingType.Sawmill, "砍伐裂口森林，生產木材。" },
            { BuildingType.Quarry, "開採崖壁岩層，生產石材。" },
            { BuildingType.Farm, "耕作邊境田地，生產糧食。" },
            { BuildingType.CrystalMine, "挖掘深處滲出的晶礦。" },
            { BuildingType.Warehouse, "提高每種資源的存放上限。" },
            { BuildingType.Barracks, "訓練部隊。等級越高，單批可訓練越多。" },
        };

        // 兵種名稱（2026-10-08 使用者定案）
        public static readonly Dictionary<TroopType, string> Troops = new Dictionary<TroopType, string>
        {
            { TroopType.Shield, "沉錨衛" },
            { TroopType.Crossbow, "鳴弦手" },
            { TroopType.Rider, "懸索騎" },
        };

        public static readonly Dictionary<TroopType, string> TroopRoles = new Dictionary<TroopType, string>
        {
            { TroopType.Shield, "前排坦克 · 克制懸索騎" },
            { TroopType.Crossbow, "遠程輸出 · 克制沉錨衛" },
            { TroopType.Rider, "機動突擊 · 克制鳴弦手" },
        };

        public static readonly Dictionary<ItemCategory, string> ItemCategories = new Dictionary<ItemCategory, string>
        {
            { ItemCategory.General, "通用" },
            { ItemCategory.Building, "建築" },
            { ItemCategory.Research, "科研" },
            { ItemCategory.Magic, "魔導" },
            { ItemCategory.Training, "訓練" },
        };

        public static readonly Dictionary<QueueType, string> Queues = new Dictionary<QueueType, string>
        {
            { QueueType.Building, "建築隊列" },
            { QueueType.Training, "訓練隊列" },
        };

        public static string Item(ItemType item)
        {
            ItemConfig.TryGetSpeedup(item, out var category, out var ms);
            long minutes = ms / ItemConfig.MsPerMinute;
            string time = minutes >= 60 ? $"{minutes / 60} 小時" : $"{minutes} 分鐘";
            return $"{ItemCategories[category]}加速 {time}";
        }

        /// <summary>伺服器回傳的錯誤碼 → 給玩家看的說明。</summary>
        public static string Error(string code, string detail)
        {
            switch (detail)
            {
                case "NotEnoughResources": return "資源不足";
                case "QueueBusy": return "隊列忙碌中";
                case "KeepLevelTooLow": return "需要先升級主堡";
                case "AlreadyMaxLevel": return "已達最高等級";
                case "BarracksRequired": return "需要先建造兵營";
                case "ExceedsBatchLimit": return "超過單批訓練上限";
                case "InvalidCount": return "數量不正確";
                case "NotFreeYet": return "剩餘時間超過 5 分鐘，無法免費完成";
                case "NotEnoughItems": return "道具不足";
                case "ItemNotApplicable": return "這個道具不能用在這裡";
                case "NothingInQueue":
                case "NothingToCancel": return "隊列是空的";
                case "invalid token": return "登入驗證失敗，請重新登入";
            }
            return $"{code}：{detail}";
        }

        /// <summary>CanUpgrade 的結果 → 按鈕上的短說明。</summary>
        public static string UpgradeBlocker(UpgradeResult r)
        {
            switch (r)
            {
                case UpgradeResult.AlreadyMaxLevel: return "已滿級";
                case UpgradeResult.QueueBusy: return "隊列忙碌";
                case UpgradeResult.KeepLevelTooLow: return "需升級主堡";
                case UpgradeResult.NotEnoughResources: return "資源不足";
                default: return "無法升級";
            }
        }

        public static string Duration(long ms)
        {
            long s = (ms + 999) / 1000;
            if (s < 0) s = 0;
            long h = s / 3600, m = s / 60 % 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m:00}:{sec:00}";
        }
    }
}
