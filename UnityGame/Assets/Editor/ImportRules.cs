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
        else if (assetPath.Contains("ThirdParty/Props/") || assetPath.Contains("ThirdParty/Guns/") || assetPath.Contains("Assets/Weapons/") || assetPath.Contains("ThirdParty/Nature/") || assetPath.Contains("ThirdParty/Cars/"))
        {
            importer.addCollider = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = assetPath.Contains("ThirdParty/Guns/") || assetPath.Contains("Assets/Weapons/");   // gun meshes get new texture coordinates
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
        if (assetPath.Contains("Textures/Extra/"))
        {
            // the textures kept in the project: 512 px at most, small ASTC blocks on phones
            var extra = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            extra.maxTextureSize = 512;
            extra.mipmapEnabled = true;
            extra.anisoLevel = 2;
            if (name.EndsWith("_Normal")) extra.textureType = TextureImporterType.NormalMap;
            if (name.Contains("Sky")) { extra.wrapModeU = TextureWrapMode.Repeat; extra.wrapModeV = TextureWrapMode.Clamp; }
            var phone = extra.GetPlatformTextureSettings("Android");
            phone.overridden = true;
            phone.maxTextureSize = 512;
            phone.format = name.EndsWith("_Normal") ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
            phone.compressionQuality = 50;
            extra.SetPlatformTextureSettings(phone);
            return;
        }
        if (!assetPath.Contains("ThirdParty/") && !assetPath.Contains("Assets/Weapons/") && !assetPath.Contains("Assets/Characters/")) return;
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
        // the guns are small on the screen: half size too
        small |= assetPath.Contains("Assets/Weapons/");
        android.maxTextureSize = small ? 512 : 1024;
        // colour keeps the finer 6x6 blocks; normal and roughness maps can be coarser (smaller APK)
        android.format = file.EndsWith("_Normal") || file.EndsWith("_MS") ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
        android.compressionQuality = 50;
        if (assetPath.Contains("Assets/Characters/") && !file.EndsWith("_Normal"))
        {
            // the characters' faces are small on their 1024 texture: the finest blocks, sharper mip maps, and
            // never halved by the low quality setting
            importer.anisoLevel = 4;
            importer.mipMapBias = -0.3f;
            importer.ignoreMipmapLimit = true;
            android.format = TextureImporterFormat.ASTC_4x4;
            android.compressionQuality = 100;
        }
        importer.SetPlatformTextureSettings(android);
    }

    private void OnPreprocessAudio()
    {
        var importer = (AudioImporter)assetImporter;
        bool music = assetPath.Contains("/music_");
        importer.forceToMono = !music;   // the music keeps its stereo
        var settings = importer.defaultSampleSettings;
        // the sounds ready to play at once; the two minutes of music stay compressed in memory (2 MB, not 23)
        settings.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = music ? 0.6f : 0.5f;
        importer.defaultSampleSettings = settings;
    }
}
