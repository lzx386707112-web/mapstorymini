using UnityEngine;

// 源端 OnlineTimeTask 的单机等价（org.gms.net.server.task.OnlineTimeTask）：
// 每 5 秒给在线角色 +5 秒，累计值落在账号扩展值 ExtendKey.ONLINE_TIME（"每日在线时间"），
// 跨日归零。脚本用 cm.getOnlineTime() 读它（BeiDouSpecial/在线奖励.js 按分钟算档位）。
//
// 单机不跑定时线程：改成读取时按真实经过时间补算，每 60 秒落一次库。
// 跨日归零交给 extend_value 的 period=1（每日）——NativeScriptValues 读的时候
// 就会把昨天的行删掉，和源端 isNextDay→onlineTime=0 的结果一致。
public static class OnlineTime
{
    const string Key = "每日在线时间";
    const float SaveInterval = 60f;

    static bool loaded;
    static int baseSeconds;                 // 已落库的秒数
    static float loadedAt;                  // 载入时刻（realtimeSinceStartup）
    static float lastSaveAt;

    public static int Seconds
    {
        get
        {
            if (!GameSession.InGame) return 0;
            if (!loaded) Load();
            return baseSeconds + Mathf.FloorToInt(Time.realtimeSinceStartup - loadedAt);
        }
    }

    // 每帧由 HUD 调用：累计够 60 秒就落库，避免只在退游戏时才写。
    public static void Tick()
    {
        if (!GameSession.InGame) return;
        if (!loaded) Load();
        if (Time.realtimeSinceStartup - lastSaveAt < SaveInterval) return;
        Flush();
    }

    public static void Flush()
    {
        if (!loaded || !GameSession.InGame) return;
        int seconds = Seconds;
        Write(seconds);
        baseSeconds = seconds;
        loadedAt = Time.realtimeSinceStartup;
        lastSaveAt = loadedAt;
    }

    public static void Reset()
    {
        loaded = false;
        baseSeconds = 0;
    }

    static void Load()
    {
        loaded = true;
        baseSeconds = Read();
        loadedAt = lastSaveAt = Time.realtimeSinceStartup;
    }

    static ScriptValueRequest Request(bool write, string value) => new ScriptValueRequest
    {
        characterId = GameSession.CharacterId, account = true, period = 1, name = Key,
        write = write, value = value
    };

    static int Read()
    {
        try
        {
            string text = GameSession.Net?.ScriptValue(Request(false, null))?.value;
            return int.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out int seconds) ? Mathf.Max(0, seconds) : 0;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[OnlineTime] 读取在线时长失败：" + e.Message);
            return 0;
        }
    }

    static void Write(int seconds)
    {
        try
        {
            GameSession.Net?.ScriptValue(Request(true, seconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[OnlineTime] 保存在线时长失败：" + e.Message);
        }
    }
}
