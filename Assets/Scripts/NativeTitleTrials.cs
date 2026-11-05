using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 称号试炼（项目自创玩法，设计稿 docs/title-trial-design-2026-11-04.md）。
//
// 点亮规则：档位 tier(1..10) 点亮 = 战力 ≥ .titles 同款阈值 + 系列试炼计数达标。
// 计数尽量复用既有持久化（2026-11-04 逐键核实）：
//   0 玄幻   累计击杀            → 本类新计数 title_trial_kill（NativeMobController 击杀旁路）
//   1 斗罗   成长BOSS击杀总和    → 现成 boss_growth_kill_<key>（NativeBossService 15 只）求和
//   2 龙珠   怪物图鉴收集种类    → 现成 Social.cards（monsterbook 行数）
//   3 火影   完成任务总数        → 现成 Boss.ReadGrowth.questCount（queststatus status==2）
//   4 海贼   PQ 副本总积分       → 现成 extend_value「副本总积分」
//   5 死神   星力升星成功次数    → 本类新计数 title_trial_star（NativeEnhancementWindow 提交旁路）
//   6 鬼灭   每日任务累计轮数    → 现成 extend_value「每日任务总完成轮数」
//   7 咒术   钓鱼等级            → 现成 characters.fishLevel（getFishLevel 同源）
//   8 英雄学院 累计签到天数      → 现成 extend_value「累计签到次数」
// 已点亮档存 combined 键 title_trial_lit（9 个 int 逗号串，extend_value），
// 里程碑名牌戒指发放位掩码存 title_trial_claimed_ring。
//
// 统一加成（全系列同表，仅佩戴系列生效，档位 n = 已点亮档）：
//   每档增量递增（三角数按用户上限标定）：四维≤100、攻/魔≤150、HP≤5500、防御≤200。
//   在 EquipLoadout.Apply 并进 set 差量表（与 282 成长系统同一结算口径），
//   因此出战属性、能力值窗、战力公式全部走同一条路，不出现两套面板。
// ————————————————————————————————————————————————————————————
public static class NativeTitleTrials
{
    public const int SeriesCount = NativeNameplate.SeriesCount;   // 9
    public const int TierCount = NativeNameplate.TierCount;       // 10

    // 点亮档位 n（1..10）的统一加成表（下标 n-1）。
    static readonly int[] Quad = { 2, 5, 11, 18, 27, 38, 51, 65, 82, 100 };        // 四维各
    static readonly int[] Attack = { 3, 8, 16, 27, 41, 57, 76, 98, 123, 150 };     // 攻/魔各
    static readonly int[] Hp = { 100, 300, 600, 1000, 1500, 2100, 2800, 3600, 4500, 5500 };
    static readonly int[] Defense = { 4, 11, 22, 36, 55, 73, 102, 129, 164, 200 }; // PDD/MDD 各

    // 里程碑名牌戒指：从 GM 商店在售的 nameTag 池选定（要换 id 只改这里）。
    static readonly int[] RingMilestoneTiers = { 1, 5, 8, 10 };
    static readonly int[] RingIds = { 1112100, 1112110, 1112200, 1112230 };

    const string LitKey = "title_trial_lit";
    const string KillKey = "title_trial_kill";
    const string StarKey = "title_trial_star";
    const string RingMaskKey = "title_trial_claimed_ring";

    // 15 只成长 BOSS 的击杀键（NativeBossService.Growths 的 key 列表）。
    static readonly string[] BossKeys =
    {
        "papulatus", "scarga", "yaoseng", "krexel", "zakum", "showa",
        "horntail", "pinkbean", "tokyo_vergamot", "tokyo_dunas", "tokyo_nibergen",
        "tokyo_nux", "tokyo_dunas2", "tokyo_aufheben", "vonleon"
    };

    static readonly int[] litCache = new int[SeriesCount];
    static bool litLoaded;
    static double litUntil;

    static string ReadValue(string key)
    {
        if (!GameSession.InGame) return null;
        return GameSession.Net.ScriptValue(new ScriptValueRequest
        { characterId = GameSession.CharacterId, name = key }).value;
    }

    static void WriteValue(string key, string value)
    {
        if (!GameSession.InGame) return;
        GameSession.Net.ScriptValue(new ScriptValueRequest
        { characterId = GameSession.CharacterId, write = true, name = key, value = value });
    }

