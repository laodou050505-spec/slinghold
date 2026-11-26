// 天上的粒子（烟花）：和天空穹顶同一条规则——从眼睛到这个像素连一条视线，它穿过天花板平面的那一点必须落在“盒盖开口”里，
// 而且这个像素本身在天花板以上，才画出来。头显里真实天花板是透视画面、挡不住虚拟物体，
// 11-26 合成真实房间时看到：开口外的火星直接画在了真实天花板 / 墙上，像在屋里炸开。开口参数由 SkyDome 写成全局变量。
Shader "PaperSiege/SkyParticle"
{
    Properties
    {
        _BaseMap ("贴图（圆形柔点）", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest LEqual
        Blend SrcAlpha One   // 叠加：烟花发光
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
            CBUFFER_END
            float4 _SkyLidCenter, _SkyLidRight, _SkyLidFwd, _SkyLidHalf;   // 全局（SkyDome.BuildLid 写入）

            struct A { float4 pos : POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wp = TransformObjectToWorld(a.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.col = a.col; o.uv = TRANSFORM_TEX(a.uv, _BaseMap);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 eye = _WorldSpaceCameraPos;
                float ceil = _SkyLidCenter.y;
                if (i.wp.y < ceil + 0.02) discard;                     // 掉到天花板以下：不画
                float3 vd = i.wp - eye;
                if (vd.y <= 0.001) discard;
                float3 hp = eye + vd * ((ceil - eye.y) / vd.y) - _SkyLidCenter.xyz;
                float2 lp = float2(dot(hp, _SkyLidRight.xyz), dot(hp, _SkyLidFwd.xyz));
                float2 edge = _SkyLidHalf.xy - abs(lp);
                float mask = saturate(min(edge.x, edge.y) / 0.03);
                if (mask <= 0) discard;
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * i.col;
                c.a *= mask;
                return c;
            }
            ENDHLSL
        }
    }
}
