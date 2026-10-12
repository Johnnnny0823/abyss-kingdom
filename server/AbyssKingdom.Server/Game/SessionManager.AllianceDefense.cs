using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Server.Game;

public sealed record AllianceDefenseResolution(bool Saved, PvpOutcome? Outcome = null, bool AlliedRetreat = false);

public sealed partial class SessionManager
{
    private static bool ReinforcementMembersValid(AllianceDirectory d, AllianceReinforcementRecord r)
    {
        var alliance = d.Alliances.FirstOrDefault(a => !a.PendingCreate && a.Id == r.AllianceId);
        return alliance != null && alliance.Members.Any(m => m.PlayerId == r.SenderId && m.Epoch == r.SenderEpoch) &&
            alliance.Members.Any(m => m.PlayerId == r.ReceiverId && m.Epoch == r.ReceiverEpoch);
    }

    /// <summary>
    /// Reserve the garrison battle, freeze one owner, persist the defender outcome, then persist
    /// the garrison owner's casualties. Never holds two player Gates at the same time.
    /// </summary>
    public async Task<AllianceDefenseResolution> ResolveAllianceDefenseAsync(string attackerId, string defenderId,
        List<(SquadInput squad, TroopType troop)> attackers, ulong seed, long nowMs, string battleId, CancellationToken ct)
    {
        await _allianceGate.WaitAsync(ct);
        try
        {
            var directory = await _allianceStore.LoadAsync(ct);
            var defender = await ReadCityAsync(defenderId, ct);
            if (defender == null) return new(false);
            if (defender.SiegeLog.TryGetValue(battleId, out var persisted))
            {
                if (!await SettleGarrisonLockedAsync(directory, persisted, battleId, nowMs, ct)) return new(false);
                return new(true, persisted);
            }
            bool allied = directory.Alliances.Any(a => !a.PendingCreate &&
                a.Members.Any(m => m.PlayerId == attackerId) && a.Members.Any(m => m.PlayerId == defenderId));
            bool alreadyFrozen = directory.Reinforcements.Any(r => r.ReceiverId == defenderId && r.SettlementId == battleId);
            if (allied && !alreadyFrozen)
            {
                var retreat = new PvpOutcome { Shielded = true };
                foreach (var squad in attackers) retreat.AttackerSurvivors[squad.troop] = squad.squad.Count;
                return new(true, retreat, true);
            }
            if (PvpLogic.IsShielded(defender, nowMs) && !alreadyFrozen)
            {
                var protectedOutcome = new PvpOutcome { Shielded = true };
                foreach (var squad in attackers) protectedOutcome.AttackerSurvivors[squad.troop] = squad.squad.Count;
                return new(true, protectedOutcome);
            }

            AllianceReinforcementRecord? record = null;
            var home = SurfaceLogic.CityCenter(defender.Surface);
            var defenseAlliance = directory.Alliances.FirstOrDefault(a => !a.PendingCreate && a.Members.Any(m => m.PlayerId == defenderId));
            int defenseBasisPoints = defenseAlliance != null && AllianceTerritory.Contains(defenseAlliance.Flags,
                SurfaceLogic.Tile(home.x, home.y), nowMs) ? AllianceTerritory.DefenseBasisPoints : 10_000;
            GarrisonDefenseInput? garrison = null;
            if (!PvpLogic.IsShielded(defender, nowMs) || alreadyFrozen)
            {
                record = directory.Reinforcements.FirstOrDefault(r => r.ReceiverId == defenderId &&
                    (r.State == ReinforcementState.Garrisoned ||
                     (r.State == ReinforcementState.Traveling && nowMs >= r.ArriveAtMs)));
                if (record?.SettlementId != null && record.SettlementId != battleId)
                    return new(false); // Older battle must settle before another battle may use these troops.
                if (record != null && (record.SettlementId == battleId ||
                    (!record.ReturnRequested && ReinforcementMembersValid(directory, record))))
                {
                    if (record.SettlementId == null)
                    {
                        record.SettlementId = battleId;
                        record.State = ReinforcementState.Garrisoned;
                        record.FrozenAttackers = attackers.Select(s => s.squad).ToList();
                        record.BattleSeed = seed;
                        record.BattleAtMs = nowMs;
                        record.DefenseBasisPoints = defenseBasisPoints;
                        record.BattleAttackerId = attackerId;
                        long.TryParse(battleId.Substring(battleId.LastIndexOf(':') + 1), out long attackerMarch);
                        record.BattleMarchId = attackerMarch;
                        directory.Version++;
                        await _allianceStore.SaveAsync(directory, ct);
                    }
                    bool eligible = false;
                    bool frozen = await ModifyCityAsync(record.SenderId, owner =>
                    {
                        MarchLogic.AdvanceReinforcement(owner, record.MarchId, nowMs);
                        var march = owner.Surface.Marches.FirstOrDefault(m => m.Id == record.MarchId &&
                            m.ReinforcementOperationId == record.Id);
                        if (march == null || !march.Stationed || march.Returning) return;
                        if (march.GarrisonSettlementId != null && march.GarrisonSettlementId != battleId) return;
                        march.GarrisonSettlementId = battleId;
                        eligible = true;
                        record.FrozenDefense ??= new GarrisonDefenseInput
                        {
                            OwnerId = record.SenderId, MarchId = march.Id, ReinforcementId = record.Id,
                            Squads = MarchLogic.Squads(owner, march).Select(s => s.squad).ToList()
                        };
                    }, ct, saveNow: true);
                    if (!frozen) return new(false);
                    if (eligible)
                    {
                        garrison = record.FrozenDefense;
                        attackers = record.FrozenAttackers!.Select(s => (s, s.Troop)).ToList();
                        seed = record.BattleSeed;
                        nowMs = record.BattleAtMs;
                        defenseBasisPoints = record.DefenseBasisPoints;
                    }
                    else { record.SettlementId = null; record.FrozenDefense = null; }
                    directory.Version++;
                    await _allianceStore.SaveAsync(directory, ct);
                }
                else record = null;
            }

            PvpOutcome? outcome = null;
            bool saved = await ModifyCityAsync(defenderId,
                city => outcome = PvpLogic.ResolveOnDefender(city, attackers, seed, nowMs, battleId, garrison, defenseBasisPoints),
                ct, saveNow: true);
            if (!saved || outcome == null) return new(false);
            if (outcome.Shielded && record?.SettlementId == battleId)
            {
                if (!await ReleaseShieldedGarrisonLockedAsync(directory, record, battleId, nowMs, ct)) return new(false);
            }
            else if (!await SettleGarrisonLockedAsync(directory, outcome, battleId, nowMs, ct)) return new(false);
            return new(true, outcome);
        }
        finally { _allianceGate.Release(); }
    }

