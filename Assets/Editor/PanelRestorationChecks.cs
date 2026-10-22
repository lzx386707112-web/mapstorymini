using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Isolated profile fixtures: never replace or save the player's real profile.
public static class PanelRestorationChecks
{
    public static void Run()
    {
        var novice = new PlayerProfileData();
        LevelSystem.SetLevel(novice, 6);
        Require(novice.ap == 0 && novice.attributes.str == 37 && novice.attributes.dex == 5, "novice early auto AP");
        LevelSystem.SetLevel(novice, 11);
        Require(novice.ap == 0 && novice.attributes.str == 57 && novice.attributes.dex == 10, "novice late auto AP");
        novice.cls = "knight";
        LevelSystem.SetLevel(novice, 12);
        Require(novice.ap == 5, "advanced job earns manual AP");
        var old = new PlayerProfileData { level = 6, ap = 25 };
        LevelSystem.MigrateNoviceAp(old);
        Require(old.ap == 0 && old.attributes.str == 37, "legacy AP migration");
        LevelSystem.MigrateNoviceAp(old);
        Require(old.attributes.str == 37, "migration is idempotent");
        Debug.Log("[PanelRestoration] PASS: auto AP, job AP, legacy migration");
        SessionState.SetBool("PanelRestoration", true);
        PlaySmokeTest.Run();
    }
    static void Require(bool ok, string name)
    {
        if (!ok) throw new System.Exception("[PanelRestoration] " + name);
    }
    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool("PanelRestoration", false) || !EditorApplication.isPlaying || GameLoop.I == null || GameLoop.I.player == null) return;
        SessionState.SetBool("PanelRestoration", false);
        new GameObject("Panel restoration review").AddComponent<PanelRestorationProbe>();
    }
}
public class PanelRestorationProbe : MonoBehaviour
{
    static void Set(System.Type type, string name, object value)
        => type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(1);
        Set(typeof(Profile), "debugOverride", true);
        var fixture = new PlayerProfileData { baseMaxHp = 50, baseMaxMp = 5, playerName = "ecbd" };
        Set(typeof(Profile), "data", fixture);
        fixture.ap = 5;
        if (Profile.AssignAp(0) || fixture.ap != 5 || fixture.attributes.str != 12)
            throw new System.Exception("novice manual AP must be rejected");
        fixture.ap = 0;
        GameLoop.I.player.SendMessage("ApplyLoadout");
        SkillWindow.Visible = false;
        StatWindow.Visible = true;
        InvenWindow.Visible = false;
        Set(typeof(StatWindow), "pos", new Vector2(20, 70));
        yield return new WaitForSecondsRealtime(.5f);
        ScreenCapture.CaptureScreenshot("docs/stat-window-beginner-restored.png");
        yield return new WaitForSecondsRealtime(.5f);
        fixture.cls = "knight";
        fixture.ap = 5;
        if (!Profile.AssignAp(0) || fixture.ap != 4 || fixture.attributes.str != 13)
            throw new System.Exception("manual AP transaction failed");
        fixture.ap = 0;
        if (Profile.AssignAp(0)) throw new System.Exception("AP cannot be overspent");
        fixture.ap = 4;
        fixture.attributes.dex = 32767;
        if (Profile.AssignAp(1) || fixture.ap != 4) throw new System.Exception("AP cap must be enforced");
        fixture.attributes.dex = 5;
        yield return new WaitForSecondsRealtime(.5f);
        ScreenCapture.CaptureScreenshot("docs/stat-window-job-restored.png");
        yield return new WaitForSecondsRealtime(.5f);
        StatWindow.Visible = false;
        InvenWindow.Visible = true;
        Set(typeof(InvenWindow), "expanded", false);
        yield return new WaitForSecondsRealtime(.5f);
        ScreenCapture.CaptureScreenshot("docs/inventory-small-restored.png");
        yield return new WaitForSecondsRealtime(.5f);
        Set(typeof(InvenWindow), "expanded", true);
        yield return new WaitForSecondsRealtime(.5f);
        ScreenCapture.CaptureScreenshot("docs/inventory-full-restored.png");
        Debug.Log("[PanelRestoration] PASS: AP transaction and four live UI screenshots");
    }
}
