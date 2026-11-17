// Soft translucent hand for the runtime hand mesh. MetaHandVisual drives _Alpha per renderer through a
// MaterialPropertyBlock. A depth-only pass first keeps the hand from showing its own back faces and fingers
// through each other while it fades. Works in the Built-in pipeline and in URP.
Shader "MusicRoom/HandGhost"
{
    Properties
    {
        _Color ("Color", Color) = (0.86, 0.78, 0.70, 1)
        _RimColor ("Rim Color", Color) = (1.0, 0.94, 0.86, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _MaxOpacity ("Max Opacity", Range(0, 1)) = 0.85
        _Alpha ("Alpha (fade)", Range(0, 1)) = 1
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    float4 _Color;
    float4 _RimColor;
    float _RimPower;
    float _MaxOpacity;
    float _Alpha;

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float3 normalWS : TEXCOORD0;
        float3 viewWS : TEXCOORD1;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    v2f vert(appdata v)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.pos = UnityObjectToClipPos(v.vertex);
        o.normalWS = UnityObjectToWorldNormal(v.normal);
        o.viewWS = WorldSpaceViewDir(v.vertex);
        return o;
    }

    fixed4 fragDepth(v2f i) : SV_Target { return 0; }

    fixed4 frag(v2f i) : SV_Target
    {
        float rim = pow(1.0 - saturate(dot(normalize(i.normalWS), normalize(i.viewWS))), _RimPower);
        float3 c = lerp(_Color.rgb, _RimColor.rgb, rim);
        return fixed4(c, _Alpha * _MaxOpacity * lerp(0.65, 1.0, rim));
    }
    ENDCG

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal": "12.0.0" }
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            ENDCG
        }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
