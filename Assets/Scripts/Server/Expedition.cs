using System;
using System.Collections.Generic;

// 远征队（Expedition）—— 对照 gms-server：
//   server/expeditions/Expedition.java
//   server/expeditions/ExpeditionType.java
//   server/expeditions/ExpeditionBossLog.java
//   net/server/channel/Channel.java:411-437（同频道同类型只允许一支）
//
// 这是脚本侧 `cm.getExpedition / cm.createExpedition / cm.endExpedition`
// 与 `em.startInstance(expedition)`、`eim.isExpeditionTeamLackingNow(...)` 的数据来源。
// 规则本身仍然在 scripts/BeiDouSpecial/露希妲远征.js 与 event/*Battle.js 里，C# 只提供对象。
//
// 单机语义：真实在线的角色永远只有本地玩家，广播都落到本地玩家；但成员表、黑名单、
// 招募倒计时、队长判定、成员列表顺序必须与源端一致，否则脚本的
// isLeader / contains / getMemberList 分支会走错。

// ExpeditionType.java:34-50。参数顺序与源端构造一致：
// (minSize, maxSize, minLevel, maxLevel, registrationMinutes)。
public enum ExpeditionType
{
    BALROG_EASY, BALROG_NORMAL, SCARGA, SHOWA, ZAKUM, HORNTAIL,
    CHAOS_ZAKUM, CHAOS_HORNTAIL, ARIANT, ARIANT1, ARIANT2,
    PINKBEAN, CWKPQ, CYGNUS, AKAYRUM, LUCID, KARING,
}

public static class ExpeditionTypes
{
    public static readonly string[] Names = Enum.GetNames(typeof(ExpeditionType));

    public static ExpeditionType[] All => (ExpeditionType[])Enum.GetValues(typeof(ExpeditionType));

    // ExpeditionType.java:34-50 五行参数逐条照抄。
    public static void Stats(ExpeditionType type,
        out int minSize, out int maxSize, out int minLevel, out int maxLevel, out int registrationMinutes)
    {
        minSize = 1; maxSize = 30; registrationMinutes = 5;
        switch (type)
        {
            case ExpeditionType.ARIANT:
            case ExpeditionType.ARIANT1:
            case ExpeditionType.ARIANT2:
                maxSize = 7; minLevel = 20; break;
            case ExpeditionType.BALROG_EASY:
            case ExpeditionType.BALROG_NORMAL:
            case ExpeditionType.ZAKUM:
                minLevel = 50; break;
            case ExpeditionType.CWKPQ:
                minLevel = 90; break;
            case ExpeditionType.SCARGA:
            case ExpeditionType.SHOWA:
            case ExpeditionType.HORNTAIL:
            case ExpeditionType.KARING:
                minLevel = 100; break;
            case ExpeditionType.CHAOS_ZAKUM:
            case ExpeditionType.CHAOS_HORNTAIL:
            case ExpeditionType.PINKBEAN:
                minLevel = 120; break;
            case ExpeditionType.AKAYRUM:
                minLevel = 140; break;
            case ExpeditionType.CYGNUS:
                minLevel = 170; break;
            case ExpeditionType.LUCID:
                minLevel = 220; break;
            default:
                minLevel = 50; break;
        }
        maxLevel = 255;
    }

    public static int MinSize(ExpeditionType t) { Stats(t,out int a,out _,out _,out _,out _); return a; }
    public static int MaxSize(ExpeditionType t) { Stats(t,out _,out int b,out _,out _,out _); return b; }
    public static int MinLevel(ExpeditionType t) { Stats(t,out _,out _,out int c,out _,out _); return c; }
    public static int MaxLevel(ExpeditionType t) { Stats(t,out _,out _,out _,out int d,out _); return d; }
    public static int RegistrationMinutes(ExpeditionType t) { Stats(t,out _,out _,out _,out _,out int e); return e; }

    public static bool TryParse(string name,out ExpeditionType type)
    {
        foreach (var candidate in All)
            if (string.Equals(candidate.ToString(),name,StringComparison.Ordinal)) { type = candidate; return true; }
        type = default; return false;
    }
}

