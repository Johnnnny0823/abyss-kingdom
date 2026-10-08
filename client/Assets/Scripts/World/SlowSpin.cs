using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>讓物件繞 Y 軸慢慢轉（深淵裡的霧）。純畫面效果。</summary>
    public sealed class SlowSpin : MonoBehaviour
    {
        public float DegreesPerSecond = 2f;

        private void Update() => transform.Rotate(0, DegreesPerSecond * Time.deltaTime, 0, Space.Self);
    }
}
