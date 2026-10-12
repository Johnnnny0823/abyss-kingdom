using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Server.Persistence;

public sealed class CampSettlementRecord
{
    public string Id { get; set; } = "";
    public int Tile { get; set; }
    public int Level { get; set; }
    public long AtMs { get; set; }
    public ulong Seed { get; set; }
    public bool Rally { get; set; }
    public bool Gone { get; set; }
    public List<CampParticipantInput> Inputs { get; set; } = new();
    public CampSettlementOutcome? Outcome { get; set; }
    public HashSet<string> AppliedOwners { get; set; } = new();
    public bool Terminal { get; set; }
}
