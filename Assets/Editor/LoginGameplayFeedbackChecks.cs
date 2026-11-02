using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

public static class LoginGameplayFeedbackChecks
{
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Set(string name, object value) => typeof(GameSession).GetProperty(name).SetValue(null, value);
    static void CheckSession(NpcSession session)
    {
        Require(session != null && session.LastError == null, "Script failure: " + session?.LastError);
    }
    static NpcSession Center(int choice)
    {
        var session = NpcSession.Talk(9900001, "技能中心"); CheckSession(session);
        session.Reply(1, choice);
        CheckSession(session); CheckSession(NpcSession.Active);
        return NpcSession.Active;
    }
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "feedback-five-" + Guid.NewGuid().ToString("N") + ".db");
        int oldMap = Profile.LastMapId;
        var oldData = typeof(Profile).GetField("data", Private).GetValue(null);
        var oldOverride = typeof(Profile).GetField("debugOverride", Private).GetValue(null);
        try
        {
            CheckPanelOcclusion();
            typeof(Profile).GetField("data", Private).SetValue(null, new PlayerProfileData());
            typeof(Profile).GetField("debugOverride", Private).SetValue(null, true);
            var db = DbStore.Open(path);
            var login = LoginService.Login(db, "admin", "feedback-five");
            var created = CharFactory.CreateCharacter(db, login.accountId, 0, "五项回归", 1,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", login.accountId);
            Set("CharacterId", created.characterId); Set("Character", CharService.Load(db, created.characterId)); Set("InGame", true);
            GameSession.ApplyToProfile(); KeyMap.LoadCharacter(GameSession.Character.keymap);

            var quick = Center(1); quick.Reply(1, 1); CheckSession(quick);
            Require(SkillProgress.LevelOf(4111006) == 20, "quick flash jump level");
            Require(KeyMap.ExportCharacter().Exists(e => e.key == 21 && e.type == 1 && e.action == 4111006), "quick Y binding");
            quick = Center(1); quick.Reply(1, 2); CheckSession(quick);
            Require(SkillProgress.LevelOf(4111006) == 0, "force reset to zero");

            Profile.Data.gold = 20000000;
            var pets = Center(3); pets.Reply(1); CheckSession(pets);
            Require(SkillProgress.LevelOf(8) == 1 && Profile.Data.gold == 0, "three pets grants skill and charges once");
            var steal = Center(4); steal.Reply(1, 0); steal.Reply(1); CheckSession(steal);
            Require(steal.Dialog.RawText.Contains("不足"), "steal insufficient points branch");
            var api = NpcApi.Build(steal, 9900001, 0, false);
            var engine = new MapleDash.Js.JsEngine("cm.saveOrUpdateCharacterExtendValue('偷学技能点','3');", "feedback-points");
            engine.Bind("cm", api); engine.Run();
            steal = Center(4); steal.Reply(1, 0); steal.Reply(1); CheckSession(steal);
            Require(SkillProgress.LevelOf(2311003) == 30, "steal retains learned skill after removing old bindings");
            Require(KeyMap.ExportCharacter().Exists(e => e.key == 21 && e.action == 2311003), "steal Y binding");
            Profile.Data.SetJob(1311); SkillProgress.SetLevel(13101006, 7);
            var fix = Center(5); CheckSession(fix);
            Require(SkillProgress.LevelOf(14100005) == 7, "SkillFactory object ID for windwalk repair");
            Profile.Data.SetJob(112); Profile.Data.level = 120; Profile.AddItem(4000313, 1);
            var max = Center(2); max.Reply(1); CheckSession(max);
            Require(SkillProgress.LevelOf(1121002) == 30, "max skill raises master level");
            Require(Profile.ItemCount(4000313) == 0, "max skill original item charge");
            Debug.Log("FIVE_FEEDBACK_PASS skill_center_all_five_branches");

            bool checkedSnail = false;
            foreach (int id in QuestCatalog.Ids)
            {
                var quest = QuestCatalog.Get(id);
                if (!quest.completeReq.mobs.Exists(n => n.item == 100100)) continue;
                Profile.StartQuest(quest.id);
                QuestLogic.OnMobKilled(100100);
                Require(QuestAlarm.Tracked.Contains(quest.id), "snail quest tracked");
                Require((bool)typeof(QuestAlarm).GetField("expanded", Private).GetValue(null), "progress visible without opening details");
                checkedSnail = true; break;
            }
            Require(checkedSnail, "snail quest fixture exists");
            var priorLoop = GameLoop.I;
            var fixture = new GameObject("VirtualInputFixture");
            try
            {
                var loop = fixture.AddComponent<GameLoop>(); GameLoop.I = loop;
                loop.state = GameLoop.GameState.Playing;
                VirtualControls.OverlayVisible = true;
                Require(VirtualControls.Capturing, "virtual controls active before selecting skill");
                SkillWindow.PickForTest(4111006);
                Require(!VirtualControls.Capturing, "carried skill cannot activate virtual keys");
                SkillWindow.CancelSkillDrag();
            }
            finally { GameLoop.I = priorLoop; UnityEngine.Object.DestroyImmediate(fixture); }
            CheckAdmin();
        }
        finally
        {
            NpcSession.Active?.Close();
            Profile.LastMapId = oldMap;
            typeof(Profile).GetField("data", Private).SetValue(null, oldData);
            typeof(Profile).GetField("debugOverride", Private).SetValue(null, oldOverride);
            if (File.Exists(path)) File.Delete(path);
        }
    }
    static void CheckAdmin()
    {
        AdminMainThread.Pumping = false;
        var api = new AdminApi(); AdminCatalog.Register(api); AdminQuestBrowse.Register(api); AdminGive.Register(api);
        string token = api.CreateSession(new AdminAccount { Id = 1, Name = "fixture", WebAdmin = true });
        Func<string, string, string, object> request = (method, url, body) =>
        {
            var req = new AdminHttpRequest { Method = method, Path = url, BodyText = body ?? "" };
            req.Headers["Authorization"] = "Bearer " + token;
            if (method == "GET") req.Query["keyword"] = url.Contains("mob/") ? "蜗牛" : "";
            var response = api.Handle(req);
            var parsed = AdminJson.Parse(Encoding.UTF8.GetString(response.Body));
            Require(AdminJson.GetInt(parsed, "code") == 20000, "admin request " + url + ": " + Encoding.UTF8.GetString(response.Body));
            return AdminJson.Get(parsed, "data");
        };
        long balance = Profile.Data.gold;
        request("POST", "/give/v1/resource", AdminJson.Write(AdminJson.Obj("type",3,"playerId",GameSession.CharacterId,"quantity",10000000000L)));
        Require(Profile.Data.gold == balance + 10000000000L && GameSession.Character.record.meso == Profile.Data.gold,
            "online meso grant preserves long and synchronizes profile/record");
        GameSession.SaveToDb();
        Require(CharService.Load(GameSession.Server.Db,GameSession.CharacterId).record.meso == Profile.Data.gold,
            "save cannot overwrite admin meso grant");
        foreach (string route in new[] { "/setItem/v1/item/catalog", "/setItem/v1/equipment/catalog", "/mob/v1/catalog", "/skill/v1/catalog" })
        {
            var result = request("GET", route, null);
            Require(AdminJson.GetList(result, "records")?.Count > 0, "catalog must serialize records: " + route);
            Require(AdminJson.GetLong(result, "total") > 0, "catalog total " + route);
        }
        var search = request("POST", "/common/v1/informationSearch", "{\"types\":[\"mob\"],\"filter\":\"蜗牛\"}") as List<object>;
        Require(search != null && search.Count > 0, "Chinese information search");
        var all = request("POST", "/common/v1/informationSearch", "{\"types\":[],\"filter\":\"100100\"}") as List<object>;
        Require(all != null && all.Count > 0, "empty types searches all");
        Require((request("GET", "/quest/v1/townTree", null) as List<object>)?.Count > 0, "quest town tree");
        var quests = request("POST", "/quest/v1/list", "{}") as List<object>;
        Require(quests != null && quests.Count > 0, "quest list");
        var detail = request("POST", "/quest/v1/detail", AdminJson.Write(AdminJson.Obj("questId", AdminJson.GetString(quests[0], "questId"))));
        Require(!string.IsNullOrEmpty(AdminJson.GetString(detail, "name")), "quest detail");
        foreach (string image in new[] { "/assets/item-icons/2000000.png", "/assets/equipment-icons/1302000.png", "/assets/mob-icons/100100.png", "/assets/skill-icons/4111006.png", "/assets/npc-icons/9900001.png" })
        {
            var result = api.Handle(new AdminHttpRequest { Path = image });
            Require(result.Status == 200 && result.ContentType == "image/png" && result.Body.Length > 8, "local original IMG preview " + image);
        }
        Debug.Log("FIVE_FEEDBACK_PASS catalog_http_json information_search quest_browse original_img_icons");
    }
    static void CheckPanelOcclusion()
    {
        var oldPos = Pointer.Pos; var oldPress = Pointer.PressPosition;
        bool oldDown = Pointer.Down, oldReleased = Pointer.Released, visible = true;
        NativeUi.RegisterWindow("FeedbackOcclusionFixture", new Rect(10, 10, 100, 100), () => visible);
        try
        {
            Pointer.Down = Pointer.Released = false; Pointer.Pos = new Vector2(20, 20);
            Require(NativeUi.WindowAtPointer, "Visible panel did not block lower UI");
            Pointer.PressPosition = Pointer.Pos; Pointer.Pos = new Vector2(200, 200); Pointer.Released = true;
            Require(NativeUi.WindowAtPointer, "Panel drag release leaked into lower UI");
            visible = false;
            Require(!NativeUi.WindowAtPointer, "Closed panel retained stale pointer blocking");
            visible = true; Pointer.Released = false;
            Require(!NativeUi.WindowAtPointer, "Finished panel gesture blocked unrelated input");
            Debug.Log("FIVE_FEEDBACK_PASS panel_occlusion_press_release_close");
        }
        finally { visible = false; Pointer.Pos = oldPos; Pointer.PressPosition = oldPress; Pointer.Down = oldDown; Pointer.Released = oldReleased; }
    }
}
