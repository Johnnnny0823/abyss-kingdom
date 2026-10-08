using System.Collections.Concurrent;
using System.Text.Json;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
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
    private readonly WorldService _world;
    private readonly IClock _clock;
    private readonly ILogger<SessionManager> _log;
    private readonly JsonSerializerOptions _json;

    public SessionManager(IPlayerStore store, IClock clock, JsonSerializerOptions json, ILogger<SessionManager> log, WorldService world)
    {
        _json = json;
        _world = world;
        _store = store;
        _clock = clock;
        _log = log;
    }

    public int OnlineCount => _online.Count;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _loadLocks = new();
    private SemaphoreSlim LoadLock(string playerId) => _loadLocks.GetOrAdd(playerId, _ => new SemaphoreSlim(1, 1));

    public async Task<PlayerSession> LoginAsync(string playerId, string displayName, IClientConnection connection, CancellationToken ct)
    {
        var loadLock = LoadLock(playerId);
        await loadLock.WaitAsync(ct);
        PlayerSession result;
        List<MarchEvent> loginAttacks;
        try
        {
            (result, loginAttacks) = await LoginLockedAsync(playerId, displayName, connection, ct);
        }
        finally
        {
            loadLock.Release();
        }
        foreach (var ev in loginAttacks) await ResolveAttackAsync(result, ev, ct); // 離線期間抵達敵城的行軍
        return result;
    }

    private async Task<(PlayerSession, List<MarchEvent>)> LoginLockedAsync(string playerId, string displayName, IClientConnection connection, CancellationToken ct)
    {
        var noAttacks = new List<MarchEvent>();
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
            return (existing, noAttacks);
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
        if (CityLayout.EnsureLayout(record.City)) session.Dirty = true;
        if (HeroLogic.EnsureStarterHeroes(record.City)) session.Dirty = true;
        if (GearLogic.EnsureStarterGear(record.City)) session.Dirty = true;
        if (LegacyMigration.Run(record.City)) session.Dirty = true; // 舊深淵行軍的部隊還給兵營
        if (PvpLogic.GrantNewbieShieldOnce(record.City, _clock.NowMs)) session.Dirty = true; // 功能上線前的舊帳號也有一次新手護盾
        if (await _world.EnsureSiteAsync(record, ct)) session.Dirty = true; // 地表主城位置
        var loginEvents = MarchLogic.Advance(record.City, _clock.NowMs, _world, playerId);
        if (loginEvents.Count > 0) session.Dirty = true; // 離線期間抵達、採集的行軍
        var added = _online.GetOrAdd(playerId, session);
        if (!ReferenceEquals(added, session))
        {
            // 極少見：兩個連線同時登入同一帳號，以先到的為準
            added.Connection = connection;
            return (added, noAttacks);
        }
        _log.LogInformation("Player {Id} logged in (new={New}). Online={Count}", playerId, isNew, _online.Count);
        return (session, loginEvents.Where(e => e.AttackTarget != null).ToList());
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
            HealingJob? healed;
            List<MarchEvent> marchEvents;
            byte[]? snapshot = null;
            await session.Gate.WaitAsync(ct);
            try
            {
                completed = CityLogic.Advance(session.Record.City, now, out trained, out healed);
                marchEvents = MarchLogic.Advance(session.Record.City, now, _world, session.PlayerId);
                if (completed != null || trained != null || healed != null || marchEvents.Count > 0)
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
                if (healed != null)
                    await SafeSend(session, MessageTypes.HealingCompleted, new HealingCompletedMessage { Troops = healed.Troops });
                foreach (var ev in marchEvents.Where(e => e.AttackTarget == null))
                    await SafeSend(session, MessageTypes.MarchArrived, new MarchArrivedMessage
                    {
                        March = ev.MarchId, Hero = ev.Hero, Tile = ev.Tile, Disbanded = ev.Disbanded,
                        Gathering = ev.Gathering, Occupied = ev.Occupied, GatherDone = ev.GatherDone, Carry = ev.Carry,
                        LootItems = ev.LootItems, BattleWon = ev.BattleWon, BattleLost = ev.BattleLost, CampGone = ev.CampGone,
                        Wounded = ev.Wounded, Dead = ev.Dead,
                    });
                await SafeSend(session, MessageTypes.CityState, new CityStateMessage { ServerTimeMs = now, City = JsonSerializer.Deserialize<CityState>(snapshot, _json) });
                foreach (var ev in marchEvents.Where(e => e.AttackTarget != null))
                    await ResolveAttackAsync(session, ev, ct);
            }
        }
    }

    /// <summary>
    /// 攻城結算（兩段，不同時持有兩位玩家的鎖）：
    /// 1. 在守方身上開打、扣兵、掠奪（守方在線就鎖 Gate，離線就讀檔改完存回）；
    /// 2. 回到攻方的鎖內，套用倖存部隊與掠奪品，行軍回城。
    /// </summary>
    private async Task ResolveAttackAsync(PlayerSession attacker, MarchEvent ev, CancellationToken ct)
    {
        var now = _clock.NowMs;
        ulong seed = _world.NextSeed();
        PvpOutcome? outcome = null;
        bool found = await ModifyCityAsync(ev.AttackTarget, def => outcome = PvpLogic.ResolveOnDefender(def, ev.AttackSquads, seed, now), ct);
        if (!found || outcome == null)
        {
            outcome = new PvpOutcome { Shielded = true };
            foreach (var a in ev.AttackSquads) outcome.AttackerSurvivors[a.troop] = a.squad.Count;
        }
        string targetName = _world.NameOf(ev.AttackTarget);

        if (!outcome.Shielded && _online.TryGetValue(ev.AttackTarget, out var defender))
            await SafeSend(defender, MessageTypes.CityAttacked, new CityAttackedMessage
            {
                AttackerName = attacker.Record.DisplayName, DefenderWon = !outcome.AttackerWon,
                Wounded = outcome.DefenderWounded, Dead = outcome.DefenderDead, Plundered = outcome.Plunder,
            });

        MarchEvent? done;
        byte[]? snapshot = null;
        await attacker.Gate.WaitAsync(ct);
        try
        {
            done = PvpLogic.FinishAttack(attacker.Record.City, ev.MarchId, outcome, now);
            if (done != null)
            {
                attacker.Dirty = true;
                snapshot = JsonSerializer.SerializeToUtf8Bytes(attacker.Record.City, _json);
            }
        }
        finally
        {
            attacker.Gate.Release();
        }
        if (done == null || snapshot == null) return;
        await SafeSend(attacker, MessageTypes.MarchArrived, new MarchArrivedMessage
        {
            March = done.MarchId, Hero = done.Hero, Tile = done.Tile, BattleWon = done.BattleWon, BattleLost = done.BattleLost,
            Shielded = done.Shielded, Carry = done.Carry, Wounded = done.Wounded, Dead = done.Dead, TargetName = targetName,
        });
        await SafeSend(attacker, MessageTypes.CityState, new CityStateMessage { ServerTimeMs = now, City = JsonSerializer.Deserialize<CityState>(snapshot, _json) });
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
            _world.Update(session.Record);
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

        var loadLock = LoadLock(playerId);
        await loadLock.WaitAsync(ct);
        try
        {
            // 拿到鎖之後玩家可能剛好登入了：改走線上的路
            if (_online.ContainsKey(playerId))
            {
                loadLock.Release();
                loadLock = null;
                return await ModifyCityAsync(playerId, apply, ct);
            }
            var record = await _store.LoadAsync(playerId, ct);
            if (record == null) return false;
            CityLogic.Advance(record.City, now);
            apply(record.City);
            await _store.SaveAsync(record, ct);
            _world.Update(record);
            return true;
        }
        finally
        {
            loadLock?.Release();
        }
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
