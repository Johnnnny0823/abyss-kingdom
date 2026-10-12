using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Server.Persistence;

public sealed class AllianceMemberRecord
{
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public AllianceRole Role { get; set; }
    public long JoinedAtMs { get; set; }
    public long Epoch { get; set; }
}

public sealed class AllianceApplicationRecord
{
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AllianceId { get; set; } = "";
    public long ExpiresAtMs { get; set; }
}

public sealed class AllianceChatRecord
{
    public long Sequence { get; set; }
    public string OperationId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Text { get; set; } = "";
    public long SentAtMs { get; set; }
    public bool Hidden { get; set; }
}

public sealed class AllianceChatAuditRecord
{
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetId { get; set; } = "";
    public long AtMs { get; set; }
}

public sealed class AllianceChatReceipt
{
    public long Sequence { get; set; }
    public long SentAtMs { get; set; }
}

public sealed class AllianceHelpRequestRecord
{
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public QueueType Queue { get; set; }
    public long QueueId { get; set; }
    public long OwnerMembershipEpoch { get; set; }
    public long ExpiresAtMs { get; set; }
    public int MaxCount { get; set; }
    public int UsedCount { get; set; }
}

public sealed class AllianceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Announcement { get; set; } = "";
    public AllianceJoinMode JoinMode { get; set; }
    public bool PendingCreate { get; set; }
    public string CreateOperationId { get; set; } = "";
    public List<AllianceMemberRecord> Members { get; set; } = new();
    public List<AllianceApplicationRecord> Applications { get; set; } = new();
    public long NextChatSequence { get; set; } = 1;
    public List<AllianceChatRecord> Chat { get; set; } = new();
    public Dictionary<string, AllianceChatReceipt> ChatOperationReceipts { get; set; } = new();
    public Dictionary<string, List<long>> ChatRateWindows { get; set; } = new();
    public Dictionary<string, long> ChatMuteUntilMs { get; set; } = new();
    public List<AllianceChatAuditRecord> ChatAudit { get; set; } = new();
    public List<AllianceHelpRequestRecord> HelpRequests { get; set; } = new();
    public List<AllianceFlag> Flags { get; set; } = new();
}

public sealed class AllianceDirectory
{
    public long Version { get; set; }
    public List<AllianceRecord> Alliances { get; set; } = new();
    public Dictionary<string, long> RejoinAfterMs { get; set; } = new();
    public Dictionary<string, long> MembershipEpochs { get; set; } = new();
    public HashSet<string> DissolvedBy { get; set; } = new();
    public List<AllianceReinforcementRecord> Reinforcements { get; set; } = new();
    public Dictionary<int, long> CampRespawns { get; set; } = new();
    public List<CampSettlementRecord> CampSettlements { get; set; } = new();
    public List<AllianceRallyRecord> Rallies { get; set; } = new();
    public Dictionary<string, int> GuideProgress { get; set; } = new();
    public List<AllianceAdminAudit> AdminAudit { get; set; } = new();
}

/// <summary>One authoritative snapshot; the single game server serializes mutations with SocialGate.</summary>
public interface IAllianceStore
{
    Task<AllianceDirectory> LoadAsync(CancellationToken ct);
    Task SaveAsync(AllianceDirectory directory, CancellationToken ct);
}

public sealed class InMemoryAllianceStore : IAllianceStore
{
    private readonly JsonSerializerOptions _json;
    private string _data = "{}";
    public InMemoryAllianceStore(JsonSerializerOptions json) => _json = json;
    public Task<AllianceDirectory> LoadAsync(CancellationToken ct) =>
        Task.FromResult(JsonSerializer.Deserialize<AllianceDirectory>(_data, _json) ?? new());
    public Task SaveAsync(AllianceDirectory directory, CancellationToken ct)
    {
        _data = JsonSerializer.Serialize(directory, _json);
        return Task.CompletedTask;
    }
}

/// <summary>PostgREST storage for the one-server social snapshot. Schema: db/002_alliances.sql.</summary>
public sealed class SupabaseAllianceStore : IAllianceStore
{
    private readonly IHttpClientFactory _http;
    private readonly string _url;
    private readonly string _key;
    private readonly JsonSerializerOptions _json;

    public SupabaseAllianceStore(IHttpClientFactory http, IConfiguration config, JsonSerializerOptions json)
    {
        _http = http;
        _url = (config["Supabase:Url"] ?? throw new InvalidOperationException("缺少 Supabase:Url")).TrimEnd('/');
        _key = config["Supabase:ServiceKey"] ?? throw new InvalidOperationException("缺少 Supabase:ServiceKey");
        _json = json;
    }

    private HttpRequestMessage Request(HttpMethod method)
    {
        var req = new HttpRequestMessage(method, _url + "/rest/v1/alliance_directory?on_conflict=id");
        req.Headers.Add("apikey", _key);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        return req;
    }

    private sealed class Row
    {
        public int Id { get; set; } = 1;
        public AllianceDirectory State { get; set; } = new();
    }

    public async Task<AllianceDirectory> LoadAsync(CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get);
        req.RequestUri = new Uri(_url + "/rest/v1/alliance_directory?select=state&id=eq.1");
        using var res = await _http.CreateClient().SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<List<Row>>(await res.Content.ReadAsStringAsync(ct), _json)?.FirstOrDefault()?.State ?? new();
    }

    public async Task SaveAsync(AllianceDirectory directory, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Post);
        req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        req.Content = new StringContent(JsonSerializer.Serialize(new Row { State = directory }, _json), Encoding.UTF8, "application/json");
        using var res = await _http.CreateClient().SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }
}
