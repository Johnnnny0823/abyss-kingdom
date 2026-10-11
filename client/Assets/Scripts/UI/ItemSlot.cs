using AbyssKingdom.Shared.City;
using UnityEngine.UIElements;
using static AbyssKingdom.Client.UI.UiKit;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// 道具格子：品質邊框＋圖示＋右下數量＋左上小標籤（加速道具的時間）。
    /// 沒有圖檔時顯示道具名稱的第一個字。背包與加速視窗共用。
    /// </summary>
    public static class ItemSlot
    {
        public static VisualElement Create(ItemType item, long count, bool small = false)
        {
            var slot = El("item-slot", "rarity-" + DisplayNames.ItemRarity(item));
            if (small) slot.AddToClassList("item-slot-small");
            slot.Add(Icon(item));
            string badge = DisplayNames.ItemBadge(item);
            if (badge != "") slot.Add(Lbl(badge, "item-badge"));
            if (count > 0) slot.Add(Lbl(count > 9999 ? $"{count / 1000}K" : count.ToString(), "item-qty"));
            return slot;
        }

        public static VisualElement Icon(ItemType item)
        {
            var icon = El("item-icon");
            var tex = ArtLibrary.ItemIcon(item);
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            else icon.Add(Lbl(DisplayNames.Item(item).Substring(0, 1), "item-glyph"));
            return icon;
        }
    }
}
