// The ghost hands (the playback show: your hands from before, playing the recording back). The look of Hands/HandGhost
// (a soft translucent hand whose rim glows, a depth-only pass first so a finger behind the palm adds nothing and the hand
// reads as one surface), in a cool pale blue so it is never taken for the player's own (lit, skin-toned) hands nor for
// Sunny's warm cream demo hand. Unlike HandGhost, the opacity is per vertex (the vertex colour's alpha): every hand of
// the one merged mesh fades on its own, and the forearm fades out toward the elbow. The depth pass skips nearly
// transparent vertices, so a hand fading out never hides what is behind it.
// Unlit, alpha-blended with separate alpha factors (the eye buffer's alpha stays right over passthrough), single-pass
// instanced / multiview: instance id in, stereo output initialised in the vertex stage (as Room/Shaders/Butterfly.shader).
Shader "MusicRoom/GhostHands"
{
    Properties
    {
        _Color ("Colour", Color) = (0.56, 0.76, 1.0, 1)
        _RimColor ("Rim colour", Color) = (0.86, 0.95, 1.0, 1)
        _RimPower ("Rim power", Range(0.5, 8)) = 2.2
        _MaxOpacity ("Max opacity", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        half4 _RimColor;
        half _RimPower;
        half _MaxOpacity;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        half4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 normalWS : TEXCOORD0;
        float3 viewWS : TEXCOORD1;
        half alpha : TEXCOORD2;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
        o.positionCS = TransformWorldToHClip(positionWS);
        o.normalWS = TransformObjectToWorldNormal(input.normalOS);
        o.viewWS = GetWorldSpaceViewDir(positionWS);
        o.alpha = input.color.a;
        return o;
    }

    half4 FragDepth(Varyings i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        clip(i.alpha - 0.08h);
        return 0;
    }

    half4 Frag(Varyings i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        half facing = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
        half rim = pow(1.0h - facing, _RimPower);
        half3 c = lerp(_Color.rgb, _RimColor.rgb, rim);
        half a = i.alpha * _MaxOpacity * lerp(0.55h, 1.0h, rim);
        return half4(c, a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+2" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "GhostHands"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
