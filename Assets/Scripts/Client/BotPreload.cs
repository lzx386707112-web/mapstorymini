using UnityEngine;
using Server.Bot;

// 假人运行时预建 + 登录期预热驱动。
//
// 为什么要有这个（对齐联机架构）：
// 联机客户端进图「不卡」，不是因为加载快，而是因为加载发生在**进图之前** ——
// 点开始游戏时 service 已经把角色数据、地图、周边玩家全部就绪，进图那一刻只是摆上去。
// 而这里的假人系统原来完全挂在 FieldDirector.TickBots 上，那个方法头一句就要求
// GameSession.InGame，等于「登录/选角界面期间假人系统一动不动，进图第一帧才开始建
// 市场 10 + 环境 30 + 城镇 14 个假人」——分帧只是把这段忙摊开，位置仍然在玩家
// 已经能操作之后，所以感觉「进图后要等」。
//
// 现在照联机来：在登录/选角界面就把 BotTasks / BotScheduler 驱动起来，三波环境假人
// 在这段时间建完；进图时 BotRegistry 已经就绪，FieldDirector 只负责摆视图。
public sealed class BotPreload : MonoBehaviour
{
    // 兜底：登录界面最多驱动这么久就退场。
    // 正常情况下 IsMarketStartupComplete 早就置位了；这条只防「配置关掉了自动环境启动」
    // 或某一步异常打圈时，组件永远挂在那里每帧空转。
    const float MaxWarmupSeconds = 45f;

    bool built, finished;
    float warmupSeconds;

    void Update()
    {
        if (finished) return;
        var server = GameServer.Local;
        if (server == null) return;      // GameSession.Boot 还没跑完
        // 三个大 JSON 没解析完就别碰：BotWarmup.Root() 会 Join 后台线程，
        // 等于把整段解析搬回主线程（这正是要避免的卡顿）。
        if (!BotWarmup.Ready("Bot/Items") || !BotWarmup.Ready("Bot/SoloMapling") ||
            !BotWarmup.Ready("Bot/Maps")) return;

        if (!built)
        {
            built = true;
            try
            {
                var bots = server.Bots;      // 首次访问即构造；构造完保持不动，进图直接可用
                Debug.Log("[Bot] 运行时已预建：假人 " + BotRegistry.Count() + " 个");
            }
            catch (System.Exception e)
            {
                // 资源包缺失等异常不该在登录界面刷屏；进游戏后 TickBots 仍会按原逻辑重试/报错。
                Debug.LogWarning("[Bot] 运行时预建失败：" + e.Message);
                finished = true; Destroy(this); return;
            }
        }

        // 已经进图：驱动权交给 FieldDirector.TickBots（它每帧推同一个 Drive），这里退场。
        if (GameSession.InGame) { finished = true; Destroy(this); return; }

        // 等纸娃娃把 stand 帧铺完再建假人：顺序必须是「纹理先就绪 → 再建假人 → 进图摆视图」。
        // 反过来的话，假人先建好、纹理没就绪，进图那一刻 BotField.Create 会现场 CPU 合成，
        // 一帧连做十几次，正是要消灭的那种卡顿。
        // 模板关闭时不预合成，这里自然放行（回退到每次现合成的老路径）。
        if (BotAppearanceTemplates.Enabled && !BotAppearancePrebake.StandReady) return;

        // 登录/选角界面：把假人系统整个跑起来。用 realtime 时钟——与 FieldDirector
        // 注入的时钟同源，交接时不会跳变（两边都是 Time.realtimeSinceStartup*1000）。
        BotWorkPump.BeginFrame();
        server.Bots.Drive((long)(Time.realtimeSinceStartup * 1000f));
        warmupSeconds += Time.unscaledDeltaTime;
        if (Server.Bot.Environment.BotEnvironment.IsMarketStartupComplete || warmupSeconds >= MaxWarmupSeconds)
        {
            finished = true;
            Debug.Log("[Bot] 假人预热完成：在场 " + BotRegistry.Count() + " 个，进图不再生成");
            Destroy(this);
        }
    }
}
