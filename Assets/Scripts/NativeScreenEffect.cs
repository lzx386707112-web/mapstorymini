using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// 屏幕特效（源端 showEffect）。
//
// EventInstanceManager.showClearEffect（:1293-1329）四个重载最终都发：
//   showEffect("quest/party/clear") + playSound("Party1/Clear")，hasGate 时再加
//   environmentChange("gate", 2)。
// 素材就是 Map/Effect.img/quest/party/clear：4 帧（0..2 各 130ms、3 是 2000ms 的停留帧），
// 每帧自带 origin（299,184 / 255,180 / 183,129 / 183,93），画的时候按 origin 对齐锚点。
//
// 这里的锚点取屏幕中心：showEffect 是客户端屏幕特效（不是地图对象特效），
// 与 FIELD_EFFECT 那套走 Map/Obj 坐标的入口不是同一条路。
// 音效系统尚未迁移（无 AudioManager），所以只播动画。
public static class NativeScreenEffect
{
    sealed class Playing
    {
        public List<ImgNode> Frames;
        public float Started;
        public float Total;
    }

    static readonly List<Playing> active = new List<Playing>();

    public static int ActiveCount => active.Count;

    public static void Play(string path)
    {
        var node = WzAssets.Node(path);
        if (node == null) return;
        var frames = WzAssets.Frames(node);
        if (frames.Count == 0) return;
        float total = 0f;
        foreach (var frame in frames) total += Mathf.Max(1, frame.Int("delay", 120));
        active.Add(new Playing { Frames = frames, Started = Time.unscaledTime, Total = total });
    }

    public static void Clear() => active.Clear();

    public static void Draw()
    {
        if (active.Count == 0) return;
        float elapsedMs = 0f;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var playing = active[i];
            elapsedMs = (Time.unscaledTime - playing.Started) * 1000f;
            if (elapsedMs >= playing.Total) { active.RemoveAt(i); continue; }
            var frame = FrameAt(playing.Frames, elapsedMs);
            if (frame == null) continue;
            var texture = WzAssets.Texture(frame);
            if (texture == null) continue;
            // origin 是锚点相对帧左上角的偏移，所以左上角 = 锚点 - origin。
            var origin = WzAssets.Origin(frame);
            var rect = new Rect(UiKit.W * 0.5f - origin.x, UiKit.H * 0.5f - origin.y,
                texture.width, texture.height);
            GUI.DrawTexture(rect, texture);
        }
    }

    static ImgNode FrameAt(List<ImgNode> frames, float elapsedMs)
    {
        float cursor = 0f;
        for (int i = 0; i < frames.Count; i++)
        {
            cursor += Mathf.Max(1, frames[i].Int("delay", 120));
            if (elapsedMs < cursor) return frames[i];
        }
        return frames[frames.Count - 1];
    }
}
