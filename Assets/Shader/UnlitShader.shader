Shader "Custom/UnlitTwoToneDitherSimple"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MaxDistance("Max distance", float) = 100
        _NoiseOffset("Noise offset", float) = 0
        
        _NoiseTiling("Noise tiling", float) = 1
        _DensityThreshold("Density threshold", Range(0, 1)) = 0.1
        
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

//#define LOW_DEVICE
#define MID_DEVICE
//#define HIGHT_DEVICE
//#define ULTRA_DEVICE

            
#ifdef ULTRA_DEVICE
            #define MAX_STEPS 16
#endif
#ifdef HIGHT_DEVICE
            #define MAX_STEPS 8
#endif
#ifdef MID_DEVICE
            #define MAX_STEPS 16
#endif
#ifdef LOW_DEVICE
            #define MAX_STEPS 16
#endif

            
            
#ifdef MID_DEVICE
#define SHADOW_STRIDE 4
#endif
#ifdef LOW_DEVICE
#define SHADOW_STRIDE 6
#endif

            
            
            half4 _Color;
            half _MaxDistance;
            half _NoiseOffset;
            half _DensityThreshold;
            half _NoiseTiling;
            half4 _LightContribution;
            half _LightScattering;
            
            half  _StepSize;
            half _Density;
            half _HeightStart;
            half _HeightFalloff;

#ifdef SHADOW_STRIDE
            // anisotropy g = _LightScattering (если используешь Шклика)
            inline half phase_schlick(half mu, half g) {
                half k = (half)1 - g*g;
                half d = (half)1 + g*mu;
                d *= d;
                return k / ((half)4*PI * d);
            }
#else
            inline half schlick(half mu, half g) { // mu = dot(view, light)
                half k = 1.0 - g*g;
                return k / (4.0*PI*pow(1.0 + g*mu, 2.0));
            }
#endif            
            inline half height_density(half y)
            {
                // простая экспонента по высоте (mobile-friendly)
                half h = max( (half)0, y - _HeightStart);
                return _Density * exp( -h * _HeightFalloff ) * saturate(1 - _DensityThreshold) * 0.299;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                float depth = SampleSceneDepth(IN.texcoord);
                float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);

                float3 entryPoint = _WorldSpaceCameraPos;
                float3 viewDir = worldPos - _WorldSpaceCameraPos;
                float viewLength = length(viewDir);
                float3 rayDir = normalize(viewDir);

                float2 pixelCoords = IN.texcoord * _BlitTexture_TexelSize.zw;
                half distLimit = min(viewLength, _MaxDistance);
                half distTravelled = InterleavedGradientNoise(pixelCoords, (int)(_Time.y / max(HALF_EPS, unity_DeltaTime.x))) * _NoiseOffset;
                float transmittance = 1;
                half4 fogCol = _Color;
                half stepSize = -(distTravelled - distLimit) / MAX_STEPS;

#ifdef SHADOW_STRIDE
                Light ml = GetMainLight();                         // без шадоу-координат
                half3 L      = normalize(-(half3)ml.direction);
                half3 Lcolor = (half3)ml.color * (half3)_LightContribution.rgb;
                                
                half phase = phase_schlick( dot((half3)rayDir, L), (half)_LightScattering );
                                

                half shadowCached = (half)1;

                [loop]
                for (int i = 0; i < MAX_STEPS; ++i)
                {
                    float3 p = entryPoint + rayDir * distTravelled;
                    half density = height_density( (half)p.y );
                    if (density > (half)0) {
                        if ( (i & (SHADOW_STRIDE - 1)) == 0 ) {  // дёшево вместо i % N
                            float4 sc = TransformWorldToShadowCoord(p);
                            shadowCached = MainLightRealtimeShadow(sc);
                        }

                        // интеграция
                        half attenStep = exp( -density * stepSize );
                        half scatter   = density * stepSize;

                        fogCol.rgb   += Lcolor * phase * scatter * shadowCached;
                        transmittance *= attenStep;

                        // ранний выход
                        if (transmittance < (half)0.02) break;
                    }
                    distTravelled += stepSize;
                }
#else
                [loop]
                for (int i = 0; i < MAX_STEPS; i++)
                {
                    if(distTravelled > distLimit)
                    {
                        break;
                    }
                    float3 rayPos = entryPoint + rayDir * distTravelled;
                    half density = height_density(rayPos.y);
                    if (density > (half)0)
                    {
                        Light mainLight = GetMainLight(TransformWorldToShadowCoord(rayPos));
                        fogCol.rgb += mainLight.color.rgb * _LightContribution.rgb * schlick(dot(rayDir, -mainLight.direction), _LightScattering) * density * mainLight.shadowAttenuation * stepSize;
                        transmittance *= exp(-density * stepSize);
                    }
                    distTravelled += stepSize;
                }
#endif           
                return lerp(col, fogCol, 1.0 - saturate(transmittance));
            }
            ENDHLSL
        }
    }
}
