using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AbyssKingdom.Server.Auth;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Net;

/// <summary>包裝一條 WebSocket：送訊息時加鎖（WebSocket 不允許同時多個 Send）。</summary>
public sealed class WsConnection : IClientConnection
{
    private readonly WebSocket _socket;
    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WsConnection(WebSocket socket, JsonSerializerOptions json)
    {
        _socket = socket;
        _json = json;
    }

    public async Task SendAsync(string type, int reqId, object? data)
    {
        if (_socket.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type, reqId, data }, _json);
        await _sendLock.WaitAsync();
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task CloseAsync(string reason)
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None);
        }
        catch (WebSocketException) { }
    }
}

/// <summary>
/// 處理一條客戶端連線：收訊息 → 分派 → 回應。
/// 所有遊戲數值都在這裡（伺服器）計算，客戶端只送「意圖」（例如：我想升級兵營）。
/// </summary>
public sealed class WebSocketHandler
{
    private const int MaxMessageBytes = 64 * 1024;

    private readonly SessionManager _sessions;
    private readonly IAuthVerifier _auth;
    private readonly IClock _clock;
    private readonly JsonSerializerOptions _json;
    private readonly ILogger<WebSocketHandler> _log;

    public WebSocketHandler(SessionManager sessions, IAuthVerifier auth, IClock clock, JsonSerializerOptions json, ILogger<WebSocketHandler> log)
    {
        _sessions = sessions;
        _auth = auth;
        _clock = clock;
        _json = json;
        _log = log;
    }

