using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Field;

namespace Server.Bot.Types.Pq
{
    // 组队任务假人。一个类型干两件事（同一个 id 全程不变，队伍成员身份才稳）：
    //   ① 在招募图当「大厅路人」——闲逛、喊组队任务的话、做表情、放技能、丢东西，
    //      并处理玩家的组队邀请（同意 or 给出拒绝理由）；
    //   ② 进了副本就把行为交给 PqRun（打怪 / 砸反应堆 / 站位 / 交道具），
    //      自己只保留「别站着发呆」的小动作。
    //
    // 闲聊节奏刻意做慢（6~15s 一次）：十几个假人在同一张图，源端也是这个量级，
    // 说得太密会把聊天窗刷满，玩家反而看不见自己的发言。
    public sealed class PqBot : BotStateMachine
    {
        enum Mode { Lobby, Run }

        readonly Random random = new Random();
        Mode mode = Mode.Lobby;
        int recruitMap;
        string pqKey;
        long nextIdleAt, nextEmoteAt, nextSkillAt, nextDropAt, nextChatAt;
        bool wanderStarted;
        long lastMapCheck;

        public PqBot(BotCharacter chr) : base(chr)
        {
            botType = "PQBot";
            var def = PqCatalog.ForMap(chr.mapId);
            if (def != null) { recruitMap = def.recruitMap; pqKey = def.key; }
            // 这几个「到点就做」的计时器默认是 0，而 BotScheduler.Now() 远大于 0：
            // 等于每个假人第一次 tick 就把表情、技能、丢东西、喊话全部触发一遍。
            // 整摊假人又是同一个 BotEnvironment 泵连着铺出来的（首 tick 只错峰
            // 250~1100ms），于是「走到假人堆里」那一两秒会集体爆发：十几个表情帧同时
            // 走 WzCharacter.Compose（单张 3~10ms，且表情/技能动作都不在预合成范围）。
            // 首轮按行为类型错开，之后各走各的随机间隔。
            long now = BotScheduler.Now();
            nextIdleAt = now + random.Next(1500);
            nextChatAt = now + 2000 + random.Next(6000);
            nextEmoteAt = now + 3000 + random.Next(15000);
            nextSkillAt = now + 5000 + random.Next(20000);
            nextDropAt = now + 8000 + random.Next(30000);
        }

        public override bool IsAvailableForAmbientActions() => mode == Mode.Lobby;

        // 大厅要闲逛、副本要按步骤动手，都比默认的 2~6s 快一档。
        protected override long DefaultPeriodMs() => 700;

        // 入口假人是「玩家进图才铺」的，没有 portalenterdrop 出生编舞可等。
        public override bool SkipsSpawnChoreography => true;

        // 基类的 CheckPrioritySpeed 会把周期改成 2~6s（SetPriorityNormal），把上面的 700ms
        // 直接冲掉——表现出来就是假人站着好几秒才动一下。这里改成：玩家在场保持自己的快节奏，
        // 玩家离图才降频；回到图上时立刻把周期拉回 700ms。
        public override void CheckPrioritySpeed()
        {
            if (!CheckMainPlayersOnMap()) { SetPriorityLow(); return; }
            BotScheduler.Reschedule(chr.id, DefaultPeriodMs());
        }

        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || chr == null) return;
            long now = BotScheduler.Now();

            // 跑图期间一切行为交给 PqRun：它知道这一阶段该打怪还是该站桩。
            var run = PqDirector.RunOfBot(chr.id);
            if (run != null)
            {
                mode = Mode.Run;
                if (wanderStarted) { BotWander.Stop(chr); wanderStarted = false; }
                run.DriveBot(chr, now);
                return;
            }

            if (mode == Mode.Run)
            {
                // 刚出本：换回大厅行为，并把落点先按住（否则会从副本坐标一路走过去）。
                mode = Mode.Lobby;
                BotMovementService.Stop(chr);
            }

