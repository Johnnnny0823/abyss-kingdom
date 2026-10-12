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
        private bool _nameFromArgs;

        public string ServerUrl { get; set; }
        public string PlayerName { get; set; }
        public string PlayerId { get; private set; }
        public bool IsLoggedIn => PlayerId != null;
        public bool IsBusy { get; private set; }

        /// <summary>伺服器最後送來的權威城市狀態。</summary>
        public CityState City { get; private set; }
        public AllianceStateMessage Alliance { get; private set; }
        public List<AllianceChatView> AllianceChat { get; private set; } = new List<AllianceChatView>();
        public int AllianceChatVersion { get; private set; }
        private string _chatAllianceId;
        private bool _fullChatRefreshPending;
        private string _allianceCreateOperationId;
        private string _allianceCreateKey;

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
                if (args[i] == "-devname" && i + 1 < args.Length) { PlayerName = args[i + 1]; _nameFromArgs = true; }
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
                Alliance = null;
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
                if (!_nameFromArgs) PlayerPrefs.SetString(PrefName, PlayerName ?? "");
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

        public void Heal(Dictionary<TroopType, long> troops) => _conn.Send(MessageTypes.CityHeal, new HealRequest { Troops = troops });

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

        public void StartMarch(HeroId hero, Dictionary<TroopType, long> troops, int x, int y) =>
            _conn.Send(MessageTypes.MapMarch, new MapMarchRequest { Hero = hero, Troops = troops, X = x, Y = y });

        /// <summary>地表所有主城（定期向伺服器要一次）。</summary>
        public List<CityPin> Cities { get; private set; } = new List<CityPin>();

        public void RequestCities() => _conn.Send(MessageTypes.WorldCities);

        /// <summary>領取目前新手任務的獎勵。</summary>
        public void ClaimQuest() => _conn.Send(MessageTypes.QuestClaim);

        public void RequestAlliance()
        {
            _fullChatRefreshPending = true;
            _conn.Send(MessageTypes.AllianceGet);
        }
        public void CreateAlliance(string name, string tag)
        {
            string key = (name ?? "").Trim() + "|" + (tag ?? "").Trim().ToUpperInvariant();
            if (_allianceCreateKey != key)
            {
                _allianceCreateKey = key;
                _allianceCreateOperationId = Guid.NewGuid().ToString();
            }
            _conn.Send(MessageTypes.AllianceCreate, new AllianceCreateRequest
            { Name = name, Tag = tag, OperationId = _allianceCreateOperationId });
        }
        public void JoinAlliance(string id, bool apply) =>
            _conn.Send(apply ? MessageTypes.AllianceApply : MessageTypes.AllianceJoin, new AllianceIdRequest { AllianceId = id });
        public void ReviewAlliance(string playerId, bool accept) =>
            _conn.Send(MessageTypes.AllianceReview, new AllianceReviewRequest { PlayerId = playerId, Accept = accept });
        public void LeaveAlliance() => _conn.Send(MessageTypes.AllianceLeave);
        public void KickAllianceMember(string playerId) =>
            _conn.Send(MessageTypes.AllianceKick, new AllianceTargetRequest { PlayerId = playerId });
        public void SetAllianceOfficer(string playerId, bool officer) =>
            _conn.Send(MessageTypes.AllianceOfficer, new AllianceOfficerRequest { PlayerId = playerId, Officer = officer });
        public void TransferAlliance(string playerId) =>
            _conn.Send(MessageTypes.AllianceTransfer, new AllianceTargetRequest { PlayerId = playerId });
        public void DissolveAlliance() => _conn.Send(MessageTypes.AllianceDissolve);
        public void SetAllianceJoinMode(AbyssKingdom.Shared.Social.AllianceJoinMode mode) =>
            _conn.Send(MessageTypes.AllianceJoinMode, new AllianceJoinModeRequest { Mode = mode });
        public void PostAllianceChat(string text) => _conn.Send(MessageTypes.AllianceChatPost,
            new AllianceChatPostRequest { Text = text, OperationId = Guid.NewGuid().ToString() });
        public void RequestAllianceChat(long afterSequence = 0) => _conn.Send(MessageTypes.AllianceChatHistory,
            new AllianceChatHistoryRequest { AfterSequence = afterSequence });
        public void ReportAllianceChat(long sequence) => _conn.Send(MessageTypes.AllianceChatReport,
            new AllianceChatTargetRequest { Sequence = sequence });
        public void HideAllianceChat(long sequence)
        {
            _fullChatRefreshPending = true;
            _conn.Send(MessageTypes.AllianceChatHide, new AllianceChatTargetRequest { Sequence = sequence });
        }
        public void MuteAllianceChat(string playerId, int hours) => _conn.Send(MessageTypes.AllianceChatMute,
            new AllianceChatMuteRequest { PlayerId = playerId, Hours = hours });
        public void SetAllianceAnnouncement(string text) => _conn.Send(MessageTypes.AllianceAnnouncement,
            new AllianceAnnouncementRequest { Text = text });
        public void RequestAllianceHelp(QueueType queue) => _conn.Send(MessageTypes.AllianceHelpRequest,
            new AllianceHelpRequestMessage { Queue = queue });
        public void HelpAllAlliance() => _conn.Send(MessageTypes.AllianceHelpAll);
        public void CreateRally(HeroId hero, Dictionary<TroopType, long> troops, int x, int y, int seconds, string operationId) =>
            _conn.Send(MessageTypes.AllianceRallyCreate, new AllianceRallyCreateRequest
            { Hero = hero, Troops = troops, X = x, Y = y, Seconds = seconds, OperationId = operationId });
        public void JoinRally(string id, HeroId hero, Dictionary<TroopType, long> troops, string operationId) =>
            _conn.Send(MessageTypes.AllianceRallyJoin, new AllianceRallyJoinRequest { Id = id, Hero = hero, Troops = troops, OperationId = operationId });
        public void RecallRally(string id) => _conn.Send(MessageTypes.AllianceRallyRecall, new AllianceRallyRecallRequest { Id = id });
        public List<FlagMapView> TerritoryFlags { get; private set; } = new List<FlagMapView>();
        public int TerritoryVersion { get; private set; }
        private long _territoryRevision = -1;
        public void RequestTerritory() => _conn.Send(MessageTypes.WorldTerritory);
        public void PlaceAllianceFlag(int slot, int x, int y, string operationId) => _conn.Send(MessageTypes.AllianceFlagPlace,
            new AllianceFlagPlaceRequest { Slot = slot, X = x, Y = y, OperationId = operationId });
        public void RemoveAllianceFlag(int slot) => _conn.Send(MessageTypes.AllianceFlagRemove,
            new AllianceFlagRemoveRequest { Slot = slot });
        public void SendReinforcement(string playerId, HeroId hero, Dictionary<TroopType, long> troops, string operationId) =>
            _conn.Send(MessageTypes.AllianceReinforcementSend, new AllianceReinforcementSendRequest
            { PlayerId = playerId, Hero = hero, Troops = troops, OperationId = operationId });
        public void ReturnReinforcement(string id) => _conn.Send(MessageTypes.AllianceReinforcementReturn,
            new AllianceReinforcementReturnRequest { Id = id });
        public void SetReinforcementAcceptance(bool accept) => _conn.Send(MessageTypes.AllianceReinforcementAccept,
            new AllianceReinforcementAcceptRequest { Accept = accept });

        /// <summary>目前被擊敗、還沒重生的怪物營地（大地圖讓營地前的怪物消失）。</summary>
        public List<DefeatedCamp> DefeatedCamps { get; private set; } = new List<DefeatedCamp>();

        public void RequestCamps() => _conn.Send(MessageTypes.WorldCamps);

        /// <summary>最近一次查詢的格子狀態（資源田存量、誰在採）。</summary>
        public TileInfoMessage Tile { get; private set; }

        public void RequestTile(int x, int y) => _conn.Send(MessageTypes.WorldTile, new TileInfoRequest { X = x, Y = y });

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
            // 伺服器狀態沒變就不重新序列化（每次刷新都把整個城市轉成 JSON 再轉回來很浪費）；
            // 同一個畫面幀裡多次呼叫回傳同一份；複本和伺服器狀態分開，呼叫端改它不會影響伺服器狀態。
            if (!ReferenceEquals(_predictSource, City))
            {
                _predictSource = City;
                _predictJson = JsonConvert.SerializeObject(City, GameConnection.Settings);
                _predictFrame = -1;
            }
            long now = ServerNowMs;
            if (_predictFrame == Time.frameCount && _predicted != null && _predictAt == now) return _predicted;
            var copy = JsonConvert.DeserializeObject<CityState>(_predictJson, GameConnection.Settings);
            CityLogic.Advance(copy, now);
            _predicted = copy;
            _predictFrame = Time.frameCount;
            _predictAt = now;
            return copy;
        }

        private CityState _predictSource, _predicted;
        private string _predictJson;
        private int _predictFrame = -1;
        private long _predictAt;

        // ───── 伺服器訊息 ─────

        private void Handle(ServerMessage msg)
        {
            switch (msg.Type)
            {
                case MessageTypes.LoginOk:
                    var ok = msg.DataAs<LoginOk>();
                    PlayerId = ok.PlayerId;
                    PlayerName = ok.DisplayName;
                    Alliance = null; _fullChatRefreshPending = true;
                    _territoryRevision = -1; TerritoryFlags.Clear(); TerritoryVersion++;
                    SyncClock(ok.ServerTimeMs);
                    Notice?.Invoke($"歡迎回來，{ok.DisplayName} 領主", false);
                    _conn.Send(MessageTypes.AllianceGet);
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
                case MessageTypes.AllianceState:
                    var incomingAlliance = msg.DataAs<AllianceStateMessage>();
                    if (Alliance != null && incomingAlliance.Version < Alliance.Version) break;
                    Alliance = incomingAlliance;
                    RequestCities();
                    if (Alliance.ServerTimeMs > 0) SyncClock(Alliance.ServerTimeMs);
                    RequestTerritory();
                    if (_chatAllianceId != Alliance?.MyAllianceId)
                    {
                        _chatAllianceId = Alliance?.MyAllianceId;
                        AllianceChat.Clear();
                        AllianceChatVersion++;
                        _fullChatRefreshPending = true;
                    }
                    if (!string.IsNullOrEmpty(_chatAllianceId))
                    {
                        long after = _fullChatRefreshPending || AllianceChat.Count == 0
                            ? 0 : AllianceChat[AllianceChat.Count - 1].Sequence;
                        RequestAllianceChat(after);
                        _fullChatRefreshPending = false;
                    }
                    if (!string.IsNullOrEmpty(Alliance?.PendingCreateOperationId))
                    {
                        _allianceCreateOperationId = Alliance.PendingCreateOperationId;
                        _allianceCreateKey = Alliance.PendingCreateName + "|" + Alliance.PendingCreateTag;
                    }
                    if (!string.IsNullOrEmpty(Alliance?.MyAllianceId)) _allianceCreateOperationId = null;
                    break;
                case MessageTypes.AllianceChat:
                case MessageTypes.AllianceChatMessages:
                    var chat = msg.DataAs<AllianceChatMessagesMessage>();
                    if (chat.AllianceId != _chatAllianceId) break;
                    if (chat.FullSnapshot)
                    {
                        var newer = AllianceChat.FindAll(m => m.Sequence > chat.LatestSequence);
                        AllianceChat.Clear();
                        AllianceChat.AddRange(newer);
                    }
                    else if (chat.OldestSequence > 0)
                        AllianceChat.RemoveAll(m => m.Sequence < chat.OldestSequence);
                    foreach (var entry in chat.Messages)
                    {
                        int index = AllianceChat.FindIndex(m => m.Sequence == entry.Sequence);
                        if (index >= 0) AllianceChat[index] = entry;
                        else AllianceChat.Add(entry);
                    }
                    AllianceChat.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
                    if (AllianceChat.Count > 100) AllianceChat.RemoveRange(0, AllianceChat.Count - 100);
                    AllianceChatVersion++;
                    break;
                case MessageTypes.AllianceHelpResult:
                    var helped = msg.DataAs<AllianceHelpResultMessage>();
                    Notice?.Invoke($"聯盟幫助成功 {helped.Applied} 件，失效 {helped.Expired} 件", false);
                    break;
                case MessageTypes.AllianceHelpChanged:
                case MessageTypes.AllianceMilitaryChanged:
                    if (IsLoggedIn) RequestAlliance();
                    break;
                case MessageTypes.WorldTerritory:
                    var territory = msg.DataAs<WorldTerritoryMessage>();
                    if (territory.Version < _territoryRevision) break;
                    _territoryRevision = territory.Version;
                    TerritoryFlags = territory.Flags ?? new List<FlagMapView>();
                    TerritoryVersion++;
                    break;
                case MessageTypes.WorldCamps:
                    DefeatedCamps = msg.DataAs<WorldCampsMessage>().Camps ?? new List<DefeatedCamp>();
                    break;
                case MessageTypes.CityAttacked:
                    var atk = msg.DataAs<CityAttackedMessage>();
                    string lost = DisplayNames.CarryText(atk.Plundered);
                    string hurt = (DisplayNames.TroopsText(atk.Wounded) is var aw && aw != "" ? $"傷兵 {aw}" : "")
                        + (DisplayNames.TroopsText(atk.Dead) is var ad && ad != "" ? $"　陣亡 {ad}" : "");
                    Notice?.Invoke(atk.DefenderWon ? $"{atk.AttackerName} 攻打你的主城，被擊退了！{hurt}"
                        : $"你的主城被 {atk.AttackerName} 攻破，被掠奪 {(lost != "" ? lost : "（沒有損失資源）")}。{hurt}", !atk.DefenderWon);
                    break;
                case MessageTypes.HealingCompleted:
                    Notice?.Invoke($"治療完成：{DisplayNames.TroopsText(msg.DataAs<HealingCompletedMessage>().Troops)} 回到兵營", false);
                    break;
                case MessageTypes.WorldTile:
                    Tile = msg.DataAs<TileInfoMessage>();
                    break;
                case MessageTypes.MarchArrived:
                    var ma = msg.DataAs<MarchArrivedMessage>();
                    if (ma.BattleWon && ma.TargetName == null) RequestCamps(); // 剛打掉營地：馬上更新，怪物立刻消失
                    var (tx, ty) = AbyssKingdom.Shared.Map.SurfaceLogic.FromTile(ma.Tile);
                    string hero = DisplayNames.Heroes[ma.Hero];
                    string carry = DisplayNames.CarryText(ma.Carry);
                    string loot = DisplayNames.ItemsText(ma.LootItems);
                    string losses = (DisplayNames.TroopsText(ma.Wounded) is var w && w != "" ? $"傷兵 {w}" : "")
                        + (DisplayNames.TroopsText(ma.Dead) is var d && d != "" ? $"　陣亡 {d}" : "");
                    if (ma.TargetName != null)
                    {
                        Notice?.Invoke(ma.Allied ? $"{ma.TargetName} 已是盟友，{hero} 的部隊撤回"
                            : ma.Shielded ? $"{ma.TargetName} 的主城有護盾，{hero} 的部隊撤回"
                            : ma.BattleWon ? $"{hero} 攻破了 {ma.TargetName} 的主城！掠奪 {(carry != "" ? carry : "（對方沒有可掠奪的資源）")}。{losses}"
                            : $"{hero} 攻打 {ma.TargetName} 的主城失敗，倖存的部隊撤回。{losses}", ma.BattleLost);
                        break;
                    }
                    if (ma.BattleWon || ma.BattleLost || ma.CampGone)
                    {
                        Notice?.Invoke(ma.CampGone ? $"{hero} 抵達時營地已經被清空了，原地駐紮"
                            : ma.BattleWon ? $"{hero} 擊敗了怪物營地！戰利品：{carry}{(loot != "" ? "、" + loot : "")}。{losses}"
                            : $"{hero} 打輸了，倖存的部隊撤回主城。{losses}", ma.BattleLost);
                        break;
                    }
                    Notice?.Invoke(ma.Disbanded ? (carry != "" || loot != "" ? $"{hero} 回到主城，帶回 {carry}{(loot != "" ? "、" + loot : "")}" : $"{hero} 的部隊已回到兵營")
                        : ma.GatherDone ? $"{hero} 採滿了，帶著 {carry} 返回主城"
                        : ma.Gathering ? $"{hero} 抵達資源田，開始採集"
                        : ma.Occupied ? $"{hero} 抵達，但這塊資源田已經有人在採（或採空了），原地駐紮"
                        : $"{hero} 抵達 X:{tx} Y:{ty}，駐紮中", ma.Occupied);
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