    public async Task RunAsync(WebSocket socket, CancellationToken ct)
    {
        var conn = new WsConnection(socket, _json);
        PlayerSession? session = null;
        var buffer = new byte[8 * 1024];
        using var message = new MemoryStream();

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // 回應關閉握手，讓客戶端正常結束
                        if (socket.State == WebSocketState.CloseReceived)
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        await conn.CloseAsync("message_too_large");
                        return;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text) continue;
                session = await HandleAsync(conn, session, message.ToArray(), ct);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // 客戶端斷線屬正常情況
            _log.LogDebug(ex, "Connection ended");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled error on connection");
        }
        finally
        {
            if (session != null) await _sessions.LogoutAsync(session, conn);
        }
    }

    private async Task<PlayerSession?> HandleAsync(WsConnection conn, PlayerSession? session, byte[] raw, CancellationToken ct)
    {
        string type;
        int reqId;
        JsonElement data;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            type = root.GetProperty("type").GetString() ?? "";
            reqId = root.TryGetProperty("reqId", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0;
            data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            await SendError(conn, 0, ErrorCodes.BadRequest, "invalid envelope");
            return session;
        }

        switch (type)
        {
            case MessageTypes.Ping:
                await conn.SendAsync(MessageTypes.Pong, reqId, new { serverTimeMs = _clock.NowMs });
                return session;

            case MessageTypes.Login:
                return await HandleLogin(conn, session, reqId, data, ct);
        }

        if (session == null)
        {
            await SendError(conn, reqId, ErrorCodes.NotLoggedIn, "login first");
            return null;
        }

        switch (type)
        {
            case MessageTypes.CityGet:
                await SendCity(conn, session, reqId, ct);
                break;

            case MessageTypes.CityUpgrade:
                var up = Deserialize<UpgradeRequest>(data);
                if (up == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "building required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.UpgradeRejected, ct, (city, now) =>
                    Fail(CityLogic.StartUpgrade(city, up.Building, now), UpgradeResult.Ok));
                break;

            case MessageTypes.CityTrain:
                var train = Deserialize<TrainRequest>(data);
                if (train == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "troop and count required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.TrainRejected, ct, (city, now) =>
                    Fail(CityLogic.StartTraining(city, train.Troop, train.Count, now), TrainResult.Ok));
                break;

            case MessageTypes.CityCancel:
                var cancel = Deserialize<CancelRequest>(data);
                if (cancel == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "queue required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.CancelRejected, ct, (city, now) =>
                    Fail(CityLogic.Cancel(city, cancel.Queue, now), CancelResult.Ok));
                break;

            case MessageTypes.CitySpeedup:
                var speed = Deserialize<SpeedupRequest>(data);
                if (speed == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "queue required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.SpeedupRejected, ct, (city, now) =>
                    Fail(speed.Item == null
                        ? CityLogic.SpeedupFree(city, speed.Queue, now)
                        : CityLogic.UseSpeedupItem(city, speed.Queue, speed.Item.Value, speed.Count, now), SpeedupResult.Ok));
                break;
            default:
                await SendError(conn, reqId, ErrorCodes.UnknownType, type);
                break;
        }
        return session;
    }

    private async Task<PlayerSession?> HandleLogin(WsConnection conn, PlayerSession? session, int reqId, JsonElement data, CancellationToken ct)
    {
        if (session != null)
        {
            await SendError(conn, reqId, ErrorCodes.BadRequest, "already logged in");
            return session;
        }

        var req = Deserialize<LoginRequest>(data);
        if (req?.Token == null)
        {
            await SendError(conn, reqId, ErrorCodes.BadRequest, "token required");
            return null;
        }

        var auth = await _auth.VerifyAsync(req.Token, ct);
        if (auth == null)
        {
            await SendError(conn, reqId, ErrorCodes.AuthFailed, "invalid token");
            return null;
        }

        var name = SanitizeName(req.DisplayName) ?? "探窟者";
        var newSession = await _sessions.LoginAsync(auth.PlayerId, name, conn, ct);
        await conn.SendAsync(MessageTypes.LoginOk, reqId, new LoginOk
        {
            PlayerId = newSession.PlayerId,
            DisplayName = newSession.Record.DisplayName,
            ServerTimeMs = _clock.NowMs,
        });
        await SendCity(conn, newSession, 0, ct);
        return newSession;
    }

    /// <summary>結果碼不是 ok 就回傳錯誤字串（給客戶端顯示），否則 null。</summary>
    private static string? Fail<T>(T result, T ok) where T : struct, Enum
        => EqualityComparer<T>.Default.Equals(result, ok) ? null : result.ToString();

    /// <summary>
    /// 在玩家鎖內執行一個城市操作：先推進到現在（把已完成的工程收下來並通知），再執行操作。
    /// 成功回傳最新城市（帶 reqId），失敗回傳錯誤。
    /// </summary>
    private async Task RunCityAction(WsConnection conn, PlayerSession session, int reqId, string errorCode,
        CancellationToken ct, Func<CityState, long, string?> action)
    {
        UpgradeJob? done;
        TrainingJob? trained;
        string? error;
        await session.Gate.WaitAsync(ct);
        try
        {
            var now = _clock.NowMs;
            done = CityLogic.Advance(session.Record.City, now, out trained);
            error = action(session.Record.City, now);
            if (done != null || trained != null || error == null) session.Dirty = true;
        }
        finally
        {
            session.Gate.Release();
        }

        await NotifyCompleted(conn, done, trained);
        if (error != null) await SendError(conn, reqId, errorCode, error);
        else await SendCity(conn, session, reqId, ct);
    }

    /// <summary>送出最新城市狀態；推進時若有工程完成（例如剛被加速），先推播完成通知。</summary>
    private async Task SendCity(WsConnection conn, PlayerSession session, int reqId, CancellationToken ct)
    {
        byte[] snapshot;
        UpgradeJob? done;
        TrainingJob? trained;
        var now = _clock.NowMs;
        await session.Gate.WaitAsync(ct);
        try
        {
            done = CityLogic.Advance(session.Record.City, now, out trained);
            if (done != null || trained != null) session.Dirty = true;
            // 在鎖內序列化成快照，避免送出途中狀態被其他操作修改
            snapshot = JsonSerializer.SerializeToUtf8Bytes(session.Record.City, _json);
        }
        finally
        {
            session.Gate.Release();
        }
        await NotifyCompleted(conn, done, trained);
        var city = JsonSerializer.Deserialize<CityState>(snapshot, _json);
        await conn.SendAsync(MessageTypes.CityState, reqId, new CityStateMessage { ServerTimeMs = now, City = city });
    }

    private static async Task NotifyCompleted(WsConnection conn, UpgradeJob? done, TrainingJob? trained)
    {
        if (done != null)
            await conn.SendAsync(MessageTypes.UpgradeCompleted, 0, new UpgradeCompletedMessage { Building = done.Building, Level = done.TargetLevel });
        if (trained != null)
            await conn.SendAsync(MessageTypes.TrainingCompleted, 0, new TrainingCompletedMessage { Troop = trained.Troop, Count = trained.Count });
    }

    private T? Deserialize<T>(JsonElement data) where T : class
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        try { return data.Deserialize<T>(_json); }
        catch (JsonException) { return null; }
    }

    private static string? SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = new string(name.Trim().Where(c => !char.IsControl(c)).ToArray());
        if (trimmed.Length == 0) return null;
        return trimmed.Length > 16 ? trimmed[..16] : trimmed;
    }

    private static Task SendError(IClientConnection conn, int reqId, string code, string detail)
        => conn.SendAsync(MessageTypes.Error, reqId, new ErrorMessage { Code = code, Detail = detail });
}
