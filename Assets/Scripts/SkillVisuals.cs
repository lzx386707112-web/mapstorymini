using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class SkillSystem
{
    readonly List<SkillFx> ownedVisuals=new List<SkillFx>();
    // 调试开关：PlayerPrefs "debug_hitlog"=1 时把每次真正生成的层打一行日志。
    // 不能在 static 字段初始化器里读 PlayerPrefs —— MonoBehaviour 的构造/字段初始化
    // 阶段 Unity 禁止调用它，会让整个 SkillSystem 抛 TypeInitializationException。
    // 改成首次真正播层时（已经在 Play 的 Update 里）惰性读一次并缓存。
    static bool fxDebugChecked, fxDebugValue;
    static bool FxDebug
    {
        get
        {
            if (!fxDebugChecked) { fxDebugChecked = true; fxDebugValue = PlayerPrefs.GetInt("debug_hitlog", 0) != 0; }
            return fxDebugValue;
        }
    }
    SkillFx Visual(string key,string part,Vector3 point,int facing,Transform follow=null,bool loop=false,int order=360)
    {
        var fx=SkillFx.PlayPart(key,part,point,facing,1,order,loop);
        if(fx!=null){ownedVisuals.RemoveAll(item=>item==null);ownedVisuals.Add(fx);if(follow!=null)fx.Follow(follow);}
        if(FxDebug && fx!=null)Debug.Log("[FxPlay] key="+key+" part="+fx.Part);
        return fx;
    }
    void BeginVisuals(SkillDef s,bool bodyOnly=false)
    {
        StartCoroutine(CastVisuals(s,p.FeetPos,p.facing,bodyOnly));
    }
    // bodyOnly：只播「本体」层（effect/affected/special…），跳过 prepare 与起手等待。
    // 周期攻击（祝福之鎚每 1.8 秒补一发）复用它，否则后续每一发只有伤害、屏幕上什么都没有。
    IEnumerator CastVisuals(SkillDef s,Vector2 origin,int facing,bool bodyOnly=false)
    {
        if(!bodyOnly)
        {
            foreach(string branch in NativeSkillVfx.Layers(s.vfx,"prepare"))
                foreach(var clip in NativeSkillVfx.Parts(s.vfx,branch))
                    PlayLayer(s.vfx,clip,p.FeetPos,facing,p.transform,360);
            float delay=Preparation(s);
            if(delay>0)yield return new WaitForSeconds(delay);
            if(delay>0)p.PlaySkill(s.action);
        }
        // z<0 是施法者后层，不是全场景背景。282 的 effect 是 effect/effect1..6 的族，
        // 每层的 z 可能写在分支上（effect1 z=-1），逐层取。
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"effect"))
            foreach(var clip in NativeSkillVfx.Parts(s.vfx,branch))
            {
                int z=(int)NativeSkillVfx.Number(s.vfx,clip.path,"z");
                if(z==0) z=(int)NativeSkillVfx.Number(s.vfx,branch,"z");
                bool field=s.art=="doom" || s.art=="rupture";
                PlayLayer(s.vfx,clip,field ? (Vector3)origin : (Vector3)p.FeetPos,facing,
                    field ? null : p.transform, z<0 ? p.SortingOrder-1 : 360+Mathf.Max(0,z));
            }
        // affected 也成族：affected0 是独立一层，不是 affected 的别名——400011003 神聖團結
        // 的 affected 是 11 帧 216x92 起、affected0 是 6 帧 216x88 起，帧数与尺寸都不同。
        // Parts 只认一级名，旧写法把 affected0 整层丢掉（受影响的还有 400011026 / 400011127）。
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"affected"))
            foreach(var clip in NativeSkillVfx.Parts(s.vfx,branch))
                PlayLayer(s.vfx,clip,p.FeetPos,facing,p.transform,360);
        // 400011104 [神速]奔馳的閃光 的 rushEffect/0（13 帧突进残影，画布约 1000x400）。
        // 口径同 effect：z 先读 clip、再回退分支；z<0 画在施法者之后。
        //
        // 400011028 火神重擊 的 mask **不能这样画**：解出来的画布是 1368x164、
        // R=G=B=0、alpha 99.9% 不透明 —— 那是**纯黑 stencil**（282 拿它做遮罩/混合），
        // 当精灵直接叠上去就是屏幕正中一条黑条，把角色整个人盖掉（实测截图即是）。
        // 想做对必须先逆向 EXE 对 mask 的混合方式，没逆向前一个像素都不要画。
        foreach(string stem in new[]{"rushEffect"})
            foreach(string branch in NativeSkillVfx.Layers(s.vfx,stem))
                foreach(var clip in NativeSkillVfx.Parts(s.vfx,branch))
                {
                    int z=(int)NativeSkillVfx.Number(s.vfx,clip.path,"z");
                    if(z==0) z=(int)NativeSkillVfx.Number(s.vfx,branch,"z");
                    PlayLayer(s.vfx,clip,p.FeetPos,facing,p.transform,z<0 ? p.SortingOrder-1 : 360+Mathf.Max(0,z));
                }
        // 282 V 技能把角色/链接对象身上的后续演出放在
        // specialAffected、specialAffected1、specialAffected2，而不是 affected。
        // 神聖團結的翅膀与持续光效就在这三组；只播 effect/special 会留下前半段的蓝色核心。
        //
        // 但 specialAffected 的子段是「羁绊对象槽」：400011003 的 /0../5 六段 origin 完全相同
        // （都是 25,247）、像素却各不相同 —— /0 是自己，/1../5 是五名羁绊队员。
        // 旧写法把六段一起挂在自己身上，六个不同角色的光效叠成一团，
        // 表现就是「神聖團結放出来跟导出图完全不一样」。这里只演自己的那一份；
        // 队员那五段要等队伍实体拿到各自锚点再分发，不能平铺在施法者身上。
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"specialAffected"))
        {
            var clips=NativeSkillVfx.Parts(s.vfx,branch);
            int take=clips.Count>1 && SameOrigin(clips) ? 1 : clips.Count;
            // 走 PlayLayer 而不是 Visual：这几层的首帧 delay 就是「整层晚出现」的偏移
            // （specialAffected1 首帧 210ms），直接 Visual 会把后段光效提前叠上来。
            for(int i=0;i<take;i++) PlayLayer(s.vfx,clips[i],p.FeetPos,facing,p.transform,360);
        }
        // effect_ple 是玩家专属层（PLEY 前缀），形态与 frameEffect 相同：
        // pre 播一次 → loop 循环 → end 收尾。
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"effect_ple"))
            PlaySegmentedLayer(s,branch,p.FeetPos,facing,p.transform,360,p.SkillActionDuration);
        // frameEffect/N 是帧驱动的场景层（天堂神罚的落雷），不跟人走。
        if(NativeSkillVfx.Has(s.vfx,"frameEffect"))
            foreach(string branch in NativeSkillVfx.SubBranches(s.vfx,"frameEffect"))
                PlaySegmentedLayer(s,branch,(Vector3)origin,facing,null,340,p.SkillActionDuration);
        // customEffect：start/N 立刻播，end/N 在施法动作走完后播。
        if(NativeSkillVfx.Has(s.vfx,"customEffect"))
            PlayCustomEffect(s,(Vector3)origin,facing,null,340);
        // start / repeat / end：放置物与召唤物的「起手 → 循环 → 消失」三段。
        PlayFieldLoop(s,origin,facing);
        // afterimage：武器挥砍残影，依附角色，按段错开。
        if(NativeSkillVfx.Has(s.vfx,"afterimage"))
            StartCoroutine(Afterimage(s,facing));
        // 282 的屏幕层是一族编号分支：screen / screen1..screen12，还有 screen3/pre|loop|end。
        // 画布帧与视频两种形态都要吃到，不能只认写死的名字。
        foreach(string root in NativeSkillVfx.Roots(s.vfx))
        {
            if(!IsScreenLayer(root))continue;
            foreach(var clip in NativeSkillVfx.Parts(s.vfx,root)) PlayScreenClip(s.vfx,clip,origin);
            foreach(var video in NativeSkillVfx.VideoParts(s.vfx,root)) PlayVideo(video,true);
        }
        // 视频也能挂在 effect/effect0/special 上（不是全屏，是贴地/贴人的表现）。
        foreach(string branch in new[]{"effect","effect0","special"})
            foreach(var video in NativeSkillVfx.VideoParts(s.vfx,branch)) PlayVideo(video,false);
        if(NativeSkillVfx.Has(s.vfx,"tile"))
        {
            if(s.art=="firebolt")StartCoroutine(GroundFire(s,origin,facing));
            else TileField(s,origin,facing);
        }
        if(NativeSkillVfx.HasLayer(s.vfx,"special") && s.art!="rupture")
            yield return SpecialField(s,origin,facing);
        // finish/0|1|2：连击收尾段，施法动作走完后播。
        PlayFinish(s,(Vector3)origin,facing,p.transform);
    }
    // 多段层是不是「同一锚点的多个对象槽」：282 用这个形状表达
    // 「6 名队员身上各一份」。同 origin + 不同像素 ⇒ 只该演其中一份（自己）。
    // 分裂波次（同一位置按 interval 依次发）origin 也相同，但那种层带 start/interval，
    // 走 SpecialLayer；这里只用来判 specialAffected 这一族，不做通用推断。
    static bool SameOrigin(List<NativeVfxClip> clips)
    {
        if(clips==null || clips.Count<2) return false;
        if(clips[0].frames==null || clips[0].frames.Length==0) return false;
        Vector2 first=clips[0].frames[0].origin;
        foreach(var clip in clips)
            if(clip.frames==null || clip.frames.Length==0 || clip.frames[0].origin!=first) return false;
        return true;
    }
    // 三段层：pre|start 播一次 → loop|repeat 循环 → end 收尾。
    // branch 既可以是 effect_ple，也可以是 frameEffect/0 这种带编号的子分支。
    void PlaySegmentedLayer(SkillDef s,string branch,Vector3 point,int facing,Transform follow,int order,float minimumHold)
    {
        bool any=false;
        var loops=NativeSkillVfx.Segment(s.vfx,branch,"loop");
        if(loops.Count==0)loops=NativeSkillVfx.Segment(s.vfx,branch,"repeat");
        // 循环段至少走三圈：天使之泉VI 的 loop 是 16 帧，按施法动作时长只有 0.55s 会截断。
        float hold=Mathf.Max(minimumHold,loops.Count>0 ? loops[0].Duration*3f : .8f);
        foreach(var clip in NativeSkillVfx.Segment(s.vfx,branch,"pre")){PlayLayer(s.vfx,clip,point,facing,follow,order);any=true;}
        foreach(var clip in NativeSkillVfx.Segment(s.vfx,branch,"start")){PlayLayer(s.vfx,clip,point,facing,follow,order);any=true;}
        foreach(var clip in loops)
        {
            var fx=Visual(s.vfx,clip.path,point,facing,follow,true,order);
            if(fx!=null && hold>0)Destroy(fx.gameObject,hold);
            any=true;
        }
        if(!any)return;
        // pre/loop/end 之外还有 special 这类并列子段（天使之泉VI 的 effect_ple/special），
        // 只认三段会把它们整段丢掉。三段名之外的一律播一次。
        foreach(var clip in NativeSkillVfx.Parts(s.vfx,branch))
        {
            string sub=clip.path.Length>branch.Length ? clip.path.Substring(branch.Length+1) : "";
            int slash=sub.IndexOf('/');
            string head=slash<0 ? sub : sub.Substring(0,slash);
            if(head=="pre"||head=="start"||head=="loop"||head=="repeat"||head=="end")continue;
            PlayLayer(s.vfx,clip,point,facing,follow,order);
        }
        var ends=NativeSkillVfx.Segment(s.vfx,branch,"end");
        if(ends.Count>0)StartCoroutine(PlayStageLater(s.vfx,ends,hold,point,facing,follow,order));
    }
    // customEffect：start/N 立刻播，end/N 延迟到施法动作结束。
    void PlayCustomEffect(SkillDef s,Vector3 point,int facing,Transform follow,int order)
    {
        foreach(var clip in NativeSkillVfx.Segment(s.vfx,"customEffect","start"))
            PlayLayer(s.vfx,clip,point,facing,follow,order);
        var ends=NativeSkillVfx.Segment(s.vfx,"customEffect","end");
        if(ends.Count>0)StartCoroutine(PlayStageLater(s.vfx,ends,p.SkillActionDuration,point,facing,follow,order));
    }
    // finish：连击收尾，按施放序号取一个变体，避免每次都放同一段。
    void PlayFinish(SkillDef s,Vector3 point,int facing,Transform follow)
    {
        // finish 同理成族：400011026 的收尾段只有 finish0，没有 finish。
        var finishes=new List<NativeVfxClip>();
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"finish"))
            finishes.AddRange(NativeSkillVfx.Parts(s.vfx,branch));
        if(finishes.Count==0)return;
        var clip=finishes[finishVariant++%finishes.Count];
        StartCoroutine(PlayStageLater(s.vfx,new List<NativeVfxClip>{clip},p.SkillActionDuration,point,facing,follow,360));
    }
    // start / repeat / end：放置物（火灵结界、暗影之刃、海龙石、凤凰）与召唤物的
    // 三段生命周期，落在场上而不是跟着人走。三段都成族出现（start/start0、
    // repeat/repeat0、end/end0），必须按族枚举，否则只会播到第 0 个变体。
    void PlayFieldLoop(SkillDef s,Vector2 origin,int facing)
    {
        var starts=new List<NativeVfxClip>();
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"start"))starts.AddRange(NativeSkillVfx.Parts(s.vfx,branch));
        var repeats=new List<NativeVfxClip>();
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"repeat"))repeats.AddRange(NativeSkillVfx.Parts(s.vfx,branch));
        var ends=new List<NativeVfxClip>();
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"end"))ends.AddRange(NativeSkillVfx.Parts(s.vfx,branch));
        if(starts.Count==0 && repeats.Count==0 && ends.Count==0)return;
        // 只有 start 没有循环/收段的多半是别的语义，宁可不播也不误挂一个放置物。
        if(repeats.Count==0 && ends.Count==0)return;
        // buff 类的循环段是贴在角色身上的（海神护体、暗影之刃），
        // attack/summon 才是落在地上的放置物（火灵结界、船员）。
        bool onBody=s.native.Kind=="Buff";
        Vector3 point=onBody ? (Vector3)p.FeetPos
            : new Vector3(origin.x+facing*48/Art.PPU,PlayerCtrl.GroundAt(origin.x,origin.y),0);
        Transform anchor=onBody ? p.transform : null;
        int order=onBody ? p.SortingOrder-1 : 340;
        float hold=Mathf.Max(p.SkillActionDuration,repeats.Count>0 ? repeats[0].Duration*3 : .8f);
        foreach(var clip in starts)PlayLayer(s.vfx,clip,point,facing,anchor,order);
        foreach(var clip in repeats)
        {
            var fx=Visual(s.vfx,clip.path,point,facing,anchor,true,order);
            if(fx!=null)Destroy(fx.gameObject,hold);
        }
        if(ends.Count>0)StartCoroutine(PlayStageLater(s.vfx,ends,hold,point,facing,anchor,order));
    }
    // afterimage/swordOL/swingO1..OF：武器挥砍残影，每段是一小截独立动画。
    IEnumerator Afterimage(SkillDef s,int facing)
    {
        foreach(var clip in NativeSkillVfx.Parts(s.vfx,"afterimage"))
        {
            var fx=Visual(s.vfx,clip.path,p.FeetPos,facing,p.transform,false,p.SortingOrder-2);
            if(fx!=null)Destroy(fx.gameObject,Mathf.Max(.12f,clip.Duration));
            yield return new WaitForSeconds(.05f);
        }
    }
    IEnumerator PlayStageLater(string key,List<NativeVfxClip> clips,float wait,Vector3 point,int facing,Transform follow,int order)
    {
        if(wait>0)yield return new WaitForSeconds(wait);
        foreach(var clip in clips)PlayLayer(key,clip,point,facing,follow,order);
    }
    // 层的起始偏移：首帧 delay 明显偏长时先等这段再出图，避免后段特效提前叠上。
    void PlayLayer(string key,NativeVfxClip clip,Vector3 point,int facing,Transform follow,int order,bool loop=false)
    {
        if(clip.StartDelay<=0f){Visual(key,clip.path,point,facing,follow,loop,order);return;}
        StartCoroutine(DelayedLayer(key,clip,point,facing,follow,order,loop));
    }
    IEnumerator DelayedLayer(string key,NativeVfxClip clip,Vector3 point,int facing,Transform follow,int order,bool loop)
    {
        yield return new WaitForSeconds(clip.StartDelay);
        Visual(key,clip.path,point,facing,follow,loop,order);
    }
    static bool IsScreenLayer(string root) =>
        root.StartsWith("screen",StringComparison.Ordinal) || root=="background";

    void PlayScreenClip(string key,NativeVfxClip clip,Vector2 origin)
    {
        var camera=Camera.main;
        Vector3 anchor=camera==null ? (Vector3)origin : new Vector3(camera.transform.position.x,camera.transform.position.y,0);
        var fx=Visual(key,clip.path,anchor,-1,camera==null ? null : camera.transform,false,700+(int)NativeSkillVfx.Number(key,clip.path,"z"));
        if(fx!=null && camera!=null)
        {
            var size=fx.CurrentSprite.bounds.size;
            float scale=Mathf.Max(camera.orthographicSize*2/size.y,camera.orthographicSize*2*camera.aspect/size.x);
            fx.transform.localScale=Vector3.one*scale;
        }
    }

    // screen 层是相机覆盖，压在角色之上、伤害数字（900）之下；
    // 其余层贴施法者，z<0 画在人物之后（282 的前后景口径）。
    void PlayVideo(NativeVfxVideo video,bool screen)
    {
        int order = screen ? 700+video.z : (video.z<0 ? p.SortingOrder-1 : 360+video.z);
        NativeSkillVideo.PlayNode(video.node?.VideoData,video.path.Replace('/','_'),p,order,video.loop,video.delay);
    }

    IEnumerator GroundFire(SkillDef s,Vector2 origin,int facing)
    {
        yield return new WaitForSeconds(.45f);
        TileField(s,origin,facing);
    }
    // shootobj 是 282 的 Actor 发射物：只有它、没有 ball 的技能（天使之箭VI / 光速神弩VI /
    // 禁咒之符 / 四飞闪VI…）以前是瞬发命中，飞行过程没有画面。
    static bool UsesProjectile(SkillDef s) => s.art!="firebolt" &&
        (NativeSkillVfx.Has(s.vfx,"ball") || NativeShootObj.Has(s.native.Id));
    // 95571F (snail/magic ball): 50..300px, y=feet-28 in WZ.
    // 761845 (weapon shots): explicit range, then weapon + range-passive.
    static float ProjectileRange(SkillDef s)
    {
        var row=SkillBook.Get(s.native.Id)?.Level(s.native.Level);
        int range=Art.Int(row,"range");
        if(range>0)return range/Art.PPU;
        if(s.native.HasBounds)return Mathf.Max(-s.native.Min.x,s.native.Max.x);
        // shootobj 的弹道长度就是它的真实射程，必须排在武器射程之前：
        // 4141001 四飞闪VI 前向 600px，退回拳套射程只有 200px，目标搜索范围够不到落点。
        float reach=ShootReach(s);
        if(reach>0)return reach;
        if(!NativeProjectiles.IsRangedSkill(s.native.Id) || s.native.Magic)return 300/Art.PPU;
        if(s.native.Id==4111003 || s.native.Id==4221003)return 300/Art.PPU;
        var weapon=WeaponDefs.Get(Profile.equipWeapon).kind;
        int passive=weapon==WeaponKind.claw ? (Profile.JobId/1000==1 ? 14000001 : 4000001)
            : Profile.JobId/1000==1 ? 13000001 : 3000002;
        int level=SkillProgress.LevelOf(passive);
        int extra=level>0 ? Art.Int(SkillBook.Get(passive)?.Level(level),"range") : 0;
        return (weapon==WeaponKind.bow || weapon==WeaponKind.crossbow ? 300+extra
            : weapon==WeaponKind.claw ? 200+extra : weapon==WeaponKind.gun ? 200 : 0)/Art.PPU;
    }
    bool InProjectileRange(Component target,SkillDef s,Vector2 origin,int facing)
    {
        if(s.native.HasBounds)return InRange(target,s.native,origin,facing);
        Rect bounds=target is EnemyUnit enemy && enemy.ProjectileBounds.HasValue
            ? enemy.ProjectileBounds.Value : new Rect(target.transform.position.x,target.transform.position.y,0,0);
        return ProjectileIntersects(bounds,origin,facing,ProjectileRange(s),
            !s.native.Magic && NativeProjectiles.IsRangedSkill(s.native.Id) ? 0 : 50/Art.PPU);
    }
    // CMobPool::FindHitMobInRect 6789ED: forward 20px slices, expanding by distance/4.
    internal static bool ProjectileIntersects(Rect bounds,Vector2 origin,int facing,float range,float start)
    {
        float y=origin.y+28/Art.PPU;
        int limit=Mathf.RoundToInt(range*Art.PPU);
        for(int pixels=Mathf.RoundToInt(start*Art.PPU);pixels<limit;pixels+=20)
        {
            float x=pixels/Art.PPU;
            float end=Mathf.Min(pixels+20,limit)/Art.PPU;
            float half=(pixels/4)/Art.PPU;
            float a=origin.x+x*facing,b=origin.x+end*facing;
            if(bounds.xMax>=Mathf.Min(a,b) && bounds.xMin<=Mathf.Max(a,b) &&
                bounds.yMax>=y-half && bounds.yMin<=y+half)return true;
        }
        return false;
    }
    float Preparation(SkillDef s)
    {
        float duration=0;
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"prepare"))
            foreach(var clip in NativeSkillVfx.Parts(s.vfx,branch))duration=Mathf.Max(duration,clip.Duration);
        return duration;
    }
    void TileField(SkillDef s,Vector2 origin,int facing)
    {
        var tiles=NativeSkillVfx.Parts(s.vfx,"tile");
        float spacing=NativeSkillVfx.Number(s.vfx,"tile","effectDistance",180)/Art.PPU;
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
            Visual(s.vfx,tiles[n++%tiles.Count].path,new Vector3(x,y,0),facing);
        }
    }
    IEnumerator SpecialField(SkillDef s,Vector2 origin,int facing)
    {
        // special / special0..3 各自是一层独立波次，count/interval 写在层上。
        foreach(string branch in NativeSkillVfx.Layers(s.vfx,"special"))
            StartCoroutine(SpecialLayer(s,origin,facing,branch));
        yield break;
    }
    IEnumerator SpecialLayer(SkillDef s,Vector2 origin,int facing,string branch)
    {
        var parts=NativeSkillVfx.Parts(s.vfx,branch);
        if(parts.Count==0)yield break;
        float start=NativeSkillVfx.Number(s.vfx,branch,"start")/1000;
        if(start>0)yield return new WaitForSeconds(start);
        int count=(int)NativeSkillVfx.Number(s.vfx,branch,"count",parts.Count);
        float interval=NativeSkillVfx.Number(s.vfx,branch,"interval",60)/1000;
        float width=NativeSkillVfx.Number(s.vfx,branch,"x",200)/Art.PPU;
        float height=NativeSkillVfx.Number(s.vfx,branch,"y")/Art.PPU;
        float fall=height>0 ? NativeSkillVfx.Number(s.vfx,branch,"duration",500)/1000 : 0;
        // `special` 有两种形态，数据本身能区分，不能一律按「多次生成」沿 x 铺开：
        //   ① 多次生成：分支上写了 count（400011003 神聖團結的 special 才是分波次）；
        //   ② 层级容器：分支上没有 count，子段是「同一处的前后景分层」
        //      （400011131 雷神戰槌的 special/0 + special/front，一锤子的本体 + 前方拖影）。
        // 旧写法对 ② 也按份数铺开，结果锤子在人物左右各一个、且不分朝向 ——
        // 表现就是「锤子没飞出去／多出一个」。
        bool spread=NativeSkillVfx.Number(s.vfx,branch,"count")>0;
        for(int i=0;i<count;i++)
        {
            var clip=parts[i%parts.Count];
            // y 是发射高度，不能拿 fall 当高度，否则烈火箭会从屏幕外落下。
            float x;
            if(spread) x=origin.x+((i+.5f)/count*2-1)*width;
            else
            {
                // 不铺开时只按子段名分前后：front 放到朝向那一侧，back 放反侧，其余叠在原点。
                // 子段可能是 `special/front`（一层）也可能是 `special/front/0`（多层），
                // 所以按「路径里含 /front/」判断，不能只看结尾。
                int dir=ContainsSegment(clip.path,"front")?facing
                    :ContainsSegment(clip.path,"back")?-facing:0;
                x=origin.x+dir*width;
            }
            Vector3 point=new Vector3(x,origin.y+height,0);
            if(s.art=="doom")
            {
                // Explosion particles belong within the authored area, not a line on the floor.
                point.x=origin.x+Mathf.Lerp(s.native.Min.x,s.native.Max.x,(i+.5f)/count);
                point.y=origin.y+(i%3)*45/Art.PPU;
            }
            // special 也会分前后景：层 z 为负就落到施法者之后。
            float z=NativeSkillVfx.Number(s.vfx,clip.path,"z");
            if(z==0) z=NativeSkillVfx.Number(s.vfx,branch,"z");
            var fx=Visual(s.vfx,clip.path,point,facing,null,fall>0,z<0 ? p.SortingOrder-1 : 360+(int)z);
            if(fall>0 && fx!=null) StartCoroutine(Fall(fx,point,new Vector3(x,PlayerCtrl.GroundAt(x,origin.y),0),fall));
            yield return new WaitForSeconds(Mathf.Max(.001f,interval));
        }
    }
    // 路径里是否含某一段名字：`special/front` 与 `special/front/0` 都算含 "front"，
    // 但 `special/front2` 不算（段名必须整段相等）。
    static bool ContainsSegment(string path,string segment)
    {
        if(string.IsNullOrEmpty(path))return false;
        int index=path.IndexOf("/"+segment,StringComparison.Ordinal);
        while(index>=0)
        {
            int after=index+1+segment.Length;
            if(after>=path.Length||path[after]=='/')return true;
            index=path.IndexOf("/"+segment,index+1,StringComparison.Ordinal);
        }
        return false;
    }
    IEnumerator Fall(SkillFx fx,Vector3 from,Vector3 to,float duration)    {
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
        string key=definition.Id==s.native.Id ? s.vfx : NativeSkillVfx.Key(definition.Id,definition.Level);
        // hit 是随机变体集（hit/0..hit/32），hit2 是「第二段命中」的整体动画。
        // 多段技能以前只播 hit，hit2 永远出不来；按命中序号交替，两套都能看到。
        // 单段技能不该有第二段，直接不取，免得把 hit2 当随机变体乱播。
        var strikes=NativeSkillVfx.Parts(key,"hit");
        var second=definition.HitCount>=2 ? NativeSkillVfx.Parts(key,"hit2") : new List<NativeVfxClip>();
        int variant=impactVariant++;
        var pool=second.Count>0 && variant%2==1 ? second : strikes;
        if(pool.Count==0)pool=second.Count>0 ? second : strikes;
        if(pool.Count>0)
        {
            var clip=pool[variant%pool.Count];
            Vector3 anchor=TargetAnchor(target,NativeSkillVfx.Number(key,clip.path,"pos"));
            Visual(key,clip.path,anchor,direction,target.transform);
        }
        ShowComboNumber(s);
    }
    // number / number2：282 打在角色身上的连击/层数数字（number/icon%d）。
    // 1.5 秒内没再命中就归零，避免跨技能累加。
    void ShowComboNumber(SkillDef s)
    {
        float now=Time.unscaledTime;
        if(now>comboUntil)comboCount=0;
        comboCount++;comboUntil=now+1.5f;
        var icons=NativeSkillVfx.Icons(s.vfx,"number");
        if(icons.Count>0)
        {
            if(comboFx!=null)comboFx.Stop();
            var clip=icons[Mathf.Clamp(comboCount,1,icons.Count)-1];
            comboFx=Visual(s.vfx,clip.path,p.FeetPos,p.facing,p.transform,false,500);
            if(comboFx!=null)Destroy(comboFx.gameObject,1.2f);
        }
        var extra=NativeSkillVfx.Icons(s.vfx,"number2");
        if(extra.Count>0)
        {
            if(comboFx2!=null)comboFx2.Stop();
            var clip=extra[Mathf.Clamp(comboCount,1,extra.Count)-1];
            comboFx2=Visual(s.vfx,clip.path,p.FeetPos,p.facing,p.transform,false,501);
            if(comboFx2!=null)Destroy(comboFx2.gameObject,1.2f);
        }
    }
    int impactVariant, finishVariant, comboCount;
    float comboUntil;
    SkillFx comboFx, comboFx2;
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
    // ---- shootobj：282 的 Actor 化发射物（ASkillProjectile）-----------------------
    // 数据：info（起点/碰撞箱/起手延迟/速度）+ moveList（p1..pN 的 pos/v/delay）
    // + layerList（s* 起手层、b* 飞行层，飞多久都要有画面）。
    IEnumerator ShootObjCast(SkillDef s,Vector2 origin,int facing,bool immediate)
    {
        var data=NativeShootObj.Get(s.native.Id);
        if(data==null)yield break;
        // 连射的每一发都立刻出手，不再吃起手延迟。
        float launch=immediate ? 0 : data.startDelayMs;
        if(launch>0)yield return new WaitForSeconds(launch/1000f);
        int groups=Mathf.Max(1,data.groupCount);
        for(int group=0;group<groups;group++)
        {
            if(group>0)
            {
                // groupInterval 是固定间隔；没有它就用 randomCreateDelay 的随机区间
                // （4441501 禁咒之符 100..900ms、12120023 火焰旋涡 90..240ms）。
                float gap=data.groupIntervalMs>0 ? data.groupIntervalMs
                    : data.randomDelayMaxMs>0
                        ? UnityEngine.Random.Range(data.randomDelayMinMs,Mathf.Max(data.randomDelayMinMs,data.randomDelayMaxMs))
                        : data.randomDelayMinMs;
                if(gap>0)yield return new WaitForSeconds(gap/1000f);
            }
            // 飞行交给独立协程：最长的弹道要飞 6 秒，不能把施法状态机一起卡住。
            if(data.paths.Count==0)StartCoroutine(ShootObjActor(s,data,null,origin,facing,group));
            else foreach(var path in data.paths)StartCoroutine(ShootObjActor(s,data,path,origin,facing,group));
        }
    }
    // WZ 坐标：x 前向为负、y 向上为负，按 facing 镜像、y 取反后落到世界坐标。
    static Vector3 ShootPoint(Vector2 origin,int facing,Vector2 wz)
        => new Vector3(origin.x-wz.x*facing,origin.y-wz.y,0);
    IEnumerator ShootObjActor(SkillDef s,ShootObjData data,ShootObjPath path,Vector2 origin,int facing,int group)
    {
        // 没有 moveList 的技能（5211020 / 5241013）把速度写在 info/v 上：
        // 按前向射程合成一条直线，否则弹道会停在出生点。
        if(path==null)
        {
            path=new ShootObjPath { name="info" };
            path.points.Add(new ShootObjPoint
            {
                pos=new Vector2(-ProjectileRange(s)*Art.PPU,0f),
                v=data.infoSpeed,
                hasPos=true,
            });
        }
        // startAlphaDelay：源端先等再渐显（火焰旋涡 210ms），延迟创建即可。
        if(data.startAlphaDelayMs>0)yield return new WaitForSeconds(data.startAlphaDelayMs/1000f);
        Vector3 spawn=ShootPoint(origin,facing,data.start);
        // multiStartPos：多路弹幕的出生点沿纵向散开，别全叠在同一个像素上。
        if(data.multiStartPos && group>0) spawn+=Vector3.up*(group*20f/Art.PPU);
        // startOffset：连发的起点接在前一发后面（沿前向递推），否则每发都从脚底出发。
        if(data.startOffset && group>0) spawn+=Vector3.right*facing*(group*40f/Art.PPU);
        // pickDir3Way / pickDir5Way：源端每次施法从 3/5 个方向里挑一个，不是固定朝向。
        int lanes=data.pickDir5Way ? 5 : data.pickDir3Way ? 3 : 0;
        float lane=lanes>0 ? UnityEngine.Random.Range(-(lanes/2),(lanes/2)+1)*(lanes==5 ? 30f : 15f) : 0f;
        var visuals=new List<SkillFx>();
        foreach(string layer in data.layers)
        {
            // s* 是起手层（播一次）；b*/c*/e* 是飞行层，循环播到停为止。
            bool once=layer.Length>0 && layer[0]=='s';
            var fx=Visual(s.vfx,"shootobj/layerList/"+layer,spawn,facing,null,!once,360);
            if(fx!=null)
            {
                // rorateWhenFlip（原端拼写错）：镜像施法时飞行层要再转 180°，否则贴图是倒的。
                if(data.flipRotate!=0 && facing<0)
                {
                    var scale=fx.transform.localScale;scale.x=-scale.x;fx.transform.localScale=scale;
                }
                visuals.Add(fx);
            }
        }
        // 命中预算：struck 记「每个目标已经被命中几次」，原来是 HashSet（只记有没有）。
        var struck=new Dictionary<int,int>();
        var targets=ShootCandidates(s,origin,facing);
        int budget=data.HitBudget(s.native.TargetCount);
        int perSweep=data.PerSweep, perMob=data.PerMobLimit;
        Vector3 position=spawn;
        int landed=0;
        float traveledMs=0;
        // 路径总时长用于 deleteAlphaDelay（负值 = 结束前 N 毫秒开始渐隐）。
        float totalMs=data.Duration(path);
        bool fading=false;
        var points=path.points;
        for(int i=0;i<points.Count;i++)
        {
            var point=points[i];
            if(point.delayMs>0){yield return new WaitForSeconds(point.delayMs/1000f);traveledMs+=point.delayMs;}
            Vector3 target=ShootPoint(origin,facing,ShootObjTarget(s,data,point,origin,facing,lane));
            // moveList 没写速度的弹道用 groupbuild/vmin（12120023 的火焰旋涡就是这样）。
            float speed=ShootObjData.Speed(point.v>0 ? point.v : data.groupVMin);
            if(speed<=0f){position=target;SetShootPosition(visuals,position);continue;}
            float duration=Vector3.Distance(position,target)/speed,elapsed=0;
            Vector3 from=position;
            // rotateAttack：飞行层朝飞行方向旋转（火焰旋涡 / 四飞闪VI 的箭矢朝向）。
            if(data.rotateAttack)RotateShoot(visuals,target-from);
            // 追踪弹：WZ 里没有 chase 键，只有 sprayType=AngelRay（天使之箭VI）会追最近目标。
            Component chased=data.chaseTarget ? NearestShootTarget(targets,from) : null;
            while(elapsed<duration)
            {
                elapsed+=Time.deltaTime;traveledMs+=Time.deltaTime*1000f;
                if(chased!=null)target=TargetAnchor(chased);
                position=Vector3.Lerp(from,target,Mathf.Clamp01(elapsed/Mathf.Max(.001f,duration)));
                SetShootPosition(visuals,position);
                landed+=ShootStrike(s,data,position,facing,targets,struck,ref budget,perSweep,perMob);
                if(!fading)fading=StartShootFade(data,visuals,traveledMs,totalMs);
                if(data.useUpWhenMaxMobCount && struck.Count>=perSweep)break;
                yield return null;
            }
            SetShootPosition(visuals,position);
            if(data.useUpWhenMaxMobCount && struck.Count>=perSweep)break;
            if(budget<=0)break;
        }
        // v=0 的段是「原地蓄力」：发射层留在出生点直到整段走完再消散。
        // deleteAlpha=1 是源端的「消失前渐隐」，直接 Stop 会让弹道在命中瞬间闪掉。
        if(!fading)
        {
            float fade=data.deleteAlpha>0 ? .12f : 0;
            foreach(var fx in visuals)
                if(fx!=null){if(fade>0)fx.FadeOut(fade);else fx.Stop();}
        }
        // 一条也没碰到时按落点补一次结算，避免目标站在边界上被漏掉。
        // 多路弹幕只在第一条路径上兜底，否则每路都补一次范围伤害。
        bool first=data.paths.Count==0 || ReferenceEquals(path,data.paths[0]) || path.name=="info";
        if(landed==0 && struck.Count==0 && first)HitAt(s,s.native,position,facing);
    }
    // deleteAlphaDelay 是负值（-600..-910）：在结束前 |delay| 毫秒开始渐隐。
    static bool StartShootFade(ShootObjData data,List<SkillFx> visuals,float traveledMs,float totalMs)
    {
        if(data.deleteAlpha<=0 || data.deleteAlphaDelayMs>=0 || totalMs<=0)return false;
        if(traveledMs<totalMs+data.deleteAlphaDelayMs)return false;
        foreach(var fx in visuals)if(fx!=null)fx.FadeOut(.12f);
        return true;
    }
    static void RotateShoot(List<SkillFx> visuals,Vector3 direction)
    {
        if(direction.sqrMagnitude<.0001f)return;
        float angle=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
        foreach(var fx in visuals)if(fx!=null)fx.transform.rotation=Quaternion.Euler(0,0,angle);
    }
    static Component NearestShootTarget(List<Component> targets,Vector3 from)
    {
        Component best=null;float bestDistance=float.MaxValue;
        foreach(var target in targets)
        {
            if(target==null)continue;
            var enemy=target as EnemyUnit;
            if(enemy!=null && enemy.dead)continue;
            var boss=target as BossUnit;
            if(boss!=null && boss.dead)continue;
            float distance=(target.transform.position-from).sqrMagnitude;
            if(distance<bestDistance){bestDistance=distance;best=target;}
        }
        return best;
    }
    // triggerSpecialCore：282 全库只有 4141000（四飛閃VI）一个 shootobj 带这个标记。
    // 语义是「这次弹道命中也要跑特殊核心的命中判定」——四飛閃走 shootobj 而不是 HitAt，
    // 不显式接线的话，特殊核心的攻击型计数（每 1000 次 / 同怪 800 次）会漏掉这类技能。
    public static event Action<SkillDef,Component> SpecialCoreTriggered;
    void SpecialCoreHit(SkillDef s,Component target)
    {
        SpecialCoreTriggered?.Invoke(s,target);
        NativeSpecialCore.OnAttack(p, target != null ? target.GetInstanceID() : 0);
    }
    static Vector2 ShootObjTarget(SkillDef s,ShootObjData data,ShootObjPoint point,Vector2 origin,int facing,float lane)
    {
        Vector2 value;
        if(point.hasPos)value=point.pos;
        // 只有 angleList 的技能（禁咒之符）：角度 + 射程决定落点，0° 是前向。
        else if(data.angles.Count>0)
        {
            var span=data.angles[UnityEngine.Random.Range(0,data.angles.Count)];
            float angle=UnityEngine.Random.Range(span.x,span.y)*Mathf.Deg2Rad;
            float distance=ProjectileRange(s)*Art.PPU;
            value=new Vector2(-Mathf.Cos(angle)*distance,-Mathf.Sin(angle)*distance);
        }
        else value=Vector2.zero;
        // pickDir3Way/5Way 的 lane 偏移：把整条路径绕出生点旋转。
        if(lane==0f)return value;
        float rad=lane*Mathf.Deg2Rad,cos=Mathf.Cos(rad),sin=Mathf.Sin(rad);
        return new Vector2(value.x*cos-value.y*sin,value.x*sin+value.y*cos);
    }
    List<Component> ShootCandidates(SkillDef s,Vector2 origin,int facing)
    {
        var targets=SelectTargets(s,s.native,origin,facing,null,null);
        if(targets.Count>0)return targets;
        // 没有攻击框的 shootobj：按弹道能到的范围放宽找目标。
        float limit=ProjectileRange(s)+ShootReach(s);
        foreach(var b in FindObjectsOfType<BossUnit>())
            if(!b.dead && Mathf.Abs(b.transform.position.x-origin.x)<=limit)targets.Add(b);
        foreach(var e in EnemyUnit.All)
            if(!e.dead && Mathf.Abs(e.transform.position.x-origin.x)<=limit)targets.Add(e);
        return targets;
    }
    static float ShootReach(SkillDef s)
    {
        var data=NativeShootObj.Get(s.native.Id);
        if(data==null)return 0;
        float reach=0;
        foreach(var path in data.paths)
            foreach(var point in path.points)
                reach=Mathf.Max(reach,new Vector2(point.pos.x,point.pos.y).magnitude/Art.PPU);
        return reach;
    }
    int ShootStrike(SkillDef s,ShootObjData data,Vector3 at,int facing,List<Component> targets,
        Dictionary<int,int> struck,ref int budget,int perSweep,int perMob)
    {
        int hits=0;
        foreach(var target in targets)
        {
            // perSweep = maxMobCount（原意是「每次碰撞判定最多结算几个目标」，
            // 以前被当成总目标数上限，导致 maxMobCount=4/6 的弹幕也只穿一个）。
            if(hits>=perSweep || budget<=0)break;
            if(target==null)continue;
            int id=target.GetInstanceID();
            struck.TryGetValue(id,out int times);
            // perMob = hitLimitEachMob / hitLimitEveryMob：单怪命中次数上限。
            if(times>=perMob)continue;
            if(!ShootOverlaps(target,at,data.bodyWH))continue;
            struck[id]=times+1;
            budget--;
            HitAt(s,s.native,target.transform.position,facing,null,target);
            if(data.triggerSpecialCore)SpecialCoreHit(s,target);
            hits++;
            // multiAttackCount：同一目标的追加段，按 multiAttackHitDelay 间隔结算
            // （400011004 是 9 段 / 90ms，400031025 是 2 段 / 390ms）。
            if(data.multiAttackCount>1)
                StartCoroutine(MultiStrike(s,target,facing,data.multiAttackCount,data.multiAttackHitDelayMs,struck,perMob));
        }
        return hits;
    }
    IEnumerator MultiStrike(SkillDef s,Component target,int facing,int count,float delayMs,
        Dictionary<int,int> struck,int perMob)
    {
        for(int i=1;i<count;i++)
        {
            if(delayMs>0)yield return new WaitForSeconds(delayMs/1000f);
            if(target==null)yield break;
            int id=target.GetInstanceID();
            struck.TryGetValue(id,out int times);
            if(times>=perMob)yield break;
            struck[id]=times+1;
            HitAt(s,s.native,target.transform.position,facing,null,target);
        }
    }
    static bool ShootOverlaps(Component target,Vector3 at,Vector2 bodyWH)
    {
        var box=new Rect(at.x-bodyWH.x*.5f/Art.PPU,at.y-bodyWH.y*.5f/Art.PPU,bodyWH.x/Art.PPU,bodyWH.y/Art.PPU);
        // Boss 只有逻辑坐标（与 InRange 同口径），小怪才有 Select 用的像素框。
        var enemy=target as EnemyUnit;
        Rect bounds=enemy!=null && enemy.SelectionBounds.HasValue
            ? enemy.SelectionBounds.Value
            : new Rect(target.transform.position.x,target.transform.position.y,0,0);
        return box.Overlaps(bounds);
    }
    static void SetShootPosition(List<SkillFx> visuals,Vector3 position)
    {
        foreach(var fx in visuals)if(fx!=null)fx.transform.position=position;
    }
    IEnumerator ProjectileCast(SkillDef s,Vector2 origin,int facing,bool immediate=false)
    {
        // 有 shootobj 就用 Actor 弹道（4141001 四飞闪VI 同时有 ball 和 shootobj，
        // ball 的射程会退回默认 300px，而源端 shootobj 是前向 420px）。
        if(NativeShootObj.Has(s.native.Id))
        {
            yield return ShootObjCast(s,origin,facing,immediate);
            yield break;
        }
        var targets=SelectTargets(s,s.native,origin,facing);
        float launch=immediate ? 0 : NativeSkillVfx.Number(s.vfx,"ball","delay",p.SkillActionDuration*500)/1000;
        if(launch>0)yield return new WaitForSeconds(launch);
        bool pierce=s.art=="dragon" || s.art=="ray";
        // 无目标仍沿面向射出，不把 ball 挂在人物原地。
        if(targets.Count==0)
        {
            Vector3 from=origin+Vector2.up*(28/Art.PPU);
            StartCoroutine(Fly(s,from,from+Vector3.right*facing*ProjectileRange(s),null,facing));
        }
        else if(pierce) yield return Pierce(s,origin,facing,targets);
        else foreach(var target in targets)StartCoroutine(Fly(s,origin+Vector2.up*(28/Art.PPU),TargetAnchor(target),target,facing));
    }
    // 95445F..954511: native travel time is distance in pixels * 1.5ms.
    const float ProjectileSpeed=(1000f/1.5f)/Art.PPU;
    // 3141004 箭座VI 同时有 ball 与 flipBall：两套 origin 不同的箭图
    // （x≈95 vs x≈130），源端按连射序号交替用。只播 ball 会丢一套。
    string BallPart(SkillDef s)
    {
        if(!NativeSkillVfx.Has(s.vfx,"flipBall"))return "ball";
        return (ballVariant++&1)==0 ? "ball" : "flipBall";
    }
    int ballVariant, hammerVariant;
    // 周期攻击（祝福之鎚）的「一发锤体」：282 的 ball/0..2 是三种铁鎚形态，
    // 不是一次性弹道。普通 Fly 从角色飞到 1~2 世界单位外的怪只要 ~70ms，
    // 肉眼根本看不见 —— 这就是「后续攻击没特效」的第二个根因。
    // 这里按发取变体、至少播 minSeconds，纯表现不结算（结算由 HitAt 统一做）。
    IEnumerator RepeatingHammer(SkillDef body,Vector2 origin,int facing,float minSeconds)
    {
        var balls=NativeSkillVfx.Parts(body.vfx,"ball");
        if(balls.Count==0)yield break;
        var clip=balls[hammerVariant++%balls.Count];
        Vector3 from=new Vector3(origin.x,origin.y+28/Art.PPU,0);
        Vector3 to=from+Vector3.right*facing*Mathf.Max(2f,ProjectileRange(body));
        var fx=Visual(body.vfx,clip.path,from,facing,null,true,360);
        float duration=Mathf.Max(minSeconds,Vector3.Distance(from,to)/ProjectileSpeed),elapsed=0;
        while(elapsed<duration)
        {
            elapsed+=Time.deltaTime;
            if(fx!=null)fx.transform.position=Vector3.Lerp(from,to,Mathf.Clamp01(elapsed/Mathf.Max(.001f,duration)));
            yield return null;
        }
        if(fx!=null)fx.Stop();
    }
    IEnumerator Fly(SkillDef s,Vector3 from,Vector3 to,Component target,int facing)
    {
        var fx=Visual(s.vfx,BallPart(s),from,facing,null,true);
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
        Vector3 from=origin+Vector2.up*(28/Art.PPU);
        float distance=ProjectileRange(s);
        var fx=Visual(s.vfx,BallPart(s),from,facing,null,true);
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
        foreach(var b in FindObjectsOfType<BossUnit>())if(!b.dead && InProjectileRange(b,s,origin,facing))targets.Add(b);
        foreach(var e in EnemyUnit.All)if(!e.dead && InProjectileRange(e,s,origin,facing))targets.Add(e);
        targets.Sort((a,b)=>
        {
            if(s.art=="illusion" && (a is BossUnit)!=(b is BossUnit))return a is BossUnit ? -1 : 1;
            return Mathf.Abs(a.transform.position.x-origin.x).CompareTo(Mathf.Abs(b.transform.position.x-origin.x));
        });
        if(targets.Count>d.TargetCount)targets.RemoveRange(d.TargetCount,targets.Count-d.TargetCount);
        return targets;
    }
    void ClearVisuals()
    {
        foreach(var fx in ownedVisuals)if(fx!=null)fx.Stop();
        ownedVisuals.Clear();
        NativeSkillVideo.ClearLayers();
    }
}
