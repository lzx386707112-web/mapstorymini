using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

// End-to-end check: the real boot path must render the native hand above the lobby.
public static class CursorLobbyChecks
{
    const string RunKey = "CursorLobbyChecks.running";
    public static void Run()
    {
        SessionState.SetBool(RunKey, true);
        SessionState.SetInt("CursorLobbyChecks.autostart", PlayerPrefs.GetInt("autostart", 0));
        PlayerPrefs.SetInt("autostart", 0);
        HudChecks.SetGameViewSize(1280, 720);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool(RunKey, false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool(RunKey, false);
        new GameObject("Cursor lobby check").AddComponent<CursorLobbyProbe>();
    }

    public static void Finish(int code)
    {
        PlayerPrefs.SetInt("autostart", SessionState.GetInt("CursorLobbyChecks.autostart", 0));
        EditorApplication.Exit(code);
    }
}

public class CursorLobbyProbe : MonoBehaviour
{
    void LateUpdate() { Pointer.Pos = new Vector2(640, 360); }
    IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(2);
        yield return new WaitForEndOfFrame();
        try
        {
            if (GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Lobby)
                throw new Exception("Boot did not enter lobby");
            if (FindObjectOfType<NativeCursor>() == null) throw new Exception("Cursor component missing in lobby");
            var hand = Art.Tex("ui/cursor/0_0.bytes");
            if (hand == null) throw new Exception("Cursor texture missing");
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            System.IO.File.WriteAllBytes("/tmp/mapledash-lobby-cursor.png", shot.EncodeToPNG());
            int tested = 0, matched = 0;
            float scale = UiKit.Scale;
            // Compare opaque pixels against the actual composited screen, including IMGUI.
            for (int y = 0; y < hand.height; y++)
                for (int x = 0; x < hand.width; x++)
                {
                    var expected = hand.GetPixel(x, y);
                    if (expected.a < 0.99f) continue;
                    int sx = Mathf.FloorToInt((640 + x + 0.5f) * scale);
                    int sy = shot.height - 1 - Mathf.FloorToInt((360 - 3 + hand.height - y - 0.5f) * scale);
                    var actual = shot.GetPixel(sx, sy);
                    tested++;
                    if (Mathf.Abs(actual.r - expected.r) + Mathf.Abs(actual.g - expected.g) +
                        Mathf.Abs(actual.b - expected.b) < 0.08f) matched++;
                }
            Debug.Log("[CursorLobby] composited native pixels=" + matched + "/" + tested);
            if (tested == 0 || matched < tested * 0.9f) throw new Exception("Lobby obscures native cursor; inspect screenshot");
            Debug.Log("[CursorLobby] PASS: native hand visible above initial lobby");
            Destroy(shot);
            CursorLobbyChecks.Finish(0);
        }
        catch (Exception e) { Debug.LogException(e); CursorLobbyChecks.Finish(1); }
    }
}
