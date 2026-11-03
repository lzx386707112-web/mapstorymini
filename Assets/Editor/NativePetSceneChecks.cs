using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NativePetSceneChecks
{
    public static void Run()
    {
        SessionState.SetBool("NativePetSceneChecks",true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        HudChecks.SetGameViewSize(800,600);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Hook()=>EditorApplication.update+=Tick;
    static void Tick()
    {
        if(!SessionState.GetBool("NativePetSceneChecks",false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool("NativePetSceneChecks",false);
        new GameObject("PetSceneChecks").AddComponent<NativePetSceneProbe>();
    }
}
public sealed class NativePetSceneProbe : MonoBehaviour
{
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance;
    int count;
    GameLoop loop;
    NativePetField pets;
    string path;
    PlayerProfileData oldData;
    bool oldSaving;
    bool ready,feedbackDraw;
    int feedbackFrames;
    string keyboardSnapshot;
    TouchScreenKeyboard.Status keyboardStatus;
    void Require(bool ok,string message) {if(!ok) throw new Exception(message);count++;}
    static void Session(string key,object value)=>typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null,new[] {value});
    static void SendEnter(bool down)=>typeof(VirtualControls).GetMethod("SendButton",Flags).Invoke(null,
        new object[] {new VCElem {type=VCElemType.Button,key=KeyCode.Return},down});
    IEnumerator Start()
    {
        var steps=Run();
        while(true)
        {
            object current;
            try {if(!steps.MoveNext()) break;current=steps.Current;}
            catch(Exception e) {Debug.LogException(e);Cleanup();EditorApplication.Exit(1);yield break;}
            yield return current;
        }
        Cleanup();EditorApplication.Exit(0);
    }
    IEnumerator Run()
    {
        Application.runInBackground=true;
        oldData=Profile.Data;
        oldSaving=(bool)typeof(Profile).GetField("debugOverride",Flags).GetValue(null);
        typeof(Profile).GetField("data",Flags).SetValue(null,new PlayerProfileData());
        typeof(Profile).GetField("debugOverride",Flags).SetValue(null,true);
        path=Path.Combine(Path.GetTempPath(),"pet-field-"+Guid.NewGuid()+".json");
        var db=DbStore.Create(path);var server=GameServer.StartWith(db);
        db.Table("accounts").Insert(new DbRow {["name"]="PetFeedback",["nxCredit"]=0L});
        var c=new CharRecord {accountid=1,name="PetTest",level=200,face=20000,hair=30000,
            hp=500,maxhp=500,maxmp=500,equipslots=96,useslots=96,setupslots=96,etcslots=96};
        c.id=CharactersDao.Insert(db,c);
        Profile.Data.playerName=c.name;
        Session("Server",server);Session("Net",server.Transport);Session("CharacterId",(int)c.id);Session("InGame",true);
        foreach(int id in new[] {5000000,1802000,1802014,1812000,1812001})
            Require(NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=(int)c.id,itemId=id,quantity=1}).ok,"Seed "+id);
        db.Table("inventoryitems").Find(row=>DbStore.Int(row,"itemid")==5000000)["expiration"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+86400000L;
        Session("Character",CharService.Load(db,(int)c.id));GameSession.ApplyInventoryToProfile();
        var game=new GameObject("PetFixture");
        loop=game.AddComponent<GameLoop>();loop.enabled=false;GameLoop.I=loop;
        loop.state=GameLoop.GameState.Playing;
        var owner=game.AddComponent<PlayerCtrl>();owner.enabled=false;loop.player=owner;owner.hp=500;
        loop.field=game.AddComponent<FieldDirector>();loop.field.enabled=false;
        typeof(FieldDirector).GetField("<Map>k__BackingField",Flags).SetValue(loop.field,new FieldMap {id=100000000});
        pets=game.AddComponent<NativePetField>();
        PlayerCtrl.XMin=-20;PlayerCtrl.XMax=20;
        PlayerCtrl.Terrain=new List<Vector4> {new Vector4(-20,0,20,0)};
        owner.PlaceAt(0,0);
        var camera=new GameObject("PetCamera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=3;
        camera.tag="MainCamera";camera.transform.position=new Vector3(0,1,-10);camera.backgroundColor=new Color(.2f,.3f,.4f);
        Require(NativeInventoryActions.TryUse(5000000)==null,"Pet use");
        Require(NativePetItems.GetPet(db,(int)c.id,0)!=null,"Pet use must set authoritative summoned state");
        pets.Tick(owner,.03f);
        var sprite=FindPet();Require(sprite!=null,"Summon must create pet renderer");
        Require(sprite.sprite!=null,"Summon must load original pet animation; " +
            WzAssets.Frames(WzAssets.Node("Item/Pet/5000000.img/stand0")).Count);
        owner.PlaceAt(5,0);
        for(int i=0;i<150;i++) pets.Tick(owner,.03f);
        Require(sprite.transform.position.x>2.9f && sprite.transform.position.x<5,"Pet must pursue then stop inside native band");
        var attrs=new CharacterAttributes {level=200,str=999,dex=999,intelligence=999,luk=999};
        Require(EquipLoadout.Equip(1802014,attrs)!=null,"Reject another pet's clothing");
        Require(EquipLoadout.Equip(1802000,attrs)==null,"Wear compatible pet clothing");
        yield return new WaitForSecondsRealtime(.3f);pets.Tick(owner,.03f);
        Require(Profile.EquipOfSlot(-14)==1802000,"Pet clothing slot must be visible in equipment UI");
        Require(sprite.transform.Find("equipment").GetComponent<SpriteRenderer>().sprite!=null,"Clothing must render on pet");
        Require(EquipLoadout.Equip(1812000,attrs)==null && Profile.EquipOfSlot(-23)==1812000,"Meso ability uses native pet slot");
        long gold=Profile.gold;
        var coin=GroundItem.Spawn(0,10,sprite.transform.position,alreadyOnGround:true);
        pets.Tick(owner,.03f);
        Require(Profile.gold==gold+10 && GroundItem.Count()==0,"Meso magnet actually collects into owner inventory");
        EquipLoadout.Unequip(-23);
        GroundItem.Spawn(0,7,sprite.transform.position,alreadyOnGround:true);pets.Tick(owner,.03f);
        Require(Profile.gold==gold+10 && GroundItem.Count()==1,"Unequipped magnet must stop collecting");
        GroundItem.Clear();
        Require(EquipLoadout.Equip(1812001,attrs)==null,"Wear item pouch");
        int before=Profile.ItemCount(2000000);
        GroundItem.Spawn(2000000,1,sprite.transform.position,alreadyOnGround:true);pets.Tick(owner,.03f);
        Require(Profile.ItemCount(2000000)==before+1,"Item pouch actually collects an item");
        yield return new WaitForEndOfFrame();
        var petShot=ScreenCapture.CaptureScreenshotAsTexture();
        Directory.CreateDirectory("docs/pet-review");
        File.WriteAllBytes("docs/pet-review/pet-clothing.png",petShot.EncodeToPNG());Destroy(petShot);
        EquipLoadout.Unequip(-14);yield return new WaitForSecondsRealtime(.3f);pets.Tick(owner,.03f);
        Require(sprite.transform.Find("equipment").GetComponent<SpriteRenderer>().sprite==null,"Unequip clears map clothing");
        // The owner moves across an upper foothold; the pet must jump, then land.
        PlayerCtrl.Terrain=new List<Vector4> {new Vector4(-20,0,4,0),new Vector4(4,1,20,1)};
        owner.PlaceAt(7,1);
        bool jumped=false,landed=false;
        for(int i=0;i<200;i++) {pets.Tick(owner,.03f);jumped|=sprite.transform.position.y>.1f;landed|=sprite.transform.position.x>4.1f && Mathf.Abs(sprite.transform.position.y-1)<.01f;}
        Require(jumped && landed,"Pet follows onto upper foothold using jump and landing");
        // Same X, different groups: horizontal pursuit alone cannot reach this platform.
        var fixture=loop.field.Map;
        fixture.footholds.Add(new FieldFoothold {id=1,layer=1,group=1,x1=-800,x2=800,y1=0,y2=0});
        fixture.footholds.Add(new FieldFoothold {id=2,layer=1,group=2,x1=200,x2=360,y1=-40,y2=-40});
        PlayerCtrl.Terrain=new List<Vector4> {new Vector4(-20,0,20,0)};
        owner.PlaceAt(-10,0);pets.Tick(owner,.03f);
        owner.PlaceAt(7,0);pets.Tick(owner,.03f);
        PlayerCtrl.Terrain.Add(new Vector4(5,1,9,1));
        owner.PlaceAt(7,1);
        bool groupLanded=false;
        for(int i=0;i<100;i++) {pets.Tick(owner,.03f);groupLanded|=Mathf.Abs(sprite.transform.position.y-1)<.01f;}
        Require(groupLanded,"Pet jumps from directly below into owner's foothold group");
        typeof(PlayerCtrl).GetField("climb",Flags).SetValue(owner,new Art.ClimbLine {x=7,bottom=0,top=5});
        pets.Tick(owner,.03f);
        Require(Mathf.Abs(sprite.transform.position.y-owner.FeetPos.y)<.01f,"Pet follows owner onto rope");
        typeof(PlayerCtrl).GetField("climb",Flags).SetValue(owner,null);
        for(int i=0;i<8;i++) pets.Tick(owner,.03f);
        Require(sprite.sprite!=null,"Pet leaves hanging action after native 200ms delay");
        owner.PlaceAt(-10,0);pets.Tick(owner,.03f);
        Require(Mathf.Abs(sprite.transform.position.x+10)<.1f,"Native distance warp");
        typeof(FieldDirector).GetField("<Map>k__BackingField",Flags).SetValue(loop.field,new FieldMap {id=104000000});
        pets.Tick(owner,.03f);Require(FindPet()!=null,"Map transition recreates summoned pet");
        ready=true;BottomBar.ResetChat();
        SendEnter(true);InputHub.EndFrame();
        yield return new WaitForEndOfFrame();yield return null;
        Require(BottomBar.ChatFocused,"Virtual Enter opens rendered chat after input edge is cleared");
        Directory.CreateDirectory("docs/pet-review");
        yield return new WaitForEndOfFrame();
        var shot=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("docs/pet-review/chat-enter.png",shot.EncodeToPNG());Destroy(shot);
        SendEnter(false);yield return null;
        Require(BottomBar.ChatFocused,"Release cannot close chat");
        typeof(BottomBar).GetField("chatInput",Flags).SetValue(null,"pet chat regression");
        SendEnter(true);yield return new WaitForEndOfFrame();yield return null;
        Require(!BottomBar.ChatFocused,"Virtual Enter submits and closes chat");
        Require(BottomBar.ChatHistory.Count==1 && BottomBar.ChatHistory[0].text.EndsWith("pet chat regression"),"Submitted text appears exactly once in chat history");
        var chatField=game.GetComponent<BotField>();
        Require(chatField!=null && ((System.Collections.IDictionary)typeof(BotField).GetField("balloons",Flags).GetValue(chatField)).Count==1,"Ordinary local chat creates a balloon before bot synchronization");
        // Simulate Android's final composition arriving with Done and an Enter edge.
        yield return new WaitForSecondsRealtime(.25f);
        BottomBar.FocusChat();typeof(BottomBar).GetField("chatInput",Flags).SetValue(null,"stale text");
        keyboardSnapshot="中文最终输入";keyboardStatus=TouchScreenKeyboard.Status.Done;
        SendEnter(true);yield return new WaitForEndOfFrame();yield return null;
        Require(!BottomBar.ChatFocused,"Done and Enter in the same frame do not reopen chat");
        Require(BottomBar.ChatHistory.Count==2 && BottomBar.ChatHistory[1].text.EndsWith("中文最终输入"),"Android final text replaces stale draft and is sent exactly once");
        var balloons=(System.Collections.IDictionary)typeof(BotField).GetField("balloons",Flags).GetValue(chatField);
        var localBalloon=balloons[(int)c.id];
        Require((string)localBalloon.GetType().GetField("text").GetValue(localBalloon)==Profile.playerName+": 中文最终输入","Local balloon carries the actual accepted text");
        yield return new WaitForSecondsRealtime(.25f);
        BottomBar.FocusChat();keyboardSnapshot="屏幕Enter最终输入";keyboardStatus=TouchScreenKeyboard.Status.Visible;
        SendEnter(true);yield return new WaitForEndOfFrame();yield return null;
        Require(BottomBar.ChatHistory.Count==3 && BottomBar.ChatHistory[2].text.EndsWith("屏幕Enter最终输入"),"Queued screen Enter reads latest Android text before submission");
        BottomBar.FocusChat();keyboardSnapshot="取消不能发送";keyboardStatus=TouchScreenKeyboard.Status.Canceled;
        yield return new WaitForEndOfFrame();yield return null;
        Require(BottomBar.ChatHistory.Count==3 && !BottomBar.ChatFocused,"Cancel closes without sending");
        owner.PlaceAt(0,0);camera.transform.position=new Vector3(0,1,-10);
        yield return new WaitForEndOfFrame();
        var chatShot=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("docs/pet-review/chat-sent-balloon.png",chatShot.EncodeToPNG());Destroy(chatShot);
        Require(typeof(BotField).GetField("balloonC",Flags).GetValue(null)!=null,"Local balloon rendering loads original ChatBalloon IMG");
        var acceptedBalloon=balloons[(int)c.id];
        yield return new WaitForSecondsRealtime(.25f);
        Require(!BottomBar.SubmitChat("组队失败不能冒泡",NativeChatChannel.Party),"Server rejects party chat without membership");
        Require(BottomBar.ChatHistory[BottomBar.ChatHistory.Count-1].text=="尚未加入组队" &&
            BottomBar.ChatHistory[BottomBar.ChatHistory.Count-1].color==BottomBar.ChatPink,"Failed send remains visible as the original pink chat notice");
        Require(ReferenceEquals(acceptedBalloon,balloons[(int)c.id]),"Rejected chat cannot replace accepted speech balloon");
        chatField.Clear();Require(balloons.Count==0,"Map clear removes local chat balloon");
        Require(NativeInventoryActions.TryUse(5000000)==null,"Recall pet");
        yield return new WaitForSecondsRealtime(.3f);pets.Tick(owner,.03f);
        yield return null;Require(FindPet()==null,"Recall removes map sprite");
        SkillsDao.Upsert(db,(int)c.id,8,1,1,-1);
        foreach(int id in new[] {5000001,5000002})
        {
            long row=InventoryDao.InsertItem(db,(int)c.id,id,5,id-5000000+1,1);
            db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==row)["expiration"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+86400000L;
        }
        Session("Character",CharService.Load(db,(int)c.id));GameSession.ApplyInventoryToProfile();
        foreach(int id in new[] {5000000,5000001,5000002}) Require(NativeInventoryActions.TryUse(id)==null,"Summon trio "+id);
        yield return new WaitForSecondsRealtime(.3f);pets.Tick(owner,.03f);
        owner.facing=1;owner.PlaceAt(-5,0);
        for(int i=0;i<200;i++) pets.Tick(owner,.03f);
        var positions=new List<float>();
        foreach(Transform child in pets.transform) if(child.name.StartsWith("pet-")) positions.Add(child.position.x);
        positions.Sort();
        Require(positions.Count==3 && positions[2]-positions[0]>2,"Native three-pet formation spreads pets instead of stacking sprites");
        Require(EquipLoadout.Equip(1812000,attrs)==null,"Equip magnet for authoritative pickup");
        var bots=server.Bots;
        var actor=bots.SynchronizePlayer(GameSession.Character,loop.field.Map.id,owner.FeetPos.x*Art.PPU,-owner.FeetPos.y*Art.PPU);
        var bridge=game.AddComponent<GroundItemBridge>();bridge.Bind(loop.field,bots);
        var petFeet=pets.PickupTarget(0).Value;
        long authoritativeGold=actor.character.record.meso, profileGold=Profile.gold;
        var bound=new Server.Field.MapItem {objectId=900001,mapId=loop.field.Map.id,meso=13,
            ownerCharacterId=(int)c.id,dropTime=(long)(Time.realtimeSinceStartupAsDouble*1000)-1000};
        var boundView=GroundItem.Spawn(0,13,petFeet,bound,alreadyOnGround:true);
        Require(GroundItem.TryPetPickup(petFeet,0,true,false,()=>pets.PickupTarget(0)),"Bound drop goes through actual pet pickup bridge");
        Require(bound.pickedUp && bound.pickupPetIndex==0 && bound.pickupCharacterId==(int)c.id,"Bound pickup records original pet removal reason identity");
        Require(actor.character.record.meso==authoritativeGold+13 && Profile.gold==profileGold+13,"Bound pickup updates authoritative character and profile");
        var destination=(Func<Vector2?>)typeof(GroundItem).GetField("pickupTarget",Flags).GetValue(boundView);
        Require(destination!=null && destination()==pets.PickupTarget(0),"Bound pickup animation targets pet instead of owner");
        var refused=new Server.Field.MapItem {objectId=900002,mapId=loop.field.Map.id,meso=17,
            ownerCharacterId=99999,dropTime=(long)(Time.realtimeSinceStartupAsDouble*1000)-1000};
        GroundItem.Spawn(0,17,petFeet,refused,alreadyOnGround:true);
        Require(!GroundItem.TryPetPickup(petFeet,0,true,false,()=>pets.PickupTarget(0)) && !refused.pickedUp,"Pet cannot steal an owned drop");
        GroundItem.Clear();
        foreach(int id in new[] {1812002,1812003,2000000,2000003})
            Require(NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=(int)c.id,itemId=id,quantity=id<2000000 ? 1 : 10}).ok,"Seed autopot "+id);
        Session("Character",CharService.Load(db,(int)c.id));GameSession.ApplyInventoryToProfile();
        KeyMap.LoadCharacter(GameSession.Character.keymap);
        Require(EquipLoadout.Equip(1812002,attrs)==null && EquipLoadout.Equip(1812003,attrs)==null,"Equip HP/MP abilities");
        EquipWindow.Visible=true;
        typeof(EquipWindow).GetField("petVisible",Flags).SetValue(null,true);
        Vector2 equipmentPos=(Vector2)typeof(EquipWindow).GetField("pos",Flags).GetValue(null);
        for(int i=0;i<2;i++)
        {
            int id=i==0 ? 2000000 : 2000003;
            NativeInventoryActions.Pick(id);
            Pointer.Pos=(equipmentPos+new Vector2(172+(i==0 ? 47 : 113),135))*BottomBar.S;
            Pointer.Pressed=true;NativeInventoryActions.CarryPointerStep();Pointer.Pressed=false;
            Require(!NativeInventoryActions.Carrying && KeyMap.PetPotion(i==0)==id,"Carry-drop binds actual HP/MP cell");
            Pointer.Released=true;NativeInventoryActions.CarryPointerStep();Pointer.Released=false;
        }
        var savedKeys=CharService.Load(db,(int)c.id).keymap;
        KeyMap.LoadCharacter(savedKeys);
        Require(KeyMap.PetPotion(true)==2000000 && KeyMap.PetPotion(false)==2000003,"Type7 91/92 bindings survive DB reload");
        for(int i=0;i<2;i++)
        {
            KeyMap.BindPetPotion(i==0,0);
            NativeInventoryActions.PickFromInventory(i==0 ? 2000000 : 2000003);
            Pointer.Pos=(equipmentPos+new Vector2(172+(i==0 ? 47 : 113),135))*BottomBar.S;
            Pointer.Pressed=false;Pointer.Released=true;
            NativeInventoryActions.CarryPointerStep();Pointer.Released=false;
            Require(!NativeInventoryActions.Carrying && KeyMap.PetPotion(i==0)==(i==0 ? 2000000 : 2000003),
                "Release after inventory drag binds pet potion without a second click");
        }
        var cashService=new NativeCashService(db) {Catalog=new NativeCashCatalog()};
        var cash=cashService.Handle(new CashRequest {characterId=(int)c.id,operation=CashOperation.Read});
        foreach(int id in new[] {1812002,1812003})
            Require(cash.catalog.Exists(item=>item.itemId==id && item.sn/100000==601 && item.onSale==1),
                "Original HP/MP bag is purchasable in pet-equipment catalog: "+id);
        int hpAlarm=ClientSettings.HpAlarm,mpAlarm=ClientSettings.MpAlarm;
        ClientSettings.HpAlarm=50;ClientSettings.MpAlarm=50;
        int hpCount=Profile.ItemCount(2000000),mpCount=Profile.ItemCount(2000003);
        owner.hpMax=owner.mpMax=500;owner.hp=owner.mp=100;
        NativePetAutopot.Tick(owner,500,500,true);
        Require(owner.hp==150 && owner.mp==200 && Profile.ItemCount(2000000)==hpCount-1 && Profile.ItemCount(2000003)==mpCount-1,"Normal autopot consumes one correct potion per decreased stat hp="+owner.hp+" mp="+owner.mp+" counts="+Profile.ItemCount(2000000)+","+Profile.ItemCount(2000003));
        NativePetAutopot.Tick(owner,owner.hp,owner.mp,true);
        Require(Profile.ItemCount(2000000)==hpCount-1 && Profile.ItemCount(2000003)==mpCount-1,"No consumption without another HP/MP decrease");
        owner.hp=100;owner.mp=100;
        NativePetAutopot.Tick(owner,500,500,false);
        Require(owner.hp==100 && owner.mp==100,"Normal client mode needs a summoned pet");
        Require(NativePetAutopot.Use(owner,2000000,.5,.5,true) && owner.hp==250 && Profile.ItemCount(2000000)==hpCount-4,"Server autopot ceil deficit count");
        owner.hp=0;
        Require(!NativePetAutopot.Use(owner,2000000,.5,.5,true),"Dead players cannot autopot");
        owner.hp=owner.mp=500;
        Require(!NativePetAutopot.Use(owner,2000000,.5,.5,false),"Full HP/MP cannot consume");
        hpCount=Profile.ItemCount(2000000);mpCount=Profile.ItemCount(2000003);
        owner.hp=owner.mp=250;
        NativePetAutopot.Tick(owner,251,251,true);
        Require(owner.hp==250 && owner.mp==250,"Exactly 50 percent does not trigger native client alarm");
        owner.hp=249;NativePetAutopot.OnLoss(owner,250,250);
        Require(owner.hp==299 && Profile.ItemCount(2000000)==hpCount-1,"Per-loss HP callback triggers immediately below 50 percent");
        Require(owner.TrySpendMp(1) && owner.mp==349 && Profile.ItemCount(2000003)==mpCount-1,"Skill MP cost immediately triggers pet potion");
        owner.hp=100;NativePetAutopot.OnLoss(owner,299,owner.mp);
        int afterLoss=Profile.ItemCount(2000000);
        pets.Tick(owner,0);
        Require(owner.hp==150 && Profile.ItemCount(2000000)==afterLoss,"Field polling cannot consume a duplicate bottle for the same loss");
        Require(owner.TrySpendMp(150) && owner.mp==299 && Profile.ItemCount(2000003)==mpCount-2,"Second MP loss in same frame is not missed");
        owner.hp=owner.mp=500;
        ClientSettings.HpAlarm=hpAlarm;ClientSettings.MpAlarm=mpAlarm;
        var nx=GroundItem.Spawn(4031865,2,owner.FeetPos,alreadyOnGround:true);
        Require((bool)typeof(GroundItem).GetMethod("Pickup",Flags).Invoke(nx,new object[] {-1,null}),"Local NX card pickup");
        Require(DbStore.Int(AccountsDao.FindById(db,1),"nxCredit")==200 && Profile.ItemCount(4031865)==0,"Local NX directly credits account without inventory");
        var nxBound=new Server.Field.MapItem {objectId=900003,mapId=loop.field.Map.id,
            item=new InventoryEntry {itemId=4031866,quantity=2,inventoryType=4},dropTime=(long)(Time.realtimeSinceStartupAsDouble*1000)-1000};
        Require(GroundItem.AuthoritativePickup(nxBound),"Authoritative NX card pickup");
        Require(DbStore.Int(AccountsDao.FindById(db,1),"nxCredit")==700 && Profile.ItemCount(4031866)==0,"Bound NX invokes real cash award hook");
        Require(NativeMonsterVac.For(owner).StartVac(),"Vac starts with item buff");
        Require(NativeBuffs.For(owner).HasSource(-2431158),"Vac negative item source present");
        feedbackDraw=true;
        yield return new WaitForEndOfFrame();yield return null;
        Require(feedbackFrames>0,"Item buff renders through status bar and windows without aborting GUI");
        feedbackDraw=false;NativeMonsterVac.For(owner).StopVac();EquipWindow.Visible=false;
        foreach(int id in new[] {1902000,1912000})
            Require(NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=(int)c.id,itemId=id,quantity=1}).ok,"Seed mount "+id);
        Session("Character",CharService.Load(db,(int)c.id));GameSession.ApplyInventoryToProfile();
        SkillProgress.SetLearned(1004,1);
        var shown=(System.Collections.Generic.List<int>)typeof(SkillWindow).GetMethod("TabSkills",Flags).Invoke(null,null);
        Require(shown.Contains(1004),"Learned NPC riding skill appears in beginner skill tab");
        foreach(string field in new[] {"SmallCells","FullCells"})
        {
            var cells=(int[,])typeof(EquipWindow).GetField(field,Flags).GetValue(null);
            Require(cells[6,0]==-18 && cells[6,1]==-19,"Original mount and saddle equipment cells are available: "+field);
        }
        Require(EquipLoadout.Equip(1902000,attrs)==null && EquipLoadout.Equip(1912000,attrs)==null,"Equip NPC mount and saddle");
        var system=owner.GetComponent<SkillSystem>() ?? owner.gameObject.AddComponent<SkillSystem>();
        Require(system.TryCastSkill(1004) && NativeMount.Item(owner)==1902000,"Learned riding skill activates actual player mount");
        Require(system.TryCastSkill(1004) && NativeMount.Item(owner)==0,"Riding skill toggles off");
        Require(system.TryCastSkill(1004),"Ride again before unequipping saddle");
        EquipLoadout.Unequip(-19);
        Require(NativeMount.Item(owner)==0,"Removing saddle cancels riding");
        Require(!system.TryCastSkill(1004),"Reject riding without saddle");
        Profile.Data.level=200;Profile.Data.attributes=attrs.Copy();
        foreach(int id in new[] {1902008,1912003,1912004})
            Require(NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=(int)c.id,itemId=id,quantity=1}).ok,"Seed cash mount equipment "+id);
        Session("Character",CharService.Load(db,(int)c.id));GameSession.ApplyInventoryToProfile();
        Require(NativeInventoryActions.TryUse(1912003)=="请先装备普通鞍具，再装备现金鞍具" && Profile.ItemCount(1912003)==1,"Double-click cash saddle requires normal saddle and preserves item");
        EquipLoadout.Unequip(-18);
        Require(NativeInventoryActions.TryUse(1902008)=="请先装备普通骑兽，再装备现金骑兽" && Profile.ItemCount(1902008)==1,"Double-click cash mount requires normal mount and preserves item");
        string baseMountError=NativeInventoryActions.TryUse(1902000),baseSaddleError=NativeInventoryActions.TryUse(1912000);
        Require(baseMountError==null && baseSaddleError==null,"Double-click equips normal riding base: "+baseMountError+"/"+baseSaddleError);
        Require(NativeInventoryActions.TryUse(1902008)==null && Profile.EquipOfSlot(-118)==1902008,"Double-click equips cash mount to original cash slot");
        Require(!system.TryCastSkill(1004),"Cash mount without cash saddle cannot ride");
        Require(NativeInventoryActions.TryUse(1912004)!=null && Profile.EquipOfSlot(-119)==0,"Reject mismatched cash saddle without moving item");
        Require(NativeInventoryActions.TryUse(1912003)==null && Profile.EquipOfSlot(-119)==1912003,"Double-click equips matching cash saddle");
        Require(system.TryCastSkill(1004) && NativeMount.Item(owner)==1902008 && NativeMount.Saddle==1912003,"Cash riding overlays matching pair on normal base");
        Session("Character",CharService.Load(db,(int)c.id));GameSession.ApplyInventoryToProfile();
        Require(Profile.EquipOfSlot(-18)==1902000 && Profile.EquipOfSlot(-19)==1912000 &&
            Profile.EquipOfSlot(-118)==1902008 && Profile.EquipOfSlot(-119)==1912003,"Reload retains both normal and cash riding equipment");
        Require(NativeMount.VisibleSlot(-18)==-118 && NativeMount.VisibleSlot(-19)==-119,"Equipment cells resolve visible cash positions");
        EquipWindow.Visible=true;
        typeof(EquipWindow).GetField("expanded",Flags).SetValue(null,false);
        float scale=BottomBar.S;
        var equipPos=(Vector2)typeof(EquipWindow).GetField("pos",Flags).GetValue(null);
        var equipOrigin=NativeUi.ClampWindowPosition(equipPos,new Vector2(175,304),scale)*scale;
        Require(EquipWindow.EquipmentAt(equipOrigin+new Vector2(21,249)*scale)?.itemId==1902008 &&
            EquipWindow.EquipmentAt(equipOrigin+new Vector2(54,249)*scale)?.itemId==1912003,"Equipment hover/drag hits actual cash mount and saddle entries");
        EquipWindow.Visible=false;
        EquipLoadout.Unequip(-119);
        Require(NativeMount.Item(owner)==0 && NativeMount.VisibleSlot(-19)==-19 && Profile.EquipOfSlot(-19)==1912000,"Removing cash saddle cancels riding and reveals normal saddle");
        Require(NativeInventoryActions.TryUse(1912003)==null && system.TryCastSkill(1004),"Re-equip cash saddle and ride again");
        EquipLoadout.Unequip(-18);
        Require(NativeMount.Item(owner)==0 && !system.TryCastSkill(1004),"Removing base mount cancels riding even when cash pair remains");
        Session("Character",CharService.Load(db,(int)c.id));GameSession.ApplyInventoryToProfile();
        Require(NativeMount.VisibleSlot(-18)==-118 && Profile.EquipOfSlot(-118)==1902008,"Previously equipped cash mount without base remains visible after reload");
        EquipLoadout.Unequip(-118);EquipLoadout.Unequip(-119);
        Require(Profile.ItemCount(1902008)==1 && Profile.ItemCount(1912003)==1,"Legacy cash equipment can be removed without loss");
        string recipe=WzCharacter.CurrentAppearanceKey();
        string script=File.ReadAllText(Path.Combine(WzAssets.DataRoot,"script-sources/scripts-zh-CN/npc/坐骑领取.js"));
        foreach(System.Text.RegularExpressions.Match pair in System.Text.RegularExpressions.Regex.Matches(script,@"\[(190\d{4}),\s*(191\d{4})\]"))
        {
            int mount=int.Parse(pair.Groups[1].Value),saddle=int.Parse(pair.Groups[2].Value);
            string key=WzCharacter.MountedAppearance(recipe,mount,saddle);
            foreach(string action in new[] {"stand","walk","jump","prone","ladder","rope"})
            {
                var clip=WzCharacter.Clip(key,action);
                Require(clip!=null && clip.frames.Length>0 && WzCharacter.Frame(clip.frames[0])!=null,
                    "NPC mount and saddle original IMG composition "+mount+"/"+action);
            }
        }
        bridge.Unbind();GroundItem.Clear();
        Debug.Log("[NativePetSceneChecks] PASS "+count);
    }
    SpriteRenderer FindPet()
    {
        foreach(Transform child in pets.transform)
            if(child.name.StartsWith("pet-")) return child.GetComponent<SpriteRenderer>();
        return null;
    }
    void OnGUI()
    {
        if(!ready) return;
        UiKit.Begin();
        if(feedbackDraw) { TemporaryStatView.Draw(); GameWindows.DrawAll(); feedbackFrames++; }
        if(keyboardSnapshot!=null)
        {
            string text=keyboardSnapshot;keyboardSnapshot=null;
            typeof(BottomBar).GetMethod("ReadChatKeyboard",Flags).Invoke(null,new object[] {text,keyboardStatus});
        }
        typeof(BottomBar).GetMethod("DrawChat",Flags).Invoke(null,null);
    }
    void Cleanup()
    {
        ready=false;BottomBar.ResetChat();Session("InGame",false);GameServer.Stop();
        typeof(Profile).GetField("data",Flags).SetValue(null,oldData);
        typeof(Profile).GetField("debugOverride",Flags).SetValue(null,oldSaving);
        if(path!=null && File.Exists(path)) File.Delete(path);
    }
}
