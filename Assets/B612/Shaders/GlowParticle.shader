// Additive soft dot for fireflies and stardust. No texture: the falloff is computed from the quad UVs.
Shader "B612/Glow Particle"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 3
        _Softness ("Softness", Range(0.5, 8)) = 2.5
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Tint;
            float _Intensity, _Softness;

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0);
                float core = pow(saturate(1.0 - r), _Softness);
                core += exp(-r * r * 60.0) * 0.8;                       // hot centre
                return half4(i.color.rgb * _Tint.rgb * core * _Intensity * i.color.a, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
