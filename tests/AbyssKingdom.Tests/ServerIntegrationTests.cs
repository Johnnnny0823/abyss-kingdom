using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AbyssKingdom.Server;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AbyssKingdom.Tests;

/// <summary>端到端測試：真的啟動伺服器、用 WebSocket 連線，模擬客戶端行為。</summary>
public static class ServerIntegrationTests
{
    const long T0 = 1_700_000_000_000;

    sealed class TestServer : IAsyncDisposable
    {
        public WebApplication App { get; }
        public ManualClock Clock { get; } = new(T0);
        public Uri WsUri { get; }

        public TestServer()
        {
            int port = FreePort();
            App = GameServer.Build(
                new[] { "--Logging:LogLevel:Default=Warning" },
                services => services.Replace(ServiceDescriptor.Singleton<IClock>(Clock)),
                $"http://127.0.0.1:{port}");
            WsUri = new Uri($"ws://127.0.0.1:{port}/ws");
        }

        public Task StartAsync() => App.StartAsync();
        public SessionManager Sessions => App.Services.GetRequiredService<SessionManager>();
        public async ValueTask DisposeAsync() { await App.StopAsync(); await App.DisposeAsync(); }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }
    }

    sealed class TestClient : IAsyncDisposable
    {
        readonly ClientWebSocket _ws = CreateSocket();

        static ClientWebSocket CreateSocket()
        {
            var ws = new ClientWebSocket();
            ws.Options.Proxy = null; // 本機連線不走代理
            return ws;
        }
        int _nextReq = 1;

        public async Task ConnectAsync(Uri uri) => await _ws.ConnectAsync(uri, CancellationToken.None);

        public async Task<int> SendAsync(string type, object? data)
        {
            int id = _nextReq++;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type, reqId = id, data }, Json.Options);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            return id;
        }

        /// <summary>讀訊息直到出現指定 type（略過其他推播）。</summary>
        public async Task<JsonElement> ExpectAsync(string type, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(buffer, cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) throw new AssertException($"連線被關閉，等待 {type}");
                    ms.Write(buffer, 0, r.Count);
                } while (!r.EndOfMessage);
                using var doc = JsonDocument.Parse(ms.ToArray());
                var root = doc.RootElement.Clone();
                if (root.GetProperty("type").GetString() == type) return root;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            _ws.Dispose();
        }
    }

    static CityState City(JsonElement msg) =>
        msg.GetProperty("data").GetProperty("city").Deserialize<CityState>(Json.Options)!;

    [Test]
    public static async Task Login_ReturnsPlayerAndCity()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);

        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:alice", DisplayName = "Alice" });
        var ok = await client.ExpectAsync(MessageTypes.LoginOk);
        Assert.Equal("dev-alice", ok.GetProperty("data").GetProperty("playerId").GetString());
        var city = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(1, city.GetLevel(BuildingType.Keep));
        Assert.Equal(1, server.Sessions.OnlineCount, "上線人數");
    }

    [Test]
    public static async Task Requests_BeforeLogin_AreRejected()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);

        await client.SendAsync(MessageTypes.CityGet, null);
        var err = await client.ExpectAsync(MessageTypes.Error);
        Assert.Equal(ErrorCodes.NotLoggedIn, err.GetProperty("data").GetProperty("code").GetString());
    }

    [Test]
    public static async Task Login_WithBadToken_Fails()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);

        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "not-a-token" });
        var err = await client.ExpectAsync(MessageTypes.Error);
        Assert.Equal(ErrorCodes.AuthFailed, err.GetProperty("data").GetProperty("code").GetString());
    }

    [Test]
    public static async Task Upgrade_CompletesAndPushes_ThenPersistsAcrossReconnect()
    {
        await using var server = new TestServer();
        await server.StartAsync();

        await using (var client = new TestClient())
        {
            await client.ConnectAsync(server.WsUri);
            await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:bob" });
            await client.ExpectAsync(MessageTypes.CityState);

            int req = await client.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = BuildingType.Keep });
            var afterStart = await client.ExpectAsync(MessageTypes.CityState);
            Assert.Equal(req, afterStart.GetProperty("reqId").GetInt32(), "回應帶回 reqId");
            Assert.NotNull(City(afterStart).Upgrade, "升級已排入");

            // 快轉時間並觸發一次 tick → 伺服器主動推播完成
            server.Clock.Advance(CityConfig.UpgradeDurationMs(BuildingType.Keep, 2) + 1);
            await server.Sessions.TickAsync(CancellationToken.None);
            var done = await client.ExpectAsync(MessageTypes.UpgradeCompleted);
            Assert.Equal("Keep", done.GetProperty("data").GetProperty("building").GetString());
            Assert.Equal(2, done.GetProperty("data").GetProperty("level").GetInt32());

            // 同時間第二次升級請求被拒（隊列忙碌）測試：先排一個，再排一個
            await client.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = BuildingType.Barracks });
            await client.ExpectAsync(MessageTypes.CityState);
            await client.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = BuildingType.Warehouse });
            var err = await client.ExpectAsync(MessageTypes.Error);
            Assert.Equal("QueueBusy", err.GetProperty("data").GetProperty("detail").GetString());
        }

        // 等伺服器處理完斷線（下線時存檔）
        for (int i = 0; i < 50 && server.Sessions.OnlineCount > 0; i++) await Task.Delay(50);
        Assert.Equal(0, server.Sessions.OnlineCount, "斷線後應下線");

        await using var again = new TestClient();
        await again.ConnectAsync(server.WsUri);
        await again.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:bob" });
        var city = City(await again.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(2, city.GetLevel(BuildingType.Keep), "重新登入後主堡仍是 2 級");
        Assert.Equal(BuildingType.Barracks, Assert.NotNull(city.Upgrade, "兵營升級仍在進行").Building);
    }

    [Test]
    public static async Task SecondLogin_KicksFirstConnection()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var first = new TestClient();
        await first.ConnectAsync(server.WsUri);
        await first.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:carol" });
        await first.ExpectAsync(MessageTypes.CityState);

        await using var second = new TestClient();
        await second.ConnectAsync(server.WsUri);
        await second.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:carol" });
        await second.ExpectAsync(MessageTypes.CityState);

        bool closed = false;
        try { await first.ExpectAsync("never", 3000); }
        catch (AssertException) { closed = true; }
        Assert.True(closed, "第一條連線應被關閉");
        Assert.Equal(1, server.Sessions.OnlineCount);
    }

    [Test]
    public static async Task Ping_ReturnsServerTime()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Ping, null);
        var pong = await client.ExpectAsync(MessageTypes.Pong);
        Assert.Equal(T0, pong.GetProperty("data").GetProperty("serverTimeMs").GetInt64());
    }
}
