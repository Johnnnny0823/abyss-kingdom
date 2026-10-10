using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

/// <summary>
/// 地表的全服世界狀態：哪個主城位置被誰占用（key = SurfaceLogic.Tile(x, y)）。
/// 第一次使用時從存檔讀入所有玩家的主城位置；新玩家登入時分配一個沒人用的候選位置（優先外環）。
/// 玩家的主城位置本身存在各自的城市存檔（CityState.Surface.X/Y），這裡只是全服索引。
/// 也負責資源田的即時狀態（誰在採、剩多少）。資源田狀態只在記憶體：伺服器重開後存量回滿，
/// 但載入時會從存檔把「採集中」的行軍重新登記為佔用者。
/// </summary>
public sealed class WorldService : IFieldRegistry
{
    private sealed class Field
    {
        public long Stock;
        public string? Owner;
        public long March;
        public long RespawnAt;
        /// <summary>佔用者離線時，最早什麼時候需要替他結算（通常是採滿的時間）；0 表示還不知道、下次掃描就查。</summary>
        public long CheckAt;
    }

    private readonly Dictionary<int, Field> _fields = new();
    private readonly Dictionary<int, long> _campBack = new();

    private sealed class Owner
    {
        public string PlayerId = "";
        public string Name = "";
        public long Power;
        public long ShieldUntil;
        public int KeepLevel;
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
        lock (_sites)
            foreach (var r in all)
            {
                var s = r.City.Surface;
                if (!s.Placed) continue;
                int site = SurfaceLogic.Tile(s.X, s.Y);
                if (_sites.ContainsKey(site)) continue;
                Register(site, r);
            }
        lock (_fields)
            foreach (var r in all)
                foreach (var m in r.City.Surface.Marches)
                    if (m.Gathering) _fields[m.Destination] = new Field { Stock = MaxStock(m.Destination), Owner = r.Id, March = m.Id, CheckAt = m.GatherEndMs };
        _loaded = true;
        _log.LogInformation("World loaded: {Count} cities on the surface", _sites.Count);
    }

    /// <summary>登記主城位置（呼叫者要持有 _sites 鎖）。</summary>
    private void Register(int site, PlayerRecord r)
    {
        if (_byPlayer.TryGetValue(r.Id, out var old) && old != site) _sites.Remove(old);
        _sites[site] = new Owner { PlayerId = r.Id, Name = r.DisplayName, Power = PowerLogic.TotalPower(r.City), ShieldUntil = r.City.ShieldUntilMs, KeepLevel = r.City.GetLevel(BuildingType.Keep) };
        _byPlayer[r.Id] = site;
    }

    /// <summary>確保玩家在地表有主城位置。回傳 true 表示這次新分配（存檔需要寫回）。</summary>
    public async Task<bool> EnsureSiteAsync(PlayerRecord record, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            // _lock 只負責「讀存檔、分配」不重入；城市索引本身的讀寫一律用 _sites 鎖（查詢端也用它）
            lock (_sites)
            {
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
            o.ShieldUntil = record.City.ShieldUntilMs;
            o.KeepLevel = record.City.GetLevel(BuildingType.Keep);
        }
    }

    // ───── 資源田 ─────

    private static long MaxStock(int tile)
    {
        var (x, y) = SurfaceLogic.FromTile(tile);
        var f = SurfaceLogic.FeatureAt(x, y);
        return f != null && f.Type == FeatureType.ResourceField ? GatherConfig.FieldStock(f.Level) : 0;
    }

    /// <summary>取得資源田狀態（需持有 _fields 鎖）；採空且過了重生時間就長滿。</summary>
    private Field FieldAt(int tile, long nowMs)
    {
        if (!_fields.TryGetValue(tile, out var f))
        {
            f = new Field { Stock = MaxStock(tile) };
            _fields[tile] = f;
        }
        if (f.Stock <= 0 && f.Owner == null && nowMs >= f.RespawnAt) f.Stock = MaxStock(tile);
        return f;
    }

    public long Occupy(int tile, string owner, long marchId, long nowMs)
    {
        if (MaxStock(tile) <= 0) return 0;
        lock (_fields)
        {
            var f = FieldAt(tile, nowMs);
            if (f.Owner != null && !(f.Owner == owner && f.March == marchId)) return -1;
            if (f.Stock <= 0) return 0;
            f.Owner = owner;
            f.March = marchId;
            f.CheckAt = 0;
            return f.Stock;
        }
    }

    /// <summary>
    /// 被離線玩家佔用、而且到了該替他結算時間的資源田的佔用者（不重複）。
    /// 線上玩家由每秒的推進處理，這裡只管離線的，避免採滿了還一直佔著田。
    /// </summary>
    public List<string> OfflineOccupantsDue(long nowMs, Func<string, bool> isOnline)
    {
        lock (_fields)
            return _fields.Values
                .Where(f => f.Owner != null && nowMs >= f.CheckAt && !isOnline(f.Owner))
                .Select(f => f.Owner!).Distinct().ToList();
    }

