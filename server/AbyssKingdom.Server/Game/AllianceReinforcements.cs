using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

public sealed record ReinforcementActionResult(AllianceResult Result,
    AllianceMilitaryResult MilitaryResult = AllianceMilitaryResult.Ok, string? Id = null);

public sealed partial class AllianceService
{
    private static bool OccupiesSlot(AllianceReinforcementRecord r) =>
        r.State is ReinforcementState.Reserved or ReinforcementState.Traveling or ReinforcementState.Garrisoned;

    private static bool EpochsMatch(AllianceDirectory d, AllianceReinforcementRecord r)
    {
        var a = d.Alliances.FirstOrDefault(a => a.Id == r.AllianceId && !a.PendingCreate);
        return a != null && a.Members.Any(m => m.PlayerId == r.SenderId && m.Epoch == r.SenderEpoch) &&
            a.Members.Any(m => m.PlayerId == r.ReceiverId && m.Epoch == r.ReceiverEpoch);
    }

    public async Task<ReinforcementActionResult> DispatchReinforcementAsync(string senderId, string receiverId,
        HeroId hero, Dictionary<TroopType, long> troops, string operationId, CancellationToken ct)
    {
        if (_sessions == null) return new(AllianceResult.PersistenceFailed);
        if (senderId == receiverId || string.IsNullOrWhiteSpace(receiverId) || troops == null ||
            !Guid.TryParse(operationId, out var operation)) return new(AllianceResult.InvalidTarget);
        string id = senderId + ":" + operation.ToString("N");
        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            var record = directory.Reinforcements.FirstOrDefault(r => r.Id == id);
            if (record != null)
            {
                if (record.ReceiverId != receiverId || record.Hero != hero || record.Troops.Count != troops.Count ||
                    record.Troops.Any(t => !troops.TryGetValue(t.Key, out var count) || count != t.Value))
                    return new(AllianceResult.InvalidTarget);
            }
            else
            {
                var sender = Membership(directory, senderId);
                var receiver = Membership(directory, receiverId);
                if (sender == null || receiver == null || sender.Value.Item1.PendingCreate ||
                    sender.Value.Item1.Id != receiver.Value.Item1.Id) return new(AllianceResult.NotMember);
                foreach (var existing in directory.Reinforcements.Where(r => r.ReceiverId == receiverId && OccupiesSlot(r)))
                    if (!await RefreshReinforcementLockedAsync(directory, existing, ct)) return new(AllianceResult.PersistenceFailed);
                if (directory.Reinforcements.Any(r => r.ReceiverId == receiverId && OccupiesSlot(r)))
                    return new(AllianceResult.ReinforcementSlotBusy);
                var target = await _sessions.ReadCityAsync(receiverId, ct);
                if (target == null) return new(AllianceResult.NotFound);
                if (!target.AcceptReinforcements) return new(AllianceResult.ReinforcementsClosed);
                record = new AllianceReinforcementRecord
                {
                    Id = id, AllianceId = sender.Value.Item1.Id, SenderId = senderId, ReceiverId = receiverId,
                    SenderEpoch = sender.Value.Item2.Epoch, ReceiverEpoch = receiver.Value.Item2.Epoch,
                    Hero = hero, Troops = new(troops), CreatedAtMs = _clock.NowMs
                };
                directory.Reinforcements.Add(record);
                directory.Version++;
                // Reserve before deducting troops: concurrent senders cannot both occupy this city.
                await _store.SaveAsync(directory, ct);
            }
            if (record.State == ReinforcementState.Reserved)
            {
                if (!await CompleteDispatchLockedAsync(directory, record, ct)) return new(AllianceResult.PersistenceFailed);
                directory.Version++;
                await _store.SaveAsync(directory, ct);
            }
            return record.State == ReinforcementState.Rejected
                ? new(AllianceResult.MilitaryRejected, record.MilitaryResult, id)
                : new(AllianceResult.Ok, Id: id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(AllianceResult.PersistenceFailed); }
        finally { _operationGate.Release(); }
    }

    private async Task<bool> CompleteDispatchLockedAsync(AllianceDirectory directory,
        AllianceReinforcementRecord record, CancellationToken ct)
    {
        var receiver = await _sessions!.ReadCityAsync(record.ReceiverId, ct);
        bool valid = EpochsMatch(directory, record) && receiver?.AcceptReinforcements == true;
        string receipt = "alliance:reinforce:" + record.Id;
        bool exists = await _sessions.ModifyCityAsync(record.SenderId, city =>
        {
            var march = city.Surface.Marches.FirstOrDefault(m => m.ReinforcementOperationId == record.Id);
            if (!city.SocialReceipts.Contains(receipt))
            {
                if (!valid)
                {
                    record.State = ReinforcementState.Rejected;
                    record.MilitaryResult = AllianceMilitaryResult.InvalidTarget;
                    return;
                }
                record.MilitaryResult = MarchLogic.StartReinforcement(city, receiver!, record.ReceiverId,
                    record.Hero, record.Troops, _clock.NowMs, out long marchId);
                if (record.MilitaryResult != AllianceMilitaryResult.Ok)
                { record.State = ReinforcementState.Rejected; return; }
                march = city.Surface.Marches.First(m => m.Id == marchId);
                march.ReinforcementOperationId = record.Id;
                city.SocialReceipts.Add(receipt);
            }
            // A city receipt survives even if the return march was already removed.
            if (march == null) { record.State = ReinforcementState.Returned; return; }
            record.MarchId = march.Id;
            if (!valid || record.ReturnRequested)
            {
                record.ReturnRequested = true;
                MarchLogic.RecallReinforcement(city, march.Id, _clock.NowMs);
            }
            UpdateFromMarch(record, march);
        }, ct, saveNow: true);
        if (!exists) return false;
        return true;
    }

