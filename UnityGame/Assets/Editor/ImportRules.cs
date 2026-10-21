using UnityEditor;
using UnityEngine;

/// <summary>Import settings for the downloaded CC0 assets and our sounds, applied on their first import.</summary>
public class ImportRules : AssetPostprocessor
{
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
        else if (assetPath.Contains("ThirdParty/UAL/"))
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Low;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        }
    }

    private void OnPreprocessTexture()
    {
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
        var android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = file.Contains("interior") || file.Contains("Ornaments") ? 512 : 1024;
        android.format = TextureImporterFormat.ASTC_6x6;
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
