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
        Persistence();
        if (args.Length>0) AuditData(args[0]);
        Console.WriteLine("Boss checks: "+passed+" passed");
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
    }
}
