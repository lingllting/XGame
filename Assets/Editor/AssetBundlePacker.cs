using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using UnityEditor;
using UnityEngine;

public class Asset
{
    public const int BIG_LIMIT_TIME = 20000;
    public string name = string.Empty;
    public string md5 = string.Empty;
    public string metaMD5 = string.Empty;
    public long size = 0;
    public int refCount = 0;
    public eAssetType type = eAssetType.NONE;
    public bool isBig = true;
    public float floatParam = 0;
    public int chapter = int.MaxValue;
    public bool isValidChapter = false;
    public bool isBigChecked = false;
    public List<string> directDependencyList = new List<string>();
    public eMerageType optimize = eMerageType.None;
    public string directory = string.Empty;
    public eFilterType filterType = eFilterType.None;
    public eFilterType uniqueFilterType = eFilterType.None;
    public bool isAtlas = false;
    public bool isInitAsset = false;
}

public class Bundle
{
    public string uniqueName = string.Empty;
    public string srcMD5 = string.Empty;
    public string decMD5 = string.Empty;
    public long srcSize = 0;    // 压缩前大小
    public long dstSize = 0;    // 压缩后大小
    public int chapter = int.MaxValue;
    public int iLayer = 0;
    public int nBatchCount = 0; // 加载时该加载批次需要加载依赖包的个数
    public bool isScene = false;
    public string scenePath = string.Empty;
    public bool isNeedRebuild = false;
    public bool isModified = false;
    public bool isForceRebuild = false;
    public bool isInitBundle = false;
    public bool isOutAPKBundle = false;
    public List<string> assetList = new List<string>();
    public List<string> dependentBundleList = new List<string>();
    public List<Bundle> lstParents = null;
    public List<Bundle> lstChildren = new List<Bundle>(4);

    // thread
    public string locationPath = string.Empty;
    public Action<Bundle> CompressOverAction = null;

    public bool isDirectory = false;                   //是否打包文件夹下面的资源，加快设置AssetbundleName
    public string strBundlePath = string.Empty;        //对应的路径

    public string smartName = string.Empty;

    public eStatisBundleType staticBundleType = eStatisBundleType.NONE;
    public eBundleType bundleType = eBundleType.None;
}

public static partial class AssetBundlePacker
{
    private static Dictionary<string, Asset> s_dirAssetDict = new Dictionary<string, Asset>();                                  //dir - asset 
    private static Dictionary<eAssetType, List<string>> s_typeAssetDirsDict = new Dictionary<eAssetType, List<string>>();       //type - asset dirs
    private static Dictionary<string, List<string>> s_assetDependenciesDict = new Dictionary<string, List<string>>();           //asset dir - direct dependencies' dirs
    private static Dictionary<string, string> s_assetDirBundleNameDict = new Dictionary<string, string>();                      //asset dir - bundle unique name
    private static Dictionary<string, List<string>> s_folderAssetDirsDict = new Dictionary<string, List<string>>();             //folder dir - asset dirs
    
    private static Dictionary<string, Bundle> s_bundleDict = new Dictionary<string, Bundle>();                                  //bundle unique name - bundle
    private static HashSet<string> s_buildBundleHashSet = new HashSet<string>();                                                //unique names of bundles to be built
    
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

    [MenuItem("Tools/AssetBundle/0.AnalyseAssetDependence")]
    public static void AnalyseAssetDependence()
    {
        InitData();
        string path = "Assets/Prefabs";
        EditorUtility.DisplayProgressBar("Analysis", "ImportAssetsFromDirectory", 0.3f);
        ImportAssetsFromDirectory(path);
        EditorUtility.DisplayProgressBar("Analysis", "BuildDirectDependencies", 0.6f);
        BuildDirectDependencies();
        EditorUtility.DisplayProgressBar("Analysis", "SaveAssetXml", 0.8f);
        string output = Application.dataPath + "/../Analysis";
        if (!Directory.Exists(output))
        {
            Directory.CreateDirectory(output);
        }
        SaveAssetXml(output + "/AssetDependence" + ".xml");
        EditorUtility.ClearProgressBar();
    }

