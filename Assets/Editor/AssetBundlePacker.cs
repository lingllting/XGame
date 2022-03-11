using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using UnityEditor;
using UnityEngine;

public static partial class AssetBundlePacker
{
    public static void InitData()
    {
        s_dirAssetDict.Clear();

        s_typeAssetDirsDict.Clear();
        s_typeAssetDirsDict.Add(eAssetType.NONE, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.TEXT, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.SCRIPT, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.AUDIO, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.ANIM, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.SHADER, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.TEXTURE, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.CONTROLLER, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.MATERIAL, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.FBX, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.PREFAB, new List<string>());
        s_typeAssetDirsDict.Add(eAssetType.SCENE, new List<string>());

        s_assetDependenciesDict.Clear();
        s_bundleDict.Clear();
        s_assetDirBundleNameDict.Clear();
        s_folderAssetDirsDict.Clear();
    }

    [MenuItem("Tools/AssetBundle/2.BuildAssetBundles")]
    public static void BuildAssetBundles()
    {
        PrePack();
        Pack();
        PostPack();
    }

    [MenuItem("Tools/AssetBundle/1.PrePack")]
    public static void PrePack()
    {
        InitData();
        
        PrepareAssets();
        BuildAssetDependency();
        PrepareBundles();
        BuildBundleDependency();
    }

    public static void Pack()
    {
        int nBundlesLength = s_bundleDict?.Count ?? 0;
        if (s_bundleDict != null && s_bundleDict.Count <= 0)
        {
            return;
        }

        int buildBundleIndex = 0;
        AssetBundleBuild[] assetBundleBuildArray = new AssetBundleBuild[nBundlesLength];
        // prepare the assetBundleBuildArray
        if (s_bundleDict != null)
        {
            using Dictionary<string, Bundle>.Enumerator it = s_bundleDict.GetEnumerator();
            while (it.MoveNext())
            {
                Bundle bundle = it.Current.Value;
                if (bundle == null)
                    return;

                if (s_buildBundleHashSet.Contains(bundle.uniqueName))
                {
                    return;
                }

                assetBundleBuildArray[buildBundleIndex].assetBundleName = bundle.uniqueName;
                assetBundleBuildArray[buildBundleIndex].assetNames = bundle.assetList.ToArray();
                s_buildBundleHashSet.Add(bundle.uniqueName);
                buildBundleIndex++;
            }
        }
        
        AssetBundleManifest manifest = null;
        string outputPath = AssetBundleDirectory + EditorUserBuildSettings.activeBuildTarget;
        Debug.Log("Save Bundle Directory: " + outputPath);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        try
        {
            manifest = BuildPipeline.BuildAssetBundles(outputPath, assetBundleBuildArray, BuildAssetBundleOptions.ChunkBasedCompression, EditorUserBuildSettings.activeBuildTarget);
        }
        catch (Exception e)
        {
            Debug.Log("Build Error: " + e.Message);
            EditorUtility.ClearProgressBar();
            throw new Exception(e.Message);
        }

        if (manifest == null)
        {
            Debug.Log("Build AssetBundle Failed!");
        }
        else
        {
            Debug.Log("Build AssetBundle Succeeded!");
        }
    }

    public static void PostPack()
    {
        using Dictionary<string, Bundle>.Enumerator it = s_bundleDict.GetEnumerator();
        while (it.MoveNext())
        {
            Bundle bundle = it.Current.Value;
            if (bundle != null)
            {
                bundle.locationPath = string.Format("{0}/{1}", AssetBundleDirectory, bundle.uniqueName);
                bundle.isModified = true;

                ProcessBundle(bundle);
            }
        }
        
        SaveAssetXml(AssetBundleDirectory + "/Asset.xml");
        SaveBundleXml(AssetBundleDirectory + "/Bundle.xml");
    }

    private static bool IsValidAssetPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if ((path.Length <= 7) || path.EndsWith(".db") || path.EndsWith(".csv.txt") ||
            path.EndsWith(".meta") || path.Substring(0, 7) != "Assets/" ||
            path.IndexOf("Assets/Editor/", StringComparison.Ordinal) == 0 || path.IndexOf("Assets/Test/", StringComparison.Ordinal) == 0)
        {
            return false;
        }

        if (path.EndsWith(".cs") || path.EndsWith(".js"))
        {
            return false;
        }

        if (IsFileInBlackList(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            Debug.LogError("Pack asset but path doesn't exist!! path::" + path);
            return false;
        }

        return true;
    }
    
    private static eFilterType ConvertFilterType(eFilterType filterType)
    {
        if ((filterType & eFilterType.Shader) == eFilterType.Shader)
        {
            return eFilterType.Shader;
        }
        else if ((filterType & eFilterType.Config) == eFilterType.Config)
        {
            return eFilterType.Config;
        }
        else if ((filterType & eFilterType.Dependence) == eFilterType.Dependence)
        {
            return eFilterType.Dependence;
        }
        else if ((filterType & eFilterType.Directory) == eFilterType.Directory)
        {
            return eFilterType.Directory;
        }

        return eFilterType.None;
    }
    
    private static eAssetType TypeOfAsset(string path)
    {
        if (!string.IsNullOrEmpty(path) && path.LastIndexOf('.') > 0)
        {
            string strExt = path.Substring(path.LastIndexOf('.')).ToLower();
            switch (strExt)
            {
                case ".mask": return eAssetType.NONE;
                case ".fnt":
                case ".bytes":
                case ".xml":
                case ".ttf":
                case ".json":
                case ".asset":
                case ".txt": return eAssetType.TEXT;
                case ".cs": return eAssetType.SCRIPT;
                case ".mp3":
                case ".wav":
                case ".ogg": return eAssetType.AUDIO;
                case ".anim": return eAssetType.ANIM;
                case ".shadervariants":
                case ".cginc":
                case ".cg":
                case ".shader": return eAssetType.SHADER;
                case ".exr":
                case ".tga":
                case ".png":
                case ".PNG":
                case ".jpg": return eAssetType.TEXTURE;
                case ".controller": return eAssetType.CONTROLLER;
                case ".mat": return eAssetType.MATERIAL;
                case ".FBX":
                case ".fbx": return eAssetType.FBX;
                case ".prefab": return eAssetType.PREFAB;
                case ".unity": return eAssetType.SCENE;
            }
        }
        return eAssetType.NONE;
    }

    private static bool IsFileInBlackList(string path)
    {
        return false;
    }
}
