using System;
using AbyssKingdom.Shared.Map;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 大地圖的裝飾物（Models/WorldProps，Codex 製作，見 tools/blender/WORLD3D.md）：依分區與密度確定性地散佈，
    /// 避開主城候選位置、資源田／營地、關隘與深淵。同一個位置每次都長一樣的東西。純畫面效果。
    /// 密度來自 tools/blender/worldprops_manifest.json（每 100 格面積、整個 family 的數量）。
    /// </summary>
    public static class WorldProps
    {
        private struct Family
        {
            public string Name;
            public float Outer, Middle, Rim; // 每 100 格的數量
            public float Scale;              // 顯示倍率（小東西放大一點，遠看才看得到）
        }

        private static readonly Family[] Families =
        {
            new Family { Name = "Pine", Outer = 3f, Middle = .35f, Scale = 1.2f },
            new Family { Name = "Broadleaf", Outer = 2.5f, Scale = 1.2f },
            new Family { Name = "DeadTree", Middle = 1.4f, Rim = .7f, Scale = 1.2f },
            new Family { Name = "AbyssTree", Rim = .45f, Scale = 1.2f },
            new Family { Name = "Bush", Outer = 3f, Middle = .7f, Scale = 1.4f },
            new Family { Name = "GrassTuft", Outer = 9f, Middle = 4f, Scale = 1.6f },
            new Family { Name = "Flowers", Outer = 2.5f, Scale = 1.6f },
            new Family { Name = "GlowMushroom", Rim = 1.2f, Scale = 1.5f },
            new Family { Name = "Rock", Outer = 1.3f, Middle = 2.2f, Rim = 3f, Scale = 1.5f },
            new Family { Name = "Boulder", Outer = .25f, Middle = .55f, Rim = .8f, Scale = 1.2f },
            new Family { Name = "RockSpire", Rim = .7f, Scale = 1.3f },
            new Family { Name = "BrokenPillar", Middle = .18f, Rim = .3f, Scale = 1.2f },
            new Family { Name = "RuinArch", Middle = .06f, Rim = .09f, Scale = 1.2f },
            new Family { Name = "OldStatue", Middle = .035f, Rim = .065f, Scale = 1.2f },
            new Family { Name = "Stump", Outer = .35f, Middle = .4f, Scale = 1.3f },
            new Family { Name = "FallenLog", Outer = .28f, Middle = .25f, Scale = 1.3f },
        };

        /// <summary>散佈的格子大小（格）。每格依密度決定放幾個。</summary>
        private const int CellTiles = 5;

        /// <summary>整體密度倍率（效能不夠時調低）。</summary>
        public static float Density = .6f;

        public static bool Available => ModelLibrary.Get("WorldProps/Pine_A") != null;

        /// <summary>在一個區塊（tile 範圍 [x0, x1) × [y0, y1)）裡放裝飾物。tileSize = 1 格的世界單位。回傳放了幾個。</summary>
        public static int Build(Transform chunk, int x0, int y0, int x1, int y1, float tileSize, Func<float, float, Vector3> tilePos)
        {
            int placed = 0;
            for (int gy = y0 / CellTiles; gy * CellTiles < y1; gy++)
                for (int gx = x0 / CellTiles; gx * CellTiles < x1; gx++)
                    for (int f = 0; f < Families.Length; f++)
                    {
                        var fam = Families[f];
                        float expected = Math.Max(fam.Outer, Math.Max(fam.Middle, fam.Rim)) * CellTiles * CellTiles / 100f * Density;
                        int n = (int)expected + (Hash(gx, gy, f * 3 + 1) % 1000 < (expected - (int)expected) * 1000 ? 1 : 0);
                        for (int k = 0; k < n; k++)
                        {
                            int salt = f * 97 + k * 13;
                            float x = gx * CellTiles + Hash(gx, gy, salt + 2) % 1000 / 1000f * CellTiles;
                            float y = gy * CellTiles + Hash(gx, gy, salt + 3) % 1000 / 1000f * CellTiles;
                            int tx = (int)x, ty = (int)y;
                            if (tx < x0 || tx >= x1 || ty < y0 || ty >= y1) continue;
                            // 依實際分區的密度再篩一次（family 在這區比較稀就少放）
                            float zoneDensity = ZoneDensity(fam, SurfaceLogic.Zone(tx, ty));
                            float peak = Math.Max(fam.Outer, Math.Max(fam.Middle, fam.Rim));
                            if (zoneDensity <= 0 || Hash(gx, gy, salt + 4) % 1000 >= zoneDensity / peak * 1000) continue;
                            if (!Clear(tx, ty)) continue;
                            string variant = fam.Name + (Hash(gx, gy, salt + 5) % 2 == 0 ? "_A" : "_B");
                            float scale = fam.Scale * (.85f + Hash(gx, gy, salt + 6) % 30 / 100f);
                            var go = ModelLibrary.Spawn("WorldProps/" + variant, chunk, tilePos(x, y), tileSize * scale, 0, Hash(gx, gy, salt + 7) % 360);
                            if (go == null) continue;
                            LivingModel.Attach(go, variant, 0);
                            placed++;
                        }
                    }
            return placed;
        }

        private static float ZoneDensity(Family f, SurfaceZone z) =>
            z == SurfaceZone.Outer ? f.Outer : z == SurfaceZone.Middle ? f.Middle : z == SurfaceZone.Rim ? f.Rim : 0;

        /// <summary>這格可以放裝飾：能走、離關隘遠、不壓到主城候選位置與資源田／營地。</summary>
        private static bool Clear(int x, int y)
        {
            if (!SurfaceLogic.Walkable(x, y) || SurfaceLogic.NearPass(x, y, 2)) return false;
            int cx = x / SurfaceConfig.Cell, cy = y / SurfaceConfig.Cell;
            // 主城候選位置（2×2）周圍留空
            var site = SurfaceLogic.SiteOf(cx, cy);
            if (site.HasValue && SurfaceLogic.Dist2(x, y, site.Value.x + 1, site.Value.y + 1) <= 3 * 3) return false;
            // 資源田、營地、深淵入口放在格點上，周圍留空
            int gx = (x + SurfaceConfig.Cell / 2) / SurfaceConfig.Cell * SurfaceConfig.Cell;
            int gy = (y + SurfaceConfig.Cell / 2) / SurfaceConfig.Cell * SurfaceConfig.Cell;
            if (SurfaceLogic.Dist2(x, y, gx, gy) <= 3 * 3 && SurfaceLogic.FeatureAt(gx, gy) != null) return false;
            return true;
        }

        private static int Hash(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }
    }
}
