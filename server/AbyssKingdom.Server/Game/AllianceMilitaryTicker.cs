using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Server.Game;

/// <summary>Replays durable dispatch/return intents, including disconnected owners.</summary>
public sealed class AllianceMilitaryTicker(AllianceService alliances, SessionManager sessions, AllianceSynchronization sync,
    IClock clock, ILogger<AllianceMilitaryTicker> log) : BackgroundService
{
    private long _lastRevision = -1, _nextDue, _retryAt;
    private int _failures;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                if (clock.NowMs < _retryAt || (sync.Revision == _lastRevision && clock.NowMs < _nextDue)) continue;
                try
                {
                    await alliances.AdvanceReinforcementsAsync(stoppingToken);
                    await alliances.RecoverFlagPaymentsAsync(stoppingToken);
                    await sessions.RecoverCampSettlementsAsync(stoppingToken);
                    await sessions.RecoverAllianceDefensesAsync(stoppingToken);
                    await alliances.AdvanceRalliesAsync(stoppingToken);
                    long revisionBeforeRead = sync.Revision;
                    var snapshot = await alliances.SnapshotAsync(stoppingToken);
                    _nextDue = NextDue(snapshot, clock.NowMs);
                    _lastRevision = revisionBeforeRead;
                    _failures = 0; _retryAt = 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _failures = Math.Min(6, _failures + 1);
                    _retryAt = clock.NowMs + Math.Min(30, 1 << (_failures - 1)) * 1_000L;
                    log.LogError(ex, "Alliance military recovery failed; will retry after {RetryAt}", _retryAt);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }
    private static long NextDue(AllianceDirectory d, long now)
    {
        long next = long.MaxValue;
        if (d.Alliances.SelectMany(a => a.Flags).Any(f => f.Live && !f.Paid) ||
            d.CampSettlements.Any(s => !s.Terminal || s.AppliedOwners.Count < s.Inputs.Count)) next = now + 1_000;
        foreach (var r in d.Reinforcements)
        {
            if (r.State is ReinforcementState.Returned or ReinforcementState.Rejected) continue;
            if (r.State is ReinforcementState.Traveling or ReinforcementState.Returning) next = Math.Min(next, r.ArriveAtMs);
            else if (r.State == ReinforcementState.Reserved || r.ReturnRequested || r.SettlementId != null) next = Math.Min(next, now + 1_000);
        }
        foreach (var r in d.Rallies.Where(r => r.State != RallyState.Finished))
        {
            if (r.Participants.Any(p => !p.Paid)) next = Math.Min(next, now + 1_000);
            foreach (var p in r.Participants.Where(p => !p.Returned && (p.Left || r.State == RallyState.Returning)))
                next = Math.Min(next, p.ReturnAtMs == 0 ? now + 1_000 : p.ReturnAtMs);
            next = Math.Min(next, r.State switch
            {
                RallyState.Forming => r.DepartAtMs, RallyState.Traveling => r.Times[^1],
                RallyState.Launching or RallyState.Settling => now + 1_000, _ => long.MaxValue
            });
        }
        return next;
    }
}
