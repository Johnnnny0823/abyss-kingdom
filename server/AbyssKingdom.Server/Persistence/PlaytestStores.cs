using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AbyssKingdom.Server.Persistence;

public sealed class PlaytestEvent
{
    public string Id { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Kind { get; set; } = "";
    public long AtMs { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
}

public interface IPlaytestStore
{
    Task AppendAsync(PlaytestEvent item, CancellationToken ct);
    Task<List<PlaytestEvent>> ListAsync(string? playerId, CancellationToken ct);
}

public sealed class InMemoryPlaytestStore : IPlaytestStore
{
    private readonly ConcurrentDictionary<string, string> _rows = new();
    private readonly JsonSerializerOptions _json;
    public InMemoryPlaytestStore(JsonSerializerOptions json) => _json = json;
    public Task AppendAsync(PlaytestEvent item, CancellationToken ct)
    {
        _rows.TryAdd(item.Id, JsonSerializer.Serialize(item, _json));
        return Task.CompletedTask;
    }
    public Task<List<PlaytestEvent>> ListAsync(string? playerId, CancellationToken ct) => Task.FromResult(
        _rows.Values.Select(s => JsonSerializer.Deserialize<PlaytestEvent>(s, _json)!)
            .Where(e => playerId == null || e.PlayerId == playerId).OrderBy(e => e.AtMs).ThenBy(e => e.Id, StringComparer.Ordinal).ToList());
}

public sealed class SupabasePlaytestStore : IPlaytestStore
{
    private readonly IHttpClientFactory _http;
    private readonly string _url, _key;
    private readonly JsonSerializerOptions _snake;
    public SupabasePlaytestStore(IHttpClientFactory http, IConfiguration config, JsonSerializerOptions json)
    {
        _http = http;
        _url = (config["Supabase:Url"] ?? throw new InvalidOperationException("Missing Supabase:Url")).TrimEnd('/');
        _key = config["Supabase:ServiceKey"] ?? throw new InvalidOperationException("Missing Supabase:ServiceKey");
        _snake = new(json) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    }
    private HttpRequestMessage Request(HttpMethod method, string query)
    {
        var request = new HttpRequestMessage(method, _url + "/rest/v1/playtest_events" + query);
        request.Headers.Add("apikey", _key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        return request;
    }
    public async Task AppendAsync(PlaytestEvent item, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "?on_conflict=id");
        request.Headers.Add("Prefer", "resolution=ignore-duplicates,return=minimal");
        request.Content = new StringContent(JsonSerializer.Serialize(item, _snake), Encoding.UTF8, "application/json");
        using var response = await _http.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task<List<PlaytestEvent>> ListAsync(string? playerId, CancellationToken ct)
    {
        var result = new List<PlaytestEvent>();
        for (int offset = 0; ; offset += 500)
        {
            using var request = Request(HttpMethod.Get, "?select=*&order=at_ms.asc,id.asc&limit=500&offset=" + offset +
                (playerId == null ? "" : "&player_id=eq." + Uri.EscapeDataString(playerId)));
            using var response = await _http.CreateClient().SendAsync(request, ct); response.EnsureSuccessStatusCode();
            var page = JsonSerializer.Deserialize<List<PlaytestEvent>>(await response.Content.ReadAsStringAsync(ct), _snake) ?? new();
            result.AddRange(page); if (page.Count < 500) return result;
        }
    }
}
