using UnityEngine;

// 五六转进度写入点：282 的写入脚本（q3860x 系列 s/e、q38625s）不在客户端资源里，
// mission4 更是全库无写入点（docs/tms282-skill/19 §4、§6）。2026-11-04 拍板：
// 按 GMS/TMS 攻略口径自实现写入规则；判定本身仍读 Quest.img 原节点，这里只补「谁写状态」。
//
// 依据（19 §6.2 网络侧证 + 任务名映射）：
// - mission1..3 = 三颗女神石依次回收：38603/38605/38607（艾爾達中的女神(1)(2)(3)）完成即写。
//   顺序由任务链保证（38605 要求 38604 完成，而 38604 要求 38603 完成），无需额外校验。
// - mission4    = 38608「前往靈魂艾爾達之內」完成即写（六区域符文 38618..38623 全部回收、
//   38608 可完成的同一时刻）。38609「全新的時代」的 Check/0 要求 quest(38608)=2 且 mission4=1，
//   两者在此同时满足。
// - 38625 完成  = 38624「新的力量」完成时一并标记（GMS/TMS 攻略口径：此时 HEXA 矩阵解锁；
//   38625 自身的完成脚本 q38625s 缺失，Check/1 也无 endscript）。
//
// QR38616 的 missionN 在 QuestLogic.Check 的 infoEx 口径里读作
// Profile.QuestProgress(38616, "info" + (N-1))，所以这里按 info0..info3 写。
public static class AdvancementProgress
{
    static readonly int[] GoddessStoneQuests = { 38603, 38605, 38607 };
    const int FifthAwakeningQuest = 1465;  // [5轉] 紀錄力量，完成覺醒（改码点：四转 ID → +1）
    const int SolErdaEntryQuest = 38608;   // [6轉] 前往靈魂艾爾達之內
    const int NewPowerQuest     = 38624;   // [6轉] 新的力量
    const int HexaMatrixQuest   = 38625;   // [6轉]HEXA矩陣
    const int MissionVarQuest   = 38616;   // infoNumber 载体（mission1..4）

    // QuestLogic.Complete 收尾调用。对还没登记进 QuestCatalog 的任务也安全：
    // 只读写 Profile 的任务状态与进度，不依赖 QuestDef。
    public static void OnQuestCompleted(int questId)
    {
        // 五转觉醒：四转编码 → 独立编码（113/1113…）。282 后续六转任务只认新编码。
        // 交付完直接把 V 矩阵弹出来——原端五转觉醒对话结束就是这一步，
        // 否则玩家在游戏里没有任何入口能打开 V 矩阵（只有 GM 命令）。
        if (questId == FifthAwakeningQuest)
        {
            JobSystem.PromoteToAdvanced();
            NativeVMatrixWindow.Open();
            return;
        }

        for (int i = 0; i < GoddessStoneQuests.Length; i++)
            if (GoddessStoneQuests[i] == questId) { SetMission(i + 1); return; }

        if (questId == SolErdaEntryQuest) SetMission(4);
        else if (questId == NewPowerQuest) { CompleteHexaMatrix(); NativeHexaMatrixWindow.Open(); }
    }

    static void SetMission(int n)
    {
        string key = "info" + (n - 1);
        if (Profile.QuestProgress(MissionVarQuest, key, "") == "1") return;
        Profile.SetQuestProgress(MissionVarQuest, key, "1");
        Debug.Log("[Advancement] QR38616 mission" + n + " = 1（写入点自实现，任务 " + n + " 阶段完成）");
    }

    static void CompleteHexaMatrix()
    {
        if (Profile.IsQuestCompleted(HexaMatrixQuest)) return;
        Profile.CompleteQuest(HexaMatrixQuest);
        Debug.Log("[Advancement] 38624 完成 → 38625 HEXA矩陣 标记完成（写入点③，HEXA 解锁口径）");
    }
}
