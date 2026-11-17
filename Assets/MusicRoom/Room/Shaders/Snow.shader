// The snow field (spec 07 section 5.4), one draw of N flakes moved by the vertex shader alone. Verbatim maths in the
// Unity window frame (+z outward, source frame -z): p = (x, y, +dd), eye distance in front of the glass c = -cam.z.
// Points become camera-facing quads: each flake is 4 vertices (corner in TEXCOORD0.xy) expanded in view space to the
// spec's point size ps (pixels at _SnowPx = half the eye-buffer height). A flake is drawn only where the line from the
// eye to it crosses the window rectangle and only behind the glass. Mesh: POSITION = the flake's (x, y, z) seeds 0..1,
// TEXCOORD0 = (corner x, corner y, tone, 0), TEXCOORD1 = (size, sway, rate, phase); bounds = the volume (set by C#).
Shader "MusicRoom/Snow"
{
    Properties
    {
        _Vol ("Depth near, far, bias", Vector) = (0.1, 15, 1.6, 0)
        _Lat ("Lateral half-width, growth", Vector) = (1.4, 0.9, 0, 0)
        _Low ("Bottom, growth", Vector) = (-1.6, 0, 0, 0)
        _High ("Top, growth", Vector) = (1.3, 0.9, 0, 0)
        _Rect ("Window half-size", Vector) = (0.94, 0.89, 0, 0)
        _Fade ("Fade start, end", Vector) = (10, 18.5, 0, 0)
        _Size ("Flake diameter range", Vector) = (0.007, 0.022, 0, 0)
        _Color ("Flake colour (linear)", Vector) = (0.578, 0.658, 0.871, 1)
        _Haze ("Haze colour (linear)", Vector) = (0.023, 0.036, 0.089, 1)
        _HazeAmt ("Haze amount", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+2" "IgnoreProjector"="True" }
        Pass
        {
            Name "Snow"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "WindowShow.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Vol, _Lat, _Low, _High, _Rect, _Fade, _Size, _Color, _Haze;
                float _HazeAmt;
            CBUFFER_END
            float _SnowTime, _SnowIntensity, _SnowWind, _SnowWindVel, _SnowPx;

            #define GUST_FRONT 7.0

            struct Attributes
            {
                float4 positionOS : POSITION;   // the flake's random (x, y, z) in 0..1
                float4 corner : TEXCOORD0;      // corner xy (0/1), tone
                float4 seed : TEXCOORD1;        // size, sway amount, sway rate, phase
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 pc : TEXCOORD0;          // gl_PointCoord (0,0 top-left)
                float4 v : TEXCOORD1;           // alpha, tone, spin, squash
                float haze : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 position = input.positionOS.xyz;
                float4 aSeed = input.seed;
                float aTone = input.corner.z;

                float sizeN = aSeed.x;
                float size = lerp(_Size.x, _Size.y, sizeN * sizeN);
                // Bigger flakes (clumps) fall faster: 0.5 to 1.2 m/s.
                float fall = 0.5 + 0.7 * (0.75 * sizeN + 0.25 * aTone);
                float d = _Vol.x + (_Vol.y - _Vol.x) * pow(max(position.z, 0.0), _Vol.z);
                float span = 2.0 * (_Lat.x + _Lat.y * d);
                // Wind along the window; a gust sweeps across from the upwind side, arriving later downwind.
                float lag = frac(position.x + _SnowWind / span) * span / GUST_FRONT;
                float xn = frac(position.x + (_SnowWind - _SnowWindVel * lag) / span);
                float x = (xn - 0.5) * span;
                float yLo = _Low.x + _Low.y * d;
                float H = _High.x + _High.y * d - yLo;
                float yn = frac(position.y - fall * _SnowTime / H);
                float y = yLo + yn * H;
                // Sway: each flake spirals on its own rate and phase, small flakes more than big ones.
                float ph = aSeed.w * 6.2831853;
                float st = _SnowTime * lerp(0.7, 2.1, aSeed.z) + ph;
                float A = lerp(0.02, 0.12, aSeed.y) * (1.25 - 0.5 * sizeN);
                x += A * sin(st);
                // The playback show: a gust swirls the flakes (exactly (x, y) while it rests).
                float2 sw = WindowSwirl(float2(x, y), d, aSeed);
                x = sw.x; y = sw.y;
                float dd = max(d + 0.6 * A * cos(st), _Vol.x * 0.5);
                float3 p = float3(x, y, dd);

                float3 positionWS = TransformObjectToWorld(p);
                float3 positionVS = TransformWorldToView(positionWS);
                float dist = -positionVS.z;
                float P11 = abs(UNITY_MATRIX_P._m11);
                float px = size * _SnowPx * P11 / max(dist, 0.05);
                float ps = clamp(px, 1.5, 40.0);

                // Seen through the window? The eye-to-flake line must cross the glass inside the rectangle (by more than
                // the sprite's own radius there), the eye in front of the glass and the flake behind it.
                float3 cam = TransformWorldToObject(_WorldSpaceCameraPos);
                float c = -cam.z;
                float keep = step(0.02, c) * step(0.02, dd);
                float k = c / max(c + dd, 1e-4);
                float2 hit = cam.xy + (p.xy - cam.xy) * k;
                float2 e = _Rect.xy - abs(hit);
                float rWin = 0.5 * size * max(1.0, ps / max(px, 1e-4)) * k;
                keep *= smoothstep(rWin, rWin + 0.015, min(e.x, e.y));

                // Appear at the top, melt into the ground at the bottom, no popping at the side edges.
                float edge = smoothstep(0.0, 0.05, xn) * smoothstep(1.0, 0.95, xn) * smoothstep(0.0, 0.06, yn) * smoothstep(1.0, 0.94, yn);
                float a = _SnowIntensity * keep * edge * min(1.0, (px * px) / (ps * ps));
                a *= (1.0 - smoothstep(_Fade.x, _Fade.y, dist)) * smoothstep(0.2, 0.5, dist);
                float alpha = a * lerp(0.55, 1.0, aTone);

                if (alpha < 0.002)
                {
                    o.positionCS = float4(2.0, 2.0, 0.5, 1.0);   // outside the clip volume: the flake's quad is culled
                    return o;
                }
                float halfSize = 0.5 * ps * dist / (_SnowPx * P11);
                float2 corner = input.corner.xy * 2.0 - 1.0;
                positionVS.xy += corner * halfSize;
                o.positionCS = TransformWViewToHClip(positionVS);
                o.pc = float2(input.corner.x, 1.0 - input.corner.y);
                o.v = float4(alpha, aTone, _SnowTime * (aSeed.z - 0.5) * 5.0 + ph, 0.5 + 0.5 * abs(cos(_SnowTime * lerp(0.6, 2.0, aSeed.y) + ph * 1.7)));
                o.haze = smoothstep(3.0, _Fade.y, dist) * _HazeAmt;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 c = i.pc - 0.5;
                float s = sin(i.v.z), k = cos(i.v.z);
                c = float2(k * c.x - s * c.y, s * c.x + k * c.y);
                c.y /= i.v.w;   // tumbling: seen edge-on now and then
                float r = length(c) * 2.0;
                float a = i.v.x * (1.0 - smoothstep(0.3, 1.0, r)) * (0.75 + 0.25 * smoothstep(0.6, 0.0, r));
                clip(a - 0.004);
                float3 col = lerp(_Color.rgb * (0.78 + 0.3 * i.v.y), _Haze.rgb, i.haze) + _WindowSnowTint.rgb;   // the aurora's light (0 at rest)
                // Over-black form, as the frost: the spec's sRGB col x a added over the sky, so the soft fringes keep
                // their size and brightness under URP's linear blend.
                return float4(SRGBToLinear(saturate(col) * a) / max(a, 1e-4), a);
            }
            ENDHLSL
        }
    }
}
