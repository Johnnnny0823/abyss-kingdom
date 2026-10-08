using System;
using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.World;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>深淵頁：第 1 層的層網狀地圖（迷霧、力場、節點），點節點看資訊、派主將帶兵出征、召回。</summary>
    public sealed class MapPage
    {
        private static GameClient G => GameClient.Instance;

        public readonly VisualElement Root;
        private readonly AbyssMap _map;
        private readonly MapCanvas _canvas;
        private readonly ScrollView _scroll;
        private readonly Label _title, _sub, _desc, _status, _progress;
        // 出征面板
        private readonly VisualElement _marchBox, _heroRow, _marchList;
        private readonly Dictionary<TroopType, SliderInt> _troopSliders = new Dictionary<TroopType, SliderInt>();
        private readonly Dictionary<TroopType, Label> _troopLabels = new Dictionary<TroopType, Label>();
        private readonly Label _eta;
        private readonly Button _marchBtn;
        private HeroId? _marchHero;
        private string _heroSig, _listSig;
        private int _selected = -1;
        private string _signature;
        private bool _centered;
        private readonly VisualElement _view3d;
        private readonly Button _modeBtn;
        private readonly AbyssWorld _world;
        private bool _tabActive;

        /// <summary>目前是 3D 剖面（預設）還是 2D 平面地圖。</summary>
        public bool Is3D { get; private set; } // 3D 剖面已停用（深淵改成和地表一樣的大地圖），只保留 2D

        public MapPage()
        {
            _map = MapConfig.Layer(1);
            Root = El("map-page");

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _scroll.AddToClassList("map-scroll");
            _canvas = new MapCanvas(_map);
            _canvas.NodeClicked += id => { _selected = id; _signature = null; };
            _scroll.Add(_canvas);
            Root.Add(_scroll);

            // 3D 剖面：透明的視窗區，3D 場景從底下透出來
            _view3d = El("map-view3d");
            Root.Add(_view3d);
            _world = AbyssWorld.Instance ?? AbyssWorld.Create();
            _world.NodeClicked += id => { _selected = id; _signature = null; };
            _world.IsPointerOverViewport = screen =>
            {
                if (_view3d.panel == null || !Is3D) return false;
                var p = RuntimePanelUtils.ScreenToPanel(_view3d.panel, new Vector2(screen.x, Screen.height - screen.y));
                return _view3d.panel.Pick(p) == _view3d;
            };

            var side = new ScrollView(ScrollViewMode.Vertical);
            side.AddToClassList("map-side");
            side.Add(Lbl("第一層 · 餘燼緣野", "panel-title"));
            _modeBtn = Btn("", () => { Is3D = !Is3D; SetTabActive(_tabActive); }, "btn-small");
            Show(_modeBtn, false);
            _modeBtn.style.marginLeft = 0;
            _modeBtn.style.marginBottom = 6;
            _modeBtn.style.alignSelf = Align.FlexStart;
            side.Add(_modeBtn);
            _progress = Lbl("", "muted");
            side.Add(_progress);
            var info = El("panel", "map-info");
            _title = Lbl("點地圖上的節點查看", "queue-title");
            info.Add(_title);
            _sub = Lbl("", "muted");
            info.Add(_sub);
            _desc = Lbl("", "hero-bio");
            info.Add(_desc);
            _status = Lbl("", "syn-line");
            info.Add(_status);
            _marchBox = El("march-box");
            _marchBox.Add(Lbl("出征", "queue-title"));
            _heroRow = El("row");
            _heroRow.style.flexWrap = Wrap.Wrap;
            _marchBox.Add(_heroRow);
            foreach (var t in CityConfig.AllTroops)
            {
                var row = El("row");
                var name = Lbl(DisplayNames.Troops[t], "march-troop-name");
                row.Add(name);
                var slider = new SliderInt(0, 0) { value = 0 };
                slider.AddToClassList("grow");
                slider.AddToClassList("count-slider");
                row.Add(slider);
                var count = Lbl("0", "march-troop-count");
                row.Add(count);
                _troopSliders[t] = slider;
                _troopLabels[t] = count;
                _marchBox.Add(row);
            }
            var autoRow = El("row");
            autoRow.Add(Btn("自動配兵", AutoFill, "btn-small"));
            autoRow.Add(El("spacer"));
            _eta = Lbl("", "muted");
            autoRow.Add(_eta);
            _marchBox.Add(autoRow);
            _marchBtn = Btn("出征", StartMarch, "btn-primary");
            _marchBtn.style.marginLeft = 0;
            _marchBtn.style.marginTop = 6;
            _marchBox.Add(_marchBtn);
            info.Add(_marchBox);
            side.Add(info);

            var marches = El("panel");
            marches.Add(Lbl("行軍", "queue-title"));
            _marchList = El();
            marches.Add(_marchList);
            side.Add(marches);

            var legend = El("panel");
            legend.Add(Lbl("圖例", "queue-title"));
            foreach (NodeType t in Enum.GetValues(typeof(NodeType)))
                if ((int)t <= (int)NodeType.GuardianLair) // 只列深淵的節點種類
                legend.Add(Lbl($"{DisplayNames.NodeGlyph(t, Element.None)}　{DisplayNames.NodeTypes[t]}", "muted"));
            legend.Add(Lbl("紫色廊道：遮蔽力場（看不到另一頭）", "muted"));
            legend.Add(Lbl("紅色廊道：封鎖力場（無法通過）", "muted"));
            legend.Add(Lbl("3D：拖曳平移、滾輪上下、Ctrl＋滾輪縮放", "muted"));
            side.Add(legend);
            // 測試用：-select <節點> 直接選取
            var args = Environment.GetCommandLineArgs();
            int si = Array.IndexOf(args, "-select");
            if (si >= 0 && si + 1 < args.Length && int.TryParse(args[si + 1], out var sel)) _selected = sel;
            Root.Add(side);
        }

        public void Refresh(CityState city)
        {
            var explored = city.Map.Explored;
            long now = G.ServerNowMs;
            UpdateMarches(city, now);
            string sig = explored.Count + ":" + (explored.Count > 0 ? explored[explored.Count - 1] : -1) + ":" + _selected;
            if (sig == _signature) { UpdateMarchUI(city, now); return; }
            _signature = sig;

            var visible = MapLogic.Visible(_map, city.Map);
            var exploredSet = new HashSet<int>(explored);
            _canvas.SetState(exploredSet, visible, _selected);
            _world.SetState(exploredSet, visible, _selected, n => DisplayNames.NodeGlyph(n.Type, n.Element));
            _progress.text = $"已探索 {explored.Count} / {_map.Nodes.Count} 個節點";

            if (!_centered && explored.Count > 0)
            {
                // 第一次進入：把畫面對準自己的垂降口
                var start = _map.Nodes[explored[0]];
                _scroll.schedule.Execute(() =>
                    _scroll.scrollOffset = new Vector2(Mathf.Max(0, start.X - 330), Mathf.Max(0, start.Y - 120))).StartingIn(50);
                _centered = true;
            }

            if (_selected < 0 || !visible.Contains(_selected))
            {
                _title.text = "點地圖上的節點查看";
                _sub.text = _desc.text = _status.text = "";
                Show(_marchBox, false);
                return;
            }

            var n = _map.Nodes[_selected];
            bool known = explored.Contains(_selected);
            _title.text = known ? DisplayNames.NodeName(n) : "未知的洞室";
            _sub.text = $"深度 第 {n.Row} 段　· 節點 #{n.Id}";
            _desc.text = known ? DisplayNames.NodeDescriptions[n.Type] : "迷霧籠罩。派部隊前往才知道裡面有什麼。";
            UpdateMarchUI(city, now);
        }

        /// <summary>切換頁籤或 2D／3D 時呼叫：決定 3D 場景要不要顯示。</summary>
        public void SetTabActive(bool active)
        {
            _tabActive = active;
            Show(_scroll, !Is3D);
            Show(_view3d, Is3D);
            _modeBtn.text = Is3D ? "切換成 2D 平面地圖" : "切換成 3D 剖面";
            _world.SetActive(active && Is3D);
        }

        private CityState _lastCity;

        /// <summary>行軍標記位置與行軍列表（每次刷新都更新，讓標記平滑移動、倒數跳動）。</summary>
        private void UpdateMarches(CityState city, long now)
        {
            _lastCity = city;
            var marks = new List<(Vector2 pos, bool returning)>();
            foreach (var m in city.Map.Marches) marks.Add((MarchPos(m, now), m.Returning));
            _canvas.SetMarches(marks);
            _world.SetMarches(marks);

            string sig = string.Join(",", city.Map.Marches.Select(m => $"{m.Id}:{m.Stationed}:{m.Returning}"));
            if (sig != _listSig)
            {
                _listSig = sig;
                _marchList.Clear();
                if (city.Map.Marches.Count == 0) _marchList.Add(Lbl("沒有行軍。選一個節點，派主將出征。", "muted"));
                foreach (var m in city.Map.Marches)
                {
                    var row = El("item-row");
                    var col = El("grow");
                    col.Add(Lbl($"{DisplayNames.Heroes[m.Hero]} · {m.TotalTroops():N0} 兵", "item-name"));
                    var st = Lbl("", "muted");
                    st.name = "status";
                    col.Add(st);
                    row.Add(col);
                    var id = m.Id;
                    if (m.Stationed) row.Add(Btn("召回", () => G.RecallMarch(id), "btn-small", "btn-danger"));
                    else row.Add(Btn("查看", () => { _selected = m.Destination; _signature = null; }, "btn-small"));
                    row.userData = m.Id;
                    _marchList.Add(row);
                }
            }
            foreach (var row in _marchList.Children())
            {
                if (!(row.userData is long id)) continue;
                var m = city.Map.Marches.Find(x => x.Id == id);
                var st = row.Q<Label>("status");
                if (m == null || st == null) continue;
                string where = city.Map.Explored.Contains(m.Destination) ? DisplayNames.NodeName(_map.Nodes[m.Destination]) : "未知的洞室"; // 不洩漏迷霧裡的內容
                st.text = m.Stationed ? $"駐紮於 {where}"
                    : m.Returning ? $"返回垂降口　剩 {DisplayNames.Duration(m.ArriveAt - now)}"
                    : $"前往 {where}　剩 {DisplayNames.Duration(m.ArriveAt - now)}";
            }
        }

        private Vector2 MarchPos(March m, long now)
        {
            var (seg, pm) = MarchLogic.Position(m, now);
            var a = _map.Nodes[m.Path[seg]];
            if (seg + 1 >= m.Path.Count) return new Vector2(a.X, a.Y);
            var b = _map.Nodes[m.Path[seg + 1]];
            return Vector2.Lerp(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), pm / 1000f);
        }

        /// <summary>出征面板：可選的主將、各兵種滑桿（上限＝兵營數量與統率）、路徑與預估時間。</summary>
        private void UpdateMarchUI(CityState city, long now)
        {
            if (_selected < 0 || !MapLogic.Visible(_map, city.Map).Contains(_selected))
            {
                Show(_marchBox, false);
                return;
            }
            bool known = city.Map.Explored.Contains(_selected);
            Show(_marchBox, _selected != city.Map.HomeGate);
            var busy = new HashSet<HeroId>(city.Map.Marches.Select(m => m.Hero));
            var free = city.Heroes.Keys.Where(h => !busy.Contains(h)).OrderBy(h => (int)h).ToList();
            if (_marchHero == null || busy.Contains(_marchHero.Value)) _marchHero = free.Count > 0 ? free[0] : (HeroId?)null;

            string heroSig = string.Join(",", free) + "|" + _marchHero;
            if (heroSig != _heroSig)
            {
                _heroSig = heroSig;
                _heroRow.Clear();
                foreach (var h in free)
                {
                    var chip = Lbl($"{DisplayNames.Heroes[h]} · 統率 {PowerLogic.TotalStats(city, h).Leadership:N0}", "assign-chip");
                    chip.EnableInClassList("on", h == _marchHero);
                    var hid = h;
                    chip.RegisterCallback<ClickEvent>(_ => { _marchHero = hid; _heroSig = null; });
                    _heroRow.Add(chip);
                }
                if (free.Count == 0) _heroRow.Add(Lbl("所有主將都在行軍中", "muted"));
            }

            long leadership = _marchHero.HasValue ? PowerLogic.TotalStats(city, _marchHero.Value).Leadership : 0;
            long total = 0;
            var troops = new Dictionary<TroopType, long>();
            foreach (var t in CityConfig.AllTroops)
            {
                var s = _troopSliders[t];
                int max = (int)Math.Min(city.GetTroops(t), leadership);
                if (s.highValue != max) s.highValue = max;
                if (s.value > max) s.value = max;
                _troopLabels[t].text = $"{s.value:N0}/{city.GetTroops(t):N0}";
                troops[t] = s.value;
                total += s.value;
            }

            var path = MarchLogic.FindPath(_map, city.Map, city.Map.HomeGate, _selected);
            string reason = null;
            if (_marchHero == null) reason = "沒有空閒的主將";
            else if (path == null) reason = "到不了：只能走已探索的廊道（終點可以是相鄰的未知節點），封鎖力場要繞路";
            else if (total <= 0) reason = city.Troops.Values.Sum() == 0 ? "兵營沒有部隊，先到城鎮訓練" : "至少帶一名士兵";
            else if (total > leadership) reason = $"超過統率（{total:N0} / {leadership:N0}）";
            else if (city.Map.Marches.Count >= MarchConfig.MaxMarches) reason = $"同時最多 {MarchConfig.MaxMarches} 支行軍";

            if (path != null && total > 0)
            {
                var times = MarchLogic.Schedule(_map, path, now, MarchLogic.SpeedPercent(troops));
                _eta.text = $"路程 {path.Count - 1} 段 · 約 {DisplayNames.Duration(times[times.Count - 1] - now)}";
            }
            else _eta.text = "";

            _status.text = reason ?? (known ? "可以派部隊駐紮在這裡。" : "抵達後就會揭開迷霧。");
            _marchBtn.SetEnabled(reason == null);
            _marchBtn.text = reason == null ? $"出征（{total:N0} 兵）" : "無法出征";
        }

        private void AutoFill()
        {
            var city = _lastCity;
            if (city == null || _marchHero == null) return;
            HeroConfig.TryGet(_marchHero.Value, out var def);
            long left = PowerLogic.TotalStats(city, _marchHero.Value).Leadership;
            foreach (var s in _troopSliders.Values) s.value = 0;
            // 專精兵種優先，其餘依序補滿統率
            var order = new List<TroopType> { def.Specialty };
            order.AddRange(CityConfig.AllTroops.Where(t => t != def.Specialty));
            foreach (var t in order)
            {
                long take = Math.Min(left, city.GetTroops(t));
                _troopSliders[t].value = (int)take;
                left -= take;
            }
        }

        private void StartMarch()
        {
            if (_selected < 0 || _marchHero == null) return;
            var troops = new Dictionary<TroopType, long>();
            foreach (var pair in _troopSliders) if (pair.Value.value > 0) troops[pair.Key] = pair.Value.value;
            G.StartMarch(_marchHero.Value, troops, _selected);
            foreach (var s in _troopSliders.Values) s.value = 0;
        }

        /// <summary>地圖畫布：Painter2D 畫廊道、力場、迷霧光暈與節點；節點上的字用絕對定位的 Label。</summary>
        private sealed class MapCanvas : VisualElement
        {
            private readonly AbyssMap _map;
            private readonly Dictionary<int, Label> _labels = new Dictionary<int, Label>();
            private HashSet<int> _explored = new HashSet<int>(), _visible = new HashSet<int>();
            private int _selected = -1;
            private List<(Vector2 pos, bool returning)> _marches = new List<(Vector2, bool)>();
            public event Action<int> NodeClicked;

            public void SetMarches(List<(Vector2 pos, bool returning)> marches)
            {
                if (marches.Count == 0 && _marches.Count == 0) return;
                _marches = marches;
                MarkDirtyRepaint();
            }

            public MapCanvas(AbyssMap map)
            {
                _map = map;
                AddToClassList("map-canvas");
                style.width = map.Width;
                style.height = map.Height;
                generateVisualContent += Draw;
                foreach (var n in map.Nodes)
                {
                    var l = Lbl("", "map-node-label");
                    l.pickingMode = PickingMode.Ignore;
                    l.style.left = n.X - 15;
                    l.style.top = n.Y - 15;
                    _labels[n.Id] = l;
                    Add(l);
                }
                RegisterCallback<PointerDownEvent>(e =>
                {
                    int best = -1;
                    float bestD = 26 * 26;
                    foreach (var id in _visible)
                    {
                        var n = _map.Nodes[id];
                        float dx = n.X - e.localPosition.x, dy = n.Y - e.localPosition.y;
                        float d = dx * dx + dy * dy;
                        if (d < bestD) { bestD = d; best = id; }
                    }
                    if (best >= 0) NodeClicked?.Invoke(best);
                });
            }

            public void SetState(HashSet<int> explored, HashSet<int> visible, int selected)
            {
                _explored = explored;
                _visible = visible;
                _selected = selected;
                foreach (var pair in _labels)
                {
                    var n = _map.Nodes[pair.Key];
                    bool vis = visible.Contains(pair.Key), known = explored.Contains(pair.Key);
                    Show(pair.Value, vis);
                    pair.Value.text = known ? DisplayNames.NodeGlyph(n.Type, n.Element) : "?";
                    pair.Value.EnableInClassList("unknown", !known);
                }
                MarkDirtyRepaint();
            }

            private static Color C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

            private static Color NodeColor(MapNode n)
            {
                switch (n.Type)
                {
                    case NodeType.Gate: return C(214, 170, 88);
                    case NodeType.MonsterNest: return C(170, 60, 60);
                    case NodeType.ShaftHub: return C(60, 170, 170);
                    case NodeType.GuardianLair: return C(200, 40, 60);
                    case NodeType.Fissure:
                        switch (n.Element)
                        {
                            case Element.Wood: return C(96, 176, 96);
                            case Element.Fire: return C(226, 96, 70);
                            case Element.Earth: return C(198, 156, 80);
                            case Element.Metal: return C(206, 208, 218);
                            default: return C(80, 146, 226);
                        }
                    default: return C(80, 92, 130);
                }
            }

            private void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;

                // 迷霧中的光：已探索節點周圍微亮
                foreach (var id in _explored)
                {
                    var n = _map.Nodes[id];
                    p.BeginPath();
                    p.Arc(new Vector2(n.X, n.Y), 78, Angle.Degrees(0), Angle.Degrees(360));
                    p.fillColor = C(30, 44, 86, 46);
                    p.Fill();
                }

                // 廊道：至少一端已探索才畫
                foreach (var e in _map.Edges)
                {
                    bool ka = _explored.Contains(e.A), kb = _explored.Contains(e.B);
                    if (!ka && !kb) continue;
                    var a = _map.Nodes[e.A];
                    var b = _map.Nodes[e.B];
                    var from = new Vector2(a.X, a.Y);
                    var to = new Vector2(b.X, b.Y);
                    bool otherVisible = _visible.Contains(ka ? e.B : e.A);
                    if (e.Field == ForceField.Veil && !(ka && kb) && !otherVisible)
                        to = ka ? Vector2.Lerp(from, to, 0.45f) : Vector2.Lerp(to, from, 0.45f); // 遮蔽力場：只看得到一半
                    if (!ka) { var t = from; from = to; to = t; }

                    p.BeginPath();
                    p.MoveTo(from);
                    p.LineTo(to);
                    p.lineCap = LineCap.Round;
                    p.lineWidth = ka && kb ? 5 : 3;
                    p.strokeColor = e.Field == ForceField.Veil ? C(150, 100, 230, 200)
                        : e.Field == ForceField.Seal ? C(220, 70, 80, 220)
                        : ka && kb ? C(150, 160, 200, 220) : C(90, 100, 140, 160);
                    p.Stroke();

                    if (e.Field == ForceField.Seal)
                    {
                        // 封鎖力場：廊道中間畫一道橫槓
                        var mid = Vector2.Lerp(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), 0.5f);
                        var dir = (new Vector2(b.X, b.Y) - new Vector2(a.X, a.Y)).normalized;
                        var nrm = new Vector2(-dir.y, dir.x) * 12;
                        p.BeginPath();
                        p.MoveTo(mid - nrm);
                        p.LineTo(mid + nrm);
                        p.lineWidth = 5;
                        p.strokeColor = C(250, 90, 100);
                        p.Stroke();
                    }
                }

                // 節點
                foreach (var id in _visible)
                {
                    var n = _map.Nodes[id];
                    bool known = _explored.Contains(id);
                    float r = n.Type == NodeType.GuardianLair ? 22 : n.Type == NodeType.ShaftHub ? 19 : 15;
                    p.BeginPath();
                    p.Arc(new Vector2(n.X, n.Y), r, Angle.Degrees(0), Angle.Degrees(360));
                    p.fillColor = known ? NodeColor(n) : C(28, 34, 58);
                    p.Fill();
                    p.lineWidth = id == _selected ? 4 : 2;
                    p.strokeColor = id == _selected ? C(255, 236, 170) : known ? C(230, 232, 245, 180) : C(90, 100, 140);
                    p.Stroke();
                }

                // 行軍：發光的小旗點（返回中為藍色）
                foreach (var (pos, returning) in _marches)
                {
                    p.BeginPath();
                    p.Arc(pos, 9, Angle.Degrees(0), Angle.Degrees(360));
                    p.fillColor = returning ? C(110, 180, 250) : C(255, 214, 120);
                    p.Fill();
                    p.lineWidth = 3;
                    p.strokeColor = C(20, 20, 30);
                    p.Stroke();
                }
            }
        }
    }
}
