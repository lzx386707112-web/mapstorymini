using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot
{
    // Unity adapters supply live monster instances; all selection/timing stays on the server.
    public sealed class BotGrindMob
    {
        public int id, mapId, world, channel, x, y;
        public long maxHp;
        public bool alive, fake, boss;
    }
    public sealed class BotPartyGrindService : IDisposable
    {
        public const int ItemId = 2431159, Count = 5;
        public const long Duration = 1800000;
        readonly BotPartyService parties;
        readonly Func<int,MapRuntime> maps;
        readonly Random random;
        readonly Decorator.BotDecorate decorator;
        readonly Dictionary<int,int> leaders = new Dictionary<int,int>();
        readonly Dictionary<int,List<int>> companions = new Dictionary<int,List<int>>();
        readonly Dictionary<int,int> claims = new Dictionary<int,int>();
        readonly Dictionary<int,BotPartyGrindFloors.Floor> floors = new Dictionary<int,BotPartyGrindFloors.Floor>();
        readonly Dictionary<int,int> floorMaps = new Dictionary<int,int>();
        readonly Dictionary<int,(long expiry,BotTasks.Handle watch)> active = new Dictionary<int,(long,BotTasks.Handle)>();
        List<BotGrindMob> cachedMobs = new List<BotGrindMob>();
        long cachedAt = long.MinValue;
        int cachedMap, cachedWorld, cachedChannel;
        public Func<int,int,int,List<BotGrindMob>> Monsters;
        public Action<BotCharacter,BotGrindMob,int,BotAttack.Skill,int[]> Hit;
        public Action<int,string> Notice;
        public Action<int,bool> Effect;
        public BotPartyGrindService(BotPartyService parties, Func<int,MapRuntime> maps, Random random = null)
        {
            this.parties=parties; this.maps=maps; this.random=random ?? new Random();
            decorator = new Decorator.BotDecorate(BotResources.Load(),BotItemCatalog.Shared,BotTypeManager.Output,this.random);
        }
        public int LeaderId(int botId) => leaders.TryGetValue(botId,out int id) ? id : -1;
        public long Expiry(int id) => active.TryGetValue(id,out var state) ? state.expiry : 0;
        public bool IsActive(int id) => active.ContainsKey(id);
        public IReadOnlyList<int> Companions(int id) => companions.TryGetValue(id,out var ids) ? ids : (IReadOnlyList<int>)Array.Empty<int>();
        public BotActor Leader(int botId)
        {
            var leader=parties.Get(LeaderId(botId));
            var bot=BotRegistry.Get(botId);
            return leader != null && leader.online && !leader.IsBot() && bot != null &&
                leader.world==bot.world && leader.channel==bot.channel ? leader : null;
        }
        public static int CompanionLevel(int level,int offset) => Math.Max(1,Math.Min(255,level+Math.Max(-3,Math.Min(3,offset))));
        public bool Start(BotActor leader)
        {
            if (leader == null || leader.IsBot() || !leader.online) return false;
            string failure = IsActive(leader.id) ? "基友集合仍在生效中，无法重复使用。" :
                parties.For(leader.id) == null ? "请先开启组队后再使用基友集合。未消耗道具。" :
                parties.For(leader.id).members.Count != 1 ? "队伍空位不足，需要5个空位才能召唤基友。未消耗道具。" : null;
            if (failure != null) { Notice?.Invoke(leader.id,failure); return false; }
            if (!Spawn(leader)) { Notice?.Invoke(leader.id,"基友召唤失败，未消耗道具。"); return false; }
            long expiry=BotScheduler.Now()+Duration;
            var watch=BotTasks.ScheduleFixedRate(() => {
                if (BotScheduler.Now() >= expiry) Stop(leader.id,"搬砖时间结束，基友已离开。");
                else if (parties.For(leader.id) == null) Stop(leader.id,"组队已解散，基友集合结束。");
            },1000,1000);
            active[leader.id]=(expiry,watch);
            Effect?.Invoke(leader.id,true); Notice?.Invoke(leader.id,"基友集合生效，持续30分钟。"); return true;
        }
        bool Spawn(BotActor leader)
        {
            Stop(leader.id);
            var map=maps(leader.mapId);
            var available=BotPartyGrindFloors.FromMap(map,new Point((int)leader.x,(int)leader.y));
            var ids=new List<int>();
            try
            {
                for (int i=0;i<Count;i++)
                {
                    var floor=available[BotPartyGrindFloors.IndexFor(i,available.Count)];
                    var at=BotPartyGrindFloors.Station(map,floor,(int)leader.x+((i%3)-1)*48);
                    var bot=BotGeneration.CreateBotPollReadiness(at.x,at.y,leader.mapId,true,false);
                    if (bot == null) { Cleanup(ids); return false; }
                    ids.Add(bot.id);
                    bot.world=leader.world; bot.channel=leader.channel; bot.client.world=leader.world; bot.client.channel=leader.channel;
                    bot.level=CompanionLevel(leader.level,random.Next(-3,4)); bot.job=decorator.SelectJob(bot.level);
                    int primary=Math.Min(999,4+Math.Max(0,bot.level-1)*5);
                    bot.str=bot.dex=bot.intelligence=bot.luk=primary; decorator.EquipWeapon(bot);
                    leaders[bot.id]=leader.id; floors[bot.id]=new BotPartyGrindFloors.Floor(at.y,floor.minX,floor.maxX);
                    var sm=BotTypeManager.CreateAndSetBot(bot,BotType.COMPANION_GRIND_BOT);
                    if (!parties.Join(bot,parties.For(leader.id).id)) { Cleanup(ids); return false; }
                    BotMovementService.SetGrinding(bot,true); BotMovementService.Teleport(bot,at);
                    sm.StartScheduledTask(180+i*80);
                }
                companions[leader.id]=ids; floorMaps[leader.id]=leader.mapId; return ids.Count==Count;
            }
            catch { Cleanup(ids); return false; }
        }
        void BindFloors(BotActor leader)
        {
            if (!companions.TryGetValue(leader.id,out var ids)) return;
            bool complete=true; foreach (int id in ids) if (!floors.ContainsKey(id)) complete=false;
            if (floorMaps.TryGetValue(leader.id,out int mapId) && mapId==leader.mapId && complete) return;
            var map=maps(leader.mapId); var available=BotPartyGrindFloors.FromMap(map,new Point((int)leader.x,(int)leader.y));
            for (int i=0;i<ids.Count;i++)
            {
                var floor=available[BotPartyGrindFloors.IndexFor(i,available.Count)];
                var at=BotPartyGrindFloors.Station(map,floor,(int)leader.x);
                floors[ids[i]]=new BotPartyGrindFloors.Floor(at.y,floor.minX,floor.maxX);
            }
            floorMaps[leader.id]=leader.mapId;
        }
        public Point Station(BotCharacter bot,BotActor leader)
        {
            BindFloors(leader);
            if (!floors.TryGetValue(bot.id,out var floor)) return new Point((int)leader.x,(int)leader.y);
            int slot=companions.TryGetValue(leader.id,out var ids) ? Math.Max(0,ids.IndexOf(bot.id)) : 0;
            return BotPartyGrindFloors.Station(maps(leader.mapId),floor,(int)leader.x+((slot%3)-1)*48);
        }
        public bool OnFloor(BotCharacter bot) => !floors.TryGetValue(bot.id,out var floor) || floor.ContainsY((int)bot.y);
        List<BotGrindMob> Mobs(BotActor bot)
        {
            long now=BotScheduler.Now();
            if (cachedAt != long.MinValue && cachedMap==bot.mapId && cachedWorld==bot.world && cachedChannel==bot.channel && now-cachedAt<50) return cachedMobs;
            cachedMobs=Monsters?.Invoke(bot.mapId,bot.world,bot.channel) ?? new List<BotGrindMob>();
            cachedAt=now; cachedMap=bot.mapId; cachedWorld=bot.world; cachedChannel=bot.channel; return cachedMobs;
        }
        public BotGrindMob Target(BotCharacter bot,BotActor leader)
        {
            if (bot.mapId!=leader.mapId || bot.world!=leader.world || bot.channel!=leader.channel) return null;
            BindFloors(leader); var mobs=Mobs(bot);
            bool Eligible(BotGrindMob mob) => mob != null && mob.alive && !mob.fake && !mob.boss &&
                mob.mapId==bot.mapId && mob.world==bot.world && mob.channel==bot.channel &&
                (!floors.TryGetValue(bot.id,out var floor) || floor.ContainsY(mob.y) || Math.Abs(mob.y-bot.y)<=80) && Math.Abs(mob.x-leader.x)<=560;
            if (claims.TryGetValue(bot.id,out int held))
            { var mob=mobs.Find(m=>m.id==held); if (Eligible(mob)) return mob; claims.Remove(bot.id); }
            var taken=new HashSet<int>();
            if (companions.TryGetValue(leader.id,out var siblings)) foreach (int sibling in siblings)
                if (sibling!=bot.id && claims.TryGetValue(sibling,out int target)) taken.Add(target);
            BotGrindMob best=null; float distance=float.MaxValue;
            foreach (var mob in mobs)
                if (Eligible(mob) && !taken.Contains(mob.id) && Math.Abs(mob.x-bot.x)<distance)
                { best=mob; distance=Math.Abs(mob.x-bot.x); }
            if (best!=null) claims[bot.id]=best.id; return best;
        }
        public bool SameParty(BotCharacter bot,BotActor leader) => parties.For(leader.id)!=null && parties.For(bot.id)==parties.For(leader.id);
        public void Strike(BotCharacter bot,BotGrindMob target)
        {
            if (target==null || !target.alive) return;
            int damage=BotAttack.LineDamage(BotAttack.JobTier(bot.job),bot.level,target.maxHp,random);
            bot.stance=target.x<bot.x ? 5 : 4; bot.facing=target.x<bot.x ? -1 : 1;
            bot.equips.TryGetValue(-11,out int weapon); var skill=BotAttack.GrindSkill(bot.job,weapon);
            BotAttack.BasicSwing(bot,random);
            Hit?.Invoke(bot,target,damage,skill,BotAttack.SplitDamage(damage,skill.hits));
        }
        public void Stop(int leaderId,string message=null)
        {
            bool wasActive=active.TryGetValue(leaderId,out var running);
            if (wasActive) { active.Remove(leaderId); running.watch.Cancel(); }
            if (companions.TryGetValue(leaderId,out var ids)) { companions.Remove(leaderId); Cleanup(ids); }
            floorMaps.Remove(leaderId);
            if (wasActive) { Effect?.Invoke(leaderId,false); if (!string.IsNullOrEmpty(message)) Notice?.Invoke(leaderId,message); }
        }
        void Cleanup(List<int> ids)
        {
            foreach (int id in ids)
            {
                leaders.Remove(id); floors.Remove(id); claims.Remove(id);
                var bot=BotRegistry.Get(id); parties.Leave(id);
                if (bot==null) continue;
                BotMovementService.Stop(bot); BotMovementService.Disable(bot);
                BotTypeManager.ManuallyStopBot(bot); BotGeneration.RemoveBotFromServer(bot);
            }
        }
        public void Dispose()
        { foreach (int id in new List<int>(active.Keys)) Stop(id); foreach (int id in new List<int>(companions.Keys)) Stop(id); Monsters=null; Hit=null; Notice=null; Effect=null; }
    }
}
