using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Map
{
    public sealed class GarrisonDefenseInput
    {
        public string OwnerId { get; set; }
        public string ReinforcementId { get; set; }
        public long MarchId { get; set; }
        public List<SquadInput> Squads { get; set; } = new List<SquadInput>();
    }

    public sealed class GarrisonOutcome
    {
        public string OwnerId { get; set; }
        public string ReinforcementId { get; set; }
        public long MarchId { get; set; }
        public Dictionary<TroopType, long> Losses { get; set; } = new Dictionary<TroopType, long>();
        public Dictionary<TroopType, long> Survivors { get; set; } = new Dictionary<TroopType, long>();
    }
}
