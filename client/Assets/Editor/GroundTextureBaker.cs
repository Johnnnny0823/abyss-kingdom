using System;
using System.IO;
using AbyssKingdom.Client.World;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把地面貼圖（GroundTextures 的雜訊）預先烤成 Assets/Resources/Ground/*.png，
/// 執行期直接讀圖，不必每次登入都花 1 秒多現算。打包前會自動執行；也可以從選單手動產生。
/// </summary>
public static class GroundTextureBaker
{
    private const string Folder = "Assets/Resources/" + GroundTextures.BakedFolder;

    [MenuItem("Abyss Kingdom/Bake Ground Textures")]
    public static void Bake()
    {
        Directory.CreateDirectory(Folder);
        foreach (GroundTextures.Style s in Enum.GetValues(typeof(GroundTextures.Style)))
        {
            string path = $"{Folder}/Ground_{s}.png";
            if (!File.Exists(path))
            {
                var tex = GroundTextures.Generate(s);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp.wrapMode != TextureWrapMode.Repeat || imp.anisoLevel != 4 || !imp.mipmapEnabled)
            {
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.filterMode = FilterMode.Trilinear;
                imp.anisoLevel = 4;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
        }
        Debug.Log("[GroundTextureBaker] baked");
    }
}
