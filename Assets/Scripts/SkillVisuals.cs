using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class SkillSystem
{
    readonly List<SkillFx> ownedVisuals=new List<SkillFx>();
    SkillFx Visual(string key,string part,Vector3 point,int facing,Transform follow=null,bool loop=false,int order=360)
    {
        var fx=SkillFx.PlayPart(key,part,point,facing,1,order,loop);
        if(fx!=null){ownedVisuals.RemoveAll(item=>item==null);ownedVisuals.Add(fx);if(follow!=null)fx.Follow(follow);}
        return fx;
    }
    void BeginVisuals(SkillDef s)
    {
        StartCoroutine(CastVisuals(s,p.FeetPos,p.facing));
    }
    IEnumerator CastVisuals(SkillDef s,Vector2 origin,int facing)
    {
        var prepare=NativeSkillVfx.Parts(s.art,"prepare");
        foreach(var clip in prepare) Visual(s.art,clip.path,p.FeetPos,facing,p.transform);
        float delay=Preparation(s);
        if(delay>0)yield return new WaitForSeconds(delay);
        if(delay>0)p.PlaySkill(s.art);
        // z<0 是施法者后层，不是全场景背景。
        foreach(string branch in new[]{"effect","effect0"})
            foreach(var clip in NativeSkillVfx.Parts(s.art,branch))
            {
                bool behind=NativeSkillVfx.Number(s.art,clip.path,"z")<0;
                bool field=s.art=="doom" || s.art=="rupture";
                Visual(s.art,clip.path,field ? (Vector3)origin : (Vector3)p.FeetPos,facing,field ? null : p.transform,false,behind ? p.SortingOrder-1 : 360);
            }
        foreach(var clip in NativeSkillVfx.Parts(s.art,"affected")) Visual(s.art,clip.path,p.FeetPos,facing,p.transform);
        foreach(string branch in new[]{"screen","screenEffect","effectScreen","background"})
            foreach(var clip in NativeSkillVfx.Parts(s.art,branch))
            {
                var camera=Camera.main;
                Vector3 anchor=camera==null ? (Vector3)origin : new Vector3(camera.transform.position.x,camera.transform.position.y,0);
                var fx=Visual(s.art,clip.path,anchor,-1,camera==null ? null : camera.transform,false,700);
                if(fx!=null && camera!=null)
                {
                    var size=fx.CurrentSprite.bounds.size;
                    float scale=Mathf.Max(camera.orthographicSize*2/size.y,camera.orthographicSize*2*camera.aspect/size.x);
                    fx.transform.localScale=Vector3.one*scale;
                }
            }
        if(NativeSkillVfx.Has(s.art,"tile"))
        {
            if(s.art=="firebolt")StartCoroutine(GroundFire(s,origin,facing));
            else TileField(s,origin,facing);
        }
        if(NativeSkillVfx.Has(s.art,"special") && s.art!="rupture")
            yield return SpecialField(s,origin,facing);
    }
    IEnumerator GroundFire(SkillDef s,Vector2 origin,int facing)
    {
        yield return new WaitForSeconds(.45f);
        TileField(s,origin,facing);
    }
    static bool UsesProjectile(SkillDef s) => s.art!="firebolt" && NativeSkillVfx.Has(s.art,"ball");
    // Old projectile skills have no level lt/rb. Do not reuse the 200px melee fallback.
    static float ProjectileRange(SkillDef s) => Mathf.Max(600/Art.PPU,Mathf.Max(-s.native.Min.x,s.native.Max.x));
    static bool InProjectileRange(Vector3 point,SkillDef s,Vector2 origin,int facing)
    {
        var delta=(Vector2)point-origin;
        return delta.x*facing>=0 && delta.x*facing<=ProjectileRange(s) && Mathf.Abs(delta.y)<=100/Art.PPU;
    }
    float Preparation(SkillDef s)
    {
        float duration=0;
        foreach(var clip in NativeSkillVfx.Parts(s.art,"prepare"))duration=Mathf.Max(duration,clip.Duration);
        return duration;
    }
    void TileField(SkillDef s,Vector2 origin,int facing)
    {
        var tiles=NativeSkillVfx.Parts(s.art,"tile");
        float spacing=NativeSkillVfx.Number(s.art,"tile","effectDistance",180)/Art.PPU;
        if(tiles.Count==0 || spacing<=0)return;
        // 大范围技能由多组原 tile 铺满可见攻击范围，不把一张爆点图拉伸成全屏。
        var camera=Camera.main;
        float half=camera==null ? 10 : camera.orthographicSize*camera.aspect;
        float left=Mathf.Max(origin.x+s.native.Min.x, camera==null ? origin.x-half : camera.transform.position.x-half);
        float right=Mathf.Min(origin.x-s.native.Min.x, camera==null ? origin.x+half : camera.transform.position.x+half);
        int n=0;
        for(float x=left;x<=right;x+=spacing)
        {
            float y=PlayerCtrl.GroundAt(x,origin.y);
            Visual(s.art,tiles[n++%tiles.Count].path,new Vector3(x,y,0),facing);
        }
    }
    IEnumerator SpecialField(SkillDef s,Vector2 origin,int facing)
    {
        var parts=NativeSkillVfx.Parts(s.art,"special");
        float start=NativeSkillVfx.Number(s.art,"special","start")/1000;
        if(start>0)yield return new WaitForSeconds(start);
        int count=(int)NativeSkillVfx.Number(s.art,"special","count",parts.Count);
        float interval=NativeSkillVfx.Number(s.art,"special","interval",60)/1000;
        float width=NativeSkillVfx.Number(s.art,"special","x",200)/Art.PPU;
        float height=NativeSkillVfx.Number(s.art,"special","y")/Art.PPU;
        float fall=height>0 ? NativeSkillVfx.Number(s.art,"special","duration",500)/1000 : 0;
        for(int i=0;i<count;i++)
        {
            // y 是发射高度，不能拿 fall 当高度，否则烈火箭会从屏幕外落下。
            float x=origin.x+((i+.5f)/count*2-1)*width;
            Vector3 point=new Vector3(x,origin.y+height,0);
            if(s.art=="doom")
            {
                // Explosion particles belong within the authored area, not a line on the floor.
                point.x=origin.x+Mathf.Lerp(s.native.Min.x,s.native.Max.x,(i+.5f)/count);
                point.y=origin.y+(i%3)*45/Art.PPU;
            }
            var clip=parts[i%parts.Count];
            var fx=Visual(s.art,clip.path,point,facing,null,fall>0);
            if(fall>0 && fx!=null) StartCoroutine(Fall(fx,point,new Vector3(x,PlayerCtrl.GroundAt(x,origin.y),0),fall));
            yield return new WaitForSeconds(Mathf.Max(.001f,interval));
        }
    }
    IEnumerator Fall(SkillFx fx,Vector3 from,Vector3 to,float duration)
    {
        float elapsed=0;
        while(fx!=null && elapsed<duration)
        {
            elapsed+=Time.deltaTime;
            fx.transform.position=Vector3.Lerp(from,to,Mathf.Clamp01(elapsed/duration));
            yield return null;
        }
        if(fx!=null)fx.Stop();
    }
    void Impact(SkillDef s,NativeSkillDefinition definition,Component target,int direction,bool successful)
    {
        if(!successful)return;
        if(definition.Id!=s.native.Id || string.IsNullOrEmpty(s.art))
        {
            string path="Skill/"+(definition.Id/10000).ToString("D3")+".img/skill/"+definition.Id+"/hit/0";
            var node=WzAssets.Node(path);
            if(node!=null)
            {
                var effect=NativeImgAnimation.Spawn(path,target.transform,TargetAnchor(target,node.Int("pos")),360,direction>0);
                if(effect!=null){nativeReplayEffects.RemoveAll(x=>x==null);nativeReplayEffects.Add(effect);return;}
            }
        }
        string key=definition.Id==1121021 ? "illusion_slash" : definition.Id==1121022 ? "illusion_explosion" : s.art;
        var hits=NativeSkillVfx.Parts(key,"hit");
        if(hits.Count>0)
        {
            var clip=hits[impactVariant++%hits.Count];
            Vector3 anchor=TargetAnchor(target,NativeSkillVfx.Number(key,clip.path,"pos"));
            Visual(key,clip.path,anchor,direction,target.transform);
        }

    }
    int impactVariant;
    static Vector3 TargetAnchor(Component target,float pos=0)
    {
        var renderers=target.GetComponentsInChildren<SpriteRenderer>();
        SpriteRenderer body=null;
        foreach(var renderer in renderers)
            if(renderer.enabled && renderer.sprite!=null && (body==null || renderer.bounds.size.y>body.bounds.size.y))body=renderer;
        if(body==null)return target.transform.position+Vector3.up*.6f;
        // 原 mob pos=2 表示头顶状态；普通 hit 使用怪物身体中心。
        return new Vector3(body.bounds.center.x,pos==2 ? body.bounds.max.y : body.bounds.center.y,0);
    }
    IEnumerator ProjectileCast(SkillDef s,Vector2 origin,int facing)
    {
        float launch=NativeSkillVfx.Number(s.art,"ball","delay",p.SkillActionDuration*500)/1000;
        if(launch>0)yield return new WaitForSeconds(launch);
        var targets=SelectTargets(s,s.native,origin,facing);
        bool pierce=s.art=="dragon" || s.art=="ray";
        // 无目标仍沿面向射出，不把 ball 挂在人物原地。
        if(targets.Count==0)
        {
            Vector3 from=origin+Vector2.up*(30/Art.PPU);
            StartCoroutine(Fly(s,from,from+Vector3.right*facing*ProjectileRange(s),null,facing));
        }
        else if(pierce) yield return Pierce(s,origin,facing,targets);
        else foreach(var target in targets)StartCoroutine(Fly(s,origin+Vector2.up*(30/Art.PPU),TargetAnchor(target),target,facing));
    }
    // WZ 未给旧版飞弹速度，800px/s 是明确的移植参数，不修改原图尺寸。
    const float ProjectileSpeed=800/Art.PPU;
    IEnumerator Fly(SkillDef s,Vector3 from,Vector3 to,Component target,int facing)
    {
        var fx=Visual(s.art,"ball",from,facing,null,true);
        float duration=Vector3.Distance(from,to)/ProjectileSpeed,elapsed=0;
        while(elapsed<duration)
        {
            elapsed+=Time.deltaTime;
            if(fx!=null)fx.transform.position=Vector3.Lerp(from,to,Mathf.Clamp01(elapsed/Mathf.Max(.001f,duration)));
            yield return null;
        }
        if(fx!=null)fx.Stop();
        if(target!=null)HitAt(s,s.native,target.transform.position,facing,null,target);
    }
    IEnumerator Pierce(SkillDef s,Vector2 origin,int facing,List<Component> targets)
    {
        targets.Sort((a,b)=>Mathf.Abs(a.transform.position.x-origin.x).CompareTo(Mathf.Abs(b.transform.position.x-origin.x)));
        Vector3 from=origin+Vector2.up*(30/Art.PPU);
        float distance=ProjectileRange(s);
        var fx=Visual(s.art,"ball",from,facing,null,true);
        int next=0;float traveled=0;
        while(traveled<distance)
        {
            traveled+=ProjectileSpeed*Time.deltaTime;
            if(fx!=null)fx.transform.position=from+Vector3.right*facing*traveled;
            while(next<targets.Count && (targets[next]==null || Mathf.Abs(targets[next].transform.position.x-origin.x)<=traveled))
            {
                var target=targets[next++];
                if(target!=null)HitAt(s,s.native,target.transform.position,facing,null,target);
            }
            yield return null;
        }
        if(fx!=null)fx.Stop();
    }
    List<Component> SelectTargets(SkillDef s,NativeSkillDefinition d,Vector2 origin,int facing)
    {
        var targets=new List<Component>();
        foreach(var b in FindObjectsOfType<BossUnit>())if(!b.dead && InProjectileRange(b.transform.position,s,origin,facing))targets.Add(b);
        foreach(var e in EnemyUnit.All)if(!e.dead && InProjectileRange(e.transform.position,s,origin,facing))targets.Add(e);
        targets.Sort((a,b)=>
        {
            if(s.art=="illusion" && (a is BossUnit)!=(b is BossUnit))return a is BossUnit ? -1 : 1;
            return (a.transform.position-(Vector3)origin).sqrMagnitude.CompareTo((b.transform.position-(Vector3)origin).sqrMagnitude);
        });
        if(targets.Count>d.TargetCount)targets.RemoveRange(d.TargetCount,targets.Count-d.TargetCount);
        return targets;
    }
    void ClearVisuals()
    {
        foreach(var fx in ownedVisuals)if(fx!=null)fx.Stop();
        ownedVisuals.Clear();
    }
}
