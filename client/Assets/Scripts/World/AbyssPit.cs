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
        /// <summary>
        /// 坑口輪廓：角度 a（弧度）上的半徑倍率，約 0.86～1.14。幾個不同頻率的起伏疊在一起，
        /// 讓坑口像被撕開的裂口，而不是圓規畫的圓。地面的洞與坑壁都用同一條輪廓，接縫才對得上。
        /// </summary>
        public static float RimProfile(float a) =>
            1f + .07f * Mathf.Sin(3 * a + 1.3f) + .045f * Mathf.Sin(5 * a + .4f) + .028f * Mathf.Sin(9 * a + 2.1f);

        /// <summary>地面環：內半徑到外半徑的平面圓環（朝上），UV 依世界座標每 10 單位重複一次。</summary>
        public static Mesh Annulus(float rIn, float rOut, int segments) => Band(rIn, rOut, segments, 0, Mathf.PI * 2, null);

        /// <summary>同上，內緣沿著深淵的坑口輪廓（給崖緣的地面用）。</summary>
        public static Mesh AnnulusAroundPit(float rIn, float rOut, int segments) => Band(rIn, rOut, segments, 0, Mathf.PI * 2, RimProfile);

        /// <summary>從角度 a0 到 a1 的一段圓環；innerProfile 不為 null 時內緣半徑乘上它。</summary>
        private static Mesh Band(float rIn, float rOut, int segments, float a0, float a1, System.Func<float, float> innerProfile)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = a0 + i * (a1 - a0) / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                foreach (var r in new[] { innerProfile != null ? rIn * innerProfile(a) : rIn, rOut })
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
        private static Mesh Wall(float rTop, float rBottom, float yTop, float yBottom, int segments, int seed,
            float fadeTop = 0, float fadeBottom = 0)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 P(int i, bool top)
            {
                float a = i * Mathf.PI * 2 / segments;
                // 坑口輪廓越往下越淡（深處趨近圓形）；岩面雜湊只往外凸，不會在地面邊緣露出縫
                float shape = Mathf.Lerp(1f, RimProfile(a), top ? fadeTop : fadeBottom);
                float jitter = 1f + Hash(i % segments, seed + (top ? 0 : 7)) % 100 / 2800f;
                float r = (top ? rTop : rBottom) * shape * jitter;
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

        /// <summary>每一層坑壁（頂深、底深、頂半徑、底半徑、頂／底的輪廓強度），給放在壁上的東西對齊用。</summary>
        private static readonly List<(float y0, float y1, float r0, float r1, float f0, float f1)> _layers =
            new List<(float, float, float, float, float, float)>();

        /// <summary>角度 a、深度 d（正數往下）處坑壁的半徑。</summary>
        private static float WallRadius(float a, float d)
        {
            foreach (var L in _layers)
            {
                if (d > L.y1) continue;
                float t = Mathf.InverseLerp(L.y0, L.y1, d);
                return Mathf.Lerp(L.r0, L.r1, t) * Mathf.Lerp(1f, RimProfile(a), Mathf.Lerp(L.f0, L.f1, t));
            }
            var last = _layers[_layers.Count - 1];
            return last.r1;
        }

        /// <summary>把平面網格的每個頂點依它的角度乘上坑口輪廓（強度 fade），讓岩棚貼著坑壁。</summary>
        private static Mesh ProfiledCopy(Mesh m, float fade)
        {
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
                v[i] *= Mathf.Lerp(1f, RimProfile(Mathf.Atan2(v[i].z, v[i].x)), fade);
            m.vertices = v;
            m.RecalculateBounds();
            return m;
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
            const int Seg = 80;
            const int Layers = 9;
            float depth = 0;
            float r = radius;
            _layers.Clear();
            for (int k = 0; k < Layers; k++)
            {
                // 越往下越窄、每層越高，但高度與內縮量各層不同（地層有厚有薄），顏色從土褐一路暗到純黑
                float h = (1.6f + k * .9f) * (.7f + Hash(k, 40) % 60 / 100f);
                float r2 = r * ((k < 2 ? .9f : .93f) - Hash(k, 41) % 4 / 100f);
                float fadeTop = 1f - .8f * k / Layers, fadeBottom = 1f - .8f * (k + 1) / Layers;
                float dark = Mathf.Pow(1f - (float)k / Layers, 3.2f);
                var c = Color.Lerp(new Color(.008f, .006f, .012f), new Color(.09f, .078f, .082f), dark);
                MeshObject("PitWall" + k, Wall(r, r2, -depth, -depth - h, Seg, k * 13, fadeTop, fadeBottom), Matte(w, c), root);
                _layers.Add((depth, depth + h, r, r2, fadeTop, fadeBottom));

                // 地層岩棚：不是整圈，而是幾段斷斷續續、寬窄不一的突岩
                if (k < Layers - 2)
                {
                    var ledge = Color.Lerp(c, new Color(.3f, .22f, .2f), .25f);
                    int arcs = 2 + Hash(k, 42) % 3;
                    for (int j = 0; j < arcs; j++)
                    {
                        float a0 = (j + Hash(k * 5 + j, 43) % 40 / 100f) * Mathf.PI * 2 / arcs;
                        float span = (.35f + Hash(k * 5 + j, 44) % 45 / 100f) * Mathf.PI * 2 / arcs;
                        float inner = .9f + Hash(k * 5 + j, 45) % 6 / 100f;
                        var arc = Band(r2 * inner, r2 * 1.01f, 24, a0, a0 + span, null);
                        var go = MeshObject("Strata" + k + "_" + j, ProfiledCopy(arc, fadeBottom), Matte(w, ledge), root);
                        go.transform.localPosition = new Vector3(0, -depth - h + .02f, 0);
                    }
                }
                depth += h;
                r = r2;
            }
            // 看不到底：最下面一片純黑，再往下延伸一段黑色的井
            MeshObject("Void", Wall(r, r * .9f, -depth, -depth - 30f, Seg, 99), Matte(w, new Color(0, 0, 0)), root);
            var bottom = w.Prim(PrimitiveType.Cylinder, new Color32(0, 0, 0, 255), new Vector3(0, -depth - 30f, 0), new Vector3(r * 2, .01f, r * 2), root);
            bottom.name = "Bottomless";

            // 坑口的碎石唇緣：沿著輪廓，大小不一，有的地方堆得多
            int lip = 70;
            for (int i = 0; i < lip; i++)
            {
                float a = (i + Hash(i, 2) % 60 / 100f) * Mathf.PI * 2 / lip;
                float s = (.2f + Hash(i, 3) % 30 / 100f) * (Hash(i, 4) % 5 == 0 ? 1.8f : 1f);
                float rr = radius * RimProfile(a) * (1.02f + Hash(i, 8) % 5 / 100f);
                var rock = w.Prim(PrimitiveType.Cube, new Color32(40, 36, 34, 255),
                    new Vector3(Mathf.Cos(a) * rr, .04f, Mathf.Sin(a) * rr), new Vector3(s * 1.6f, s * .35f, s), root);
                rock.transform.localRotation = Quaternion.Euler(Hash(i, 6) % 20 - 10, Hash(i, 5) % 360, Hash(i, 7) % 20 - 10);
                rock.GetComponent<Renderer>().sharedMaterial = Matte(w, Hash(i, 9) % 3 == 0 ? new Color32(70, 62, 58, 255) : new Color32(48, 42, 40, 255));
            }

            // 探險者的木棧與繩索：沿著上半部的坑壁，往下錯落
            var wood = new Color32(122, 86, 56, 255);
            var rope = new Color32(196, 170, 120, 255);
            for (int i = 0; i < 7; i++)
            {
                float a = (i * 51 + 20) * Mathf.Deg2Rad;
                float y = -.8f - i * 1.4f;
                float rr = WallRadius(a, -y) - .6f;
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
            BuildRimModels(w, root, radius);

            // 岩壁上零星發光的契印（越深越少）
            var glowMat = new Material(w.Glow);
            glowMat.color = new Color(.4f, .95f, .85f);
            glowMat.SetColor("_EmissionColor", new Color(.25f, 1.1f, .95f));
            for (int i = 0; i < 18; i++)
            {
                float a = Hash(i, 11) % 360 * Mathf.Deg2Rad;
                float y = -1f - Hash(i, 12) % 120 / 10f;
                float rr = WallRadius(a, -y) - .15f;
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
                var go = MeshObject("Mist" + i, Disc(radius * (1f - depth[i] * .022f), 48), m, root);                go.transform.localPosition = new Vector3(0, -depth[i], 0);
                go.transform.localRotation = Quaternion.Euler(0, i * 47, 0);
                go.AddComponent<SlowSpin>().DegreesPerSecond = (i % 2 == 0 ? 1 : -1) * (1.5f + i * .6f);
            }

            // 坑口光暈：一圈往外淡出的發光環，貼在地面上方一點點
            var glowTex = RingTexture(256);
            var glow = new Material(glowBase) { mainTexture = glowTex };
            glow.color = new Color(.4f, .95f, .85f, .14f);
            glow.SetColor("_EmissionColor", new Color(.08f, .35f, .3f));
            var ring = MeshObject("RimGlow", ProfiledCopy(Disc(radius * 1.18f, 96), 1f), glow, root); // 光暈沿著坑口輪廓
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

        private static readonly Dictionary<Color32, Material> _matte = new Dictionary<Color32, Material>();

        /// <summary>
        /// 霧面岩石材質（Resources/Abyss/Rock：沒有太陽高光、沒有環境反射）。一般灰盒材質的高光在 Gamma 色彩空間下
        /// 會把整片岩面打亮約 +50 灰階，再暗的顏色也黑不下去；深淵的岩石一律用這個。
        /// </summary>
        private static Material Matte(WorldScene w, Color32 c)
        {
            if (!_matte.TryGetValue(c, out var m))
            {
                var rock = Resources.Load<Material>("Abyss/Rock");
                m = rock != null ? new Material(rock) { color = c } : w.Mat(c);
                _matte[c] = m;
            }
            return m;
        }

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
            // 岩牙：沿坑口輪廓成簇長出（有的地方密、有的地方禿），大小不一、有的斷了半截，往坑內傾斜
            var fangMat = Matte(w, new Color32(30, 24, 28, 255));
            int clusters = 13;
            for (int cI = 0; cI < clusters; cI++)
            {
                float ca = (cI + Hash(cI, 79) % 60 / 100f) * Mathf.PI * 2 / clusters;
                int n = 1 + Hash(cI, 78) % 4;
                for (int j = 0; j < n; j++)
                {
                    int id = cI * 8 + j;
                    float a = ca + (j - (n - 1) / 2f) * (.05f + Hash(id, 77) % 4 / 100f);
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    bool broken = Hash(id, 83) % 4 == 0;
                    float h = (.7f + Hash(id, 81) % 12 * .12f) * (broken ? .45f : 1f) * (j == n / 2 ? 1.25f : .85f);
                    float wdt = .26f + Hash(id, 84) % 20 / 100f;
                    var tooth = MeshObject("Fang", Spike(), fangMat, root);
                    tooth.transform.localPosition = dir * radius * RimProfile(a) * 1.025f;
                    tooth.transform.localScale = new Vector3(wdt, h * 1.3f, wdt * (.8f + Hash(id, 85) % 40 / 100f));
                    // 往坑內倒 25~45 度，再隨機扭一點，不像一排整齊的柱子
                    tooth.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(-(25 + Hash(id, 82) % 20), Hash(id, 86) % 30 - 15, Hash(id, 87) % 16 - 8);
                }
            }

            // 地裂：從坑口往外放射，主裂縫曲折、越往外越細，中途分岔出更細的支裂；裂縫底部透出暗紅光
            var crackMat = Matte(w, new Color32(14, 11, 13, 255));
            var emberMat = Emissive(w, new Color(.12f, .02f, .02f), new Color(.45f, .05f, .04f));
            void CrackLine(Vector3 from, float ang, float len, int segs, float width, int seed, int depthLeft)
            {
                var p = from;
                for (int s = 0; s < segs; s++)
                {
                    ang += (Hash(seed * 7 + s, 93) % 50 - 25) * Mathf.Deg2Rad; // 每段隨機拐彎
                    float segLen = len / segs * (.7f + Hash(seed * 7 + s, 94) % 60 / 100f);
                    var q = p + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * segLen;
                    float wdt = width * (1f - s * .7f / segs) * (.8f + Hash(seed * 7 + s, 95) % 40 / 100f);
                    var mid = (p + q) / 2f;
                    var dir = q - p;
                    var crack = w.Prim(PrimitiveType.Cube, Color.black, mid + Vector3.up * .006f, new Vector3(wdt, .01f, dir.magnitude * 1.08f), root);
                    crack.GetComponent<Renderer>().sharedMaterial = crackMat;
                    crack.transform.localRotation = Quaternion.LookRotation(dir);
                    var ember = w.Prim(PrimitiveType.Cube, Color.black, mid + Vector3.up * .012f, new Vector3(wdt * .3f, .01f, dir.magnitude), root);
                    ember.GetComponent<Renderer>().sharedMaterial = emberMat;
                    ember.transform.localRotation = Quaternion.LookRotation(dir);
                    // 分岔：往一側岔出一條更短更細的支裂
                    if (depthLeft > 0 && s >= 1 && Hash(seed * 7 + s, 96) % 3 == 0)
                    {
                        float side = Hash(seed * 7 + s, 97) % 2 == 0 ? 1 : -1;
                        CrackLine(q, ang + side * (35 + Hash(seed, 98) % 25) * Mathf.Deg2Rad, len * .45f, 3, wdt * .6f, seed * 31 + s, depthLeft - 1);
                    }
                    p = q;
                }
            }
            int cracks = 12;
            for (int i = 0; i < cracks; i++)
            {
                float a = (i + Hash(i, 90) % 60 / 100f) * Mathf.PI * 2 / cracks;
                float len = radius * (.12f + Hash(i, 91) % 22 / 100f);
                var start = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius * RimProfile(a) * 1.01f;
                CrackLine(start, a, len, 4 + Hash(i, 92) % 2, .1f + Hash(i, 99) % 8 / 100f, i + 1, 2);
            }

            // 黑暗裡的眼睛：只有三對，縮在岩壁凹處、半藏在岩棚下，偶爾才睜開
            var eyeMat = Emissive(w, new Color(1f, .25f, .15f), new Color(2.4f, .22f, .08f));
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 131 + 40) * Mathf.Deg2Rad;
                float depth = 5.5f + i * 2.6f;
                float rr = WallRadius(a, depth) * .9f;
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

        /// <summary>
        /// Codex 做的坑口模型（Models/Abyss）：沿坑口輪廓的破碎斷崖（往坑內伸出一點）、探險者的殘破木架與鐵鍊柱、
        /// 從坑壁長出的發光晶簇。模型單位是地圖格（1 公尺 = 1 格），沒有模型就略過。
        /// </summary>
        private static void BuildRimModels(WorldScene w, Transform root, float radius)
        {
            if (ModelLibrary.Get("Abyss/RimRock_A") == null) return;
            float tile = radius / AbyssKingdom.Shared.Map.SurfaceConfig.PitRadius; // 1 格的世界單位
            string[] rocks = { "RimRock_A", "RimRock_B", "RimRock_C" };
            int n = 22;
            for (int i = 0; i < n; i++)
            {
                float a = (i + Hash(i, 120) % 60 / 100f) * Mathf.PI * 2 / n;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float r = radius * RimProfile(a) * (.985f + Hash(i, 121) % 4 / 100f);
                string name = rocks[Hash(i, 122) % rocks.Length];
                // 模型正面朝 -Z：讓正面朝向坑中心，岩塊才會往坑內突出
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + (Hash(i, 123) % 30 - 15);
                var go = ModelLibrary.Spawn("Abyss/" + name, root, dir * r + Vector3.down * .05f, tile * (2.2f + Hash(i, 124) % 10 / 10f), 0, yaw);
                if (go != null) LivingModel.Attach(go, name, i);
            }
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 73 + 25) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                string name = i % 2 == 0 ? "RimRuin_A" : "RimRuin_B";
                var go = ModelLibrary.Spawn("Abyss/" + name, root, dir * radius * RimProfile(a) * 1.08f, tile * 2.2f, 0, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 90);
                if (go != null) LivingModel.Attach(go, name, i + 30);
            }
            for (int i = 0; i < 12; i++)
            {
                float a = (i * 137 + 11) * Mathf.Deg2Rad;
                float depth = 1.2f + Hash(i, 125) % 90 / 10f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var pos = dir * WallRadius(a, depth) * .97f + Vector3.down * depth;
                string name = i % 2 == 0 ? "AbyssCrystal_A" : "AbyssCrystal_B";
                var go = ModelLibrary.Spawn("Abyss/" + name, root, pos, tile * (2.4f + Hash(i, 126) % 12 / 10f), 0, 0);
                if (go == null) continue;
                // 從坑壁斜斜長出來：往坑中心倒 50~70 度
                go.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(-(50 + Hash(i, 127) % 20), 0, 0);
                LivingModel.Attach(go, name, i + 60);
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
