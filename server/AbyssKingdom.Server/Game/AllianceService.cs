using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Server.Game;

public enum AllianceResult
{
    Ok, NotFound, AlreadyMember, NotMember, LevelTooLow, Cooldown, Full,
    ApprovalRequired, ApplicationLimit, NoApplication, Forbidden, OfficerLimit,
    InvalidTarget, LeaderMustTransfer, PendingOperations, InvalidName, NameTaken,
    InsufficientResources, PersistenceFailed, InvalidMessage, RateLimited, Muted, HelpUnavailable,
    ReinforcementsClosed, ReinforcementSlotBusy, MilitaryRejected
}

/// <summary>Serializes alliance metadata on the approved single-server deployment.</summary>
public sealed partial class AllianceService
{
    private readonly IAllianceStore _store;
    private readonly IClock _clock;
    private readonly SessionManager? _sessions;
    private readonly SemaphoreSlim _operationGate;
    private readonly WorldService? _territoryWorld;

    public AllianceService(IAllianceStore store, IClock clock, SessionManager? sessions = null,
        AllianceSynchronization? synchronization = null, WorldService? territoryWorld = null)
    {
        var sync = synchronization ?? new AllianceSynchronization();
        _store = sync.BindStore(store);
        _clock = clock;
        _sessions = sessions;
        _operationGate = sync.Operations;
        _territoryWorld = territoryWorld;
    }

