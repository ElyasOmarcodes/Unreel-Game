// Soft light halo around a lamp at night: a quad that always faces the camera around its own centre,
// added on top of the picture (no light source, so it costs almost nothing on phones).
Shader "UA/Glow"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.7, 0.35, 1)
        _Size ("Size (m)", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Size;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert (float4 vertex : POSITION)
            {
                v2f o;
                float3 centre = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 view = mul(UNITY_MATRIX_V, float4(centre, 1)).xyz;
                // a little towards the camera so walls behind the lamp do not cut the halo
                view.z += 0.3;
                view.xy += vertex.xy * _Size;
                o.pos = mul(UNITY_MATRIX_P, float4(view, 1));
                o.uv = vertex.xy * 2;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float d = saturate(1 - length(i.uv));
                fixed4 c = _Color * (d * d * d);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0, 0, 0, 0));
                return c;
            }
            ENDCG
        }
    }
}
