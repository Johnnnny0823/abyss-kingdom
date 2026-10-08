using System.Collections.Concurrent;
using System.Text.Json;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

/// <summary>對一個已連線客戶端送訊息的抽象（WebSocket 實作在 Net 資料夾）。</summary>
public interface IClientConnection
{
    Task SendAsync(string type, int reqId, object? data);
    Task CloseAsync(string reason);
}

/// <summary>
/// 一位上線中的玩家。狀態放在記憶體運算，所有修改都必須先取得 Gate（同一玩家的操作一次只跑一個，避免競爭條件）。
/// </summary>
public sealed class PlayerSession
{
    public PlayerSession(PlayerRecord record, IClientConnection connection)
    {
        Record = record;
        Connection = connection;
    }

    public PlayerRecord Record { get; }
    public IClientConnection Connection { get; set; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public bool Dirty { get; set; }
    public string PlayerId => Record.Id;
}

/// <summary>
/// 管理所有上線玩家：登入載入存檔、背景每秒推進城市（完成升級時推播）、定時存檔、下線存檔。
/// </summary>
public sealed class SessionManager : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, PlayerSession> _online = new();
    private readonly IPlayerStore _store;
    private readonly IClock _clock;
    private readonly ILogger<SessionManager> _log;
    private readonly JsonSerializerOptions _json;

    public SessionManager(IPlayerStore store, IClock clock, JsonSerializerOptions json, ILogger<SessionManager> log)
    {
        _json = json;
        _store = store;
        _clock = clock;
        _log = log;
    }

    public int OnlineCount => _online.Count;

    public async Task<PlayerSession> LoginAsync(string playerId, string displayName, IClientConnection connection, CancellationToken ct)
    {
        // 同一帳號重複登入：沿用記憶體中的狀態，踢掉舊連線
        if (_online.TryGetValue(playerId, out var existing))
        {
            await existing.Gate.WaitAsync(ct);
            try
            {
                var old = existing.Connection;
                existing.Connection = connection;
                _ = old.CloseAsync("logged_in_elsewhere");
            }
            finally
            {
                existing.Gate.Release();
            }
            return existing;
        }

        var record = await _store.LoadAsync(playerId, ct);
        var isNew = record == null;
        if (record == null)
        {
            var now = _clock.NowMs;
            record = new PlayerRecord
            {
                Id = playerId,
                DisplayName = displayName,
                City = CityState.CreateNew(now),
                CreatedAtMs = now,
            };
            await _store.SaveAsync(record, ct);
        }

        var session = new PlayerSession(record, connection);
        // 舊存檔（加入道具前建立的帳號）補發一次新手道具
        if (CityLogic.GrantStarterItemsOnce(record.City)) session.Dirty = true;
        var added = _online.GetOrAdd(playerId, session);
        if (!ReferenceEquals(added, session))
        {
            // 極少見：兩個連線同時登入同一帳號，以先到的為準
            added.Connection = connection;
            return added;
        }
        _log.LogInformation("Player {Id} logged in (new={New}). Online={Count}", playerId, isNew, _online.Count);
        return session;
    }

    public async Task LogoutAsync(PlayerSession session, IClientConnection connection)
    {
        if (!ReferenceEquals(session.Connection, connection)) return; // 已被新連線取代
        if (!_online.TryRemove(new KeyValuePair<string, PlayerSession>(session.PlayerId, session))) return;
        await SaveSessionAsync(session, force: true, CancellationToken.None);
        _log.LogInformation("Player {Id} logged out. Online={Count}", session.PlayerId, _online.Count);
    }

    /// <summary>每秒推進所有上線玩家的城市；有升級或訓練完成就推播。</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        var now = _clock.NowMs;
        foreach (var session in _online.Values)
        {
            UpgradeJob? completed;
            TrainingJob? trained;
            byte[]? snapshot = null;
            await session.Gate.WaitAsync(ct);
            try
            {
                completed = CityLogic.Advance(session.Record.City, now, out trained);
                if (completed != null || trained != null)
                {
                    session.Dirty = true;
                    snapshot = JsonSerializer.SerializeToUtf8Bytes(session.Record.City, _json);
                }
            }
            finally
            {
                session.Gate.Release();
            }

            if (snapshot != null)
            {
                if (completed != null)
                    await SafeSend(session, MessageTypes.UpgradeCompleted, new UpgradeCompletedMessage
                    {
                        Building = completed.Building,
                        Level = completed.TargetLevel,
                    });
                if (trained != null)
                    await SafeSend(session, MessageTypes.TrainingCompleted, new TrainingCompletedMessage
                    {
                        Troop = trained.Troop,
                        Count = trained.Count,
                    });
                await SafeSend(session, MessageTypes.CityState, new CityStateMessage { ServerTimeMs = now, City = JsonSerializer.Deserialize<CityState>(snapshot, _json) });
            }
        }
    }

    public async Task SaveAllAsync(bool force, CancellationToken ct)
    {
        foreach (var session in _online.Values)
            await SaveSessionAsync(session, force, ct);
    }

    private async Task SaveSessionAsync(PlayerSession session, bool force, CancellationToken ct)
    {
        await session.Gate.WaitAsync(ct);
        try
        {
            if (!force && !session.Dirty) return;
            CityLogic.Advance(session.Record.City, _clock.NowMs);
            await _store.SaveAsync(session.Record, ct);
            session.Dirty = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Save failed for {Id}", session.PlayerId);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    /// <summary>
    /// GM 工具：修改某位玩家的城市。在線上就在鎖內改並推播最新狀態；離線就讀檔、修改、存檔。
    /// （離線修改與該玩家同時登入有極小的競態，GM 工具可接受。）回傳 false 表示找不到玩家。
    /// </summary>
    public async Task<bool> ModifyCityAsync(string playerId, Action<CityState> apply, CancellationToken ct)
    {
        var now = _clock.NowMs;
        if (_online.TryGetValue(playerId, out var session))
        {
            byte[] snapshot;
            await session.Gate.WaitAsync(ct);
            try
            {
                CityLogic.Advance(session.Record.City, now);
                apply(session.Record.City);
                session.Dirty = true;
                snapshot = JsonSerializer.SerializeToUtf8Bytes(session.Record.City, _json);
            }
            finally
            {
                session.Gate.Release();
            }
            await SafeSend(session, MessageTypes.CityState, new CityStateMessage { ServerTimeMs = now, City = JsonSerializer.Deserialize<CityState>(snapshot, _json) });
            return true;
        }

        var record = await _store.LoadAsync(playerId, ct);
        if (record == null) return false;
        CityLogic.Advance(record.City, now);
        apply(record.City);
        await _store.SaveAsync(record, ct);
        return true;
    }

    private async Task SafeSend(PlayerSession session, string type, object data)
    {
        try { await session.Connection.SendAsync(type, 0, data); }
        catch (Exception ex) { _log.LogDebug(ex, "Push failed for {Id}", session.PlayerId); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastSave = DateTime.UtcNow;
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(stoppingToken);
                    if (DateTime.UtcNow - lastSave >= SaveInterval)
                    {
                        lastSave = DateTime.UtcNow;
                        await SaveAllAsync(force: false, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Tick failed");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await SaveAllAsync(force: true, cancellationToken); // 關機前全部存檔
    }
}
