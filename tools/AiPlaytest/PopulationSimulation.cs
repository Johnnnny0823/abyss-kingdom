using System.Text.Json;
using AbyssKingdom.Server;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.Protocol;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AbyssKingdom.Playtesting;

public sealed record PopulationOptions(int Population, int Days, int Seed, DateTimeOffset Start, string Output)
{
    public static PopulationOptions Parse(string[] args)
    {
        string Value(string key, string fallback)
        {
            int i = Array.IndexOf(args, key);
            if (i >= 0) return i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : throw new ArgumentException($"Missing value: {key}");
            return args.FirstOrDefault(a => a.StartsWith(key + "="))?.Substring(key.Length + 1) ?? fallback;
        }
        int n = int.Parse(Value("--population", "300")), d = int.Parse(Value("--days", "7"));
        if (n < 1 || n > 2000 || d < 1 || d > 30) throw new ArgumentException("Population must be 1..2000; days must be 1..30.");
        var start = DateTimeOffset.Parse(Value("--start", "2026-10-01T00:00:00+08:00")).ToUniversalTime();
        string root = RepositoryRoot();
        return new(n, d, int.Parse(Value("--seed", "20261020")), start,
            Path.GetFullPath(Value("--output", Path.Combine(root, "analytics", "data"))));
    }
    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

public sealed record SimPlayer(int Number, string Persona, double ReturnProbability, int AbandonQuest,
    double AbandonProbability, int SessionMinutes, int StartDay, int StartMinute, int Seed);
public sealed record SimVisit(int Player, long StartMs, long EndMs);
public sealed record PopulationPlan(SimPlayer[] Players, SimVisit[] Visits, DateTimeOffset ObservationEndAt)
{
    public static PopulationPlan Create(PopulationOptions options)
    {
        var random = new Random(options.Seed);
        string[] personas = { "quest", "diligent", "random", "offline" };
        var players = new List<SimPlayer>(); var visits = new List<SimVisit>();
        var cutoff = options.Start.AddDays(7 + options.Days);
        for (int i = 0; i < options.Population; i++)
        {
            int role = i % 4;
            var p = new SimPlayer(i, personas[role], new[] { .68, .88, .42, .72 }[role] + random.NextDouble() * .1,
                random.Next(2, 16), new[] { .35, .12, .65, .25 }[role],
                random.Next(new[] { 8, 25, 2, 12 }[role], new[] { 35, 80, 25, 50 }[role]),
                random.Next(7), random.Next(1440), random.Next());
            players.Add(p);
            for (int day = 0; day <= options.Days; day++)
            {
                if (day > 0 && random.NextDouble() > p.ReturnProbability) continue;
                int count = role == 3 && day > 0 ? 2 : 1;
                for (int visit = 0; visit < count; visit++)
                {
                    int minute = day == 0 ? p.StartMinute : random.Next(1440);
                    var at = options.Start.AddDays(p.StartDay + day).AddMinutes(minute);
                    var end = at.AddMinutes(Math.Max(1, p.SessionMinutes + random.Next(-4, 5)));
                    if (end > cutoff) end = cutoff;
                    visits.Add(new(i, at.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds()));
                }
            }
        }
        // Keep one connection per player. Overlapping sampled visits are omitted, never double counted.
        var ordered = new List<SimVisit>();
        foreach (var group in visits.GroupBy(v => v.Player))
        {
            long lastEnd = 0;
            foreach (var v in group.OrderBy(v => v.StartMs))
                if (v.StartMs >= lastEnd) { ordered.Add(v); lastEnd = v.EndMs; }
        }
        return new(players.ToArray(), ordered.OrderBy(v => v.StartMs).ThenBy(v => v.Player).ToArray(), cutoff);
    }
}

public static class PopulationSimulation
{
    sealed class Visitor
    {
        public required SimPlayer Profile;
        public required SocketPlayer Socket;
        public required long EndMs;
        public int Turn;
    }

