// Mud and straw plaster (kahgel) for village buildings. The texture is projected in world space
// from three sides, so walls, roofs and parapets made of separate pieces read as one plastered
// surface. A second, large-scale texture adds patches of darker and lighter mud, and the bottom of
// every wall gets a band of damp dirt.
Shader "UA/Mud"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Plaster", 2D) = "white" {}
        _Patches ("Patches (large scale)", 2D) = "gray" {}
        _Tile ("Tile size (m)", Float) = 2.2
        _PatchTile ("Patch tile size (m)", Float) = 9
        _GroundY ("Ground height", Float) = 0
        _Dirt ("Damp base colour", Color) = (0.55,0.45,0.36,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex, _Patches;
        fixed4 _Color, _Dirt;
        float _Tile, _PatchTile, _GroundY;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        fixed3 Tri(sampler2D tex, float3 p, float3 w, float tile)
        {
            return tex2D(tex, p.zy / tile).rgb * w.x + tex2D(tex, p.xz / tile).rgb * w.y + tex2D(tex, p.xy / tile).rgb * w.z;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            float3 w = pow(abs(IN.worldNormal), 4);
            w /= (w.x + w.y + w.z);
            fixed3 plaster = Tri(_MainTex, IN.worldPos, w, _Tile);
            fixed3 patch = Tri(_Patches, IN.worldPos, w, _PatchTile);
            float lum = dot(patch, fixed3(0.3, 0.59, 0.11));
            fixed3 c = plaster * _Color.rgb * lerp(0.82, 1.15, lum);
            // damp, dirty foot of the walls
            float base = saturate(1 - (IN.worldPos.y - _GroundY) / 0.6);
            c = lerp(c, c * _Dirt.rgb * 1.4, base * (1 - w.y));
            o.Albedo = c;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
