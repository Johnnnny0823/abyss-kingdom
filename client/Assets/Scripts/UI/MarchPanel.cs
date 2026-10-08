using System;
using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>出征面板：選主將、各兵種滑桿（上限＝兵營數量與統率）、自動配兵、路程與預估時間。</summary>
    public sealed class MarchPanel
    {
        private static GameClient G => GameClient.Instance;

        public readonly VisualElement Root = El("panel", "march-panel");
        /// <summary>送出出征後呼叫（關閉面板）。</summary>
        public event Action Sent;

        private readonly Label _target, _eta, _status;
        private readonly VisualElement _heroRow;
        private readonly Dictionary<TroopType, SliderInt> _sliders = new Dictionary<TroopType, SliderInt>();
        private readonly Dictionary<TroopType, Label> _counts = new Dictionary<TroopType, Label>();
        private readonly Button _go;
        private HeroId? _hero;
        private string _heroSig;
        private int _x, _y;
        private CityState _last;

        public MarchPanel()
        {
            _target = Lbl("", "queue-title");
            Root.Add(_target);
            Root.Add(Lbl("主將", "muted"));
            _heroRow = El("row");
            _heroRow.style.flexWrap = Wrap.Wrap;
            Root.Add(_heroRow);
            Root.Add(Lbl("部隊", "muted"));
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
                _sliders[t] = slider;
                _counts[t] = count;
                Root.Add(row);
            }
            var autoRow = El("row");
            autoRow.Add(Btn("自動配兵", AutoFill, "btn-small"));
            autoRow.Add(El("spacer"));
            _eta = Lbl("", "muted");
            autoRow.Add(_eta);
            Root.Add(autoRow);
            _status = Lbl("", "syn-line");
            Root.Add(_status);
            _go = Btn("出征", Go, "btn-primary");
            _go.style.marginLeft = 0;
            _go.style.marginTop = 8;
            Root.Add(_go);
        }

        public void SetTarget(int x, int y, string title)
        {
            _x = x;
            _y = y;
            _target.text = $"目標：{title}（X:{x} Y:{y}）";
            foreach (var s in _sliders.Values) s.value = 0;
            _heroSig = null;
        }

        public void Refresh(CityState city, long now)
        {
            _last = city;
            var state = city.Surface;
            var busy = new HashSet<HeroId>(state.Marches.Select(m => m.Hero));
            var free = city.Heroes.Keys.Where(h => !busy.Contains(h)).OrderBy(h => (int)h).ToList();
            if (_hero == null || busy.Contains(_hero.Value)) _hero = free.Count > 0 ? free[0] : (HeroId?)null;

            string heroSig = string.Join(",", free) + "|" + _hero;
            if (heroSig != _heroSig)
            {
                _heroSig = heroSig;
                _heroRow.Clear();
                foreach (var h in free)
                {
                    var chip = Lbl($"{DisplayNames.Heroes[h]} · 統率 {PowerLogic.TotalStats(city, h).Leadership:N0}", "assign-chip");
                    chip.EnableInClassList("on", h == _hero);
                    var hid = h;
                    chip.RegisterCallback<ClickEvent>(_ => { _hero = hid; _heroSig = null; });
                    _heroRow.Add(chip);
                }
                if (free.Count == 0) _heroRow.Add(Lbl("所有主將都在行軍中", "muted"));
            }

            long leadership = _hero.HasValue ? PowerLogic.TotalStats(city, _hero.Value).Leadership : 0;
            long total = 0;
            var troops = new Dictionary<TroopType, long>();
            foreach (var t in CityConfig.AllTroops)
            {
                var s = _sliders[t];
                int max = (int)Math.Min(city.GetTroops(t), leadership);
                if (s.highValue != max) s.highValue = max;
                if (s.value > max) s.value = max;
                _counts[t].text = $"{s.value:N0}/{city.GetTroops(t):N0}";
                troops[t] = s.value;
                total += s.value;
            }

            List<(int x, int y)> path = null;
            if (state.Placed)
            {
                var home = SurfaceLogic.CityCenter(state);
                path = MarchLogic.FindPath(home.x, home.y, _x, _y);
            }
            string reason = null;
            if (!state.Placed) reason = "還沒有主城位置";
            else if (_hero == null) reason = "沒有空閒的主將";
            else if (!SurfaceLogic.Walkable(_x, _y)) reason = "這裡不能去（深淵、山脈或地圖外）";
            else if (path == null) reason = "到不了這裡";
            else if (total <= 0) reason = city.Troops.Values.Sum() == 0 ? "兵營沒有部隊，先到兵營訓練" : "至少帶一名士兵";
            else if (total > leadership) reason = $"超過統率（{total:N0} / {leadership:N0}）";
            else if (state.Marches.Count >= MarchConfig.MaxMarches) reason = $"同時最多 {MarchConfig.MaxMarches} 支行軍";

            if (path != null)
            {
                long dist = 0;
                for (int i = 1; i < path.Count; i++) dist += SurfaceLogic.Distance(path[i - 1].x, path[i - 1].y, path[i].x, path[i].y);
                var tiles = path.Select(p => SurfaceLogic.Tile(p.x, p.y)).ToList();
                var times = MarchLogic.Schedule(tiles, now, MarchLogic.SpeedPercent(troops));
                _eta.text = $"路程約 {dist:N0} 格 · {DisplayNames.Duration(times[times.Count - 1] - now)}" + (path.Count > 2 ? " · 經過關隘" : "");
            }
            else _eta.text = "";

            var field = SurfaceLogic.FeatureAt(_x, _y);
            if (reason == null && field != null && field.Type == FeatureType.MonsterCamp)
            {
                var lineup = CampConfig.Lineup(field.Level, SurfaceLogic.Tile(field.X, field.Y));
                _status.text = $"敵方 {lineup.Count} 支小隊、共 {lineup.Sum(e => e.Count):N0} 隻魔物（{string.Join("、", lineup.Select(e => DisplayNames.Troops[e.Troop]))}型）。抵達就開打，打完自動回城；倒下的士兵七成進醫館。";
            }
            else if (reason == null && field != null && field.Type == FeatureType.ResourceField)
            {
                long load = GatherConfig.Load(troops);
                long rate = GatherConfig.RatePerMinute(field.Level);
                _status.text = $"載重 {load:N0} · 每分鐘採 {rate:N0} {DisplayNames.Resources[field.Resource]} · 採滿約 {DisplayNames.Duration(load * 60_000 / rate)}，滿了會自動回城";
            }
            else _status.text = reason ?? "抵達後會駐紮在那裡，可以隨時召回。";
            _go.SetEnabled(reason == null);
            _go.text = reason == null ? $"出征（{total:N0} 兵）" : "無法出征";
        }

        private void AutoFill()
        {
            var city = _last;
            if (city == null || _hero == null) return;
            HeroConfig.TryGet(_hero.Value, out var def);
            long left = PowerLogic.TotalStats(city, _hero.Value).Leadership;
            foreach (var s in _sliders.Values) s.value = 0;
            // 專精兵種優先，其餘依序補滿統率
            var order = new List<TroopType> { def.Specialty };
            order.AddRange(CityConfig.AllTroops.Where(t => t != def.Specialty));
            foreach (var t in order)
            {
                long take = Math.Min(left, city.GetTroops(t));
                _sliders[t].value = (int)take;
                left -= take;
            }
        }

        /// <summary>測試用：自動配兵後直接出征。</summary>
        public void AutoFillAndGo()
        {
            AutoFill();
            Go();
        }

        private void Go()
        {
            if (_hero == null) return;
            var troops = new Dictionary<TroopType, long>();
            foreach (var pair in _sliders) if (pair.Value.value > 0) troops[pair.Key] = pair.Value.value;
            G.StartMarch(_hero.Value, troops, _x, _y);
            foreach (var s in _sliders.Values) s.value = 0;
            Sent?.Invoke();
        }
    }

    /// <summary>HUD 上的行軍列表：每支行軍的狀態、倒數與召回。</summary>
    public sealed class MarchList
    {
        private static GameClient G => GameClient.Instance;

        public readonly VisualElement Root = El();
        /// <summary>點「查看」時呼叫：把鏡頭移到這支行軍。</summary>
        public event Action<March> Focus;
        private readonly Label _title;
        private readonly VisualElement _list = El();
        private string _sig;

        public MarchList()
        {
            _title = Lbl("", "panel-title");
            Root.Add(_title);
            Root.Add(_list);
        }

        public void Refresh(CityState city, long now)
        {
            var marches = city.Surface.Marches;
            _title.text = $"行軍 ({marches.Count}/{MarchConfig.MaxMarches})";
            string sig = string.Join(",", marches.Select(m => $"{m.Id}:{m.Stationed}:{m.Returning}"));
            if (sig != _sig)
            {
                _sig = sig;
                _list.Clear();
                if (marches.Count == 0) _list.Add(Lbl("在大地圖上點一個地點，派主將出征", "queue-idle"));
                foreach (var m in marches)
                {
                    var row = El("item-row");
                    var col = El("grow");
                    col.Add(Lbl($"{DisplayNames.Heroes[m.Hero]} · {m.TotalTroops():N0} 兵", "item-name"));
                    var st = Lbl("", "muted");
                    st.name = "status";
                    col.Add(st);
                    row.Add(col);
                    var march = m;
                    row.Add(Btn("查看", () => Focus?.Invoke(march), "btn-small"));
                    if (!m.Returning) row.Add(Btn("召回", () => G.RecallMarch(march.Id), "btn-small", "btn-danger"));
                    row.userData = m.Id;
                    _list.Add(row);
                }
            }
            foreach (var row in _list.Children())
            {
                if (!(row.userData is long id)) continue;
                var m = marches.Find(x => x.Id == id);
                var st = row.Q<Label>("status");
                if (m == null || st == null) continue;
                var (x, y) = SurfaceLogic.FromTile(m.Destination);
                string carry = DisplayNames.CarryText(m.Carry);
                st.text = m.Gathering ? $"採集{DisplayNames.Resources[m.GatherResource]} {MarchLogic.Gathered(m, now):N0} / {m.GatherCap:N0}　剩 {DisplayNames.Duration(m.GatherEndMs - now)}"
                    : m.Stationed ? $"駐紮於 X:{x} Y:{y}"
                    : m.Returning ? $"返回主城　剩 {DisplayNames.Duration(m.ArriveAt - now)}" + (carry != "" ? $"　帶著 {carry}" : "")
                    : $"前往 X:{x} Y:{y}　剩 {DisplayNames.Duration(m.ArriveAt - now)}";
            }
        }
    }
}
