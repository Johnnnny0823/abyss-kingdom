using System;
using AbyssKingdom.Client.World;
using AbyssKingdom.Shared.Map;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// 新手開場「墜入深淵」：鏡頭從深淵上方俯瞰 → 沉入坑中看地層 → 升出坑口俯瞰大陸 → 飛到自己的主城，
    /// 配上四段旁白（世界觀見 GDD 2.1）。上下黑邊、可隨時跳過。播完回呼 onDone。純畫面，不影響規則。
    /// </summary>
    public sealed class IntroSequence : MonoBehaviour
    {
        private const float LineSeconds = 6f;

        private static readonly string[] Lines =
        {
            "三百年前，大陸中央塌陷，裂開一道看不見底的深淵——永暗之喉。",
            "每當一位神祇死去，祂的整個世界便剝落、沉入喉中，一層壓著一層，成了神話的地層。",
            "如今，你是新任的端座領主。在裂口邊緣築城，與深淵立約，取得淵契之力。",
            "鄰人虎視眈眈，深淵在腳下低語——先讓你的領地站穩腳步吧。",
        };

        private struct Key { public Vector3 Pos, Look; }

        private WorldScene _w;
        private Key[] _keys;
        private Vector3 _home;
        private VisualElement _overlay;
        private Label _line;
        private Action _onDone;
        private float _t;
        private bool _done;

        /// <summary>開始播放。root = UI 根元素；home = 自己主城的世界座標。</summary>
        public static IntroSequence Play(GameObject host, VisualElement root, WorldScene w, Vector3 home, Action onDone)
        {
            var s = host.AddComponent<IntroSequence>();
            s._w = w;
            s._home = home;
            s._onDone = onDone;
            float pit = SurfaceConfig.PitRadius * SurfaceView.S;
            // 鏡頭關鍵格（世界座標）：深淵中心在原點，往下是 -Y
            var homeLook = new Vector3(home.x, 0, home.z);
            var homeRot = Quaternion.Euler(52, 0, 0);
            s._keys = new[]
            {
                new Key { Pos = new Vector3(0, 7f, -1.5f), Look = new Vector3(0, -25f, 0) },                       // 從上方俯瞰無底的黑
                new Key { Pos = new Vector3(0, -3f, -pit * .55f), Look = new Vector3(0, -11f, pit * .45f) },       // 沉進坑裡，看對面一層層的岩壁
                new Key { Pos = new Vector3(0, 12f, -pit * 1.9f), Look = new Vector3(0, -3f, 0) },                 // 升出坑口，俯瞰大陸
                new Key { Pos = homeLook - homeRot * Vector3.forward * 13f, Look = homeLook },                     // 飛到自己的主城
            };
            w.SetMode(WorldScene.Mode.Surface);

            s._overlay = El("intro");
            s._overlay.Add(El("intro-bar", "intro-top"));
            s._overlay.Add(El("intro-bar", "intro-bottom"));
            s._line = Lbl("", "intro-line");
            s._overlay.Add(s._line);
            s._overlay.Add(Btn("跳過 ▶", s.Finish, "intro-skip"));
            root.Add(s._overlay);
            return s;
        }

        private void Update()
        {
            if (_done) return;
            _t += Time.unscaledDeltaTime;
            float total = LineSeconds * Lines.Length;
            if (_t >= total) { Finish(); return; }

            // 鏡頭：在相鄰關鍵格之間平滑移動（每段旁白對應一段運鏡，最後一段停在主城上方）
            float seg = Mathf.Max(0, _t / LineSeconds - 1f); // 第一段旁白時停在第一格（俯瞰深淵），之後每段移到下一格
            int i = Mathf.Min((int)seg, _keys.Length - 1);
            int j = Mathf.Min(i + 1, _keys.Length - 1);
            float f = Mathf.SmoothStep(0, 1, Mathf.Clamp01((seg - i) * 1.25f)); // 每段前 80% 移動、後 20% 停一下
            var pos = Vector3.Lerp(_keys[i].Pos, _keys[j].Pos, f);
            var look = Vector3.Lerp(_keys[i].Look, _keys[j].Look, f);
            var rot = Quaternion.LookRotation(look - pos, Vector3.up);
            _w.SetCinematic(pos, rot, j == _keys.Length - 1 ? Vector3.Lerp(Vector3.zero, _home, f) : Vector3.zero);

            // 旁白：每段淡入、停留、淡出
            int line = Mathf.Min((int)(_t / LineSeconds), Lines.Length - 1);
            float lt = _t - line * LineSeconds;
            _line.text = Lines[line];
            _line.style.opacity = Mathf.Clamp01(lt / .8f) * Mathf.Clamp01((LineSeconds - lt) / .8f);
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            _overlay.RemoveFromHierarchy();
            _w.EndCinematic();
            _onDone?.Invoke();
            Destroy(this);
        }
    }
}
