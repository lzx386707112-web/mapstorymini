using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Original Direction*.img command streams. No reconstructed artwork or screen-sized scaling.
public sealed class NativeMapIntro : MonoBehaviour
{
    sealed class MovingLayer
    {
        public NativeImgAnimation Image;public NativeIntroTimeline.Command Command;public long Started;
    }
    NativeIntroTimeline scene;
    readonly List<MovingLayer> layers=new List<MovingLayer>();
    FieldDirector field;
    object character;
    bool ownsLock;
    int previousLock;
    static long Now=>(long)(Time.realtimeSinceStartupAsDouble*1000);
    public static void Enter(FieldDirector field) => field.GetComponent<NativeMapIntro>()?.Clear();
    public static bool Active(FieldDirector field) => field.GetComponent<NativeMapIntro>()?.scene!=null;
    public static NativeMapIntro Show(string path,bool lockUi=false)
    {
        var field=GameLoop.I?.field;
        if(field==null)throw new InvalidOperationException("Intro requires an active original field");
        var host=field.GetComponent<NativeMapIntro>() ?? field.gameObject.AddComponent<NativeMapIntro>();
        host.Begin(field,path,lockUi,Now);return host;
    }
    public void Begin(FieldDirector owner,string path,bool lockUi,long now)
    {
        var player=GameLoop.I?.player;
        var next=new NativeIntroTimeline(WzAssets.Node(path),now,(int)((player?.worldX ?? 0)*Art.PPU),(int)(-(player?.FeetPos.y ?? 0)*Art.PPU));
        // Validate every visual before locking controls or showing a partial scene.
        foreach(var command in next.Commands)
        {
            if(command.Type==0)
            {
                var variants=Variants(command.Visual);
                if(variants.Count==0)throw new InvalidOperationException("Missing intro variants: "+command.Visual);
                foreach(var variant in variants)
                    if(WzAssets.Frames(variant).Count==0)throw new InvalidOperationException("Missing intro frames: "+command.Visual+"/"+variant.Name);
            }
            if(!string.IsNullOrEmpty(command.Sound))
            {
                if(!AudioManager.Has(command.Sound))throw new InvalidOperationException("Unexported original intro sound: "+command.Sound);
                AudioManager.Preload(command.Sound);
            }
        }
        Clear();field=owner;character=GameSession.Character;scene=next;
        previousLock=PlayerCtrl.ScriptUiLock;ownsLock=lockUi;
        if(ownsLock)PlayerCtrl.ScriptUiLock++;
    }
    static List<ImgNode> Variants(string path)
    {
        var root=WzAssets.Node(path);var result=new List<ImgNode>();
        if(root!=null)for(int i=0;;i++) {var child=root.Get(i.ToString());if(child==null)break;result.Add(child);}
        return result;
    }
    public void Tick(long now)
    {
        if(scene==null)return;
        if(!ReferenceEquals(character,GameSession.Character)) {Clear();return;}
        scene.Tick(now,command=>
        {
            if(command.Type==2) {field.WarpTo(command.Field,null,0);return;}
            if(command.Type==5) {AudioManager.Play(command.Sound,1,false);return;}
            var variants=Variants(command.Visual);var variant=variants[UnityEngine.Random.Range(0,variants.Count)];
            float lifetime=0;foreach(var frame in WzAssets.Frames(variant))lifetime+=Math.Max(1,Math.Abs(frame.Int("delay",100)))/1000f;
            // Native world-space layer with null origin/overlay (4398F6 -> 43EA3E).
            // Preserve z within the scene band; map art is grouped at -400 in Unity.
            var image=NativeImgAnimation.SpawnAt(variant,transform,new Vector3(command.X/Art.PPU,-command.Y/Art.PPU,0),500+command.Z,false,false,lifetime);
            if(image==null)throw new InvalidOperationException("Cannot display original intro: "+command.Visual);
            layers.Add(new MovingLayer {Image=image,Command=command,Started=now});
            if(!string.IsNullOrEmpty(command.Sound))AudioManager.Play(command.Sound,1,false);
        });
        foreach(var layer in layers)
        {
            if(layer.Image==null)continue;
            var command=layer.Command;
            long elapsed=now-layer.Started;
            layer.Image.transform.position=new Vector3((command.X+command.Displacement(command.MoveX,elapsed))/Art.PPU,
                -(command.Y+command.Displacement(command.MoveY,elapsed))/Art.PPU,0);
        }
        layers.RemoveAll(layer=>layer.Image==null);
    }
    public void Clear()
    {
        scene=null;
        foreach(var layer in layers)if(layer.Image!=null)layer.Image.Remove();
        layers.Clear();
        if(ownsLock)PlayerCtrl.ScriptUiLock=previousLock;
        ownsLock=false;
    }
    void Update()=>Tick(Now);
    void OnDestroy()=>Clear();
}
