using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.Battle;
using AbyssKingdom.Shared.Map;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 深淵的 3D 剖面灰盒（美術方向原型）：從側面切開的岩壁，垂直隧道、洞室、升降井、裂隙與力場，
    /// 探索過的洞室掛燈，其餘是黑暗。全部用基本幾何體＋燈光組成，正式美術之後換成 Blender 模組零件。
    /// 地圖資料與規則完全沿用 shared 的 AbyssMap，只負責「呈現」。
    /// </summary>
    public sealed class AbyssWorld : MonoBehaviour
    {
        private const float Scale = 0.01f;   // 地圖座標（像素）→ 世界單位
        private const int Layer = 31;        // 不受場景原本平行光影響

        public static AbyssWorld Instance { get; private set; }
        public event Action<int> NodeClicked;

        /// <summary>點選判定區域（螢幕座標）。由介面提供：滑鼠在這個區域裡才處理點擊與拖曳。</summary>
        public Func<Vector2, bool> IsPointerOverViewport = _ => true;

        private AbyssMap _map;
        private Camera _cam;
        private Transform _root;
        private readonly Dictionary<int, NodeView> _nodes = new Dictionary<int, NodeView>();
        private readonly Dictionary<int, EdgeView> _edges = new Dictionary<int, EdgeView>();
        private readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        private GameObject _selectRing;
        private Font _font;
        private Vector3 _dragStart, _camStart;
        private bool _dragging, _pressed;
        private float _minY, _maxY, _minX, _maxX;
        private bool _centered;

        private sealed class NodeView
        {
            public GameObject Root, Body, Label, Lamp, Extra;
            public Renderer BodyRenderer;
            public Vector3 Pos;
        }

        private sealed class EdgeView
        {
            public GameObject Tube, Field;
            public Renderer TubeRenderer;
        }

        public static AbyssWorld Create()
        {
            var go = new GameObject("AbyssWorld");
            DontDestroyOnLoad(go);
            var w = go.AddComponent<AbyssWorld>();
            w.Build(MapConfig.Layer(1));
            w.SetActive(false);
            return w;
        }

        public void SetActive(bool on)
        {
            _root.gameObject.SetActive(on);
            _cam.enabled = on;
            // 場景原本的平行光不要照到深淵
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.cullingMask &= ~(1 << Layer);
            if (on)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.035f, 0.04f, 0.065f);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = Color.black;
                RenderSettings.fogStartDistance = 15f;
                RenderSettings.fogEndDistance = 32f;
            }
        }

        private Material M(string name)
        {
            if (!_mats.TryGetValue(name, out var m))
            {
                m = Resources.Load<Material>("Abyss/" + name);
                _mats[name] = m;
            }
            return m;
        }

        private Vector3 WorldPos(MapNode n)
        {
            // 每欄前後錯開一點，讓剖面有立體感
            float z = ((n.Column * 7) % 3 - 1) * 0.45f;
            return new Vector3((n.X - _map.Width / 2f) * Scale, -n.Y * Scale, z);
        }

        private GameObject Prim(PrimitiveType type, string mat, Vector3 pos, Vector3 scale, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = M(mat);
            return go;
        }

        private Light Lamp(Transform parent, Vector3 pos, Color color, float range, float intensity)
        {
            var go = new GameObject("Lamp") { layer = Layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.cullingMask = 1 << Layer;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        private void Build(AbyssMap map)
        {
            Instance = this;
            _map = map;
            _root = new GameObject("World").transform;
            _root.SetParent(transform, false);
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei", "Arial" }, 64);

            // 相機：側面看岩壁（剖面），deferred 讓每個洞室都能掛燈
            var camGo = new GameObject("AbyssCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.fieldOfView = 34;
            _cam.cullingMask = 1 << Layer;
            _cam.renderingPath = RenderingPath.DeferredShading;
            _cam.depth = 10;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 60f;
            camGo.transform.position = new Vector3(0, -1.5f, -12f);

            float w = map.Width * Scale, h = map.Height * Scale;
            _minX = -w / 2f; _maxX = w / 2f; _minY = -h - 0.5f; _maxY = 1.5f;

            // 岩壁背板與零散岩塊（剖面的質感）
            Prim(PrimitiveType.Cube, "Rock", new Vector3(0, -h / 2f, 2.2f), new Vector3(w + 8, h + 6, 1f), _root);
            var rnd = new System.Random(7);
            for (int i = 0; i < 420; i++)
            {
                var p = new Vector3((float)(rnd.NextDouble() * (w + 4) - (w + 4) / 2), -(float)(rnd.NextDouble() * (h + 1)), 1.5f + (float)rnd.NextDouble() * 0.6f);
                float s = 0.25f + (float)rnd.NextDouble() * 0.9f;
                var c = Prim(PrimitiveType.Cube, "RockDark", p, new Vector3(s, s * 0.7f, s), _root);
                c.transform.localRotation = Quaternion.Euler(rnd.Next(360), rnd.Next(360), rnd.Next(360));
            }

            // 地表（裂口邊緣）：地面、天空、主城
            Prim(PrimitiveType.Cube, "Ground", new Vector3(0, 0.45f, 0.5f), new Vector3(w + 8, 0.5f, 4f), _root);
            Prim(PrimitiveType.Quad, "Sky", new Vector3(0, 3.2f, 2.6f), new Vector3(w + 12, 5f, 1f), _root);
            foreach (var g in map.Gates)
            {
                var gp = WorldPos(map.Nodes[g]);
                var city = new GameObject("City") { layer = Layer }.transform;
                city.SetParent(_root, false);
                city.localPosition = new Vector3(gp.x, 0.95f, 0.6f);
                Prim(PrimitiveType.Cube, "City", Vector3.zero, new Vector3(0.7f, 0.45f, 0.5f), city);
                var roof = Prim(PrimitiveType.Cube, "City", new Vector3(0, 0.32f, 0), new Vector3(0.5f, 0.5f, 0.5f), city);
                roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
                Lamp(city, new Vector3(0, 0.2f, -0.8f), new Color(1f, 0.75f, 0.4f), 2.5f, 1.4f);
            }

            // 廊道（隧道）
            foreach (var e in map.Edges)
            {
                var a = WorldPos(map.Nodes[e.A]);
                var b = WorldPos(map.Nodes[e.B]);
                var tube = Prim(PrimitiveType.Cylinder, "Tunnel", Vector3.zero, Vector3.one, _root);
                var view = new EdgeView { Tube = tube, TubeRenderer = tube.GetComponent<Renderer>() };
                PlaceTube(tube.transform, a, b, 0.16f);
                if (e.Field != ForceField.None)
                {
                    var mid = (a + b) / 2f;
                    if (e.Field == ForceField.Veil)
                        view.Field = Prim(PrimitiveType.Sphere, "Veil", mid, new Vector3(0.55f, 0.55f, 0.12f), _root); // 圓形力場薄膜
                    else
                    {
                        view.Field = Prim(PrimitiveType.Cube, "Seal", mid, new Vector3(0.5f, 0.08f, 0.3f), _root);
                        view.Field.transform.localRotation = Quaternion.FromToRotation(Vector3.right, Vector3.Cross(b - a, Vector3.forward).normalized);
                        Lamp(view.Field.transform, Vector3.zero, new Color(1f, 0.2f, 0.25f), 1.2f, 2f);
                    }
                }
                _edges[e.Id] = view;
            }

            // 節點（洞室）
            foreach (var n in map.Nodes)
            {
                var pos = WorldPos(n);
                var root = new GameObject("Node" + n.Id) { layer = Layer }.transform;
                root.SetParent(_root, false);
                root.localPosition = pos;
                float r = n.Type == NodeType.GuardianLair ? 0.9f : n.Type == NodeType.ShaftHub ? 0.7f : 0.52f;
                var body = Prim(PrimitiveType.Sphere, "Chamber", Vector3.zero, new Vector3(r, r * 0.8f, r), root);
                var view = new NodeView { Root = root.gameObject, Body = body, BodyRenderer = body.GetComponent<Renderer>(), Pos = pos };

                var lightColor = new Color(1f, 0.72f, 0.38f);
                if (n.Type == NodeType.Fissure) lightColor = ElementColor(n.Element);
                if (n.Type == NodeType.ShaftHub) lightColor = new Color(0.4f, 1f, 1f);
                if (n.Type == NodeType.GuardianLair) lightColor = new Color(1f, 0.2f, 0.3f);
                view.Lamp = Lamp(root, new Vector3(0, 0.15f, -0.7f), lightColor, n.Type == NodeType.GuardianLair ? 3.2f : 2.3f, 1.8f).gameObject;

                if (n.Type == NodeType.ShaftHub)
                    view.Extra = Prim(PrimitiveType.Cylinder, "Hub", new Vector3(0, 0.9f, 0), new Vector3(0.12f, 0.9f, 0.12f), root);
                else if (n.Type == NodeType.Gate)
                    view.Extra = Prim(PrimitiveType.Cylinder, "Rope", new Vector3(0, (0.95f - pos.y) / 2f, 0), new Vector3(0.05f, (0.95f - pos.y) / 2f, 0.05f), root);
                else if (n.Type == NodeType.Fissure)
                {
                    view.Extra = Prim(PrimitiveType.Cube, "Fissure_" + n.Element, new Vector3(0, 0, -0.35f), new Vector3(0.22f, 0.42f, 0.22f), root);
                    view.Extra.transform.localRotation = Quaternion.Euler(0, 45, 20);
                }

                var label = new GameObject("Label") { layer = Layer };
                label.transform.SetParent(root, false);
                label.transform.localPosition = new Vector3(0, -0.05f, -0.62f);
                var tm = label.AddComponent<TextMesh>();
                tm.font = _font;
                tm.fontSize = 64;
                tm.characterSize = 0.045f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(1f, 0.97f, 0.9f);
                label.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
                view.Label = label;
                _nodes[n.Id] = view;
            }

            _selectRing = Prim(PrimitiveType.Cylinder, "Select", Vector3.zero, new Vector3(1.3f, 0.02f, 1.3f), _root);
            _selectRing.transform.localRotation = Quaternion.Euler(90, 0, 0);
            _selectRing.SetActive(false);
        }

        private static Color ElementColor(Element e)
        {
            switch (e)
            {
                case Element.Wood: return new Color(0.4f, 1f, 0.4f);
                case Element.Fire: return new Color(1f, 0.45f, 0.2f);
                case Element.Earth: return new Color(1f, 0.8f, 0.35f);
                case Element.Metal: return new Color(0.9f, 0.92f, 1f);
                default: return new Color(0.35f, 0.65f, 1f);
            }
        }

        private static void PlaceTube(Transform t, Vector3 a, Vector3 b, float radius)
        {
            t.localPosition = (a + b) / 2f;
            t.localScale = new Vector3(radius, (b - a).magnitude / 2f, radius);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        }

        /// <summary>依探索狀態更新可見度：已探索亮燈、邊界節點是暗影、其餘隱藏在黑暗中。</summary>
        public void SetState(HashSet<int> explored, HashSet<int> visible, int selected, Func<MapNode, string> glyph)
        {
            foreach (var n in _map.Nodes)
            {
                var v = _nodes[n.Id];
                bool known = explored.Contains(n.Id), vis = visible.Contains(n.Id);
                v.Root.SetActive(vis);
                if (!vis) continue;
                v.BodyRenderer.sharedMaterial = M(!known ? "Unknown"
                    : n.Type == NodeType.MonsterNest ? "Nest"
                    : n.Type == NodeType.Gate ? "Gate"
                    : n.Type == NodeType.ShaftHub ? "Hub"
                    : n.Type == NodeType.GuardianLair ? "Lair" : "Chamber");
                v.Lamp.SetActive(known);
                if (v.Extra != null) v.Extra.SetActive(known);
                v.Label.GetComponent<TextMesh>().text = known ? glyph(n) : "?";
            }

            foreach (var e in _map.Edges)
            {
                var v = _edges[e.Id];
                bool ka = explored.Contains(e.A), kb = explored.Contains(e.B);
                bool show = ka || kb;
                v.Tube.SetActive(show);
                if (v.Field != null) v.Field.SetActive(show);
                if (!show) continue;
                var a = _nodes[e.A].Pos;
                var b = _nodes[e.B].Pos;
                bool otherVisible = visible.Contains(ka ? e.B : e.A);
                if (e.Field == ForceField.Veil && !(ka && kb) && !otherVisible)
                    b = ka ? Vector3.Lerp(a, b, 0.45f) : Vector3.Lerp(b, a, 0.45f); // 遮蔽力場：只看得到一半
                if (!ka) { var tmp = a; a = b; b = tmp; }
                PlaceTube(v.Tube.transform, a, b, ka && kb ? 0.16f : 0.11f);
                v.TubeRenderer.sharedMaterial = M(ka && kb ? "Tunnel" : "TunnelDim");
            }

            _selectRing.SetActive(selected >= 0 && visible.Contains(selected));
            if (selected >= 0 && _nodes.TryGetValue(selected, out var sv))
                _selectRing.transform.localPosition = sv.Pos + new Vector3(0, 0, 0.3f);

            if (!_centered && explored.Count > 0)
            {
                foreach (var id in explored)
                {
                    var p = _nodes[id].Pos;
                    _cam.transform.position = new Vector3(p.x, p.y - 2.2f, -12f);
                    break;
                }
                _centered = true;
            }
        }

        private void Update()
        {
            if (!_cam.enabled) return;
            Vector2 mouse = Input.mousePosition;
            bool over = IsPointerOverViewport(mouse);

            // 滾輪：上下移動（越往下越深）；按住 Ctrl：縮放
            float wheel = Input.mouseScrollDelta.y;
            if (over && Mathf.Abs(wheel) > 0.01f)
            {
                var p = _cam.transform.position;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) p.z = Mathf.Clamp(p.z + wheel * 1.2f, -22f, -6f);
                else p.y += wheel * 1.1f;
                _cam.transform.position = Clamp(p);
            }

            // 左鍵：拖曳平移；沒拖動就算點擊（選節點）
            if (Input.GetMouseButtonDown(0) && over)
            {
                _pressed = true;
                _dragging = false;
                _dragStart = mouse;
                _camStart = _cam.transform.position;
            }
            if (_pressed && Input.GetMouseButton(0))
            {
                Vector2 delta = (Vector2)Input.mousePosition - (Vector2)_dragStart;
                if (delta.magnitude > 6) _dragging = true;
                if (_dragging)
                {
                    float unitsPerPixel = 2f * Mathf.Abs(_cam.transform.position.z) * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
                    _cam.transform.position = Clamp(_camStart - new Vector3(delta.x, delta.y, 0) * unitsPerPixel);
                }
            }
            if (_pressed && Input.GetMouseButtonUp(0))
            {
                if (!_dragging) PickAt(mouse);
                _pressed = false;
            }
        }

        private Vector3 Clamp(Vector3 p) =>
            new Vector3(Mathf.Clamp(p.x, _minX, _maxX), Mathf.Clamp(p.y, _minY, _maxY), p.z);

        /// <summary>點選：把可見節點投影到螢幕，取離滑鼠最近（30 像素內）的那個。不需要物理碰撞。</summary>
        private void PickAt(Vector2 mouse)
        {
            int best = -1;
            float bestD = 30f * 30f;
            foreach (var pair in _nodes)
            {
                if (!pair.Value.Root.activeSelf) continue;
                var sp = _cam.WorldToScreenPoint(pair.Value.Root.transform.position);
                if (sp.z < 0) continue;
                float d = ((Vector2)sp - mouse).sqrMagnitude;
                if (d < bestD) { bestD = d; best = pair.Key; }
            }
            if (best >= 0) NodeClicked?.Invoke(best);
        }
    }
}
