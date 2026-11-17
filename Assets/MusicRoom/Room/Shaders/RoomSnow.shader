// Snow in the whole room (Room/Decor/RoomSnow.cs draws it, RoomSnowModel.cs has the numbers): one draw of N flakes moved
// by the vertex shader alone. RoomSnowFlake below is RoomSnowModel.FlakeAt line for line (EditMode / PlayMode tests check
// the CPU side). Each flake is 4 vertices: POSITION = its (x, y, z) seeds 0..1, TEXCOORD0 = (corner x, corner y, tone,
// size), TEXCOORD1 = (sway amount, sway rate, phase, fall); expanded to a camera-facing quad in view space (each eye its
// own: stereo instancing / multiview safe, no camera or matrix access in the fragment). Its size is metres (perspective),
// never under 1.5 px (smaller ones fade instead). The clock, wind and SNOW level, the volume, the head, the show and the
// fire come as material vectors written by RoomSnow once a frame (no global but Room's _SnowPx).
Shader "MusicRoom/RoomSnow"
{
    Properties
    {
        _Clock ("Time, wind, wind speed, level", Vector) = (0, 0, 0, 0)
        _BoxC ("Volume centre, cos yaw", Vector) = (0, 1.45, 0, 1)
        _BoxH ("Volume half size, sin yaw", Vector) = (3, 1.4, 2.5, 0)
        _Mode ("Round, 0, 0, 0", Vector) = (0, 0, 0, 0)
        _Head ("Head centre", Vector) = (0, 1.2, 0, 0)
        _Fwd ("Head forward on the floor", Vector) = (0, 0, 1, 0)
        _Show ("Gust, eddy phase, whirl, lift", Vector) = (0, 0, 0, 0)
        _Fire ("Fire centre, warmth", Vector) = (0, 0, 0, 0)
        _Tint ("Tint (linear), amount", Vector) = (0, 0, 0, 0)
        _Color ("Flake colour (linear), opacity", Vector) = (0.8, 0.75, 0.7, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+5" "IgnoreProjector"="True" }
        Pass
        {
            Name "RoomSnow"
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

            CBUFFER_START(UnityPerMaterial)
                float4 _Clock, _BoxC, _BoxH, _Mode, _Head, _Fwd, _Show, _Fire, _Tint, _Color;
            CBUFFER_END
            float _SnowPx;

            // RoomSnowModel's constants (keep them equal).
            #define SIZE_MIN 0.012
            #define SIZE_MAX 0.032
            // the quad holds the flake and its soft glow (x the flake's size)
            #define HALO 1.9
            #define FALL_MIN 0.17
            #define FALL_MAX 0.4
            #define LAND_REST 2.4
            #define DRAFT 0.1
            #define HEAD_CLEAR 0.34
            #define HEAD_FULL 0.62
            #define EYE_CLEAR 0.26
            #define EYE_FULL 0.45
            #define CLEAR_AHEAD 0.5
            #define CLEAR_DOWN 0.42
            #define CLEAR_R float3(0.72, 0.55, 0.62)
            #define CLEAR_KEEP 0.14
            #define WALL_FADE 0.18
            #define FIRE_GONE 0.45
            #define FIRE_MELT 1.05
            #define FIRE_WARM 2.2
            #define WHIRL_REACH 2.3
            #define EDDY_MIN 0.05
            #define EDDY_MAX 0.26
            #define TINT_REACH 2.0

            struct Attributes
            {
                float4 positionOS : POSITION;   // the flake's (x, y, z) seeds 0..1
                float4 corner : TEXCOORD0;      // corner xy (0/1), tone, size
                float4 seed : TEXCOORD1;        // sway amount, sway rate, phase, fall
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 pc : TEXCOORD0;          // the quad's corner (0..1)
                float4 v : TEXCOORD1;           // alpha, melt, spin, squash
                half3 col : TEXCOORD2;          // linear colour
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 ToFrame(float3 w) { return float3(_BoxC.w * w.x - _BoxH.w * w.z, w.y, _BoxH.w * w.x + _BoxC.w * w.z); }
            float3 ToWorld(float3 f) { return float3(_BoxC.w * f.x + _BoxH.w * f.z, f.y, -_BoxH.w * f.x + _BoxC.w * f.z); }

            // RoomSnowModel.FlakeAt: the flake's centre in the world, its opacity (before the pixel clamp) and its melt.
            void RoomSnowFlake(float3 pos01, float tone, float sizeN, float4 sd, float3 eye, out float3 pos, out float alpha, out float melt)
            {
                float time = _Clock.x, wind = _Clock.y, windVel = _Clock.z, level = _Clock.w;
                float3 cf = ToFrame(_BoxC.xyz);
                float3 size = _BoxH.xyz * 2.0;
                float3 bmin = cf - _BoxH.xyz;
                float fall = lerp(FALL_MIN, FALL_MAX, sd.w) * (0.8 + 0.4 * sizeN);
                float rest = LAND_REST * lerp(0.6, 1.4, tone);
                float fallT = size.y / fall;
                float cycle = fallT + rest;
                float tc = frac(pos01.y + time / cycle) * cycle;
                float landed = tc >= fallT ? 1.0 : 0.0;
                float tAir = min(tc, fallT);
                melt = landed * (tc - fallT) / rest;
                float since = tc - tAir;
                float tMove = time - since;
                float windAt = wind - windVel * since;

                float ph = sd.z * 6.2831853;
                float st = tMove * lerp(0.55, 1.7, sd.y) + ph;
                float amp = lerp(0.03, 0.14, sd.x) * (1.2 - 0.4 * sizeN);
                float fx = pos01.x * size.x + DRAFT * windAt + amp * sin(st);
                float fz = pos01.z * size.z + amp * 0.8 * cos(st * 0.83 + ph);
                float fy = bmin.y + size.y - tAir * fall;
                if (landed > 0.0) fy = bmin.y + 0.004;

                float px = bmin.x + frac((fx - bmin.x) / size.x) * size.x;
                float pz = bmin.z + frac((fz - bmin.z) / size.z) * size.z;
                float k = (1.0 - landed) * _Show.x;
                if (k > 0.0)
                {
                    float3 hf = ToFrame(_Head.xyz);
                    float rx = px - hf.x, rz = pz - hf.z;
                    float sp = _Show.y * lerp(0.7, 1.6, sd.y) + ph * 1.3;
                    float er = k * lerp(EDDY_MIN, EDDY_MAX, sd.x);
                    rx += er * cos(sp);
                    rz += er * sin(sp);
                    fy += er * 0.5 * sin(sp * 1.3) + (1.0 - landed) * _Show.w * lerp(0.4, 1.0, sizeN);
                    float ang = (1.0 - landed) * _Show.z * exp(-(rx * rx + rz * rz) / (WHIRL_REACH * WHIRL_REACH));
                    float sa = sin(ang), ca = cos(ang);
                    float qx = ca * rx - sa * rz, qz = sa * rx + ca * rz;
                    px = clamp(hf.x + qx, bmin.x, bmin.x + size.x);
                    pz = clamp(hf.z + qz, bmin.z, bmin.z + size.z);
                }
                fy = min(fy, bmin.y + size.y);
                pos = ToWorld(float3(px, fy, pz));

                float gate = frac(sd.z * 13.71 + pos01.x * 3.17 + pos01.z * 7.13);
                float a = saturate((level * 1.08 - gate) * 10.0);
                if (_Mode.x > 0.5)
                {
                    float2 d = float2(px - cf.x, pz - cf.z);
                    float r = min(_BoxH.x, _BoxH.z);
                    a *= 1.0 - smoothstep(0.78 * r, r, length(d));
                }
                else
                {
                    float e = min(min(px - bmin.x, bmin.x + size.x - px), min(pz - bmin.z, bmin.z + size.z - pz));
                    a *= smoothstep(0.0, WALL_FADE, e);
                }
                a *= smoothstep(0.0, 0.35, tAir * fall);
                a *= 1.0 - melt;
                a *= smoothstep(HEAD_CLEAR, HEAD_FULL, distance(pos, _Head.xyz));
                a *= smoothstep(EYE_CLEAR, EYE_FULL, distance(pos, eye));
                float3 czone = _Head.xyz + _Fwd.xyz * CLEAR_AHEAD - float3(0.0, CLEAR_DOWN, 0.0);
                float3 q = pos - czone;
                float3 right = float3(_Fwd.z, 0.0, -_Fwd.x);
                float3 l = float3(dot(q, right), q.y, dot(q, _Fwd.xyz)) / CLEAR_R;
                a *= lerp(CLEAR_KEEP, 1.0, smoothstep(0.55, 1.3, dot(l, l)));
                if (_Fire.w > 0.0) a *= smoothstep(FIRE_GONE, FIRE_MELT, distance(pos, _Fire.xyz));
                alpha = a * lerp(0.6, 1.0, tone);
            }

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float tone = input.corner.z, sizeN = input.corner.w;
                float4 sd = input.seed;
                float3 eye = _WorldSpaceCameraPos;
                float3 pos; float alpha; float melt;
                RoomSnowFlake(input.positionOS.xyz, tone, sizeN, sd, eye, pos, alpha, melt);

                float size = lerp(SIZE_MIN, SIZE_MAX, sizeN * sizeN) * lerp(1.0, 0.45, melt);
                float3 positionVS = TransformWorldToView(pos);
                float dist = max(-positionVS.z, 0.05);
                float P11 = abs(UNITY_MATRIX_P._m11);
                float px = size * _SnowPx * P11 / dist;
                float ps = clamp(px, 1.5, 48.0);
                alpha *= min(1.0, (px * px) / (ps * ps)) * _Color.w;
                alpha *= lerp(1.0, 0.6, smoothstep(1.5, 5.5, dist));   // depth: the far side of the room a little dimmer
                if (alpha < 0.003)
                {
                    o.positionCS = float4(2.0, 2.0, 0.5, 1.0);   // outside the clip volume: the quad is culled
                    return o;
                }
                float halfSize = 0.5 * HALO * ps * dist / (_SnowPx * P11);
                float2 corner = input.corner.xy * 2.0 - 1.0;
                positionVS.xy += corner * halfSize;
                o.positionCS = TransformWViewToHClip(positionVS);
                o.pc = input.corner.xy;
                float ph = sd.z * 6.2831853;
                float time = _Clock.x;
                // tumbling: seen edge-on now and then; a landed flake lies flat
                float squash = lerp(0.45 + 0.55 * abs(cos(time * lerp(0.5, 1.6, sd.x) + ph * 1.7)), 0.35, step(1e-4, melt));
                o.v = float4(alpha, melt, time * (sd.y - 0.5) * 3.0 + ph, squash);
                // now and then a tumbling flake catches the light: a brief glint
                float glint = step(0.62, tone) * pow(abs(sin(time * lerp(0.7, 1.9, sd.x) + ph * 2.3)), 30.0);

                // colour: the room's light on the flake, warmer by the fire, the playing instrument's tint close to the player
                float3 col = _Color.rgb * (lerp(0.78, 1.12, tone) + 0.9 * glint);
                if (_Fire.w > 0.0)
                {
                    float df = distance(pos, _Fire.xyz);
                    col += _Fire.w * exp(-df * df / (FIRE_WARM * FIRE_WARM)) * float3(0.55, 0.22, 0.06);
                }
                if (_Tint.w > 0.0)
                {
                    float dh = distance(pos, _Head.xyz);
                    float t = _Tint.w * exp(-dh * dh / (TINT_REACH * TINT_REACH));
                    col = lerp(col, _Tint.rgb * max(max(col.r, col.g), col.b) * 1.15, t);
                }
                o.col = (half3)col;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.pc - 0.5;
                float s = sin(i.v.z), k = cos(i.v.z);
                c = float2(k * c.x - s * c.y, s * c.x + k * c.y);
                c.y /= i.v.w;
                float r = length(c) * 2.0 * HALO;   // 1 at the flake's edge, HALO at the quad's
                // a soft flake with a brighter heart and a faint glow round it
                float core = (1.0 - smoothstep(0.3, 1.0, r)) * (0.75 + 0.25 * smoothstep(0.6, 0.0, r));
                float halo = saturate(1.0 - r / HALO);
                float a = i.v.x * (core + 0.2 * halo * halo);
                clip(a - 0.004);
                return half4(i.col, a);
            }
            ENDHLSL
        }
    }
}
