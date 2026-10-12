using System.Text;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Protocol;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Server.Game;

public sealed record AllianceChatResult(AllianceResult Result, string? AllianceId = null,
    AllianceChatRecord? Message = null);

public sealed partial class AllianceService
{
    private const long ChatRetentionMs = 7L * 24 * 60 * 60 * 1000;

    public async Task<AllianceChatResult> PostChatAsync(string playerId, string operationId, string? input,
        CancellationToken ct)
    {
        if (!Guid.TryParse(operationId, out _) || operationId.Length > 64 || input == null)
            return new(AllianceResult.InvalidMessage);
        string message = input.Trim();
        if (message.Length == 0 || message.EnumerateRunes().Count() > 200 || message.Any(char.IsControl))
            return new(AllianceResult.InvalidMessage);

        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            var found = Membership(directory, playerId);
            if (found == null || found.Value.Item1.PendingCreate) return new(AllianceResult.NotMember);
            var (alliance, member) = found.Value;
            long now = _clock.NowMs;
            string receiptKey = playerId + ":" + operationId;
            if (alliance.ChatOperationReceipts.TryGetValue(receiptKey, out var receipt))
                return new(AllianceResult.Ok, alliance.Id,
                    alliance.Chat.FirstOrDefault(m => m.Sequence == receipt.Sequence));
            if (alliance.ChatMuteUntilMs.TryGetValue(playerId, out long muteUntil) && muteUntil > now)
                return new(AllianceResult.Muted);
            if (!alliance.ChatRateWindows.TryGetValue(playerId, out var recent)) recent = new();
            recent = recent.Where(t => t > now - 60_000).ToList();
            if (recent.Count > 0 && now - recent[^1] < 2_000) return new(AllianceResult.RateLimited);
            if (recent.Count >= 20) return new(AllianceResult.RateLimited);

            var posted = new AllianceChatRecord
            {
                Sequence = alliance.NextChatSequence++, OperationId = operationId,
                PlayerId = playerId, DisplayName = member.DisplayName, Text = message, SentAtMs = now
            };
            alliance.Chat.Add(posted);
            recent.Add(now);
            alliance.ChatRateWindows[playerId] = recent;
            alliance.Chat.RemoveAll(m => m.SentAtMs <= now - ChatRetentionMs);
            if (alliance.Chat.Count > 100) alliance.Chat.RemoveRange(0, alliance.Chat.Count - 100);
            alliance.ChatOperationReceipts[receiptKey] = new() { Sequence = posted.Sequence, SentAtMs = now };
            // Keep retry receipts for seven days, even after the 100-message window evicts a message.
            foreach (string key in alliance.ChatOperationReceipts.Where(x => x.Value.SentAtMs <= now - ChatRetentionMs)
                         .Select(x => x.Key).ToList()) alliance.ChatOperationReceipts.Remove(key);
            directory.Version++;
            await _store.SaveAsync(directory, ct);
            return new(AllianceResult.Ok, alliance.Id, posted);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<(AllianceResult Result, string? AllianceId, List<AllianceChatRecord> Messages,
        long OldestSequence, long LatestSequence)> ReadChatAsync(
        string playerId, long afterSequence, CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        try
        {
            var directory = await _store.LoadAsync(ct);
            var found = Membership(directory, playerId);
            if (found == null || found.Value.Item1.PendingCreate) return (AllianceResult.NotMember, null, new(), 0, 0);
            var alliance = found.Value.Item1;
            var retained = alliance.Chat.Where(m => m.SentAtMs > _clock.NowMs - ChatRetentionMs).ToList();
            long oldest = retained.Count == 0 ? 0 : retained[0].Sequence;
            long latest = alliance.NextChatSequence - 1;
            if (retained.Count > 0 && afterSequence < retained[0].Sequence - 1) afterSequence = 0;
            return (AllianceResult.Ok, alliance.Id,
                retained.Where(m => m.Sequence > afterSequence || m.Hidden).ToList(), oldest, latest);
        }
        finally { _operationGate.Release(); }
    }

    public async Task BroadcastChatAsync(string allianceId, AllianceChatRecord message, CancellationToken ct)
    {
        if (_sessions == null) return;
        await _operationGate.WaitAsync(ct);
        try
        {
            var alliance = (await _store.LoadAsync(ct)).Alliances.FirstOrDefault(a => a.Id == allianceId && !a.PendingCreate);
            if (alliance == null) return;
            var current = alliance.Chat.FirstOrDefault(m => m.Sequence == message.Sequence);
            if (current == null) return;
            // Membership changes use this same gate, so no former member receives a late push.
            await _sessions.PushToPlayersAsync(alliance.Members.Select(m => m.PlayerId), MessageTypes.AllianceChat,
                new AllianceChatMessagesMessage
                { AllianceId = allianceId, Messages = new() { ToView(current) }, LatestSequence = current.Sequence });
        }
        finally { _operationGate.Release(); }
    }

    public static AllianceChatView ToView(AllianceChatRecord message) => new()
    {
        Sequence = message.Sequence, PlayerId = message.PlayerId, DisplayName = message.DisplayName,
        Text = message.Hidden ? "" : message.Text, Hidden = message.Hidden, SentAtMs = message.SentAtMs
    };

    public Task<AllianceResult> SetAnnouncementAsync(string actorId, string? text, CancellationToken ct)
    {
        string value = text?.Trim() ?? "";
        if (value.Length > 300 || value.Any(char.IsControl)) return Task.FromResult(AllianceResult.InvalidMessage);
        return ChangeAsync(d =>
        {
            var found = Membership(d, actorId);
            if (found == null) return AllianceResult.NotMember;
            if (found.Value.Item2.Role == AllianceRole.Member) return AllianceResult.Forbidden;
            found.Value.Item1.Announcement = value;
            return AllianceResult.Ok;
        }, ct);
    }

    public Task<AllianceResult> HideChatAsync(string actorId, long sequence, CancellationToken ct) => ChangeAsync(d =>
    {
        var found = Membership(d, actorId);
        if (found == null) return AllianceResult.NotMember;
        var (alliance, actor) = found.Value;
        var target = alliance.Chat.FirstOrDefault(m => m.Sequence == sequence);
        if (target == null) return AllianceResult.InvalidTarget;
        var targetRole = alliance.Members.FirstOrDefault(m => m.PlayerId == target.PlayerId)?.Role;
        if (actor.Role == AllianceRole.Member || (targetRole == AllianceRole.Leader && actorId != target.PlayerId) ||
            (targetRole == AllianceRole.Officer && actor.Role != AllianceRole.Leader)) return AllianceResult.Forbidden;
        target.Hidden = true;
        alliance.ChatAudit.Add(new() { ActorId = actorId, Action = "hide", TargetId = sequence.ToString(), AtMs = _clock.NowMs });
        return AllianceResult.Ok;
    }, ct);

    public Task<AllianceResult> MuteChatAsync(string actorId, string targetId, int hours, CancellationToken ct) => ChangeAsync(d =>
    {
        var found = Membership(d, actorId);
        if (found == null) return AllianceResult.NotMember;
        var (alliance, actor) = found.Value;
        var target = alliance.Members.FirstOrDefault(m => m.PlayerId == targetId);
        if (target == null || (hours != 1 && hours != 24)) return AllianceResult.InvalidTarget;
        if (actor.Role == AllianceRole.Member || target.Role == AllianceRole.Leader ||
            (target.Role == AllianceRole.Officer && actor.Role != AllianceRole.Leader)) return AllianceResult.Forbidden;
        alliance.ChatMuteUntilMs[targetId] = _clock.NowMs + hours * 3_600_000L;
        alliance.ChatAudit.Add(new() { ActorId = actorId, Action = "mute" + hours, TargetId = targetId, AtMs = _clock.NowMs });
        return AllianceResult.Ok;
    }, ct);

    public Task<AllianceResult> ReportChatAsync(string actorId, long sequence, CancellationToken ct) => ChangeAsync(d =>
    {
        var found = Membership(d, actorId);
        if (found == null) return AllianceResult.NotMember;
        var alliance = found.Value.Item1;
        if (!alliance.Chat.Any(m => m.Sequence == sequence)) return AllianceResult.InvalidTarget;
        if (alliance.ChatAudit.Any(a => a.ActorId == actorId && a.Action == "report" && a.TargetId == sequence.ToString()))
            return AllianceResult.InvalidTarget;
        alliance.ChatAudit.Add(new() { ActorId = actorId, Action = "report", TargetId = sequence.ToString(), AtMs = _clock.NowMs });
        return AllianceResult.Ok;
    }, ct);
}
