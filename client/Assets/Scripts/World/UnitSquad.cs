using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 大地圖上的 3D 行軍小隊（Models/Units3D）：三名士兵排成三角隊形，朝行進方向轉身，
    /// 用分件人偶的零件（LegL/LegR/ArmL/ArmR/Head，原點在關節，見 tools/blender/UNITS3D.md）做走路；
    /// 駐紮時改成輕微呼吸的待機。純畫面效果。
    /// </summary>
    public sealed class UnitSquad : MonoBehaviour
    {
        /// <summary>正在移動；false = 駐紮、採集中。</summary>
        public bool Moving;
        /// <summary>行進方向（世界座標、水平）。</summary>
        public Vector3 Direction;

        private sealed class Soldier
        {
            public Transform Root, LegL, LegR, ArmL, ArmR, Head;
            public Vector3 Base;
            public float Phase;
        }

        private readonly List<Soldier> _soldiers = new List<Soldier>();

        /// <summary>隊形（模型單位，士兵身高約 1；臉朝 -Z，所以前方是 -Z）。</summary>
        private static readonly Vector3[] Formation = { new Vector3(0, 0, -.32f), new Vector3(-.42f, 0, .28f), new Vector3(.42f, 0, .28f) };

        /// <summary>建一支小隊。unitName = Units3D 底下的模型名（例如 Shield）；沒有模型回傳 null。</summary>
        public static UnitSquad Create(Transform parent, string unitName, float height, int seed)
        {
            if (ModelLibrary.Get("Units3D/" + unitName) == null) return null;
            var go = new GameObject("Squad3D_" + unitName) { layer = WorldScene.Layer };
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * height;
            var squad = go.AddComponent<UnitSquad>();
            for (int i = 0; i < Formation.Length; i++)
            {
                var model = ModelLibrary.Spawn("Units3D/" + unitName, go.transform, Formation[i], 1f);
                LivingModel.Attach(model, unitName, seed + i * 7);
                var t = model.transform;
                squad._soldiers.Add(new Soldier
                {
                    Root = t, Base = Formation[i], Phase = i * .9f + (seed & 0xf) * .37f,
                    LegL = t.Find("LegL"), LegR = t.Find("LegR"), ArmL = t.Find("ArmL"), ArmR = t.Find("ArmR"), Head = t.Find("Head"),
                });
            }
            return squad;
        }

        private void Update()
        {
            // 慢慢轉向行進方向（模型臉朝 -Z）
            if (Moving && Direction.sqrMagnitude > 1e-6f)
            {
                var target = Quaternion.LookRotation(-new Vector3(Direction.x, 0, Direction.z).normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            }

            foreach (var s in _soldiers)
            {
                float t = Time.time + s.Phase;
                if (Moving)
                {
                    // 一步約 0.33 秒：腿前後交錯、手臂反向擺、身體隨步伐上下
                    float step = t * Mathf.PI * 3f;
                    float swing = Mathf.Sin(step);
                    Rot(s.LegL, swing * 30f);
                    Rot(s.LegR, -swing * 30f);
                    Rot(s.ArmL, -swing * 14f);
                    Rot(s.ArmR, swing * 18f);
                    Rot(s.Head, Mathf.Sin(step * 2f) * 2f);
                    s.Root.localPosition = s.Base + Vector3.up * Mathf.Abs(Mathf.Cos(step)) * .05f;
                }
                else
                {
                    // 待機：慢慢呼吸，頭偶爾轉一下
                    float breathe = Mathf.Sin(t * 1.6f);
                    Rot(s.LegL, 0); Rot(s.LegR, 0);
                    Rot(s.ArmL, breathe * 2f); Rot(s.ArmR, -breathe * 2f);
                    if (s.Head != null) s.Head.localRotation = Quaternion.Euler(0, Mathf.Sin(t * .4f) * 12f, 0);
                    s.Root.localPosition = s.Base + Vector3.up * (breathe * .006f + .006f);
                }
            }
        }

        private static void Rot(Transform part, float degreesX)
        {
            if (part != null) part.localRotation = Quaternion.Euler(degreesX, 0, 0);
        }
    }
}
