using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 營地怪物（Models/World3D/Monster_1~3，分件人偶，原點在關節）的待機動作：
    /// 身體呼吸、頭左右張望、尾巴擺動、四足偶爾換腳、雙足的長臂垂擺。純畫面效果。
    /// </summary>
    public sealed class MonsterIdle : MonoBehaviour
    {
        private Transform _body, _head, _tail, _armL, _armR, _fl, _fr, _bl, _br, _legL, _legR;
        private Vector3 _base;
        private float _phase;

        public static void Attach(GameObject model, int seed)
        {
            var m = model.AddComponent<MonsterIdle>();
            var t = model.transform;
            m._body = t.Find("Body"); m._head = t.Find("Head"); m._tail = t.Find("Tail");
            m._armL = t.Find("ArmL"); m._armR = t.Find("ArmR");
            m._fl = t.Find("LegFL"); m._fr = t.Find("LegFR"); m._bl = t.Find("LegBL"); m._br = t.Find("LegBR");
            m._legL = t.Find("LegL"); m._legR = t.Find("LegR");
            m._base = t.localPosition;
            m._phase = (seed & 0xff) * .173f;
        }

        private void Update()
        {
            float t = Time.time + _phase;
            float breathe = Mathf.Sin(t * 1.3f);
            transform.localPosition = _base + Vector3.up * (breathe * .015f + .015f) * transform.localScale.y;
            if (_body != null) _body.localRotation = Quaternion.Euler(breathe * 2f, 0, 0);
            // 頭：慢慢張望，偶爾低頭嗅聞
            if (_head != null)
                _head.localRotation = Quaternion.Euler(Mathf.Max(0, Mathf.Sin(t * .37f)) * 14f, Mathf.Sin(t * .53f) * 24f, 0);
            if (_tail != null) _tail.localRotation = Quaternion.Euler(Mathf.Sin(t * 2.1f) * 6f, Mathf.Sin(t * 1.7f) * 22f, 0);
            if (_armL != null) _armL.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.1f) * 8f, 0, Mathf.Sin(t * .9f) * 4f);
            if (_armR != null) _armR.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.1f + 1.3f) * 8f, 0, -Mathf.Sin(t * .9f) * 4f);
            // 四足：偶爾抬一隻前腳刨地
            float paw = Mathf.Max(0, Mathf.Sin(t * .8f)) * Mathf.Max(0, Mathf.Sin(t * 6f));
            if (_fl != null) _fl.localRotation = Quaternion.Euler(-paw * 25f, 0, 0);
            if (_fr != null) _fr.localRotation = Quaternion.identity;
            if (_bl != null) _bl.localRotation = Quaternion.identity;
            if (_br != null) _br.localRotation = Quaternion.identity;
            // 雙足：重心左右換腳
            float sway = Mathf.Sin(t * .7f);
            if (_legL != null) _legL.localRotation = Quaternion.Euler(0, 0, sway * 3f);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(0, 0, sway * 3f);
        }
    }
}
