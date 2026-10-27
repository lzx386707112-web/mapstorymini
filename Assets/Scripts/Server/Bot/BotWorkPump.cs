using System.Diagnostics;

namespace Server.Bot
{
    // 假人系统的分帧预算泵。
    //
    // 问题：进游戏那几帧里同时挤着三件重活——
    //   1. 地图分片 JSON（每个 0.3~1.3MB），城镇游荡要连读七个分片；
    //   2. 连续生成几十个假人（随机外观 + 装备池 + 注册状态机）；
    //   3. 纸娃娃合成 + 导航图构建。
    // 一帧里做不完就表现为「进游戏后画面卡一段时间」。
    //
    // 做法：把这些活拆成一件一件的小事（BotEnvironment.Post / BotField 的出队循环 /
    // BotScheduler 的分发轮），每帧只干到 BudgetMs 为止，剩下的交给下一帧。
    // 计时用 Stopwatch——Server/ 是纯 C#，不能引用 UnityEngine.Time。
    public static class BotWorkPump
    {
        // 每帧留给假人系统的毫秒预算。60fps 一帧 16.7ms，4ms 之外仍有 12ms 给渲染和其它系统；
        // 单件小事超预算时也会做完（不可再拆），所以实际峰值会比这个数字高一点。
        public const float BudgetMs = 4f;

        static readonly Stopwatch watch = Stopwatch.StartNew();
        static double frameBeginMs = -1;

        // 前端每帧开头调用一次（FieldDirector.Update 第一句）。
        public static void BeginFrame() => frameBeginMs = watch.Elapsed.TotalMilliseconds;

        // 本帧到此刻为止花掉的时间（毫秒）。
        // 没调过 BeginFrame（离线自检 / 纯后端调用）时返回 0：这些场景没有渲染帧可言，
        // 预算只是节流手段，绝不能变成卡死的开关。
        public static double FrameSpentMs
        {
            get
            {
                if (frameBeginMs < 0) return 0;
                return watch.Elapsed.TotalMilliseconds - frameBeginMs;
            }
        }

        // 预算还有余量吗（reserveMs = 下一步预计要花的毫秒数）。
        public static bool Has(float reserveMs = 0f) => FrameSpentMs + reserveMs < BudgetMs;
    }
}
