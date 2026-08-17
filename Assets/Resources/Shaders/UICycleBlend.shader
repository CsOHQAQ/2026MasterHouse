// 延时序列播放（2026-08-17）：单层里同时采样「当前帧 / 下一帧 / 形状遮罩」，
// rgb 按权重插值、alpha 只取遮罩 —— 关键是**一次渲染**：
// 用两层半透明叠加做交叉淡化时，羽化区的总不透明度会随权重来回变化，边缘就会周期性明暗闪烁。
Shader "MasterHouse/UICycleBlend"
{
    Properties
    {
        _MainTex ("当前帧", 2D) = "white" {}
        _NextTex ("下一帧", 2D) = "white" {}
        _MaskTex ("形状遮罩", 2D) = "white" {}
        _Blend ("混合权重", Range(0, 1)) = 0
        _FadeUV ("边缘羽化(uv 宽度)", Vector) = (0, 0, 0, 0)
        _GradeGain ("局部校色增益", Vector) = (1, 1, 1, 1)
        _GradeY ("校色纵向范围", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _NextTex;
            sampler2D _MaskTex;
            float _Blend;
            float4 _FadeUV;
            float4 _GradeGain;
            float4 _GradeY;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 rgb = lerp(tex2D(_MainTex, i.uv).rgb, tex2D(_NextTex, i.uv).rgb, _Blend);
                // 独立下层结构用：接缝处对齐 HouseCycle 色调，沿柱身向下过渡回外景原色。
                // _GradeY.z = 0 时为中性，其他循环材质不受影响。
                float grade = smoothstep(_GradeY.x, _GradeY.y, i.uv.y) * _GradeY.z;
                rgb *= lerp(fixed3(1, 1, 1), _GradeGain.rgb, grade);
                // 形状遮罩（主楼层：只画建筑，天空留给下层——太阳/云/星空才透得上来）
                float a = tex2D(_MaskTex, i.uv).a;
                // 额外的四周羽化（_FadeUV 为 0 时不生效）
                float2 d = min(i.uv, 1 - i.uv) / max(_FadeUV.xy, 1e-5);
                float edge = saturate(min(d.x, d.y));
                a *= edge * edge * (3 - 2 * edge);
                return fixed4(rgb, a) * i.color;
            }
            ENDCG
        }
    }
}
