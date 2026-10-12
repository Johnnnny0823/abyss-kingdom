using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Social;

namespace AbyssKingdom.Server.Game;

public sealed partial class WorldService
{
    private readonly object _territoryLock = new();
    private Dictionary<string, List<(long finish, HashSet<int> cells)>> _territories = new();
    public void UpdateCampSnapshot(AllianceDirectory d)
    {
        lock (_campBack)
        {
            _campBack.Clear(); foreach (var entry in d.CampRespawns) _campBack[entry.Key] = entry.Value;
        }
    }
    public void UpdateTerritory(AllianceDirectory d)
    {
        var next = new Dictionary<string, List<(long, HashSet<int>)>>();
        foreach (var alliance in d.Alliances.Where(a => !a.PendingCreate))
        {
            var regions = alliance.Flags.Where(f => f.Live && f.Paid)
                .Select(f => (f.CompletesAtMs, AllianceTerritory.Coverage(f.X, f.Y))).ToList();
            foreach (var member in alliance.Members) next[member.PlayerId] = regions;
        }
        lock (_territoryLock) _territories = next;
    }
    public int GatherBonusBasisPoints(string owner, int tile, long nowMs)
    {
        lock (_territoryLock)
            return owner != null && _territories.TryGetValue(owner, out var regions) &&
                regions.Any(r => nowMs >= r.finish && r.cells.Contains(tile)) ? AllianceTerritory.GatherBasisPoints : 10_000;
    }
}
