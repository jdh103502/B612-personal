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
        [HideInInspector] _DiagnosticUnlit("Diagnostic color", Float) = 0
        [HideInInspector] _DiagnosticLegacy("Diagnostic previous palette", Float) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _EarthColor, _VegetationColor, _RockColor;
            float4 _VegetationRemap;
            float _PaperScale, _PaperContrast, _Radius, _HeightColorRange, _Smoothness;
            float _PaletteStrength, _DiagnosticUnlit, _DiagnosticLegacy;
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Paper);
            SAMPLER(sampler_Paper);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 surfaceWeights : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalOS : TEXCOORD2;
                half4 weights : TEXCOORD3;
                half4 fogAndVertexLight : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.weights = input.surfaceWeights;
                output.fogAndVertexLight = half4(ComputeFogFactor(positions.positionCS.z),
                    VertexLighting(positions.positionWS, TransformObjectToWorldNormal(input.normalOS)));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 radial = normalize(input.positionOS);
                float3 normalOS = normalize(input.normalOS);
                float height = length(input.positionOS) - _Radius;
                // Equivalent to a smooth transition from 18 to 46 degrees.
                half rock = 1 - smoothstep(0.694658, 0.951057, dot(normalOS, radial));
                half highGround = smoothstep(-_HeightColorRange, _HeightColorRange, height);
                half3 albedo;
                if (_DiagnosticLegacy > 0.5)
                {
                    highGround = smoothstep(-0.65, 0.65, height);
                    half vegetation = saturate(0.24 + input.weights.r * 0.32 - input.weights.g * 0.16 + highGround * 0.22);
                    half3 earth = _BaseColor.rgb * half3(0.92, 0.84, 0.76);
                    half3 grass = _BaseColor.rgb * half3(0.84, 1.04, 0.88);
                    half3 stone = _BaseColor.rgb * half3(0.77, 0.78, 0.84);
                    albedo = lerp(lerp(earth, grass, vegetation), stone, rock * 0.78);
                    albedo = lerp(albedo, earth * 0.78, input.weights.b * 0.42);
                    albedo *= lerp(0.90, 1.08, input.weights.a) * lerp(0.94, 1.06, highGround);
                }
                else
                {
                    half fertility = input.weights.a * 0.82 + input.weights.r * 0.23
                        - input.weights.g * 0.35 + highGround * 0.14;
                    half meadow = smoothstep(_VegetationRemap.x, _VegetationRemap.y, fertility);
                    meadow *= (1 - rock) * (1 - input.weights.b * 0.9);
                    half3 earth = lerp(_BaseColor.rgb, _EarthColor.rgb, _PaletteStrength);
                    half3 grass = lerp(_BaseColor.rgb, _VegetationColor.rgb, _PaletteStrength);
                    half3 stone = lerp(_BaseColor.rgb, _RockColor.rgb, _PaletteStrength);
                    albedo = lerp(lerp(earth, grass, meadow), stone, rock);
                    albedo *= lerp(0.88, 1.08, highGround);
                }
                if (_DiagnosticUnlit > 0.5) return half4(albedo, 1);

                float3 samplePosition = input.positionOS * _PaperScale;
                half3 blend = abs(normalOS);
                blend *= blend;
                blend /= dot(blend, half3(1, 1, 1));
                half3 paper = half3(SAMPLE_TEXTURE2D(_Paper, sampler_Paper, samplePosition.yz).r,
                    SAMPLE_TEXTURE2D(_Paper, sampler_Paper, samplePosition.zx).r,
                    SAMPLE_TEXTURE2D(_Paper, sampler_Paper, samplePosition.xy).r);
                float cameraDistance = distance(input.positionWS, GetCameraPositionWS());
                albedo *= 1 + (dot(paper, blend) - 0.5) * _PaperContrast * (1 - smoothstep(5, 16, cameraDistance));

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = TransformObjectToWorldNormal(normalOS);
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
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
