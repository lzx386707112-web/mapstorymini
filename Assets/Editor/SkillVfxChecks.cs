using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class SkillVfxChecks
{
    public static void RunAssets()
    {
        Validate();EditorApplication.Exit(0);
    }
    public static void RunProblems()
    {
        Validate();
        SessionState.SetBool("MapleDash.SkillProblemOnly",true);
        SessionState.SetBool("MapleDash.CheckAllSkillVfx",true);
        GameplayPlayChecks.Run();
    }
    public static void Run()
    {
        SessionState.SetBool("MapleDash.SkillProblemOnly",false);
        Validate();
        SessionState.SetBool("MapleDash.CheckAllSkillVfx",true);
        GameplayPlayChecks.Run();
    }
    static void Validate()
    {
        var root=(Dictionary<string,object>)MiniJson.Parse(Resources.Load<TextAsset>("GameplaySkillVfx").text);
        int frames=0,clips=0;
        foreach(Dictionary<string,object> skill in (List<object>)root["skills"])
        {
            string key=(string)skill["key"];
            foreach(var pair in (Dictionary<string,object>)skill["clips"])
            {
                var clip=NativeSkillVfx.Get(key,pair.Key);
                foreach(var frame in clip.frames)
                {
                    var sprite=Art.FrameAt(frame.file,frame.origin);
                    if(sprite==null || frame.delay<=0)throw new Exception("Missing frame: "+frame.file);
                    Vector2 expected=new Vector2(frame.origin.x,sprite.rect.height-frame.origin.y);
                    if(Vector2.Distance(expected,sprite.pivot)>.01f)throw new Exception("Pivot clamped: "+frame.file);
                    frames++;
                }
                var fx=SkillFx.PlayPart(key,pair.Key,Vector3.zero,1);
                if(fx==null || fx.CurrentSprite==null)throw new Exception("Cannot play: "+key+"/"+pair.Key);
                UnityEngine.Object.DestroyImmediate(fx.gameObject);clips++;
            }
        }
        Debug.Log("[SkillVfxChecks] PASS "+clips+" clips / "+frames+" loaded frames and exact pivots");

    }
}

public sealed class SkillVfxRuntimeChecks : MonoBehaviour
{
    public static bool Done;
    public static Exception Failure;
    static void Require(bool value,string label){if(!value)throw new Exception("[SkillVfxRuntimeChecks] "+label);}
    System.Collections.IEnumerator Start()
    {
        Done=false;Failure=null;
        var checks=Check();
        while(true)
        {
            object next;
            try{if(!checks.MoveNext())break;next=checks.Current;}
            catch(Exception ex){Failure=ex;Debug.LogException(ex);break;}
            yield return next;
        }
        Done=true;
    }
    System.Collections.IEnumerator Check()
    {
        int checkedSkills=0;
        var game=GameLoop.I;
        for(int cls=0;cls<ClassDefs.Count;cls++)
        {
            game.TeardownBattle();Profile.SetClass(ClassDefs.At(cls).id);
            game.StartGame();game.stage.StopSession();
            var p=game.player;p.invulnUntil=Time.unscaledTime+1000;p.mp=0;
            var sys=p.GetComponent<SkillSystem>();
            sys.BindCooldowns(new Dictionary<int,SkillRuntimeState>());
            for(int slot=0;slot<4;slot++)
            {
                var s=sys.skills[slot];
                if(SessionState.GetBool("MapleDash.SkillProblemOnly",false) && s.art!="firebolt" && s.art!="arrowrain" && s.art!="rupture" && s.art!="doom" && s.art!="firearrow" && s.art!="berserk")continue;
                float range=(NativeSkillVfx.Has(s.art,"ball") && s.art!="firebolt") ? 10 : 2;
                p.facing=slot%2==0 ? 1 : -1;
                var enemy=game.SpawnMob("snail",p.worldX+p.facing*range,2);
                enemy.transform.position=(Vector3)p.FeetPos+Vector3.right*p.facing*range;
                enemy.Stun(100);enemy.hp=enemy.hpMax=10000000;
                EnemyUnit behind=null;
                if(s.art=="firebolt" || s.art=="arrowrain")
                {
                    behind=game.SpawnMob("snail",p.worldX-p.facing*2,2);
                    behind.transform.position=(Vector3)p.FeetPos-Vector3.right*p.facing*2;
                    behind.Stun(100);behind.hp=behind.hpMax=10000000;
                }
                Require(sys.TryCast(slot),s.art+" casts at zero MP: "+sys.LastFailure);
                Require(UnityEngine.Object.FindObjectsOfType<SkillFx>().Length>0,s.art+" has native release/prepare");
                yield return new WaitForSeconds(.1f);
                if(NativeSkillVfx.Has(s.art,"ball"))Require(enemy.hp==enemy.hpMax,s.art+" no damage before projectile arrives");
                float shotAt=s.art=="doom" ? 1.3f : s.art=="rupture" ? .8f : (s.art=="firebolt" || s.art=="firearrow") ? .65f : .35f;
                yield return new WaitForSeconds(shotAt-.1f);
                if(s.art=="firebolt" || s.art=="arrowrain" || s.art=="rupture" || s.art=="doom" || s.art=="firearrow" || s.art=="berserk")
                    Capture(s.art,p);
                yield return new WaitForSeconds(s.art=="illusion" ? 3.1f : 2.8f);
                if(behind!=null)Require(behind.hp<behind.hpMax,s.art+" attacks both sides of its authored area");
                if(s.native.Kind!="Buff" && s.native.Kind!="Summon" && s.native.Kind!="Dispel" && s.native.Kind!="Pull")
                    Require(enemy.hp<enemy.hpMax,s.art+" reaches target");
                sys.CancelActions();
                Require(UnityEngine.Object.FindObjectsOfType<SkillFx>().Length==0,s.art+" cancellation removes owned visuals");
                UnityEngine.Object.Destroy(enemy.gameObject);
                if(behind!=null)UnityEngine.Object.Destroy(behind.gameObject);
                yield return null;
                checkedSkills++;
                Debug.Log("[SkillVfxRuntimeChecks] PASS "+s.art);
            }
        }
        game.TeardownBattle();
        SessionState.SetBool("MapleDash.SkillProblemOnly",false);
        Require(UnityEngine.Object.FindObjectsOfType<SkillFx>().Length==0,"lobby cleanup");
        Debug.Log("[SkillVfxRuntimeChecks] PASS "+checkedSkills+" casts / projectile timing / cancellation");
    }
    static void Capture(string key,PlayerCtrl player)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
        var camera=Camera.main;
        var saved=camera.targetTexture;
        var previous=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(1280,720,24);
        var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
            string folder=System.IO.Path.GetFullPath("docs/skill-regression");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,key+".png"),texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=saved;RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(texture);
        }
    }

}
