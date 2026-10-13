using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Shared.Protocol;
using UnityEngine;

namespace AbyssKingdom.Client.Game
{
    public sealed class ClientErrorReporter : MonoBehaviour
    {
        private readonly Dictionary<string, float> _last = new Dictionary<string, float>();
        private int _sent;
        private bool _sending;
        private GameClient _client;
        private void Start()
        {
            _client = GameClient.Instance;
            if (_client == null) return;
            _client.LoginCompleted += ResetLimit;
            Application.logMessageReceived += OnLog;
        }
        private void ResetLimit() { _sent = 0; _last.Clear(); }
        private void OnLog(string message, string stack, LogType type)
        {
            if (_sending || _client == null || !_client.IsLoggedIn || _sent >= 20 ||
                (type != LogType.Error && type != LogType.Exception)) return;
            string clean = PlaytestReports.Sanitize(message, 1200);
            float now = Time.realtimeSinceStartup;
            if (_last.TryGetValue(clean, out var last) && now - last < 60) return;
            _last[clean] = now; _sent++;
            _sending = true;
            try { _client.ReportClientError(clean, PlaytestReports.Sanitize(string.Join("\n", (stack ?? "").Split('\n').Take(5)), 800)); }
            finally { _sending = false; }
        }
        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (_client != null) _client.LoginCompleted -= ResetLimit;
        }
    }
}
