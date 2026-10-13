using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AbyssKingdom.Server.Auth;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Server.Net;

/// <summary>包裝一條 WebSocket：送訊息時加鎖（WebSocket 不允許同時多個 Send）。</summary>
public sealed class WsConnection : IClientConnection
{
    public string TelemetryId { get; } = Guid.NewGuid().ToString("N");
    public string? TelemetryPlayerId { get; set; }
    private readonly WebSocket _socket;
    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WsConnection(WebSocket socket, JsonSerializerOptions json)
    {
        _socket = socket;
        _json = json;
    }

    /// <summary>單則訊息（含排隊等前一則送完）最多等這麼久；對方一直不收就斷線，避免拖住全服推進與存檔。</summary>
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    public async Task SendAsync(string type, int reqId, object? data)
    {
        if (_socket.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type, reqId, data }, _json);
        using var timeout = new CancellationTokenSource(SendTimeout);
        try
        {
            await _sendLock.WaitAsync(timeout.Token);
            try
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _socket.Abort(); // 接收迴圈會跟著結束、走正常的下線存檔
            throw new TimeoutException("WebSocket send timed out");
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
    private readonly WorldService _world;
    private readonly AllianceService _alliances;
    private readonly IClock _clock;
    private readonly JsonSerializerOptions _json;
    private readonly ILogger<WebSocketHandler> _log;
    private readonly PlaytestTelemetry _telemetry;

    public WebSocketHandler(SessionManager sessions, IAuthVerifier auth, IClock clock, JsonSerializerOptions json, ILogger<WebSocketHandler> log, WorldService world, AllianceService alliances, PlaytestTelemetry telemetry)
    {
        _sessions = sessions;
        _auth = auth;
        _clock = clock;
        _json = json;
        _log = log;
        _world = world;
        _alliances = alliances;
        _telemetry = telemetry;
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
            if (conn.TelemetryPlayerId != null) _telemetry.End(conn.TelemetryPlayerId, conn.TelemetryId);
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
            case MessageTypes.ClientError:
                var clientError = Deserialize<ClientErrorRequest>(data);
                if (clientError != null) _telemetry.ClientError(session.PlayerId, session.Record.DisplayName, clientError.Message, clientError.Stack);
                break;
            case MessageTypes.FeedbackSend:
                var feedback = Deserialize<FeedbackRequest>(data);
                string? feedbackError = _telemetry.ValidateFeedback(session.PlayerId, feedback?.Text, feedback?.Screen);
                if (feedbackError != null) { await SendError(conn, reqId, ErrorCodes.BadRequest, feedbackError); break; }
                var feedbackCity = await _sessions.ReadCityAsync(session.PlayerId, ct);
                bool accepted = _telemetry.Record(session.PlayerId, session.Record.DisplayName, "feedback", feedbackCity, new()
                {
                    ["text"] = PlaytestTelemetry.Clean(feedback!.Text, 1000), ["screen"] = feedback.Screen,
                    ["sessionPlaySeconds"] = _telemetry.SessionSeconds(session.PlayerId),
                    ["totalPlaySeconds"] = _telemetry.TotalSeconds(session.PlayerId),
                    ["totalPlaySecondsKnown"] = _telemetry.HistoricTotalsKnown
                });
                await conn.SendAsync(MessageTypes.FeedbackReceived, reqId, new FeedbackReceivedMessage { Accepted = accepted });
                break;
            case MessageTypes.AllianceGet:
                try { await SendAlliance(conn, session, reqId, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Alliance read failed for {PlayerId}", session.PlayerId);
                    await SendError(conn, reqId, ErrorCodes.AllianceRejected, AllianceResult.PersistenceFailed.ToString());
                }
                break;
            case MessageTypes.AllianceCreate:
                var createAlliance = Deserialize<AllianceCreateRequest>(data);
                if (createAlliance?.Name == null || createAlliance.Tag == null || createAlliance.OperationId == null)
                { await SendError(conn, reqId, ErrorCodes.BadRequest, "name, tag and operationId required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.CreateAsync(session.PlayerId, session.Record.DisplayName,
                    createAlliance.Name, createAlliance.Tag, createAlliance.OperationId, ct));
                break;
            case MessageTypes.AllianceJoin:
            case MessageTypes.AllianceApply:
                var join = Deserialize<AllianceIdRequest>(data);
                if (string.IsNullOrWhiteSpace(join?.AllianceId)) { await SendError(conn, reqId, ErrorCodes.BadRequest, "allianceId required"); break; }
                int keep = await KeepLevel(session, ct);
                await RunAllianceAction(conn, session, reqId, ct, () => type == MessageTypes.AllianceJoin
                    ? _alliances.JoinAsync(session.PlayerId, session.Record.DisplayName, keep, join.AllianceId, ct)
                    : _alliances.ApplyAsync(session.PlayerId, session.Record.DisplayName, keep, join.AllianceId, ct));
                break;
            case MessageTypes.AllianceReview:
                var review = Deserialize<AllianceReviewRequest>(data);
                if (string.IsNullOrWhiteSpace(review?.PlayerId)) { await SendError(conn, reqId, ErrorCodes.BadRequest, "playerId required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.ReviewAsync(session.PlayerId, review.PlayerId, review.Accept, ct));
                break;
            case MessageTypes.AllianceLeave:
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.LeaveAsync(session.PlayerId, ct));
                break;
            case MessageTypes.AllianceKick:
                var kick = Deserialize<AllianceTargetRequest>(data);
                if (string.IsNullOrWhiteSpace(kick?.PlayerId)) { await SendError(conn, reqId, ErrorCodes.BadRequest, "playerId required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.KickAsync(session.PlayerId, kick.PlayerId, ct));
                break;
            case MessageTypes.AllianceOfficer:
                var officer = Deserialize<AllianceOfficerRequest>(data);
                if (string.IsNullOrWhiteSpace(officer?.PlayerId)) { await SendError(conn, reqId, ErrorCodes.BadRequest, "playerId required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.SetOfficerAsync(session.PlayerId, officer.PlayerId, officer.Officer, ct));
                break;
            case MessageTypes.AllianceTransfer:
                var transfer = Deserialize<AllianceTargetRequest>(data);
                if (string.IsNullOrWhiteSpace(transfer?.PlayerId)) { await SendError(conn, reqId, ErrorCodes.BadRequest, "playerId required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.TransferAsync(session.PlayerId, transfer.PlayerId, ct));
                break;
            case MessageTypes.AllianceDissolve:
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.DissolveAsync(session.PlayerId, ct));
                break;
            case MessageTypes.AllianceJoinMode:
                var joinMode = Deserialize<AllianceJoinModeRequest>(data);
                if (joinMode == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "mode required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.SetJoinModeAsync(session.PlayerId, joinMode.Mode, ct));
                break;
            case MessageTypes.AllianceChatPost:
                var post = Deserialize<AllianceChatPostRequest>(data);
                if (post == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "message required"); break; }
                try
                {
                    var result = await _alliances.PostChatAsync(session.PlayerId, post.OperationId, post.Text, ct);
                    if (result.Result != AllianceResult.Ok)
                        await SendError(conn, reqId, ErrorCodes.AllianceRejected, result.Result.ToString());
                    else
                    {
                        await conn.SendAsync(MessageTypes.AllianceChatMessages, reqId, new AllianceChatMessagesMessage
                        {
                            AllianceId = result.AllianceId,
                            Messages = result.Message == null ? new() : new() { AllianceService.ToView(result.Message) },
                            LatestSequence = result.Message?.Sequence ?? 0
                        });
                        if (result.Message != null)
                            await _alliances.BroadcastChatAsync(result.AllianceId!, result.Message, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Alliance chat post failed for {PlayerId}", session.PlayerId);
                    await SendError(conn, reqId, ErrorCodes.AllianceRejected, AllianceResult.PersistenceFailed.ToString());
                }
                break;
            case MessageTypes.AllianceChatHistory:
                var history = Deserialize<AllianceChatHistoryRequest>(data);
                try
                {
                    var result = await _alliances.ReadChatAsync(session.PlayerId, Math.Max(0, history?.AfterSequence ?? 0), ct);
                    if (result.Result != AllianceResult.Ok)
                        await SendError(conn, reqId, ErrorCodes.AllianceRejected, result.Result.ToString());
                    else await conn.SendAsync(MessageTypes.AllianceChatMessages, reqId, new AllianceChatMessagesMessage
                    {
                        AllianceId = result.AllianceId, Messages = result.Messages.Select(AllianceService.ToView).ToList(),
                        OldestSequence = result.OldestSequence, LatestSequence = result.LatestSequence,
                        FullSnapshot = history == null || history.AfterSequence <= 0 || result.OldestSequence == 0 ||
                            history.AfterSequence < result.OldestSequence - 1
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Alliance chat history failed for {PlayerId}", session.PlayerId);
                    await SendError(conn, reqId, ErrorCodes.AllianceRejected, AllianceResult.PersistenceFailed.ToString());
                }
                break;
            case MessageTypes.AllianceChatReport:
            case MessageTypes.AllianceChatHide:
                var chatTarget = Deserialize<AllianceChatTargetRequest>(data);
                if (chatTarget == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "sequence required"); break; }
                var moderationResult = await RunAllianceAction(conn, session, reqId, ct, () => type == MessageTypes.AllianceChatHide
                    ? _alliances.HideChatAsync(session.PlayerId, chatTarget.Sequence, ct)
                    : _alliances.ReportChatAsync(session.PlayerId, chatTarget.Sequence, ct));
                if (type == MessageTypes.AllianceChatHide && moderationResult == AllianceResult.Ok)
                {
                    try
                    {
                        var visible = await _alliances.ReadChatAsync(session.PlayerId, 0, ct);
                        var hidden = visible.Messages.FirstOrDefault(m => m.Sequence == chatTarget.Sequence && m.Hidden);
                        if (hidden != null && visible.AllianceId != null)
                            await _alliances.BroadcastChatAsync(visible.AllianceId, hidden, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { _log.LogError(ex, "Alliance chat hide broadcast failed for {PlayerId}", session.PlayerId); }
                }
                break;
            case MessageTypes.AllianceChatMute:
                var mute = Deserialize<AllianceChatMuteRequest>(data);
                if (mute == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "target required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.MuteChatAsync(session.PlayerId, mute.PlayerId, mute.Hours, ct));
                break;
            case MessageTypes.AllianceAnnouncement:
                var announcement = Deserialize<AllianceAnnouncementRequest>(data);
                if (announcement == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "text required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.SetAnnouncementAsync(session.PlayerId, announcement.Text, ct));
                break;
            case MessageTypes.AllianceHelpRequest:
                var helpRequest = Deserialize<AllianceHelpRequestMessage>(data);
                if (helpRequest == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "queue required"); break; }
                await RunAllianceAction(conn, session, reqId, ct,
                    () => _alliances.RequestHelpAsync(session.PlayerId, helpRequest.Queue, ct));
                break;
            case MessageTypes.AllianceHelpAll:
                var helpResult = await _alliances.HelpAllAsync(session.PlayerId, ct);
                if (helpResult.Result != AllianceResult.Ok)
                    await SendError(conn, reqId, ErrorCodes.AllianceRejected, helpResult.Result.ToString());
                else
                {
                    await conn.SendAsync(MessageTypes.AllianceHelpResult, reqId, new AllianceHelpResultMessage
                    { Applied = helpResult.Applied, Expired = helpResult.Expired });
                    await SendAlliance(conn, session, 0, ct);
                }
                break;
            case MessageTypes.AllianceRallyCreate:
                var createRally = Deserialize<AllianceRallyCreateRequest>(data);
                if (createRally?.Troops == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "hero, troops, x, y, seconds, operationId required"); break; }
                await SendRallyAction(conn, session, reqId, await _alliances.CreateRallyAsync(session.PlayerId, createRally.Hero,
                    createRally.Troops, createRally.X, createRally.Y, createRally.Seconds, createRally.OperationId, ct), ct);
                break;
            case MessageTypes.AllianceRallyJoin:
                var joinRally = Deserialize<AllianceRallyJoinRequest>(data);
                if (joinRally?.Id == null || joinRally.Troops == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "id, hero, troops, operationId required"); break; }
                await SendRallyAction(conn, session, reqId, await _alliances.JoinRallyAsync(session.PlayerId, joinRally.Id,
                    joinRally.Hero, joinRally.Troops, joinRally.OperationId, ct), ct);
                break;
            case MessageTypes.AllianceRallyRecall:
                var recallRally = Deserialize<AllianceRallyRecallRequest>(data);
                if (recallRally?.Id == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "id required"); break; }
                await RunAllianceAction(conn, session, reqId, ct, () => _alliances.RecallRallyAsync(session.PlayerId, recallRally.Id, ct));
                break;
            case MessageTypes.AllianceFlagPlace:
                var placeFlag = Deserialize<AllianceFlagPlaceRequest>(data);
                if (placeFlag == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "slot, x, y, operationId required"); break; }
                var placeFlagResult = await _alliances.PlaceFlagAsync(session.PlayerId, placeFlag.Slot,
                    placeFlag.X, placeFlag.Y, placeFlag.OperationId, ct);
                if (placeFlagResult == FlagResult.Ok) await SendAlliance(conn, session, reqId, ct);
                else await SendError(conn, reqId, ErrorCodes.AllianceRejected, "Flag:" + placeFlagResult);
                break;
            case MessageTypes.AllianceFlagRemove:
                var removeFlag = Deserialize<AllianceFlagRemoveRequest>(data);
                if (removeFlag == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "slot required"); break; }
                var removeFlagResult = await _alliances.RemoveFlagAsync(session.PlayerId, removeFlag.Slot, ct);
                if (removeFlagResult == FlagResult.Ok) await SendAlliance(conn, session, reqId, ct);
                else await SendError(conn, reqId, ErrorCodes.AllianceRejected, "Flag:" + removeFlagResult);
                break;
            case MessageTypes.WorldTerritory:
                var territoryDirectory = await _alliances.SnapshotAsync(ct);
                await conn.SendAsync(MessageTypes.WorldTerritory, reqId, new WorldTerritoryMessage
                {
                    Version = territoryDirectory.Version, ServerTimeMs = _clock.NowMs,
                    Flags = territoryDirectory.Alliances.Where(a => !a.PendingCreate).SelectMany(a => a.Flags.Where(f => f.Live)
                        .Select(f => new FlagMapView { Id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(f.Id))), AllianceId = a.Id, Tag = a.Tag,
                            X = f.X, Y = f.Y, Paid = f.Paid, CompletesAtMs = f.CompletesAtMs })).ToList()
                });
                break;
            case MessageTypes.AllianceReinforcementSend:
                var reinforcementSend = Deserialize<AllianceReinforcementSendRequest>(data);
                if (reinforcementSend?.PlayerId == null || reinforcementSend.Troops == null || reinforcementSend.OperationId == null)
                { await SendError(conn, reqId, ErrorCodes.BadRequest, "playerId, hero, troops and operationId required"); break; }
                var reinforcementResult = await _alliances.DispatchReinforcementAsync(session.PlayerId, reinforcementSend.PlayerId,
                    reinforcementSend.Hero, reinforcementSend.Troops, reinforcementSend.OperationId, ct);
                if (reinforcementResult.Result != AllianceResult.Ok)
                    await SendError(conn, reqId, ErrorCodes.AllianceRejected,
                        reinforcementResult.Result == AllianceResult.MilitaryRejected
                            ? "Military:" + reinforcementResult.MilitaryResult : reinforcementResult.Result.ToString());
                else
                {
                    await SendAlliance(conn, session, reqId, ct);
                    await _alliances.NotifyReinforcementAsync(reinforcementResult.Id!, ct);
                }
                break;
            case MessageTypes.AllianceReinforcementReturn:
                var reinforcementReturn = Deserialize<AllianceReinforcementReturnRequest>(data);
                if (reinforcementReturn?.Id == null)
                { await SendError(conn, reqId, ErrorCodes.BadRequest, "id required"); break; }
                if (await RunAllianceAction(conn, session, reqId, ct,
                    () => _alliances.ReturnReinforcementAsync(session.PlayerId, reinforcementReturn.Id, ct)) == AllianceResult.Ok)
                    await _alliances.NotifyReinforcementAsync(reinforcementReturn.Id, ct);
                break;
            case MessageTypes.AllianceReinforcementAccept:
                var reinforcementAccept = Deserialize<AllianceReinforcementAcceptRequest>(data);
                if (reinforcementAccept == null)
                { await SendError(conn, reqId, ErrorCodes.BadRequest, "accept required"); break; }
                await RunAllianceAction(conn, session, reqId, ct,
                    () => _alliances.SetReinforcementAcceptanceAsync(session.PlayerId, reinforcementAccept.Accept, ct));
                break;
            case MessageTypes.CityGet:
                await SendCity(conn, session, reqId, ct);
                break;

            case MessageTypes.CityUpgrade:
                var up = Deserialize<UpgradeRequest>(data);
                if (up == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "building required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.UpgradeRejected, ct, (city, now) =>
                    Fail(CityLogic.StartUpgrade(city, up.Building, now), UpgradeResult.Ok));
                break;

            case MessageTypes.CityMove:
                var move = Deserialize<MoveBuildingRequest>(data);
                if (move == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "building, x, y required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.MoveRejected, ct, (city, now) =>
                    Fail(CityLayout.Move(city, move.Building, move.X, move.Y), MoveResult.Ok));
                break;

            case MessageTypes.CityHeal:
                var heal = Deserialize<HealRequest>(data);
                if (heal?.Troops == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "troops required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.HealRejected, ct, (city, now) =>
                    Fail(CityLogic.StartHealing(city, heal.Troops, now), HealResult.Ok));
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
                await SyncHelpAfterQueueAction(session, cancel.Queue, ct);
                break;

            case MessageTypes.HeroLevelUp:
                var lv = Deserialize<HeroLevelUpRequest>(data);
                if (lv == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "hero and item required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.HeroRejected, ct, (city, now) =>
                    Fail(HeroLogic.UseExpItem(city, lv.Hero, lv.Item, lv.Count), HeroResult.Ok));
                break;

            case MessageTypes.GearEquip:
                var eq = Deserialize<GearEquipRequest>(data);
                if (eq == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "gear and hero required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.GearRejected, ct, (city, now) =>
                    Fail(GearLogic.Equip(city, eq.Gear, eq.Hero), GearResult.Ok));
                break;

            case MessageTypes.GearUnequip:
                var uneq = Deserialize<GearUnequipRequest>(data);
                if (uneq == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "gear required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.GearRejected, ct, (city, now) =>
                    Fail(GearLogic.Unequip(city, uneq.Gear), GearResult.Ok));
                break;

            case MessageTypes.ExpeditionBattle:
                var battle = Deserialize<ExpeditionBattleRequest>(data);
                if (battle == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "stage and teams required"); break; }
                await HandleExpeditionBattle(conn, session, reqId, battle, ct);
                break;

            case MessageTypes.ExpeditionClaimIdle:
                await HandleClaimIdle(conn, session, reqId, ct);
                break;

            case MessageTypes.DomainUseScroll:
                var scroll = Deserialize<DomainScrollRequest>(data);
                if (scroll == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "hero and item required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.DomainRejected, ct, (city, now) =>
                    Fail(DomainLogic.UseScroll(city, scroll.Hero, scroll.Item, scroll.Count), DomainResult.Ok));
                break;

            case MessageTypes.WorldCities:
                var directoryForPins = await _alliances.SnapshotAsync(ct);
                var allianceTags = directoryForPins.Alliances.Where(a => !a.PendingCreate)
                    .SelectMany(a => a.Members.Select(m => (m.PlayerId, a.Tag)))
                    .ToDictionary(x => x.PlayerId, x => x.Tag);
                var alliedPlayers = directoryForPins.Alliances.Where(a => !a.PendingCreate && a.Members.Any(m => m.PlayerId == session.PlayerId))
                    .SelectMany(a => a.Members.Select(m => m.PlayerId)).ToHashSet(StringComparer.Ordinal);
                await conn.SendAsync(MessageTypes.WorldCities, reqId, new WorldCitiesMessage
                { Cities = _world.Pins(session.PlayerId, _clock.NowMs, id => allianceTags.TryGetValue(id, out var tag) ? tag : null, alliedPlayers.Contains) });
                break;

            case MessageTypes.WorldCamps:
                await conn.SendAsync(MessageTypes.WorldCamps, reqId, new WorldCampsMessage { Camps = _world.DefeatedCamps(_clock.NowMs) });
                break;

            case MessageTypes.WorldTile:
                var tileReq = Deserialize<TileInfoRequest>(data);
                if (tileReq == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "x and y required"); break; }
                await conn.SendAsync(MessageTypes.WorldTile, reqId, _world.TileInfo(tileReq.X, tileReq.Y, session.PlayerId, _clock.NowMs));
                break;

            case MessageTypes.MapMarch:
                var march = Deserialize<MapMarchRequest>(data);
                if (march == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "hero, troops and destination required"); break; }
                var targetOwner = _world.CityOwnerAt(SurfaceLogic.Tile(march.X, march.Y));
                if (targetOwner != null && await _alliances.SameAllianceAsync(session.PlayerId, targetOwner, ct))
                { await SendError(conn, reqId, ErrorCodes.MapRejected, "AlliedCity"); break; }
                var startEvents = new List<MarchEvent>();
                await RunCityAction(conn, session, reqId, ErrorCodes.MapRejected, ct, (city, now) =>
                {
                    // 先推進並收下事件（其他行軍剛好抵達敵城等），Start 內部的推進就不會把它們吞掉
                    startEvents = _sessions.AdvanceMarches(session, now);
                    return Fail(MarchLogic.Start(city, march.Hero, march.Troops, march.X, march.Y, now, _world, session.PlayerId), MarchResult.Ok);
                });
                await _sessions.DispatchMarchEventsAsync(session, startEvents, ct);
                break;

            case MessageTypes.MapRecall:
                var recall = Deserialize<MapRecallRequest>(data);
                if (recall == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "march required"); break; }
                var recallSnapshot = await _sessions.ReadCityAsync(session.PlayerId, ct);
                var rallyMarch = recallSnapshot?.Surface.Marches.FirstOrDefault(m => m.Id == recall.March && m.RallyId != null);
                if (rallyMarch != null)
                {
                    await RunAllianceAction(conn, session, reqId, ct, () => _alliances.RecallRallyAsync(session.PlayerId, rallyMarch.RallyId, ct));
                    break;
                }
                var reinforcementMarch = recallSnapshot?.Surface.Marches.FirstOrDefault(m => m.Id == recall.March && m.ReinforcementOperationId != null);
                if (reinforcementMarch != null)
                {
                    if (await RunAllianceAction(conn, session, reqId, ct, () => _alliances.ReturnReinforcementAsync(
                        session.PlayerId, reinforcementMarch.ReinforcementOperationId, ct)) == AllianceResult.Ok)
                        await _alliances.NotifyReinforcementAsync(reinforcementMarch.ReinforcementOperationId, ct);
                    break;
                }
                var recallEvents = new List<MarchEvent>();
                await RunCityAction(conn, session, reqId, ErrorCodes.MapRejected, ct, (city, now) =>
                {
                    recallEvents = _sessions.AdvanceMarches(session, now);
                    return Fail(MarchLogic.Recall(city, recall.March, now, _world, session.PlayerId), MarchResult.Ok);
                });
                await _sessions.DispatchMarchEventsAsync(session, recallEvents, ct);
                break;

            case MessageTypes.QuestClaim:
                await RunCityAction(conn, session, reqId, ErrorCodes.QuestRejected, ct, (city, now) =>
                    Fail(QuestLogic.Claim(city), QuestResult.Ok));
                break;

            case MessageTypes.HeroStarUp:
                var star = Deserialize<HeroStarUpRequest>(data);
                if (star == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "hero required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.HeroRejected, ct, (city, now) =>
                    Fail(StarLogic.StarUp(city, star.Hero), StarResult.Ok));
                break;

            case MessageTypes.DomainTrial:
                var trial = Deserialize<DomainTrialRequest>(data);
                if (trial == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "hero and domain required"); break; }
                await HandleDomainTrial(conn, session, reqId, trial, ct);
                break;

            case MessageTypes.CitySpeedup:
                var speed = Deserialize<SpeedupRequest>(data);
                if (speed == null) { await SendError(conn, reqId, ErrorCodes.BadRequest, "queue required"); break; }
                await RunCityAction(conn, session, reqId, ErrorCodes.SpeedupRejected, ct, (city, now) =>
                    Fail(speed.Item == null
                        ? CityLogic.SpeedupFree(city, speed.Queue, now)
                        : CityLogic.UseSpeedupItem(city, speed.Queue, speed.Item.Value, speed.Count, now), SpeedupResult.Ok));
                await SyncHelpAfterQueueAction(session, speed.Queue, ct);
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
        conn.TelemetryPlayerId = newSession.PlayerId;
        _telemetry.Start(newSession.PlayerId, newSession.Record.DisplayName, conn.TelemetryId);
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
        HealingJob? healed;
        string? error;
        await session.Gate.WaitAsync(ct);
        try
        {
            var now = _clock.NowMs;
            done = CityLogic.Advance(session.Record.City, now, out trained, out healed);
            var beforeUpgrade = session.Record.City.Upgrade;
            int beforeQuest = session.Record.City.QuestIndex;
            error = action(session.Record.City, now);
            if (error == null)
            {
                var city = session.Record.City;
                if (beforeUpgrade == null && city.Upgrade != null)
                    _telemetry.Record(session.PlayerId, session.Record.DisplayName, "building.upgrade.start", city,
                        new() { ["building"] = city.Upgrade.Building.ToString(), ["targetLevel"] = city.Upgrade.TargetLevel });
                if (city.QuestIndex > beforeQuest)
                    _telemetry.Record(session.PlayerId, session.Record.DisplayName, "quest.claim", city, new() { ["questNumber"] = beforeQuest + 1 });
                _telemetry.Observe(session.PlayerId, session.Record.DisplayName, city);
            }
            if (done != null || trained != null || healed != null || error == null) session.Dirty = true;
        }
        finally
        {
            session.Gate.Release();
        }

        await NotifyCompleted(conn, done, trained, healed);
        if (error != null) await SendError(conn, reqId, errorCode, error);
        else await SendCity(conn, session, reqId, ct);
    }

    private async Task HandleExpeditionBattle(WsConnection conn, PlayerSession session, int reqId, ExpeditionBattleRequest req, CancellationToken ct)
    {
        ExpeditionCode code;
        ExpeditionResult? result;
        // 種子由伺服器決定；客戶端無法預先知道或選擇
        ulong seed = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        var teams = (req.Teams ?? new()).Select(t => (IList<HeroId>)(t ?? new())).ToList();
        await session.Gate.WaitAsync(ct);
        try
        {
            code = ExpeditionLogic.Fight(session.Record.City, req.Stage, teams, seed, _clock.NowMs, out result);
            if (code == ExpeditionCode.Ok) session.Dirty = true;
        }
        finally
        {
            session.Gate.Release();
        }
        if (code != ExpeditionCode.Ok || result == null)
        {
            await SendError(conn, reqId, ErrorCodes.ExpeditionRejected, code.ToString());
            return;
        }
        await conn.SendAsync(MessageTypes.ExpeditionResultMessage, reqId, result);
        await SendCity(conn, session, 0, ct);
    }

    private async Task HandleDomainTrial(WsConnection conn, PlayerSession session, int reqId, DomainTrialRequest req, CancellationToken ct)
    {
        DomainResult code;
        DomainTrialResult? result;
        ulong seed = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        await session.Gate.WaitAsync(ct);
        try
        {
            code = DomainLogic.Trial(session.Record.City, req.Hero, req.Domain, seed, out result);
            if (code == DomainResult.Ok) session.Dirty = true;
        }
        finally
        {
            session.Gate.Release();
        }
        if (code != DomainResult.Ok || result == null)
        {
            await SendError(conn, reqId, ErrorCodes.DomainRejected, code.ToString());
            return;
        }
        await conn.SendAsync(MessageTypes.DomainTrialResultMessage, reqId, result);
        await SendCity(conn, session, 0, ct);
    }

    private async Task HandleClaimIdle(WsConnection conn, PlayerSession session, int reqId, CancellationToken ct)
    {
        ExpeditionCode code;
        ExpeditionRewards rewards;
        await session.Gate.WaitAsync(ct);
        try
        {
            code = ExpeditionLogic.ClaimIdle(session.Record.City, _clock.NowMs, out rewards);
            if (code == ExpeditionCode.Ok) session.Dirty = true;
        }
        finally
        {
            session.Gate.Release();
        }
        if (code != ExpeditionCode.Ok)
        {
            await SendError(conn, reqId, ErrorCodes.ExpeditionRejected, code.ToString());
            return;
        }
        await conn.SendAsync(MessageTypes.ExpeditionIdleClaimed, reqId, rewards);
        await SendCity(conn, session, 0, ct);
    }

    /// <summary>送出最新城市狀態；推進時若有工程完成（例如剛被加速），先推播完成通知。</summary>
    private async Task SendCity(WsConnection conn, PlayerSession session, int reqId, CancellationToken ct)
    {
        byte[] snapshot;
        UpgradeJob? done;
        TrainingJob? trained;
        HealingJob? healed;
        var now = _clock.NowMs;
        await session.Gate.WaitAsync(ct);
        try
        {
            done = CityLogic.Advance(session.Record.City, now, out trained, out healed);
            if (done != null || trained != null || healed != null) session.Dirty = true;
            // 在鎖內序列化成快照，避免送出途中狀態被其他操作修改
            snapshot = JsonSerializer.SerializeToUtf8Bytes(session.Record.City, _json);
        }
        finally
        {
            session.Gate.Release();
        }
        await NotifyCompleted(conn, done, trained, healed);
        var city = JsonSerializer.Deserialize<CityState>(snapshot, _json);
        await conn.SendAsync(MessageTypes.CityState, reqId, new CityStateMessage { ServerTimeMs = now, City = city });
    }

    private static async Task NotifyCompleted(WsConnection conn, UpgradeJob? done, TrainingJob? trained, HealingJob? healed)
    {
        if (done != null)
            await conn.SendAsync(MessageTypes.UpgradeCompleted, 0, new UpgradeCompletedMessage { Building = done.Building, Level = done.TargetLevel });
        if (trained != null)
            await conn.SendAsync(MessageTypes.TrainingCompleted, 0, new TrainingCompletedMessage { Troop = trained.Troop, Count = trained.Count });
        if (healed != null)
            await conn.SendAsync(MessageTypes.HealingCompleted, 0, new HealingCompletedMessage { Troops = healed.Troops });
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

    private static async Task<int> KeepLevel(PlayerSession session, CancellationToken ct)
    {
        await session.Gate.WaitAsync(ct);
        try { return session.Record.City.GetLevel(BuildingType.Keep); }
        finally { session.Gate.Release(); }
    }

    private async Task SyncHelpAfterQueueAction(PlayerSession session, QueueType queue, CancellationToken ct)
    {
        if (queue != QueueType.Building && queue != QueueType.Healing) return;
        long queueId, finish;
        await session.Gate.WaitAsync(ct);
        try
        {
            var city = session.Record.City;
            var progress = queue == QueueType.Building ? city.Upgrade?.Help : city.Healing?.Help;
            queueId = progress?.QueueId ?? 0;
            finish = queue == QueueType.Building ? city.Upgrade?.FinishAtMs ?? 0 : city.Healing?.FinishAtMs ?? 0;
        }
        finally { session.Gate.Release(); }
        try { await _alliances.SyncOwnerHelpAsync(session.PlayerId, queue, queueId, finish, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _log.LogError(ex, "Alliance help request sync failed for {PlayerId}", session.PlayerId); }
    }

    private async Task<AllianceResult> RunAllianceAction(WsConnection conn, PlayerSession session, int reqId, CancellationToken ct,
        Func<Task<AllianceResult>> action)
    {
        try
        {
            var result = await action();
            if (result == AllianceResult.Ok) await SendAlliance(conn, session, reqId, ct);
            else await SendError(conn, reqId, ErrorCodes.AllianceRejected, result.ToString());
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Alliance action failed for {PlayerId}", session.PlayerId);
            await SendError(conn, reqId, ErrorCodes.AllianceRejected, AllianceResult.PersistenceFailed.ToString());
            return AllianceResult.PersistenceFailed;
        }
    }

    private async Task SendRallyAction(WsConnection conn, PlayerSession session, int reqId, ReinforcementActionResult result, CancellationToken ct)
    {
        if (result.Result == AllianceResult.Ok) await SendAlliance(conn, session, reqId, ct);
        else await SendError(conn, reqId, ErrorCodes.AllianceRejected, result.Result == AllianceResult.MilitaryRejected
            ? "Military:" + result.MilitaryResult : result.Result.ToString());
    }
    private async Task SendAlliance(WsConnection conn, PlayerSession session, int reqId, CancellationToken ct)
    {
        var directory = await _alliances.SnapshotAsync(ct);
        foreach (var alliance in directory.Alliances.Where(a => !a.PendingCreate))
            foreach (var member in alliance.Members)
                _telemetry.Record(member.PlayerId, member.DisplayName, "first.alliance", atMs: member.JoinedAtMs > 0 ? member.JoinedAtMs : null);
        var pending = directory.Alliances.FirstOrDefault(a => a.PendingCreate && a.Members.Any(m => m.PlayerId == session.PlayerId));
        var mine = directory.Alliances.FirstOrDefault(a => !a.PendingCreate && a.Members.Any(m => m.PlayerId == session.PlayerId));
        var role = mine?.Members.FirstOrDefault(m => m.PlayerId == session.PlayerId)?.Role;
        var result = new AllianceStateMessage
        {
            Version = directory.Version, ServerTimeMs = _clock.NowMs,
            GuideProgress = directory.GuideProgress.GetValueOrDefault(session.PlayerId) | (mine != null ? AllianceConfig.GuideJoined : 0),
            Alliances = directory.Alliances.Where(a => !a.PendingCreate).Select(a => new AllianceSummary
            { Id = a.Id, Name = a.Name, Tag = a.Tag, Members = a.Members.Count, JoinMode = a.JoinMode }).ToList(),
            MyAllianceId = mine?.Id,
            MyRole = role,
            Members = mine?.Members.Select(m => new AllianceMemberView
            { PlayerId = m.PlayerId, DisplayName = m.DisplayName, Role = m.Role, KeepLevel = _world.KeepLevelOf(m.PlayerId, _clock.NowMs) }).ToList() ?? new(),
            Applications = role is AllianceRole.Leader or AllianceRole.Officer
                ? mine!.Applications.Where(a => a.ExpiresAtMs > _clock.NowMs).Select(a => new AllianceApplicationView
                { PlayerId = a.PlayerId, DisplayName = a.DisplayName, ExpiresAtMs = a.ExpiresAtMs }).ToList()
                : new(),
            RejoinAfterMs = directory.RejoinAfterMs.TryGetValue(session.PlayerId, out long until) ? until : 0,
            PendingCreateOperationId = pending?.CreateOperationId.StartsWith("alliance:create:" + session.PlayerId + ":") == true
                ? pending.CreateOperationId.Substring(("alliance:create:" + session.PlayerId + ":").Length) : null,
            PendingCreateName = pending?.Name,
            PendingCreateTag = pending?.Tag,
            Announcement = mine?.Announcement,
            Flags = mine?.Flags ?? new(),
            Rallies = directory.Rallies.Where(r => r.AllianceId == mine?.Id || r.LeaderId == session.PlayerId ||
                r.FrozenInputs.Any(p => p.OwnerId == session.PlayerId) || r.Participants.Any(p => p.OwnerId == session.PlayerId && !p.Returned))
                .OrderByDescending(r => r.DepartAtMs).Take(30).Select(r =>
                {
                    var target = SurfaceLogic.FromTile(r.TargetTile);
                    return new RallyView { Id = r.Id, LeaderId = r.LeaderId, LeaderName = _world.NameOf(r.LeaderId),
                        X = target.x, Y = target.y, Level = r.Level, Capacity = r.Capacity, DepartAtMs = r.DepartAtMs,
                        ArriveAtMs = r.Times.Count == 0 ? 0 : r.Times[^1], State = r.State.ToString(),
                        Seconds = r.Seconds,
                        Outcome = r.Cancelled ? "Cancelled" : r.Outcome == null ? null : r.Outcome.CampGone ? "CampGone" : r.Outcome.Won ? "Victory" : "Defeat",
                        Participants = r.Participants.Where(p => !p.Left || !p.Returned).Select(p =>
                        {
                            var outcome = r.Outcome?.Participants.FirstOrDefault(o => o.OwnerId == p.OwnerId);
                            return new RallyParticipantView { OwnerId = p.OwnerId, Name = _world.NameOf(p.OwnerId), Hero = p.Hero,
                                Count = outcome == null ? p.Troops.Values.Sum() : outcome.Survivors.Values.Sum(), Paid = p.Paid, Returned = p.Returned,
                                OperationId = p.OwnerId == session.PlayerId ? p.JoinId.Substring(p.OwnerId.Length + 1) : null,
                                Troops = p.OwnerId == session.PlayerId ? new(p.Troops) : new(),
                                Contribution = outcome?.Contribution ?? 0, Resources = outcome?.Resources ?? new(), Items = outcome?.Items ?? new(), Losses = outcome?.Losses ?? new() };
                        }).ToList() };
                }).ToList(),
            OnlineMembers = mine == null ? 0 : _sessions.CountOnline(mine.Members.Select(m => m.PlayerId)),
            HelpRequests = mine?.HelpRequests.Where(r => r.ExpiresAtMs > _clock.NowMs && r.UsedCount < r.MaxCount).Select(r => new AllianceHelpView
            {
                OwnerId = r.OwnerId, OwnerName = r.OwnerName, Queue = r.Queue,
                QueueId = r.QueueId, MaxCount = r.MaxCount, UsedCount = r.UsedCount
            }).ToList() ?? new(),
            Reinforcements = directory.Reinforcements.Where(r =>
                (r.SenderId == session.PlayerId || r.ReceiverId == session.PlayerId) &&
                r.State is not (ReinforcementState.Returned or ReinforcementState.Rejected)).Select(r => new AllianceReinforcementView
            {
                Id = r.Id, SenderId = r.SenderId, SenderName = _world.NameOf(r.SenderId),
                ReceiverId = r.ReceiverId, ReceiverName = _world.NameOf(r.ReceiverId), Hero = r.Hero,
                Troops = new(r.State == ReinforcementState.Reserved ? r.Troops : r.CurrentTroops), State = r.State, MarchId = r.MarchId, ArriveAtMs = r.ArriveAtMs,
                ReturnRequested = r.ReturnRequested, BattleFrozen = r.SettlementId != null
            }).ToList()
        };
        await conn.SendAsync(MessageTypes.AllianceState, reqId, result);
    }
}
