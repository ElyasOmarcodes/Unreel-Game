// Trees and grass drawn as pictures (impostors): crossed cards cut out by alpha, lit as a round crown
// (the normal leans up and out from the trunk, the same on both sides of a card), with the scene's
// shadows, fog and a little warmth of their own. Cheap on phones: a tree is four cards.
Shader "UA/Impostor"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Atlas", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.45
        _Wrap ("Light wrap", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Wrapped alphatest:_Cutoff addshadow vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Wrap;

        struct Input
        {
            float2 uv_MainTex;
        };

        void vert (inout appdata_full v)
        {
            // a crown is lit like a ball: up, and out from the trunk where the card is
            float3 outward = float3(v.vertex.x, 0, v.vertex.z);
            float len = length(outward);
            outward = len > 0.001 ? outward / len : float3(0, 0, 0);
            v.normal = normalize(float3(0, 1, 0) * 0.8 + outward * 0.6);
        }

        // wrapped diffuse: the shaded side of a crown stays soft, as leaves let light through
        half4 LightingWrapped (SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
            half diff = saturate((ndl + _Wrap) / (1 + _Wrap));
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (diff * atten);
            c.a = s.Alpha;
            return c;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    Fallback "Legacy Shaders/Transparent/Cutout/VertexLit"
}
