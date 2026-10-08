using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 產生深淵 3D 灰盒用的材質（Assets/Resources/Abyss/*.mat）。
/// 用材質檔而不是執行期 new Material，是為了讓發光、半透明等著色器變體一定會被打包進去。
/// 打包前會自動執行；也可以從選單 Abyss Kingdom → Generate Abyss Materials 手動產生。
/// </summary>
public static class AbyssMaterials
{
    private const string Folder = "Assets/Resources/Abyss";

    [MenuItem("Abyss Kingdom/Generate Abyss Materials")]
    public static void Generate()
    {
        Directory.CreateDirectory(Folder);
        var standard = Shader.Find("Standard");

        Make(standard, "Rock", new Color(0.13f, 0.12f, 0.12f), smooth: 0.05f);
        Make(standard, "RockDark", new Color(0.07f, 0.07f, 0.08f), smooth: 0.05f);
        Make(standard, "Ground", new Color(0.30f, 0.26f, 0.22f), smooth: 0.1f);
        Make(standard, "Tunnel", new Color(0.42f, 0.38f, 0.33f), smooth: 0.15f);
        Make(standard, "TunnelDim", new Color(0.16f, 0.15f, 0.15f), smooth: 0.1f);
        Make(standard, "Chamber", new Color(0.48f, 0.45f, 0.42f), smooth: 0.2f);
        Make(standard, "Unknown", new Color(0.06f, 0.07f, 0.10f), smooth: 0.0f);
        Make(standard, "Nest", new Color(0.45f, 0.12f, 0.12f), new Color(0.35f, 0.04f, 0.04f));
        Make(standard, "Gate", new Color(0.85f, 0.65f, 0.30f), new Color(0.9f, 0.6f, 0.2f));
        Make(standard, "Hub", new Color(0.25f, 0.65f, 0.65f), new Color(0.1f, 0.6f, 0.6f));
        Make(standard, "Lair", new Color(0.6f, 0.08f, 0.15f), new Color(0.9f, 0.05f, 0.15f));
        Make(standard, "City", new Color(0.75f, 0.5f, 0.25f), new Color(0.35f, 0.2f, 0.05f));
        Make(standard, "Sky", new Color(0.1f, 0.14f, 0.25f), new Color(0.12f, 0.16f, 0.30f));
        Make(standard, "Select", new Color(1f, 0.85f, 0.45f), new Color(1.2f, 0.9f, 0.4f));
        Make(standard, "Seal", new Color(0.8f, 0.15f, 0.2f), new Color(1.5f, 0.2f, 0.25f));
        Make(standard, "Rope", new Color(0.55f, 0.45f, 0.3f), smooth: 0.1f);
        Make(standard, "Fissure_Wood", new Color(0.35f, 0.7f, 0.35f), new Color(0.3f, 1.1f, 0.3f));
        Make(standard, "Fissure_Fire", new Color(0.9f, 0.35f, 0.2f), new Color(1.4f, 0.4f, 0.1f));
        Make(standard, "Fissure_Earth", new Color(0.8f, 0.6f, 0.3f), new Color(1.0f, 0.7f, 0.2f));
        Make(standard, "Fissure_Metal", new Color(0.85f, 0.85f, 0.9f), new Color(0.9f, 0.9f, 1.1f));
        Make(standard, "Fissure_Water", new Color(0.3f, 0.55f, 0.9f), new Color(0.2f, 0.6f, 1.4f));
        MakeFade(standard, "Veil", new Color(0.6f, 0.4f, 1f, 0.35f), new Color(0.5f, 0.3f, 1.2f));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AbyssMaterials] generated");
    }

    private static Material Load(Shader shader, string name)
    {
        string path = $"{Folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        return mat;
    }

    private static void Make(Shader shader, string name, Color color, Color? emission = null, float smooth = 0.2f)
    {
        var mat = Load(shader, name);
        mat.color = color;
        mat.SetFloat("_Glossiness", smooth);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else mat.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat);
    }

    /// <summary>Standard 著色器的 Fade（半透明）模式。</summary>
    private static void MakeFade(Shader shader, string name, Color color, Color emission)
    {
        Make(shader, name, color, emission, 0.5f);
        var mat = Load(shader, name);
        mat.SetFloat("_Mode", 2);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
    }
}
