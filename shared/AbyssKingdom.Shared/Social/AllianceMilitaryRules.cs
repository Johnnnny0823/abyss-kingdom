using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Social
{
    public enum ReinforcementState { Reserved, Traveling, Garrisoned, Returning, Returned, Rejected }
    public enum AllianceMilitaryResult
    {
        Ok, KeepLocked, EmptyTroops, InvalidTroops, NotEnoughTroops, OverCapacity,
        NoCity, InvalidTarget, HeroNotOwned, HeroBusy, TooManyMarches, Unreachable, ArrivesLate
    }

    /// <summary>Server callers pass authoritative city states, never client-supplied levels or capacity flags.</summary>
    public static class AllianceMilitaryRules
    {
        public const int UnlockKeepLevel = 3;
        public static long PersonalCapacity(int keepLevel) => keepLevel < UnlockKeepLevel ? 0
            : 100L * Math.Min(keepLevel, CityConfig.MaxLevel);
        public static long RallyTotalCapacity(int leaderKeepLevel) => leaderKeepLevel < UnlockKeepLevel ? 0
            : 500L * Math.Min(leaderKeepLevel, CityConfig.MaxLevel);

        public static AllianceMilitaryResult ValidateReinforcement(CityState sender, CityState receiver,
            IReadOnlyDictionary<TroopType, long> troops) => ValidateTroops(sender, troops,
                Math.Min(PersonalCapacity(sender.GetLevel(BuildingType.Keep)),
                    PersonalCapacity(receiver.GetLevel(BuildingType.Keep))));

        public static AllianceMilitaryResult ValidateRallyParticipant(CityState participant,
            IReadOnlyDictionary<TroopType, long> troops) => ValidateTroops(participant, troops,
                PersonalCapacity(participant.GetLevel(BuildingType.Keep)));

        private static AllianceMilitaryResult ValidateTroops(CityState sender,
            IReadOnlyDictionary<TroopType, long> troops, long capacity)
        {
            if (capacity == 0) return AllianceMilitaryResult.KeepLocked;
            if (troops == null || troops.Count == 0) return AllianceMilitaryResult.EmptyTroops;
            long total = 0;
            foreach (var (type, count) in troops)
            {
                if (!Enum.IsDefined(typeof(TroopType), type) || count < 0)
                    return AllianceMilitaryResult.InvalidTroops;
                if (count > sender.GetTroops(type)) return AllianceMilitaryResult.NotEnoughTroops;
                if (count > capacity - total) return AllianceMilitaryResult.OverCapacity;
                total += count;
            }
            return total > 0 ? AllianceMilitaryResult.Ok : AllianceMilitaryResult.EmptyTroops;
        }
    }
}
