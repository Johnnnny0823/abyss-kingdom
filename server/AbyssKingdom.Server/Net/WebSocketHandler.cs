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
                await HandleUpgrade(conn, session, reqId, data, ct);
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

    private async Task HandleUpgrade(WsConnection conn, PlayerSession session, int reqId, JsonElement data, CancellationToken ct)
    {
        var req = Deserialize<UpgradeRequest>(data);
        if (req == null)
        {
            await SendError(conn, reqId, ErrorCodes.BadRequest, "building required");
            return;
        }

        UpgradeResult result;
        await session.Gate.WaitAsync(ct);
        try
        {
            result = CityLogic.StartUpgrade(session.Record.City, req.Building, _clock.NowMs);
            if (result == UpgradeResult.Ok) session.Dirty = true;
        }
        finally
        {
            session.Gate.Release();
        }

        if (result != UpgradeResult.Ok)
        {
            await SendError(conn, reqId, ErrorCodes.UpgradeRejected, result.ToString());
            return;
        }
        await SendCity(conn, session, reqId, ct);
    }

    private async Task SendCity(WsConnection conn, PlayerSession session, int reqId, CancellationToken ct)
    {
        byte[] snapshot;
        var now = _clock.NowMs;
        await session.Gate.WaitAsync(ct);
        try
        {
            CityLogic.Advance(session.Record.City, now);
            // 在鎖內序列化成快照，避免送出途中狀態被其他操作修改
            snapshot = JsonSerializer.SerializeToUtf8Bytes(session.Record.City, _json);
        }
        finally
        {
            session.Gate.Release();
        }
        var city = JsonSerializer.Deserialize<CityState>(snapshot, _json);
        await conn.SendAsync(MessageTypes.CityState, reqId, new CityStateMessage { ServerTimeMs = now, City = city });
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
