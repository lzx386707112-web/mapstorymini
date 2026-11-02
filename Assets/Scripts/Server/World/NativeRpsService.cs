// 猜拳小游戏（Rock Paper Scissors）—— 源端 server/minigame/RockPaperScissor.java +
// net/server/channel/handlers/RPSActionHandler.java 的直译。
//
// 源端语义（数值 1:1）：
//   · 入口：NPC 9000019（RPS Admin）脚本 openRPSNPC 打开窗口；开一局付 1000 金币。
//   · 每局 10 轮；玩家出 0石头/1剪刀/2布（客户端包 answer 0..2），服务端 Randomizer.nextInt(3)。
//   · 平局：rpsSelection(服务器手, 当前轮) —— 可以继续出（同一轮重答）。
//   · 玩家赢：rpsSelection(服务器手, round+1)，win=true，点「继续」→ nextRound：round++；
//     round<10 → rpsMode(0x0C) 下一轮；round==10 → reward：发证书 4031332+round。
//   · 玩家输：rpsSelection(服务器手, -1)，ableAnswer=false；点「离开」→ dispose（win=false 无奖励）。
//   · 超时：rpsMode(0x0A)；之后任何操作 → reward（win 才发）+ 关窗 0x0D。
//   · 奖励只有 win 状态下 reward 才发：即「赢着离场」才拿证书（4031332+round，round=已赢轮数）。
//
// 与源端的差异（均注明）：
//   1. 源端构造函数只在 mode==0（首局）扣 1000 金币，mode==5（再来一局）不扣——
//      但 handler 对 0/5 都先校验 meso>=1000，意图明显是每局都收费；照意图两种都扣（源端 bug 修正）。
//   2. 源端 handler 校验地图上有 NPC 9000019 才受理；本工程 NPC 出生是客户端视图层
//      （910000000 补充出生，见 NativeMapView.ExtraNpcs），服务端无 life 数据，免查。
//   3. 会话在内存（源端 chr.setRPS 同生命周期），换角色由调用方 Reset。
using System;
using System.Collections.Generic;

public enum RpsOperation { Start = 0, Answer = 1, TimeOver = 2, Continue = 3, Leave = 4, Retry = 5 }

public sealed class RpsRequest
{
    public int characterId;
    public RpsOperation operation;
    public int answer;          // Answer：0 石头 / 1 剪刀 / 2 布
}

public sealed class RpsResponse
{
    public bool ok;
    public string error;

    // —— 下行码（对齐 RPS_GAME 包）——
    public int mode;            // 9 开局 / 14 再来一局 / 0x0A 超时 / 0x0B 出手结果 / 0x0C 下一轮 / 0x0D 关窗 / 6 金币不足
    public int selection = -1;  // 0x0B：服务器出的手（0 石 1 剪 2 布）
    public int roundLabel;      // 0x0B：轮数标签（赢=round+1 / 输=-1 / 平=round）
    public int rewardItem;      // 非 0 = 本次发了证书道具
    public bool closed;         // true = 窗口该关（0x0D）
}

public static class NativeRpsService
{
    public const int RpsAdminNpc = 9000019;
    public const int CertificateBase = 4031332;   // ItemId.RPS_CERTIFICATE_BASE
    const int EntryFee = 1000;
    const int Rounds = 10;

    sealed class Session
    {
        public int round;          // 已完成的胜场数（源端 round）
        public bool ableAnswer = true;
        public bool win;
    }

    static readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();
    static readonly Random rng = new Random();
    static Func<int> random = () => rng.Next(3);
    public static void SetRandom(Func<int> r) => random = r ?? (() => rng.Next(3));

    public static void ResetForTest() { sessions.Clear(); random = () => rng.Next(3); }
    public static void EndSession(int characterId) => sessions.Remove(characterId);

    // 登出清场：源端 RPS 会话挂在 Character 对象上，登出即销毁（无中途费用挂账，开局费已扣）
    public static void ClearSessions() => sessions.Clear();

