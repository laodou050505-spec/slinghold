// 弹弓攻城：积木着色器（手绘风）
// 贴图约定见 Tools/blender/ps_blocks.py：albedo = 裸木/裸石本色；mask.R = 漆面覆盖（石块是青苔），G = AO，B = 笔触明暗；normal = 切线空间法线。
// 漆色 _PaintColor 不烘进贴图，同一套贴图靠换材质拼出多种漆色的城（材质共用，SRP Batcher 合批）。
// 第二个 pass 是描边：外扩背面（inverted hull），沿“平滑法线”（导入时算好存在 UV3）在屏幕空间外扩固定像素宽，
// 颜色 = 这块积木自己的颜色压暗，不是统一的黑线。
Shader "PaperSiege/Block"
{
    Properties
    {
        [NoScaleOffset] _Albedo ("裸木/裸石本色 (sRGB)", 2D) = "white" {}
        [NoScaleOffset] _Mask ("遮罩 R漆面 G缝隙AO B笔触", 2D) = "white" {}
        [NoScaleOffset][Normal] _Normal ("法线", 2D) = "bump" {}
        _PaintColor ("漆色（石块：青苔色）", Color) = (0.36, 0.55, 0.52, 1)
        _NormalScale ("法线强度", Range(0, 2)) = 1
        _BrushStrength ("笔触明暗", Range(0, 1)) = 0.55
        _AOStrength ("缝隙 AO", Range(0, 1)) = 0.75
        _PaintCover ("漆面覆盖倍率", Range(0, 1.5)) = 1
        _BareTint ("裸木 / 裸石色调", Color) = (1, 1, 1, 1)
        [Header(Light)]
        _ShadowTint ("背光面色调", Color) = (0.72, 0.66, 0.76, 1)
        _Terminator ("明暗交界位置", Range(0, 1)) = 0.42
        _Softness ("明暗交界柔和度", Range(0.01, 1)) = 0.32
        _Ambient ("环境光强度", Range(0, 2)) = 1.1
        _Spec ("漆面微高光", Range(0, 1)) = 0.12
        [Header(Outline)]
        _OutlineWidth ("描边宽度（像素）", Range(0, 4)) = 1.3
        _OutlineDarken ("描边亮度（相对本色）", Range(0, 1)) = 0.36
        _OutlineSat ("描边饱和度", Range(0, 2)) = 1.35
        _OutlineMaxWorld ("描边最大世界宽度（米）", Range(0, 0.01)) = 0.0016
        _LineStrength ("棱线强度（mask.A）", Range(0, 1)) = 0.85
        [Header(State)]
        _Char ("烧焦", Range(0, 1)) = 0
        _Ember ("余烬", Range(0, 1)) = 0
        _Frost ("冰冻", Range(0, 1)) = 0
        _Flash ("受击闪白", Range(0, 1)) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
    TEXTURE2D(_Mask);   SAMPLER(sampler_Mask);
    TEXTURE2D(_Normal); SAMPLER(sampler_Normal);

    CBUFFER_START(UnityPerMaterial)
        half4 _PaintColor;
        half _NormalScale, _BrushStrength, _AOStrength, _PaintCover;
        half4 _BareTint;
        half4 _ShadowTint;
        half _Terminator, _Softness, _Ambient, _Spec;
        half _OutlineWidth, _OutlineDarken, _OutlineSat, _OutlineMaxWorld, _LineStrength;
        half _Char, _Ember, _Frost, _Flash;
    CBUFFER_END

    // 不受光的本色：漆色 × 笔触 / 裸木，乘 AO，再叠状态（烧焦、冰冻）
    half3 BaseColor(float2 uv, out half4 m)
    {
        m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, uv);
        half3 bare = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv).rgb * _BareTint.rgb;
        half brush = lerp(1.0h, (m.b - 0.5h) * 1.1h + 1.0h, _BrushStrength);
        half3 paint = _PaintColor.rgb * brush;
        half cover = saturate(m.r * _PaintCover);
        half3 c = lerp(bare, paint, cover);
        c *= lerp(1.0h, m.g, _AOStrength);
        // 烧焦：先变深褐再变炭黑，笔触纹理保留一点
        half3 charC = lerp(half3(0.16, 0.10, 0.07), half3(0.05, 0.045, 0.045), saturate(_Char * 1.6 - 0.6)) * (0.8 + 0.4 * m.b);
        c = lerp(c, charC, saturate(_Char));
        // 冰冻：偏冷蓝、提亮
        c = lerp(c, lerp(c, half3(0.70, 0.86, 0.95), 0.65) + 0.08, saturate(_Frost));
        return c;
    }

    // 描边色：本色压暗、提一点饱和（外轮廓描边和棱线共用）
    half3 LineColor(half3 c)
    {
        half l = dot(c, half3(0.299, 0.587, 0.114));
        return max(0, lerp(l.xxx, c, _OutlineSat)) * _OutlineDarken;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1;
                half3 n : TEXCOORD2; half4 t : TEXCOORD3; half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            V vert(A a)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
                o.pos = p.positionCS; o.wp = p.positionWS; o.uv = a.uv;
                o.n = nn.normalWS; o.t = half4(nn.tangentWS, a.t.w * GetOddNegativeScale());
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // 手绘风漫反射：包裹光照 + 柔和交界，背光面不是变黑而是染上 _ShadowTint
            half3 Painterly(half3 base, half3 N, half3 V, Light l, half atten)
            {
                half ndl = dot(N, l.direction) * 0.5h + 0.5h;
                half k = smoothstep(_Terminator - _Softness * 0.5h, _Terminator + _Softness * 0.5h, ndl) * atten;
                half3 lit = l.color * k;
                half3 H = normalize(l.direction + V);
                half sp = pow(saturate(dot(N, H)), 24.0h) * _Spec * k;
                return base * lit + sp * l.color * 0.5h;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 m; half3 base = BaseColor(i.uv, m);
                half lineK = m.a * _LineStrength;
                half3 nt = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal, sampler_Normal, i.uv), _NormalScale);
                half3 nw = normalize(i.n);
                half3 tw = normalize(i.t.xyz);
                half3 bw = cross(nw, tw) * i.t.w;
                half3 N = normalize(nt.x * tw + nt.y * bw + nt.z * nw);
                // 冰冻时法线压平一点（冰壳更光滑）
                N = normalize(lerp(N, nw, _Frost * 0.6h));
                half3 V = GetWorldSpaceNormalizeViewDir(i.wp);

                float4 sc = TransformWorldToShadowCoord(i.wp);
                Light ml = GetMainLight(sc);
                half3 col = Painterly(base, N, V, ml, ml.shadowAttenuation * ml.distanceAttenuation);
                // 背光 / 阴影里：环境光 × 色调，保证暗部有颜色、看得清
                half3 amb = SampleSH(N) * _Ambient;
                col += base * amb * lerp(_ShadowTint.rgb, 1.0h, 0.35h);

                #if defined(_ADDITIONAL_LIGHTS)
                uint cnt = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0; inputData.positionWS = i.wp; inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.pos);
                LIGHT_LOOP_BEGIN(cnt)
                    Light al = GetAdditionalLight(lightIndex, i.wp, half4(1, 1, 1, 1));
                    col += Painterly(base, N, V, al, al.distanceAttenuation * al.shadowAttenuation);
                LIGHT_LOOP_END
                #else
                for (uint li = 0u; li < cnt; ++li)
                {
                    Light al = GetAdditionalLight(li, i.wp, half4(1, 1, 1, 1));
                    col += Painterly(base, N, V, al, al.distanceAttenuation * al.shadowAttenuation);
                }
                #endif
                #endif

                // 冰冻：边缘一圈冷白（菲涅尔），像结了一层霜
                half fr = pow(1.0h - saturate(dot(N, V)), 3.0h);
                col += half3(0.75, 0.9, 1.0) * fr * _Frost * 0.45h;
                // 余烬：烧焦块缝隙里透出暗红（AO 低的地方更亮），随时间闪由脚本控制 _Ember
                col += half3(1.0, 0.36, 0.08) * _Ember * (1.0h - m.g) * 1.6h;
                // 棱线：画在受光之后，和外轮廓描边是同一种颜色（不受光照影响，像墨线）
                col = lerp(col, LineColor(base), lineK);
                col = lerp(col, half3(1, 0.97, 0.9), _Flash * 0.6h);
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // 描边：外扩背面。宽度按屏幕像素算（远近一样细），但限制最大世界宽度，凑到眼前的小块也不会描成粗线。
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            struct A { float4 pos : POSITION; float3 n : NORMAL; float3 sn : TEXCOORD3; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A a)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 sn = dot(a.sn, a.sn) > 0.01 ? a.sn : a.n;   // 没有平滑法线时退回普通法线
                float3 wp = TransformObjectToWorld(a.pos.xyz);
                float3 wn = normalize(TransformObjectToWorldNormal(sn));
                float4 cp = TransformWorldToHClip(wp);
                // 屏幕空间方向
                float3 cn = mul((float3x3)GetWorldToHClipMatrix(), wn);
                float2 dir = normalize(cn.xy + 1e-5);
                // 这个深度上一个像素对应多少米；描边宽度不超过 _OutlineMaxWorld
                float pxToWorld = 2.0 * cp.w / (_ScreenParams.y * abs(UNITY_MATRIX_P[1][1]));
                float wpx = min(_OutlineWidth, _OutlineMaxWorld / max(1e-7, pxToWorld));
                cp.xy += dir * (wpx * 2.0 / _ScreenParams.xy) * cp.w;
                o.pos = cp; o.uv = a.uv;
                o.fog = ComputeFogFactor(cp.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 m; half3 c = LineColor(BaseColor(i.uv, m));
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 pos : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a);
                float3 wp = TransformObjectToWorld(a.pos.xyz);
                float3 wn = TransformObjectToWorldNormal(a.n);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - wp);
                #else
                float3 ld = _LightDirection;
                #endif
                float4 cp = TransformWorldToHClip(ApplyShadowBias(wp, wn, ld));
                #if UNITY_REVERSED_Z
                cp.z = min(cp.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cp.z = max(cp.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.pos = cp; return o;
            }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(a.pos.xyz); return o; }
            half frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
