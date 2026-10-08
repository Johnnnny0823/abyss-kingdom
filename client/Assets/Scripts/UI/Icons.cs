using AbyssKingdom.Shared.City;
using UnityEngine;
using UnityEngine.UIElements;

namespace AbyssKingdom.Client.UI
{
    public enum IconKind
    {
        Wood,          // 根材：貫穿地層的古樹根
        Stone,         // 層岩：神話地層壓成的岩
        Food,          // 菌糧：發光菇
        Crystal,       // 響晶：凝固的回響
        MagicCrystal,  // 魔晶石
        AbyssHeart,    // 淵心鑽
    }

    /// <summary>
    /// 資源與貨幣圖示：用 UI Toolkit 的向量繪圖（Painter2D）直接畫，不需要圖檔，任何大小都清晰。
    /// 造型依 2026-10-08 確認的樣式提案（72×72 設計座標）；正式美術完成後換成圖片即可。
    /// </summary>
    public sealed class IconElement : VisualElement
    {
        private readonly IconKind _kind;

        public IconElement(IconKind kind, float size)
        {
            _kind = kind;
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public static IconKind For(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Wood: return IconKind.Wood;
                case ResourceType.Stone: return IconKind.Stone;
                case ResourceType.Food: return IconKind.Food;
                default: return IconKind.Crystal;
            }
        }

        private static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float k = contentRect.width / 72f;
            var p = ctx.painter2D;
            Vector2 P(float x, float y) => new Vector2(x * k, y * k);

            void Circle(float x, float y, float r, Color? fill, Color? stroke = null, float width = 2f)
            {
                p.BeginPath();
                p.Arc(P(x, y), r * k, Angle.Degrees(0), Angle.Degrees(360));
                p.ClosePath();
                if (fill.HasValue) { p.fillColor = fill.Value; p.Fill(); }
                if (stroke.HasValue) { p.strokeColor = stroke.Value; p.lineWidth = width * k; p.Stroke(); }
            }

            void Poly(Color fill, Color? stroke, float width, params float[] xy)
            {
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                p.ClosePath();
                p.fillColor = fill;
                p.Fill();
                if (stroke.HasValue) { p.strokeColor = stroke.Value; p.lineWidth = width * k; p.Stroke(); }
            }

            void Rect(float x, float y, float w, float h, Color fill) => Poly(fill, null, 0, x, y, x + w, y, x + w, y + h, x, y + h);

            void Line(Color color, float width, float x1, float y1, float x2, float y2)
            {
                p.BeginPath();
                p.MoveTo(P(x1, y1));
                p.LineTo(P(x2, y2));
                p.strokeColor = color;
                p.lineWidth = width * k;
                p.lineCap = LineCap.Round;
                p.Stroke();
            }

            switch (_kind)
            {
                case IconKind.Wood:
                    Circle(36, 36, 32, C("#2c2418"), C("#BA7517"));
                    var amber = C("#EF9F27");
                    Line(amber, 4, 36, 14, 36, 40);
                    Line(amber, 4, 36, 40, 22, 56);
                    Line(amber, 4, 36, 40, 50, 56);
                    Line(amber, 4, 36, 32, 26, 22);
                    Line(amber, 4, 36, 30, 47, 21);
                    Line(amber, 4, 36, 46, 30, 58);
                    Line(amber, 4, 36, 46, 42, 58);
                    break;

                case IconKind.Stone:
                    Circle(36, 36, 32, C("#22252b"), C("#888780"));
                    Rect(18, 22, 36, 8, C("#B4B2A9"));
                    Rect(15, 32, 42, 8, C("#888780"));
                    Rect(18, 42, 36, 8, C("#5F5E5A"));
                    Circle(30, 46, 2.5f, C("#D3D1C7"));
                    break;

                case IconKind.Food:
                    Circle(36, 36, 32, C("#1c2618"), C("#639922"));
                    // 菇傘：半圓弧（180° → 360° 經過上方）
                    p.BeginPath();
                    p.Arc(P(36, 40), 20 * k, Angle.Degrees(180), Angle.Degrees(360));
                    p.ClosePath();
                    p.fillColor = C("#97C459");
                    p.Fill();
                    Rect(31, 39, 10, 16, C("#C0DD97"));
                    Circle(28, 30, 3f, C("#EAF3DE"));
                    Circle(43, 28, 2.5f, C("#EAF3DE"));
                    break;

                case IconKind.Crystal:
                    Circle(36, 36, 32, C("#1a1d33"), C("#378ADD"));
                    var ring = C("#85B7EB");
                    Circle(36, 38, 20, null, new Color(ring.r, ring.g, ring.b, 0.55f), 1.8f);
                    Circle(36, 38, 14, null, new Color(ring.r, ring.g, ring.b, 0.8f), 1.8f);
                    Poly(C("#B5D4F4"), C("#E6F1FB"), 1.8f, 36, 16, 45, 32, 36, 54, 27, 32);
                    break;

                case IconKind.MagicCrystal:
                    Circle(36, 36, 32, C("#211c3a"), C("#7F77DD"));
                    Poly(C("#AFA9EC"), C("#EEEDFE"), 1.8f, 22, 30, 30, 18, 42, 18, 50, 30, 36, 56);
                    Line(C("#534AB7"), 1.4f, 22, 30, 50, 30);
                    Line(C("#534AB7"), 1.4f, 30, 18, 36, 30);
                    Line(C("#534AB7"), 1.4f, 36, 30, 42, 18);
                    Line(C("#534AB7"), 1.4f, 36, 30, 36, 56);
                    break;

                case IconKind.AbyssHeart:
                    Circle(36, 36, 32, C("#2a1216"), C("#A32D2D"));
                    p.BeginPath();
                    p.MoveTo(P(36, 56));
                    p.LineTo(P(18, 34));
                    p.BezierCurveTo(P(14, 24), P(22, 18), P(29, 20));
                    p.BezierCurveTo(P(33, 21), P(35, 23), P(36, 26));
                    p.BezierCurveTo(P(37, 23), P(39, 21), P(43, 20));
                    p.BezierCurveTo(P(50, 18), P(58, 24), P(54, 34));
                    p.ClosePath();
                    p.fillColor = C("#791F1F");
                    p.Fill();
                    p.strokeColor = C("#F09595");
                    p.lineWidth = 1.6f * k;
                    p.Stroke();
                    Circle(36, 34, 10, null, C("#E24B4A"), 1.4f);
                    Circle(36, 34, 6, C("#F7C1C1"));
                    break;
            }
        }
    }
}
