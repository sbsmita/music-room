// E3 (10-upgrade/experience.md section 5): soft contact shadows under the fingertips and the stick tips, every blob in
// ONE dynamic mesh (ContactShadowPool), one multiply-blended draw. Each occluder is a sphere of radius r whose centre is
// h over the receiving plane; the cosine-weighted ambient occlusion it casts at horizontal distance rho is
//   AO = cos(alpha) (r / d)^2 = r^2 h / (h^2 + rho^2)^1.5     (Quilez, "Sphere ambient occlusion")
// and the drawn darkness is min(_MaxDark, A * AO), so the shadow darkens and tightens as the finger comes down.
// Vertex data: uv0 = (rho_x, rho_y) in metres from the blob centre, uv1 = (h_eff, r, A, clipHalf or 1e3), uv2 = the
// clip's centre line along uv0.x; on a pressed key the shadow is clipped along the key's width to half its visible
// width either side of the key's centre line (not of the fingertip). Queue Transparent-9 (2991: after the
// opaques AND the printed key labels at 2990, so a finger's shadow darkens the sticker under it too; before the menu
// face 2999, the glass 3000 and the additive glows 3010; the spec's 2980 left every sticker bright inside the shadow);
// Blend DstColor Zero multiplies the colour already there and keeps its alpha (output alpha 1), so in MR the virtual key
// under a shadow never turns see-through. Single-pass instanced stereo.
Shader "MusicRoom/ContactShadow"
{
    Properties
    {
        _MaxDark ("Max darkness", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-9" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _MaxDark;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 rho : TEXCOORD0;
                float4 blob : TEXCOORD1;
                float clipCentre : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 rho : TEXCOORD0;
                float4 blob : TEXCOORD1;
                float clipCentre : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.rho = v.rho;
                o.blob = v.blob;
                o.clipCentre = v.clipCentre;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float h = i.blob.x, r = i.blob.y, A = i.blob.z, clipHalf = i.blob.w;
                if (abs(i.rho.x - i.clipCentre) > clipHalf) discard;
                // (h^2 + rho^2)^1.5 as q sqrt(q): the same number, without pow's "negative base" warning on the device compilers.
                float q = h * h + dot(i.rho, i.rho);
                float d = min(_MaxDark, A * r * r * h / (q * sqrt(q)));
                return half4((half)(1.0 - d), (half)(1.0 - d), (half)(1.0 - d), 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
