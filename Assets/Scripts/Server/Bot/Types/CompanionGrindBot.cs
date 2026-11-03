using System;
using Server.Bot.Environment;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Types
{
    // 野外挂机的伴侣假人。
    // 行为顺序：切图跟随 → Boss/普通怪分工 → 顺手拾取 → 战斗 → 无怪巡逻和低频聊天。
    // 以前只有「有怪打怪、离队长远了回去」两条，怪清完就站在 station 上不动，
    // 打出来的东西也一件不捡，观感就是木桩。
    public sealed class CompanionGrindBot : BotStateMachine
    {
        readonly BotPartyGrindService service;
        readonly Random rng = new Random();
        int leaderId, lastMoveX=int.MinValue, lastMoveY=int.MinValue;
        long nextSwing, nextMoveAt, nextChatAt;
        int trackedTarget, bestTargetDistance=int.MaxValue;
        long targetProgressAt, blockedLootUntil;
        int blockedLootX, blockedLootY;
        bool blocksLoot;
        static readonly string[] IdleLines = { "这边清完了，我去旁边看看。", "还没刷怪，先转一圈。", "我看看上面还有没有怪。" };
        static readonly string[] BossLines = { "发现Boss，先集火！", "先打Boss，小怪等会儿清。" };
        // 拾取：400ms 扫一次掉落表，扫到就记下目标点，6 秒追不到就放弃。
        long nextLootAt, lootUntil;
        int lootX, lootY; bool hasLoot;
        // 巡逻：无怪时沿本层左右走，隔几秒换一个点。
        long nextPatrolAt, lastCombatAt;
        int patrolX=int.MinValue, patrolY=int.MinValue;
        public CompanionGrindBot(BotCharacter bot,BotPartyGrindService service) : base(bot)
        { this.service=service ?? throw new ArgumentNullException(nameof(service)); leaderId=service.LeaderId(bot.id); botType="CompanionGrindBot"; IsCompanion=()=>true; nextChatAt=BotScheduler.Now()+20000+rng.Next(20000); }
        protected override long DefaultPeriodMs() => 220;
        public override void CheckPrioritySpeed() => UpdateScheduleDelay(220);
        public override void StartScheduledTask(long initialDelayMs) => base.StartScheduledTask(Math.Min(initialDelayMs,220));
        public override bool IsAvailableForAmbientActions() => false;
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || state==State.TRADING) return;
            var leader=service.Leader(chr.id);
            if (leader==null) { service.Stop(leaderId); StopScheduledTask(); return; }
            leaderId=leader.id;
            if (!service.SameParty(chr,leader)) { service.Stop(leader.id,"组队已解散，基友集合结束。"); return; }
            if (chr.mapId!=leader.mapId || chr.channel!=leader.channel || chr.world!=leader.world)
            {
                var destination=service.Station(chr,leader);
                BotMovementService.Disable(chr);
                chr.world=leader.world; chr.channel=leader.channel;
                BotRegistry.EnterMap(chr,leader.mapId); chr.x=destination.x; chr.y=destination.y;
                ResetMapActions();
                service.ReleaseTarget(chr);
                return;
            }
            // 观察图先等导航图准备好，避免每220ms轮询触发同步构图。
            if (!BotMovementService.PrepareNavigation(chr)) return;
            BotMovementService.SetGrinding(chr,true);
            long now=BotScheduler.Now();
            if (lastCombatAt==0) lastCombatAt=now;
            var station=service.Station(chr,leader);
            var target=service.Target(chr,leader);
            if (target!=null)
            {
                int distance=(int)(Math.Abs(chr.x-target.x)+Math.Abs(chr.y-target.y)*2);
                if (trackedTarget!=target.id)
                { trackedTarget=target.id; bestTargetDistance=distance; targetProgressAt=now; }
                else if (distance<bestTargetDistance-16)
                { bestTargetDistance=distance; targetProgressAt=now; }
                chr.equips.TryGetValue(-11,out int heldWeapon);
                var engage=BotAttack.EngageRange(chr.job,heldWeapon);
                bool inRange=Math.Abs(chr.x-target.x)<=engage.x && Math.Abs(chr.y-target.y)<=engage.y;
                if (inRange) targetProgressAt=now;
                else if (now-targetProgressAt>10000)
                {
                    service.AvoidTarget(chr,target.id,now);
                    BotMovementService.Stop(chr); lastMoveX=int.MinValue;
                    trackedTarget=0; target=null;
                }
            }
            else trackedTarget=0;
            if (target!=null)
            {
                lastCombatAt=now;
                if (target.boss && now>=nextChatAt && service.TryChat(chr,BossLines[rng.Next(BossLines.Length)],now))
                    nextChatAt=now+25000+rng.Next(20000);
            }

            // ① 顺手把脚下的东西捡了。两套掉落表 + 背包结算都不便宜，400ms 扫一次就够。
            if (hasLoot && now>lootUntil)
            {
                blocksLoot=true; blockedLootX=lootX; blockedLootY=lootY; blockedLootUntil=now+10000;
                hasLoot=false;
            }
            if (now>=nextLootAt)
            {
                nextLootAt=now+400;
                // 有怪时只顺手捡近的（120px），否则一场刷怪会变成满地图追掉落。
                var spot=service.Loot(chr,target==null?360f:120f);
                // 脚边就有：直接收（收起后重新找下一件，地上常常一次掉好几样）。
                if (spot.found && spot.distance<=56f && service.TakeLootNearby(chr,60f,2)>0)
                { hasLoot=false; lootUntil=0; spot=service.Loot(chr,target==null?360f:120f); }
                if (spot.found && blocksLoot && now<blockedLootUntil &&
                    Math.Abs(spot.x-blockedLootX)<=16 && Math.Abs(spot.y-blockedLootY)<=24) spot.found=false;
                if (spot.found && spot.distance<=380f)
                {
                    // 同一个点只追 6 秒：掉在走不到的地方（别的平台、墙后面）时，
                    // 不能一直贴着墙蹭到掉落过期，怪都不打了。
                    bool same=hasLoot && Math.Abs(spot.x-lootX)<=16 && Math.Abs(spot.y-lootY)<=24;
                    if (!same) lootUntil=now+6000;
                    hasLoot=true; lootX=spot.x; lootY=spot.y;
                }
                else hasLoot=false;
            }

            // ② 掉落优先：没怪就去捡；有怪但东西就掉在脚边，先弯腰再打。
            if (hasLoot && now<=lootUntil && (target==null || (!target.boss && NearX(lootX,90) && NearY(lootY,120))))
            {
                if (!NearX(lootX,34) || !NearY(lootY,90)) { Approach(new Point(lootX,lootY)); return; }
                // 已经站上去了：能收就收；收不了（归属没到期 / 背包满）这一拍就放弃，别在原地蹭。
                if (service.TakeLootNearby(chr,56f,2)==0)
                { blocksLoot=true; blockedLootX=lootX; blockedLootY=lootY; blockedLootUntil=now+10000; }
                hasLoot=false; lootUntil=0;
            }

            if (target!=null)
            {
                // 真实攻击口径：按职业/武器的攻击类别停步（近战贴身、远程/魔法在射程内），
                // 不再沿用源端 120px 一刀切的 ENGAGE 圈。
                chr.equips.TryGetValue(-11,out int weapon);
                var range=BotAttack.EngageRange(chr.job,weapon);
                if (Math.Abs(chr.x-target.x)<=range.x && Math.Abs(chr.y-target.y)<=range.y)
                {
                    BotMovementService.Stop(chr); lastMoveX=int.MinValue;
                    if (now>=nextSwing) { service.Strike(chr,target); nextSwing=now+800; }
                }
                else Approach(new Point(target.x,target.y));
                return;
            }

            // ③ 没怪：全队共享聊天限频，不打断移动或同时刷屏。
            if (now>=nextChatAt && now-lastCombatAt>=5000 && service.TryChat(chr,IdleLines[rng.Next(IdleLines.Length)],now))
                nextChatAt=now+25000+rng.Next(20000);
            // 到达重绑后的集合点再巡逻，避免旧巡逻目标把人拉回去。
            // 本层刷空了（守着小平台）：跟到队长那层去，并且把 floor 重绑那一层，
            // 否则下一次巡逻又把它拉回旧平台。
            if (now-lastCombatAt>15000)
            {
                var home=service.LeaderSpot(chr,leader);
                if (home.HasValue && (!NearX(home.Value.x,56) || !NearY(home.Value.y,110)))
                { Approach(home.Value); patrolX=int.MinValue; nextPatrolAt=now+1500; return; }
                lastCombatAt=now;
            }
            if (!service.OnFloor(chr) || Math.Abs(chr.x-leader.x)>520)
            { Approach(station); patrolX=int.MinValue; nextPatrolAt=now+1500; return; }
            if (now>=nextPatrolAt)
            {
                var spot=service.PatrolPoint(chr,leader);
                if (spot.HasValue)
                {
                    patrolX=spot.Value.x; patrolY=spot.Value.y;
                    Approach(new Point(patrolX,patrolY));
                    nextPatrolAt=now+2400+rng.Next(2600);
                }
                else nextPatrolAt=now+2000;
                return;
            }
            if (patrolX!=int.MinValue && (!NearX(patrolX,40) || !NearY(patrolY,90)))
                Approach(new Point(patrolX,patrolY));
        }
        void ResetMapActions()
        {
            lastMoveX=lastMoveY=int.MinValue; patrolX=patrolY=int.MinValue;
            hasLoot=false; blocksLoot=false; lootUntil=nextLootAt=nextPatrolAt=0;
            trackedTarget=0; bestTargetDistance=int.MaxValue; targetProgressAt=lastCombatAt=0;
            nextMoveAt=nextSwing=0;
        }
        bool NearX(int x,int tolerance) => Math.Abs(chr.x-x)<=tolerance;
        bool NearY(int y,int tolerance) => Math.Abs(chr.y-y)<=tolerance;
        void Approach(Point destination)
        {
            if (Math.Abs((long)destination.x-lastMoveX)<16 && Math.Abs((long)destination.y-lastMoveY)<16 && BotMovementService.IsMoving(chr)) return;
            long now=BotScheduler.Now();
            if (BotMovementService.IsMoving(chr) && now<nextMoveAt) return;
            nextMoveAt=now+700;
            lastMoveX=destination.x; lastMoveY=destination.y; BotMovementService.Move(chr,destination.x,destination.y);
        }
    }
}
