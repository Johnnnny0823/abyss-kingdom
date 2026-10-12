using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

public sealed partial class AllianceService
{
    public async Task<FlagResult> PlaceFlagAsync(string actorId, int slot, int x, int y, string operationId, CancellationToken ct)
    {
        if (_sessions == null) return FlagResult.PersistenceFailed;
        if (!Guid.TryParse(operationId, out var operation)) return FlagResult.NotFound;
        string id = actorId + ":" + operation.ToString("N");
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct);
            var existing = d.Alliances.SelectMany(a => a.Flags).FirstOrDefault(f => f.Id == id);
            if (existing != null)
            {
                if (existing.Slot != slot || existing.X != x || existing.Y != y) return FlagResult.NotFound;
                if (!existing.Live) return FlagResult.NotFound;
                return await PayFlagLockedAsync(d, existing, ct);
            }
            var found = Membership(d, actorId);
            if (found == null || found.Value.Item1.PendingCreate) return FlagResult.NotMember;
            var (alliance, actor) = found.Value;
            if (actor.Role == AllianceRole.Member) return FlagResult.Forbidden;
            if (AllianceTerritory.Cost(slot) == 0) return FlagResult.InvalidSlot;
            if (alliance.Flags.Any(f => f.Slot == slot && f.Live)) return FlagResult.SlotBusy;
            if (alliance.Flags.Any(f => f.Slot == slot && f.RemovedAtMs > 0 &&
                _clock.NowMs < f.RemovedAtMs + AllianceTerritory.RebuildCooldownMs)) return FlagResult.Cooldown;
            if (!AllianceTerritory.AllowedPoint(x, y)) return FlagResult.BlockedPoint;
            var coverage = AllianceTerritory.Coverage(x, y);
            foreach (var other in d.Alliances.Where(a => a.Id != alliance.Id))
                foreach (var flag in other.Flags.Where(f => f.Live))
                    if (coverage.Overlaps(AllianceTerritory.Coverage(flag.X, flag.Y))) return FlagResult.Conflict;
            var active = new HashSet<int>();
            foreach (var flag in alliance.Flags.Where(f => f.Active(_clock.NowMs)))
                active.UnionWith(AllianceTerritory.Coverage(flag.X, flag.Y));
            if (alliance.Flags.Any(f => f.Live))
            {
                if (!AllianceTerritory.Touches(coverage, active)) return FlagResult.Disconnected;
            }
            else
            {
                bool near = false;
                foreach (var member in alliance.Members)
                {
                    var city = await _sessions.ReadCityAsync(member.PlayerId, ct);
                    if (city?.Surface.Placed != true) continue;
                    var center = SurfaceLogic.CityCenter(city.Surface);
                    if (AllianceTerritory.Coverage(center.x, center.y, 10).Contains(SurfaceLogic.Tile(x, y)))
                    { near = true; break; }
                }
                if (!near) return FlagResult.Disconnected;
            }
            var reserved = new AllianceFlag { Id = id, PayerId = actorId, Slot = slot, X = x, Y = y,
                CreatedAtMs = _clock.NowMs, CompletesAtMs = _clock.NowMs + AllianceTerritory.BuildMs };
            alliance.Flags.Add(reserved);
            d.Version++; await _store.SaveAsync(d, ct); // Conflict reservation before payment.
            return await PayFlagLockedAsync(d, reserved, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return FlagResult.PersistenceFailed; }
        finally { _operationGate.Release(); }
    }

    private async Task<FlagResult> PayFlagLockedAsync(AllianceDirectory d, AllianceFlag flag, CancellationToken ct)
    {
        if (flag.Paid) return FlagResult.Ok;
        FlagResult result = FlagResult.Ok;
        string receipt = "alliance:flag:" + flag.Id;
        bool saved = await _sessions!.ModifyCityAsync(flag.PayerId, city =>
        {
            if (city.SocialReceipts.Contains(receipt)) return;
            var member = Membership(d, flag.PayerId);
            if (member == null || member.Value.Item2.Role == AllianceRole.Member ||
                !member.Value.Item1.Flags.Contains(flag)) { result = FlagResult.Forbidden; return; }
            long cost = AllianceTerritory.Cost(flag.Slot);
            if (city.GetResource(ResourceType.Wood) < cost || city.GetResource(ResourceType.Stone) < cost)
            { result = FlagResult.InsufficientResources; return; }
            city.Resources[ResourceType.Wood] -= cost; city.Resources[ResourceType.Stone] -= cost;
            city.SocialReceipts.Add(receipt);
        }, ct, saveNow: true);
        if (!saved) return FlagResult.PersistenceFailed;
        flag.Paid = result == FlagResult.Ok; flag.Rejected = result != FlagResult.Ok;
        d.Version++; await _store.SaveAsync(d, ct);
        _territoryWorld?.UpdateTerritory(d);
        var alliance = d.Alliances.First(a => a.Flags.Contains(flag));
        await _sessions.PushToPlayersAsync(alliance.Members.Select(m => m.PlayerId), MessageTypes.AllianceMilitaryChanged, new { });
        return result;
    }

    public async Task<FlagResult> RemoveFlagAsync(string actorId, int slot, CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct);
            var found = Membership(d, actorId);
            if (found == null) return FlagResult.NotMember;
            var (alliance, actor) = found.Value;
            if (actor.Role == AllianceRole.Member) return FlagResult.Forbidden;
            var flag = alliance.Flags.FirstOrDefault(f => f.Slot == slot && f.Live);
            if (flag == null) return FlagResult.NotFound;
            if (!flag.Paid)
            {
                var payment = await PayFlagLockedAsync(d, flag, ct);
                if (payment != FlagResult.Ok) return payment;
            }
            var remaining = alliance.Flags.Where(f => f.Id != flag.Id && f.Live).ToList();
            var active = remaining.Where(f => f.Active(_clock.NowMs)).Select(f => AllianceTerritory.Coverage(f.X, f.Y)).ToList();
            if (!AllianceTerritory.Connected(active)) return FlagResult.Disconnected;
            var activeUnion = new HashSet<int>(); foreach (var region in active) activeUnion.UnionWith(region);
            foreach (var pending in remaining.Where(f => !f.Active(_clock.NowMs)))
                if (!AllianceTerritory.Touches(AllianceTerritory.Coverage(pending.X, pending.Y), activeUnion))
                    return FlagResult.Disconnected;
            flag.RemovedAtMs = _clock.NowMs;
            d.Version++; await _store.SaveAsync(d, ct);
            _territoryWorld?.UpdateTerritory(d);
            if (_sessions != null) await _sessions.PushToPlayersAsync(alliance.Members.Select(m => m.PlayerId), MessageTypes.AllianceMilitaryChanged, new { });
            return FlagResult.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return FlagResult.PersistenceFailed; }
        finally { _operationGate.Release(); }
    }

    public async Task RecoverFlagPaymentsAsync(CancellationToken ct)
    {
        if (_sessions == null) return;
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct);
            foreach (var flag in d.Alliances.SelectMany(a => a.Flags).Where(f => f.Live && !f.Paid))
                if (await PayFlagLockedAsync(d, flag, ct) == FlagResult.PersistenceFailed)
                    throw new IOException("Flag payment recovery remains pending.");
            _territoryWorld?.UpdateTerritory(d);
        }
        finally { _operationGate.Release(); }
    }
}
