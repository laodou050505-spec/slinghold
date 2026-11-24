// 检查用：正面灰色带简单光照，背面染成纯红。模型有法线反 / 破洞时一眼能看出来。
Shader "PaperSiege/BackfaceCheck"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };
            V vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                if (!front) return half4(1, 0, 0, 1);
                half k = dot(normalize(i.n), normalize(half3(0.4, 0.8, -0.45))) * 0.4 + 0.6;
                return half4(k.xxx * 0.85, 1);
            }
            ENDHLSL
        }
    }
}
