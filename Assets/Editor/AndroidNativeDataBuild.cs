using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

// 构建期对生成的 Gradle 工程做两件事：
// 1) 补共享存储权限（Data 部署在 /sdcard/mapstory，不在应用私有目录内）；
// 2) 放行本地回环的明文 HTTP——后台管理是游戏进程内起的 127.0.0.1 HTTP 服务，
//    Android 9+ 默认禁止 cleartext，系统 WebView 会直接报 ERR_CLEARTEXT_NOT_PERMITTED（黑屏）。
//    只对 127.0.0.1/localhost 开洞，其余域名仍按默认策略禁止。
public sealed class AndroidNativeDataBuild : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 1000;

    const string Android = "http://schemas.android.com/apk/res/android";
    const string NetworkSecurityConfigRes = "network_security_config";

    // 只允许回环地址走明文 http；base-config 不放开，公网/局域网仍是默认的仅 HTTPS。
    const string NetworkSecurityConfigXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<network-security-config>\n" +
        "  <base-config cleartextTrafficPermitted=\"false\" />\n" +
        "  <domain-config cleartextTrafficPermitted=\"true\">\n" +
        "    <domain includeSubdomains=\"false\">127.0.0.1</domain>\n" +
        "    <domain includeSubdomains=\"false\">localhost</domain>\n" +
        "  </domain-config>\n" +
        "</network-security-config>\n";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
        var xml = new XmlDocument(); xml.Load(manifest);

        foreach (string permission in new[] { "android.permission.MANAGE_EXTERNAL_STORAGE", "android.permission.READ_EXTERNAL_STORAGE", "android.permission.WRITE_EXTERNAL_STORAGE" })
        {
            bool exists = false;
            foreach (XmlElement element in xml.DocumentElement.SelectNodes("uses-permission"))
                if (element.GetAttribute("name", Android) == permission) exists = true;
            if (!exists)
            {
                var element = xml.CreateElement("uses-permission");
                element.SetAttribute("name", Android, permission); xml.DocumentElement.AppendChild(element);
            }
        }

        var application = (XmlElement)xml.DocumentElement.SelectSingleNode("application");
        if (application != null)
        {
            application.SetAttribute("requestLegacyExternalStorage", Android, "true");
            // 引用 res/xml/network_security_config.xml；资源在下面写入，合并后归 launcher 的 resources.arsc。
            application.SetAttribute("networkSecurityConfig", Android, "@xml/" + NetworkSecurityConfigRes);
        }

        string xmlDir = Path.Combine(path, "src/main/res/xml");
        Directory.CreateDirectory(xmlDir);
        File.WriteAllText(Path.Combine(xmlDir, NetworkSecurityConfigRes + ".xml"), NetworkSecurityConfigXml, new System.Text.UTF8Encoding(false));

        xml.Save(manifest);
        // Unity passes the generated unityLibrary directory, never the source Assets directory.
        string data = Path.Combine(path, "src/main/assets/Wz/Data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Debug.Log("[Build] manifest 已注入 networkSecurityConfig（仅放行 127.0.0.1 明文 HTTP）；Android Data 独立部署，已从生成的 Gradle 工程排除完整 Data。");
    }
}
