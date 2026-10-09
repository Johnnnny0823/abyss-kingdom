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
        Assert.True(pins.EnumerateArray().Any(p => p.GetProperty("name").GetString() == "南境"), "看得到鄰居的名字");
    }

    [Test]
    public static async Task Login_ReturnsTroopsFromLegacyAbyssMarches()
    {
        await using var server = new TestServer();
        await server.StartAsync();
        await using (var c1 = new TestClient())
        {
            await c1.ConnectAsync(server.WsUri);
            await c1.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:oldsave" });
            await c1.ExpectAsync(MessageTypes.CityState);
            await server.Sessions.ModifyCityAsync("dev-oldsave", c =>
                c.Map = new LegacyMapState { Marches = { new LegacyMarch { Troops = { [TroopType.Shield] = 70 } } } }, CancellationToken.None);
        }
        await server.Sessions.SaveAllAsync(true, CancellationToken.None);
        await using var c2 = new TestClient();
        await c2.ConnectAsync(server.WsUri);
        await c2.SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:oldsave" });
        var city = City(await c2.ExpectAsync(MessageTypes.CityState));
        Assert.Equal(70L, city.GetTroops(TroopType.Shield), "舊深淵行軍的部隊回到兵營");
        Assert.True(city.Map == null, "舊狀態清掉");
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
