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
        public static readonly Dictionary<int, string> AllianceGuide = new Dictionary<int, string>
        {
            [1] = "加入聯盟：主堡 Lv.2 後尋找盟友",
            [2] = "幫助一次：盟友有升級或治療隊列時，按幫助全部",
            [4] = "參與集結：至少兩人到齊出發，無須強制建立聯盟或插旗"
        };
        public const string FlagDescription = "盟主與幹部使用根材、層岩建造盟旗。生效的同區領地內，盟友採集速度 +10%、城防防禦 +5%；不疊加，也不阻擋任何領主通行。";
        public const string AbyssWorldDescription = "永暗之喉。深處的回聲預示著下一次深淵潮，異變將席捲地表。";
        public static readonly Dictionary<ResourceType, string> Resources = new Dictionary<ResourceType, string>
        {
            { ResourceType.Wood, "根材" },
            { ResourceType.Stone, "層岩" },
            { ResourceType.Food, "菌糧" },
            { ResourceType.Crystal, "響晶" },
        };

        /// <summary>資源圖示上的單字。</summary>
        /// <summary>資源說明（點上方資源列時顯示）：由來與用途。</summary>
        public static readonly Dictionary<ResourceType, string> ResourceDescriptions = new Dictionary<ResourceType, string>
        {
            { ResourceType.Wood, "貫穿各個地層的古樹根，堅韌耐用。\n用途：建築升級；訓練磐盾衛、鳴弦手。" },
            { ResourceType.Stone, "一層層神話地層壓成的岩石。\n用途：建築升級；訓練磐盾衛、裂蹄騎。" },
            { ResourceType.Food, "崖洞裡的發光菇，士兵的口糧。\n用途：建築升級；訓練所有兵種、治療傷兵。" },
            { ResourceType.Crystal, "凝固的回響，帶著深淵的力量。\n用途：主堡升級；訓練裂蹄騎；主將升星。" },
        };

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

        /// <summary>城內擴張區（GDD 5.9「城內區域擴張」）。</summary>
        public static readonly Dictionary<CityZoneId, string> CityZones = new Dictionary<CityZoneId, string>
        {
            { CityZoneId.Housing, "民居區" },
            { CityZoneId.Research, "研究區" },
        };

        public static readonly Dictionary<CityZoneId, string> CityZoneDescriptions = new Dictionary<CityZoneId, string>
        {
            { CityZoneId.Housing, "探窟者居住的地方。蓋好民居，居民才能入城，被安排到各建築裡工作。" },
            { CityZoneId.Research, "研究所蓋在這裡，研究發展、經濟與戰鬥的長期加成。" },
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
        /// <summary>新手任務的說明文字（依條件組出來）。</summary>
        public static string QuestText(QuestDef q)
        {
            switch (q.Goal)
            {
                case QuestGoal.BuildingLevel: return q.Target == 1 ? $"建造{Buildings[q.Building]}" : $"將{Buildings[q.Building]}升到 {q.Target} 級";
                case QuestGoal.TroopsTrained: return $"累計訓練 {q.Target:N0} 名士兵";
                case QuestGoal.GatherTrips: return q.Target == 1 ? "派部隊到資源田採集一次" : $"累計採集 {q.Target} 次";
                case QuestGoal.CampsWon: return q.Target == 1 ? "擊敗一座魔物營地" : $"累計擊敗 {q.Target} 座魔物營地";
                case QuestGoal.HeroLevel: return $"任一主將升到 {q.Target} 級";
                case QuestGoal.ExpeditionCleared: return $"遠征通關第 {q.Target} 關";
                case QuestGoal.Power: return $"總戰力達到 {q.Target:N0}";
                case QuestGoal.ProducersLevel: return $"四座生產建築都升到 {q.Target} 級";
                default: return "";
            }
        }

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
            { FeatureType.AbyssGate, "通往深淵的入口。之後開放「探險深淵」。" },
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

        /// <summary>道具說明（背包點選時顯示）。</summary>
        public static string ItemDescription(ItemType item)
        {
            if (ItemConfig.TryGetSpeedup(item, out var category, out var ms))
            {
                string where = category == ItemCategory.General ? "任何隊列" : $"{ItemCategories[category]}隊列";
                long m = ms / ItemConfig.MsPerMinute;
                return $"讓{where}的剩餘時間減少 {(m >= 60 ? $"{m / 60} 小時" : $"{m} 分鐘")}。在隊列旁按「加速」使用。";
            }
            if (ItemConfig.TryGetHeroExp(item, out var exp)) return $"主將獲得 {exp:N0} 經驗。在主將頁使用。";
            if (ItemConfig.TryGetDomainScroll(item, out var domain, out var xp)) return $"{Domains[domain]} 修練值 +{xp:N0}。在主將頁的四印使用。";
            if (item == ItemType.HeroShardAny) return "任何主將升星都能用的信物。";
            foreach (var h in Heroes)
                if (ItemConfig.ShardFor(h.Key) == item) return $"{h.Value}的信物，升星用。";
            return "";
        }

        /// <summary>道具品質（格子邊框顏色）：0 普通、1 精良、2 稀有、3 史詩。</summary>
        public static int ItemRarity(ItemType item)
        {
            if (ItemConfig.TryGetSpeedup(item, out _, out var ms))
                return ms >= 60 * ItemConfig.MsPerMinute ? 3 : ms >= 30 * ItemConfig.MsPerMinute ? 2 : ms >= 5 * ItemConfig.MsPerMinute ? 1 : 0;
            switch (item)
            {
                case ItemType.HeroExpSmall: return 1;
                case ItemType.HeroExpMedium: return 2;
                case ItemType.HeroExpLarge: return 3;
                case ItemType.HeroShardAny: return 3;
            }
            return item >= ItemType.HeroShardAny ? 3 : 2;
        }

        /// <summary>格子角落的小標籤：加速道具顯示時間（5分、1時）。</summary>
        public static string ItemBadge(ItemType item)
        {
            if (!ItemConfig.TryGetSpeedup(item, out _, out var ms)) return "";
            long m = ms / ItemConfig.MsPerMinute;
            return m >= 60 ? $"{m / 60}時" : $"{m}分";
        }

        /// <summary>伺服器回傳的錯誤碼 → 給玩家看的說明。</summary>
        public static string Error(string code, string detail)
        {
            if (detail != null && detail.StartsWith("Military:")) detail = detail.Substring(9);
            if (detail != null && detail.StartsWith("Flag:"))
            {
                if (detail == "Flag:Cooldown") return "此盟旗槽位撤旗／取消後需冷卻 24 小時";
                if (detail == "Flag:InsufficientResources") return "支付此槽盟旗所需的根材或層岩不足";
                detail = detail.Substring(5);
            }
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
                case "NotDone": return "任務還沒完成";
                case "NoQuest": return "這一章的任務都完成了";
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
                case "AlliedCity": return "不能攻擊同盟主城";
                case "LevelTooLow": return "主堡等級不足";
                case "Cooldown": return "退出或被移除後，需等待 24 小時才能加入或建立聯盟";
                case "Full": return "聯盟已滿員";
                case "AlreadyMember": return "你已加入聯盟";
                case "ApprovalRequired": return "此聯盟需要申請審核，或可直接加入";
                case "ApplicationLimit": return "最多同時申請三個聯盟";
                case "NoApplication": return "申請已失效";
                case "Forbidden": return "你的聯盟階級沒有此權限";
                case "OfficerLimit": return "最多四名幹部";
                case "LeaderMustTransfer": return "盟主須先轉讓職位，或在只剩自己時解散";
                case "NameTaken": return "聯盟名稱或簡稱已有人使用";
                case "InvalidName": return "名稱或簡稱格式不正確";
                case "InsufficientResources": return "創盟所需資源不足";
                case "PersistenceFailed": return "伺服器暫時無法存檔，請稍後重試";
                case "NotMember": return "你目前不在聯盟中";
                case "PendingOperations": return "仍有出發或戰鬥結算中的操作，請等待完成";
                case "InvalidTarget": return "目標已失效，請重新整理聯盟頁";
                case "InvalidMessage": return "訊息不可為空、包含控制字元或超過字數上限";
                case "RateLimited": return "發言太頻繁，請稍後再試";
                case "Muted": return "你目前被禁言";
                case "HelpUnavailable": return "隊列已完成、取消，或不能請求幫助";
                case "ReinforcementsClosed": return "盟友暫時關閉新援軍，請先與對方聯絡";
                case "ReinforcementSlotBusy": return "此城已有駐軍或在途援軍，最多接收一支";
                case "KeepLocked": return "派出與接收援軍雙方都需要主堡 Lv.3";
                case "OverCapacity": return "兵種總人數超過派出或收援方的主堡容量";
                case "EmptyTroops": return "至少派出一名士兵";
                case "InvalidTroops": return "兵種或數量不正確";
                case "ArrivesLate": return "預計抵達晚於集結出發，請選較長倒數或較近的集結";
                case "InvalidSlot": return "只能使用三個盟旗槽位";
                case "BlockedPoint": return "此處是主城候選位置、固定物件、山脈或關隘，不能建旗";
                case "SlotBusy": return "此盟旗槽位已使用";
                case "Conflict": return "領地與其他聯盟生效或施工中的領地重疊";
                case "Disconnected": return "領地必須連接生效盟旗；第一旗須在同區盟友主城 10 格內。撤旗也不能斷開領地";
            }
            return $"{code}：{detail}";
        }

        public static string RallyState(string state) => state switch
        {
            "Forming" => "等待集結", "Launching" => "出發準備中", "Traveling" => "遠征中",
            "Settling" => "戰鬥結算中", "Returning" => "各自返城", _ => "已結束"
        };
        public static string RallyOutcome(string outcome) => outcome switch
        {
            "Victory" => "集結勝利，戰利品隨各隊返城入庫", "Defeat" => "集結戰敗，傷兵回各自醫館",
            "CampGone" => "營地已被其他部隊擊敗，全隊無戰損、無獎勵返還", _ => "集結已取消，部隊返還"
        };

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

        public const string WorldFriendlyLegend = "● 自己／同盟（淺藍）";
        public const string WorldOtherLegend = "● 非同盟（紅）";
        public const string AlliedCityDescription = "同聯盟的主城，可以從聯盟頁派援軍協防，不能互相攻城。";

        public static string Duration(long ms)
        {
            long s = (ms + 999) / 1000;
            if (s < 0) s = 0;
            long h = s / 3600, m = s / 60 % 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m:00}:{sec:00}";
        }
    }
}
