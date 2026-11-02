// GM 活动竞技场（雪球大战 / 椰子树）—— 源端 server/events/gm/Snowball.java、Coconut.java、
// Coconuts.java + SnowballHandler.java、CoconutHandler.java 的直译合并。
//
// 源端语义（数值 1:1）：
//   雪球（地图 109060000，10 分钟）：
//     · 每队一个雪球 {position 0, hits 3, snowmanhp 1000, hittable}
//     · 客户端包 what(1B)：what%2 必须 == 队伍；同角色 500ms 攻击间隔
//     · what 0/1 = 推雪球：对方雪人 HP>0 时 damage=10（hits--，3 次 position++）
//       对方雪人 HP==0 时 damage=0（源端 hit() 的 damage 分支在 what<2 且 damage>0 时
//       只走 hits--，damage<=0 时对雪人扣血——但 damage 恒 >=0，扣血分支实际不可达，照抄结构）
//     · what 2/3 = 打雪人：3% → 45 / 其余 15（仅广播表现，不改状态 —— 源端 hit() 只处理 what<2）
//     · 里程碑 45/290/560 → 源端给对方队 snowballMessage(1/2/3)，这里折进 notice
//     · 600 秒后比 position 定胜负；10 秒结算窗口后按队传送（胜 109050000 / 负 109050001）
//   椰子（地图 109080000，5 分钟 + 平局加时 120 秒）：
//     · 506 个椰子；可击落 401、炸弹 80、哑弹 20（源端 Coconut 构造值）
//     · 打椰子：该椰子 750ms 内不重复结算；hits>2 且 40% 进入掉落判定：
//       1% 且哑弹余量>0 → 哑弹（不再可打）；5% 且炸弹余量>0 → 炸弹；否则击落 → 按
//       打击者队伍计分（Maple=0 / Story=1），源端广播 serverNotice(5) + coconutScore
//     · 300 秒后：平分 → 加时 120 秒再判；否则 12 秒结算窗口后按队传送
//
// 单机化改造（与 CPQ 同口径处理，均注明）：
//   1. 源端由 GM 事件脚本触发 startEvent；项目无 GM，进图（Sync）即自动开局并按加入
//      顺序分队（对齐源端 NPCConversationManager:671 的 getLimit()%2 交替分队）。
//   2. 椰子 id 源端由客户端逐个上报；项目 HUD 不逐个点椰子，param=-1 时服务端在可打
//      椰子里随机取一个（概率流程与源端完全一致）。
//   3. 里程碑/胜负表现走聊天行 + HUD（源端走专用包驱动地图对象动画），状态机与判定忠实。
//   4. 假人不参战（无活动 AI）；单人=对方球停在 0，推完必胜。
using System;
using System.Collections.Generic;

public enum ArenaOperation { Sync = 0, Hit = 1, Leave = 2 }

public sealed class ArenaRequest
{
    public int characterId;
    public ArenaOperation operation;
    // Hit：雪球=what(0..3)；椰子=椰子 id（-1 = 服务端随机挑一个可打的）
    public int param;
}

public sealed class ArenaResponse
{
    public bool ok;
    public string error;

    public int kind;              // 0 无活动 / 1 雪球 / 2 椰子
    public int myTeam;            // 0 Maple / 1 Story
    public bool ended;
    public long timeLeftMs;
    public int warpTo;            // 非 0 = 已结算，客户端换图（109050000 胜 / 109050001 负）

    // 雪球
    public int myPosition, otherPosition, mySnowmanHp, otherSnowmanHp;

    // 椰子
    public int mapleScore, storyScore, bombings, falling, stopped;

    public string notice;         // 本次动作累积的公告（客户端进聊天行）
}

public static class NativeEventArenaService
{
    public const int SnowballMap = 109060000;
    public const int CoconutMap = 109080000;
    public const int WinnerMap = 109050000;
    public const int ExitMap = 109050001;

    // —— 源端常量 ——
    const int SnowballDurationMs = 600_000, SnowballWarpDelayMs = 10_000;
    const int SnowballAttackIntervalMs = 500, SnowmanHpMax = 1000, HitsPerRoll = 3;
    const int CoconutDurationMs = 300_000, CoconutBonusMs = 120_000, CoconutWarpDelayMs = 12_000;
    const int CoconutHitLockMs = 750, CoconutCount = 506;
    const int CountBombing = 80, CountFalling = 401, CountStopped = 20;

