using System.IO;
using UnityEditor;
using UnityEngine;

// 把角色单独隔离到一个相机层，用洋红背景渲染，验证「绿发」覆盖层是否落在头顶
// （绿发颜色独特，不会被蓝天/法袍干扰）。洋红 (255,0,255) 在检测时被排除。
// 进 Play 会触发域重载，static 和 update 订阅会被清空，所以走 EditorPrefs + InitializeOnLoadMethod。
public static class HairShot
{
    const int LAYER = 8;
    const string K = "HairShot.go";
    static bool captured;

    [InitializeOnLoadMethod]
    static void Hook()
    {
        if (EditorPrefs.GetBool(K, false)) EditorApplication.update += Tick;
    }

    public static void Run()
    {
        captured = false;
        // 用绿发 + 金衣，确保头发颜色在整个画面里唯一
        PlayerPrefs.SetString("mxd.cls", "mage");
        PlayerPrefs.SetString("mxd.hair", "green");
        PlayerPrefs.SetString("mxd.outfit", "gold");
        PlayerPrefs.SetString("mxd.equipWeapon", "w_staff");
        PlayerPrefs.SetString("mxd.owned", "w_sword_basic,w_staff");
        PlayerPrefs.SetInt("mxd.gold", 320);
        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("debug_god", 1);
        PlayerPrefs.Save();

        EditorPrefs.SetBool(K, true);
        PlaySmokeTest.Run();
    }

    static void SetLayer(Transform t, int l)
    {
        t.gameObject.layer = l;
        foreach (Transform c in t) SetLayer(c, l);
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K, false) || !EditorApplication.isPlaying || captured) return;
        var gl = GameLoop.I;
        if (gl == null || gl.player == null) return;

        var p = gl.player;
        SetLayer(p.transform, LAYER);

        var cam = new GameObject("HairCam").AddComponent<Camera>();
        cam.cullingMask = 1 << LAYER;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
        cam.orthographic = true;
        cam.orthographicSize = 2.8f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        float px = p.worldX;
        cam.transform.position = new Vector3(px, 0f, -10f);
        cam.transform.LookAt(new Vector3(px, 0f, 0f));

        const int W = 300, H = 420;
        RenderTexture rt = null, prev = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            Directory.CreateDirectory("/tmp");
            File.WriteAllBytes("/tmp/hairshot.png", tex.EncodeToPNG());
            Debug.Log("[HairShot] 已保存 /tmp/hairshot.png");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[HairShot] " + e.Message);
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = prev;
            if (rt != null) Object.Destroy(rt);
            if (tex != null) Object.Destroy(tex);
            Object.Destroy(cam.gameObject);
        }

        captured = true;
        EditorPrefs.DeleteKey(K);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }
}
