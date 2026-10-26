using System;
using System.Collections;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Isolated in-memory fixture. Does not open a server or modify a player's inventory.
public static class NativeCashSceneChecks {
    public static void Run() {
        SessionState.SetBool("NativeCashSceneChecks",true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        HudChecks.SetGameViewSize(800,600);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Hook() { EditorApplication.update += Tick; }
    static void Tick() {
        if (!SessionState.GetBool("NativeCashSceneChecks",false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool("NativeCashSceneChecks",false);
        new GameObject("CashSceneFixture").AddComponent<NativeCashSceneProbe>();
    }
}
public class NativeCashSceneProbe : MonoBehaviour {
    Vector2? click;
    void OnGUI() {
        UiKit.Begin();
        if (Event.current.type == EventType.Repaint && click.HasValue) {
            Rect scene = NativeCashWindow.SceneRect(UiKit.W,UiKit.H);
            Pointer.Pos = scene.position + click.Value * (scene.width / 800);
            Pointer.Clicked = true;
            click = null;
        }
        NativeCashWindow.Draw();
    }
    static object Get(string field) => typeof(NativeCashWindow).GetField(field,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
    static void Set(string field, object value) => typeof(NativeCashWindow).GetField(field,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value);
    static void Check(bool value, string message) { if (!value) throw new Exception("[NativeCashSceneChecks] " + message); }
    IEnumerator Click(float x, float y) { click = new Vector2(x,y); yield return new WaitForEndOfFrame(); yield return null; }
    IEnumerator Capture(string path) {
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(path,shot.EncodeToPNG());
        Destroy(shot);
    }
    IEnumerator Start() {
        Application.runInBackground = true;
        var camera = new GameObject("CashSceneCamera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var state = new CashResponse { character = new FullCharacter {
            record = new CharRecord { name="商城检查",face=20000,hair=30000 },
            inventory = new System.Collections.Generic.List<InventoryEntry>() }, credit=123456,points=2345,prepaid=6789 };
        var catalog = new NativeCashCatalog();
        foreach (var item in catalog.All) if (item.onSale != 0) state.catalog.Add(item);
        typeof(NativeCashWindow).GetField("state",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,state);
        ShopWindow.Visible = true;
        Directory.CreateDirectory("docs/hud-review");
        foreach (var size in new[] {new Vector2Int(800,600),new Vector2Int(1280,720)}) {
            HudChecks.SetGameViewSize(size.x,size.y);
            yield return new WaitForSecondsRealtime(2);
            Set("category",1); Set("subcategory",0); Set("page",0);
            yield return Capture("docs/hud-review/cash-scene-"+size.x+".png");
            yield return Click(450,50);
            Check((int)Get("category") == 3,"scaled category hit target " + size);
            yield return Click(350,50);
            Check((int)Get("category") == 1,"return to hot category " + size);
            yield return Click(270 + 85,97 + 55);
            Check((bool)Get("purchaseVisible"),"buy opens confirmation instead of charging " + size);
            yield return Capture("docs/hud-review/cash-buy-"+size.x+".png");
            yield return Click(257+225,202+168);
            Check(!(bool)Get("purchaseVisible"),"purchase cancellation " + size);
            yield return Click(275+135,97+60);
            Check((bool)Get("giftVisible"),"gift opens native modal " + size);
            yield return Capture("docs/hud-review/cash-gift-"+size.x+".png");
            yield return Click(267+185,215+148);
            Check((bool)Get("birthdayVisible"),"gift asks for birthday instead of hardcoded value " + size);
            yield return Capture("docs/hud-review/cash-birthday-"+size.x+".png");
            Set("birthdayVisible",false);
            yield return Click(267+230,215+148);
            Check(!(bool)Get("giftVisible"),"gift cancellation " + size);
        }
        Debug.Log("[NativeCashSceneChecks] PASS native/widescreen screenshots, category hits, purchase/gift modal and cancel; no transactions");
        ShopWindow.Visible = false;
        EditorApplication.Exit(0);
    }
}