// Expedition.java:61-510。
public sealed class Expedition
{
    // —— 外部注入（Server 层不引用 UnityEngine）——
    public static Func<int> LocalPlayerId;          // 本地角色 id
    public static Action<int,string> Notice;        // PacketCreator.serverNotice(type,text) 广播给队伍
    public static Action<int,string> DropMessage;   // chr.dropMessage(type,text)
    public static Action<int> ShowClock;            // PacketCreator.getClock(seconds)
    public static Action HideClock;                 // PacketCreator.removeClock()
    public static Action<int,int> ChangeMap;        // chr.changeMap(mapId, portal)
    public static Func<string,bool> ConfigBool;     // GameConfig.getServerBoolean
    public static Action<string> Log;               // 战绩日志（源端 log4j）
    public static Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    readonly Dictionary<int,string> members = new Dictionary<int,string>();
    readonly List<int> memberOrder = new List<int>();
    readonly List<int> banned = new List<int>();
    readonly Dictionary<string,string> props = new Dictionary<string,string>();
    readonly List<string> bossLogs = new List<string>();

    public readonly ExpeditionType Type;
    public readonly int LeaderId;
    public readonly string LeaderName;
    public readonly int StartMapId;
    public readonly int MinSize;
    public readonly int MaxSize;
    readonly bool silent;

    public bool Registering { get; private set; }
    public bool Silent => silent;
    public long StartTime { get; private set; }
    public long RegistrationEndAt { get; private set; }
    public bool Disposed { get; private set; }

    public Expedition(int leaderId,string leaderName,int startMapId,ExpeditionType type,
        bool silent=false,int minPlayers=0,int maxPlayers=0)
    {
        LeaderId = leaderId;
        LeaderName = leaderName ?? "";
        StartMapId = startMapId;
        Type = type;
        this.silent = silent;
        ExpeditionTypes.Stats(type,out int minSize,out int maxSize,out _,out _,out _);
        MinSize = minPlayers != 0 ? minPlayers : minSize;
        MaxSize = maxPlayers != 0 ? maxPlayers : maxSize;
        members[leaderId] = LeaderName;
        memberOrder.Add(leaderId);
    }

    public int MemberCount => members.Count;
    public bool IsLeader(int id) => id == LeaderId;
    public bool IsInProgress() => !Registering;
    public bool Contains(int id) => members.ContainsKey(id) || IsLeader(id);
    public IReadOnlyList<string> BossLogs => bossLogs;
    public long GetStartTime() => StartTime;

    // Expedition.java:392-409：队长排第一，其余按加入顺序。
    public List<KeyValuePair<int,string>> GetMemberList()
    {
        var list = new List<KeyValuePair<int,string>>();
        foreach (int id in memberOrder)
            if (id != LeaderId && members.TryGetValue(id,out var name)) list.Add(new KeyValuePair<int,string>(id,name));
        if (members.TryGetValue(LeaderId,out var leaderName)) list.Insert(0,new KeyValuePair<int,string>(LeaderId,leaderName));
        return list;
    }

    public Dictionary<int,string> GetMembers() => new Dictionary<int,string>(members);

    public void SetProperty(string key,string value) => props[key] = value;
    public string GetProperty(string key) => props.TryGetValue(key,out var value) ? value : null;

    // Expedition.java:162-170。
    public void BeginRegistration()
    {
        Registering = true;
        int minutes = ExpeditionTypes.RegistrationMinutes(Type);
        ShowClock?.Invoke(minutes * 60);
        if (!silent)
            Notice?.Invoke(6,"[远征队] [" + LeaderName + "] 已经开启了远征队挑战. 请抓紧时间注册进入远征队！！");
        RegistrationEndAt = Clock() + minutes * 60000L;
    }

    public void FinishRegistration() => Registering = false;

    // Expedition.java:229-238。
    public void Start()
    {
        FinishRegistration();
        HideClock?.Invoke();
        if (!silent) Notice?.Invoke(6,"[远征队] 远征队已经开始! 勇敢的英雄们，祝你们好运！");
        StartTime = Clock();
    }

    // Expedition.java:240-262（返回给玩家看的字符串版）。
    public string AddMember(int id,string name)
    {
        if (!Registering) return "抱歉, 里边的远征队已经进行中. 不能再加入远征队了!";
        if (banned.Contains(id)) return "抱歉, 你已经被远征队长 #b" + LeaderName + "#k 列入了黑名单，不能加入该队伍.";
        if (members.Count >= MaxSize) return "抱歉, 远征队人数已满!";
        if (!ExpeditionBossLog.AttemptBoss(id,Type,false))
            return "抱歉, 你今日的进入次数已经用完! 请明天在进行尝试...";
        Attach(id,name);
        ShowClock?.Invoke((int)((RegistrationEndAt - Clock()) / 1000));
        if (!silent) Notice?.Invoke(6,"[远征队] " + (name ?? "") + " 玩家加入了远征队!");
        return "你已经成功加入了远征队!";
    }

