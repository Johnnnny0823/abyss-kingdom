using System.Net.WebSockets;
using System.Text.Json;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;

namespace AbyssKingdom.Playtesting;
public sealed class SocketPlayer : IAsyncDisposable
{
    readonly ClientWebSocket _socket = new();
    int _request;
    public CityState City { get; private set; } = null!;
    public async Task LoginAsync(string name)
    {
        _socket.Options.Proxy = null;
        await _socket.ConnectAsync(new Uri("ws://127.0.0.1:5090/ws"), default);
        await SendAsync(MessageTypes.Login, new LoginRequest { Token = "dev:aiplay-" + name, DisplayName = "AI-" + name }, MessageTypes.LoginOk);
        await SendAsync(MessageTypes.CityGet, null, MessageTypes.CityState);
    }
    public async Task<JsonElement> SendAsync(string type, object? data, string? expected = null)
    {
        if (type != MessageTypes.Login && !AiPlaytestPolicy.Allowed.Contains(type)) throw new InvalidOperationException("Intent is not player-visible: " + type);
        int id = ++_request; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type, reqId = id, data }, Json.Options);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
        while (true)
        {
            var buffer = new byte[65536]; using var stream = new MemoryStream(); WebSocketReceiveResult result;
            do { result = await _socket.ReceiveAsync(buffer, timeout.Token); stream.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
            var response = JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
            string received = response.GetProperty("type").GetString()!;
            if (received == MessageTypes.CityState) City = response.GetProperty("data").GetProperty("city").Deserialize<CityState>(Json.Options)!;
            if (response.GetProperty("reqId").GetInt32() == id && (expected == null || received == expected || received == MessageTypes.Error)) return response;
        }
    }
    public async ValueTask DisposeAsync()
    {
        try { using var timeout = new CancellationTokenSource(2000); await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token); } catch { }
        _socket.Dispose();
    }
}
