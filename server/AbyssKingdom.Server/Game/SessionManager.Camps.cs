using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

public sealed partial class SessionManager
{
    public async Task<CampSettlementOutcome> SettleCampAsync(string id, int tile, int level, long atMs,
        List<CampParticipantInput> participants, bool rally, CancellationToken ct)
    {
        await _allianceGate.WaitAsync(ct);
        try { return await SettleCampLockedAsync(await _allianceStore.LoadAsync(ct), id, tile, level, atMs, participants, rally, ct); }
        finally { _allianceGate.Release(); }
    }
    /// <summary>Caller holds AllianceSynchronization.Gate; solo battles and rallies share this durable claim.</summary>
    public async Task<CampSettlementOutcome> SettleCampLockedAsync(AllianceDirectory d, string id, int tile, int level,
        long atMs, List<CampParticipantInput> participants, bool rally, CancellationToken ct)
    {
        foreach (var previous in d.CampSettlements.Where(s => s.Tile == tile && s.Id != id && s.AppliedOwners.Count < s.Inputs.Count).ToList())
            await ResumeCampLockedAsync(d, previous, ct);
        var record = d.CampSettlements.FirstOrDefault(s => s.Id == id);
        if (record == null)
        {
            record = new CampSettlementRecord { Id = id, Tile = tile, Level = level, AtMs = atMs, Rally = rally,
                Seed = _world.NextSeed(), Inputs = participants, Gone = d.CampRespawns.TryGetValue(tile, out long back) && atMs < back };
            d.CampSettlements.Add(record); d.Version++; await _allianceStore.SaveAsync(d, ct);
        }
        await ResumeCampLockedAsync(d, record, ct);
        return record.Outcome!;
    }
    private async Task ResumeCampLockedAsync(AllianceDirectory d, CampSettlementRecord record, CancellationToken ct)
    {
        if (record.Outcome == null)
        {
            record.Outcome = RallyBattle.Resolve(record.Inputs, record.Level, record.Tile, record.Seed, record.Rally, record.Gone);
            if (record.Outcome.Won) d.CampRespawns[record.Tile] = record.AtMs + CampConfig.RespawnMs;
            d.Version++; await _allianceStore.SaveAsync(d, ct);
        }
        _world.UpdateCampSnapshot(d);
        foreach (var outcome in record.Outcome.Participants)
        {
            if (record.AppliedOwners.Contains(outcome.OwnerId)) continue;
            bool applied = false; MarchEvent? ev = null;
            bool saved = await ModifyCityAsync(outcome.OwnerId, city =>
            {
                ev = MarchLogic.ApplyCampOutcome(city, record.Id, outcome, record.Outcome.Won,
                    record.Outcome.CampGone, record.AtMs, record.Rally);
                applied = city.SocialReceipts.Contains("camp:outcome:" + record.Id);
            }, ct, beforePush: s => ev == null ? Task.CompletedTask : SendMarchArrivalsAsync(s, new() { ev }), saveNow: true);
            if (!saved || !applied) throw new IOException("Camp owner outcome remains pending.");
            record.AppliedOwners.Add(outcome.OwnerId); d.Version++; await _allianceStore.SaveAsync(d, ct);
        }
        if (!record.Terminal)
        {
            // Every owner ACK is already durable. Retain identities/results, discard large battle payloads.
            foreach (var input in record.Inputs) input.Squads.Clear();
            foreach (var outcome in record.Outcome.Participants) outcome.Report = null;
            record.Terminal = true; d.Version++; await _allianceStore.SaveAsync(d, ct);
        }
    }
    private async Task ResolveCampEventAsync(string ownerId, MarchEvent ev, CancellationToken ct)
    {
        try
        {
            await _allianceGate.WaitAsync(ct);
            try
            {
                var city = await ReadCityAsync(ownerId, ct);
                var march = city?.Surface.Marches.FirstOrDefault(m => m.Id == ev.MarchId);
                if (march == null || !march.CampPending) return;
                var point = SurfaceLogic.FromTile(ev.CampTarget!.Value);
                var feature = SurfaceLogic.FeatureAt(point.x, point.y)!;
                var d = await _allianceStore.LoadAsync(ct);
                await SettleCampLockedAsync(d, "solo:" + ownerId + ":" + ev.MarchId, ev.CampTarget.Value, feature.Level,
                    march.ArriveAt, new() { new CampParticipantInput { OwnerId = ownerId, MarchId = march.Id, Hero = march.Hero,
                        Squads = march.CampInputs ?? MarchLogic.Squads(city!, march).Select(s => s.squad).ToList() } }, false, ct);
            }
            finally { _allianceGate.Release(); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogError(ex, "Camp {Owner}:{March} will retry", ownerId, ev.MarchId); }
        finally { _sieging.TryRemove((ownerId, ev.MarchId), out _); }
    }
    public async Task RecoverCampSettlementsAsync(CancellationToken ct)
    {
        await _allianceGate.WaitAsync(ct);
        try
        {
            var d = await _allianceStore.LoadAsync(ct);
            foreach (var record in d.CampSettlements.Where(s => !s.Terminal || s.AppliedOwners.Count < s.Inputs.Count).ToList())
                await ResumeCampLockedAsync(d, record, ct);
            _world.UpdateCampSnapshot(d);
        }
        finally { _allianceGate.Release(); }
    }
}
