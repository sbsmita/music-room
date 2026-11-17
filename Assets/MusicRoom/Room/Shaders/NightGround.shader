// The ground outside (spec 07 sections 5.2 and 5.3): an unlit colour (C# lerps #05070f -> #435070 by .92 cover^.8 in
// linear) times the snow drift pattern once snow has settled: drift = gn(.35g)*.6 + gn(1.3g + 4)*.4 on g = the source
// frame's world (x, z) = Unity (x, -z), darker toward the hills. Colour-managed as specified, so no extra conversion.
// The pattern runs in the room frame (object space: the merged static mesh sits at the room root, which carries the
// room's turn, Core's RoomFrame), so the ground looks the same whichever way the room faces.
Shader "MusicRoom/NightGround"
{
    Properties
    {
        _Tint ("Ground colour (linear)", Vector) = (0.0015, 0.0021, 0.0048, 1)
        _WallX ("Window wall x", Float) = 3.2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "NightGround"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RoomNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _WallX;
            CBUFFER_END
            float _SnowCover;

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionRoom : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionRoom = v.positionOS.xyz;   // room frame
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS.xyz));
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float3 rgb = _Tint.rgb;
                if (_SnowCover > 0.0)
                {
                    float2 g = float2(i.positionRoom.x, -i.positionRoom.z);
                    float drift = GroundNoise(g * 0.35) * 0.6 + GroundNoise(g * 1.3 + 4.0) * 0.4;
                    float far = smoothstep(5.0, 15.0, i.positionRoom.x - _WallX);
                    float k = (0.78 + 0.34 * drift + 0.08 * GroundNoise(g * 9.0)) * (1.0 - 0.3 * far);
                    rgb *= lerp(1.0, k, smoothstep(0.0, 0.5, _SnowCover));
                }
                return float4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