    private static void UpdateFromMarch(AllianceReinforcementRecord record, March march)
    {
        record.State = march.Returning ? ReinforcementState.Returning
            : march.Stationed ? ReinforcementState.Garrisoned : ReinforcementState.Traveling;
        record.ArriveAtMs = march.ArriveAt;
        record.SettlementId = march.GarrisonSettlementId;
        record.CurrentTroops = new(march.Troops);
    }

    private async Task<bool> RefreshReinforcementLockedAsync(AllianceDirectory directory,
        AllianceReinforcementRecord record, CancellationToken ct)
    {
        if (record.State == ReinforcementState.Reserved)
            return await CompleteDispatchLockedAsync(directory, record, ct);
        if (record.State is ReinforcementState.Returned or ReinforcementState.Rejected) return true;
        if (!EpochsMatch(directory, record)) record.ReturnRequested = true;
        if (record.SettlementId != null) return true;
        return await _sessions!.ModifyCityAsync(record.SenderId, city =>
        {
            MarchLogic.AdvanceReinforcement(city, record.MarchId, _clock.NowMs);
            var march = city.Surface.Marches.FirstOrDefault(m => m.Id == record.MarchId && m.ReinforcementOperationId == record.Id);
            if (march == null) { record.State = ReinforcementState.Returned; record.SettlementId = null; return; }
            if (record.ReturnRequested) MarchLogic.RecallReinforcement(city, march.Id, _clock.NowMs);
            UpdateFromMarch(record, march);
        }, ct, saveNow: true);
    }

    public async Task<AllianceResult> ReturnReinforcementAsync(string actorId, string id, CancellationToken ct)
    {
        if (_sessions == null) return AllianceResult.PersistenceFailed;
        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            var record = directory.Reinforcements.FirstOrDefault(r => r.Id == id);
            if (record == null) return AllianceResult.NotFound;
            if (actorId != record.SenderId && actorId != record.ReceiverId) return AllianceResult.Forbidden;
            if (record.State is ReinforcementState.Returned or ReinforcementState.Rejected) return AllianceResult.Ok;
            record.ReturnRequested = true;
            directory.Version++;
            // Persist the intention first; battle-frozen troops return after their settlement finishes.
            await _store.SaveAsync(directory, ct);
            if (!await RefreshReinforcementLockedAsync(directory, record, ct)) return AllianceResult.PersistenceFailed;
            directory.Version++;
            await _store.SaveAsync(directory, ct);
            return AllianceResult.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return AllianceResult.PersistenceFailed; }
        finally { _operationGate.Release(); }
    }

    public async Task<AllianceResult> SetReinforcementAcceptanceAsync(string playerId, bool accept, CancellationToken ct)
    {
        if (_sessions == null) return AllianceResult.PersistenceFailed;
        await _operationGate.WaitAsync(ct);
        try
        {
            return await _sessions.ModifyCityAsync(playerId, city => city.AcceptReinforcements = accept, ct, saveNow: true)
                ? AllianceResult.Ok : AllianceResult.PersistenceFailed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return AllianceResult.PersistenceFailed; }
        finally { _operationGate.Release(); }
    }

    public async Task AdvanceReinforcementsAsync(CancellationToken ct)
    {
        if (_sessions == null) return;
        var notify = new HashSet<string>();
        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            bool changed = false;
            foreach (var r in directory.Reinforcements)
            {
                if (r.State is ReinforcementState.Returned or ReinforcementState.Rejected) continue;
                bool due = r.State == ReinforcementState.Reserved || (!EpochsMatch(directory, r) && !r.ReturnRequested) ||
                    (r.ReturnRequested && r.State != ReinforcementState.Returning && r.SettlementId == null) ||
                    ((r.State is ReinforcementState.Traveling or ReinforcementState.Returning) && _clock.NowMs >= r.ArriveAtMs);
                if (!due) continue;
                if (!await RefreshReinforcementLockedAsync(directory, r, ct))
                    throw new InvalidOperationException("Reinforcement player save failed; directory remains unchanged.");
                changed = true;
                notify.Add(r.SenderId); notify.Add(r.ReceiverId);
            }
            if (changed) { directory.Version++; await _store.SaveAsync(directory, ct); }
        }
        finally { _operationGate.Release(); }
        if (notify.Count > 0) await _sessions.PushToPlayersAsync(notify, MessageTypes.AllianceMilitaryChanged, new { });
    }

    public async Task NotifyReinforcementAsync(string id, CancellationToken ct)
    {
        if (_sessions == null) return;
        var directory = await SnapshotAsync(ct);
        var record = directory.Reinforcements.FirstOrDefault(r => r.Id == id);
        if (record != null) await _sessions.PushToPlayersAsync(new[] { record.SenderId, record.ReceiverId },
            MessageTypes.AllianceMilitaryChanged, new { });
    }
}