    public static async Task RunAsync(PopulationOptions options)
    {
        var plan = PopulationPlan.Create(options);
        Directory.CreateDirectory(options.Output);
        var clock = LocalClock.Create("Dev", "InMemory", options.Start.ToUnixTimeMilliseconds());
        var battleRandom = new Random(options.Seed ^ 0x5a5a5a5a);
        ulong Seed() { lock (battleRandom) { var bytes = new byte[8]; battleRandom.NextBytes(bytes); return BitConverter.ToUInt64(bytes); } }
        await using var app = GameServer.Build(new[] { "--Auth:Mode=Dev", "--Storage:Mode=InMemory", "--Admin:Token=", "--Logging:LogLevel:Default=Warning" }, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IClock>(clock));
            services.AddSingleton(new Random(options.Seed));
            services.AddSingleton<Func<ulong>>(Seed);
            // Only the tool replaces wall-clock tick scheduling. All player changes still enter via WebSocket.
            services.RemoveAll<IHostedService>();
            services.AddHostedService(s => s.GetRequiredService<PlaytestTelemetry>());
        }, "http://127.0.0.1:5090");
        var sessions = app.Services.GetRequiredService<SessionManager>();
        var store = app.Services.GetRequiredService<IPlaytestStore>();
        var telemetry = app.Services.GetRequiredService<PlaytestTelemetry>();
        var active = new Dictionary<int, Visitor>(); var churned = new HashSet<int>(); var checkedAbandon = new HashSet<int>();
        var decisions = plan.Players.ToDictionary(p => p.Number, p => new Random(p.Seed));
        int next = 0, requests = 0, rejected = 0, visits = 0;
        await app.StartAsync();
        try
        {
            while (next < plan.Visits.Length || active.Count > 0)
            {
                long nextAt = next < plan.Visits.Length ? plan.Visits[next].StartMs : long.MaxValue;
                long step = active.Count == 0 ? nextAt : Math.Min(clock.NowMs + 30_000, active.Values.Min(v => v.EndMs));
                long now = Math.Min(step, nextAt);
                clock.Advance(Math.Max(0, now - clock.NowMs));
                await sessions.TickAsync(CancellationToken.None);
                foreach (var id in active.Keys.OrderBy(i => i).ToArray())
                {
                    var v = active[id];
                    await v.Socket.SendAsync(MessageTypes.CityGet, null, MessageTypes.CityState);
                    if (!checkedAbandon.Contains(id) && v.Socket.City.QuestIndex >= v.Profile.AbandonQuest)
                    {
                        checkedAbandon.Add(id);
                        if (decisions[id].NextDouble() < v.Profile.AbandonProbability) churned.Add(id);
                    }
                    if (clock.NowMs >= v.EndMs || churned.Contains(id))
                    { await v.Socket.DisposeAsync(); active.Remove(id); continue; }
                    for (int action = 0; action < 6; action++)
                    {
                        var intent = AiPlaytestPolicy.Next(v.Socket.City, v.Profile.Persona == "offline" ? "diligent" : v.Profile.Persona,
                            v.Turn++, clock.NowMs);
                        if (intent == null) break;
                        var reply = await v.Socket.SendAsync(intent.Type, intent.Data); requests++;
                        await v.Socket.SendAsync(MessageTypes.CityGet, null, MessageTypes.CityState);
                        if (reply.GetProperty("type").GetString() == MessageTypes.Error) { rejected++; break; }
                    }
                }
                while (next < plan.Visits.Length && plan.Visits[next].StartMs <= clock.NowMs)
                {
                    var visit = plan.Visits[next++];
                    if (churned.Contains(visit.Player)) continue;
                    var p = plan.Players[visit.Player]; var socket = new SocketPlayer();
                    await socket.LoginAsync($"sim-{p.Number:D4}", $"SIM-{p.Persona}-{p.Number:D4}");
                    active.Add(p.Number, new Visitor { Profile = p, Socket = socket, EndMs = visit.EndMs }); visits++;
                }
                // Closing the socket handshake can precede LogoutAsync; don't advance simulated time until it finishes.
                for (int wait = 0; sessions.OnlineCount != active.Count && wait < 5000; wait++) await Task.Delay(1);
                if (sessions.OnlineCount != active.Count) throw new InvalidOperationException("Session close did not settle.");
            }
            for (int wait = 0; wait < 5000; wait++)
            {
                if ((await store.ListAsync(null, default)).Count == (await telemetry.EventsAsync(null, default)).Count) break;
                await Task.Delay(1);
            }
            var events = await store.ListAsync(null, default);
            if (events.Count != (await telemetry.EventsAsync(null, default)).Count) throw new InvalidOperationException("Telemetry did not flush.");
            var snake = new JsonSerializerOptions(Json.Options) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            await using (var writer = new StreamWriter(Path.Combine(options.Output, "raw_events.jsonl"), false))
                foreach (var e in events.OrderBy(e => e.AtMs).ThenBy(e => PlaytestTelemetry.Number(e, "sequence")))
                    await writer.WriteLineAsync(JsonSerializer.Serialize(e, snake));
            var metadata = new { isSimulated = true, options, plan.ObservationEndAt, players = plan.Players,
                plannedVisits = plan.Visits, actualSessions = visits, permanentDropouts = churned.OrderBy(i => i),
                requests, rejectedRequests = rejected, eventCount = events.Count,
                reproduction = "Same seed/runtime reproduces behavior and aggregates; server-generated event/session GUIDs differ." };
            await File.WriteAllTextAsync(Path.Combine(options.Output, "simulation.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions(Json.Options) { WriteIndented = true }));
            Console.WriteLine($"SIMULATED DATA: players={options.Population}, sessions={visits}, events={events.Count}, intents={requests}, rejected={rejected}");
        }
        finally { foreach (var v in active.Values) await v.Socket.DisposeAsync(); await app.StopAsync(); }
    }
}
