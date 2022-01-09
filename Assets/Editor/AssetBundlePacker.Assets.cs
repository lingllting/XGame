using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

public static partial class AssetBundlePacker
{
    private static List<string> s_assetFolderList = new List<string>();
    private static Dictionary<string, Asset> s_dirAssetDict = new Dictionary<string, Asset>();                                  //asset dir - asset 
    private static Dictionary<eAssetType, List<string>> s_typeAssetDirsDict = new Dictionary<eAssetType, List<string>>();       //type - asset dirs
    private static Dictionary<string, List<string>> s_assetDependenciesDict = new Dictionary<string, List<string>>();           //asset dir - direct dependencies' dirs
    private static Dictionary<string, List<string>> s_folderAssetDirsDict = new Dictionary<string, List<string>>();             //folder dir - asset dirs

    private static void PrepareAssets()
    {
        //TODO consider to implement a editor window to config these folders.
        s_assetFolderList.Clear();
        s_assetFolderList.Add("Assets/Prefabs");
        s_assetFolderList.Add("Assets/Textures/UI");
        
        for (var i = 0; i < s_assetFolderList.Count; i++)
        {
            ImportAssetsFromDirectory(s_assetFolderList[i]);
        }
    }
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
    private static Asset AddAsset(string path, eFilterType filterType, bool bCollectDependencies = true)
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
                if (bCollectDependencies && s_assetDependenciesDict.TryGetValue(asset.name, out var lstDtPaths))
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
    private static void BuildAssetDependencies()
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
    
}
