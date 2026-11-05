using System;
using System.Collections.Generic;
using UnityEngine;

// 282 特殊核心（V 矩阵 type==2）：18 个被动核心的触发与效果。
//
// 数据链（三处都已经在仓库里，没有一处是猜的）：
//   Etc/VCore.img/info{registerSpecialCorevPoint=14, extendSpecialCorevPoint=50}
//        → VMatrix.json/costs/RegisterSpecialCost（开一个的 V 点价格）
//   VCore.img/CoreData/3000000x  → VMatrix.json/cores[*].special{cond,effect}
//        cond.type 一共 8 种：killCount / attackCount / attackCountMob / hitCount /
//                              prob / cooltime / die / rune / combokill
//        effect.type 一共 3 种：selfbuff（弹 400007xxx 的增益）/ reduceCooltime / heal
//   400007xxx 的数值  → SpecialCores.json（tools/ms282/export_special_cores.py 从 desc 解析）
//        客户端 40000.img 里没有 400007xxx 的定义，服务端 TMS282.5 也零实现，
//        所以数值只能来自 String/Skill.img 的 desc 文案，脚本解析不出来会直接报错。
//
// 归属判定：服务端 NativeVMatrixService 的 SelectSpecial 明确「全局只允许一个」，
// 所以这里只有一个 activeId，不需要多核心叠加。
public static class NativeSpecialCore
{
    sealed class Entry
    {
        public int id;
        public string name = "";
        public string condType = "";
        public int count;          // 需要累积的次数
        public double cooldown;    // 秒：多久只能发动一次
        public double validtime;   // 秒：多久没累积就清零
        public float prob;         // 概率型的单次概率（0..1）
        public string effectType = "";
        public string stat = "";
        public int value;
        public double duration;    // 秒：selfbuff 持续
    }

    static Dictionary<int, Entry> table;
    static int activeId;
    static int counter;
    static int mobKey;             // attackCountMob 用：当前累计的是哪只怪
    static double lastEvent = -99;
    static double coolUntil = -99;
    static double buffUntil = -99;
    static string buffStat = "";
    static int buffValue;

    static double Now => Time.realtimeSinceStartupAsDouble;

    public static int ActiveId => activeId;
    public static bool Ready => table != null && table.ContainsKey(activeId);

    static void Ensure()
    {
        if (table != null) return;
        table = new Dictionary<int, Entry>();
        var asset = Resources.Load<TextAsset>("UI/SpecialCores");
        if (asset == null) return;
        var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
        if (root == null || !(root.TryGetValue("cores", out var raw) && raw is Dictionary<string, object> cores))
            return;
        foreach (var pair in cores)
        {
            if (!(pair.Value is Dictionary<string, object> node)) continue;
            var entry = new Entry
            {
                id = Int(node, "id"),
                name = Text(node, "name"),
                effectType = Text(node, "effectType"),
            };
            if (node.TryGetValue("cond", out var condRaw) && condRaw is Dictionary<string, object> cond)
            {
                entry.condType = Text(cond, "type");
                entry.count = Int(cond, "count");
                entry.cooldown = Int(cond, "cooltime") / 1000.0;
                entry.validtime = Int(cond, "validtime") / 1000.0;
                entry.prob = (float)Real(cond, "prob");
            }
            if (node.TryGetValue("effect", out var effRaw) && effRaw is Dictionary<string, object> eff)
            {
                entry.stat = Text(eff, "stat");
                entry.value = Int(eff, "value");
                entry.duration = Int(eff, "duration") / 1000.0;
            }
            table[entry.id] = entry;
        }
    }

    static string Text(Dictionary<string, object> node, string key)
        => node.TryGetValue(key, out var value) ? value as string ?? "" : "";

    static int Int(Dictionary<string, object> node, string key)
        => node.TryGetValue(key, out var value) && value != null ? Convert.ToInt32(value) : 0;

    static double Real(Dictionary<string, object> node, string key)
        => node.TryGetValue(key, out var value) && value != null ? Convert.ToDouble(value) : 0;

    // 服务端 SelectSpecial 是「全局只允许一个」，所以这里取第一个 selected 的就是唯一那个。
    public static void Observe(VMatrixResponse response)
    {
        Ensure();
        int previous = activeId;
        activeId = 0;
        if (response != null && response.cores != null)
            foreach (var core in response.cores)
                if (core != null && core.selected && core.type == 2 && core.level > 0) { activeId = core.coreId; break; }
        if (activeId != previous) ResetProgress();
    }

    // 进图 / 换角色时主动拉一次快照。窗口打开时也会 Observe 一次，两条路写的是同一个字段。
    public static void Sync()
    {
        if (!GameSession.InGame || GameSession.Net == null || GameSession.CharacterId <= 0) return;
        var response = GameSession.Net.VMatrix(new VMatrixRequest
        {
            characterId = GameSession.CharacterId, operation = VMatrixOperation.Snapshot,
        });
        Observe(response);
    }

    static void ResetProgress()
    {
        counter = 0; mobKey = 0; lastEvent = -99; coolUntil = -99;
    }

    static int comboKills;
    static double lastComboKill=-99;

    public static void Reset()
    {
        comboKills=0; lastComboKill=-99;
        activeId = 0;
        ResetProgress();
        buffUntil = -99; buffStat = ""; buffValue = 0;
    }

