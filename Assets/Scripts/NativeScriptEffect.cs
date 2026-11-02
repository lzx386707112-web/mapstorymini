// PacketCreator.showEffect(String) 的客户端落点。
//
// 源端这个包就是「在图上播一个 Map/Effect.img 里的动画」：脚本传的是相对路径，
// 例如 "quest/party/clear"、"customSkill/lucid/furyVideoLayer"。
// 客户端有两条素材通道：
//   1. customSkill/*VideoLayer 这类 FIELD_EFFECT 标记 —— 由 MCV 播真实视频（NativeBossVideoEffect）；
//   2. 其余是普通的帧动画 —— NativeScreenEffect 按 Map/Effect.img 逐帧播。
// 两条都直接读原始素材，没有任何自造表现。
public static class NativeScriptEffect
{
    // 最近一次真正播过的路径。编辑器回归（batchmode 不渲染）靠它断言「脚本确实让地图播了什么」。
    public static string Last { get; private set; }

    public static void Play(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Last = path;
        if (NativeBossVideoEffect.Handle(path)) return;
        NativeScreenEffect.Play("Map/Effect.img/" + path);
    }

    public static void Reset() => Last = null;
}
