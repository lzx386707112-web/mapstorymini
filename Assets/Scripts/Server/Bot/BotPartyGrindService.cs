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
        // id   = 这一只怪的运行时唯一句柄（Unity GetInstanceID），只用来反查 EnemyUnit / 认领。
        // mobId= 怪物模板 id（Mob.wz 里的 9300001 这种）。组队任务按「打哪种怪」过滤必须用它，
        //        拿 id 去比模板 id 永远不相等 —— 那就是「假人看不到怪、站在 NPC 旁边不动」的根因。
        public int id, mobId, mapId, world, channel, x, y;
        public long maxHp;
        public bool alive, fake, boss;
    }
    // 野外挂机用：附近一件可捡掉落。Unity 侧 BotGroundPickup 填，Server 只认坐标。
    public struct GrindLoot
    {
        public bool found;
        public int x, y;
        public float distance;
    }
    public sealed class BotPartyGrindService : IDisposable
    {
        // 原版数值（PartyGrindCompat），后台可配后仍是默认值
        public const int ItemId = 2431159, Count = 5, PartyCap = 6;
        public const long Duration = 1800000;
        readonly BotPartyService parties;
        readonly Func<int,MapRuntime> maps;
        readonly Random random;
        readonly DbStore db;
        readonly Decorator.BotDecorate decorator;
        readonly Dictionary<int,int> leaders = new Dictionary<int,int>();
        readonly Dictionary<int,List<int>> companions = new Dictionary<int,List<int>>();
        readonly Dictionary<int,int> claims = new Dictionary<int,int>();
        readonly Dictionary<int,(int map,int mob,long until)> avoided = new Dictionary<int,(int,int,long)>();
        readonly Dictionary<int,long> nextChat = new Dictionary<int,long>();
        // 队长层重绑的节流（见 LeaderSpot）：每个假人最多 5 秒算一次地面聚类。
        readonly Dictionary<int,long> floorRebindAt = new Dictionary<int,long>();
        readonly Dictionary<int,BotPartyGrindFloors.Floor> floors = new Dictionary<int,BotPartyGrindFloors.Floor>();
        readonly Dictionary<int,int> floorMaps = new Dictionary<int,int>();
        readonly Dictionary<int,(long expiry,BotTasks.Handle watch)> active = new Dictionary<int,(long,BotTasks.Handle)>();
        List<BotGrindMob> cachedMobs = new List<BotGrindMob>();
        long cachedAt = long.MinValue;
        int cachedMap, cachedWorld, cachedChannel;
        public Func<int,int,int,List<BotGrindMob>> Monsters;
        public Action<BotCharacter,BotGrindMob,int,BotAttack.Skill,int[]> Hit;
        /// <summary>附近最近的可捡掉落（Unity 侧 BotGroundPickup.Nearest）。</summary>
        public Func<BotCharacter,float,GrindLoot> FindLoot;
        /// <summary>捡附近的东西（Unity 侧 BotGroundPickup.PickAny）：(bot, range, max) → 件数。</summary>
        public Func<BotCharacter,float,int,int> TakeLoot;
        public Action<int,string> Notice;
        public Action<int,bool> Effect;
        public BotPartyGrindService(BotPartyService parties, Func<int,MapRuntime> maps, Random random = null, DbStore db = null)
        {
            this.parties=parties; this.maps=maps; this.random=random ?? new Random(); this.db=db;
            decorator = new Decorator.BotDecorate(BotResources.Load(),BotItemCatalog.Shared,BotTypeManager.Output,this.random);
        }
        public int LeaderId(int botId) => leaders.TryGetValue(botId,out int id) ? id : -1;
        public long Expiry(int id) => active.TryGetValue(id,out var state) ? state.expiry : 0;
        public bool IsActive(int id) => active.ContainsKey(id);
        public IReadOnlyList<int> Companions(int id) => companions.TryGetValue(id,out var ids) ? ids : (IReadOnlyList<int>)Array.Empty<int>();
        /// <summary>当前在场的伴侣队长（后台「组队伴侣」面板用）。</summary>
        public IReadOnlyCollection<int> ActiveLeaders => active.Keys;
        /// <summary>队长下所有伴侣的 id（面板展示，Companions 同源）。</summary>
        public int CompanionCountOf(int leaderId) => companions.TryGetValue(leaderId,out var ids) ? ids.Count : 0;
        /// <summary>后台配置的召唤数量（队伍只剩自己时需要几个空位）。</summary>
        public int ConfiguredCount => BotConfig.PartyCompanionCount(db);
        public long ConfiguredDurationMs => BotConfig.PartyCompanionDurationMs(db);
        public bool Enabled => BotConfig.PartyCompanionEnabled(db);
        public BotActor Leader(int botId)
        {
            var leader=parties.Get(LeaderId(botId));
            var bot=BotRegistry.Get(botId);
            return leader != null && leader.online && !leader.IsBot() && bot != null &&
                leader.world==bot.world && leader.channel==bot.channel ? leader : null;
        }
        public static int CompanionLevel(int level,int offset,int jitter=3)
            => Math.Max(1,Math.Min(255,level+Math.Max(-jitter,Math.Min(jitter,offset))));
        public bool Start(BotActor leader)
        {
            if (leader == null || leader.IsBot() || !leader.online) return false;
            if (!Enabled) { Notice?.Invoke(leader.id,"组队伴侣已被后台关闭。"); return false; }
            int count = ConfiguredCount;
            bool requireParty = BotConfig.PartyCompanionRequireParty(db);
            var party = parties.For(leader.id);
            if (party == null && !requireParty) { if (!parties.Create(leader)) { Notice?.Invoke(leader.id,"无法创建队伍，基友召唤失败。"); return false; } party = parties.For(leader.id); }
            string failure = IsActive(leader.id) ? "基友集合仍在生效中，无法重复使用。" :
                party == null ? "请先开启组队后再使用基友集合。未消耗道具。" :
                party.members.Count + count > PartyCap ? "队伍空位不足，需要" + count + "个空位才能召唤基友。未消耗道具。" : null;
            if (failure != null) { Notice?.Invoke(leader.id,failure); return false; }
            if (!Spawn(leader,count)) { Notice?.Invoke(leader.id,"基友召唤失败，未消耗道具。"); return false; }
            long expiry=BotScheduler.Now()+ConfiguredDurationMs;
            var watch=BotTasks.ScheduleFixedRate(() => {
                if (BotScheduler.Now() >= expiry) Stop(leader.id,"搬砖时间结束，基友已离开。");
                else if (parties.For(leader.id) == null) Stop(leader.id,"组队已解散，基友集合结束。");
            },1000,1000);
            active[leader.id]=(expiry,watch);
            Effect?.Invoke(leader.id,true);
            Notice?.Invoke(leader.id,"基友集合生效，持续" + BotConfig.PartyCompanionMinutes(db) + "分钟。"); return true;
        }
        bool Spawn(BotActor leader,int count)
        {
            Stop(leader.id);
            var map=maps(leader.mapId);
            var available=BotPartyGrindFloors.FromMap(map,new Point((int)leader.x,(int)leader.y));
            var ids=new List<int>();
            try
            {
                for (int i=0;i<count;i++)
                {
                    var floor=available[BotPartyGrindFloors.IndexFor(i,available.Count)];
                    var at=BotPartyGrindFloors.Station(map,floor,(int)leader.x+((i%3)-1)*48);
                    var bot=BotGeneration.CreateBotPollReadiness(at.x,at.y,leader.mapId,true,false);
                    if (bot == null) { Cleanup(ids); return false; }
                    ids.Add(bot.id);
                    bot.world=leader.world; bot.channel=leader.channel; bot.client.world=leader.world; bot.client.channel=leader.channel;
                    int jitter=BotConfig.PartyCompanionLevelJitter(db);
                    bot.level=CompanionLevel(leader.level,random.Next(-jitter,jitter+1)); bot.job=decorator.SelectJob(bot.level);
                    int primary=Math.Min(999,4+Math.Max(0,bot.level-1)*5);
                    bot.str=bot.dex=bot.intelligence=bot.luk=primary; decorator.EquipWeapon(bot);
                    leaders[bot.id]=leader.id; floors[bot.id]=new BotPartyGrindFloors.Floor(at.y,floor.minX,floor.maxX);
                    var sm=BotTypeManager.CreateAndSetBot(bot,BotType.COMPANION_GRIND_BOT);
                    if (!parties.Join(bot,parties.For(leader.id).id)) { Cleanup(ids); return false; }
                    BotMovementService.SetGrinding(bot,true); BotMovementService.Teleport(bot,at);
                    sm.StartScheduledTask(180+i*80);
                }
                companions[leader.id]=ids; floorMaps[leader.id]=leader.mapId; return ids.Count==count;
            }
            catch { Cleanup(ids); return false; }
        }
        void BindFloors(BotActor leader)
        {
            if (!companions.TryGetValue(leader.id,out var ids)) return;
            bool complete=true; foreach (int id in ids) if (!floors.ContainsKey(id)) complete=false;
            if (floorMaps.TryGetValue(leader.id,out int mapId) && mapId==leader.mapId && complete) return;
            var map=maps(leader.mapId); var available=BotPartyGrindFloors.FromMap(map,new Point((int)leader.x,(int)leader.y));
            foreach (int id in ids) { claims.Remove(id); avoided.Remove(id); }
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
        /// <summary>附近最近的可捡掉落；没接线（纯 Server 自检）时 found 恒为 false。</summary>
        public GrindLoot Loot(BotCharacter bot,float range)
        {
            if (FindLoot == null || bot == null) return default;
            try { return FindLoot(bot,range); }
            catch (Exception) { return default; }
        }
        /// <summary>捡附近的东西（金币也捡），返回件数。</summary>
        public int TakeLootNearby(BotCharacter bot,float range,int max)
        {
            if (TakeLoot == null || bot == null) return 0;
            try { return TakeLoot(bot,range,max); }
            catch (Exception) { return 0; }
        }
        /// <summary>
        /// 无怪时沿本层左右巡逻的落点：队长附近、又落在本层范围内的一个地面点。
        /// 以前没怪时只有「离队长超过 280px 才回去」这一条，假人站在 station 上像木桩。
        /// </summary>
        public Point? PatrolPoint(BotCharacter bot,BotActor leader)
        {
            if (bot == null || leader == null || bot.mapId != leader.mapId) return null;
            BindFloors(leader);
            if (!floors.TryGetValue(bot.id,out var floor)) return null;
            int lo = floor.minX, hi = floor.maxX;
            int anchor = (int)leader.x;
            int near = Math.Max(lo,anchor-420), far = Math.Min(hi,anchor+420);
            if (far-near >= 160) { lo = near; hi = far; }
            // 窄平台（小台阶）：还是要给一个点，否则「没怪」时又变回站桩。
            if (hi-lo < 80) { lo = floor.minX; hi = floor.maxX; }
            if (hi-lo < 8) return null;
            int here = (int)bot.x;
            int x = lo + random.Next(0,hi-lo);
            // 别选在脚边：「走两步就到」的巡逻看起来还是在原地抖。
            if (Math.Abs(x-here) < 90)
                x = here + (here-lo > hi-here ? -1 : 1) * random.Next(90,200);
            x = Math.Max(lo,Math.Min(hi,x));
            return BotPartyGrindFloors.Station(maps(bot.mapId),floor,x);
        }
        /// <summary>
        /// 队长所在层的站位。长时间在本层打不到怪时（守着一个刷空了的小平台），
        /// 假人跟到队长那层去；顺手把 floor 重绑到队长层，否则巡逻点又把它拉回旧平台。
        /// </summary>
        public Point? LeaderSpot(BotCharacter bot,BotActor leader)
        {
            if (bot == null || leader == null || bot.mapId != leader.mapId) return null;
            var map = maps(leader.mapId);
            int slot = companions.TryGetValue(leader.id,out var ids) ? Math.Max(0,ids.IndexOf(bot.id)) : 0;
            int desired = (int)leader.x + ((slot%3)-1)*48;
            bool hasFloor = floors.TryGetValue(bot.id,out var current);
            if (hasFloor && current.ContainsY((int)leader.y))
                return BotPartyGrindFloors.Station(map,current,desired);
            long now = BotScheduler.Now();
            if (floorRebindAt.TryGetValue(bot.id,out long at) && now-at < 5000)
                return hasFloor ? BotPartyGrindFloors.Station(map,current,desired) : (Point?)null;
            floorRebindAt[bot.id]=now;
            var available = BotPartyGrindFloors.FromMap(map,new Point((int)leader.x,(int)leader.y));
            if (available.Count == 0) return null;
            var pick = available[0]; float best = float.MaxValue;
            foreach (var floor in available)
            { float d = Math.Abs(floor.centerY-(int)leader.y); if (d<best) { best=d; pick=floor; } }
            var home = BotPartyGrindFloors.Station(map,pick,desired);
            floors[bot.id]=new BotPartyGrindFloors.Floor(home.y,pick.minX,pick.maxX);
            return home;
        }
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
            long now=BotScheduler.Now();
            bool Eligible(BotGrindMob mob) => mob != null && mob.alive && !mob.fake &&
                mob.mapId==bot.mapId && mob.world==bot.world && mob.channel==bot.channel &&
                (!avoided.TryGetValue(bot.id,out var skip) || skip.map!=bot.mapId || skip.mob!=mob.id || now>=skip.until);
            float Distance(BotGrindMob mob) => Math.Abs(mob.x-bot.x)+Math.Abs(mob.y-bot.y)*2f;
            // Boss 出现立即抢占普通怪；全队允许集火同一只 Boss。
            BotGrindMob boss=null; float bossDistance=float.MaxValue;
            foreach (var mob in mobs)
                if (Eligible(mob) && mob.boss && Distance(mob)<bossDistance)
                { boss=mob; bossDistance=Distance(mob); }
            if (boss!=null) { claims[bot.id]=boss.id; return boss; }
            var taken=new HashSet<int>();
            if (companions.TryGetValue(leader.id,out var siblings)) foreach (int sibling in siblings)
                if (sibling!=bot.id && BotRegistry.Get(sibling)?.mapId==bot.mapId && claims.TryGetValue(sibling,out int target)) taken.Add(target);
            if (claims.TryGetValue(bot.id,out int held))
            {
                var mob=mobs.Find(m=>m!=null && m.id==held);
                if (Eligible(mob) && !taken.Contains(held)) return mob;
                claims.Remove(bot.id);
            }
            BotGrindMob best=null; float distance=float.MaxValue;
            foreach (var mob in mobs)
                if (Eligible(mob) && !taken.Contains(mob.id))
                {
                    // 楼层只作偏好，不再阻止跨层/远处寻怪。
                    float d=Distance(mob)+(floors.TryGetValue(bot.id,out var floor) && !floor.ContainsY(mob.y) ? 160 : 0);
                    if (d<distance) { best=mob; distance=d; }
                }
            if (best==null) foreach (var mob in mobs)
                if (Eligible(mob) && Distance(mob)<distance) { best=mob; distance=Distance(mob); }
            if (best!=null) claims[bot.id]=best.id;
            return best;
        }
        public void AvoidTarget(BotCharacter bot,int mobId,long now)
        {
            claims.Remove(bot.id);
            avoided[bot.id]=(bot.mapId,mobId,now+10000);
        }
        public void ReleaseTarget(BotCharacter bot) => claims.Remove(bot.id);
        public bool TryChat(BotCharacter bot,string text,long now)
        {
            int leader=LeaderId(bot.id);
            var actor=Leader(bot.id);
            if (actor==null || actor.mapId!=bot.mapId || string.IsNullOrWhiteSpace(text) ||
                nextChat.TryGetValue(leader,out long ready) && now<ready) return false;
            nextChat[leader]=now+15000;
            BotTypeManager.Output.Speak(bot,text);
            return true;
        }
        public bool SameParty(BotCharacter bot,BotActor leader) => parties.For(leader.id)!=null && parties.For(bot.id)==parties.For(leader.id);
        public void Strike(BotCharacter bot,BotGrindMob target)
        {
            if (target==null || !target.alive) return;
            bot.equips.TryGetValue(-11,out int weapon);
            var skill=BotAttack.GrindSkill(bot.job,weapon);
            var range=BotAttack.EngageRange(bot.job,weapon);
            // 服务端二次校验：不在攻击距离内不出手（AI 每帧先判 ENGAGE，这里拦瞬移边缘帧）。
            if (Math.Abs(target.x-bot.x)>range.x || Math.Abs(target.y-bot.y)>range.y) return;
            int damage=BotAttack.LineDamage(BotAttack.JobTier(bot.job),bot.level,target.maxHp,random);
            bot.stance=target.x<bot.x ? 5 : 4; bot.facing=target.x<bot.x ? -1 : 1;
            // 源端 grindHit：有技能只播技能攻击（showSkillVisual + 对应 CLOSE/RANGED/MAGIC 包），
            // 无技能才走普攻 visibleHit。此前总是先 BasicSwing 再叠技能特效，观感全是普攻。
            if (skill.id==0) BotAttack.BasicSwing(bot,random);
            Hit?.Invoke(bot,target,damage,skill,BotAttack.SplitDamage(damage,skill.hits));
        }
        public void Stop(int leaderId,string message=null)
        {
            bool wasActive=active.TryGetValue(leaderId,out var running);
            if (wasActive) { active.Remove(leaderId); running.watch.Cancel(); }
            if (companions.TryGetValue(leaderId,out var ids)) { companions.Remove(leaderId); Cleanup(ids); }
            floorMaps.Remove(leaderId); nextChat.Remove(leaderId);
            if (wasActive) { Effect?.Invoke(leaderId,false); if (!string.IsNullOrEmpty(message)) Notice?.Invoke(leaderId,message); }
        }
        void Cleanup(List<int> ids)
        {
            foreach (int id in ids)
            {
                leaders.Remove(id); floors.Remove(id); claims.Remove(id); avoided.Remove(id); floorRebindAt.Remove(id);
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
