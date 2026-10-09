// 2.5D 動態建築圖：在 2D 精緻圖上，依效果遮罩（_FxTex）做出
//   R：契印緩慢呼吸發光   G：窗燈／發光菇燭光閃爍   B：旗幟布料隨風擺動
// 外加點選高亮（_Highlight）。只用於畫面，不影響規則。
Shader "AbyssKingdom/LivingSprite"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _FxTex ("Effect mask (R rune, G warm, B cloth)", 2D) = "black" {}
        _RuneColor ("Rune glow", Color) = (0.35, 1.0, 0.88, 1)
        _WarmColor ("Warm light", Color) = (1.0, 0.72, 0.32, 1)
        _Phase ("Phase offset", Float) = 0
        _Highlight ("Highlight", Range(0, 1)) = 0
        _Wind ("Wind strength", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _FxTex;
            float4 _MainTex_TexelSize;
            fixed4 _RuneColor, _WarmColor;
            float _Phase, _Highlight, _Wind;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y + _Phase;
                fixed4 fx = tex2D(_FxTex, i.uv);

                // 布料：沿高度傳遞的波，只在布料遮罩內位移取樣點（幾個像素）
                float wave = sin(t * 2.6 + i.uv.y * 38.0) + 0.5 * sin(t * 4.1 + i.uv.y * 71.0);
                float2 uv = i.uv + float2(wave * fx.b * _Wind * 3.0 * _MainTex_TexelSize.x, 0);
                fixed4 c = tex2D(_MainTex, uv) * i.color;

                // 契印：慢慢呼吸；窗燈：燭光不規則閃爍
                float breathe = 0.5 + 0.5 * sin(t * 1.4);
                float flicker = 0.82 + 0.1 * sin(t * 8.7) + 0.08 * sin(t * 21.3 + 1.7);
                c.rgb += fx.r * _RuneColor.rgb * (0.25 + 0.75 * breathe) * 0.85;
                c.rgb += fx.g * _WarmColor.rgb * flicker * 0.3;

                // 點選高亮：整體提亮、邊緣帶一點金色
                c.rgb = lerp(c.rgb, c.rgb * 1.25 + fixed3(0.08, 0.06, 0.02), _Highlight);
                return c;
            }
            ENDCG
        }
    }
}