    public async Task<AllianceResult> CreateAsync(string playerId, string displayName, string nameInput, string tagInput,
        string operationId, CancellationToken ct)
    {
        string? name = AllianceRules.NormalizeName(nameInput);
        string? tag = AllianceRules.NormalizeTag(tagInput);
        if (name == null || tag == null || !Guid.TryParse(operationId, out _)) return AllianceResult.InvalidName;
        if (_sessions == null) return AllianceResult.PersistenceFailed;
        string receipt = "alliance:create:" + playerId + ":" + operationId;
        AllianceRecord? reserved;
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct);
            reserved = d.Alliances.FirstOrDefault(a => a.CreateOperationId == receipt);
            if (reserved == null)
            {
                if (Membership(d, playerId) != null) return AllianceResult.AlreadyMember;
                if (d.RejoinAfterMs.TryGetValue(playerId, out long until) && !AllianceRules.CooldownEnded(_clock.NowMs, until)) return AllianceResult.Cooldown;
                if (d.Alliances.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || a.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))) return AllianceResult.NameTaken;
                reserved = new AllianceRecord
                {
                    Id = Guid.NewGuid().ToString("N"), Name = name, Tag = tag,
                    PendingCreate = true, CreateOperationId = receipt,
                    Members = new() { new AllianceMemberRecord { PlayerId = playerId, DisplayName = displayName, Role = AllianceRole.Leader, JoinedAtMs = _clock.NowMs } }
                };
                d.Alliances.Add(reserved);
                d.Version++;
                await _store.SaveAsync(d, ct);
            }
            if (!reserved.PendingCreate) return AllianceResult.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return AllianceResult.PersistenceFailed; }
        finally { _operationGate.Release(); }

        AllianceResult payment = AllianceResult.Ok;
        bool saved;
        try
        {
            saved = await _sessions.ModifyCityAsync(playerId, city =>
            {
                city.SocialReceipts ??= new HashSet<string>();
                if (city.SocialReceipts.Contains(receipt)) return;
                if (city.GetLevel(AbyssKingdom.Shared.City.BuildingType.Keep) < AllianceConfig.CreateKeepLevel)
                { payment = AllianceResult.LevelTooLow; return; }
                if (city.GetResource(AbyssKingdom.Shared.City.ResourceType.Wood) < AllianceConfig.CreateWood ||
                    city.GetResource(AbyssKingdom.Shared.City.ResourceType.Stone) < AllianceConfig.CreateStone)
                { payment = AllianceResult.InsufficientResources; return; }
                city.Resources[AbyssKingdom.Shared.City.ResourceType.Wood] -= AllianceConfig.CreateWood;
                city.Resources[AbyssKingdom.Shared.City.ResourceType.Stone] -= AllianceConfig.CreateStone;
                city.SocialReceipts.Add(receipt);
            }, ct, saveNow: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return AllianceResult.PersistenceFailed; }
        if (!saved) return AllianceResult.PersistenceFailed;
        if (payment != AllianceResult.Ok)
        {
            await _operationGate.WaitAsync(ct);
            try
            {
                var d = await _store.LoadAsync(ct);
                d.Alliances.RemoveAll(a => a.CreateOperationId == receipt && a.PendingCreate);
                d.Version++;
                await _store.SaveAsync(d, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return AllianceResult.PersistenceFailed; }
            finally { _operationGate.Release(); }
            return payment;
        }

        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct);
            var alliance = d.Alliances.First(a => a.CreateOperationId == receipt);
            if (!alliance.PendingCreate) return AllianceResult.Ok;
            alliance.PendingCreate = false;
            long epoch = d.MembershipEpochs.TryGetValue(playerId, out long previous) ? previous + 1 : 1;
            d.MembershipEpochs[playerId] = epoch;
            alliance.Members[0].Epoch = epoch;
            MarkGuide(d, playerId, AllianceConfig.GuideJoined);
            d.Version++;
            await _store.SaveAsync(d, ct);
            return AllianceResult.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return AllianceResult.PersistenceFailed; }
        finally { _operationGate.Release(); }
    }

    public Task<AllianceDirectory> SnapshotAsync(CancellationToken ct) => _store.LoadAsync(ct);

    public async Task<bool> SameAllianceAsync(string firstPlayerId, string secondPlayerId, CancellationToken ct)
    {
        if (firstPlayerId == secondPlayerId) return false;
        var d = await _store.LoadAsync(ct);
        return d.Alliances.Any(a => !a.PendingCreate &&
            a.Members.Any(m => m.PlayerId == firstPlayerId) &&
            a.Members.Any(m => m.PlayerId == secondPlayerId));
    }

    public async Task<AllianceResult> JoinAsync(string playerId, string displayName, int keepLevel, string allianceId, CancellationToken ct)
    {
        return await ChangeAsync(d =>
        {
            long now = _clock.NowMs;
            if (keepLevel < AllianceConfig.JoinKeepLevel) return AllianceResult.LevelTooLow;
            if (Membership(d, playerId) != null) return AllianceResult.AlreadyMember;
            if (d.RejoinAfterMs.TryGetValue(playerId, out long until) && !AllianceRules.CooldownEnded(now, until)) return AllianceResult.Cooldown;
            var alliance = d.Alliances.FirstOrDefault(a => a.Id == allianceId);
            if (alliance == null || alliance.PendingCreate) return AllianceResult.NotFound;
            if (alliance.Members.Count >= AllianceConfig.MaxMembers) return AllianceResult.Full;
            if (alliance.JoinMode == AllianceJoinMode.Approval) return AllianceResult.ApprovalRequired;
            AddMember(d, alliance, playerId, displayName, now);
            return AllianceResult.Ok;
        }, ct);
    }

    public async Task<AllianceResult> ApplyAsync(string playerId, string displayName, int keepLevel, string allianceId, CancellationToken ct)
    {
        return await ChangeAsync(d =>
        {
            long now = _clock.NowMs;
            if (keepLevel < AllianceConfig.JoinKeepLevel) return AllianceResult.LevelTooLow;
            if (Membership(d, playerId) != null) return AllianceResult.AlreadyMember;
            if (d.RejoinAfterMs.TryGetValue(playerId, out long until) && !AllianceRules.CooldownEnded(now, until)) return AllianceResult.Cooldown;
            var alliance = d.Alliances.FirstOrDefault(a => a.Id == allianceId);
            if (alliance == null || alliance.PendingCreate) return AllianceResult.NotFound;
            if (alliance.Members.Count >= AllianceConfig.MaxMembers) return AllianceResult.Full;
            if (alliance.JoinMode == AllianceJoinMode.Open) return AllianceResult.ApprovalRequired;
            if (d.Alliances.Any(a => a.Applications.Any(x => x.PlayerId == playerId && x.AllianceId == allianceId && AllianceRules.ApplicationActive(now, x.ExpiresAtMs)))) return AllianceResult.Ok;
            int active = d.Alliances.Sum(a => a.Applications.Count(x => x.PlayerId == playerId && AllianceRules.ApplicationActive(now, x.ExpiresAtMs)));
            if (active >= AllianceConfig.MaxApplications) return AllianceResult.ApplicationLimit;
            alliance.Applications.RemoveAll(x => x.PlayerId == playerId);
            alliance.Applications.Add(new AllianceApplicationRecord { PlayerId = playerId, DisplayName = displayName, AllianceId = allianceId, ExpiresAtMs = now + AllianceConfig.ApplicationDurationMs });
            return AllianceResult.Ok;
        }, ct);
    }

    public async Task<AllianceResult> ReviewAsync(string actorId, string applicantId, bool accept, CancellationToken ct)
    {
        return await ChangeAsync(d =>
        {
            long now = _clock.NowMs;
            var found = Membership(d, actorId);
            if (found == null) return AllianceResult.NotMember;
            var (alliance, actor) = found.Value;
            if (!AllianceRules.Can(actor.Role, AlliancePermission.ReviewApplications)) return AllianceResult.Forbidden;
            var application = alliance.Applications.FirstOrDefault(x => x.PlayerId == applicantId && AllianceRules.ApplicationActive(now, x.ExpiresAtMs));
            if (application == null) return AllianceResult.NoApplication;
            if (accept)
            {
                if (Membership(d, applicantId) != null) return AllianceResult.AlreadyMember;
                if (d.RejoinAfterMs.TryGetValue(applicantId, out long until) && !AllianceRules.CooldownEnded(now, until)) return AllianceResult.Cooldown;
                if (alliance.Members.Count >= AllianceConfig.MaxMembers) return AllianceResult.Full;
                AddMember(d, alliance, applicantId, application.DisplayName, now);
            }
            else alliance.Applications.Remove(application);
            return AllianceResult.Ok;
        }, ct);
    }

    public async Task<AllianceResult> LeaveAsync(string playerId, CancellationToken ct)
    {
        return await ChangeAsync(d =>
        {
            var found = Membership(d, playerId);
            if (found == null) return AllianceResult.NotMember;
            var (alliance, member) = found.Value;
            if (member.Role == AllianceRole.Leader) return AllianceResult.LeaderMustTransfer;
            alliance.HelpRequests.RemoveAll(r => r.OwnerId == playerId);
            alliance.ChatRateWindows.Remove(playerId);
            alliance.Members.Remove(member);
            d.RejoinAfterMs[playerId] = _clock.NowMs + AllianceConfig.RejoinCooldownMs;
            return AllianceResult.Ok;
        }, ct);
    }

    public async Task<AllianceResult> KickAsync(string actorId, string targetId, CancellationToken ct)
    {
        return await ChangeAsync(d =>
        {
            var found = Membership(d, actorId);
            if (found == null) return AllianceResult.NotMember;
            var (alliance, actor) = found.Value;
            var target = alliance.Members.FirstOrDefault(x => x.PlayerId == targetId);
            if (target == null) return AllianceResult.InvalidTarget;
            if (!AllianceRules.CanKick(actor.Role, target.Role, actorId == targetId)) return AllianceResult.Forbidden;
            alliance.HelpRequests.RemoveAll(r => r.OwnerId == targetId);
            alliance.ChatRateWindows.Remove(targetId);
            alliance.Members.Remove(target);
            d.RejoinAfterMs[targetId] = _clock.NowMs + AllianceConfig.RejoinCooldownMs;
            return AllianceResult.Ok;
        }, ct);
    }

    public async Task<AllianceResult> SetOfficerAsync(string actorId, string targetId, bool officer, CancellationToken ct)
    {
        return await ChangeAsync(d =>
        {
            var found = Membership(d, actorId);
            if (found == null) return AllianceResult.NotMember;
            var (alliance, actor) = found.Value;
            if (!AllianceRules.Can(actor.Role, AlliancePermission.ManageOfficers)) return AllianceResult.Forbidden;
            var target = alliance.Members.FirstOrDefault(x => x.PlayerId == targetId);
            if (target == null || target.Role == AllianceRole.Leader) return AllianceResult.InvalidTarget;
            if (officer && target.Role != AllianceRole.Officer && alliance.Members.Count(x => x.Role == AllianceRole.Officer) >= AllianceConfig.MaxOfficers) return AllianceResult.OfficerLimit;
            target.Role = officer ? AllianceRole.Officer : AllianceRole.Member;
            return AllianceResult.Ok;
        }, ct);
    }

    public async Task<AllianceResult> TransferAsync(string actorId, string targetId, CancellationToken ct)
    {
        return await ChangeAsync(d =>
        {
            var found = Membership(d, actorId);
            if (found == null) return AllianceResult.NotMember;
            var (alliance, actor) = found.Value;
            if (actor.Role == AllianceRole.Member && alliance.Members.Any(m => m.PlayerId == targetId && m.Role == AllianceRole.Leader))
                return AllianceResult.Ok;
            if (!AllianceRules.Can(actor.Role, AlliancePermission.TransferLeadership)) return AllianceResult.Forbidden;
            var target = alliance.Members.FirstOrDefault(x => x.PlayerId == targetId);
            if (target == null || target.PlayerId == actorId) return AllianceResult.InvalidTarget;
            target.Role = AllianceRole.Leader;
            actor.Role = AllianceRole.Member;
            return AllianceResult.Ok;
        }, ct);
    }

    public async Task<AllianceResult> SetJoinModeAsync(string actorId, AllianceJoinMode mode, CancellationToken ct)
    {
        return await ChangeAsync(d =>
        {
            var found = Membership(d, actorId);
            if (found == null) return AllianceResult.NotMember;
            var (alliance, actor) = found.Value;
            if (!AllianceRules.Can(actor.Role, AlliancePermission.ChangeJoinMode)) return AllianceResult.Forbidden;
            if (!Enum.IsDefined(mode)) return AllianceResult.InvalidTarget;
            alliance.JoinMode = mode;
            return AllianceResult.Ok;
        }, ct);
    }

    public async Task<AllianceResult> DissolveAsync(string actorId, CancellationToken ct)
    {
        var city = _sessions == null ? null : await _sessions.ReadCityAsync(actorId, ct);
        return await ChangeAsync(d =>
        {
            var found = Membership(d, actorId);
            if (found == null) return d.DissolvedBy.Contains(actorId) ? AllianceResult.Ok : AllianceResult.NotMember;
            var (alliance, actor) = found.Value;
            if (!AllianceRules.Can(actor.Role, AlliancePermission.Dissolve)) return AllianceResult.Forbidden;
            if (alliance.Members.Count != 1) return AllianceResult.PendingOperations;
            if (city?.Surface.Marches.Count > 0 || alliance.Flags.Any(f => f.Live) ||
                d.Rallies.Any(r => r.AllianceId == alliance.Id && r.State != RallyState.Finished) ||
                d.Reinforcements.Any(r => r.AllianceId == alliance.Id &&
                    r.State is not (ReinforcementState.Returned or ReinforcementState.Rejected))) return AllianceResult.PendingOperations;
            d.Alliances.Remove(alliance);
            d.DissolvedBy.Add(actorId);
            d.RejoinAfterMs[actorId] = _clock.NowMs + AllianceConfig.RejoinCooldownMs;
            return AllianceResult.Ok;
        }, ct);
    }

    private async Task<AllianceResult> ChangeAsync(Func<AllianceDirectory, AllianceResult> change, CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            var result = change(directory);
            if (result == AllianceResult.Ok)
            {
                directory.Version++;
                await _store.SaveAsync(directory, ct);
                _territoryWorld?.UpdateTerritory(directory);
            }
            return result;
        }
        finally { _operationGate.Release(); }
    }

    private static (AllianceRecord, AllianceMemberRecord)? Membership(AllianceDirectory d, string playerId)
    {
        foreach (var alliance in d.Alliances)
        {
            var member = alliance.Members.FirstOrDefault(x => x.PlayerId == playerId);
            if (member != null) return (alliance, member);
        }
        return null;
    }

    private static void AddMember(AllianceDirectory d, AllianceRecord alliance, string playerId, string displayName, long now)
    {
        MarkGuide(d, playerId, AllianceConfig.GuideJoined);
        long epoch = d.MembershipEpochs.TryGetValue(playerId, out long previous) ? previous + 1 : 1;
        d.MembershipEpochs[playerId] = epoch;
        alliance.Members.Add(new AllianceMemberRecord { PlayerId = playerId, DisplayName = displayName, Role = AllianceRole.Member, JoinedAtMs = now, Epoch = epoch });
        foreach (var a in d.Alliances) a.Applications.RemoveAll(x => x.PlayerId == playerId);
    }
    private static void MarkGuide(AllianceDirectory d, string playerId, int step) =>
        d.GuideProgress[playerId] = d.GuideProgress.GetValueOrDefault(playerId) | step;

    public async Task RecordAdminRetryAsync(string reason, CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct);
            d.AdminAudit.Add(new AllianceAdminAudit { Id = Guid.NewGuid().ToString("N"), Action = "retryPending", Reason = reason, AtMs = _clock.NowMs });
            d.Version++; await _store.SaveAsync(d, ct);
        }
        finally { _operationGate.Release(); }
    }
}
