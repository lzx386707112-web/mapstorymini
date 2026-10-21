// 真实发型清单：直接对应客户端 Character/Hair 里抽出来的素材。
//
// 每个职业在 wz_assets.py 里都按「默认发型 + 额外几种」抽成了
// chars/<职业>_h0 … _h5 这几套角色帧（同一张画布、同一轴心，只是头发不同），
// 所以这里只要保存一个下标，运行时拼出 artKey = 职业 + "_" + Keys[i] 即可。
//
// 发型资源是原游戏的，不是手搓染色 —— 这是用户明确要求「直接拿来用」。
public static class HairStyles
{
    public static readonly string[] Keys  = { "h0", "h1", "h2", "h3", "h4", "h5" };
    public static readonly string[] Names = { "发型一", "发型二", "发型三", "发型四", "发型五", "发型六" };

    public static int Count => Keys.Length;

    public static string KeyOf(int i) => (i >= 0 && i < Keys.Length) ? Keys[i] : Keys[0];

    public static int IndexOf(string key)
    {
        for (int i = 0; i < Keys.Length; i++)
            if (Keys[i] == key) return i;
        return 0;
    }

    public static bool Contains(string key)
    {
        for (int i = 0; i < Keys.Length; i++)
            if (Keys[i] == key) return true;
        return false;
    }

    public static string NameOf(string key) => Names[IndexOf(key)];
}
