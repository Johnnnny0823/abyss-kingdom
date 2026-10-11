using System;
using System.Linq;
using AbyssKingdom.Shared.City;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// HUD 左上角的新手任務：一次一個任務、進度條、獎勵。沒完成時按「前往」帶玩家到該去的地方；完成時按「領取」。
    /// 進度用共用規則在客戶端算（QuestLogic），領獎由伺服器檢查。
    /// </summary>
    public sealed class QuestPanel
    {
        public readonly VisualElement Root;
        /// <summary>按下「前往」：要去做這個任務。</summary>
        public event Action<QuestDef> GoRequested;
        /// <summary>按下「領取」。</summary>
        public event Action ClaimRequested;

        private readonly Label _title, _progress, _reward;
        private readonly VisualElement _fill;
        private readonly Button _go, _claim;
        private QuestDef _quest;
        private string _sig;

        public QuestPanel()
        {
            Root = El("panel", "hud-panel", "quest-panel");
            var head = El("row");
            head.Add(Lbl("任務", "panel-title", "grow"));
            _progress = Lbl("", "muted");
            head.Add(_progress);
            Root.Add(head);
            _title = Lbl("", "quest-title");
            Root.Add(_title);
            var bar = El("quest-bar");
            _fill = El("quest-fill");
            bar.Add(_fill);
            Root.Add(bar);
            _reward = Lbl("", "quest-reward");
            Root.Add(_reward);
            var row = El("row");
            _go = Btn("前往", () => { if (_quest != null) GoRequested?.Invoke(_quest); }, "btn-small");
            _claim = Btn("領取獎勵", () => ClaimRequested?.Invoke(), "btn-primary", "btn-small", "quest-claim");
            row.Add(_go);
            row.Add(_claim);
            Root.Add(row);
        }

        public void Refresh(CityState city)
        {
            var q = QuestLogic.Current(city);
            _quest = q;
            Show(Root, q != null);
            if (q == null) return;
            long p = QuestLogic.Progress(city, q);
            bool done = p >= q.Target;
            string sig = $"{q.Id}:{p}";
            if (sig == _sig) return;
            _sig = sig;
            _title.text = DisplayNames.QuestText(q);
            _progress.text = $"{p:N0} / {q.Target:N0}";
            _fill.style.width = Length.Percent(q.Target > 0 ? 100f * p / q.Target : 0);
            string rewards = DisplayNames.CarryText(q.Resources);
            string items = DisplayNames.ItemsText(q.Items);
            _reward.text = "獎勵：" + string.Join("、", new[] { rewards, items }.Where(s => !string.IsNullOrEmpty(s)));
            Show(_go, !done);
            Show(_claim, done);
            Root.EnableInClassList("quest-done", done);
        }
    }
}
