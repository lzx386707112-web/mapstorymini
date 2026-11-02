using System.Collections.Generic;
using UnityEngine;

// 升级所需经验：读 Field/ExpTable.json（源端 ExpTable.java 的 0..199 级真实值），
// 200 级以上的扩展与 300 级上限由 ExpCurve 现算（见 ExpCurve.cs）。
//
// 宽整数：源端表尾 2,100,000,000 被 int 削平；本工程 200 级起每级 +20%，
// 300 级需求已是 1e17 量级，整条链路（表 / 存档 / DB / 经验条）一律用 long。
public static class ExpTable
{
    static long[] exp;
    static int maxLevel = ExpCurve.CapLevel;
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
            Debug.LogError("[Exp] 缺少 Resources/Field/ExpTable.json");
            exp = ExpCurve.Build(new long[] { 0, 15, 34 });
            return;
        }

        var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
        if (root == null)
        {
            Debug.LogError("[Exp] ExpTable.json 解析失败");
            exp = ExpCurve.Build(new long[] { 0, 15, 34 });
            return;
        }

        maxLevel = Mathf.Clamp((int)Art.Num(root, "maxLevel", ExpCurve.CapLevel), 1, ExpCurve.CapLevel);
        var list = root.ContainsKey("exp") ? root["exp"] as List<object> : null;
        if (list == null || list.Count == 0)
        {
            Debug.LogError("[Exp] ExpTable.json 没有 exp 数组");
            exp = ExpCurve.Build(new long[] { 0, 15, 34 });
            return;
        }

        // 源端值都 < 2.1e9，double 能精确表示；200 级以上的大数由 ExpCurve 在 long 里算，
        // 不经过 JSON 的 double 通道（否则 1e17 会被截掉低位）。
        var source = new long[list.Count];
        for (int i = 0; i < list.Count; i++)
            source[i] = System.Convert.ToInt64(System.Math.Round(System.Convert.ToDouble(list[i], System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        exp = ExpCurve.Build(source);
    }

    public static long Needed(int level)
    {
        Ensure();
        return ExpCurve.Needed(exp, level);
    }
}
