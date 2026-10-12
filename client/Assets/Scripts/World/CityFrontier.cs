using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 城內的邊界（GDD 5.9「城內區域擴張」）：核心區外是同一塊高地，北側是往下的深淵懸崖，
    /// 其他方向被深淵升起的霧蓋住，霧下隱約看得到遺跡、岩塊與樹。擴張區開放規則在共用的 CityZones。
    /// </summary>
    public sealed class CityFrontier : MonoBehaviour
    {
        /// <summary>高地的範圍（世界座標）：左右 ±HalfW，南緣 -South，北緣（懸崖）+North。</summary>
        public const float HalfW = 16f, South = 16f, North = 6.8f;

        private struct Puff
        {
            public Transform T;
            public Vector3 Home;
            public float Phase, Speed, Amp;
        }

        private readonly List<Puff> _puffs = new List<Puff>();

        public static CityFrontier Build(WorldScene w, Transform root)
        {
            var t = w.Group("Frontier", root, Vector3.zero);
            var f = t.gameObject.AddComponent<CityFrontier>();
            f.Create(w, t);
            return f;
        }

        private static float Hash01(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        private static float Half => CityLayout.GridSize * CityView.CellSize / 2f;

        private void Create(WorldScene w, Transform t)
        {
            float core = CityLayout.GridSize * CityView.CellSize;

            // 高地地面（核心區外）與核心區的鋪面
            float depth = South + North;
            var ground = w.Prim(PrimitiveType.Cube, Color.white, new Vector3(0, -0.03f, (North - South) / 2f), new Vector3(HalfW * 2, 0.02f, depth), t);
            ground.GetComponent<Renderer>().sharedMaterial = GroundTextures.Material(w.BaseMaterial, GroundTextures.Style.DryGrass, HalfW * 2 / 5f);
            var yard = w.Prim(PrimitiveType.Cube, Color.white, new Vector3(0, -0.01f, 0), new Vector3(core, 0.02f, core), t);
            yard.GetComponent<Renderer>().sharedMaterial = GroundTextures.Material(w.BaseMaterial, GroundTextures.Style.Courtyard, core / 4f);

            BuildCliff(w, t);
            BuildRuins(w, t);
            BuildFog(t);
        }

        /// <summary>北側懸崖：參差的岩緣，往下是一層層越來越暗的岩棚，最底下是深淵的霧。</summary>
        private static void BuildCliff(WorldScene w, Transform t)
        {
            // 崖緣：大小不一、半埋在地裡的碎岩，顏色深淺交錯
            for (int i = 0; i < 90; i++)
            {
                float x = -HalfW + Hash01(i, 4) * HalfW * 2;
                float s = 0.25f + Hash01(i, 1) * 0.55f;
                float z = North - 0.15f - Hash01(i, 2) * 0.6f;
                byte g = (byte)(84 + Hash01(i, 5) * 40);
                var rock = w.Prim(PrimitiveType.Sphere, new Color32(g, (byte)(g - 6), (byte)(g - 14), 255),
                    new Vector3(x, -0.02f, z), new Vector3(s * 1.3f, s * (0.35f + Hash01(i, 6) * 0.4f), s), t);
                rock.transform.localRotation = Quaternion.Euler(0, Hash01(i, 3) * 180, 0);
            }
            // 岩壁與岩棚：越深越暗
            for (int k = 0; k < 4; k++)
            {
                byte c = (byte)(96 - k * 20);
                float top = -0.05f - k * 1.6f;
                w.Prim(PrimitiveType.Cube, new Color32(c, (byte)(c - 4), (byte)(c - 10), 255),
                    new Vector3(0, top - 0.8f, North + 0.4f + k * 1.1f), new Vector3(HalfW * 2 + 2, 1.6f, 0.8f + k * 0.2f), t);
            }
        }

        /// <summary>霧下的遺跡、岩塊與樹：暗示霧後還有地方。不放在核心區內。</summary>
        private static void BuildRuins(WorldScene w, Transform t)
        {
            var stone = new Color32(150, 142, 128, 255);
            for (int i = 0; i < 70; i++)
            {
                float x = (Hash01(i, 11) * 2 - 1) * (HalfW - 1);
                float z = -South + 1 + Hash01(i, 12) * (South + North - 2.5f);
                if (Mathf.Abs(x) < Half + 0.8f && Mathf.Abs(z) < Half + 0.8f) continue; // 核心區留給建築
                if (z > North - 1.2f) continue;                                         // 懸崖邊
                float r = Hash01(i, 13);
                if (r < 0.45f)
                {
                    if (ModelLibrary.Spawn("World/Tree", t, new Vector3(x, 0, z), 0.9f, i, Hash01(i, 14) * 360) == null)
                        w.Prim(PrimitiveType.Cylinder, new Color32(70, 96, 70, 255), new Vector3(x, 0.3f, z), new Vector3(0.3f, 0.3f, 0.3f), t);
                }
                else if (r < 0.7f)
                {
                    // 斷柱
                    float h = 0.3f + Hash01(i, 15) * 0.6f;
                    w.Prim(PrimitiveType.Cylinder, stone, new Vector3(x, h / 2f, z), new Vector3(0.22f, h / 2f, 0.22f), t);
                }
                else if (r < 0.85f)
                {
                    // 傾倒的石塊
                    var b = w.Prim(PrimitiveType.Cube, stone, new Vector3(x, 0.1f, z), new Vector3(0.5f, 0.2f, 0.3f), t);
                    b.transform.localRotation = Quaternion.Euler(0, Hash01(i, 16) * 180, Hash01(i, 17) * 20);
                }
                else if (ModelLibrary.Spawn("World/Mountain", t, new Vector3(x, 0, z), 0.22f, i, Hash01(i, 18) * 360) == null)
                    w.Prim(PrimitiveType.Sphere, new Color32(118, 108, 96, 255), new Vector3(x, 0.2f, z), new Vector3(0.8f, 0.5f, 0.8f), t);
            }
        }

        // ───── 霧 ─────

        private static Sprite _cloud;

        /// <summary>一團柔邊的雲（中心濃、邊緣透明，帶一點雜訊才不會像圓盤）。</summary>
        private static Sprite Cloud()
        {
            if (_cloud != null) return _cloud;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + .5f) / S * 2 - 1, v = (y + .5f) / S * 2 - 1;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float n = Mathf.PerlinNoise(x * .05f, y * .05f) * .6f + Mathf.PerlinNoise(x * .12f + 7, y * .12f + 3) * .4f;
                    float a = Mathf.Clamp01(1 - d);
                    a = a * a * (3 - 2 * a) * Mathf.Lerp(.55f, 1.15f, n);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _cloud = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), S);
            return _cloud;
        }

        private void BuildFog(Transform t)
        {
            var land = new Color(.84f, .87f, .91f, .9f);   // 高地上的霧：淡藍灰
            var abyss = new Color(.42f, .47f, .56f, .7f);  // 懸崖下升起的霧：暗一點
            int i = 0;
            const float step = 1.25f;
            for (float z = -South - 2; z <= North + 6; z += step)
                for (float x = -HalfW - 2; x <= HalfW + 2; x += step)
                {
                    i++;
                    float jx = x + (Hash01(i, 21) - .5f) * step, jz = z + (Hash01(i, 22) - .5f) * step;
                    bool overAbyss = jz > North;
                    // 核心區保持清楚：雲的中心離核心區邊緣至少 1.4，邊緣會柔和地滲進來一點
                    if (!overAbyss && Mathf.Abs(jx) < Half + 1.4f && Mathf.Abs(jz) < Half + 1.4f) continue;
                    float size = 3.2f + Hash01(i, 23) * 2.2f;
                    float y = overAbyss ? -1.2f - Hash01(i, 24) * 2.5f : .35f + Hash01(i, 24) * .9f;
                    AddPuff(t, new Vector3(jx, y, jz), size, overAbyss ? abyss : land, i);
                    // 高地上再疊一層較高、較淡的，讓霧有厚度
                    if (!overAbyss && Hash01(i, 25) < .8f)
                        AddPuff(t, new Vector3(jx + .6f, y + .7f, jz - .4f), size * .8f, new Color(land.r, land.g, land.b, .55f), i + 5000);
                }
        }

        private void AddPuff(Transform parent, Vector3 pos, float size, Color color, int seed)
        {
            var go = new GameObject("Fog") { layer = WorldScene.Layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(90, Hash01(seed, 31) * 360, 0); // 平躺
            go.transform.localScale = Vector3.one * size;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Cloud();
            sr.color = color;
            _puffs.Add(new Puff { T = go.transform, Home = pos, Phase = Hash01(seed, 32) * 6.28f, Speed = .12f + Hash01(seed, 33) * .12f, Amp = .25f + Hash01(seed, 34) * .35f });
        }

        private void Update()
        {
            // 霧慢慢飄：每團在原位附近畫小圈，速度與幅度各不相同
            float time = Time.time;
            foreach (var p in _puffs)
            {
                float a = p.Phase + time * p.Speed;
                p.T.localPosition = p.Home + new Vector3(Mathf.Cos(a) * p.Amp, Mathf.Sin(a * 1.3f) * .08f, Mathf.Sin(a) * p.Amp * .6f);
            }
        }
    }
}
