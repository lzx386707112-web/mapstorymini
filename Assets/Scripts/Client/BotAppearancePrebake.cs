using Server.Bot;
using UnityEngine;

// 假人纸娃娃预合成：Android 在加载页等待全部完成，其他平台借登录/选角空闲帧推进。
//
// 背景：WzCharacter.Compose 是 CPU 逐像素混合 + Texture2D 上传，单次 3~10ms，
// 且 Texture2D 只能在主线程创建。等玩家进图再合成，就是「进游戏卡一段」的直接来源。
// 外观收敛到模板池（BotAppearanceTemplates）之后帧集合固定，就能赶在进图前摊掉；
// 进图时这些帧已在缓存里，假人出现只需查表。
public sealed class BotAppearancePrebake : MonoBehaviour
{
    // 进图第一眼看得到的就是站着/走路，先铺这两个；sit/jump 少见，排在后面。
    // 外层动作、内层模板（见 Step）：必须先让所有模板的 stand 都铺完再轮到 walk。
    static readonly string[] AnimOrder = { "stand", "walk", "sit", "jump" };
    // 登录/选角界面每帧最多花多少毫秒。
    // 这里比游戏内宽：登录界面没有战斗、没有输入采样压力，把预算给足才能赶在进图前铺完。
    // 3ms 时 32 套模板的 stand 要铺十几秒，玩家早就进图了，等于白做。
    const double FrameBudgetMs = 6.0;
    // 进图后只在「本帧确实很空」时补一步，绝不和渲染抢时间。
    const float InGameBudgetMs = 8f;

    int templateIndex, animIndex, frameIndex, frameCount;
    bool started, finished;
    readonly System.Diagnostics.Stopwatch budget = new System.Diagnostics.Stopwatch();

    // 最近一次预算调整时间，用于「登录界面跑满、游戏内让路」。
    public static bool Finished { get; private set; }
    // 所有模板的 stand 帧是否已铺完。
    // 这是「进图第一眼」用到的唯一动作，铺完它就意味着进图那一刻不会再有任何 CPU 合成；
    // 假人生成本身很快（纯对象构造），所以让它等这个信号，顺序就变成
    // 「纹理先就绪 → 再建假人 → 进图只摆视图」，跟联机 loading 的次序一致。
    public static bool StandReady { get; private set; }

    void Awake() { Finished = false; StandReady = false; }   // 重登会重新挂载，静态标记要跟着这一轮走

    void Update()
    {
        if (finished) return;
        if (!started)
        {
            var server = GameServer.Local;
            if (server == null) return;                 // GameSession.Boot 还没跑
            // 资源没解析完就别碰：这里会走 WzAssets 节点树，等后台线程解完更省事。
            if (!BotWarmup.Ready("Bot/Items") || !BotWarmup.Ready("Bot/SoloMapling")) return;
            BotAppearanceTemplates.Ensure(server.Db);
            started = true;
            if (!BotAppearanceTemplates.Enabled) { Finish(); return; }
            Debug.Log("[Bot] 纸娃娃预合成开始：模板 " + BotAppearanceTemplates.All.Count + " 套");
        }

        budget.Restart();
        // 进图后不再抢帧：只在「本帧已经很空」的时候补剩下的，绝不和渲染抢预算。
        // 玩家可能登录没停几秒就进图，那就边玩边补，没补到的按需合成（模板共享，只合成一次）。
        bool inGame = GameSession.InGame;
        if (inGame && !Server.Bot.BotWorkPump.Has(InGameBudgetMs)) return;
        do
        {
            if (!Step()) { Finish(); return; }
        } while (!inGame && budget.Elapsed.TotalMilliseconds < FrameBudgetMs);
    }

    // 推进一帧合成；返回 false 表示全部铺完。
    //
    // 循环顺序是「外层动作、内层模板」——这点很关键：必须先让所有模板的 stand 都铺完，
    // 再轮到 walk。若按模板外层循环，铺到一半时只有前几个模板可用，
    // 玩家进图碰到没铺到的模板照样现场合成，等于白做。
    bool Step()
    {
        var all = BotAppearanceTemplates.All;
        if (animIndex >= AnimOrder.Length) return false;
        if (templateIndex >= all.Count)
        {
            animIndex++; templateIndex = 0; frameIndex = 0; frameCount = 0;
            // 刚把 stand 的所有模板铺完：这是进图第一眼唯一会显示的动作，
            // 到这里就可以放行假人生成了（见 BotPreload）。
            if (animIndex == 1) StandReady = true;
            return animIndex < AnimOrder.Length;
        }
        var template = all[templateIndex];
        if (frameCount == 0)
        {
            // 第一次碰这个模板：先把 recipe 建起来（Appearance 内部会合成 stand 第 0 帧）。
            WzCharacter.Appearance(template.key, template.ids);
            frameCount = WzCharacter.PrecomposeFrameCount(template.key, AnimOrder[animIndex]);
            if (frameCount == 0) { templateIndex++; frameIndex = 0; return true; }   // 该角色没有这个动作
        }
        if (frameIndex < frameCount)
        {
            WzCharacter.PrecomposeFrame(template.key, AnimOrder[animIndex], frameIndex);
            frameIndex++;
            return true;
        }
        templateIndex++; frameIndex = 0; frameCount = 0;
        return true;
    }

    void Finish()
    {
        finished = true;
        Finished = true;
        StandReady = true;      // 全铺完了，stand 自然也在内
        if (BotAppearanceTemplates.Enabled)
            Debug.Log("[Bot] 纸娃娃预合成结束：模板 " + BotAppearanceTemplates.All.Count + " 套");
        Destroy(this);
    }
}
