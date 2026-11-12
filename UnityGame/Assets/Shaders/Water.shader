// Water for phones: opaque (no sorting, no refraction), small moving waves made from a few sine waves in
// world space (no texture), the sky mirrored at grazing angles (a small panorama of a lakeside sky, darkened
// with the ambient light so the night water stays dark), a bright glint of the sun, fog.
Shader "UA/Water"
{
    Properties
    {
        _Deep ("Deep colour", Color) = (0.03, 0.15, 0.2, 1)
        _Shallow ("Shallow colour", Color) = (0.1, 0.33, 0.38, 1)
        _Scale ("Wave size (bigger = smaller waves)", Float) = 1
        _Speed ("Wave speed", Float) = 1
        _Strength ("Wave height", Float) = 0.3
        _Flow ("Flow (x, z direction, speed)", Vector) = (0, 0, 0, 0)
        _Sky ("Sky panorama (equirectangular)", 2D) = "white" {}
        _SkyAmount ("Sky panorama amount", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Deep, _Shallow;
            float _Scale, _Speed, _Strength;
            float4 _Flow;
            sampler2D _Sky;
            float _SkyAmount;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert (float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.world = mul(unity_ObjectToWorld, vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // slope of one wave: direction, frequency (1/m), amplitude
            float2 Wave(float2 p, float2 dir, float freq, float amp, float t)
            {
                float phase = dot(dir, p) * freq + t * sqrt(freq * 9.8);
                return dir * (freq * amp * cos(phase));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // running water (channels, cascades): the whole wave pattern drifts downstream
                float2 p = (i.world.xz - _Flow.xy * _Flow.z * _Time.y) * _Scale;
                float t = _Time.y * _Speed;
                float2 g = Wave(p, float2(0.8, 0.6), 0.9, 0.05, t)
                         + Wave(p, float2(-0.45, 0.89), 1.7, 0.03, t)
                         + Wave(p, float2(0.97, -0.24), 3.1, 0.016, t)
                         + Wave(p, float2(-0.7, -0.71), 5.3, 0.009, t)
                         + Wave(p, float2(0.2, 0.98), 8.9, 0.005, t);
                float3 n = normalize(float3(-g.x * _Strength * 10, 1, -g.y * _Strength * 10));
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float3 light = normalize(_WorldSpaceLightPos0.xyz);
                // looking down: the colour of the water; looking along it: the sky
                float facing = saturate(dot(n, view));
                float fresnel = 0.04 + 0.96 * pow(1 - facing, 5);
                fixed3 body = lerp(_Deep.rgb, _Shallow.rgb, saturate(view.y * 1.2));
                body *= unity_AmbientEquator.rgb * 1.4 + _LightColor0.rgb * saturate(dot(n, light)) * 0.35;
                fixed3 sky = unity_AmbientSky.rgb * 1.25;
                // the panorama in the reflected direction (upper half), lit like the scene's sky
                float3 r = reflect(-view, n);
                float2 uv = float2(atan2(r.x, r.z) * 0.15915 + 0.5, asin(clamp(r.y, 0.0, 1.0)) * 0.31831 + 0.5);
                fixed3 pano = tex2D(_Sky, uv).rgb * (unity_AmbientSky.rgb * 1.6 + 0.1);
                sky = lerp(sky, pano, _SkyAmount);
                float glint = pow(saturate(dot(reflect(-light, n), view)), 180) * 2.5;
                fixed4 c = fixed4(lerp(body, sky, saturate(fresnel)) + _LightColor0.rgb * glint, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
