// 相片火烧转场（2026-08-14 标题页进入主背景）：对整屏快照做「从起点向外烧穿」的溶解。
// 烧穿边缘分两层：先焦黑（char）再余烬亮线（ember），毛边由噪声贴图提供。
// 仅供 TitleBurnFx 的全屏 RawImage 使用；放 Resources 保证运行时 Load 可得。
Shader "MasterHouse/UIBurn"
{
    Properties
    {
        _MainTex ("屏幕快照", 2D) = "white" {}
        _NoiseTex ("噪声", 2D) = "white" {}
        _Progress ("烧穿进度", Range(0, 1.5)) = 0
        _Origin ("起火点(UV)", Vector) = (.5, .5, 0, 0)
        _Aspect ("画面宽高比", Float) = 1.78
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            float _Progress;
            float4 _Origin;
            float _Aspect;

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
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                float noise = tex2D(_NoiseTex, i.uv * 3).r;
                // 起火点距离（按宽高比校正成圆形扩散）+ 噪声毛边
                float2 offset = (i.uv - _Origin.xy) * float2(_Aspect, 1);
                float dist = length(offset) / 1.2;
                float burn = dist * .55 + noise * .45;
                float d = burn - _Progress; // < 0 = 已烧没
                clip(d);
                // 焦黑带：接近烧穿边缘的纸面先碳化
                float charT = saturate(1 - d / .12);
                col.rgb = lerp(col.rgb, col.rgb * .12, charT * charT);
                // 余烬亮线：紧贴烧穿边缘的一圈火光
                float ember = saturate(1 - d / .045);
                col.rgb += float3(1.6, .55, .12) * ember * ember;
                return col;
            }
            ENDCG
        }
    }
}
