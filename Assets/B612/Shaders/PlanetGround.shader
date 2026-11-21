Shader "B612/Planet Ground"
{
    Properties
    {
        _BaseColor("Planet color", Color) = (1, 1, 1, 1)
        _Paper("Paper albedo", 2D) = "gray" {}
        _PaperScale("Paper repeats per meter", Float) = 1.4
        _PaperContrast("Paper contrast", Range(0, 0.15)) = 0.05
        _Radius("Nominal radius", Float) = 12
        _HeightColorRange("Height color range (m)", Float) = 0.65
        _EarthColor("Earth", Color) = (0.83, 0.57, 0.42, 1)
        _VegetationColor("Vegetation", Color) = (0.57, 0.80, 0.49, 1)
        _RockColor("Rock", Color) = (0.69, 0.61, 0.76, 1)
        _PaletteStrength("Palette strength", Range(0, 1)) = 0.70
        _VegetationRemap("Vegetation remap", Vector) = (0.35, 0.63, 0, 0)
        [ToggleUI] _GroundTexturesEnabled("Grass and dirt textures", Float) = 0
        [NoScaleOffset] _DirtAlbedo("Dirt base color", 2D) = "white" {}
        [NoScaleOffset] _GrassAlbedo("Grass base color", 2D) = "white" {}
        [NoScaleOffset] [Normal] _DirtNormal("Dirt normal", 2D) = "bump" {}
        [NoScaleOffset] [Normal] _GrassNormal("Grass normal", 2D) = "bump" {}
        _DirtTileMeters("Dirt tile size (m)", Float) = 2
        _GrassTileMeters("Grass tile size (m)", Float) = 2
        _GroundTextureStrength("Base color detail strength", Range(0, 1)) = 0.5
        _DirtNormalStrength("Dirt normal strength", Range(0, 1)) = 0.2
        _GrassNormalStrength("Grass normal strength", Range(0, 1)) = 0.2
        _DirtAlbedoMean("Dirt mean linear RGB", Vector) = (1, 1, 1, 0)
        _GrassAlbedoMean("Grass mean linear RGB", Vector) = (1, 1, 1, 0)
        [HideInInspector] _DiagnosticUnlit("Diagnostic color", Float) = 0
        [HideInInspector] _DiagnosticLegacy("Diagnostic previous palette", Float) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.1
        _MoistureStrength("Moisture colour strength", Range(0, 1.5)) = 0
        _MoistureMean("Mean moisture of this planet", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_DirtAlbedo);
        SAMPLER(sampler_DirtAlbedo);
        TEXTURE2D(_GrassAlbedo);
        SAMPLER(sampler_GrassAlbedo);
        TEXTURE2D(_DirtNormal);
        SAMPLER(sampler_DirtNormal);
        TEXTURE2D(_GrassNormal);
        SAMPLER(sampler_GrassNormal);

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _EarthColor, _VegetationColor, _RockColor;
            float4 _VegetationRemap, _DirtAlbedoMean, _GrassAlbedoMean;
            float _PaperScale, _PaperContrast, _Radius, _HeightColorRange, _Smoothness;
            float _PaletteStrength, _DiagnosticUnlit, _DiagnosticLegacy;
            float _GroundTexturesEnabled, _DirtTileMeters, _GrassTileMeters, _GroundTextureStrength;
            float _DirtNormalStrength, _GrassNormalStrength;
            float _MoistureStrength, _MoistureMean;
        CBUFFER_END

        struct GroundSurface
        {
            half rock;
            half highGround;
            half meadow;
        };

        GroundSurface GetGroundSurface(float3 positionOS, float3 normalOS, half4 weights, half moisture)
        {
            GroundSurface surface;
            // Equivalent to a smooth transition from 18 to 46 degrees.
            surface.rock = 1 - smoothstep(0.694658, 0.951057, dot(normalOS, normalize(positionOS)));
            float height = length(positionOS) - _Radius;
            surface.highGround = smoothstep(-_HeightColorRange, _HeightColorRange, height);
            // The older height rule (high ground greener, basins bare) fades out as moisture takes over: water-fed
            // ground (drainage lines, hollows, lowland) turns greener and dry crests turn to bare earth. Moisture is
            // centred on the planet's mean so the overall grass share stays about the same.
            half heightRule = weights.r * 0.23 - weights.g * 0.35 + surface.highGround * 0.14;
            half fertility = weights.a * 0.82 + heightRule * (1 - saturate(_MoistureStrength))
                + _MoistureStrength * (moisture - _MoistureMean);
            surface.meadow = smoothstep(_VegetationRemap.x, _VegetationRemap.y, fertility);
            surface.meadow *= (1 - surface.rock) * (1 - weights.b * 0.9);
            return surface;
        }

        half3 GetProjectionWeights(float3 normalOS)
        {
            half3 blend = abs(normalOS);
            blend *= blend;
            return blend / dot(blend, half3(1, 1, 1));
        }

        struct GroundProjection
        {
            float2 x, y, z;
        };

        GroundProjection GetGroundProjection(float3 positionOS, float meters, float3 axisSign)
        {
            float3 p = positionOS / max(meters, 0.01);
            GroundProjection uv;
            uv.x = float2(p.y * axisSign.x, p.z);
            uv.y = float2(p.z * axisSign.y, p.x);
            uv.z = float2(p.x * axisSign.z, p.y);
            return uv;
        }

        float3 GetAxisSign(float3 normalOS)
        {
            // Flipping U on negative faces keeps each projection basis right-handed.
            return 2 * step(0, normalOS) - 1;
        }

        half3 SampleGroundColor(TEXTURE2D_PARAM(map, mapSampler), GroundProjection uv, half3 blend)
        {
            return SAMPLE_TEXTURE2D(map, mapSampler, uv.x).rgb * blend.x
                + SAMPLE_TEXTURE2D(map, mapSampler, uv.y).rgb * blend.y
                + SAMPLE_TEXTURE2D(map, mapSampler, uv.z).rgb * blend.z;
        }

        half3 GroundColorDetail(half3 sampleColor, float3 mean)
        {
            return lerp(half3(1, 1, 1), clamp(sampleColor / max(mean, 0.01), 0.35, 2.2), _GroundTextureStrength);
        }

        float2 UnpackSlope(half4 packedNormal)
        {
            float3 normalTS = UnpackNormal(packedNormal);
            return normalTS.xy / max(normalTS.z, 0.25);
        }

        float3 SampleGroundSlope(TEXTURE2D_PARAM(map, mapSampler), GroundProjection uv, half3 blend, float3 axisSign)
        {
            float2 x = UnpackSlope(SAMPLE_TEXTURE2D(map, mapSampler, uv.x));
            float2 y = UnpackSlope(SAMPLE_TEXTURE2D(map, mapSampler, uv.y));
            float2 z = UnpackSlope(SAMPLE_TEXTURE2D(map, mapSampler, uv.z));
            // Express the slopes along each projection's signed U and V axes in object space.
            return float3(0, x.x * axisSign.x, x.y) * blend.x
                + float3(y.y, 0, y.x * axisSign.y) * blend.y
                + float3(z.x * axisSign.z, z.y, 0) * blend.z;
        }

        bool GroundTexturesActive()
        {
            return _GroundTexturesEnabled > 0.5 && _DiagnosticLegacy <= 0.5 && _DiagnosticUnlit <= 0.5;
        }

        float3 GetGroundNormalOS(float3 positionOS, float3 normalOS, GroundSurface surface, half3 blend)
        {
            if (!GroundTexturesActive() || surface.rock >= 1 ||
                max(_DirtNormalStrength, _GrassNormalStrength) <= 0)
                return normalOS;

            float3 axisSign = GetAxisSign(normalOS);
            float3 dirt = SampleGroundSlope(TEXTURE2D_ARGS(_DirtNormal, sampler_DirtNormal),
                GetGroundProjection(positionOS, _DirtTileMeters, axisSign), blend, axisSign) * _DirtNormalStrength;
            float3 grass = SampleGroundSlope(TEXTURE2D_ARGS(_GrassNormal, sampler_GrassNormal),
                GetGroundProjection(positionOS, _GrassTileMeters, axisSign), blend, axisSign) * _GrassNormalStrength;
            float3 perturbation = lerp(dirt, grass, surface.meadow) * (1 - surface.rock);
            // Project onto the mesh tangent plane so a flat map leaves the geometric normal intact.
            perturbation -= normalOS * dot(perturbation, normalOS);
            return normalize(normalOS + perturbation);
        }

        struct GroundAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 surfaceWeights : COLOR;
            float2 moisture : TEXCOORD1;
        };
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Paper);
            SAMPLER(sampler_Paper);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalOS : TEXCOORD2;
                half4 weights : TEXCOORD3;
                half4 fogAndVertexLight : TEXCOORD4;
                half moisture : TEXCOORD5;
            };

            Varyings Vert(GroundAttributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.weights = input.surfaceWeights;
                output.moisture = input.moisture.x;
                output.fogAndVertexLight = half4(ComputeFogFactor(positions.positionCS.z),
                    VertexLighting(positions.positionWS, TransformObjectToWorldNormal(input.normalOS)));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalOS = normalize(input.normalOS);
                GroundSurface ground = GetGroundSurface(input.positionOS, normalOS, input.weights, input.moisture);
                half3 blend = GetProjectionWeights(normalOS);
                bool useColorDetail = GroundTexturesActive() && _GroundTextureStrength > 0;
                half3 albedo;
                if (_DiagnosticLegacy > 0.5)
                {
                    half highGround = smoothstep(-0.65, 0.65, length(input.positionOS) - _Radius);
                    half vegetation = saturate(0.24 + input.weights.r * 0.32 - input.weights.g * 0.16 + highGround * 0.22);
                    half3 earth = _BaseColor.rgb * half3(0.92, 0.84, 0.76);
                    half3 grass = _BaseColor.rgb * half3(0.84, 1.04, 0.88);
                    half3 stone = _BaseColor.rgb * half3(0.77, 0.78, 0.84);
                    albedo = lerp(lerp(earth, grass, vegetation), stone, ground.rock * 0.78);
                    albedo = lerp(albedo, earth * 0.78, input.weights.b * 0.42);
                    albedo *= lerp(0.90, 1.08, input.weights.a) * lerp(0.94, 1.06, highGround);
                }
                else
                {
                    half3 earth = lerp(_BaseColor.rgb, _EarthColor.rgb, _PaletteStrength);
                    half3 grass = lerp(_BaseColor.rgb, _VegetationColor.rgb, _PaletteStrength);
                    half3 stone = lerp(_BaseColor.rgb, _RockColor.rgb, _PaletteStrength);
                    if (useColorDetail && ground.rock < 1)
                    {
                        float3 axisSign = GetAxisSign(normalOS);
                        half3 dirtSample = SampleGroundColor(TEXTURE2D_ARGS(_DirtAlbedo, sampler_DirtAlbedo),
                            GetGroundProjection(input.positionOS, _DirtTileMeters, axisSign), blend);
                        half3 grassSample = SampleGroundColor(TEXTURE2D_ARGS(_GrassAlbedo, sampler_GrassAlbedo),
                            GetGroundProjection(input.positionOS, _GrassTileMeters, axisSign), blend);
                        earth *= GroundColorDetail(dirtSample, _DirtAlbedoMean.rgb);
                        grass *= GroundColorDetail(grassSample, _GrassAlbedoMean.rgb);
                    }
                    albedo = lerp(lerp(earth, grass, ground.meadow), stone, ground.rock);
                    albedo *= lerp(0.88, 1.08, ground.highGround);
                }
                if (_DiagnosticUnlit > 0.5) return half4(albedo, 1);

                float3 samplePosition = input.positionOS * _PaperScale;
                half3 paper = half3(SAMPLE_TEXTURE2D(_Paper, sampler_Paper, samplePosition.yz).r,
                    SAMPLE_TEXTURE2D(_Paper, sampler_Paper, samplePosition.zx).r,
                    SAMPLE_TEXTURE2D(_Paper, sampler_Paper, samplePosition.xy).r);
                float cameraDistance = distance(input.positionWS, GetCameraPositionWS());
                half paperContrast = _PaperContrast * (useColorDetail ? 1 - 0.55 * _GroundTextureStrength : 1);
                albedo *= 1 + (dot(paper, blend) - 0.5) * paperContrast * (1 - smoothstep(5, 16, cameraDistance));

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = TransformObjectToWorldNormal(GetGroundNormalOS(input.positionOS, normalOS, ground, blend));
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, input.fogAndVertexLight.x);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
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

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                half3 normalOS : TEXCOORD1;
                half4 weights : TEXCOORD2;
                half moisture : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings DepthNormalsVertex(GroundAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.weights = input.surfaceWeights;
                output.moisture = input.moisture.x;
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalOS = normalize(input.normalOS);
                GroundSurface ground = GetGroundSurface(input.positionOS, normalOS, input.weights, input.moisture);
                float3 normalWS = TransformObjectToWorldNormal(
                    GetGroundNormalOS(input.positionOS, normalOS, ground, GetProjectionWeights(normalOS)));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = saturate(PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5);
                    return half4(PackFloat2To888(octNormal), 0);
                #else
                    return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
