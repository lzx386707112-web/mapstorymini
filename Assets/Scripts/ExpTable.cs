using System.Collections.Generic;
using UnityEngine;

// 升级所需经验：直接读 tools/wz_exp.py 从原服务端 ExpTable.java 导出的表。
// 原端 getExpNeededForLevel(level) = exp[Math.min(level, exp.length - 1)]，
// 也就是超过表尾的等级一律取最后一项，这里保持同样的 clamp。
public static class ExpTable
{
    static int[] exp;
    static int maxLevel = 255;
    public static bool Ready { get; private set; }

    public static int MaxLevel { get { Ensure(); return maxLevel; } }
    public static int Count { get { Ensure(); return exp != null ? exp.Length : 0; } }

    public static void Ensure()
    {
        if (Ready) return;
        Ready = true;

        var asset = Resources.Load<TextAsset>("Field/ExpTable");
        if (asset == null)
        {
            Debug.LogError("[Exp] 缺少 Resources/Field/ExpTable.json（运行 tools/wz_exp.py）");
            exp = new[] { 0, 15, 34 };
            return;
        }

        var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
        if (root == null)
        {
            Debug.LogError("[Exp] ExpTable.json 解析失败");
            exp = new[] { 0, 15, 34 };
            return;
        }

        maxLevel = Mathf.Max(1, (int)Art.Num(root, "maxLevel", 255));
        var list = root.ContainsKey("exp") ? root["exp"] as List<object> : null;
        if (list == null || list.Count == 0)
        {
            Debug.LogError("[Exp] ExpTable.json 没有 exp 数组");
            exp = new[] { 0, 15, 34 };
            return;
        }

        exp = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
            exp[i] = System.Convert.ToInt32(list[i], System.Globalization.CultureInfo.InvariantCulture);
    }

    // 原端返回 int；本地保持同一宽度（表尾 2,100,000,000 仍在 int 范围内）
    public static int Needed(int level)
    {
        Ensure();
        if (exp == null || exp.Length == 0) return 15;
        return exp[Mathf.Clamp(level, 0, exp.Length - 1)];
    }
}
