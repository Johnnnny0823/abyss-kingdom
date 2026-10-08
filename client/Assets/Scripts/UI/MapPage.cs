using System;
using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>深淵頁：第 1 層的層網狀地圖（迷霧、力場、節點），點節點看資訊、派斥候探路。</summary>
    public sealed class MapPage
    {
        private static GameClient G => GameClient.Instance;

        public readonly VisualElement Root;
        private readonly AbyssMap _map;
        private readonly MapCanvas _canvas;
        private readonly ScrollView _scroll;
        private readonly Label _title, _sub, _desc, _status, _progress;
        private readonly Button _explore;
        private int _selected = -1;
        private string _signature;
        private bool _centered;

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

            var side = El("map-side");
            side.Add(Lbl("第一層 · 餘燼緣野", "panel-title"));
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
            _explore = Btn($"斥候探路（菌糧 {MapConfig.ExploreFoodCost}）", () => { if (_selected >= 0) G.ExploreNode(_selected); }, "btn-primary");
            _explore.style.marginLeft = 0;
            _explore.style.marginTop = 8;
            info.Add(_explore);
            side.Add(info);

            var legend = El("panel");
            legend.Add(Lbl("圖例", "queue-title"));
            foreach (NodeType t in Enum.GetValues(typeof(NodeType)))
                legend.Add(Lbl($"{DisplayNames.NodeGlyph(t, Element.None)}　{DisplayNames.NodeTypes[t]}", "muted"));
            legend.Add(Lbl("紫色廊道：遮蔽力場（看不到另一頭）", "muted"));
            legend.Add(Lbl("紅色廊道：封鎖力場（無法通過）", "muted"));
            side.Add(legend);
            Root.Add(side);
        }

        public void Refresh(CityState city)
        {
            var explored = city.Map.Explored;
            string sig = explored.Count + ":" + (explored.Count > 0 ? explored[explored.Count - 1] : -1) + ":" + _selected;
            if (sig == _signature) { UpdateButton(city); return; }
            _signature = sig;

            var visible = MapLogic.Visible(_map, city.Map);
            _canvas.SetState(new HashSet<int>(explored), visible, _selected);
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
                Show(_explore, false);
                return;
            }

            var n = _map.Nodes[_selected];
            bool known = explored.Contains(_selected);
            _title.text = known ? DisplayNames.NodeName(n) : "未知的洞室";
            _sub.text = $"深度 第 {n.Row} 段　· 節點 #{n.Id}";
            _desc.text = known ? DisplayNames.NodeDescriptions[n.Type] : "迷霧籠罩。派斥候探路才知道裡面有什麼。";
            UpdateButton(city);
        }

        private void UpdateButton(CityState city)
        {
            if (_selected < 0) return;
            bool known = city.Map.Explored.Contains(_selected);
            if (known)
            {
                _status.text = "已探索。行軍、採集、戰鬥將在下一步加入。";
                Show(_explore, false);
                return;
            }
            // 判斷能不能探：有一條非封鎖的廊道連到已探索節點
            bool reachable = false, sealedOnly = false;
            foreach (var ei in _map.Nodes[_selected].Edges)
            {
                var e = _map.Edges[ei];
                if (!city.Map.Explored.Contains(e.Other(_selected))) continue;
                if (e.Field == ForceField.Seal) sealedOnly = true; else reachable = true;
            }
            Show(_explore, true);
            _explore.SetEnabled(reachable && city.GetResource(ResourceType.Food) >= MapConfig.ExploreFoodCost);
            _status.text = reachable ? "可以派斥候探路。" : sealedOnly ? "通往這裡的廊道被封鎖力場擋住，需要找其他路。" : "距離太遠，先探索相鄰的節點。";
        }

        /// <summary>地圖畫布：Painter2D 畫廊道、力場、迷霧光暈與節點；節點上的字用絕對定位的 Label。</summary>
        private sealed class MapCanvas : VisualElement
        {
            private readonly AbyssMap _map;
            private readonly Dictionary<int, Label> _labels = new Dictionary<int, Label>();
            private HashSet<int> _explored = new HashSet<int>(), _visible = new HashSet<int>();
            private int _selected = -1;
            public event Action<int> NodeClicked;

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
            }
        }
    }
}
