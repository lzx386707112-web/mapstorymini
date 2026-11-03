using UnityEngine;

// ————————————————————————————————————————————————————————————
// 名牌附加行「称号 || 战斗力：N」——迁移 BeiDouSetItemCompat.dll。
//
// 源端没有把这段塞进 WZ，全部在客户端 DLL 里：
//   .titles 节 0x3120B9 起 8 个 dword 是战力阈值（power >= 阈值就升一档，最多 8 档）
//   0x312059 `cmp eax,0x540BE400` + 高位 `cmp edx,2` ⇒ 第 10 档的门槛是 1e10
//   0x3120D9 是称号字符串池、0x3123AF 是 word 偏移表，下标 = 系列*10 + 档位
//   0x59BF 是格式串 `%s || 战斗力：%s`
//   0x31206A `movzx edx,byte[edi+8]; dec edx; cmp edx,8` ⇒ 系列 = clamp(titleState-1, 0, 8)
// 显示条件（PacketCreator.nameplatePowerUpdate）：身上有 nameTag 戒指，且 titleState != 0。
// 战力公式（PacketCreator.calculateNameplatePower）：
//   (str+dex+int+luk) * max(1, max(watk, magic)) + maxHp/10 + maxMp/20
// 称号系列由 NPC 9900001 的「称号系列选择」脚本（cm.setNameplateSeries）设置，
// 持久化在角色扩展表的 nameplate_title_series 键。
// ————————————————————————————————————————————————————————————
public static class NativeNameplate
{
    public const int SeriesCount = 9, TierCount = 10;
    // 0x3120B9..0x3120D8，8 个 dword
    static readonly long[] Thresholds = { 10000, 100000, 1000000, 10000000, 50000000, 200000000, 500000000, 2000000000 };
    // 第 10 档：高位 2、低位 0x540BE400 ⇒ 恰好 10,000,000,000
    public const long TopTierPower = 10000000000L;

    // .titles 节字符串池，逐字对齐（顺序不能调整，下标就是系列 id）
    static readonly string[][] Titles =
    {
        new[]{ "乾元玄阶", "坤极天域", "天元墟境", "玄枢天宿", "星墟玄阙", "太白星河", "苍元帝宿", "昊天玄宿", "九曜神王", "无敌大帝" },
        new[]{ "魂师", "大魂师", "魂尊", "魂王", "魂圣", "封号斗罗", "极限斗罗", "三级神祇", "神帝", "神帝" },
        new[]{ "赛亚人", "超级赛亚", "赛亚二阶", "赛亚三阶", "超赛神境", "超赛蓝境", "蓝级进化", "破坏神位", "自在极意", "自在极意" },
        new[]{ "学院生徒", "下忍", "中忍", "上忍", "影级强者", "仙人模式", "尾兽化身", "六道之力", "大筒木境", "大筒木境" },
        new[]{ "海贼新秀", "超新星", "七武海", "中将", "四皇候补", "四皇", "大将", "觉醒者", "海贼王", "海贼王" },
        new[]{ "席官", "副队长", "队长", "破面十刃", "归刃解放", "完现术者", "零番队级", "灵王断片", "灵王", "灵王" },
        new[]{ "鬼杀队员", "十人众级", "柱级", "下弦之鬼", "上弦之鬼", "上弦首座", "鬼舞辻无", "赫刀觉醒", "缘壹之境", "缘壹之境" },
        new[]{ "四级术师", "三级术师", "二级术师", "一级术师", "准特级位", "特别一级", "特级术师", "领域展开", "最强之境", "最强之境" },
        new[]{ "无个性者", "普通学生", "雄英生徒", "实习英雄", "职业英雄", "前十强者", "前五英雄", "第一英雄", "超觉醒者", "超觉醒者" },
    };

    /// <summary>战力 → 称号档位（0..9）。与 .titles 节 0x312035 的循环逐条同义。</summary>
    public static int TierOf(long power)
    {
        if (power <= 0) return 0;
        if (power >= TopTierPower) return TierCount - 1;
        int tier = 0;
        while (tier < Thresholds.Length && power >= Thresholds[tier]) tier++;
        return tier;
    }

    public static string TitleOf(int series, int tier)
    {
        int s = Mathf.Clamp(series, 0, SeriesCount - 1);
        int t = Mathf.Clamp(tier, 0, TierCount - 1);
        return Titles[s][t];
    }

    /// <summary>当前角色选的系列（服务端 nameplate_title_series 的本地镜像）。</summary>
    public static int Series => Mathf.Clamp(Profile.Data.nameplateTitleSeries, 0, SeriesCount - 1);

    public static void SetSeries(int series)
    {
        int value = Mathf.Clamp(series, 0, SeriesCount - 1);
        Profile.Data.nameplateTitleSeries = value;
        Profile.Save();
    }

    /// <summary>进游戏时从服务端把持久化的系列灌进本地镜像。</summary>
    public static void RefreshFromServer()
    {
        if (!GameSession.InGame) return;
        try
        {
            string value = GameSession.Net.ScriptValue(new ScriptValueRequest
            { characterId = GameSession.CharacterId, name = "nameplate_title_series" }).value;
            if (int.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out int series))
                Profile.Data.nameplateTitleSeries = Mathf.Clamp(series, 0, SeriesCount - 1);
        }
        catch (System.Exception error)
        {
            Debug.LogWarning("[Nameplate] 读取称号系列失败：" + error.Message);
        }
    }

    /// <summary>是否穿着名牌戒指（源端 ItemInformationProvider.isNameTagRing 的等价判定）。</summary>
    public static bool WearsNameTagRing => NativeNameTag.WearsField("nameTag");

    /// <summary>PacketCreator.calculateNameplatePower。</summary>
    public static long PowerOf(PlayerCtrl player)
    {
        if (player == null || player.Attributes == null) return 0;
        var a = player.Attributes;
        long stats = (long)a.str + a.dex + a.intelligence + a.luk;
        long attack = Mathf.Max(player.WeaponAttack, player.MagicAttack);
        return System.Math.Max(0L, stats * System.Math.Max(1L, attack) + a.maxHp / 10L + a.maxMp / 20L);
    }

    /// <summary>
    /// 脚下那一行的文字；不显示时返回 null。
    /// titleState = 有名牌戒指 ? 系列+1 : 0（0 表示整行不画，战力也不下发）。
    /// 每帧都要读，戒指/战力的判定按 0.5s 缓存一次即可（战力只随装备和 buff 变）。
    /// </summary>
    public static string LineFor(PlayerCtrl player)
    {
        if (player == null || player.Attributes == null) return null;
        long power = PowerOf(player);
        double now = Time.unscaledTimeAsDouble;
        if (now < cachedUntil && cachedPower == power && cachedSeries == Series) return cachedLine;
        cachedPower = power; cachedSeries = Series; cachedUntil = now + 0.5;
        cachedLine = WearsNameTagRing ? TitleOf(cachedSeries, TierOf(power)) + " || 战斗力：" + power : null;
        return cachedLine;
    }
    static string cachedLine;
    static long cachedPower;
    static int cachedSeries = -1;
    static double cachedUntil;
}
