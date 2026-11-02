using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class GameplayFeedbackChecks
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);checks++;}
    public static void Run()
    {
        try
        {
            Evasion(); Geometry(); Gauge(); VacuumAndOrdering(); Storage(); Npc();
            Debug.Log("[GameplayFeedbackChecks] PASS "+checks);
            EditorApplication.Exit(0);
        }
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Evasion()
    {
        var root=(Dictionary<string,object>)MiniJson.Parse(File.ReadAllText("tools/native-evasion-oracle.json"));
        foreach(Dictionary<string,object> row in (List<object>)root["cases"])
            Check(CombatRules.PhysicalMiss(Art.Int(row,"avoid"),Art.Int(row,"accuracy"),Art.Int(row,"level"),
                Art.Int(row,"mobLevel"),Art.Int(row,"job"),Art.Int(row,"roll"))==(bool)row["miss"],"Native x86 evasion mismatch");
    }
    static void Geometry()
    {
        var rangeMethod=typeof(SkillSystem).GetMethod("ProjectileRange",Flags);
        for(int level=1;level<=3;level++)
        {
            var skill=new SkillDef(new ClassSkill {name="Snail",art="1000"},SkillBook.Native(1000,level));
            float range=(float)rangeMethod.Invoke(null,new object[] {skill});
            Check(Math.Abs(range*Art.PPU-300)<.001f,"Every native snail level has 300px range");
        }
        var method=typeof(SkillSystem).GetMethod("ProjectileIntersects",Flags);
        bool Hit(Rect bounds,int facing=1)=>(bool)method.Invoke(null,new object[] {bounds,Vector2.zero,facing,300/Art.PPU,50/Art.PPU});
        Rect Body(float x,float y)=>Rect.MinMaxRect((x-10)/Art.PPU,y/Art.PPU,(x+10)/Art.PPU,(y+30)/Art.PPU);
        Check(Hit(Body(100,0)),"Snail must hit an ordinary monster in front");
        Check(!Hit(Body(600,0)),"Snail must not acquire a 600px distant monster");
        Check(!Hit(Body(200,-100)),"Snail must not acquire a monster on a lower platform");
        Check(!Hit(Body(-100,0)),"Snail must not acquire a monster behind the caster");
        Check(Hit(Body(-100,0),-1) && !Hit(Body(100,0),-1),"Left-facing targeting mirrors native scan");
        Check(Hit(Body(299,0)) && !Hit(Body(320,0)),"300px boundary uses monster body, not a doubled range");
    }
    static void Gauge()
    {
        var method=typeof(StatusBar).GetMethod("AdvanceWarning",Flags);
        float previous=50;
        bool Change(float current)
        {
            object[] args={current,100f,50,previous};
            bool flash=(bool)method.Invoke(null,args);previous=(float)args[3];return flash;
        }
        Check(!Change(90) && !Change(70) && !Change(50),"HP loss at/above warning must not flash");
        Check(Change(49),"Crossing below alarm must flash once");
        Check(!Change(49) && !Change(49),"Standing at low HP must not loop flash");
        Check(Change(45),"Further low HP loss must retrigger");
        Check(!Change(48) && Change(47),"Heal below alarm rearms the next decrease");
        float frame=(float)typeof(StatusBar).GetField("FlashFrame",Flags).GetRawConstantValue();
        Check(Math.Abs(frame-.13f)<.00001f,"Original IMG flash delay is 130ms");
    }
    static void VacuumAndOrdering()
    {
        var first=new GameObject("FeedbackMobA");var second=new GameObject("FeedbackMobB");
        var cameraObject=new GameObject("FeedbackCamera");
        var target=new RenderTexture(128,128,24);var copy=new Texture2D(128,128,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;var terrain=PlayerCtrl.Terrain;
        try
        {
            PlayerCtrl.Terrain=new List<Vector4> {new Vector4(-20,0,20,0)};
            var a=first.AddComponent<EnemyUnit>();var b=second.AddComponent<EnemyUnit>();
            a.artKey="snail";b.artKey="mushroom";a.Init(2,0,0);b.Init(2,0,0);
            a.VacuumLocked=true;a.transform.position=new Vector3(0,2,0);
            typeof(EnemyUnit).GetMethod("SyncVisual",Flags).Invoke(a,null);
            Check(a.transform.position.y==2,"Vacuum flight must not snap to lower foothold");
            var step=(Vector3)typeof(NativeMonsterVac).GetMethod("StepToward",Flags).Invoke(null,
                new object[] {Vector3.zero,new Vector3(300/Art.PPU,400/Art.PPU,0)});
            Check((step-new Vector3(90/Art.PPU,120/Art.PPU,0)).sqrMagnitude<.000001f,"Vacuum integer 150px vector step");
            a.transform.position=b.transform.position=Vector3.zero;
            var ar=(SpriteRenderer)typeof(EnemyUnit).GetField("body",Flags).GetValue(a);
            var br=(SpriteRenderer)typeof(EnemyUnit).GetField("body",Flags).GetValue(b);
            Check(ar.sortingOrder==br.sortingOrder && ar.transform.position.z!=br.transform.position.z,"Mob render tie has stable unique depth");
            ar.gameObject.layer=br.gameObject.layer=30;
            var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=64/Art.PPU;
            camera.transform.position=new Vector3(0,32/Art.PPU,-10);camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=Color.clear;camera.cullingMask=1<<30;camera.targetTexture=target;
            var av=ar.GetComponent<SpriteAnim>();var bv=br.GetComponent<SpriteAnim>();
            var apply=typeof(SpriteAnim).GetMethod("Apply",Flags);
            Color32[] Capture() {camera.Render();RenderTexture.active=target;copy.ReadPixels(new Rect(0,0,128,128),0,0);copy.Apply();return copy.GetPixels32();}
            int pixels=0;
            foreach(string aa in new[] {"stand","walk","hit"})
            foreach(string ba in new[] {"stand","walk","hit"})
            foreach(int frame in new[] {0,1,2})
            {
                av.Play(aa,false,true);bv.Play(ba,false,true);apply.Invoke(av,new object[] {frame});apply.Invoke(bv,new object[] {frame});
                ar.enabled=true;br.enabled=false;var ap=Capture();
                ar.enabled=false;br.enabled=true;var bp=Capture();ar.enabled=true;var both=Capture();
                bool aFront=ar.transform.position.z<br.transform.position.z;
                for(int i=0;i<both.Length;i++)
                {
                    if(ap[i].a!=255 || bp[i].a!=255)continue;
                    var expected=aFront ? ap[i] : bp[i];var actual=both[i];
                    Check(Math.Abs(expected.r-actual.r)<=1 && Math.Abs(expected.g-actual.g)<=1 && Math.Abs(expected.b-actual.b)<=1,"Overlapping monsters exchanged foreground after IMG frame change");pixels++;
                }
            }
            Check(pixels>100,"Real IMG overlap render check has opaque intersection");
            Debug.Log("[GameplayFeedbackChecks] 27 mob pose/frame combinations, "+pixels+" overlap pixels");
        }
        finally
        {
            PlayerCtrl.Terrain=terrain;RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);UnityEngine.Object.DestroyImmediate(cameraObject);
            target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(copy);
        }
    }
    static void Storage()
    {
        string path=Path.Combine(Path.GetTempPath(),"feedback-"+Guid.NewGuid()+".json");
        try
        {
            var db=DbStore.Create(path);var c=new CharRecord {name="Feedback",level=10,hp=50,maxhp=50,maxmp=50};
            c.id=CharactersDao.Insert(db,c);db.Save();
            var community=new NativeCommunityService(db);
            var stamp=new DateTime(2000,1,1,0,0,0,DateTimeKind.Utc);File.SetLastWriteTimeUtc(path,stamp);
            community.Handle(new CommunityRequest {characterId=(int)c.id});
            community.Handle(new CommunityRequest {characterId=(int)c.id,operation=CommunityOperation.RecordKill,value=10});
            Check(File.GetLastWriteTimeUtc(path)==stamp,"Read/no-family kill must not rewrite database");
            string saved=File.ReadAllText(path);
            using(db.DeferSaves())
            {
                db.Table("characters").rows[0]["meso"]=123L;db.Save();
                using(db.DeferSaves()) {db.Table("characters").rows[0]["meso"]=456L;db.Save();}
                Check(File.ReadAllText(path)==saved,"Nested operation saves are deferred");
            }
            Check(CharactersDao.FindById(DbStore.Open(path),(int)c.id).meso==456,"Outer return flushes final authoritative state");
            try {using(db.DeferSaves()) {db.Table("characters").rows[0]["meso"]=789L;db.Save();throw new InvalidOperationException();}}
            catch(InvalidOperationException) {}
            Check(CharactersDao.FindById(DbStore.Open(path),(int)c.id).meso==789,"Exceptional exit flushes preceding successful mutations");
        }
        finally {if(File.Exists(path))File.Delete(path);}
    }
    static void Npc()
    {
        var go=new GameObject("FeedbackNpcLoop");var old=GameLoop.I;
        try
        {
            var loop=go.AddComponent<GameLoop>();loop.enabled=false;GameLoop.I=loop;loop.field=go.AddComponent<FieldDirector>();loop.field.enabled=false;
            NpcSession.Conversation(2000,"",(session,mode)=>{});
            var gate=typeof(NpcDialogUi).GetProperty("CanClickField",Flags);
            Check((bool)gate.GetValue(null),"Invisible old NPC session must not block another NPC click");
            NpcSession.Active.SetDialog(NpcDialogMode.Next,"test");
            Check(!(bool)gate.GetValue(null),"Visible dialogue must retain exclusive field input");
        }
        finally {NpcSession.CloseActive();GameLoop.I=old;UnityEngine.Object.DestroyImmediate(go);}
    }
}
