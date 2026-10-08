using System;
using System.Collections.Generic;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>大陸頁（地表）：俯視深淵周圍的同心環，所有玩家的主城、資源田、怪物營地、關隘。</summary>
    public sealed class ContinentPage
    {
        private static GameClient G => GameClient.Instance;
        private const float RefreshCitiesSeconds = 10f;

        public readonly VisualElement Root;
        private readonly AbyssMap _map;
        private readonly ScrollView _scroll;
        private readonly Canvas _canvas;
        private readonly Label _title, _sub, _desc, _owner, _stats;
        private int _selected = -1;
        private bool _centered;
        private float _nextFetch;
        private string _sig;

        public ContinentPage()
        {
            _map = SurfaceConfig.Map();
            Root = El("map-page");
            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _scroll.AddToClassList("map-scroll");
            _canvas = new Canvas(_map);
            _canvas.NodeClicked += id => { _selected = id; _sig = null; };
            _scroll.Add(_canvas);
            Root.Add(_scroll);

            var side = new ScrollView(ScrollViewMode.Vertical);
            side.AddToClassList("map-side");
            side.Add(Lbl("大陸 · 永暗之喉周邊", "panel-title"));
            _stats = Lbl("", "muted");
            side.Add(_stats);
            var info = El("panel", "map-info");
            _title = Lbl("點地圖上的地點查看", "queue-title");
            info.Add(_title);
            _sub = Lbl("", "muted");
            info.Add(_sub);
            _owner = Lbl("", "syn-line");
            info.Add(_owner);
            _desc = Lbl("", "hero-bio");
            info.Add(_desc);
            side.Add(info);

            var legend = El("panel");
            legend.Add(Lbl("圖例", "queue-title"));
            legend.Add(Lbl("城　玩家主城（金色是你的城）", "muted"));
            legend.Add(Lbl("地　空的主城用地", "muted"));
            legend.Add(Lbl("根層菌響　資源田（數字是等級）", "muted"));
            legend.Add(Lbl("怪　怪物營地", "muted"));
            legend.Add(Lbl("隘　山谷關隘（分區之間唯一的通道）", "muted"));
            legend.Add(Lbl("降　垂降口（深淵入口，之後的賽季開放）", "muted"));
            legend.Add(Lbl("內圈崖緣最肥最危險，外圈是新手區", "muted"));
            side.Add(legend);
            Root.Add(side);
        }

        public void Refresh(CityState city)
        {
            if (Time.unscaledTime >= _nextFetch)
            {
                _nextFetch = Time.unscaledTime + RefreshCitiesSeconds;
                G.RequestCities();
            }

            var pins = G.Cities;
            string sig = pins.Count + ":" + _selected + ":" + city.Surface.Site;
            if (sig == _sig) return;
            _sig = sig;

            var byPin = new Dictionary<int, CityPin>();
            foreach (var p in pins) byPin[p.Site] = p;
            _canvas.SetState(byPin, city.Surface.Site, _selected);
            _stats.text = $"地表共有 {pins.Count} 座主城";

            if (!_centered && city.Surface.Site >= 0)
            {
                var me = _map.Nodes[city.Surface.Site];
                _scroll.schedule.Execute(() =>
                    _scroll.scrollOffset = new Vector2(Mathf.Max(0, me.X - 380), Mathf.Max(0, me.Y - 300))).StartingIn(50);
                _centered = true;
            }

            if (_selected < 0)
            {
                _title.text = "點地圖上的地點查看";
                _sub.text = _owner.text = _desc.text = "";
                return;
            }
            var n = _map.Nodes[_selected];
            byPin.TryGetValue(n.Id, out var pin);
            _title.text = pin != null ? $"{pin.Name} 的主城" : DisplayNames.SurfaceNodeName(n);
            _sub.text = $"{DisplayNames.Zones[SurfaceLogic.Zone(n)]} · 第 {n.Row + 1} 圈 · 地點 #{n.Id}";
            _owner.text = pin != null ? (pin.Mine ? "這是你的主城" : $"總戰力 {pin.Power:N0}") : "";
            _desc.text = pin != null ? "行軍、採集、攻打將在接下來的步驟加入。" : DisplayNames.NodeDescriptions[n.Type];
        }

        /// <summary>俯視畫布：深淵、分區色帶、道路、地點與主城。</summary>
        private sealed class Canvas : VisualElement
        {
            private readonly AbyssMap _map;
            private readonly Dictionary<int, Label> _labels = new Dictionary<int, Label>();
            private readonly Dictionary<int, Label> _names = new Dictionary<int, Label>();
            private Dictionary<int, CityPin> _pins = new Dictionary<int, CityPin>();
            private int _mine = -1, _selected = -1;
            public event Action<int> NodeClicked;

            public Canvas(AbyssMap map)
            {
                _map = map;
                AddToClassList("continent-canvas");
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
                    float bestD = 22 * 22;
                    foreach (var n in _map.Nodes)
                    {
                        float dx = n.X - e.localPosition.x, dy = n.Y - e.localPosition.y;
                        float d = dx * dx + dy * dy;
                        if (d < bestD) { bestD = d; best = n.Id; }
                    }
                    if (best >= 0) NodeClicked?.Invoke(best);
                });
            }

            public void SetState(Dictionary<int, CityPin> pins, int mine, int selected)
            {
                _pins = pins;
                _mine = mine;
                _selected = selected;
                foreach (var n in _map.Nodes)
                {
                    _pins.TryGetValue(n.Id, out var pin);
                    _labels[n.Id].text = pin != null ? "城" : DisplayNames.SurfaceGlyph(n);
                    if (pin != null && !_names.ContainsKey(n.Id))
                    {
                        var name = Lbl("", "city-name");
                        name.pickingMode = PickingMode.Ignore;
                        name.style.left = n.X - 50;
                        name.style.top = n.Y + 14;
                        _names[n.Id] = name;
                        Add(name);
                    }
                    if (_names.TryGetValue(n.Id, out var nl))
                    {
                        Show(nl, pin != null);
                        if (pin != null) nl.text = pin.Name;
                        nl.EnableInClassList("mine", n.Id == mine);
                    }
                }
                MarkDirtyRepaint();
            }

            private static Color C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

            private void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                var center = new Vector2(_map.Width / 2f, _map.Height / 2f);

                // 分區色帶（由外往內畫）
                void Disc(float r, Color c)
                {
                    p.BeginPath();
                    p.Arc(center, r, Angle.Degrees(0), Angle.Degrees(360));
                    p.fillColor = c;
                    p.Fill();
                }
                float half = SurfaceConfig.RingSpacing / 2f;
                Disc(SurfaceConfig.RingRadius(SurfaceConfig.Rings - 1) + 60, C(54, 50, 44));  // 山谷屏障
                Disc(SurfaceConfig.RingRadius(SurfaceConfig.Rings - 1) + half, C(46, 62, 44)); // 外環
                Disc(SurfaceConfig.RingRadius(4) + half, C(58, 66, 46));                       // 中環
                Disc(SurfaceConfig.RingRadius(1) + half, C(84, 70, 48));                       // 崖緣
                // 深淵：一圈圈往下的黑
                for (int i = 0; i < 6; i++) Disc(SurfaceConfig.PitRadius + 20 - i * 18, C(20 - i * 3, 18 - i * 3, 24 - i * 3));

                // 道路
                foreach (var e in _map.Edges)
                {
                    var a = _map.Nodes[e.A];
                    var b = _map.Nodes[e.B];
                    p.BeginPath();
                    p.MoveTo(new Vector2(a.X, a.Y));
                    p.LineTo(new Vector2(b.X, b.Y));
                    bool pass = a.Type == NodeType.Pass && b.Type == NodeType.Pass && a.Row != b.Row;
                    p.lineWidth = pass ? 5 : 3;
                    p.strokeColor = pass ? C(230, 200, 120, 220) : C(150, 140, 110, 150);
                    p.Stroke();
                }

                // 地點
                foreach (var n in _map.Nodes)
                {
                    bool city = _pins.ContainsKey(n.Id);
                    float r = city ? 15 : n.Type == NodeType.Gate ? 14 : 11;
                    p.BeginPath();
                    p.Arc(new Vector2(n.X, n.Y), r, Angle.Degrees(0), Angle.Degrees(360));
                    p.fillColor = city ? (n.Id == _mine ? C(214, 170, 88) : C(150, 80, 70))
                        : n.Type == NodeType.ResourceField ? ResColor(n.Resource)
                        : n.Type == NodeType.MonsterCamp ? C(140, 50, 50)
                        : n.Type == NodeType.Pass ? C(200, 170, 90)
                        : n.Type == NodeType.Gate ? C(40, 40, 60)
                        : C(70, 76, 70);
                    p.Fill();
                    p.lineWidth = n.Id == _selected ? 4 : 1.5f;
                    p.strokeColor = n.Id == _selected ? C(255, 240, 180) : C(20, 20, 20, 160);
                    p.Stroke();
                }
            }

            private static Color ResColor(ResourceType r)
            {
                switch (r)
                {
                    case ResourceType.Wood: return C(150, 104, 58);
                    case ResourceType.Stone: return C(118, 126, 142);
                    case ResourceType.Food: return C(92, 140, 52);
                    default: return C(56, 120, 200);
                }
            }
        }
    }
}
