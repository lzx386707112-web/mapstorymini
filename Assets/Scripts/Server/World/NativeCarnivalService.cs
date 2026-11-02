// 怪物嘉年华 CPQ —— 源端 MonsterCarnival.java + MonsterCarnivalHandler.java + CarnivalFactory.java 直译（单机化）。
// 源端语义：
//   两队进战斗图（红 portal 0/紫图 2、蓝 1），计时 map.info/monsterCarnival/timeDefault 后按 totalCP 判胜；
//   结束前 10s complete() 快照 totalCP 为 timeupCP（终盘再杀不影响判定，快照相同则 extendTime 3 分钟）；
//   CP：杀怪得 getCP（Character.gainCP：gain>0 才涨 totalCP；扣 CP 不回退 totalCP）；
//   finish：胜队每人 gainFestivalPoints(己方 totalCP) → 传送 赢=map+2(cpq1)/map+200(cpq2)、输=map+3/map+300；
//   召唤怪物(tab0)/守护者(tab2)/debuff(tab1) 需地图 info/monsterCarnival 与 Skill/MCSkill|MCGuardian.img 数据。
// 数据残缺（本 GMS083 数据包，源端同样受影响）：全部 9 张战斗图 info 无 monsterCarnival 子树、Skill 目录无
//   MCSkill.img/MCGuardian.img → 召唤列表/守护者/debuff 无数据可用（Handler tab0 的 mobsToSpawn 为空 →
//   num>=size → CPQMessage(1)，与源端一致）。计时兜底：timeDefault 600s / timeExpand 180s（构造函数 10 分钟
//   与 extendTime 3 分钟的源端字面量佐证）。
// 单机化决策：红队=玩家（use_enable_solo_expeditions 语义），蓝队=假人对手——真实对抗不可行，
//   蓝队 CP 由 BotTick 速率模拟（注释明示）；victim/partner、ChallengeParty 匹配（MatchChecker）不做；
//   仅 CPQ1 六个 field（cpq2 的 9800311xx 地图几何未分片，留待后续）。
using System;
using System.Collections.Generic;

public sealed class CarnivalRequest
{
    public int characterId;
    public int operation;       // 0 进入 / 1 杀怪 CP / 2 状态查询 / 3 离开（earlyFinish）
    public int mapId;           // operation 0：目标战斗图
    public int cp;              // operation 1：本次击杀的 getCP
}

public sealed class CarnivalResponse
{
    public bool ok;
    public string error;
    public bool inCarnival;     // 当前处于战斗
    public int mapId;
    public int team;            // 玩家恒 0（红）
    public int redCp, blueCp, redTotal, blueTotal;
    public int secondsLeft;
    public bool finished;       // 本次请求触发了结算
    public int winner;          // 结算时：0 红 / 1 蓝
    public int warpTo;          // 结算传送目标图（0 = 不传）
    public int festivalPoints;  // 累计 rank 分（Spiegelmann 兑换读这个）
    public bool win;            // 玩家是否获胜
}

public static class NativeCarnivalService
{
    public sealed class Session
    {
        public int mapId;
        public long deadline;
        public bool extended;           // 平局后已延长一次（源端可无限延长，单机限一次防止假人模拟下永不结束）
        public int redCp, blueCp, redTotal, blueTotal;
        public int summons;             // 召唤计数（map.maxMobs 上限；数据缺失恒 0 上限）
    }

    public static Session Active;
    public static int FestivalPoints;   // 源端 Character.festivalPoints（内存，登录重置）

    public static Func<long> Now = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static Func<long> BotTickMs = null;   // 蓝队 CP 模拟注入（测试）
    static long lastBotTick;

    const int TimeDefault = 600;        // 秒（源端构造 10 分钟）
    const int TimeExpand = 180;         // 秒（源端 extendTime 3 分钟）
    const int MinLevel = 30, MaxLevel = 50;   // 2042000.js cpqMinLvl/cpqMaxLvl

    static readonly int[] Cpq1Fields = { 980000101, 980000201, 980000301, 980000401, 980000501, 980000601 };

