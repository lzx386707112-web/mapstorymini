#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using UnityEngine;

// 浏览器调试入口：用 URL 参数直接进指定地图，省掉「大厅 → 出战 → 走到某张图」。
//   index.html                       正常流程（进大厅）
//   index.html?map=40001             直接进地图 40001
//   index.html?map=1000000&god=1     直接进彩虹村并开无敌
//
// 只往 PlayerPrefs 里写 GameLoop 已经在读的那几个调试键
// （autostart / debug_field_map / debug_god），不改任何游戏逻辑；
// 并且只在 WebGL 构建里编译，编辑器和安卓流程完全不受影响。
public static class WebBoot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        string url = Application.absoluteURL;
        if (string.IsNullOrEmpty(url)) return;

        int q = url.IndexOf('?');
        if (q < 0 || q == url.Length - 1) return;

        string query = url.Substring(q + 1);
        int hash = query.IndexOf('#');
        if (hash >= 0) query = query.Substring(0, hash);

        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;
            int eq = pair.IndexOf('=');
            string key = eq < 0 ? pair : pair.Substring(0, eq);
            string val = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1));

            switch (key)
            {
                case "map":
                    int id;
                    if (int.TryParse(val, out id))
                    {
                        PlayerPrefs.SetInt("debug_field_map", id);
                        PlayerPrefs.SetInt("autostart", 1);
                        Debug.Log("[WebBoot] 直接进入地图 " + id);
                    }
                    break;
                case "god":
                    if (val == "1" || val == "true")
                    {
                        PlayerPrefs.SetInt("debug_god", 1);
                        PlayerPrefs.SetInt("autostart", 1);
                    }
                    break;
                case "skill":
                    if (val == "1" || val == "true") PlayerPrefs.SetInt("debug_skillwin", 1);
                    break;
                case "auto":
                    if (val == "1" || val == "true") PlayerPrefs.SetInt("autostart", 1);
                    break;
                case "lv":
                    // 验收用：临时抬等级（不写存档）。MP 上限按原成长曲线算，
                    // 低等级放不出五/六转技能（mpCon 22~30）是原版行为。
                    int lv;
                    if (int.TryParse(val, out lv) && lv > 0)
                    {
                        PlayerPrefs.SetInt("debug_level", lv);
                        PlayerPrefs.SetInt("autostart", 1);
                        Debug.Log("[WebBoot] 调试等级 " + lv);
                    }
                    break;
                case "name":
                    if (!string.IsNullOrEmpty(val)) PlayerPrefs.SetString("mxd.name", val);
                    break;
            }
        }
        PlayerPrefs.Save();
    }
}
#endif