    [MenuItem("Tools/AssetBundle/1.AnalyseBundleDependence")]
    public static void AnalyseBundleDependence()
    {
        AnalyseAssetDependence();
        SetupBundles();
        BuildBundleDependency();
        
        string output = Application.dataPath + "/../Analysis";
        if (!Directory.Exists(output))
        {
            Directory.CreateDirectory(output);
        }
        SaveBundleXml(output + "/BundleDependence" + ".xml");
    }

    [MenuItem("Tools/AssetBundle/2.BuildAssetBundles")]
    public static void BuildAssetBundles()
    {
        InitData();
        string path = "Assets/Prefabs";
        ImportAssetsFromDirectory(path);
        BuildDirectDependencies();
        SetupBundles();
        BuildBundleDependency();
        
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
        string outputPath = Application.dataPath + "/../AssetBundles/" + EditorUserBuildSettings.activeBuildTarget;
        Debug.Log("Save Bundle Directory: " + outputPath);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        try
        {
            manifest = BuildPipeline.BuildAssetBundles(outputPath, assetBundleBuildArray, 
                BuildAssetBundleOptions.ChunkBasedCompression, 
                EditorUserBuildSettings.activeBuildTarget);
        }
        catch (Exception e)
        {
            Debug.Log("打包出现异常，请检查: " + e.Message);
            EditorUtility.ClearProgressBar();
            throw new Exception(e.Message);
        }

        if (manifest == null)
        {
            Debug.Log("AssetBundle打包失败");
        }
        else
        {
            Debug.Log("AssetBundle打包完毕");
        }
    }

