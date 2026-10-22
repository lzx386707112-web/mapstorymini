using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

public static class NativeUiReview
{
    public static void Run()
    {
        SessionState.SetBool("NativeUiReview", true);
        HudChecks.SetGameViewSize(1280, 720);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool("NativeUiReview", false) || !EditorApplication.isPlaying || GameLoop.I == null || GameLoop.I.player == null) return;
        SessionState.SetBool("NativeUiReview", false);
        new GameObject("Native UI review").AddComponent<NativeUiReviewProbe>();
    }
}
public class NativeUiReviewProbe : MonoBehaviour
{
    IEnumerator Start()
    {
        Application.runInBackground = true;
        yield return new WaitForSecondsRealtime(2);
        if (GameLoop.I.state != GameLoop.GameState.Playing || Profile.cls != "beginner" || FindObjectOfType<Lobby>() != null)
        { Debug.LogError("[NativeUiReview] boot failed"); EditorApplication.Exit(1); yield break; }
        foreach (string path in new[] { "UI/UIWindow.img/Stat/backgrnd", "UI/UIWindow.img/Equip/backgrnd", "UI/UIWindow.img/Item/backgrnd", "UI/Basic.img/Cursor/0/0", "Skill/000.img/skill/0001000/icon" })
            if (Art.Tex("wz://" + path) == null) { Debug.LogError("[NativeUiReview] missing " + path); EditorApplication.Exit(1); yield break; }
        for (int i = 0; i < 5; i++)
        {
            StatWindow.Visible = i == 1; EquipWindow.Visible = i == 2; SkillWindow.Visible = i == 3;
            if (i == 4) KeyConfigWindow.Toggle();
            yield return new WaitForSecondsRealtime(.5f);
            ScreenCapture.CaptureScreenshot("/tmp/mapledash-native-ui-" + i + ".png");
            yield return new WaitForSecondsRealtime(.5f);
        }
        Debug.Log("[NativeUiReview] PASS: direct map boot, beginner, original panel/cursor/skill textures");
        EditorApplication.Exit(0);
    }
}