    // —————————————————— 入口 ——————————————————
    public static RpsResponse Handle(DbStore db, RpsRequest r)
    {
        if (r == null) return new RpsResponse { ok = false, error = "无效请求" };
        sessions.TryGetValue(r.characterId, out var s);
        switch (r.operation)
        {
            case RpsOperation.Start:
            case RpsOperation.Retry:
                return Start(db, r.characterId, s, r.operation == RpsOperation.Retry);
            case RpsOperation.Answer: return Answer(db, r.characterId, s, r.answer);
            case RpsOperation.TimeOver: return TimeOver(db, r.characterId, s);
            case RpsOperation.Continue: return NextRound(db, r.characterId, s);
            case RpsOperation.Leave: return Dispose(db, r.characterId, s);
            default: return new RpsResponse { ok = false, error = "无效操作" };
        }
    }

    static RpsResponse Start(DbStore db, int cid, Session s, bool retry)
    {
        // 源端 handler：0/5 都先 reward 旧会话（win 才发），再校验金币
        int? rewarded = RewardIfWin(db, cid, s);
        var rec = CharactersDao.FindById(db, cid);
        if (rec.meso < EntryFee)
            return new RpsResponse { ok = false, mode = 6, error = "金币不足，无法开始游戏。" };
        rec.meso -= EntryFee;
        CharactersDao.Save(db, rec);
        sessions[cid] = new Session();
        return new RpsResponse { ok = true, mode = retry ? 14 : 9, rewardItem = rewarded ?? 0 };
    }

    // RockPaperScissor.answer 直译
    static RpsResponse Answer(DbStore db, int cid, Session s, int answer)
    {
        if (s == null || !s.ableAnswer || s.win || answer < 0 || answer > 2)
        {
            int? rewarded = RewardIfWin(db, cid, s);
            sessions.Remove(cid);
            return new RpsResponse { ok = false, mode = 0x0D, closed = true, rewardItem = rewarded ?? 0,
                error = "当前不能出手。" };
        }
        int response = random();
        if (response == answer)
        {
            // 平局：同轮可以再出
            return new RpsResponse { ok = true, mode = 0x0B, selection = response, roundLabel = s.round };
        }
        if ((answer == 0 && response == 2) || (answer == 1 && response == 0) || (answer == 2 && response == 1))
        {
            // 玩家赢：label = round+1，点「继续」进下一轮
            s.win = true;
            s.ableAnswer = false;
            return new RpsResponse { ok = true, mode = 0x0B, selection = response, roundLabel = s.round + 1 };
        }
        // 玩家输：label = -1，会话保留（离场/再来一局时结算，win=false 无奖励）
        s.ableAnswer = false;
        return new RpsResponse { ok = true, mode = 0x0B, selection = response, roundLabel = -1 };
    }

    // RockPaperScissor.timeOut 直译：ableAnswer && !win → 0x0A，不结算
    static RpsResponse TimeOver(DbStore db, int cid, Session s)
    {
        if (s != null && s.ableAnswer && !s.win)
        {
            s.ableAnswer = false;
            return new RpsResponse { ok = true, mode = 0x0A };
        }
        return Dispose(db, cid, s);
    }

    // RockPaperScissor.nextRound 直译
    static RpsResponse NextRound(DbStore db, int cid, Session s)
    {
        if (s != null && s.win)
        {
            s.round++;
            if (s.round < Rounds)
            {
                s.win = false;
                s.ableAnswer = true;
                return new RpsResponse { ok = true, mode = 0x0C };
            }
            s.round = Rounds;
        }
        return Dispose(db, cid, s);
    }

    // RockPaperScissor.dispose 直译：结算 + 0x0D 关窗
    static RpsResponse Dispose(DbStore db, int cid, Session s)
    {
        int? rewarded = RewardIfWin(db, cid, s);
        sessions.Remove(cid);
        return new RpsResponse { ok = true, mode = 0x0D, closed = true, rewardItem = rewarded ?? 0 };
    }

    // RockPaperScissor.reward：只有 win 才发证书（4031332 + round，ETC 格）
    static int? RewardIfWin(DbStore db, int cid, Session s)
    {
        if (s == null || !s.win) return null;
        int itemId = CertificateBase + s.round;
        var rec = CharactersDao.FindById(db, cid);
        if (!NativeInventoryService.CanReceive(db, rec,
                new InventoryChangeRequest { checkItems = new[] { itemId }, checkQuantities = new[] { 1 } }))
            return null;    // 源端 addFromDrop 装不下会掉地上；这里选择不发（同个人商店口径：不静默丢道具）
        NativeInventoryService.AddItems(db, rec, itemId, 1, -1);
        return itemId;
    }
}
