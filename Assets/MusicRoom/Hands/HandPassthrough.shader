// E6 (10-upgrade/experience.md section 8.2): in "Play in my room" the tracked hand mesh is drawn as a HOLE in the eye
// buffer, so wherever the hand is in front of virtual content the compositor shows the player's real hand (passthrough
// underlay, clear (0, 0, 0, 0), ARCHITECTURE.md 12); where a virtual key is in front of the finger, depth keeps the key.
//   pass 1: ZWrite On, ColorMask 0 (the hand's own depth, after every other draw: queue 3999);
//   pass 2: ZWrite Off, ZTest LEqual, Blend Zero OneMinusSrcAlpha, Zero OneMinusSrcAlpha with output (0, 0, 0, a),
//           a = _Alpha * smoothstep(0.05, 0.35, saturate(N.V)): the premultiplied colour and alpha are scaled by 1 - a,
//           revealing passthrough, with a soft edge at the silhouette.
// The vertex shader pushes 1.5 mm along the normal (_Dilate) to cover small tracking mismatch. MetaHandVisual poses it
// at displayTime - cutoutLatency (GetHandStateAtTime) so it sits over the real hand the passthrough shows ~39 ms late.
Shader "MusicRoom/HandPassthrough"
{
    Properties
    {
        _Alpha ("Alpha (fade)", Range(0, 1)) = 1
        _Dilate ("Dilate (m)", Float) = 0.0015
        _EdgeFrom ("Edge from (N.V)", Range(0, 1)) = 0.05
        _EdgeTo ("Edge to (N.V)", Range(0, 1)) = 0.35
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half _Alpha;
        float _Dilate;
        half _EdgeFrom;
        half _EdgeTo;
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
        float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
        o.positionWS = TransformObjectToWorld(v.positionOS.xyz) + n * _Dilate;
        o.normalWS = n;
        o.positionCS = TransformWorldToHClip(o.positionWS);
        return o;
    }

    half4 fragDepth(Varyings i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        return 0;
    }

    half4 fragHole(Varyings i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        half3 n = normalize(i.normalWS);
        half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
        half a = _Alpha * smoothstep(_EdgeFrom, _EdgeTo, saturate(dot(n, v)));
        return half4(0.0h, 0.0h, 0.0h, a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "CutoutDepth"
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
            Name "CutoutHole"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Blend Zero OneMinusSrcAlpha, Zero OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragHole
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
