using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Original Canvas frames: retain origin, per-frame delay/alpha and explicit lifetime.
public sealed class NativeImgAnimation : MonoBehaviour
{
    List<ImgNode> frames;
    SpriteRenderer image;
    double started, expires;
    bool repeat;
    public Color Tint = Color.white;
    public SpriteRenderer Image => image;
    public static NativeImgAnimation Spawn(string path, Transform parent, Vector3 position, int order,
        bool flip=false, bool repeat=false, float lifetime=0)
    {
        var root=WzAssets.Node(path);if(root==null)return null;
        var frames=WzAssets.Frames(root);if(frames.Count==0)return null;
        var go=new GameObject(path);go.transform.SetParent(parent,false);go.transform.position=position;
        var a=go.AddComponent<NativeImgAnimation>();a.frames=frames;a.repeat=repeat;
        a.started=Time.realtimeSinceStartupAsDouble;a.expires=lifetime>0?a.started+lifetime:double.PositiveInfinity;
        if(lifetime<=0 && !repeat && frames.Count>1)
        {double duration=0;foreach(var f in frames)duration+=Mathf.Max(1,Mathf.Abs(f.Int("delay",100)))/1000.0;a.expires=a.started+duration;}
        a.image=go.AddComponent<SpriteRenderer>();a.image.sortingOrder=order;a.image.flipX=flip;a.Tick(a.started);
        return a;
    }
    public void Tick(double now)
    {
        if(now>=expires){Remove();return;}
        double total=0;foreach(var f in frames)total+=Mathf.Max(1,Mathf.Abs(f.Int("delay",100)))/1000.0;
        double age=now-started;
        if(age>=total && !repeat){if(frames.Count==1)age=0;else{image.enabled=false;return;}}
        if(repeat)age%=total;
        foreach(var f in frames)
        {
            double duration=Mathf.Max(1,Mathf.Abs(f.Int("delay",100)))/1000.0;
            if(age<duration)
            {
                image.sprite=WzAssets.Sprite(f);var color=Tint;
                color.a*=Mathf.Lerp(f.Int("a0",255),f.Int("a1",255),(float)(age/duration))/255f;
                image.color=color;return;
            }
            age-=duration;
        }
    }
    void Update()=>Tick(Time.realtimeSinceStartupAsDouble);
    public void Remove(){if(Application.isPlaying)Destroy(gameObject);else DestroyImmediate(gameObject);}
}
