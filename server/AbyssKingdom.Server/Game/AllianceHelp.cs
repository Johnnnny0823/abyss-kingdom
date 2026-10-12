using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

public sealed record AllianceHelpAllResult(AllianceResult Result, int Applied = 0, int Expired = 0);

public sealed partial class AllianceService
{
    public async Task<AllianceResult> RequestHelpAsync(string ownerId, QueueType queue, CancellationToken ct)
    {
        if (_sessions == null) return AllianceResult.PersistenceFailed;
        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            var found = Membership(directory, ownerId);
            if (found == null || found.Value.Item1.PendingCreate) return AllianceResult.NotMember;
            var (alliance, member) = found.Value;
            AllianceHelpResult result = AllianceHelpResult.NoQueue;
            long queueId = 0;
            long expiresAt = 0;
            int maxCount = 0, usedCount = 0;
            bool saved = await _sessions.ModifyCityAsync(ownerId, city =>
            {
                result = CityLogic.RequestAllianceHelp(city, queue, alliance.Id, member.Epoch, _clock.NowMs, out queueId);
                expiresAt = queue == QueueType.Building ? city.Upgrade?.FinishAtMs ?? 0 : city.Healing?.FinishAtMs ?? 0;
                var progress = queue == QueueType.Building ? city.Upgrade?.Help : city.Healing?.Help;
                maxCount = progress?.MaxCount ?? 0;
                usedCount = progress?.HelpedBy.Count ?? 0;
            },
                ct, saveNow: true);
            if (!saved) return AllianceResult.PersistenceFailed;
            if (result != AllianceHelpResult.Ok) return AllianceResult.HelpUnavailable;

            alliance.HelpRequests.RemoveAll(r => r.OwnerId == ownerId && r.Queue == queue);
            alliance.HelpRequests.Add(new AllianceHelpRequestRecord
            {
                OwnerId = ownerId, OwnerName = member.DisplayName, Queue = queue,
                QueueId = queueId, OwnerMembershipEpoch = member.Epoch, ExpiresAtMs = expiresAt,
                MaxCount = maxCount, UsedCount = usedCount
            });
            directory.Version++;
            await _store.SaveAsync(directory, ct);
            await _sessions.PushToPlayersAsync(alliance.Members.Select(m => m.PlayerId),
                MessageTypes.AllianceHelpChanged, new { });
            return AllianceResult.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return AllianceResult.PersistenceFailed; }
        finally { _operationGate.Release(); }
    }

    public async Task<AllianceHelpAllResult> HelpAllAsync(string helperId, CancellationToken ct)
    {
        if (_sessions == null) return new(AllianceResult.PersistenceFailed);
        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            var found = Membership(directory, helperId);
            if (found == null || found.Value.Item1.PendingCreate) return new(AllianceResult.NotMember);
            var alliance = found.Value.Item1;
            int applied = 0, expired = 0;
            foreach (var request in alliance.HelpRequests.ToList())
            {
                if (request.OwnerId == helperId) continue;
                var owner = alliance.Members.FirstOrDefault(m => m.PlayerId == request.OwnerId);
                if (owner == null || owner.Epoch != request.OwnerMembershipEpoch)
                {
                    alliance.HelpRequests.Remove(request);
                    expired++;
                    continue;
                }
                AllianceHelpResult result = AllianceHelpResult.NoQueue;
                long expiresAt = 0;
                int usedCount = request.UsedCount;
                bool saved = await _sessions.ModifyCityAsync(request.OwnerId, city =>
                {
                    result = CityLogic.ApplyAllianceHelp(city, request.Queue, request.QueueId,
                        request.OwnerId, helperId, alliance.Id, owner.Epoch, _clock.NowMs, out _);
                    expiresAt = request.Queue == QueueType.Building ? city.Upgrade?.FinishAtMs ?? 0 : city.Healing?.FinishAtMs ?? 0;
                    usedCount = (request.Queue == QueueType.Building ? city.Upgrade?.Help : city.Healing?.Help)?.HelpedBy.Count ?? 0;
                },
                    ct, saveNow: true);
                if (!saved) return new(AllianceResult.PersistenceFailed, applied, expired);
                if (result is AllianceHelpResult.Ok or AllianceHelpResult.AlreadyHelped)
                { request.ExpiresAtMs = expiresAt; request.UsedCount = usedCount; MarkGuide(directory, helperId, AbyssKingdom.Shared.Social.AllianceConfig.GuideHelped); }
                if (result == AllianceHelpResult.Ok) applied++;
                bool stale = result is AllianceHelpResult.NoQueue or AllianceHelpResult.WrongQueue or
                    AllianceHelpResult.NotRequested or AllianceHelpResult.Finished or AllianceHelpResult.HelpLimit;
                bool exhausted = (result is AllianceHelpResult.Ok or AllianceHelpResult.AlreadyHelped) &&
                    (request.ExpiresAtMs <= _clock.NowMs || request.UsedCount >= request.MaxCount);
                if (stale || exhausted)
                {
                    alliance.HelpRequests.Remove(request);
                    if (stale) expired++;
                }
            }
            directory.Version++;
            await _store.SaveAsync(directory, ct);
            if (applied > 0 || expired > 0)
                await _sessions.PushToPlayersAsync(alliance.Members.Select(m => m.PlayerId),
                    MessageTypes.AllianceHelpChanged, new { });
            return new(AllianceResult.Ok, applied, expired);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(AllianceResult.PersistenceFailed); }
        finally { _operationGate.Release(); }
    }

    public async Task SyncOwnerHelpAsync(string ownerId, QueueType queue, long queueId, long finishAtMs,
        CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            var found = Membership(directory, ownerId);
            if (found == null) return;
            var alliance = found.Value.Item1;
            var request = alliance.HelpRequests.FirstOrDefault(r => r.OwnerId == ownerId && r.Queue == queue);
            if (request == null) return;
            bool remove = queueId == 0 || request.QueueId != queueId || finishAtMs <= _clock.NowMs;
            if (remove) alliance.HelpRequests.Remove(request);
            else if (request.ExpiresAtMs == finishAtMs) return;
            else request.ExpiresAtMs = finishAtMs;
            directory.Version++;
            await _store.SaveAsync(directory, ct);
            if (_sessions != null)
                await _sessions.PushToPlayersAsync(alliance.Members.Select(m => m.PlayerId),
                    MessageTypes.AllianceHelpChanged, new { });
        }
        finally { _operationGate.Release(); }
    }
}
