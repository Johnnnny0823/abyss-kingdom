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
                float dark = Mathf.Pow(1f - (float)k / Layers, 2.4f);
                var c = Color.Lerp(new Color(.015f, .015f, .02f), new Color(.3f, .26f, .22f), dark);
                MeshObject("PitWall" + k, Wall(r, r2, -depth, -depth - h, Seg, k * 13), w.Mat(c), root);

                // 地層岩棚：每層交界一圈往內突出的薄岩
                if (k < Layers - 2)
                {
                    var ledge = Color.Lerp(c, new Color(.55f, .5f, .44f), .35f);
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
