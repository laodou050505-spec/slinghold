// 光柱（结局天亮时从天花板开口斜照下来的阳光）：两片交叉的长条面，叠加发光。
// u = 横向（两边柔化），v = 0 在天花板、1 在地上（上亮下淡）；带一点缓慢流动的明暗，像光里有尘。
// 强度由 SkyDome 写 _Intensity（0 = 看不见）。
Shader "PaperSiege/LightShaft"
{
    Properties
    {
        _Color ("颜色", Color) = (1, 0.85, 0.6, 1)
        _Intensity ("强度", Range(0, 2)) = 0
        _Seed ("随机种子", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest LEqual
        Blend One One
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity, _Seed;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; float3 n : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wp = TransformObjectToWorld(a.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.uv = a.uv;
                o.n = TransformObjectToWorldNormal(a.n);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float u = i.uv.x, v = i.uv.y;
                float side = sin(u * 3.14159);
                side = side * side;                                       // 横向两边柔
                float along = saturate(v / 0.06) * pow(saturate(1 - v), 1.6) * (1 - 0.45 * v);   // 天花板处很快亮起，往下渐淡
                float t = _Time.y * 0.12 + _Seed;
                float flow = 0.75 + 0.25 * sin(u * 9.0 + t * 3.1) * sin(v * 5.0 - t * 2.0);   // 缓慢流动的明暗
                // 面越侧对眼睛越淡（交叉的两片在侧看时不会出现一道亮线）
                float3 vd = normalize(_WorldSpaceCameraPos - i.wp);
                float facing = abs(dot(normalize(i.n), vd));
                float k = side * along * flow * smoothstep(0.05, 0.5, facing) * _Intensity;
                return half4(_Color.rgb * k * 0.5, k * 0.35);
            }
            ENDHLSL
        }
    }
}
