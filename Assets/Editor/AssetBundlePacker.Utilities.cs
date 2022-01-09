using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

public static partial class AssetBundlePacker
{
    private static void GetFilesInDirectory(string dirPath, List<string> fileList, bool recursively = true)
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
    
                if (!recursively)
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
    
                    GetFilesInDirectory(filePath, fileList, recursively);
                }
            }
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
}
