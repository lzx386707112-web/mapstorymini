// ≡ BotTypes/OPQ/OPQConstants.java。地图 / 物品 / 坐标 / 调参常量表。
// 坐标沿用源端（screen 坐标，y 向下为正，所以「顶部平台」是很大的负 y）。
namespace Server.Bot.Types.Opq
{
    public static class OpqConstants
    {
        // ---- 地图 ---------------------------------------------------------------
        /// <summary>招募大厅（Orbis 售票处一带）。</summary>
        public const int OpqLobby = 200080101;
        /// <summary>关卡 1：云朵收集。</summary>
        public const int Stage1 = 920010000;
        /// <summary>中转塔（关卡 1 与关卡 2 之间）。</summary>
        public const int Tower = 920010100;
        /// <summary>关卡 2：唱片收集。</summary>
        public const int Stage2 = 920010400;
        /// <summary>出口大厅。</summary>
        public const int ExitLobby = 920011200;

        // ---- NPC ---------------------------------------------------------------
        /// <summary>关卡 1 交云朵的 NPC（Chamberlain Eak）；交完后由服务端脚本动态生成。</summary>
        public const int ChamberlainEak = 2013001;

        // ---- 物品 ---------------------------------------------------------------
        /// <summary>云朵碎片。</summary>
        public const int CloudPiece = 4001063;
        /// <summary>关卡 2 的 7 张唱片（按箱子从右到左的位置一一对应）。</summary>
        public static readonly int[] Stage2Items = { 4001056, 4001057, 4001058, 4001059, 4001060, 4001061, 4001062 };
        public static readonly int[] Stage1Items = { CloudPiece };

        // ---- 反应堆模板 id ------------------------------------------------------
        /// <summary>关卡 1 的祭坛反应堆（eak），不可打、不参与分配。</summary>
        public const int Stage1AltarReactorId = 2006000;
        /// <summary>关卡 1 的 20 个可破坏云朵反应堆（cloud1..cloud20）。</summary>
        public const int Stage1CloudReactorId = 2002001;
        /// <summary>关卡 2 的 7 个可破坏箱子反应堆（一环一个）。</summary>
        public const int Stage2BoxReactorFirst = 2002004;
        public const int Stage2BoxReactorLast = 2002010;
        /// <summary>关卡 2 中央的音乐盒反应堆（投放点）。</summary>
        public const int Stage2MusicBoxReactorId = 2008006;
        /// <summary>关卡 2 箱子序号（从右到左），只用于喊话。</summary>
        public static readonly string[] Stage2BoxOrdinals = { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th" };

        // ---- 关键坐标 -----------------------------------------------------------
        public const int Stage1EntryX = 266, Stage1EntryY = 143;
        /// <summary>关卡 1 交完云朵后 NPC 把队长传上去的顶部平台。</summary>
        public const int Stage1CompleteX = 165, Stage1CompleteY = -1270;
        /// <summary>关卡 1 的投放点（NPC 附近地面）。</summary>
        public const int Stage1DropX = 497, Stage1DropY = 143;
        /// <summary>关卡 1 里通往中转塔的传送门 id。</summary>
        public const int Stage1ExitPortalId = 4;

        public const int TowerSpawnX = -260, TowerSpawnY = -32;
        /// <summary>中转塔里走向下一段路的落脚点。</summary>
        public const int TowerWalkX = 159, TowerWalkY = -32;

        public const int Stage2SpawnX = -113, Stage2SpawnY = -321;
        /// <summary>关卡 2 的投放点（音乐盒前）。</summary>
        public const int Stage2DropX = -1588, Stage2DropY = -127;

        public const int ExitLobbyX = -161, ExitLobbyY = 323;
        public const int LobbyX = -233, LobbyY = 174;

        // ---- 调参 ---------------------------------------------------------------
        /// <summary>每段等待的上限，超时回 LOOP_CHECK（源端注释：卡住的假人不能永远挂着）。</summary>
        public const long StageWaitTimeoutMs = 120_000;
        /// <summary>大厅喊话间隔。</summary>
        public const long RecruitMessageIntervalMs = 12_000;
        /// <summary>走到目标点后的稳定等待。</summary>
        public const long NavigateSettleMs = 1_500;
        /// <summary>没有分配时多久重试一次。</summary>
        public const long AssignmentPollMs = 500;
        /// <summary>OPQ 云堆/箱子都是 4 下砸破。</summary>
        public const int MaxReactorHits = 4;
        /// <summary>两次挥击间隔（≈1H 攻速）。</summary>
        public const long SwingIntervalMs = 700;
        /// <summary>与反应堆 x 距离在此之内才能命中。</summary>
        public const int ReactorHitRangePx = 200;

        // ---- 关卡 1 拾取 --------------------------------------------------------
        /// <summary>拾取扫描范围（距离平方，与 MapItemService.ItemsInRange 同语义）。</summary>
        public const double LootScanRangeSq = 8000;
        /// <summary>云朵可能穿过好几层平台，按反应堆 x 逐层向下探。</summary>
        public const int LootFallbackSteps = 20;
        public const int LootFallbackStepPx = 2500;
    }
}
