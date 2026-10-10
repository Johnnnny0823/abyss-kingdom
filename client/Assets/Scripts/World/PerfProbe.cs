using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 效能量測（啟動參數 -perflog）：開場 12 秒後量 6 秒的平均與最慢幀時間、場景物件數，寫進 Player.log（搜尋 PERF）。
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        private float _start = -1, _sum, _worst;
        private int _frames;

        public static void AttachIfRequested(GameObject host)
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-perflog") >= 0) host.AddComponent<PerfProbe>();
        }

        private void Update()
        {
            float t = Time.realtimeSinceStartup;
            if (t < 12f) return;
            if (_start < 0) _start = t;
            float dt = Time.unscaledDeltaTime;
            _sum += dt; _frames++; _worst = Mathf.Max(_worst, dt);
            if (t - _start < 6f) return;
            int objects = FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
            int renderers = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.isVisible) renderers++;
            Debug.Log($"PERF avg {_sum / _frames * 1000f:F1} ms ({_frames / _sum:F0} fps), worst {_worst * 1000f:F1} ms, active objects {objects}, visible renderers {renderers}");
            Destroy(this);
        }
    }
}
