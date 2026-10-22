using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Exercises the actual player movement and touch code, not a duplicate physics model.
public static class MovementChecks
{
    static readonly MethodInfo Step = typeof(PlayerCtrl).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance);
    static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
    static void Tick(PlayerCtrl p, float h, bool jump, float dt)
    {
        TouchInput.MoveH = h;
        if (jump) TouchInput.PushJump();
        Step.Invoke(p, new object[] { dt });
    }
    static PlayerCtrl Create(List<Vector4> terrain, float x, float y)
    {
        PlayerCtrl.SetBounds(-20f,20f);
        PlayerCtrl.SetTerrain(terrain);
        PlayerCtrl.Climbs = null;
        TouchInput.Reset();
        var p = new GameObject("MovementCheck").AddComponent<PlayerCtrl>();
        typeof(PlayerCtrl).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(p,null);
        p.worldX=x; p.terrainY=y; p.jumpY=0f; p.grounded=true;
        return p;
    }
    public static void Run()
    {
        try
        {
            foreach (float dt in new[] {1f/30f,1f/60f,1f/120f})
            {
                var terrain = new List<Vector4> {new Vector4(-10,0,2,0),new Vector4(2,0,2,1.5f),new Vector4(2,1.5f,10,1.5f)};
                var p=Create(terrain,1.6f,0);
                for(int i=0;i<Mathf.CeilToInt(0.3f/dt);i++) Tick(p,1,false,dt);
                Require(p.worldX<2f,"walked through a 60px wall");
                Tick(p,1,true,dt);
                for(int i=0;i<Mathf.CeilToInt(0.8f/dt);i++) Tick(p,1,false,dt);
                Require(p.worldX>2f && p.grounded && Mathf.Abs(p.terrainY-1.5f)<0.01f,"failed to jump onto 60px step at "+dt);
                UnityEngine.Object.DestroyImmediate(p.gameObject);

                p=Create(new List<Vector4> {new Vector4(-10,0,1,0),new Vector4(1,-3,10,-3)},0.9f,0);
                for(int i=0;i<Mathf.CeilToInt(0.15f/dt);i++) Tick(p,1,false,dt);
                Require(!p.grounded && p.terrainY+p.jumpY<0f,"walked off ledge without falling");
                for(int i=0;i<Mathf.CeilToInt(0.7f/dt);i++) Tick(p,0,false,dt);
                Require(p.grounded && Mathf.Abs(p.terrainY+3)<0.01f,"missed lower platform");
                UnityEngine.Object.DestroyImmediate(p.gameObject);
            }
            // The actual harbor step is two consecutive 35px + 25px vertical WZ footholds.
            var harbor=Art.Stage("harbor");
            var native=Create(harbor.Footholds,harbor.WorldX(510),harbor.WorldY(215));
            for(int i=0;i<15;i++) Tick(native,-1,false,1f/60f);
            Require(native.worldX>harbor.WorldX(489),"walked through original harbor step");
            Tick(native,-1,true,1f/60f);
            for(int i=0;i<45;i++) Tick(native,-1,false,1f/60f);
            Require(native.worldX<harbor.WorldX(489) && native.grounded &&
                Mathf.Abs(native.terrainY-harbor.WorldY(155))<0.01f,"failed to jump onto original harbor step");
            UnityEngine.Object.DestroyImmediate(native.gameObject);
            var under=Create(new List<Vector4> {new Vector4(-10,0,10,0),new Vector4(1,3,5,3)},0.9f,0);
            for(int i=0;i<30;i++) Tick(under,1,false,1f/60f);
            Require(under.worldX>1.5f && under.grounded && under.terrainY==0,"overhead platform became invisible wall");
            UnityEngine.Object.DestroyImmediate(under.gameObject);
            CheckRush();
            CheckClimbs();
            CheckAllFootholds();
            TouchInput.Reset();
            CheckTouch();
            Debug.Log("[MovementChecks] PASS: native ropes and upper exits, all native foothold landings, 30/60/120Hz steps, ledges and touch ownership");
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void CheckRush()
    {
        foreach (float dt in new[]{1f/30,1f/60,1f/120})
        {
            var skill = NativeSkillCatalog.Get("dash");
            var p = Create(new List<Vector4>{new Vector4(-10,0,10,0)},0,0);
            p.facing = 1; p.BeginRush(skill,.5f);
            for (int i=0;i<Mathf.CeilToInt(.6f/dt);i++) Tick(p,0,false,dt);
            Require(Mathf.Abs(p.worldX-4)<.01f && p.grounded,"rush distance/frame independence " + dt);
            UnityEngine.Object.DestroyImmediate(p.gameObject);
            p = Create(new List<Vector4>{new Vector4(-10,0,2,0),new Vector4(2,0,2,3),new Vector4(2,3,10,3)},0,0);
            p.facing=1; p.BeginRush(skill,.5f);
            for (int i=0;i<Mathf.CeilToInt(.6f/dt);i++) Tick(p,0,false,dt);
            Require(p.worldX<2 && !p.IsRushing,"rush passed wall " + dt);
            UnityEngine.Object.DestroyImmediate(p.gameObject);
            p = Create(new List<Vector4>{new Vector4(-10,0,1,0),new Vector4(1,-3,10,-3)},0,0);
            p.facing=1; p.BeginRush(skill,.5f);
            for (int i=0;i<Mathf.CeilToInt(.3f/dt);i++) Tick(p,0,false,dt);
            Require(!p.grounded && p.FeetPos.y<0,"rush did not fall from ledge " + dt);
            p.CancelRush(); Require(!p.IsRushing,"rush cancel");
            UnityEngine.Object.DestroyImmediate(p.gameObject);
            p = Create(new List<Vector4>{new Vector4(-10,-2,10,2)},0,0);
            p.facing=-1; p.BeginRush(skill,.5f);
            for (int i=0;i<Mathf.CeilToInt(.6f/dt);i++) Tick(p,0,false,dt);
            Require(Mathf.Abs(p.worldX+4)<.01f && p.grounded && Mathf.Abs(p.FeetPos.y+.8f)<.01f,"rush slope/left facing " + dt);
            UnityEngine.Object.DestroyImmediate(p.gameObject);
        }
    }

    static void CheckClimbs()
    {
        foreach (string theme in new[]{"harbor","forest","cliff","nest"})
        {
            var stage = Art.Stage(theme);
            Require(stage.Climbs.Count > 0, "missing native ladderRope: " + theme);
            foreach (var rope in stage.Climbs)
            {
                var p = Create(stage.Footholds,rope.x,(rope.top+rope.bottom)*0.5f);
                PlayerCtrl.SetBounds(stage.WalkMin,stage.WalkMax);
                PlayerCtrl.Climbs=stage.Climbs;
                p.grounded=false;
                TouchInput.MoveV=1f;
                Tick(p,0,false,1f/60f);
                Require(p.IsClimbing && Mathf.Abs(p.worldX-rope.x)<0.001f,"failed native rope grab: "+theme);
                var animation=p.GetComponentInChildren<SpriteAnim>();
                Require(animation != null && animation.Current==(rope.ladder ? "ladder" : "rope"),"wrong climbing animation");
                float y=p.FeetPos.y;
                TouchInput.MoveV=0f;
                for(int i=0;i<30;i++) Tick(p,0,false,1f/60f);
                Require(p.IsClimbing && Mathf.Abs(y-p.FeetPos.y)<0.001f,"hanging drifted");
                Require(animation.speed==0f,"hanging animation did not pause");
                TouchInput.MoveV=-1f;
                Tick(p,0,false,0.1f);
                Require(p.FeetPos.y<y,"down input did not descend");
                TouchInput.MoveV=1f;
                int climbTicks=Mathf.CeilToInt((rope.top-p.FeetPos.y)/p.moveSpeed*60)+60;
                for(int i=0;i<climbTicks;i++)
                {
                    Tick(p,0,false,1f/60f);
                    if (!p.IsClimbing) break;
                }
                if (rope.upperExit)
                    Require(p.grounded && !p.IsClimbing && Mathf.Abs(p.FeetPos.y-rope.top)<0.31f,"native upper exit failed: "+theme);
                else Require(p.IsClimbing,"uf=0 rope incorrectly exited through top");
                UnityEngine.Object.DestroyImmediate(p.gameObject);
            }
        }
        var line=new Art.ClimbLine{x=0,top=5,bottom=0,upperExit=false};
        var test=Create(new List<Vector4>{new Vector4(-10,-1,10,-1)},0,2);
        PlayerCtrl.Climbs=new List<Art.ClimbLine>{line};test.grounded=false;
        TouchInput.MoveV=1;Tick(test,0,false,0.02f);
        Tick(test,1,true,0.02f);
        Require(!test.IsClimbing && !test.grounded && test.worldX>0,"rope jump-off failed");
        UnityEngine.Object.DestroyImmediate(test.gameObject);
        PlayerCtrl.Climbs=null;
    }
    static void CheckAllFootholds()
    {
        foreach(string theme in new[]{"harbor","forest","cliff","nest"})
        {
            var st=Art.Stage(theme);
            foreach(var f in st.Footholds)
            {
                if(Mathf.Abs(f.z-f.x)<0.01f) continue;
                float x=(f.x+f.z)*0.5f, y=(f.y+f.w)*0.5f;
                var p=Create(st.Footholds,x,y);
                PlayerCtrl.SetBounds(st.WalkMin,st.WalkMax);
                p.jumpY=0.2f;p.grounded=false;
                for(int i=0;i<30;i++) Tick(p,0,false,1f/60f);
                Require(p.grounded && Mathf.Abs(p.FeetPos.y-y)<0.01f,"missed native foothold: "+theme+" "+f);
                UnityEngine.Object.DestroyImmediate(p.gameObject);
            }
        }
    }

    // 触屏摇杆已随「放弃触屏点击」移除，这里改成验证 键盘/虚拟键 → 意图层 的通路
    static void CheckTouch()
    {
        UiKit.RefreshMetrics();
        TouchInput.Reset();
        var right = KeyMap.Of(GameAction.Right);
        Require(right != KeyCode.None, "移动右键位为空");
        InputHub.SetVirtual(right, true);
        Require(InputHub.Held(GameAction.Right), "虚拟右键按下后 Held 仍为 false");
        Require(InputHub.AxisX > 0.5f, "虚拟右键没有产生前进输入");
        InputHub.SetVirtual(right, false);
        Require(InputHub.AxisX == 0f, "松开虚拟右键后仍有移动输入");
        TouchInput.Reset();
        }
}
