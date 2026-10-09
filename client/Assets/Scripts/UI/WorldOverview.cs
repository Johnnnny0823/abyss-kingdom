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

            private static int Hash(int i, int salt)
            {
                unchecked
                {
                    uint h = (uint)(i * 73856093) ^ (uint)(salt * 19349663);
                    h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    return (int)(h & 0x7fffffff);
                }
            }

            /// <summary>一座山峰：圓心角 deg、半徑 r（格）處的三角形，左右兩面明暗不同，高的有雪頂。</summary>
            private void Peak(Painter2D p, float r, float deg, float size, bool snow)
            {
                var basePt = P(SurfaceConfig.Center + Mathf.Cos(deg * Mathf.Deg2Rad) * r, SurfaceConfig.Center + Mathf.Sin(deg * Mathf.Deg2Rad) * r);
                var top = basePt + new Vector2(0, -size * 1.15f);
                var l = basePt + new Vector2(-size * .8f, size * .35f);
                var rt = basePt + new Vector2(size * .8f, size * .35f);
                var mid = basePt + new Vector2(size * .1f, size * .35f);
                p.BeginPath(); p.MoveTo(top); p.LineTo(l); p.LineTo(mid); p.ClosePath();
                p.fillColor = C(150, 140, 128); p.Fill();
                p.BeginPath(); p.MoveTo(top); p.LineTo(mid); p.LineTo(rt); p.ClosePath();
                p.fillColor = C(98, 90, 84); p.Fill();
                if (snow && size > 10.5f)
                {
                    var s1 = Vector2.Lerp(top, l, .32f);
                    var s2 = Vector2.Lerp(top, rt, .32f);
                    p.BeginPath(); p.MoveTo(top); p.LineTo(s1); p.LineTo(Vector2.Lerp(s1, s2, .5f) + new Vector2(0, size * .08f)); p.LineTo(s2); p.ClosePath();
                    p.fillColor = C(232, 236, 240); p.Fill();
                }
            }

            /// <summary>深淵：往外裂開的暗紅地裂、一圈圈往下的黑、漩渦紋、深處暗紅光，以及一對眼睛。</summary>
            private void DrawAbyss(Painter2D p)
            {
                var c = P(SurfaceConfig.Center, SurfaceConfig.Center);
                float pr = R(SurfaceConfig.PitRadius);
                // 地裂
                for (int i = 0; i < 14; i++)
                {
                    float a = (i + Hash(i, 90) % 50 / 100f) * Mathf.PI * 2 / 14;
                    float len = pr * (.5f + Hash(i, 91) % 40 / 100f);
                    float a2 = a + (Hash(i, 92) % 30 - 15) * Mathf.Deg2Rad;
                    var p0 = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pr * .95f;
                    var p1 = p0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len * .55f;
                    var p2 = p1 + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * len * .45f;
                    p.BeginPath(); p.MoveTo(p0); p.LineTo(p1); p.LineTo(p2);
                    p.lineWidth = 3.2f; p.strokeColor = C(20, 14, 16); p.Stroke();
                    p.BeginPath(); p.MoveTo(p0); p.LineTo(p1); p.LineTo(p2);
                    p.lineWidth = 1.2f; p.strokeColor = C(200, 40, 34); p.Stroke();
                }
                // 一圈圈往下變黑
                for (int k = 0; k < 7; k++)
                {
                    float t = k / 6f;
                    p.BeginPath();
                    p.Arc(c, pr * (1f - t * .85f), Angle.Degrees(0), Angle.Degrees(360));
                    p.fillColor = Color.Lerp(C(46, 34, 40), C(2, 1, 4), Mathf.Pow(t, .7f));
                    p.Fill();
                }
                // 漩渦紋：幾條往中心捲進去的暗紫線
                for (int arm = 0; arm < 5; arm++)
                {
                    p.BeginPath();
                    for (int s = 0; s <= 40; s++)
                    {
                        float f = s / 40f;
                        float ang = arm * Mathf.PI * 2 / 5 + f * 4.2f;
                        var pt = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * pr * (.92f - f * .8f);
                        if (s == 0) p.MoveTo(pt); else p.LineTo(pt);
                    }
                    p.lineWidth = 1.6f; p.strokeColor = C(90, 40, 90, 150); p.Stroke();
                }
                // 深處暗紅光（隨時間脈動）
                float beat = .5f + .5f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(Time.unscaledTime * 1.1f)), 4);
                p.BeginPath();
                p.Arc(c, pr * .3f, Angle.Degrees(0), Angle.Degrees(360));
                p.lineWidth = pr * .12f; p.strokeColor = C(150, 14, 24, (int)(90 + 120 * beat)); p.Stroke();
                // 眼睛
                foreach (var dx in new[] { -1f, 1f })
                {
                    var e = c + new Vector2(dx * pr * .13f, -pr * .05f);
                    p.BeginPath();
                    p.MoveTo(e + new Vector2(-pr * .07f, 0));
                    p.LineTo(e + new Vector2(0, -pr * .028f));
                    p.LineTo(e + new Vector2(pr * .07f, 0));
                    p.LineTo(e + new Vector2(0, pr * .028f));
                    p.ClosePath();
                    p.fillColor = C(255, 214, 120); p.Fill();
                }
            }

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

                // 三個分區
                Disc(p, SurfaceConfig.OuterRadius + 4, C(86, 124, 68));
                Disc(p, SurfaceConfig.MiddleOuter, C(126, 128, 80));
                Disc(p, SurfaceConfig.RimOuter, C(132, 106, 82));

                // 外圍山谷：兩排錯開的山峰（灰岩＋雪頂），整圈沒有缺口
                for (int row = 1; row >= 0; row--)
                {
                    float r = SurfaceConfig.OuterRadius + 6 + row * 9;
                    int n = 150 - row * 10;
                    for (int i = 0; i < n; i++)
                        Peak(p, r, (i + row * .5f) * 360f / n, 9.5f + row * 2f + Hash(i, row) % 4, true);
                }
                // 分區之間的山脈：較矮的山峰，關隘處留缺口
                foreach (var r in SurfaceConfig.Boundaries)
                {
                    int n = r / 2;
                    for (int i = 0; i < n; i++)
                    {
                        float deg = i * 360f / n;
                        int x = SurfaceConfig.Center + Mathf.RoundToInt(Mathf.Cos(deg * Mathf.Deg2Rad) * r);
                        int y = SurfaceConfig.Center + Mathf.RoundToInt(Mathf.Sin(deg * Mathf.Deg2Rad) * r);
                        if (SurfaceLogic.NearPass(x, y, 2)) continue;
                        Peak(p, r, deg, 5.5f + Hash(i, r) % 3, false);
                    }
                }

                DrawAbyss(p);

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
