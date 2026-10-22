using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

// Reject stale generated Data copies left by builds before resources moved outside Assets.
public sealed class AndroidNativeDataBuild : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 1000;
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        // Shared /sdcard/mapstory is outside scoped application storage.
        string manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
        var xml = new XmlDocument(); xml.Load(manifest);
        const string android = "http://schemas.android.com/apk/res/android";
        foreach (string permission in new[] { "android.permission.MANAGE_EXTERNAL_STORAGE", "android.permission.READ_EXTERNAL_STORAGE", "android.permission.WRITE_EXTERNAL_STORAGE" })
        {
            bool exists = false;
            foreach (XmlElement element in xml.DocumentElement.SelectNodes("uses-permission"))
                if (element.GetAttribute("name", android) == permission) exists = true;
            if (!exists)
            {
                var element = xml.CreateElement("uses-permission");
                element.SetAttribute("name", android, permission); xml.DocumentElement.AppendChild(element);
            }
        }
        var application = (XmlElement)xml.DocumentElement.SelectSingleNode("application");
        if (application != null) application.SetAttribute("requestLegacyExternalStorage", android, "true");
        xml.Save(manifest);
        // Unity passes the generated unityLibrary directory, never the source Assets directory.
        string data = Path.Combine(path, "src/main/assets/Wz/Data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Debug.Log("[Build] Android Data 独立部署，已从生成的 Gradle 工程排除完整 Data；项目外原文件保留。安装时运行 tools/sync_android_data.py。");
    }
}
