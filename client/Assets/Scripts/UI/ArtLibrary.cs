using System.Collections.Generic;
using AbyssKingdom.Shared.City;
using AbyssKingdom.Shared.Heroes;
using UnityEngine;

namespace AbyssKingdom.Client.UI
{
    /// <summary>
    /// 角色美術圖（2D PNG）：主將立繪、主將頭像、兵種圖。放在 Resources/Art/ 就生效，沒有就回傳 null，
    /// 呼叫端維持原本的剪影或 3D 模型。規格見 docs/art/CHARACTER_SPEC.md。
    /// </summary>
    public static class ArtLibrary
    {
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<Texture2D, Sprite> Sprites = new Dictionary<Texture2D, Sprite>();

        private static Texture2D Load(string path)
        {
            if (!Textures.TryGetValue(path, out var tex))
            {
                tex = Resources.Load<Texture2D>("Art/" + path);
                Textures[path] = tex;
            }
            return tex;
        }

        /// <summary>主將全身立繪（主將頁）。檔名：Art/Heroes/&lt;HeroId&gt;_Portrait.png</summary>
        public static Texture2D HeroPortrait(HeroId id) => Load($"Heroes/{id}_Portrait");

        /// <summary>主將頭像（列表、地圖上行軍隊伍的頭上）。檔名：Art/Heroes/&lt;HeroId&gt;_Icon.png</summary>
        public static Texture2D HeroIcon(HeroId id) => Load($"Heroes/{id}_Icon");

        /// <summary>兵種圖（地圖上行軍的 2D 部隊）。檔名：Art/Troops/&lt;TroopType&gt;.png</summary>
        public static Texture2D Troop(TroopType t) => Load($"Troops/{t}");

        /// <summary>城內建築圖（2D）。檔名：Art/Buildings/&lt;BuildingType&gt;_&lt;1~3&gt;.png，外觀 1 = Lv.1~3、2 = Lv.4~6、3 = Lv.7~10；未建造用 Site.png。</summary>
        public static Texture2D Building(string name) => Load($"Buildings/{name}");

        /// <summary>給 SpriteRenderer 用：圖的寬度對應 worldWidth 個世界單位，錨點在底部中央。</summary>
        public static Sprite SpriteOfWidth(Texture2D tex, float worldWidth)
        {
            if (tex == null) return null;
            var key = tex;
            if (!Sprites.TryGetValue(key, out var s))
            {
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, 0), tex.width / worldWidth);
                Sprites[key] = s;
            }
            return s;
        }

        /// <summary>給 SpriteRenderer 用：圖的高度對應 worldHeight 個世界單位，錨點在底部中央。</summary>
        public static Sprite SpriteOf(Texture2D tex, float worldHeight)
        {
            if (tex == null) return null;
            if (!Sprites.TryGetValue(tex, out var s))
            {
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, 0), tex.height / worldHeight);
                Sprites[tex] = s;
            }
            return s;
        }
    }
}
