// The characters: the Standard lighting with the same properties (the materials keep their textures when
// switched to it), plus what keeps a far player readable against the map, as most shooters do: a light rim
// round the outline and a little light of its own, both growing with the distance (nothing close by), and
// no distance fog (a far body does not melt into the haze).
Shader "UA/Character"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossMapScale ("Smoothness Scale", Range(0,1)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _BumpScale ("Normal Scale", Float) = 1
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _RimColor ("Rim Colour", Color) = (0.85, 0.9, 1, 1)
        _RimStrength ("Rim Strength", Float) = 0.75
        _Lift ("Own Light", Float) = 0.22
        _RimStart ("Rim Starts At (m)", Float) = 14
        _RimRange ("Rim Full After (m)", Float) = 30
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow nofog
        #pragma multi_compile __ _NORMALMAP
        #pragma multi_compile __ _METALLICGLOSSMAP
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _MetallicGlossMap;
        fixed4 _Color;
        half _Glossiness, _GlossMapScale, _Metallic, _BumpScale;
        fixed4 _RimColor;
        half _RimStrength, _Lift, _RimStart, _RimRange;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
        #ifdef _NORMALMAP
            float3 n = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            n.xy *= _BumpScale;
            o.Normal = normalize(n);
        #endif
        #ifdef _METALLICGLOSSMAP
            fixed4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Metallic = mg.r;
            o.Smoothness = mg.a * _GlossMapScale;
        #else
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
        #endif
            float far = saturate((distance(_WorldSpaceCameraPos, IN.worldPos) - _RimStart) / max(1.0, _RimRange));
            float rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            rim = rim * rim * rim;
            o.Emission = (_RimColor.rgb * rim * _RimStrength + c.rgb * _Lift) * far;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
