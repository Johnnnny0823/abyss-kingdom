using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 大地圖（地表大陸，800×800 格）的灰盒：中央的深淵、三個同心分區、分區之間的山脈與關隘、
    /// 資源田、怪物營地、各領主的主城，以及自己的行軍（虛線＋移動中的部隊）。
    /// 沒有道路；空的主城位置平常不顯示。物件分區塊，只顯示鏡頭附近的區塊。
    /// </summary>
    public sealed class SurfaceView
    {
        public const float S = 0.2f;          // 1 格 = 0.2 世界單位
        private const int ChunkTiles = 40;
        private const int Chunks = SurfaceConfig.Size / ChunkTiles;

        public readonly Transform Root;
        public event Action<int, int> TileClicked;

        private readonly WorldScene _w;
        private readonly Transform[,] _chunks = new Transform[Chunks, Chunks];
        private readonly Dictionary<int, (GameObject castle, TextMesh name, bool mine)> _castles = new Dictionary<int, (GameObject, TextMesh, bool)>();
        private readonly GameObject _select;
        private readonly Dictionary<int, GameObject> _shields = new Dictionary<int, GameObject>();
        private readonly List<GameObject> _dots = new List<GameObject>();
        private readonly List<Transform> _troops = new List<Transform>();
        private float _nextCull;

        /// <summary>鏡頭可平移的範圍（半寬、半高）。</summary>
        public Vector2 Bounds => new Vector2(SurfaceConfig.OuterRadius * S, SurfaceConfig.OuterRadius * S);

        public SurfaceView(WorldScene w)
        {
            _w = w;
            Root = w.Group("Surface", w.transform, Vector3.zero);
            for (int cy = 0; cy < Chunks; cy++)
                for (int cx = 0; cx < Chunks; cx++)
                    _chunks[cx, cy] = w.Group($"Chunk{cx}_{cy}", Root, Vector3.zero);
            BuildGround();
            BuildWalls();
            foreach (var f in SurfaceLogic.Features()) BuildFeature(f);
            BuildTrees();
            _select = w.Prim(PrimitiveType.Cube, Color.white, Vector3.zero, Vector3.one, Root);
            _select.GetComponent<Renderer>().sharedMaterial = w.Glow;
            _select.SetActive(false);
        }

        /// <summary>格子 (x, y) 的中心點（世界座標）。</summary>
        public static Vector3 TilePos(float x, float y) =>
            new Vector3((x + 0.5f - SurfaceConfig.Center) * S, 0, -(y + 0.5f - SurfaceConfig.Center) * S);

        public static Vector3 CityPos(int x, int y) => TilePos(x + 0.5f, y + 0.5f);

        /// <summary>世界座標 → 格子。</summary>
        public static (int x, int y) TileAt(Vector3 p) =>
            (Mathf.FloorToInt(p.x / S + SurfaceConfig.Center), Mathf.FloorToInt(-p.z / S + SurfaceConfig.Center));

        private Transform ChunkOf(int x, int y) =>
            _chunks[Mathf.Clamp(x / ChunkTiles, 0, Chunks - 1), Mathf.Clamp(y / ChunkTiles, 0, Chunks - 1)];

        // ───── 建構 ─────

        private void Disc(float radiusTiles, float top, Color32 c)
        {
            float d = radiusTiles * 2 * S;
            _w.Prim(PrimitiveType.Cylinder, c, new Vector3(0, top - 0.01f, 0), new Vector3(d, 0.01f, d), Root);
        }

        private void BuildGround()
        {
            float size = SurfaceConfig.Size * S;
            _w.Prim(PrimitiveType.Cube, new Color32(52, 50, 48, 255), new Vector3(0, -0.03f, 0), new Vector3(size + 40, 0.02f, size + 40), Root);
            Disc(SurfaceConfig.OuterRadius + 6, -0.010f, new Color32(84, 78, 66, 255));   // 地圖邊緣的荒地
            Disc(SurfaceConfig.OuterRadius, -0.008f, new Color32(104, 132, 80, 255));     // 外環（新手區）
            Disc(SurfaceConfig.MiddleOuter, -0.006f, new Color32(128, 128, 82, 255));     // 中環
            Disc(SurfaceConfig.RimOuter, -0.004f, new Color32(146, 118, 84, 255));        // 崖緣
            for (int i = 0; i < 7; i++)                                                    // 深淵：一圈圈往下變黑
                Disc(SurfaceConfig.PitRadius + 4 - i * 9, -0.002f + 0.0005f * i, new Color32((byte)(46 - i * 6), (byte)(38 - i * 5), (byte)(48 - i * 6), 255));
        }

        /// <summary>分區之間的環狀山脈；關隘的位置留空，兩側各一座石塔。</summary>
        private void BuildWalls()
        {
            for (int b = 0; b < 2; b++)
            {
                int r = SurfaceConfig.Boundaries[b];
                int n = Mathf.RoundToInt(2 * Mathf.PI * r / 3f);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n;
                    float jitter = (Hash(i, b * 7 + 1) % 5 - 2) * 0.6f;
                    int x = SurfaceConfig.Center + Mathf.RoundToInt(Mathf.Cos(a) * (r + jitter));
                    int y = SurfaceConfig.Center + Mathf.RoundToInt(Mathf.Sin(a) * (r + jitter));
                    if (SurfaceLogic.NearPass(x, y, 1)) continue;
                    float h = 0.5f + Hash(i, b * 7 + 2) % 6 * 0.12f;
                    float wdt = 0.9f + Hash(i, b * 7 + 3) % 4 * 0.15f;
                    var p = TilePos(x, y);
                    _w.Prim(PrimitiveType.Sphere, new Color32(118, 108, 96, 255), p + Vector3.up * h * 0.35f, new Vector3(wdt, h, wdt), ChunkOf(x, y));
                    if (h > 0.9f) _w.Prim(PrimitiveType.Sphere, new Color32(220, 224, 228, 255), p + Vector3.up * h * 0.72f, new Vector3(wdt * 0.4f, h * 0.25f, wdt * 0.4f), ChunkOf(x, y));
                }
                for (int i = 0; i < SurfaceConfig.PassesPerBoundary; i++)
                {
                    var (px, py) = SurfaceLogic.Pass(b, i);
                    var center = TilePos(px, py);
                    var radial = new Vector3(center.x, 0, center.z).normalized;
                    var side = Vector3.Cross(radial, Vector3.up);
                    var stone = new Color32(190, 172, 124, 255);
                    var t = ChunkOf(px, py);
                    foreach (var s in new[] { -1f, 1f })
                    {
                        var tower = center + side * (SurfaceConfig.PassRadius * S * s);
                        _w.Prim(PrimitiveType.Cylinder, stone, tower + Vector3.up * 0.35f, new Vector3(0.35f, 0.35f, 0.35f), t);
                        _w.Prim(PrimitiveType.Cylinder, new Color32(150, 60, 50, 255), tower + Vector3.up * 0.76f, new Vector3(0.42f, 0.06f, 0.42f), t);
                    }
                    _w.Text(t, center + Vector3.up * 0.9f, 0.05f, new Color(1f, 0.9f, 0.6f)).text = "關隘";
                }
            }
        }

        private void BuildFeature(Feature f)
        {
            var t = _w.Group("Feature", ChunkOf(f.X, f.Y), TilePos(f.X, f.Y));
            t.localScale = Vector3.one * 1.5f;
            var label = new Color(1f, 0.97f, 0.9f);
            switch (f.Type)
            {
                case FeatureType.ResourceField:
                    BuildField(f, t);
                    _w.Text(t, new Vector3(0, 0.3f, 0), 0.022f, label).text = "Lv." + f.Level;
                    break;
                case FeatureType.MonsterCamp:
                    for (int i = 0; i < 3; i++)
                    {
                        var tent = _w.Prim(PrimitiveType.Cube, new Color32(120, 46, 42, 255), new Vector3((i - 1) * 0.11f, 0.05f, (i % 2) * 0.08f), new Vector3(0.09f, 0.09f, 0.12f), t);
                        tent.transform.localRotation = Quaternion.Euler(0, 0, 45);
                    }
                    _w.Prim(PrimitiveType.Cylinder, new Color32(70, 50, 40, 255), new Vector3(0.12f, 0.12f, -0.06f), new Vector3(0.015f, 0.12f, 0.015f), t);
                    _w.Prim(PrimitiveType.Cube, new Color32(170, 40, 40, 255), new Vector3(0.15f, 0.2f, -0.06f), new Vector3(0.06f, 0.04f, 0.005f), t);
                    _w.Text(t, new Vector3(0, 0.32f, 0), 0.022f, new Color(1f, 0.7f, 0.65f)).text = "怪 Lv." + f.Level;
                    break;
                case FeatureType.AbyssGate:
                    t.localScale = Vector3.one * 3f;
                    _w.Prim(PrimitiveType.Cylinder, new Color32(150, 130, 96, 255), new Vector3(0, 0.004f, 0), new Vector3(0.34f, 0.004f, 0.34f), t);
                    _w.Prim(PrimitiveType.Cylinder, new Color32(8, 8, 12, 255), new Vector3(0, 0.006f, 0), new Vector3(0.26f, 0.004f, 0.26f), t);
                    for (int i = 0; i < 2; i++)
                        _w.Prim(PrimitiveType.Cube, new Color32(110, 96, 140, 255), new Vector3((i - 0.5f) * 0.3f, 0.12f, 0), new Vector3(0.04f, 0.24f, 0.04f), t);
                    _w.Prim(PrimitiveType.Cube, new Color32(110, 96, 140, 255), new Vector3(0, 0.25f, 0), new Vector3(0.36f, 0.03f, 0.04f), t);
                    _w.Text(t, new Vector3(0, 0.36f, 0), 0.016f, new Color(0.8f, 0.78f, 1f)).text = "深淵入口";
                    break;
            }
        }

        private void BuildField(Feature f, Transform t)
        {
            switch (f.Resource)
            {
                case ResourceType.Wood:
                    for (int i = 0; i < 4; i++) TreeAt(t, new Vector3((i % 2 - 0.5f) * 0.14f, 0, (i / 2 - 0.5f) * 0.14f), 1f);
                    break;
                case ResourceType.Stone:
                    for (int i = 0; i < 4; i++)
                    {
                        var r = _w.Prim(PrimitiveType.Cube, new Color32(140, 146, 160, 255), new Vector3((i % 2 - 0.5f) * 0.12f, 0.04f, (i / 2 - 0.5f) * 0.12f), new Vector3(0.09f, 0.08f, 0.09f), t);
                        r.transform.localRotation = Quaternion.Euler(0, i * 23, 0);
                    }
                    break;
                case ResourceType.Food:
                    for (int i = 0; i < 4; i++)
                        _w.Prim(PrimitiveType.Cube, i % 2 == 0 ? new Color32(120, 168, 70, 255) : new Color32(170, 160, 80, 255),
                            new Vector3((i % 2 - 0.5f) * 0.13f, 0.01f, (i / 2 - 0.5f) * 0.13f), new Vector3(0.12f, 0.02f, 0.12f), t);
                    _w.Prim(PrimitiveType.Sphere, new Color32(200, 190, 170, 255), new Vector3(0, 0.05f, 0), new Vector3(0.07f, 0.07f, 0.07f), t);
                    break;
                default:
                    for (int i = 0; i < 3; i++)
                    {
                        var c = _w.Prim(PrimitiveType.Cube, new Color32(90, 150, 230, 255), new Vector3((i - 1) * 0.08f, 0.08f, (i % 2) * 0.05f), new Vector3(0.04f, 0.16f + i % 2 * 0.06f, 0.04f), t);
                        c.transform.localRotation = Quaternion.Euler(10 - i * 10, i * 30, 12 - i * 12);
                    }
                    break;
            }
        }

        private void TreeAt(Transform parent, Vector3 p, float k)
        {
            _w.Prim(PrimitiveType.Cylinder, new Color32(96, 70, 46, 255), p + Vector3.up * 0.04f * k, new Vector3(0.02f * k, 0.04f * k, 0.02f * k), parent);
            _w.Prim(PrimitiveType.Sphere, new Color32(70, 120, 60, 255), p + Vector3.up * 0.12f * k, new Vector3(0.1f * k, 0.12f * k, 0.1f * k), parent);
        }

        /// <summary>零星的樹（純裝飾）：放在主城候選位置與資源點之間的空隙。</summary>
        private void BuildTrees()
        {
            for (int cy = 0; cy < SurfaceConfig.Cells; cy++)
                for (int cx = 0; cx < SurfaceConfig.Cells; cx++)
                {
                    if (Hash(cx, cy * 31 + 5) % 100 >= 28) continue;
                    int x = cx * SurfaceConfig.Cell + 8, y = cy * SurfaceConfig.Cell + 1 + Hash(cx, cy) % 3;
                    if (!SurfaceLogic.Walkable(x, y) || SurfaceLogic.NearPass(x, y, 2)) continue;
                    var t = ChunkOf(x, y);
                    var p = TilePos(x, y);
                    for (int i = 0; i < 3; i++)
                        TreeAt(t, p + new Vector3((i - 1) * 0.18f, 0, (i % 2) * 0.16f), 2.2f + i % 2 * 0.5f);
                }
        }

        private GameObject Castle(Transform parent, Vector3 pos, bool mine)
        {
            var root = _w.Group("Castle", parent, pos).gameObject;
            var t = root.transform;
            t.localScale = Vector3.one * 1.25f;
            var wall = mine ? new Color32(226, 196, 130, 255) : new Color32(176, 150, 130, 255);
            var roof = mine ? new Color32(70, 110, 190, 255) : new Color32(150, 60, 50, 255);
            _w.Prim(PrimitiveType.Cube, wall, new Vector3(0, 0.05f, 0), new Vector3(0.3f, 0.1f, 0.3f), t);
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3((i % 2 - 0.5f) * 0.28f, 0, (i / 2 - 0.5f) * 0.28f);
                _w.Prim(PrimitiveType.Cylinder, wall, c + Vector3.up * 0.08f, new Vector3(0.07f, 0.08f, 0.07f), t);
                _w.Prim(PrimitiveType.Cylinder, roof, c + Vector3.up * 0.18f, new Vector3(0.08f, 0.02f, 0.08f), t);
            }
            _w.Prim(PrimitiveType.Cube, wall, new Vector3(0, 0.2f, 0), new Vector3(0.15f, 0.22f, 0.15f), t);
            var top = _w.Prim(PrimitiveType.Cube, roof, new Vector3(0, 0.33f, 0), new Vector3(0.12f, 0.12f, 0.12f), t);
            top.transform.localRotation = Quaternion.Euler(45, 45, 0);
            _w.Prim(PrimitiveType.Cylinder, new Color32(80, 70, 60, 255), new Vector3(0, 0.45f, 0), new Vector3(0.01f, 0.08f, 0.01f), t);
            _w.Prim(PrimitiveType.Cube, roof, new Vector3(0.035f, 0.5f, 0), new Vector3(0.07f, 0.04f, 0.004f), t);
            return root;
        }

        // ───── 狀態 ─────

        /// <summary>更新主城（全服）與選取標記。</summary>
        public void SetCities(List<CityPin> pins, (int x, int y)? selected, bool selectedIsCity)
        {
            var seen = new HashSet<int>();
            foreach (var pin in pins)
            {
                int key = SurfaceLogic.Tile(pin.X, pin.Y);
                seen.Add(key);
                _castles.TryGetValue(key, out var c);
                if (c.castle == null || c.mine != pin.Mine)
                {
                    if (c.castle != null) UnityEngine.Object.Destroy(c.castle);
                    var castle = Castle(ChunkOf(pin.X, pin.Y), CityPos(pin.X, pin.Y), pin.Mine);
                    var name = _w.Text(castle.transform, new Vector3(0, 0.66f, 0), 0.026f, pin.Mine ? new Color(1f, 0.86f, 0.5f) : Color.white);
                    c = (castle, name, pin.Mine);
                    _castles[key] = c;
                }
                c.name.text = pin.Name;

                // 護盾：主城外一圈發光的圓
                _shields.TryGetValue(key, out var shield);
                if (pin.Shielded && shield == null)
                {
                    shield = _w.Prim(PrimitiveType.Cylinder, Color.white, CityPos(pin.X, pin.Y) + Vector3.up * 0.003f, new Vector3(0.9f, 0.003f, 0.9f), ChunkOf(pin.X, pin.Y));
                    shield.GetComponent<Renderer>().sharedMaterial = _w.Mat(new Color32(110, 180, 255, 255));
                    _shields[key] = shield;
                }
                else if (!pin.Shielded && shield != null)
                {
                    UnityEngine.Object.Destroy(shield);
                    _shields.Remove(key);
                }
            }
            var gone = new List<int>();
            foreach (var key in _castles.Keys) if (!seen.Contains(key)) gone.Add(key);
            foreach (var key in gone)
            {
                UnityEngine.Object.Destroy(_castles[key].castle);
                _castles.Remove(key);
                if (_shields.TryGetValue(key, out var s)) { UnityEngine.Object.Destroy(s); _shields.Remove(key); }
            }

            _select.SetActive(selected.HasValue);
            if (selected.HasValue)
            {
                var (x, y) = selected.Value;
                float size = selectedIsCity ? 2 * S : S;
                _select.transform.localPosition = (selectedIsCity ? CityPos(x, y) : TilePos(x, y)) + Vector3.up * 0.004f;
                _select.transform.localScale = new Vector3(size * 1.15f, 0.006f, size * 1.15f);
            }
        }

        /// <summary>自己的行軍：沿路徑的虛線＋目前位置的部隊（返回中為藍旗）。</summary>
        public void SetMarches(List<March> marches, long nowMs)
        {
            int dot = 0;
            for (int i = 0; i < marches.Count; i++)
            {
                var m = marches[i];
                var (seg, pm) = MarchLogic.Position(m, nowMs);
                Vector3 P(int k) { var t = SurfaceLogic.FromTile(m.Path[k]); return TilePos(t.x, t.y); }
                Vector3 here = seg >= m.Path.Count - 1 ? P(m.Path.Count - 1) : Vector3.Lerp(P(seg), P(seg + 1), pm / 1000f);

                if (!m.Stationed)
                {
                    // 虛線：從目前位置畫到終點
                    var points = new List<Vector3> { here };
                    for (int k = seg + 1; k < m.Path.Count; k++) points.Add(P(k));
                    var mat = _w.Mat(m.Returning ? new Color32(120, 190, 255, 255) : new Color32(255, 226, 140, 255));
                    for (int k = 1; k < points.Count; k++)
                    {
                        var a = points[k - 1];
                        var b = points[k];
                        float len = (b - a).magnitude;
                        if (len < 0.01f) continue;
                        for (float d = 0.15f; d < len; d += 0.35f)
                        {
                            var g = Dot(dot++);
                            g.transform.localPosition = Vector3.Lerp(a, b, d / len) + Vector3.up * 0.01f;
                            g.transform.localRotation = Quaternion.LookRotation(b - a);
                            g.GetComponent<Renderer>().sharedMaterial = mat;
                        }
                    }
                }

                var troop = Troop(i);
                troop.gameObject.SetActive(true);
                troop.localPosition = here;
                troop.Find("Flag").GetComponent<Renderer>().sharedMaterial =
                    _w.Mat(m.Returning ? new Color32(70, 130, 220, 255) : new Color32(220, 170, 70, 255));
            }
            for (int k = dot; k < _dots.Count; k++) _dots[k].SetActive(false);
            for (int k = marches.Count; k < _troops.Count; k++) _troops[k].gameObject.SetActive(false);
        }

        private GameObject Dot(int i)
        {
            while (_dots.Count <= i) _dots.Add(_w.Prim(PrimitiveType.Cube, Color.white, Vector3.zero, new Vector3(0.05f, 0.01f, 0.16f), Root));
            _dots[i].SetActive(true);
            return _dots[i];
        }

        private Transform Troop(int i)
        {
            while (_troops.Count <= i)
            {
                var t = _w.Group("Troop", Root, Vector3.zero);
                t.localScale = Vector3.one * 1.6f;
                for (int k = 0; k < 3; k++)
                    _w.Prim(PrimitiveType.Cube, new Color32(70, 70, 80, 255), new Vector3((k - 1) * 0.09f, 0.06f, (k % 2) * 0.07f), new Vector3(0.05f, 0.12f, 0.05f), t);
                _w.Prim(PrimitiveType.Cylinder, new Color32(80, 70, 60, 255), new Vector3(0, 0.2f, -0.06f), new Vector3(0.012f, 0.2f, 0.012f), t);
                _w.Prim(PrimitiveType.Cube, Color.white, new Vector3(0.06f, 0.34f, -0.06f), new Vector3(0.12f, 0.08f, 0.006f), t).name = "Flag";
                _troops.Add(t);
            }
            return _troops[i];
        }

        /// <summary>只顯示鏡頭附近的區塊（每 0.3 秒更新一次）。</summary>
        public void Cull(Vector3 focus, float viewDist)
        {
            if (Time.unscaledTime < _nextCull) return;
            _nextCull = Time.unscaledTime + 0.3f;
            float radius = viewDist * 1.4f + ChunkTiles * S;
            for (int cy = 0; cy < Chunks; cy++)
                for (int cx = 0; cx < Chunks; cx++)
                {
                    var c = TilePos(cx * ChunkTiles + ChunkTiles / 2, cy * ChunkTiles + ChunkTiles / 2);
                    float dx = c.x - focus.x, dz = c.z - focus.z;
                    _chunks[cx, cy].gameObject.SetActive(dx * dx + dz * dz < radius * radius);
                }
        }

        public void Click(Vector2 mouse)
        {
            if (!_w.Ground(mouse, out var hit)) return;
            var (x, y) = TileAt(hit);
            TileClicked?.Invoke(x, y);
        }

        private static int Hash(int i, int salt)
        {
            unchecked
            {
                uint h = (uint)(i * 73856093) ^ (uint)(salt * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }
    }
}
