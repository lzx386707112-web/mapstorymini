using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NativeBuffRenderChecks
{
    public static void Run()
    {
        SessionState.SetBool("NativeBuffRenderChecks",true);
        HudChecks.SetGameViewSize(1280,720);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update+=Tick; }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || !SessionState.GetBool("NativeBuffRenderChecks",false)) return;
        SessionState.SetBool("NativeBuffRenderChecks",false);
        new GameObject("BuffRenderProbe").AddComponent<NativeBuffRenderProbe>();
    }
}
public sealed class NativeBuffRenderProbe : MonoBehaviour
{
    bool draw;
    static void Require(bool ok,string message) { if (!ok) throw new Exception(message); }
    void OnGUI() { if (draw) { UiKit.Begin(); TemporaryStatView.Draw(); } }
    IEnumerator Start()
    {
        var routine=Check();
        while (true)
        {
            object next;
            try { if (!routine.MoveNext()) break; next=routine.Current; }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); yield break; }
            yield return next;
        }
        Debug.Log("[NativeBuffRenderChecks] PASS actual skill casts, recovery tick, paired max HP/MP, original HUD rendering, right-click cancellation, dispel and death");
        EditorApplication.Exit(0);
    }
    IEnumerator Check()
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        typeof(Profile).GetField("data",flags).SetValue(null,new PlayerProfileData());
        typeof(Profile).GetField("debugOverride",flags).SetValue(null,true);
        var gameObject=new GameObject("InactiveBuffGame"); gameObject.SetActive(false);
        var game=gameObject.AddComponent<GameLoop>(); GameLoop.I=game; game.state=GameLoop.GameState.Playing;
        var actor=new GameObject("BuffPlayer"); actor.AddComponent<SpriteRenderer>();
        var player=actor.AddComponent<PlayerCtrl>(); player.enabled=false;
        player.hp=20; player.mp=1000;
        PlayerCtrl.SetTerrain(new List<Vector4>{new Vector4(-100,0,100,0)});
        player.PlaceAt(0,0); game.player=player;
        var system=actor.AddComponent<SkillSystem>();
        new GameObject("BuffHitFeelRunner").AddComponent<HitFeelRunner>();
        HitFeel.Reset();
        PlayerCtrl.InputLocked=false;
        yield return null; // First PlayMode Start can have deltaTime=0; the mob scheduler intentionally pauses then.
        NativeProjectileChecks.Check();
        string previousWeapon=Profile.Data.equipWeapon;
        int previousLevel=Profile.Data.level;Profile.Data.level=120;
        Profile.Data.equipWeapon="1452000";
        typeof(PlayerCtrl).GetField("weapon",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(player,WeaponDefs.Get("1452000"));
        Profile.Data.SetSkillLevel(3001005,1);
        player.mp=1000;
        Require(!SkillCast.TryCast(3001005,out _) && player.mp==1000,"Missing arrows must reject real skill entry before MP mutation");
        Profile.AddItem(2060000,3);
        Require(SkillCast.TryCast(3001005,out var arrowFailure) && Profile.ItemCount(2060000)==1,"Double Shot must consume original two arrows: "+arrowFailure);
        float arrowMp=player.mp;system.CancelActions();
        Require(!SkillCast.TryCast(3001005,out _) && player.mp==arrowMp && Profile.ItemCount(2060000)==1,
            "Insufficient single-stack bullets must preserve MP and arrows");
        typeof(PlayerCtrl).GetMethod("SwingEmpty",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(player,null);
        Require(Profile.ItemCount(2060000)==0,"Basic empty ranged attack must consume last arrow once");
        Require(NativeBuffs.For(player).ApplySkill(3101004,1,NativeBuffs.Now),"Soul Arrow test registration");
        Require(SkillCast.TryCast(3001005,out _),"Soul Arrow must allow actual skill entry with no arrows");
        system.CancelActions();NativeBuffs.For(player).Clear();
        Profile.Data.equipWeapon=previousWeapon;
        typeof(PlayerCtrl).GetField("weapon",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(player,WeaponDefs.Get(previousWeapon));
        player.hp=20;player.mp=1000;
        foreach (int crash in new[]{1111007,1211009,1311007})
        {
            Require(!SkillBook.Get(crash).IsPassive && SkillBook.Native(crash,1).Kind=="Dispel","Crash must enter shared monster-status path "+crash);
            Profile.Data.SetSkillLevel(crash,1);
            Require(SkillCast.TryCast(crash,out var crashFailure),"Crash active entry failed "+crash+": "+crashFailure);
            system.CancelActions();
        }
        foreach (int taunt in new[]{4121003,4221003})
        {
            Require(!SkillBook.Get(taunt).IsBuff && SkillBook.Native(taunt,1).Kind=="Attack","Taunt must target monsters "+taunt);
            Profile.Data.SetSkillLevel(taunt,1);
            Require(SkillCast.TryCast(taunt,out var tauntFailure),"Taunt active entry failed "+taunt+": "+tauntFailure);
            system.CancelActions();
        }
        CheckContinuousSkills(system,player);
        CheckFlashJump(system,player);
        CheckMobLifecycle(system,player);
        var camera=new GameObject("BuffCamera").AddComponent<Camera>(); camera.tag="MainCamera";
        camera.orthographic=true; camera.orthographicSize=6; camera.transform.position=new Vector3(0,1,-10);
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.clear;
        var buffs=NativeBuffs.For(player);
        var diseases=NativeMobDebuffs.For(player);
        buffs.ApplySkill(4101004,20,NativeBuffs.Now);
        player.RefreshMovementAttributes();
        Require(Mathf.Abs(player.moveSpeed-125f/Art.PPU*1.4f)<.0001f &&
            Mathf.Abs(player.jumpSpeed-555f/Art.PPU*1.2f)<.0001f,"Haste did not reach movement parameters");
        diseases.Apply(126,80,0,10000); player.RefreshMovementAttributes();
        Require(Mathf.Abs(player.moveSpeed-125f/Art.PPU*.8f)<.0001f,"Slow ignored or multiplied haste");
        diseases.Clear(); buffs.Clear(); player.RefreshMovementAttributes();
        Require(Mathf.Abs(player.moveSpeed-125f/Art.PPU)<.0001f,"Cancelled haste/slow retained movement multiplier");
        int movementJob=Profile.Data.jobId;
        Profile.Data.jobId=311; Profile.Data.SetSkillLevel(3110000,1);
        player.RefreshMovementAttributes();
        Require(Mathf.Abs(player.moveSpeed-125f/Art.PPU*1.02f)<.0001f,"Thrust speed field not applied");
        Profile.Data.jobId=movementJob; Profile.Data.SetSkillLevel(3110000,0); player.RefreshMovementAttributes();
        Require(buffs.ApplySkill(2321005,1,NativeBuffs.Now),"Holy Shield registration");
        diseases.Apply(120,1,0,10000);
        Require(!diseases.Has(120),"Holy Shield failed to prevent seal");
        diseases.Apply(123,1,0,10000); diseases.Apply(128,1,0,10000);
        Require(diseases.Has(123) && diseases.Has(128),"Holy Shield incorrectly blocked stun/seduce");
        diseases.Clear(); buffs.Clear();
        diseases.Apply(120,1,0,10000);
        Require(diseases.Has(120),"Cancelled Holy Shield still prevents disease");
        diseases.Clear();
        diseases.Apply(123,1,0,10000); diseases.Apply(133,1,0,10000);
        diseases.Purge(); Require(diseases.Has(123) && !diseases.Has(133),"Hero's Will must preserve stun");
        diseases.Clear();
        diseases.Apply(125,1,0,10000); diseases.Apply(128,1,0,10000);
        diseases.Dispel(); Require(!diseases.Has(125) && diseases.Has(128),"Dispel removed seduce");
        diseases.Clear();
        player.hpMax=100; player.mpMax=100; player.hp=80; player.mp=0;
        Profile.Data.SetSkillLevel(14100005,1);
        Require(buffs.ApplySkill(4001003,1,NativeBuffs.Now) && buffs.ApplySkill(14001003,1,NativeBuffs.Now),"Stacked Dark Sight registration");
        buffs.OnRangedAttack(0);
        Require(buffs.HasStat("DARKSIGHT"),"Empty ranged attack must retain Dark Sight");
        buffs.OnRangedAttack(1);
        Require(!buffs.HasStat("DARKSIGHT"),"Ranged hit must remove winning and suppressed Dark Sight");
        Profile.Data.SetSkillLevel(13101006,1);
        Require(buffs.ApplySkill(13101006,1,NativeBuffs.Now),"Wind Walk registration");
        buffs.OnRangedAttack(1);
        Require(!buffs.HasStat("WIND_WALK"),"Ranged hit must remove Wind Walk");
        Require(buffs.ApplySkill(4001003,1,NativeBuffs.Now),"Close Dark Sight registration");
        Profile.Data.SetSkillLevel(14100005,0); Profile.Data.SetSkillLevel(4001003,1);
        buffs.OnRangedAttack(1);
        Require(buffs.HasStat("DARKSIGHT"),"Ranged route must retain ordinary Rogue Dark Sight without Vanish");
        buffs.OnCloseAttack();
        Require(!buffs.HasStat("DARKSIGHT"),"Close empty attack must cancel learned Rogue Dark Sight");
        buffs.Clear(); player.hpMax=100; player.mpMax=100; player.hp=80; player.mp=0;
        Profile.Data.SetSkillLevel(4121006,1);
        player.mp=1000;
        Require(!system.TryCastSkill(4121006) && player.mp==1000,"Shadow Stars missing projectiles must reject without MP mutation");
        int starsRequired=Art.Int(SkillBook.Get(4121006).Level(1),"bulletConsume");
        Profile.Data.AddItem(2070000,starsRequired+1);
        Require(system.TryCastSkill(4121006) && Profile.ItemCount(2070000)==1,"Shadow Stars initial projectile consumption");
        yield return new WaitForSeconds(2);
        buffs.Clear(); player.hpMax=100; player.mpMax=100; player.hp=80; player.mp=100;
        Profile.Data.SetSkillLevel(2301002,1);
        player.mp=100;
        Require(SkillCast.TryCast(2301002,out var healFailure),"Heal UI entry rejected as passive: "+healFailure);
        Require(player.hp==90 && player.mp==88,"Heal level HP percentage/MP consumption mismatch");
        yield return new WaitForSeconds(2);
        Profile.Data.SetSkillLevel(4111004,1);
        player.mp=1000; Profile.Data.gold=1000;
        Require(system.TryCastSkill(4111004),"Shadow Meso cast rejected: "+system.LastFailure);
        Require(Profile.gold<=865 && Profile.gold>=799,"Shadow Meso random moneyCon range");
        yield return new WaitForSeconds(2);
        Profile.Data.gold=5;
        Require(system.TryCastSkill(4111004) && Profile.gold==0,"Shadow Meso must clamp cost to available mesos");
        yield return new WaitForSeconds(2);
        player.hp=80; player.mp=0;
        Profile.Data.SetSkillLevel(5101005,1);
        Require(SkillCast.TryCast(5101005,out var instantFailure),"MP Recovery UI entry rejected as passive: "+instantFailure);
        Require(player.hp==70 && player.mp==5,"MP Recovery lost HP/MP conversion precision");
        yield return new WaitForSeconds(2);
        Profile.Data.SetSkillLevel(2311002,1);
        player.hp=100; player.mp=1000;
        Require(!system.TryCastSkill(2311002),"Magic rock requirement ignored");
        Require(player.hp==100 && player.mp==1000,"Rejected item cost changed HP/MP");
        Profile.AddItem(4006000,2);
        Require(system.TryCastSkill(2311002),"Item rejection incorrectly started cooldown/action: "+system.LastFailure);
        Require(Profile.ItemCount(4006000)==1,"Primary cast did not consume exactly one magic rock");
        Require(!system.TryCastSkill(2311002) && Profile.ItemCount(4006000)==1,"Rejected repeated cast consumed another rock");
        yield return new WaitForSeconds(2);
        buffs.Clear();
        buffs.ApplySkill(1311008,1,NativeBuffs.Now);
        player.hpMax=100; player.hp=90;
        buffs.TickPeriodic(player,NativeBuffs.Now+4.01);
        Require(player.hp==42,"Dragon Blood must drain x every four seconds");
        buffs.Clear();
        SkillProgress.SetLevel(1320008,1); SkillProgress.SetLevel(1320009,1);
        Require(buffs.ApplySkill(1321007,1,NativeBuffs.Now),"Beholder registration");
        player.hpMax=500; player.hp=1;
        int interval=Art.Int(SkillBook.Get(1320008).Level(1),"x");
        buffs.TickPeriodic(player,NativeBuffs.Now+interval+.01);
        Require(player.hp==Mathf.Min(500,1+Art.Int(SkillBook.Get(1320008).Level(1),"hp")),"Beholder healing task");
        buffs.Clear();
        player.hp=20; player.mp=1000;
        foreach (int id in new[]{1001,1001003,1301007})
        {
            Profile.Data.SetSkillLevel(id,1); player.mp=1000;
            Require(system.TryCastSkill(id),"Actual cast failed "+id+": "+system.LastFailure);
            Require(buffs.HasSource(id),"Cast did not register status "+id);
            yield return new WaitForSeconds(2);
        }
        Require(player.hp>=24,"Recovery icon had no periodic healing");
        Require(player.hpMax==51 && player.mpMax==5,"Hyper Body stat application/truncation");
        Require(buffs.Visible.Count==3,"Actual cast icon count");
        draw=true;
        Directory.CreateDirectory("docs/buff-regression");
        foreach (var size in new[]{new Vector2Int(800,600),new Vector2Int(1280,720)})
        {
            HudChecks.SetGameViewSize(size.x,size.y);
            yield return null; yield return null;
            Pointer.Pos=new Vector2(-100,-100);
            yield return new WaitForEndOfFrame();
            var shot=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("docs/buff-regression/status-"+size.x+"x"+size.y+".png",shot.EncodeToPNG());
            // The native bitmaps must reach the final framebuffer in the status row.
            int pixels=0;
            for (int y=0;y<shot.height;y++) for (int x=Math.Max(0,shot.width-150);x<shot.width;x++)
            {
                var c=shot.GetPixel(x,y); if (c.r+c.g+c.b>.3f) pixels++;
            }
            Require(pixels>100,"Status bar did not render to framebuffer");
            Destroy(shot);
        }
        UiKit.RefreshMetrics();
        Pointer.Pos=TemporaryStatView.Slot(0,buffs.Visible.Count,UiKit.W).center;
        yield return new WaitForEndOfFrame();
        var tooltipShot=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("docs/buff-regression/skill-tooltip.png",tooltipShot.EncodeToPNG());
        Destroy(tooltipShot);
        Pointer.RightPressed=true;
        yield return new WaitForEndOfFrame();
        Require(!buffs.HasSource(1001),"Right-click did not cancel selected status");
        player.DispelNativeBuffs(); Require(buffs.Visible.Count==0,"Dispel retained icons");
        buffs.ApplySkill(1001,1,NativeBuffs.Now); player.hp=0;
        yield return null; Require(buffs.Visible.Count==0,"Death retained icons");
    }
    static void CheckContinuousSkills(SkillSystem system,PlayerCtrl player)
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var weapon=typeof(PlayerCtrl).GetField("weapon",flags);
        var shot=typeof(SkillSystem).GetField("lastHeldShot",flags);
        var tick=typeof(SkillSystem).GetMethod("Update",flags);
        string previousWeapon=Profile.Data.equipWeapon;
        int previousLevel=Profile.Data.level;Profile.Data.level=120;
        int[] slots=new int[QuickSlots.Count];for(int i=0;i<slots.Length;i++)slots[i]=QuickSlots.Id(i);
        bool enabled=InputHub.Enabled;InputHub.Enabled=true;
        try
        {
            foreach(int id in new[]{3121004,5221004,13111002})
            {
                system.CancelActions();player.PlaceAt(0,0);player.FinishHeldSkill();player.mp=1000;
                Profile.Data.SetSkillLevel(id,1);
                string selected=id==5221004 ? "1492000" : "1452000";
                Profile.Data.equipWeapon=selected;weapon.SetValue(player,WeaponDefs.Get(selected));
                int ammo=id==5221004 ? 2330000 : 2060000;
                Profile.AddItem(ammo,10);
                int count=Profile.ItemCount(ammo);
                var binding=new int[QuickSlots.Count];binding[0]=id;QuickSlots.LoadCharacter(binding);
                InputHub.SetVirtualAction(GameAction.Slot1,true);
                Require(system.TryCastBoundSkill(id,GameAction.Slot1),"Continuous start rejected "+id+": "+system.LastFailure+" weapon="+Profile.equipWeapon+"/"+WeaponDefs.Get(Profile.equipWeapon).kind+" level="+Profile.level+" ammo="+Profile.ItemCount(ammo));
                Require(system.ContinuousSkillId==id && player.mp==1000 && Profile.ItemCount(ammo)==count,"Key-down start must not spend attack costs "+id);
                shot.SetValue(system,(long)(Time.realtimeSinceStartupAsDouble*1000)-101);
                tick.Invoke(system,null);
                int mp=SkillBook.Native(id,1).MpCost;
                Require(player.mp==1000-mp && Profile.ItemCount(ammo)==count-1,"Continuous shot did not spend once "+id);
                tick.Invoke(system,null);
                Require(player.mp==1000-mp && Profile.ItemCount(ammo)==count-1,"Continuous shot ignored 100ms gate "+id);
                InputHub.SetVirtualAction(GameAction.Slot1,false);tick.Invoke(system,null);
                Require(system.ContinuousSkillId==0 && player.mp==1000-mp,"Release must end without another attack "+id);
                player.FinishHeldSkill();
                InputHub.SetVirtualAction(GameAction.Slot1,true);
                Require(system.TryCastBoundSkill(id,GameAction.Slot1),"Continuous restart rejected "+id);
                player.mp=0;shot.SetValue(system,(long)(Time.realtimeSinceStartupAsDouble*1000)-101);
                tick.Invoke(system,null);
                Require(system.ContinuousSkillId==0 && player.mp==0,"Insufficient MP must finish held skill "+id);
                InputHub.SetVirtualAction(GameAction.Slot1,false);
                Profile.RemoveItem(ammo,Profile.ItemCount(ammo));
            }
        }
        finally
        {
            system.CancelActions();InputHub.SetVirtualAction(GameAction.Slot1,false);InputHub.Enabled=enabled;
            QuickSlots.LoadCharacter(slots);Profile.Data.equipWeapon=previousWeapon;Profile.Data.level=previousLevel;
            weapon.SetValue(player,WeaponDefs.Get(previousWeapon));player.mp=1000;
        }
    }

    static void CheckFlashJump(SkillSystem system,PlayerCtrl player)
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var vertical=typeof(PlayerCtrl).GetField("vy",flags);
        var horizontal=typeof(PlayerCtrl).GetField("horizontalVelocity",flags);
        var visual=(SpriteAnim)typeof(PlayerCtrl).GetField("vis",flags).GetValue(player);
        visual?.Play("stand",false,true);
        foreach (int id in new[]{4111006,14101004})
        {
            system.CancelActions();player.PlaceAt(0,0);player.mp=1000;
            Profile.Data.SetSkillLevel(id,20);
            Require(!SkillCast.TryCast(id,out _) && player.mp==1000,"Grounded flash jump must reject before MP "+id);
            player.grounded=false;player.jumpY=3;vertical.SetValue(player,-1f);
            Require(!SkillCast.TryCast(id,out _) && player.mp==1000,"Ordinary falling flash jump must reject "+id);
            vertical.SetValue(player,0f);horizontal.SetValue(player,0d);player.facing=-1;
            Require(SkillCast.TryCast(id,out var failure),"Airborne flash jump failed "+id+": "+failure);
            Require((double)horizontal.GetValue(player)==-550d && Math.Abs((float)vertical.GetValue(player)-350f/Art.PPU)<.00001f,
                "Flash jump level/facing/native impulse "+id);
            float after=player.mp;
            Require(!SkillCast.TryCast(id,out _) && player.mp==after,"Second airborne flash jump must reject without MP "+id);
            // A real landing, rather than manually resetting the private used flag.
            player.jumpY=.01f;vertical.SetValue(player,-1f);
            typeof(PlayerCtrl).GetMethod("StepMovement",flags).Invoke(player,new object[]{0f,false,.02f});
            Require(player.grounded,"Flash jump landing fixture failed");
            player.grounded=false;vertical.SetValue(player,1f);
            Require(player.CanFlashJump,"Landing did not reset native flash jump flag "+id);
        }
        system.CancelActions();player.PlaceAt(0,0);visual?.Play("stand",false,true);player.mp=1000;
        Profile.Data.SetSkillLevel(11101005,10);
        var deadline=typeof(PlayerCtrl).GetField("soulRushUntil",flags);
        deadline.SetValue(player,0d);
        var diseases=NativeMobDebuffs.For(player);diseases.Clear();
        diseases.Apply(122,1,0,10000);
        Require(!SkillCast.TryCast(11101005,out _) && player.mp==1000,"Soul Rush must reject WEAKEN before MP");
        diseases.Clear();player.grounded=false;
        Require(!SkillCast.TryCast(11101005,out _) && player.mp==1000,"Soul Rush requires a foothold");
        player.PlaceAt(0,0);horizontal.SetValue(player,0d);vertical.SetValue(player,0f);player.facing=1;
        Require(SkillCast.TryCast(11101005,out var soulFailure),"Ground Soul Rush failed: "+soulFailure);
        Require(!player.grounded && (double)horizontal.GetValue(player)==550d &&
            Math.Abs((float)vertical.GetValue(player)-350f/Art.PPU)<.00001f,"Soul Rush double-level impulse and support detach");
        float soulMp=player.mp;
        player.jumpY=.01f;vertical.SetValue(player,-1f);
        typeof(PlayerCtrl).GetMethod("StepMovement",flags).Invoke(player,new object[]{0f,false,.02f});
        visual?.Play("stand",false,true);
        Require(player.grounded && !SkillCast.TryCast(11101005,out _) && player.mp==soulMp,"Soul Rush independent 1500ms gate after landing");
        deadline.SetValue(player,Time.realtimeSinceStartupAsDouble-1);
        Require(SkillCast.TryCast(11101005,out soulFailure),"Soul Rush deadline expiry did not admit: "+soulFailure);
        system.CancelActions();player.PlaceAt(0,0);player.facing=1;
    }

    static void CheckMobLifecycle(SkillSystem system,PlayerCtrl player)
    {
        var fieldObject=new GameObject("MobLifecycleField");
        var field=fieldObject.AddComponent<FieldDirector>(); field.enabled=false;
        typeof(FieldDirector).GetField("<Map>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance)
            .SetValue(field,new FieldMap { id=100000000 });
        var enemy=field.SpawnNativeMob(2220000,Vector3.zero);
        Require(enemy!=null,"Lifecycle fixture mob unavailable"); enemy.enabled=false;
        var control=enemy.NativeController; control.enabled=false;
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var state=(NativeMobCastState)typeof(NativeMobController).GetField("state",flags).GetValue(control);
        int maximum=NativeCombatData.Int(enemy.CombatStats,"maxMP");
        Require(maximum>0,"Lifecycle fixture needs MP");
        var ownRoot=WzAssets.FileRoot("Mob/2220000.img");
        var sourceArt=WzAssets.MobArtRoot(2220000);
        var chaseFixture=new MapleDash.Wz.ImgNode {Name="chaseFixture"};
        chaseFixture.Children.Add(new MapleDash.Wz.ImgNode {Name="chase",Parent=chaseFixture});
        var speedFixture=new MapleDash.Wz.ImgNode {Name="speedFixture"};
        var infoFixture=new MapleDash.Wz.ImgNode {Name="info",Parent=speedFixture};speedFixture.Children.Add(infoFixture);
        infoFixture.Children.Add(new MapleDash.Wz.ImgNode {Name="speed",Value=-50,Parent=infoFixture});
        infoFixture.Children.Add(new MapleDash.Wz.ImgNode {Name="chaseSpeed",Value=40,Parent=infoFixture});
        var rootField=typeof(NativeMobController).GetField("root",flags);
        var artField=typeof(NativeMobController).GetField("artRoot",flags);
        try
        {
            rootField.SetValue(control,speedFixture);artField.SetValue(control,chaseFixture);
            double basis=NativeMovementPhysics.Load().WalkSpeed/Art.PPU;
            Require(Math.Abs(control.GroundMoveSpeed(false)-basis*.5)<.00001 &&
                Math.Abs(control.GroundMoveSpeed(true)-basis*1.4)<.00001,"Ground chase must select own-file chaseSpeed only with a target");
            artField.SetValue(control,new MapleDash.Wz.ImgNode {Name="noChase"});
            Require(Math.Abs(control.GroundMoveSpeed(true)-basis*.5)<.00001,"Missing chase animation must keep ordinary speed");
        }
        finally {rootField.SetValue(control,ownRoot);artField.SetValue(control,sourceArt);}

        Require(state.TryUse("probe",0,maximum,0,false),"Cannot drain mob MP");
        typeof(NativeMobController).GetField("nextMpRecovery",flags).SetValue(control,0L);
        typeof(NativeMobController).GetField("busyUntil",flags).SetValue(control,long.MaxValue);
        typeof(NativeMobController).GetMethod("Update",flags).Invoke(control,null);
        Require(state.Mp==Math.Min(maximum,NativeCombatData.Int(enemy.CombatStats,"level",1)),"Mob MP recovery does not use level");
        // The real AI selector must still cast an unrestricted skill below full HP.
        state.HealMp(maximum,maximum);control.SetHpPercent(50);
        typeof(NativeMobController).GetField("blockedUntil",flags).SetValue(control,0L);
        typeof(NativeMobController).GetField("busyUntil",flags).SetValue(control,0L);
        typeof(NativeMobController).GetField("nextDecision",flags).SetValue(control,0L);
        typeof(NativeMobController).GetMethod("Update",flags).Invoke(control,null);
        Require(control.Busy,"Damaged monster AI failed to select an hp=100 skill; mp="+state.Mp+"/"+maximum+" hp="+player.hp+" locked="+PlayerCtrl.InputLocked);
        int beforeMp=state.Mp;
        Require(control.AbsorbMp(100)==0 && state.Mp==beforeMp,"MP Eater absorbed from boss");
        typeof(NativeMobController).GetField("destructionHp",flags).SetValue(control,10);
        typeof(NativeMobController).GetField("destructionAction",flags).SetValue(control,2);
        control.SetHpPercent(100);
        control.ApplyDamage((int)Math.Min(int.MaxValue,control.CurrentHp-10));
        Require(control.CurrentHp==0 && control.DeathAction==2,"Self destruction threshold/action ignored");
        long exp=Profile.experience;
        control.Expire(2);
        var active=(List<EnemyUnit>)typeof(FieldDirector).GetField("dynamicMobs",flags).GetValue(field);
        Require(enemy.dead && !EnemyUnit.All.Contains(enemy) && !active.Contains(enemy),"Expiry failed to release owning field");
        Require(Profile.experience==exp && control.DeathAction==2,"Expiry granted rewards or lost death action");
        UnityEngine.Object.DestroyImmediate(enemy.gameObject);
        var victim=field.SpawnNativeMob(100100,Vector3.zero); victim.NativeController.enabled=false;
        typeof(NativeMobController).GetField("health",flags).SetValue(victim.NativeController,new NativeMobHealth(1000));
        victim.hp=victim.hpMax=1000;
        var row=new Dictionary<string,object>(SkillBook.Get(4101005).Level(1));
        row["level"]=1; row["fixdamage"]=100; row["attackCount"]=1; row["mobCount"]=1;
        var definition=new NativeSkillDefinition(new Dictionary<string,object>{{"id",4101005},{"kind","Attack"},{"magic",false}},row);
        var cast=new SkillDef(new ClassSkill {art="4101005",name="Drain"},definition);
        float previousHp=player.hp,previousMax=player.hpMax;
        player.hpMax=1000; player.hp=1;
        Require((int)typeof(SkillSystem).GetMethod("HitAt",flags).Invoke(system,
            new object[]{cast,definition,Vector2.zero,1,null,victim})==1,"Drain did not hit fixture");
        Require(player.hp==1+SkillSystem.DrainRecovery(4101005,100,1000,1000,Art.Int(row,"x")),"Actual hit failed to recover drain HP");
        foreach (int taunt in new[]{4121003,4221003})
        {
            var tauntRow=new Dictionary<string,object>(SkillBook.Get(taunt).Level(1));
            tauntRow["level"]=1;tauntRow["fixdamage"]=1;tauntRow["mobCount"]=1;
            var tauntDef=new NativeSkillDefinition(new Dictionary<string,object>{{"id",taunt},{"kind","Attack"},{"magic",false}},tauntRow);
            var tauntCast=new SkillDef(new ClassSkill {art=taunt.ToString(),name="Taunt probe"},tauntDef);
            typeof(SkillSystem).GetMethod("HitAt",flags).Invoke(system,new object[]{tauntCast,tauntDef,Vector2.zero,1,null,victim});
            Require(victim.NativeController.Showdown==Art.Int(tauntRow,"x"),"Actual Taunt hit failed to register reward status "+taunt);
        }
        var selection=victim.SelectionBounds;
        Require(selection.HasValue && selection.Value.xMin<victim.transform.position.x,"Original monster bounding box fixture");
        var edge=selection.Value;
        float edgeRight=Mathf.Min(edge.xMin+.05f,victim.transform.position.x-.001f);
        var edgeRow=new Dictionary<string,object>{{"level",1},{"fixdamage",1},{"mobCount",1},{"attackCount",1},
            {"lt",new List<object>{-edgeRight*Art.PPU,-edge.yMax*Art.PPU}},
            {"rb",new List<object>{-edge.xMin*Art.PPU,-edge.yMin*Art.PPU}}};
        var edgeDef=new NativeSkillDefinition(new Dictionary<string,object>{{"id",1001004},{"kind","Attack"},{"magic",false}},edgeRow);
        var edgeCast=new SkillDef(new ClassSkill {art="1001004",name="BBox edge probe"},edgeDef);
        Require((int)typeof(SkillSystem).GetMethod("HitAt",flags).Invoke(system,
            new object[]{edgeCast,edgeDef,Vector2.zero,1,null,null})==1,"Skill touching the body must select a monster whose feet are outside the attack rectangle");
        player.hp=previousHp; player.hpMax=previousMax;
        UnityEngine.Object.DestroyImmediate(victim.gameObject);
        var manaVictim=field.SpawnNativeMob(3000002,Vector3.zero); manaVictim.NativeController.enabled=false;
        typeof(NativeMobController).GetField("health",flags).SetValue(manaVictim.NativeController,new NativeMobHealth(100000));
        manaVictim.hp=manaVictim.hpMax=100000;
        var magicRow=new Dictionary<string,object>{{"level",1},{"fixdamage",1},{"mobCount",1},{"attackCount",1}};
        var magic=new NativeSkillDefinition(new Dictionary<string,object>{{"id",2101004},{"kind","Attack"},{"magic",true}},magicRow);
        var magicCast=new SkillDef(new ClassSkill {art="2101004",name="MP Eater probe"},magic);
        int previousJob=Profile.Data.jobId; float previousMp=player.mp,previousMpMax=player.mpMax;
        var randomState=UnityEngine.Random.state;
        player.invulnUntil=0; player.hp=1000; player.hpMax=1000;
        var guard=NativeBuffs.For(player);
        Require(guard.ApplySkill(1101007,30,NativeBuffs.Now),"Power Guard fixture registration");
        long mobBefore=manaVictim.NativeController.CurrentHp;
        for (int n=0;n<20 && manaVictim.NativeController.CurrentHp==mobBefore;n++)
        {
            player.invulnUntil=0; player.hp=1000;
            player.TakeDamage(100,1,false,1,manaVictim);
        }
        Require(manaVictim.NativeController.CurrentHp<mobBefore,"Actual contact Power Guard did not reflect");
        guard.Clear(); player.invulnUntil=0; player.hp=previousHp; player.hpMax=previousMax;
        Profile.Data.jobId=210; Profile.Data.SetSkillLevel(2100000,20);
        player.mpMax=1000; player.mp=0; UnityEngine.Random.InitState(42);
        for (int n=0;n<100 && player.mp==0;n++)
            typeof(SkillSystem).GetMethod("HitAt",flags).Invoke(system,new object[]{magicCast,magic,Vector2.zero,1,null,manaVictim});
        int expected=NativeCombatData.Int(manaVictim.CombatStats,"maxMP")*Art.Int(SkillBook.Get(2100000).Level(20),"x")/100;
        Require(expected>0 && player.mp==expected,"Actual magic hit did not transfer mob MP");
        Require(manaVictim.NativeController.AbsorbMp(100)==NativeCombatData.Int(manaVictim.CombatStats,"maxMP")-expected,"MP Eater did not reduce monster MP");
        Require(manaVictim.NativeController.AbsorbMp(100)==0,"MP Eater generated MP from empty monster");
        Profile.Data.jobId=previousJob; Profile.Data.SetSkillLevel(2100000,0);
        player.mp=previousMp; player.mpMax=previousMpMax; UnityEngine.Random.state=randomState;
        UnityEngine.Object.DestroyImmediate(manaVictim.gameObject);
        UnityEngine.Object.DestroyImmediate(fieldObject);
    }
}
