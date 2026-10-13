using System;
using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Playtesting
{
    public sealed class PlayIntent
    {
        public string Type;
        public object Data;
        public string Reason;
        public PlayIntent(string type, object data, string reason) { Type = type; Data = data; Reason = reason; }
    }
    /// <summary>Player-only intent policy, shared by the socket bot and opt-in Unity diagnostic.</summary>
    public static class AiPlaytestPolicy
    {
        public static readonly HashSet<string> Allowed = new HashSet<string>
        {
            MessageTypes.CityGet, MessageTypes.CityUpgrade, MessageTypes.CityTrain, MessageTypes.CitySpeedup,
            MessageTypes.QuestClaim, MessageTypes.MapMarch, MessageTypes.MapRecall, MessageTypes.HeroLevelUp,
            MessageTypes.ExpeditionBattle, MessageTypes.ExpeditionClaimIdle, MessageTypes.CityHeal, MessageTypes.FeedbackSend
        };
        public static PlayIntent Next(CityState c, string profile, int turn, long now)
        {
            var q = QuestLogic.Current(c);
            if (q != null && QuestLogic.IsDone(c, q)) return new PlayIntent(MessageTypes.QuestClaim, null, "claim quest " + q.Id);
            foreach (var queue in new[] { QueueType.Building, QueueType.Training, QueueType.Healing })
            {
                long finish = queue == QueueType.Building ? c.Upgrade?.FinishAtMs ?? 0 :
                    queue == QueueType.Training ? c.Training?.FinishAtMs ?? 0 : c.Healing?.FinishAtMs ?? 0;
                if (finish > now && finish - now <= CityConfig.FreeSpeedupMs)
                    return new PlayIntent(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = queue }, "free complete " + queue);
            }
            if (profile == "random" && turn % 7 == 0)
                return new PlayIntent(MessageTypes.CityUpgrade, new UpgradeRequest { Building = (BuildingType)(turn % 8) }, "probe upgrade rejection");
            if (profile == "random")
            {
                var legal = CityConfig.AllBuildings.Where(b => CityLogic.CanUpgrade(c, b) == UpgradeResult.Ok).ToArray();
                if (legal.Length > 0) return Upgrade(c, legal[new Random(turn + 42).Next(legal.Length)]);
            }
            if (profile != "quest" && c.Training == null && c.GetLevel(BuildingType.Barracks) > 0 &&
                c.GetTroops(TroopType.Shield) < 300 && CityLogic.CanTrain(c, TroopType.Shield, 50) == TrainResult.Ok)
                return new PlayIntent(MessageTypes.CityTrain, new TrainRequest { Troop = TroopType.Shield, Count = 50 }, "train 50");
            if (profile != "quest" && c.Healing == null && c.GetLevel(BuildingType.Hospital) > 0 && c.Wounded.Values.Sum() > 0)
                return new PlayIntent(MessageTypes.CityHeal, new HealRequest { Troops = new Dictionary<TroopType, long>(c.Wounded) }, "heal wounded");
            if (q != null)
            {
                switch (q.Goal)
                {
                    case QuestGoal.BuildingLevel:
                        var upgrade = Upgrade(c, q.Building);
                        if (upgrade != null || profile == "quest") return upgrade;
                        break;
                    case QuestGoal.ProducersLevel:
                        return Upgrade(c, new[] { BuildingType.Sawmill, BuildingType.Quarry, BuildingType.Farm, BuildingType.CrystalMine }.OrderBy(c.GetLevel).First());
                    case QuestGoal.TroopsTrained:
                        if (c.Training == null && CityLogic.CanTrain(c, TroopType.Shield, 50) == TrainResult.Ok)
                            return new PlayIntent(MessageTypes.CityTrain, new TrainRequest { Troop = TroopType.Shield, Count = 50 }, "quest training");
                        break;
                    case QuestGoal.GatherTrips:
                        var gathering = March(c, FeatureType.ResourceField);
                        if (gathering != null || profile == "quest") return gathering;
                        break;
                    case QuestGoal.CampsWon:
                        var camping = March(c, FeatureType.MonsterCamp);
                        if (camping != null || profile == "quest") return camping;
                        break;
                    case QuestGoal.HeroLevel:
                        var hero = c.Heroes.OrderByDescending(h => h.Value.Level).First().Key;
                        foreach (var item in new[] { ItemType.HeroExpLarge, ItemType.HeroExpMedium, ItemType.HeroExpSmall })
                            if (c.GetItemCount(item) > 0)
                                return new PlayIntent(MessageTypes.HeroLevelUp, new HeroLevelUpRequest { Hero = hero, Item = item, Count = 1 }, "use owned experience book");
                        break;
                    case QuestGoal.ExpeditionCleared:
                        return new PlayIntent(MessageTypes.ExpeditionBattle, new ExpeditionBattleRequest
                        { Stage = c.Expedition.Cleared + 1, Teams = new List<List<HeroId>> { c.Heroes.Keys.ToList() } }, "quest expedition");
                    case QuestGoal.Power:
                        foreach (var b in CityConfig.AllBuildings.OrderBy(c.GetLevel))
                            if (CityLogic.CanUpgrade(c, b) == UpgradeResult.Ok) return Upgrade(c, b);
                        break;
                }
            }
            if (profile != "quest")
            {
                foreach (var b in CityConfig.AllBuildings.OrderBy(c.GetLevel))
                    if (CityLogic.CanUpgrade(c, b) == UpgradeResult.Ok) return Upgrade(c, b);
                var march = March(c, turn % 3 == 0 ? FeatureType.MonsterCamp : FeatureType.ResourceField);
                if (march != null) return march;
            }
            return null;
        }
        private static PlayIntent Upgrade(CityState c, BuildingType b)
        {
            if (b == BuildingType.Keep)
                foreach (var p in CityConfig.KeepPrerequisites(c.GetLevel(b) + 1))
                    if (c.GetLevel(p.building) < p.level) return Upgrade(c, p.building);
            return CityLogic.CanUpgrade(c, b) == UpgradeResult.Ok ? new PlayIntent(MessageTypes.CityUpgrade,
                new UpgradeRequest { Building = b }, "upgrade " + b) : null;
        }
        private static PlayIntent March(CityState c, FeatureType type)
        {
            if (c.Surface.Marches.Count > 0 || c.GetTroops(TroopType.Shield) == 0) return null;
            var hero = c.Heroes.Keys.FirstOrDefault(h => !c.Surface.Marches.Any(m => m.Hero == h));
            var home = SurfaceLogic.CityCenter(c.Surface);
            var target = SurfaceLogic.Features().Where(f => f.Type == type && SurfaceLogic.Zone(f.X, f.Y) == SurfaceLogic.Zone(home.x, home.y) &&
                (type != FeatureType.MonsterCamp || f.Level == 1)).OrderBy(f => SurfaceLogic.Dist2(home.x, home.y, f.X, f.Y)).First();
            return new PlayIntent(MessageTypes.MapMarch, new MapMarchRequest { Hero = hero, X = target.X, Y = target.Y,
                Troops = new Dictionary<TroopType, long> { [TroopType.Shield] = Math.Min(200, c.GetTroops(TroopType.Shield)) } }, "nearest " + type);
        }
    }
}
