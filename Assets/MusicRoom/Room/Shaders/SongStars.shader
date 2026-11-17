// The song's night sky (Room/Decor/SongSky.cs): ONE mesh of three kinds of quad (uv2.y): 0 a star, 1 a line between two
// stars of an instrument, 2 the soft night field under the ceiling (VR only). The stars' and lines' data are written once
// when a star is made; this moves them from the model's clock (_SkyTime), the same maths as SongSkyModel.PositionAt:
//   star: rises from uv1.xyz (born at uv1.w) to the vertex position over _Rise s on a gentle curve (ease out), pushed out of
//         the head's sphere (_SkyHead, _HeadClear); a small arrival flash, then a gentle twinkle (phase uv0.w); size uv0.z
//         (its core; the quad holds its halo), colour rgb and brightness a; fades over _DieFade from its death (uv2.x);
//   line: from the position to uv1.xyz, uv0.z wide, faced to the eye, a little short of both stars; fades in over _LineIn
//         from uv1.w, out from uv2.x; drawn only for the stars born since _SkyEpoch (uv2.z the star's birth);
//   field: an oval of the colour, _SkyField opaque at its middle, a few faint fixed specks in it.
// Everything x _SkyFade. Premultiplied alpha (One OneMinusSrcAlpha): the stars and lines add light with alpha 0 (over the
// passthrough they are light, as the motes are); the field darkens a little (its alpha). ZWrite off, Cull off.
// Single-pass instanced / multiview: instance id in, stereo output initialised, eye index set up in the fragment; the
// billboard and the line's width use the eye's own view.
Shader "MusicRoom/SongStars"
{
    Properties
    {
        _SkyTime ("Time", Float) = 0
        _SkyFade ("Fade", Float) = 1
        _SkyField ("Field", Float) = 0
        _SkyEpoch ("Epoch", Float) = -1
        _SkyHead ("Head", Vector) = (0, 1.2, 0, 1)
        _HeadClear ("Head clear", Float) = 0.35
        _Rise ("Rise", Float) = 1.6
        _DieFade ("Die fade", Float) = 0.7
        _LineIn ("Line in", Float) = 0.9
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardSongStars"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SkyTime;
                float _SkyFade;
                float _SkyField;
                float _SkyEpoch;
                float4 _SkyHead;
                float _HeadClear;
                float _Rise;
                float _DieFade;
                float _LineIn;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0;      // xy: the quad's corner (-1..1), z: the kind
                half4 color : COLOR;        // rgb x brightness, a: the field's opacity
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 ClearOfHead(float3 p)
            {
                float3 v = p - _SkyHead.xyz;
                float m = length(v);
                return m >= _HeadClear ? p : _SkyHead.xyz + (m > 1e-5 ? v / m : float3(0, 1, 0)) * _HeadClear;
            }

            float Dying(float death)
            {
                return saturate(1.0 - (_SkyTime - death) / _DieFade);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float kind = v.uv2.y;
                float3 positionWS;
                half4 col = half4(0, 0, 0, 0);
                float3 camRight = UNITY_MATRIX_V[0].xyz, camUp = UNITY_MATRIX_V[1].xyz;
                if (kind < 0.5)
                {
                    // a star
                    float birth = v.uv1.w;
                    float age = _SkyTime - birth;
                    float alive = (birth >= _SkyEpoch && age >= 0.0) ? 1.0 : 0.0;
                    float u = saturate(age / _Rise);
                    float e = 1.0 - (1.0 - u) * (1.0 - u) * (1.0 - u);
                    float3 from = v.uv1.xyz, to = v.positionOS.xyz;
                    float3 ctrl = (from + to) * 0.5 + float3(0, 0.2, 0);
                    float3 c = lerp(lerp(from, ctrl, e), lerp(ctrl, to, e), e);
                    c = ClearOfHead(c);
                    float3 cw = TransformObjectToWorld(c);
                    float settled = max(age - _Rise, 0.0);
                    float flash = u >= 1.0 ? 1.0 + 0.9 * exp(-settled / 0.3) : 1.0;
                    float phase = v.uv0.w * 6.2831853;
                    float twinkle = u >= 1.0 ? 0.8 + 0.2 * sin(_SkyTime * (1.6 + 1.8 * v.uv0.w) + phase) : 1.0;
                    float appear = saturate(age / 0.12);
                    float size = v.uv0.z * lerp(0.6, 1.0, e) * (0.85 + 0.15 * flash) * 4.0;   // the quad holds the halo
                    positionWS = cw + (camRight * v.uv0.x + camUp * v.uv0.y) * (size * 0.5 * alive);
                    float b = v.color.a * appear * flash * twinkle * Dying(v.uv2.x) * _SkyFade * alive * lerp(0.8, 1.0, e);
                    col = half4(v.color.rgb * b, 0);
                }
                else if (kind < 1.5)
                {
                    // a line from the star before to this one
                    float alive = v.uv2.z >= _SkyEpoch ? 1.0 : 0.0;
                    float3 a = TransformObjectToWorld(v.positionOS.xyz), bb = TransformObjectToWorld(v.uv1.xyz);
                    float3 d = bb - a;
                    float len = max(length(d), 1e-4);
                    float3 dir = d / len;
                    float inset = min(0.45, 0.035 / len);   // a little short of each star
                    float3 p = a + d * lerp(inset, 1.0 - inset, v.uv0.y);
                    float3 toEye = normalize(GetCameraPositionWS() - p);
                    float3 side = cross(dir, toEye);
                    side = dot(side, side) > 1e-8 ? normalize(side) : camRight;
                    positionWS = p + side * (v.uv0.x * v.uv0.z * 0.5 * alive);
                    float k = saturate((_SkyTime - v.uv1.w) / _LineIn) * Dying(v.uv2.x) * _SkyFade * alive;
                    col = half4(v.color.rgb * (0.42 * k), 0);
                }
                else
                {
                    // the night field
                    positionWS = TransformObjectToWorld(v.positionOS.xyz);
                    float k = _SkyField * _SkyFade * (v.uv1.w >= _SkyEpoch ? 1.0 : 0.0);
                    col = half4(v.color.rgb, k);
                }
                o.uv = float3(v.uv0.xy, kind);
                o.color = col;
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 q = i.uv.xy;
                if (i.uv.z < 0.5)
                {
                    float r2 = dot(q, q);
                    float core = exp(-r2 * 30.0);
                    float halo = exp(-r2 * 7.0) * 0.32 + exp(-r2 * 2.6) * 0.06;
                    float3 rgb = i.color.rgb * (halo + core) + core * 0.55 * (i.color.r + i.color.g + i.color.b) * 0.333;
                    return half4(rgb, 0);
                }
                if (i.uv.z < 1.5)
                {
                    float across = 1.0 - abs(q.x);
                    return half4(i.color.rgb * (across * across), 0);
                }
                // the field: an oval, soft to its edge, and a few faint fixed specks
                float r = length(q);
                float a = i.color.a * smoothstep(1.0, 0.25, r);
                float2 cell = floor(q * 26.0);
                float2 f = frac(q * 26.0) - 0.5;
                float h = Hash(cell);
                float speck = h > 0.93 ? exp(-dot(f, f) * 60.0) * (h - 0.93) * 9.0 : 0.0;
                float3 rgb = i.color.rgb * a + speck * a * float3(0.75, 0.8, 1.0);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
