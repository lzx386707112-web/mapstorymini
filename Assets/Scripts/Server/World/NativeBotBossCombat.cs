using UnityEngine;

// 源端 soloMapling/ArtificialPlayer/BotBossCombatManager.java 的单机等价实现。
// 触发链（源端 L53-89）：自由市场入口图（910000000）聊天命中触发词 → 找本图离玩家最近的
// 活 Boss → 拉假人集火，每 1200ms 一跳（scheduleWithFixedDelay 800ms 起步 + 1200ms 周期）。
// 伤害公式 calculateDamage（源端 L299）：clamp(max(50000, bossMaxHp/180), 50000, 2_000_000)；
// 伤害归属触发真人（findDamageOwner，掉落/经验全归玩家）；Boss 死亡或失效即停，无独立超时。
// 表现层差异：源端让假人走位站桩+广播 damageMonster 包；本端复用假人伤害入口
// EnemyUnit.TakeBotHit（伤害数字/死亡链/伤害贡献记账全套现成），站位由假人自身系统处理。
public static class NativeBotBossCombat
{
    public const int FmEntrance = 910000000;
    const long InitialDelayMs = 800;      // 源端 initialDelay
    const long AttackIntervalMs = 1200;   // 源端 ATTACK_INTERVAL_MS
    static readonly string[] TriggerPhrases = { "假人打boss", "假人攻击", "攻击boss", "打boss", "botattack" };
    static readonly string[] StartLines = { "来了，集火Boss！", "开打开打，注意躲技能！", "老板出现了，兄弟们上！" };
    static EnemyUnit boss;                // 进行中的集火目标
    static long nextTick;

    // —— 触发（源端 handleChatTrigger：未命中触发词/前置返回 false，聊天照常继续） ——
    // player.getMapId() != FM_ENTRANCE 或 isBot 时源端直接返回 false；isBot 过滤在
    // GeneralChatHandler 里对假人消息根本不会进来，这里只校验地图。
    public static bool HandleChatTrigger(int mapId, string message)
    {
        if (string.IsNullOrEmpty(message) || mapId != FmEntrance) return false;
        string phrase = message.Trim().Replace(" ", "").ToLowerInvariant();
        bool hit = false;
        foreach (string trigger in TriggerPhrases) if (phrase == trigger) hit = true;
        if (!hit) return false;

        var field = GameLoop.I != null ? GameLoop.I.field : null;
        if (field == null || field.Map == null || field.Map.id != mapId) return false;
        // 源端 findBoss：getAllMonsters 里 isBoss && isAlive，取离玩家最近的一只。
        var player = GameLoop.I.player;
        float px = player != null ? player.transform.position.x * Art.PPU : 0f;
        var target = field.NearestAliveBoss(px);
        if (target == null)
        {
            NativeSystemMessage.Drop(12, "当前市场没有可攻击的Boss。");
            return true;
        }
        if (Server.Bot.BotRegistry.Count() == 0)
        {
            NativeSystemMessage.Drop(12, "当前市场没有可用假人。");
            return true;
        }
        // 源端同图已有攻击任务先 cancel 再开新的（ACTIVE_BY_MAP 换班）。
        boss = target;
        nextTick = NativeMobController.Now + InitialDelayMs;
        NativeSystemMessage.Drop(12, StartLines[Random.Range(0, StartLines.Length)]);
        NativeSystemMessage.Drop(12, "假人已开始集火Boss。");
        return true;
    }

    // 每帧 tick（GameLoop.Update 驱动）。源端 scheduleWithFixedDelay 的等价节拍。
    public static void Tick(long now)
    {
        if (boss == null) return;
        // shouldStop（源端 L264）：boss 失效/死亡/不在场即结束。
        if (boss == null || boss.dead || !boss.isActiveAndEnabled ||
            GameLoop.I == null || GameLoop.I.field == null) { boss = null; return; }
        if (now < nextTick) return;
        nextTick = now + AttackIntervalMs;
        // calculateDamage（源端 L299）：clamp(max(50000, bossMaxHp/180), 50000, 2_000_000)
        long maxHp = boss.NativeController != null ? boss.NativeController.MaximumHp : boss.hpMax;
        long damage = System.Math.Clamp(System.Math.Max(50000, maxHp / 180), 50000, 2000000L);
        // findDamageOwner：伤害记在触发真人头上；单机触发者就是本地角色，
        // TakeBotHit 让伤害数字/死亡链/伤害贡献按 actorId=真人记账（经验/掉落归玩家）。
        var player = GameLoop.I.player;
        int fromDir = player != null && boss.transform.position.x < player.transform.position.x ? -1 : 1;
        boss.TakeBotHit((int)GameSession.CharacterId, damage, fromDir);
    }

    // 换图/登出时由 GameLoop 清掉进行中的集火（源端按地图 key 存，地图失效自然停）。
    public static void Stop() => boss = null;
}
