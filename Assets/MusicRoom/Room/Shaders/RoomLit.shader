// URP's own Lit forward pass for the room's textured / emissive materials, plus (keyword _SWAY) the plants' sway in the
// vertex stage (spec 07 sections 3.2 and 11), so all six plants' stems are ONE renderer and all their
// leaves another (not one group per plant swayed on the CPU). Why not "Universal Render Pipeline/Lit" itself for the
// gloss / normal / emissive templates: its ShaderGUI.ValidateMaterial re-derives the keywords from the material's
// properties whenever the editor loads the asset (no _MetallicGlossMap / _BumpMap texture in a template, or no
// emissive GI flag, means no _METALLICSPECGLOSSMAP / _NORMALMAP / _EMISSION), so a template could not keep them. This
// shader has no ShaderGUI: the keywords the Room's provider sets stay. With _SWAY each vertex
// carries its plant's base (TEXCOORD2.xyz, object space) and (phase, amplitude, cos yaw, sin yaw) of the plant's frame
// (TEXCOORD3); the time is the room's _FireTime global (the Decor clock). Per vertex, about the base and in the
// plant's own frame: rotZ = sin(.7t + phi) amp, rotX = sin(.5t + 1.3 phi) .7 amp, source-frame Euler XYZ (rotX, 0, rotZ)
// mirrored into Unity as AngleAxis(-rotX, right) * AngleAxis(rotZ, forward) (PlantSway.Rotation is the CPU mirror).
// Everything else is URP's own Lit forward pass (LitInput.hlsl + LitForwardPass.hlsl, same UnityPerMaterial, so the
// SRP Batcher keeps it); _SWAY_TWO_SIDED flips the normal on back faces (two-sided lighting, the leaves).
Shader "MusicRoom/RoomLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}
        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}
        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0
        _Surface("__surface", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        HLSLINCLUDE
        // The sway (shared by both passes). Not in UnityPerMaterial: globals, like the fire shaders' clock. _MusicSway (0 at
        // rest, unset = 0) is the room's music level x ListenBackPulse.FlowerSway: a vertex answers it by its pivot's w (0 on
        // the plants, which keep exactly their own sway; the flowers' response on theirs), a faster second sway on top.
        float _FireTime;
        float _MusicSway;

        void Sway(inout float3 positionOS, inout float3 normalOS, inout float3 tangentOS, float4 pivot4, float4 sway)
        {
            float3 pivot = pivot4.xyz;
            float music = _MusicSway * pivot4.w * sway.y;
            float rz = sin(_FireTime * 0.7 + sway.x) * sway.y + music * sin(_FireTime * 2.3 + sway.x * 1.7);
            float rx = sin(_FireTime * 0.5 + sway.x * 1.3) * sway.y * 0.7 + music * 0.6 * sin(_FireTime * 1.9 + sway.x * 2.1);
            float cy = sway.z, sy = sway.w;          // the plant frame's yaw (Unity AngleAxis(yaw, up))
            float cx = cos(-rx), sx = sin(-rx), cz = cos(rz), sz = sin(rz);
            // M = Ry(yaw) Rx(-rotX) Rz(rotZ) Ry(-yaw), rows of the standard rotation matrices (Unity's AngleAxis).
            float3x3 ry = float3x3(cy, 0, sy, 0, 1, 0, -sy, 0, cy);
            float3x3 ryInv = float3x3(cy, 0, -sy, 0, 1, 0, sy, 0, cy);
            float3x3 rxm = float3x3(1, 0, 0, 0, cx, -sx, 0, sx, cx);
            float3x3 rzm = float3x3(cz, -sz, 0, sz, cz, 0, 0, 0, 1);
            float3x3 m = mul(ry, mul(rxm, mul(rzm, ryInv)));
            positionOS = pivot + mul(m, positionOS - pivot);
            normalOS = mul(m, normalOS);
            tangentOS = mul(m, tangentOS);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite[_ZWrite]
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SwayLitVertex
            #pragma fragment SwayLitFragment

            #pragma shader_feature_local_vertex _SWAY
            #pragma shader_feature_local _SWAY_TWO_SIDED
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            struct SwayAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 texcoord : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float4 pivot : TEXCOORD2;
                float4 sway : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings SwayLitVertex(SwayAttributes input)
            {
                Attributes a = (Attributes)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, a);
                float3 p = input.positionOS.xyz, n = input.normalOS, t = input.tangentOS.xyz;
            #if defined(_SWAY)
                Sway(p, n, t, input.pivot, input.sway);
            #endif
                a.positionOS = float4(p, 1.0);
                a.normalOS = n;
                a.tangentOS = float4(t, input.tangentOS.w);
                a.texcoord = input.texcoord;
                a.staticLightmapUV = input.staticLightmapUV;
                return LitPassVertex(a);
            }

            void SwayLitFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
            #if defined(_SWAY_TWO_SIDED)
                input.normalWS = IS_FRONT_VFACE(face, input.normalWS, -input.normalWS);
            #endif
                LitPassFragment(input, outColor
            #ifdef _WRITE_RENDERING_LAYERS
                    , outRenderingLayers
            #endif
                );
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SwayDepthVertex
            #pragma fragment DepthOnlyFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _SWAY
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"

            struct SwayDepthAttributes
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float2 texcoord : TEXCOORD0;
                float4 pivot : TEXCOORD2;
                float4 sway : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings SwayDepthVertex(SwayDepthAttributes input)
            {
                Attributes a = (Attributes)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, a);
                float3 p = input.position.xyz, n = input.normal, t = float3(1, 0, 0);
            #if defined(_SWAY)
                Sway(p, n, t, input.pivot, input.sway);
            #endif
                a.position = float4(p, 1.0);
                a.texcoord = input.texcoord;
                return DepthOnlyVertex(a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
