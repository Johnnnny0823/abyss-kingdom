using System;
using System.IO;
using AbyssKingdom.Client.Game;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    public sealed class FeedbackPanel
    {
        public readonly VisualElement Root;
        private readonly TextField _text;
        private readonly Label _status;
        private readonly Button _send;
        private float _deadline;
        private bool _waiting;
        private int _requestId;
        private readonly Func<string> _screen;
        public FeedbackPanel(Func<string> screen, Action close)
        {
            _screen = screen; Root = El("dialog");
            var head = El("row");
            head.Add(Lbl(DisplayNames.FeedbackTitle, "panel-title", "grow"));
            head.Add(Btn("✕", close, "btn-small")); Root.Add(head);
            Root.Add(Lbl(DisplayNames.FeedbackPrompt, "hero-bio"));
            _text = new TextField { multiline = true, maxLength = 1000 };
            _text.AddToClassList("feedback-input");
            _text.style.height = 150; Root.Add(_text);
            _status = Lbl("", "muted"); Root.Add(_status);
            var row = El("row");
            _send = Btn(DisplayNames.FeedbackSend, Send, "btn-primary"); row.Add(_send);
            var logs = Btn(DisplayNames.OpenLogFolder, () =>
            {
                string path = Application.persistentDataPath;
                Directory.CreateDirectory(path);
                Application.OpenURL(new Uri(path + Path.DirectorySeparatorChar).AbsoluteUri);
            }, "btn-small");
            logs.tooltip = DisplayNames.LogFolderDescription; row.Add(logs); Root.Add(row);
        }
        public void Refresh()
        {
            if (_waiting && Time.realtimeSinceStartup >= _deadline) Received(_requestId, false);
            _send.SetEnabled(!_waiting && GameClient.Instance.IsLoggedIn && !string.IsNullOrWhiteSpace(_text.value));
        }
        private void Send()
        {
            if (_waiting || string.IsNullOrWhiteSpace(_text.value)) return;
            _waiting = true; _deadline = Time.realtimeSinceStartup + 10;
            _status.text = DisplayNames.FeedbackWaiting; _send.SetEnabled(false);
            _requestId = GameClient.Instance.SendFeedback(_text.value, _screen());
        }
        public void Received(int reqId, bool accepted)
        {
            if (!_waiting || reqId != _requestId) return;
            _waiting = false; _status.text = accepted ? DisplayNames.FeedbackThanks : DisplayNames.FeedbackRetry;
            if (accepted) _text.value = "";
        }
    }
}
