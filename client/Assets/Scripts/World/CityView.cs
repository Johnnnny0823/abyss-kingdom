using System;
using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 城內畫面的灰盒：CityLayout 的 24×24 格子上擺建築（高度隨等級長高），可點選與移動。
    /// 移動時建築跟著滑鼠吸附格子，綠色＝可以放、紅色＝不行；左鍵確認、右鍵或 Esc 取消。
    /// </summary>
    public sealed class CityView
    {
        public const float CellSize = 0.5f;

        /// <summary>3D 模型改用霧面材質（保留貼圖）：Gamma 色彩空間下 Standard 的高光會讓整棟發灰。</summary>
        public static void MakeMatte(GameObject model)
        {
            var rock = Resources.Load<Material>("Abyss/Rock");
            if (rock == null) return;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                    mats[k] = new Material(rock) { color = Color.white, mainTexture = mats[k] != null ? mats[k].mainTexture : null };
                r.sharedMaterials = mats;
            }
        }

        public readonly Transform Root;
        public event Action<BuildingType> BuildingClicked;
        public event Action<BuildingType, int, int> MoveRequested;

        public BuildingType? Moving { get; private set; }

        private readonly WorldScene _w;
        private readonly Dictionary<BuildingType, Transform> _buildings = new Dictionary<BuildingType, Transform>();
        private readonly Dictionary<BuildingType, string> _signatures = new Dictionary<BuildingType, string>();
        private readonly Dictionary<BuildingType, LivingBuilding> _living = new Dictionary<BuildingType, LivingBuilding>();
        private readonly Dictionary<BuildingType, (Cell cell, float until)> _pending = new Dictionary<BuildingType, (Cell, float)>();
        private readonly GameObject _ghost, _gridLines, _select;
        private Material _ok, _bad;
        private CityState _state;
        private BuildingType? _selected;
        private Cell _hoverCell;

        /// <summary>點到核心區外（霧、懸崖）：參數是格子座標，可能是負的。</summary>
        public event Action<int, int> FrontierClicked;

        /// <summary>鏡頭可以移動的範圍：核心區再往外一些，看得到擴張區的霧和北側懸崖。</summary>
        public Vector2 Bounds => new Vector2(CityLayout.GridSize * CellSize / 2f + 5f, CityLayout.GridSize * CellSize / 2f + 2f);

        public CityView(WorldScene w)
        {
            _w = w;
            Root = w.Group("City", w.transform, Vector3.zero);
            float size = CityLayout.GridSize * CellSize;

            // 地面、北側深淵懸崖、外圍的霧（取代原本的方形城牆）
            CityFrontier.Build(w, Root);

            // 格線（只在移動建築時顯示）
            _gridLines = w.Group("Grid", Root, Vector3.zero).gameObject;
            var line = new Color32(120, 108, 84, 255);
            for (int i = 0; i <= CityLayout.GridSize; i++)
            {
                float p = -size / 2f + i * CellSize;
                w.Prim(PrimitiveType.Cube, line, new Vector3(p, 0.002f, 0), new Vector3(0.015f, 0.002f, size), _gridLines.transform);
                w.Prim(PrimitiveType.Cube, line, new Vector3(0, 0.002f, p), new Vector3(size, 0.002f, 0.015f), _gridLines.transform);
            }
            _gridLines.SetActive(false);

            _ghost = w.Prim(PrimitiveType.Cube, Color.green, Vector3.zero, Vector3.one, Root);
            _ghost.SetActive(false);
            _ok = w.Mat(new Color32(80, 220, 110, 255));
            _bad = w.Mat(new Color32(230, 70, 60, 255));

            _select = w.Prim(PrimitiveType.Cube, Color.white, Vector3.zero, Vector3.one, Root);
            _select.GetComponent<Renderer>().sharedMaterial = w.Glow;
            _select.SetActive(false);

            CityWalkers.Create(w, Root, () => _state);
        }

        /// <summary>格子 (x, y) 為佔地左上角時，建築中心的世界座標。</summary>
        public static Vector3 CenterOf(BuildingType type, int x, int y)
        {
            float f = CityLayout.Footprint(type);
            float half = CityLayout.GridSize / 2f;
            return new Vector3((x + f / 2f - half) * CellSize, 0, -(y + f / 2f - half) * CellSize);
        }

        public Vector3 PositionOf(BuildingType type)
        {
            var c = CellOf(type);
            return CenterOf(type, c.X, c.Y);
        }

        private Cell CellOf(BuildingType type)
        {
            if (_pending.TryGetValue(type, out var p)) return p.cell;
            return _state != null ? CityLayout.CellOf(_state, type) : CityLayout.DefaultCell(type);
        }

        // ───── 狀態 ─────

        public void Refresh(CityState state, BuildingType? selected)
        {
            _state = state;
            _selected = selected;
            foreach (var type in CityConfig.AllBuildings)
            {
                // 等伺服器確認移動：位置一致或逾時就放掉預測
                if (_pending.TryGetValue(type, out var p))
                {
                    var real = CityLayout.CellOf(state, type);
                    if ((real.X == p.cell.X && real.Y == p.cell.Y) || Time.unscaledTime > p.until) _pending.Remove(type);
                }

                int level = state.GetLevel(type);
                bool upgrading = state.Upgrade != null && state.Upgrade.Building == type;
                string sig = level + ":" + upgrading;
                if (!_signatures.TryGetValue(type, out var old) || old != sig)
                {
                    _signatures[type] = sig;
                    if (_buildings.TryGetValue(type, out var t)) UnityEngine.Object.Destroy(t.gameObject);
                    _living.Remove(type);
                    _buildings[type] = BuildBuilding(type, level, upgrading);
                }
                if (Moving != type) _buildings[type].localPosition = PositionOf(type);
            }

            foreach (var kv in _living)
                if (kv.Value != null) kv.Value.SetSelected(selected == kv.Key && Moving == null);
            // 2D 建築用自己的高亮，不再顯示選取方框
            bool selIsLiving = selected.HasValue && _living.TryGetValue(selected.Value, out var lv) && lv != null;
            _select.SetActive(selected.HasValue && Moving == null && !selIsLiving);
            if (selected.HasValue)
            {
                float f = CityLayout.Footprint(selected.Value) * CellSize;
                _select.transform.localPosition = PositionOf(selected.Value) + Vector3.up * 0.01f;
                _select.transform.localScale = new Vector3(f, 0.01f, f);
            }
        }

        private static readonly Dictionary<BuildingType, Color32> Colors = new Dictionary<BuildingType, Color32>
        {
            { BuildingType.Keep, new Color32(214, 196, 150, 255) },
            { BuildingType.Sawmill, new Color32(150, 104, 62, 255) },
            { BuildingType.Quarry, new Color32(140, 146, 160, 255) },
            { BuildingType.Farm, new Color32(120, 160, 70, 255) },
            { BuildingType.CrystalMine, new Color32(90, 140, 220, 255) },
            { BuildingType.Warehouse, new Color32(176, 150, 110, 255) },
            { BuildingType.Barracks, new Color32(170, 80, 66, 255) },
            { BuildingType.Hospital, new Color32(230, 226, 214, 255) },
        };

        private Transform BuildBuilding(BuildingType type, int level, bool upgrading)
        {
            var t = _w.Group(type.ToString(), Root, Vector3.zero);
            float f = CityLayout.Footprint(type) * CellSize;
            var color = Colors[type];
            var buildingName = AbyssKingdom.Client.UI.DisplayNames.Buildings[type];

            // 美術：依等級分三種外觀（1~3、4~6、7~10 級），未建造用空地基。
            // 順序：新版 3D 模型（Models/City3D，ArtSettings.City3D 開著時）→ 2D 建築圖 → 舊灰盒
            int tier = ArtSettings.TierOf(level);
            string artName = level > 0 ? $"{type}_{tier}" : "Site";
            if (ArtSettings.City3D)
            {
                // 新規格：底面 1×1、原點在底部中心、正面朝 -Z（tools/blender/KEEP_1_TRIAL.md），放大到建築佔地
                var m3 = ModelLibrary.Spawn("City3D/" + artName, t, Vector3.zero, f);
                if (m3 != null)
                {
                    LivingModel.Attach(m3, artName, (int)type * 37);
                    float top3 = 0;
                    foreach (var r in m3.GetComponentsInChildren<Renderer>()) top3 = Mathf.Max(top3, r.bounds.max.y - t.position.y);
                    if (upgrading)
                    {
                        var glow = _w.Prim(PrimitiveType.Cube, Color.white, new Vector3(0, top3 + .2f, 0), new Vector3(0.14f, 0.14f, 0.14f), t);
                        glow.GetComponent<Renderer>().sharedMaterial = _w.Glow;
                        glow.transform.localRotation = Quaternion.Euler(45, 45, 0);
                    }
                    _w.Nameplate(t, new Vector3(0, top3 + (upgrading ? 0.45f : 0.2f), 0), 0.034f, Color.white).text =
                        level > 0 ? $"{buildingName} Lv.{level}" : $"{buildingName}（未建造）";
                    return t;
                }
            }
            var tex = AbyssKingdom.Client.UI.ArtLibrary.Building(artName);
            if (tex != null)
            {
                var art = _w.Group("Art2D", t, new Vector3(0, 0, -f * .3f));
                var sr = art.gameObject.AddComponent<SpriteRenderer>();
                sr.sprite = AbyssKingdom.Client.UI.ArtLibrary.SpriteOfWidth(tex, f * 1.1f);
                _w.AddBillboard(art);
                float h = f * 1.1f * tex.height / tex.width;
                _living[type] = LivingBuilding.Attach(_w, t, art, sr, artName, f * 1.1f, h);
                if (upgrading)
                {
                    var glow = _w.Prim(PrimitiveType.Cube, Color.white, new Vector3(0, h + .2f, 0), new Vector3(0.14f, 0.14f, 0.14f), t);
                    glow.GetComponent<Renderer>().sharedMaterial = _w.Glow;
                    glow.transform.localRotation = Quaternion.Euler(45, 45, 0);
                }
                _w.Nameplate(t, new Vector3(0, h + (upgrading ? 0.45f : 0.2f), 0), 0.034f, Color.white).text =
                    level > 0 ? $"{buildingName} Lv.{level}" : $"{buildingName}（未建造）";
                return t;
            }
            var model = ModelLibrary.Spawn(level > 0 ? $"City/{type}_{tier}" : "City/Site", t, Vector3.zero, CellSize);
            if (model != null)
            {
                float top = 0;
                foreach (var r in model.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
                if (upgrading)
                {
                    for (int i = 0; i < 4; i++)
                        _w.Prim(PrimitiveType.Cylinder, new Color32(190, 150, 90, 255), new Vector3((i % 2 - 0.5f) * f * 0.85f, top * 0.5f, (i / 2 - 0.5f) * f * 0.85f), new Vector3(0.035f, top * 0.5f, 0.035f), t);
                    var glow = _w.Prim(PrimitiveType.Cube, Color.white, new Vector3(0, top + 0.25f, 0), new Vector3(0.14f, 0.14f, 0.14f), t);
                    glow.GetComponent<Renderer>().sharedMaterial = _w.Glow;
                    glow.transform.localRotation = Quaternion.Euler(45, 45, 0);
                }
                _w.Nameplate(t, new Vector3(0, top + (upgrading ? 0.5f : 0.25f), 0), 0.034f, Color.white).text =
                    level > 0 ? $"{buildingName} Lv.{level}" : $"{buildingName}（未建造）";
                return t;
            }
            var roof = new Color32((byte)(color.r * 0.55f), (byte)(color.g * 0.55f), (byte)(color.b * 0.6f), 255);
            _w.Prim(PrimitiveType.Cube, new Color32(120, 112, 96, 255), new Vector3(0, 0.02f, 0), new Vector3(f * 0.94f, 0.04f, f * 0.94f), t);

            if (level <= 0)
            {
                // 未建造：空地基＋四根木樁
                for (int i = 0; i < 4; i++)
                    _w.Prim(PrimitiveType.Cylinder, new Color32(140, 110, 70, 255), new Vector3((i % 2 - 0.5f) * f * 0.7f, 0.12f, (i / 2 - 0.5f) * f * 0.7f), new Vector3(0.05f, 0.12f, 0.05f), t);
            }
            else
            {
                float h = 0.25f + 0.06f * level;
                float body = f * (type == BuildingType.Keep ? 0.62f : 0.66f);
                _w.Prim(PrimitiveType.Cube, color, new Vector3(0, 0.04f + h / 2f, 0), new Vector3(body, h, body * 0.85f), t);
                var r = _w.Prim(PrimitiveType.Cube, roof, new Vector3(0, 0.04f + h, 0), new Vector3(body * 0.72f, body * 0.72f, body * 0.88f), t);
                r.transform.localRotation = Quaternion.Euler(0, 0, 45);
                r.transform.localScale = new Vector3(body * 0.6f, body * 0.6f, body * 0.88f);
                if (type == BuildingType.Keep)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var c = new Vector3((i % 2 - 0.5f) * f * 0.78f, 0, (i / 2 - 0.5f) * f * 0.78f);
                        _w.Prim(PrimitiveType.Cylinder, color, c + Vector3.up * (h * 0.6f), new Vector3(0.22f, h * 0.6f, 0.22f), t);
                        _w.Prim(PrimitiveType.Cylinder, roof, c + Vector3.up * (h * 1.2f + 0.04f), new Vector3(0.26f, 0.04f, 0.26f), t);
                    }
                }
                else Decor(type, t, f);
            }

            if (upgrading)
            {
                // 施工中：鷹架與發光標記
                for (int i = 0; i < 4; i++)
                    _w.Prim(PrimitiveType.Cylinder, new Color32(190, 150, 90, 255), new Vector3((i % 2 - 0.5f) * f * 0.85f, 0.3f, (i / 2 - 0.5f) * f * 0.85f), new Vector3(0.035f, 0.3f, 0.035f), t);
                var mark = _w.Prim(PrimitiveType.Cube, Color.white, new Vector3(0, 0.04f + 0.25f + 0.06f * level + f * 0.55f, 0), new Vector3(0.14f, 0.14f, 0.14f), t);
                mark.GetComponent<Renderer>().sharedMaterial = _w.Glow;
                mark.transform.localRotation = Quaternion.Euler(45, 45, 0);
            }

            var name = AbyssKingdom.Client.UI.DisplayNames.Buildings[type];
            _w.Nameplate(t, new Vector3(0, 0.55f + 0.06f * level + f * 0.3f, 0), 0.034f, Color.white).text =
                level > 0 ? $"{name} Lv.{level}" : $"{name}（未建造）";
            return t;
        }

        private void Decor(BuildingType type, Transform t, float f)
        {
            float e = f * 0.42f;
            switch (type)
            {
                case BuildingType.Sawmill:
                    for (int i = 0; i < 3; i++)
                    {
                        var log = _w.Prim(PrimitiveType.Cylinder, new Color32(120, 84, 50, 255), new Vector3(e, 0.06f + i * 0.08f, -e * 0.5f + i * 0.02f), new Vector3(0.08f, 0.22f, 0.08f), t);
                        log.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    }
                    break;
                case BuildingType.Quarry:
                    for (int i = 0; i < 3; i++)
                        _w.Prim(PrimitiveType.Cube, new Color32(160, 164, 176, 255), new Vector3(e - i * 0.12f, 0.08f, -e), new Vector3(0.12f, 0.12f, 0.12f), t);
                    break;
                case BuildingType.Farm:
                    for (int i = 0; i < 2; i++)
                        _w.Prim(PrimitiveType.Cube, new Color32(170, 170, 80, 255), new Vector3((i - 0.5f) * f * 0.5f, 0.05f, -e), new Vector3(f * 0.4f, 0.03f, 0.22f), t);
                    break;
                case BuildingType.CrystalMine:
                    for (int i = 0; i < 3; i++)
                    {
                        var c = _w.Prim(PrimitiveType.Cube, new Color32(120, 190, 255, 255), new Vector3(e - i * 0.1f, 0.15f, -e), new Vector3(0.06f, 0.26f, 0.06f), t);
                        c.transform.localRotation = Quaternion.Euler(0, i * 30, 12 - i * 12);
                    }
                    break;
                case BuildingType.Warehouse:
                    for (int i = 0; i < 4; i++)
                        _w.Prim(PrimitiveType.Cube, new Color32(150, 110, 70, 255), new Vector3(e - (i % 2) * 0.13f, 0.06f + (i / 2) * 0.12f, -e), new Vector3(0.11f, 0.11f, 0.11f), t);
                    break;
                case BuildingType.Hospital:
                    var seal = _w.Prim(PrimitiveType.Cube, new Color32(90, 220, 200, 255), new Vector3(0, 0.45f, -f * 0.3f), new Vector3(0.18f, 0.18f, 0.02f), t);
                    seal.transform.localRotation = Quaternion.Euler(0, 0, 45);
                    break;
                case BuildingType.Barracks:
                    _w.Prim(PrimitiveType.Cylinder, new Color32(80, 70, 60, 255), new Vector3(e, 0.4f, -e), new Vector3(0.03f, 0.4f, 0.03f), t);
                    _w.Prim(PrimitiveType.Cube, new Color32(200, 60, 50, 255), new Vector3(e + 0.1f, 0.7f, -e), new Vector3(0.2f, 0.13f, 0.01f), t);
                    break;
            }
        }

        // ───── 點選與移動 ─────

        private bool CellAt(Vector2 mouse, BuildingType type, out Cell cell)
        {
            cell = null;
            if (!_w.Ground(mouse, out var hit)) return false;
            float f = CityLayout.Footprint(type);
            float half = CityLayout.GridSize / 2f;
            // 讓建築中心對準滑鼠
            int x = Mathf.RoundToInt(hit.x / CellSize + half - f / 2f);
            int y = Mathf.RoundToInt(-hit.z / CellSize + half - f / 2f);
            cell = new Cell(x, y);
            return true;
        }

        public void BeginMove(BuildingType type)
        {
            Moving = type;
            _gridLines.SetActive(true);
            _select.SetActive(false);
        }

        public void CancelMove()
        {
            if (Moving == null) return;
            var type = Moving.Value;
            Moving = null;
            _gridLines.SetActive(false);
            _ghost.SetActive(false);
            if (_buildings.TryGetValue(type, out var t)) t.localPosition = PositionOf(type);
        }

        public void Hover(Vector2 mouse, bool overUi)
        {
            if (Moving == null || _state == null) return;
            var type = Moving.Value;
            if (overUi || !CellAt(mouse, type, out var cell)) return;
            _hoverCell = cell;
            bool ok = CityLayout.CanMove(_state, type, cell.X, cell.Y) == MoveResult.Ok;
            float f = CityLayout.Footprint(type) * CellSize;
            var pos = CenterOf(type, cell.X, cell.Y);
            _buildings[type].localPosition = pos + Vector3.up * 0.08f;
            _ghost.SetActive(true);
            _ghost.transform.localPosition = pos + Vector3.up * 0.005f;
            _ghost.transform.localScale = new Vector3(f, 0.01f, f);
            _ghost.GetComponent<Renderer>().sharedMaterial = ok ? _ok : _bad;
        }

        public void Click(Vector2 mouse)
        {
            if (Moving != null)
            {
                var type = Moving.Value;
                if (_hoverCell != null && _state != null && CityLayout.CanMove(_state, type, _hoverCell.X, _hoverCell.Y) == MoveResult.Ok)
                {
                    _pending[type] = (_hoverCell, Time.unscaledTime + 3f);
                    MoveRequested?.Invoke(type, _hoverCell.X, _hoverCell.Y);
                    CancelMove();
                }
                return;
            }

            if (!_w.Ground(mouse, out var hit)) return;
            foreach (var type in CityConfig.AllBuildings)
            {
                var c = PositionOf(type);
                float half = CityLayout.Footprint(type) * CellSize / 2f;
                if (Mathf.Abs(hit.x - c.x) <= half && Mathf.Abs(hit.z - c.z) <= half)
                {
                    BuildingClicked?.Invoke(type);
                    return;
                }
            }
            BuildingClicked?.Invoke((BuildingType)(-1)); // 點空地：取消選取
            float half2 = CityLayout.GridSize / 2f;
            int cx = Mathf.FloorToInt(hit.x / CellSize + half2), cy = Mathf.FloorToInt(-hit.z / CellSize + half2);
            if (cx < 0 || cy < 0 || cx >= CityLayout.GridSize || cy >= CityLayout.GridSize) FrontierClicked?.Invoke(cx, cy);
        }
    }
}
