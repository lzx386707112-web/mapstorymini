using UnityEngine;

// MapleTV 播放显示（源端 UI/MapleTV.img：TVon 16 帧 240x90 开机动画、TVbasic 静态屏）。
// 源端 sendTV 包：发送者数(1/3) + type(0 Normal/1 Star/2 Heart) + 角色外观 + 名字 + 5 条消息
// （第 5 条限 15 字）+ 1337；TV 挂在屏幕顶部，角色立绘显示在 TV 屏里。
// 单机简化：唯一真人无观众，TV 屏内不渲染角色立绘（原端为完整 addCharLook 外观），
//   显示 TV 开机动画 → 静态屏 + 「说话人 : 消息」轮播条；服务端语义见 NativeMapleTvService。
// 入口：双击 MapleTV 券 5075000..5075005（UseCashItemHandler case 5）→ 输入一条消息
//   （源端 5 条逐条轮播；单机一次输入一条，服务端置其余为空）。
public static class NativeMapleTvDisplay
{
    const float FrameDelay = 0.1f;

    static string speaker, message;
    static int sendType;
    static float until;             // unscaledTime
    static float startedAt;
    static bool playing;

    public static bool Playing => playing && Time.unscaledTime < until;

    public static void Start(TvResponse r)
    {
        speaker = r.speaker;
        message = r.message ?? "";
        sendType = r.sendType;
        startedAt = Time.unscaledTime;
        until = Time.unscaledTime + r.durationMs / 1000f;
        playing = true;
    }

    public static void Reset() { playing = false; speaker = message = null; }

    public static void Draw()
    {
        if (!Playing) { if (playing) playing = false; return; }
        var frame = Time.unscaledTime - startedAt < 16f * FrameDelay
            ? Art.Tex("wz://UI/MapleTV.img/TVon/" + ((int)((Time.unscaledTime - startedAt) / FrameDelay) % 16))
            : Art.Tex("wz://UI/MapleTV.img/TVbasic/0");
        if (frame == null) return;
        float s = BottomBar.S;
        // TV 挂屏幕右上（origin=(0,89) 是底对齐基准，按素材几何贴顶）
        var rect = new Rect(UiKit.W - (frame.width + 6) * s, 6f * s, frame.width * s, frame.height * s);
        GUI.DrawTexture(rect, frame);

        // TV 屏内文字： Normal/Star/Heart 样式仅素材差异（TVchat1/TVchat2 双人样式单机不用）
        if (!string.IsNullOrEmpty(message))
        {
            var style = new GUIStyle
            {
                font = UiKit.Font,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(11 * s)),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(rect.x + 14f * s, rect.y + 24f * s, rect.width - 28f * s, 20f * s),
                speaker + " : " + message, style);
        }
    }
}
