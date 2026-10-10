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

        // 模型顯示倍率：讓主城、資源點、部隊在一般遊玩距離也看得清楚（只影響畫面，不影響規則上的佔地）
        private const float CityScale = 1.5f, FeatureScale = 1.45f, TreeScale = 1.25f, TroopScale = 1.7f, TowerScale = 1.2f;
        private const int ChunkTiles = 40;
        private const int Chunks = SurfaceConfig.Size / ChunkTiles;

        public readonly Transform Root;
        public event Action<int, int> TileClicked;

        private readonly WorldScene _w;
        private readonly Transform[,] _chunks = new Transform[Chunks, Chunks];
        private readonly Dictionary<int, (GameObject castle, TextMesh name, bool mine, int tier)> _castles = new Dictionary<int, (GameObject, TextMesh, bool, int)>();
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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Root = w.Group("Surface", w.transform, Vector3.zero);
            for (int cy = 0; cy < Chunks; cy++)
                for (int cx = 0; cx < Chunks; cx++)
                    _chunks[cx, cy] = w.Group($"Chunk{cx}_{cy}", Root, Vector3.zero);
            BuildGround();
            BuildWalls();
            BuildOuterValley();
            foreach (var f in SurfaceLogic.Features())
            {
                var feature = f;
                Defer(f.X, f.Y, () => BuildFeature(feature));
            }
            BuildTrees();
            _select = w.Prim(PrimitiveType.Cube, Color.white, Vector3.zero, Vector3.one, Root);
            _select.GetComponent<Renderer>().sharedMaterial = w.Glow;
            _select.SetActive(false);
            // 效能量測：建大地圖花多久、多少物件（看 Player.log）
            Debug.Log($"[SurfaceView] built {Root.GetComponentsInChildren<Transform>(true).Length} objects in {watch.ElapsedMilliseconds} ms");
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

        /// <summary>每個區塊還沒建的東西：區塊第一次進到鏡頭附近時才建（登入時不必一次建好上萬個物件）。</summary>
        private readonly List<Action>[,] _pending = new List<Action>[Chunks, Chunks];

        private void Defer(int x, int y, Action build)
        {
            int cx = Mathf.Clamp(x / ChunkTiles, 0, Chunks - 1), cy = Mathf.Clamp(y / ChunkTiles, 0, Chunks - 1);
            (_pending[cx, cy] ??= new List<Action>()).Add(build);
        }

        // ───── 建構 ─────

        private void BuildGround()
        {
            // 各分區是互不重疊的地面環，中央留出深淵的洞（之前是疊在一起的實心圓盤，深淵只是一片黑）
            Ring(SurfaceConfig.OuterRadius, 900, GroundTextures.Style.Wasteland);                 // 地圖邊緣的荒地
            Ring(SurfaceConfig.MiddleOuter, SurfaceConfig.OuterRadius, GroundTextures.Style.LushGrass); // 外環（新手區）：青草
            Ring(SurfaceConfig.RimOuter, SurfaceConfig.MiddleOuter, GroundTextures.Style.DryGrass);     // 中環：乾草
            Ring(SurfaceConfig.PitRadius, SurfaceConfig.RimOuter, GroundTextures.Style.RockyDirt, aroundPit: true); // 崖緣：碎石泥地（內緣沿坑口輪廓）
            AbyssPit.Build(_w, Root, SurfaceConfig.PitRadius * S);                                     // 永暗之喉：往下的深淵
        }

        /// <summary>貼地面貼圖的圓環（半徑以格為單位）；UV 依世界座標，貼圖每 10 個世界單位重複一次。</summary>
        private void Ring(float innerTiles, float outerTiles, GroundTextures.Style style, bool aroundPit = false)
        {
            var go = new GameObject("Ground_" + style) { layer = WorldScene.Layer };
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(0, -0.002f, 0);
            go.AddComponent<MeshFilter>().sharedMesh = aroundPit
                ? AbyssPit.AnnulusAroundPit(innerTiles * S, outerTiles * S, 160)
                : AbyssPit.Annulus(innerTiles * S, outerTiles * S, 160);
            go.AddComponent<MeshRenderer>().sharedMaterial = GroundTextures.Material(_w.BaseMaterial, style, 1f);
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
                    int seed = Hash(i, b * 7 + 4), yaw = Hash(i, b * 7 + 5) % 360;
                    Defer(x, y, () =>
                    {
                        if (ModelLibrary.Spawn("World/Mountain", ChunkOf(x, y), p, S, seed, yaw) != null) return;
                        _w.Prim(PrimitiveType.Sphere, new Color32(118, 108, 96, 255), p + Vector3.up * h * 0.35f, new Vector3(wdt, h, wdt), ChunkOf(x, y));
                        if (h > 0.9f) _w.Prim(PrimitiveType.Sphere, new Color32(220, 224, 228, 255), p + Vector3.up * h * 0.72f, new Vector3(wdt * 0.4f, h * 0.25f, wdt * 0.4f), ChunkOf(x, y));
                    });
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
                        if (ModelLibrary.Spawn("World/PassTower", t, tower, S * TowerScale, 0, Mathf.Atan2(radial.x, radial.z) * Mathf.Rad2Deg) != null) continue;
                        _w.Prim(PrimitiveType.Cylinder, stone, tower + Vector3.up * 0.35f, new Vector3(0.35f, 0.35f, 0.35f), t);
                        _w.Prim(PrimitiveType.Cylinder, new Color32(150, 60, 50, 255), tower + Vector3.up * 0.76f, new Vector3(0.42f, 0.06f, 0.42f), t);
                    }
                    _w.Text(t, center + Vector3.up * 0.9f, 0.05f, new Color(1f, 0.9f, 0.6f)).text = "關隘";
                }
            }
        }

        /// <summary>整張地圖最外圍環繞的山谷：兩排錯開的高山，沒有缺口。</summary>
        private void BuildOuterValley()
        {
            for (int row = 0; row < 2; row++)
            {
                float r = SurfaceConfig.OuterRadius + 4 + row * 7;
                int n = Mathf.RoundToInt(2 * Mathf.PI * r / 4.5f);
                for (int i = 0; i < n; i++)
                {
                    int ii = i, rw = row; // 延後建造：迴圈變數要先複製一份
                    float a = (i + row * .5f) * Mathf.PI * 2 / n;
                    float jitter = (Hash(i, 40 + row) % 5 - 2) * .8f;
                    int x = SurfaceConfig.Center + Mathf.RoundToInt(Mathf.Cos(a) * (r + jitter));
                    int y = SurfaceConfig.Center + Mathf.RoundToInt(Mathf.Sin(a) * (r + jitter));
                    var p = TilePos(x, y);
                    float k = 1.3f + row * .35f + Hash(i, 50 + row) % 4 * .12f;
                    Defer(x, y, () =>
                    {
                        if (ModelLibrary.Spawn("World/Mountain", ChunkOf(x, y), p, S * k, Hash(ii, 60 + rw), Hash(ii, 70 + rw) % 360) != null) return;
                        _w.Prim(PrimitiveType.Sphere, new Color32(110, 100, 90, 255), p + Vector3.up * .4f * k, new Vector3(1.2f * k, 1.1f * k, 1.2f * k), ChunkOf(x, y));
                    });
                }
            }
        }

        private static string ModelOf(Feature f)
        {
            switch (f.Type)
            {
                case FeatureType.ResourceField: return "World/Field_" + f.Resource;
                case FeatureType.MonsterCamp: return "World/MonsterCamp";
                default: return "World/AbyssGate";
            }
        }

        private void BuildFeature(Feature f)
        {
            // 有美術模型就用模型（文字標籤另外放），沒有就用下面的灰盒
            var pos = TilePos(f.X, f.Y);
            float fs = f.Type == FeatureType.AbyssGate ? 1f : FeatureScale;
            if (ModelLibrary.Spawn(ModelOf(f), ChunkOf(f.X, f.Y), pos, S * fs, f.X * 31 + f.Y, Hash(f.X, f.Y) % 4 * 90) != null)
            {
                var chunk = ChunkOf(f.X, f.Y);
                if (f.Type == FeatureType.ResourceField) _w.Text(chunk, pos + Vector3.up * 0.6f, 0.038f, new Color(1f, 0.97f, 0.9f)).text = "Lv." + f.Level;
                else if (f.Type == FeatureType.MonsterCamp) _w.Text(chunk, pos + Vector3.up * 0.62f, 0.038f, new Color(1f, 0.7f, 0.65f)).text = "怪 Lv." + f.Level;
                else _w.Text(chunk, pos + Vector3.up * 1.1f, 0.048f, new Color(0.8f, 0.78f, 1f)).text = "深淵入口";
                return;
            }

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
                    int gx = cx, gy = cy;
                    Defer(x, y, () =>
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            var tp = p + new Vector3((i - 1) * 0.18f, 0, (i % 2) * 0.16f);
                            if (ModelLibrary.Spawn("World/Tree", t, tp, S * TreeScale, Hash(gx * 3 + i, gy), Hash(gy, gx * 3 + i) % 360) == null)
                                TreeAt(t, tp, 2.2f + i % 2 * 0.5f);
                        }
                    });
                }
        }

        private GameObject Castle(Transform parent, Vector3 pos, bool mine, int tier)
        {
            var root = _w.Group("Castle", parent, pos).gameObject;
            var t = root.transform;
            // 新版 3D 主堡（和城內同一個模型，底面 1×1，放大到約 2×2 格佔地 × 顯示倍率）；沒有就用舊的大地圖主城模型
            GameObject keep3D = ArtSettings.City3D ? ModelLibrary.Spawn("City3D/Keep_" + tier, t, Vector3.zero, S * 2 * CityScale) : null;
            if (keep3D != null) LivingModel.Attach(keep3D, "Keep_" + tier, Hash((int)pos.x * 100, (int)pos.z * 100));
            if (keep3D != null || ModelLibrary.Spawn("World/City", t, Vector3.zero, S * CityScale) != null)
            {
                var ring = _w.Prim(PrimitiveType.Cylinder, mine ? new Color32(230, 190, 90, 255) : new Color32(190, 70, 60, 255), Vector3.up * 0.002f, new Vector3(0.88f, 0.002f, 0.88f), t);
                ring.name = "OwnerRing";
                return root;
            }
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
                int tier = ArtSettings.TierOf(pin.KeepLevel);
                if (c.castle == null || c.mine != pin.Mine || c.tier != tier)
                {
                    if (c.castle != null) UnityEngine.Object.Destroy(c.castle);
                    var castle = Castle(ChunkOf(pin.X, pin.Y), CityPos(pin.X, pin.Y), pin.Mine, tier);
                    var name = _w.Text(castle.transform, new Vector3(0, 0.95f, 0), 0.03f, pin.Mine ? new Color(1f, 0.86f, 0.5f) : Color.white);
                    c = (castle, name, pin.Mine, tier);
                    _castles[key] = c;
                }
                c.name.text = pin.Name;

                // 護盾：主城外一圈發光的圓
                _shields.TryGetValue(key, out var shield);
                if (pin.Shielded && shield == null)
                {
                    shield = _w.Prim(PrimitiveType.Cylinder, Color.white, CityPos(pin.X, pin.Y) + Vector3.up * 0.003f, new Vector3(1.15f, 0.003f, 1.15f), ChunkOf(pin.X, pin.Y));
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
                var moveDir = m.Stationed || seg >= m.Path.Count - 1 ? Vector3.zero : P(seg + 1) - P(seg);
                ApplyTroopArt(troop, m, moveDir);
                troop.Find("Flag").GetComponent<Renderer>().sharedMaterial =
                    _w.Mat(m.Returning ? new Color32(70, 130, 220, 255) : new Color32(220, 170, 70, 255));
            }
            for (int k = dot; k < _dots.Count; k++) _dots[k].SetActive(false);
            for (int k = marches.Count; k < _troops.Count; k++) _troops[k].gameObject.SetActive(false);
        }

        /// <summary>
        /// 2D 部隊：主力兵種的圖（面向鏡頭）＋頭上的主將頭像。沒有兵種圖就維持 3D 模型。
        /// 主力兵種 = 這支部隊人數最多的兵種。
        /// </summary>
        private void ApplyTroopArt(Transform troop, March m, Vector3 moveDir)
        {
            TroopType main = TroopType.Shield;
            long most = -1;
            foreach (var kv in m.Troops)
                if (kv.Value > most) { most = kv.Value; main = kv.Key; }
            var tex = AbyssKingdom.Client.UI.ArtLibrary.Troop(main);
            var art = troop.Find("Art2D");
            if (tex == null)
            {
                if (art != null) art.gameObject.SetActive(false);
                return;
            }
            if (art == null)
            {
                art = _w.Group("Art2D", troop, Vector3.zero);
                var body = _w.Group("Body", art, Vector3.zero);
                body.gameObject.AddComponent<SpriteRenderer>();
                body.gameObject.AddComponent<MarchAnimator>();
                var iconGo = _w.Group("HeroIcon", art, new Vector3(0, .9f, 0));
                iconGo.gameObject.AddComponent<SpriteRenderer>();
                _w.AddBillboard(art);
                var shadow = _w.Prim(PrimitiveType.Cylinder, new Color32(30, 34, 30, 255), new Vector3(0, .004f, 0), new Vector3(.5f, .002f, .28f), troop);
                shadow.name = "Shadow";
            }
            art.gameObject.SetActive(true);
            var bodyT = art.Find("Body");
            bodyT.GetComponent<SpriteRenderer>().sprite = AbyssKingdom.Client.UI.ArtLibrary.SpriteOf(tex, .85f);
            var anim = bodyT.GetComponent<MarchAnimator>();
            anim.Moving = moveDir.sqrMagnitude > 1e-6f;
            if (anim.Moving) anim.FacingLeft = moveDir.x < -1e-3f; // 鏡頭朝北：畫面左右 = 世界 X

            // 3D 小隊（有 Units3D 模型時）：取代 2D 兵種圖，頭上的主將頭像與腳下的顏色圈照舊
            string unitName = main.ToString(), heroName = m.Hero.ToString();
            var squadT = troop.Find($"Squad3D_{unitName}_{heroName}");
            foreach (Transform child in troop)
                if (child.name.StartsWith("Squad3D_") && child != squadT) UnityEngine.Object.Destroy(child.gameObject);
            UnitSquad squad = squadT != null ? squadT.GetComponent<UnitSquad>() : null;
            if (squad == null && ArtSettings.City3D)
                squad = UnitSquad.Create(troop, unitName, heroName, .42f / Mathf.Max(.01f, troop.localScale.x), (int)m.Id * 13 + (int)m.Hero);
            if (squad != null)
            {
                squad.Moving = anim.Moving;
                if (anim.Moving) squad.Direction = moveDir;
            }
            bodyT.GetComponent<SpriteRenderer>().enabled = squad == null;
            var heroIcon = AbyssKingdom.Client.UI.ArtLibrary.HeroIcon(m.Hero);
            var iconRenderer = art.Find("HeroIcon").GetComponent<SpriteRenderer>();
            iconRenderer.sprite = AbyssKingdom.Client.UI.ArtLibrary.SpriteOf(heroIcon, .32f);
            iconRenderer.enabled = heroIcon != null && (squad == null || !squad.HasHeroModel); // 主將有 3D 模型走在隊伍最前面，就不用頭像
            // 有 2D 圖時把 3D 小兵與旗子藏起來；改用腳下影子的顏色表示出征（暖金）或返回（藍）
            troop.Find("Shadow").GetComponent<Renderer>().sharedMaterial =
                _w.Mat(m.Returning ? new Color32(60, 100, 160, 255) : new Color32(150, 116, 52, 255));
            foreach (Transform child in troop)
                if (child != art && child.name != "Shadow" && !child.name.StartsWith("Squad3D_")) child.gameObject.SetActive(false);
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
                if (ModelLibrary.Spawn("World/Troop", t, Vector3.zero, S * TroopScale) != null)
                {
                    // 模型之外仍保留一面小旗：顏色表示出征（金）或返回（藍）
                    _w.Prim(PrimitiveType.Cylinder, new Color32(80, 70, 60, 255), new Vector3(0.12f, 0.3f, 0), new Vector3(0.012f, 0.3f, 0.012f), t);
                    _w.Prim(PrimitiveType.Cube, Color.white, new Vector3(0.2f, 0.52f, 0), new Vector3(0.18f, 0.11f, 0.008f), t).name = "Flag";
                    _troops.Add(t);
                    continue;
                }
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
                    bool on = dx * dx + dz * dz < radius * radius;
                    if (on && _pending[cx, cy] != null)
                    {
                        var list = _pending[cx, cy];
                        _pending[cx, cy] = null;
                        foreach (var build in list) build();
                    }
                    _chunks[cx, cy].gameObject.SetActive(on);
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
