using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Game;

/// <summary>Best-effort bounded telemetry. No storage I/O on a gameplay path or inside a player Gate.</summary>
public sealed class PlaytestTelemetry : BackgroundService
{
    private sealed class ActiveSession
    {
        public string Id = "", PlayerId = "", Name = "";
        public long StartedAt;
        public Dictionary<string, long> Errors = new();
        public int ErrorCount;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, ActiveSession> _active = new();
    private readonly Dictionary<string, Queue<long>> _feedbackTimes = new();
    private readonly Dictionary<string, long> _historicSeconds = new(), _endedSeconds = new();
    private readonly HashSet<string> _first = new();
    private readonly Dictionary<string, PlaytestEvent> _pending = new();
    private readonly Channel<PlaytestEvent> _queue = Channel.CreateBounded<PlaytestEvent>(new BoundedChannelOptions(1024)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly IPlaytestStore _store;
    private readonly IClock _clock;
    private readonly ILogger<PlaytestTelemetry> _log;
    private long _sequence;
    public bool HistoricTotalsKnown { get; private set; }
    public PlaytestTelemetry(IPlaytestStore store, IClock clock, ILogger<PlaytestTelemetry> log)
        { _store = store; _clock = clock; _log = log; }

    public static string Clean(string? value, int max) => PlaytestReports.Sanitize(value ?? "", max);
    private static Dictionary<string, object?> Snapshot(string name, CityState? city) => new()
        { ["displayName"] = name, ["keepLevel"] = city?.GetLevel(BuildingType.Keep) ?? 0, ["questIndex"] = city?.QuestIndex ?? 0 };
    public bool Record(string playerId, string name, string kind, CityState? city = null,
        Dictionary<string, object?>? extra = null, long? atMs = null)
    {
        var data = Snapshot(name, city);
        data["sequence"] = Interlocked.Increment(ref _sequence);
        if (extra != null) foreach (var pair in extra) data[pair.Key] = pair.Value;
        bool first = kind.StartsWith("first.", StringComparison.Ordinal);
        string id = first ? "first-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(playerId + ":" + kind))) : Guid.NewGuid().ToString("N");
        var item = new PlaytestEvent { Id = id, PlayerId = playerId, Kind = kind, AtMs = atMs ?? _clock.NowMs, Data = data };
        lock (_gate)
        {
            if (first && !_first.Add(id)) return true;
            _pending[id] = item;
            if (_queue.Writer.TryWrite(item)) return true;
            _pending.Remove(id); if (first) _first.Remove(id);
        }
        _log.LogWarning("Playtest queue full; dropped {Kind}", kind); return false;
    }
    public void Start(string playerId, string name, string connectionId)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(playerId, out var previous)) EndLocked(previous);
            _active[playerId] = new ActiveSession { PlayerId = playerId, Name = name, Id = connectionId, StartedAt = _clock.NowMs };
            Record(playerId, name, "session.start", extra: new() { ["sessionId"] = connectionId, ["playSeconds"] = 0 });
        }
    }
    private void EndLocked(ActiveSession session)
    {
        _endedSeconds[session.PlayerId] = _endedSeconds.GetValueOrDefault(session.PlayerId) + Math.Max(0, (_clock.NowMs - session.StartedAt) / 1000);
        Record(session.PlayerId, session.Name, "session.end", extra: new()
            { ["sessionId"] = session.Id, ["playSeconds"] = Math.Max(0, (_clock.NowMs - session.StartedAt) / 1000) });
        _active.Remove(session.PlayerId);
    }
    public void End(string playerId, string connectionId)
    { lock (_gate) if (_active.TryGetValue(playerId, out var s) && s.Id == connectionId) EndLocked(s); }
    public long SessionSeconds(string playerId)
    { lock (_gate) return _active.TryGetValue(playerId, out var s) ? Math.Max(0, (_clock.NowMs - s.StartedAt) / 1000) : 0; }
    public string? ValidateFeedback(string playerId, string? text, string? screen)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000 || Clean(text, 1000).Length == 0 || (screen != "city" && screen != "world")) return "InvalidFeedback";
        lock (_gate)
        {
            if (!_active.TryGetValue(playerId, out var s)) return "NotLoggedIn";
            if (!_feedbackTimes.TryGetValue(playerId, out var times)) _feedbackTimes[playerId] = times = new();
            while (times.Count > 0 && times.Peek() <= _clock.NowMs - 60_000) times.Dequeue();
            if (times.Count >= 3) return "FeedbackRateLimited";
            times.Enqueue(_clock.NowMs); return null;
        }
    }
    public void ClientError(string playerId, string name, string? message, string? stack)
    {
        string clean = Clean(message, 1200);
        if (clean.Length == 0) return;
        lock (_gate)
        {
            if (!_active.TryGetValue(playerId, out var s) || s.ErrorCount >= 20 ||
                (s.Errors.TryGetValue(clean, out var last) && _clock.NowMs - last < 60_000)) return;
            s.Errors[clean] = _clock.NowMs; s.ErrorCount++;
        }
        Record(playerId, name, "client.error", extra: new() { ["message"] = clean,
            ["stack"] = Clean(string.Join('\n', (stack ?? "").Split('\n').Take(5)), 800) });
    }
    public void Observe(string playerId, string name, CityState city)
    {
        if (city.Training != null || city.Stats.TroopsTrained > 0) Record(playerId, name, "first.training", city);
        if (city.Surface.Marches.Count > 0) Record(playerId, name, "first.march", city);
        if (city.Stats.GatherTrips > 0 || city.Surface.Marches.Any(m => m.Gathering)) Record(playerId, name, "first.gather", city);
        if (city.Stats.CampsWon > 0 || city.Surface.Marches.Any(m => m.CampPending)) Record(playerId, name, "first.camp", city);
    }
    public async Task<List<PlaytestEvent>> EventsAsync(string? playerId, CancellationToken ct)
    {
        List<PlaytestEvent> pending;
        lock (_gate) pending = _pending.Values.Where(e => playerId == null || e.PlayerId == playerId).ToList();
        var stored = await _store.ListAsync(playerId, ct);
        return stored.Concat(pending)
            .GroupBy(e => e.Id).Select(g => g.First()).OrderBy(e => e.AtMs).ThenBy(e => Number(e, "sequence")).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    }
    public long TotalSeconds(string playerId)
    { lock (_gate) return _historicSeconds.GetValueOrDefault(playerId) + _endedSeconds.GetValueOrDefault(playerId) + SessionSeconds(playerId); }
    public static long Number(PlaytestEvent item, string key) => item.Data.TryGetValue(key, out var value)
        ? value is JsonElement j && j.TryGetInt64(out var n) ? n : long.TryParse(value?.ToString(), out var parsed) ? parsed : 0 : 0;
    public static string Text(PlaytestEvent item, string key) => item.Data.GetValueOrDefault(key)?.ToString() ?? "";
    public async Task<object[]> SummaryAsync(SessionManager sessions, CancellationToken ct)
    {
        var events = await EventsAsync(null, ct); var rows = new List<object>();
        foreach (var group in events.GroupBy(e => e.PlayerId))
        {
            var starts = group.Where(e => e.Kind == "session.start").ToList(); var last = group.Last();
            var city = await sessions.ReadCityAsync(group.Key, ct);
            bool online; lock (_gate) online = _active.ContainsKey(group.Key);
            rows.Add(new { playerId = group.Key, displayName = Text(group.Last(e => e.Data.ContainsKey("displayName")), "displayName"),
                firstLoginMs = starts.Count == 0 ? 0 : starts.Min(e => e.AtMs),
                lastOnlineMs = online ? _clock.NowMs : group.Where(e => e.Kind == "session.start" || e.Kind == "session.end").Select(e => e.AtMs).DefaultIfEmpty(0).Max(),
                totalPlaySeconds = group.Where(e => e.Kind == "session.end").Sum(e => Number(e, "playSeconds")) + SessionSeconds(group.Key),
                loginDays = starts.Select(e => e.AtMs / 86_400_000).Distinct().Count(),
                questIndex = city?.QuestIndex ?? Number(last, "questIndex"), keepLevel = city?.GetLevel(BuildingType.Keep) ?? Number(last, "keepLevel"),
                lastEvent = last.Kind, online });
        }
        return rows.ToArray();
    }
    public async Task<object[]> ErrorsAsync(CancellationToken ct) => (await EventsAsync(null, ct)).Where(e => e.Kind == "client.error")
        .GroupBy(e => Text(e, "message")).Select(g => (object)new { message = g.Key, count = g.Count(), players = g.Select(e => e.PlayerId).Distinct().Count(), lastAtMs = g.Max(e => e.AtMs) }).ToArray();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var history = await _store.ListAsync(null, timeout.Token);
            lock (_gate)
            {
                foreach (var group in history.Where(e => e.Kind == "session.end").GroupBy(e => e.PlayerId))
                    _historicSeconds[group.Key] = group.Sum(e => Number(e, "playSeconds"));
                foreach (var e in history.Where(e => e.Kind.StartsWith("first.", StringComparison.Ordinal))) _first.Add(e.Id);
                HistoricTotalsKnown = true;
            }
        }
        catch (Exception) { _log.LogWarning("Playtest history unavailable; game continues"); }
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await _store.AppendAsync(item, timeout.Token);
            }
            catch (Exception) { _log.LogWarning("Playtest event write failed: {Kind}", item.Kind); lock (_gate) _first.Remove(item.Id); }
            finally { lock (_gate) _pending.Remove(item.Id); }
        }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate) foreach (var session in _active.Values.ToArray()) EndLocked(session);
        _queue.Writer.TryComplete();
        if (ExecuteTask != null) { try { await ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); } catch (Exception) { } }
        await base.StopAsync(cancellationToken);
    }
}
