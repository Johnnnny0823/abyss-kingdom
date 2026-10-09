using System;
using System.Collections.Generic;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.World;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;
using UnityEngine;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// 全區地圖：整張大陸的俯視圖（分區、外圍山谷、分區山脈與關隘、深淵、深淵入口、所有主城、自己的行軍）。
    /// 點圖上任一處，鏡頭就移到大地圖上的那個位置。
    /// </summary>
    public sealed class WorldOverview
    {
        private const float Px = 600f; // 畫布邊長（像素）對應整張 800 格地圖

        public readonly VisualElement Root;
        public event Action<int, int> TilePicked;
        private readonly Canvas _canvas;

        public WorldOverview()
        {
            Root = El("overview");
            var row = El("row");
            row.Add(Lbl("點地圖上任一處，鏡頭會移到那裡", "muted", "grow"));
            Root.Add(row);
            _canvas = new Canvas();
            _canvas.Picked += (x, y) => TilePicked?.Invoke(x, y);
            Root.Add(_canvas);
            var legend = El("row", "overview-legend");
            legend.Add(Lbl("● 你的主城", "ov-mine"));
            legend.Add(Lbl("● 其他領主", "ov-other"));
            legend.Add(Lbl("◆ 深淵入口", "ov-gate"));
            legend.Add(Lbl("— 行軍", "ov-march"));
            Root.Add(legend);
        }

        public void Refresh(CityState city, List<CityPin> pins, long now) => _canvas.Set(city, pins, now);

        private sealed class Canvas : VisualElement
        {
            public event Action<int, int> Picked;
            private CityState _city;
            private List<CityPin> _pins = new List<CityPin>();
            private long _now;
            private float _nextRepaint;

            public Canvas()
            {
                AddToClassList("overview-canvas");
                style.width = Px;
                style.height = Px;
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(e =>
                {
                    int x = Mathf.Clamp(Mathf.RoundToInt(e.localPosition.x / Px * SurfaceConfig.Size), 0, SurfaceConfig.Size - 1);
                    int y = Mathf.Clamp(Mathf.RoundToInt(e.localPosition.y / Px * SurfaceConfig.Size), 0, SurfaceConfig.Size - 1);
                    Picked?.Invoke(x, y);
                });
            }

            public void Set(CityState city, List<CityPin> pins, long now)
            {
                _city = city;
                _pins = pins ?? new List<CityPin>();
                _now = now;
                if (Time.unscaledTime < _nextRepaint) return;
                _nextRepaint = Time.unscaledTime + .5f;
                MarkDirtyRepaint();
            }

            private static Vector2 P(float x, float y) => new Vector2(x / SurfaceConfig.Size * Px, y / SurfaceConfig.Size * Px);
            private static float R(float tiles) => tiles / SurfaceConfig.Size * Px;
            private static Color C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

            private void Disc(Painter2D p, float r, Color c)
            {
                p.BeginPath();
                p.Arc(P(SurfaceConfig.Center, SurfaceConfig.Center), R(r), Angle.Degrees(0), Angle.Degrees(360));
                p.fillColor = c;
                p.Fill();
            }

            private void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                p.BeginPath();
                p.MoveTo(Vector2.zero); p.LineTo(new Vector2(Px, 0)); p.LineTo(new Vector2(Px, Px)); p.LineTo(new Vector2(0, Px)); p.ClosePath();
                p.fillColor = C(52, 46, 42);
                p.Fill();

                // 外圍山谷（環繞整張地圖）、三個分區、深淵
                Disc(p, SurfaceConfig.OuterRadius + 14, C(96, 88, 80));
                Disc(p, SurfaceConfig.OuterRadius, C(86, 124, 68));
                Disc(p, SurfaceConfig.MiddleOuter, C(126, 128, 80));
                Disc(p, SurfaceConfig.RimOuter, C(132, 106, 82));
                Disc(p, SurfaceConfig.PitRadius, C(14, 12, 18));
                Disc(p, SurfaceConfig.PitRadius * .6f, C(4, 4, 8));

                // 分區之間的山脈（關隘處留缺口）
                foreach (var r in SurfaceConfig.Boundaries)
                {
                    int n = 360;
                    bool open = false;
                    for (int i = 0; i <= n; i++)
                    {
                        float a = i * Mathf.PI * 2 / n;
                        int x = SurfaceConfig.Center + Mathf.RoundToInt(Mathf.Cos(a) * r);
                        int y = SurfaceConfig.Center + Mathf.RoundToInt(Mathf.Sin(a) * r);
                        bool wall = !SurfaceLogic.NearPass(x, y);
                        if (wall && !open) { p.BeginPath(); p.MoveTo(P(x, y)); open = true; }
                        else if (wall) p.LineTo(P(x, y));
                        else if (open) { p.lineWidth = 4; p.strokeColor = C(170, 158, 140); p.Stroke(); open = false; }
                    }
                    if (open) { p.lineWidth = 4; p.strokeColor = C(170, 158, 140); p.Stroke(); }
                }
                // 外圍山谷的山脊線
                p.BeginPath();
                p.Arc(P(SurfaceConfig.Center, SurfaceConfig.Center), R(SurfaceConfig.OuterRadius + 6), Angle.Degrees(0), Angle.Degrees(360));
                p.lineWidth = 6;
                p.strokeColor = C(150, 140, 128);
                p.Stroke();

                // 深淵入口
                foreach (var f in SurfaceLogic.Features())
                {
                    if (f.Type != FeatureType.AbyssGate) continue;
                    var c = P(f.X, f.Y);
                    p.BeginPath();
                    p.MoveTo(c + new Vector2(0, -6)); p.LineTo(c + new Vector2(6, 0)); p.LineTo(c + new Vector2(0, 6)); p.LineTo(c + new Vector2(-6, 0)); p.ClosePath();
                    p.fillColor = C(110, 240, 215);
                    p.Fill();
                }

                // 主城
                foreach (var pin in _pins)
                {
                    p.BeginPath();
                    p.Arc(P(pin.X + 1, pin.Y + 1), pin.Mine ? 6 : 3.2f, Angle.Degrees(0), Angle.Degrees(360));
                    p.fillColor = pin.Mine ? C(255, 214, 110) : C(220, 90, 80);
                    p.Fill();
                    if (pin.Mine)
                    {
                        p.lineWidth = 2;
                        p.strokeColor = Color.white;
                        p.Stroke();
                    }
                }

                // 自己的行軍：剩下的路線＋目前位置
                if (_city != null)
                    foreach (var m in _city.Surface.Marches)
                    {
                        var (seg, pm) = MarchLogic.Position(m, _now);
                        var here = MarchLogic.CurrentTile(m, _now);
                        p.BeginPath();
                        p.MoveTo(P(here.x, here.y));
                        for (int k = seg + 1; k < m.Path.Count; k++)
                        {
                            var t = SurfaceLogic.FromTile(m.Path[k]);
                            p.LineTo(P(t.x, t.y));
                        }
                        p.lineWidth = 2;
                        p.strokeColor = m.Returning ? C(120, 190, 255) : C(255, 226, 140);
                        p.Stroke();
                        p.BeginPath();
                        p.Arc(P(here.x, here.y), 3.5f, Angle.Degrees(0), Angle.Degrees(360));
                        p.fillColor = Color.white;
                        p.Fill();
                    }
            }
        }
    }
}
