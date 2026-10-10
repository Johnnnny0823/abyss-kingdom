using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 3D 建築（Models/City3D）的生命感：換上 Building3D 著色器（契印、晶簇青綠呼吸，窗燈燭光閃爍，見 Shaders/Building3D.shader），
    /// 有煙囪的模型冒炊煙。純畫面效果。
    /// </summary>
    public static class LivingModel
    {
        private static Material _base;
        private static readonly Dictionary<(Texture, int), Material> Mats = new Dictionary<(Texture, int), Material>();

        /// <summary>
        /// 煙囪頂端的位置（模型座標，底面 1×1）。來自 tools/blender/generate_city3d.py：
        /// 伐木場的煙囪蓋在 Blender (x -0.29, y -0.27, z 0.82 + 0.10×(外觀-1))；匯出後 Blender 的 z 是 Unity 的 y、y 是 -z，x 鏡像。
        /// 其他模型沒有煙囪，就不冒煙。
        /// </summary>
        private static readonly Dictionary<string, Vector3[]> Chimneys = new Dictionary<string, Vector3[]>
        {
            { "Sawmill_1", new[] { FromBlender(-.29f, -.27f, .85f) } },
            { "Sawmill_2", new[] { FromBlender(-.29f, -.27f, .95f) } },
            { "Sawmill_3", new[] { FromBlender(-.29f, -.27f, 1.05f) } },
        };

        private static Vector3 FromBlender(float x, float y, float z) => new Vector3(-x, z, -y);

        /// <summary>把一個 3D 建築模型裝上發光材質與炊煙。phaseSeed 讓每棟的呼吸節奏錯開。</summary>
        public static void Attach(GameObject model, string artName, int phaseSeed)
        {
            if (_base == null) _base = Resources.Load<Material>("Abyss/Building3D");
            if (_base != null)
            {
                int phase = phaseSeed & 0xff;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++)
                    {
                        var tex = mats[k] != null ? mats[k].mainTexture : null;
                        if (!Mats.TryGetValue((tex, phase), out var m))
                        {
                            m = new Material(_base) { mainTexture = tex };
                            m.SetFloat("_Phase", phase / 25f);
                            Mats[(tex, phase)] = m;
                        }
                        mats[k] = m;
                    }
                    r.sharedMaterials = mats;
                }
            }
            else CityView.MakeMatte(model);

            if (Chimneys.TryGetValue(artName, out var spots))
                foreach (var p in spots)
                {
                    // 掛在建築根物件（沒有縮放）上，位置換算成它的座標：煙的大小和 2D 建築的一樣
                    var parent = model.transform.parent != null ? model.transform.parent : model.transform;
                    LivingBuilding.MakeSmoke(parent, parent.InverseTransformPoint(model.transform.TransformPoint(p)));
                }
        }
    }
}
