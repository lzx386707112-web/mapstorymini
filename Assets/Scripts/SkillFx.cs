using UnityEngine;

// 原 IMG 分支/帧元数据直接播放，不再只消费旧 manifest 的 cast。
public class SkillFx : MonoBehaviour
{
    NativeVfxClip clip;
    SpriteRenderer sr;
    float speed=1, frameElapsed;
    int index, baseOrder;
    bool loop, hasFollow;
    EnemyUnit enemy;
    BossUnit boss;
    public Transform follow;
    public float followOffsetX;
    Vector3 followOffset;
    public string SkillKey { get; private set; }
    public string Part => clip.path;
    public Sprite CurrentSprite => sr.sprite;
    public static SkillFx Play(string key,Vector3 anchor,int facing,float speed=1,int order=360)
        => PlayPart(key,"effect",anchor,facing,speed,order);

    public static SkillFx PlayPart(string key,string part,Vector3 anchor,int facing,float speed=1,int order=360,bool loop=false)
    {
        part=NativeSkillVfx.Alias(part);
        var clip=NativeSkillVfx.Get(key,part);
        if(clip==null)
        {
            var alternatives=NativeSkillVfx.Parts(key,part);
            if(alternatives.Count==0) return null;
            clip=alternatives[0];
        }
        var go=new GameObject("SkillFx_"+key+"_"+clip.path.Replace('/','_'));
        go.transform.position=anchor;
        var fx=go.AddComponent<SkillFx>();fx.clip=clip;fx.SkillKey=key;fx.speed=Mathf.Max(.1f,speed);fx.loop=loop;fx.baseOrder=order;
        fx.sr=go.AddComponent<SpriteRenderer>();
        fx.sr.flipX=facing>0;
        fx.ShowFrame();
        if(fx.sr.sprite==null){Destroy(go);return null;}
        return fx;
    }
    public void Follow(Transform target)
    {
        follow=target;hasFollow=true;enemy=target.GetComponent<EnemyUnit>();boss=target.GetComponent<BossUnit>();followOffset=transform.position-target.position;
    }
    public void Stop() { gameObject.SetActive(false); Destroy(gameObject); }
    void ShowFrame()
    {
        var f=clip.frames[index];sr.sprite=Art.FrameAt(f.file,f.origin);sr.sortingOrder=baseOrder+f.z;
        sr.color=new Color(1,1,1,f.alpha0);
    }
    void Update()
    {
        if(clip==null)return;
        if(hasFollow && (follow==null || !follow.gameObject.activeInHierarchy || (enemy!=null && enemy.dead) || (boss!=null && boss.dead)))
        {
            // 头顶循环状态随目标结束；一次性命中动画在最后位置播完。
            if(loop){Stop();return;}
            follow=null;hasFollow=false;
        }
        if(follow!=null)transform.position=follow.position+followOffset+Vector3.right*followOffsetX;
        float dt=Time.deltaTime*speed;frameElapsed+=dt;
        while(frameElapsed>=clip.frames[index].delay)
        {
            frameElapsed-=clip.frames[index].delay;index++;
            if(index>=clip.frames.Length)
            {
                if(!loop){Stop();return;}
                index=0;
            }
            ShowFrame();
        }
        var f=clip.frames[index];sr.color=new Color(1,1,1,Mathf.Lerp(f.alpha0,f.alpha1,frameElapsed/f.delay));
    }
}
