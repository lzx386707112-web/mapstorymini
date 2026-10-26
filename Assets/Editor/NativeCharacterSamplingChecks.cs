using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Render the same cached native frame at 1:1 and mobile viewport scale.
// No gameplay session, database, or preferences are changed.
public static class NativeCharacterSamplingChecks
{
    public static void Run()
    {
        var previousCamera = Camera.main;
        string previousTag = previousCamera != null ? previousCamera.tag : null;
        var cameraObject = new GameObject("SamplingCheckCamera");
        var actor = new GameObject("SamplingCheckCharacter");
        Texture2D texture = null;
        FilterMode previousFilter = FilterMode.Point;
        try
        {
            if (previousCamera != null) previousCamera.tag = "Untagged";
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 96f / Art.PPU;
            camera.transform.position = new Vector3(0, 40f / Art.PPU, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(180, 180, 180, 255);
            camera.cullingMask = 1 << 30;
            actor.layer = 30;
            var renderer = actor.AddComponent<SpriteRenderer>();
            var animation = actor.AddComponent<SpriteAnim>();
            animation.Init("chars", "knight_h0", "stand", renderer);
            if (renderer.sprite == null) throw new System.Exception("Native character frame missing");
            texture = renderer.sprite.texture;
            previousFilter = texture.filterMode;
            var update = typeof(SpriteAnim).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Directory.CreateDirectory("Library/UiInputAnalysis");
            foreach (int size in new[] { 192, 270, 192 })
            {
                var target = new RenderTexture(size, size, 24);
                var capture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var previousTarget = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    update.Invoke(animation, null);
                    var expected = size == 192 ? FilterMode.Point : FilterMode.Bilinear;
                    if (texture.filterMode != expected) throw new System.Exception("Viewport sampling mismatch: " + size);
                    camera.Render();
                    RenderTexture.active = target;
                    capture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    capture.Apply();
                    File.WriteAllBytes("Library/UiInputAnalysis/sampling-" + size + ".png", capture.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previousTarget;
                    Object.DestroyImmediate(capture);
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }
            Debug.Log("[NativeCharacterSamplingChecks] PASS native/mobile/native viewport transitions");
        }
        finally
        {
            if (texture != null) texture.filterMode = previousFilter;
            Object.DestroyImmediate(actor);
            Object.DestroyImmediate(cameraObject);
            if (previousCamera != null) previousCamera.tag = previousTag;
        }
    }
}
