using System;
using System.Collections.Generic;
using AbyssKingdom.Client.Net;
using AbyssKingdom.Client.UI;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Protocol;
using Newtonsoft.Json;
using UnityEngine;

namespace AbyssKingdom.Client.Game
{
    /// <summary>
    /// 客戶端的遊戲狀態與連線：送出玩家「意圖」、接收伺服器的權威狀態。介面（GameUI）只讀這裡的資料。
    /// 命令列參數（測試用）：-server &lt;wss 網址&gt; 指定伺服器；-autologin 啟動後自動訪客登入；-devlogin 自動開發模式登入。
    /// </summary>
    public sealed class GameClient : MonoBehaviour
    {
        public const string DefaultServerUrl = "ws://127.0.0.1:5080/ws";
        private const string PrefServerUrl = "debug.serverUrl";
        private const string PrefName = "debug.name";

        public static GameClient Instance { get; private set; }

        private readonly GameConnection _conn = new GameConnection();
        private long _serverOffsetMs;
        private bool _urlFromArgs; // 命令列指定的網址不寫回玩家設定

        public string ServerUrl { get; set; }
        public string PlayerName { get; set; }
        public string PlayerId { get; private set; }
        public bool IsLoggedIn => PlayerId != null;
        public bool IsBusy { get; private set; }

        /// <summary>伺服器最後送來的權威城市狀態。</summary>
        public CityState City { get; private set; }

        /// <summary>最近一次遠征結果與當時的編隊（介面用來顯示戰報）。</summary>
        public ExpeditionResult LastExpedition { get; private set; }
        public List<List<HeroId>> LastExpeditionTeams { get; private set; }
        private List<List<HeroId>> _pendingTeams;

        public long ServerNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _serverOffsetMs;

        /// <summary>給玩家看的通知（訊息, 是否為錯誤）。</summary>
        public event Action<string, bool> Notice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("GameClient");
            DontDestroyOnLoad(go);
            go.AddComponent<GameClient>();
            GameUI.Create();
        }

