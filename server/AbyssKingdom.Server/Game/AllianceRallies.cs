using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

public sealed partial class AllianceService
{
    private static bool RallyMember(AllianceDirectory d, AllianceRallyRecord r, string owner, long epoch) =>
        d.Alliances.Any(a => a.Id == r.AllianceId && !a.PendingCreate && a.Members.Any(m => m.PlayerId == owner && m.Epoch == epoch));
    private static bool SameTroops(Dictionary<TroopType, long> first, Dictionary<TroopType, long> second) =>
        first.Count == second.Count && first.All(p => second.TryGetValue(p.Key, out var count) && p.Value == count);

    public async Task<ReinforcementActionResult> CreateRallyAsync(string actorId, HeroId hero, Dictionary<TroopType, long> troops,
        int x, int y, int seconds, string operationId, CancellationToken ct)
    {
        if (_sessions == null || _territoryWorld == null) return new(AllianceResult.PersistenceFailed);
        if (!Guid.TryParse(operationId, out var op) || troops == null || !RallyBattle.ValidCountdown(seconds)) return new(AllianceResult.InvalidTarget);
        if (!SurfaceLogic.Walkable(x, y)) return new(AllianceResult.InvalidTarget);
        var feature = SurfaceLogic.FeatureAt(x, y);
        if (feature?.Type != FeatureType.MonsterCamp || feature.Level < 2) return new(AllianceResult.InvalidTarget);
        string id = actorId + ":" + op.ToString("N");
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct); var rally = d.Rallies.FirstOrDefault(r => r.Id == id);
            if (rally == null)
            {
                if (d.Rallies.SelectMany(r => r.Participants).Any(p => p.JoinId == id)) return new(AllianceResult.InvalidTarget);
                var found = Membership(d, actorId);
                if (found == null || found.Value.Item1.PendingCreate) return new(AllianceResult.NotMember);
                int target = SurfaceLogic.Tile(x, y);
                if (!_territoryWorld.CampAvailable(target, _clock.NowMs)) return new(AllianceResult.InvalidTarget);
                var city = await _sessions.ReadCityAsync(actorId, ct);
                if (city == null) return new(AllianceResult.NotFound);
                var valid = AllianceMilitaryRules.ValidateRallyParticipant(city, troops);
                if (valid != AllianceMilitaryResult.Ok) return new(AllianceResult.MilitaryRejected, valid);
                valid = MarchLogic.StartRally(city, city, id, id, hero, troops, _clock.NowMs,
                    _clock.NowMs + seconds * 1_000L, true, out _);
                if (valid != AllianceMilitaryResult.Ok) return new(AllianceResult.MilitaryRejected, valid);
                var home = SurfaceLogic.CityCenter(city.Surface);
                rally = new AllianceRallyRecord { Id = id, AllianceId = found.Value.Item1.Id, LeaderId = actorId,
                    LeaderEpoch = found.Value.Item2.Epoch, HostTile = SurfaceLogic.Tile(home.x, home.y), TargetTile = target,
                    Level = feature.Level, Capacity = AllianceMilitaryRules.RallyTotalCapacity(city.GetLevel(BuildingType.Keep)),
                    DepartAtMs = _clock.NowMs + seconds * 1_000L,
                    Seconds = seconds,
                    Participants = new() { new() { OwnerId = actorId, JoinId = id, Epoch = found.Value.Item2.Epoch,
                        Hero = hero, Troops = new(troops) } } };
                d.Rallies.Add(rally); d.Version++; await _store.SaveAsync(d, ct);
            }
            var leader = rally.Participants[0];
            if (rally.TargetTile != SurfaceLogic.Tile(x, y) || rally.Seconds != seconds || leader.Hero != hero || !SameTroops(leader.Troops, troops))
                return new(AllianceResult.InvalidTarget);
            if (!await PayRallyParticipantLockedAsync(d, rally, leader, ct)) return new(AllianceResult.PersistenceFailed);
            if (leader.Left) { rally.CancelRequested = true; await SaveRallyDirectory(d, ct); }
            await NotifyRallyLockedAsync(rally);
            return leader.Left ? new(AllianceResult.MilitaryRejected, leader.MilitaryResult, id) : new(AllianceResult.Ok, Id: id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(AllianceResult.PersistenceFailed); }
        finally { _operationGate.Release(); }
    }

    public async Task<ReinforcementActionResult> JoinRallyAsync(string actorId, string id, HeroId hero,
        Dictionary<TroopType, long> troops, string operationId, CancellationToken ct)
    {
        if (_sessions == null) return new(AllianceResult.PersistenceFailed);
        if (!Guid.TryParse(operationId, out var op) || troops == null) return new(AllianceResult.InvalidTarget);
        string joinId = actorId + ":" + op.ToString("N");
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct); var r = d.Rallies.FirstOrDefault(r => r.Id == id);
            if (r == null) return new(AllianceResult.NotFound);
            var p = r.Participants.FirstOrDefault(p => p.JoinId == joinId);
            if (p == null)
            {
                if (d.Rallies.SelectMany(other => other.Participants).Any(p => p.JoinId == joinId)) return new(AllianceResult.InvalidTarget);
                if (r.State != RallyState.Forming || r.CancelRequested || _clock.NowMs >= r.DepartAtMs) return new(AllianceResult.PendingOperations);
                var member = Membership(d, actorId);
                if (member == null || member.Value.Item1.Id != r.AllianceId || member.Value.Item1.PendingCreate) return new(AllianceResult.NotMember);
                if (r.Participants.Any(p => p.OwnerId == actorId && !p.Left)) return new(AllianceResult.AlreadyMember);
                if (r.Participants.Count(p => !p.Left) >= 5) return new(AllianceResult.Full);
                var city = await _sessions.ReadCityAsync(actorId, ct);
                if (city == null) return new(AllianceResult.NotFound);
                var valid = AllianceMilitaryRules.ValidateRallyParticipant(city, troops);
                if (valid != AllianceMilitaryResult.Ok) return new(AllianceResult.MilitaryRejected, valid);
                long total = r.Participants.Where(p => !p.Left).Sum(p => p.Troops.Values.Sum()) + troops.Values.Sum();
                var host = await _sessions.ReadCityAsync(r.LeaderId, ct);
                long currentCapacity = host == null ? 0 : AllianceMilitaryRules.RallyTotalCapacity(host.GetLevel(BuildingType.Keep));
                if (currentCapacity == 0) return new(AllianceResult.MilitaryRejected, AllianceMilitaryResult.KeepLocked);
                if (total > currentCapacity) return new(AllianceResult.MilitaryRejected, AllianceMilitaryResult.OverCapacity);
                valid = MarchLogic.StartRally(city, host!, r.Id, joinId, hero, troops, _clock.NowMs, r.DepartAtMs, false, out _);
                if (valid != AllianceMilitaryResult.Ok) return new(AllianceResult.MilitaryRejected, valid);
                r.Capacity = currentCapacity;
                p = new RallyParticipantRecord { OwnerId = actorId, JoinId = joinId, Epoch = member.Value.Item2.Epoch, Hero = hero, Troops = new(troops) };
                r.Participants.Add(p); d.Version++; await _store.SaveAsync(d, ct);
            }
            if (p.Hero != hero || !SameTroops(p.Troops, troops)) return new(AllianceResult.InvalidTarget);
            if (!await PayRallyParticipantLockedAsync(d, r, p, ct)) return new(AllianceResult.PersistenceFailed);
            await NotifyRallyLockedAsync(r);
            return p.Left ? new(AllianceResult.MilitaryRejected, p.MilitaryResult, id) : new(AllianceResult.Ok, Id: id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(AllianceResult.PersistenceFailed); }
        finally { _operationGate.Release(); }
    }

    private async Task<bool> PayRallyParticipantLockedAsync(AllianceDirectory d, AllianceRallyRecord r, RallyParticipantRecord p, CancellationToken ct)
    {
        if (p.Paid) return true;
        var leader = await _sessions!.ReadCityAsync(r.LeaderId, ct);
        bool saved = await _sessions.ModifyCityAsync(p.OwnerId, city =>
        {
            var march = city.Surface.Marches.FirstOrDefault(m => m.RallyJoinId == p.JoinId && m.RallyId == r.Id);
            string receipt = "rally:join:" + p.JoinId;
            if (!city.SocialReceipts.Contains(receipt))
            {
                if (p.Left || r.CancelRequested || !RallyMember(d, r, p.OwnerId, p.Epoch) || leader == null)
                { p.Left = true; p.Returned = true; p.MilitaryResult = AllianceMilitaryResult.InvalidTarget; return; }
                p.MilitaryResult = MarchLogic.StartRally(city, leader, r.Id, p.JoinId, p.Hero, p.Troops,
                    _clock.NowMs, r.DepartAtMs, p.OwnerId == r.LeaderId, out long marchId);
                if (p.MilitaryResult != AllianceMilitaryResult.Ok) { p.Left = true; p.Returned = true; return; }
                march = city.Surface.Marches.First(m => m.Id == marchId); city.SocialReceipts.Add(receipt);
            }
            if (march == null) p.Returned = true;
            else { p.MarchId = march.Id; if (p.Left) MarchLogic.ReturnRally(city, march.Id, _clock.NowMs); }
        }, ct, saveNow: true);
        if (!saved) return false;
        p.Paid = true; d.Version++; await _store.SaveAsync(d, ct);
        return true;
    }

    public async Task<AllianceResult> RecallRallyAsync(string actorId, string id, CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct); var r = d.Rallies.FirstOrDefault(r => r.Id == id);
            if (r == null) return AllianceResult.NotFound;
            var p = r.Participants.FirstOrDefault(p => p.OwnerId == actorId && !p.Left);
            if (p == null && actorId != r.LeaderId) return AllianceResult.Forbidden;
            if ((r.State == RallyState.Forming && _clock.NowMs >= r.DepartAtMs) ||
                (r.State == RallyState.Traveling && _clock.NowMs >= r.Times[^1]))
                await AdvanceRallyLockedAsync(d, r, ct);
            if (r.State == RallyState.Settling) return AllianceResult.PendingOperations;
            if (r.State is RallyState.Returning or RallyState.Finished) return AllianceResult.Ok;
            if (r.State != RallyState.Forming && actorId != r.LeaderId) return AllianceResult.Forbidden;
            if (actorId == r.LeaderId) r.CancelRequested = true;
            else p!.Left = true;
            d.Version++; await _store.SaveAsync(d, ct);
            await AdvanceRallyLockedAsync(d, r, ct);
            await NotifyRallyLockedAsync(r);
            return AllianceResult.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return AllianceResult.PersistenceFailed; }
        finally { _operationGate.Release(); }
    }

    private async Task SaveRallyDirectory(AllianceDirectory d, CancellationToken ct) { d.Version++; await _store.SaveAsync(d, ct); }
    private async Task AdvanceRallyLockedAsync(AllianceDirectory d, AllianceRallyRecord r, CancellationToken ct)
    {
        foreach (var p in r.Participants.Where(p => !p.Paid))
            if (!await PayRallyParticipantLockedAsync(d, r, p, ct)) throw new IOException("Rally troop debit pending.");
        if (r.State == RallyState.Forming)
        {
            if (!RallyMember(d, r, r.LeaderId, r.LeaderEpoch)) r.CancelRequested = true;
            foreach (var p in r.Participants.Where(p => !p.Left && !RallyMember(d, r, p.OwnerId, p.Epoch))) p.Left = true;
            await SaveRallyDirectory(d, ct);
        }
        if (r.CancelRequested && r.State != RallyState.Settling && r.State != RallyState.Finished)
        { r.Cancelled = true; r.State = RallyState.Returning; foreach (var p in r.Participants) p.Left = true; await SaveRallyDirectory(d, ct); }
        foreach (var p in r.Participants.Where(p => p.Left && !p.Returned))
        {
            if (p.ReturnAtMs > _clock.NowMs) continue;
            if (!await _sessions!.ModifyCityAsync(p.OwnerId, city =>
            {
                MarchLogic.ReturnRally(city, p.MarchId, _clock.NowMs);
                MarchLogic.AdvanceRallyReturn(city, p.MarchId, _clock.NowMs);
                p.Returned = city.Surface.Marches.All(m => m.RallyJoinId != p.JoinId);
                p.ReturnAtMs = city.Surface.Marches.FirstOrDefault(m => m.RallyJoinId == p.JoinId)?.ArriveAt ?? 0;
            }, ct, saveNow: true)) throw new IOException("Rally withdrawn participant return pending.");
            await SaveRallyDirectory(d, ct);
        }
        if (r.State == RallyState.Forming && _clock.NowMs >= r.DepartAtMs)
        {
            var active = r.Participants.Where(p => !p.Left && !p.Returned).ToList();
            r.FrozenInputs.Clear(); long speed = long.MaxValue;
            foreach (var p in active)
            {
                var owner = await _sessions!.ReadCityAsync(p.OwnerId, ct);
                var march = owner?.Surface.Marches.FirstOrDefault(m => m.RallyJoinId == p.JoinId && !m.Returning);
                if (march == null || march.ArriveAt > r.DepartAtMs) { p.Left = true; continue; }
                r.FrozenInputs.Add(new CampParticipantInput { OwnerId = p.OwnerId, MarchId = march.Id, Hero = p.Hero,
                    Squads = MarchLogic.Squads(owner!, march).Select(s => s.squad).ToList() });
                speed = Math.Min(speed, MarchLogic.SpeedPercent(march.Troops));
            }
            if (r.FrozenInputs.Count < 2 || !r.FrozenInputs.Any(p => p.OwnerId == r.LeaderId))
            { r.CancelRequested = true; await SaveRallyDirectory(d, ct); await AdvanceRallyLockedAsync(d, r, ct); return; }
            var host = SurfaceLogic.FromTile(r.HostTile); var target = SurfaceLogic.FromTile(r.TargetTile);
            var path = MarchLogic.FindPath(host.x, host.y, target.x, target.y) ?? throw new InvalidOperationException("Rally target unreachable.");
            r.Path = path.Select(p => SurfaceLogic.Tile(p.x, p.y)).ToList(); r.Times = MarchLogic.Schedule(r.Path, r.DepartAtMs, speed);
            r.State = RallyState.Launching; await SaveRallyDirectory(d, ct);
        }
        if (r.State == RallyState.Launching)
        {
            long total = r.FrozenInputs.Sum(p => p.Squads.Sum(s => s.Count));
            var combinedTroops = r.FrozenInputs.SelectMany(p => p.Squads).GroupBy(s => s.Troop).ToDictionary(g => g.Key, g => g.Sum(s => s.Count));
            foreach (var input in r.FrozenInputs)
                if (!await _sessions!.ModifyCityAsync(input.OwnerId, city =>
                {
                    var m = city.Surface.Marches.First(m => m.Id == input.MarchId);
                    m.Path = new(r.Path); m.ArriveAtMs = new(r.Times); m.Stationed = false; m.RallyTotalTroops = total;
                    m.RallyLeaderHero = r.FrozenInputs.First(p => p.OwnerId == r.LeaderId).Hero;
                    m.RallyMainTroop = MarchLogic.MainTroop(combinedTroops);
                }, ct, saveNow: true)) throw new IOException("Rally launch pending.");
            r.State = RallyState.Traveling;
            foreach (var input in r.FrozenInputs) MarkGuide(d, input.OwnerId, AllianceConfig.GuideRallied);
            await SaveRallyDirectory(d, ct);
        }
        if (r.State == RallyState.Traveling && _clock.NowMs >= r.Times[^1])
        { r.State = RallyState.Settling; await SaveRallyDirectory(d, ct); }
        if (r.State == RallyState.Settling)
        {
            r.Outcome = await _sessions!.SettleCampLockedAsync(d, "rally:" + r.Id, r.TargetTile, r.Level, r.Times[^1], r.FrozenInputs, true, ct);
            r.State = RallyState.Returning; await SaveRallyDirectory(d, ct);
        }
        if (r.State == RallyState.Returning)
        {
            foreach (var p in r.Participants.Where(p => !p.Returned))
            {
                if (p.ReturnAtMs > _clock.NowMs) continue;
                if (!await _sessions!.ModifyCityAsync(p.OwnerId, city =>
                {
                    MarchLogic.AdvanceRallyReturn(city, p.MarchId, _clock.NowMs);
                    p.Returned = city.Surface.Marches.All(m => m.RallyJoinId != p.JoinId);
                    p.ReturnAtMs = city.Surface.Marches.FirstOrDefault(m => m.RallyJoinId == p.JoinId)?.ArriveAt ?? 0;
                }, ct, saveNow: true)) throw new IOException("Rally homecoming pending.");
                await SaveRallyDirectory(d, ct);
            }
            if (r.Participants.All(p => p.Returned)) { r.State = RallyState.Finished; await SaveRallyDirectory(d, ct); }
        }
    }
    private Task NotifyRallyLockedAsync(AllianceRallyRecord r) => _sessions == null ? Task.CompletedTask
        : _sessions.PushToPlayersAsync(r.Participants.Select(p => p.OwnerId), MessageTypes.AllianceMilitaryChanged, new { });
    public async Task AdvanceRalliesAsync(CancellationToken ct)
    {
        if (_sessions == null) return;
        await _operationGate.WaitAsync(ct);
        try
        {
            var d = await _store.LoadAsync(ct);
            foreach (var r in d.Rallies.Where(r => r.State != RallyState.Finished).ToList())
            { await AdvanceRallyLockedAsync(d, r, ct); await NotifyRallyLockedAsync(r); }
        }
        finally { _operationGate.Release(); }
    }
}
