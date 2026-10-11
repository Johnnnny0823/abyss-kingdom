using System;
using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.Net;
using AbyssKingdom.Client.World;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
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
        private Label _loginStatus, _playerName, _troopTotal, _powerDelta, _shieldLabel;
        private long _lastTotalPower;
        private readonly Dictionary<ResourceType, (Label amount, Label cap)> _resChips = new Dictionary<ResourceType, (Label, Label)>();
        private Texture2D _gradient;
        private bool _worldMode;

        // 主畫面：3D 場景（城內／大地圖）＋四周的 HUD
        private WorldScene _world;
        private ContinentPage _continent;
        private VisualElement _surfaceSel, _buildingSel, _cardHost;
        private Label _moveHint;
        private Button _homeBtn, _moveBtn, _trainBtn;
        private readonly Dictionary<BuildingType, BuildingCard> _cards = new Dictionary<BuildingType, BuildingCard>();
        private BuildingType? _selBuilding;
        private QueueView _buildQueue, _hudTrainQueue, _trainQueue;

        // 彈出面板（主將、遠征、兵營、背包；出征面板從大地圖的資訊卡打開）
        private const int PageHeroes = 0, PageExpedition = 1, PageBarracks = 2, PageBag = 3, PageMarch = 4, PageHospital = 5;
        private const int HudPages = 4;
        private static readonly string[] PageNames = { "主將", "遠征", "兵營", "背包", "出征", "醫館" };
        private HospitalPanel _hospital;
        private QueueView _hudHealQueue, _healQueue;
        private VisualElement _healPanel;
        private Label _woundedLabel;
        private VisualElement[] _pageRoots;
        private VisualElement _pageOverlay, _pageBody;
        private Label _pageTitle;
        private int _page = -1;
        private HeroPage _heroPage;
        private ExpeditionPage _expPage;
        private MarchPanel _march;
        private MarchList _marchList;
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
            _root.pickingMode = PickingMode.Ignore;
            _gradient = MakeGradient();
            _root.style.backgroundImage = new StyleBackground(_gradient);
            _world = WorldScene.Instance ?? WorldScene.Create();
            _world.SetVisible(false);
            _world.IsPointerOverUi = IsPointerOverUi;
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
            // 測試用：-continent 開大地圖、-heroes／-expedition 直接開面板、-building <名稱>／-tile <x> <y> 直接選取
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-continent") >= 0) _world.SetMode(WorldScene.Mode.Surface);
            if (Array.IndexOf(args, "-heroes") >= 0) OpenPage(PageHeroes);
            if (Array.IndexOf(args, "-expedition") >= 0) OpenPage(PageExpedition);
            int bi = Array.IndexOf(args, "-building");
            if (bi >= 0 && bi + 1 < args.Length && Enum.TryParse<BuildingType>(args[bi + 1], out var bt)) _selBuilding = bt;
            // 測試用：-movetest <建築> <x> <y> 走一次「移動 → 放下」流程（和滑鼠操作同一段程式）
            int mi = Array.IndexOf(args, "-movetest");
            if (mi >= 0 && mi + 3 < args.Length && Enum.TryParse<BuildingType>(args[mi + 1], out var mt)
                && int.TryParse(args[mi + 2], out var mx) && int.TryParse(args[mi + 3], out var my))
                _root.schedule.Execute(() =>
                {
                    _world.City.BeginMove(mt);
                    Vector2 p = _world.Cam.WorldToScreenPoint(CityView.CenterOf(mt, mx, my));
                    _world.City.Hover(p, false);
                    _world.City.Click(p);
                }).StartingIn(5000);
            // 測試用：-gathertest 派第一位空閒主將去最近的資源田採集
            if (Array.IndexOf(args, "-gathertest") >= 0)
                _root.schedule.Execute(() =>
                {
                    var c = G.Predicted();
                    var (hx, hy) = SurfaceLogic.CityCenter(c.Surface);
                    var f = SurfaceLogic.Features().Where(x => x.Type == FeatureType.ResourceField && SurfaceLogic.Zone(x.X, x.Y) == SurfaceLogic.Zone(hx, hy))
                        .OrderBy(x => SurfaceLogic.Dist2(hx, hy, x.X, x.Y)).First();
                    _march.SetTarget(f.X, f.Y, DisplayNames.FeatureName(f));
                    _march.Refresh(c, G.ServerNowMs);
                    _march.AutoFillAndGo();
                    _continent.Select(f.X, f.Y);
                }).StartingIn(4000);
            // 測試用：-camptest 對最近的 Lv.1 營地派一支滿編（會贏）和一支 20 兵（會輸、產生傷兵）；-hospital 直接開醫館
            if (Array.IndexOf(args, "-camptest") >= 0)
                _root.schedule.Execute(() =>
                {
                    var c = G.Predicted();
                    var (hx, hy) = SurfaceLogic.CityCenter(c.Surface);
                    var f = SurfaceLogic.Features().Where(x => x.Type == FeatureType.MonsterCamp && x.Level == 1 && SurfaceLogic.Zone(x.X, x.Y) == SurfaceLogic.Zone(hx, hy))
                        .OrderBy(x => SurfaceLogic.Dist2(hx, hy, x.X, x.Y)).First();
                    G.StartMarch(HeroId.Vesper, new Dictionary<TroopType, long> { [TroopType.Crossbow] = 20 }, f.X, f.Y);
                    _march.SetTarget(f.X, f.Y, DisplayNames.FeatureName(f));
                    _march.Refresh(c, G.ServerNowMs);
                    _march.AutoFillAndGo();
                    _continent.Select(f.X, f.Y);
                }).StartingIn(4000);
            if (Array.IndexOf(args, "-hospital") >= 0) OpenPage(PageHospital);
            if (Array.IndexOf(args, "-overview") >= 0) _root.schedule.Execute(OpenOverview).StartingIn(4000);
            int ti = Array.IndexOf(args, "-tile");
            if (ti >= 0 && ti + 2 < args.Length && int.TryParse(args[ti + 1], out var tx) && int.TryParse(args[ti + 2], out var ty))
                _root.schedule.Execute(() => { _continent.Select(tx, ty); _continent.FocusTile(tx, ty); }).StartingIn(3000);
            // 測試用：-marchtest <x> <y> 用自動配兵派第一位空閒主將出征
            int mti = Array.IndexOf(args, "-marchtest");
            if (mti >= 0 && mti + 2 < args.Length && int.TryParse(args[mti + 1], out var mx2) && int.TryParse(args[mti + 2], out var my2))
                _root.schedule.Execute(() => { _march.SetTarget(mx2, my2, "測試"); _march.Refresh(G.Predicted(), G.ServerNowMs); _march.AutoFillAndGo(); }).StartingIn(4000);
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
            Show(_game, inGame && !_introPlaying);
            if (inGame && !_introChecked)
            {
                _introChecked = true;
                if (ShouldPlayIntro(city)) StartIntro(city);
            }
            _world.SetVisible(inGame);
            SetWorldMode(inGame);
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
            var game = El("game");
            game.pickingMode = PickingMode.Ignore;

            var top = El("topbar");
            top.Add(Lbl("深淵王國", "brand"));
            top.Add(Lbl("永暗之喉", "brand-sub"));
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
            var nameRow = El("row");
            _shieldLabel = Lbl("", "shield-label");
            nameRow.Add(_shieldLabel);
            nameRow.Add(_playerName);
            player.Add(nameRow);
            var powerRow = El("row");
            powerRow.Add(_powerDelta);
            powerRow.Add(_troopTotal);
            player.Add(powerRow);

            top.Add(player);
            game.Add(top);

            // 左側：隊列；右側：功能按鈕
            var hud = Pass(El("hud"));
            var left = Pass(El("hud-left"));
            _quest = new QuestPanel();
            _quest.GoRequested += GoToQuest;
            _quest.ClaimRequested += () =>
            {
                var q = QuestLogic.Current(G.Predicted());
                G.ClaimQuest();
                if (q != null) ShowToast("任務完成！" + string.Join("、", new[] { DisplayNames.CarryText(q.Resources), DisplayNames.ItemsText(q.Items) }.Where(s => !string.IsNullOrEmpty(s))), false);
            };
            left.Add(_quest.Root);
            var buildPanel = El("panel", "hud-panel");
            buildPanel.Add(Lbl("建築隊列", "panel-title"));
            _buildQueue = new QueueView(QueueType.Building, OpenSpeedupDialog);
            buildPanel.Add(_buildQueue.Root);
            left.Add(buildPanel);
            var trainPanel = El("panel", "hud-panel");
            trainPanel.Add(Lbl("訓練隊列", "panel-title"));
            _hudTrainQueue = new QueueView(QueueType.Training, OpenSpeedupDialog);
            trainPanel.Add(_hudTrainQueue.Root);
            left.Add(trainPanel);
            _healPanel = El("panel", "hud-panel");
            var healHead = El("row");
            healHead.Add(Lbl("醫館", "panel-title", "grow"));
            _woundedLabel = Lbl("", "muted");
            healHead.Add(_woundedLabel);
            _healPanel.Add(healHead);
            _hudHealQueue = new QueueView(QueueType.Healing, OpenSpeedupDialog);
            _healPanel.Add(_hudHealQueue.Root);
            _healPanel.Add(Btn("前往醫館", () => OpenPage(PageHospital), "btn-small"));
            left.Add(_healPanel);
            var marchPanel = El("panel", "hud-panel");
            _marchList = new MarchList();
            marchPanel.Add(_marchList.Root);
            left.Add(marchPanel);
            hud.Add(left);
            hud.Add(Pass(El("spacer")));
            var right = Pass(El("hud-right"));
            for (int i = 0; i < HudPages; i++)
            {
                int idx = i;
                right.Add(Btn(PageNames[i], () => OpenPage(idx), "hud-btn"));
            }
            right.Add(Btn("全圖", OpenOverview, "hud-btn", "hud-btn-map"));
            hud.Add(right);
            game.Add(hud);

            // 畫面下方的選取資訊卡
            var selHost = Pass(El("sel-host"));
            _moveHint = Lbl("移動建築：移到想放的位置後按左鍵放下（綠色＝可以放），右鍵或 Esc 取消", "move-hint");
            selHost.Add(_moveHint);
            _surfaceSel = Pass(El());
            _continent = new ContinentPage(_world.Surface);
            _continent.MarchRequested += (x, y, title) => { _march.SetTarget(x, y, title); OpenPage(PageMarch); };
            _surfaceSel.Add(_continent.Root);
            selHost.Add(_surfaceSel);
            _buildingSel = El("panel", "sel-card");
            var selRow = El("row");
            _cardHost = El("grow");
            selRow.Add(_cardHost);
            var actions = El("sel-actions");
            actions.Add(Btn("✕", () => _selBuilding = null, "btn-small"));
            _moveBtn = Btn("移動", () => { if (_selBuilding.HasValue) _world.City.BeginMove(_selBuilding.Value); }, "btn-small");
            actions.Add(_moveBtn);
            _trainBtn = Btn("訓練", () => OpenPage(_selBuilding == BuildingType.Hospital ? PageHospital : PageBarracks), "btn-primary", "btn-small");
            actions.Add(_trainBtn);
            selRow.Add(actions);
            _buildingSel.Add(selRow);
            selHost.Add(_buildingSel);
            game.Add(selHost);
            foreach (var b in CityConfig.AllBuildings) _cards[b] = new BuildingCard(b);

            _world.City.BuildingClicked += t => _selBuilding = (int)t < 0 ? (BuildingType?)null : t;
            _world.City.MoveRequested += (t, x, y) => G.MoveBuilding(t, x, y);

            // 右下角：回城／出城
            _homeBtn = Btn("", ToggleHome, "home-btn");
            game.Add(_homeBtn);

            // 彈出面板
            _heroPage = new HeroPage();
            _expPage = new ExpeditionPage();
            _march = new MarchPanel();
            _march.Sent += ClosePage;
            _marchList.Focus += m =>
            {
                if (_world.Current != WorldScene.Mode.Surface) ToggleHome();
                var (fx, fy) = MarchLogic.CurrentTile(m, G.ServerNowMs);
                _continent.FocusTile(fx, fy);
            };
            _trainQueue = new QueueView(QueueType.Training, OpenSpeedupDialog);
            _training = new TrainingPanel(_trainQueue);
            var bag = El("panel");
            _itemList = El();
            bag.Add(_itemList);
            _healQueue = new QueueView(QueueType.Healing, OpenSpeedupDialog);
            _hospital = new HospitalPanel(_healQueue);
            _pageRoots = new[] { _heroPage.Root, _expPage.Root, _training.Root, bag, _march.Root, _hospital.Root };

            _pageOverlay = El("page-overlay");
            var window = El("page-window");
            var head = El("row", "page-head");
            _pageTitle = Lbl("", "section-title", "grow");
            head.Add(_pageTitle);
            head.Add(Btn("關閉", ClosePage, "btn-small"));
            window.Add(head);
            _pageBody = El("page-body");
            window.Add(_pageBody);
            _pageOverlay.Add(window);
            Show(_pageOverlay, false);
            game.Add(_pageOverlay);
            return game;
        }

        private static VisualElement Pass(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>滑鼠是否在介面元素上（是的話 3D 場景就不處理點擊與拖曳）。</summary>
        private bool IsPointerOverUi(Vector2 screen)
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(p) != null;
        }

        private WorldOverview _overview;
        private VisualElement _overviewModal;

        /// <summary>全區地圖：彈出整張大陸的俯視圖；點選位置就切到大地圖並把鏡頭移過去。</summary>
        private void OpenOverview()
        {
            if (_overview == null)
            {
                _overview = new WorldOverview();
                _overview.TilePicked += (x, y) =>
                {
                    if (_world.Current != WorldScene.Mode.Surface) ToggleHome();
                    _continent.FocusTile(x, y);
                    _overviewModal?.RemoveFromHierarchy();
                    _overviewModal = null;
                };
            }
            var box = El("dialog", "overview-dialog");
            var head = El("row");
            head.Add(Lbl("全區地圖", "panel-title", "grow"));
            head.Add(Btn("關閉", () => { _overviewModal?.RemoveFromHierarchy(); _overviewModal = null; }, "btn-small"));
            box.Add(head);
            box.Add(_overview.Root);
            _overviewModal = OpenModal(box);
            var city = G.Predicted();
            if (city != null) _overview.Refresh(city, G.Cities, G.ServerNowMs, _continent.Selected, SurfaceViewQuad());
            G.RequestCities();
        }

        /// <summary>大地圖鏡頭看到的地面範圍（畫面四角打到地面的格子座標）；不在大地圖時回傳 null。</summary>
        private Vector2[] SurfaceViewQuad()
        {
            if (_world == null || _world.Current != WorldScene.Mode.Surface) return null;
            var corners = new[] { new Vector2(0, Screen.height), new Vector2(Screen.width, Screen.height), new Vector2(Screen.width, 0), new Vector2(0, 0) };
            var quad = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                if (!_world.Ground(corners[i], out var hit)) return null;
                quad[i] = new Vector2(hit.x / SurfaceView.S + SurfaceConfig.Center, -hit.z / SurfaceView.S + SurfaceConfig.Center);
            }
            return quad;
        }

        private QuestPanel _quest;
        private bool _introChecked, _introPlaying;

        /// <summary>
        /// 新手開場只給新帳號看一次（還在第一個任務、主堡 1 級、還沒練過兵；看過記在這台電腦）。
        /// 測試用：啟動參數 -intro 一定播。
        /// </summary>
        private bool ShouldPlayIntro(CityState city)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-intro") >= 0) return true;
            if (!city.Surface.Placed || PlayerPrefs.GetInt("intro.seen." + G.PlayerId, 0) == 1) return false;
            return city.QuestIndex == 0 && city.GetLevel(BuildingType.Keep) == 1 && city.Stats.TroopsTrained == 0;
        }

        private void StartIntro(CityState city)
        {
            _introPlaying = true;
            Show(_game, false);
            ClosePage();
            var home = SurfaceView.CityPos(city.Surface.X, city.Surface.Y);
            IntroSequence.Play(gameObject, _root, _world, home, () =>
            {
                _introPlaying = false;
                PlayerPrefs.SetInt("intro.seen." + G.PlayerId, 1);
                PlayerPrefs.Save();
                // 回城內，任務欄閃幾下，提示從第一個任務開始
                _world.SetMode(WorldScene.Mode.City);
                Show(_game, true);
                _quest.Root.AddToClassList("quest-pulse");
                _root.schedule.Execute(() => _quest.Root.RemoveFromClassList("quest-pulse")).StartingIn(4500);
                ShowToast("從左上角的任務開始吧！", false);
            });
        }

        /// <summary>任務的「前往」：帶玩家到該去的地方（城內選取建築、打開面板、或在大地圖選最近的資源田／營地）。</summary>
        private void GoToQuest(QuestDef q)
        {
            var city = G.Predicted();
            if (city == null) return;
            switch (q.Goal)
            {
                case QuestGoal.BuildingLevel:
                case QuestGoal.ProducersLevel:
                    if (_world.Current != WorldScene.Mode.City) ToggleHome();
                    ClosePage();
                    // 四座生產建築：選最低的那一座
                    _selBuilding = q.Goal == QuestGoal.BuildingLevel ? q.Building
                        : new[] { BuildingType.Sawmill, BuildingType.Quarry, BuildingType.Farm, BuildingType.CrystalMine }.OrderBy(b => city.GetLevel(b)).First();
                    break;
                case QuestGoal.TroopsTrained:
                    OpenPage(PageBarracks);
                    break;
                case QuestGoal.HeroLevel:
                    OpenPage(PageHeroes);
                    break;
                case QuestGoal.ExpeditionCleared:
                    OpenPage(PageExpedition);
                    break;
                case QuestGoal.GatherTrips:
                case QuestGoal.CampsWon:
                {
                    if (!city.Surface.Placed) break;
                    if (_world.Current != WorldScene.Mode.Surface) ToggleHome();
                    ClosePage();
                    var (hx, hy) = SurfaceLogic.CityCenter(city.Surface);
                    var type = q.Goal == QuestGoal.GatherTrips ? FeatureType.ResourceField : FeatureType.MonsterCamp;
                    var f = SurfaceLogic.Features()
                        .Where(x => x.Type == type && SurfaceLogic.Zone(x.X, x.Y) == SurfaceLogic.Zone(hx, hy) && (type != FeatureType.MonsterCamp || x.Level == 1))
                        .OrderBy(x => SurfaceLogic.Dist2(hx, hy, x.X, x.Y)).FirstOrDefault();
                    if (f == null) break;
                    _continent.Select(f.X, f.Y);
                    _continent.FocusTile(f.X, f.Y);
                    ShowToast(type == FeatureType.ResourceField ? "選好最近的資源田了，按「出征」派部隊去採集" : "選好最近的魔物營地了，按「出征」派部隊去打", false);
                    break;
                }
                case QuestGoal.Power:
                    ShowToast("升級建築、訓練部隊、培養主將都會提高總戰力", false);
                    break;
            }
        }

        private void ToggleHome()
        {
            bool toSurface = _world.Current == WorldScene.Mode.City;
            _world.SetMode(toSurface ? WorldScene.Mode.Surface : WorldScene.Mode.City);
            _selBuilding = null;
            var city = G.Predicted();
            if (toSurface && city != null) _continent.CenterOnHome(city);
        }

        private void OpenPage(int page)
        {
            _page = page;
            _pageBody.Clear();
            _pageBody.Add(_pageRoots[page]);
            _pageTitle.text = PageNames[page];
            Show(_pageOverlay, true);
            _world.City.CancelMove();
        }

        private void ClosePage()
        {
            _page = -1;
            Show(_pageOverlay, false);
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
            bool shielded = PvpLogic.IsShielded(city, now);
            Show(_shieldLabel, shielded);
            if (shielded) _shieldLabel.text = "護盾 " + DisplayNames.Duration(city.ShieldUntilMs - now);
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

            bool inCity = _world.Current == WorldScene.Mode.City;
            _homeBtn.text = inCity ? "出城" : "回城";
            Show(_surfaceSel, !inCity);
            Show(_moveHint, inCity && _world.City.Moving != null);
            if (inCity)
            {
                _world.City.Refresh(city, _selBuilding);
                bool sel = _selBuilding.HasValue && _world.City.Moving == null;
                Show(_buildingSel, sel);
                if (sel)
                {
                    var card = _cards[_selBuilding.Value];
                    if (card.Root.parent != _cardHost)
                    {
                        _cardHost.Clear();
                        _cardHost.Add(card.Root);
                    }
                    card.Refresh(city);
                    bool barracks = _selBuilding == BuildingType.Barracks && city.GetLevel(BuildingType.Barracks) > 0;
                    Show(_trainBtn, barracks || _selBuilding == BuildingType.Hospital);
                    _trainBtn.text = barracks ? "訓練" : "治療";
                }
            }
            else
            {
                Show(_buildingSel, false);
                _continent.Refresh(city, now);
            }

            _quest.Refresh(city);
            _buildQueue.Refresh(city, now);
            _hudTrainQueue.Refresh(city, now);
            _hudHealQueue.Refresh(city, now);
            long woundedTotal = CityLogic.WoundedTotal(city);
            Show(_healPanel, woundedTotal > 0 || city.Healing != null);
            _woundedLabel.text = $"傷兵 {woundedTotal:N0}";
            _marchList.Refresh(city, now);
            switch (_page)
            {
                case PageHeroes: _heroPage.Refresh(city); break;
                case PageExpedition: _expPage.Refresh(city); break;
                case PageBarracks: _training.Refresh(city, now); break;
                case PageBag: RefreshItems(city); break;
                case PageMarch: _march.Refresh(city, now); break;
                case PageHospital: _hospital.Refresh(city, now); break;
            }
            if (_dialog != null) RefreshDialog(city, now);
            if (_overviewModal != null && _overviewModal.panel != null) _overview.Refresh(city, G.Cities, now, _continent.Selected, SurfaceViewQuad());
        }

        private void RefreshItems(CityState city)
        {
            var owned = ItemConfig.AllItems.Where(i => city.GetItemCount(i) > 0).ToList();
            string sig = string.Join(",", owned.Select(i => $"{i}:{city.GetItemCount(i)}"));
            if (sig == _itemSignature) return;
            _itemSignature = sig;
            _itemList.Clear();
            if (owned.Count == 0) _itemList.Add(Lbl("背包是空的", "muted"));
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
            long? finish = _dialogQueue == QueueType.Building ? city?.Upgrade?.FinishAtMs
                : _dialogQueue == QueueType.Healing ? city?.Healing?.FinishAtMs : city?.Training?.FinishAtMs;
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
                _idle = Lbl(queue == QueueType.Building ? "空閒中 — 點城內的建築開始升級" : queue == QueueType.Healing ? "沒有在治療" : "訓練隊列空閒中", "queue-idle");
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
                else if (_queue == QueueType.Healing && city.Healing != null)
                {
                    _title.text = $"治療中：{DisplayNames.TroopsText(city.Healing.Troops)}";
                    start = city.Healing.StartedAtMs;
                    finish = city.Healing.FinishAtMs;
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

        /// <summary>醫館：各兵種傷兵的滑桿、成本與時間、治療隊列。</summary>
        private sealed class HospitalPanel
        {
            public readonly VisualElement Root = El("panel");
            private readonly Label _cap, _time;
            private readonly VisualElement _none, _controls;
            private readonly Dictionary<TroopType, (VisualElement row, SliderInt slider, Label count)> _rows = new Dictionary<TroopType, (VisualElement, SliderInt, Label)>();
            private readonly CostRow _cost = new CostRow();
            private readonly Button _heal;
            private readonly QueueView _queue;

            public HospitalPanel(QueueView queue)
            {
                _queue = queue;
                var head = El("row");
                head.Add(Lbl("傷兵", "panel-title", "grow"));
                _cap = Lbl("", "muted");
                head.Add(_cap);
                Root.Add(head);
                Root.Add(Lbl("戰鬥中倒下的士兵有七成會被送來這裡（醫館滿了就直接陣亡）。治療花訓練成本與時間的一半。", "muted"));
                _none = Lbl("醫館裡沒有傷兵", "queue-idle");
                Root.Add(_none);
                _controls = El();
                foreach (var t in CityConfig.AllTroops)
                {
                    var row = El("row");
                    row.Add(Lbl(DisplayNames.Troops[t], "march-troop-name"));
                    var slider = new SliderInt(0, 0) { value = 0 };
                    slider.AddToClassList("grow");
                    slider.AddToClassList("count-slider");
                    row.Add(slider);
                    var count = Lbl("0", "march-troop-count");
                    row.Add(count);
                    _rows[t] = (row, slider, count);
                    _controls.Add(row);
                }
                var costRow = El("row");
                costRow.Add(Btn("全部", () => { foreach (var r in _rows.Values) r.slider.value = r.slider.highValue; }, "btn-small"));
                costRow.Add(_cost.Root);
                costRow.Add(El("spacer"));
                _time = Lbl("", "muted");
                costRow.Add(_time);
                _controls.Add(costRow);
                _heal = Btn("治療", Heal, "btn-primary");
                _heal.style.marginLeft = 0;
                _heal.style.marginTop = 8;
                _controls.Add(_heal);
                Root.Add(_controls);
                var queueBox = El();
                queueBox.style.marginTop = 14;
                queueBox.Add(_queue.Root);
                Root.Add(queueBox);
            }

            private Dictionary<TroopType, long> Selected()
            {
                var d = new Dictionary<TroopType, long>();
                foreach (var kv in _rows) if (kv.Value.slider.value > 0) d[kv.Key] = kv.Value.slider.value;
                return d;
            }

            private void Heal()
            {
                G.Heal(Selected());
                foreach (var r in _rows.Values) r.slider.value = 0;
            }

            public void Refresh(CityState city, long now)
            {
                long total = CityLogic.WoundedTotal(city);
                _cap.text = $"{total:N0} / {CityConfig.HospitalCapacity(city.GetLevel(BuildingType.Hospital)):N0}";
                Show(_none, total == 0);
                Show(_controls, total > 0);
                foreach (var t in CityConfig.AllTroops)
                {
                    var (row, slider, count) = _rows[t];
                    long w = city.Wounded.TryGetValue(t, out var v) ? v : 0;
                    Show(row, w > 0);
                    if (slider.highValue != (int)w) slider.highValue = (int)w;
                    if (slider.value > w) slider.value = (int)w;
                    count.text = $"{slider.value:N0}/{w:N0}";
                }
                var sel = Selected();
                _cost.Refresh(r => CityLogic.HealCost(sel, r), city);
                _time.text = "需時 " + DisplayNames.Duration(CityLogic.HealDurationMs(sel));
                var check = CityLogic.CanHeal(city, sel);
                _heal.SetEnabled(check == HealResult.Ok);
                _heal.text = check == HealResult.Ok ? $"治療 {sel.Values.Sum():N0} 名" : check == HealResult.QueueBusy ? "治療中" : check == HealResult.NotEnoughResources ? "資源不足" : "選擇要治療的傷兵";
                _queue.Refresh(city, now);
            }
        }

        /// <summary>進入遊戲後介面底色改成透明，讓 3D 場景透出來。</summary>
        private void SetWorldMode(bool on)
        {
            if (on == _worldMode) return;
            _worldMode = on;
            _root.style.backgroundImage = on ? new StyleBackground(StyleKeyword.None) : new StyleBackground(_gradient);
            _root.style.backgroundColor = on ? new StyleColor(Color.clear) : new StyleColor(StyleKeyword.Null);
        }

        /// <summary>背景：上方微亮（地表）往下沉入深淵的漸層。</summary>
        private static Texture2D MakeGradient()
        {
            const int h = 256;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var deep = new Color32(6, 9, 12, 255);
            var surface = new Color32(30, 44, 54, 255);
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
