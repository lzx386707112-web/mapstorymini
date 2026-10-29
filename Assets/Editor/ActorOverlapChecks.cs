using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Render actual IMG frames separately and together: where both are opaque,
// the composite must retain the player's pixels across all animation changes.
public static class ActorOverlapChecks
{
    static Color32[] Capture(Camera camera, RenderTexture target, Texture2D copy)
    {
        camera.Render();
        RenderTexture.active = target;
        copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        copy.Apply();
        return copy.GetPixels32();
    }
    public static void Run()
    {
        var cameraObject = new GameObject("OverlapCamera");
        var playerObject = new GameObject("OverlapPlayer");
        var mobObject = new GameObject("OverlapMob");
        var target = new RenderTexture(128, 128, 24);
        var copy = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 64 / Art.PPU;
            camera.transform.position = new Vector3(0, 32 / Art.PPU, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << 30; camera.targetTexture = target;
            var player = playerObject.AddComponent<PlayerCtrl>();
            var mob = mobObject.AddComponent<EnemyUnit>();
            var p = Art.Attach(playerObject.transform, "chars", "knight_h0", "stand", 0);
            var m = Art.Attach(mobObject.transform, "mobs", "snail", "stand", 0);
            p.gameObject.layer = m.gameObject.layer = 30;
            var apply = typeof(SpriteAnim).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance);
            int cases = 0, pixels = 0, tiedCases = 0;
            foreach (int lane in new[] { 0, 2, 4 })
            {
                player.lane = mob.lane = lane;
                p.Renderer.sortingOrder = player.SortingOrder;
                m.Renderer.sortingOrder = mob.BodySortingOrder;
                if (p.Renderer.sortingOrder <= m.Renderer.sortingOrder) throw new Exception("Actor sort tie/inversion");
                foreach (string action in new[] { "stand", "alert", "walk" })
                foreach (string monsterAction in new[] { "stand", "walk", "hit" })
                foreach (int frame in new[] { 0, 1, 2 })
                {
                    p.Play(action, false, true); m.Play(monsterAction, false, true);
                    apply.Invoke(p, new object[] { frame });
                    p.Renderer.enabled = true; m.Renderer.enabled = false;
                    var playerPixels = Capture(camera, target, copy);
                    p.Renderer.enabled = false; m.Renderer.enabled = true;
                    var mobPixels = Capture(camera, target, copy);
                    p.Renderer.enabled = true;
                    var composite = Capture(camera, target, copy);
                    m.Renderer.sortingOrder = p.Renderer.sortingOrder;
                    var tied = Capture(camera, target, copy);
                    m.Renderer.sortingOrder = mob.BodySortingOrder;
                    int overlap = 0;
                    bool tiedDiffers = false;
                    for (int i = 0; i < composite.Length; i++)
                    {
                        if (playerPixels[i].a != 255 || mobPixels[i].a != 255) continue;
                        var expected = playerPixels[i]; var actual = composite[i];
                        if (Math.Abs(expected.r - tied[i].r) > 1 || Math.Abs(expected.g - tied[i].g) > 1 ||
                            Math.Abs(expected.b - tied[i].b) > 1) tiedDiffers = true;
                        if (Math.Abs(expected.r - actual.r) > 1 || Math.Abs(expected.g - actual.g) > 1 ||
                            Math.Abs(expected.b - actual.b) > 1) throw new Exception("Actor overlap swapped at " + action + "/" + frame + "/" + monsterAction);
                        overlap++;
                    }
                    if (overlap == 0) throw new Exception("No opaque overlap pixels; render check invalid");
                    pixels += overlap; cases++;
                    if (tiedDiffers) tiedCases++;
                }
            }
            Debug.Log("[ActorOverlapChecks] PASS " + cases + " original IMG pose/frame/lane combinations, " + pixels + " overlap pixels; player consistently above ordinary mob; old equal-order differed in " + tiedCases + " cases");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(playerObject);
            UnityEngine.Object.DestroyImmediate(mobObject);
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(copy);
        }
    }
}
