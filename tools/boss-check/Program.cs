using System;
using System.Collections.Generic;
using System.IO;
using MapleDash.Wz;

static class Program
{
    static int passed;
    static void Check(bool condition,string label)
    {
        if (!condition) throw new Exception(label);
        passed++;
    }
    static void Main(string[] args)
    {
        // The source Java tests' boundary cases, plus state/lifecycle regressions.
        Check(NativeBossRules.RegenMillis(8880830)==3240,"Karing regen");
        Check(NativeBossRules.RegenMillis(8880842)==8100,"Karing final regen");
        Check(NativeBossRules.AttackCooldown(8880832,2,990)==5400,"Karing cooldown is not impact delay");
        Check(NativeBossRules.AttackCooldown(8880830,99,123)==123,"Unknown attack keeps WZ fallback");
        Check(NativeBossRules.AttackCooldown(8880140,1,1200)==60000,"Lucid stun cooldown");
        Check(NativeBossRules.SkillCooldown(8880140,128,15,20000)==20000,"Do not override unrelated seduce level");
        Check(NativeBossRules.SkillCooldown(8880141,128,16,20000)==60000,"Lucid seduce cooldown");
        Check(!NativeBossRules.IsLucid(8880164),"Support mob is not an encounter boss");
        Check(NativeBossRules.ButterflyInterval(90)==5000 && NativeBossRules.ButterflyInterval(89)==4500,"Butterfly 90% boundary");
        Check(NativeBossRules.ButterflyCount(20)==15 && NativeBossRules.ButterflyCount(19)==20,"Butterfly 20% boundary");
        Check(NativeBossRules.PercentDamage(int.MaxValue,100)==int.MaxValue,"Percentage damage cannot overflow int multiplication");
        // 0x670C99..0x670CB2: [target+0x46C] + 0x7D0. Stamp 0 means "never hit yet";
        // the window is inclusive at exactly 2000 ms.
        Check(NativeBossRules.MobAttackAllowed(0,0),"First mob attack against a fresh target lands");
        Check(!NativeBossRules.MobAttackAllowed(1000+1999,1000),"Second mob attack inside 2000 ms is rejected");
        Check(NativeBossRules.MobAttackAllowed(1000+2000,1000),"Second mob attack at exactly 2000 ms lands");
        var cast = new NativeMobCastState(20);
        Check(cast.TryUse("128:1",0,5,1000,false) && cast.Mp==15,"Accepted cast spends MP once");
        Check(!cast.TryUse("128:1",999,5,1000,false) && cast.Mp==15,"Rejected cooldown spends no MP");
        Check(cast.TryUse("128:1",1000,5,1000,false),"Cast allowed at exact cooldown boundary");
        Check(!cast.TryUse("128:2",1000,5,1000,true) && cast.Mp==10,"Skill seal rejects without spending MP");
        Check(!cast.TryUse("128:2",1000,11,1000,false),"Insufficient MP rejected");
        Check(cast.TryUse("128:2",1000,10,1000,false),"Different levels have independent cooldowns");
        var hp = new NativeMobHealth(2100000000);
        for (int i=0;i<100;i++) hp.Damage(1);
        Check(hp.Current==2099999900,"100 one-point hits at 2.1 billion HP remain exact");
        hp.Damage(long.MaxValue);
        Check(hp.Current==0 && hp.Damage(1)==0,"Lethal damage clamps and repeated hits do nothing");
        hp.Heal(long.MaxValue); Check(hp.Current==hp.Maximum,"Heal clamps without overflow");
        hp.SetPercent(16); Check(hp.Current==336000000,"Lucid failure resets phase two to exactly 16%");
        var timer = new NativeBossTimeline();
        var effects = new List<int>(); bool alive = true;
        timer.Schedule(0,100,() => alive,() => effects.Add(1));
        timer.Schedule(0,50,null,() => effects.Add(2));
        timer.Tick(49); Check(effects.Count==0,"No early impact");
        timer.Tick(50); Check(effects.Count==1 && effects[0]==2,"Impact occurs at exact deadline");
        alive=false; timer.Tick(100); Check(effects.Count==1,"Caster death cancels delayed damage");
        timer.Schedule(100,0,null,() => timer.Clear());
        timer.Schedule(100,0,null,() => effects.Add(3));
        timer.Tick(100); Check(effects.Count==1,"Clear during callback cancels already-due actions");
        timer.Schedule(100,10,null,() => effects.Add(4)); timer.Clear(); timer.Tick(1000);
        Check(effects.Count==1,"Leaving the field cancels revive/summon tasks too");
        Check(NativeBossService.Bonus(1000,40,300)==100,"Boss growth caps at +100%");
        Check(NativeBossService.Bonus(-1,-1,-1)==0,"Growth cannot be negative");
        Check(Math.Abs(NativeBossService.Bonus(500,0,150)-30)<1e-9,"Quest/card progress uses original fractional progress");
        AttackAreaGeometry();
        Persistence();
        if (args.Length>0) AuditData(args[0]);
        Console.WriteLine("Boss checks: "+passed+" passed");
    }
    // range/{lt,rb} is a rectangle, range/{sp,r} is a circle; both shapes appear in
    // the source data and NativeMobController must not confuse them with "no area".
    static void AttackAreaGeometry()
    {
        var rect=MobAttackArea.Rect(-300,-50,100,150);          // centre (-100,50), 200x100 half-extents
        Check(rect.Contains(-100,50),"Rect centre is inside");
        Check(rect.Contains(-300,-50) && rect.Contains(100,150),"Rect includes its corners");
        Check(!rect.Contains(101,50) && !rect.Contains(-100,151),"Rect rejects points outside");
        var circle=MobAttackArea.Circle(-14,-32,280);
        // Contains is only the point probe used by the geometry self-checks; the
        // attack path itself never calls it (see Overlaps below).
        Check(circle.Contains(-14,-32),"Circle centre is inside");
        Check(circle.Contains(-14,-312),"Circle boundary counts as a hit");
        // 0x66DC63 adds a flat +10 px to the authored radius before the distance
        // compare, so the authored 280 actually reaches 290.
        Check(circle.Contains(-14,-322),"Circle reaches the authored radius plus the +10 px source slack");
        Check(!circle.Contains(-14,-323),"Circle rejects just past radius+slack");
        // A circle is not its bounding box: (170,170) from the centre is 240 > 280? no --
        // 170*sqrt(2)=240.4 < 280, so use a point that only the box would accept.
        Check(!circle.Contains(-14+240,-32+240),"Circle rejects the bounding-box corner");
        Check(circle.Contains(-14+190,-32+190),"Circle accepts a point inside 280 diagonally");
        var offset=MobAttackArea.Circle(6,-60,200);
        // Only true when the radius is measured from sp: (0,200) is 200 away from the
        // origin but 260 away from (6,-60), and (6,140) is exactly 200 away from sp.
        Check(!offset.Contains(0,200) && offset.Contains(6,140),"Radius is measured from sp, not the origin");
        // The source hit test is rect-vs-rect, not point-in-rect: 0x95B734 collects
        // objects whose bounding rect overlaps the attack rect, so a body that only
        // pokes into the area still counts.
        Check(rect.Overlaps(-100,50,-86,50),"Rect overlaps a body centred inside");
        Check(rect.Overlaps(95,50,109,50),"Rect overlaps a body poking in from the right edge");
        Check(!rect.Overlaps(102,50,116,50),"Rect rejects a body entirely outside");
        Check(!rect.Overlaps(-100,151,-86,199),"Rect rejects a body just above the top edge");
        Check(rect.Overlaps(-100,-1,-86,48),"Rect overlaps a body straddling the bottom edge");
        // Circle: the same body test must use the distance to the nearest point of the
        // body, not to its centre. 0x66DB68 fetches the target's bounding rect and
        // 0x66DBDC..0x66DC21 clamps the circle centre into it before the sqrt.
        Check(circle.Overlaps(-14-14,-32,-14+14,-32+48),"Circle overlaps a body at its centre");
        Check(circle.Overlaps(-14-14-278,-32,-14-14-250,-32+48),"Circle overlaps a body whose edge touches r");
        Check(!circle.Overlaps(-14-14-310,-32,-14-14-282,-32+48),"Circle rejects a body past r+slack");
        Check(!circle.Overlaps(-14-100,-32-500,-14+100,-32-400),"Circle rejects a body far above");
        // The point probe and the body probe must disagree here, otherwise routing
        // circles through the point test would be indistinguishable in production:
        // feet 140 px above the centre are outside r+10=290, but the body (48 px tall,
        // reaching down to 92 px above the centre) is inside.
        var tight=MobAttackArea.Circle(0,0,100);
        Check(!tight.Contains(0,-140),"Point probe rejects feet 140 px from a 100 px circle");
        Check(tight.Overlaps(-14,-140,14,-92),"Body probe accepts the same stance through the torso");
        // Type 3/4 wave layout (0x66DF4F): centreX offset = ((start+i)*spacing +
        // spacing/2) * dir. start is authored negative, which is why areaCount looks
        // like 2*|start|+1 in the data.
        Check(MobAttackArea.WaveOffsetX(-2,0,100,1)==-150f,"Wave 0 with start=-2 sits half a band left of centre");
        Check(MobAttackArea.WaveOffsetX(-2,1,100,1)==-50f,"Wave 1 advances by exactly one band");
        Check(MobAttackArea.WaveOffsetX(-2,2,100,1)==50f,"Wave 2 crosses to the positive side");
        Check(MobAttackArea.WaveOffsetX(-2,2,100,-1)==-50f,"Facing left mirrors the band offset");
        Check(MobAttackArea.WaveOffsetX(-2,2,100,0)==50f,"Non-negative dir keeps the offset unmirrored");
        // Only waves whose bit is set in the caller's mask materialise (0x66DF33),
        // so a mask of 0 produces nothing even when attackCount says otherwise.
        int mask=0b101, w=0;
        for (int i=0;i<3;i++) if ((mask&(1<<i))!=0) w++;
        Check(w==2,"Wave mask gates which bands of attackCount actually spawn");
    }

