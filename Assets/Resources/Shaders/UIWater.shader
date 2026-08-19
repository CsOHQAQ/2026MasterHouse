// 冲咖啡水面（2026-08-17，俯视·尾迹版）：一张 Image 上纯数学画水，不依赖网格细分与序列帧——
// 改材质属性不触发 Canvas rebuild，逐帧开销可忽略。
// Top-down 视角：液面是一个圆盘，半径由 _FillRadius 定。
// 2026-08-20 换上美术底图后，制作咖啡把它**钉在满值**（0.5）——满杯的咖啡已经画在底图里了，
// 这一层退成"薄薄一层水色 + 搅动波纹"，进度改由 HUD 的进度条表达。
// 水色压得很淡（alpha 0.18 左右）纯粹是为了让晃动的液面边沿看得出来，太浓会糊掉底图的笔触。
// 波纹走开尔文尾迹的成因：倒水点是移动波源，高频冒出微弱的细圆波（_Rings 32 槽位循环复用），
// 单个波元看不见，看得见的是叠加包络——拖动时是船尾那样的 V 形臂＋弯曲拖尾
// （前提：拖动速度 > 波元扩散速度），停在原地是同一点持续搅动的光斑。
// 每个波元记住出生点、自行扩散变淡，半径/强度由代码按年龄算好喂入。
// 液面边缘的角向晃动相位独立（_WobblePhase）：晃动速度由速度方差驱动，与涡环无关。
// 杯壁裁剪与 PourGame.InsideCup 是同一个圆（半径 = 区域短边一半），视觉与判定天然同圆。
// 所有动态量由代码逐帧喂入，不用 _Time：时间统一走根组件的 dt，暂停时水面跟着停。
Shader "MasterHouse/UIWater"
{
    Properties
    {
        [PerRendererData] _MainTex ("精灵贴图（UI 由 CanvasRenderer 自动喂，无精灵时为白图）", 2D) = "white" {}
        _WaterColor ("水体色（薄薄压一层，别盖住底图）", Color) = (0.24, 0.16, 0.10, 0.18)
        _RippleColor ("波纹亮纹色", Color) = (0.88, 0.78, 0.62, 0.55)
        _FillRadius ("液面半径 0~0.5（代码喂；满杯底图下钉在 0.5）", Range(0, 0.5)) = 0.5
        _WobblePhase ("边缘晃动相位（弧度，代码喂：速度随方差变）", Float) = 0
        _WobbleAmp ("边缘晃动幅度 0~1（代码喂：倒水涨、停手落）", Range(0, 1)) = 0.35
        _EdgeWaveCount ("边缘波瓣数（取整数，否则圆周接缝处裂开）", Float) = 6
        _EdgeWobble ("边缘晃动基准幅度（uv，乘 _WobbleAmp）", Range(0, 0.05)) = 0.015
        _RingThickness ("波元环带厚度（uv，细才叠得出包络）", Range(0.001, 0.1)) = 0.012
        _EdgeSoft ("杯壁羽化（uv）", Range(0.001, 0.1)) = 0.012
        // 波元槽位 _Rings[32] 是数组 uniform，进不了 Properties——由代码每帧 SetVectorArray
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
            #pragma target 3.0   // 32 个波元槽位的循环索引需要
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _WaterColor;
            fixed4 _RippleColor;
            float _FillRadius;
            float _WobblePhase;
            float _WobbleAmp;
            float _EdgeWaveCount;
            float _EdgeWobble;
            float _RingThickness;
            float _EdgeSoft;

            // 每槽 xy=出生点(uv) z=当前半径(uv) w=当前强度(0=空槽)，年龄衰减在 CPU 侧算好
            #define RING_SLOTS 32
            float4 _Rings[RING_SLOTS];

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
                float2 p = i.uv - 0.5;
                float r = length(p);
                // 杯壁裁剪：uv 中心 (0.5, 0.5)、半径 0.5，边缘羽化抗锯齿
                float cup = 1 - smoothstep(0.5 - _EdgeSoft, 0.5, r);

                // 液面盘：半径随进度从杯心扩到杯壁，边缘带角向晃动。
                // 两组波瓣数都是整数才能在 atan2 的 ±π 接缝处连续
                float ang = atan2(p.y, p.x);
                float wobble = sin(ang * _EdgeWaveCount + _WobblePhase)
                             + 0.5 * sin(ang * (_EdgeWaveCount + 3.0) - _WobblePhase * 1.3);
                float edgeR = _FillRadius + wobble * _EdgeWobble * _WobbleAmp;
                float water = 1 - smoothstep(edgeR - 0.008, edgeR + 0.008, r);

                // 波元叠加：每个波元是一条以出生点为心的细环带，微弱的波元相加后饱和——
                // 亮度只在包络（环带扎堆处）积累出来，这正是尾迹的形状
                float ripple = 0;
                for (int k = 0; k < RING_SLOTS; k++)
                {
                    float4 ring = _Rings[k];
                    float band = 1 - smoothstep(0, _RingThickness, abs(length(i.uv - ring.xy) - ring.z));
                    ripple += band * ring.w;
                }
                ripple = saturate(ripple);

                fixed4 col;
                col.rgb = lerp(_WaterColor.rgb, _RippleColor.rgb, ripple);
                col.a = lerp(_WaterColor.a, _RippleColor.a, ripple) * water * cup;
                return col * tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }
}
