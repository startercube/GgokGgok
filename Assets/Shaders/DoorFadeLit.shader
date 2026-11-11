// URP Lit 조명을 그대로 쓰되, 오브젝트 높이 기준으로 아래→위 그라데이션되며 사라지는 문 셰이더 (2026-10-21 할로윈 창살 문)
// _Fade 0 = 온전, 1 = 완전히 사라짐. DoorController(fadeOut)가 MaterialPropertyBlock으로 _Fade·_FadeMinY·_FadeMaxY를 넣는다
Shader "Custom/DoorFadeLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0)

        [Header(Fade)]
        _Fade("Fade (0 = 보임, 1 = 사라짐)", Range(0.0, 1.0)) = 0.0
        _FadeSoftness("그라데이션 폭 (높이 비율)", Range(0.01, 1.0)) = 0.35
        _FadeMinY("메시 바닥 높이 (오브젝트 공간)", Float) = -0.5
        _FadeMaxY("메시 꼭대기 높이 (오브젝트 공간)", Float) = 0.5
        [HDR] _FadeEdgeColor("경계선 빛 (검정 = 끔)", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex LitPassVertex
            #pragma fragment DoorFadeFragment

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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            #include "DoorFade.hlsl"

            // LitPassFragment와 같은 흐름 + 높이 기반 알파
            void DoorFadeFragment(Varyings input, out half4 outColor : SV_Target0)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half fade = DoorFadeAlpha(input.positionWS);

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));
                InitializeBakedGIData(input, inputData);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb += _FadeEdgeColor.rgb * (fade * (1.0 - fade) * 4.0);   // 경계선에서만 빛남
                color.rgb = MixFog(color.rgb, inputData.fogCoord);

                outColor = half4(color.rgb, surfaceData.alpha * fade);
            }
            ENDHLSL
        }

        // 그림자도 같은 높이 기준으로 깎여 함께 사라진다
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DoorFadeShadowVertex
            #pragma fragment DoorFadeShadowFragment

            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            #include "DoorFade.hlsl"

            struct DoorFadeShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DoorFadeShadowVaryings DoorFadeShadowVertex(Attributes input)
            {
                DoorFadeShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 DoorFadeShadowFragment(DoorFadeShadowVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(DoorFadeAlpha(input.positionWS) - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
