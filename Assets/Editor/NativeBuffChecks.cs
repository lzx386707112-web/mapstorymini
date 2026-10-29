using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class NativeBuffChecks
{
    static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    static bool renderAfter;
    public static void RunMovementOracle()
    {
        try { CheckMovementOracle(); NativeCombinationChecks.Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void CheckMovementOracle()
    {
        var fixture=(Dictionary<string,object>)MiniJson.Parse(System.IO.File.ReadAllText("tools/native-movement-stat-oracle.json"));
        var cases=(List<object>)fixture["cases"];
        foreach (Dictionary<string,object> entry in cases)
        {
            var input=(Dictionary<string,object>)entry["input"]; var output=(Dictionary<string,object>)entry["output"];
            int walk=PlayerCtrl.MovementPercent(Art.Int(input,"equipment"),Art.Int(input,"passive"),Art.Int(input,"temporary"),140,Art.Int(input,"forced"));
            int jump=PlayerCtrl.MovementPercent(Art.Int(input,"jumpEquipment"),0,Art.Int(input,"jumpTemporary"),123);
            Require(Math.Abs(walk/100.0-Convert.ToDouble(output["walk"]))<1e-10 &&
                Math.Abs(jump/100.0-Convert.ToDouble(output["jump"]))<1e-10,"Original movement stat oracle mismatch");
        }
        Debug.Log("[NativeMovementStatChecks] PASS "+cases.Count+" original x86 attribute/getter/clamp cases");
    }
    public static void RunAll() { renderAfter=true; Run(); }
    public static void Run()
    {
        GameObject actor=null;
        try
        {
            CheckMovementOracle();
            var flags=BindingFlags.NonPublic|BindingFlags.Static;
            typeof(Profile).GetField("data",flags).SetValue(null,new PlayerProfileData());
            typeof(Profile).GetField("debugOverride",flags).SetValue(null,true);
            var root=(Dictionary<string,object>)MiniJson.Parse(Resources.Load<TextAsset>("UI/JobTree").text);
            int levels=0,buffLevels=0;
            var keys=new HashSet<string>();
            foreach (var pair in (Dictionary<string,object>)root["skills"])
            {
                int id=int.Parse(pair.Key);
                var skill=SkillBook.Get(id);
                foreach (var level in skill.levels.Keys)
                {
                    var stats=NativeBuffs.SkillStats(id,int.Parse(level)); levels++;
                    if (stats.Count==0) continue;
                    buffLevels++;
                    foreach (string stat in stats.Keys) keys.Add(stat);
                    Require(Art.Tex(skill.icon)!=null,"Missing status icon: "+id);
                }
            }
            Require(NativeBuffs.SkillStats(1001,1)["RECOVERY"]==4,"Recovery mapping");
            Require(NativeBuffs.SkillStats(1001003,1)["WDEF"]==2,"Iron Body generic status");
            Require(NativeBuffs.SkillStats(1301007,1).Count==2,"Hyper Body paired status");
            Require(NativeBuffs.SkillStats(4121006,1).ContainsKey("SHADOW_CLAW"),"Explicit zero buff was dropped");
            Require(NativeBuffs.SkillStats(3111005,1).Count==1,"Summon PAD became player attack");
            Require(NativeBuffs.SkillStats(1201006,1).Count==0,"Monster-only Threaten became player buff");
            for (int i=0;i<16;i++)
            {
                var tex=Art.Tex("wz://UI/UIWindow.img/Skill/CoolTime/"+i);
                Require(tex!=null && tex.width==32 && tex.height==32,"Missing original shadow "+i);
            }
            actor=new GameObject("BuffLifecycleCheck");
            var buffs=actor.AddComponent<NativeBuffs>();
            double now=NativeBuffs.Now;
            Require(buffs.ApplySkill(1001003,1,now),"Iron Body rejected");
            Require(buffs.ApplySkill(1101006,1,now),"Rage rejected");
            Require(buffs.Value("WATK")==3 && buffs.Value("WDEF")==2,"Shared weaker stat overwrote stronger");
            Require(buffs.ApplySkill(1001,1,now),"Recovery rejected");
            Require(buffs.Visible.Count==3,"Independent buffs failed coexistence");
            Require(buffs.ApplySkill(1001,1,now),"Recovery refresh rejected");
            Require(buffs.Visible.Count==3,"Refresh duplicated status icon");
            buffs.Cancel(1001003);
            Require(buffs.Value("WDEF")==-3,"Suppressed source not restored");
            buffs.Tick(now+31);
            Require(buffs.Visible.Count==1 && buffs.Value("RECOVERY")==0,"Expiry did not remove status");
            var effect=buffs.Visible[0];
            Require(effect.Shadow(now)==15 && effect.Shadow(now+33)==8,"Native 16-step timing");
            buffs.Clear(); Require(buffs.Visible.Count==0,"Dispel/death clear");
            Require(NativeBuffs.AttackSpeed(6,-2,-1)==3 && NativeBuffs.AttackSpeed(6,-2,-2)==2,"Booster/infusion stacking");
            Require(NativeBuffs.AttackSpeed(2,-2,-2)==2 && NativeBuffs.AttackSpeed(10,2,2)==10,"Attack speed limits");
            Require(NativeBuffs.AttackSpeed(6,0,0,4001334)==4,"Skill intrinsic attack speed");
            foreach (int id in new[]{4101005,5111004,15111001,14101006})
            {
                Require(SkillSystem.DrainRecovery(id,101,1000,101,33)==33,"Drain integer truncation");
                Require(SkillSystem.DrainRecovery(id,1000,20,101,100)==20,"Drain monster HP cap");
                Require(SkillSystem.DrainRecovery(id,1000,1000,101,100)==50,"Drain player half HP cap");
            }
            Require(SkillSystem.DrainRecovery(1000,1000,1000,100,100)==0,"Drain applied to unrelated skill");
            Require(PlayerCtrl.MovementPercent(10,20,40,140)==140,"Walk equipment/passive/buff limit");
            Require(PlayerCtrl.MovementPercent(-20,0,10,140)==110,"Native equipment base lower limit/order");
            Require(PlayerCtrl.MovementPercent(10,0,40,123)==123,"Jump upper limit");
            Require(PlayerCtrl.MovementPercent(10,20,40,140,80)==80,"Slow must override haste");
            Require(PlayerCtrl.MovementPercent(0,0,-100,140)==80,"Ordinary movement lower limit");
            Require(NativeBuffs.PowerGuardDamage(101,40,10000,false)==40 && NativeBuffs.PowerGuardDamage(101,40,10000,true)==20,"Power Guard boss half rate/truncation");
            Require(NativeBuffs.PowerGuardDamage(1000,40,109,false)==10,"Power Guard monster HP cap");
            Require(NativeBuffs.PassiveIncoming(101,900,945,0)==85,"Barrier/Achilles sequential truncation");
            Require(NativeBuffs.PassiveIncoming(101,0,0,850)==101,"Source High Defense ceil semantics changed");
            Require(FieldDirector.FieldProtection(211010000,211000000,1,false),"Cold food protection");
            Require(!FieldDirector.FieldProtection(200082300,211000000,1,true),"Cold protection leaked to Orbis tower bottom");
            Require(FieldDirector.FieldProtection(200082300,211000000,2,false),"Air bubble tower protection");
            Require(FieldDirector.FieldProtection(230010000,230000000,2,false),"Air bubble underwater protection");
            Require(FieldDirector.FieldProtection(211010000,211000000,0,true),"Cold equipment protection");
            Require(!FieldDirector.FieldProtection(211010000,211000000,2,true),"Source protection precedence");
            Require(FieldDirector.IsEnvironmentalEquipment(new InventoryEntry {inventoryType=InventoryDao.Equipped,position=-5}),"Equipped protection item ignored");
            Require(!FieldDirector.IsEnvironmentalEquipment(new InventoryEntry {inventoryType=InventoryDao.Equip,position=5}),"Unequipped protection item applied");
            buffs.ApplySkill(1001003,1,now);
            buffs.ApplyItem(2020022,new Dictionary<string,int>{{"WATK",5}},1800000,now);
            buffs.Dispel();
            Require(!buffs.HasSource(1001003) && buffs.HasSource(-2020022),"Monster dispel removed item buffs");
            buffs.Clear();
            buffs.ApplySkill(1101006,1,now);
            buffs.ApplyItem(2020022,new Dictionary<string,int>{{"WATK",5}},1800000,now);
            Require(buffs.Value("WATK")==5,"Item/skill priority diverged");
            Require(buffs.CancelByUser(-2020022) && buffs.Value("WATK")==3,"Item cancellation/fallback");
            buffs.Clear();
            buffs.ApplySkill(1101004,1,now); buffs.ApplySkill(1101005,1,now);
            Require(!buffs.HasSource(1101004) && buffs.HasSource(1101005),"Singleton booster retained previous source");
            buffs.Cancel(1101005); Require(buffs.Value("BOOSTER")==0,"Singleton incorrectly restored cancelled source");
            var permanent=new NativeBuffs.Effect { DurationMs=int.MaxValue,Until=now+int.MaxValue/1000.0 };
            NativeBuffs.GuardLoss(101,100,100,80,50,true,true,0,out int h,out int m,out int g,out bool cancel);
            Require(h==21 && m==80 && g==0 && !cancel,"Magic Guard priority/truncation");
            NativeBuffs.GuardLoss(101,30,100,80,50,true,true,0,out h,out m,out g,out cancel);
            Require(h==71 && m==30 && g==0,"Magic Guard insufficient MP spillover");
            NativeBuffs.GuardLoss(101,30,24,80,50,false,true,0,out h,out m,out g,out cancel);
            Require(h==50 && g==24 && cancel,"Meso Guard integer division/insufficient funds");
            NativeBuffs.GuardLoss(101,30,25,80,50,false,true,0,out h,out m,out g,out cancel);
            Require(h==50 && g==25 && !cancel,"Meso Guard exact funds cancellation");
            NativeBuffs.GuardLoss(101,30,100,80,50,true,true,5,out h,out m,out g,out cancel);
            Require(h==50 && m==5 && g==25,"MP attack must bypass Magic Guard");
            Require(permanent.RemainingMs(now)>int.MaxValue-2 && permanent.Shadow(now)==15,"Permanent duration overflow");
            Require(TemporaryStatView.Slot(0,2,800)==new Rect(733,23,32,32),"Original first slot position");
            Require(TemporaryStatView.Slot(1,2,800)==new Rect(765,23,32,32),"Original right edge");
            Debug.Log("[NativeBuffChecks] PASS "+levels+" levels / "+buffLevels+" status levels / "+keys.Count+" status keys; original icons + 16 masks; coexistence, priority, refresh, fallback, expiry, clear, layout");
            if (renderAfter) NativeBuffRenderChecks.Run();
            else EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally { if (actor!=null) UnityEngine.Object.DestroyImmediate(actor); }
    }
}
