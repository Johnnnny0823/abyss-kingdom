using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;

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
            { BuildingType.Hospital, "醫館" },
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
            { BuildingType.Hospital, "收容戰鬥中倒下的士兵（七成成為傷兵、其餘陣亡），花資源與時間治療後回到兵營。等級越高收得越多。" },
        };

        // 兵種名稱（2026-10-08 使用者定案）
        public static readonly Dictionary<TroopType, string> Troops = new Dictionary<TroopType, string>
        {
            { TroopType.Shield, "磐盾衛" },
            { TroopType.Crossbow, "鳴弦手" },
            { TroopType.Rider, "裂蹄騎" },
        };

        public static readonly Dictionary<TroopType, string> TroopRoles = new Dictionary<TroopType, string>
        {
            { TroopType.Shield, "前排坦克 · 克制裂蹄騎" },
            { TroopType.Crossbow, "遠程輸出 · 克制磐盾衛" },
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
            { HeroId.Vesper, "銀弦公會 · 遊擊射手" },
            { HeroId.Karst, "自由傭兵團 · 疾蹄騎手" },
        };

        public static readonly Dictionary<HeroId, string> HeroBios = new Dictionary<HeroId, string>
        {
            { HeroId.Hold, "在裂口邊守了三十年的盾，說話不多，退後的次數更少。" },
            { HeroId.Vesper, "聽得見弦上的回響，總是第一個發現往下的路。" },
            { HeroId.Karst, "他說馬蹄比任何道路都可靠，整片大陸對他來說都太小了。" },
        };

        /// <summary>契級顯示名稱（品質）。</summary>
        public static readonly Dictionary<RopeGrade, string> Grades = new Dictionary<RopeGrade, string>
        {
            { RopeGrade.Common, "白契 · 普通" },
            { RopeGrade.Uncommon, "綠契 · 罕見" },
            { RopeGrade.Rare, "藍契 · 稀有" },
            { RopeGrade.Epic, "紫契 · 史詩" },
            { RopeGrade.Legendary, "金契 · 傳說" },
            { RopeGrade.Mythic, "黑契 · 神秘" },
        };

        // ───── 深淵地圖 ─────

        public static readonly Dictionary<SurfaceZone, string> Zones = new Dictionary<SurfaceZone, string>
        {
            { SurfaceZone.Rim, "崖緣（內環）" },
            { SurfaceZone.Middle, "中環" },
            { SurfaceZone.Outer, "外環（新手區）" },
            { SurfaceZone.Pit, "深淵" },
            { SurfaceZone.Edge, "地圖邊緣" },
        };

        /// <summary>「根材 600、層岩 200」這樣的文字；沒有東西回傳空字串。</summary>
        public static string CarryText(Dictionary<ResourceType, long> carry)
        {
            if (carry == null) return "";
            var parts = new List<string>();
            foreach (var kv in carry) if (kv.Value > 0) parts.Add($"{Resources[kv.Key]} {kv.Value:N0}");
            return string.Join("、", parts);
        }

        /// <summary>「磐盾衛 30、裂蹄騎 5」這樣的文字；沒有回傳空字串。</summary>
        public static string TroopsText(Dictionary<TroopType, long> troops)
        {
            if (troops == null) return "";
            var parts = new List<string>();
            foreach (var kv in troops) if (kv.Value > 0) parts.Add($"{Troops[kv.Key]} {kv.Value:N0}");
            return string.Join("、", parts);
        }

        public static string ItemsText(Dictionary<ItemType, long> items)
        {
            if (items == null) return "";
            var parts = new List<string>();
            foreach (var kv in items) if (kv.Value > 0) parts.Add($"{Item(kv.Key)} ×{kv.Value}");
            return string.Join("、", parts);
        }

        public static string FeatureName(Feature f) =>
            f.Type == FeatureType.ResourceField ? $"{Resources[f.Resource]}田 Lv.{f.Level}"
            : f.Type == FeatureType.MonsterCamp ? $"怪物營地 Lv.{f.Level}"
            : "深淵入口";

        public static readonly Dictionary<FeatureType, string> FeatureDescriptions = new Dictionary<FeatureType, string>
        {
            { FeatureType.ResourceField, "可以派部隊採集的資源田。越靠近深淵，等級越高。" },
            { FeatureType.MonsterCamp, "怪物盤踞的營地，擊敗牠們可以取得材料。" },
            { FeatureType.AbyssGate, "通往深淵下一層大陸的入口，賽季結束後開放。" },
        };

        // ───── 淵脈與浮沉（程式內部 = 五行陰陽，2026-10-08 使用者定案改用深淵風格名稱） ─────
        // 根剋層、層剋潮、潮剋燼、燼剋鐘、鐘剋根；根→燼→層→鐘→潮→根 相生。浮 = 陽、沉 = 陰。

        public static readonly Dictionary<Element, string> Elements = new Dictionary<Element, string>
        {
            { Element.None, "無" }, { Element.Wood, "根" }, { Element.Fire, "燼" },
            { Element.Earth, "層" }, { Element.Metal, "鐘" }, { Element.Water, "潮" },
        };

        public static string PolarityName(Polarity p) => p == Polarity.Yin ? "沉" : "浮";

        /// <summary>例如「浮層」「沉鐘」。</summary>
        public static string ElementLabel(Element e, Polarity p) =>
            e == Element.None ? "無脈" : PolarityName(p) + Elements[e];

        public static readonly Dictionary<Synergy, string> Synergies = new Dictionary<Synergy, string>
        {
            { Synergy.Triad, "三鳴" }, { Synergy.Quintet, "五鳴" },
            { Synergy.ThreeTalents, "交響" }, { Synergy.FiveComplete, "全脈" },
            { Synergy.Chain3, "脈流三" }, { Synergy.Chain4, "脈流四" }, { Synergy.Chain5, "脈流五" },
            { Synergy.Harmony, "浮沉調和" }, { Synergy.PureYang, "全浮" }, { Synergy.PureYin, "全沉" },
        };

        /// <summary>戰報用：脈剋標籤。</summary>
        public static string ElementTag(int percent) =>
            percent == AbyssKingdom.Shared.Battle.Elements.SamePolarityOvercome ? "（共鳴剋制 +25%）"
            : percent > 100 ? $"（脈剋 +{percent - 100}%）"
            : percent < 100 ? "（脈被剋）" : "";

        public static string SynergyList(IEnumerable<Synergy> list)
        {
            var parts = new List<string>();
            foreach (var s in list)
            {
                var (atk, red) = AbyssKingdom.Shared.Battle.Elements.Effect(s);
                var fx = new List<string>();
                if (atk > 0) fx.Add($"攻+{atk}%");
                if (red > 0) fx.Add($"受傷-{red}%");
                parts.Add($"{Synergies[s]}（{string.Join(" ", fx)}）");
            }
            return parts.Count == 0 ? "無" : string.Join("、", parts);
        }

        // ───── 四印（程式內部 = 四境；2026-10-08 使用者定案） ─────

        public static readonly Dictionary<Domain, string> Domains = new Dictionary<Domain, string>
        {
            { Domain.Power, "崩印 · 力" },
            { Domain.Technique, "巧印 · 技" },
            { Domain.Speed, "迅印 · 速" },
            { Domain.Durability, "磐印 · 韌" },
        };

        public static readonly Dictionary<Domain, string> DomainGlyphs = new Dictionary<Domain, string>
        {
            { Domain.Power, "崩" },
            { Domain.Technique, "巧" },
            { Domain.Speed, "迅" },
            { Domain.Durability, "磐" },
        };

        public static readonly Dictionary<Domain, string> DomainBonus = new Dictionary<Domain, string>
        {
            { Domain.Power, "崩印——擊碎一切的蠻力。刻成：攻擊 +15%。修練：打出傷害。印證試煉：20 回合內擊倒不還手的重甲。" },
            { Domain.Technique, "巧印——看穿破綻的技巧。刻成：克制倍率 +15%，被克制時懲罰減少。修練：打出克制。印證試煉：對抗剋制你的敵人。" },
            { Domain.Speed, "迅印——先發制人的速度。刻成：出手速度 +2。修練：搶先出手。印證試煉：和更快的同兵種敵人比誰先倒下。" },
            { Domain.Durability, "磐印——如磐石般不動。刻成：承受傷害 -15%。修練：扛住傷害並活下來。印證試煉：撐過 20 回合不倒。" },
        };

        public static readonly string[] StarTiers = { "金星", "藍星", "紅星", "銀彩星" };

        /// <summary>刻成幾道印的稱號。</summary>
        public static readonly Dictionary<DomainTier, string> Tiers = new Dictionary<DomainTier, string>
        {
            { DomainTier.Unbound, "尚未立契" },
            { DomainTier.Roped, "立契者" },
            { DomainTier.Delver, "承印者" },
            { DomainTier.Deepwalker, "深契者" },
            { DomainTier.AbyssReacher, "淵契主" },
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
            { GearId.RustAnchorBlade, "鏽鐵刃" },
            { GearId.RopeHood, "契紋兜帽" },
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
            { QueueType.Healing, "治療隊列" },
        };

        public static string Item(ItemType item)
        {
            switch (item)
            {
                case ItemType.HeroExpSmall: return "主將經驗書（小）";
                case ItemType.HeroExpMedium: return "主將經驗書（中）";
                case ItemType.HeroExpLarge: return "主將經驗書（大）";
                case ItemType.HeroShardAny: return "信物 · 通用";
                case ItemType.HeroShardHold: return "信物 · 霍德";
                case ItemType.HeroShardVesper: return "信物 · 薇絲珀";
                case ItemType.HeroShardKarst: return "信物 · 卡斯特";
                case ItemType.DomainScrollPower: return "印譜 · 崩";
                case ItemType.DomainScrollTechnique: return "印譜 · 巧";
                case ItemType.DomainScrollSpeed: return "印譜 · 迅";
                case ItemType.DomainScrollDurability: return "印譜 · 磐";
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
                case "Unreachable": return "到不了這裡";
                case "UnknownNode": return "這裡不能去（深淵、山脈或地圖外）";
                case "NoCity": return "還沒有主城位置";
                case "HeroBusy": return "這位主將正在行軍";
                case "NoTroops": return "至少要帶一名士兵";
                case "NotEnoughTroops": return "兵營的部隊不足";
                case "ExceedsLeadership": return "超過主將的統率";
                case "TooManyMarches": return "同時最多 3 支行軍";
                case "AlreadyThere": return "已經在這裡了";
                case "Sieging": return "正在攻城，戰鬥結算完才能召回";
                case "NotReady": return "修練值還沒滿，還不能挑戰印證試煉";
                case "AlreadyMastered": return "這道印已經刻成";
                case "StarsRequired": return "要先把對應的星色升滿，才能挑戰下一道印";
                case "NotEnoughShards": return "信物不足";
                case "MaxStars": return "已經升到最高星";
                case "WrongTeamCount": return "岔路關卡需要兩支隊伍";
                case "EmptyTeam": return "每條路都需要至少一位主將";
                case "DuplicateHero": return "同一位主將不能同時走兩條路";
                case "ChapterComplete": return "本章已全部通關";
                case "NothingToClaim": return "目前沒有可領取的掛機收益";
                case "HeroNotOwned": return "尚未擁有這位主將";
                case "Overlaps": return "這裡和其他建築重疊了";
                case "NothingToHeal": return "至少選一名傷兵";
                case "NotEnoughWounded": return "傷兵數量不足";
                case "OutOfBounds": return "超出城牆範圍";
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
