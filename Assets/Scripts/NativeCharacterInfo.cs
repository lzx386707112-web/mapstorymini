using Server.Bot;
using UnityEngine;

// CUIUserInfo basic page: 8FFB4A, 901185, 903FC7 (same BeiDou.exe identity as reverse skill).
public static class NativeCharacterInfo
{
    public static bool Visible { get; private set; }
    static BotCharacter target;
    static Vector2 position = new Vector2(240, 100), grab;
    static bool dragging;
    static string[] appearance;
    static string guild = "-";
    static NativeCharacterInfoData details;
    static int mode, petIndex;
    static int detailPane;
    static float detailScroll;
    static float medalScroll;
    static float equipmentScroll;
    static NativeCashCatalog cashCatalog;
    const string Root = "UI/UIWindow.img/UserInfo/";

    public static void Close() { Visible = dragging = false; target = null; appearance = null; NativePetExclusionWindow.Close(); }
    public static void Open(BotCharacter actor = null)
    {
        if (!GameSession.InGame) return;
        if (actor != null && (!actor.online || actor.mapId != Profile.LastMapId ||
            actor.world != GameSession.World || actor.channel != GameSession.Channel)) return;
        target = actor;
        appearance = actor == null
            ? WzCharacter.CharacterIds(GameSession.Character.record, GameSession.Character.inventory)
            : BotAppearance.CharacterIds(actor);
        GameSession.SaveToDb();
        var response = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId,
            operation = SocialOperation.CharacterInfo, memberId = actor?.id ?? GameSession.CharacterId });
        if (!response.ok) { BottomBar.Toast(response.error); return; }
        details = response.characterInfo; guild = details.guild;
        mode = petIndex = detailPane = 0; detailScroll = medalScroll = equipmentScroll = 0;
        NativePetExclusionWindow.Close();
        Visible = true; dragging = false; Pointer.ConsumeClick();
    }
    public static void Draw()
    {
        if (!Visible) return;
        if (!GameSession.InGame) { Close(); return; }
        float scale = BottomBar.S;
        Vector2 pointer = Pointer.Pos / scale;
        int height = mode == 1 ? 400 : mode == 2 ? 362 : mode == 3 ? 404 : 199;
        var groupSize = new Vector2(detailPane != 0 ? 510 : NativePetExclusionWindow.Visible ? 429 : 275,
            Mathf.Max(height, NativePetExclusionWindow.Visible ? (mode == 0 ? 181 : 343) : 0));
        position = NativeUi.ClampWindowPosition(position, groupSize, scale);
        var rect = new Rect(position.x, position.y, 275, height);
        NativeUi.RegisterWindow("NativeCharacterInfo", new Rect(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale), () => NativeCharacterInfo.Visible);
        if (Event.current.type == EventType.Repaint && Pointer.Pressed &&
            new Rect(rect.x, rect.y, 251, 20).Contains(pointer))
        { dragging = true; grab = pointer - position; }
        if (dragging && Pointer.Down)
        { position = NativeUi.ClampWindowPosition(pointer - grab, groupSize, scale); rect.position = position; }
        if (!Pointer.Down) dragging = false;
        var matrix = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUI.BeginGroup(rect);
        try
        {
            pointer -= position;
            void Image(string path, float x, float y)
            {
                var t = Art.Tex("wz://" + Root + path);
                if (t != null) GUI.DrawTexture(new Rect(x, y, t.width, t.height), t);
            }
            bool Button(string path, float x, float y, bool active)
            {
                var t = Art.Tex("wz://" + Root + path + "/normal/0");
                if (t == null) return false;
                GUI.enabled = enabled && active && !NativeUtilPrompt.Visible;
                return NativeUi.Button(new Rect(x, y, t.width, t.height), Root + path, pointer);
            }
            var style = new GUIStyle { font = UiKit.Font, fontSize = 12, clipping = TextClipping.Clip };
            style.normal.textColor = Color.black;
            Image(mode == 1 ? "backgrnd2" : mode == 2 ? "backgrnd5" : mode == 3 ? "backgrnd7" : "backgrnd", 0, 0);
            GUI.enabled = enabled && !NativeUtilPrompt.Visible;
            if (NativeUi.Button(new Rect(256, 5, 12, 12), "UI/Basic.img/BtClose", pointer)) { Close(); return; }
            var c = target?.character?.record ?? (target == null ? GameSession.Character.record : null);
            string name = target?.name ?? c.name;
            int level = target?.level ?? c.level, job = target?.job ?? c.job, fame = target?.fame ?? c.fame;
            var sprite = WzCharacter.Appearance(string.Join("_", appearance), appearance,
                WzCharacter.AppearanceFrame(appearance, Time.unscaledTime));
            if (sprite != null)
                GUI.DrawTexture(new Rect(55 - sprite.pivot.x, 130 - sprite.rect.height + sprite.pivot.y,
                    sprite.rect.width, sprite.rect.height), sprite.texture);
            GUI.Label(new Rect(52 - style.CalcSize(new GUIContent(name)).x / 2, 144, 104, 17), name, style);
            GUI.Label(new Rect(160, 77, 105, 17), level.ToString(), style);
            GUI.Label(new Rect(160, 96, 105, 17), JobDefs.NameOf(job), style);
            GUI.Label(new Rect(160, 113, 75, 17), fame.ToString(), style);
            GUI.Label(new Rect(160, 131, 105, 17), guild, style);
            Image("Marry/" + (c != null && c.partnerId > 0 ? "married" : "unmarried") + "/0", 10, 30);
            bool other = target != null;
            if (Button("BtItem", 208, 22, true)) { detailPane = detailPane == 1 ? 0 : 1; detailScroll = 0; }
            if (Button("BtWish", 208, 41, true)) { detailPane = detailPane == 2 ? 0 : 2; detailScroll = 0; }
            if (Button("BtFamily", 105, 23, true))
            { NativeCommunityWindow.Toggle(GameAction.School, name); }
            if (Button("BtParty", 149, 23, other)) NativeSocialWindow.Request(SocialOperation.InviteParty, name);
            if (Button("BtTrade", 149, 42, other)) NativeTradeWindow.Invite(target.id);
            int bottom = mode == 1 ? 378 : mode == 2 ? 340 : mode == 3 ? 382 : 177;
            if (Button(mode == 1 ? "BtPetHide" : "BtPetShow", 93, bottom, details.pets.Count > 0)) { mode = mode == 1 ? 0 : 1; equipmentScroll = 0; }
            if (Button(mode == 2 ? "BtTamingHide" : "BtTamingShow", 5, bottom, details.mount > 0)) { mode = mode == 2 ? 0 : 2; equipmentScroll = 0; }
            if (Button(mode == 3 ? "BtBookHide" : "BtBookShow", 145, bottom, true)) { mode = mode == 3 ? 0 : 3; equipmentScroll = 0; }
            // 900643..90068D checks the first active pet's ignorePickup ability (705E2C).
            bool canExclude = target == null && details.pets.Count > 0 &&
                details.petItems[0].Exists(id => (NativeItemSource.Node(id)?.Int("info/ignorePickup") ?? 0) != 0);
            if (Button(NativePetExclusionWindow.Visible ? "BtExceptionHide" : "BtExceptionShow", 199, bottom, canExclude))
                NativePetExclusionWindow.Toggle(details.pets[Mathf.Clamp(petIndex, 0, details.pets.Count - 1)]);
            void Text(object value, int x, int y, int width = 85) => GUI.Label(new Rect(x, y, width, 17), value?.ToString() ?? "", style);
            void Equipment(System.Collections.Generic.List<int> items, bool pet)
            {
                int top=pet?217:179, baseline=pet?248:212;
                equipmentScroll=NativeUi.Scroll4(new Rect(252,pet?215:177,15,112),new Rect(104,top,147,112),equipmentScroll,
                    Mathf.Max(0,items.Count-3),1,pointer);
                for(int row=0;row<3;row++)
                {
                    int index=(int)equipmentScroll+row;if(index>=items.Count)break;
                    int id=items[index],dy=row*38;var icon=NativeItemSource.Icon(id);
                    if(icon!=null)GUI.DrawTexture(new Rect(106,baseline+dy-icon.height,icon.width,icon.height),icon);
                    Text(WzNames.Item(id),147,top+dy,100);
                    Image("ReqLv",147,top+19+dy);
                    Text(NativeItemSource.Node(id)?.Int("info/reqLevel")??0,183,top+18+dy,60);
                }
            }
            if (mode == 1 && details.pets.Count > 0)
            {
                petIndex = Mathf.Clamp(petIndex, 0, details.pets.Count - 1);
                for (int i = 0; i < 3; i++) if (Button("BtPet" + (i + 1), 168 + i * 34, 177, i < details.pets.Count)) { petIndex = i; equipmentScroll = 0; }
                var pet = details.pets[petIndex]; var data = pet.petData;
                string petName = data["name"].ToString();
                Text(petName, (int)(52 - style.CalcSize(new GUIContent(petName)).x / 2), 306, 104);
                Text(WzNames.Item(pet.itemId), 58, 331, 75);
                Text(data["level"], 58, 348); Text(data["closeness"], 189, 330); Text(data["fullness"], 189, 348);
                // 9044E4/9044EA: pet layer baseline (53,299), source stand0 frames.
                var frames = WzAssets.Frames(WzAssets.Node("Item/Pet/" + pet.itemId.ToString("D7") + ".img/stand0"));
                if (frames.Count > 0)
                {
                    float duration = 0;
                    foreach (var f in frames) duration += Mathf.Max(1, Mathf.Abs(f.Int("delay", 100))) / 1000f;
                    float time = Time.unscaledTime % duration;
                    int frame = 0;
                    while (frame < frames.Count - 1 && time >= Mathf.Max(1, Mathf.Abs(frames[frame].Int("delay", 100))) / 1000f)
                    { time -= Mathf.Max(1, Mathf.Abs(frames[frame].Int("delay", 100))) / 1000f; frame++; }
                    var petSprite = WzAssets.Sprite(frames[frame]);
                    if (petSprite != null) GUI.DrawTexture(new Rect(53 - petSprite.pivot.x, 299 - petSprite.rect.height + petSprite.pivot.y,
                        petSprite.rect.width, petSprite.rect.height), petSprite.texture);
                    if (details.petClothing != 0)
                    {
                        var clothing = WzAssets.Node("Character/PetEquip/" + details.petClothing.ToString("D8") + ".img/" + pet.itemId + "/stand0/" + frames[frame].Name);
                        var coat = clothing == null ? null : WzAssets.Sprite(clothing);
                        if (coat != null) GUI.DrawTexture(new Rect(53 - coat.pivot.x, 299 - coat.rect.height + coat.pivot.y,
                            coat.rect.width, coat.rect.height), coat.texture);
                    }
                }
                Equipment(details.petItems[petIndex],true);
            }
            if (mode == 2)
            {
                string mountedKey = WzCharacter.MountedAppearance(string.Join("_",appearance),details.mount);
                var clip = WzCharacter.Clip(mountedKey,"stand1");
                if (clip != null && clip.frames.Length > 0)
                {
                    float total=0; foreach(float delay in clip.delays)total+=delay;
                    float time=Mathf.Repeat(Time.unscaledTime,total); int frame=0;
                    while(frame<clip.frames.Length-1 && time>=clip.delays[frame]) {time-=clip.delays[frame];frame++;}
                    var mountSprite=WzCharacter.Frame(clip.frames[frame]);
                    var info=NativeItemSource.Node(details.mount);
                    int percent=info?.Int("info/userInfoScale",100)??100;
                    int dx=info?.Int("info/userInfoDiffX")??0,dy=info?.Int("info/userInfoDiffY")??0;
                    switch(details.mount)
                    {
                        case 1902002:percent=70;dx=dy=-25;break;
                        case 1902015:percent=80;dx=0;dy=-12;break;
                        case 1902016:case 1902017:percent=80;dx=-5;dy=-15;break;
                        case 1902018:percent=75;dx=-5;dy=-20;break;
                    }
                    float factor=percent/100f;
                    if(mountSprite!=null)GUI.DrawTexture(new Rect(53+dx-mountSprite.pivot.x*factor,
                        261+dy-(mountSprite.rect.height-mountSprite.pivot.y)*factor,
                        mountSprite.rect.width*factor,mountSprite.rect.height*factor),mountSprite.texture);
                }
                Text(WzNames.Item(details.mount), 58, 293); Text(details.mountLevel, 58, 311);
                Text(details.mountExp, 189, 293); Text(details.mountTiredness, 189, 311);
                Equipment(details.mountItems,false);
            }
            if (mode == 3)
            {
                var bookIcon=Art.Tex("wz://UI/UIWindow.img/MonsterBook/icon/"+(details.bookLevel-1));
                if(bookIcon!=null)GUI.DrawTexture(new Rect(14,182,bookIcon.width,bookIcon.height),bookIcon);
                Text(details.bookLevel, 101, 181); Text(details.normalCards + details.specialCards, 101, 217, 35);
                if(details.bookCover>0)Text(WzNames.Mob(NativeItemSource.Node(details.bookCover)?.Int("info/mob")??0),101,199,155);
                Text(details.normalCards, 168, 217, 30); Text(details.specialCards, 236, 217, 30);
                if (details.medal != 0)
                {
                    Text(WzNames.Item(details.medal), 101, 247, 155);
                    var icon=NativeItemSource.Icon(details.medal);
                    if(icon!=null)GUI.DrawTexture(new Rect(8,278-icon.height,icon.width,icon.height),icon);
                }
                Text(details.medalQuests.Count, 101, 265);
                medalScroll=NativeUi.Scroll4(new Rect(252,298,15,73),new Rect(47,299,204,72),medalScroll,
                    Mathf.Max(0,details.medalQuests.Count-4),1,pointer);
                for(int row=0;row<4;row++)
                {
                    int index=(int)medalScroll+row;if(index>=details.medalQuests.Count)break;
                    var quest=QuestCatalog.Get(details.medalQuests[index]);
                    if(quest?.medalItem>0)Text(WzNames.Item(quest.medalItem).Replace("'s Medal",""),52,301+row*18,198);
                }
            }
        }
        finally { GUI.EndGroup(); GUI.matrix = matrix; GUI.enabled = enabled; }
        DrawDetail(scale);
        NativePetExclusionWindow.Draw(position,mode,scale);
    }
    static void DrawDetail(float scale)
    {
        if (detailPane == 0 || !Visible) return;
        Vector2 origin = position + new Vector2(270, mode == 0 ? 34 : 196);
        var bounds = new Rect(origin.x * scale, origin.y * scale, 240 * scale, 162 * scale);
        NativeUi.RegisterWindow("NativeCharacterInfo.Detail", bounds, () => Visible && detailPane != 0);
        var matrix = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUI.BeginGroup(new Rect(origin.x, origin.y, 240, 162));
        try
        {
            var p = Pointer.Pos / scale - origin;
            var background = Art.Tex("wz://" + Root + (detailPane == 1 ? "backgrnd4" : "backgrnd3"));
            if (background != null) GUI.DrawTexture(new Rect(0, 0, background.width, background.height), background);
            GUI.enabled = enabled && !NativeUtilPrompt.Visible && !NativePetExclusionWindow.CoversPointer(position,mode,scale);
            if (NativeUi.Button(new Rect(220, 6, 12, 12), "UI/Basic.img/BtHide", p)) { detailPane = 0; return; }
            var entries = detailPane == 1 ? details.items : details.wishes;
            detailScroll = NativeUi.Scroll4(new Rect(218, 33, 15, 112), new Rect(9, 29, 207, 120),
                detailScroll, Mathf.Max(0, entries.Count - 3), 1, p);
            var style = new GUIStyle { font = UiKit.Font, fontSize = 12, clipping = TextClipping.Clip };
            style.normal.textColor = Color.black;
            for (int row = 0; row < 3; row++)
            {
                int index = (int)detailScroll + row;
                if (index >= entries.Count) break;
                CashCommodity commodity = null;
                if (detailPane == 2) { cashCatalog ??= new NativeCashCatalog(); commodity = cashCatalog.Find(entries[index]); }
                int item = detailPane == 1 ? entries[index] : commodity?.itemId ?? 0;
                if (item == 0) continue;
                int y = row * 40;
                var icon = NativeItemSource.Icon(item);
                if (icon != null) GUI.DrawTexture(new Rect(17, 64 + y - icon.height, icon.width, icon.height), icon);
                GUI.Label(new Rect(57, 32 + y, 153, 17), WzNames.Item(item), style);
                if (detailPane == 1)
                {
                    var req = Art.Tex("wz://" + Root + "ReqLv");
                    if (req != null) GUI.DrawTexture(new Rect(57, 50 + y, req.width, req.height), req);
                    GUI.Label(new Rect(91, 48 + y, 80, 17), (NativeItemSource.Node(item)?.Int("info/reqLevel") ?? 0).ToString(), style);
                }
                else
                {
                    GUI.Label(new Rect(57, 49 + y, 105, 17), commodity.price.ToString("N0") + " NX", style);
                    if (NativeUi.Button(new Rect(165, 50 + y, 47, 19), Root + "BtPresent", p))
                    { string recipient = target?.name ?? GameSession.Character.record.name; Close(); NativeCashWindow.GiftWish(commodity.sn, recipient); return; }
                }
            }
        }
        finally { GUI.EndGroup(); GUI.matrix = matrix; GUI.enabled = enabled; }
    }
}