    static readonly Random rng = new Random();
    // 测试注入口：默认真随机
    static Func<double> random = () => rng.NextDouble();
    public static void SetRandom(Func<double> r) => random = r ?? (() => rng.NextDouble());
    // 测试注入口：时钟（节流/锁/时长都走它）。Stopwatch = 进程级单调毫秒（Unity 目标框架无 TickCount64）
    static readonly System.Diagnostics.Stopwatch boot = System.Diagnostics.Stopwatch.StartNew();
    static Func<long> clock = () => boot.ElapsedMilliseconds;
    public static void SetClock(Func<long> c) { clock = c != null ? c : (Func<long>)(() => boot.ElapsedMilliseconds); }

    abstract class Arena
    {
        public readonly int mapId;
        public readonly Dictionary<int, int> teams = new Dictionary<int, int>();
        public long startAt, endAt, clearedAt;
        public bool ended;
        public int winnerTeam = -1;
        public readonly List<string> notices = new List<string>();

        protected Arena(int mapId) { this.mapId = mapId; }
        public abstract int Kind { get; }
        public int TeamOf(int cid) => teams.TryGetValue(cid, out var t) ? t : -1;
        public void Notice(string msg) => notices.Add(msg);
    }

    sealed class SnowballArena : Arena
    {
        public sealed class Ball { public int position, hits = HitsPerRoll, snowmanHp = SnowmanHpMax; }
        public readonly Ball[] balls = { new Ball(), new Ball() };
        public readonly Dictionary<int, long> lastAttack = new Dictionary<int, long>();
        public SnowballArena() : base(SnowballMap) { }
        public override int Kind => 1;
    }

    sealed class CoconutArena : Arena
    {
        public sealed class Nut { public int hits; public bool hittable = true; public long hitTime; }
        public readonly Nut[] nuts = new Nut[CoconutCount];
        public int mapleScore, storyScore, bombings = CountBombing, falling = CountFalling, stopped = CountStopped;
        public bool bonusUsed;
        public CoconutArena() : base(CoconutMap)
        {
            for (int i = 0; i < CoconutCount; i++) nuts[i] = new Nut();
        }
        public override int Kind => 2;
    }

    static readonly Dictionary<int, Arena> arenas = new Dictionary<int, Arena>();   // mapId → arena
    static readonly object gate = new object();

    public static void ResetForTest()
    {
        lock (gate) { arenas.Clear(); random = () => rng.NextDouble(); clock = () => boot.ElapsedMilliseconds; }
    }

    // —————————————————— 入口 ——————————————————
    public static ArenaResponse Handle(DbStore db, ArenaRequest r)
    {
        if (r == null) return new ArenaResponse { ok = false, error = "无效请求" };
        lock (gate)
        {
            switch (r.operation)
            {
                case ArenaOperation.Sync: return Sync(db, r.characterId);
                case ArenaOperation.Hit: return Hit(db, r.characterId, r.param);
                case ArenaOperation.Leave: return Leave(db, r.characterId);
                default: return new ArenaResponse { ok = false, error = "无效操作" };
            }
        }
    }

    static int MapOf(DbStore db, int cid)
    {
        var rec = CharactersDao.FindById(db, cid);
        return rec == null ? -1 : (int)rec.map;
    }

    // 进图自动开局 + 分队（源端 getLimit()%2 交替）；离开地图自动退场
    static ArenaResponse Sync(DbStore db, int cid)
    {
        int map = MapOf(db, cid);
        if (map != SnowballMap && map != CoconutMap)
        {
            LeaveAll(db, cid);
            return new ArenaResponse { ok = true, kind = 0 };
        }
        var arena = EnsureArena(map);
        Tick(arena);
        if (arena.TeamOf(cid) < 0 && !arena.ended)
            arena.teams[cid] = arena.teams.Count % 2;   // 交替分队
        return Snapshot(arena, cid);
    }

    static Arena EnsureArena(int map)
    {
        if (!arenas.TryGetValue(map, out var arena) || (arena.ended && clock() > arena.clearedAt))
        {
            arena = map == SnowballMap ? new SnowballArena() : (Arena)new CoconutArena();
            arena.startAt = clock();
            arena.endAt = arena.startAt + (map == SnowballMap ? SnowballDurationMs : CoconutDurationMs);
            arenas[map] = arena;
        }
        return arena;
    }