        private void Awake()
        {
            Instance = this;
            ServerUrl = PlayerPrefs.GetString(PrefServerUrl, DefaultServerUrl);
            PlayerName = PlayerPrefs.GetString(PrefName, "新任領主");

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-server" && i + 1 < args.Length) { ServerUrl = args[i + 1]; _urlFromArgs = true; }
                if (args[i] == "-autologin") Invoke(nameof(AutoLogin), 0.5f);
                if (args[i] == "-devlogin") Invoke(nameof(DevLogin), 0.5f);
            }
        }

        private void AutoLogin() => Login(guest: FirebaseGuestAuth.IsConfigured);

        private void DevLogin() => Login(guest: false);

        private void Update()
        {
            while (_conn.TryDequeue(out var msg)) Handle(msg);

            if (IsLoggedIn && !_conn.IsConnected)
            {
                PlayerId = null;
                City = null;
                Notice?.Invoke("與伺服器的連線中斷：" + _conn.LastError, true);
            }
        }

        private void OnDestroy() => _conn.Dispose();

        // ───── 玩家意圖 ─────

        public async void Login(bool guest)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                ServerUrl = (ServerUrl ?? "").Trim();
                if (!_urlFromArgs) PlayerPrefs.SetString(PrefServerUrl, ServerUrl);
                PlayerPrefs.SetString(PrefName, PlayerName ?? "");
                PlayerPrefs.Save();

                string token = "dev:" + PlayerName;
                if (guest) token = await FirebaseGuestAuth.GetIdTokenAsync();

                if (!await _conn.ConnectAsync(ServerUrl))
                {
                    Notice?.Invoke("連線失敗：" + _conn.LastError, true);
                    return;
                }
                _conn.Send(MessageTypes.Login, new LoginRequest { Token = token, DisplayName = PlayerName });
            }
            catch (Exception ex)
            {
                Notice?.Invoke("登入失敗：" + ex.Message, true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Upgrade(BuildingType building) => _conn.Send(MessageTypes.CityUpgrade, new UpgradeRequest { Building = building });

        public void MoveBuilding(BuildingType building, int x, int y) =>
            _conn.Send(MessageTypes.CityMove, new MoveBuildingRequest { Building = building, X = x, Y = y });

        public void Train(TroopType troop, long count) => _conn.Send(MessageTypes.CityTrain, new TrainRequest { Troop = troop, Count = count });

        public void Cancel(QueueType queue) => _conn.Send(MessageTypes.CityCancel, new CancelRequest { Queue = queue });

        public void LevelUpHero(HeroId hero, ItemType item, long count = 1) =>
            _conn.Send(MessageTypes.HeroLevelUp, new HeroLevelUpRequest { Hero = hero, Item = item, Count = count });

        public void EquipGear(long uid, HeroId hero) => _conn.Send(MessageTypes.GearEquip, new GearEquipRequest { Gear = uid, Hero = hero });

        public void UnequipGear(long uid) => _conn.Send(MessageTypes.GearUnequip, new GearUnequipRequest { Gear = uid });

        public void ExpeditionBattle(int stage, List<List<HeroId>> teams)
        {
            _pendingTeams = teams;
            _conn.Send(MessageTypes.ExpeditionBattle, new ExpeditionBattleRequest { Stage = stage, Teams = teams });
        }

        public void ClaimExpeditionIdle() => _conn.Send(MessageTypes.ExpeditionClaimIdle);

        public void StartMarch(HeroId hero, Dictionary<TroopType, long> troops, int destination) =>
            _conn.Send(MessageTypes.MapMarch, new MapMarchRequest { Hero = hero, Troops = troops, Destination = destination });

        /// <summary>地表所有主城（定期向伺服器要一次）。</summary>
        public List<CityPin> Cities { get; private set; } = new List<CityPin>();

        public void RequestCities() => _conn.Send(MessageTypes.WorldCities);

        public void RecallMarch(long march) => _conn.Send(MessageTypes.MapRecall, new MapRecallRequest { March = march });

        public void StarUp(HeroId hero) => _conn.Send(MessageTypes.HeroStarUp, new HeroStarUpRequest { Hero = hero });

        public void UseDomainScroll(HeroId hero, ItemType item, long count = 1) =>
            _conn.Send(MessageTypes.DomainUseScroll, new DomainScrollRequest { Hero = hero, Item = item, Count = count });

        public void DomainTrial(HeroId hero, Domain domain) =>
            _conn.Send(MessageTypes.DomainTrial, new DomainTrialRequest { Hero = hero, Domain = domain });

        /// <summary>item 為 null = 免費加速。</summary>
        public void Speedup(QueueType queue, ItemType? item, long count = 1) =>
            _conn.Send(MessageTypes.CitySpeedup, new SpeedupRequest { Queue = queue, Item = item, Count = count });

        /// <summary>客戶端預測：用共用規則把伺服器狀態推進到「現在」，讓資源與倒數即時跳動。</summary>
        public CityState Predicted()
        {
            if (City == null) return null;
            var copy = JsonConvert.DeserializeObject<CityState>(JsonConvert.SerializeObject(City, GameConnection.Settings), GameConnection.Settings);
            CityLogic.Advance(copy, ServerNowMs);
            return copy;
        }

        // ───── 伺服器訊息 ─────

        private void Handle(ServerMessage msg)
        {
            switch (msg.Type)
            {
                case MessageTypes.LoginOk:
                    var ok = msg.DataAs<LoginOk>();
                    PlayerId = ok.PlayerId;
                    PlayerName = ok.DisplayName;
                    SyncClock(ok.ServerTimeMs);
                    Notice?.Invoke($"歡迎回來，{ok.DisplayName} 領主", false);
                    break;
                case MessageTypes.CityState:
                    var state = msg.DataAs<CityStateMessage>();
                    SyncClock(state.ServerTimeMs);
                    City = state.City;
                    break;
                case MessageTypes.UpgradeCompleted:
                    var up = msg.DataAs<UpgradeCompletedMessage>();
                    Notice?.Invoke($"{DisplayNames.Buildings[up.Building]} 升級至 Lv.{up.Level}", false);
                    break;
                case MessageTypes.TrainingCompleted:
                    var tr = msg.DataAs<TrainingCompletedMessage>();
                    Notice?.Invoke($"{tr.Count:N0} 名{DisplayNames.Troops[tr.Troop]}訓練完成", false);
                    break;
                case MessageTypes.ExpeditionResultMessage:
                    LastExpedition = msg.DataAs<ExpeditionResult>();
                    LastExpeditionTeams = _pendingTeams;
                    Notice?.Invoke(LastExpedition.Win ? $"遠征第 {LastExpedition.Stage} 關勝利！" : $"遠征第 {LastExpedition.Stage} 關失敗", !LastExpedition.Win);
                    break;
                case MessageTypes.DomainTrialResultMessage:
                    var trial = msg.DataAs<DomainTrialResult>();
                    string who = DisplayNames.Heroes[trial.Hero], dom = DisplayNames.Domains[trial.Domain];
                    Notice?.Invoke(trial.Success ? $"{who} 通過印證試煉，{dom}刻成！" : $"{who} 的 {dom} 印證試煉失敗，修練值保留，變強後再來", !trial.Success);
                    break;
                case MessageTypes.WorldCities:
                    Cities = msg.DataAs<WorldCitiesMessage>().Cities ?? new List<CityPin>();
                    break;
                case MessageTypes.MarchArrived:
                    var ma = msg.DataAs<MarchArrivedMessage>();
                    var node = AbyssKingdom.Shared.Map.MapConfig.Layer(1).Nodes[ma.Node];
                    Notice?.Invoke(ma.Disbanded ? $"{DisplayNames.Heroes[ma.Hero]} 的部隊已回到兵營"
                        : ma.Discovered ? $"{DisplayNames.Heroes[ma.Hero]} 抵達並揭開了「{DisplayNames.NodeName(node)}」"
                        : $"{DisplayNames.Heroes[ma.Hero]} 抵達「{DisplayNames.NodeName(node)}」，駐紮中", false);
                    break;
                case MessageTypes.ExpeditionIdleClaimed:
                    var idle = msg.DataAs<ExpeditionRewards>();
                    idle.Resources.TryGetValue(ResourceType.Wood, out var each);
                    Notice?.Invoke($"領取掛機收益：各資源 +{each:N0}", false);
                    break;
                case MessageTypes.Error:
                    var err = msg.DataAs<ErrorMessage>();
                    Notice?.Invoke(DisplayNames.Error(err.Code, err.Detail), true);
                    break;
            }
        }

        private void SyncClock(long serverMs)
        {
            _serverOffsetMs = serverMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
