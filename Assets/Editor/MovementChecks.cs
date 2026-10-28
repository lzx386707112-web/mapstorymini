using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MapleDash.Wz;

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
    public static void CheckNativeOracle()
    {
        try {
            var root = (Dictionary<string, object>)MiniJson.Parse(System.IO.File.ReadAllText("tools/native-movement-oracle.json"));
            var physics = NativeMovementPhysics.Load();
            int count = 0;
            foreach (Dictionary<string, object> row in (List<object>)root["cases"]) {
                var input = (Dictionary<string, object>)row["input"];
                var output = (Dictionary<string, object>)row["output"];
                Func<string, double, double> value = (key, fallback) => input.TryGetValue(key, out var v) ? Convert.ToDouble(v) : fallback;
                double before = value("vx", 0), dt = value("ms", 8) / 1000;
                bool air = input.ContainsKey("air");
                double velocity = air ? physics.Air(before, value("vy", 0), (int)value("h", 0), dt) :
                    physics.Ground(before, (int)value("h", 0), value("force", 0), value("drag", 1), 1, value("slope", 0), dt);
                double expected = Convert.ToDouble(output["velocity"]);
                Require(Math.Abs(velocity - expected) < .0001,
                    "Native oracle velocity mismatch case=" + count + " expected=" + expected + " actual=" + velocity);
                Require(Math.Abs((before + velocity) * .5 * dt - Convert.ToDouble(output["position"])) < .0001,
                    "Native oracle displacement mismatch case=" + count);
                if (air) Require(Math.Abs(Math.Min(physics.FallSpeed, value("vy", 0) + physics.Gravity * dt) -
                    Convert.ToDouble(output["vertical"])) < .0001, "Native vertical integration case=" + count);
                count++;
            }
            Debug.Log("[MovementChecks] PASS original x86 oracle " + count + " cases");
            EditorApplication.Exit(0);
        } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Separate entry: these regressions do not depend on conveyor tuning.
    public static void CheckStepCorners()
    {
        try
        {
            var horizontal = typeof(PlayerCtrl).GetMethod("MoveHorizontal", BindingFlags.NonPublic | BindingFlags.Instance);
            var landing = typeof(PlayerCtrl).GetMethod("FindLanding", BindingFlags.NonPublic | BindingFlags.Static);
            var movement = typeof(PlayerCtrl).GetMethod("StepMovement", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 120 })
            foreach (int dir in new[] { -1, 1 })
            {
                // Descending beside a step, then reversing / getting pushed into its side.
                var p = Create(new List<Vector4> {
                    new Vector4(-10, 0, 0, 0), new Vector4(0, 0, 0, 1.5f),
                    new Vector4(0, 1.5f, 10, 1.5f)
                }, -.1f, .8f);
                if (dir < 0) PlayerCtrl.SetTerrain(new List<Vector4> {
                    new Vector4(-10, 1.5f, 0, 1.5f), new Vector4(0, 1.5f, 0, 0),
                    new Vector4(0, 0, 10, 0) });
                p.PlaceAt(-dir * .1f, .8f); p.grounded = false;
                horizontal.Invoke(p, new object[] { dir * .15f });
                Require(p.worldX == 0, "Contact displacement crossed step wall");
                for (int i = 0; i < Mathf.CeilToInt(.6f / dt); i++) Tick(p, dir, false, dt);
                Require(p.grounded && Mathf.Abs(p.FeetPos.y) < .001f,
                    "Wall slide missed lower endpoint at " + dt + " dir=" + dir);
                p.PlaceAt(0, .8f); p.grounded = false;
                horizontal.Invoke(p, new object[] { dir * .1f });
                Require(p.worldX == 0, "Exact-wall start crossed wall");
                horizontal.Invoke(p, new object[] { 0f }); // Idle must preserve which side of the wall is free.
                horizontal.Invoke(p, new object[] { -dir * .1f });
                Require(p.worldX == -dir * .1f, "Wall contact prevented moving away");
                // Grounded movement must still block climbing a vertical step and permit jumping it.
                p.PlaceAt(-dir * .2f, 0);
                for (int i = 0; i < Mathf.CeilToInt(.3f / dt); i++) Tick(p, dir, false, dt);
                Require(p.worldX * dir <= 0 && p.grounded, "Walk crossed upright step");
                Tick(p, dir, true, dt);
                for (int i = 0; i < Mathf.CeilToInt(.8f / dt); i++) Tick(p, dir, false, dt);
                Require(p.worldX * dir > 0 && p.grounded && Mathf.Abs(p.FeetPos.y - 1.5f) < .001f,
                    "Jump onto step regressed");
                // Exercise the live damage path (native contact impulse may launch the actor).
                p.PlaceAt(-dir * .1f, 0);
                p.hp = p.hpMax = 100000;
                p.TakeDamage(1000, dir);
                Require(p.hp < 100000, "Damage regression did not apply damage");
                for (int i = 0; i < Mathf.CeilToInt(.8f / dt); i++) Tick(p, 0, false, dt);
                Require(p.worldX * dir <= 0 && p.grounded && Mathf.Abs(p.FeetPos.y) < .001f,
                    "Damage crossed step wall or missed lower landing");
                UnityEngine.Object.DestroyImmediate(p.gameObject);
            }
            CheckRush();
            CheckClimbs();
            int maps = 0, corners = 0;
            var probe = Create(new List<Vector4>(), 0, 0);
            foreach (string file in System.IO.Directory.EnumerateFiles(
                System.IO.Path.Combine(WzAssets.DataRoot, "Map/Map"), "*.img", System.IO.SearchOption.AllDirectories))
            {
                if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), out int id)) continue;
                var map = NativeMapView.LoadMap(new FieldMap { id = id });
                var terrain = new List<Vector4>();
                foreach (var f in map.footholds)
                    terrain.Add(new Vector4(f.x1 / Art.PPU, -f.y1 / Art.PPU, f.x2 / Art.PPU, -f.y2 / Art.PPU));
                PlayerCtrl.SetTerrain(terrain);
                PlayerCtrl.SetBounds(float.NegativeInfinity, float.PositiveInfinity);
                foreach (var wall in terrain)
                {
                    if (wall.x != wall.z || wall.y == wall.w) continue;
                    float bottom = Mathf.Min(wall.y, wall.w), top = Mathf.Max(wall.y, wall.w);
                    bool joined = terrain.Exists(f => f.x != f.z &&
                        ((f.x == wall.x && f.y == bottom) || (f.z == wall.x && f.w == bottom)));
                    if (!joined) continue;
                    foreach (int dir in new[] { -1, 1 })
                    {
                        float y = bottom + Mathf.Min((top - bottom) * .5f, 1f / Art.PPU);
                        probe.PlaceAt(wall.x - dir * .1f / Art.PPU, y); probe.grounded = false;
                        horizontal.Invoke(probe, new object[] { wall.x + dir * .1f / Art.PPU });
                        Require(Mathf.Abs(probe.worldX - wall.x) < .00001f,
                            "Native corner wall crossed map=" + id + " wall=" + wall);
                        object[] args = { probe.worldX, y, bottom - 1f / Art.PPU, -1, 0f };
                        Require((bool)landing.Invoke(null, args), "Native lower endpoint missed map=" + id + " wall=" + wall);
                        for (int tick = 0; tick < 10 && !probe.grounded; tick++)
                            movement.Invoke(probe, new object[] { 0f, false, .008f });
                        Require(probe.grounded && probe.FeetPos.y >= bottom - .001f && probe.FeetPos.y <= y + .01f,
                            "Native wall slide fell below lower endpoint map=" + id + " wall=" + wall);
                    }
                    corners++;
                }
                maps++;
            }
            UnityEngine.Object.DestroyImmediate(probe.gameObject);
            TouchInput.Reset();
            Debug.Log("[StepCorners] PASS 30/60/120Hz contact, exact wall, move away; all map wall slides=" + maps + " lower corners=" + corners);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void CheckConveyors()
    {
        var map = NativeMapView.LoadMap(new FieldMap { id = 1 });
        var belt = map.footholds.Find(f => f.id == 20);
        Require(belt != null && belt.force == -100, "Map 1 conveyor force was not parsed");
        foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 120 })
        {
            var p = Create(new List<Vector4> { new Vector4(-10, 0, 10, 0),
                new Vector4(-10, -2, 10, -2) }, 0, 0);
            try
            {
                PlayerCtrl.SetTerrain(PlayerCtrl.Terrain, null, new List<int> { belt.force, 0 });
                int face = p.facing;
                for (int i = 0; i < Mathf.RoundToInt(1f / dt); i++) Tick(p, 0, false, dt);
                Require(p.worldX < -p.moveSpeed * .9f && p.worldX > -p.moveSpeed && p.grounded && p.facing == face,
                    "Idle conveyor failed or changed facing at " + dt);
                p.PlaceAt(0, 0);
                Tick(p, -1, false, dt);
                Require(p.worldX < 0 && Mathf.Abs(p.worldX) < 2 * p.moveSpeed * dt, "Conveyor following speed");
                p.PlaceAt(0, 0);
                Tick(p, 1, false, dt);
                Require(p.worldX > 0 && p.worldX < .2f * p.moveSpeed * dt, "Conveyor opposing speed");
                p.PlaceAt(0, -2);
                Tick(p, 0, false, dt);
                Require(p.worldX == 0, "Upper conveyor moved actor on lower platform");
                p.PlaceAt(0, 0);
                Tick(p, 0, true, dt);
                Require(!p.grounded && p.worldX == 0, "Conveyor applied in air");
                PlayerCtrl.SetTerrain(PlayerCtrl.Terrain);
                p.PlaceAt(0, 0);
                Tick(p, 0, false, dt);
                Require(p.worldX == 0, "Conveyor data leaked after terrain reset");
            }
            finally { UnityEngine.Object.DestroyImmediate(p.gameObject); }
        }
        Debug.Log("[MovementChecks] PASS conveyor IMG parse, idle, input directions, lower platform, jump, terrain reset at 30/60/120Hz");
    }

    static void CheckNativeMomentum()
    {
        var p = Create(new List<Vector4> { new Vector4(-20, 0, 20, 0) }, 0, 0);
        try {
            var movement = typeof(PlayerCtrl).GetMethod("StepMovement", BindingFlags.Instance | BindingFlags.NonPublic);
            var velocity = typeof(PlayerCtrl).GetField("horizontalVelocity", BindingFlags.Instance | BindingFlags.NonPublic);
            movement.Invoke(p, new object[] { 1f, false, .008f });
            Require(Math.Abs((double)velocity.GetValue(p) - 11.2) < .0001, "Native first 8ms acceleration");
            Require(Math.Abs(p.worldX * Art.PPU - .0448) < .0001, "Native trapezoid first 8ms displacement");
            for (int i = 0; i < 30; i++) movement.Invoke(p, new object[] { 1f, false, .008f });
            Require(Math.Abs((double)velocity.GetValue(p) - 125) < .0001, "Native walk speed limit");
            float before = p.worldX;
            movement.Invoke(p, new object[] { 0f, false, .008f });
            Require(p.worldX > before && Math.Abs((double)velocity.GetValue(p) - 118.6) < .0001,
                "Releasing direction discarded native inertia");
            movement.Invoke(p, new object[] { 0f, true, .008f });
            Require(!p.grounded && (double)velocity.GetValue(p) > 118, "Jump discarded horizontal momentum");
            p.PlaceAt(0, 0);
            Require((double)velocity.GetValue(p) == 0, "Warp retained stale velocity");
            movement.Invoke(p, new object[] { 1f, true, .008f });
            Require(!p.grounded && (double)velocity.GetValue(p) > 99 && (double)velocity.GetValue(p) <= 100,
                "Native jump from rest did not apply 80 percent walk impulse");
        }
        finally { UnityEngine.Object.DestroyImmediate(p.gameObject); }
    }

    public static void Run()
    {
        try
        {
            foreach (float dt in new[] {1f/30f,1f/60f,1f/120f})
            {
                foreach (float direction in new[] {-1f, 1f})
                {
                    // 上层侧壁穿过下层地面；下层跨段仍连续，不能当成台阶。
                    var underWall = Create(new List<Vector4> {
                        new Vector4(-10,0,1,0), new Vector4(1,0,10,0),
                        new Vector4(1,-1,1,3), new Vector4(1,3,5,3)
                    }, 1f - direction * .2f, 0f);
                    for (int i=0;i<Mathf.CeilToInt(.3f/dt);i++) Tick(underWall,direction,false,dt);
                    Require((underWall.worldX-1f)*direction>.5f && underWall.grounded &&
                        Mathf.Abs(underWall.FeetPos.y)<.001f,
                        "upper sidewall blocked continuous lower ground at " + dt + " direction=" + direction + " x=" + underWall.worldX + " feet=" + underWall.FeetPos.y + " grounded=" + underWall.grounded);
                    UnityEngine.Object.DestroyImmediate(underWall.gameObject);

                    var slope = Create(new List<Vector4> {new Vector4(-10,-2,0,0),new Vector4(0,0,10,2)}, -direction * .1f, -direction * .02f);
                    for (int i=0;i<Mathf.CeilToInt(.5f/dt);i++) Tick(slope,direction,false,dt);
                    Require(slope.grounded && Mathf.Abs(slope.FeetPos.y - slope.worldX * .2f) < .001f,
                        "feet lost connected slope at " + dt + " direction=" + direction);
                    UnityEngine.Object.DestroyImmediate(slope.gameObject);
                }
                var terrain = new List<Vector4> {new Vector4(-10,0,2,0),new Vector4(2,0,2,1.5f),new Vector4(2,1.5f,10,1.5f)};
                var p=Create(terrain,1.6f,0);
                for(int i=0;i<Mathf.CeilToInt(0.3f/dt);i++) Tick(p,1,false,dt);
                Require(p.worldX<=2f,"walked through a 60px wall");
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
            CheckConveyors();
            CheckNativeMomentum();
            // The actual harbor step is two consecutive 35px + 25px vertical WZ footholds.
            var harbor=Art.Stage("harbor");
            var native=Create(harbor.Footholds,harbor.WorldX(510),harbor.WorldY(215));
            for(int i=0;i<15;i++) Tick(native,-1,false,1f/60f);
            Require(native.worldX>=harbor.WorldX(489),"walked through original harbor step");
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
            Require(p.worldX<=2 && !p.IsRushing,"rush passed wall " + dt);
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
                for(int i=0;i<30 && !p.grounded;i++) Tick(p,0,false,1f/60f);
                Require(p.grounded && p.worldX >= Mathf.Min(f.x,f.z) && p.worldX <= Mathf.Max(f.x,f.z) &&
                    Mathf.Abs(p.FeetPos.y-Mathf.Lerp(f.y,f.w,(p.worldX-f.x)/(f.z-f.x)))<0.01f,"missed native foothold: "+theme+" "+f+" actual="+p.FeetPos+" expectedY="+y+" grounded="+p.grounded+" bounds="+PlayerCtrl.XMin+","+PlayerCtrl.XMax);
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