    /// <summary>替離線佔用者結算後：還在採的田記下採滿時間，到時再查；佔用者已經找不到的就放開。</summary>
    public void AfterOfflineCheck(string owner, Dictionary<int, long>? stillGatheringUntil)
    {
        lock (_fields)
            foreach (var (tile, f) in _fields)
            {
                if (f.Owner != owner) continue;
                if (stillGatheringUntil != null && stillGatheringUntil.TryGetValue(tile, out var until)) f.CheckAt = until;
                else f.Owner = null; // 存檔裡已經沒有這支採集中的行軍
            }
    }

    public void Release(int tile, string owner, long marchId, long gathered, long nowMs)
    {
        if (MaxStock(tile) <= 0) return;
        lock (_fields)
        {
            var f = FieldAt(tile, nowMs);
            if (f.Owner == owner && f.March == marchId) f.Owner = null;
            f.Stock = Math.Max(0, f.Stock - gathered);
            if (f.Stock == 0) f.RespawnAt = nowMs + GatherConfig.RespawnMs;
        }
    }

    // ───── 怪物營地與戰鬥種子 ─────

    public bool CampAvailable(int tile, long nowMs)
    {
        lock (_campBack) return !_campBack.TryGetValue(tile, out var back) || nowMs >= back;
    }

    public bool TryDefeatCamp(int tile, long nowMs)
    {
        lock (_campBack)
        {
            if (_campBack.TryGetValue(tile, out var back) && nowMs < back) return false;
            _campBack[tile] = nowMs + CampConfig.RespawnMs;
            return true;
        }
    }

    /// <summary>目前被擊敗、還沒重生的營地（公開資訊）。</summary>
    public List<DefeatedCamp> DefeatedCamps(long nowMs)
    {
        lock (_campBack)
            return _campBack.Where(kv => kv.Value > nowMs).Select(kv =>
            {
                var (x, y) = SurfaceLogic.FromTile(kv.Key);
                return new DefeatedCamp { X = x, Y = y, BackAt = kv.Value };
            }).ToList();
    }

    public ulong NextSeed() => BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    /// <summary>某一格的即時狀態（給資訊卡看）。</summary>
    public TileInfoMessage TileInfo(int x, int y, string viewerId, long nowMs)
    {
        var info = new TileInfoMessage { X = x, Y = y };
        int tile = SurfaceLogic.Tile(x, y);
        lock (_campBack)
            if (_campBack.TryGetValue(tile, out var back) && back > nowMs) info.CampBackAt = back;
        info.Max = MaxStock(tile);
        if (info.Max <= 0) return info;
        string? owner;
        lock (_fields)
        {
            var f = FieldAt(tile, nowMs);
            info.Stock = f.Stock;
            owner = f.Owner;
        }
        if (owner != null)
        {
            info.Mine = owner == viewerId;
            lock (_sites)
                info.Occupant = _byPlayer.TryGetValue(owner, out var site) && _sites.TryGetValue(site, out var o) ? o.Name : "某位領主";
        }
        return info;
    }

    /// <summary>地表所有主城（公開資訊；不包含其他玩家的帳號 ID）。</summary>
    public List<CityPin> Pins(string viewerId, long nowMs)
    {
        lock (_sites)
            return _sites.Select(p => new CityPin
            {
                X = p.Key % SurfaceConfig.Size, Y = p.Key / SurfaceConfig.Size, Name = p.Value.Name, Power = p.Value.Power,
                Mine = p.Value.PlayerId == viewerId, Shielded = p.Value.ShieldUntil > nowMs, KeepLevel = p.Value.KeepLevel,
            }).ToList();
    }

    /// <summary>這一格是哪位領主的主城（2×2 佔地內任一格）。</summary>
    public string? CityOwnerAt(int tile)
    {
        var (x, y) = SurfaceLogic.FromTile(tile);
        lock (_sites)
            for (int dy = 0; dy < SurfaceConfig.CityFootprint; dy++)
                for (int dx = 0; dx < SurfaceConfig.CityFootprint; dx++)
                    if (x - dx >= 0 && y - dy >= 0 && _sites.TryGetValue(SurfaceLogic.Tile(x - dx, y - dy), out var o)) return o.PlayerId;
        return null;
    }

    /// <summary>領主名稱（找不到回傳「某位領主」）。</summary>
    public string NameOf(string playerId)
    {
        lock (_sites)
            return _byPlayer.TryGetValue(playerId, out var site) && _sites.TryGetValue(site, out var o) ? o.Name : "某位領主";
    }
}
