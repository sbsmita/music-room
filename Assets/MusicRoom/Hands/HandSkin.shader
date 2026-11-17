// E6 (10-upgrade/experience.md section 8.1): the runtime hand mesh lit like skin in the VR room, so the floor lamp and
// the flickering fire warm it and it darkens when they go out (HandGhost was unlit: a fixed colour plus a rim, which
// looked pasted on in the dim room).
//   c = albedo * (SampleSH(N) + sum over lights of L.color * atten * saturate((N.L + w) / (1 + w))) + rim
// with w = 0.5 wrap (the usual cheap soft-skin approximation), albedo = HandGhost's _Color, rim = _RimColor * 0.35 at
// _RimPower 2.5, alpha = _Alpha * 0.92. Main light plus up to 4 additional per-pixel lights (ARCHITECTURE.md 14), lit per
// pixel (hand pixels only). The depth-only pass keeps the hand from showing its own back faces and fingers through each
// other while it fades; MetaHandVisual drives _Alpha per renderer through a MaterialPropertyBlock (allowed on this custom
// shader, ARCHITECTURE.md 14). Single-pass instanced stereo.
Shader "MusicRoom/HandSkin"
{
    Properties
    {
        _Color ("Albedo", Color) = (0.86, 0.78, 0.70, 1)
        _RimColor ("Rim Color", Color) = (1.0, 0.94, 0.86, 1)
        _RimStrength ("Rim strength", Range(0, 1)) = 0.35
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Wrap ("Wrap", Range(0, 1)) = 0.5
        _MaxOpacity ("Max Opacity", Range(0, 1)) = 0.92
        _Alpha ("Alpha (fade)", Range(0, 1)) = 1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        half4 _RimColor;
        half _RimStrength;
        half _RimPower;
        half _Wrap;
        half _MaxOpacity;
        half _Alpha;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
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
        return o;
    }

    half4 fragDepth(Varyings i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        return 0;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HandDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "HandSkin"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half WrapDiffuse(half3 n, half3 l)
            {
                return saturate((dot(n, l) + _Wrap) / (1.0h + _Wrap));
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n = normalize(i.normalWS);
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 light = SampleSH(n);
                Light mainLight = GetMainLight();
                light += mainLight.color * (mainLight.distanceAttenuation * WrapDiffuse(n, mainLight.direction));
            #if defined(_ADDITIONAL_LIGHTS)
                uint count = (uint)GetAdditionalLightsCount();
                for (uint li = 0u; li < count; ++li)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    light += l.color * (l.distanceAttenuation * WrapDiffuse(n, l.direction));
                }
            #endif
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower);
                half3 c = _Color.rgb * light + _RimColor.rgb * (_RimStrength * rim);
                return half4(c, _Alpha * _MaxOpacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
