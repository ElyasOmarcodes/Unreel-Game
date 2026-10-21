using UnityEditor;
using UnityEngine;

/// <summary>Import settings for the downloaded CC0 assets and our sounds, applied on their first import.</summary>
public class ImportRules : AssetPostprocessor
{
    // bump to re-import everything with changed rules (Library caches keep old imports otherwise)
    public override uint GetVersion() => 3;

    private void OnPreprocessModel()
    {
        var importer = (ModelImporter)assetImporter;
        if (assetPath.Contains("ThirdParty/City/"))
        {
            importer.addCollider = true;
            importer.isReadable = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.importBlendShapes = false;
        }
        else if (assetPath.Contains("ThirdParty/Props/") || assetPath.Contains("ThirdParty/Guns/") || assetPath.Contains("ThirdParty/Nature/") || assetPath.Contains("ThirdParty/Cars/"))
        {
            importer.addCollider = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = assetPath.Contains("ThirdParty/Guns/");   // gun meshes get new texture coordinates
            importer.meshCompression = ModelImporterMeshCompression.Medium;
        }
        else if (assetPath.Contains("ThirdParty/UBC/"))
        {
            // realistic base characters and their hair: same skeleton as the animation library
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true;   // the clothes are built from the body mesh
            importer.meshCompression = ModelImporterMeshCompression.Low;
        }
        else if (assetPath.Contains("ThirdParty/UAL/"))
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true;   // the clothes are built from the body mesh
            importer.meshCompression = ModelImporterMeshCompression.Low;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        }
    }

    private void OnPreprocessTexture()
    {
        if (assetPath.Contains("Resources/Icons/"))
        {
            var icon = (TextureImporter)assetImporter;
            icon.alphaIsTransparency = true;
            icon.mipmapEnabled = true;
            icon.textureCompression = TextureImporterCompression.Uncompressed;
            icon.wrapMode = TextureWrapMode.Clamp;
            return;
        }
        if (!assetPath.Contains("ThirdParty/")) return;
        var importer = (TextureImporter)assetImporter;
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        importer.maxTextureSize = 1024;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 2;
        if (file.EndsWith("_Normal"))
        {
            importer.textureType = TextureImporterType.NormalMap;
        }
        else if (file.EndsWith("_MS"))
        {
            importer.sRGBTexture = false;
        }
        else if (assetPath.Contains("ThirdParty/Nature/") && (file.StartsWith("Leaves") || file.StartsWith("Leaf") || file.StartsWith("Grass") || file.StartsWith("Flowers")))
        {
            // leaf cards: alpha-tested, keep the alpha in the mip maps
            importer.alphaIsTransparency = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.45f;
        }
        var android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        // interiors and the village ground and mud are seen up close only in small patches: half size
        bool small = file.Contains("interior") || file.Contains("Ornaments") || file.StartsWith("Clay") || file.StartsWith("Ground1") || file.StartsWith("Gravel") || file.StartsWith("Rock0");
        android.maxTextureSize = small ? 512 : 1024;
        // colour keeps the finer 6x6 blocks; normal and roughness maps can be coarser (smaller APK)
        android.format = file.EndsWith("_Normal") || file.EndsWith("_MS") ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
        android.compressionQuality = 50;
        importer.SetPlatformTextureSettings(android);
    }

    private void OnPreprocessAudio()
    {
        var importer = (AudioImporter)assetImporter;
        importer.forceToMono = true;
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.5f;
        importer.defaultSampleSettings = settings;
    }
}
