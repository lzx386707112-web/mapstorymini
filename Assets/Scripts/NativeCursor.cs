using System;
using UnityEngine;

// WZ frame origins are the cursor hotspot, not the top-left of the bitmap.
public sealed class NativeCursor : MonoBehaviour
{
    [Serializable] class Frame { public string file; public int x, y, delay; }
    [Serializable] class State { public int id; public Frame[] frames; }
    [Serializable] class Manifest { public State[] states; }
    Manifest manifest;
    int state;
    float since;
    bool wasVisible;
    string activeFrame;

    void Awake()
    {
        var states = new System.Collections.Generic.List<State>();
        foreach (var node in WzAssets.Node("UI/Basic.img/Cursor").Children)
        {
            if (!int.TryParse(node.Name, out var id)) continue;
            var frames = new System.Collections.Generic.List<Frame>();
            foreach (var frame in WzAssets.Frames(node))
            {
                var n = frame.Resolve(); var origin = WzAssets.Origin(n);
                frames.Add(new Frame { file = "wz://UI/Basic.img/Cursor/" + id + "/" + frame.Name,
                    x = (int)origin.x, y = (int)origin.y, delay = Math.Abs(n.Int("delay", 100)) });
            }
            states.Add(new State { id = id, frames = frames.ToArray() });
        }
        manifest = new Manifest { states = states.ToArray() };
        wasVisible = Cursor.visible;
    }

    void Update()
    {
        if (manifest == null) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = !Application.isMobilePlatform;
        // 0: pointing hand; 12: pressed hand; 1: native mouse animation.
        int next = Pointer.Down || Pointer.RightDown ? 12 :
                   Time.unscaledTime - since < 1f && state == 1 ? 1 : 0;
        if (Pointer.Released || Pointer.RightReleased) next = 1;
        if (next != state) { state = next; since = Time.unscaledTime; }
        if (!Application.isMobilePlatform && Application.isFocused)
        {
            var selected = SelectState(state);
            if (selected == null) return;
            var frame = CurrentFrame(selected);
            var texture = Art.Tex(frame.file);
            if (texture != null && activeFrame != frame.file)
            {
                Cursor.SetCursor(texture, new Vector2(Mathf.Clamp(frame.x, 0, texture.width - 1), Mathf.Clamp(frame.y, 0, texture.height - 1)), CursorMode.ForceSoftware);
                activeFrame = frame.file;
            }
        }
    }

    State SelectState(int id) => Array.Find(manifest.states, s => s.id == id && s.frames.Length > 0)
        ?? Array.Find(manifest.states, s => s.id == 0 && s.frames.Length > 0);

    Frame CurrentFrame(State selected)
    {
        float elapsed = (Time.unscaledTime - since) * 1000f;
        int duration = 0;
        foreach (var f in selected.frames) duration += Mathf.Max(1, f.delay);
        elapsed %= duration;
        Frame frame = selected.frames[0];
        foreach (var f in selected.frames)
        {
            frame = f;
            elapsed -= Mathf.Max(1, f.delay);
            if (elapsed < 0) break;
        }
        return frame;
    }

    void OnGUI()
    {
        // Set depth on every IMGUI event and retain it for this behaviour.
        // Restoring it during Repaint lets the lobby paint over the cursor.
        GUI.depth = -10000;
        if (!Application.isMobilePlatform || manifest == null || !Application.isFocused || Event.current.type != EventType.Repaint) return;
        State selected = SelectState(state);
        if (selected == null || selected.frames.Length == 0) return;
        Frame frame = CurrentFrame(selected);
        var texture = Art.Tex(frame.file);
        if (texture == null) { Cursor.visible = true; return; }
        var matrix = GUI.matrix;
        var color = GUI.color;
        GUI.color = Color.white;
        GUI.matrix = Matrix4x4.Scale(new Vector3(UiKit.Scale, UiKit.Scale, 1));
        GUI.DrawTexture(new Rect(Pointer.Pos.x - frame.x, Pointer.Pos.y - frame.y,
                                 texture.width, texture.height), texture);
        GUI.matrix = matrix;
        GUI.color = color;
    }

    void OnDisable() { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); Cursor.visible = wasVisible; }
}