    static Entry Current
    {
        get
        {
            Ensure();
            if (activeId == 0 || table == null) return null;
            return table.TryGetValue(activeId, out var entry) ? entry : null;
        }
    }

    // —————————————————— 事件入口 ——————————————————

    public static void OnAttack(PlayerCtrl player, int monsterId)
    {
        var entry = Current;
        if (entry == null) return;
        switch (entry.condType)
        {
            case "attackCount":
                Accumulate(entry, player, 1);
                break;
            case "attackCountMob":
                // 「攻击相同怪物 N 次」：换目标就重新计数。
                if (monsterId != mobKey) { mobKey = monsterId; counter = 0; }
                Accumulate(entry, player, 1);
                break;
            case "prob":
                if (Now >= coolUntil && UnityEngine.Random.value < entry.prob) Trigger(entry, player);
                break;
        }
    }

    public static void OnKill(PlayerCtrl player)
    {
        // Job.handleMobKilled / Char.comboKillResetTimer：逐杀累加，间隔5秒归零。
        // 上游已由deaths.TrySettle确保一次死亡只结算一次；与战士连击技能无关。
        if(Now-lastComboKill>=5f) comboKills=0;
        comboKills++; lastComboKill=Now;
        OnCombo(player,comboKills);
        var entry = Current;
        if (entry == null) return;
        if (entry.condType == "killCount") Accumulate(entry, player, 1);
    }

    // 连击击杀：源端是「连击数为 N 的倍数时」，不是每 N 杀一次。
    public static void OnCombo(PlayerCtrl player, int combo)
    {
        var entry = Current;
        if (entry == null || entry.condType != "combokill") return;
        if (entry.count <= 0 || combo <= 0 || combo % entry.count != 0) return;
        TryTrigger(entry, player);
    }

    public static void OnHit(PlayerCtrl player)
    {
        var entry = Current;
        if (entry == null || entry.condType != "hitCount") return;
        Accumulate(entry, player, 1);
    }

    public static void OnSkillUse(PlayerCtrl player)
    {
        var entry = Current;
        if (entry == null || entry.condType != "cooltime") return;
        TryTrigger(entry, player);
    }

    public static void OnDeath(PlayerCtrl player)
    {
        var entry = Current;
        if (entry == null || entry.condType != "die") return;
        TryTrigger(entry, player);
    }

    // 死亡结算：先让「回归」减一次冷却，再把临时增益与计数清掉（activeId 是装备状态，不跟着死）。
    public static void OnPlayerDeath(PlayerCtrl player)
    {
        OnDeath(player);
        buffUntil = -99; buffStat = ""; buffValue = 0;
        ResetProgress();
    }

    public static void OnRune(PlayerCtrl player)
    {
        var entry = Current;
        if (entry == null || entry.condType != "rune") return;
        TryTrigger(entry, player);
    }

    static void Accumulate(Entry entry, PlayerCtrl player, int amount)
    {
        if (Now < coolUntil) return;
        // validtime 内没有新增就清零（源端「若 N 秒内未累积…则重置次数」）。
        if (entry.validtime > 0 && Now - lastEvent > entry.validtime) counter = 0;
        counter += amount;
        lastEvent = Now;
        if (counter < entry.count) return;
        counter = 0;
        Trigger(entry, player);
    }

    static void TryTrigger(Entry entry, PlayerCtrl player)
    {
        if (Now < coolUntil) return;
        Trigger(entry, player);
    }

    static void Trigger(Entry entry, PlayerCtrl player)
    {
        coolUntil = Now + Mathf.Max(0f, (float)entry.cooldown);
        lastEvent = Now;
        switch (entry.effectType)
        {
            case "selfbuff":
                buffStat = entry.stat;
                buffValue = entry.value;
                buffUntil = Now + Mathf.Max(0f, (float)entry.duration);
                break;
            case "reduceCooltime":
                ReduceCooldowns(player, entry.value);
                break;
            case "heal":
                Heal(player, entry.value);
                break;
        }
    }

    static void ReduceCooldowns(PlayerCtrl player, int percent)
    {
        if (player == null) return;
        player.GetComponent<SkillSystem>()?.ReduceCooldowns(percent);
    }

    static void Heal(PlayerCtrl player, int percent)
    {
        if (player == null || player.hp <= 0) return;
        int amount = Mathf.Max(1, Mathf.RoundToInt(player.hpMax * percent / 100f));
        float before = player.hp;
        player.hp = Mathf.Min(player.hpMax, player.hp + amount);
        int gained = Mathf.RoundToInt(player.hp - before);
        if (gained > 0)
            DamageText.Show((Vector3)player.FeetPos + Vector3.up * (60f / Art.PPU), (long)gained, "blue");
    }

    // —————————————————— 增益查询（战斗管线读这里） ——————————————————

    static bool BuffActive(string stat)
        => Now < buffUntil && buffStat == stat;

    public static int DamagePercent => BuffActive("damage") ? buffValue : 0;
    public static int BossDamagePercent => BuffActive("bossDamage") ? buffValue : 0;
    public static int IgnoreDefensePercent => BuffActive("ignoreDefense") ? buffValue : 0;
    public static int CriticalPercent => BuffActive("critical") ? buffValue : 0;
    public static int ExpPercent => BuffActive("exp") ? buffValue : 0;
    public static bool Invincible => BuffActive("invincible");
    public static float Remaining
    {
        get
        {
            if (Now >= buffUntil) return 0f;
            return (float)(buffUntil - Now);
        }
    }
}
