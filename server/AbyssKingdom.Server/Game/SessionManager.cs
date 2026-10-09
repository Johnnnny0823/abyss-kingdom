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

    /// <summary>已下線且存檔完成、移出線上名單（持有 Gate 時設定）。之後對它的修改都不算數，要改走離線讀檔。</summary>
    public bool Closed { get; set; }

    /// <summary>連線已斷，但下線存檔失敗：留在線上名單，等自動存檔重試成功再移除。</summary>
    public bool Disconnected { get; set; }
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

    /// <summary>正在結算中的攻城（玩家 ID, 行軍 ID）：從產生攻城事件起、到攻方套用完結果為止。</summary>
    private readonly ConcurrentDictionary<(string, long), byte> _sieging = new();

    /// <summary>
    /// 推進玩家的行軍並收下事件（呼叫者要持有該玩家的 Gate）。產生的攻城事件會登記為「結算中」，
    /// 呼叫者放開鎖後要交給 DispatchMarchEventsAsync / ResolveAttacksAsync 處理，否則行軍會卡在城下。
    /// </summary>
    public List<MarchEvent> AdvanceMarches(PlayerSession session, long nowMs) => AdvanceMarches(session.Record.City, session.PlayerId, nowMs);

    /// <summary>卡在城下、又不在結算中的攻城：重新產生攻城事件並登記為結算中（呼叫者要持有該玩家的鎖或 LoadLock）。</summary>
    private List<MarchEvent> ResumeSieges(CityState city, string playerId, long nowMs)
    {
        var events = MarchLogic.ResumeSieges(city, nowMs, id => _sieging.ContainsKey((playerId, id)));
        foreach (var ev in events) _sieging[(playerId, ev.MarchId)] = 0;
        return events;
    }

    /// <summary>
    /// 線上玩家的攻城如果結算失敗（例如守方存檔失敗）會留在城下：定期用同一個戰鬥 ID 重試。
    /// 離線玩家的會在下次登入時接續。
    /// </summary>
    public async Task RecoverStuckSiegesAsync(CancellationToken ct)
    {
        var now = _clock.NowMs;
        foreach (var session in _online.Values)
        {
            if (session.Record.City.Surface.Marches.All(m => !m.Sieging)) continue;
            List<MarchEvent> events;
            await session.Gate.WaitAsync(ct);
            try
            {
                if (session.Closed) continue;
                events = ResumeSieges(session.Record.City, session.PlayerId, now);
                if (events.Count > 0) session.Dirty = true;
            }
            finally
            {
                session.Gate.Release();
            }
            await ResolveAttacksAsync(session, events, ct);
        }
    }

    private List<MarchEvent> AdvanceMarches(CityState city, string playerId, long nowMs)
    {
        var events = MarchLogic.Advance(city, nowMs, _world, playerId);
        foreach (var ev in events)
            if (ev.AttackTarget != null) _sieging[(playerId, ev.MarchId)] = 0;
        return events;
    }

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
        await ResolveAttacksAsync(result, loginAttacks, ct); // 離線期間抵達敵城的行軍
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
                existing.Disconnected = false; // 下線存檔失敗、還留在線上的狀態：直接接回
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
        // 上次攻城結算到一半伺服器就停了（存檔裡卡在城下、又沒有在結算）：用同一個戰鬥 ID 接著結算
        var resumed = ResumeSieges(record.City, playerId, _clock.NowMs);
        if (resumed.Count > 0) session.Dirty = true;
        var loginEvents = AdvanceMarches(record.City, playerId, _clock.NowMs);
        loginEvents.AddRange(resumed);
        if (loginEvents.Count > 0) session.Dirty = true; // 離線期間抵達、採集的行軍
        var added = _online.GetOrAdd(playerId, session);
        if (!ReferenceEquals(added, session))
        {
            // 極少見：兩個連線同時登入同一帳號，以先到的為準（這份讀出來的存檔作廢）
            foreach (var ev in loginEvents) _sieging.TryRemove((playerId, ev.MarchId), out _);
            added.Connection = connection;
            return (added, noAttacks);
        }
        _log.LogInformation("Player {Id} logged in (new={New}). Online={Count}", playerId, isNew, _online.Count);
        return (session, loginEvents.Where(e => e.AttackTarget != null).ToList());
    }

    public async Task LogoutAsync(PlayerSession session, IClientConnection connection)
    {
        // 和登入、離線修改用同一把鎖：存檔完成前，不會有人讀到舊存檔
        var loadLock = LoadLock(session.PlayerId);
        await loadLock.WaitAsync();
        try
        {
            if (!ReferenceEquals(session.Connection, connection)) return; // 已被新連線取代
            await CloseLockedAsync(session);
        }
        finally
        {
            loadLock.Release();
        }
    }

    /// <summary>
    /// 下線（呼叫者要持有該玩家的 LoadLock）：存檔成功才移出線上名單；
    /// 存檔失敗就留在線上、標記斷線，由自動存檔重試，不會因此遺失進度。
    /// </summary>
    private async Task CloseLockedAsync(PlayerSession session)
    {
        bool saved;
        await session.Gate.WaitAsync();
        try
        {
            if (session.Closed) return;
            saved = await SaveLockedAsync(session, force: true, CancellationToken.None);
            if (saved)
            {
                session.Closed = true;
                session.Disconnected = false;
                _online.TryRemove(new KeyValuePair<string, PlayerSession>(session.PlayerId, session));
            }
            else session.Disconnected = true;
        }
        finally
        {
            session.Gate.Release();
        }
        if (saved) _log.LogInformation("Player {Id} logged out. Online={Count}", session.PlayerId, _online.Count);
        else _log.LogWarning("Player {Id} disconnected but save failed; kept online for retry", session.PlayerId);
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
                if (session.Closed) continue;
                completed = CityLogic.Advance(session.Record.City, now, out trained, out healed);
                marchEvents = AdvanceMarches(session, now);
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
                await SendMarchArrivalsAsync(session, marchEvents);
                await SafeSend(session, MessageTypes.CityState, new CityStateMessage { ServerTimeMs = now, City = JsonSerializer.Deserialize<CityState>(snapshot, _json) });
                await ResolveAttacksAsync(session, marchEvents, ct);
            }
        }
    }

    /// <summary>處理 AdvanceMarches 收下的事件：推播一般抵達通知，並結算攻城。</summary>
    public async Task DispatchMarchEventsAsync(PlayerSession session, List<MarchEvent> events, CancellationToken ct)
    {
        await SendMarchArrivalsAsync(session, events);
        await ResolveAttacksAsync(session, events, ct);
    }

    private async Task SendMarchArrivalsAsync(PlayerSession session, List<MarchEvent> events)
    {
        foreach (var ev in events.Where(e => e.AttackTarget == null))
            await SafeSend(session, MessageTypes.MarchArrived, new MarchArrivedMessage
            {
                March = ev.MarchId, Hero = ev.Hero, Tile = ev.Tile, Disbanded = ev.Disbanded,
                Gathering = ev.Gathering, Occupied = ev.Occupied, GatherDone = ev.GatherDone, Carry = ev.Carry,
                LootItems = ev.LootItems, BattleWon = ev.BattleWon, BattleLost = ev.BattleLost, CampGone = ev.CampGone,
                Wounded = ev.Wounded, Dead = ev.Dead,
            });
    }

    private async Task ResolveAttacksAsync(PlayerSession attacker, List<MarchEvent> events, CancellationToken ct)
    {
        foreach (var ev in events.Where(e => e.AttackTarget != null))
            await ResolveAttackAsync(attacker.PlayerId, attacker.Record.DisplayName, ev, ct);
    }

    /// <summary>
    /// 攻城結算（兩段，不同時持有兩位玩家的鎖）：
    /// 1. 在守方身上開打、扣兵、掠奪（守方在線就鎖 Gate，離線就讀檔改完存回）；
    /// 2. 回到攻方身上（同樣在線或離線都可以），套用倖存部隊與掠奪品，行軍回城。
    /// 結算期間行軍維持「攻城中」，不能被召回；兩段都完成（或失敗）才解除登記。
    /// 每場仗有戰鬥 ID：守方的結果和扣兵一起存檔（攻城紀錄），兩段都立刻存檔，攻方存好後才刪紀錄。
    /// 任何一段之間停機，重開後重試會拿到同一個結果——守方不會被打兩次，攻方也不會白拿或白損。
    /// </summary>
    private async Task ResolveAttackAsync(string attackerId, string attackerName, MarchEvent ev, CancellationToken ct)
    {
        string battleId = $"{attackerId}:{ev.MarchId}";
        try
        {
            var now = _clock.NowMs;
            ulong seed = _world.NextSeed();
            PvpOutcome? outcome = null;
            bool found;
            try
            {
                found = await ModifyCityAsync(ev.AttackTarget!, def => outcome = PvpLogic.ResolveOnDefender(def, ev.AttackSquads, seed, now, battleId), ct,
                    saveNow: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 守方那邊沒存成功：行軍留在城下，之後的復原掃描會用同一個戰鬥 ID 重試
                _log.LogError(ex, "Siege {Battle} failed on defender {Def}; will retry", battleId, ev.AttackTarget);
                return;
            }
            if (!found || outcome == null)
            {
                outcome = new PvpOutcome { Shielded = true };
                foreach (var a in ev.AttackSquads) outcome.AttackerSurvivors[a.troop] = a.squad.Count;
            }
            string targetName = _world.NameOf(ev.AttackTarget!);

            if (!outcome.Shielded && _online.TryGetValue(ev.AttackTarget!, out var defender))
                await SafeSend(defender, MessageTypes.CityAttacked, new CityAttackedMessage
                {
                    AttackerName = attackerName, DefenderWon = !outcome.AttackerWon,
                    Wounded = outcome.DefenderWounded, Dead = outcome.DefenderDead, Plundered = outcome.Plunder,
                });

            MarchEvent? done = null;
            await ModifyCityAsync(attackerId, city => done = PvpLogic.FinishAttack(city, ev.MarchId, outcome, now), ct,
                beforePush: s => done == null ? Task.CompletedTask : SafeSend(s, MessageTypes.MarchArrived, new MarchArrivedMessage
                {
                    March = done.MarchId, Hero = done.Hero, Tile = done.Tile, BattleWon = done.BattleWon, BattleLost = done.BattleLost,
                    Shielded = done.Shielded, Carry = done.Carry, Wounded = done.Wounded, Dead = done.Dead, TargetName = targetName,
                }), saveNow: true);

            // 攻方已經存好：守方的這筆紀錄可以刪了（失敗也沒關係，30 天後會自動清掉）
            if (found && !outcome.Shielded)
            {
                try { await ModifyCityAsync(ev.AttackTarget!, def => PvpLogic.ForgetSiege(def, battleId), ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "Could not clear siege log {Battle}", battleId); }
            }
        }
        finally
        {
            _sieging.TryRemove((attackerId, ev.MarchId), out _);
        }
    }

    /// <summary>
    /// 替離線玩家結算到期的採集（採滿就放開資源田、部隊回城），不必等他重新登入。
    /// 只處理「佔著田、而且到了採滿時間」的離線玩家；離線期間抵達敵城的行軍也一併結算。
    /// </summary>
    public async Task SweepOfflineGatherersAsync(CancellationToken ct)
    {
        var now = _clock.NowMs;
        foreach (var owner in _world.OfflineOccupantsDue(now, id => _online.ContainsKey(id)))
        {
            List<MarchEvent> events = new();
            Dictionary<int, long>? still = null;
            bool found;
            try
            {
                found = await ModifyCityAsync(owner, city =>
                {
                    events = AdvanceMarches(city, owner, now);
                    still = city.Surface.Marches.Where(m => m.Gathering).ToDictionary(m => m.Destination, m => m.GatherEndMs);
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Offline gather settlement failed for {Id}", owner);
                continue; // 下次掃描再試
            }
            _world.AfterOfflineCheck(owner, found ? still : null);
            foreach (var ev in events.Where(e => e.AttackTarget != null))
                await ResolveAttackAsync(owner, _world.NameOf(owner), ev, ct);
        }
    }

    public async Task SaveAllAsync(bool force, CancellationToken ct)
    {
        foreach (var session in _online.Values)
        {
            if (session.Disconnected) await RetryCloseAsync(session, ct);
            else await SaveSessionAsync(session, force, ct);
        }
    }

    private async Task RetryCloseAsync(PlayerSession session, CancellationToken ct)
    {
        var loadLock = LoadLock(session.PlayerId);
        await loadLock.WaitAsync(ct);
        try
        {
            if (session.Disconnected) await CloseLockedAsync(session);
        }
        finally
        {
            loadLock.Release();
        }
    }

    private async Task SaveSessionAsync(PlayerSession session, bool force, CancellationToken ct)
    {
        await session.Gate.WaitAsync(ct);
        try
        {
            if (!session.Closed) await SaveLockedAsync(session, force, ct);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    /// <summary>存檔（呼叫者要持有 Gate）。成功回傳 true；失敗會記錄錯誤並保留 Dirty，下次再試。</summary>
    private async Task<bool> SaveLockedAsync(PlayerSession session, bool force, CancellationToken ct)
    {
        try
        {
            if (!force && !session.Dirty) return true;
            CityLogic.Advance(session.Record.City, _clock.NowMs);
            await _store.SaveAsync(session.Record, ct);
            session.Dirty = false;
            _world.Update(session.Record);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Save failed for {Id}", session.PlayerId);
            return false;
        }
    }

    /// <summary>
    /// 修改某位玩家的城市（GM 工具、攻城結算共用）。在線上就在鎖內改並推播最新狀態；離線就讀檔、修改、存檔。
    /// 和登入、下線共用 LoadLock，所以離線修改不會和登入讀檔交錯。回傳 false 表示找不到玩家。
    /// beforePush：線上時，在推播城市狀態之前先送的通知。
    /// saveNow：線上時也立刻存檔（攻城結算用；離線本來就會立刻存）。
    /// </summary>
    public async Task<bool> ModifyCityAsync(string playerId, Action<CityState> apply, CancellationToken ct,
        Func<PlayerSession, Task>? beforePush = null, bool saveNow = false)
    {
        var now = _clock.NowMs;
        if (_online.TryGetValue(playerId, out var session))
        {
            byte[]? snapshot = null;
            await session.Gate.WaitAsync(ct);
            try
            {
                if (!session.Closed)
                {
                    CityLogic.Advance(session.Record.City, now);
                    apply(session.Record.City);
                    session.Dirty = true;
                    if (saveNow) await SaveLockedAsync(session, force: true, ct);
                    snapshot = JsonSerializer.SerializeToUtf8Bytes(session.Record.City, _json);
                }
            }
            finally
            {
                session.Gate.Release();
            }
            if (snapshot != null)
            {
                if (beforePush != null) await beforePush(session);
                await SafeSend(session, MessageTypes.CityState, new CityStateMessage { ServerTimeMs = now, City = JsonSerializer.Deserialize<CityState>(snapshot, _json) });
                return true;
            }
            // 剛好下線並存完檔：改走離線的路，讀到的就是剛存好的檔
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
                return await ModifyCityAsync(playerId, apply, ct, beforePush, saveNow);
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
                        await SweepOfflineGatherersAsync(stoppingToken);
                        await RecoverStuckSiegesAsync(stoppingToken);
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