    // Expedition.java:264-281（返回码版）。
    public int AddMemberInt(int id,string name)
    {
        if (!Registering) return 1;
        if (banned.Contains(id)) return 2;
        if (members.Count >= MaxSize) return 3;
        Attach(id,name);
        ShowClock?.Invoke((int)((RegistrationEndAt - Clock()) / 1000));
        if (!silent) Notice?.Invoke(6,"[远征队] " + (name ?? "") + " 玩家已经加入了远征队!");
        return 0;
    }

    void Attach(int id,string name)
    {
        if (!members.ContainsKey(id)) memberOrder.Add(id);
        members[id] = name ?? "";
    }

    // Expedition.java:297-308。
    public bool RemoveMember(int id)
    {
        if (!members.TryGetValue(id,out var name)) return false;
        members.Remove(id);
        memberOrder.Remove(id);
        HideClock?.Invoke();
        if (!silent)
        {
            Notice?.Invoke(6,"[远征队] " + name + " 已经离开了远征队.");
            if (IsLocal(id)) DropMessage?.Invoke(6,"[远征队] 你已经离开了该远征队.");
        }
        return true;
    }

    // Expedition.java:310-331。
    public void Ban(int id)
    {
        if (banned.Contains(id)) return;
        string name = members.TryGetValue(id,out var found) ? found : "";
        banned.Add(id);
        members.Remove(id);
        memberOrder.Remove(id);
        if (!silent) Notice?.Invoke(6,"[远征队] " + name + " 玩家已经被列入了黑名单中.");
        if (!IsLocal(id)) return;
        HideClock?.Invoke();
        if (!silent) DropMessage?.Invoke(6,"[远征队] 你已经被列入该远征队的黑名单中.");
        // Expedition.java:326-328：ARIANT 系列被踢回无舵手之船等候室。
        if (Type == ExpeditionType.ARIANT || Type == ExpeditionType.ARIANT1 || Type == ExpeditionType.ARIANT2)
            ChangeMap?.Invoke(980000000,0);
    }

    static bool IsLocal(int id) => LocalPlayerId == null || LocalPlayerId() == id;

    // Expedition.java:333-350：只登记 BOSS 击杀。
    public void MonsterKilled(int mobId,string mobName)
    {
        if (!ExpeditionBossLog.IsExpeditionBoss(mobId)) return;
        long duration = Clock() - StartTime;
        int seconds = (int)(duration / 1000) % 60;
        int minutes = (int)((duration / 60000) % 60);
        bossLogs.Add("> " + mobName + " was killed after " + minutes + " 分钟 " + seconds + " 秒");
    }

    // Expedition.java:411-429：单机只有本地玩家在线，>1 人恒为同图。
    public bool IsExpeditionTeamTogether() => true;

    // Expedition.java:188-197。
    public void Dispose(bool log)
    {
        if (Disposed) return;
        Disposed = true;
        HideClock?.Invoke();
        if (log && !Registering) WriteBossLog();
        ExpeditionRegistry.Detach(this);
    }

    void WriteBossLog()
    {
        if (bossLogs.Count == 0 || Log == null) return;
        var lines = new List<string> { "[" + Type + "] 的远征队", "队长: " + LeaderName, "击杀BOSS" };
        lines.AddRange(bossLogs);
        Log(string.Join("\n",lines));
    }
}

// Channel.java:411-437 的 expeditions 表 + Expedition.java:176-186 的招募到点回收。
public static class ExpeditionRegistry
{
    static readonly Dictionary<ExpeditionType,Expedition> expeditions = new Dictionary<ExpeditionType,Expedition>();

    public static int Count => expeditions.Count;

    public static Expedition GetExpedition(ExpeditionType type)
        => expeditions.TryGetValue(type,out var exped) && !exped.Disposed ? exped : null;

    // Channel.addExpedition：同类型已存在就 false，否则登记并 beginRegistration。
    public static bool AddExpedition(Expedition exped)
    {
        if (exped == null || expeditions.ContainsKey(exped.Type)) return false;
        expeditions[exped.Type] = exped;
        exped.BeginRegistration();
        return true;
    }

    public static void RemoveExpedition(Expedition exped)
    {
        if (exped == null) return;
        Detach(exped);
    }

    // Expedition.dispose 走这条，避免和 RemoveExpedition 互相递归。
    internal static void Detach(Expedition exped)
    {
        if (exped == null) return;
        if (expeditions.TryGetValue(exped.Type,out var current) && ReferenceEquals(current,exped))
            expeditions.Remove(exped.Type);
    }

    public static void Clear()
    {
        foreach (var exped in new List<Expedition>(expeditions.Values)) exped.Dispose(false);
        expeditions.Clear();
    }

