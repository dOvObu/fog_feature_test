Shader "Custom/UnlitTwoToneDitherSimple"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MaxDistance("Max distance", float) = 100
        
        _DensityThreshold("Density threshold", Range(0, 1)) = 0
        _Density("Density", Range(0, 2)) = 0.3
        _HeightStart("Height start (Y)", Float) = 0
        _HeightFalloff("Height falloff", Range(0.1, 10)) = 2
        
        [HDR]_LightContribution("Light contribution", Color) = (1, 1, 1, 1)
        _LightScattering("Light scattering", Range(0, 0.998)) = 0.674
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma enable_d3d11_debug_symbols

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            
            #define MAX_STEPS 4
            
            
            
            half4 _Color;
            half _MaxDistance;
            half _DensityThreshold;
            half4 _LightContribution;
            half _LightScattering;
            
            half  _StepSize;
            half _Density;
            half _HeightStart;
            half _HeightFalloff;

            inline half schlick(half mu, half g) { // mu = dot(view, light)
                half k = 1.0 - g*g;
                return k / (4.0*PI*pow(1.0 + g*mu, 2.0));
            }
            inline half height_density(half y)
            {
                // простая экспонента по высоте (mobile-friendly)
                half h = max( (half)0, y - _HeightStart);
                return _Density * exp( -h * _HeightFalloff ) * 0.299 * saturate(1 - _DensityThreshold);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float depth = SampleSceneDepth(IN.texcoord);
                float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);

                float3 entryPoint = _WorldSpaceCameraPos;
                float3 viewDir = worldPos - _WorldSpaceCameraPos;
                float viewLength = length(viewDir);
                float3 rayDir = normalize(viewDir);

                half distLimit = min(viewLength, _MaxDistance);
                half distTravelled = 1;
                float transmittance = 1;
                half4 fogCol = _Color;
                half stepSize = -(distTravelled - distLimit) / MAX_STEPS;

                [unroll]
                for (int i = 0; i < MAX_STEPS; i++)
                {
                    float3 rayPos = entryPoint + rayDir * distTravelled;
                    half density = height_density(rayPos.y);
                    
                    Light mainLight = GetMainLight(TransformWorldToShadowCoord(rayPos));
                    fogCol.rgb += mainLight.color.rgb * _LightContribution.rgb * schlick(dot(rayDir, -mainLight.direction), _LightScattering) * density * mainLight.shadowAttenuation * stepSize;
                    transmittance *= exp(-density * stepSize);

                    distTravelled += stepSize;
                }

                half4 result;
                result.rgb = fogCol.rgb;
                result.a   = 1.0h - saturate(transmittance);
                
                //return half4(result.aaa,1);
                return result; //lerp(col, fogCol, 1.0 - saturate(transmittance));
            }
            ENDHLSL
        }

        // Second pass: upsample fog and blend with scene color
        Pass
        {
            Name "FOG_COMPOSITE"
            Cull Off ZTest Always ZWrite Off Blend Off

            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragComposite
            #pragma enable_d3d11_debug_symbols

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define DEPTH_AWARE_UPSCALE

            #ifdef DEPTH_AWARE_UPSCALE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            TEXTURE2D_X(_CameraTexture);
            SAMPLER(sampler_CameraTexture);
            
            TEXTURE2D_X(_FogLow);
            SAMPLER(sampler_FogLow);
            SAMPLER(sampler_BlitTexture);

            Varyings VertFullScreen(uint vertexID : SV_VertexID)
            {
                Varyings OUT;
                // Full-screen triangle covering the viewport
                half2 pos;
                pos.x = (vertexID == 1) ? 3.0 : -1.0;
                pos.y = (vertexID == 2) ? 3.0 : -1.0;
                OUT.positionCS = float4(pos, 0.0, 1.0);
                OUT.texcoord = pos * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                OUT.texcoord.y = 1- OUT.texcoord.y;
                #endif
                return OUT;
            }
            
            half4 FragComposite(Varyings IN) : SV_Target
            {
                #ifdef DEPTH_AWARE_UPSCALE
                
                // Depth at high res fragment
                half highZ = Linear01Depth(SampleSceneDepth(IN.texcoord), _ZBufferParams);
                
                half bestWeight = 0;
                half4 fog = 0;

                half4 offset[] = {
                    _FogLow.Sample(sampler_FogLow, IN.texcoord, int2(0,1)),
                    _FogLow.Sample(sampler_FogLow, IN.texcoord, int2(0,-1)),
                    _FogLow.Sample(sampler_FogLow, IN.texcoord, int2(1,0)),
                    _FogLow.Sample(sampler_FogLow, IN.texcoord, int2(-1,0))
                };

                [unroll]
                for (int i = 0; i < 4; ++i)
                {
                    half fogZ = offset[i].a;
                    half weight = 1.0 / (abs(fogZ - highZ) + 0.001);
                    fog += offset[i] * weight;
                    bestWeight += weight;
                }
                fog /= bestWeight;
                
                #else
                half4 fog = SAMPLE_TEXTURE2D_X(_FogLow, sampler_FogLow, IN.texcoord);
                #endif

                
                // Sample the original scene color and the fog low-res texture
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_CameraTexture, sampler_CameraTexture, IN.texcoord);
                
                // Linearly blend scene with fog using fog alpha
                half3 blendedRGB = lerp(sceneColor.rgb, fog.rgb, fog.a);

                // Preserve original scene alpha (if any) or set to 1 for full opacity
                return half4(blendedRGB, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
