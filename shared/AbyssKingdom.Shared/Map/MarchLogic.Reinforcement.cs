using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Shared.Map
{
    public static partial class MarchLogic
    {
        /// <summary>Advances only this reinforcement, so offline recovery cannot swallow other march events.</summary>
        public static MarchEvent AdvanceReinforcement(CityState city, long marchId, long nowMs)
        {
            var march = city.Surface.Marches.Find(m => m.Id == marchId);
            if (march == null || string.IsNullOrEmpty(march.ReinforcementTarget) || march.Stationed || nowMs < march.ArriveAt)
                return null;
            if (march.Returning) return CompleteReturn(city, march);
            march.Stationed = true;
            return new MarchEvent { MarchId = march.Id, Hero = march.Hero, Tile = march.Destination,
                ReinforcementTarget = march.ReinforcementTarget };
        }

        public static MarchResult RecallReinforcement(CityState city, long marchId, long nowMs)
        {
            var march = city.Surface.Marches.Find(m => m.Id == marchId);
            if (march == null || string.IsNullOrEmpty(march.ReinforcementTarget)) return MarchResult.UnknownMarch;
            if (!string.IsNullOrEmpty(march.GarrisonSettlementId)) return MarchResult.Sieging;
            if (!march.Returning) TurnBack(city, march, nowMs);
            return MarchResult.Ok;
        }

        /// <summary>
        /// The server must reserve the recipient slot and verify membership before calling this.
        /// Inputs are authoritative cities. Troops remain owned by the sender throughout travel.
        /// </summary>
        public static AllianceMilitaryResult StartReinforcement(CityState sender, CityState receiver,
            string targetId, HeroId hero, Dictionary<TroopType, long> troops, long nowMs, out long marchId)
        {
            marchId = 0;
            if (!sender.Surface.Placed || !receiver.Surface.Placed) return AllianceMilitaryResult.NoCity;
            if (string.IsNullOrWhiteSpace(targetId) || ReferenceEquals(sender, receiver) ||
                (sender.Surface.X == receiver.Surface.X && sender.Surface.Y == receiver.Surface.Y))
                return AllianceMilitaryResult.InvalidTarget;
            if (!sender.Heroes.ContainsKey(hero)) return AllianceMilitaryResult.HeroNotOwned;
            foreach (var m in sender.Surface.Marches)
                if (m.Hero == hero) return AllianceMilitaryResult.HeroBusy;
            if (sender.Surface.Marches.Count >= MarchConfig.MaxMarches) return AllianceMilitaryResult.TooManyMarches;
            var validation = AllianceMilitaryRules.ValidateReinforcement(sender, receiver, troops);
            if (validation != AllianceMilitaryResult.Ok) return validation;
            var home = SurfaceLogic.CityCenter(sender.Surface);
            var destination = SurfaceLogic.CityCenter(receiver.Surface);
            var path = FindPath(home.x, home.y, destination.x, destination.y);
            if (path == null) return AllianceMilitaryResult.Unreachable;

            var march = new March { Id = ++sender.Surface.NextMarchId, Hero = hero,
                ReinforcementTarget = targetId, Path = Encode(path) };
            foreach (var pair in troops)
            {
                if (pair.Value == 0) continue;
                march.Troops[pair.Key] = pair.Value;
                sender.Troops[pair.Key] = sender.GetTroops(pair.Key) - pair.Value;
            }
            march.ArriveAtMs = Schedule(march.Path, nowMs, SpeedPercent(march.Troops));
            sender.Surface.Marches.Add(march);
            marchId = march.Id;
            return AllianceMilitaryResult.Ok;
        }
    }
}
