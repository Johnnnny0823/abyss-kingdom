using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Server.Persistence;

public enum RallyState { Forming, Launching, Traveling, Settling, Returning, Finished }
public sealed class RallyParticipantRecord
{
    public string OwnerId { get; set; } = "";
    public string JoinId { get; set; } = "";
    public long Epoch { get; set; }
    public HeroId Hero { get; set; }
    public Dictionary<TroopType, long> Troops { get; set; } = new();
    public long MarchId { get; set; }
    public bool Paid { get; set; }
    public bool Left { get; set; }
    public bool Returned { get; set; }
    public long ReturnAtMs { get; set; }
    public AllianceMilitaryResult MilitaryResult { get; set; }
}
public sealed class AllianceRallyRecord
{
    public string Id { get; set; } = "";
    public string AllianceId { get; set; } = "";
    public string LeaderId { get; set; } = "";
    public long LeaderEpoch { get; set; }
    public int HostTile { get; set; }
    public int TargetTile { get; set; }
    public int Level { get; set; }
    public long Capacity { get; set; }
    public long DepartAtMs { get; set; }
    public int Seconds { get; set; } = 180;
    public RallyState State { get; set; }
    public bool CancelRequested { get; set; }
    public bool Cancelled { get; set; }
    public List<RallyParticipantRecord> Participants { get; set; } = new();
    public List<int> Path { get; set; } = new();
    public List<long> Times { get; set; } = new();
    public List<CampParticipantInput> FrozenInputs { get; set; } = new();
    public CampSettlementOutcome? Outcome { get; set; }
}
