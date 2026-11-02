using Server.Bot;
using UnityEngine;

// 假人（队员）跑一次 NPC 脚本对话的同步驱动器。
//
// 与 PqNpcDriver 的分工：那个是「替真人队长点 NPC」，要按帧推、还要防窗口闪烁；
// 这里是「假人自己去点」，不需要任何 UI，一次调用把整段对话跑完，返回脚本最后一页原文
// （废都第一关队员分支就是靠它把「你的题目是……请搜集 N 张」念出来的）。
public static class PqBotNpcDriver
{
    // 单次对话最多回话次数：源端一个阶段 NPC 最多三四页，这里留足余量防死循环。
    const int MaxSteps = 24;

    /// <summary>假人找 npcId 说话。true = 脚本正常跑完（dispose），say 为最后一页原文。</summary>
    public static bool Talk(BotCharacter bot, int npcId, out string say)
    {
        say = null;
        if (bot == null || npcId == 0 || !GameSession.InGame) return false;
        bool silent = NpcSession.Silent;
        NpcSession.Silent = true;
        NpcSession session = null;
        try
        {
            session = NpcSession.TalkAs(npcId, bot);
            if (session == null) return false;
            int steps = 0;
            while (!session.Disposed && !session.Closed && steps++ < MaxSteps)
                if (!session.Advance()) break;
            say = session.LastSay;
            if (!string.IsNullOrEmpty(session.LastError))
            {
                Debug.LogWarning("[BotNpc] " + bot.name + " 与 NPC " + npcId + " 对话出错：" + session.LastError);
                return false;
            }
            return true;
        }
        catch (System.Exception error)
        {
            Debug.LogWarning("[BotNpc] " + bot.name + " 与 NPC " + npcId + " 对话异常：" + error.Message);
            return false;
        }
        finally
        {
            try { session?.Close(); } catch (System.Exception) { /* 关闭失败不影响流程 */ }
            NpcSession.Silent = silent;
        }
    }
}
