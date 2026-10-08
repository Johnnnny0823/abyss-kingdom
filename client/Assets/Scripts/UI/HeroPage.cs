using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>主將頁：上方選主將，下方是角色雛形（剪影＋6 格裝備）與屬性、戰力、升級。</summary>
    public sealed class HeroPage
    {
        private static readonly ItemType[] Books = { ItemType.HeroExpSmall, ItemType.HeroExpMedium, ItemType.HeroExpLarge };
        private static readonly EquipSlot[] LeftSlots = { EquipSlot.Weapon, EquipSlot.Head, EquipSlot.Body };
        private static readonly EquipSlot[] RightSlots = { EquipSlot.Hands, EquipSlot.Accessory, EquipSlot.Relic };
        private static GameClient G => GameClient.Instance;

        public readonly VisualElement Root;
        private HeroId _selected = HeroId.Hold;
        private readonly Dictionary<HeroId, (VisualElement chip, Label level, Label power)> _chips = new Dictionary<HeroId, (VisualElement, Label, Label)>();
        private readonly Dictionary<EquipSlot, (VisualElement box, Label name)> _slots = new Dictionary<EquipSlot, (VisualElement, Label)>();
        private readonly Dictionary<ItemType, Button> _books = new Dictionary<ItemType, Button>();
        private readonly Label[] _stats = new Label[4];
        private readonly VisualElement _portrait, _expFill;
        private readonly Label _glyph, _troop, _name, _grade, _title, _bio, _level, _power, _powerDelta, _expText;
        private readonly Dictionary<Domain, DomainRow> _domains = new Dictionary<Domain, DomainRow>();
        private readonly Label _tier, _order;
        private readonly VisualElement _elementSlot;
        private HeroId? _elementFor;
        private HeroId? _powerHero;
        private long _lastPower;
        private CityState _city;

        public HeroPage()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("grow");
            Root = scroll;

            var chips = El("row");
            chips.style.marginBottom = 12;
            foreach (var def in HeroConfig.All)
            {
                var chip = El("hero-chip");
                chip.style.borderLeftColor = GradeColor(def.Grade);
                var nameRow = El("row");
                nameRow.Add(Lbl(DisplayNames.Heroes[def.Id], "hero-chip-name"));
                nameRow.Add(ElementBadge(def.Element, def.Polarity));
                chip.Add(nameRow);
                var lv = Lbl("", "muted");
                var pw = Lbl("", "hero-chip-power");
                chip.Add(lv);
                chip.Add(pw);
                var id = def.Id;
                chip.RegisterCallback<ClickEvent>(_ => _selected = id);
                _chips[def.Id] = (chip, lv, pw);
                chips.Add(chip);
            }
            scroll.Add(chips);

            var detail = El("hero-detail");
            scroll.Add(detail);

            // 左：角色雛形（剪影 + 兩側裝備格）
            var doll = El("paperdoll");
            var leftCol = El("slot-col");
            foreach (var s in LeftSlots) leftCol.Add(BuildSlot(s));
            doll.Add(leftCol);
            _portrait = El("portrait");
            var silhouette = El("silhouette");
            silhouette.Add(El("sil-head"));
            silhouette.Add(El("sil-body"));
            _portrait.Add(silhouette);
            _glyph = Lbl("", "portrait-glyph");
            _portrait.Add(_glyph);
            _troop = Lbl("", "portrait-troop");
            _portrait.Add(_troop);
            doll.Add(_portrait);
            var rightCol = El("slot-col");
            foreach (var s in RightSlots) rightCol.Add(BuildSlot(s));
            doll.Add(rightCol);
            detail.Add(doll);

            // 右：資訊
            var info = El("hero-info");
            var head = El("row");
            _name = Lbl("", "hero-name");
            head.Add(_name);
            _grade = Lbl("", "grade-badge");
            head.Add(_grade);
            _elementSlot = El();
            head.Add(_elementSlot);
            head.Add(El("spacer"));
            _level = Lbl("", "level-badge");
            head.Add(_level);
            info.Add(head);
            _title = Lbl("", "hero-title");
            info.Add(_title);
            _bio = Lbl("", "hero-bio");
            info.Add(_bio);

            var powerRow = El("row");
            powerRow.Add(Lbl("戰力", "power-label"));
            _power = Lbl("", "power-big");
            powerRow.Add(_power);
            _powerDelta = Lbl("", "power-delta");
            powerRow.Add(_powerDelta);
            info.Add(powerRow);

            var stats = El("row");
            string[] labels = { "統率", "攻擊", "防禦", "生命" };
            for (int i = 0; i < 4; i++)
            {
                var box = El("stat");
                _stats[i] = Lbl("", "stat-value");
                box.Add(_stats[i]);
                box.Add(Lbl(labels[i], "stat-label"));
                stats.Add(box);
            }
            info.Add(stats);

            var expRow = El("exp-row");
            var bar = El("progress", "grow");
            _expFill = El("progress-fill", "training");
            bar.Add(_expFill);
            expRow.Add(bar);
            _expText = Lbl("", "muted");
            _expText.style.marginLeft = 10;
            expRow.Add(_expText);
            info.Add(expRow);

            var books = El("row");
            foreach (var item in Books)
            {
                var it = item;
                var b = Btn("", () => G.LevelUpHero(_selected, it), "btn-small", "book-btn");
                b.style.marginLeft = 0;
                _books[item] = b;
                books.Add(b);
            }
            info.Add(books);
            info.Add(Lbl("點左右的裝備格可以更換裝備；升級和換裝都會即時反映在戰力上。", "muted", "hint"));

            // 四境
            var domainHead = El("row");
            domainHead.style.marginTop = 16;
            domainHead.Add(Lbl("四境", "panel-title"));
            _tier = Lbl("", "tier-badge");
            domainHead.Add(_tier);
            domainHead.Add(El("spacer"));
            _order = Lbl("", "muted");
            domainHead.Add(_order);
            info.Add(domainHead);
            foreach (var d in DomainConfig.All)
            {
                var row = new DomainRow(d, () => _selected);
                _domains[d] = row;
                info.Add(row.Root);
            }
            info.Add(Lbl("戰鬥中的表現會累積修練值（表現為主）；境書可以補（材料為輔）。修練值滿了就能挑戰試煉，失敗不會扣修練值。", "muted", "hint"));
            detail.Add(info);
        }

        private VisualElement BuildSlot(EquipSlot slot)
        {
            var box = El("gear-slot");
            box.Add(Lbl(DisplayNames.Slots[slot], "slot-label"));
            var name = Lbl("", "slot-name");
            box.Add(name);
            box.RegisterCallback<ClickEvent>(_ => OpenPicker(slot));
            _slots[slot] = (box, name);
            return box;
        }

        public void Refresh(CityState city)
        {
            _city = city;
            foreach (var def in HeroConfig.All)
            {
                var (chip, lv, pw) = _chips[def.Id];
                bool owned = city.Heroes.TryGetValue(def.Id, out var h);
                Show(chip, owned);
                if (!owned) continue;
                chip.EnableInClassList("selected", def.Id == _selected);
                lv.text = $"Lv.{h.Level} · {DisplayNames.Grades[def.Grade]}";
                pw.text = $"戰力 {PowerLogic.HeroPower(city, def.Id):N0}";
            }

            if (!HeroConfig.TryGet(_selected, out var sel) || !city.Heroes.TryGetValue(_selected, out var hero)) return;

            _portrait.style.borderLeftColor = _portrait.style.borderRightColor =
                _portrait.style.borderTopColor = _portrait.style.borderBottomColor = GradeColor(sel.Grade);
            _glyph.text = DisplayNames.Heroes[_selected].Substring(0, 1);
            _troop.text = "專精 " + DisplayNames.Troops[sel.Specialty];
            _name.text = DisplayNames.Heroes[_selected];
            _grade.text = DisplayNames.Grades[sel.Grade];
            _grade.ClearClassList();
            _grade.AddToClassList("grade-badge");
            _grade.AddToClassList("grade-" + sel.Grade);
            if (_elementFor != _selected)
            {
                _elementSlot.Clear();
                _elementSlot.Add(ElementBadge(sel.Element, sel.Polarity));
                _elementFor = _selected;
            }
            _title.text = DisplayNames.HeroTitles[_selected];
            _bio.text = DisplayNames.HeroBios[_selected];

            bool maxed = hero.Level >= HeroConfig.MaxLevel;
            _level.text = $"Lv.{hero.Level}" + (maxed ? " MAX" : "");
            var s = PowerLogic.TotalStats(city, _selected);
            _stats[0].text = s.Leadership.ToString("N0");
            _stats[1].text = s.Attack.ToString("N0");
            _stats[2].text = s.Defense.ToString("N0");
            _stats[3].text = s.Hp.ToString("N0");

            long power = PowerLogic.Power(s);
            _power.text = power.ToString("N0");
            if (_powerHero == _selected && power != _lastPower) ShowDelta(power - _lastPower);
            _powerHero = _selected;
            _lastPower = power;

            long need = HeroConfig.ExpToNext(hero.Level);
            _expFill.style.width = Length.Percent(maxed ? 100f : Mathf.Clamp01((float)hero.Exp / need) * 100f);
            _expText.text = maxed ? "已滿級" : $"{hero.Exp:N0} / {need:N0}";

            foreach (var pair in _books)
            {
                long have = city.GetItemCount(pair.Key);
                ItemConfig.TryGetHeroExp(pair.Key, out var exp);
                pair.Value.text = $"+{exp:N0} ×{have}";
                pair.Value.SetEnabled(have > 0 && !maxed);
            }

            int mastered = hero.Mastered.Count;
            _tier.text = DisplayNames.Tiers[DomainConfig.Tier(mastered)];
            _tier.EnableInClassList("lit", mastered >= 2);
            _order.text = mastered == 0 ? "尚未通達任何一境" : "通達順序：" + string.Join(" → ", hero.Mastered.Select(d => DisplayNames.DomainGlyphs[d]));
            foreach (var row in _domains.Values) row.Refresh(city, hero);

            foreach (var pair in _slots)
            {
                var g = GearLogic.Equipped(city, _selected, pair.Key);
                var (box, name) = pair.Value;
                if (g != null && GearConfig.TryGet(g.Def, out var gd))
                {
                    name.text = DisplayNames.Gear[g.Def];
                    name.style.color = GradeColor(gd.Grade);
                    box.EnableInClassList("filled", true);
                }
                else
                {
                    name.text = "空";
                    name.style.color = (Color)new Color32(110, 118, 150, 255);
                    box.EnableInClassList("filled", false);
                }
            }
        }

        /// <summary>四境中的一列：境名、修練進度、狀態（修練中／挑戰試煉／已通達）、境書。</summary>
        private sealed class DomainRow
        {
            public readonly VisualElement Root = El("domain-row");
            private readonly Domain _domain;
            private readonly System.Func<HeroId> _hero;
            private readonly VisualElement _fill;
            private readonly Label _xp, _done;
            private readonly Button _trial, _scroll;
            private readonly ItemType _scrollItem;

            public DomainRow(Domain domain, System.Func<HeroId> hero)
            {
                _domain = domain;
                _hero = hero;
                _scrollItem = ItemType.DomainScrollPower + (int)domain;
                Root.tooltip = DisplayNames.DomainBonus[domain];
                Root.Add(Lbl(DisplayNames.DomainGlyphs[domain], "domain-glyph", "domain-" + domain));
                var col = El("grow");
                var top = El("row");
                top.Add(Lbl(DisplayNames.Domains[domain], "domain-name"));
                top.Add(El("spacer"));
                _xp = Lbl("", "muted");
                top.Add(_xp);
                col.Add(top);
                var bar = El("progress");
                bar.style.marginTop = 4;
                bar.style.marginBottom = 0;
                _fill = El("progress-fill", "domain-fill-" + domain);
                bar.Add(_fill);
                col.Add(bar);
                Root.Add(col);
                _done = Lbl("已通達", "domain-done");
                Root.Add(_done);
                _trial = Btn("挑戰試煉", () => GameClient.Instance.DomainTrial(_hero(), _domain), "btn-primary", "btn-small");
                Root.Add(_trial);
                _scroll = Btn("", () => GameClient.Instance.UseDomainScroll(_hero(), _scrollItem), "btn-small");
                Root.Add(_scroll);
            }

            public void Refresh(CityState city, HeroState hero)
            {
                bool mastered = DomainLogic.IsMastered(hero, _domain);
                bool ready = DomainLogic.ReadyForTrial(hero, _domain);
                long need = DomainConfig.XpToMaster(hero.Mastered.Count);
                long xp = DomainLogic.Xp(hero, _domain);
                _fill.style.width = Length.Percent(mastered ? 100f : Mathf.Clamp01((float)xp / need) * 100f);
                _xp.text = mastered ? "" : $"{xp:N0} / {need:N0}";
                Root.EnableInClassList("mastered", mastered);
                Show(_done, mastered);
                Show(_trial, ready);
                long scrolls = city.GetItemCount(_scrollItem);
                Show(_scroll, !mastered && !ready && scrolls > 0);
                _scroll.text = $"境書 +100 ×{scrolls}";
            }
        }

        /// <summary>戰力變化的浮動數字：綠色上升、紅色下降，1.5 秒淡出。</summary>
        private void ShowDelta(long delta)
        {
            _powerDelta.text = (delta > 0 ? "+" : "") + delta.ToString("N0");
            _powerDelta.EnableInClassList("down", delta < 0);
            _powerDelta.experimental.animation.Start(1f, 0f, 1500, (e, v) => e.style.opacity = v);
        }

        private void OpenPicker(EquipSlot slot)
        {
            if (_city == null) return;
            var box = El("dialog");
            VisualElement overlay = null;
            var head = El("row");
            head.Add(Lbl($"{DisplayNames.Heroes[_selected]} · {DisplayNames.Slots[slot]}", "panel-title", "grow"));
            head.Add(Btn("關閉", () => overlay?.RemoveFromHierarchy(), "btn-small"));
            box.Add(head);

            var items = _city.Gear.Where(g => GearConfig.TryGet(g.Def, out var d) && d.Slot == slot)
                .OrderByDescending(g => GearConfig.TryGet(g.Def, out var d) ? (int)d.Grade : 0).ToList();
            if (items.Count == 0) box.Add(Lbl("背包裡沒有這個欄位的裝備。遠征會掉落裝備。", "muted"));
            foreach (var g in items)
            {
                GearConfig.TryGet(g.Def, out var d);
                var row = El("item-row");
                var col = El("grow");
                var n = Lbl(DisplayNames.Gear[g.Def], "item-name");
                n.style.color = GradeColor(d.Grade);
                col.Add(n);
                string where = g.EquippedBy == null ? "" : g.EquippedBy == _selected ? " · 穿戴中" : $" · {DisplayNames.Heroes[g.EquippedBy.Value]} 穿戴中";
                col.Add(Lbl(DisplayNames.GearStats(d) + where, "muted"));
                row.Add(col);
                var uid = g.Uid;
                if (g.EquippedBy == _selected)
                    row.Add(Btn("卸下", () => { G.UnequipGear(uid); overlay?.RemoveFromHierarchy(); }, "btn-small", "btn-danger"));
                else
                    row.Add(Btn("裝備", () => { G.EquipGear(uid, _selected); overlay?.RemoveFromHierarchy(); }, "btn-small", "btn-primary"));
                box.Add(row);
            }
            overlay = OpenModal(box);
        }
    }
}
