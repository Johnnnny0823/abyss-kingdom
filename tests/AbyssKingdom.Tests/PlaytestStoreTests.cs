using System.Net;
using System.Text;
using System.Text.Json;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using Microsoft.Extensions.Configuration;

namespace AbyssKingdom.Tests;

public static class PlaytestStoreTests
{
    sealed class Handler : HttpMessageHandler
    {
        public string? Posted;
        public int Pages;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Assert.Equal("fake-service-key", req.Headers.GetValues("apikey").Single());
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("fake-service-key", req.Headers.Authorization.Parameter);
            if (req.Method == HttpMethod.Post)
            {
                Assert.True(req.Headers.GetValues("Prefer").Single().Contains("ignore-duplicates"), "First milestone retry does not overwrite original time");
                Posted = await req.Content!.ReadAsStringAsync(ct);
                return new(HttpStatusCode.Created);
            }
            Assert.True(req.RequestUri!.Query.Contains("player_id=eq.dev-"), "Query filters player id");
            Pages++;
            string body = req.RequestUri.Query.Contains("offset=0")
                ? "[" + string.Join(',', Enumerable.Repeat(Posted!, 500)) + "]" : "[]";
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    sealed class Factory : IHttpClientFactory
    {
        readonly HttpMessageHandler _handler;
        public Factory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, false);
    }
    [Test]
    public static async Task Supabase_UsesPrivateHeadersSnakeColumnsIgnoreDuplicatesAndPagination()
    {
        var handler = new Handler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Supabase:Url"] = "https://example.invalid", ["Supabase:ServiceKey"] = "fake-service-key" }).Build();
        var store = new SupabasePlaytestStore(new Factory(handler), config, Json.Options);
        await store.AppendAsync(new PlaytestEvent { Id = "first-example", PlayerId = "dev-p", Kind = "first.training", AtMs = 123,
            Data = new() { ["keepLevel"] = 2 } }, default);
        var json = JsonDocument.Parse(handler.Posted!).RootElement;
        Assert.Equal("dev-p", json.GetProperty("player_id").GetString()); Assert.Equal(123L, json.GetProperty("at_ms").GetInt64());
        Assert.Equal(2, json.GetProperty("data").GetProperty("keepLevel").GetInt32());
        Assert.Equal(500, (await store.ListAsync("dev-p", default)).Count); Assert.Equal(2, handler.Pages);
    }
}
