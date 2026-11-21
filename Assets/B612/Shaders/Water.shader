Shader "B612/Water"
{
    // Stylized shallow water for the natural planets' rivers and ponds.
    // Mesh data (TerrainWater.CreateNaturalMesh):
    //   UV0.xy flow-space metres: x across the river, y downstream along it (planar metres on a pond).
    //   UV1.x depth (m), UV1.y flow speed (m/s), UV1.z distance from the centre line 0..1, UV1.w 0 river / 1 pond.
    // Colour follows depth; two ripple layers and long streaks move downstream at the local speed; foam breaks
    // along the shallow banks; the main light adds a soft diffuse term, a specular glint and a sky fresnel rim.
    Properties
    {
        _ShallowColor("Shallow water", Color) = (0.3, 0.85, 0.85, 1)
        _DeepColor("Deep water", Color) = (0.06, 0.28, 0.5, 1)
        _DeepDepth("Depth for the deep colour (m)", Float) = 0.22
        _FoamColor("Foam", Color) = (0.92, 0.98, 1, 1)
        _FoamDepth("Foam below this depth (m)", Float) = 0.08
        _FoamStrength("Foam strength", Range(0, 1)) = 0.9
        _RippleScale("Ripples per metre", Float) = 1.8
        _RippleStrength("Ripple normal strength", Range(0, 2)) = 0.55
        _StreakStrength("Flow streak strength", Range(0, 1)) = 0.35
        _FlowSpeed("Flow speed multiplier", Float) = 1
        _PondDrift("Pond ripple drift (m/s)", Float) = 0.06
        _SkyColor("Sky reflection", Color) = (0.42, 0.55, 0.85, 1)
        _Smoothness("Glint sharpness", Range(0, 1)) = 0.86
        _Specular("Glint strength", Range(0, 2)) = 0.9
        [HideInInspector] _DebugTime("Fixed time for captures (-1 uses game time)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor, _DeepColor, _FoamColor, _SkyColor;
            float _DeepDepth, _FoamDepth, _FoamStrength, _RippleScale, _RippleStrength, _StreakStrength;
            float _FlowSpeed, _PondDrift, _Smoothness, _Specular, _DebugTime;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 flow : TEXCOORD0;
                float4 water : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 flow : TEXCOORD2;
                float4 water : TEXCOORD3;
                float fog : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.flow = input.flow;
                output.water = input.water;
                output.fog = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453) * 2 - 1;
            }

            // 2D gradient noise, about -0.7..0.7.
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                float a = dot(Hash2(i), f), b = dot(Hash2(i + float2(1, 0)), f - float2(1, 0));
                float c = dot(Hash2(i + float2(0, 1)), f - float2(0, 1)), d = dot(Hash2(i + 1), f - 1);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Ripple height in flow space: two layers moving downstream at different rates (or drifting on a pond).
            float Ripples(float2 p, float2 shiftA, float2 shiftB)
            {
                return Noise(p * _RippleScale + shiftA) * 0.65 + Noise(p * _RippleScale * 2.3 + shiftB) * 0.35;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Game time, so the water stops with the game's pause. Validation captures can pin it.
                float time = _DebugTime >= 0 ? _DebugTime : _Time.y;
                float depth = input.water.x;
                float speed = input.water.y * _FlowSpeed;
                bool pond = input.water.w > 0.5;
                float2 p = input.flow;

                float2 shiftA, shiftB;
                if (pond)
                {
                    shiftA = float2(time, time * 0.7) * _PondDrift * _RippleScale;
                    shiftB = float2(-time * 0.8, time) * _PondDrift * _RippleScale * 2.3;
                }
                else
                {
                    shiftA = float2(0, -time * speed) * _RippleScale;
                    shiftB = float2(0, -time * speed * 1.45) * _RippleScale * 2.3;
                }

                // Ripple slope by central differences in flow space (metres).
                const float e = 0.03;
                float hx = Ripples(p + float2(e, 0), shiftA, shiftB) - Ripples(p - float2(e, 0), shiftA, shiftB);
                float hy = Ripples(p + float2(0, e), shiftA, shiftB) - Ripples(p - float2(0, e), shiftA, shiftB);
                float2 slope = float2(hx, hy) / (2 * e) * (_RippleStrength / max(_RippleScale, 0.01));

                // Tangent frame from screen derivatives of the flow coordinates (no mesh tangents needed).
                float3 n = normalize(input.normalWS);
                float3 dp1 = ddx(input.positionWS), dp2 = ddy(input.positionWS);
                float2 duv1 = ddx(p), duv2 = ddy(p);
                float3 dp2perp = cross(dp2, n), dp1perp = cross(n, dp1);
                float3 tangent = dp2perp * duv1.x + dp1perp * duv2.x;
                float3 bitangent = dp2perp * duv1.y + dp1perp * duv2.y;
                float frame = rsqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 1e-12));
                float3 normal = normalize(n - (slope.x * tangent + slope.y * bitangent) * frame);

                // Long streaks stretched along the flow, sliding downstream; faster water shows more of them.
                float streak = 0;
                if (!pond)
                {
                    float s = Noise(float2(p.x * 5.0, (p.y - time * speed * 1.2) * 0.9));
                    streak = smoothstep(0.18, 0.5, s) * saturate(speed * 1.6) * _StreakStrength;
                }

                // Foam where the water thins out at the banks: solid at the waterline, breaking into moving
                // patches further out. A pond's floor is gentler, so its foam band uses a third of the depth.
                float foamDepth = max(_FoamDepth * (pond ? 0.35 : 1.0), 0.001);
                float shallow = 1 - smoothstep(0, foamDepth, depth);
                float2 foamFlow = pond ? p * 3.0 + float2(time * 0.2, -time * 0.15) : float2(p.x * 3.0, (p.y - time * speed) * 2.2);
                float foamNoise = Noise(foamFlow) * 0.5 + 0.5;
                float foam = saturate((shallow - (1 - foamNoise) * 0.8) * 2.5) * _FoamStrength;

                half3 albedo = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth / max(_DeepDepth, 0.001)));
                albedo = lerp(albedo, _FoamColor.rgb, saturate(streak * 0.5));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float wrap = saturate((dot(normal, light.direction) + 0.35) / 1.35);
                half3 lit = albedo * (SampleSH(normal) + light.color * wrap * light.shadowAttenuation);

                float3 halfway = normalize(light.direction + view);
                float power = exp2(4 + 10 * _Smoothness);
                float glint = pow(saturate(dot(normal, halfway)), power) * _Specular * light.shadowAttenuation;
                float fresnel = pow(1 - saturate(dot(normal, view)), 4) * 0.6;
                half3 color = lit + light.color * glint + _SkyColor.rgb * fresnel;
                color = lerp(color, _FoamColor.rgb * (SampleSH(n) + light.color * 0.6), foam);
                color = MixFog(color, input.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
}
