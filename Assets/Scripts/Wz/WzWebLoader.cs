#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

// WebGL StreamingAssets is served over HTTP, not a local filesystem. Stage the
// unchanged files before creating the game; the reader then uses the same API.
public sealed class WzWebLoader : MonoBehaviour
{
    Action complete;
    int loaded, total;
    string failure;
    public static string Root { get; private set; }
    public static void Begin(Action complete)
    {
        var go = new GameObject("[ResourceLoader]"); DontDestroyOnLoad(go);
        go.AddComponent<WzWebLoader>().complete = complete;
    }
    static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    IEnumerator Start()
    {
        Root = Path.Combine(Application.temporaryCachePath, "NativeWz");
        var files = (List<object>)WzAssets.Index["files"]; total = files.Count;
        foreach (Dictionary<string, object> file in files)
        {
            string relative = (string)file["path"], expected = (string)file["sha256"];
            string path = Path.Combine(Root, relative);
            if (File.Exists(path) && Hash(File.ReadAllBytes(path)) == expected) { loaded++; continue; }
            using (var request = UnityWebRequest.Get(Application.streamingAssetsPath + "/Wz/Data/" + relative))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success || Hash(request.downloadHandler.data) != expected)
                {
                    failure = "资源加载失败，请刷新重试。";
                    Debug.LogError("[WzWebLoader] " + relative + ": " + request.error); yield break;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, request.downloadHandler.data);
            }
            loaded++;
        }
        complete(); Destroy(gameObject);
    }
    void OnGUI()
    {
        GUI.Label(new Rect(24, 24, 500, 40), failure ?? ("加载资源 " + loaded + "/" + total));
    }
}
#endif
