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
        var p = new GameObject("MovementCheck").AddComponent<PlayerCtrl>();
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
            TouchInput.Reset();
            CheckTouch();
            Debug.Log("[MovementChecks] PASS: original harbor 60px step, 30/60/120Hz step, ledge, lower landing, overhead platform, multitouch ownership and release");
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void CheckTouch()
    {
        UiKit.RefreshMetrics();
        var go=new GameObject("TouchCheck"); var pad=go.AddComponent<TouchPad>();
        Action<string,object[]> call=(name,args)=>typeof(TouchPad).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(pad,args);
        var point=new Vector2(100,UiKit.H-150);
        call("Press",new object[]{point,7});
        call("Move",new object[]{point+Vector2.right*70,7});
        Require(TouchInput.MoveH>0.9f,"stick did not move right");
        call("Release",new object[]{8});
        Require(TouchInput.MoveH>0.9f,"second finger released the movement stick");
        call("Move",new object[]{point+Vector2.up*70,7});
        Require(TouchInput.TakeLaneStep()==0,"vertical drag changed depth on original 2D map");
        call("Release",new object[]{7});
        Require(TouchInput.MoveH==0,"movement stuck after release");
        UnityEngine.Object.DestroyImmediate(go);
    }
}