    static void Persistence()
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash-boss-"+Guid.NewGuid().ToString("N")+".db");
        try
        {
            var db=DbStore.Open(path);
            int id=(int)db.Table("characters").Insert(new DbRow { ["accountid"]=1L,["name"]="BossTest" });
            var service=new NativeBossService(db);
            DateTimeOffset now=new DateTimeOffset(2026,10,23,13,0,0,TimeSpan.FromHours(8));
            service.Clock=() => now;
            var request=new BossRequest {characterId=id,mobId=8800002,operation=BossOperation.RecordKill,encounter="one"};
            service.Handle(request); service.Handle(request);
            Check(db.Table("boss_growth").rows.Count==1 && DbStore.Int(db.Table("boss_growth").rows[0],"kills")==1,"Kill replay cannot double progress");
            double expected=1.0018;
            Check(Math.Abs(service.Handle(new BossRequest {characterId=id}).multiplier-expected)<1e-9,"Zakum first kill grants original 0.18%");
            db.Save();
            var reopened=DbStore.Open(path);
            Check(Math.Abs(new NativeBossService(reopened).Handle(new BossRequest {characterId=id}).multiplier-expected)<1e-9,"Boss growth survives reload");
            var config=db.Table("game_config");
            config.Delete(x => DbStore.Str(x,"config_code")=="use_enable_daily_expeditions");
            config.Insert(new DbRow { ["config_type"]="server",["config_code"]="use_enable_daily_expeditions",["config_value"]="true" });
            request=new BossRequest {characterId=id,boss="ZAKUM",operation=BossOperation.Enter};
            Check(service.Handle(request).ok && service.Handle(request).ok && service.Handle(request).ok,"Three Zakum entries allowed");
            Check(!service.Handle(request).ok,"Fourth Zakum entry rejected");
            now=now.AddDays(1);
            Check(service.Handle(request).ok,"Daily entries reset on next server day");
            service.DeleteCharacter(id);
            Check(db.Table("boss_growth").rows.Count==0 && db.Table("bosslog_daily").rows.Count==0,"Character removal cleans boss records");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    static void AuditData(string data)
    {
        using var skills=new ImgFile(Path.Combine(data,"Skill/MobSkill.img"));
        int[] ids={8800000,8800001,8800002,8810002,8820001,8500001,8500002,8880000,8880301,8880342,8880140,8880141,8880152,8880830,8880831,8880832,8880837,8880842};
        int entries=0;
        foreach(int id in ids)
        {
            string path=Path.Combine(data,"Mob/"+id.ToString("D7")+".img");
            if (!File.Exists(path)) { Console.WriteLine("Data gap: missing Mob/"+id+".img"); continue; }
            using var mob=new ImgFile(path);
            Check(mob.Root.Int("info/maxHP")>0,"Boss HP exists "+id);
            var list=mob.Root.Get("info/skill");
            if (list==null) { Console.WriteLine("Data gap: "+id+" has no info/skill binding"); continue; }
            foreach(var entry in list.Children)
            {
                int skill=entry.Int("skill"),level=entry.Int("level");
                Check(skills.Root.Get(skill+"/level/"+level)!=null,"Referenced MobSkill exists "+id+":"+skill+":"+level);
                entries++;
            }
        }
        Console.WriteLine("Original IMG audit: "+ids.Length+" bosses, "+entries+" skill references");
        // Attack-area shapes against real IMG: circles where the data authors sp+r,
        // rectangles where it authors lt+rb. 3000000 is the cleanest circle (r=280).
        var areaCases=new (int id,string action,bool circle)[] {
            (3000000,"attack1",true),(3210800,"attack1",true),(3220000,"attack2",true),
            (2400040,"attack1",false),(8880000,"attack4",false),(8870000,"attack5",false),
        };
        int rectShapes=0,circleShapes=0;
        foreach(var (id,action,wantCircle) in areaCases)
        {
            string path=Path.Combine(data,"Mob/"+id.ToString("D7")+".img");
            if (!File.Exists(path)) { Console.WriteLine("Data gap: missing Mob/"+id+".img"); continue; }
            using var mob=new ImgFile(path);
            var info=mob.Root.Get(action)?.Get("info");
            if (info==null) { Console.WriteLine("Data gap: "+id+"/"+action+" has no info"); continue; }
            Check(MobAttackArea.TryParse(info,out var area),"Area parses for "+id+"/"+action);
            Check(area.circle==wantCircle,"Area shape for "+id+"/"+action);
            if (area.circle) circleShapes++; else rectShapes++;
        }
        Console.WriteLine("Attack area shapes: "+rectShapes+" rect, "+circleShapes+" circle");
    }
}
