using System;
using System.Collections.Generic;
using System.Linq;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Shared.Protocol;
using AbyssKingdom.Shared.Social;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using AbyssKingdom.Shared.Map;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>聯盟名單與成員操作；所有變更只送意圖，顯示以伺服器回傳狀態為準。</summary>
    public sealed class AlliancePanel
    {
        private static GameClient G => GameClient.Instance;
        private readonly ScrollView _root = new ScrollView();
        private AllianceStateMessage _shown;
        private int _shownChatVersion = -1;
        private readonly TextField _name = new TextField("聯盟名稱");
        private readonly TextField _tag = new TextField("簡稱（2～4 英數字）");
        private readonly TextField _chatInput = new TextField("聯盟訊息（最多 200 字）");
        private readonly TextField _announcementInput = new TextField("公告（最多 300 字）");

        public VisualElement Root => _root;

        public AlliancePanel()
        {
            _root.AddToClassList("panel");
            _root.AddToClassList("bag");
        }

        public void Refresh()
        {
            var state = G.Alliance;
            if (ReferenceEquals(state, _shown) && _shownChatVersion == G.AllianceChatVersion) return;
            _shown = state;
            _shownChatVersion = G.AllianceChatVersion;
            _root.Clear();
            if (state == null)
            {
                _root.Add(Lbl("正在讀取聯盟資料…", "muted"));
                return;
            }

            _root.Add(Btn("重新整理", G.RequestAlliance, "btn-small"));
            _root.Add(Lbl("聯盟入門", "section-title"));
            foreach (var step in DisplayNames.AllianceGuide)
                _root.Add(Lbl(((state.GuideProgress & step.Key) != 0 ? "已完成：" : "待完成：") + step.Value, "muted"));
            if (string.IsNullOrEmpty(state.MyAllianceId)) BuildNoAlliance(state);
            else BuildMembership(state);
            BuildReinforcements(state);
            if (!string.IsNullOrEmpty(state.MyAllianceId)) BuildFlags(state);
            BuildRallies(state);
        }

        private void BuildNoAlliance(AllianceStateMessage state)
        {
            _root.Add(Lbl("尋找盟友", "section-title"));
            if (!string.IsNullOrEmpty(state.PendingCreateOperationId))
            {
                _root.Add(Lbl($"[{state.PendingCreateTag}] {state.PendingCreateName} 正在完成建立", "muted"));
                _root.Add(Btn("重試完成創盟", () => G.CreateAlliance(state.PendingCreateName, state.PendingCreateTag), "btn-primary"));
                return;
            }
            if (state.RejoinAfterMs > G.ServerNowMs)
                _root.Add(Lbl("再次加入或建立聯盟還需 " + DisplayNames.Duration(state.RejoinAfterMs - G.ServerNowMs), "muted"));
            foreach (var alliance in state.Alliances)
            {
                var row = El("row", "panel");
                row.Add(Lbl($"[{alliance.Tag}] {alliance.Name}　{alliance.Members}/30", "grow"));
                bool apply = alliance.JoinMode == AllianceJoinMode.Approval;
                row.Add(Btn(apply ? "申請" : "加入", () => G.JoinAlliance(alliance.Id, apply), "btn-small"));
                _root.Add(row);
            }
            _root.Add(Lbl("建立聯盟　主堡 Lv.3，根材與層岩各 5,000", "section-title"));
            _root.Add(_name);
            _root.Add(_tag);
            _root.Add(Btn("建立聯盟", () => Confirm("建立聯盟並支付資源？", () => G.CreateAlliance(_name.value, _tag.value)), "btn-primary"));
        }

        private void BuildMembership(AllianceStateMessage state)
        {
            AllianceSummary mine = state.Alliances.Find(a => a.Id == state.MyAllianceId);
            _root.Add(Lbl(mine == null ? "聯盟" : $"[{mine.Tag}] {mine.Name}", "section-title"));
            _root.Add(Lbl($"在線 {state.OnlineMembers}／{state.Members.Count} 位成員　我的階級：{RoleName(state.MyRole)}", "muted"));
            _root.Add(Lbl("公告：" + (string.IsNullOrEmpty(state.Announcement) ? "尚無公告" : state.Announcement), "muted"));
            if (state.MyRole is AllianceRole.Leader or AllianceRole.Officer)
            {
                _root.Add(_announcementInput);
                _root.Add(Btn("更新公告", () => G.SetAllianceAnnouncement(_announcementInput.value), "btn-small"));
            }
            BuildChat(state);
            _root.Add(Lbl("聯盟幫助", "section-title"));
            if (G.City?.Upgrade != null)
                _root.Add(Btn("請求升級幫助", () => G.RequestAllianceHelp(QueueType.Building), "btn-small"));
            if (G.City?.Healing != null)
                _root.Add(Btn("請求治療幫助", () => G.RequestAllianceHelp(QueueType.Healing), "btn-small"));
            foreach (var request in state.HelpRequests)
                _root.Add(Lbl(request.OwnerName + "：" +
                    (request.Queue == QueueType.Building ? "建築升級" : "醫館治療") +
                    $"　已獲幫助 {request.UsedCount}/{request.MaxCount}", "muted"));
            _root.Add(Btn("幫助全部", G.HelpAllAlliance, "btn-primary"));
            if (state.MyRole == AllianceRole.Leader)
            {
                var row = El("row");
                row.Add(Btn("自由加入", () => G.SetAllianceJoinMode(AllianceJoinMode.Open), "btn-small"));
                row.Add(Btn("需審核", () => G.SetAllianceJoinMode(AllianceJoinMode.Approval), "btn-small"));
                _root.Add(row);
            }
            foreach (var member in state.Members)
            {
                var row = El("row", "panel");
                row.Add(Lbl($"{member.DisplayName}　{RoleName(member.Role)}", "grow"));
                if (member.PlayerId != G.PlayerId)
                {
                    row.Add(Btn("派援", () => OpenReinforcement(member), "btn-small"));
                    if (state.MyRole == AllianceRole.Leader && member.Role != AllianceRole.Leader)
                    {
                        row.Add(Btn(member.Role == AllianceRole.Officer ? "卸任" : "任幹部",
                            () => G.SetAllianceOfficer(member.PlayerId, member.Role != AllianceRole.Officer), "btn-small"));
                        row.Add(Btn("轉讓", () => Confirm("將盟主轉讓給 " + member.DisplayName + "？", () => G.TransferAlliance(member.PlayerId)), "btn-small"));
                    }
                    if ((state.MyRole == AllianceRole.Leader && member.Role != AllianceRole.Leader) ||
                        (state.MyRole == AllianceRole.Officer && member.Role == AllianceRole.Member))
                        row.Add(Btn("移除", () => Confirm("移除 " + member.DisplayName + "？", () => G.KickAllianceMember(member.PlayerId)), "btn-small"));
                }
                _root.Add(row);
            }
            if (state.MyRole == AllianceRole.Leader || state.MyRole == AllianceRole.Officer)
            {
                _root.Add(Lbl("入盟申請", "section-title"));
                foreach (var request in state.Applications)
                {
                    var row = El("row", "panel");
                    row.Add(Lbl(request.DisplayName, "grow"));
                    row.Add(Btn("接受", () => G.ReviewAlliance(request.PlayerId, true), "btn-small"));
                    row.Add(Btn("拒絕", () => G.ReviewAlliance(request.PlayerId, false), "btn-small"));
                    _root.Add(row);
                }
            }
            if (state.MyRole == AllianceRole.Leader)
                _root.Add(Btn("解散聯盟", () => Confirm("確定解散聯盟？只能在只剩盟主時執行。", G.DissolveAlliance), "btn-small"));
            else
                _root.Add(Btn("退出聯盟", () => Confirm("退出後 24 小時內不能再加入或建立聯盟。", G.LeaveAlliance), "btn-small"));
        }

        private void BuildReinforcements(AllianceStateMessage state)
        {
            _root.Add(Lbl("援軍駐守", "section-title"));
            _root.Add(Lbl("每城最多一支援軍，在途即占席位。傷兵送回派出者醫館。", "muted"));
            if (G.City != null)
                _root.Add(Btn(G.City.AcceptReinforcements ? "關閉新援軍" : "開放新援軍",
                    () => G.SetReinforcementAcceptance(!G.City.AcceptReinforcements), "btn-small"));
            foreach (var r in state.Reinforcements)
            {
                var row = El("row", "panel");
                string status = r.State switch
                {
                    ReinforcementState.Reserved => "完成派遣中", ReinforcementState.Traveling => "前往盟友城",
                    ReinforcementState.Garrisoned => "駐守中", ReinforcementState.Returning => "返城中", _ => "已結束"
                };
                row.Add(Lbl($"{r.SenderName} → {r.ReceiverName} · {DisplayNames.Heroes[r.Hero]} · {r.Troops.Values.Sum():N0} 人 · {status}" +
                    (r.BattleFrozen ? "（戰鬥結算中）" : "") + (r.ReturnRequested ? " · 已要求返還" : ""), "grow"));
                if (r.State == ReinforcementState.Reserved && r.SenderId == G.PlayerId)
                    row.Add(Btn("重試派遣", () => G.SendReinforcement(r.ReceiverId, r.Hero, r.Troops,
                        r.Id.Substring(r.SenderId.Length + 1)), "btn-small"));
                if (r.State != ReinforcementState.Returning && !r.ReturnRequested)
                    row.Add(Btn(r.SenderId == G.PlayerId ? "召回" : "遣返", () => G.ReturnReinforcement(r.Id), "btn-small"));
                _root.Add(row);
            }
            if (state.Reinforcements.Count == 0) _root.Add(Lbl("目前沒有派出或接收的援軍。從成員名單點選「派援」。", "muted"));
        }

        private void OpenReinforcement(AllianceMemberView target)
        {
            var city = G.City;
            if (city == null) return;
            var free = city.Heroes.Keys.Where(h => city.Surface.Marches.All(m => m.Hero != h)).OrderBy(h => (int)h).ToList();
            var box = El("panel");
            box.Add(Lbl("派援到 " + target.DisplayName, "section-title"));
            VisualElement overlay = null;
            box.Add(Btn("取消", () => overlay.RemoveFromHierarchy(), "btn-small"));
            long capacity = Math.Min(AllianceMilitaryRules.PersonalCapacity(city.GetLevel(BuildingType.Keep)),
                AllianceMilitaryRules.PersonalCapacity(target.KeepLevel));
            box.Add(Lbl($"雙方主堡容量取較低者：{capacity:N0} 人。自由混兵，護盾保留。", "muted"));
            if (free.Count == 0 || capacity == 0 || city.Surface.Marches.Count >= MarchConfig.MaxMarches)
            {
                box.Add(Lbl(free.Count == 0 ? "沒有空閒主將" : capacity == 0 ? "雙方主堡需達 Lv.3" : "行軍位已滿", "muted"));
                overlay = OpenModal(box);
                return;
            }
            var hero = new DropdownField("主將", free.Select(h => DisplayNames.Heroes[h]).ToList(), 0);
            box.Add(hero);
            var sliders = new Dictionary<TroopType, SliderInt>();
            foreach (var type in CityConfig.AllTroops)
            {
                var slider = new SliderInt(DisplayNames.Troops[type], 0, (int)Math.Min(capacity, city.GetTroops(type))) { showInputField = true };
                sliders[type] = slider; box.Add(slider);
            }
            var total = Lbl("", "muted"); box.Add(total);
            string operationId = Guid.NewGuid().ToString();
            var send = Btn("派遣援軍", () =>
            {
                G.SendReinforcement(target.PlayerId, free[hero.index], sliders.ToDictionary(s => s.Key, s => (long)s.Value.value), operationId);
                overlay.RemoveFromHierarchy();
            }, "btn-primary");
            void Validate()
            {
                long count = sliders.Values.Sum(s => (long)s.value);
                total.text = $"合計 {count:N0}／{capacity:N0} 人";
                send.SetEnabled(count > 0 && count <= capacity);
            }
            foreach (var slider in sliders.Values) slider.RegisterValueChangedCallback(_ => Validate());
            box.Add(send); Validate();
            overlay = OpenModal(box);
        }

        private void BuildFlags(AllianceStateMessage state)
        {
            _root.Add(Lbl("盟旗領地", "section-title"));
            _root.Add(Lbl("同區步行 12 格領地：採集速度 +10%、城防防禦 +5%，不疊加、不阻擋通行。", "muted"));
            bool manage = state.MyRole == AllianceRole.Leader || state.MyRole == AllianceRole.Officer;
            for (int slot = 1; slot <= AllianceTerritory.MaxFlags; slot++)
            {
                int chosen = slot; var live = state.Flags.FirstOrDefault(f => f.Live && f.Slot == slot);
                long cooldown = state.Flags.Where(f => f.Slot == slot && f.RemovedAtMs > 0)
                    .Select(f => f.RemovedAtMs + AllianceTerritory.RebuildCooldownMs).DefaultIfEmpty().Max();
                var row = El("row", "panel"); var label = Lbl("", "grow"); row.Add(label);
                void UpdateLabel()
                {
                    label.text = "第 " + chosen + " 旗：" + (live == null
                        ? (cooldown > G.ServerNowMs ? "重建冷卻 " + DisplayNames.Duration(cooldown - G.ServerNowMs) : "空槽")
                        : $"({live.X}, {live.Y}) · " + (!live.Paid ? "付款待完成" : live.Active(G.ServerNowMs)
                            ? "領地生效" : "施工 " + DisplayNames.Duration(live.CompletesAtMs - G.ServerNowMs)));
                }
                UpdateLabel(); label.schedule.Execute(UpdateLabel).Every(1_000);
                if (manage && live != null)
                {
                    if (!live.Paid && live.PayerId == G.PlayerId) row.Add(Btn("重試付款", () => G.PlaceAllianceFlag(live.Slot, live.X, live.Y,
                        live.Id.Substring(live.PayerId.Length + 1)), "btn-small"));
                    row.Add(Btn("撤旗／取消", () => Confirm("不退款，此槽需冷卻 24 小時。若會斷開領地，操作將被拒絕。",
                        () => G.RemoveAllianceFlag(chosen)), "btn-small"));
                }
                else if (manage && cooldown <= G.ServerNowMs) row.Add(Btn("建造", () => OpenFlagPlacement(chosen), "btn-small"));
                _root.Add(row);
            }
        }
        private void OpenFlagPlacement(int slot)
        {
            var center = SurfaceLogic.CityCenter(G.City.Surface);
            var point = AllianceTerritory.Coverage(center.x, center.y, 10).Select(SurfaceLogic.FromTile)
                .FirstOrDefault(p => AllianceTerritory.AllowedPoint(p.x, p.y));
            var box = El("panel"); box.Add(Lbl("選擇第 " + slot + " 旗位置", "section-title"));
            var x = new IntegerField("X") { value = point.x }; var y = new IntegerField("Y") { value = point.y };
            box.Add(x); box.Add(y); VisualElement overlay = null;
            var preview = Lbl("", "muted"); box.Add(preview);
            void Preview()
            {
                bool valid = AllianceTerritory.AllowedPoint(x.value, y.value);
                var cells = AllianceTerritory.Coverage(x.value, y.value);
                string reason = valid ? "" : "此處不能建旗";
                foreach (var flag in G.TerritoryFlags.Where(f => f.AllianceId != G.Alliance?.MyAllianceId))
                    if (cells.Overlaps(AllianceTerritory.Coverage(flag.X, flag.Y))) { valid = false; reason = "與其他聯盟領地重疊"; break; }
                preview.text = valid ? $"預覽覆蓋 {cells.Count} 格。新領地需連接生效盟旗；第一旗需接近同區盟友主城。" : reason;
                preview.style.color = valid ? new UnityEngine.Color(.85f, .85f, .75f) : new UnityEngine.Color(1f, .4f, .35f);
                var world = AbyssKingdom.Client.World.WorldScene.Instance;
                world.SetMode(AbyssKingdom.Client.World.WorldScene.Mode.Surface);
                world.FocusOn(AbyssKingdom.Client.World.WorldScene.Mode.Surface, AbyssKingdom.Client.World.SurfaceView.TilePos(x.value, y.value));
                world.Surface.PreviewFlag(x.value, y.value, valid);
            }
            x.RegisterValueChangedCallback(_ => Preview()); y.RegisterValueChangedCallback(_ => Preview()); Preview();
            box.Add(Btn("取消", () => overlay.RemoveFromHierarchy(), "btn-small"));
            box.Add(Btn("確認位置", () =>
            {
                int px = x.value, py = y.value; overlay.RemoveFromHierarchy();
                Confirm($"由 {G.PlayerName} 支付根材與層岩各 {AllianceTerritory.Cost(slot):N0}，在 ({px}, {py}) 建造第 {slot} 旗，工期 5 分鐘。",
                    () => G.PlaceAllianceFlag(slot, px, py, Guid.NewGuid().ToString()));
            }, "btn-primary"));
            overlay = OpenModal(box);
            overlay.RegisterCallback<DetachFromPanelEvent>(_ => AbyssKingdom.Client.World.WorldScene.Instance.Surface.ClearFlagPreview());
        }

        private void BuildRallies(AllianceStateMessage state)
        {
            _root.Add(Lbl("PvE 集結", "section-title"));
            _root.Add(Lbl("最多五人，每人一位主將混兵；至少兩人到齊。單份營地獎勵按有效傷害分配，單件道具可能只給一人。", "muted"));
            if (!string.IsNullOrEmpty(state.MyAllianceId)) _root.Add(Btn("發起集結", () => OpenCreateRally(), "btn-primary"));
            foreach (var r in state.Rallies)
            {
                var box = El("panel"); var label = Lbl("", "section-title"); box.Add(label);
                void RefreshLabel()
                {
                    string eta = r.State == "Forming" ? " · " + DisplayNames.Duration(Math.Max(0, r.DepartAtMs - G.ServerNowMs))
                        : r.State == "Traveling" ? " · " + DisplayNames.Duration(Math.Max(0, r.ArriveAtMs - G.ServerNowMs)) : "";
                    label.text = $"{r.LeaderName} · Lv.{r.Level} 營地 ({r.X}, {r.Y}) · " + DisplayNames.RallyState(r.State) + eta;
                }
                RefreshLabel(); label.schedule.Execute(RefreshLabel).Every(1_000);
                box.Add(Lbl($"{r.Participants.Count}/5 人 · {r.Participants.Sum(p => p.Count):N0}/{r.Capacity:N0} 兵", "muted"));
                foreach (var p in r.Participants)
                {
                    box.Add(Lbl($"{p.Name} · {DisplayNames.Heroes[p.Hero]} · {p.Count:N0} 人" + (!p.Paid ? " · 派遣待完成" : p.Returned ? " · 已返城" : ""), "muted"));
                    if (!p.Paid && p.OwnerId == G.PlayerId && p.OperationId != null)
                        box.Add(Btn("重試派遣", () =>
                        {
                            if (r.LeaderId == G.PlayerId) G.CreateRally(p.Hero, p.Troops, r.X, r.Y, r.Seconds, p.OperationId);
                            else G.JoinRally(r.Id, p.Hero, p.Troops, p.OperationId);
                        }, "btn-small"));
                    if (r.Outcome != null)
                    {
                        string items = DisplayNames.ItemsText(p.Items);
                        box.Add(Lbl($"戰損 {p.Losses.Values.Sum():N0} · 有效傷害 {p.Contribution:N0} · " + DisplayNames.CarryText(p.Resources) + (items.Length > 0 ? " · " + items : ""), "muted"));
                    }
                }
                bool mine = r.Participants.Any(p => p.OwnerId == G.PlayerId);
                if (r.State == "Forming" && r.DepartAtMs > G.ServerNowMs && !mine && !string.IsNullOrEmpty(state.MyAllianceId))
                    box.Add(Btn("加入集結", () => OpenRallyPicker("加入 " + r.LeaderName + " 的集結", null,
                        (hero, troops, op) => G.JoinRally(r.Id, hero, troops, op)), "btn-primary"));
                if ((mine || r.LeaderId == G.PlayerId) && (r.State == "Forming" || (r.State == "Traveling" && r.LeaderId == G.PlayerId)))
                    box.Add(Btn(r.LeaderId == G.PlayerId ? "整隊召回／取消" : "退出集結", () => G.RecallRally(r.Id), "btn-small"));
                if (r.Outcome != null) box.Add(Lbl(DisplayNames.RallyOutcome(r.Outcome), "muted"));
                _root.Add(box);
            }
        }
        public void OpenCreateRally(int? targetX = null, int? targetY = null)
        {
            var city = G.City; if (city == null) return;
            var home = SurfaceLogic.CityCenter(city.Surface);
            var target = SurfaceLogic.Features().Where(f => f.Type == FeatureType.MonsterCamp && f.Level >= 2)
                .OrderBy(f => SurfaceLogic.Dist2(home.x, home.y, f.X, f.Y)).First();
            var extra = El(); var x = new IntegerField("營地 X") { value = targetX ?? target.X }; var y = new IntegerField("營地 Y") { value = targetY ?? target.Y };
            var countdown = new DropdownField("倒數", new List<string> { "60 秒", "180 秒", "300 秒" }, 1);
            extra.Add(x); extra.Add(y); extra.Add(countdown);
            OpenRallyPicker("發起 Lv.2／Lv.3 營地集結", extra,
                (hero, troops, op) => G.CreateRally(hero, troops, x.value, y.value, new[] { 60, 180, 300 }[countdown.index], op));
        }
        private void OpenRallyPicker(string title, VisualElement extra, Action<HeroId, Dictionary<TroopType, long>, string> action)
        {
            var city = G.City; if (city == null) return;
            var free = city.Heroes.Keys.Where(h => city.Surface.Marches.All(m => m.Hero != h)).OrderBy(h => (int)h).ToList();
            long capacity = AllianceMilitaryRules.PersonalCapacity(city.GetLevel(BuildingType.Keep));
            var box = El("panel"); box.Add(Lbl(title, "section-title")); VisualElement overlay = null;
            box.Add(Btn("取消", () => overlay.RemoveFromHierarchy(), "btn-small"));
            if (extra != null) box.Add(extra);
            if (capacity == 0 || free.Count == 0 || city.Surface.Marches.Count >= MarchConfig.MaxMarches)
            { box.Add(Lbl(capacity == 0 ? "需要主堡 Lv.3" : "沒有空閒主將或行軍位", "muted")); overlay = OpenModal(box); return; }
            var hero = new DropdownField("主將", free.Select(h => DisplayNames.Heroes[h]).ToList(), 0); box.Add(hero);
            var sliders = new Dictionary<TroopType, SliderInt>();
            foreach (var t in CityConfig.AllTroops)
            { var slider = new SliderInt(DisplayNames.Troops[t], 0, (int)Math.Min(capacity, city.GetTroops(t))) { showInputField = true }; sliders[t] = slider; box.Add(slider); }
            var total = Lbl("", "muted"); box.Add(total); string op = Guid.NewGuid().ToString();
            var send = Btn("派遣", () => { action(free[hero.index], sliders.ToDictionary(s => s.Key, s => (long)s.Value.value), op); overlay.RemoveFromHierarchy(); }, "btn-primary");
            void Validate() { long n = sliders.Values.Sum(s => (long)s.value); total.text = $"{n:N0}/{capacity:N0} 人 · 必須在倒數結束前抵達"; send.SetEnabled(n > 0 && n <= capacity); }
            foreach (var slider in sliders.Values) slider.RegisterValueChangedCallback(_ => Validate());
            box.Add(send); Validate(); overlay = OpenModal(box);
        }

        private void BuildChat(AllianceStateMessage state)
        {
            _root.Add(Lbl("聯盟聊天", "section-title"));
            foreach (var message in G.AllianceChat)
            {
                var row = El("row", "panel");
                row.Add(Lbl(message.Hidden ? "（訊息已隱藏）" : message.DisplayName + "：" + message.Text, "grow"));
                if (!message.Hidden)
                {
                    row.Add(Btn("檢舉", () => G.ReportAllianceChat(message.Sequence), "btn-small"));
                    var member = state.Members.Find(m => m.PlayerId == message.PlayerId);
                    bool canModerate = member != null && (member.Role != AllianceRole.Leader ||
                        (state.MyRole == AllianceRole.Leader && message.PlayerId == G.PlayerId)) &&
                        (state.MyRole == AllianceRole.Leader ||
                         (state.MyRole == AllianceRole.Officer && member.Role == AllianceRole.Member));
                    if (canModerate)
                    {
                        row.Add(Btn("隱藏", () => G.HideAllianceChat(message.Sequence), "btn-small"));
                        if (member.Role != AllianceRole.Leader)
                        {
                            row.Add(Btn("禁言 1h", () => G.MuteAllianceChat(message.PlayerId, 1), "btn-small"));
                            row.Add(Btn("禁言 24h", () => G.MuteAllianceChat(message.PlayerId, 24), "btn-small"));
                        }
                    }
                }
                _root.Add(row);
            }
            _root.Add(_chatInput);
            _root.Add(Btn("發送", () =>
            {
                if (string.IsNullOrWhiteSpace(_chatInput.value)) return;
                G.PostAllianceChat(_chatInput.value);
                _chatInput.value = "";
            }, "btn-primary"));
        }

        private static string RoleName(AllianceRole? role) => role switch
        {
            AllianceRole.Leader => "盟主", AllianceRole.Officer => "幹部", _ => "成員"
        };

        private static void Confirm(string prompt, Action action)
        {
            var box = El("panel");
            box.Add(Lbl(prompt, "section-title"));
            var row = El("row");
            VisualElement overlay = null;
            row.Add(Btn("取消", () => overlay.RemoveFromHierarchy(), "btn-small"));
            row.Add(Btn("確認", () => { overlay.RemoveFromHierarchy(); action(); }, "btn-primary"));
            box.Add(row);
            overlay = OpenModal(box);
        }
    }
}
