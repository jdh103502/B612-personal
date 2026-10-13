// Procedural night sky for B-612: gradient, Milky Way with dust lanes, nebula wisps,
// three layers of twinkling stars (the brightest get storybook sparkles), a shaded
// crescent moon with earthshine and halo, and shooting stars driven by NightSky.cs.
Shader "B612/Night Sky"
{
    Properties
    {
        [NoScaleOffset] _Noise3D ("Noise (3D, tiling)", 3D) = "" {}

        [Header(Gradient)]
        _ZenithColor ("Zenith", Color) = (0.010, 0.016, 0.060, 1)
        _MidColor ("Upper Sky", Color) = (0.022, 0.034, 0.120, 1)
        _HorizonColor ("Horizon", Color) = (0.060, 0.050, 0.170, 1)
        _NadirColor ("Below", Color) = (0.008, 0.012, 0.040, 1)
        _GlowColor ("Horizon Glow", Color) = (0.90, 0.42, 0.40, 1)
        _GlowDir ("Glow Direction", Vector) = (0.6, 0, -0.8, 0)
        _GlowStrength ("Glow Strength", Range(0, 3)) = 0.22
        _SkyUp ("Sky Up", Vector) = (0, 1, 0, 0)

        [Header(Stars)]
        _StarBrightness ("Brightness", Range(0, 20)) = 6
        _StarDensity ("Density", Range(0, 2)) = 1
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 2.5
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.55
        _SkyRotation ("Rotation (deg per sec)", Float) = 0.05
        _PoleDir ("Rotation Pole", Vector) = (0.2, 1, 0.35, 0)

        [Header(Milky Way)]
        _MilkyNormal ("Band Normal", Vector) = (0.35, 0.5, -0.8, 0)
        _MilkyCore ("Core Direction", Vector) = (0.9, 0.1, 0.4, 0)
        _MilkyWidth ("Width", Range(0.02, 0.6)) = 0.055
        _MilkyIntensity ("Intensity", Range(0, 4)) = 0.5
        _MilkyCoreColor ("Core Color", Color) = (1.00, 0.76, 0.58, 1)
        _MilkyArmColor ("Arm Color", Color) = (0.42, 0.52, 1.00, 1)
        _DustStrength ("Dust Lanes", Range(0, 1)) = 0.8

        [Header(Nebula)]
        _NebulaA ("Nebula A", Color) = (0.85, 0.25, 0.62, 1)
        _NebulaB ("Nebula B", Color) = (0.12, 0.52, 0.85, 1)
        _NebulaIntensity ("Intensity", Range(0, 2)) = 0.12

        [Header(Moon)]
        _MoonDir ("Moon Direction", Vector) = (0.5, 0.45, 0.75, 0)
        _MoonColor ("Moon Color", Color) = (1.00, 0.94, 0.80, 1)
        _MoonSize ("Moon Radius (rad)", Range(0.01, 0.2)) = 0.042
        _MoonPhase ("Phase (0 full, 1 new)", Range(0, 1)) = 0.68
        _MoonIntensity ("Intensity", Range(0, 10)) = 3
        _MoonHalo ("Halo", Range(0, 3)) = 0.8

        [Header(Shooting Stars)]
        _MeteorColor ("Color", Color) = (0.80, 0.90, 1.00, 1)
        _MeteorIntensity ("Intensity", Range(0, 20)) = 5
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_Noise3D); SAMPLER(sampler_Noise3D);

            float4 _ZenithColor, _MidColor, _HorizonColor, _NadirColor, _GlowColor, _GlowDir, _SkyUp;
            float _GlowStrength;
            float _StarBrightness, _StarDensity, _TwinkleSpeed, _TwinkleAmount, _SkyRotation;
            float4 _PoleDir;
            float4 _MilkyNormal, _MilkyCore, _MilkyCoreColor, _MilkyArmColor;
            float _MilkyWidth, _MilkyIntensity, _DustStrength;
            float4 _NebulaA, _NebulaB;
            float _NebulaIntensity;
            float4 _MoonDir, _MoonColor;
            float _MoonSize, _MoonPhase, _MoonIntensity, _MoonHalo;
            float4 _MeteorColor;
            float _MeteorIntensity;

            // Set globally by NightSky.cs: xyz = start direction / travel direction, w = progress (0..1) / arc length (rad).
            float4 _MeteorA, _MeteorADir, _MeteorB, _MeteorBDir;
            float _NightSkyTime;   // seconds of play, 0 in edit mode, so the composition stays put while editing

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            // ---------------------------------------------------------------- noise
            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float Noise(float3 p) { return SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, p * 0.0625, 0).r; }
            float Noise2(float3 p) { return SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, p * 0.0625, 0).g; }

            float Fbm(float3 p, int octaves)
            {
                float s = 0.0, a = 0.5, n = 0.0;
                for (int i = 0; i < octaves; i++)
                {
                    s += a * Noise(p); n += a;
                    p = p * 2.03 + float3(1.7, 9.2, 5.3);
                    a *= 0.5;
                }
                return s / n;
            }

            // stretch a value-noise fbm (which clusters around 0.5) back out to roughly 0..1
            float Spread(float v, float k) { return saturate((v - 0.5) * k + 0.5); }

            float3 RotateAxis(float3 v, float3 k, float a)
            {
                float c = cos(a), s = sin(a);
                return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c);
            }

            // ---------------------------------------------------------------- stars
            float2 CubeUV(float3 d, out float face)
            {
                float3 a = abs(d);
                float2 uv;
                if (a.x >= a.y && a.x >= a.z) { face = d.x > 0 ? 0 : 1; uv = d.yz / a.x; }
                else if (a.y >= a.z)          { face = d.y > 0 ? 2 : 3; uv = d.xz / a.y; }
                else                          { face = d.z > 0 ? 4 : 5; uv = d.xy / a.z; }
                return atan(uv) * (2.0 / PI) + 0.5;   // equi-angular, 0..1 per face
            }

            float3 StarColor(float t)
            {
                float3 c = lerp(float3(0.60, 0.72, 1.00), float3(1.00, 1.00, 1.00), smoothstep(0.00, 0.35, t));
                c = lerp(c, float3(1.00, 0.88, 0.66), smoothstep(0.55, 0.80, t));
                return lerp(c, float3(1.00, 0.64, 0.42), smoothstep(0.88, 1.00, t));
            }

            // One star per grid cell (or none). Stars live in the middle of their cell so nothing gets clipped.
            float3 StarLayer(float2 uv, float face, float cells, float density, float seed, float px,
                             float magPow, float bright, float sizePx, float sparkle)
            {
                float2 g = uv * cells;
                float2 id = floor(g);
                float2 f = g - id;
                float3 h = Hash33(float3(id, face * 37.0 + seed));
                if (h.z > density) return 0;
                float3 h2 = Hash33(float3(id.yx + 13.7, face * 91.0 + seed * 1.37));
                float2 dv = f - (0.25 + 0.5 * h.xy);
                float pc = max(px * cells * 0.6366, 1e-5);              // one pixel, in cell units
                float mag = pow(h2.x, magPow);
                float r = pc * sizePx * (0.6 + 0.9 * mag);
                float d2 = dot(dv, dv);
                float edge = saturate(1.0 - max(abs(dv.x), abs(dv.y)) * 4.0);
                edge *= edge;

                float tw = sin(_Time.y * _TwinkleSpeed * (0.5 + 1.8 * h2.y) + h2.z * 50.0);
                tw = 1.0 - _TwinkleAmount * (0.5 + 0.5 * tw) * (0.35 + 0.65 * h2.z);
                float b = (0.06 + mag) * bright * tw;

                float star = exp(-d2 / (r * r));
                if (sparkle > 0.0)
                {
                    float L = min(pc * 9.0, 0.07);
                    float2 a = abs(dv);
                    float spikes = exp(-a.x / (pc * 0.9) - a.y / L) + exp(-a.y / (pc * 0.9) - a.x / L);
                    float halo = exp(-sqrt(d2) / min(pc * 4.0, 0.04));
                    star += (spikes * 0.7 * smoothstep(0.35, 0.9, mag) + halo * 0.10) * sparkle * (0.7 + 0.3 * tw);
                }
                return StarColor(frac(h2.y * 7.31 + h2.z)) * star * b * edge;
            }

            // ---------------------------------------------------------------- shooting stars
            float Meteor(float3 d, float4 m, float4 md, float px)
            {
                if (m.w <= 0.0 || m.w >= 1.0) return 0.0;
                float3 p0 = normalize(m.xyz);
                float3 t = normalize(md.xyz - p0 * dot(md.xyz, p0));
                float3 b = cross(p0, t);
                float fx = dot(d, p0);
                if (fx < 0.5) return 0.0;
                float x = atan2(dot(d, t), fx);
                float y = dot(d, b);
                float prog = m.w;
                float along = prog * md.w - x;                         // > 0 behind the head
                float tail = md.w * 0.5 * smoothstep(0.0, 0.35, prog);
                float k = saturate(1.0 - along / max(tail, 1e-4)) * step(0.0, along);
                float w = max(px, 1e-5) * (0.5 + 0.9 * k);
                float lineV = exp(-(y * y) / (w * w)) * k * k;
                float headV = exp(-(along * along + y * y) / (px * px * 4.0 + 1e-10));
                float fade = smoothstep(0.0, 0.1, prog) * (1.0 - smoothstep(0.6, 1.0, prog));
                return (lineV + headV * 1.8) * fade;
            }

            // ---------------------------------------------------------------- main
            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float px = length(fwidth(d));                          // angular size of a pixel
                float3 up = normalize(_SkyUp.xyz);
                float h = dot(d, up);

                // gradient: deep zenith, violet horizon band, dark below
                float3 col = lerp(_HorizonColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.32, h));
                col = lerp(col, _ZenithColor.rgb, smoothstep(0.30, 1.0, h));
                col = lerp(col, _NadirColor.rgb, smoothstep(0.0, 0.75, -h));
                float3 hz = d - up * h;
                float3 gd = _GlowDir.xyz - up * dot(_GlowDir.xyz, up);
                float az = dot(hz, gd) * rsqrt(max(dot(hz, hz) * dot(gd, gd), 1e-6)) * 0.5 + 0.5;
                col += _GlowColor.rgb * exp(-abs(h + 0.02) * 7.0) * (0.15 + 0.85 * az * az) * _GlowStrength;

                // the celestial sphere turns slowly
                float3 sd = RotateAxis(d, normalize(_PoleDir.xyz), _NightSkyTime * _SkyRotation * (PI / 180.0));

                // Milky Way
                float3 mn = normalize(_MilkyNormal.xyz);
                float3 mc = _MilkyCore.xyz - mn * dot(_MilkyCore.xyz, mn);
                mc = normalize(mc);
                float core = pow(saturate(dot(sd, mc)), 60.0);
                float warp = (Fbm(sd * 5.0 + 3.0, 4) - 0.5) * 0.16;
                float bpos = dot(sd, mn) + warp * (0.6 + 0.4 * core);
                float w = _MilkyWidth * (1.0 + 0.8 * core);
                float band = exp(-(bpos * bpos) / (w * w));
                float clouds = Spread(Fbm(sd * 10.0 + 11.0, 5), 2.2);
                float detail = Spread(Fbm(sd * 28.0 + 29.0, 4), 2.0);
                float mw = band * (0.25 + 0.75 * clouds) * (0.55 + 0.45 * detail);
                mw += exp(-(bpos * bpos) / (w * w * 0.12)) * core * 0.5;   // bright bulge
                mw += exp(-(bpos * bpos) / (w * w * 6.0)) * 0.16;          // faint wide haze
                float ridge = 1.0 - abs(Spread(Fbm(sd * 14.0 + 1.7, 5), 2.4) * 2.0 - 1.0);
                float lanes = smoothstep(0.55, 0.92, ridge) * exp(-(bpos * bpos) / (w * w * 0.35));
                float rift = exp(-pow((bpos + (Fbm(sd * 7.0 + 5.0, 3) - 0.5) * 0.05) / (w * 0.16), 2.0)) * (0.4 + 0.6 * core);
                float dust = saturate(lanes * 0.85 + rift * 0.75) * _DustStrength;
                float3 mwCol = lerp(_MilkyArmColor.rgb, _MilkyCoreColor.rgb, saturate(core * 1.3 + band * 0.15));
                float hii = smoothstep(0.62, 0.85, Spread(Fbm(sd * 20.0 + 47.0, 3), 2.5)) * band;
                col += (mwCol * mw * (0.4 + 1.0 * core) + _NebulaA.rgb * hii * 0.35) * (1.0 - dust) * _MilkyIntensity;

                // nebula wisps away from the band
                float n1 = Spread(Fbm(sd * 3.0 + 71.0, 4), 2.3);
                float n2 = Spread(Fbm(sd * 8.0 + 23.0, 4), 2.0);
                float neb = smoothstep(0.55, 0.98, n1) * (0.3 + 0.7 * n2);
                float3 nebCol = lerp(_NebulaB.rgb, _NebulaA.rgb, smoothstep(0.35, 0.7, Noise2(sd * 1.3 + 3.0)));
                col += nebCol * neb * neb * _NebulaIntensity;

                // stars, denser inside the Milky Way
                float face;
                float2 uv = CubeUV(sd, face);
                float dens = _StarDensity;
                col += StarLayer(uv, face, 170.0, saturate(0.45 * dens + band * 0.5), 1.0, px, 7.0, _StarBrightness * 0.22, 0.75, 0.0);
                col += StarLayer(uv, face, 72.0, saturate(0.32 * dens + band * 0.25), 2.0, px, 5.0, _StarBrightness * 0.6, 0.95, 0.0);
                col += StarLayer(uv, face, 24.0, saturate(0.30 * dens), 3.0, px, 2.5, _StarBrightness, 1.15, 1.0);

                // shooting stars
                col += _MeteorColor.rgb * _MeteorIntensity *
                       (Meteor(d, _MeteorA, _MeteorADir, px) + Meteor(d, _MeteorB, _MeteorBDir, px));

                // moon: a lit sphere with earthshine on its dark side, plus a soft halo
                float3 md = normalize(_MoonDir.xyz);
                float cm = dot(d, md);
                if (cm > 0.0)
                {
                    float3 ref = abs(dot(up, md)) > 0.98 ? float3(1, 0, 0) : up;
                    float3 mx = normalize(cross(ref, md));
                    float3 my = cross(md, mx);
                    float2 mp = float2(dot(d, mx), dot(d, my)) / _MoonSize;
                    float rr = length(mp);
                    float th = _MoonPhase * PI;
                    float litFrac = 0.5 + 0.5 * cos(th);
                    float g = max(rr - 1.0, 0.0);
                    float halo = exp(-g * 2.2) * 0.22 + exp(-g * 0.3) * 0.045;
                    col += _MoonColor.rgb * halo * _MoonHalo * (0.2 + 0.8 * litFrac) * smoothstep(0.0, 0.3, cm);
                    if (rr < 1.2)
                    {
                        float edge = px / _MoonSize * 1.2;
                        float disk = 1.0 - smoothstep(1.0 - edge, 1.0 + edge, rr);
                        float3 n = float3(mp, sqrt(saturate(1.0 - rr * rr)));
                        float3 L = normalize(float3(sin(th), 0.22, cos(th)));
                        float lit = smoothstep(-0.05, 0.18, dot(n, L));
                        float maria = smoothstep(0.42, 0.62, Spread(Fbm(n * 1.7 + 7.0, 4), 2.0));
                        float craters = Spread(Fbm(n * 6.0 + 2.0, 3), 2.0);
                        float3 surf = _MoonColor.rgb * (1.0 - 0.33 * maria) * (0.86 + 0.28 * craters);
                        float limb = 0.7 + 0.3 * sqrt(n.z);
                        float3 moon = surf * limb * (lit + 0.012) * _MoonIntensity;
                        col = lerp(col, moon + col * 0.05, disk);
                    }
                }

                // tiny dither against banding in the dark gradient
                float dn = frac(sin(dot(i.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453);
                col += (dn - 0.5) * 0.0025;
                return half4(max(col, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