            if (recruitMap == 0)
            {
                var def = PqCatalog.ForMap(chr.mapId);
                if (def == null) return;
                recruitMap = def.recruitMap; pqKey = def.key;
            }
            if (chr.mapId != recruitMap)
            {
                // 出本后还没被送回招募图（PqRun 已经散了）：自己走回去。
                if (now - lastMapCheck > 2000) { lastMapCheck = now; PqDirector.WarpBot(chr, recruitMap); }
                return;
            }
            Lobby(now);
        }

        // =====================================================================
        // 大厅行为
        // =====================================================================

        void Lobby(long now)
        {
            var player = PqDirector.LocalPlayer();
            bool observed = BotMovementService.Observed(chr.mapId);

            // 玩家不在附近就别折腾了（省 CPU，也免得假人在空图里自言自语）。
            // 降频由 CheckPrioritySpeed 统一处理（base.UpdateState 每 tick 先跑），这里不再重复
            // SetPriorityNormal —— 它会把 700ms 的周期覆盖成 2~6s。
            if (player == null || player.mapId != chr.mapId)
            {
                if (wanderStarted) { BotWander.Stop(chr); wanderStarted = false; }
                return;
            }

            // —— 闲逛 ——
            if (observed && !wanderStarted)
            {
                var map = MapService.Get(chr.mapId);
                if (map != null && map.footholds.Count > 0)
                {
                    // 只在自己那一小片活动：围在招募 NPC 附近，不要满地图乱跑。
                    int lo = Math.Max((int)map.minX + 40, (int)chr.x - 220);
                    int hi = Math.Min((int)map.maxX - 40, (int)chr.x + 220);
                    if (!BotMovementService.PrepareNavigation(chr)) return;
                    if (hi - lo > 80) { BotWander.Start(chr, true, lo, hi); wanderStarted = true; }
                    else { BotWander.Start(chr, false, 0, 0); wanderStarted = true; }
                }
            }

            if (now >= nextChatAt)
            {
                nextChatAt = now + 7000 + random.Next(9000);
                var party = BotTypeManager.Parties?.For(chr.id);
                // 已经进队的假人改说「我负责什么」，还在大厅的喊求组。
                output.Speak(chr, party != null ? PqChatLines.Role(pqKey, random) : PqChatLines.Recruit(pqKey, random));
            }
            if (now >= nextEmoteAt)
            {
                nextEmoteAt = now + 9000 + random.Next(12000);
                output.Emote(chr, Emotes[random.Next(Emotes.Length)]);
            }
            if (now >= nextSkillAt)
            {
                nextSkillAt = now + 12000 + random.Next(16000);
                CastFlourish();
            }
            if (now >= nextDropAt)
            {
                nextDropAt = now + 20000 + random.Next(40000);
                DropSomething();
            }
            if (now >= nextIdleAt)
            {
                nextIdleAt = now + 2500 + random.Next(3000);
                if (!chr.moving) output.IdleStanding(chr);
            }
        }

        // 表情 id 取源端 SocialCommands 的常规表情（1..12），但这里只挑 6 种：
        // 表情帧不在 BotAppearancePrebake 的预合成范围（那里只铺 stand/walk/sit/jump），
        // 每出现一种新表情，都要为「每个站立帧 × 表情帧」现场跑一次 Compose。
        // 12 种全用等于给自己挖几百次合成，6 种既够氛围，首轮合成量也砍掉一半以上。
        static readonly int[] Emotes = { 2, 3, 4, 9, 10, 11 };

        void CastFlourish()
        {
            chr.equips.TryGetValue(-11, out int weapon);
            var skill = BotAttack.GrindSkill(chr.job, weapon);
            // 只放「看起来像在准备进本」的技能：没有可用技能就挥一下武器。
            if (skill.id > 0) output.Send(BotOutput.Kind.Skill, chr, value: skill.id);
            else BotAttack.BasicSwing(chr, random);
        }

        // 大厅里丢东西：通行证 / 优惠券这类副本相关的杂物，纯氛围（谁都能捡）。
        static readonly int[] LobbyDrops =
        {
            PqCatalog.PassCoupon, PqCatalog.PassTicket, PqCatalog.LudiPass,
            2000000, 2000001, 4020000, 4000004,
        };

        void DropSomething()
        {
            int itemId = LobbyDrops[random.Next(LobbyDrops.Length)];
            int x = (int)chr.x + random.Next(-40, 41);
            int y = (int)MapService.SnapToGround(chr.mapId, x, chr.y);
            try { DropCommands.BotThrowItemNoExpire(chr, itemId, x, y); }
            catch (Exception) { /* 物品表缺这条：跳过，不影响其它行为 */ }
        }

        public override void StopScheduledTask()
        {
            if (wanderStarted) { BotWander.Stop(chr); wanderStarted = false; }
            BotMovementService.Stop(chr);
            base.StopScheduledTask();
        }
    }
}