    static long ReadLong(string key)
        => long.TryParse(ReadValue(key), System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : 0;

    // ——— 已点亮档 ———
    public static int Lit(int series)
    {
        EnsureLit();
        return series >= 0 && series < SeriesCount ? litCache[series] : 0;
    }

    static void EnsureLit()
    {
        if (litLoaded && Time.unscaledTimeAsDouble < litUntil) return;
        for (int i = 0; i < SeriesCount; i++) litCache[i] = 0;
        string raw = ReadValue(LitKey);
        if (!string.IsNullOrEmpty(raw))
        {
            var parts = raw.Split(',');
            for (int i = 0; i < parts.Length && i < SeriesCount; i++)
                int.TryParse(parts[i], out litCache[i]);
        }
        litLoaded = true;
        litUntil = Time.unscaledTimeAsDouble + 3.0;
    }

    /// <summary>登录时由 NativeNameplate.RefreshFromServer 一并灌入。</summary>
    public static void LoadLitFromServer() => litLoaded = false;

    static void SaveLit()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < SeriesCount; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(litCache[i]);
        }
        WriteValue(LitKey, sb.ToString());
        litUntil = Time.unscaledTimeAsDouble + 3.0;
        NativeNameplate.OnLitChanged();
    }

    // ——— 系列计数 ———
    public static long Count(int series)
    {
        if (series < 0 || series >= SeriesCount || !GameSession.InGame) return 0;
        switch (series)
        {
            case 0: return ReadLong(KillKey);
            case 1:
            {
                long sum = 0;
                foreach (var key in BossKeys) sum += ReadLong("boss_growth_kill_" + key);
                return sum;
            }
            case 2: return GameSession.Net.Social(new SocialRequest
            { characterId = GameSession.CharacterId }).cards.Count;
            case 3: return GameSession.Net.Boss(new BossRequest
            { characterId = GameSession.CharacterId }).questCount;
            case 4: return ReadLong("副本总积分");
            case 5: return ReadLong(StarKey);
            case 6: return ReadLong("每日任务总完成轮数");
            case 7: return GameSession.Character?.record.fishLevel ?? 1;
            case 8: return ReadLong("累计签到次数");
        }
        return 0;
    }

    /// <summary>档位 tier（1..10）的试炼计数要求；第 1 档只要求战力。</summary>
    public static long Need(int series, int tier)
    {
        if (tier <= 1) return 0;
        if (tier > TierCount) return long.MaxValue;
        switch (series)
        {
            case 0: return 1000L << (tier - 2);      // 1000 × 2^(i-2)，档10=25.6万
            case 1: return 20L << (tier - 2);        // 档10=2560
            case 2: return 30 + 27 * (tier - 2);     // 档10=273（全图鉴 343）
            case 3: return 50L * (tier - 1);         // 档10=450
            case 4: return 200L * (tier - 1);        // 档10=1800
            case 5: return 10L << (tier - 2);        // 档10=1280
            case 6: return 30L * (tier - 1);         // 档10=270
            case 7: return 5L * (tier - 1);          // 钓鱼等级，档10=45
            case 8: return 10L * (tier - 1);         // 签到天数，档10=90
        }
        return long.MaxValue;
    }

    // ——— 击杀 / 升星挂钩 ———
    /// <summary>NativeMobController 击杀旁路：只计本地角色的场上击杀。</summary>
    public static void CountKill()
    {
        if (!GameSession.InGame) return;
        WriteValue(KillKey, (ReadLong(KillKey) + 1L).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>NativeEnhancementWindow 星力提交旁路：只计成功升星。</summary>
    public static void AddStar(int delta)
    {
        if (!GameSession.InGame || delta == 0) return;
        WriteValue(StarKey, (ReadLong(StarKey) + delta).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    // ——— 点亮 ———
    /// <summary>
    /// 尝试点亮 series 的下一档。成功返回 null 并把文案写入 granted；
    /// 失败返回错误文案（脚本侧直接展示）。
    /// </summary>
    public static string Claim(int series, out string granted)
    {
        granted = null;
        if (!GameSession.InGame) return "需要进入游戏";
        if (series < 0 || series >= SeriesCount) return "系列不存在";
        var player = GameLoop.I?.player;
        if (player == null) return "需要进入游戏";
        EnsureLit();
        int next = litCache[series] + 1;
        if (next > TierCount) return "该系列已点亮至最高档";
        long requirement = NativeNameplate.TierRequirement(next);
        long power = NativeNameplate.PowerOf(player);
        if (power < requirement)
            return "战力未达标：需要 " + requirement + "（当前 " + power + "）";
        long need = Need(series, next);
        long count = Count(series);
        if (count < need)
            return "试炼进度未达标：需要 " + need + "（当前 " + count + "）";

        litCache[series] = next;
        SaveLit();
        string message = "【" + NativeNameplate.TitleOf(series, next - 1) + "】已点亮！当前第 " +
            next + " 档";
        granted = ClaimRing(next);
        if (!string.IsNullOrEmpty(granted)) message += "\r\n" + granted;
        if (next == TierCount)
            NativeSystemMessage.Drop(6, "恭喜玩家点亮了 " + NativeNameplate.TitleOf(series, next - 1) +
                "（第 10 档）称号！");
        return null;
    }

    static string ClaimRing(int tier)
    {
        int index = System.Array.IndexOf(RingMilestoneTiers, tier);
        if (index < 0) return null;
        int bit = 1 << index;
        int mask = (int)ReadLong(RingMaskKey);
        if ((mask & bit) != 0) return null;
        int itemId = RingIds[index];
        if (!Profile.TryAddItem(itemId, 1))
            return "（装备栏已满，未能发放名牌戒指；稍后重新点亮领取即可补发）";
        WriteValue(RingMaskKey, (mask | bit).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return "获得里程碑奖励：" + (WzNames.Item(itemId) ?? "名牌戒指") + "（" + itemId + "）";
    }

    // ——— 统一加成（EquipLoadout.Apply 并进 set 差量表）———
    /// <summary>佩戴系列已点亮档对应的差量键值对；未点亮时为空。</summary>
    public static IEnumerable<KeyValuePair<string, int>> BonusSet()
    {
        if (!GameSession.InGame) yield break;
        EnsureLit();
        int n = Lit(NativeNameplate.Series);
        if (n <= 0) yield break;
        int quad = Quad[n - 1], attack = Attack[n - 1], hp = Hp[n - 1], defense = Defense[n - 1];
        yield return new KeyValuePair<string, int>("STR", quad);
        yield return new KeyValuePair<string, int>("DEX", quad);
        yield return new KeyValuePair<string, int>("INT", quad);
        yield return new KeyValuePair<string, int>("LUK", quad);
        yield return new KeyValuePair<string, int>("PAD", attack);
        yield return new KeyValuePair<string, int>("MAD", attack);
        yield return new KeyValuePair<string, int>("HP", hp);
        yield return new KeyValuePair<string, int>("PDD", defense);
        yield return new KeyValuePair<string, int>("MDD", defense);
    }
}
