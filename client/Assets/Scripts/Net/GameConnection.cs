using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace AbyssKingdom.Client.Net
{
    /// <summary>收到的一則伺服器訊息。</summary>
    public sealed class ServerMessage
    {
        public string Type;
        public int ReqId;
        public JToken Data;

        public T DataAs<T>() => Data == null ? default : Data.ToObject<T>(GameConnection.Serializer);
    }

    /// <summary>
    /// 與遊戲伺服器的 WebSocket 連線。
    /// 網路收發在背景執行緒，收到的訊息放進佇列，由 Unity 主執行緒在 Update 取出（Unity API 只能在主執行緒呼叫）。
    /// 注意：WebGL 不支援 ClientWebSocket，MVP 先支援 Android / PC / 編輯器。
    /// </summary>
    public sealed class GameConnection : IDisposable
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver
            {
                // 字典的鍵（例如資源名稱 Wood）保持原樣，與伺服器一致
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
            },
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        private readonly ConcurrentQueue<ServerMessage> _inbox = new ConcurrentQueue<ServerMessage>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private int _nextReqId = 1;

        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;
        public string LastError { get; private set; }

        public async Task<bool> ConnectAsync(string url)
        {
            Dispose();
            _cts = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            try
            {
                await _socket.ConnectAsync(new Uri(url), _cts.Token);
                _ = Task.Run(() => ReceiveLoop(_socket, _cts.Token));
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        /// <summary>送出請求，回傳 reqId（伺服器的回應會帶相同 reqId）。</summary>
        public int Send(string type, object data = null)
        {
            int reqId = Interlocked.Increment(ref _nextReqId);
            var json = JsonConvert.SerializeObject(new { type, reqId, data }, Settings);
            _ = SendRaw(json);
            return reqId;
        }

        /// <summary>主執行緒呼叫：取出一則已收到的訊息。</summary>
        public bool TryDequeue(out ServerMessage message) => _inbox.TryDequeue(out message);

        private async Task SendRaw(string json)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync();
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    using (var ms = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                LastError = "伺服器關閉連線：" + result.CloseStatusDescription;
                                return;
                            }
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        var obj = JObject.Parse(Encoding.UTF8.GetString(ms.ToArray()));
                        _inbox.Enqueue(new ServerMessage
                        {
                            Type = (string)obj["type"],
                            ReqId = (int?)obj["reqId"] ?? 0,
                            Data = obj["data"],
                        });
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                LastError = ex.Message;
                Debug.LogWarning("[GameConnection] " + ex.Message);
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _socket?.Dispose();
            _socket = null;
        }
    }
}
