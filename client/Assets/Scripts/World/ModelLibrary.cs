using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 美術模型庫：從 Resources/Models/ 載入使用者在 Blender 做的模型（.fbx 或 .obj），
    /// 找不到就回傳 null，呼叫端改用灰盒。規格見 docs/art/MODEL_SPEC.md。
    /// 模型單位：Blender 的 1 公尺 = 地圖 1 格；原點在模型底部中心。
    /// 變體：Mountain_A、Mountain_B…（依位置固定挑一個）。
    /// </summary>
    public static class ModelLibrary
    {
        private const string Root = "Models/";
        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, List<GameObject>> Variants = new Dictionary<string, List<GameObject>>();

        /// <summary>取得模型原型（沒有回傳 null）。</summary>
        public static GameObject Get(string path)
        {
            if (!Cache.TryGetValue(path, out var prefab))
            {
                prefab = Resources.Load<GameObject>(Root + path);
                Cache[path] = prefab;
            }
            return prefab;
        }

        /// <summary>取得變體清單：path_A、path_B、…（最多 26 個；也接受沒有字尾的 path 本身）。</summary>
        public static List<GameObject> GetVariants(string path)
        {
            if (Variants.TryGetValue(path, out var list)) return list;
            list = new List<GameObject>();
            var single = Get(path);
            if (single != null) list.Add(single);
            for (char c = 'A'; c <= 'Z'; c++)
            {
                var v = Get(path + "_" + c);
                if (v == null) break;
                list.Add(v);
            }
            Variants[path] = list;
            return list;
        }

        /// <summary>
        /// 放一個模型：tileSize = 每格的世界單位（大地圖 0.2、城內 0.5）。
        /// 沒有模型回傳 null。variantSeed 用來在多個變體中固定挑一個。
        /// </summary>
        public static GameObject Spawn(string path, Transform parent, Vector3 localPos, float tileSize, int variantSeed = 0, float yaw = 0)
        {
            var list = GetVariants(path);
            if (list.Count == 0) return null;
            var prefab = list[(variantSeed & 0x7fffffff) % list.Count];
            var go = Object.Instantiate(prefab, parent, false);
            go.name = prefab.name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0) * go.transform.localRotation;
            go.transform.localScale = Vector3.Scale(go.transform.localScale, Vector3.one * tileSize);
            SetLayer(go.transform, WorldScene.Layer);
            foreach (var col in go.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            return go;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }
}
