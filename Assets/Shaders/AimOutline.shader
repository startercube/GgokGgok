// 외곽선 — AimOutlineFeature 전용. 설계: 「코드 설계 - 조준 외곽선(투시)」, 「코드 설계 - 튜토리얼 가이드 시각화」
// Pass 0 마스크(투시): 대상 렌더러를 깊이 검사 없이 빨강 채널 1로 그린다 (가려진 부분까지)
// Pass 1 외곽선: 마스크 밖인데 주변(16방향 x 2반지름)에 마스크가 있으면 외곽선 색으로 섞는다. 속은 칠하지 않는다
// Pass 2 마스크(보이는 부분만, 2026-11-02 가이드용): 장면 깊이 텍스처보다 뒤에 있는 조각은 버린다
Shader "Hidden/AimOutline"
{
    Properties
    {
        // SpriteRenderer가 스프라이트 텍스처를 _MainTex로 넘겨준다 → 얼룩 이모지의 투명 부분을 잘라냄.
        // 3D 소품은 기본값(흰색)이라 메시 전체가 마스크
        [HideInInspector] _MainTex ("Sprite Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "AimOutlineMask"
            Blend Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            MaskVaryings MaskVert(MaskAttributes input)
            {
                MaskVaryings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 MaskFrag(MaskVaryings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(alpha - 0.5h);
                return half4(1.0h, 0.0h, 0.0h, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "AimOutlineComposite"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _AimOutlineColor;
            float4 _AimOutlineRadius;   // xy = 외곽선 두께 (마스크 UV 단위)

            half SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r;
            }

            half4 CompositeFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half inside = SampleMask(uv);
                if (inside >= 0.99h) discard;   // 물체 속은 칠하지 않음

                // 원 위 16방향, 두께와 그 절반 두 반지름 → 얇은 부품 주변에서도 외곽선이 끊기지 않음
                half around = 0.0h;
                [unroll]
                for (int k = 0; k < 16; k++)
                {
                    float angle = k * (6.28318530718 / 16.0);
                    float2 offset = float2(cos(angle), sin(angle)) * _AimOutlineRadius.xy;
                    around = max(around, SampleMask(uv + offset));
                    around = max(around, SampleMask(uv + offset * 0.5));
                }

                half alpha = around * (1.0h - inside) * _AimOutlineColor.a;
                if (alpha <= 0.001h) discard;
                return half4(_AimOutlineColor.rgb, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "AimOutlineMaskVisible"
            Blend Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _AimOutlineMaskSize;   // xy = 마스크(= 카메라 대상) 픽셀 크기

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            MaskVaryings MaskVert(MaskAttributes input)
            {
                MaskVaryings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 MaskFrag(MaskVaryings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(alpha - 0.5h);

                // 이 조각과 같은 화면 자리의 장면 깊이 (불투명 물체만 들어 있음 → 유리 같은 투명 대상은 뒤 벽과 비교되어 "보임")
                // SV_Position은 렌더 대상 픽셀 좌표. 마스크와 깊이 텍스처는 둘 다 카메라 해상도의 렌더 텍스처라 같은 좌표계 → 그대로 나누면 됨
                float2 screenUV = input.positionCS.xy / max(_AimOutlineMaskSize.xy, 1.0);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                // 여유: 5cm + 거리의 1% → 몸체에 붙은 모니터 화면, 바닥에 2cm 뜬 얼룩이 사라지지 않게
                clip(sceneEye + 0.05 + fragEye * 0.01 - fragEye);
                return half4(1.0h, 0.0h, 0.0h, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