    static ArenaResponse Leave(DbStore db, int cid)
    {
        LeaveAll(db, cid);
        return new ArenaResponse { ok = true, kind = 0 };
    }

    static void LeaveAll(DbStore db, int cid)
    {
        var expired = new List<int>();
        foreach (var kv in arenas)
        {
            kv.Value.teams.Remove(cid);
            if (kv.Value.teams.Count == 0) expired.Add(kv.Key);
        }
        foreach (var map in expired) arenas.Remove(map);
    }

    // —————————————————— 雪球/椰子打击 ——————————————————
    static ArenaResponse Hit(DbStore db, int cid, int param)
    {
        int map = MapOf(db, cid);
        if (!arenas.TryGetValue(map, out var arena)) return new ArenaResponse { ok = false, error = "这里没有进行中的活动" };
        Tick(arena);
        if (arena.ended) return Snapshot(arena, cid);
        int team = arena.TeamOf(cid);
        if (team < 0) return new ArenaResponse { ok = false, error = "你不在活动队伍里" };
        return arena is SnowballArena sb ? HitSnowball(sb, cid, team, param) : HitCoconut((CoconutArena)arena, db, cid, team, param);
    }

    // SnowballHandler.handlePacket 直译
    static ArenaResponse HitSnowball(SnowballArena arena, int cid, int team, int what)
    {
        var mine = arena.balls[team];
        var other = arena.balls[team == 0 ? 1 : 0];
        if (mine.snowmanHp == 0) return Snapshot(arena, cid);
        long now = clock();
        if (arena.lastAttack.TryGetValue(cid, out var last) && now - last < SnowballAttackIntervalMs)
            return Snapshot(arena, cid);
        if (team != what % 2) return new ArenaResponse { ok = false, error = "打错了目标" };
        arena.lastAttack[cid] = now;

        int damage = 0;
        if (what < 2 && other.snowmanHp > 0) damage = 10;
        else if (what == 2 || what == 3) damage = random() < 0.03 ? 45 : 15;

        if (what >= 0 && what <= 4) RollHit(arena, mine, other, what, damage);
        return Snapshot(arena, cid);
    }

    // Snowball.hit() 直译
    static void RollHit(SnowballArena arena, SnowballArena.Ball mine, SnowballArena.Ball other, int what, int damage)
    {
        if (what < 2)
        {
            if (damage > 0) mine.hits--;
            else if (mine.snowmanHp - damage < 0)
            {
                // 源端：雪人被打破 → HP=0，10 秒后恢复 7500（TimerManager.schedule）。
                // 该分支实际不可达（damage<2 时恒为 10 或 0，0 不可能击破 1000 HP），照抄结构。
                mine.snowmanHp = 0;
            }
            else mine.snowmanHp -= damage;
        }
        if (mine.hits == 0)
        {
            mine.position++;
            // 源端里程碑：给对方队的球发 snowballMessage(1/2/3)
            switch (mine.position)
            {
                case 45: arena.Notice("对方的雪球已经滚到第一段了！"); break;
                case 290: arena.Notice("对方的雪球已经滚到第二段了！"); break;
                case 560: arena.Notice("对方的雪球已经滚到第三段了！"); break;
            }
            mine.hits = HitsPerRoll;
        }
    }

