using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 地面貼圖：執行期用可無縫拼接的雜訊產生草地、泥土、碎石的變化（不用外部圖檔）。
    /// 每種地面一張 512×512，由 Style 決定配色與泥土、碎石的多寡。只用於畫面，和遊戲規則無關。
    /// </summary>
    public static class GroundTextures
    {
        public enum Style { LushGrass, DryGrass, RockyDirt, Wasteland, Courtyard, Meadow }

        private const int Size = 512;
        private static readonly Dictionary<Style, Texture2D> Cache = new Dictionary<Style, Texture2D>();

        private struct Palette
        {
            public Color A, B, Dirt, Gravel;
            public float DirtAmount, GravelAmount;
        }

        private static Palette Of(Style s)
        {
            switch (s)
            {
                case Style.LushGrass: return new Palette { A = C(86, 124, 68), B = C(104, 140, 76), Dirt = C(120, 102, 76), Gravel = C(140, 136, 126), DirtAmount = .16f, GravelAmount = .02f };
                case Style.DryGrass: return new Palette { A = C(126, 128, 80), B = C(142, 138, 88), Dirt = C(128, 106, 78), Gravel = C(146, 140, 128), DirtAmount = .25f, GravelAmount = .04f };
                case Style.RockyDirt: return new Palette { A = C(132, 106, 82), B = C(148, 122, 92), Dirt = C(108, 88, 70), Gravel = C(150, 146, 138), DirtAmount = .3f, GravelAmount = .08f };
                case Style.Wasteland: return new Palette { A = C(98, 92, 84), B = C(114, 106, 96), Dirt = C(84, 78, 70), Gravel = C(132, 128, 122), DirtAmount = .3f, GravelAmount = .07f };
                case Style.Courtyard: return new Palette { A = C(176, 160, 128), B = C(196, 180, 146), Dirt = C(150, 132, 102), Gravel = C(186, 184, 176), DirtAmount = .25f, GravelAmount = .12f };
                default: return new Palette { A = C(98, 140, 78), B = C(122, 160, 88), Dirt = C(128, 110, 80), Gravel = C(150, 146, 136), DirtAmount = .1f, GravelAmount = .02f };
            }
        }

        private static Color C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

        public static Texture2D Get(Style s)
        {
            if (Cache.TryGetValue(s, out var tex)) return tex;
            var p = Of(s);
            int seed = (int)s * 101 + 7;
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / Size, v = (float)y / Size;
                    // 草色的大小斑塊
                    float n = Fbm(u, v, seed, 4, 3);
                    var c = Color.Lerp(p.A, p.B, n);
                    // 泥土塊：低頻雜訊超過門檻的地方
                    float d = Fbm(u, v, seed + 17, 3, 2);
                    float dirt = Mathf.SmoothStep(0, 1, (d - (1 - p.DirtAmount)) / .12f);
                    c = Color.Lerp(c, Color.Lerp(p.Dirt, p.B, n * .3f), dirt);
                    // 碎石：高頻小點
                    float g = Fbm(u, v, seed + 33, 192, 1);
                    if (g > 1 - p.GravelAmount) c = Color.Lerp(c, p.Gravel, .75f);
                    // 細碎的明暗顆粒，讓近看不會一片平
                    float grain = Fbm(u, v, seed + 51, 128, 1);
                    c *= .93f + grain * .12f;
                    c.a = 1;
                    pixels[y * Size + x] = c;
                }
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                name = "Ground_" + s,
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            Cache[s] = tex;
            return tex;
        }

        /// <summary>貼了地面貼圖的材質。repeat = 貼圖在物件上重複幾次。</summary>
        public static Material Material(Material baseMat, Style s, float repeat)
        {
            var m = new Material(baseMat) { color = Color.white, mainTexture = Get(s) };
            m.mainTextureScale = new Vector2(repeat, repeat);
            m.SetFloat("_Glossiness", 0.05f);
            return m;
        }

        // ───── 可無縫拼接的數值雜訊（格點在邊界處取模） ─────

        private static float Fbm(float u, float v, int seed, int baseCells, int octaves)
        {
            float sum = 0, amp = 1, norm = 0;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u * cells, v * cells, cells, seed + o * 13) * amp;
                norm += amp;
                amp *= .5f;
                cells *= 2;
            }
            return sum / norm;
        }

        private static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
            float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xffff) / 65535f;
            }
        }
    }
}
