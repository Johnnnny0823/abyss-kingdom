using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 主畫面的 3D 場景（灰盒）：俯視斜角的鏡頭，兩個畫面切換——大地圖（SurfaceView）與城內（CityView）。
    /// 全部用基本幾何體組成，正式美術之後換成模型；這裡只負責鏡頭、輸入、材質與共用的小工具。
    /// </summary>
    public sealed class WorldScene : MonoBehaviour
    {
        public const int Layer = 30;
        private const float Pitch = 52f;

        public enum Mode { City, Surface }

        public static WorldScene Instance { get; private set; }

        /// <summary>滑鼠是否在介面上（由 GameUI 提供）：在介面上時不處理點擊、拖曳與滾輪。</summary>
        public Func<Vector2, bool> IsPointerOverUi = _ => false;

        public Mode Current { get; private set; } = Mode.City;
        public SurfaceView Surface { get; private set; }
        public CityView City { get; private set; }
        public Camera Cam => _cam;
        public Font Font { get; private set; }

        private sealed class View
        {
            public Vector3 Focus;
            public float Dist, MinDist, MaxDist, HalfW, HalfH;
        }

        private readonly View[] _views = new View[2];
        private Camera _cam;
        private Light _sun;
        private Material _base, _glow;
        private readonly Dictionary<Color32, Material> _mats = new Dictionary<Color32, Material>();
        private readonly List<Transform> _billboards = new List<Transform>();
        private bool _visible = true, _pressed, _dragging;
        private Vector2 _pressAt, _prev;

        public static WorldScene Create()
        {
            var go = new GameObject("WorldScene");
            DontDestroyOnLoad(go);
            var w = go.AddComponent<WorldScene>();
            w.Build();
            PerfProbe.AttachIfRequested(go);
            return w;
        }

        private void Build()
        {
            Instance = this;
            _base = Resources.Load<Material>("Abyss/Chamber");
            _glow = Resources.Load<Material>("Abyss/Select");
            Font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei", "Arial" }, 64);

            var camGo = new GameObject("WorldCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.09f, 0.1f, 0.09f);
            _cam.fieldOfView = 35;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = 5;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 120f;

            var sunGo = new GameObject("Sun") { layer = Layer };
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(48, -35, 0);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.color = new Color(1f, 0.95f, 0.85f);
            _sun.intensity = 1.2f;
            _sun.shadows = LightShadows.Soft;
            _sun.cullingMask = 1 << Layer;

            Surface = new SurfaceView(this);
            City = new CityView(this);
            var s = Surface.Bounds;
            _views[(int)Mode.Surface] = new View { Dist = 13f, MinDist = 3f, MaxDist = 30f, HalfW = s.x, HalfH = s.y };
            var c = City.Bounds;
            _views[(int)Mode.City] = new View { Dist = 17f, MinDist = 5f, MaxDist = 24f, HalfW = c.x, HalfH = c.y };
            SetMode(Mode.City);
        }

        // ───── 對外 ─────

        public void SetVisible(bool on)
        {
            if (on == _visible) return;
            _visible = on;
            _cam.enabled = on;
            Surface.Root.gameObject.SetActive(on && Current == Mode.Surface);
            City.Root.gameObject.SetActive(on && Current == Mode.City);
            if (on) ApplyLighting();
        }

        public void SetMode(Mode mode)
        {
            Current = mode;
            Surface.Root.gameObject.SetActive(_visible && mode == Mode.Surface);
            City.Root.gameObject.SetActive(_visible && mode == Mode.City);
            if (mode != Mode.City) City.CancelMove();
            ApplyLighting();
            ApplyCamera();
        }

        /// <summary>運鏡中（新手開場）：鏡頭照指定的位置與朝向，玩家的拖曳、縮放、點擊暫停。</summary>
        public bool Cinematic { get; private set; }
        private Vector3 _cinePos, _cineFocus;
        private Quaternion _cineRot;

        /// <summary>設定運鏡鏡頭。cullFocus = 大地圖要以哪裡為中心建造與顯示區塊。</summary>
        public void SetCinematic(Vector3 pos, Quaternion rot, Vector3 cullFocus)
        {
            Cinematic = true;
            _cinePos = pos;
            _cineRot = rot;
            _cineFocus = cullFocus;
        }

        public void EndCinematic()
        {
            Cinematic = false;
            ApplyCamera();
        }

        public void FocusOn(Mode mode, Vector3 p)
        {
            var v = _views[(int)mode];
            v.Focus = new Vector3(p.x, 0, p.z);
            ClampFocus(v);
            if (mode == Current) ApplyCamera();
        }

        /// <summary>滑鼠指到地面（y=0 平面）的位置。</summary>
        public bool Ground(Vector2 screen, out Vector3 hit)
        {
            var ray = _cam.ScreenPointToRay(screen);
            hit = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        // ───── 建構小工具（SurfaceView／CityView 共用） ─────

        public Material Mat(Color32 c)
        {
            if (!_mats.TryGetValue(c, out var m))
            {
                m = new Material(_base) { color = c };
                _mats[c] = m;
            }
            return m;
        }

        public Material Glow => _glow;

        /// <summary>灰盒與地面共用的基底材質（Standard）。</summary>
        public Material BaseMaterial => _base;

        public GameObject Prim(PrimitiveType type, Color32 color, Vector3 pos, Vector3 scale, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return go;
        }

        public Transform Group(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name) { layer = Layer }.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        /// <summary>永遠面向鏡頭的文字。</summary>
        public TextMesh Text(Transform parent, Vector3 pos, float size, Color color)
        {
            var go = new GameObject("Text") { layer = Layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = Font.material;
            _billboards.Add(go.transform);
            return tm;
        }

        private Material _nameplateMaterial;
        public TextMesh Nameplate(Transform parent, Vector3 pos, float size, Color color)
        {
            var text = Text(parent, pos, size, color);
            if (_nameplateMaterial == null)
                _nameplateMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.045f, 0.065f, 0.085f) };
            var plate = Prim(PrimitiveType.Quad, Color.white, Vector3.zero, Vector3.one, text.transform);
            plate.name = "NameplateBackdrop";
            var renderer = plate.GetComponent<Renderer>();
            renderer.sharedMaterial = _nameplateMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            text.gameObject.AddComponent<NameplateBackdrop>().Initialize(text.GetComponent<Renderer>(), plate.transform);
            return text;
        }

        /// <summary>讓物件永遠面向鏡頭（例如 2D 部隊圖）。</summary>
        public void AddBillboard(Transform t) => _billboards.Add(t);

        // ───── 鏡頭與輸入 ─────

        private void ApplyLighting()
        {
            // 場景原本的平行光不要照到這裡
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l != _sun && l.type == LightType.Directional) l.cullingMask &= ~(1 << Layer);
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.56f, 0.57f, 0.6f);
        }

        private void ClampFocus(View v)
        {
            v.Focus.x = Mathf.Clamp(v.Focus.x, -v.HalfW, v.HalfW);
            v.Focus.z = Mathf.Clamp(v.Focus.z, -v.HalfH, v.HalfH);
        }

        private void ApplyCamera()
        {
            var v = _views[(int)Current];
            var rot = Quaternion.Euler(Pitch, 0, 0);
            _cam.transform.rotation = rot;
            _cam.transform.position = v.Focus - rot * Vector3.forward * v.Dist;
        }

        private void Update()
        {
            if (!_visible) return;
            if (Cinematic)
            {
                _cam.transform.SetPositionAndRotation(_cinePos, _cineRot);
                if (Current == Mode.Surface) { Surface.Cull(_cineFocus, 14f); Surface.BuildStep(); }
                FaceBillboards();
                return;
            }
            var v = _views[(int)Current];
            Vector2 mouse = Input.mousePosition;
            bool overUi = IsPointerOverUi(mouse);

            float wheel = Input.mouseScrollDelta.y;
            if (!overUi && Mathf.Abs(wheel) > 0.01f)
                v.Dist = Mathf.Clamp(v.Dist * (wheel > 0 ? 0.88f : 1.14f), v.MinDist, v.MaxDist);

            // 左鍵：拖曳平移；沒拖動就算點擊
            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                _pressed = true;
                _dragging = false;
                _pressAt = _prev = mouse;
            }
            if (_pressed && Input.GetMouseButton(0))
            {
                if ((mouse - _pressAt).magnitude > 6) _dragging = true;
                if (_dragging && Ground(_prev, out var a) && Ground(mouse, out var b))
                {
                    v.Focus += a - b;
                    ClampFocus(v);
                }
                _prev = mouse;
            }
            if (_pressed && Input.GetMouseButtonUp(0))
            {
                _pressed = false;
                if (!_dragging)
                {
                    if (Current == Mode.Surface) Surface.Click(mouse);
                    else City.Click(mouse);
                }
            }
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) City.CancelMove();

            ApplyCamera();
            if (Current == Mode.City) City.Hover(mouse, overUi);
            else { Surface.Cull(v.Focus, v.Dist); Surface.BuildStep(); }

            FaceBillboards();
        }

        private void FaceBillboards()
        {
            var rot = _cam.transform.rotation;
            _billboards.RemoveAll(t => t == null);
            // 被區塊剔除（未啟用）的不用轉：大地圖上大部分名牌與部隊圖都在畫面外
            foreach (var t in _billboards)
                if (t.gameObject.activeInHierarchy) t.rotation = rot;
        }
    }

    /// <summary>Use the rendered glyph bounds, including CJK names; no material instances per label.</summary>
    internal sealed class NameplateBackdrop : MonoBehaviour
    {
        private Renderer _text;
        private Transform _plate;
        public void Initialize(Renderer text, Transform plate) { _text = text; _plate = plate; }
        private void LateUpdate()
        {
            var b = _text.localBounds;
            _plate.localPosition = new Vector3(b.center.x, b.center.y, b.max.z + 0.008f);
            _plate.localScale = new Vector3(Mathf.Max(0.01f, b.size.x + 0.06f), b.size.y + 0.04f, 1);
        }
    }
}
