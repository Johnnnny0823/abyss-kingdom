using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 城內走動的小人：在已蓋好的建築門口之間來回，路線用格子 BFS 繞開建築地基。
    /// 建築被拖到別處後，正在走的人立刻改走新路線。純畫面效果，不影響規則。
    /// 圖片：Resources/Art/People/*.png（沒有就不生成小人）。
    /// </summary>
    public sealed class CityWalkers : MonoBehaviour
    {
        private const int Count = 6;
        private const float Speed = .55f; // 世界單位／秒

        private sealed class Walker
        {
            public Transform Root;
            public MarchAnimator Anim;
            public readonly List<Vector3> Path = new List<Vector3>();
            public float Wait;
        }

        private WorldScene _w;
        private System.Func<CityState> _state;
        private readonly List<Walker> _walkers = new List<Walker>();

        public static CityWalkers Create(WorldScene w, Transform parent, System.Func<CityState> state)
        {
            var sprites = Resources.LoadAll<Texture2D>("Art/People");
            if (sprites.Length == 0) return null;
            var go = new GameObject("Walkers") { layer = WorldScene.Layer };
            go.transform.SetParent(parent, false);
            var cw = go.AddComponent<CityWalkers>();
            cw._w = w;
            cw._state = state;
            for (int i = 0; i < Count; i++)
            {
                var root = w.Group("Walker" + i, go.transform, Vector3.zero);
                var art = w.Group("Art2D", root, Vector3.zero);
                w.AddBillboard(art);
                var body = w.Group("Body", art, Vector3.zero);
                var tex = sprites[i % sprites.Length];
                body.gameObject.AddComponent<SpriteRenderer>().sprite = AbyssKingdom.Client.UI.ArtLibrary.SpriteOf(tex, .42f);
                var anim = body.gameObject.AddComponent<MarchAnimator>();
                var shadow = w.Prim(PrimitiveType.Cylinder, new Color32(40, 34, 28, 255), new Vector3(0, .003f, 0), new Vector3(.22f, .002f, .12f), root);
                shadow.name = "Shadow";
                cw._walkers.Add(new Walker { Root = root, Anim = anim, Wait = i * .7f });
            }
            return cw;
        }

        private int _layoutSig;
        private bool[,] _blocked;

        private void Update()
        {
            var state = _state?.Invoke();
            if (state == null) return;

            // 建築被拖走、蓋好或拆掉：重算阻擋格，正在走的人立刻改走新路線（不會穿過剛放下的建築）
            int sig = LayoutSig(state);
            if (sig != _layoutSig || _blocked == null)
            {
                bool first = _blocked == null;
                _layoutSig = sig;
                _blocked = Blocked(state);
                if (!first)
                    foreach (var wk in _walkers)
                        if (wk.Path.Count > 0) Plan(wk, state);
            }

            foreach (var wk in _walkers)
            {
                if (wk.Path.Count == 0)
                {
                    wk.Anim.Moving = false;
                    wk.Wait -= Time.deltaTime;
                    if (wk.Wait <= 0) Plan(wk, state);
                    continue;
                }
                var target = wk.Path[0];
                var pos = wk.Root.localPosition;
                var step = target - pos;
                float d = step.magnitude;
                float move = Speed * Time.deltaTime;
                wk.Anim.Moving = true;
                if (Mathf.Abs(step.x) > .01f) wk.Anim.FacingLeft = step.x < 0;
                if (d <= move)
                {
                    wk.Root.localPosition = target;
                    wk.Path.RemoveAt(0);
                    if (wk.Path.Count == 0) wk.Wait = Random.Range(1.5f, 4f); // 到門口停一下
                }
                else wk.Root.localPosition = pos + step / d * move;
            }
        }

        // ───── 路線 ─────

        private static Vector3 CellCenter(int x, int y)
        {
            float half = CityLayout.GridSize / 2f;
            return new Vector3((x + .5f - half) * CityView.CellSize, 0, -(y + .5f - half) * CityView.CellSize);
        }

        private static (int x, int y) CellAt(Vector3 p)
        {
            float half = CityLayout.GridSize / 2f;
            return (Mathf.Clamp(Mathf.FloorToInt(p.x / CityView.CellSize + half), 0, CityLayout.GridSize - 1),
                Mathf.Clamp(Mathf.FloorToInt(-p.z / CityView.CellSize + half), 0, CityLayout.GridSize - 1));
        }

        private static bool In(int x, int y) => x >= 0 && y >= 0 && x < CityLayout.GridSize && y < CityLayout.GridSize;

        private static int LayoutSig(CityState s)
        {
            unchecked
            {
                int h = 17;
                foreach (var t in CityConfig.AllBuildings)
                {
                    var c = CityLayout.CellOf(s, t);
                    h = h * 31 + c.X;
                    h = h * 31 + c.Y;
                    h = h * 31 + (s.GetLevel(t) > 0 ? 1 : 0);
                }
                return h;
            }
        }

        /// <summary>所有建築（含還沒蓋的空地）的地基格。</summary>
        private static bool[,] Blocked(CityState s)
        {
            var blocked = new bool[CityLayout.GridSize, CityLayout.GridSize];
            foreach (var t in CityConfig.AllBuildings)
            {
                var c = CityLayout.CellOf(s, t);
                int f = CityLayout.Footprint(t);
                for (int dy = 0; dy < f; dy++)
                    for (int dx = 0; dx < f; dx++)
                        if (In(c.X + dx, c.Y + dy)) blocked[c.X + dx, c.Y + dy] = true;
            }
            return blocked;
        }

        /// <summary>
        /// 門口：建築周圍第一個走得到的格子。優先正面（畫面下方）中間，其次正面其他格、兩側、背面；
        /// 靠城牆或被其他建築擋住時就換一邊，不會落在任何建築的地基上。都被堵住回傳 null。
        /// </summary>
        private static (int x, int y)? Door(CityState s, BuildingType t, bool[,] blocked)
        {
            var c = CityLayout.CellOf(s, t);
            int f = CityLayout.Footprint(t);
            var candidates = new List<(int x, int y)> { (c.X + f / 2, c.Y + f) };
            for (int i = 0; i < f; i++) candidates.Add((c.X + i, c.Y + f));           // 正面
            for (int i = f - 1; i >= 0; i--) { candidates.Add((c.X - 1, c.Y + i)); candidates.Add((c.X + f, c.Y + i)); } // 兩側
            for (int i = 0; i < f; i++) candidates.Add((c.X + i, c.Y - 1));           // 背面
            foreach (var (x, y) in candidates)
                if (In(x, y) && !blocked[x, y]) return (x, y);
            return null;
        }

        private void Plan(Walker wk, CityState s)
        {
            var blocked = _blocked ?? Blocked(s);
            // 只去已經蓋好的建築（空地沒有人進出）
            var built = new List<BuildingType>();
            foreach (var t in CityConfig.AllBuildings)
                if (s.GetLevel(t) > 0) built.Add(t);
            wk.Path.Clear();
            if (built.Count == 0) { wk.Wait = 2f; return; }
            var goal = Door(s, built[Random.Range(0, built.Count)], blocked);

            var start = CellAt(wk.Root.localPosition);
            if (wk.Root.localPosition == Vector3.zero) // 第一次：從主堡門口出發
            {
                var keepDoor = Door(s, BuildingType.Keep, blocked);
                if (keepDoor == null) { wk.Wait = 2f; return; }
                start = keepDoor.Value;
                wk.Root.localPosition = CellCenter(start.x, start.y);
            }
            if (goal == null || goal.Value == start) { wk.Wait = 2f; return; }
            var cells = Bfs(blocked, start, goal.Value);
            if (cells == null) { wk.Wait = 2f; return; }
            foreach (var (x, y) in cells)
                wk.Path.Add(CellCenter(x, y) + new Vector3(Random.Range(-.08f, .08f), 0, Random.Range(-.08f, .08f)));
        }

        private static List<(int x, int y)> Bfs(bool[,] blocked, (int x, int y) start, (int x, int y) goal)
        {
            int n = CityLayout.GridSize;
            var prev = new Dictionary<(int, int), (int, int)>();
            var q = new Queue<(int x, int y)>();
            q.Enqueue(start);
            prev[start] = start;
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c == goal) break;
                foreach (var (dx, dy) in dirs)
                {
                    var nb = (c.x + dx, c.y + dy);
                    if (nb.Item1 < 0 || nb.Item2 < 0 || nb.Item1 >= n || nb.Item2 >= n) continue;
                    if (blocked[nb.Item1, nb.Item2]) continue; // 起點可以在地基上（建築剛被放到人身上），走出來就好
                    if (prev.ContainsKey(nb)) continue;
                    prev[nb] = c;
                    q.Enqueue(nb);
                }
            }
            if (!prev.ContainsKey(goal)) return null;
            var path = new List<(int, int)>();
            for (var c = goal; c != start; c = prev[c]) path.Add(c);
            path.Reverse();
            // 只留轉角點，走起來比較自然
            var simple = new List<(int x, int y)>();
            for (int i = 0; i < path.Count; i++)
            {
                if (i == path.Count - 1) { simple.Add(path[i]); break; }
                var a = i == 0 ? start : path[i - 1];
                var b = path[i + 1];
                if ((b.Item1 - a.Item1) != 0 && (b.Item2 - a.Item2) != 0) simple.Add(path[i]);
            }
            return simple;
        }
    }
}
