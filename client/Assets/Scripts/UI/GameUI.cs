using System;
using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.Net;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// 雛型介面（UI Toolkit，全部由程式建立，樣式在 Resources/UI/Game.uss）。
    /// 只讀 GameClient 的狀態並呼叫它的「意圖」方法；正式美術完成後可整份替換。
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        private const float RefreshInterval = 0.2f;
        private const float ToastSeconds = 4f;

        private static GameClient G => GameClient.Instance;

        private VisualElement _root, _login, _game, _toasts, _dialog;
        private TextField _urlField, _nameField;
        private Label _loginStatus, _playerName, _troopTotal, _powerDelta;
        private long _lastTotalPower;
        private readonly Dictionary<ResourceType, (Label amount, Label cap)> _resChips = new Dictionary<ResourceType, (Label, Label)>();
        private readonly List<BuildingCard> _cards = new List<BuildingCard>();
        private VisualElement _cityPage;
        private HeroPage _heroPage;
        private ExpeditionPage _expPage;
        private readonly List<Label> _tabs = new List<Label>();
        private int _tab; // 0 城鎮建築、1 主將、2 遠征
        private QueueView _buildQueue, _trainQueue;
        private TrainingPanel _training;
        private VisualElement _itemList;
        private string _itemSignature;
        private QueueType _dialogQueue;
        private Label _dialogTime;
        private VisualElement _dialogList;
        private string _dialogSignature;
        private readonly List<(VisualElement el, float until)> _toastItems = new List<(VisualElement, float)>();
        private float _nextRefresh;

        public static void Create()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/AbyssTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1280, 800);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;

            var go = new GameObject("GameUI");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            go.AddComponent<UIDocument>().panelSettings = settings;
            go.AddComponent<GameUI>();
            go.SetActive(true);
        }

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.Clear();
            _root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Game"));
            _root.AddToClassList("root");
            _root.style.backgroundImage = new StyleBackground(MakeGradient());
            var font = LoadCjkFont();
            if (font != null) _root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(font));

            _game = BuildGame();
            _login = BuildLogin();
            _toasts = El("toasts");
            _toasts.pickingMode = PickingMode.Ignore;
            _root.Add(_game);
            _root.Add(_login);
            _root.Add(_toasts);
            OverlayHost = _root;
            OverlayBefore = _toasts;

            G.Notice += ShowToast;
            // 測試用：-heroes / -expedition 直接開對應頁籤
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-heroes") >= 0) _tab = 1;
            if (Array.IndexOf(args, "-expedition") >= 0) _tab = 2;
            if (Array.IndexOf(args, "-autofight") >= 0) _root.schedule.Execute(() => _expPage.FightNow()).StartingIn(4000);
        }

        private void OnDisable()
        {
            if (G != null) G.Notice -= ShowToast;
        }

        private void Update()
        {
            for (int i = _toastItems.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _toastItems[i].until) continue;
                _toastItems[i].el.RemoveFromHierarchy();
                _toastItems.RemoveAt(i);
            }

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;

            var city = G.Predicted();
            bool inGame = G.IsLoggedIn && city != null;
            Show(_login, !inGame);
            Show(_game, inGame);
            if (!inGame)
            {
                _loginStatus.text = G.IsBusy ? "連線中…" : G.IsLoggedIn ? "讀取城市中…" : "";
                if (_dialog != null) CloseDialog();
                return;
            }
            Refresh(city);
        }

        // ───── 登入畫面 ─────

        private VisualElement BuildLogin()
        {
            var overlay = El("overlay");
            var card = El("login-card");
            overlay.Add(card);
            card.Add(Lbl("深淵王國", "login-title"));
            card.Add(Lbl("ABYSS KINGDOM · 垂直深淵策略", "login-sub"));

            card.Add(Lbl("伺服器網址", "field-label"));
            _urlField = new TextField { value = G.ServerUrl };
            _urlField.RegisterValueChangedCallback(e => G.ServerUrl = e.newValue);
            card.Add(_urlField);

            card.Add(Lbl("領主名稱", "field-label"));
            _nameField = new TextField { value = G.PlayerName, maxLength = 16 };
            _nameField.RegisterValueChangedCallback(e => G.PlayerName = e.newValue);
            card.Add(_nameField);

            if (FirebaseGuestAuth.IsConfigured)
                card.Add(Btn("訪客登入", () => G.Login(guest: true), "btn-primary", "login-btn"));
            card.Add(Btn("開發模式登入（本機 Dev 伺服器）", () => G.Login(guest: false), "btn-small", "login-dev"));
            _loginStatus = Lbl("", "muted", "login-status");
            card.Add(_loginStatus);
            return overlay;
        }

        // ───── 主畫面 ─────

        private VisualElement BuildGame()
        {
            var game = El("grow");

            var top = El("topbar");
            top.Add(Lbl("深淵王國", "brand"));
            top.Add(Lbl("永暗之喉 · 第一層", "brand-sub"));
            top.Add(El("spacer"));
            foreach (var r in CityConfig.AllResources)
            {
                var chip = El("res-chip");
                chip.Add(ResIcon(r, false));
                var amount = Lbl("0", "res-amount");
                var cap = Lbl("/ 0", "res-cap");
                chip.Add(amount);
                chip.Add(cap);
                chip.tooltip = DisplayNames.Resources[r];
                _resChips[r] = (amount, cap);
                top.Add(chip);
            }
            var player = El("player-chip");
            _playerName = Lbl("", "player-name");
            _troopTotal = Lbl("", "total-power");
            _powerDelta = Lbl("", "power-delta", "small");
            player.Add(_playerName);
            var powerRow = El("row");
            powerRow.Add(_powerDelta);
            powerRow.Add(_troopTotal);
            player.Add(powerRow);
            top.Add(player);
            game.Add(top);

            var main = El("main");
            game.Add(main);

            var left = El("left");
            var tabs = El("tabs");
            string[] tabNames = { "城鎮建築", "主將", "遠征" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = Lbl(tabNames[i], "tab");
                int idx = i;
                tab.RegisterCallback<ClickEvent>(_ => _tab = idx);
                _tabs.Add(tab);
                tabs.Add(tab);
            }
            left.Add(tabs);

            _cityPage = new ScrollView(ScrollViewMode.Vertical);
            _cityPage.AddToClassList("grow");
            var grid = El("building-grid");
            foreach (var b in CityConfig.AllBuildings)
            {
                var card = new BuildingCard(b);
                _cards.Add(card);
                grid.Add(card.Root);
            }
            _cityPage.Add(grid);
            left.Add(_cityPage);

            _heroPage = new HeroPage();
            left.Add(_heroPage.Root);
            _expPage = new ExpeditionPage();
            left.Add(_expPage.Root);
            main.Add(left);

            var right = new ScrollView(ScrollViewMode.Vertical);
            right.AddToClassList("right");
            _buildQueue = new QueueView(QueueType.Building, OpenSpeedupDialog);
            var buildPanel = El("panel");
            buildPanel.Add(Lbl("建築隊列", "panel-title"));
            buildPanel.Add(_buildQueue.Root);
            right.Add(buildPanel);

            _trainQueue = new QueueView(QueueType.Training, OpenSpeedupDialog);
            _training = new TrainingPanel(_trainQueue);
            right.Add(_training.Root);

            var items = El("panel");
            items.Add(Lbl("加速道具", "panel-title"));
            _itemList = El();
            items.Add(_itemList);
            right.Add(items);
            main.Add(right);
            return game;
        }

        private void Refresh(CityState city)
        {
            long now = G.ServerNowMs;
            long cap = CityConfig.Capacity(city.GetLevel(BuildingType.Warehouse));
            foreach (var r in CityConfig.AllResources)
            {
                var (amount, capLabel) = _resChips[r];
                long v = city.GetResource(r);
                amount.text = v.ToString("N0");
                amount.EnableInClassList("full", v >= cap);
                capLabel.text = "/ " + cap.ToString("N0");
            }
            _playerName.text = G.PlayerName;
            long total = PowerLogic.TotalPower(city);
            _troopTotal.text = $"總戰力 {total:N0}";
            _troopTotal.tooltip = $"主將 {PowerLogic.HeroesPower(city):N0}　部隊 {PowerLogic.TroopsPower(city):N0}　建築 {PowerLogic.BuildingsPower(city):N0}";
            if (_lastTotalPower > 0 && total != _lastTotalPower)
            {
                long d = total - _lastTotalPower;
                _powerDelta.text = (d > 0 ? "+" : "") + d.ToString("N0");
                _powerDelta.EnableInClassList("down", d < 0);
                _powerDelta.experimental.animation.Start(1f, 0f, 2000, (e, v) => e.style.opacity = v);
            }
            _lastTotalPower = total;

            for (int i = 0; i < _tabs.Count; i++) _tabs[i].EnableInClassList("selected", i == _tab);
            Show(_cityPage, _tab == 0);
            Show(_heroPage.Root, _tab == 1);
            Show(_expPage.Root, _tab == 2);
            if (_tab == 0) foreach (var card in _cards) card.Refresh(city);
            else if (_tab == 1) _heroPage.Refresh(city);
            else _expPage.Refresh(city);
            _buildQueue.Refresh(city, now);
            _training.Refresh(city, now);
            RefreshItems(city);
            if (_dialog != null) RefreshDialog(city, now);
        }

        private void RefreshItems(CityState city)
        {
            var owned = ItemConfig.AllItems.Where(i => city.GetItemCount(i) > 0).ToList();
            string sig = string.Join(",", owned.Select(i => $"{i}:{city.GetItemCount(i)}"));
            if (sig == _itemSignature) return;
            _itemSignature = sig;
            _itemList.Clear();
            if (owned.Count == 0) _itemList.Add(Lbl("背包裡沒有加速道具", "muted"));
            foreach (var item in owned)
            {
                var row = El("item-row");
                row.Add(Lbl(DisplayNames.Item(item), "item-name"));
                row.Add(Lbl("×" + city.GetItemCount(item), "item-count"));
                _itemList.Add(row);
            }
        }

        // ───── 加速視窗 ─────

        private void OpenSpeedupDialog(QueueType queue)
        {
            CloseDialog();
            _dialogQueue = queue;
            _dialogSignature = null;
            var overlay = El("overlay");
            overlay.RegisterCallback<PointerDownEvent>(e => { if (e.target == overlay) CloseDialog(); });
            var box = El("dialog");
            overlay.Add(box);
            var head = El("row");
            head.Add(Lbl($"加速 — {DisplayNames.Queues[queue]}", "panel-title", "grow"));
            head.Add(Btn("關閉", CloseDialog, "btn-small"));
            box.Add(head);
            _dialogTime = Lbl("", "queue-time");
            box.Add(_dialogTime);
            _dialogList = El();
            _dialogList.style.marginTop = 10;
            box.Add(_dialogList);
            _dialog = overlay;
            _root.Insert(_root.IndexOf(_toasts), overlay);
            RefreshDialog(G.Predicted(), G.ServerNowMs);
        }

        private void RefreshDialog(CityState city, long now)
        {
            long? finish = _dialogQueue == QueueType.Building ? city?.Upgrade?.FinishAtMs : city?.Training?.FinishAtMs;
            if (finish == null)
            {
                CloseDialog();
                return;
            }
            _dialogTime.text = "剩餘時間 " + DisplayNames.Duration(finish.Value - now);

            var usable = ItemConfig.AllItems.Where(i =>
                city.GetItemCount(i) > 0 && ItemConfig.TryGetSpeedup(i, out var c, out _) && ItemConfig.CanApply(c, _dialogQueue)).ToList();
            string sig = string.Join(",", usable.Select(i => $"{i}:{city.GetItemCount(i)}"));
            if (sig == _dialogSignature) return;
            _dialogSignature = sig;
            _dialogList.Clear();
            if (usable.Count == 0) _dialogList.Add(Lbl("沒有可用在這裡的加速道具", "muted"));
            foreach (var item in usable)
            {
                var row = El("item-row");
                row.Add(Lbl(DisplayNames.Item(item), "item-name"));
                row.Add(Lbl("×" + city.GetItemCount(item), "item-count"));
                var it = item;
                row.Add(Btn("使用", () => G.Speedup(_dialogQueue, it), "btn-primary", "btn-small"));
                _dialogList.Add(row);
            }
        }

        private void CloseDialog()
        {
            _dialog?.RemoveFromHierarchy();
            _dialog = null;
        }

        // ───── 通知 ─────

        private void ShowToast(string message, bool isError)
        {
            var toast = Lbl(message, "toast");
            toast.EnableInClassList("error", isError);
            _toasts.Insert(0, toast);
            _toastItems.Add((toast, Time.unscaledTime + ToastSeconds));
            while (_toasts.childCount > 5)
            {
                var last = _toasts[_toasts.childCount - 1];
                _toastItems.RemoveAll(t => t.el == last);
                last.RemoveFromHierarchy();
            }
        }

        // ───── 子元件 ─────

        private sealed class BuildingCard
        {
            public readonly VisualElement Root;
            private readonly BuildingType _type;
            private readonly Label _level, _desc, _time;
            private readonly CostRow _cost;
            private readonly Button _button;

            public BuildingCard(BuildingType type)
            {
                _type = type;
                Root = El("building-card");
                var head = El("row");
                head.Add(Lbl(DisplayNames.Buildings[type], "building-name"));
                _level = Lbl("", "level-badge");
                head.Add(_level);
                Root.Add(head);
                _desc = Lbl("", "building-desc");
                Root.Add(_desc);
                _cost = new CostRow();
                Root.Add(_cost.Root);
                Root.Add(El("spacer"));
                var footer = El("building-footer");
                _time = Lbl("", "muted", "grow");
                footer.Add(_time);
                _button = Btn("升級", () => G.Upgrade(_type), "btn-primary");
                footer.Add(_button);
                Root.Add(footer);
            }

            public void Refresh(CityState city)
            {
                int level = city.GetLevel(_type);
                _level.text = level == 0 ? "未建造" : "Lv." + level;
                string desc = DisplayNames.BuildingDescriptions[_type];
                if (CityConfig.TryGetProducedResource(_type, out var res))
                    desc += $"  每小時 +{CityConfig.ProductionPerHour(level):N0}";
                if (_type == BuildingType.Warehouse)
                    desc += $"  上限 {CityConfig.Capacity(level):N0}";
                _desc.text = desc;
                Root.EnableInClassList("upgrading", city.Upgrade != null && city.Upgrade.Building == _type);

                bool maxed = level >= CityConfig.MaxLevel;
                Show(_cost.Root, !maxed);
                if (!maxed)
                {
                    _cost.Refresh(r => CityConfig.UpgradeCost(_type, level + 1, r), city);
                    _time.text = "工期 " + DisplayNames.Duration(CityConfig.UpgradeDurationMs(_type, level + 1));
                }
                else _time.text = "";

                var check = CityLogic.CanUpgrade(city, _type);
                _button.SetEnabled(check == UpgradeResult.Ok);
                _button.text = check == UpgradeResult.Ok ? (level == 0 ? "建造" : "升級") : DisplayNames.UpgradeBlocker(check);
            }
        }

        private sealed class CostRow
        {
            public readonly VisualElement Root = El("row");
            private readonly Dictionary<ResourceType, (VisualElement box, Label label)> _entries = new Dictionary<ResourceType, (VisualElement, Label)>();

            public CostRow()
            {
                foreach (var r in CityConfig.AllResources)
                {
                    var box = El("cost");
                    box.Add(ResIcon(r, true));
                    var label = Lbl("", "cost-label");
                    box.Add(label);
                    _entries[r] = (box, label);
                    Root.Add(box);
                }
            }

            public void Refresh(Func<ResourceType, long> cost, CityState city)
            {
                foreach (var r in CityConfig.AllResources)
                {
                    var (box, label) = _entries[r];
                    long c = cost(r);
                    Show(box, c > 0);
                    label.text = c.ToString("N0");
                    label.EnableInClassList("short", city.GetResource(r) < c);
                }
            }
        }

        /// <summary>一條隊列的顯示：進度條、剩餘時間、免費完成／加速／取消。</summary>
        private sealed class QueueView
        {
            public readonly VisualElement Root = El();
            private readonly QueueType _queue;
            private readonly Label _idle, _title, _time;
            private readonly VisualElement _active, _fill;
            private readonly Button _free;

            public QueueView(QueueType queue, Action<QueueType> openSpeedup)
            {
                _queue = queue;
                _idle = Lbl(queue == QueueType.Building ? "空閒中 — 從左側選擇建築開始升級" : "訓練隊列空閒中", "queue-idle");
                Root.Add(_idle);
                _active = El();
                _title = Lbl("", "queue-title");
                _active.Add(_title);
                var bar = El("progress");
                _fill = El("progress-fill");
                _fill.EnableInClassList("training", queue == QueueType.Training);
                bar.Add(_fill);
                _active.Add(bar);
                var row = El("row");
                _time = Lbl("", "queue-time", "grow");
                row.Add(_time);
                _free = Btn("免費完成", () => G.Speedup(_queue, null), "btn-free", "btn-small");
                row.Add(_free);
                row.Add(Btn("加速", () => openSpeedup(_queue), "btn-small"));
                row.Add(Btn("取消", () => G.Cancel(_queue), "btn-danger", "btn-small"));
                _active.Add(row);
                Root.Add(_active);
            }

            public void Refresh(CityState city, long now)
            {
                long start, finish;
                if (_queue == QueueType.Building && city.Upgrade != null)
                {
                    _title.text = $"{DisplayNames.Buildings[city.Upgrade.Building]} → Lv.{city.Upgrade.TargetLevel}";
                    start = city.Upgrade.StartedAtMs;
                    finish = city.Upgrade.FinishAtMs;
                }
                else if (_queue == QueueType.Training && city.Training != null)
                {
                    long done = CityLogic.TrainedSoFar(city.Training, now);
                    _title.text = $"訓練中：{DisplayNames.Troops[city.Training.Troop]}  {done:N0} / {city.Training.Count:N0}";
                    start = city.Training.StartedAtMs;
                    finish = city.Training.FinishAtMs;
                }
                else
                {
                    Show(_idle, true);
                    Show(_active, false);
                    return;
                }
                Show(_idle, false);
                Show(_active, true);
                long remaining = Math.Max(0, finish - now);
                float pct = finish > start ? Mathf.Clamp01((float)(now - start) / (finish - start)) : 1f;
                _fill.style.width = Length.Percent(pct * 100f);
                _time.text = "剩餘 " + DisplayNames.Duration(remaining);
                Show(_free, remaining <= CityConfig.FreeSpeedupMs);
            }
        }

        private sealed class TrainingPanel
        {
            public readonly VisualElement Root = El("panel");
            private readonly Label _limit, _needBarracks, _role, _countLabel, _time;
            private readonly VisualElement _controls;
            private readonly Dictionary<TroopType, (VisualElement tab, Label count)> _tabs = new Dictionary<TroopType, (VisualElement, Label)>();
            private readonly SliderInt _slider;
            private readonly CostRow _cost = new CostRow();
            private readonly Button _train;
            private readonly QueueView _queue;
            private TroopType _selected = TroopType.Shield;

            public TrainingPanel(QueueView queue)
            {
                _queue = queue;
                var head = El("row");
                head.Add(Lbl("兵營訓練", "panel-title", "grow"));
                _limit = Lbl("", "muted");
                head.Add(_limit);
                Root.Add(head);
                _needBarracks = Lbl("需要先建造兵營", "queue-idle");
                Root.Add(_needBarracks);

                _controls = El();
                var tabs = El("troop-tabs");
                foreach (var t in CityConfig.AllTroops)
                {
                    var tab = El("troop-tab");
                    tab.Add(Lbl(DisplayNames.Troops[t], "troop-tab-name"));
                    var count = Lbl("", "troop-tab-count");
                    tab.Add(count);
                    var troop = t;
                    tab.RegisterCallback<ClickEvent>(_ => _selected = troop);
                    _tabs[t] = (tab, count);
                    tabs.Add(tab);
                }
                _controls.Add(tabs);
                _role = Lbl("", "muted");
                _controls.Add(_role);

                var sliderRow = El("row");
                _slider = new SliderInt(1, 50) { value = 10 };
                _slider.AddToClassList("grow");
                _slider.AddToClassList("count-slider");
                sliderRow.Add(_slider);
                _countLabel = Lbl("", "queue-title");
                _countLabel.style.width = 64;
                _countLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                sliderRow.Add(_countLabel);
                sliderRow.Add(Btn("最大", () => _slider.value = _slider.highValue, "btn-small"));
                _controls.Add(sliderRow);

                var costRow = El("row");
                costRow.Add(_cost.Root);
                costRow.Add(El("spacer"));
                _time = Lbl("", "muted");
                costRow.Add(_time);
                _controls.Add(costRow);
                _train = Btn("開始訓練", () => G.Train(_selected, _slider.value), "btn-primary");
                _train.style.marginTop = 10;
                _train.style.marginLeft = 0;
                _controls.Add(_train);

                var queueBox = El();
                queueBox.style.marginTop = 14;
                queueBox.Add(_queue.Root);
                _controls.Add(queueBox);
                Root.Add(_controls);
            }

            public void Refresh(CityState city, long now)
            {
                int barracks = city.GetLevel(BuildingType.Barracks);
                long max = CityConfig.MaxTrainBatch(barracks);
                Show(_needBarracks, barracks <= 0);
                Show(_controls, barracks > 0);
                _limit.text = barracks > 0 ? $"單批上限 {max:N0}" : "";
                if (barracks <= 0) return;

                foreach (var t in CityConfig.AllTroops)
                {
                    var (tab, count) = _tabs[t];
                    tab.EnableInClassList("selected", t == _selected);
                    count.text = "持有 " + city.GetTroops(t).ToString("N0");
                }
                _role.text = DisplayNames.TroopRoles[_selected];

                if (_slider.highValue != (int)max)
                {
                    _slider.highValue = (int)max;
                    if (_slider.value > max) _slider.value = (int)max;
                }
                long n = _slider.value;
                _countLabel.text = "×" + n.ToString("N0");
                _cost.Refresh(r => CityConfig.TroopCost(_selected, r) * n, city);
                _time.text = "需時 " + DisplayNames.Duration(CityConfig.TroopTrainMsPerUnit(_selected) * n);

                var check = CityLogic.CanTrain(city, _selected, n);
                _train.SetEnabled(check == TrainResult.Ok);
                _train.text = check == TrainResult.Ok ? $"訓練 {n:N0} 名{DisplayNames.Troops[_selected]}"
                    : check == TrainResult.QueueBusy ? "訓練隊列忙碌中"
                    : check == TrainResult.NotEnoughResources ? "資源不足" : "無法訓練";
                _queue.Refresh(city, now);
            }
        }

        /// <summary>背景：上方微亮（地表）往下沉入深淵的漸層。</summary>
        private static Texture2D MakeGradient()
        {
            const int h = 256;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var deep = new Color32(4, 6, 13, 255);
            var surface = new Color32(24, 32, 62, 255);
            for (int y = 0; y < h; y++)
            {
                float t = Mathf.Pow(y / (float)(h - 1), 1.6f);
                tex.SetPixel(0, y, Color32.Lerp(deep, surface, t));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>從系統字型建立中文字型（Windows 正黑體優先）。找不到時回傳 null，沿用預設字型。</summary>
        private static FontAsset LoadCjkFont()
        {
            string[] families = { "Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans TC", "Noto Sans CJK TC", "PingFang TC" };
            foreach (var family in families)
            {
                try
                {
                    var font = FontAsset.CreateFontAsset(family, "Regular");
                    if (font != null) return font;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GameUI] 字型 {family} 載入失敗：{ex.Message}");
                }
            }
            Debug.LogWarning("[GameUI] 找不到中文系統字型，中文可能無法顯示");
            return null;
        }
    }
}