    private async Task<bool> SettleGarrisonLockedAsync(AllianceDirectory directory, PvpOutcome outcome,
        string battleId, long nowMs, CancellationToken ct)
    {
        if (outcome.Garrison == null) return true;
        var damage = outcome.Garrison;
        var record = directory.Reinforcements.FirstOrDefault(r => r.Id == damage.ReinforcementId);
        if (record == null || record.SenderId != damage.OwnerId || record.MarchId != damage.MarchId) return false;
        bool applied = false;
        bool saved = await ModifyCityAsync(damage.OwnerId, owner =>
        {
            applied = PvpLogic.FinishGarrison(owner, damage, battleId, nowMs,
                record.ReturnRequested || !ReinforcementMembersValid(directory, record));
            if (!applied || record.SettlementId != battleId) return;
            UpdateSettledReinforcement(record, owner, nowMs);
        }, ct, saveNow: true);
        if (!saved || !applied) return false;
        // An older battle retry cannot clear a newer battle's freeze.
        if (record.SettlementId == battleId)
        {
            record.SettlementId = null;
            record.FrozenDefense = null;
            record.FrozenAttackers = null;
            directory.Version++;
            await _allianceStore.SaveAsync(directory, ct);
        }
        return true;
    }

    private async Task<bool> ReleaseShieldedGarrisonLockedAsync(AllianceDirectory directory,
        AllianceReinforcementRecord record, string battleId, long nowMs, CancellationToken ct)
    {
        bool saved = await ModifyCityAsync(record.SenderId, owner =>
        {
            var march = owner.Surface.Marches.FirstOrDefault(m => m.Id == record.MarchId);
            if (march?.GarrisonSettlementId == battleId) march.GarrisonSettlementId = null;
            if (march != null && (record.ReturnRequested || !ReinforcementMembersValid(directory, record)))
                MarchLogic.RecallReinforcement(owner, march.Id, nowMs);
            UpdateSettledReinforcement(record, owner, nowMs);
        }, ct, saveNow: true);
        if (!saved) return false;
        record.SettlementId = null;
        record.FrozenDefense = null;
        record.FrozenAttackers = null;
        directory.Version++;
        await _allianceStore.SaveAsync(directory, ct);
        return true;
    }

    private static void UpdateSettledReinforcement(AllianceReinforcementRecord record, CityState owner, long nowMs)
    {
        var march = owner.Surface.Marches.FirstOrDefault(m => m.Id == record.MarchId && m.ReinforcementOperationId == record.Id);
        if (march == null) { record.State = ReinforcementState.Returned; record.CurrentTroops.Clear(); return; }
        record.State = march.Returning ? ReinforcementState.Returning
            : march.Stationed ? ReinforcementState.Garrisoned : ReinforcementState.Traveling;
        record.ArriveAtMs = march.ArriveAt;
        record.CurrentTroops = new(march.Troops);
    }

    public async Task RecoverAllianceDefensesAsync(CancellationToken ct)
    {
        var directory = await _allianceStore.LoadAsync(ct);
        foreach (var record in directory.Reinforcements.Where(r => r.SettlementId != null && r.BattleAttackerId != null && r.BattleMarchId > 0))
        {
            var city = await ReadCityAsync(record.BattleAttackerId!, ct);
            var march = city?.Surface.Marches.FirstOrDefault(m => m.Id == record.BattleMarchId && m.Sieging);
            if (march == null || !_sieging.TryAdd((record.BattleAttackerId!, march.Id), 0)) continue;
            await ResolveAttackAsync(record.BattleAttackerId!, _world.NameOf(record.BattleAttackerId!), new MarchEvent
            {
                MarchId = march.Id, Hero = march.Hero, Tile = march.Destination, AttackTarget = record.ReceiverId,
                AttackSquads = record.FrozenAttackers?.Select(s => (s, s.Troop)).ToList() ?? MarchLogic.Squads(city!, march)
            }, ct);
        }
    }
}