    public static void SetupBundles()
    {
        SetupAtlasBundle();
        SetupCommonBundle();
        SetupShadersBundle();
        SetupFbxBundle();
        SetupAnimationBundle();
        SetupSceneBundle();
    }

#region Private methods of analysing dependencies.
    private static void ImportAssetsFromDirectory(string dir)
    {
        if (!s_folderAssetDirsDict.ContainsKey(dir))
        {
            List<string> assetDirList = new List<string>();
            s_folderAssetDirsDict[dir] = assetDirList;

            string dirPath = Application.dataPath + "/" + dir.Replace("Assets/", "");

            if (Directory.Exists(dirPath))
            {
                List<string> fileList = new List<string>(64);
                GetFilesInDirectory(dirPath, fileList);

                for (int i = 0; i < fileList.Count; i++)
                {
                    string filePath = fileList[i];
                    string localPath = filePath.Substring(filePath.IndexOf(dir, StringComparison.Ordinal));

                    if (!string.IsNullOrEmpty(localPath))
                    {
                        AddAsset(localPath, eFilterType.Directory);

                        if (s_dirAssetDict.ContainsKey(localPath))
                        {
                            if (!assetDirList.Contains(localPath))
                            {
                                assetDirList.Add(localPath);
                            }
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("Directory is not exit: " + dir);
            }
        }
    }
    
    private static void GetFilesInDirectory(string dirPath, List<string> fileList, bool recursion = true)
    {
        if (fileList != null && Directory.Exists(dirPath))
        {
            string[] fileEntries = Directory.GetFiles(dirPath);

            for (int i = 0; i < fileEntries.Length; i++)
            {
                string fileName = fileEntries[i];
                string filePath = fileName.Replace("\\", "/");

                if (filePath.EndsWith(".meta") || filePath.EndsWith(".db"))
                {
                    continue;
                }

                if (!fileList.Contains(filePath))
                {
                    fileList.Add(filePath);
                }
            }

            if (!recursion)
            {
                return;
            }

            string[] dirs = Directory.GetDirectories(dirPath);
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                string filePath = dir.Replace("\\", "/");

                if (filePath.EndsWith(".svn"))
                {
                    continue;
                }

                GetFilesInDirectory(filePath, fileList, recursion);
            }
        }
    }
    
    private static Asset AddAsset(string path, eFilterType filterType, bool bCollectDependencis = true)
    {
        Asset asset = null;

        if (IsValidAssetPath(path))
        {
            if (!s_dirAssetDict.TryGetValue(path, out asset))
            {
                asset = new Asset();
                asset.name = path;
                asset.md5 = GetMD5(path);
                string metaPath = string.Format("{0}.meta", path);
                asset.metaMD5 = GetMD5(metaPath);
                asset.refCount = 0;
                asset.type = TypeOfAsset(path);
                asset.isBig = true;
                asset.isBigChecked = false;
                asset.directory = asset.name.Substring(0, asset.name.LastIndexOf('/'));

                if (asset.type == eAssetType.SHADER)
                {
                    asset.filterType |= eFilterType.Shader;
                }
                asset.filterType |= filterType;
                asset.uniqueFilterType = ConvertFilterType(asset.filterType);

                s_dirAssetDict.Add(path, asset);
                s_typeAssetDirsDict[asset.type].Add(path);

                s_assetDependenciesDict.Add(path, new List<string>());

                //优化
                List<string> lstDtPaths = null;
                if (bCollectDependencis && s_assetDependenciesDict.TryGetValue(asset.name, out lstDtPaths))
                {

                    string[] lstDtAssets = AssetDatabase.GetDependencies(asset.name);
                    foreach (string dtName in lstDtAssets)
                    {
                        //排除脚本
                        if (!dtName.Equals(asset.name) && TypeOfAsset(dtName) != eAssetType.SCRIPT)
                        {
                            //如果是需要被移除的文件,不要将其加入依赖中
                            if (IsFileInBlackList(dtName))
                            {
                                continue;
                            }

                            if (lstDtPaths != null && !lstDtPaths.Contains(dtName))
                            {
                                lstDtPaths.Add(dtName);
                            }
                        }
                    }

                    //计算依赖文件的信息
                    //string[] lstDtAssets = AssetDatabase.GetDependencies(path);

                    for (int i = 0; i < lstDtAssets.Length; i++)
                    {
                        string dtPath = lstDtAssets[i];

                        if (!dtPath.Equals(path))
                        {
                            if (!s_dirAssetDict.ContainsKey(dtPath))
                            {
                                AddAsset(dtPath, eFilterType.Dependence);
                            }
                        }
                    }
                }
            }

            //已经添加了，则组合类型
            asset.filterType |= filterType;
            asset.uniqueFilterType = ConvertFilterType(asset.filterType);
        }

        return asset;
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
    
    private static string GetMD5(string path)
    {
        string md5 = string.Empty;
        if (File.Exists(path))
        {
            MD5CryptoServiceProvider md5Generator = new MD5CryptoServiceProvider();
            FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read);
            byte[] hash = md5Generator.ComputeHash(file);
            md5 = System.BitConverter.ToString(hash).Replace("-", string.Empty);
            file.Close();
        }
        return md5;
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
    
    private static void BuildDirectDependencies()
    {
        //Find indirect dependencies of all assets
        Dictionary<string, List<string>> indirectDependencyDict = new Dictionary<string, List<string>>();
        foreach (Asset asset in s_dirAssetDict.Values)
        {
            List<string> indirectDependencyList = new List<string>();
            List<string> allDependencyList = s_assetDependenciesDict[asset.name];

            foreach (string dependency in allDependencyList)
            {
                foreach (string subDependency in s_assetDependenciesDict[dependency])
                {
                    if (allDependencyList.Contains(subDependency) && !indirectDependencyList.Contains(subDependency))
                    {
                        indirectDependencyList.Add(subDependency);
                    }
                }
                s_dirAssetDict[dependency].refCount++;
            }
            indirectDependencyDict.Add(asset.name, indirectDependencyList);
        }

        //Remove indirect dependencies from s_assetDependenciesDict and deduct the relevant reference count.
        //So s_assetDependenciesDict only contains direct dependencies now.
        foreach (KeyValuePair<string, List<string>> pair in indirectDependencyDict)
        {
            List<string> allDependencyList = s_assetDependenciesDict[pair.Key];
            foreach (string indirectDependency in pair.Value)
            {
                allDependencyList.Remove(indirectDependency);
                s_dirAssetDict[indirectDependency].refCount--;
            }
        }

        //Link direct dependencies to Asset
        using Dictionary<string, List<string>>.Enumerator it = s_assetDependenciesDict.GetEnumerator();
        while (it.MoveNext())
        {
            string assetName = it.Current.Key;
            List<string> directDependencyList = it.Current.Value;

            for (int i = 0; i < directDependencyList.Count; i++)
            {
                string dependency = directDependencyList[i];
                Asset asset = s_dirAssetDict[dependency];
                if (!asset.directDependencyList.Contains(assetName))
                {
                    asset.directDependencyList.Add(assetName);
                }
            }
        }
    }
    
    private static void SaveAssetXml(string path)
    {
        XmlDocument xmlDoc = new XmlDocument();
        XmlElement xmlRoot = xmlDoc.CreateElement("AssetList");
        xmlDoc.AppendChild(xmlRoot);

        int index = 0;
        foreach (Asset item in s_dirAssetDict.Values)
        {
            XmlElement xmlAsset = xmlDoc.CreateElement("Asset");

            XmlAttribute name = xmlDoc.CreateAttribute("Name");
            name.Value = item.name;
            xmlAsset.Attributes.Append(name);

            XmlAttribute md5 = xmlDoc.CreateAttribute("MD5");
            md5.Value = item.md5;
            xmlAsset.Attributes.Append(md5);

            XmlAttribute metaMD5 = xmlDoc.CreateAttribute("MetaMD5");
            metaMD5.Value = item.metaMD5;
            xmlAsset.Attributes.Append(metaMD5);

            XmlAttribute type = xmlDoc.CreateAttribute("Type");
            type.Value = item.type.ToString();
            xmlAsset.Attributes.Append(type);

            //XmlAttribute chapter = xmlDoc.CreateAttribute("Ch");
            //chapter.Value = item.chapter.ToString();
            //xmlAsset.Attributes.Append(chapter);

            XmlAttribute aBig = xmlDoc.CreateAttribute("big");
            aBig.Value = (item.isBig ? "1" : "0");
            xmlAsset.Attributes.Append(aBig);

            XmlAttribute refCount = xmlDoc.CreateAttribute("ref");
            refCount.Value = item.refCount.ToString();
            xmlAsset.Attributes.Append(refCount);

            XmlAttribute indexAttribute = xmlDoc.CreateAttribute("index");
            indexAttribute.Value = (++index).ToString();
            xmlAsset.Attributes.Append(indexAttribute);

            XmlAttribute filterType = xmlDoc.CreateAttribute("Filter");
            filterType.Value = item.filterType.ToString();
            xmlAsset.Attributes.Append(filterType);

            XmlAttribute uniqueFilterType = xmlDoc.CreateAttribute("UniqueFilter");
            uniqueFilterType.Value = item.uniqueFilterType.ToString();
            xmlAsset.Attributes.Append(uniqueFilterType);

            foreach (string dtPath in s_assetDependenciesDict[item.name])
            {
                XmlElement xmlDtAsset = xmlDoc.CreateElement("DtAsset");

                XmlAttribute dtName = xmlDoc.CreateAttribute("Name");
                dtName.Value = dtPath;
                xmlDtAsset.Attributes.Append(dtName);

                xmlAsset.AppendChild(xmlDtAsset);
            }

            xmlRoot.AppendChild(xmlAsset);
        }

        xmlDoc.Save(path);
    }
#endregion

#region Private methods of building bundles.
    private static void SetupAtlasBundle()
    {
        Dictionary<string, Bundle> atlasNameBundleDict = new Dictionary<string, Bundle>();
        foreach (Asset asset in s_dirAssetDict.Values)
        {
            if (asset.type == eAssetType.TEXTURE)
            {
                if (!asset.name.StartsWith("Assets/Textures/UI"))
                {
                    continue;
                }
                TextureImporter assetImporter = AssetImporter.GetAtPath(asset.name) as TextureImporter;
                if (assetImporter == null)
                {
                    continue;
                }
                
                asset.isAtlas = true;
                string atlasName = $"{assetImporter.spritePackingTag}";
                if (string.IsNullOrEmpty(atlasName))
                {
                    atlasName = FormatBundleName("default_atlas");
                }
                string bName = FormatBundleName(atlasName);
                if (!atlasNameBundleDict.TryGetValue(bName, out var bundle))
                {
                    bundle = new Bundle();
                    bundle.uniqueName = bName;
                    bundle.staticBundleType = eStatisBundleType.ATLAS;
                    bundle.bundleType = eBundleType.Atlas;
                    bundle.smartName = SmartBundleName(atlasName);
                    atlasNameBundleDict[bName] = bundle;
                }

                if (bundle != null && !bundle.assetList.Contains(asset.name))
                {
                    bundle.assetList.Add(asset.name);
                    s_bundleDict[bundle.uniqueName] = bundle;
                    s_assetDirBundleNameDict[asset.name] = bundle.uniqueName;
                }
            }
        }
    }
    
    private static void SetupCommonBundle()
    {
        using Dictionary<string, Asset>.Enumerator iterator = s_dirAssetDict.GetEnumerator();
        while (iterator.MoveNext())
        {
            Asset asset = iterator.Current.Value;

            if (asset.isAtlas || asset.isInitAsset)
            {
                continue;
            }
            
            if (asset.type != eAssetType.SCRIPT && asset.type != eAssetType.SHADER && asset.type != eAssetType.ANIM && asset.type != eAssetType.FBX && asset.type != eAssetType.CONTROLLER && !asset.name.EndsWith(".playable"))
            {
                if (asset.refCount > 1)
                {
                    string fileName = asset.name.Replace("Assets/", "");
                    fileName = fileName.Substring(0, fileName.IndexOf('/'));
                    string bundleName = FormatBundleName(fileName + "_common");

                    if (!s_bundleDict.TryGetValue(bundleName, out var bundle))
                    {
                        bundle = new Bundle();
                        bundle.uniqueName = bundleName;
                        s_bundleDict.Add(bundleName, bundle);

                        bundle.smartName = SmartBundleName(fileName + "_common");
                        bundle.isDirectory = true;
                        bundle.strBundlePath = asset.name;
                        bundle.bundleType = eBundleType.Common;
                        bundle.staticBundleType = eStatisBundleType.COMMON;
                    }

                    bundle.assetList.Add(asset.name);
                    s_bundleDict[bundle.uniqueName] = bundle;
                    s_assetDirBundleNameDict[asset.name] = bundle.uniqueName;
                }
            }
        }
    }
    
    private static void SetupShadersBundle()
    {
        Bundle bundle = new Bundle();
        bundle.smartName = SmartBundleName("Assets/Shaders");
        bundle.uniqueName = FormatBundleName(bundle.smartName);
        bundle.iLayer = 0;
        bundle.bundleType = eBundleType.Shader;
        bundle.staticBundleType = eStatisBundleType.SHADER;

        foreach (Asset asset in s_dirAssetDict.Values)
        {
            if (asset.isInitAsset)
            {
                continue;
            }

            if (asset.type == eAssetType.SHADER && !s_assetDirBundleNameDict.ContainsKey(asset.name))
            {
                if (!bundle.assetList.Contains(asset.name))
                {
                    bundle.assetList.Add(asset.name);
                    s_assetDirBundleNameDict.Add(asset.name, bundle.uniqueName);
                }
            }
        }
        s_bundleDict[bundle.uniqueName] = bundle;
    }
    
    private static void SetupFbxBundle()
    {
        using Dictionary<string, Asset>.Enumerator it2 = s_dirAssetDict.GetEnumerator();
        while (it2.MoveNext())
        {
            Asset asset = it2.Current.Value;
            if (asset.isInitAsset)
            {
                continue;
            }
            if (asset.type == eAssetType.FBX)
            {
                string fileName = $"{asset.name.Substring(0, asset.name.LastIndexOf('/'))}_fbx";
                string bundleName = FormatBundleName(fileName);

                if (!s_bundleDict.TryGetValue(bundleName, out var bundle))
                {
                    bundle = new Bundle();
                    bundle.uniqueName = bundleName;
                    s_bundleDict.Add(bundleName, bundle);

                    bundle.smartName = SmartBundleName(fileName);
                    bundle.staticBundleType = eStatisBundleType.MEDIA_MODEL;
                    bundle.bundleType = eBundleType.Model;
                    bundle.isDirectory = true;
                    bundle.strBundlePath = asset.name;
                }

                bundle.assetList.Add(asset.name);
                s_bundleDict[bundle.uniqueName] = bundle;
                s_assetDirBundleNameDict[asset.name] = bundle.uniqueName;
            }
        }
    }
    
    private static void SetupAnimationBundle()
    {
        Dictionary<string, Bundle> dicAnimBundles = new Dictionary<string, Bundle>();
        foreach (Asset asset in s_dirAssetDict.Values)
        {
            if (asset.type == eAssetType.ANIM || asset.type == eAssetType.CONTROLLER)
            {
                string fileName = $"{asset.name.Substring(0, asset.name.LastIndexOf('/'))}/Animation";
                string bName = FormatBundleName(fileName);
                if (!dicAnimBundles.TryGetValue(bName, out var bundle))
                {
                    bundle = new Bundle();
                    bundle.uniqueName = bName;
                    bundle.staticBundleType = eStatisBundleType.MEDIA;
                    bundle.bundleType = eBundleType.Animation;
                    bundle.smartName = SmartBundleName(fileName);
                    dicAnimBundles[bName] = bundle;
                }

                if (bundle != null && !bundle.assetList.Contains(asset.name))
                {
                    bundle.assetList.Add(asset.name);

                    s_bundleDict[bundle.uniqueName] = bundle;
                    s_assetDirBundleNameDict[asset.name] = bundle.uniqueName;
                }
            }
        }
    }

    private static void SetupSceneBundle()
    {
        foreach (Asset asset in s_dirAssetDict.Values)
        {
            if (asset.type == eAssetType.SCENE)
            {
                Debug.Log("打包场景中");

                Bundle bundle = new Bundle();
                bundle.uniqueName = FormatBundleName(asset.name);
                bundle.smartName = SmartBundleName(asset.name);
                bundle.strBundlePath = asset.name;
                bundle.assetList.Add(asset.name);
                s_bundleDict[bundle.uniqueName] = bundle;
                s_assetDirBundleNameDict[asset.name] = bundle.uniqueName;

                bundle.isScene = true;
                bundle.scenePath = asset.name;
                bundle.staticBundleType = eStatisBundleType.SCENE;
                bundle.bundleType = eBundleType.Scene;
                Debug.Log("打包场景: " + bundle.scenePath);

                List<string> lstDependencies = s_assetDependenciesDict[asset.name];
                int ndepLength = lstDependencies.Count;
                for (int i = 0; i < ndepLength; i++)
                {
                    string aName = lstDependencies[i];

                    if (s_dirAssetDict.TryGetValue(aName, out var dtAsset))
                    {
                        if (dtAsset is {refCount: 1})
                        {
                            Bundle dtBundle = null;

                            string sceneResBundle = FormatBundleName(asset.name + "_resource");

                            if (!s_bundleDict.TryGetValue(sceneResBundle, out dtBundle))
                            {
                                dtBundle = new Bundle();
                                dtBundle.uniqueName = sceneResBundle;
                                dtBundle.staticBundleType = eStatisBundleType.SCENE_RES;
                                dtBundle.bundleType = eBundleType.SceneRes;
                                dtBundle.smartName = SmartBundleName(asset.name + "_resource");
                                s_bundleDict[sceneResBundle] = dtBundle;
                            }

                            string oldBundleName = null;
                            if (s_assetDirBundleNameDict.TryGetValue(aName, out oldBundleName))
                            {
                                Bundle oldBundle = null;
                                if (s_bundleDict.TryGetValue(oldBundleName, out oldBundle))
                                {
                                    oldBundle.assetList.Remove(aName);
                                }
                            }

                            dtBundle.assetList.Add(aName);
                            s_assetDirBundleNameDict[aName] = dtBundle.uniqueName;
                        }
                    }
                }
            }
        }
    }
    
    private static void BuildBundleDependency()
    {
        using Dictionary<string, Bundle>.Enumerator iterator = s_bundleDict.GetEnumerator();
        while (iterator.MoveNext())
        {
            Bundle bundle = iterator.Current.Value;
            bundle.dependentBundleList.Clear();

            List<string> clonedAssetList = new List<string>();
            int assetList = bundle.assetList.Count;
            for (int i = 0; i < assetList; i++)
            {
                clonedAssetList.Add(bundle.assetList[i]);
            }
            assetList = clonedAssetList.Count;
            for (int i = 0; i < assetList; i++)
            {
                string aName = clonedAssetList[i];
                if (s_dirAssetDict.ContainsKey(aName))
                {
                    CollectBundleAssets(bundle, s_dirAssetDict[aName]);
                }
            }
        }
    }
    
    private static void CollectBundleAssets(Bundle bundle, Asset asset)
    {
        if (asset != null && bundle != null)
        {
            foreach (string dtName in s_assetDependenciesDict[asset.name])
            {
                if (s_assetDirBundleNameDict.TryGetValue(dtName, out var dtBundleName))
                {
                    if (!string.IsNullOrEmpty(dtBundleName) && !dtBundleName.Equals(bundle.uniqueName) && !bundle.dependentBundleList.Contains(dtBundleName))
                    {
                        bundle.dependentBundleList.Add(dtBundleName);
                    }
                }
                else if (!bundle.assetList.Contains(dtName))
                {
                    bundle.assetList.Add(dtName);
                    s_assetDirBundleNameDict[dtName] = bundle.uniqueName;
                    CollectBundleAssets(bundle, s_dirAssetDict[dtName]);
                }
            }
        }
    }
    
    private static string FormatBundleName(string name)
    {
        string localName = name.EndsWith(".unity3d") ? name.Substring(0, name.LastIndexOf('.')) : name;
        uint crc = Crc32.GetCrc32(localName);
        localName = $"{crc.ToString()}";
        return localName;
    }

    private static string SmartBundleName(string name)
    {
        string strLocalName = name.EndsWith(".unity3d") ? name.Substring(0, name.LastIndexOf('.')) : name;
        strLocalName = strLocalName.Replace('\\', '_');
        strLocalName = strLocalName.Replace('/', '_');
        strLocalName = strLocalName.Replace('.', '_');
        strLocalName = strLocalName.Replace('@', '_');
        strLocalName = $"{strLocalName}.unity3d";
        return strLocalName;
    }
    
    private static void SaveBundleXml(string path)
    {
        XmlDocument xmlDoc = new XmlDocument();
        XmlElement xmlRoot = xmlDoc.CreateElement("AssetBundleList");
        xmlDoc.AppendChild(xmlRoot);

        int index = 0;
        foreach (Bundle bundle in s_bundleDict.Values)
        {
            if (bundle != null)
            {
                XmlElement xmlBundle = xmlDoc.CreateElement("AssetBundle");

                XmlAttribute name = xmlDoc.CreateAttribute("Name");
                name.Value = bundle.uniqueName;
                xmlBundle.Attributes.Append(name);

                XmlAttribute srcMD5 = xmlDoc.CreateAttribute("srcMD5");
                srcMD5.Value = bundle.srcMD5;
                xmlBundle.Attributes.Append(srcMD5);

                XmlAttribute decMD5 = xmlDoc.CreateAttribute("decMD5");
                decMD5.Value = bundle.decMD5;
                xmlBundle.Attributes.Append(decMD5);

                XmlAttribute dir = xmlDoc.CreateAttribute("Directory");
                dir.Value = bundle.isDirectory ? "Directory" : "File";
                xmlBundle.Attributes.Append(dir);

                XmlAttribute bundlePath = xmlDoc.CreateAttribute("BundlePath");
                bundlePath.Value = bundle.strBundlePath;
                xmlBundle.Attributes.Append(bundlePath);

                XmlAttribute Index = xmlDoc.CreateAttribute("Index");
                Index.Value = (++index).ToString();
                xmlBundle.Attributes.Append(Index);

                XmlAttribute smartName = xmlDoc.CreateAttribute("SmartName");
                smartName.Value = bundle.smartName;
                xmlBundle.Attributes.Append(smartName);

                XmlAttribute bundleType = xmlDoc.CreateAttribute("BundleType");
                bundleType.Value = bundle.bundleType.ToString();
                xmlBundle.Attributes.Append(bundleType);

                //XmlAttribute batch = xmlDoc.CreateAttribute("BatchCount");
                //batch.Value = bundle.nBatchCount.ToString();
                //xmlBundle.Attributes.Append(batch);

                //XmlAttribute chapter = xmlDoc.CreateAttribute("Ch");
                //chapter.Value = bundle.chapter.ToString();
                //xmlBundle.Attributes.Append(chapter);

                XmlAttribute srcSize = xmlDoc.CreateAttribute("SS");
                srcSize.Value = bundle.srcSize.ToString();
                xmlBundle.Attributes.Append(srcSize);

                XmlAttribute dstSize = xmlDoc.CreateAttribute("DS");
                dstSize.Value = bundle.dstSize.ToString();
                xmlBundle.Attributes.Append(dstSize);

                XmlAttribute initAttr = xmlDoc.CreateAttribute("InitBundle");
                initAttr.Value = bundle.isInitBundle.ToString();
                xmlBundle.Attributes.Append(initAttr);

                XmlAttribute rebuildAttr = xmlDoc.CreateAttribute("ForceRebuild");
                rebuildAttr.Value = bundle.isForceRebuild.ToString();
                xmlBundle.Attributes.Append(rebuildAttr);

                foreach (string aPath in bundle.assetList)
                {
                    XmlElement xmlAsset = xmlDoc.CreateElement("Asset");

                    XmlAttribute aName = xmlDoc.CreateAttribute("Name");
                    aName.Value = aPath;
                    xmlAsset.Attributes.Append(aName);

                    XmlAttribute aType = xmlDoc.CreateAttribute("Type");
                    aType.Value = TypeOfAsset(aPath).ToString();
                    xmlAsset.Attributes.Append(aType);

                    XmlAttribute assetPathType = xmlDoc.CreateAttribute("PathType");
                    assetPathType.Value = bundle.isDirectory ? (aPath.Contains(bundle.strBundlePath) ? "InDirectory" : "File") : "File";
                    xmlAsset.Attributes.Append(assetPathType);

                    //XmlAttribute aChapter = xmlDoc.CreateAttribute("Ch");
                    // aChapter.Value = ms_dicAssets[aPath].chapter.ToString();
                    //xmlAsset.Attributes.Append(aChapter);

                    // Asset asset = null;
                    // if (ms_dicAssets.TryGetValue(aPath, out asset))
                    // {
                    //     XmlAttribute aBig = xmlDoc.CreateAttribute("b");
                    //      aBig.Value = (asset.isBig ? "1" : "0");
                    //     xmlAsset.Attributes.Append(aBig);
                    //  }

                    xmlBundle.AppendChild(xmlAsset);
                }

                foreach (string dPath in bundle.dependentBundleList)
                {
                    XmlElement xmlDtBundle = xmlDoc.CreateElement("Dependence");

                    XmlAttribute dName = xmlDoc.CreateAttribute("Name");
                    dName.Value = dPath;
                    xmlDtBundle.Attributes.Append(dName);

                    //XmlAttribute dChapter = xmlDoc.CreateAttribute("Ch");
                    //dChapter.Value = ms_dicBundles[dPath].chapter.ToString();
                    //xmlDtBundle.Attributes.Append(dChapter);

                    xmlBundle.AppendChild(xmlDtBundle);
                }

                xmlRoot.AppendChild(xmlBundle);
            }
        }
        xmlDoc.Save(path);
    }

#endregion
}
