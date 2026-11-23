using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The PBR materials of every map, made once each from the CC0 texture sets (ambientCG) by name.
/// </summary>
public static class Mats
{
    public const string Textures = "Assets/ThirdParty/Port/Textures";
    private static readonly Dictionary<string, Material> made = new Dictionary<string, Material>();

    private static Texture2D T(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{file}") ?? AssetSetup.ExtraTex(file);

    public static Material Pbr(string name, string set, Color tint, float smoothness = 0.8f, float metallic = 0f)
    {
        if (made.TryGetValue(name, out var m) && m != null) return m;
        // the grass of every map is the rocky grass (rocky_terrain_02) where it is in the project
        if (set == "Grass004" && AssetSetup.ExtraTex("RockyTerrain02_BaseColor.jpg") != null) set = "RockyTerrain02";
        // wooden things are the planks of wood_table, each in its own colour (the tint); not tree trunks
        if ((set == "Wood049" || set == "Planks037A") && !name.Contains("Palm") && !name.Contains("Trunk") && AssetSetup.ExtraTex("WoodTable_BaseColor.jpg") != null) set = "WoodTable";
        var albedo = T(set + "_BaseColor.jpg");
        if (albedo == null) Debug.LogWarning("[Mats] missing texture set " + set);
        return made[name] = AssetSetup.Mat("Port_" + name, tint, smoothness, metallic, albedo, T(set + "_Normal.jpg"), T(set + "_MS.png"));
    }
}
