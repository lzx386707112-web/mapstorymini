using System;
using UnityEngine;

// WZ frame origins are the cursor hotspot, not the top-left of the bitmap.
public sealed class NativeCursor : MonoBehaviour
{
    [Serializable] class Frame { public string file; public int x, y, delay; }
    [Serializable] class State { public int id; public Frame[] frames; }
    [Serializable] class Manifest { public State[] states; }
    Manifest manifest;
    int state = -1;
    float since;
    bool wasVisible;

    void Awake()
    {
        var data = Resources.Load<TextAsset>("Art/ui/cursor/manifest");
        if (data != null) manifest = JsonUtility.FromJson<Manifest>(data.text);
        wasVisible = Cursor.visible;
    }

    void Update()
    {
        if (manifest == null) return;
        Cursor.visible = !Application.isFocused;
        // 0: pointing hand; 12: pressed hand; 1: native mouse animation.
        int next = Pointer.Down || Pointer.RightDown ? 12 :
                   Time.unscaledTime - since < 1f && state == 1 ? 1 : 0;
        if (Pointer.Released || Pointer.RightReleased) next = 1;
        if (next != state) { state = next; since = Time.unscaledTime; }
    }

    void OnGUI()
    {
        if (manifest == null || !Application.isFocused || Event.current.type != EventType.Repaint) return;
        State selected = Array.Find(manifest.states, s => s.id == state);
        if (selected == null || selected.frames.Length == 0) return;
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
        var texture = Art.Tex("ui/cursor/" + frame.file);
        if (texture == null) return;
        var matrix = GUI.matrix;
        var color = GUI.color;
        int depth = GUI.depth;
        GUI.depth = -10000;
        GUI.color = Color.white;
        GUI.matrix = Matrix4x4.Scale(new Vector3(UiKit.Scale, UiKit.Scale, 1));
        GUI.DrawTexture(new Rect(Pointer.Pos.x - frame.x, Pointer.Pos.y - frame.y,
                                 texture.width, texture.height), texture);
        GUI.matrix = matrix;
        GUI.color = color;
        GUI.depth = depth;
    }

    void OnDisable() { Cursor.visible = wasVisible; }
}
