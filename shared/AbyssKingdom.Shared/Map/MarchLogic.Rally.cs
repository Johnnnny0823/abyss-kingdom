using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Shared.Map
{
    public static partial class MarchLogic
    {
        public static AllianceMilitaryResult StartRally(CityState owner, CityState leader, string rallyId, string joinId,
            HeroId hero, Dictionary<TroopType, long> troops, long nowMs, long departMs, bool isLeader, out long marchId)
        {
            marchId = 0;
            if (!owner.Surface.Placed || !leader.Surface.Placed) return AllianceMilitaryResult.NoCity;
            if (!owner.Heroes.ContainsKey(hero)) return AllianceMilitaryResult.HeroNotOwned;
            foreach (var m in owner.Surface.Marches) if (m.Hero == hero) return AllianceMilitaryResult.HeroBusy;
            if (owner.Surface.Marches.Count >= MarchConfig.MaxMarches) return AllianceMilitaryResult.TooManyMarches;
            var result = AllianceMilitaryRules.ValidateRallyParticipant(owner, troops);
            if (result != AllianceMilitaryResult.Ok) return result;
            var home = SurfaceLogic.CityCenter(owner.Surface); var destination = SurfaceLogic.CityCenter(leader.Surface);
            var path = FindPath(home.x, home.y, destination.x, destination.y);
            if (path == null) return AllianceMilitaryResult.Unreachable;
            var encoded = Encode(path); var schedule = Schedule(encoded, nowMs, SpeedPercent(troops));
            if (schedule[schedule.Count - 1] > departMs) return AllianceMilitaryResult.ArrivesLate;
            var march = new March { Id = ++owner.Surface.NextMarchId, Hero = hero, RallyId = rallyId, RallyJoinId = joinId,
                RallyLeader = isLeader, Path = encoded, ArriveAtMs = schedule, Stationed = isLeader };
            foreach (var p in troops) if (p.Value > 0) { march.Troops[p.Key] = p.Value; owner.Troops[p.Key] -= p.Value; }
            owner.Surface.Marches.Add(march); marchId = march.Id;
            return AllianceMilitaryResult.Ok;
        }
        public static MarchEvent ApplyCampOutcome(CityState owner, string settlementId, CampParticipantOutcome outcome,
            bool won, bool campGone, long nowMs, bool rally)
        {
            string receipt = "camp:outcome:" + settlementId;
            if (owner.SocialReceipts.Contains(receipt)) return null;
            var march = owner.Surface.Marches.Find(m => m.Id == outcome.MarchId);
            if (march == null) return null;
            int target = march.Destination;
            foreach (var pair in outcome.Survivors) march.Troops[pair.Key] = pair.Value;
            var casualties = CityLogic.TakeCasualties(owner, outcome.Losses);
            if (outcome.Report != null) DomainLogic.AwardFromBattle(owner, new List<HeroId> { march.Hero }, outcome.Report);
            if (won) owner.Stats.CampsWon++;
            foreach (var pair in outcome.Resources) march.Carry[pair.Key] = pair.Value;
            foreach (var pair in outcome.Items) march.LootItems[pair.Key] = pair.Value;
            march.CampPending = false; march.CampInputs = null;
            owner.SocialReceipts.Add(receipt);
            if (!campGone || rally) TurnBack(owner, march, nowMs);
            return new MarchEvent { MarchId = march.Id, Hero = march.Hero, Tile = target,
                BattleWon = won, BattleLost = !won && !campGone, CampGone = campGone,
                Carry = new Dictionary<ResourceType, long>(march.Carry), LootItems = new Dictionary<ItemType, long>(march.LootItems),
                Wounded = casualties.wounded, Dead = casualties.dead };
        }
        public static void ReturnRally(CityState owner, long marchId, long nowMs)
        {
            var march = owner.Surface.Marches.Find(m => m.Id == marchId);
            if (march != null && !march.Returning && !march.CampPending) TurnBack(owner, march, nowMs);
        }
        public static MarchEvent AdvanceRallyReturn(CityState owner, long marchId, long nowMs)
        {
            var march = owner.Surface.Marches.Find(m => m.Id == marchId);
            return march != null && march.Returning && nowMs >= march.ArriveAt ? CompleteReturn(owner, march) : null;
        }
    }
}
