using System.Collections.Generic;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.World;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// 大地圖畫面的介面部分：定期向伺服器要全服主城，交給 3D 的 SurfaceView 畫；
    /// 點到地點時在畫面下方顯示資訊卡（Root）。
    /// </summary>
    public sealed class ContinentPage
    {
        private static GameClient G => GameClient.Instance;
        private const float RefreshCitiesSeconds = 10f;

        public readonly VisualElement Root;
        private readonly SurfaceView _view;
        private readonly AbyssMap _map;
        private readonly Label _title, _sub, _desc, _owner;
        private int _selected = -1;
        private bool _centered;
        private float _nextFetch;
        private string _sig;

        public ContinentPage(SurfaceView view)
        {
            _view = view;
            _map = SurfaceConfig.Map();
            _view.NodeClicked += id => { _selected = id; _sig = null; };

            Root = El("panel", "sel-card");
            var head = El("row");
            _title = Lbl("", "queue-title", "grow");
            head.Add(_title);
            head.Add(Btn("✕", () => { _selected = -1; _sig = null; }, "btn-small"));
            Root.Add(head);
            _sub = Lbl("", "muted");
            Root.Add(_sub);
            _owner = Lbl("", "syn-line");
            Root.Add(_owner);
            _desc = Lbl("", "hero-bio");
            Root.Add(_desc);
        }

        public void Select(int id)
        {
            _selected = id;
            _sig = null;
        }

        /// <summary>切到大地圖時把鏡頭移到自己的城。</summary>
        public void CenterOnHome(CityState city)
        {
            if (city.Surface.Site >= 0)
                WorldScene.Instance.FocusOn(WorldScene.Mode.Surface, _view.NodePos(city.Surface.Site));
        }

        public void Refresh(CityState city)
        {
            if (Time.unscaledTime >= _nextFetch)
            {
                _nextFetch = Time.unscaledTime + RefreshCitiesSeconds;
                G.RequestCities();
            }
            if (!_centered && city.Surface.Site >= 0)
            {
                CenterOnHome(city);
                _centered = true;
            }

            var pins = G.Cities;
            string sig = pins.Count + ":" + _selected + ":" + city.Surface.Site + ":" + PinsHash(pins);
            if (sig == _sig) return;
            _sig = sig;

            var byPin = new Dictionary<int, CityPin>();
            foreach (var p in pins) byPin[p.Site] = p;
            _view.SetState(byPin, city.Surface.Site, _selected);

            Show(Root, _selected >= 0);
            if (_selected < 0) return;
            var n = _map.Nodes[_selected];
            byPin.TryGetValue(n.Id, out var pin);
            _title.text = pin != null ? $"{pin.Name} 的主城" : DisplayNames.SurfaceNodeName(n);
            _sub.text = $"{DisplayNames.Zones[SurfaceLogic.Zone(n)]} · 第 {n.Row + 1} 圈 · 地點 #{n.Id}";
            _owner.text = pin != null ? (pin.Mine ? "這是你的主城" : $"總戰力 {pin.Power:N0}") : "";
            _desc.text = pin != null ? "行軍、採集、攻打將在接下來的步驟加入。" : DisplayNames.NodeDescriptions[n.Type];
        }

        private static int PinsHash(List<CityPin> pins)
        {
            int h = 17;
            foreach (var p in pins) h = h * 31 + p.Site * 7 + (p.Name?.GetHashCode() ?? 0);
            return h;
        }
    }
}
