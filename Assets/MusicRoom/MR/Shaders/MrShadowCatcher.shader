// The shadow catcher (a shadow-only material, opacity .32): an invisible plane on the real surface under
// the piano that only shows the instruments' shadows, as black with alpha = .32 x shadow. Over passthrough the
// alpha darkens the camera image there. A 3 x 3 PCF over URP's main-light shadow map softens the edge (percentage-closer
// filtering) whatever the URP asset's soft-shadow setting. Without main-light shadows (VR) it outputs nothing.
// The PCF step is the shadow map's own texel, read from the texture: URP 17.3 writes _MainLightShadowmapSize only when
// soft shadows are supported (MainLightShadowCasterPass.cs:423-434), and QuestURP keeps them off, so that uniform is
// stale there (the empty map's (1, 1): the outer taps then fell off the map and the shadow showed at 1/9 strength).
Shader "MusicRoom/MR/ShadowCatcher"
{
    Properties
    {
        _Opacity ("Shadow opacity", Float) = 0.32
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ShadowCatcher"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes a)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(a);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // One texel of the main-light shadow map (see the header: not _MainLightShadowmapSize).
            float2 ShadowTexel()
            {
            #if defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                return float2(1.0 / 1024.0, 1.0 / 1024.0);   // QuestURP's 1024 map (Vulkan / Metal take the real size)
            #else
                uint w, h;
                _MainLightShadowmapTexture.GetDimensions(w, h);
                return 1.0 / float2(max(w, 1u), max(h, 1u));
            #endif
            }

            // 1 = lit, 0 = fully shadowed.
            half CatcherLight(float3 positionWS)
            {
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                float4 sc = TransformWorldToShadowCoord(positionWS);
                float2 texel = ShadowTexel();
                half sum = 0.0;
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                        sum += SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3(sc.xy + float2(x, y) * texel, sc.z));
                }
                half lit = LerpWhiteTo(sum * (1.0 / 9.0), GetMainLightShadowParams().x);
                return BEYOND_SHADOW_FAR(sc) ? 1.0 : lit;
            #else
                return 1.0;
            #endif
            }

            half4 frag(Varyings i) : SV_Target
            {
                half shadow = 1.0 - CatcherLight(i.positionWS);
                half a = _Opacity * shadow;
                clip(a - 0.002);
                return half4(0.0, 0.0, 0.0, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
