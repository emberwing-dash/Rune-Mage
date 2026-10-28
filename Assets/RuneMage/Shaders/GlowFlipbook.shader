// Unlit URP shader used by every magical surface in the game:
//  - flame skins on the zombies / Chandelure (UV-mapped flipbook atlases made from the original baked frames)
//  - the spell flame mesh
//  - the glowing ink trails (1x1 "atlas", vertex colour)
// Mobile friendly: one pass, two texture taps (frame blend), SRP-batcher compatible, single-pass-instanced-stereo ready.
Shader "RuneMage/GlowFlipbook"
{
    Properties
    {
        _MainTex ("Flipbook Atlas", 2D) = "white" {}
        _BaseColor ("Base (charred body) Colour, A = opacity", Color) = (0,0,0,0)
        [HDR] _Tint ("Flame Tint (HDR)", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,6)) = 1
        _Cols ("Columns", Float) = 5
        _Rows ("Rows", Float) = 5
        _FPS ("Frames Per Second", Float) = 18
        _Phase ("Frame Phase", Float) = 0
        _Fade ("Fade", Range(0,1)) = 1
        _Floor ("Minimum flame brightness (ink)", Range(0,1)) = 0
        _UseVertexColor ("Use Vertex Colour (particles, lines)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                half4 _Tint;
                half _Intensity;
                float _Cols;
                float _Rows;
                float _FPS;
                float _Phase;
                half _Fade;
                half _UseVertexColor;
                half _Floor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 SampleFrame(float2 uv, float frame)
            {
                float count = _Cols * _Rows;
                frame = fmod(frame, count);
                float col = fmod(frame, _Cols);
                float row = floor(frame / _Cols);
                // Frame 0 sits in the TOP row of the atlas; texture v runs bottom-up.
                float2 cell = float2(col, _Rows - 1.0 - row);
                float2 p = saturate(uv) * 0.992 + 0.004;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, (p + cell) / float2(_Cols, _Rows));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float t = _Time.y * _FPS + _Phase;
                float f0 = floor(t);
                half4 a = SampleFrame(IN.uv, f0);
                half4 b = SampleFrame(IN.uv, f0 + 1.0);
                half4 flame = lerp(a, b, frac(t));
                flame.rgb = max(flame.rgb, _Floor.xxx);

                half3 glow = flame.rgb * _Tint.rgb * _Intensity;
                half3 rgb = lerp(_BaseColor.rgb, glow, flame.a);
                half alpha = saturate(max(_BaseColor.a, flame.a * _Tint.a));
                // Imported character meshes carry Blender vertex colours (often black/transparent): only particles and lines use them.
                half4 vcol = lerp(half4(1, 1, 1, 1), IN.color, _UseVertexColor);
                half4 col = half4(rgb, alpha) * vcol;
                col.a *= _Fade;
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
