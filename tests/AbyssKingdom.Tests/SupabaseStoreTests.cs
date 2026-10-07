using System.Net;
using System.Text;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using Microsoft.Extensions.Configuration;

namespace AbyssKingdom.Tests;

/// <summary>用假的 Supabase（攔截 HTTP）驗證：網址、金鑰標頭、欄位名稱、讀寫來回一致。</summary>
public static class SupabaseStoreTests
{
    sealed class FakeSupabase : HttpMessageHandler
    {
        public readonly Dictionary<string, string> Rows = new();
        public readonly List<HttpRequestMessage> Requests = new();
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Requests.Add(req);
            if (req.Method == HttpMethod.Post)
            {
                LastBody = await req.Content!.ReadAsStringAsync(ct);
                var id = System.Text.Json.JsonDocument.Parse(LastBody).RootElement.GetProperty("id").GetString()!;
                Rows[id] = LastBody;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            var query = req.RequestUri!.Query;
            var key = Uri.UnescapeDataString(query[(query.IndexOf("id=eq.", StringComparison.Ordinal) + 6)..]);
            var body = Rows.TryGetValue(key, out var row) ? "[" + row + "]" : "[]";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    sealed class Factory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public Factory(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name) => new(_h, disposeHandler: false);
    }

    static SupabasePlayerStore Create(FakeSupabase fake)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supabase:Url"] = "https://example.supabase.co/",
            ["Supabase:ServiceKey"] = "test-key",
        }).Build();
        return new SupabasePlayerStore(new Factory(fake), config, Json.Options);
    }

    [Test]
    public static async Task SaveThenLoad_RoundTrips()
    {
        var fake = new FakeSupabase();
        var store = Create(fake);
        var city = CityState.CreateNew(1000);
        CityLogic.StartUpgrade(city, BuildingType.Keep, 2000);
        await store.SaveAsync(new PlayerRecord { Id = "uid-1", DisplayName = "測試", City = city, CreatedAtMs = 1000 }, CancellationToken.None);

        Assert.True(fake.LastBody!.Contains("\"display_name\""), "欄位應為 snake_case：" + fake.LastBody);
        Assert.True(fake.LastBody.Contains("\"created_at_ms\""), "created_at_ms 欄位");

        var post = fake.Requests[0];
        Assert.Equal("https://example.supabase.co/rest/v1/players?on_conflict=id", post.RequestUri!.ToString());
        Assert.Equal("test-key", post.Headers.GetValues("apikey").Single());
        Assert.True(post.Headers.GetValues("Prefer").Single().Contains("merge-duplicates"), "upsert");

        var loaded = Assert.NotNull(await store.LoadAsync("uid-1", CancellationToken.None), "讀回的資料");
        Assert.Equal("測試", loaded.DisplayName);
        Assert.Equal(1000L, loaded.CreatedAtMs);
        Assert.Equal(city.GetResource(ResourceType.Wood), loaded.City.GetResource(ResourceType.Wood));
        Assert.Equal(BuildingType.Keep, Assert.NotNull(loaded.City.Upgrade, "升級工程").Building);
    }

    [Test]
    public static async Task Load_Missing_ReturnsNull()
    {
        var store = Create(new FakeSupabase());
        Assert.True(await store.LoadAsync("nobody", CancellationToken.None) == null, "不存在應回傳 null");
    }
}
