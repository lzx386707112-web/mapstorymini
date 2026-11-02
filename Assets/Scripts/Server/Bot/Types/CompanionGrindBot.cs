using System;
using Server.Bot.Environment;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Types
{
    public sealed class CompanionGrindBot : BotStateMachine
    {
        readonly BotPartyGrindService service;
        int leaderId, lastMoveX=int.MinValue, lastMoveY=int.MinValue;
        long nextSwing;
        public CompanionGrindBot(BotCharacter bot,BotPartyGrindService service) : base(bot)
        { this.service=service ?? throw new ArgumentNullException(nameof(service)); leaderId=service.LeaderId(bot.id); botType="CompanionGrindBot"; IsCompanion=()=>true; }
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
                lastMoveX=int.MinValue; return;
            }
            BotMovementService.SetGrinding(chr,true);
            var station=service.Station(chr,leader); var target=service.Target(chr,leader);
            if (target!=null)
            {
                // 真实攻击口径：按职业/武器的攻击类别停步（近战贴身、远程/魔法在射程内），
                // 不再沿用源端 120px 一刀切的 ENGAGE 圈。
                chr.equips.TryGetValue(-11,out int weapon);
                var range=BotAttack.EngageRange(chr.job,weapon);
                if (Math.Abs(chr.x-target.x)<=range.x && Math.Abs(chr.y-target.y)<=range.y)
                {
                    BotMovementService.Stop(chr); lastMoveX=int.MinValue;
                    if (BotScheduler.Now()>=nextSwing) { service.Strike(chr,target); nextSwing=BotScheduler.Now()+800; }
                }
                else Approach(new Point(target.x,target.y));
                return;
            }
            if (!service.OnFloor(chr) || Math.Abs(chr.x-leader.x)>280) Approach(station);
        }
        void Approach(Point destination)
        {
            if (Math.Abs((long)destination.x-lastMoveX)<16 && Math.Abs((long)destination.y-lastMoveY)<16 && BotMovementService.IsMoving(chr)) return;
            lastMoveX=destination.x; lastMoveY=destination.y; BotMovementService.Move(chr,destination.x,destination.y);
        }
    }
}
