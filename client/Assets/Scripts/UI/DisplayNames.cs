using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;

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
            { ResourceType.Wood, "根材" },
            { ResourceType.Stone, "層岩" },
            { ResourceType.Food, "菌糧" },
            { ResourceType.Crystal, "響晶" },
        };

        /// <summary>資源圖示上的單字。</summary>
        public static readonly Dictionary<ResourceType, string> ResourceGlyphs = new Dictionary<ResourceType, string>
        {
            { ResourceType.Wood, "根" },
            { ResourceType.Stone, "層" },
            { ResourceType.Food, "菌" },
            { ResourceType.Crystal, "響" },
        };

        public static readonly Dictionary<BuildingType, string> Buildings = new Dictionary<BuildingType, string>
        {
            { BuildingType.Keep, "主堡" },
            { BuildingType.Sawmill, "掘根場" },
            { BuildingType.Quarry, "層岩坑" },
            { BuildingType.Farm, "菌圃" },
            { BuildingType.CrystalMine, "響晶礦脈" },
            { BuildingType.Warehouse, "倉庫" },
            { BuildingType.Barracks, "兵營" },
        };

        public static readonly Dictionary<BuildingType, string> BuildingDescriptions = new Dictionary<BuildingType, string>
        {
            { BuildingType.Keep, "裂口邊的要塞核心。其他建築等級不可超過主堡。" },
            { BuildingType.Sawmill, "挖掘貫穿地層的古樹根，生產根材。" },
            { BuildingType.Quarry, "開採沉積的神話地層，生產層岩。" },
            { BuildingType.Farm, "在崖洞培育發光菇，生產菌糧。" },
            { BuildingType.CrystalMine, "採集凝固的回響，生產響晶。" },
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

        // ───── 主將 ─────

        public static readonly Dictionary<HeroId, string> Heroes = new Dictionary<HeroId, string>
        {
            { HeroId.Hold, "霍德" },
            { HeroId.Vesper, "薇絲珀" },
            { HeroId.Karst, "卡斯特" },
        };

        public static readonly Dictionary<HeroId, string> HeroTitles = new Dictionary<HeroId, string>
        {
            { HeroId.Hold, "鐵錘同盟 · 老盾衛隊長" },
            { HeroId.Vesper, "銀繩公會 · 探路射手" },
            { HeroId.Karst, "自由探窟團 · 崖壁索騎" },
        };

        public static readonly Dictionary<HeroId, string> HeroBios = new Dictionary<HeroId, string>
        {
            { HeroId.Hold, "在裂口邊守了三十年的錨，說話不多，退後的次數更少。" },
            { HeroId.Vesper, "聽得見弦上的回響，總是第一個發現往下的路。" },
            { HeroId.Karst, "把繩索當成道路的人，崖壁對他來說只是另一片平原。" },
        };

        /// <summary>繩級顯示名稱（品質）。</summary>
        public static readonly Dictionary<RopeGrade, string> Grades = new Dictionary<RopeGrade, string>
        {
            { RopeGrade.Common, "白繩 · 普通" },
            { RopeGrade.Uncommon, "綠繩 · 罕見" },
            { RopeGrade.Rare, "藍繩 · 稀有" },
            { RopeGrade.Epic, "紫繩 · 史詩" },
            { RopeGrade.Legendary, "金繩 · 傳說" },
            { RopeGrade.Mythic, "黑繩 · 神秘" },
        };

        // ───── 四境 ─────

        public static readonly Dictionary<Domain, string> Domains = new Dictionary<Domain, string>
        {
            { Domain.Power, "震境 · 力" },
            { Domain.Technique, "巧境 · 技" },
            { Domain.Speed, "迅境 · 速" },
            { Domain.Durability, "磐境 · 韌" },
        };

        public static readonly Dictionary<Domain, string> DomainGlyphs = new Dictionary<Domain, string>
        {
            { Domain.Power, "震" },
            { Domain.Technique, "巧" },
            { Domain.Speed, "迅" },
            { Domain.Durability, "磐" },
        };

        public static readonly Dictionary<Domain, string> DomainBonus = new Dictionary<Domain, string>
        {
            { Domain.Power, "通達：攻擊 +15%。修練：打出傷害。試煉：20 回合內擊倒不還手的重甲。" },
            { Domain.Technique, "通達：克制倍率 +15%，被克制時懲罰減少。修練：打出克制。試煉：對抗剋制你的敵人。" },
            { Domain.Speed, "通達：出手速度 +2。修練：搶先出手。試煉：和更快的同兵種敵人比誰先倒下。" },
            { Domain.Durability, "通達：承受傷害 -15%。修練：扛住傷害並活下來。試煉：撐過 20 回合不倒。" },
        };

        public static readonly Dictionary<DomainTier, string> Tiers = new Dictionary<DomainTier, string>
        {
            { DomainTier.Mortal, "凡身" },
            { DomainTier.Dual, "雙極" },
            { DomainTier.Triple, "三極" },
            { DomainTier.Perfect, "圓極" },
        };

        public static readonly Dictionary<EquipSlot, string> Slots = new Dictionary<EquipSlot, string>
        {
            { EquipSlot.Weapon, "武器" },
            { EquipSlot.Head, "頭部" },
            { EquipSlot.Body, "身軀" },
            { EquipSlot.Hands, "手部" },
            { EquipSlot.Accessory, "飾品" },
            { EquipSlot.Relic, "遺物" },
        };

        public static readonly Dictionary<GearId, string> Gear = new Dictionary<GearId, string>
        {
            { GearId.RustAnchorBlade, "鏽錨刃" },
            { GearId.RopeHood, "繩編兜帽" },
            { GearId.StrataPlate, "層岩胸甲" },
            { GearId.EchoGloves, "鳴指手套" },
            { GearId.LanternCharm, "燈魅護符" },
            { GearId.BoneBellRelic, "骨鈴遺物" },
            { GearId.BoneBellSpear, "骨鈴長矛" },
            { GearId.HundredEyeRib, "百眼傘骨" },
        };

        public static string GearStats(GearDef d)
        {
            var parts = new List<string>();
            if (d.Attack > 0) parts.Add($"攻 +{d.Attack}");
            if (d.Defense > 0) parts.Add($"防 +{d.Defense}");
            if (d.Hp > 0) parts.Add($"命 +{d.Hp}");
            if (d.Leadership > 0) parts.Add($"統 +{d.Leadership}");
            return $"{Grades[d.Grade]} · " + string.Join("  ", parts);
        }

        // ───── 魔物（第一章：餘燼緣野） ─────

        public static readonly Dictionary<MonsterId, string> Monsters = new Dictionary<MonsterId, string>
        {
            { MonsterId.MossChild, "苔面童" },
            { MonsterId.LanternWisp, "燈魅" },
            { MonsterId.BellRat, "骨鈴鼠" },
            { MonsterId.HundredEyeUmbrella, "百眼紙傘" },
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
            switch (item)
            {
                case ItemType.HeroExpSmall: return "主將經驗書（小）";
                case ItemType.HeroExpMedium: return "主將經驗書（中）";
                case ItemType.HeroExpLarge: return "主將經驗書（大）";
                case ItemType.DomainScrollPower: return "境書 · 震";
                case ItemType.DomainScrollTechnique: return "境書 · 巧";
                case ItemType.DomainScrollSpeed: return "境書 · 迅";
                case ItemType.DomainScrollDurability: return "境書 · 磐";
            }
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
                case "MaxLevel": return "主將已達等級上限";
                case "StageLocked": return "要先通關前一關";
                case "NotReady": return "修練值還沒滿，還不能挑戰試煉";
                case "AlreadyMastered": return "這一境已經通達";
                case "WrongTeamCount": return "岔路關卡需要兩支隊伍";
                case "EmptyTeam": return "每條路都需要至少一位主將";
                case "DuplicateHero": return "同一位主將不能同時走兩條路";
                case "ChapterComplete": return "本章已全部通關";
                case "NothingToClaim": return "目前沒有可領取的掛機收益";
                case "HeroNotOwned": return "尚未擁有這位主將";
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
