using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Independent screen-center IMG plane: weather < skillback < map hills < tiles.
public sealed class NativeSkillBackdrop : MonoBehaviour
{
    public const int SortingOrder = -900;
    static readonly string[] markers = {
        "Map/Effect.img/customSkill/skillBackdrop/videoLayer",
        "Map/Effect.img/customSkill/dawnWarrior/galaxyStarBurstVideoLayer",
        "Map/Effect.img/customSkill/dawnWarrior/soulEclipseVideoLayer",
        "Map/Effect.img/customSkill/dawnWarrior/eclipseForceVideoLayer" };
    static NativeSkillBackdrop current;
    FieldDirector field;
    NativeSkillBackdropState state;
    int revision;
    string path;
    double expires, started;
    bool ownsVideo;
    SpriteRenderer image;
    readonly List<Sprite> frames = new List<Sprite>();
    readonly List<double> ends = new List<double>();
    double cycle;

    public static void Enter(FieldDirector field)
    {
        Leave();
        var go = new GameObject("NativeSkillBackdrop");
        current = go.AddComponent<NativeSkillBackdrop>(); current.field = field;
        current.state = GameSession.Server?.Backdrops.Map(field.Map.id);
        current.revision = -1; current.RefreshMap();
    }
    public static void Leave()
    {
        if (current == null) return;
        current.Drop(); current.gameObject.SetActive(false); Destroy(current.gameObject); current = null;
    }
    public static void ShowMap(string path, int duration)
    {
        if (current?.state == null) return;
        current.state.Show(path,duration,Time.realtimeSinceStartupAsDouble*1000);
        current.RefreshMap();
    }
    public static void ShowLocal(string path, int duration)
    {
        if (current == null) return;
        string normalized = NativeSkillBackdropPacket.Normalize(path);
        current.Show(normalized,Math.Max(0,Math.Min(NativeSkillBackdropPacket.MaxDuration,duration)));
    }
    public static bool HandlePacket(byte[] packet, int offset = 0)
    {
        if (!NativeSkillBackdropPacket.TryRead(packet,offset,out var path,out var duration)) return false;
        if (current != null) current.Show(path,duration);
        return true;
    }
    void RefreshMap()
    {
        if (state == null) return;
        int remaining = state.Remaining(Time.realtimeSinceStartupAsDouble*1000);
        if (state.Revision == revision) return;
        revision = state.Revision; Show(state.Path,remaining);
    }
    void Show(string requested, int duration)
    {
        Drop(); path = requested;
        expires = duration > 0 ? Time.realtimeSinceStartupAsDouble + duration/1000.0 : 0;
        started = Time.realtimeSinceStartupAsDouble;
        if (path == null) return;
        // Native consumes NeedRebuild once; a failed resource is not retried every frame.
        try
        {
            bool mcv = path.EndsWith(".mcv",StringComparison.Ordinal);
            ImgNode node = mcv ? null : TryNode(path);
            if (node == null) foreach (string fallback in markers) { node = TryNode(fallback); if (node != null) break; }
            if (node == null) { Debug.LogWarning("[Skillback] Missing original marker"); return; }
            if (mcv)
            {
                string name = path.Substring(path.LastIndexOf('/')+1);
                ownsVideo = NativeSkillVideo.Play(NativeSkillVideo.Channel.PlayerSkill,"Video/"+name,null,SortingOrder);
                return;
            }
            AddFrames(node);
            if (frames.Count == 0) return;
            image = gameObject.AddComponent<SpriteRenderer>(); image.sortingOrder = SortingOrder; image.sprite = frames[0];
        }
        catch (Exception e) { Debug.LogWarning("[Skillback] " + e.Message); }
    }
    static ImgNode TryNode(string path)
    {
        int split = path.IndexOf(".img/",StringComparison.Ordinal);
        if (split < 0 || !WzAssets.HasFile(path.Substring(0,split+4))) return null;
        return WzAssets.Node(path);
    }
    void AddFrames(ImgNode node)
    {
        node = node.Resolve();
        if (node.Kind == "Canvas") Add(node);
        else for (int i = 0; ; i++) { var frame = node.Get(i.ToString())?.Resolve(); if (frame == null) break; Add(frame); }
    }
    void Add(ImgNode node)
    {
        if (node.Kind != "Canvas") return;
        frames.Add(WzAssets.Sprite(node));
        cycle += Math.Max(1,Math.Abs(node.Int("delay",100)))/1000.0;
        ends.Add(cycle);
    }
    void LateUpdate()
    {
        if (field == null || GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Playing) { Leave(); return; }
        RefreshMap();
        double now = Time.realtimeSinceStartupAsDouble;
        if (path != null && expires > 0 && now >= expires) Show(null,0);
        if (image == null || Camera.main == null) return;
        var center = Camera.main.transform.position; center.z = 0; transform.position = center;
        double age = (now-started)%cycle;
        int frame = 0; while (frame < ends.Count-1 && age >= ends[frame]) frame++;
        image.sprite = frames[frame];
    }
    void Drop()
    {
        if (ownsVideo) NativeSkillVideo.Stop(NativeSkillVideo.Channel.PlayerSkill);
        ownsVideo = false;
        if (image != null) { image.enabled = false; Destroy(image); image = null; }
        frames.Clear(); ends.Clear(); cycle = 0;
    }
    void OnDisable() => Drop();
}
