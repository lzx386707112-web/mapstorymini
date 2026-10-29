// ≡ BotTypes/OPQ/OPQSharedContext.java。编排器（唯一的写入者）与全部 OPQ 假人（只读）之间的黑板。
//
// 线程模型：本工程假人跑在 Unity 主线程的 BotScheduler 上，没有源端那套多线程，
// 所以这里用普通 Dictionary（不再需要 volatile / ConcurrentHashMap）。
// 但写入权限照源端分层保留：假人只能调 MarkTaskComplete（「我这一份活干完了」），
// 其余写入方法都是 internal，只有编排器能改。
using System.Collections.Generic;

namespace Server.Bot.Types.Opq
{
    /// <summary>≡ OPQSharedContext.OPQPhase</summary>
    public enum OpqPhase
    {
        /// <summary>没有进行中的 PQ，假人待命。</summary>
        Inactive,
        /// <summary>在招募大厅喊话拉人。</summary>
        Recruitment,
        /// <summary>已经进队，等队长开副本。</summary>
        InPartyIdle,
        /// <summary>关卡 1（云朵）进行中。</summary>
        Stage1,
        /// <summary>关卡 2（唱片）进行中。</summary>
        Stage2,
        /// <summary>被传到出口大厅，正在回程。</summary>
        Exit,
    }

    public sealed class OpqSharedContext
    {
        // ---- 阶段与通关标志 ----
        public OpqPhase CurrentPhase { get; private set; } = OpqPhase.Inactive;
        public bool IsStage1Complete { get; private set; }
        public bool IsStage2Complete { get; private set; }
        public bool IsPqActive { get; private set; }

        // ---- 分配表（botId -> reactor objectId / platformId）----
        readonly Dictionary<int, int> cloudAssignments = new Dictionary<int, int>();
        readonly Dictionary<int, int> boxAssignments = new Dictionary<int, int>();
        readonly Dictionary<int, string> platformAssignments = new Dictionary<int, string>();

        // ---- 完成标志 ----
        readonly HashSet<int> taskComplete = new HashSet<int>();

        // ============================ 读取（假人可自由调用）============================

        /// <summary>本 bot 分到的云朵反应堆 objectId；没分到为 null。</summary>
        public int? MyCloudAssignment(int botId)
            => cloudAssignments.TryGetValue(botId, out int oid) ? oid : (int?)null;

        /// <summary>本 bot 分到的箱子反应堆 objectId；没分到为 null。</summary>
        public int? MyBoxAssignment(int botId)
            => boxAssignments.TryGetValue(botId, out int oid) ? oid : (int?)null;

        public string MyPlatformAssignment(int botId)
            => platformAssignments.TryGetValue(botId, out var id) ? id : null;

        public bool IsMyTaskComplete(int botId) => taskComplete.Contains(botId);

        public ICollection<int> CloudAssignmentValues => cloudAssignments.Values;
        public ICollection<int> BoxAssignmentValues => boxAssignments.Values;

        // ============================ 写入（编排器）============================

        /// <summary>源端唯一允许假人写的一项：我这份活干完了。</summary>
        public void MarkTaskComplete(int botId) => taskComplete.Add(botId);

        internal void SetCurrentPhase(OpqPhase phase) => CurrentPhase = phase;
        internal void SetStage1Complete(bool value) => IsStage1Complete = value;
        internal void SetStage2Complete(bool value) => IsStage2Complete = value;
        internal void SetPqActive(bool value) => IsPqActive = value;

        internal void PutCloudAssignment(int botId, int? objectId)
        {
            if (objectId.HasValue) cloudAssignments[botId] = objectId.Value;
            else cloudAssignments.Remove(botId);
        }

        internal void PutBoxAssignment(int botId, int? objectId)
        {
            if (objectId.HasValue) boxAssignments[botId] = objectId.Value;
            else boxAssignments.Remove(botId);
        }

        internal void PutPlatformAssignment(int botId, string platformId)
        {
            if (string.IsNullOrEmpty(platformId)) platformAssignments.Remove(botId);
            else platformAssignments[botId] = platformId;
        }

        /// <summary>假人收尾时清掉自己那份（platformAssignments 在 OPQ 里只是兼容保留）。</summary>
        public void ClearPlatformAssignment(int botId) => platformAssignments.Remove(botId);

        internal void ClearTaskComplete(int botId) => taskComplete.Remove(botId);

        /// <summary>清光一次跑图的状态（注册中的假人不受影响）。</summary>
        internal void Reset()
        {
            CurrentPhase = OpqPhase.Inactive;
            IsStage1Complete = false;
            IsStage2Complete = false;
            IsPqActive = false;
            cloudAssignments.Clear();
            boxAssignments.Clear();
            platformAssignments.Clear();
            taskComplete.Clear();
        }
    }
}
