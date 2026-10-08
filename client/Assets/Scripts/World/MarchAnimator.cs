using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 2D 部隊的程式動畫（不需要逐格圖）：行軍時隨步伐上下彈跳、左右輕晃、落地時微微壓扁；
    /// 往畫面左邊走時翻面；駐紮時改成慢慢呼吸的待機。掛在面向鏡頭的物件底下的 Body 上，
    /// 所以不會和「永遠面向鏡頭」的旋轉互相打架。純畫面效果。
    /// </summary>
    public sealed class MarchAnimator : MonoBehaviour
    {
        /// <summary>正在移動（行軍、返回）；false = 駐紮或採集中。</summary>
        public bool Moving;
        /// <summary>移動方向在畫面上是往左。</summary>
        public bool FacingLeft;

        private SpriteRenderer _sprite;
        private float _phase;

        private void Awake()
        {
            _sprite = GetComponent<SpriteRenderer>();
            _phase = Random.value * 10f; // 不同部隊步伐錯開
        }

        private void Update()
        {
            float t = Time.time + _phase;
            if (Moving)
            {
                // 一步約 0.4 秒：彈跳＋左右晃＋落地壓扁
                float step = t * Mathf.PI * 2.5f;
                float hop = Mathf.Abs(Mathf.Sin(step));
                transform.localPosition = new Vector3(0, hop * .07f, 0);
                transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(step) * 4f);
                float squash = 1f - (1f - hop) * .06f;
                transform.localScale = new Vector3(2f - squash, squash, 1f);
            }
            else
            {
                // 待機：慢慢呼吸
                float breathe = Mathf.Sin(t * 1.6f) * .015f;
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = new Vector3(1f - breathe * .5f, 1f + breathe, 1f);
            }
            if (_sprite != null) _sprite.flipX = FacingLeft;
        }
    }
}
