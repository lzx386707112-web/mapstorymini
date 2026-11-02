using System;
using UnityEngine;

// 组队任务（KPQ / LudiPQ / EllinPQ / PiratePQ）的 NPC 自动代点。
//
// 为什么要有这一层：四个副本的过关校验全部写在 NPC 脚本里（废都 Cloto 数通行证、
// 玩具塔每阶段交 4001022、毒雾森林的石头、海盗船的三轮勋章），而脚本接口只认
// 「本地真实角色」。假人没法代替真人开对话框，所以由宿主按队长的身份把同一份脚本
// 跑一遍：Talk() 进脚本，Tick() 里按脚本当前要的按钮回话（下一步/确定/是/选第 N 项），
// 整个过程 NpcSession.Silent=true，玩家看不到窗口闪。
//
// 重要边界：源端有些 NPC 多按一次就会把队长传出副本（海盗船 2094002，见
// npc/2094002.js:20-23 status==1 就 warp），所以支持 OpenOnly —— 只开对话触发一次
// action，然后按 ESC 关掉，绝不多点。
public enum PqNpcPolicy
{
    /// <summary>一路点到底（下一步/确定/是）。</summary>
    Complete,
    /// <summary>只打开一次（触发脚本里 start 的那次 action），然后取消，不多点。</summary>
    OpenOnly,
}

public static class PqNpcDriver
{
    // 每步之间的间隔：脚本是同步的，留一点时间给 UI/存档/传送，太快会和 cm.warp 抢帧。
    const double StepSeconds = 0.08;
    // 单次对话的上限，防止某个脚本分支把代点卡死。
    const double TimeoutSeconds = 8;

    static NpcSession session;
    static PqNpcPolicy policy;
    static string[] prefer;
    static Action<bool> finished;
    static int npcId, steps, maxSteps;
    static double nextAt, deadline;
    static bool running;

    public static bool Busy => running;
    public static int ActiveNpc => running ? npcId : 0;

    /// <summary>
    /// 开一次静默对话。prefer 是按顺序偏好的选项关键字（命中第一条就选它），
    /// 全部没命中就选第一项；done(true) = 对话按预期推进完毕。
    /// </summary>
    public static void Begin(int npcId, PqNpcPolicy policy, string[] prefer, Action<bool> done, int maxSteps = 24)
    {
        if (!GameSession.InGame) { done?.Invoke(false); return; }
        Cancel();
        PqNpcDriver.npcId = npcId;
        PqNpcDriver.policy = policy;
        PqNpcDriver.prefer = prefer;
        PqNpcDriver.finished = done;
        PqNpcDriver.maxSteps = Math.Max(1, maxSteps);
        steps = 0;
        running = true;
        NpcSession.Silent = true;
        nextAt = Time.realtimeSinceStartupAsDouble;
        deadline = nextAt + TimeoutSeconds;
        session = NpcSession.Talk(npcId);
        if (session == null)
        {
            // 没有脚本（或这是个商店 NPC）：当成一次无效代点。
            Finish(false);
            return;
        }
        if (policy == PqNpcPolicy.OpenOnly)
        {
            // start() 里那次 action 已经执行完了，直接收尾（等价于点开就按 ESC）。
            session.Cancel();
            Finish(true);
        }
    }

    public static void Tick()
    {
        if (!running) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < nextAt) return;
        nextAt = now + StepSeconds;
        if (now > deadline || session == null) { Finish(false); return; }
        if (session.Closed) { Finish(true); return; }
        var dialog = session.Dialog;
        if (dialog == null || !dialog.Visible)
        {
            // 脚本已经 dispose 且没有留下对话框：这一轮就结束了。
            if (session.Disposed) { Finish(true); return; }
            return;
        }
        if (++steps > maxSteps) { Finish(false); return; }
        try
        {
            switch (dialog.Mode)
            {
                case NpcDialogMode.Simple:
                    session.Reply(1, Choose(dialog));
                    break;
                case NpcDialogMode.Style:
                    session.Reply(1, dialog.Styles.Length > 0 ? dialog.StyleIndex : 0);
                    break;
                case NpcDialogMode.GetNumber:
                    dialog.Input = dialog.NumberDef.ToString();
                    session.Reply(1);
                    break;
                case NpcDialogMode.GetText:
                    dialog.Input = "";
                    session.Reply(1);
                    break;
                default:
                    // Next / Prev / NextPrev / Ok / YesNo / AcceptDecline 都是「继续」语义；
                    // 源端这些分支里 mode=1 就是确定/下一步，代点时一律选它。
                    session.Reply(1);
                    break;
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning("[PqNpc] npc=" + npcId + " 代点失败：" + error.Message);
            Finish(false);
        }
    }

    static int Choose(NpcDialog dialog)
    {
        var links = DialogFlow.Links(dialog.Content);
        if (links.Count == 0) return 0;
        foreach (var keyword in prefer ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(keyword)) continue;
            foreach (var selection in dialog.Selections)
            {
                if (selection.HeaderArt != null || selection.Index < 0) continue;
                if (selection.Text != null && selection.Text.Contains(keyword) &&
                    links.Contains(selection.Index)) return selection.Index;
            }
        }
        // 默认选第一个可点项（Dialog.Set 已经把它设成当前选中项）。
        return links.Contains(dialog.SelectedLink) ? dialog.SelectedLink : links[0];
    }

    public static void Cancel()
    {
        if (running) Finish(false);
    }

    static void Finish(bool ok)
    {
        var callback = finished;
        finished = null;
        running = false;
        NpcSession.Silent = false;
        if (session != null && !session.Closed) session.Close();
        session = null;
        try { callback?.Invoke(ok); } catch (Exception error) { Debug.LogWarning("[PqNpc] 回调失败：" + error.Message); }
    }
}
