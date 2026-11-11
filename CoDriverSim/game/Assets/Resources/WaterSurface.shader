Shader "CoDriver/WaterSurface"
{
    Properties { _BaseColor("Water", Color) = (.055,.15,.16,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS:TEXCOORD0; half fog:TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p=i.positionWS.xz;
                float t=_Time.y;
                // Small intersecting wind ripples; no large ocean displacement
                // or expensive planar camera is needed for the town's channel.
                float a=dot(p,float2(2.1,1.4))+t*.7;
                float b=dot(p,float2(-4.3,3.2))-t*1.1;
                float c=dot(p,float2(9.2,5.8))+t*1.8;
                float2 slope=.025*cos(a)*float2(2.1,1.4)+.009*cos(b)*float2(-4.3,3.2)
                    +.0025*cos(c)*float2(9.2,5.8);
                half3 n=normalize(half3(-slope.x,1,-slope.y));
                half3 v=GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fresnel=.025+.975*pow(1-saturate(dot(n,v)),5);
                half3 r=reflect(-v,n);
                half4 env=SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0,samplerunity_SpecCube0,r,1.4);
                half3 reflection=DecodeHDREnvironment(env,unity_SpecCube0_HDR);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 base=_BaseColor.rgb*(SampleSH(n)+light.color*saturate(dot(n,light.direction))*.35);
                half sparkle=pow(saturate(dot(n,normalize(light.direction+v))),160)*light.shadowAttenuation;
                half3 color=lerp(base,reflection,fresnel)+light.color*sparkle*.45;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
