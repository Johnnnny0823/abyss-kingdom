using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 大地圖上的 3D 行軍小隊（Models/Units3D）：主將走在最前面，三名主力兵種士兵在後面排成三角隊形，
    /// 朝行進方向轉身，用分件人偶的零件（原點在關節，見 tools/blender/UNITS3D.md）做動作：
    ///   步兵：雙腿交錯、手臂反向擺（持弩的雙手同步小擺，弩才不會離手）
    ///   騎兵：坐騎四腳對角奔跑、軀幹前後起伏，騎士坐著不擺腿
    /// 駐紮時改成輕微呼吸的待機。純畫面效果。
    /// </summary>
    public sealed class UnitSquad : MonoBehaviour
    {
        /// <summary>正在移動；false = 駐紮、採集中。</summary>
        public bool Moving;
        /// <summary>行進方向（世界座標、水平）。</summary>
        public Vector3 Direction;
        /// <summary>主將有 3D 模型（頭上的頭像可以收起來）。</summary>
        public bool HasHeroModel { get; private set; }

        /// <summary>雙手持弩的角色：手臂同步小擺。</summary>
        private static readonly HashSet<string> TwoHanded = new HashSet<string> { "Crossbow", "Vesper" };

        private sealed class Unit
        {
            public Transform Root, LegL, LegR, ArmL, ArmR, Head, Mount, FL, FR, BL, BR;
            public Vector3 Base;
            public float Phase;
            public bool TwoHanded;
        }

        private readonly List<Unit> _units = new List<Unit>();

        /// <summary>士兵隊形（模型單位，身高約 1；臉朝 -Z，所以前方是 -Z）。主將站在最前面。</summary>
        private static readonly Vector3[] Formation = { new Vector3(-.42f, 0, .1f), new Vector3(.42f, 0, .1f), new Vector3(0, 0, .62f) };
        private static readonly Vector3 HeroSpot = new Vector3(0, 0, -.5f);

        /// <summary>
        /// 建一支小隊。unitName = 主力兵種的模型名（Shield…），heroName = 主將的模型名（Hold…）。
        /// 兵種沒有模型回傳 null（改用 2D 圖）；主將沒有模型就只有士兵。
        /// </summary>
        public static UnitSquad Create(Transform parent, string unitName, string heroName, float height, int seed)
        {
            if (ModelLibrary.Get("Units3D/" + unitName) == null) return null;
            var go = new GameObject($"Squad3D_{unitName}_{heroName}") { layer = WorldScene.Layer };
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * height;
            var squad = go.AddComponent<UnitSquad>();
            bool mounted = unitName == "Rider";
            for (int i = 0; i < Formation.Length; i++)
                squad.Add(unitName, Formation[i] * (mounted ? 1.45f : 1f), seed + i * 7);
            if (ModelLibrary.Get("Units3D/" + heroName) != null)
            {
                squad.Add(heroName, HeroSpot * (mounted ? 1.3f : 1f), seed + 99);
                squad.HasHeroModel = true;
            }
            return squad;
        }

        private void Add(string name, Vector3 at, int seed)
        {
            var model = ModelLibrary.Spawn("Units3D/" + name, transform, at, 1f);
            LivingModel.Attach(model, name, seed);
            var t = model.transform;
            _units.Add(new Unit
            {
                Root = t, Base = at, Phase = (seed & 0xff) * .137f, TwoHanded = TwoHanded.Contains(name),
                LegL = t.Find("LegL"), LegR = t.Find("LegR"), ArmL = t.Find("ArmL"), ArmR = t.Find("ArmR"), Head = t.Find("Head"),
                Mount = t.Find("Mount"), FL = t.Find("MountLegFL"), FR = t.Find("MountLegFR"), BL = t.Find("MountLegBL"), BR = t.Find("MountLegBR"),
            });
        }

        private void Update()
        {
            // 慢慢轉向行進方向（模型臉朝 -Z）
            if (Moving && Direction.sqrMagnitude > 1e-6f)
            {
                var target = Quaternion.LookRotation(-new Vector3(Direction.x, 0, Direction.z).normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            }
            foreach (var u in _units)
            {
                if (u.Mount != null) AnimateRider(u);
                else AnimateWalker(u);
            }
        }

        private void AnimateWalker(Unit u)
        {
            float t = Time.time + u.Phase;
            if (Moving)
            {
                // 一步約 0.33 秒：腿前後交錯、手臂反向擺、身體隨步伐上下
                float step = t * Mathf.PI * 3f;
                float swing = Mathf.Sin(step);
                Rot(u.LegL, swing * 30f);
                Rot(u.LegR, -swing * 30f);
                if (u.TwoHanded) { Rot(u.ArmL, swing * 5f); Rot(u.ArmR, swing * 5f); }
                else { Rot(u.ArmL, -swing * 14f); Rot(u.ArmR, swing * 18f); }
                Rot(u.Head, Mathf.Sin(step * 2f) * 2f);
                u.Root.localPosition = u.Base + Vector3.up * Mathf.Abs(Mathf.Cos(step)) * .05f;
            }
            else Idle(u, t);
        }

        private void AnimateRider(Unit u)
        {
            float t = Time.time + u.Phase;
            if (Moving)
            {
                // 小跑：對角的兩腳同步（左前＋右後、右前＋左後），坐騎前後起伏，騎士跟著上下
                float step = t * Mathf.PI * 3.6f;
                float swing = Mathf.Sin(step);
                Rot(u.FL, swing * 34f); Rot(u.BR, swing * 30f);
                Rot(u.FR, -swing * 34f); Rot(u.BL, -swing * 30f);
                Rot(u.Mount, Mathf.Sin(step * 2f) * 3f);
                Rot(u.ArmL, Mathf.Sin(step * 2f) * 4f);
                Rot(u.ArmR, Mathf.Sin(step * 2f) * 4f);
                u.Root.localPosition = u.Base + Vector3.up * Mathf.Abs(Mathf.Cos(step)) * .045f;
            }
            else
            {
                // 待機：坐騎偶爾換腳、低頭吐氣
                float breathe = Mathf.Sin(t * 1.4f);
                Rot(u.FL, 0); Rot(u.FR, 0); Rot(u.BL, 0); Rot(u.BR, Mathf.Max(0, Mathf.Sin(t * .5f)) * 6f);
                Rot(u.Mount, breathe * 1.2f);
                Rot(u.ArmL, breathe * 1.5f); Rot(u.ArmR, -breathe * 1.5f);
                if (u.Head != null) u.Head.localRotation = Quaternion.Euler(0, Mathf.Sin(t * .4f) * 12f, 0);
                u.Root.localPosition = u.Base;
            }
        }

        private static void Idle(Unit u, float t)
        {
            // 待機：慢慢呼吸，頭偶爾轉一下
            float breathe = Mathf.Sin(t * 1.6f);
            Rot(u.LegL, 0); Rot(u.LegR, 0);
            Rot(u.ArmL, breathe * 2f); Rot(u.ArmR, u.TwoHanded ? breathe * 2f : -breathe * 2f);
            if (u.Head != null) u.Head.localRotation = Quaternion.Euler(0, Mathf.Sin(t * .4f) * 12f, 0);
            u.Root.localPosition = u.Base + Vector3.up * (breathe * .006f + .006f);
        }

        private static void Rot(Transform part, float degreesX)
        {
            if (part != null) part.localRotation = Quaternion.Euler(degreesX, 0, 0);
        }
    }
}
