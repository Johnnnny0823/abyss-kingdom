using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AbyssKingdom.Server;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Persistence;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Protocol;
using AbyssKingdom.Shared.Social;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AbyssKingdom.Tests;

/// <summary>端到端測試：真的啟動伺服器、用 WebSocket 連線，模擬客戶端行為。</summary>
public static class ServerIntegrationTests
{
    const long T0 = 1_700_000_000_000;

    [Test]
    public static async Task KeepUpgrade_MissingPrerequisiteRejectsClientIntentWithoutDebit()
    {
        await using var server = new TestServer(); await server.StartAsync();
        await using var client = new TestClient(); await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:keep-prereq-test" });
        await client.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-keep-prereq-test", c =>
        {
            c.Buildings[BuildingType.Keep] = 2; c.Buildings[BuildingType.Barracks] = 1;
            c.Buildings[BuildingType.Warehouse] = 0;
        }, CancellationToken.None, saveNow: true);
        var before = await FreshCity(client);
        int req = await client.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = BuildingType.Keep });
        var error = await client.ExpectAsync(MessageTypes.Error);
        Assert.Equal(req, error.GetProperty("reqId").GetInt32());
        Assert.Equal("KeepPrerequisiteMissing", error.GetProperty("data").GetProperty("detail").GetString());
        var after = await FreshCity(client);
        Assert.Equal(2, after.GetLevel(BuildingType.Keep)); Assert.True(after.Upgrade == null, "No rejected upgrade queue");
        foreach (var r in CityConfig.AllResources) Assert.Equal(before.GetResource(r), after.GetResource(r));
    }

    sealed class TestServer : IAsyncDisposable
    {
        public WebApplication App { get; }
        public ManualClock Clock { get; } = new(T0);
        public Uri WsUri { get; }
        public Uri HttpUri { get; }

        public TestServer(params string[] extraArgs) : this(null, extraArgs) { }

        public TestServer(Action<IServiceCollection>? extraServices, params string[] extraArgs)
        {
            int port = FreePort();
            // 固定測試設定，不繼承開發者電腦的環境變數（命令列優先於環境變數；extraArgs 在後面，可以再覆寫）
            App = GameServer.Build(
                new[] { "--Logging:LogLevel:Default=Warning", "--Auth:Mode=Dev", "--Storage:Mode=Memory", "--Admin:Token=" }
                    .Concat(extraArgs).ToArray(),
                services =>
                {
                    services.Replace(ServiceDescriptor.Singleton<IClock>(Clock));
                    extraServices?.Invoke(services);
                },
                $"http://127.0.0.1:{port}");
            WsUri = new Uri($"ws://127.0.0.1:{port}/ws");
            HttpUri = new Uri($"http://127.0.0.1:{port}");
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

    /// <summary>要一份最新的城市狀態（略過之前排在佇列裡的推播）。</summary>
    static async Task<CityState> FreshCity(TestClient c)
    {
        int req = await c.SendAsync(MessageTypes.CityGet, null);
        while (true)
        {
            var msg = await c.ExpectAsync(MessageTypes.CityState);
            if (msg.GetProperty("reqId").GetInt32() == req) return City(msg);
        }
    }

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
    public static async Task Train_RejectedWithoutBarracks_ThenCompletesAndPushes()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:trainer" });
        await client.ExpectAsync(MessageTypes.CityState);

        await client.SendAsync(MessageTypes.CityTrain, new TrainRequest { Troop = TroopType.Shield, Count = 5 });
        var err = await client.ExpectAsync(MessageTypes.Error);
        Assert.Equal("BarracksRequired", err.GetProperty("data").GetProperty("detail").GetString());

        // 蓋兵營 → 快轉完成
        await client.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = BuildingType.Barracks });
        await client.ExpectAsync(MessageTypes.CityState);
        server.Clock.Advance(CityConfig.UpgradeDurationMs(BuildingType.Barracks, 1) + 1);
        await server.Sessions.TickAsync(CancellationToken.None);
        await client.ExpectAsync(MessageTypes.UpgradeCompleted);
        await client.ExpectAsync(MessageTypes.CityState);

        int req = await client.SendAsync(MessageTypes.CityTrain, new TrainRequest { Troop = TroopType.Shield, Count = 5 });
        var started = await client.ExpectAsync(MessageTypes.CityState);
        Assert.Equal(req, started.GetProperty("reqId").GetInt32(), "回應帶回 reqId");
        Assert.NotNull(City(started).Training, "訓練已排入");

        server.Clock.Advance(5 * CityConfig.TroopTrainMsPerUnit(TroopType.Shield));
        await server.Sessions.TickAsync(CancellationToken.None);
        var done = await client.ExpectAsync(MessageTypes.TrainingCompleted);
        Assert.Equal("Shield", done.GetProperty("data").GetProperty("troop").GetString());
        Assert.Equal(5, done.GetProperty("data").GetProperty("count").GetInt32());
        Assert.Equal(5L, City(await client.ExpectAsync(MessageTypes.CityState)).GetTroops(TroopType.Shield), "推播的城市含新部隊");
    }

    [Test]
    public static async Task MoveBuilding_SavesLayout_AndRejectsOverlap()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:mason" });
        var city = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(CityConfig.AllBuildings.Length, city.Layout.Count, "登入就有完整擺放");

        await client.SendAsync(MessageTypes.CityMove, new MoveBuildingRequest { Building = BuildingType.Farm, X = 0, Y = 21 });
        var moved = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(21, moved.Layout[BuildingType.Farm].Y);

        var keep = moved.Layout[BuildingType.Keep];
        await client.SendAsync(MessageTypes.CityMove, new MoveBuildingRequest { Building = BuildingType.Farm, X = keep.X, Y = keep.Y });
        var err = await client.ExpectAsync(MessageTypes.Error);
        Assert.Equal(ErrorCodes.MoveRejected, err.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal("Overlaps", err.GetProperty("data").GetProperty("detail").GetString());
    }

    [Test]
    public static async Task Speedup_CompletesImmediately_AndCancelRefunds()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:speedy" });
        Assert.Equal(10L, City(await client.ExpectAsync(MessageTypes.CityState)).GetItemCount(ItemType.SpeedupGeneral1m), "新手道具");

        // 兵營 1 級工期 30 秒 < 5 分鐘 → 免費加速，不需要等 tick 就推播完成
        await client.SendAsync(MessageTypes.CityUpgrade, new UpgradeRequest { Building = BuildingType.Barracks });
        await client.ExpectAsync(MessageTypes.CityState);
        int req = await client.SendAsync(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = QueueType.Building });
        var done = await client.ExpectAsync(MessageTypes.UpgradeCompleted);
        Assert.Equal("Barracks", done.GetProperty("data").GetProperty("building").GetString());
        var city = await client.ExpectAsync(MessageTypes.CityState);
        Assert.Equal(req, city.GetProperty("reqId").GetInt32(), "回應帶回 reqId");
        Assert.Equal(1, City(city).GetLevel(BuildingType.Barracks));

        // 用道具加速訓練
        await client.SendAsync(MessageTypes.CityTrain, new TrainRequest { Troop = TroopType.Crossbow, Count = 20 });
        await client.ExpectAsync(MessageTypes.CityState);
        await client.SendAsync(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = QueueType.Training, Item = ItemType.SpeedupTraining30m });
        await client.ExpectAsync(MessageTypes.TrainingCompleted);
        var afterItem = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(20L, afterItem.GetTroops(TroopType.Crossbow));
        Assert.Equal(2L, afterItem.GetItemCount(ItemType.SpeedupTraining30m), "道具 3 → 2");

        // 取消訓練退一半
        long foodBefore = afterItem.GetResource(ResourceType.Food);
        await client.SendAsync(MessageTypes.CityTrain, new TrainRequest { Troop = TroopType.Crossbow, Count = 10 });
        await client.ExpectAsync(MessageTypes.CityState);
        await client.SendAsync(MessageTypes.CityCancel, new CancelRequest { Queue = QueueType.Training });
        var afterCancel = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.True(afterCancel.Training == null, "訓練已取消");
        Assert.Equal(foodBefore - 200 + 100, afterCancel.GetResource(ResourceType.Food), "糧 20×10 扣掉，退一半");

        await client.SendAsync(MessageTypes.CityCancel, new CancelRequest { Queue = QueueType.Training });
        var err = await client.ExpectAsync(MessageTypes.Error);
        Assert.Equal("NothingToCancel", err.GetProperty("data").GetProperty("detail").GetString());
    }

    [Test]
    public static async Task AdminGrant_RequiresToken_AndPushesToOnlinePlayer()
    {
        const string token = "test-admin-token-0123456789abcdef";
        await using var server = new TestServer($"--Admin:Token={token}");
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:gm" });
        await client.ExpectAsync(MessageTypes.CityState);

        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = server.HttpUri };
        var body = new StringContent("""{"resources":{"Wood":5000},"items":{"SpeedupGeneral60m":3},"troops":{"Rider":100}}""", Encoding.UTF8, "application/json");

        var noToken = await http.PostAsync("/admin/grant/dev-gm", body);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode, "沒帶 token 要被拒");

        http.DefaultRequestHeaders.Add("X-Admin-Token", token);
        var ok = await http.PostAsync("/admin/grant/dev-gm", body);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var city = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(CityConfig.StartingResource + 5000, city.GetResource(ResourceType.Wood));
        Assert.Equal(4L, city.GetItemCount(ItemType.SpeedupGeneral60m), "新手 1 + 3");
        Assert.Equal(100L, city.GetTroops(TroopType.Rider));

        var missing = await http.PostAsync("/admin/grant/nobody", body);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Test]
    public static async Task AdminEndpoint_DisabledWithoutToken()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = server.HttpUri };
        var res = await http.PostAsync("/admin/grant/x", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode, "未設定 token 時端點不存在");
    }

    [Test]
    public static async Task Expedition_FirstStage_WinsAndAdvances()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:explorer" });
        await client.ExpectAsync(MessageTypes.CityState);

        await client.SendAsync(MessageTypes.ExpeditionBattle, new ExpeditionBattleRequest { Stage = 2, Teams = new() { new() { HeroId.Hold } } });
        var locked = await client.ExpectAsync(MessageTypes.Error);
        Assert.Equal("StageLocked", locked.GetProperty("data").GetProperty("detail").GetString());

        int req = await client.SendAsync(MessageTypes.ExpeditionBattle, new ExpeditionBattleRequest
        {
            Stage = 1,
            Teams = new() { new() { HeroId.Hold, HeroId.Vesper, HeroId.Karst } },
        });
        var result = await client.ExpectAsync(MessageTypes.ExpeditionResultMessage);
        Assert.Equal(req, result.GetProperty("reqId").GetInt32());
        var data = result.GetProperty("data");
        Assert.True(data.GetProperty("win").GetBoolean(), "第 1 關應該贏");
        Assert.True(data.GetProperty("reports")[0].GetProperty("actions").GetArrayLength() > 0, "有戰報");
        var city = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(1, city.Expedition.Cleared);
    }

    [Test]
    public static async Task March_ArrivesAndPushes_ThenRecallReturnsTroops()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:rider" });
        var city = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.True(city.Surface.Placed, "登入時分配主城位置");

        // 測試用：直接在伺服器端給部隊
        await server.Sessions.ModifyCityAsync("dev-rider", c => c.Troops[TroopType.Rider] = 100, CancellationToken.None);
        await client.ExpectAsync(MessageTypes.CityState);

        // 目標：主城旁邊 6 格內第一個能站的格子
        var (hx, hy) = SurfaceLogic.CityCenter(city.Surface);
        var dest = Enumerable.Range(-6, 13).SelectMany(dx => Enumerable.Range(-6, 13).Select(dy => (x: hx + dx, y: hy + dy)))
            .First(p => Math.Abs(p.x - hx) + Math.Abs(p.y - hy) >= 5 && SurfaceLogic.Walkable(p.x, p.y));
        await client.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Karst, Troops = new() { [TroopType.Rider] = 60 }, X = dest.x, Y = dest.y });
        var started = City(await client.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(40L, started.GetTroops(TroopType.Rider), "部隊出征");
        var march = started.Surface.Marches[0];

        server.Clock.Advance(march.ArriveAt - T0 + 1);
        await server.Sessions.TickAsync(CancellationToken.None);
        var arrived = await client.ExpectAsync(MessageTypes.MarchArrived);
        Assert.Equal(SurfaceLogic.Tile(dest.x, dest.y), arrived.GetProperty("data").GetProperty("tile").GetInt32());
        Assert.True(City(await client.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0].Stationed, "駐紮");

        await client.SendAsync(MessageTypes.MapRecall, new MapRecallRequest { March = march.Id });
        var recalled = City(await client.ExpectAsync(MessageTypes.CityState));
        server.Clock.Advance(recalled.Surface.Marches[0].ArriveAt - march.ArriveAt);
        await server.Sessions.TickAsync(CancellationToken.None);
        var back = await client.ExpectAsync(MessageTypes.MarchArrived);
        Assert.True(back.GetProperty("data").GetProperty("disbanded").GetBoolean(), "召回解散");
        Assert.Equal(100L, City(await client.ExpectAsync(MessageTypes.CityState)).GetTroops(TroopType.Rider), "部隊歸營");
    }

    [Test]
    public static async Task Surface_EachPlayerGetsAUniqueCitySite_AndSeesOthers()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var a = new TestClient();
        await a.ConnectAsync(server.WsUri);
        await a.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:north", DisplayName = "北境" });
        var sa = City(await a.ExpectAsync(MessageTypes.CityState)).Surface;
        await using var b = new TestClient();
        await b.ConnectAsync(server.WsUri);
        await b.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:south", DisplayName = "南境" });
        var sb = City(await b.ExpectAsync(MessageTypes.CityState)).Surface;

        Assert.True(sa.Placed && sb.Placed, "都分到主城位置");
        Assert.True(sa.X != sb.X || sa.Y != sb.Y, "位置不重複");
        Assert.Equal(SurfaceZone.Outer, SurfaceLogic.Zone(sa.X, sa.Y), "新手在外環");

        await a.SendAsync(MessageTypes.WorldCities, null);
        var pins = (await a.ExpectAsync(MessageTypes.WorldCities)).GetProperty("data").GetProperty("cities");
        Assert.Equal(2, pins.GetArrayLength(), "看得到兩座城");
        var mine = pins.EnumerateArray().Single(p => p.GetProperty("mine").GetBoolean());
        Assert.Equal(sa.X, mine.GetProperty("x").GetInt32());
        Assert.Equal(sa.Y, mine.GetProperty("y").GetInt32());
        Assert.Equal(1, mine.GetProperty("keepLevel").GetInt32(), "帶主堡等級（大地圖依等級顯示主城外觀）");
        Assert.True(pins.EnumerateArray().Any(p => p.GetProperty("name").GetString() == "南境"), "看得到鄰居的名字");
    }

    [Test]
    public static async Task Gather_OneMarchPerField_FullReturnsWithResources()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        var field = SurfaceLogic.Features().First(f => f.Type == FeatureType.ResourceField && f.Level == 1
            && SurfaceLogic.Sites().Any(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, f.X, f.Y) < 225));
        var sites = SurfaceLogic.Sites().Where(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, field.X, field.Y) < 900).Take(2).ToList();

        async Task<TestClient> Join(string name, int i)
        {
            var c = new TestClient();
            await c.ConnectAsync(server.WsUri);
            await c.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:" + name, DisplayName = name });
            await c.ExpectAsync(MessageTypes.CityState);
            await server.Sessions.ModifyCityAsync("dev-" + name, city =>
            {
                city.Surface.X = sites[i].x;
                city.Surface.Y = sites[i].y;
                city.Troops[TroopType.Shield] = 100;
            }, CancellationToken.None);
            await c.ExpectAsync(MessageTypes.CityState);
            return c;
        }
        await using var a = await Join("gatherer", 0);
        await using var b = await Join("latecomer", sites.Count > 1 ? 1 : 0);

        await a.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 50 }, X = field.X, Y = field.Y });
        var ma = City(await a.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
        server.Clock.Advance(ma.ArriveAt - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);
        var arrived = await a.ExpectAsync(MessageTypes.MarchArrived);
        Assert.True(arrived.GetProperty("data").GetProperty("gathering").GetBoolean(), "開始採集");
        var arrivedCity = City(await a.ExpectAsync(MessageTypes.CityState));
        var gathering = arrivedCity.Surface.Marches[0];

        await b.SendAsync(MessageTypes.WorldTile, new TileInfoRequest { X = field.X, Y = field.Y });
        var info = (await b.ExpectAsync(MessageTypes.WorldTile)).GetProperty("data");
        Assert.Equal("gatherer", info.GetProperty("occupant").GetString(), "看得到誰在採");
        Assert.Equal(GatherConfig.FieldStock(1), info.GetProperty("stock").GetInt64());

        await b.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 10 }, X = field.X, Y = field.Y });
        var mb = City(await b.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
        if (mb.ArriveAt > server.Clock.NowMs) server.Clock.Advance(mb.ArriveAt - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);
        Assert.True((await b.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data").GetProperty("occupied").GetBoolean(), "同一塊田只能一支部隊採");

        long before = arrivedCity.GetResource(field.Resource);
        server.Clock.Advance(gathering.GatherEndMs - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);
        Assert.True((await a.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data").GetProperty("gatherDone").GetBoolean(), "採滿回城");
        var returning = City(await a.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
        server.Clock.Advance(returning.ArriveAt - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);
        Assert.True((await a.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data").GetProperty("disbanded").GetBoolean(), "回城解散");
        var home = City(await a.ExpectAsync(MessageTypes.CityState));
        Assert.True(home.GetResource(field.Resource) >= before + 600, "帶回 50 × 12 的資源");

        await b.SendAsync(MessageTypes.WorldTile, new TileInfoRequest { X = field.X, Y = field.Y });
        var after = (await b.ExpectAsync(MessageTypes.WorldTile)).GetProperty("data");
        Assert.Equal(GatherConfig.FieldStock(1) - 600, after.GetProperty("stock").GetInt64(), "資源田存量減少");
    }

    [Test]
    public static async Task Gather_OfflineGathererFinishes_FieldFreedWithoutRelogin()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        var field = SurfaceLogic.Features().First(f => f.Type == FeatureType.ResourceField && f.Level == 1
            && SurfaceLogic.Sites().Any(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, f.X, f.Y) < 225));
        var site = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, field.X, field.Y) < 900);

        March gathering;
        await using (var a = new TestClient())
        {
            await a.ConnectAsync(server.WsUri);
            await a.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:afk", DisplayName = "掛機者" });
            await a.ExpectAsync(MessageTypes.CityState);
            await server.Sessions.ModifyCityAsync("dev-afk", city =>
            {
                city.Surface.X = site.x;
                city.Surface.Y = site.y;
                city.Troops[TroopType.Shield] = 100;
            }, CancellationToken.None);
            await a.ExpectAsync(MessageTypes.CityState);
            await a.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 50 }, X = field.X, Y = field.Y });
            var m = City(await a.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
            server.Clock.Advance(m.ArriveAt - server.Clock.NowMs);
            await server.Sessions.TickAsync(CancellationToken.None);
            Assert.True((await a.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data").GetProperty("gathering").GetBoolean(), "開始採集");
            gathering = City(await a.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
        }
        for (int i = 0; i < 50 && server.Sessions.OnlineCount > 0; i++) await Task.Delay(50);
        Assert.Equal(0, server.Sessions.OnlineCount, "採集者下線");

        // 還沒採滿：掃描不會動它
        await server.Sessions.SweepOfflineGatherersAsync(CancellationToken.None);
        await using var b = new TestClient();
        await b.ConnectAsync(server.WsUri);
        await b.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:watcher" });
        await b.ExpectAsync(MessageTypes.CityState);
        await b.SendAsync(MessageTypes.WorldTile, new TileInfoRequest { X = field.X, Y = field.Y });
        Assert.Equal("掛機者", (await b.ExpectAsync(MessageTypes.WorldTile)).GetProperty("data").GetProperty("occupant").GetString(), "採集中仍佔著");

        // 採滿的時間過了：不用等他上線，田就放開、存量扣掉
        server.Clock.Advance(gathering.GatherEndMs - server.Clock.NowMs + 1);
        await server.Sessions.SweepOfflineGatherersAsync(CancellationToken.None);
        await b.SendAsync(MessageTypes.WorldTile, new TileInfoRequest { X = field.X, Y = field.Y });
        var info = (await b.ExpectAsync(MessageTypes.WorldTile)).GetProperty("data");
        Assert.True(info.TryGetProperty("occupant", out var occ) == false || occ.ValueKind == JsonValueKind.Null, "田已經放開");
        Assert.Equal(GatherConfig.FieldStock(1) - 600, info.GetProperty("stock").GetInt64(), "存量扣掉採走的量");

        // 存檔裡部隊已經在回城路上，採到的資源帶著
        await using var again = new TestClient();
        await again.ConnectAsync(server.WsUri);
        await again.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:afk" });
        var back = City(await again.ExpectAsync(MessageTypes.CityState)).Surface.Marches.Single();
        Assert.True(back.Returning && back.Carry.Values.Sum() == 600, "回城中、帶著 600");
    }

    [Test]
    public static async Task Quest_ClaimRejectedUntilDone_ThenRewards()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var c = new TestClient();
        await c.ConnectAsync(server.WsUri);
        await c.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:quester" });
        var start = City(await c.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(0, start.QuestIndex, "新帳號從第一個任務開始");

        await c.SendAsync(MessageTypes.QuestClaim, null);
        var err = (await c.ExpectAsync(MessageTypes.Error)).GetProperty("data");
        Assert.Equal(ErrorCodes.QuestRejected, err.GetProperty("code").GetString());
        Assert.Equal("NotDone", err.GetProperty("detail").GetString(), "主堡還沒升，不能領");

        await server.Sessions.ModifyCityAsync("dev-quester", city => city.Buildings[BuildingType.Keep] = 2, CancellationToken.None);
        await c.ExpectAsync(MessageTypes.CityState);
        await c.SendAsync(MessageTypes.QuestClaim, null);
        var after = (await FreshCity(c));
        Assert.Equal(1, after.QuestIndex, "換下一個任務");
        Assert.True(after.GetResource(ResourceType.Wood) > start.GetResource(ResourceType.Wood), "拿到資源獎勵");
    }

    [Test]
    public static async Task Camp_Defeated_ListedUntilRespawn()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        var camp = SurfaceLogic.Features().First(f => f.Type == FeatureType.MonsterCamp && f.Level == 1
            && SurfaceLogic.Sites().Any(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, f.X, f.Y) < 400));
        var site = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, camp.X, camp.Y) < 400);

        await using var c = new TestClient();
        await c.ConnectAsync(server.WsUri);
        await c.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:slayer" });
        await c.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-slayer", city =>
        {
            city.Surface.X = site.x;
            city.Surface.Y = site.y;
            city.Troops[TroopType.Shield] = 250;
        }, CancellationToken.None);
        await c.ExpectAsync(MessageTypes.CityState);

        await c.SendAsync(MessageTypes.WorldCamps, null);
        Assert.Equal(0, (await c.ExpectAsync(MessageTypes.WorldCamps)).GetProperty("data").GetProperty("camps").GetArrayLength(), "一開始沒有被打掉的營地");

        // 滿編去打 Lv.1 營地：打贏，營地消失
        await c.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 250 }, X = camp.X, Y = camp.Y });
        var m = City(await c.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
        server.Clock.Advance(m.ArriveAt - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);
        Assert.True((await c.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data").GetProperty("battleWon").GetBoolean(), "打贏");

        await c.SendAsync(MessageTypes.WorldCamps, null);
        var camps = (await c.ExpectAsync(MessageTypes.WorldCamps)).GetProperty("data").GetProperty("camps");
        var gone = camps.EnumerateArray().Single();
        Assert.Equal(camp.X, gone.GetProperty("x").GetInt32(), "被打掉的營地");
        Assert.Equal(camp.Y, gone.GetProperty("y").GetInt32());
        Assert.Equal(m.ArriveAt + CampConfig.RespawnMs, gone.GetProperty("backAt").GetInt64(), "帶重生時間");

        // 重生時間到了就不再列出
        server.Clock.Advance(CampConfig.RespawnMs + 1);
        await c.SendAsync(MessageTypes.WorldCamps, null);
        Assert.Equal(0, (await c.ExpectAsync(MessageTypes.WorldCamps)).GetProperty("data").GetProperty("camps").GetArrayLength(), "重生了");
    }

    [Test]
    public static async Task Camp_LostFight_WoundedThenHealed()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        var camp = SurfaceLogic.Features().First(f => f.Type == FeatureType.MonsterCamp && f.Level == 1
            && SurfaceLogic.Sites().Any(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, f.X, f.Y) < 400));
        var site = SurfaceLogic.Sites().First(s => s.z == SurfaceZone.Outer && SurfaceLogic.Dist2(s.x, s.y, camp.X, camp.Y) < 400);

        await using var c = new TestClient();
        await c.ConnectAsync(server.WsUri);
        await c.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:hunter" });
        await c.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-hunter", city =>
        {
            city.Surface.X = site.x;
            city.Surface.Y = site.y;
            city.Troops[TroopType.Shield] = 100;
        }, CancellationToken.None);
        await c.ExpectAsync(MessageTypes.CityState);

        // 只帶 30 兵去打：一定打輸，有傷兵
        await c.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 30 }, X = camp.X, Y = camp.Y });
        var m = City(await c.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
        server.Clock.Advance(m.ArriveAt - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);
        var fight = (await c.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data");
        Assert.True(fight.GetProperty("battleLost").GetBoolean(), "打輸");
        var after = City(await c.ExpectAsync(MessageTypes.CityState));
        long wounded = after.Wounded.GetValueOrDefault(TroopType.Shield);
        Assert.True(wounded > 0, "有傷兵進醫館");

        await c.SendAsync(MessageTypes.CityHeal, new HealRequest { Troops = new() { [TroopType.Shield] = wounded } });
        var healing = City(await c.ExpectAsync(MessageTypes.CityState));
        Assert.True(healing.Healing != null, "開始治療");
        server.Clock.Advance(healing.Healing!.FinishAtMs - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);
        var healed = (await c.ExpectAsync(MessageTypes.HealingCompleted)).GetProperty("data");
        Assert.Equal(wounded, healed.GetProperty("troops").GetProperty("Shield").GetInt64(), "推播治療完成");
    }

    [Test]
    public static async Task Pvp_AttackOnlineDefender_PlundersAndNotifiesBoth()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var atk = new TestClient();
        await atk.ConnectAsync(server.WsUri);
        await atk.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:raider", DisplayName = "掠奪者" });
        var atkCity = City(await atk.ExpectAsync(MessageTypes.CityState));
        await using var def = new TestClient();
        await def.ConnectAsync(server.WsUri);
        await def.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:keeper", DisplayName = "守城者" });
        var defCity = City(await def.ExpectAsync(MessageTypes.CityState));
        Assert.True(defCity.ShieldUntilMs > server.Clock.NowMs, "新帳號有新手護盾");

        // 測試用：拿掉守方護盾、給攻方部隊
        await server.Sessions.ModifyCityAsync("dev-keeper", c => { c.ShieldUntilMs = 0; foreach (var r in CityConfig.AllResources) c.Resources[r] = 5000; }, CancellationToken.None);
        await def.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-raider", c => c.Troops[TroopType.Shield] = 250, CancellationToken.None);
        await atk.ExpectAsync(MessageTypes.CityState);

        await atk.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 250 }, X = defCity.Surface.X, Y = defCity.Surface.Y });
        var started = City(await atk.ExpectAsync(MessageTypes.CityState));
        Assert.True(started.ShieldUntilMs == 0, "攻城解除自己的護盾");
        var m = started.Surface.Marches[0];
        server.Clock.Advance(m.ArriveAt - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);

        var attacked = (await def.ExpectAsync(MessageTypes.CityAttacked)).GetProperty("data");
        Assert.Equal("掠奪者", attacked.GetProperty("attackerName").GetString(), "守方收到被攻打的通知");
        Assert.True(!attacked.GetProperty("defenderWon").GetBoolean(), "空城被攻破");
        var result = (await atk.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data");
        Assert.True(result.GetProperty("battleWon").GetBoolean(), "攻方打贏");
        Assert.Equal("守城者", result.GetProperty("targetName").GetString());
        var back = City(await atk.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
        Assert.True(back.Returning && back.Carry.Values.Sum() > 0, "帶著掠奪品回城");

        await def.SendAsync(MessageTypes.CityGet, null);
        var defAfter = City(await def.ExpectAsync(MessageTypes.CityState));
        Assert.True(defAfter.GetResource(ResourceType.Wood) < 5000, "守方資源被掠奪");
    }

    [Test]
    public static async Task Pvp_SiegeReachedDuringAnotherCommand_IsStillResolved()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var atk = new TestClient();
        await atk.ConnectAsync(server.WsUri);
        await atk.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:sneak", DisplayName = "偷襲者" });
        await atk.ExpectAsync(MessageTypes.CityState);
        await using var def = new TestClient();
        await def.ConnectAsync(server.WsUri);
        await def.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:watch", DisplayName = "守望者" });
        var defCity = City(await def.ExpectAsync(MessageTypes.CityState));
        await server.Sessions.ModifyCityAsync("dev-watch", c => c.ShieldUntilMs = 0, CancellationToken.None);
        await def.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-sneak", c => c.Troops[TroopType.Shield] = 250, CancellationToken.None);
        await atk.ExpectAsync(MessageTypes.CityState);

        await atk.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 250 }, X = defCity.Surface.X, Y = defCity.Surface.Y });
        var m = City(await atk.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];

        // 抵達的那一刻，背景 tick 還沒跑，玩家先送了另一個行軍指令（這裡是召回一支不存在的行軍）
        server.Clock.Advance(m.ArriveAt - server.Clock.NowMs);
        await atk.SendAsync(MessageTypes.MapRecall, new MapRecallRequest { March = 999 });
        await atk.ExpectAsync(MessageTypes.Error);

        // 攻城事件沒有被指令吞掉：照樣結算、通知雙方
        var attacked = (await def.ExpectAsync(MessageTypes.CityAttacked)).GetProperty("data");
        Assert.Equal("偷襲者", attacked.GetProperty("attackerName").GetString(), "守方收到通知");
        var result = (await atk.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data");
        Assert.True(result.GetProperty("battleWon").GetBoolean(), "攻方收到戰果");
        var back = City(await atk.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
        Assert.True(back.Returning && !back.Sieging, "沒有卡在城下");
    }

    [Test]
    public static async Task Pvp_CrashBetweenPhases_ResumesWithSameResult_DefenderHitOnce()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var def = new TestClient();
        await def.ConnectAsync(server.WsUri);
        await def.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:victim", DisplayName = "受害者" });
        var defCity = City(await def.ExpectAsync(MessageTypes.CityState));
        await server.Sessions.ModifyCityAsync("dev-victim", c =>
        {
            c.ShieldUntilMs = 0;
            c.Troops[TroopType.Shield] = 10;
            foreach (var r in CityConfig.AllResources) c.Resources[r] = 5000;
        }, CancellationToken.None);

        long marchId;
        await using (var atk = new TestClient())
        {
            await atk.ConnectAsync(server.WsUri);
            await atk.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:crasher", DisplayName = "當機者" });
            await atk.ExpectAsync(MessageTypes.CityState);
            await server.Sessions.ModifyCityAsync("dev-crasher", c => c.Troops[TroopType.Shield] = 250, CancellationToken.None);
            await atk.ExpectAsync(MessageTypes.CityState);
            await atk.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 250 }, X = defCity.Surface.X, Y = defCity.Surface.Y });
            var m = City(await atk.ExpectAsync(MessageTypes.CityState)).Surface.Marches[0];
            marchId = m.Id;
            server.Clock.Advance(m.ArriveAt - server.Clock.NowMs);
        }
        for (int i = 0; i < 50 && server.Sessions.OnlineCount > 1; i++) await Task.Delay(50);

        // 模擬「守方那段結算完、存好了，攻方還沒套用就停機」：攻方存檔卡在城下，守方有攻城紀錄
        var now = server.Clock.NowMs;
        List<(AbyssKingdom.Shared.Battle.SquadInput squad, TroopType troop)>? squads = null;
        await server.Sessions.ModifyCityAsync("dev-crasher", c =>
        {
            var reg = new SimpleFieldRegistry();
            reg.SetCity(defCity.Surface.X, defCity.Surface.Y, "dev-victim");
            squads = MarchLogic.Advance(c, now, reg, "dev-crasher").Single().AttackSquads;
        }, CancellationToken.None);
        string battleId = $"dev-crasher:{marchId}";
        PvpOutcome? phase1 = null;
        await server.Sessions.ModifyCityAsync("dev-victim", d => phase1 = PvpLogic.ResolveOnDefender(d, squads!, 7, now, battleId), CancellationToken.None);
        var hit = await FreshCity(def);
        Assert.True(hit.SiegeLog.ContainsKey(battleId), "守方記下了這場仗");
        long troopsAfterHit = hit.GetTroops(TroopType.Shield);
        long woodAfterHit = hit.GetResource(ResourceType.Wood);
        Assert.True(woodAfterHit < 5000, "守方已經被搶");

        // 攻方重新登入：接著結算，拿回同一個結果
        await using var again = new TestClient();
        await again.ConnectAsync(server.WsUri);
        await again.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:crasher" });
        // 登入時先接續結算（戰果推播），才回登入結果
        var result = (await again.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data");
        Assert.True(result.GetProperty("battleWon").GetBoolean(), "攻方收到戰果");
        var back = (await FreshCity(again)).Surface.Marches.Single();
        Assert.True(back.Returning && !back.Sieging, "不再卡在城下");
        Assert.Equal(phase1!.Plunder.Values.Sum(), back.Carry.Values.Sum(), "帶走的正好是當初搶到的");

        CityState after = await FreshCity(def);
        for (int i = 0; i < 40 && after.SiegeLog.Count > 0; i++) { await Task.Delay(50); after = await FreshCity(def); } // 等第三段（刪紀錄）跑完
        Assert.Equal(troopsAfterHit, after.GetTroops(TroopType.Shield), "守方沒有被打第二次");
        Assert.Equal(woodAfterHit, after.GetResource(ResourceType.Wood), "資源沒有被搶第二次");
        Assert.Equal(0, after.SiegeLog.Count, "攻方套用完，紀錄刪掉");
    }

    /// <summary>可以模擬存檔失敗的記憶體存檔。</summary>
    sealed class FlakyStore : IPlayerStore
    {
        readonly InMemoryPlayerStore _inner = new(Json.Options);
        public volatile bool FailSaves;
        public Task<PlayerRecord?> LoadAsync(string playerId, CancellationToken ct) => _inner.LoadAsync(playerId, ct);
        public Task SaveAsync(PlayerRecord record, CancellationToken ct) =>
            FailSaves ? throw new IOException("simulated outage") : _inner.SaveAsync(record, ct);
        public Task<List<PlayerRecord>> ListAllAsync(CancellationToken ct) => _inner.ListAllAsync(ct);
    }

    sealed class FlakyAllianceStore : IAllianceStore
    {
        readonly InMemoryAllianceStore _inner = new(Json.Options);
        public int FailNextSave;
        public Task<AllianceDirectory> LoadAsync(CancellationToken ct) => _inner.LoadAsync(ct);
        public Task SaveAsync(AllianceDirectory directory, CancellationToken ct) =>
            Interlocked.Exchange(ref FailNextSave, 0) == 1
                ? throw new IOException("simulated alliance outage") : _inner.SaveAsync(directory, ct);
    }

    [Test]
    public static async Task Logout_SaveFails_KeptOnlineAndRetried_NoProgressLost()
    {
        var store = new FlakyStore();
        await using var server = new TestServer(s => s.Replace(ServiceDescriptor.Singleton<IPlayerStore>(store)));
        await server.StartAsync();

        await using (var c = new TestClient())
        {
            await c.ConnectAsync(server.WsUri);
            await c.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:unlucky" });
            await c.ExpectAsync(MessageTypes.CityState);
            await server.Sessions.ModifyCityAsync("dev-unlucky", city => city.Troops[TroopType.Rider] = 77, CancellationToken.None);
            await c.ExpectAsync(MessageTypes.CityState);
            store.FailSaves = true;
        }

        // 下線存檔失敗：不移出線上名單（否則進度只剩記憶體裡、而且沒人會再存）
        await Task.Delay(300);
        Assert.Equal(1, server.Sessions.OnlineCount, "存檔失敗時保留在線上等重試");

        // 資料庫恢復，自動存檔重試成功才下線
        store.FailSaves = false;
        await server.Sessions.SaveAllAsync(false, CancellationToken.None);
        Assert.Equal(0, server.Sessions.OnlineCount, "重試成功後下線");

        await using var again = new TestClient();
        await again.ConnectAsync(server.WsUri);
        await again.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:unlucky" });
        var city = City(await again.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(77L, city.GetTroops(TroopType.Rider), "進度沒有遺失");
    }

    [Test]
    public static async Task ModifyCity_SaveNowFails_ReturnsFailureWithoutPushingSuccess()
    {
        var store = new FlakyStore();
        await using var server = new TestServer(s => s.Replace(ServiceDescriptor.Singleton<IPlayerStore>(store)));
        await server.StartAsync();

        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:save-now" });
        await client.ExpectAsync(MessageTypes.CityState);

        store.FailSaves = true;
        bool saved = await server.Sessions.ModifyCityAsync(
            "dev-save-now",
            city => city.Troops[TroopType.Rider] = 88,
            CancellationToken.None,
            saveNow: true);

        Assert.True(!saved, "需要立即持久化的操作不可在存檔失敗時回報成功");
        bool pushed = true;
        try { await client.ExpectAsync(MessageTypes.CityState, 300); }
        catch (Exception ex) when (ex is AssertException or TaskCanceledException) { pushed = false; }
        Assert.True(!pushed, "存檔失敗時不可推播看似成功的城市狀態");
    }

    [Test]
    public static async Task AllianceCreate_RetryDoesNotChargeTwice()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:founder" });
        await client.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-founder", city => city.Buildings[BuildingType.Keep] = 3, CancellationToken.None, saveNow: true);
        await client.ExpectAsync(MessageTypes.CityState);
        var alliances = server.App.Services.GetRequiredService<AllianceService>();
        string operationId = Guid.NewGuid().ToString();
        Assert.Equal(AllianceResult.Ok, await alliances.CreateAsync("dev-founder", "Founder", "淵契盟", "AK", operationId, CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await alliances.CreateAsync("dev-founder", "Founder", "淵契盟", "AK", operationId, CancellationToken.None));
        Assert.Equal(1, (await alliances.SnapshotAsync(CancellationToken.None)).Alliances.Count);
        var city = await FreshCity(client);
        Assert.Equal(CityConfig.StartingResource - AllianceConfig.CreateWood, city.GetResource(ResourceType.Wood));
        Assert.Equal(CityConfig.StartingResource - AllianceConfig.CreateStone, city.GetResource(ResourceType.Stone));
    }

    [Test]
    public static async Task AllianceProtocol_CreateJoinAndRejectUnauthorizedReview()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var leader = new TestClient();
        await leader.ConnectAsync(server.WsUri);
        await leader.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:alliance-leader" });
        await leader.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-alliance-leader", city => city.Buildings[BuildingType.Keep] = 3, CancellationToken.None, saveNow: true);
        await leader.ExpectAsync(MessageTypes.CityState);
        int createReq = await leader.SendAsync(MessageTypes.AllianceCreate,
            new AllianceCreateRequest { Name = "契印聯盟", Tag = "SEAL", OperationId = Guid.NewGuid().ToString() });
        var created = await leader.ExpectAsync(MessageTypes.AllianceState);
        Assert.Equal(createReq, created.GetProperty("reqId").GetInt32());
        var allianceId = created.GetProperty("data").GetProperty("myAllianceId").GetString();
        Assert.True(!string.IsNullOrEmpty(allianceId), "創盟後回傳盟籍");

        await using var member = new TestClient();
        await member.ConnectAsync(server.WsUri);
        await member.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:alliance-member" });
        await member.ExpectAsync(MessageTypes.CityState);
        int blockedReq = await member.SendAsync(MessageTypes.AllianceJoin, new AllianceIdRequest { AllianceId = allianceId });
        var blocked = await member.ExpectAsync(MessageTypes.Error);
        Assert.Equal(blockedReq, blocked.GetProperty("reqId").GetInt32());
        Assert.Equal("LevelTooLow", blocked.GetProperty("data").GetProperty("detail").GetString());
        await server.Sessions.ModifyCityAsync("dev-alliance-member", city => city.Buildings[BuildingType.Keep] = 2, CancellationToken.None, saveNow: true);
        await member.ExpectAsync(MessageTypes.CityState);
        await member.SendAsync(MessageTypes.AllianceJoin, new AllianceIdRequest { AllianceId = allianceId });
        var joined = await member.ExpectAsync(MessageTypes.AllianceState);
        Assert.Equal(allianceId, joined.GetProperty("data").GetProperty("myAllianceId").GetString());
        await leader.SendAsync(MessageTypes.WorldCities, null);
        var world = await leader.ExpectAsync(MessageTypes.WorldCities);
        var ownPin = world.GetProperty("data").GetProperty("cities").EnumerateArray().Single(x => x.GetProperty("mine").GetBoolean());
        Assert.True(ownPin.GetProperty("name").GetString()!.StartsWith("[SEAL] "), "大地圖主城名牌顯示聯盟簡稱");
        Assert.True(ownPin.GetProperty("allied").GetBoolean(), "同盟關係包含本人");
        Assert.True(world.GetProperty("data").GetProperty("cities").EnumerateArray().All(x => x.GetProperty("allied").GetBoolean()), "盟友由伺服器標示");
        await member.SendAsync(MessageTypes.AllianceReview, new AllianceReviewRequest { PlayerId = "dev:someone", Accept = true });
        var denied = await member.ExpectAsync(MessageTypes.Error);
        Assert.Equal("Forbidden", denied.GetProperty("data").GetProperty("detail").GetString());
    }

    [Test]
    public static async Task WorldCities_AllianceIdentityIsAuthoritativeAndRefreshesAfterLeave()
    {
        await using var server = new TestServer(); await server.StartAsync();
        var ids = new[] { "blue-owner", "blue-member", "red-owner", "neutral-owner" };
        var clients = new List<TestClient>();
        try
        {
            foreach (var id in ids)
            {
                var client = new TestClient(); clients.Add(client); await client.ConnectAsync(server.WsUri);
                await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:" + id });
                await client.ExpectAsync(MessageTypes.CityState);
            }
            var store = server.App.Services.GetRequiredService<IAllianceStore>();
            await store.SaveAsync(new AllianceDirectory { Alliances = new()
            {
                new AllianceRecord { Id = "blue", Tag = "SAME", Members = new()
                { new AllianceMemberRecord { PlayerId = "dev-blue-owner", Role = AllianceRole.Leader, Epoch = 1 },
                  new AllianceMemberRecord { PlayerId = "dev-blue-member", Role = AllianceRole.Member, Epoch = 1 } } },
                new AllianceRecord { Id = "red", Tag = "SAME", Members = new()
                { new AllianceMemberRecord { PlayerId = "dev-red-owner", Role = AllianceRole.Leader, Epoch = 1 } } }
            } }, CancellationToken.None);
            await clients[0].SendAsync(MessageTypes.WorldCities, null);
            var pins = (await clients[0].ExpectAsync(MessageTypes.WorldCities)).GetProperty("data").GetProperty("cities");
            Assert.Equal(2, pins.EnumerateArray().Count(p => p.GetProperty("allied").GetBoolean()));
            Assert.Equal(2, pins.EnumerateArray().Count(p => !p.GetProperty("allied").GetBoolean()));
            Assert.True(pins.EnumerateArray().All(p => !p.TryGetProperty("playerId", out _)), "公開主城不外洩帳號 ID");
            var service = server.App.Services.GetRequiredService<AllianceService>();
            Assert.Equal(AllianceResult.Ok, await service.LeaveAsync("dev-blue-member", CancellationToken.None));
            await clients[1].SendAsync(MessageTypes.WorldCities, null);
            var after = (await clients[1].ExpectAsync(MessageTypes.WorldCities)).GetProperty("data").GetProperty("cities");
            Assert.Equal(0, after.EnumerateArray().Count(p => p.GetProperty("allied").GetBoolean()));
            Assert.Equal(1, after.EnumerateArray().Count(p => p.GetProperty("mine").GetBoolean()));
        }
        finally { foreach (var client in clients) await client.DisposeAsync(); }
    }

    [Test]
    public static async Task AllianceRallyProtocol_FormTravelSettleAndReturn()
    {
        await using var server = new TestServer(); await server.StartAsync();
        await using var leader = new TestClient(); await using var member = new TestClient();
        await leader.ConnectAsync(server.WsUri); await member.ConnectAsync(server.WsUri);
        await leader.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:rally-owner" });
        await member.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:rally-member" });
        await leader.ExpectAsync(MessageTypes.CityState); await member.ExpectAsync(MessageTypes.CityState);
        var store = server.App.Services.GetRequiredService<IAllianceStore>();
        await store.SaveAsync(new AllianceDirectory { Alliances = new() { new AllianceRecord { Id = "rally-a", Members = new()
        { new AllianceMemberRecord { PlayerId = "dev-rally-owner", Role = AllianceRole.Leader, Epoch = 1 },
          new AllianceMemberRecord { PlayerId = "dev-rally-member", Role = AllianceRole.Member, Epoch = 1 } } } } }, CancellationToken.None);
        var own = await FreshCity(leader);
        foreach (var id in new[] { "dev-rally-owner", "dev-rally-member" })
            await server.Sessions.ModifyCityAsync(id, city =>
            {
                city.Buildings[BuildingType.Keep] = 3; city.Buildings[BuildingType.Hospital] = 10;
                city.Heroes[HeroId.Hold].Level = 10; foreach (var t in CityConfig.AllTroops) city.Troops[t] = 1_000;
                if (id.EndsWith("member")) { city.Surface.X = own.Surface.X + 4; city.Surface.Y = own.Surface.Y; }
            }, CancellationToken.None, saveNow: true);
        var target = SurfaceLogic.Features().First(f => f.Type == FeatureType.MonsterCamp && f.Level == 2);
        var troops = CityConfig.AllTroops.ToDictionary(t => t, _ => 100L);
        await leader.SendAsync(MessageTypes.AllianceRallyCreate, new AllianceRallyCreateRequest
        { Hero = HeroId.Hold, Troops = troops, X = target.X, Y = target.Y, Seconds = 60, OperationId = Guid.NewGuid().ToString() });
        var created = await leader.ExpectAsync(MessageTypes.AllianceState);
        string rallyId = created.GetProperty("data").GetProperty("rallies")[0].GetProperty("id").GetString()!;
        await member.SendAsync(MessageTypes.AllianceRallyJoin, new AllianceRallyJoinRequest
        { Id = rallyId, Hero = HeroId.Hold, Troops = troops, OperationId = Guid.NewGuid().ToString() });
        var joined = await member.ExpectAsync(MessageTypes.AllianceState);
        Assert.Equal(2, joined.GetProperty("data").GetProperty("rallies")[0].GetProperty("participants").GetArrayLength());
        var service = server.App.Services.GetRequiredService<AllianceService>();
        server.Clock.Advance(60_000); await service.AdvanceRalliesAsync(CancellationToken.None);
        await leader.SendAsync(MessageTypes.AllianceGet, null); var traveling = await leader.ExpectAsync(MessageTypes.AllianceState);
        Assert.Equal("Traveling", traveling.GetProperty("data").GetProperty("rallies")[0].GetProperty("state").GetString());
        Assert.True((traveling.GetProperty("data").GetProperty("guideProgress").GetInt32() & AllianceConfig.GuideRallied) != 0, "集結出發記錄入門檢核");
        var city = await FreshCity(leader); Assert.True(city.ShieldUntilMs > server.Clock.NowMs, "PvE 集結保留盾");
        server.Clock.Advance(city.Surface.Marches.Single().ArriveAt - server.Clock.NowMs);
        await service.AdvanceRalliesAsync(CancellationToken.None);
        await member.SendAsync(MessageTypes.AllianceGet, null); var outcome = await member.ExpectAsync(MessageTypes.AllianceState);
        Assert.True(outcome.GetProperty("data").GetProperty("rallies")[0].GetProperty("outcome").ValueKind == JsonValueKind.String, "戰報結果與分配已送到介面");
        server.Clock.Advance(10_000_000); await service.AdvanceRalliesAsync(CancellationToken.None);
        Assert.Equal(0, (await FreshCity(leader)).Surface.Marches.Count);
        Assert.Equal(0, (await FreshCity(member)).Surface.Marches.Count);
    }

    [Test]
    public static async Task AdminAllianceDiagnostics_RequireTokenAndAuditRetryReason()
    {
        const string token = "local-test-admin-alliance-token-12345";
        await using var server = new TestServer("--Admin:Token=" + token); await server.StartAsync();
        using var http = new HttpClient { BaseAddress = server.HttpUri };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/admin/alliance")).StatusCode);
        http.DefaultRequestHeaders.Add("X-Admin-Token", token);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/admin/alliance")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/admin/alliance/retry", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/admin/alliance/retry", new StringContent("{\"reason\":\"local recovery verification\"}", Encoding.UTF8, "application/json"))).StatusCode);
        var d = await server.App.Services.GetRequiredService<AllianceService>().SnapshotAsync(CancellationToken.None);
        Assert.Equal(1, d.AdminAudit.Count);
        Assert.Equal("local recovery verification", d.AdminAudit[0].Reason);
    }

    [Test]
    public static async Task AllianceFlagProtocol_PlaceRetryPublicMapAndRemoval()
    {
        await using var server = new TestServer(); await server.StartAsync();
        await using var client = new TestClient(); await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:flag-owner" });
        await client.ExpectAsync(MessageTypes.CityState);
        var store = server.App.Services.GetRequiredService<IAllianceStore>();
        await store.SaveAsync(new AllianceDirectory { Alliances = new() { new AllianceRecord { Id = "flag-a", Tag = "FL",
            Members = new() { new AllianceMemberRecord { PlayerId = "dev-flag-owner", Role = AllianceRole.Leader } } } } }, CancellationToken.None);
        await server.Sessions.ModifyCityAsync("dev-flag-owner", city =>
        {
            foreach (var b in CityConfig.AllBuildings) city.Buildings[b] = 0;
            city.Buildings[BuildingType.Keep] = 3;
            city.Resources[ResourceType.Wood] = 50_000; city.Resources[ResourceType.Stone] = 50_000;
        }, CancellationToken.None, saveNow: true);
        var city = await FreshCity(client); var home = SurfaceLogic.CityCenter(city.Surface);
        var point = AllianceTerritory.Coverage(home.x, home.y, 10).Select(SurfaceLogic.FromTile)
            .First(p => AllianceTerritory.AllowedPoint(p.x, p.y));
        var request = new AllianceFlagPlaceRequest { Slot = 1, X = point.x, Y = point.y, OperationId = Guid.NewGuid().ToString() };
        await client.SendAsync(MessageTypes.AllianceFlagPlace, request);
        var created = await client.ExpectAsync(MessageTypes.AllianceState);
        Assert.True(created.GetProperty("data").GetProperty("flags")[0].GetProperty("paid").GetBoolean(), "付款成功");
        await client.SendAsync(MessageTypes.AllianceFlagPlace, request); await client.ExpectAsync(MessageTypes.AllianceState);
        Assert.Equal(40_000L, (await FreshCity(client)).GetResource(ResourceType.Wood));
        await client.SendAsync(MessageTypes.WorldTerritory, null);
        var map = await client.ExpectAsync(MessageTypes.WorldTerritory);
        Assert.Equal("FL", map.GetProperty("data").GetProperty("flags")[0].GetProperty("tag").GetString());
        server.Clock.Advance(AllianceTerritory.BuildMs);
        await client.SendAsync(MessageTypes.AllianceFlagRemove, new AllianceFlagRemoveRequest { Slot = 1 });
        await client.ExpectAsync(MessageTypes.AllianceState);
        await client.SendAsync(MessageTypes.WorldTerritory, null);
        map = await client.ExpectAsync(MessageTypes.WorldTerritory);
        Assert.Equal(0, map.GetProperty("data").GetProperty("flags").GetArrayLength());
        request.OperationId = Guid.NewGuid().ToString();
        await client.SendAsync(MessageTypes.AllianceFlagPlace, request);
        var denied = await client.ExpectAsync(MessageTypes.Error);
        Assert.Equal("Flag:Cooldown", denied.GetProperty("data").GetProperty("detail").GetString());
    }

    [Test]
    public static async Task AllianceReinforcementProtocol_DispatchGarrisonAndNormalRecall()
    {
        await using var server = new TestServer(); await server.StartAsync();
        await using var sender = new TestClient(); await using var receiver = new TestClient();
        await sender.ConnectAsync(server.WsUri); await receiver.ConnectAsync(server.WsUri);
        await sender.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:reinforce-sender" });
        await receiver.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:reinforce-receiver" });
        await sender.ExpectAsync(MessageTypes.CityState); await receiver.ExpectAsync(MessageTypes.CityState);
        var store = server.App.Services.GetRequiredService<IAllianceStore>();
        await store.SaveAsync(new AllianceDirectory { Alliances = new()
        {
            new AllianceRecord { Id = "reinforcement-alliance", Members = new()
            {
                new AllianceMemberRecord { PlayerId = "dev-reinforce-sender", Role = AllianceRole.Leader, Epoch = 1 },
                new AllianceMemberRecord { PlayerId = "dev-reinforce-receiver", Role = AllianceRole.Member, Epoch = 1 }
            } }
        } }, CancellationToken.None);
        foreach (var id in new[] { "dev-reinforce-sender", "dev-reinforce-receiver" })
            await server.Sessions.ModifyCityAsync(id, city =>
            {
                city.Buildings[BuildingType.Keep] = 3;
                foreach (var t in CityConfig.AllTroops) city.Troops[t] = 1_000;
            }, CancellationToken.None, saveNow: true);
        var request = new AllianceReinforcementSendRequest { PlayerId = "dev-reinforce-receiver", Hero = HeroId.Hold,
            OperationId = Guid.NewGuid().ToString(), Troops = new() { [TroopType.Shield] = 100,
                [TroopType.Crossbow] = 120, [TroopType.Rider] = 80 } };
        await sender.SendAsync(MessageTypes.AllianceReinforcementSend, request);
        var sent = await sender.ExpectAsync(MessageTypes.AllianceState);
        var view = sent.GetProperty("data").GetProperty("reinforcements")[0];
        Assert.Equal("Traveling", view.GetProperty("state").GetString());
        long marchId = view.GetProperty("marchId").GetInt64();
        await sender.SendAsync(MessageTypes.AllianceReinforcementSend, request);
        await sender.ExpectAsync(MessageTypes.AllianceState);
        var city = await FreshCity(sender);
        Assert.Equal(1, city.Surface.Marches.Count);
        Assert.Equal(900L, city.GetTroops(TroopType.Shield));
        Assert.True(city.ShieldUntilMs > server.Clock.NowMs, "派援保留護盾");
        server.Clock.Advance(city.Surface.Marches.Single().ArriveAt - server.Clock.NowMs);
        var alliances = server.App.Services.GetRequiredService<AllianceService>();
        await alliances.AdvanceReinforcementsAsync(CancellationToken.None);
        await receiver.SendAsync(MessageTypes.AllianceGet, null);
        var arrived = await receiver.ExpectAsync(MessageTypes.AllianceState);
        Assert.Equal("Garrisoned", arrived.GetProperty("data").GetProperty("reinforcements")[0].GetProperty("state").GetString());
        await sender.SendAsync(MessageTypes.MapRecall, new MapRecallRequest { March = marchId });
        await sender.ExpectAsync(MessageTypes.AllianceState);
        city = await FreshCity(sender);
        Assert.True(city.Surface.Marches.Single().Returning, "行軍列表召回走援軍存檔流程");
        server.Clock.Advance(city.Surface.Marches.Single().ArriveAt - server.Clock.NowMs);
        await alliances.AdvanceReinforcementsAsync(CancellationToken.None);
        city = await FreshCity(sender);
        Assert.Equal(0, city.Surface.Marches.Count);
        Assert.Equal(1_000L, city.GetTroops(TroopType.Shield));
        Assert.Equal(1_000L, (await FreshCity(receiver)).GetTroops(TroopType.Shield));
    }

    [Test]
    public static async Task AllianceHelp_RequestAndRetryShortensOwnerQueueOnce()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var owner = new TestClient();
        await using var helper = new TestClient();
        await owner.ConnectAsync(server.WsUri);
        await helper.ConnectAsync(server.WsUri);
        await owner.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:help-owner" });
        await helper.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:help-helper" });
        await owner.ExpectAsync(MessageTypes.CityState);
        await helper.ExpectAsync(MessageTypes.CityState);
        var store = server.App.Services.GetRequiredService<IAllianceStore>();
        await store.SaveAsync(new AllianceDirectory { Alliances = new()
        {
            new AllianceRecord { Id = "help-alliance", Members = new()
            {
                new AllianceMemberRecord { PlayerId = "dev-help-owner", DisplayName = "Owner", Role = AllianceRole.Leader, Epoch = 1 },
                new AllianceMemberRecord { PlayerId = "dev-help-helper", DisplayName = "Helper", Role = AllianceRole.Member, Epoch = 1 }
            } }
        } }, CancellationToken.None);
        await server.Sessions.ModifyCityAsync("dev-help-owner", city => city.Upgrade = new UpgradeJob
        {
            Building = BuildingType.Keep, TargetLevel = 2, StartedAtMs = T0,
            FinishAtMs = T0 + 600_000,
            Help = new AllianceHelpProgress { QueueId = 42, OriginalDurationMs = 600_000, MaxCount = 10 }
        }, CancellationToken.None, saveNow: true);

        await owner.SendAsync(MessageTypes.AllianceHelpRequest, new AllianceHelpRequestMessage { Queue = QueueType.Building });
        var requested = await owner.ExpectAsync(MessageTypes.AllianceState);
        Assert.Equal(42L, requested.GetProperty("data").GetProperty("helpRequests")[0].GetProperty("queueId").GetInt64());
        await helper.SendAsync(MessageTypes.AllianceHelpAll, null);
        var helped = await helper.ExpectAsync(MessageTypes.AllianceHelpResult);
        Assert.Equal(1, helped.GetProperty("data").GetProperty("applied").GetInt32());
        await helper.SendAsync(MessageTypes.AllianceGet, null);
        var guide = await helper.ExpectAsync(MessageTypes.AllianceState);
        Assert.True((guide.GetProperty("data").GetProperty("guideProgress").GetInt32() & AllianceConfig.GuideHelped) != 0, "成功互助記錄入門檢核");
        await helper.SendAsync(MessageTypes.AllianceGet, null);
        Assert.Equal(1, (await helper.ExpectAsync(MessageTypes.AllianceState))
            .GetProperty("data").GetProperty("helpRequests")[0].GetProperty("usedCount").GetInt32());
        Assert.Equal(T0 + 594_000, (await FreshCity(owner)).Upgrade!.FinishAtMs);

        await helper.SendAsync(MessageTypes.AllianceHelpAll, null);
        var retry = await helper.ExpectAsync(MessageTypes.AllianceHelpResult);
        Assert.Equal(0, retry.GetProperty("data").GetProperty("applied").GetInt32());
        Assert.Equal(T0 + 594_000, (await FreshCity(owner)).Upgrade!.FinishAtMs);
        await owner.SendAsync(MessageTypes.AllianceHelpAll, null);
        Assert.Equal(0, (await owner.ExpectAsync(MessageTypes.AllianceHelpResult))
            .GetProperty("data").GetProperty("applied").GetInt32());
        await owner.SendAsync(MessageTypes.CityCancel, new CancelRequest { Queue = QueueType.Building });
        await owner.ExpectAsync(MessageTypes.CityState);
        int refresh = await owner.SendAsync(MessageTypes.AllianceGet, null);
        while ((await owner.ExpectAsync(MessageTypes.AllianceState)).GetProperty("reqId").GetInt32() != refresh) { }
        Assert.Equal(0, (await server.App.Services.GetRequiredService<AllianceService>()
            .SnapshotAsync(CancellationToken.None)).Alliances.Single().HelpRequests.Count);
    }

    [Test]
    public static async Task AllianceHelp_DirectorySaveFailure_RetryReconcilesWithoutDoubleHelp()
    {
        var allianceStore = new FlakyAllianceStore();
        await using var server = new TestServer(s =>
            s.Replace(ServiceDescriptor.Singleton<IAllianceStore>(allianceStore)));
        await server.StartAsync();
        await using var owner = new TestClient();
        await using var helper = new TestClient();
        await owner.ConnectAsync(server.WsUri);
        await helper.ConnectAsync(server.WsUri);
        await owner.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:failure-owner" });
        await helper.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:failure-helper" });
        await owner.ExpectAsync(MessageTypes.CityState);
        await helper.ExpectAsync(MessageTypes.CityState);
        await allianceStore.SaveAsync(new AllianceDirectory { Alliances = new()
        {
            new AllianceRecord { Id = "failure-alliance", Members = new()
            {
                new AllianceMemberRecord { PlayerId = "dev-failure-owner", DisplayName = "Owner", Role = AllianceRole.Leader, Epoch = 1 },
                new AllianceMemberRecord { PlayerId = "dev-failure-helper", DisplayName = "Helper", Role = AllianceRole.Member, Epoch = 1 }
            } }
        } }, CancellationToken.None);
        await server.Sessions.ModifyCityAsync("dev-failure-owner", city => city.Upgrade = new UpgradeJob
        {
            Building = BuildingType.Keep, TargetLevel = 2, StartedAtMs = T0, FinishAtMs = T0 + 600_000,
            Help = new AllianceHelpProgress { QueueId = 17, OriginalDurationMs = 600_000, MaxCount = 10 }
        }, CancellationToken.None, saveNow: true);
        await owner.SendAsync(MessageTypes.AllianceHelpRequest, new AllianceHelpRequestMessage { Queue = QueueType.Building });
        await owner.ExpectAsync(MessageTypes.AllianceState);

        allianceStore.FailNextSave = 1;
        await helper.SendAsync(MessageTypes.AllianceHelpAll, null);
        var error = await helper.ExpectAsync(MessageTypes.Error);
        Assert.Equal("PersistenceFailed", error.GetProperty("data").GetProperty("detail").GetString());
        Assert.Equal(T0 + 594_000, (await FreshCity(owner)).Upgrade!.FinishAtMs);
        await helper.SendAsync(MessageTypes.AllianceHelpAll, null);
        var retry = await helper.ExpectAsync(MessageTypes.AllianceHelpResult);
        Assert.Equal(0, retry.GetProperty("data").GetProperty("applied").GetInt32());
        Assert.Equal(T0 + 594_000, (await FreshCity(owner)).Upgrade!.FinishAtMs);
        Assert.Equal(1, (await allianceStore.LoadAsync(CancellationToken.None))
            .Alliances.Single().HelpRequests.Single().UsedCount);
    }

    [Test]
    public static async Task AllianceAttack_RejectsAtLaunchAndRetreatsIfAlliedBeforeArrival()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using var attacker = new TestClient();
        await attacker.ConnectAsync(server.WsUri);
        await attacker.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:ally-attacker" });
        await attacker.ExpectAsync(MessageTypes.CityState);
        await using var defender = new TestClient();
        await defender.ConnectAsync(server.WsUri);
        await defender.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:ally-defender" });
        var target = City(await defender.ExpectAsync(MessageTypes.CityState));
        await server.Sessions.ModifyCityAsync("dev-ally-defender", c => c.ShieldUntilMs = 0, CancellationToken.None);
        await defender.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-ally-attacker", c => c.Troops[TroopType.Shield] = 250, CancellationToken.None);
        await attacker.ExpectAsync(MessageTypes.CityState);
        var store = server.App.Services.GetRequiredService<IAllianceStore>();
        var alliance = new AllianceRecord { Id = "allied", Name = "Allied", Tag = "ALLY", Members = new()
        {
            new AllianceMemberRecord { PlayerId = "dev-ally-attacker", Role = AllianceRole.Leader },
            new AllianceMemberRecord { PlayerId = "dev-ally-defender", Role = AllianceRole.Member }
        } };
        await store.SaveAsync(new AllianceDirectory { Alliances = new() { alliance } }, CancellationToken.None);
        await attacker.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 250 }, X = target.Surface.X, Y = target.Surface.Y });
        var rejected = await attacker.ExpectAsync(MessageTypes.Error);
        Assert.Equal("AlliedCity", rejected.GetProperty("data").GetProperty("detail").GetString());
        Assert.Equal(250L, (await FreshCity(attacker)).GetTroops(TroopType.Shield));

        await store.SaveAsync(new AllianceDirectory(), CancellationToken.None);
        await attacker.SendAsync(MessageTypes.MapMarch, new MapMarchRequest { Hero = HeroId.Hold, Troops = new() { [TroopType.Shield] = 250 }, X = target.Surface.X, Y = target.Surface.Y });
        var march = City(await attacker.ExpectAsync(MessageTypes.CityState)).Surface.Marches.Single();
        await store.SaveAsync(new AllianceDirectory { Alliances = new() { alliance } }, CancellationToken.None);
        server.Clock.Advance(march.ArriveAt - server.Clock.NowMs);
        await server.Sessions.TickAsync(CancellationToken.None);
        var arrival = (await attacker.ExpectAsync(MessageTypes.MarchArrived)).GetProperty("data");
        Assert.True(arrival.GetProperty("allied").GetBoolean(), "抵達時重新確認盟籍並無戰鬥折返");
        Assert.True(!arrival.GetProperty("battleWon").GetBoolean(), "盟友不能被掠奪");
        Assert.Equal(250L, City(await attacker.ExpectAsync(MessageTypes.CityState)).Surface.Marches.Single().Troops[TroopType.Shield]);
    }

    [Test]
    public static async Task AllianceCreate_SaveFailure_RetryUsesSameReceipt()
    {
        var playerStore = new FlakyStore();
        await using var server = new TestServer(s => s.Replace(ServiceDescriptor.Singleton<IPlayerStore>(playerStore)));
        await server.StartAsync();
        await using var client = new TestClient();
        await client.ConnectAsync(server.WsUri);
        await client.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:alliance-retry" });
        await client.ExpectAsync(MessageTypes.CityState);
        await server.Sessions.ModifyCityAsync("dev-alliance-retry", c => c.Buildings[BuildingType.Keep] = 3, CancellationToken.None, saveNow: true);
        await client.ExpectAsync(MessageTypes.CityState);
        var service = server.App.Services.GetRequiredService<AllianceService>();
        string op = Guid.NewGuid().ToString();
        playerStore.FailSaves = true;
        Assert.Equal(AllianceResult.PersistenceFailed, await service.CreateAsync("dev-alliance-retry", "Retry", "重試之盟", "RE", op, CancellationToken.None));
        Assert.True((await service.SnapshotAsync(CancellationToken.None)).Alliances.Single().PendingCreate, "失敗時預留仍可恢復");
        playerStore.FailSaves = false;
        Assert.Equal(AllianceResult.Ok, await service.CreateAsync("dev-alliance-retry", "Retry", "重試之盟", "RE", op, CancellationToken.None));
        Assert.Equal(AllianceResult.Ok, await service.CreateAsync("dev-alliance-retry", "Retry", "重試之盟", "RE", op, CancellationToken.None));
        var city = await FreshCity(client);
        Assert.Equal(CityConfig.StartingResource - AllianceConfig.CreateWood, city.GetResource(ResourceType.Wood));
        Assert.Equal(1, city.SocialReceipts.Count);
        Assert.True(!(await service.SnapshotAsync(CancellationToken.None)).Alliances.Single().PendingCreate, "重試後聯盟生效");
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