    // CoconutHandler.handlePacket 直译
    static ArenaResponse HitCoconut(CoconutArena arena, DbStore db, int cid, int team, int id)
    {
        long now = clock();
        // 单机化：id=-1 由服务端在可打椰子里随机挑（源端由客户端上报具体椰子 id）
        if (id < 0 || id >= CoconutCount)
        {
            var candidates = new List<int>();
            for (int i = 0; i < CoconutCount; i++)
                if (arena.nuts[i].hittable && now >= arena.nuts[i].hitTime) candidates.Add(i);
            if (candidates.Count == 0) return Snapshot(arena, cid);
            id = candidates[rng.Next(candidates.Count)];
        }
        var nut = arena.nuts[id];
        if (!nut.hittable) return Snapshot(arena, cid);
        if (now < nut.hitTime) return Snapshot(arena, cid);

        if (nut.hits > 2 && random() < 0.4)
        {
            if (random() < 0.01 && arena.stopped > 0)
            {
                nut.hittable = false;
                arena.stopped--;
                return Snapshot(arena, cid);   // 源端 hitCoconut(false,id,1) 哑弹表现
            }
            nut.hittable = false;
            nut.hits = 0;
            if (random() < 0.05 && arena.bombings > 0)
            {
                arena.bombings--;
                return Snapshot(arena, cid);   // hitCoconut(false,id,2) 炸弹表现
            }
            if (arena.falling > 0)
            {
                arena.falling--;
                string name = CharactersDao.FindById(db, cid)?.name ?? "?";
                if (team == 0)
                {
                    arena.mapleScore++;
                    arena.Notice(name + " of Team Maple knocks down a coconut.");
                }
                else
                {
                    arena.storyScore++;
                    arena.Notice(name + " of Team Story knocks down a coconut.");
                }
                // 源端 coconutScore 广播随 Snapshot 下发
            }
        }
        else
        {
            nut.hits++;
            nut.hitTime = now + CoconutHitLockMs;
        }
        return Snapshot(arena, cid);
    }

    // —————————————————— 时间推进与结算 ——————————————————
    static void Tick(Arena arena)
    {
        if (arena.ended) return;
        long now = clock();
        if (now < arena.endAt) return;

        arena.ended = true;
        arena.clearedAt = now + (arena is SnowballArena ? SnowballWarpDelayMs : CoconutWarpDelayMs);

        if (arena is SnowballArena sb)
        {
            // 源端 startEvent 定时器：比 position 定胜负（平局双方都走失败传送）
            sb.winnerTeam = sb.balls[0].position > sb.balls[1].position ? 0
                          : sb.balls[1].position > sb.balls[0].position ? 1 : -1;
            sb.Notice(sb.winnerTeam < 0 ? "雪球大战平局！" : "Team " + (sb.winnerTeam == 0 ? "Maple" : "Story") + " 获胜！");
        }
        else if (arena is CoconutArena co)
        {
            // 源端：平分 → bonusTime 120 秒再判
            if (co.mapleScore == co.storyScore && !co.bonusUsed)
            {
                co.bonusUsed = true;
                co.ended = false;
                co.endAt = now + CoconutBonusMs;
                co.Notice("平分！加时 2 分钟！");
                return;
            }
            co.winnerTeam = co.mapleScore > co.storyScore ? 0 : co.storyScore > co.mapleScore ? 1 : -1;
            for (int i = 0; i < CoconutCount; i++) co.nuts[i].hittable = false;
            co.Notice(co.winnerTeam < 0 ? "椰子树大战平局！" : "Team " + (co.winnerTeam == 0 ? "Maple" : "Story") + " 获胜！");
        }
    }

    static ArenaResponse Snapshot(Arena arena, int cid)
    {
        var res = new ArenaResponse { ok = true, kind = arena.Kind, myTeam = arena.TeamOf(cid) };
        long now = clock();
        if (!arena.ended) res.timeLeftMs = Math.Max(0, arena.endAt - now);
        else res.ended = true;

        if (arena is SnowballArena sb)
        {
            int t = Math.Max(0, res.myTeam);
            res.myPosition = sb.balls[t].position;
            res.otherPosition = sb.balls[t == 0 ? 1 : 0].position;
            res.mySnowmanHp = sb.balls[t].snowmanHp;
            res.otherSnowmanHp = sb.balls[t == 0 ? 1 : 0].snowmanHp;
            if (sb.winnerTeam >= 0 && res.myTeam >= 0)
                res.warpTo = res.myTeam == sb.winnerTeam ? WinnerMap : ExitMap;
        }
        else if (arena is CoconutArena co)
        {
            res.mapleScore = co.mapleScore;
            res.storyScore = co.storyScore;
            res.bombings = co.bombings;
            res.falling = co.falling;
            res.stopped = co.stopped;
            if (co.winnerTeam >= 0 && res.myTeam >= 0)
                res.warpTo = res.myTeam == co.winnerTeam ? WinnerMap : ExitMap;
        }
        // 一次性吐出累积的公告
        if (arena.notices.Count > 0)
        {
            res.notice = string.Join("\n", arena.notices);
            arena.notices.Clear();
        }
        return res;
    }
}
