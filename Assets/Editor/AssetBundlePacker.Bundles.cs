using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using UnityEditor;
using UnityEngine;

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
    private static Dictionary<string, string> s_assetDirBundleNameDict = new Dictionary<string, string>();                      //asset dir - bundle unique name
    private static Dictionary<string, Bundle> s_bundleDict = new Dictionary<string, Bundle>();                                  //bundle unique name - bundle
    private static HashSet<string> s_buildBundleHashSet = new HashSet<string>();                                                //unique names of bundles to be built
    
    public static void PrepareBundles()
    {
        SetupAtlasBundle();
        SetupCommonBundle();
        SetupShadersBundle();
        SetupFbxBundle();
        SetupAnimationBundle();
        SetupSceneBundle();
    }
    
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
    
    private static void ProcessBundle(Bundle bundle)
    {
        if (bundle != null && File.Exists(bundle.locationPath))
        {
            FileStream file = new FileStream(bundle.locationPath, FileMode.Open);
            byte[] data = new byte[(int)file.Length];
            file.Read(data, 0, data.Length);
            file.Close();
            file.Dispose();

            //计算MD5
            bundle.srcSize = data.Length >> 10;
            MD5CryptoServiceProvider md5Generator = new MD5CryptoServiceProvider();
            bundle.srcMD5 = System.BitConverter.ToString(md5Generator.ComputeHash(data)).Replace("-", string.Empty);
        }
    }
    
}
