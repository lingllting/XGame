using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static partial class AssetBundlePacker
{
    public static string OutputDirectory
    {
        get
        {
            return Application.dataPath + "/../";
        }
    }

    public static string AssetBundleDirectory
    {
        get
        {
            return OutputDirectory + "/AssetBundles";
        }
    }
}
