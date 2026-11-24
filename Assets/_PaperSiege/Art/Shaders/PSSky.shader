// 程序天空穹顶（手绘感）：天顶 → 地平线渐变、太阳 / 月亮（柔边 + 光晕）、手绘云层（分层噪声 + 硬一点的边 + 底部压暗）、星星（入夜后）。
// 画在一个包住玩家的大球内侧，深度最远，透视画面之前先画；只在“盒盖掀开”的那片天花板区域里看得到（盒盖木板挡住其余部分）。
// 时段参数由 SkyDome.cs 插值后写进来。
Shader "PaperSiege/Sky"
{
    Properties
    {
        _Zenith ("天顶色", Color) = (0.42, 0.62, 0.86, 1)
        _Horizon ("地平线色", Color) = (0.93, 0.86, 0.72, 1)
        _SunColor ("太阳色", Color) = (1, 0.92, 0.7, 1)
        _SunDir ("太阳方向", Vector) = (0.3, 0.5, 0.8, 0)
        _SunSize ("太阳大小", Range(0.005, 0.2)) = 0.045
        _CloudColor ("云色", Color) = (1, 0.98, 0.95, 1)
        _CloudShade ("云底色", Color) = (0.75, 0.72, 0.78, 1)
        _CloudCover ("云量", Range(0, 1)) = 0.45
        _Stars ("星星", Range(0, 1)) = 0
        _Moon ("月亮（1 = 太阳画成月亮）", Range(0, 1)) = 0
        _Swirl ("敌城上空的云打转", Range(0, 1)) = 0
        _SwirlDir ("打转中心方向", Vector) = (0, 0.3, 1, 0)
        _Fade ("整体透明度", Range(0, 1)) = 1
        [Header(Lid mask)]
        _LidCenter ("开口中心（世界，天花板高度）", Vector) = (0, 2.5, 1.6, 0)
        _LidRight ("开口横向（单位向量）", Vector) = (1, 0, 0, 0)
        _LidFwd ("开口纵向（单位向量）", Vector) = (0, 0, 1, 0)
        _LidHalf ("开口半宽 / 半长（米）", Vector) = (1.2, 2.2, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-300" "RenderPipeline" = "UniversalPipeline" }
        Cull Front ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _SunColor, _CloudColor, _CloudShade;
                float4 _SunDir, _SwirlDir, _LidCenter, _LidRight, _LidFwd, _LidHalf;
                half _SunSize, _CloudCover, _Stars, _Moon, _Swirl, _Fade;
            CBUFFER_END

            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; float3 wp : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(a.pos.xyz);
                o.dir = TransformObjectToWorldDir(a.pos.xyz);
                o.wp = TransformObjectToWorld(a.pos.xyz);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p) { float v = 0, a = 0.5; for (int k = 0; k < 5; k++) { v += a * noise(p); p *= 2.03; a *= 0.5; } return v; }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // 只在“盒盖开口”里画天空：从眼睛沿视线打到天花板平面，落点在开口矩形里才算天空，其余透明（露出真实房间）
                float3 eye = _WorldSpaceCameraPos;
                float3 vd = normalize(i.wp - eye);
                if (vd.y <= 0.02) discard;
                float tc = (_LidCenter.y - eye.y) / vd.y;
                float3 hp = eye + vd * tc - _LidCenter.xyz;
                float2 lp = float2(dot(hp, _LidRight.xyz), dot(hp, _LidFwd.xyz));
                float2 edge = _LidHalf.xy - abs(lp);
                float mask = saturate(min(edge.x, edge.y) / 0.03);   // 边缘 3 cm 柔一下
                if (mask <= 0) discard;
                float3 d = normalize(i.dir);
                float up = saturate(d.y);
                // 渐变：地平线附近暖，往上过渡到天顶色（曲线偏向地平线）
                half3 col = lerp(_Horizon.rgb, _Zenith.rgb, pow(up, 0.55));
                // 太阳 / 月亮
                float3 sd = normalize(_SunDir.xyz);
                float c = dot(d, sd);
                float disc = smoothstep(cos(_SunSize * 1.08), cos(_SunSize * 0.92), c);
                float glow = pow(saturate(c), 24.0) * 0.35 + pow(saturate(c), 4.0) * 0.12;
                // 月牙：再减一个偏一点的圆
                float3 sd2 = normalize(sd + float3(0.03, 0.02, 0));
                float bite = smoothstep(cos(_SunSize * 1.0), cos(_SunSize * 0.85), dot(d, sd2)) * _Moon;
                col = lerp(col, _SunColor.rgb, saturate(disc - bite));
                col += _SunColor.rgb * glow * (1 - _Moon * 0.6);
                // 星星（只在天上半球、云少的地方）
                float2 sp = d.xz / (d.y + 0.35) * 60;
                float st = step(0.9965, hash(floor(sp))) * smoothstep(0.5, 0.15, length(frac(sp) - 0.5)) * smoothstep(0.05, 0.3, up);
                // 云：天空平面上的分层噪声，手绘感 = 边缘偏硬 + 两层颜色（亮面 / 阴面）
                float2 cp = d.xz / (d.y + 0.18) * 1.6;
                // 打转：靠近 _SwirlDir 的地方把采样坐标绕中心转
                float3 sw = normalize(_SwirlDir.xyz);
                float2 swc = sw.xz / (sw.y + 0.18) * 1.6;
                float2 rel = cp - swc;
                float r = length(rel);
                float ang = _Swirl * 3.0 * exp(-r * 1.2) + _Time.y * 0.6 * _Swirl * exp(-r * 1.5);
                float cs = cos(ang), sn = sin(ang);
                cp = swc + float2(rel.x * cs - rel.y * sn, rel.x * sn + rel.y * cs);
                cp += _Time.y * float2(0.012, 0.004);
                float n = fbm(cp);
                float cover = 1.0 - _CloudCover;
                float cl = smoothstep(cover, cover + 0.08, n) * smoothstep(0.0, 0.18, up);
                float shade = smoothstep(cover + 0.02, cover + 0.22, fbm(cp + float2(0.07, 0.05)));
                half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, shade);
                col = lerp(col + st * _Stars * (1 - cl), cloud, cl * 0.92);
                return half4(col, _Fade * mask);
            }
            ENDHLSL
        }
    }
}
