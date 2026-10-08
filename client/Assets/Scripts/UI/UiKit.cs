using System;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using UnityEngine;
using UnityEngine.UIElements;

namespace AbyssKingdom.Client.UI
{
    /// <summary>介面共用的小工具：建立元素、顯示切換、品質顏色、彈出視窗。</summary>
    public static class UiKit
    {
        /// <summary>彈出視窗要插入的地方（GameUI 啟動時設定）：插在 OverlayBefore（通知層）之前。</summary>
        public static VisualElement OverlayHost, OverlayBefore;

        public static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static Label Lbl(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        public static Label ResIcon(ResourceType r, bool small)
        {
            var icon = Lbl(DisplayNames.ResourceGlyphs[r], "res-icon", "res-" + r);
            icon.EnableInClassList("small", small);
            return icon;
        }

        public static void Show(VisualElement e, bool visible)
        {
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static Color GradeColor(RopeGrade g)
        {
            switch (g)
            {
                case RopeGrade.Uncommon: return new Color32(96, 170, 90, 255);
                case RopeGrade.Rare: return new Color32(70, 130, 220, 255);
                case RopeGrade.Epic: return new Color32(150, 90, 220, 255);
                case RopeGrade.Legendary: return new Color32(220, 170, 70, 255);
                case RopeGrade.Mythic: return new Color32(200, 50, 60, 255);
                default: return new Color32(150, 150, 160, 255);
            }
        }

        /// <summary>開一個置中的彈出視窗；點背景會關閉。回傳整個遮罩，呼叫 RemoveFromHierarchy() 即可關閉。</summary>
        public static VisualElement OpenModal(VisualElement box)
        {
            var overlay = El("overlay");
            overlay.Add(box);
            overlay.RegisterCallback<PointerDownEvent>(e => { if (e.target == overlay) overlay.RemoveFromHierarchy(); });
            OverlayHost.Insert(OverlayHost.IndexOf(OverlayBefore), overlay);
            return overlay;
        }
    }
}
