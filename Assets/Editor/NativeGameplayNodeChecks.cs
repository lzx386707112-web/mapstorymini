using System;
using System.Collections.Generic;
using System.IO;
using MapleDash.Wz;
using UnityEditor;
using UnityEngine;

public static class NativeGameplayNodeChecks
{
    static void Require(bool ok, string label) => NativeSkillNodeChecks.Require(ok, label);
    public static void Run()
    {
        try { Check(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void CheckMapScripts()
    {
        var go=new GameObject("OriginalMapEntryScriptCheck");
        bool inputLock=PlayerCtrl.InputLocked;
        try
        {
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var field=go.AddComponent<FieldDirector>();field.enabled=false;
            var map=typeof(FieldDirector).GetField("<Map>k__BackingField",flags);
            var pending=typeof(FieldDirector).GetField("pendingWarpMap",flags);
            var index=typeof(FieldDirector).GetField("pendingWarpPortalIndex",flags);
            map.SetValue(field,new FieldMap {id=410007100,onUserEnter="karing_first"});
            var npc=NpcSession.Active;
            NativeMapScripts.Enter(field);
            var host=go.GetComponent<NativeMapScripts>();host.enabled=false;
            var timers=(NativeBossTimeline)typeof(NativeMapScripts).GetField("timers",flags).GetValue(host);
            Require((int)pending.GetValue(field)==-1,"Map script warp must wait for original delay");
            map.SetValue(field,new FieldMap {id=100000000});
            timers.Tick(long.MaxValue);
            Require((int)pending.GetValue(field)==-1,"Map script warped a player who left the expected map");
            map.SetValue(field,new FieldMap {id=410007100,onUserEnter="karing_first"});
            NativeMapScripts.Enter(field);timers.Tick(long.MaxValue);
            Require((int)pending.GetValue(field)==410007120 && (int)index.GetValue(field)==0,"Original karing_first.js did not schedule warp to portal 0");
            Require(NpcSession.Active==npc,"Map script took ownership of NPC conversation");
        }
        finally { UnityEngine.Object.DestroyImmediate(go);PlayerCtrl.InputLocked=inputLock;ScreenFade.Dismiss(); }
    }
    static void CheckMapIntro()
    {
        var timed=new NativeIntroTimeline.Command {Duration=10000};
        Require(timed.Displacement(722,5001)==361 && timed.Displacement(-722,5001)==-361,
            "Shape2D timed displacement must truncate signed pixel coordinates toward zero");
        Require(timed.Displacement(722,10001)==722 && timed.Displacement(722,-1)==0,
            "Shape2D timed displacement endpoints");
        var go=new GameObject("OriginalIntroCheck");int before=PlayerCtrl.ScriptUiLock;
        try
        {
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var field=go.AddComponent<FieldDirector>();field.enabled=false;
            typeof(FieldDirector).GetField("<Map>k__BackingField",flags).SetValue(field,new FieldMap {id=0});
            var pending=typeof(FieldDirector).GetField("pendingWarpMap",flags);
            var intro=go.AddComponent<NativeMapIntro>();intro.enabled=false;
            for(int gender=0;gender<2;gender++)
            {
                intro.Begin(field,"Effect/Direction3.img/goAdventure/Scene"+gender,true,1000);
                Require(PlayerCtrl.ScriptUiLock==before+1,"Intro must acquire UI lock only after validation");
                intro.Tick(1000);Require(go.GetComponentsInChildren<NativeImgAnimation>().Length==0,"Intro start equality must wait");
                intro.Tick(1001);var image=go.GetComponentInChildren<NativeImgAnimation>();
                Require(image!=null && image.Image.sprite!=null && image.Image.sprite.texture.width==800,"Original boy/girl intro canvas");
                intro.Tick(6001);
                Require(Math.Abs(image.transform.position.y-150/Art.PPU)<.001,"Intro displacement must add y1 to y, not use y1 as endpoint");
                intro.Tick(15200);Require((int)pending.GetValue(field)==-1,"Intro map command equality must wait");
                intro.Tick(15201);Require((int)pending.GetValue(field)==10000,"Intro original field warp");
                pending.SetValue(field,-1);
                var warp=typeof(FieldDirector).GetField("warpState",flags);
                warp.SetValue(field,Enum.ToObject(warp.FieldType,0));
                NativeMapIntro.Enter(field);
                Require(!NativeMapIntro.Active(field) && PlayerCtrl.ScriptUiLock==before && go.GetComponentsInChildren<NativeImgAnimation>().Length==0,
                    "Changing fields must clear intro sprites and its UI lock");
            }
            for(int gender=0;gender<2;gender++)
            {
                intro.Begin(field,"Effect/Direction3.img/goLith/Scene"+gender,true,1000);
                intro.Tick(1001);Require(go.GetComponentsInChildren<NativeImgAnimation>().Length==3,"goLith initial parallel visual commands");
                intro.Clear();
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(go);PlayerCtrl.ScriptUiLock=before;ScreenFade.Dismiss(); }
    }
    public static void Check()
    {
        var physics=NativeMovementPhysics.Load();
        var flightFixture=(Dictionary<string,object>)MiniJson.Parse(File.ReadAllText("tools/native-flight-oracle.json"));
        int flightCases=0;
        foreach (Dictionary<string,object> entry in (List<object>)flightFixture["cases"])
        {
            var input=(Dictionary<string,object>)entry["input"];var output=(Dictionary<string,object>)entry["output"];
            double vx=Convert.ToDouble(input["vx"]),vy=Convert.ToDouble(input["vy"]),oldX=vx,oldY=vy;
            double dt=Convert.ToDouble(input["ms"])*.001;
            physics.Flight(ref vx,ref vy,Convert.ToInt32(input["h"]),Convert.ToInt32(input["vertical_input"]),Convert.ToDouble(input["fly"]),dt);
            Require(Math.Abs(vx-Convert.ToDouble(output["velocity"]))<.0001 && Math.Abs(vy-Convert.ToDouble(output["vertical"]))<.0001 &&
                Math.Abs((oldX+vx)*dt*.5-Convert.ToDouble(output["position"]))<.0001 &&
                Math.Abs((oldY+vy)*dt*.5-Convert.ToDouble(output["y"]))<.0001,"Original x86 flight integrator case "+flightCases);
            flightCases++;
        }
        Require(flightCases==540,"Flight oracle coverage");
        Require(NativeMobMotion.AcquisitionRange(409,2) && !NativeMobMotion.AcquisitionRange(410,0) &&
            NativeMobMotion.AcquisitionRange(-10,-117) && !NativeMobMotion.AcquisitionRange(10,120),"Native integer acquisition geometry");
        var pursuit=new NativeMobPursuit();pursuit.Acquire(0);pursuit.Tick(180000,true);
        Require(pursuit.Active,"Target expiry must be strictly negative");pursuit.Acquire(59999);pursuit.Tick(1,true);
        Require(!pursuit.Active,"Repeated acquisition must not refresh target lifetime");
        pursuit.Acquire(59999);pursuit.Tick(1,false);Require(!pursuit.Active,"Dead target must release pursuit");
        var fixture = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText("tools/native_skill_nodes.json"));
        var branches = (Dictionary<string, object>)fixture["visualBranches"];
        var allSkills=new HashSet<int>();
        foreach (Dictionary<string,object> entry in (List<object>)fixture["cases"])
        {
            int id=Convert.ToInt32(entry["id"]),level=Convert.ToInt32(entry["level"]);
            var skill=SkillBook.Get(id);
            Require(skill!=null && skill.Level(level)!=null,"Original skill missing from runtime data "+id+"/"+level);
            allSkills.Add(id);
        }
        Require(allSkills.Count==802,"Original skill fixture coverage changed");
        Require(SkillBook.Get(21120004).name=="防守策略" && Art.Int(SkillBook.Get(21120004).Level(1),"x")==995,
            "Hidden High Defense must retain source name and level parameters");
        Require(SkillBook.Get(21121003).Level(1)!=null && SkillBook.Get(20000012).maxLevel==20,
            "Unexported Aran buff and beginner passive absent");
        Require(Art.Tex(SkillBook.Get(21120004).icon)!=null,"Fallback skill original icon unresolved");
        var debuffObject=new GameObject("MonsterDispelCheck");
        try
        {
            var controller=debuffObject.AddComponent<NativeMobController>();
            Require(SkillBook.Native(4121003,1).Kind=="Attack" && SkillBook.Native(4221003,1).Kind=="Attack","Taunt ranged attack routing");
            Require(!controller.ApplyShowdown(4121003,40,10000,true) && controller.Showdown==0,"Boss must reject Taunt");
            Require(!controller.ApplyShowdown(4111004,40,10000,false),"Unrelated skill cannot apply Showdown");
            Require(controller.ApplyShowdown(4121003,20,10000,false) && controller.Showdown==20,"Night Lord Showdown state");
            Require(controller.ApplyShowdown(4221003,40,10000,false) && controller.Showdown==40,"Shadower replaces Showdown");
            Require(controller.ApplyShowdown(4221003,40,-100000,false) && controller.Showdown==0,"Showdown expires");
            float expected=(float)(1.1f*1.4);
            Require(Server.Bot.BotPartyExperience.StatusMultiplier(50,false,false,40)==expected,"HS then Showdown float multiplication");
            Require(Server.Bot.BotPartyExperience.StatusMultiplier(50,true,false,40)==(float)(1.5f*1.4),"Party Showdown status multiplier");
            var buff=typeof(NativeMobController).GetMethod("Buff",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var original=new Dictionary<string,object>{{"PADamage",100},{"MADamage",100},{"PDDamage",100},{"MDDamage",100}};
            foreach (int skill in new[]{1111007,1211009,1311007,2311001,4111004})
            {
                foreach (int status in new[]{100,101,102,103,110,111,112,113,150,151,152,153})
                    buff.Invoke(controller,new object[]{status,200,100000L});
                controller.Dispel(skill);
                var result=controller.EffectiveStats(original);
                Require(Art.Int(result,"PADamage")== (skill==1311007 || skill==2311001 ? 100 : 200),"Crash physical attack status family "+skill);
                Require(Art.Int(result,"MADamage")== (skill==1211009 || skill==2311001 ? 100 : 200),"Crash magic attack status family "+skill);
                Require(Art.Int(result,"PDDamage")== (skill==1111007 || skill==2311001 || skill==4111004 ? 100 : 200),"Crash physical defense status family "+skill);
                Require(Art.Int(result,"MDDamage")== (skill==2311001 || skill==4111004 ? 100 : 200),"Dispel magic defense status family "+skill);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(debuffObject); }
        var geometry=new ImgNode {Name="GeometryFixture"};
        ImgNode Frame(string action,int left,int top,int right,int bottom)
        {
            var container=new ImgNode {Name=action,Parent=geometry};geometry.Children.Add(container);
            var frame=new ImgNode {Name="0",Kind="Canvas",Parent=container};container.Children.Add(frame);
            frame.Children.Add(new ImgNode {Name="lt",X=left,Y=top,Parent=frame});
            frame.Children.Add(new ImgNode {Name="rb",X=right,Y=bottom,Parent=frame});return frame;
        }
        Frame("stand",-10,-20,30,0);Frame("attack1",-30,-10,10,5);Frame("die1",-1000,-1000,1000,1000);
        var fallback=Frame("hit1",0,0,0,0);fallback.Children.Clear();fallback.Width=80;fallback.Height=50;
        fallback.Children.Add(new ImgNode {Name="origin",X=10,Y=40,Parent=fallback});
        var box=NativeTargetGeometry.Build(geometry);
        Require(box.HasValue && box.Value==Rect.MinMaxRect(-30,-40,70,10),"Source multi-action bbox merge, Canvas fallback and die exclusion");
        var worldBox=NativeTargetGeometry.World(box.Value,Vector2.zero,true);
        Require(Math.Abs(worldBox.xMin+70/Art.PPU)<.00001f && Math.Abs(worldBox.yMax-40/Art.PPU)<.00001f,
            "BBox world Y conversion and facing mirror");
        Require(NativeMobCastState.HpAllows(500,1000,100),"Unrestricted mob skill must remain selectable after taking damage");
        Require(NativeMobCastState.HpAllows(500,1000,50) && !NativeMobCastState.HpAllows(510,1000,50),"Mob low HP skill threshold direction/equality");
        Require(NativeMobCastState.HpAllows(509,1000,50) && NativeMobCastState.HpAllows(999,1000,0),"Mob HP percentage truncation and hp<=0 normalization");
        Require(NativeMobMotion.Multiplier(-200)==.1 && NativeMobMotion.Multiplier(-100)==.1 &&
            NativeMobMotion.Multiplier(-50)==.5 && NativeMobMotion.Multiplier(0)==1 &&
            Math.Abs(NativeMobMotion.Multiplier(100)-1.4)<1e-12,"Mob raw speed +100 and template/runtime clamps");
        Require(Math.Abs(NativeMobMotion.Multiplier(100,-100)-.4)<1e-12 && Math.Abs(NativeMobMotion.Multiplier(int.MaxValue,int.MaxValue)-1.4)<1e-12 &&
            NativeMobMotion.Multiplier(int.MinValue,int.MinValue)==.1,"Mob template must clamp before runtime correction without overflow");
        Require(!NativeHeldSkill.ShotDue(1100,1000) && NativeHeldSkill.ShotDue(1101,1000),"Continuous strict >100ms clock");
        Require(NativeHeldSkill.ReleaseElapsed(3121004,5000)==2000 && NativeHeldSkill.ReleaseElapsed(2121001,5000)==1000 &&
            NativeHeldSkill.ReleaseElapsed(2121001,0)==30 && NativeHeldSkill.ReleaseElapsed(1000,100)==0,"Native charge elapsed clamps/classification");
        CheckMapScripts();
        CheckMapIntro();
        int summons = 0;
        foreach (var raw in (List<object>)branches["summon"])
        {
            int id = Convert.ToInt32(raw);
            var root = NativeSummon.Root(id, 1);
            Require(root != null, "Missing summon " + id);
            int frames = 0;
            foreach (var action in root.Resolve().Children)
                foreach (var frame in WzAssets.Frames(action))
                {
                    Require(frame.Resolve().Kind == "Canvas", "Non-canvas summon frame " + id);
                    Require(WzAssets.Sprite(frame.Resolve()) != null, "Undecodable summon sprite " + id);
                    frames++;
                }
            Require(frames > 0, "Empty summon " + id);
            summons++;
        }
        var puppet = SkillBook.Native(3111002, 1);
        var hawk = SkillBook.Native(3111005, 1);
        var libra = SkillBook.Native(2321020, 1);
        Require(NativeSummon.Stationary(puppet.Id) && !NativeSummon.Stationary(hawk.Id), "Summon movement classification");
        Require(NativeSummon.AttackInfo(puppet) == null && !NativeSummon.AttackRange(puppet).HasValue,
            "Puppet borrowed an attacking summon range");
        Require(NativeSummon.AttackInfo(hawk) != NativeSummon.AttackInfo(libra), "Summon attack data still shared");
        Require(SkillBook.Native(2101004, 1).Element == "f", "Fire Arrow element lost on ID cast");
        Require(SkillBook.Native(2201004, 1).Element == "i", "Cold Beam element lost on ID cast");
        Require(CombatRules.ElementModifier(new Dictionary<string, object> { { "elemAttr", "F1I3" } }, "f") == 0 &&
            CombatRules.ElementModifier(new Dictionary<string, object> { { "elemAttr", "F1I3" } }, "i") == 1.5,
            "Elemental resistance/weakness mismatch");
        Require(new FieldPortal { tm = 0, type = 2 }.Usable, "Map zero portal rejected");
        Require(!new FieldPortal { tm = 999999999, type = 2 }.Usable, "Non-warp sentinel accepted");
        foreach (int id in new[] { 106021002, 910320100 })
        {
            var map = NativeMapView.LoadMap(new FieldMap { id = id });
            var info = WzAssets.FileRoot(WzAssets.MapPath(id)).Get("info");
            Require(map.returnMap == info.Int("returnMap") && map.fieldLimit == info.Int("fieldLimit") &&
                map.createMobInterval == info.Int("createMobInterval", 5000), "Requested field rules lost " + id);
            Require(map.footholds.Count > 0, "Linked geometry absent " + id);
            if (id == 106021002) Require(map.fieldLimit == 262216, "Linked fieldLimit replaced requested fieldLimit");
            if (id == 910320100) Require(map.createMobInterval == 3000, "Custom spawn interval replaced with default");
        }
        var linked = WzAssets.MobArtRoot(3000002);
        Require(linked == WzAssets.FileRoot("Mob/3000001.img"), "Mob art link did not resolve");
        Require(NativeMobController.AnimationMillis(linked.Get("move")) > 0, "Linked mob move timing absent");
        Require(WzAssets.FileRoot("Mob/3000002.img").Int("info/maxHP") == 800, "Mob link replaced local stats");
        var actionNode = new ImgNode { Name = "attack", Kind = "Property" };
        var frameNode = new ImgNode { Name = "0", Kind = "Canvas", Parent = actionNode };
        frameNode.Children.Add(new ImgNode { Name = "delay", Value = -90, Parent = frameNode });
        actionNode.Children.Add(frameNode);
        actionNode.Children.Add(new ImgNode { Name = "info", Kind = "Property", Parent = actionNode });
        Require(NativeMobController.AnimationMillis(actionNode) == 90, "Action delay sign/metadata handling");
        Require(NativeMobController.ServerMillis(actionNode)==-90,"Server delays must retain signed authored values");
        Require(NativeMobController.ServerMillis(WzAssets.FileRoot("Mob/3000002.img").Get("die1"))==0,
            "Linked mob server death timing must not borrow art file");
        var timed=NativeMapView.LoadMap(new FieldMap { id=211010000 });
        Require(timed.timeMobId==6090001 && timed.timeMobMessage=="雪山魔女的身影出现在暴风雪中！若想要平息魔女的怒火，需在祭坛放上战利品。",
            "Requested map timeMob id/message lost");
        Debug.Log("[NativeGameplayNodeChecks] PASS " + summons + " summon asset families, per-skill attack data, elements, map rules, map-zero portal, linked mob timing");
    }
}
