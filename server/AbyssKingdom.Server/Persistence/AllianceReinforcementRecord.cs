using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Battle;

namespace AbyssKingdom.Server.Persistence;

/// <summary>Durable slot reservation and dispatch intent; retained as an idempotency receipt.</summary>
public sealed class AllianceReinforcementRecord
{
    public string Id { get; set; } = "";
    public string AllianceId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string ReceiverId { get; set; } = "";
    public long SenderEpoch { get; set; }
    public long ReceiverEpoch { get; set; }
    public HeroId Hero { get; set; }
    public Dictionary<TroopType, long> Troops { get; set; } = new();
    public Dictionary<TroopType, long> CurrentTroops { get; set; } = new();
    public long MarchId { get; set; }
    public ReinforcementState State { get; set; }
    public AllianceMilitaryResult MilitaryResult { get; set; }
    public bool ReturnRequested { get; set; }
    public long CreatedAtMs { get; set; }
    public long ArriveAtMs { get; set; }
    public string? SettlementId { get; set; }
    public GarrisonDefenseInput? FrozenDefense { get; set; }
    public List<SquadInput>? FrozenAttackers { get; set; }
    public ulong BattleSeed { get; set; }
    public long BattleAtMs { get; set; }
    public int DefenseBasisPoints { get; set; } = 10_000;
    public string? BattleAttackerId { get; set; }
    public long BattleMarchId { get; set; }
}
