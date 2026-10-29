using System;
using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;
using Server.Field;

// CReactorPool: 原坐标/IMG origin；状态改变播放旧state/hit一次，再循环新state。
public sealed class NativeReactorView : MonoBehaviour
{
    sealed class Clip
    {
        public readonly List<Sprite> frames = new List<Sprite>();
        public readonly List<int> delays = new List<int>();
        public int duration;
        public Sprite At(long elapsed)
        {
            int t = (int)(Math.Max(0, elapsed) % duration);
            for (int i = 0; i < frames.Count; i++) { if (t < delays[i]) return frames[i]; t -= delays[i]; }
            return frames[frames.Count - 1];
        }
    }
    sealed class View
    {
        public MapReactor reactor;
        public SpriteRenderer renderer;
        public int revision = -1, state = -1;
        public Clip stand, hit;
        public long started;
    }
    readonly Dictionary<int, View> views = new Dictionary<int, View>();
    readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
    int mapId;
    public void Bind(int id) { mapId = id; }
    ImgNode Root(int id)
    {
        var seen = new HashSet<int>();
        while (seen.Add(id))
        {
            string path = "Reactor/" + id.ToString("D7") + ".img";
            if (!WzAssets.HasFile(path)) return null;
            var root = WzAssets.FileRoot(path); string link = root.Text("info/link");
            if (string.IsNullOrEmpty(link)) return root;
            id = int.Parse(link);
        }
        throw new System.IO.InvalidDataException("Reactor IMG link cycle");
    }
    Clip Load(int id, int state, bool hit)
    {
        string key = id + "/" + state + (hit ? "/hit" : "");
        if (clips.TryGetValue(key, out var clip)) return clip;
        var node = Root(id)?.Get(state + (hit ? "/hit" : ""));
        if (node == null) { clips[key] = null; return null; }
        var frames = new List<ImgNode>();
        foreach (var child in node.Children) if (int.TryParse(child.Name, out _)) frames.Add(child);
        frames.Sort((a,b) => int.Parse(a.Name).CompareTo(int.Parse(b.Name)));
        clip = new Clip();
        foreach (var child in frames)
        {
            var frame = child.TryResolve(); if (frame == null || frame.Kind != "Canvas") continue;
            int delay = Math.Max(1, Math.Abs(frame.Int("delay", 100)));
            clip.frames.Add(WzAssets.Sprite(frame, "reactor:" + key + "/" + child.Name)); clip.delays.Add(delay); clip.duration += delay;
        }
        if (clip.frames.Count == 0) clip = null;
        clips[key] = clip; return clip;
    }
    void LateUpdate() => Tick(MapReactorService.Now());
    public void Tick(long now)
    {
        var current = new HashSet<int>();
        foreach (var reactor in MapReactorService.Get(mapId))
        {
            current.Add(reactor.objectId);
            if (!views.TryGetValue(reactor.objectId, out var view))
            {
                var go = new GameObject("Reactor_" + reactor.objectId); go.transform.SetParent(transform, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                // 世界层适配：沿用现有静态NPC物件层；不改原画origin或绘制偏移。
                renderer.sortingOrder = 100;
                view = new View {reactor = reactor, renderer = renderer}; views.Add(reactor.objectId, view);
            }
            view.renderer.transform.localPosition = new Vector3(reactor.x / Art.PPU, -reactor.y / Art.PPU, 0);
            if (view.revision != reactor.visualRevision || view.state != reactor.state)
            {
                bool changed = view.revision >= 0;
                view.revision = reactor.visualRevision; view.state = reactor.state;
                view.stand = Load(reactor.id, reactor.state, false);
                view.hit = changed && reactor.previousState >= 0 ? Load(reactor.id, reactor.previousState, true) : null;
                view.started = changed ? reactor.lastHitAt : now;
            }
            long elapsed = Math.Max(0, now - view.started);
            if (view.hit != null && elapsed < view.hit.duration) view.renderer.sprite = view.hit.At(elapsed);
            else view.renderer.sprite = view.stand?.At(elapsed - (view.hit?.duration ?? 0));
        }
        foreach (int id in new List<int>(views.Keys)) if (!current.Contains(id))
        { Destroy(views[id].renderer.gameObject); views.Remove(id); }
    }
}
