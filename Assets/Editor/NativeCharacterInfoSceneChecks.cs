using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Render-only fixture: no server, database, transactions or player-save writes.
public static class NativeCharacterInfoSceneChecks
{
    public static void Run()
    {
        SessionState.SetBool("NativeCharacterInfoSceneChecks", true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        HudChecks.SetGameViewSize(800,600);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Hook() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool("NativeCharacterInfoSceneChecks",false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool("NativeCharacterInfoSceneChecks",false);
        new GameObject("CharacterInfoFixture").AddComponent<NativeCharacterInfoSceneProbe>();
    }
}
public class NativeCharacterInfoSceneProbe : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    CommunityResponse family = new CommunityResponse();
    bool drawFamily;
    Vector2? click;
    static void Set(Type type,string field,object value) => type.GetField(field,Private).SetValue(null,value);
    void OnGUI()
    {
        UiKit.Begin();
        if(Event.current.type==EventType.Repaint && click.HasValue)
        {
            Pointer.Pos=click.Value*BottomBar.S;Pointer.Clicked=Pointer.Pressed=true;click=null;
        }
        if(drawFamily)NativeFamilyWindow.Draw(family);else NativeCharacterInfo.Draw();
    }
    IEnumerator Click(float x,float y)
    {
        click=new Vector2(x,y);yield return new WaitForEndOfFrame();yield return null;
        Pointer.Clicked=Pointer.Pressed=false;
    }
    static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    IEnumerator Capture(string name)
    {
        if (!NativeCharacterInfo.Visible || !GameSession.InGame)
            throw new Exception("Character-info fixture was closed by another game loop");
        yield return null;
        yield return new WaitForEndOfFrame();
        var shot=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("docs/character-info-review/"+name+".png",shot.EncodeToPNG());
        Destroy(shot);
    }
    IEnumerator Start()
    {
        Application.runInBackground=true;
        Application.logMessageReceived += OnLog;
        var camera=new GameObject("CharacterInfoCamera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.18f,.18f);
        typeof(GameSession).GetProperty("Character").SetValue(null,new FullCharacter {
            record=new CharRecord {name="角色核验",level=200,job=112,face=20000,hair=30000},inventory=new List<InventoryEntry>() });
        typeof(GameSession).GetProperty("InGame").SetValue(null,true);
        Set(typeof(Profile),"data",new PlayerProfileData());Set(typeof(Profile),"debugOverride",true);
        string[] appearance=WzCharacter.CharacterIds(GameSession.Character.record,GameSession.Character.inventory);
        WzCharacter.Appearance(string.Join("_",appearance),appearance,0);
        var details=new NativeCharacterInfoData {mount=1902000,mountLevel=3,mountExp=123,normalCards=5,specialCards=2,
            bookCover=2380000,medal=1142000};
        foreach(int id in QuestCatalog.Ids)
            if(id>=29000 && QuestCatalog.Get(id).medalItem>0 && details.medalQuests.Count<6)details.medalQuests.Add(id);
        Check(details.medalQuests.Count==6,"Original medal quest fixtures");
        details.mountItems.Add(1902000);details.mountItems.Add(1912000);
        details.pets.Add(new InventoryEntry {itemId=5000000,petData=new Dictionary<string,object> {
            {"name","原版宠物"},{"level",10},{"closeness",123},{"fullness",90} }});
        details.petItems[0].Add(1812007);details.items.Add(1302000);details.items.Add(1040002);
        Set(typeof(NativeCharacterInfo),"details",details);Set(typeof(NativeCharacterInfo),"appearance",appearance);
        Set(typeof(NativeCharacterInfo),"position",new Vector2(20,20));
        typeof(NativeCharacterInfo).GetProperty("Visible").SetValue(null,true);
        Directory.CreateDirectory("docs/character-info-review");
        yield return new WaitForSecondsRealtime(2);
        for(int mode=0;mode<4;mode++)
        {
            Set(typeof(NativeCharacterInfo),"mode",mode);
            yield return Capture("userinfo-"+mode);
        }
        yield return Click(20+258,20+365);
        Check((float)typeof(NativeCharacterInfo).GetField("medalScroll",Private).GetValue(null)==1,"Medal list next arrow");
        yield return Capture("userinfo-medal-scrolled");
        Set(typeof(NativeCharacterInfo),"detailPane",1);yield return Capture("userinfo-items");
        Set(typeof(NativeCharacterInfo),"detailPane",2);yield return Capture("userinfo-wishes");
        Set(typeof(NativeCharacterInfo),"detailPane",0);
        Set(typeof(NativeCharacterInfo),"mode",0);
        yield return Click(20+96,20+180);
        Check((int)typeof(NativeCharacterInfo).GetField("mode",Private).GetValue(null)==1,"Pet expand button");
        yield return Click(20+8,20+381);
        Check((int)typeof(NativeCharacterInfo).GetField("mode",Private).GetValue(null)==2,"Mount expand button");
        yield return Click(20+148,20+343);
        Check((int)typeof(NativeCharacterInfo).GetField("mode",Private).GetValue(null)==3,"Book expand button");
        Set(typeof(NativePetExclusionWindow),"visible",true);
        Set(typeof(NativePetExclusionWindow),"entries",new List<int> {int.MaxValue,2000000,2000001,2000002,4000000,4000001,4000002});
        yield return Capture("pet-exclusions");
        Set(typeof(NativePetExclusionWindow),"search",true);
        Set(typeof(NativePetExclusionWindow),"results",true);
        Set(typeof(NativePetExclusionWindow),"matches",new List<int> {2000000,2000001,2000002,2000003,2000004,2000005});
        yield return Capture("pet-exclusions-search");
        Pointer.Pos=new Vector2(300,230)*BottomBar.S;
        Check(NativePetExclusionWindow.CoversPointer(new Vector2(20,20),3,BottomBar.S),"Search must block covered item/wish controls");
        // Search is clamped to viewport; at this fixture position it remains at290,216.
        yield return Click(290+198,216+11);
        Check(!(bool)typeof(NativePetExclusionWindow).GetField("search",Private).GetValue(null),"Original search close button");
        NativePetExclusionWindow.Close();
        NativeCommunityWindow.Visible=true;
        Set(typeof(NativeCommunityWindow),"action",GameAction.School);
        drawFamily=true;Set(typeof(NativeFamilyWindow),"position",new Vector2(20,20));
        yield return Capture("family");
        yield return Click(20+209,20+9);
        Check(!NativeCommunityWindow.Visible,"Original academy close button");
        NativeCommunityWindow.Visible=true;
        Set(typeof(NativeFamilyWindow),"tree",true);yield return Capture("family-tree");
        yield return Click(20+561,20+13);
        Check(!NativeCommunityWindow.Visible,"Original family-tree close button");
        Debug.Log("[NativeCharacterInfoSceneChecks] PASS 11 rendered panels, pet/mount/book expand, medal scroll, search occlusion and original close-button hits; no transactions");
        EditorApplication.Exit(0);
    }
    static void OnLog(string message,string stack,LogType type)
    {
        if(type==LogType.Exception || type==LogType.Error)EditorApplication.Exit(1);
    }
}