    public static void ResetForTest() { Active = null; FestivalPoints = 0; Now = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); BotTickMs = null; lastBotTick = 0; }

    // 登出 = 中局弃权：源端 Character 对象销毁即失去 monsterCarnival 引用；
    // 单机没有其他真人续场，继续计时只会挂机白送胜利，故丢弃会话不发奖励。
    public static void AbandonOnLogout() { Active = null; lastBotTick = 0; }

    static bool IsCpq1Field(int mapId)
    {
        foreach (int f in Cpq1Fields) if (f == mapId) return true;
        return false;
    }

    // MonsterCarnival 构造：两队入图、CP 清零、开始计时
    public static CarnivalResponse Handle(DbStore db, CarnivalRequest r)
    {
        var resp = new CarnivalResponse();
        var self = db.Table("characters").Find(x => DbStore.Int(x, "id") == r.characterId);
        if (self == null) { resp.error = "角色不存在"; return resp; }
        long now = Now();
        if (Active != null && now >= Active.deadline) Finish(resp, self, db, now);   // 惰性结算
        BotTick(db, r.characterId);   // 蓝队假人杀怪模拟（真实服务端由对手玩家杀怪驱动）

        switch (r.operation)
        {
            case 0: // 进入（源端 cpqLobby → MonsterCarnival 构造）
            {
                if (Active != null) { resp.error = "已有嘉年华战斗正在进行"; return resp; }
                if (!IsCpq1Field(r.mapId)) { resp.error = "不是怪物嘉年华战斗图"; return resp; }
                int level = DbStore.Int(self, "level");
                if (level < MinLevel || level > MaxLevel) { resp.error = "等级须在 " + MinLevel + "~" + MaxLevel + " 之间"; return resp; }
                Active = new Session { mapId = r.mapId, deadline = now + TimeDefault * 1000L };
                lastBotTick = now;
                resp.ok = true; resp.inCarnival = true;
                FillLive(resp, now);
                return resp;
            }
            case 1: // 杀怪 CP（MapleMap.killMonster → gainCP(getCP)）；负值 = 花费/结算扣减（源端 gainCP 同径）
            {
                if (Active == null) { resp.error = "不在嘉年华战斗中"; return resp; }
                // Character.gainCP：gain>0 才涨 totalCP；花费/扣减不回退 totalCP，可花费 CP 不为负
                Active.redCp += r.cp;
                if (r.cp > 0) Active.redTotal += r.cp;
                if (Active.redCp < 0) Active.redCp = 0;
                resp.ok = true; FillLive(resp, now);
                return resp;
            }
            case 2: // 状态查询（HUD 拉取）
                resp.ok = true; FillLive(resp, now);
                return resp;
            case 3: // 离开（源端 playerDisconnected → earlyFinish → dispose，判负离场）
                if (Active == null) { resp.error = "不在嘉年华战斗中"; return resp; }
                Finish(resp, self, db, now, forceLose: true);
                return resp;
            default:
                resp.error = "未知操作"; return resp;
        }
    }

    static void FillLive(CarnivalResponse resp, long now)
    {
        if (Active == null) return;
        resp.inCarnival = true;
        resp.mapId = Active.mapId;
        resp.team = 0;
        resp.redCp = Active.redCp; resp.blueCp = Active.blueCp;
        resp.redTotal = Active.redTotal; resp.blueTotal = Active.blueTotal;
        resp.secondsLeft = (int)Math.Max(0, (Active.deadline - now) / 1000);
        resp.festivalPoints = FestivalPoints;
    }

    // 蓝队假人杀怪模拟：每秒判定一次（真实服务端无此逻辑——单机对手）
    public static void BotTick(DbStore db, int characterId)
    {
        if (Active == null || BotTickMs == null) return;
        long now = Now();
        if (now >= Active.deadline) { Handle(db, new CarnivalRequest { characterId = characterId, operation = 2 }); return; }
        if (lastBotTick == 0) lastBotTick = now;
        while (now - lastBotTick >= 1000)
        {
            lastBotTick += 1000;
            long roll = BotTickMs();
            if (roll >= 90) continue;                       // 空过秒（寻路/死亡）
            if (roll < 40) { Active.blueCp += 1; Active.blueTotal += 1; }
            else if (roll < 60) { Active.blueCp += 2; Active.blueTotal += 2; }
            else { Active.blueCp += 1; Active.blueTotal += 1; }
            if (Active.blueCp < 0) Active.blueCp = 0;
        }
    }

    // MonsterCarnival.complete + timeUp + finish
    static void Finish(CarnivalResponse resp, DbRow self, DbStore db, long now, bool forceLose = false)
    {
        var s = Active; if (s == null) return;
        int winner;
        if (!forceLose && s.redTotal == s.blueTotal && !s.extended)
        {
            // timeUp 平局 → extendTime（3 分钟）；单机限一次
            s.extended = true;
            s.deadline = now + TimeExpand * 1000L;
            resp.ok = true; FillLive(resp, now);
            return;
        }
        winner = forceLose ? 1 : s.redTotal > s.blueTotal ? 0 : 1;
        bool cpq1 = IsCpq1Field(s.mapId);
        // finish：胜队 map+2（cpq1）/map+200（cpq2），负队 map+3/map+300
        int baseMap = s.mapId / 100 * 100;              // 980000201 → 980000200 段
        int winMap, loseMap;
        if (cpq1) { winMap = baseMap + 3; loseMap = baseMap + 4; }   // 980000201+2=980000203
        else { winMap = baseMap + 200; loseMap = baseMap + 300; }
        bool playerWin = winner == 0;
        int myTotal = playerWin ? s.redTotal : s.blueTotal;
        // gainFestivalPoints(己方 totalCP)：rank 分给赢的一队（源端 finish 只给胜队）
        if (playerWin) FestivalPoints += myTotal;

        resp.ok = true;
        resp.finished = true;
        resp.inCarnival = false;
        resp.winner = winner;
        resp.win = playerWin;
        resp.warpTo = playerWin ? winMap : loseMap;
        resp.festivalPoints = FestivalPoints;
        resp.redTotal = s.redTotal; resp.blueTotal = s.blueTotal;
        Active = null;
    }
}