    // 源端用 TimerManager 定时回收（Expedition.java:176-186）；这里做成显式 Tick，
    // 便于客户端驱动与回归快进。到点先广播「组队倒计时已结束」，再摘表并 dispose。
    public static void Tick(long now)
    {
        foreach (var exped in new List<Expedition>(expeditions.Values))
        {
            if (exped.Disposed || !exped.Registering || exped.RegistrationEndAt <= 0) continue;
            if (now < exped.RegistrationEndAt) continue;
            Detach(exped);
            if (!exped.Silent) Expedition.Notice?.Invoke(6,"[远征队] 远征队组建倒计时已结束. 远征队已经被解散。");
            exped.Dispose(false);
        }
    }
}

// ExpeditionBossLog.java。use_enable_daily_expeditions 默认 false，
// 关掉时 attemptBoss 恒 true，所以默认路径不需要 bosslog 表。
public static class ExpeditionBossLog
{
    public struct Entry
    {
        public readonly string Name;
        public readonly int Entries;
        public readonly bool Week;
        public Entry(string name,int entries,bool week=false)
        { Name = name; Entries = entries; Week = week; }
    }

    // ExpeditionBossLog.java:44-54。
    public static readonly Entry[] Entries =
    {
        new Entry("ZAKUM",3), new Entry("CHAOS_ZAKUM",5), new Entry("HORNTAIL",3),
        new Entry("CHAOS_HORNTAIL",5), new Entry("CYGNUS",1), new Entry("AKAYRUM",1),
        new Entry("LUCID",1), new Entry("PINKBEAN",3), new Entry("SCARGA",1), new Entry("PAPULATUS",2),
    };

    // bosslog_daily 的读写口，由 GameSession 注入。
    public static Func<int,string,int> CountEntries;
    public static Action<int,string> InsertEntry;

    public static bool AttemptBoss(int characterId,ExpeditionType type,bool log)
    {
        // 未接开关时按源端默认（false）放行。
        if (Expedition.ConfigBool == null) return true;
        if (!Expedition.ConfigBool("use_enable_daily_expeditions")) return true;
        Entry entry = default; bool found = false;
        foreach (var candidate in Entries)
            if (candidate.Name == type.ToString()) { entry = candidate; found = true; break; }
        if (!found) return true;
        if (CountEntries != null && CountEntries(characterId,entry.Name) >= entry.Entries) return false;
        if (log) InsertEntry?.Invoke(characterId,entry.Name);
        return true;
    }

    // Expedition.java:64-126 的 EXPEDITION_BOSSES（MobId.java 真值）+ isCygnusBoss 区间。
    static readonly int[] BossMobs =
    {
        // ZAKUM_1..3 = 8800000..8800002，ZAKUM_ARM_1..8 = 8800003..8800010
        8800000,8800001,8800002,8800003,8800004,8800005,8800006,8800007,8800008,8800009,8800010,
        // CHAOS_ZAKUM_1..3 = 8800100..8800102，CHAOS_ZAKUM_ARM_1..8 = 8800103..8800110
        8800100,8800101,8800102,8800103,8800104,8800105,8800106,8800107,8800108,8800109,8800110,
        // HORNTAIL_PREHEAD_LEFT/RIGHT(8810000/8810001)、HEAD_A/B/C(8810002..8810004)、
        // HAND_LEFT/RIGHT(8810005/8810006)、WINGS(8810007)、LEGS(8810008)、TAIL(8810009)
        8810000,8810001,8810002,8810003,8810004,8810005,8810006,8810007,8810008,8810009,
        // CHAOS_HORNTAIL_PREHEAD..TAIL = 8810100..8810109、CHAOS_HORNTAIL=8810118、CHAOS_HORNTAIL_5=8810122
        8810100,8810101,8810102,8810103,8810104,8810105,8810106,8810107,8810108,8810109,8810118,8810122,
        // CYGNUS_MIHILE..HAWKEYE(8850000..8850004)、CYGNUS(8850011)
        8850000,8850001,8850002,8850003,8850004,8850011,
        // LUCID_PHASE_1/2/3
        8880140,8880141,8880152,
        // SCARLION_STATUE/SCARLION/ANGRY/FURIOUS
        9420546,9420547,9420548,9420549,
        // TARGA_STATUE/TARGA/ANGRY/FURIOUS
        9420541,9420542,9420543,9420544,
    };

    // MobId.isCygnusBoss：8850000..8850013 全区间都算。
    const int CygnusBossMin = 8850000, CygnusBossMax = 8850013;

    public static bool IsExpeditionBoss(int mobId)
    {
        if (mobId >= CygnusBossMin && mobId <= CygnusBossMax) return true;
        foreach (int id in BossMobs) if (id == mobId) return true;
        return false;
    }
}
