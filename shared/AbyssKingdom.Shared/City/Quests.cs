using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Shared.City
{
    /// <summary>任務的完成條件種類。</summary>
    public enum QuestGoal
    {
        BuildingLevel = 0,      // Building 升到 Target 級
        TroopsTrained = 1,      // 累計訓練完成 Target 名士兵
        GatherTrips = 2,        // 累計採集 Target 次（採滿或召回時有採到東西）
        CampsWon = 3,           // 累計擊敗 Target 座魔物營地
        HeroLevel = 4,          // 任一主將升到 Target 級
        ExpeditionCleared = 5,  // 遠征通關第 Target 關
        Power = 6,              // 總戰力達到 Target
        ProducersLevel = 7,     // 四座生產建築都升到 Target 級
    }

    /// <summary>一個任務：條件與獎勵。玩家看到的文字由客戶端依條件組出來（DisplayNames）。</summary>
    public sealed class QuestDef
    {
        public int Id;
        public QuestGoal Goal;
        public BuildingType Building;
        public long Target;
        public Dictionary<ResourceType, long> Resources = new Dictionary<ResourceType, long>();
        public Dictionary<ItemType, long> Items = new Dictionary<ItemType, long>();
    }

    /// <summary>為了任務條件而累計的數字（存在城市裡）。</summary>
    public class QuestStats
    {
        public long TroopsTrained { get; set; }
        public long GatherTrips { get; set; }
        public long CampsWon { get; set; }
    }

    public enum QuestResult
    {
        Ok = 0,
        NoQuest = 1,   // 這一章的任務都領完了
        NotDone = 2,   // 條件還沒達成
    }

    /// <summary>
    /// 新手任務線（第一章）：一次只顯示一個任務，照新玩家自然的成長順序帶路——
    /// 升主堡、蓋兵營、練兵、採集、打營地、升主將、推遠征、拉高戰力。領獎後才換下一個。
    /// 2026-10-11 依 SLG 對照分析（任務引導是寒霜啟示錄等作品前期留存的關鍵）加入。
    /// </summary>
    public static class QuestConfig
    {
        static Dictionary<ResourceType, long> Res(long wood, long stone, long food, long crystal = 0)
        {
            var d = new Dictionary<ResourceType, long>();
            if (wood > 0) d[ResourceType.Wood] = wood;
            if (stone > 0) d[ResourceType.Stone] = stone;
            if (food > 0) d[ResourceType.Food] = food;
            if (crystal > 0) d[ResourceType.Crystal] = crystal;
            return d;
        }

        static Dictionary<ItemType, long> Items(params (ItemType item, long n)[] list)
        {
            var d = new Dictionary<ItemType, long>();
            foreach (var (item, n) in list) d[item] = n;
            return d;
        }

        public static readonly QuestDef[] Chapter1 =
        {
            new QuestDef { Id = 1, Goal = QuestGoal.BuildingLevel, Building = BuildingType.Keep, Target = 2, Resources = Res(1000, 1000, 500), Items = Items((ItemType.SpeedupBuilding5m, 2)) },
            new QuestDef { Id = 2, Goal = QuestGoal.BuildingLevel, Building = BuildingType.Barracks, Target = 1, Resources = Res(500, 500, 1500) },
            new QuestDef { Id = 3, Goal = QuestGoal.TroopsTrained, Target = 50, Resources = Res(800, 800, 1500), Items = Items((ItemType.SpeedupTraining5m, 2)) },
            new QuestDef { Id = 4, Goal = QuestGoal.BuildingLevel, Building = BuildingType.Sawmill, Target = 2, Resources = Res(0, 1000, 1000) },
            new QuestDef { Id = 5, Goal = QuestGoal.BuildingLevel, Building = BuildingType.Farm, Target = 2, Resources = Res(1000, 1000, 0) },
            new QuestDef { Id = 6, Goal = QuestGoal.GatherTrips, Target = 1, Resources = Res(1000, 1000, 1000), Items = Items((ItemType.HeroExpSmall, 3)) },
            new QuestDef { Id = 7, Goal = QuestGoal.BuildingLevel, Building = BuildingType.Warehouse, Target = 1, Resources = Res(1500, 1500, 0) },
            new QuestDef { Id = 8, Goal = QuestGoal.BuildingLevel, Building = BuildingType.Hospital, Target = 1, Resources = Res(1000, 1000, 1000) },
            new QuestDef { Id = 9, Goal = QuestGoal.CampsWon, Target = 1, Resources = Res(1000, 1000, 1000, 200), Items = Items((ItemType.HeroExpSmall, 5)) },
            new QuestDef { Id = 10, Goal = QuestGoal.HeroLevel, Target = 5, Resources = Res(1000, 1000, 1000), Items = Items((ItemType.HeroExpMedium, 2)) },
            new QuestDef { Id = 11, Goal = QuestGoal.ExpeditionCleared, Target = 3, Resources = Res(1500, 1500, 1500, 300) },
            new QuestDef { Id = 12, Goal = QuestGoal.BuildingLevel, Building = BuildingType.Keep, Target = 3, Resources = Res(2000, 2000, 1000), Items = Items((ItemType.SpeedupBuilding30m, 1)) },
            new QuestDef { Id = 13, Goal = QuestGoal.ProducersLevel, Target = 3, Resources = Res(2000, 2000, 2000, 500) },
            new QuestDef { Id = 14, Goal = QuestGoal.Power, Target = 10000, Resources = Res(2000, 2000, 2000), Items = Items((ItemType.SpeedupGeneral30m, 2)) },
            new QuestDef { Id = 15, Goal = QuestGoal.BuildingLevel, Building = BuildingType.Keep, Target = 4, Resources = Res(3000, 3000, 3000, 800), Items = Items((ItemType.HeroExpLarge, 1)) },
        };
    }

    public static class QuestLogic
    {
        static readonly BuildingType[] Producers = { BuildingType.Sawmill, BuildingType.Quarry, BuildingType.Farm, BuildingType.CrystalMine };

        /// <summary>目前的任務（這一章都領完回傳 null）。</summary>
        public static QuestDef Current(CityState city) =>
            city.QuestIndex >= 0 && city.QuestIndex < QuestConfig.Chapter1.Length ? QuestConfig.Chapter1[city.QuestIndex] : null;

        /// <summary>目前進度（不超過目標）。</summary>
        public static long Progress(CityState city, QuestDef q)
        {
            long v;
            switch (q.Goal)
            {
                case QuestGoal.BuildingLevel: v = city.GetLevel(q.Building); break;
                case QuestGoal.TroopsTrained: v = city.Stats.TroopsTrained; break;
                case QuestGoal.GatherTrips: v = city.Stats.GatherTrips; break;
                case QuestGoal.CampsWon: v = city.Stats.CampsWon; break;
                case QuestGoal.HeroLevel:
                    v = 0;
                    foreach (var h in city.Heroes.Values) v = Math.Max(v, h.Level);
                    break;
                case QuestGoal.ExpeditionCleared: v = city.Expedition.Cleared; break;
                case QuestGoal.Power: v = PowerLogic.TotalPower(city); break;
                case QuestGoal.ProducersLevel:
                    v = long.MaxValue;
                    foreach (var b in Producers) v = Math.Min(v, city.GetLevel(b));
                    break;
                default: v = 0; break;
            }
            return Math.Min(v, q.Target);
        }

        public static bool IsDone(CityState city, QuestDef q) => Progress(city, q) >= q.Target;

        /// <summary>領取目前任務的獎勵（資源直接入庫，不受倉庫上限限制，和遠征獎勵一樣），換下一個任務。</summary>
        public static QuestResult Claim(CityState city)
        {
            var q = Current(city);
            if (q == null) return QuestResult.NoQuest;
            if (!IsDone(city, q)) return QuestResult.NotDone;
            foreach (var kv in q.Resources) city.Resources[kv.Key] = city.GetResource(kv.Key) + kv.Value;
            foreach (var kv in q.Items) city.Items[kv.Key] = city.GetItemCount(kv.Key) + kv.Value;
            city.QuestIndex++;
            return QuestResult.Ok;
        }
    }
}
