using System;
using System.Collections.Generic;
using AbyssKingdom.Client.Net;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Protocol;
using Newtonsoft.Json;
using UnityEngine;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// 開發用的城建面板（IMGUI，不需要任何美術素材或場景設定）。
    /// 按下 Play 就會自動出現：輸入伺服器網址與名字 → 登入 → 看資源、升級建築。
    /// 之後正式 UI 做好再把它關掉（把 AutoCreate 改成 false）。
    /// </summary>
    public sealed class CityDebugPanel : MonoBehaviour
    {
        private const bool AutoCreate = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!AutoCreate || FindAnyObjectByType<CityDebugPanel>() != null) return;
            var go = new GameObject("CityDebugPanel");
            DontDestroyOnLoad(go);
            go.AddComponent<CityDebugPanel>();
        }

        private static readonly Dictionary<BuildingType, string> BuildingNames = new Dictionary<BuildingType, string>
        {
            { BuildingType.Keep, "主堡" },
            { BuildingType.Sawmill, "伐木場" },
            { BuildingType.Quarry, "採石場" },
            { BuildingType.Farm, "農田" },
            { BuildingType.CrystalMine, "晶礦場" },
            { BuildingType.Warehouse, "倉庫" },
            { BuildingType.Barracks, "兵營" },
        };

        private static readonly Dictionary<ResourceType, string> ResourceNames = new Dictionary<ResourceType, string>
        {
            { ResourceType.Wood, "木材" },
            { ResourceType.Stone, "石材" },
            { ResourceType.Food, "糧食" },
            { ResourceType.Crystal, "晶礦" },
        };

        private readonly GameConnection _conn = new GameConnection();
        private readonly List<string> _log = new List<string>();

        private string _serverUrl = "ws://127.0.0.1:5080/ws";
        private string _name = "探窟者";
        private string _playerId;
        private CityState _city;          // 伺服器最後送來的權威狀態
        private long _serverOffsetMs;     // 伺服器時間 - 本機時間
        private bool _connecting;
        private Vector2 _scroll;

        private long ServerNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _serverOffsetMs;

        private const string PrefServerUrl = "debug.serverUrl";
        private const string PrefName = "debug.name";

        private void Awake()
        {
            // 記住上次的網址與名字（tunnel 網址很長，玩家不用每次重貼）
            _serverUrl = PlayerPrefs.GetString(PrefServerUrl, _serverUrl);
            _name = PlayerPrefs.GetString(PrefName, _name);
        }

        private void Update()
        {
            while (_conn.TryDequeue(out var msg)) Handle(msg);
        }

        private void Handle(ServerMessage msg)
        {
            switch (msg.Type)
            {
                case MessageTypes.LoginOk:
                    var ok = msg.DataAs<LoginOk>();
                    _playerId = ok.PlayerId;
                    SyncClock(ok.ServerTimeMs);
                    Log($"登入成功：{ok.DisplayName}（{ok.PlayerId}）");
                    break;
                case MessageTypes.CityState:
                    var state = msg.DataAs<CityStateMessage>();
                    SyncClock(state.ServerTimeMs);
                    _city = state.City;
                    break;
                case MessageTypes.UpgradeCompleted:
                    var done = msg.DataAs<UpgradeCompletedMessage>();
                    Log($"升級完成：{BuildingNames[done.Building]} Lv{done.Level}");
                    break;
                case MessageTypes.Error:
                    var err = msg.DataAs<ErrorMessage>();
                    Log($"錯誤：{err.Code} {err.Detail}");
                    break;
            }
        }

        private void SyncClock(long serverMs)
        {
            _serverOffsetMs = serverMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <param name="guest">true：Firebase 訪客登入（伺服器 Auth__Mode=Firebase）；false：開發模式 "dev:名字"。</param>
        private async void Connect(bool guest)
        {
            _connecting = true;
            try
            {
                _serverUrl = _serverUrl.Trim();
                PlayerPrefs.SetString(PrefServerUrl, _serverUrl);
                PlayerPrefs.SetString(PrefName, _name);
                PlayerPrefs.Save();

                string token = "dev:" + _name;
                if (guest)
                {
                    Log("取得訪客帳號…");
                    token = await FirebaseGuestAuth.GetIdTokenAsync();
                }

                Log("連線中…");
                if (!await _conn.ConnectAsync(_serverUrl))
                {
                    Log("連線失敗：" + _conn.LastError);
                    return;
                }
                _conn.Send(MessageTypes.Login, new LoginRequest { Token = token, DisplayName = _name });
            }
            catch (Exception ex)
            {
                Log("登入失敗：" + ex.Message);
            }
            finally
            {
                _connecting = false;
            }
        }

        private void Log(string line)
        {
            _log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {line}");
            if (_log.Count > 30) _log.RemoveAt(_log.Count - 1);
        }

        /// <summary>客戶端預測：用共用邏輯把伺服器狀態推進到「現在」，讓資源與倒數即時跳動。</summary>
        private CityState Predicted()
        {
            if (_city == null) return null;
            var copy = JsonConvert.DeserializeObject<CityState>(JsonConvert.SerializeObject(_city, GameConnection.Settings), GameConnection.Settings);
            CityLogic.Advance(copy, ServerNowMs);
            return copy;
        }

        private void OnGUI()
        {
            float scale = Mathf.Max(1f, Screen.dpi / 160f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(10, 10, Screen.width / scale - 20, Screen.height / scale - 20));
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("<b>深淵王國 — 開發面板</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18 });

            if (_playerId == null)
            {
                GUILayout.Label("伺服器網址");
                _serverUrl = GUILayout.TextField(_serverUrl);
                GUILayout.Label("名字");
                _name = GUILayout.TextField(_name, 16);
                GUI.enabled = !_connecting;
                if (FirebaseGuestAuth.IsConfigured && GUILayout.Button("訪客登入", GUILayout.Height(40))) Connect(guest: true);
                if (GUILayout.Button("開發模式登入（伺服器需為 Dev 模式）", GUILayout.Height(30))) Connect(guest: false);
                GUI.enabled = true;
            }
            else
            {
                DrawCity(Predicted());
            }

            GUILayout.Space(10);
            GUILayout.Label("訊息紀錄");
            foreach (var line in _log) GUILayout.Label(line);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCity(CityState city)
        {
            if (city == null)
            {
                GUILayout.Label("讀取城市中…");
                return;
            }

            long cap = CityConfig.Capacity(city.GetLevel(BuildingType.Warehouse));
            GUILayout.BeginHorizontal();
            foreach (var r in CityConfig.AllResources)
                GUILayout.Label($"{ResourceNames[r]} {city.GetResource(r):N0}/{cap:N0}", GUILayout.Width(150));
            GUILayout.EndHorizontal();

            if (city.Upgrade != null)
            {
                long left = Math.Max(0, city.Upgrade.FinishAtMs - ServerNowMs) / 1000;
                GUILayout.Label($"建造中：{BuildingNames[city.Upgrade.Building]} → Lv{city.Upgrade.TargetLevel}，剩 {left / 60}:{left % 60:00}");
            }
            else
            {
                GUILayout.Label("建築隊列：空閒");
            }

            GUILayout.Space(6);
            foreach (var b in CityConfig.AllBuildings)
            {
                int level = city.GetLevel(b);
                var check = CityLogic.CanUpgrade(city, b);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{BuildingNames[b]}  Lv{level}", GUILayout.Width(140));

                if (level < CityConfig.MaxLevel)
                {
                    var cost = new List<string>();
                    foreach (var r in CityConfig.AllResources)
                    {
                        long c = CityConfig.UpgradeCost(b, level + 1, r);
                        if (c > 0) cost.Add($"{ResourceNames[r]}{c}");
                    }
                    long secs = CityConfig.UpgradeDurationMs(b, level + 1) / 1000;
                    GUILayout.Label($"{string.Join(" ", cost)}  {secs}s", GUILayout.Width(320));
                    GUI.enabled = check == UpgradeResult.Ok;
                    if (GUILayout.Button(check == UpgradeResult.Ok ? "升級" : check.ToString(), GUILayout.Width(150)))
                        _conn.Send(MessageTypes.CityUpgrade, new UpgradeRequest { Building = b });
                    GUI.enabled = true;
                }
                else
                {
                    GUILayout.Label("已達上限");
                }
                GUILayout.EndHorizontal();
            }
        }

        private void OnDestroy() => _conn.Dispose();
    }
}
