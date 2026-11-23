// Ground of the open maps: four tiling textures (grass, dirt, gravel, rock) blended by the mesh's
// vertex colour (r dirt, g grass, b gravel, a rock), projected from above in world space. Paved
// paths on top where the path mask says so: the stones come in one by one at its soft edge (the
// lighter stones first, the joints last), over the trodden dirt beside them.
Shader "UA/Ground"
{
    Properties
    {
        _Grass ("Grass", 2D) = "white" {}
        _Dirt ("Dirt", 2D) = "white" {}
        _Gravel ("Gravel", 2D) = "white" {}
        _Rock ("Rock", 2D) = "white" {}
        _GrassTint ("Grass tint", Color) = (1,1,1,1)
        _Tiles ("Tile size: grass, dirt, gravel, rock (m)", Vector) = (5,4,3,8)
        _Path ("Path stones", 2D) = "gray" {}
        _PathMask ("Path mask (r)", 2D) = "black" {}
        _PathArea ("Path mask area: x0, z0, size; stone tile (m)", Vector) = (0,0,1,2.5)
        _PathTint ("Path stone tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0

        sampler2D _Grass, _Dirt, _Gravel, _Rock, _Path, _PathMask;
        fixed4 _GrassTint, _PathTint;
        float4 _Tiles, _PathArea;

        struct Input
        {
            float3 worldPos;
            float4 color : COLOR;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            float2 p = IN.worldPos.xz;
            fixed3 grass = tex2D(_Grass, p / _Tiles.x).rgb * _GrassTint.rgb;
            fixed3 dirt = tex2D(_Dirt, p / _Tiles.y).rgb;
            fixed3 gravel = tex2D(_Gravel, p / _Tiles.z).rgb;
            // rock on steep banks: projected from the side as well, to avoid stretching
            fixed3 rock = tex2D(_Rock, (p + IN.worldPos.y) / _Tiles.w).rgb;
            float4 w = IN.color;
            float sum = max(0.001, w.r + w.g + w.b + w.a);
            fixed3 ground = (dirt * w.r + grass * w.g + gravel * w.b + rock * w.a) / sum;
            float mask = tex2D(_PathMask, (p - _PathArea.xy) / _PathArea.z).r;
            fixed3 stone = tex2D(_Path, p / _PathArea.w).rgb * _PathTint.rgb;
            float lum = dot(stone, fixed3(0.3, 0.59, 0.11));
            float paved = saturate((mask - 0.5) * 3.0 + (lum - 0.45) * 2.0 + 0.5);
            o.Albedo = lerp(ground, stone, paved * step(0.01, mask));
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
