using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Server.Persistence;

/// <summary>存進資料庫的一筆玩家資料。</summary>
public sealed class PlayerRecord
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public CityState City { get; set; } = new();
    public long CreatedAtMs { get; set; }
}

public interface IPlayerStore
{
    Task<PlayerRecord?> LoadAsync(string playerId, CancellationToken ct);
    Task SaveAsync(PlayerRecord record, CancellationToken ct);
    /// <summary>讀取所有玩家（伺服器啟動時建立地表世界狀態用）。</summary>
    Task<List<PlayerRecord>> ListAllAsync(CancellationToken ct);
}

/// <summary>本機開發用：存在記憶體，伺服器重啟就消失。存的是 JSON 副本，行為與資料庫一致。</summary>
public sealed class InMemoryPlayerStore : IPlayerStore
{
    private readonly ConcurrentDictionary<string, string> _rows = new();
    private readonly JsonSerializerOptions _json;

    public InMemoryPlayerStore(JsonSerializerOptions json) => _json = json;

    public Task<PlayerRecord?> LoadAsync(string playerId, CancellationToken ct)
    {
        return Task.FromResult(_rows.TryGetValue(playerId, out var s)
            ? JsonSerializer.Deserialize<PlayerRecord>(s, _json)
            : null);
    }

    public Task SaveAsync(PlayerRecord record, CancellationToken ct)
    {
        _rows[record.Id] = JsonSerializer.Serialize(record, _json);
        return Task.CompletedTask;
    }

    public Task<List<PlayerRecord>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult(_rows.Values.Select(s => JsonSerializer.Deserialize<PlayerRecord>(s, _json)!).ToList());
}

/// <summary>
/// 透過 Supabase 的 REST API（PostgREST）讀寫 players 資料表。
/// 只在伺服器使用 service_role key；資料表開啟 RLS 且不給任何 policy，客戶端無法直接存取。
/// 需要設定 Supabase:Url 與 Supabase:ServiceKey（請用環境變數，不要寫進檔案）。
/// 資料表結構見 db/001_players.sql。
/// </summary>
public sealed class SupabasePlayerStore : IPlayerStore
{
    private readonly IHttpClientFactory _http;
    private readonly string _baseUrl;
    private readonly string _key;
    private readonly JsonSerializerOptions _snake;

    public SupabasePlayerStore(IHttpClientFactory http, IConfiguration config, JsonSerializerOptions json)
    {
        _http = http;
        _baseUrl = (config["Supabase:Url"] ?? throw new InvalidOperationException("缺少 Supabase:Url")).TrimEnd('/');
        _key = config["Supabase:ServiceKey"] ?? throw new InvalidOperationException("缺少 Supabase:ServiceKey");
        // PostgREST 欄位名稱是 snake_case（display_name、created_at_ms）
        _snake = new JsonSerializerOptions(json) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    }

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public CityState City { get; set; } = new();
        public long CreatedAtMs { get; set; }
    }

    private HttpRequestMessage Request(HttpMethod method, string pathAndQuery)
    {
        var req = new HttpRequestMessage(method, _baseUrl + "/rest/v1/" + pathAndQuery);
        req.Headers.Add("apikey", _key);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        return req;
    }

    public async Task<PlayerRecord?> LoadAsync(string playerId, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, "players?select=*&id=eq." + Uri.EscapeDataString(playerId));
        using var res = await _http.CreateClient().SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = JsonSerializer.Deserialize<List<Row>>(await res.Content.ReadAsStringAsync(ct), _snake);
        var row = rows?.FirstOrDefault();
        if (row == null) return null;
        return new PlayerRecord { Id = row.Id, DisplayName = row.DisplayName, City = row.City, CreatedAtMs = row.CreatedAtMs };
    }

    public async Task<List<PlayerRecord>> ListAllAsync(CancellationToken ct)
    {
        // MVP：一次讀完（玩家數少）；之後玩家多時改成分頁或只讀需要的欄位
        using var req = Request(HttpMethod.Get, "players?select=*");
        using var res = await _http.CreateClient().SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var rows = JsonSerializer.Deserialize<List<Row>>(await res.Content.ReadAsStringAsync(ct), _snake) ?? new();
        return rows.Select(r => new PlayerRecord { Id = r.Id, DisplayName = r.DisplayName, City = r.City, CreatedAtMs = r.CreatedAtMs }).ToList();
    }

    public async Task SaveAsync(PlayerRecord record, CancellationToken ct)
    {
        var row = new Row { Id = record.Id, DisplayName = record.DisplayName, City = record.City, CreatedAtMs = record.CreatedAtMs };
        using var req = Request(HttpMethod.Post, "players?on_conflict=id");
        req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        var body = JsonSerializer.Serialize(row, _snake);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await _http.CreateClient().SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }
}
