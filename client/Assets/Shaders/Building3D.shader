// 3D 建築（Models/City3D）：霧面漫射（沒有高光，Gamma 色彩空間下才不會整棟發灰），
// 並依貼圖上的「保留色格」自動發光（顏色見 tools/blender/city3d_common.py）：
//   契印 B8FFF1、晶簇與玉印 37BCAE：青綠色緩慢呼吸
//   窗燈與發光菇 F6B647：暖色燭光閃爍
// 外加點選高亮（_Highlight）；_Wind > 0 時（樹、草、花）頂端隨風輕擺，越高擺越大。只用於畫面，不影響規則。
Shader "AbyssKingdom/Building3D"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _RuneColor ("Rune glow", Color) = (0.35, 1.0, 0.88, 1)
        _WarmColor ("Warm light", Color) = (1.0, 0.72, 0.32, 1)
        _Phase ("Phase offset", Float) = 0
        _Highlight ("Highlight", Range(0, 1)) = 0
        _Wind ("Wind sway", Float) = 0
        _Cloth ("Cloth amplitude", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color, _RuneColor, _WarmColor;
        float _Phase, _Highlight, _Wind, _Cloth;

        // 風：用世界座標算（動態合批時頂點已經是世界座標，這樣寫兩種情況都對）。
        // 擺幅和離地高度成正比再加一點，地面附近幾乎不動；相位依位置錯開，整片樹林像被一陣陣風吹過。
        void vert (inout appdata_full v)
        {
            if (_Cloth > 0)
            {
                float4 clothWorld = mul(unity_ObjectToWorld, v.vertex);
                float weight = v.color.r;
                float wave = sin(_Time.y * 3.2 + weight * 5.5 + _Phase)
                           + 0.3 * sin(_Time.y * 5.1 - weight * 8.0 + _Phase);
                clothWorld.z += wave * weight * weight * _Cloth;
                v.vertex = mul(unity_WorldToObject, clothWorld);
            }
            if (_Wind <= 0) return;
            float4 wp = mul(unity_ObjectToWorld, v.vertex);
            float h = max(0, wp.y);
            float t = _Time.y;
            float2 sway = float2(sin(t * 1.6 + wp.x * 2.3 + wp.z * 1.7) + 0.35 * sin(t * 3.7 + wp.x * 5.1),
                                 0.6 * sin(t * 1.25 + wp.z * 2.1 + wp.x * 0.9));
            wp.xz += sway * h * (h + 0.1) * _Wind;
            v.vertex = mul(unity_WorldToObject, wp);
        }

        struct Input { float2 uv_MainTex; };

        // 和保留色的距離越近越接近 1（貼圖壓縮與縮圖會讓顏色稍微偏移，所以給一點容差）
        float Match(fixed3 c, fixed3 key, float tolerance)
        {
            return saturate(1 - distance(c, key) / tolerance);
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;

            float t = _Time.y + _Phase;
            float rune = max(Match(c.rgb, fixed3(0.722, 1.0, 0.945), 0.16), Match(c.rgb, fixed3(0.216, 0.737, 0.682), 0.09) * 0.7);
            float warm = Match(c.rgb, fixed3(0.965, 0.714, 0.278), 0.10);
            float breathe = 0.5 + 0.5 * sin(t * 1.4);
            float flicker = 0.82 + 0.1 * sin(t * 8.7) + 0.08 * sin(t * 21.3 + 1.7);

            o.Emission = rune * _RuneColor.rgb * (0.35 + 0.65 * breathe) * 0.9
                       + warm * _WarmColor.rgb * flicker * 0.45
                       + _Highlight * (c.rgb * 0.3 + fixed3(0.08, 0.06, 0.02));
        }
        ENDCG
    }
    FallBack "Diffuse"
}
