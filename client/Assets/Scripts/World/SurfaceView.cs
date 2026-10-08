using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 大地圖（地表大陸）的灰盒：中央的深淵、同心分區、道路、關隘、資源田、怪物營地，以及所有領主的主城。
    /// 地圖資料沿用 shared 的 SurfaceConfig.Map()，這裡只負責呈現與點選。
    /// </summary>
    public sealed class SurfaceView
    {
        private const float S = 0.01f; // 地圖座標（像素）→ 世界單位

        public readonly Transform Root;
        public event Action<int> NodeClicked;

        private readonly WorldScene _w;
        private readonly AbyssMap _map;
        private readonly Dictionary<int, Transform> _nodes = new Dictionary<int, Transform>();
        private readonly Dictionary<int, GameObject> _siteMarks = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, (GameObject castle, TextMesh name, bool mine)> _castles = new Dictionary<int, (GameObject, TextMesh, bool)>();
        private readonly GameObject _select;

        /// <summary>鏡頭可平移的範圍（半寬、半高）。</summary>
        public Vector2 Bounds => new Vector2(_map.Width / 2f * S, _map.Height / 2f * S);

        public SurfaceView(WorldScene w)
        {
            _w = w;
            _map = SurfaceConfig.Map();
            Root = w.Group("Surface", w.transform, Vector3.zero);
            BuildGround();
            BuildRoads();
            foreach (var n in _map.Nodes) BuildNode(n);
            _select = w.Prim(PrimitiveType.Cylinder, Color.white, Vector3.zero, new Vector3(0.5f, 0.004f, 0.5f), Root);
            _select.GetComponent<Renderer>().sharedMaterial = w.Glow;
            _select.SetActive(false);
        }

        public Vector3 NodePos(int id)
        {
            var n = _map.Nodes[id];
            return new Vector3((n.X - _map.Width / 2f) * S, 0, -(n.Y - _map.Height / 2f) * S);
        }

        // ───── 建構 ─────

        private void Disc(float radiusPx, float top, Color32 c)
        {
            float d = radiusPx * 2 * S;
            _w.Prim(PrimitiveType.Cylinder, c, new Vector3(0, top - 0.01f, 0), new Vector3(d, 0.01f, d), Root);
        }

        private void BuildGround()
        {
            float half = SurfaceConfig.RingSpacing / 2f;
            int outer = SurfaceConfig.RingRadius(SurfaceConfig.Rings - 1);
            _w.Prim(PrimitiveType.Cube, new Color32(58, 54, 46, 255), new Vector3(0, -0.02f, 0), new Vector3(_map.Width * S + 6, 0.02f, _map.Height * S + 6), Root);
            Disc(outer + 90, -0.008f, new Color32(92, 84, 66, 255));            // 山谷屏障
            Disc(outer + half, -0.006f, new Color32(96, 122, 72, 255));          // 外環（新手區）
            Disc(SurfaceConfig.RingRadius(4) + half, -0.004f, new Color32(118, 120, 76, 255)); // 中環
            Disc(SurfaceConfig.RingRadius(1) + half, -0.002f, new Color32(140, 112, 78, 255)); // 崖緣
            // 深淵：一圈圈往下變黑
            for (int i = 0; i < 6; i++)
                Disc(SurfaceConfig.PitRadius + 24 - i * 20, 0.001f * i, new Color32((byte)(40 - i * 7), (byte)(34 - i * 6), (byte)(42 - i * 7), 255));

            // 屏障上的樹林與岩塊（純裝飾，位置用整數雜湊固定）
            for (int i = 0; i < 260; i++)
            {
                float ang = Hash(i, 1) % 3600 / 10f * Mathf.Deg2Rad;
                float r = (outer + half + 8 + Hash(i, 2) % 80) * S;
                var p = new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
                if (i % 4 == 0)
                {
                    float s = 0.08f + Hash(i, 3) % 10 / 100f;
                    var rock = _w.Prim(PrimitiveType.Cube, new Color32(110, 104, 96, 255), p + Vector3.up * s / 2, new Vector3(s, s * 0.7f, s), Root);
                    rock.transform.localRotation = Quaternion.Euler(0, Hash(i, 4) % 90, 0);
                }
                else Tree(p, 0.8f + Hash(i, 3) % 5 / 10f);
            }
        }

        private void Tree(Vector3 p, float k)
        {
            _w.Prim(PrimitiveType.Cylinder, new Color32(90, 66, 44, 255), p + Vector3.up * 0.05f * k, new Vector3(0.025f * k, 0.05f * k, 0.025f * k), Root);
            _w.Prim(PrimitiveType.Sphere, new Color32(58, 98, 54, 255), p + Vector3.up * 0.15f * k, new Vector3(0.13f * k, 0.15f * k, 0.13f * k), Root);
        }

        private void BuildRoads()
        {
            foreach (var e in _map.Edges)
            {
                var a = _map.Nodes[e.A];
                var b = _map.Nodes[e.B];
                bool pass = a.Type == NodeType.Pass && b.Type == NodeType.Pass && a.Row != b.Row;
                var pa = NodePos(a.Id);
                var pb = NodePos(b.Id);
                var road = _w.Prim(PrimitiveType.Cube, pass ? new Color32(214, 186, 120, 255) : new Color32(168, 150, 116, 255),
                    (pa + pb) / 2f + Vector3.up * 0.002f, new Vector3(pass ? 0.07f : 0.045f, 0.004f, (pb - pa).magnitude), Root);
                road.transform.localRotation = Quaternion.LookRotation(pb - pa);
            }
        }

        private void BuildNode(MapNode n)
        {
            var t = _w.Group("Node" + n.Id, Root, NodePos(n.Id));
            _nodes[n.Id] = t;
            var label = new Color(1f, 0.97f, 0.9f);
            switch (n.Type)
            {
                case NodeType.CitySite:
                    _siteMarks[n.Id] = _w.Prim(PrimitiveType.Cube, new Color32(150, 146, 120, 255), new Vector3(0, 0.005f, 0), new Vector3(0.3f, 0.01f, 0.3f), t);
                    break;
                case NodeType.ResourceField:
                    BuildField(n, t);
                    _w.Text(t, new Vector3(0, 0.32f, 0), 0.013f, label).text = "Lv." + n.Level;
                    break;
                case NodeType.MonsterCamp:
                    for (int i = 0; i < 3; i++)
                    {
                        var tent = _w.Prim(PrimitiveType.Cube, new Color32(120, 46, 42, 255), new Vector3((i - 1) * 0.11f, 0.05f, (i % 2) * 0.08f), new Vector3(0.09f, 0.09f, 0.12f), t);
                        tent.transform.localRotation = Quaternion.Euler(0, 0, 45);
                    }
                    _w.Prim(PrimitiveType.Cylinder, new Color32(70, 50, 40, 255), new Vector3(0.12f, 0.12f, -0.06f), new Vector3(0.015f, 0.12f, 0.015f), t);
                    _w.Prim(PrimitiveType.Cube, new Color32(170, 40, 40, 255), new Vector3(0.15f, 0.2f, -0.06f), new Vector3(0.06f, 0.04f, 0.005f), t);
                    _w.Text(t, new Vector3(0, 0.34f, 0), 0.013f, new Color(1f, 0.7f, 0.65f)).text = "怪 Lv." + n.Level;
                    break;
                case NodeType.Pass:
                    var stone = new Color32(176, 158, 112, 255);
                    _w.Prim(PrimitiveType.Cube, stone, new Vector3(-0.1f, 0.1f, 0), new Vector3(0.05f, 0.2f, 0.05f), t);
                    _w.Prim(PrimitiveType.Cube, stone, new Vector3(0.1f, 0.1f, 0), new Vector3(0.05f, 0.2f, 0.05f), t);
                    _w.Prim(PrimitiveType.Cube, stone, new Vector3(0, 0.22f, 0), new Vector3(0.28f, 0.04f, 0.06f), t);
                    _w.Text(t, new Vector3(0, 0.36f, 0), 0.013f, new Color(1f, 0.9f, 0.6f)).text = "關隘";
                    break;
                case NodeType.Gate:
                    _w.Prim(PrimitiveType.Cylinder, new Color32(150, 130, 96, 255), new Vector3(0, 0.004f, 0), new Vector3(0.34f, 0.004f, 0.34f), t);
                    _w.Prim(PrimitiveType.Cylinder, new Color32(8, 8, 12, 255), new Vector3(0, 0.006f, 0), new Vector3(0.26f, 0.004f, 0.26f), t);
                    _w.Prim(PrimitiveType.Cylinder, new Color32(110, 86, 60, 255), new Vector3(0.15f, 0.14f, 0), new Vector3(0.02f, 0.14f, 0.02f), t);
                    _w.Prim(PrimitiveType.Cube, new Color32(110, 86, 60, 255), new Vector3(0.07f, 0.27f, 0), new Vector3(0.18f, 0.02f, 0.02f), t);
                    _w.Text(t, new Vector3(0, 0.4f, 0), 0.014f, new Color(0.75f, 0.8f, 1f)).text = "深淵入口";
                    break;
            }
        }

        private void BuildField(MapNode n, Transform t)
        {
            switch (n.Resource)
            {
                case ResourceType.Wood:
                    for (int i = 0; i < 4; i++) TreeAt(t, new Vector3((i % 2 - 0.5f) * 0.14f, 0, (i / 2 - 0.5f) * 0.14f));
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

        private void TreeAt(Transform parent, Vector3 p)
        {
            _w.Prim(PrimitiveType.Cylinder, new Color32(96, 70, 46, 255), p + Vector3.up * 0.04f, new Vector3(0.02f, 0.04f, 0.02f), parent);
            _w.Prim(PrimitiveType.Sphere, new Color32(70, 120, 60, 255), p + Vector3.up * 0.12f, new Vector3(0.1f, 0.12f, 0.1f), parent);
        }

        private GameObject Castle(Transform parent, bool mine)
        {
            var root = _w.Group("Castle", parent, Vector3.zero).gameObject;
            var t = root.transform;
            var wall = mine ? new Color32(226, 196, 130, 255) : new Color32(176, 150, 130, 255);
            var roof = mine ? new Color32(70, 110, 190, 255) : new Color32(150, 60, 50, 255);
            _w.Prim(PrimitiveType.Cube, wall, new Vector3(0, 0.05f, 0), new Vector3(0.34f, 0.1f, 0.34f), t);
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3((i % 2 - 0.5f) * 0.3f, 0, (i / 2 - 0.5f) * 0.3f);
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

        public void SetState(Dictionary<int, CityPin> pins, int mine, int selected)
        {
            foreach (var pair in _siteMarks)
            {
                pins.TryGetValue(pair.Key, out var pin);
                pair.Value.SetActive(pin == null);
                _castles.TryGetValue(pair.Key, out var c);
                bool isMine = pair.Key == mine;
                if (pin != null && (c.castle == null || c.mine != isMine))
                {
                    if (c.castle != null) UnityEngine.Object.Destroy(c.castle);
                    var castle = Castle(_nodes[pair.Key], isMine);
                    var name = _w.Text(castle.transform, new Vector3(0, 0.62f, 0), 0.02f, isMine ? new Color(1f, 0.86f, 0.5f) : Color.white);
                    c = (castle, name, isMine);
                    _castles[pair.Key] = c;
                }
                else if (pin == null && c.castle != null)
                {
                    UnityEngine.Object.Destroy(c.castle);
                    _castles.Remove(pair.Key);
                    continue;
                }
                if (pin != null) c.name.text = pin.Name;
            }
            _select.SetActive(selected >= 0);
            if (selected >= 0) _select.transform.localPosition = NodePos(selected) + Vector3.up * 0.003f;
        }

        /// <summary>點選：把地點投影到螢幕，取離滑鼠最近（36 像素內）的那個。</summary>
        public void Click(Vector2 mouse)
        {
            int best = -1;
            float bestD = 36f * 36f;
            foreach (var pair in _nodes)
            {
                var sp = _w.Cam.WorldToScreenPoint(pair.Value.position + Vector3.up * 0.1f);
                if (sp.z < 0) continue;
                float d = ((Vector2)sp - mouse).sqrMagnitude;
                if (d < bestD) { bestD = d; best = pair.Key; }
            }
            NodeClicked?.Invoke(best);
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
