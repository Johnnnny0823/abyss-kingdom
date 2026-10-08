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

        // ───── 深淵地圖 ─────

        public static readonly Dictionary<NodeType, string> NodeTypes = new Dictionary<NodeType, string>
        {
            { NodeType.Gate, "垂降口" },
            { NodeType.Cavern, "資源洞室" },
            { NodeType.MonsterNest, "深淵小怪巢" },
            { NodeType.Fissure, "地脈裂隙" },
            { NodeType.ShaftHub, "升降井樞紐" },
            { NodeType.GuardianLair, "守層者巢穴" },
            { NodeType.CitySite, "主城用地" },
            { NodeType.ResourceField, "資源田" },
            { NodeType.MonsterCamp, "怪物營地" },
            { NodeType.Pass, "山谷關隘" },
        };

        public static readonly Dictionary<SurfaceZone, string> Zones = new Dictionary<SurfaceZone, string>
        {
            { SurfaceZone.Rim, "崖緣（內環）" },
            { SurfaceZone.Middle, "中環" },
            { SurfaceZone.Outer, "外環（新手區）" },
        };

        /// <summary>地表節點的單字圖示。</summary>
        public static string SurfaceGlyph(MapNode n)
        {
            switch (n.Type)
            {
                case NodeType.ResourceField: return ResourceGlyphs[n.Resource] + n.Level;
                case NodeType.MonsterCamp: return "怪";
                case NodeType.Pass: return "隘";
                case NodeType.Gate: return "降";
                default: return "地";
            }
        }

        public static string SurfaceNodeName(MapNode n) =>
            n.Type == NodeType.ResourceField ? $"{Resources[n.Resource]}田 Lv.{n.Level}"
            : n.Type == NodeType.MonsterCamp ? $"怪物營地 Lv.{n.Level}"
            : n.Type == NodeType.Gate ? "垂降口（深淵入口）"
            : NodeTypes[n.Type];

        public static readonly Dictionary<NodeType, string> NodeDescriptions = new Dictionary<NodeType, string>
        {
            { NodeType.Gate, "從裂口邊緣垂降下來的入口，探窟隊由此進出深淵。" },
            { NodeType.Cavern, "岩壁間的洞室，散落著可採集的資源。" },
            { NodeType.MonsterNest, "深淵小怪盤踞的巢穴。清掉牠們可以取得材料。" },
            { NodeType.Fissure, "地脈從岩層裂縫湧出。佔領後，同脈的主將會得到加成，並產出稀有資源。" },
            { NodeType.ShaftHub, "連通上下層的升降井樞紐。控制樞紐，就控制了誰能往下走、往上撤。" },
            { NodeType.GuardianLair, "守層者「百眼紙傘」的巢穴。這一層最危險的地方。" },
            { NodeType.CitySite, "空的主城用地。之後可以遷城到這裡。" },
            { NodeType.ResourceField, "可以派部隊採集的資源田。越靠近深淵，等級越高。" },
            { NodeType.MonsterCamp, "怪物盤踞的營地，擊敗牠們可以取得材料。" },
            { NodeType.Pass, "山谷中的關隘，是分區之間唯一的通道。聯盟會爭奪這裡。" },
        };

        /// <summary>節點上的單字圖示；裂隙顯示它的淵脈。</summary>
        public static string NodeGlyph(NodeType t, Element e)
        {
            switch (t)
            {
                case NodeType.Gate: return "降";
                case NodeType.MonsterNest: return "巢";
                case NodeType.Fissure: return e == Element.None ? "脈" : Elements[e];
                case NodeType.ShaftHub: return "井";
                case NodeType.GuardianLair: return "守";
                default: return "洞";
            }
        }

        public static string NodeName(MapNode n) =>
            n.Type == NodeType.Fissure ? $"地脈裂隙 · {Elements[n.Element]}脈" : NodeTypes[n.Type];

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

        // ───── 四股索（程式內部 = 四境；2026-10-08 使用者定案） ─────

        public static readonly Dictionary<Domain, string> Domains = new Dictionary<Domain, string>
        {
            { Domain.Power, "崩索 · 力" },
            { Domain.Technique, "結索 · 技" },
            { Domain.Speed, "墜索 · 速" },
            { Domain.Durability, "錨索 · 韌" },
        };

        public static readonly Dictionary<Domain, string> DomainGlyphs = new Dictionary<Domain, string>
        {
            { Domain.Power, "崩" },
            { Domain.Technique, "結" },
            { Domain.Speed, "墜" },
            { Domain.Durability, "錨" },
        };

        public static readonly Dictionary<Domain, string> DomainBonus = new Dictionary<Domain, string>
        {
            { Domain.Power, "崩索——一擊崩開岩壁的力量。編成：攻擊 +15%。修練：打出傷害。斷索試煉：20 回合內擊倒不還手的重甲。" },
            { Domain.Technique, "結索——打結與收放的技巧。編成：克制倍率 +15%，被克制時懲罰減少。修練：打出克制。斷索試煉：對抗剋制你的敵人。" },
            { Domain.Speed, "墜索——深淵裡最快的是墜落。編成：出手速度 +2。修練：搶先出手。斷索試煉：和更快的同兵種敵人比誰先倒下。" },
            { Domain.Durability, "錨索——釘死在崖壁不鬆動。編成：承受傷害 -15%。修練：扛住傷害並活下來。斷索試煉：撐過 20 回合不倒。" },
        };

        public static readonly string[] StarTiers = { "金星", "藍星", "紅星", "銀彩星" };

        /// <summary>編成幾股索的稱號。</summary>
        public static readonly Dictionary<DomainTier, string> Tiers = new Dictionary<DomainTier, string>
        {
            { DomainTier.Unbound, "尚未繫索" },
            { DomainTier.Roped, "繫索者" },
            { DomainTier.Delver, "下探者" },
            { DomainTier.Deepwalker, "深行者" },
            { DomainTier.AbyssReacher, "抵淵者" },
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
                case ItemType.HeroShardAny: return "信物 · 通用";
                case ItemType.HeroShardHold: return "信物 · 霍德";
                case ItemType.HeroShardVesper: return "信物 · 薇絲珀";
                case ItemType.HeroShardKarst: return "信物 · 卡斯特";
                case ItemType.DomainScrollPower: return "索譜 · 崩";
                case ItemType.DomainScrollTechnique: return "索譜 · 結";
                case ItemType.DomainScrollSpeed: return "索譜 · 墜";
                case ItemType.DomainScrollDurability: return "索譜 · 錨";
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
                case "UnknownNode": return "找不到這個節點";
                case "Unreachable": return "到不了：只能走已探索的廊道，封鎖力場要繞路";
                case "HeroBusy": return "這位主將正在行軍";
                case "NoTroops": return "至少要帶一名士兵";
                case "NotEnoughTroops": return "兵營的部隊不足";
                case "ExceedsLeadership": return "超過主將的統率";
                case "TooManyMarches": return "同時最多 3 支行軍";
                case "NotStationed": return "行軍抵達後才能召回";
                case "AlreadyThere": return "已經在這裡了";
                case "NotReady": return "修練值還沒滿，還不能挑戰斷索試煉";
                case "AlreadyMastered": return "這股索已經編成";
                case "StarsRequired": return "要先把對應的星色升滿，才能挑戰下一股索";
                case "NotEnoughShards": return "信物不足";
                case "MaxStars": return "已經升到最高星";
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
