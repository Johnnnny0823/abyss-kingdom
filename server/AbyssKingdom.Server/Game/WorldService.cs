using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

/// <summary>
/// 地表的全服世界狀態：哪個主城位置被誰占用（key = SurfaceLogic.Tile(x, y)）。
/// 第一次使用時從存檔讀入所有玩家的主城位置；新玩家登入時分配一個沒人用的候選位置（優先外環）。
/// 玩家的主城位置本身存在各自的城市存檔（CityState.Surface.X/Y），這裡只是全服索引。
/// </summary>
public sealed class WorldService
{
    private sealed class Owner
    {
        public string PlayerId = "";
        public string Name = "";
        public long Power;
    }

    private readonly IPlayerStore _store;
    private readonly ILogger<WorldService> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<int, Owner> _sites = new();
    private readonly Dictionary<string, int> _byPlayer = new();
    private readonly Random _rng = new();
    private bool _loaded;

    public WorldService(IPlayerStore store, ILogger<WorldService> log)
    {
        _store = store;
        _log = log;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        var all = await _store.ListAllAsync(ct);
        foreach (var r in all)
        {
            var s = r.City.Surface;
            if (!s.Placed) continue;
            int site = SurfaceLogic.Tile(s.X, s.Y);
            if (_sites.ContainsKey(site)) continue;
            Register(site, r);
        }
        _loaded = true;
        _log.LogInformation("World loaded: {Count} cities on the surface", _sites.Count);
    }

    private void Register(int site, PlayerRecord r)
    {
        if (_byPlayer.TryGetValue(r.Id, out var old) && old != site) _sites.Remove(old);
        _sites[site] = new Owner { PlayerId = r.Id, Name = r.DisplayName, Power = PowerLogic.TotalPower(r.City) };
        _byPlayer[r.Id] = site;
    }

    /// <summary>確保玩家在地表有主城位置。回傳 true 表示這次新分配（存檔需要寫回）。</summary>
    public async Task<bool> EnsureSiteAsync(PlayerRecord record, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            var s = record.City.Surface;
            if (s.Placed)
            {
                int site = SurfaceLogic.Tile(s.X, s.Y);
                if (!_sites.TryGetValue(site, out var owner) || owner.PlayerId == record.Id)
                {
                    Register(site, record);
                    return false;
                }
            }

            var picked = SurfaceLogic.PickSite(_sites.Keys, n => _rng.Next(n));
            if (picked == null)
            {
                _log.LogWarning("No free city site for {Id}", record.Id);
                return false;
            }
            s.X = picked.Value.x;
            s.Y = picked.Value.y;
            Register(SurfaceLogic.Tile(s.X, s.Y), record);
            _log.LogInformation("Assigned city ({X},{Y}) to {Id}", s.X, s.Y, record.Id);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>更新玩家公開資訊（名字、總戰力），例如存檔時。</summary>
    public void Update(PlayerRecord record)
    {
        lock (_sites)
        {
            if (!_byPlayer.TryGetValue(record.Id, out var site) || !_sites.TryGetValue(site, out var o)) return;
            o.Name = record.DisplayName;
            o.Power = PowerLogic.TotalPower(record.City);
        }
    }

    /// <summary>地表所有主城（公開資訊；不包含其他玩家的帳號 ID）。</summary>
    public List<CityPin> Pins(string viewerId)
    {
        lock (_sites)
            return _sites.Select(p => new CityPin { X = p.Key % SurfaceConfig.Size, Y = p.Key / SurfaceConfig.Size, Name = p.Value.Name, Power = p.Value.Power, Mine = p.Value.PlayerId == viewerId }).ToList();
    }
}
