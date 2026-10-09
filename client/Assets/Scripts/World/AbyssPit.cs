using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 大地圖中央的深淵「永暗之喉」：往下凹的漏斗狀坑洞。岩壁一層層往下，越深越暗，最後是看不到底的黑；
    /// 壁上有地層的突出岩棚、探險者搭的木棧與繩索、零星發光的契印。只用於畫面，和規則無關。
    /// 另外提供挖了洞的地面環（Annulus），讓各分區的地面不會蓋住深淵。
    /// </summary>
    public static class AbyssPit
    {
        /// <summary>地面環：內半徑到外半徑的平面圓環（朝上），UV 依世界座標每 10 單位重複一次。</summary>
        public static Mesh Annulus(float rIn, float rOut, int segments)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                foreach (var r in new[] { rIn, rOut })
                {
                    var v = dir * r;
                    verts.Add(v);
                    uvs.Add(new Vector2(v.x / 10f, v.z / 10f));
                }
                if (i == segments) break;
                int k = i * 2;
                tris.AddRange(new[] { k, k + 3, k + 1, k, k + 2, k + 3 }); // 朝上
            }
            var mesh = new Mesh { name = "GroundRing" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 一段朝內的坑壁（截頭圓錐，法線朝中心），邊緣用雜湊擾動成不規則的岩面；
        /// 每個四邊形各自頂點，形成低多邊形的稜角。
        /// </summary>
        private static Mesh Wall(float rTop, float rBottom, float yTop, float yBottom, int segments, int seed)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 P(int i, bool top)
            {
                float a = i * Mathf.PI * 2 / segments;
                float jitter = 1f + (Hash(i % segments, seed + (top ? 0 : 7)) % 100 - 50) / 900f;
                float r = (top ? rTop : rBottom) * jitter;
                return new Vector3(Mathf.Cos(a) * r, top ? yTop : yBottom, Mathf.Sin(a) * r);
            }
            for (int i = 0; i < segments; i++)
            {
                var a = P(i, true);
                var b = P(i + 1, true);
                var c = P(i + 1, false);
                var d = P(i, false);
                int k = verts.Count;
                verts.AddRange(new[] { a, b, c, d });
                // 從坑內看的正面（法線朝中心）
                tris.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            var mesh = new Mesh { name = "PitWall" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh _spike;

        /// <summary>四角錐尖刺（底在 y=0、尖端在 y=1），各面獨立頂點以保留稜角。</summary>
        private static Mesh Spike()
        {
            if (_spike != null) return _spike;
            var b = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f) };
            var top = new Vector3(.08f, 1f, -.05f);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int k = verts.Count;
                verts.Add(b[i]); verts.Add(top); verts.Add(b[(i + 1) % 4]);
                tris.AddRange(new[] { k, k + 1, k + 2 });
            }
            _spike = new Mesh { name = "Spike" };
            _spike.SetVertices(verts);
            _spike.SetTriangles(tris, 0);
            _spike.RecalculateNormals();
            _spike.RecalculateBounds();
            return _spike;
        }

        private static GameObject MeshObject(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name) { layer = WorldScene.Layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>建出深淵。radius = 坑口半徑（世界單位）。</summary>
        public static void Build(WorldScene w, Transform parent, float radius)
        {
            var root = w.Group("AbyssPit", parent, Vector3.zero);
            const int Seg = 40;
            const int Layers = 9;
            float depth = 0;
            float r = radius;
            for (int k = 0; k < Layers; k++)
            {
                // 越往下越窄、每層越高，顏色從土褐一路暗到純黑
                float h = 1.6f + k * .9f;
                float r2 = r * (k < 2 ? .9f : .93f);
                float dark = Mathf.Pow(1f - (float)k / Layers, 3.2f);
                var c = Color.Lerp(new Color(.008f, .006f, .012f), new Color(.07f, .06f, .065f), dark);
                MeshObject("PitWall" + k, Wall(r, r2, -depth, -depth - h, Seg, k * 13), w.Mat(c), root);

                // 地層岩棚：每層交界一圈往內突出的薄岩
                if (k < Layers - 2)
                {
                    var ledge = Color.Lerp(c, new Color(.3f, .22f, .2f), .2f);
                    MeshObject("Strata" + k, Annulus(r2 * .94f, r2 * 1.01f, Seg), w.Mat(ledge), root)
                        .transform.localPosition = new Vector3(0, -depth - h + .02f, 0);
                }
                depth += h;
                r = r2;
            }
            // 看不到底：最下面一片純黑，再往下延伸一段黑色的井
            MeshObject("Void", Wall(r, r * .9f, -depth, -depth - 30f, Seg, 99), w.Mat(new Color(0, 0, 0)), root);
            var bottom = w.Prim(PrimitiveType.Cylinder, new Color32(0, 0, 0, 255), new Vector3(0, -depth - 30f, 0), new Vector3(r * 2, .01f, r * 2), root);
            bottom.name = "Bottomless";

            // 坑口的碎石唇緣
            int lip = 56;
            for (int i = 0; i < lip; i++)
            {
                float a = i * Mathf.PI * 2 / lip;
                float s = .25f + Hash(i, 3) % 30 / 100f;
                var rock = w.Prim(PrimitiveType.Cube, new Color32(78, 72, 66, 255),
                    new Vector3(Mathf.Cos(a) * radius * 1.03f, .04f, Mathf.Sin(a) * radius * 1.03f), new Vector3(s * 1.6f, s * .35f, s), root);
                rock.transform.localRotation = Quaternion.Euler(Hash(i, 6) % 20 - 10, Hash(i, 5) % 360, Hash(i, 7) % 20 - 10);
            }

            // 探險者的木棧與繩索：沿著上半部的坑壁，往下錯落
            var wood = new Color32(122, 86, 56, 255);
            var rope = new Color32(196, 170, 120, 255);
            for (int i = 0; i < 7; i++)
            {
                float a = (i * 51 + 20) * Mathf.Deg2Rad;
                float y = -.8f - i * 1.4f;
                float rr = radius * (1f - i * .035f) - .6f;
                var pos = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
                var plank = w.Prim(PrimitiveType.Cube, wood, pos, new Vector3(1.6f, .08f, .9f), root);
                plank.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0);
                for (int s = -1; s <= 1; s += 2)
                {
                    var postPos = pos + plank.transform.right * (.7f * s);
                    w.Prim(PrimitiveType.Cylinder, wood, postPos + Vector3.down * .5f, new Vector3(.07f, .5f, .07f), root);
                }
                // 從上一層垂下來的繩子
                var ropeTop = pos + Vector3.up * 1.4f;
                w.Prim(PrimitiveType.Cylinder, rope, (pos + ropeTop) / 2, new Vector3(.03f, .7f, .03f), root);
            }

            BuildMist(w, root, radius);
            BuildDread(w, root, radius);

            // 岩壁上零星發光的契印（越深越少）
            var glowMat = new Material(w.Glow);
            glowMat.color = new Color(.4f, .95f, .85f);
            glowMat.SetColor("_EmissionColor", new Color(.25f, 1.1f, .95f));
            for (int i = 0; i < 18; i++)
            {
                float a = Hash(i, 11) % 360 * Mathf.Deg2Rad;
                float y = -1f - Hash(i, 12) % 120 / 10f;
                float rr = radius * (1f + y * .012f) - .15f;
                var g = w.Prim(PrimitiveType.Cube, Color.white, new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr), new Vector3(.18f, .18f, .04f), root);
                g.GetComponent<Renderer>().sharedMaterial = glowMat;
                g.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 45);
            }
        }

        // ───── 霧與光暈 ─────

        /// <summary>
        /// 坑裡一層層緩慢旋轉的霧（越深越濃、靠岩壁處較濃、中央較淡，仍看得到底下的黑），
        /// 加上坑口一圈淡淡的青綠光暈。材質 Mist／RimGlow 在打包時由 AbyssMaterials 產生。
        /// </summary>
        private static void BuildMist(WorldScene w, Transform root, float radius)
        {
            var mistBase = Resources.Load<Material>("Abyss/Mist");
            var glowBase = Resources.Load<Material>("Abyss/RimGlow");
            if (mistBase == null || glowBase == null) return;

            var mistTex = MistTexture(256, 5, falloffPower: 1.6f, centerClear: .1f);
            float[] depth = { .6f, 2.4f, 5f, 9f };
            float[] alpha = { .1f, .14f, .18f, .22f };
            for (int i = 0; i < depth.Length; i++)
            {
                // 薄薄一層、越深越偏藍紫越暗，不能蓋掉底下的黑
                var m = new Material(mistBase) { mainTexture = mistTex };
                var tint = Color.Lerp(new Color(.2f, .18f, .24f), new Color(.05f, .03f, .08f), i / 3f);
                m.color = new Color(tint.r, tint.g, tint.b, alpha[i]);
                m.SetColor("_EmissionColor", Color.black);
                var go = MeshObject("Mist" + i, Disc(radius * (1f - depth[i] * .022f), 48), m, root);
                go.transform.localPosition = new Vector3(0, -depth[i], 0);
                go.transform.localRotation = Quaternion.Euler(0, i * 47, 0);
                go.AddComponent<SlowSpin>().DegreesPerSecond = (i % 2 == 0 ? 1 : -1) * (1.5f + i * .6f);
            }

            // 坑口光暈：一圈往外淡出的發光環，貼在地面上方一點點
            var glowTex = RingTexture(256);
            var glow = new Material(glowBase) { mainTexture = glowTex };
            glow.color = new Color(.4f, .95f, .85f, .14f);
            glow.SetColor("_EmissionColor", new Color(.08f, .35f, .3f));
            var ring = MeshObject("RimGlow", Disc(radius * 1.18f, 64), glow, root);
            ring.transform.localPosition = new Vector3(0, .03f, 0);
        }

        /// <summary>圓盤（朝上），UV 以圓心為 (0.5, 0.5) 放射狀對應。</summary>
        private static Mesh Disc(float r, int segments)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(.5f, .5f) };
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                verts.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                uvs.Add(new Vector2(.5f + .5f * Mathf.Cos(a), .5f + .5f * Mathf.Sin(a)));
                if (i > 0) tris.AddRange(new[] { 0, i + 1, i });
            }
            var mesh = new Mesh { name = "Disc" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>霧的貼圖：一縷一縷的雜訊，靠外圈（岩壁）濃、中央淡。</summary>
        private static Texture2D MistTexture(int size, int seed, float falloffPower, float centerClear)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size - .5f, v = y / (float)size - .5f;
                    float d = Mathf.Sqrt(u * u + v * v) * 2; // 0 中央 → 1 外緣
                    float wisps = Noise(x / 24f, y / 24f, seed) * .6f + Noise(x / 9f, y / 9f, seed + 3) * .4f;
                    float edge = Mathf.Pow(Mathf.Clamp01(d), falloffPower);
                    float a = Mathf.Clamp01((edge * .8f + centerClear * .4f) * (.45f + wisps * .9f));
                    if (d > .98f) a *= Mathf.Clamp01((1.02f - d) / .04f); // 最外圈淡出，不要有硬邊
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "AbyssMist" };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>坑口光暈貼圖：在坑口半徑附近最亮，往內外淡出。</summary>
        private static Texture2D RingTexture(int size)
        {
            var px = new Color32[size * size];
            float peak = .97f / 1.18f; // 略在坑口內側，不溢到地面上
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size - .5f, v = y / (float)size - .5f;
                    float d = Mathf.Sqrt(u * u + v * v) * 2;
                    float a = Mathf.Exp(-Mathf.Pow((d - peak) / .035f, 2));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "AbyssRimGlow" };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static float Noise(float x, float y, int seed) => Mathf.PerlinNoise(x + seed * 17.3f, y + seed * 9.1f);

        // ───── 驚悚感 ─────

        private static Material Emissive(WorldScene w, Color baseColor, Color emission)
        {
            var m = new Material(w.Glow) { color = baseColor };
            m.SetColor("_EmissionColor", emission);
            return m;
        }

        /// <summary>
        /// 讓深淵在地圖上看起來可怕：坑口一圈往內彎的黑色岩牙、往外裂開並透出暗紅光的地裂、
        /// 深處緩慢脈動的暗紅光，以及偶爾在黑暗裡睜開又閉上的眼睛。
        /// </summary>
        private static void BuildDread(WorldScene w, Transform root, float radius)
        {
            // 岩牙：坑口一圈，往坑內傾斜，像一張嘴
            int teeth = 34;
            for (int i = 0; i < teeth; i++)
            {
                float a = (i + (Hash(i, 80) % 40) / 100f) * Mathf.PI * 2 / teeth;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float h = .9f + Hash(i, 81) % 10 * .12f;
                var tooth = MeshObject("Fang", Spike(), w.Mat(new Color32(34, 28, 32, 255)), root);
                tooth.transform.localPosition = dir * radius * 1.03f;
                tooth.transform.localScale = new Vector3(.38f, h * 1.3f, .38f);
                // 尖端朝上再往坑內倒 35~50 度，像一圈獠牙
                tooth.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(-(28 + Hash(i, 82) % 14), 0, 0);
            }

            // 地裂：從坑口往外放射，裂縫底部透出暗紅光
            var crackMat = w.Mat(new Color32(18, 14, 16, 255));
            var emberMat = Emissive(w, new Color(.12f, .02f, .02f), new Color(.45f, .05f, .04f));
            int cracks = 14;
            for (int i = 0; i < cracks; i++)
            {
                float a = (i + Hash(i, 90) % 50 / 100f) * Mathf.PI * 2 / cracks;
                float len = radius * (.14f + Hash(i, 91) % 18 / 100f);
                var pts = new List<Vector3> { new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius * 1.01f };
                float ang = a;
                for (int s = 0; s < 4; s++)
                {
                    ang += (Hash(i * 7 + s, 93) % 50 - 25) * Mathf.Deg2Rad; // 每段隨機拐彎
                    pts.Add(pts[pts.Count - 1] + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * len / 4f);
                }
                for (int s = 0; s + 1 < pts.Count; s++)
                {
                    float wdt = .13f * (1f - s * .22f); // 越往外越細
                    var mid = (pts[s] + pts[s + 1]) / 2f;
                    var dir = pts[s + 1] - pts[s];
                    var crack = w.Prim(PrimitiveType.Cube, Color.black, mid + Vector3.up * .006f, new Vector3(wdt, .01f, dir.magnitude * 1.05f), root);
                    crack.GetComponent<Renderer>().sharedMaterial = crackMat;
                    crack.transform.localRotation = Quaternion.LookRotation(dir);
                    var ember = w.Prim(PrimitiveType.Cube, Color.black, mid + Vector3.up * .012f, new Vector3(wdt * .3f, .01f, dir.magnitude), root);
                    ember.GetComponent<Renderer>().sharedMaterial = emberMat;
                    ember.transform.localRotation = Quaternion.LookRotation(dir);
                }
            }

            // 黑暗裡的眼睛：成對、偶爾眨眼，位置錯開
            var eyeMat = Emissive(w, new Color(1f, .25f, .15f), new Color(2.4f, .22f, .08f));
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 97 + 30) * Mathf.Deg2Rad;
                float depth = 4.5f + i * 1.8f;
                float rr = radius * (.62f - i * .07f);
                var center = new Vector3(Mathf.Cos(a) * rr, -depth, Mathf.Sin(a) * rr);
                var pair = new GameObject("Eyes" + i) { layer = WorldScene.Layer }.transform;
                pair.SetParent(root, false);
                pair.localPosition = center;
                var side = Vector3.Cross(Vector3.up, -center.normalized);
                for (int s = -1; s <= 1; s += 2)
                {
                    var eye = w.Prim(PrimitiveType.Sphere, Color.white, side * .24f * s, new Vector3(.2f, .12f, .2f), pair);
                    eye.GetComponent<Renderer>().sharedMaterial = eyeMat;
                }
                pair.gameObject.AddComponent<Blink>().Seed = i;
            }
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

    /// <summary>深處暗紅光的脈動（像心跳）。</summary>
    public sealed class DeepPulse : MonoBehaviour
    {
        public Material Mat;
        private void Update()
        {
            if (Mat == null) return;
            float t = Time.time;
            float beat = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * 1.1f)), 6) + .6f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * 1.1f - .5f)), 8);
            Mat.SetColor("_EmissionColor", new Color(.35f, .02f, .05f) * (.25f + beat * 1.1f));
        }
    }

    /// <summary>黑暗中的眼睛：大部分時間閉著，偶爾睜開一陣子、眨一下又閉上。</summary>
    public sealed class Blink : MonoBehaviour
    {
        public int Seed;
        private float _openUntil, _nextOpen;
        private Vector3 _scale;

        private void Start()
        {
            _scale = transform.localScale;
            _nextOpen = Time.time + 2f + Seed * 3.7f;
            transform.localScale = new Vector3(_scale.x, 0.001f, _scale.z);
        }

        private void Update()
        {
            float now = Time.time;
            bool open = now < _openUntil;
            if (!open && now >= _nextOpen)
            {
                _openUntil = now + Random.Range(2.5f, 5f);
                _nextOpen = _openUntil + Random.Range(6f, 14f);
            }
            // 睜開時中間眨一下
            bool blink = open && Mathf.Repeat(now, 2.3f) < .12f;
            float target = open && !blink ? 1f : 0.001f;
            float y = Mathf.MoveTowards(transform.localScale.y / Mathf.Max(.0001f, _scale.y), target, Time.deltaTime * 8f);
            transform.localScale = new Vector3(_scale.x, _scale.y * y, _scale.z);
        }
    }
}
