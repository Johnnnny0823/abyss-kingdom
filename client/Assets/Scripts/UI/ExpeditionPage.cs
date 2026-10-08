using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>遠征頁：關卡路線、敵方陣容、編隊（岔路要兩隊）、出征、戰報、掛機收益。</summary>
    public sealed class ExpeditionPage
    {
        private static GameClient G => GameClient.Instance;
        private static readonly string[] TeamNames = { "第一隊", "第二隊" };

        public readonly VisualElement Root;
        private readonly Label _idleText;
        private readonly Button _idleBtn;
        private readonly List<VisualElement> _nodes = new List<VisualElement>();
        private readonly Label _stageTitle, _stageTags;
        private readonly VisualElement _branches, _heroRow, _result, _stagePanel;
        private readonly Label _doneText;
        private readonly Button _fight;
        private readonly Dictionary<HeroId, int> _assign = new Dictionary<HeroId, int>(); // -1 = 不出戰
        private readonly List<Label> _teamPower = new List<Label>();
        private readonly List<long> _enemyPower = new List<long>();
        private readonly List<Label> _ourSynergy = new List<Label>();
        private int _builtStage = -1;
        private ExpeditionResult _shownResult;

        public ExpeditionPage()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("grow");
            Root = scroll;

            var head = El("row");
            head.Add(Lbl("第一章 · 餘燼緣野", "section-title", "grow"));
            var idle = El("idle-box");
            _idleText = Lbl("", "muted");
            idle.Add(_idleText);
            _idleBtn = Btn("領取掛機收益", () => G.ClaimExpeditionIdle(), "btn-small", "btn-free");
            idle.Add(_idleBtn);
            head.Add(idle);
            scroll.Add(head);

            var path = El("stage-path");
            for (int i = 1; i <= ExpeditionConfig.Chapter1.Length; i++)
            {
                if (i > 1) path.Add(El("stage-link"));
                var node = Lbl(i.ToString(), "stage-node");
                var def = ExpeditionConfig.Stage(i);
                node.EnableInClassList("boss", def.IsBoss);
                node.EnableInClassList("branch", def.Branches.Count > 1);
                node.tooltip = def.IsBoss ? "守層者" : def.Branches.Count > 1 ? "岔路：需要兩支隊伍" : "";
                _nodes.Add(node);
                path.Add(node);
            }
            scroll.Add(path);

            _stagePanel = El("panel");
            var titleRow = El("row");
            _stageTitle = Lbl("", "panel-title");
            titleRow.Add(_stageTitle);
            _stageTags = Lbl("", "stage-tag");
            titleRow.Add(_stageTags);
            _stagePanel.Add(titleRow);
            _branches = El("row");
            _branches.style.alignItems = Align.FlexStart;
            _stagePanel.Add(_branches);
            _stagePanel.Add(Lbl("點主將切換隊伍（岔路關卡：同一位主將只能走一條路）", "muted", "hint"));
            _heroRow = El("row");
            _heroRow.style.flexWrap = Wrap.Wrap;
            _stagePanel.Add(_heroRow);
            _fight = Btn("出征", Fight, "btn-primary", "login-btn");
            _stagePanel.Add(_fight);
            scroll.Add(_stagePanel);

            _doneText = Lbl("第一章已全部通關！下一章「沉鐘水城」與無盡迴廊即將開放。", "panel", "queue-idle");
            scroll.Add(_doneText);

            _result = El("panel");
            scroll.Add(_result);
            Show(_result, false);
        }

        public void Refresh(CityState city)
        {
            var exp = city.Expedition;
            for (int i = 0; i < _nodes.Count; i++)
            {
                int n = i + 1;
                _nodes[i].EnableInClassList("cleared", n <= exp.Cleared);
                _nodes[i].EnableInClassList("current", n == exp.Cleared + 1);
            }

            var pending = ExpeditionLogic.PendingIdle(city, G.ServerNowMs);
            long res = pending.Resources.TryGetValue(ResourceType.Wood, out var r) ? r : 0;
            long books = pending.Items.TryGetValue(ItemType.HeroExpSmall, out var b) ? b : 0;
            _idleText.text = exp.Cleared == 0 ? "通關第 1 關後開始累積掛機收益"
                : $"掛機收益：各資源 +{res:N0}、小經驗書 +{books}（每小時 +{ExpeditionConfig.IdleResourcePerHour(exp.Cleared)}，最多 12 小時）";
            _idleBtn.SetEnabled(res > 0 || books > 0);

            int stage = exp.Cleared + 1;
            var def = ExpeditionConfig.Stage(stage);
            Show(_stagePanel, def != null);
            Show(_doneText, def == null);
            if (def != null)
            {
                if (_builtStage != stage) BuildStage(city, def);
                RefreshTeams(city, def);
            }

            if (G.LastExpedition != null && G.LastExpedition != _shownResult)
            {
                _shownResult = G.LastExpedition;
                ShowResult(_shownResult, G.LastExpeditionTeams);
            }
        }

        private void BuildStage(CityState city, StageDef def)
        {
            _builtStage = def.Number;
            _stageTitle.text = $"第 {def.Number} 關";
            _stageTags.text = def.IsBoss ? "守層者" : def.Branches.Count > 1 ? "岔路 · 需要兩支隊伍" : "";
            Show(_stageTags, _stageTags.text.Length > 0);

            _branches.Clear();
            _teamPower.Clear();
            _enemyPower.Clear();
            _ourSynergy.Clear();
            for (int i = 0; i < def.Branches.Count; i++)
            {
                var col = El("branch-col");
                col.Add(Lbl(def.Branches.Count > 1 ? $"岔路 {(char)('A' + i)} · 由{TeamNames[i]}出戰" : "敵方陣容", "queue-title"));
                long power = 0;
                foreach (var e in def.Branches[i])
                {
                    long p = PowerLogic.Power(new HeroStats { Attack = e.Attack, Defense = e.Defense, Hp = e.Hp, Leadership = e.Count });
                    power += p;
                    var row = El("enemy-row");
                    row.Add(Lbl(DisplayNames.Monsters[e.Monster], "enemy-name"));
                    row.Add(ElementBadge(e.Element, e.Polarity));
                    row.Add(Lbl($"{DisplayNames.Troops[e.Troop]} ×{e.Count:N0}", "muted", "grow"));
                    row.Add(Lbl($"{p:N0}", "muted"));
                    col.Add(row);
                }
                _enemyPower.Add(power);
                var enemySyn = Elements.Synergies(def.Branches[i].Select(e => (e.Element, e.Polarity)).ToList());
                col.Add(Lbl("敵方加成：" + DisplayNames.SynergyList(enemySyn), "syn-line", "enemy"));
                var pl = Lbl("", "team-power");
                _teamPower.Add(pl);
                col.Add(pl);
                var ours = Lbl("", "syn-line");
                _ourSynergy.Add(ours);
                col.Add(ours);
                _branches.Add(col);
            }

            // 預設編隊：單路 = 全員；岔路 = 前兩位第一隊、其餘第二隊
            _assign.Clear();
            int k = 0;
            foreach (var id in city.Heroes.Keys.OrderBy(h => (int)h))
                _assign[id] = def.Branches.Count == 1 ? (k++ < BattleConfig.MaxSquadsPerSide ? 0 : -1) : (k++ < 2 ? 0 : 1);

            _heroRow.Clear();
            foreach (var id in _assign.Keys.ToList())
            {
                var chip = Lbl("", "assign-chip");
                var hid = id;
                chip.RegisterCallback<ClickEvent>(_ =>
                {
                    int next = _assign[hid] + 1;
                    _assign[hid] = next >= def.Branches.Count ? -1 : next;
                });
                chip.name = id.ToString();
                _heroRow.Add(chip);
            }
        }

        private void RefreshTeams(CityState city, StageDef def)
        {
            foreach (var chip in _heroRow.Children().OfType<Label>())
            {
                if (!System.Enum.TryParse<HeroId>(chip.name, out var id) || !_assign.TryGetValue(id, out var team)) continue;
                string where = team < 0 ? "不出戰" : def.Branches.Count > 1 ? TeamNames[team] : "出戰";
                HeroConfig.TryGet(id, out var hd);
                chip.text = $"{DisplayNames.Heroes[id]}〔{DisplayNames.Stem(hd.Element, hd.Polarity)}{DisplayNames.Elements[hd.Element]}〕 {PowerLogic.HeroPower(city, id):N0} · {where}";
                chip.EnableInClassList("on", team >= 0);
                chip.EnableInClassList("team2", team == 1);
            }

            bool ready = true;
            for (int i = 0; i < def.Branches.Count; i++)
            {
                var members = _assign.Where(p => p.Value == i).Select(p => p.Key).ToList();
                long ours = members.Sum(h => PowerLogic.HeroPower(city, h));
                if (members.Count == 0 || members.Count > BattleConfig.MaxSquadsPerSide) ready = false;
                _teamPower[i].text = $"我方 {ours:N0}  vs  敵方 {_enemyPower[i]:N0}";
                _teamPower[i].EnableInClassList("weak", ours < _enemyPower[i]);
                var syn = Elements.Synergies(members.Select(h => { HeroConfig.TryGet(h, out var d); return (d.Element, d.Polarity); }).ToList());
                _ourSynergy[i].text = "我方加成：" + DisplayNames.SynergyList(syn);
            }
            _fight.SetEnabled(ready);
            _fight.text = ready ? $"出征第 {def.Number} 關" : "每條路都需要至少一位主將";
        }

        /// <summary>測試用（-autofight）：用目前編隊出征。</summary>
        public void FightNow() => Fight();

        private void Fight()
        {
            var def = ExpeditionConfig.Stage(_builtStage);
            if (def == null) return;
            var teams = new List<List<HeroId>>();
            for (int i = 0; i < def.Branches.Count; i++)
                teams.Add(_assign.Where(p => p.Value == i).Select(p => p.Key).OrderBy(h => (int)h).ToList());
            G.ExpeditionBattle(def.Number, teams);
        }

        private void ShowResult(ExpeditionResult r, List<List<HeroId>> teams)
        {
            _result.Clear();
            Show(_result, true);
            var banner = Lbl(r.Win ? $"第 {r.Stage} 關 · 勝利" : $"第 {r.Stage} 關 · 失敗", "result-banner");
            banner.EnableInClassList("lose", !r.Win);
            _result.Add(banner);

            if (r.Win && r.Rewards != null)
            {
                var parts = new List<string>();
                if (r.Rewards.Resources.TryGetValue(ResourceType.Wood, out var res)) parts.Add($"各資源 +{res:N0}");
                foreach (var kv in r.Rewards.Items) parts.Add($"{DisplayNames.Item(kv.Key)} ×{kv.Value}");
                if (r.Rewards.Gear != null) parts.Add($"裝備「{DisplayNames.Gear[r.Rewards.Gear.Value]}」");
                _result.Add(Lbl("獲得：" + string.Join("、", parts), "reward-line"));
            }
            else if (!r.Win)
            {
                _result.Add(Lbl("提示：升級主將、換上更好的裝備，或調整兵種克制後再挑戰。", "muted"));
            }

            var def = ExpeditionConfig.Stage(r.Stage);
            for (int i = 0; i < r.Reports.Count; i++)
            {
                var rep = r.Reports[i];
                var ours = teams != null && i < teams.Count ? teams[i] : new List<HeroId>();
                string outcome = rep.Outcome == BattleOutcome.AttackerWins ? "勝" : rep.Outcome == BattleOutcome.DefenderWins ? "敗" : "平手";
                string label = r.Reports.Count > 1 ? $"岔路 {(char)('A' + i)}：" : "";
                _result.Add(Lbl($"{label}{outcome}（{rep.Rounds} 回合）", "queue-title"));
                if (i < r.OurSynergies.Count)
                    _result.Add(Lbl($"我方加成：{DisplayNames.SynergyList(r.OurSynergies[i])}　敵方加成：{DisplayNames.SynergyList(r.EnemySynergies[i])}", "syn-line"));

                string Name(BattleSide side, int idx)
                {
                    if (side == BattleSide.Attacker)
                        return idx < ours.Count ? DisplayNames.Heroes[ours[idx]] : "我方";
                    var enemies = def?.Branches[i];
                    return enemies != null && idx < enemies.Count ? DisplayNames.Monsters[enemies[idx].Monster] : "敵方";
                }

                var log = El("battle-log");
                foreach (var a in rep.Actions.Take(14))
                {
                    var enemySide = a.Side == BattleSide.Attacker ? BattleSide.Defender : BattleSide.Attacker;
                    string tag = (a.CounterPercent > 100 ? "（兵種克制）" : a.CounterPercent < 100 ? "（兵種被克）" : "")
                        + (a.ElementPercent > 100 ? $"（五行剋 {a.ElementPercent}%）" : a.ElementPercent < 100 ? "（五行被剋）" : "");
                    var line = Lbl($"第{a.Round}回合　{Name(a.Side, a.Actor)} → {Name(enemySide, a.Target)}　{a.Damage:N0} 傷害{tag}", "log-line");
                    line.EnableInClassList("enemy", a.Side == BattleSide.Defender);
                    log.Add(line);
                }
                if (rep.Actions.Count > 14) log.Add(Lbl($"……共 {rep.Actions.Count} 次攻擊", "muted"));
                _result.Add(log);
            }
        }
    }
}
