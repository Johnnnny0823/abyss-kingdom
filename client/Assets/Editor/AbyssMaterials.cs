using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 產生 3D 灰盒用的材質（Assets/Resources/Abyss/*.mat）。
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

        // 主畫面灰盒的基底材質（顏色在執行期換）與選取標記的發光材質
        Make(standard, "Chamber", new Color(0.48f, 0.45f, 0.42f), smooth: 0.2f);
        Make(standard, "Select", new Color(1f, 0.85f, 0.45f), new Color(1.2f, 0.9f, 0.4f));
        // 2D 建築圖的動態著色器與契印光點
        var living = Load(Shader.Find("AbyssKingdom/LivingSprite"), "LivingSprite");
        EditorUtility.SetDirty(living);
        var mote = Load(Shader.Find("Legacy Shaders/Particles/Additive"), "Mote");
        mote.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        mote.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(mote);
        var smoke = Load(Shader.Find("Legacy Shaders/Particles/Alpha Blended"), "Smoke");
        smoke.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        smoke.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(smoke);
        // 深淵的岩石：完全霧面（關掉太陽高光與環境反射）。Gamma 色彩空間下 Standard 的高光會把粗糙表面整片打亮，
        // 再暗的顏色都黑不下去；關鍵字變體要有材質檔用到才會被打包，所以做成材質檔。
        Make(standard, "Rock", new Color(0.1f, 0.1f, 0.1f), smooth: 0f);
        var rock = Load(standard, "Rock");
        rock.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        rock.SetFloat("_SpecularHighlights", 0f);
        rock.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        rock.SetFloat("_GlossyReflections", 0f);
        EditorUtility.SetDirty(rock);
        // 3D 建築：霧面＋契印／窗燈自動發光（Shaders/Building3D.shader）
        var building3D = Load(Shader.Find("AbyssKingdom/Building3D"), "Building3D");
        EditorUtility.SetDirty(building3D);
        // 深淵的霧（半透明、微微發光）與坑口光暈（青綠）
        MakeFade(standard, "Mist", new Color(0.72f, 0.78f, 0.84f, 1f), new Color(0.08f, 0.1f, 0.12f));
        MakeFade(standard, "RimGlow", new Color(0.4f, 0.95f, 0.85f, 1f), new Color(0.2f, 0.9f, 0.75f));

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
