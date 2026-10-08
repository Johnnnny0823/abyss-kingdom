using System;
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
    /// 點格子時在畫面下方顯示資訊卡（Root），可以從這裡出征。
    /// </summary>
    public sealed class ContinentPage
    {
        private static GameClient G => GameClient.Instance;
        private const float RefreshCitiesSeconds = 10f;

        public readonly VisualElement Root;
        private readonly SurfaceView _view;
        private readonly Label _title, _sub, _desc, _owner;
        private readonly Button _marchBtn;
        private (int x, int y)? _selected;
        private bool _centered;
        private float _nextFetch;
        private string _sig;
        private string _targetName;
        private float _nextTile;
        private Feature _feature;
        private (int x, int y) _target;

        /// <summary>按下「出征」：目標格子與名稱。</summary>
        public event Action<int, int, string> MarchRequested;

        public ContinentPage(SurfaceView view)
        {
            _view = view;
            _view.TileClicked += (x, y) => Select(x, y);

            Root = El("panel", "sel-card");
            var head = El("row");
            _title = Lbl("", "queue-title", "grow");
            head.Add(_title);
            _marchBtn = Btn("出征", () => { if (_selected.HasValue) MarchRequested?.Invoke(_target.x, _target.y, _targetName); }, "btn-primary", "btn-small");
            head.Add(_marchBtn);
            head.Add(Btn("✕", () => { _selected = null; _sig = null; }, "btn-small"));
            Root.Add(head);
            _sub = Lbl("", "muted");
            Root.Add(_sub);
            _owner = Lbl("", "syn-line");
            Root.Add(_owner);
            _desc = Lbl("", "hero-bio");
            Root.Add(_desc);
        }

        public void Select(int x, int y)
        {
            _selected = (x, y);
            _sig = null;
        }

        /// <summary>把鏡頭移到某個格子。</summary>
        public void FocusTile(int x, int y) => WorldScene.Instance.FocusOn(WorldScene.Mode.Surface, SurfaceView.TilePos(x, y));

        /// <summary>切到大地圖時把鏡頭移到自己的城。</summary>
        public void CenterOnHome(CityState city)
        {
            if (city.Surface.Placed) WorldScene.Instance.FocusOn(WorldScene.Mode.Surface, SurfaceView.CityPos(city.Surface.X, city.Surface.Y));
        }

        /// <summary>這格上的主城（2×2 佔地）。</summary>
        private static CityPin PinAt(List<CityPin> pins, int x, int y)
        {
            foreach (var p in pins)
                if (x >= p.X && x < p.X + SurfaceConfig.CityFootprint && y >= p.Y && y < p.Y + SurfaceConfig.CityFootprint) return p;
            return null;
        }

        /// <summary>這格附近（1 格內）的資源田、怪物營地或深淵入口。</summary>
        private static Feature FeatureNear(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var f = SurfaceLogic.FeatureAt(x + dx, y + dy);
                    if (f != null) return f;
                }
            foreach (var f in SurfaceLogic.Features())
                if (f.Type == FeatureType.AbyssGate && SurfaceLogic.Dist2(x, y, f.X, f.Y) <= 4) return f;
            return null;
        }

        public void Refresh(CityState city, long now)
        {
            if (Time.unscaledTime >= _nextFetch)
            {
                _nextFetch = Time.unscaledTime + RefreshCitiesSeconds;
                G.RequestCities();
            }
            if (!_centered && city.Surface.Placed)
            {
                CenterOnHome(city);
                _centered = true;
            }
            _view.SetMarches(city.Surface.Marches, now);
            if (_feature != null && _feature.Type == FeatureType.MonsterCamp)
            {
                if (Time.unscaledTime >= _nextTile)
                {
                    _nextTile = Time.unscaledTime + 3f;
                    G.RequestTile(_feature.X, _feature.Y);
                }
                var info = G.Tile;
                if (info != null && info.X == _feature.X && info.Y == _feature.Y)
                    _owner.text = info.CampBackAt > now ? $"已被擊敗，{DisplayNames.Duration(info.CampBackAt - now)} 後重生" : $"魔物 {CampConfig.Lineup(_feature.Level, 0).Count} 支小隊";
            }
            if (_feature != null && _feature.Type == FeatureType.ResourceField)
            {
                if (Time.unscaledTime >= _nextTile)
                {
                    _nextTile = Time.unscaledTime + 3f;
                    G.RequestTile(_feature.X, _feature.Y);
                }
                var info = G.Tile;
                if (info != null && info.X == _feature.X && info.Y == _feature.Y)
                    _owner.text = $"存量 {info.Stock:N0} / {info.Max:N0}" + (info.Occupant != null ? (info.Mine ? "　你的部隊正在採集" : $"　{info.Occupant} 正在採集") : "　沒人在採");
            }

            var pins = G.Cities;
            string sig = pins.Count + ":" + _selected + ":" + city.Surface.X + "," + city.Surface.Y + ":" + PinsHash(pins);
            if (sig == _sig) return;
            _sig = sig;

            CityPin pin = _selected.HasValue ? PinAt(pins, _selected.Value.x, _selected.Value.y) : null;
            Feature feature = _selected.HasValue && pin == null ? FeatureNear(_selected.Value.x, _selected.Value.y) : null;
            if (feature != _feature) _nextTile = 0;
            _feature = feature;
            (int x, int y)? mark = pin != null ? (pin.X, pin.Y) : feature != null ? (feature.X, feature.Y) : _selected;
            _view.SetCities(pins, mark, pin != null);

            Show(Root, _selected.HasValue);
            if (!_selected.HasValue) return;
            var (sx, sy) = mark.Value;
            var zone = SurfaceLogic.Zone(sx, sy);
            bool wall = SurfaceLogic.IsWall(sx, sy);
            bool pass = SurfaceLogic.NearPass(sx, sy) && zone != SurfaceZone.Pit;

            _title.text = pin != null ? $"{pin.Name} 的主城"
                : feature != null ? DisplayNames.FeatureName(feature)
                : wall ? "山脈" : pass ? "關隘" : zone == SurfaceZone.Pit ? "深淵" : zone == SurfaceZone.Edge ? "地圖邊緣" : "空地";
            _sub.text = $"{DisplayNames.Zones[zone]} · X:{sx} Y:{sy}";
            _owner.text = pin != null ? (pin.Mine ? "這是你的主城" : $"總戰力 {pin.Power:N0}") : "";
            _desc.text = pin != null ? (pin.Mine ? "" : "攻打其他領主將在之後的步驟加入。")
                : feature != null ? DisplayNames.FeatureDescriptions[feature.Type]
                : wall ? "分區之間的山脈，部隊無法翻越，只能從關隘通過。"
                : pass ? "山脈中的關隘，是分區之間唯一的通道。聯盟會爭奪這裡。"
                : zone == SurfaceZone.Pit ? "永暗之喉。每個賽季結束後，下一層的大陸會開放。"
                : zone == SurfaceZone.Edge ? "大陸的盡頭。" : "什麼都沒有的空地。部隊可以駐紮在這裡。";

            bool mine = pin != null && pin.Mine;
            _targetName = _title.text;
            _target = (sx, sy);
            Show(_marchBtn, !mine && SurfaceLogic.Walkable(sx, sy) && feature?.Type != FeatureType.AbyssGate);
        }

        private static int PinsHash(List<CityPin> pins)
        {
            int h = 17;
            foreach (var p in pins) h = h * 31 + p.X * 7 + p.Y * 13 + (p.Name?.GetHashCode() ?? 0) + (p.Mine ? 1 : 0);
            return h;
        }
    }
}
