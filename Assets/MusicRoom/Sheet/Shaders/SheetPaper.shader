// The music book's paper (Docs/12-new-instruments/sheet.md 2.2, 4, 5.3): both open pages and the turning leaves in
// ONE draw. Each vertex carries its page position in mm (x from the page's left edge, y down from its top) and a role:
// 0 the left page, 1 the right page, 2 + 2k leaf k's front (a right page), 3 + 2k its back (a left page); the vertex stage
// maps the role to a slice of the page Texture2DArray (_Slices0/1), whose R8 texels are ink coverage, linear.
//   albedo = lerp(paper x mottle x marker tint, ink, coverage) x gutter shade x leaf shadow
//   light  = SH + main light + additional lights (Lambert, wrap 0.15) + the reading lamp (analytic: a soft 55 degree cone,
//            inverse square, no URP light slot) + a faint grazing sheen (3 %)
// The current-note marker and the contents hover are multiplies of the paper toward lamplight amber (#F0C884) inside a
// soft rect (page mm): ink stays ink, nothing glows. No texture update and no extra draw for either.
// Opaque, depth written, back faces culled (each leaf has a front and a back layer), single-pass instanced stereo.
Shader "MusicRoom/SheetPaper"
{
    Properties
    {
        _Pages ("Pages (R8 ink coverage)", 2DArray) = "" {}
        _DetailMap ("Paper mottle", 2D) = "grey" {}
        _PaperColor ("Paper (linear)", Color) = (0.896, 0.823, 0.665, 1)
        _InkColor ("Ink (linear)", Color) = (0.0232, 0.0185, 0.0152, 1)
        _MarkColor ("Marker (linear)", Color) = (0.871, 0.578, 0.235, 1)
        _TexScale ("Texture uv per mm (xy), mottle uv per mm (zw)", Vector) = (0.003125, 0.0040973, 0.0138889, 0.0138889)
        _Slices0 ("Slices: left, right, leaf0 front, leaf0 back", Vector) = (0, 1, 2, 3)
        _Slices1 ("Slices: leaf1 front, back, leaf2 front, back", Vector) = (2, 3, 2, 3)
        _Ink0 ("Ink on (1) or blank paper (0) per role 0-3", Vector) = (0, 0, 0, 0)
        _Ink1 ("Ink per role 4-7", Vector) = (0, 0, 0, 0)
        _Mark ("Marker rect (mm x0 y0 x1 y1)", Vector) = (0, 0, 0, 0)
        _MarkInfo ("Marker: slice, strength, feather mm", Vector) = (-1, 0, 3, 0)
        _Hover ("Hover rect (mm)", Vector) = (0, 0, 0, 0)
        _HoverInfo ("Hover: slice, strength, feather mm", Vector) = (-1, 0, 3, 0)
        _Leaf ("Leaf shadow on the right, left page", Vector) = (0, 0, 0, 0)
        _LampPos ("Lamp position (world), intensity", Vector) = (0, 0, 0, 0)
        _LampDir ("Lamp opening direction (world), cos outer", Vector) = (0, -1, 0, 0.5)
        _LampAxis ("Lamp tube half axis (world)", Vector) = (0.17, 0, 0, 0)
        _LampColor ("Lamp colour (linear), cos inner", Vector) = (1, 0.69, 0.39, 0.7)
        _Wrap ("Wrap", Range(0, 1)) = 0.15
        _Sheen ("Sheen", Range(0, 0.2)) = 0.03
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SheetPaper"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_Pages); SAMPLER(sampler_Pages);
            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _Pages_ST;
                float4 _DetailMap_ST;
                half4 _PaperColor;
                half4 _InkColor;
                half4 _MarkColor;
                float4 _TexScale;
                float4 _Slices0;
                float4 _Slices1;
                float4 _Ink0;
                float4 _Ink1;
                float4 _Mark;
                float4 _MarkInfo;
                float4 _Hover;
                float4 _HoverInfo;
                float4 _Leaf;
                float4 _LampPos;
                float4 _LampDir;
                float4 _LampAxis;
                float4 _LampColor;
                half _Wrap;
                half _Sheen;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 page : TEXCOORD0;     // x mm, y mm, role, 0
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 mm : TEXCOORD2;
                nointerpolation float4 info : TEXCOORD3;   // slice, left side (1) / right (0), static page (1) / leaf (0), ink on
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.mm = v.page.xy;
                float role = v.page.z;
                float slice = role < 0.5 ? _Slices0.x : role < 1.5 ? _Slices0.y : role < 2.5 ? _Slices0.z : role < 3.5 ? _Slices0.w
                            : role < 4.5 ? _Slices1.x : role < 5.5 ? _Slices1.y : role < 6.5 ? _Slices1.z : _Slices1.w;
                float inkOn = role < 0.5 ? _Ink0.x : role < 1.5 ? _Ink0.y : role < 2.5 ? _Ink0.z : role < 3.5 ? _Ink0.w
                            : role < 4.5 ? _Ink1.x : role < 5.5 ? _Ink1.y : role < 6.5 ? _Ink1.z : _Ink1.w;
                // left-side faces: the left page and every leaf's back; right-side: the right page and the leaves' fronts
                float leftSide = (role < 0.5 || (role > 2.5 && fmod(role, 2.0) > 0.5)) ? 1.0 : 0.0;
                o.info = float4(slice, leftSide, role < 1.5 ? 1.0 : 0.0, inkOn);
                return o;
            }

            half SoftRect(float2 p, float4 r, float feather)
            {
                float kx = min(saturate((p.x - r.x) / feather + 0.5), saturate((r.z - p.x) / feather + 0.5));
                float ky = min(saturate((p.y - r.y) / feather + 0.5), saturate((r.w - p.y) / feather + 0.5));
                return (half)(kx * ky);
            }

            half Wrap(half3 n, half3 l) { return saturate((dot(n, l) + _Wrap) / (1.0h + _Wrap)); }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float slice = i.info.x;
                float2 uv = i.mm * _TexScale.xy;
                half ink = SAMPLE_TEXTURE2D_ARRAY(_Pages, sampler_Pages, uv, slice).r * (half)i.info.w;   // 0: a page not drawn yet is blank paper
                // paper fibre mottle (+-3 %): the page's own mm, offset per side so the two pages differ
                half detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, (i.mm + float2(i.info.y * 37.0, 0.0)) * _TexScale.zw).r;
                half mottle = 1.0h + (detail - 0.5h) * 0.06h;
                half3 paper = _PaperColor.rgb * mottle;
                // the marker and the hover: multiply toward lamplight amber where the rect is (same slice only)
                half3 tintTo = _MarkColor.rgb / max(_PaperColor.rgb, 1e-3h);
                half mk = abs(slice - _MarkInfo.x) < 0.5 ? SoftRect(i.mm, _Mark, _MarkInfo.z) * (half)_MarkInfo.y : 0.0h;
                half hv = abs(slice - _HoverInfo.x) < 0.5 ? SoftRect(i.mm, _Hover, _HoverInfo.z) * (half)_HoverInfo.y : 0.0h;
                half a = saturate(mk + hv - mk * hv);
                paper *= lerp(half3(1, 1, 1), tintTo, a);
                half3 albedo = lerp(paper, _InkColor.rgb, ink);
                // gutter: d = mm from the spine; the outer edge a little darker too
                float d = i.info.y > 0.5 ? 320.0 - i.mm.x : i.mm.x;
                float outer = 320.0 - d;
                half shade = (half)((1.0 - 0.22 * exp(-d / 9.0) - 0.06 * exp(-d / 40.0)) * (1.0 - 0.05 * exp(-outer / 6.0)));
                // a turning leaf darkens the page it is over (static pages only), most near the spine
                half leaf = i.info.z > 0.5 ? (half)(i.info.y > 0.5 ? _Leaf.y : _Leaf.x) : 0.0h;
                shade *= 1.0h - leaf * (half)exp(-d / 40.0);
                albedo *= shade;

                half3 n = normalize(i.normalWS);
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 light = SampleSH(n);
                half spec = 0.0h;
                Light mainLight = GetMainLight();
                half3 mainC = mainLight.color * mainLight.distanceAttenuation;
                light += mainC * Wrap(n, mainLight.direction);
                spec += dot(mainC, half3(0.333h, 0.333h, 0.333h)) * pow(saturate(dot(n, normalize(mainLight.direction + v))), 6.0h);
            #if defined(_ADDITIONAL_LIGHTS)
                uint count = (uint)GetAdditionalLightsCount();
                for (uint li = 0u; li < count; ++li)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    light += l.color * (l.distanceAttenuation * Wrap(n, l.direction));
                }
            #endif
                // the reading lamp: a soft cone along _LampDir, inverse-square from the nearest point of its tube (no URP light)
                float3 rel = i.positionWS - _LampPos.xyz;
                float ta = clamp(dot(rel, _LampAxis.xyz) / max(dot(_LampAxis.xyz, _LampAxis.xyz), 1e-6), -1.0, 1.0);
                float3 toLamp = _LampPos.xyz + _LampAxis.xyz * ta - i.positionWS;
                float dist2 = dot(toLamp, toLamp);
                half3 ld = (half3)(toLamp * rsqrt(max(dist2, 1e-6)));
                half cone = smoothstep((half)_LampDir.w, (half)_LampColor.w, dot(-ld, (half3)_LampDir.xyz));
                half3 lampC = (half3)_LampColor.rgb * (half)(_LampPos.w / (dist2 + 0.05)) * cone;   // a diffuse tube: a soft inverse square
                light += lampC * Wrap(n, ld);
                spec += dot(lampC, half3(0.333h, 0.333h, 0.333h)) * pow(saturate(dot(n, normalize(ld + v))), 6.0h);
                half3 c = albedo * light + spec * _Sheen * (1.0h - ink) * shade;
                return half4(c, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
