using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Runs unchanged scripts through NpcSession/NpcApi and an isolated real server DB.
// This is distinct from script-harness's deliberately simplified host fixtures.
public static class ScriptApiAudit
{
    [Serializable] public sealed class Entry { public string script, error; public int pages; }
    [Serializable] public sealed class Report { public int auditMapId = 1000000; public int scripts, passed, failed, hostChecks, requestedScripts, requestedPassed, requestedFailed; public string hostError = ""; public List<Entry> entries = new List<Entry>(); }

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash-script-api-" + Guid.NewGuid() + ".json");
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        bool hadMapPreference = PlayerPrefs.HasKey("mxd.fieldmap");
        int savedMap = Profile.LastMapId;
        var overrideField = typeof(Profile).GetField("debugOverride", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool previousOverride = (bool)overrideField.GetValue(null);
        var report = new Report();
        var requested = new HashSet<string>();
        string baselinePath=Path.Combine(Application.dataPath,"../docs/npc-script-mock-baseline.json");
        if(File.Exists(baselinePath))
            foreach(var entry in JsonUtility.FromJson<Report>(File.ReadAllText(baselinePath)).entries) requested.Add(entry.script);
        try
        {
            overrideField.SetValue(null, true);
            var db = DbStore.Create(path);
            var server = GameServer.StartWith(db);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../MapleDashResources/Data/scripts/npc"));
            var files = Directory.GetFiles(directory, "*.js"); Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                var entry = new Entry { script = Path.GetFileName(file), error = "" };
                try
                {
                    JsonUtility.FromJsonOverwrite(snapshot, data);
                    Profile.LastMapId = report.auditMapId;
                    var character = new CharRecord { accountid = 1, name = "Audit" + report.scripts, level = 30,
                        equipslots = 24, useslots = 24, setupslots = 24, etcslots = 24 };
                    int id = (int)CharactersDao.Insert(db, character); character.id = id;
                    foreach (var pair in new Dictionary<string, object> { ["Net"] = server.Transport, ["CharacterId"] = id,
                        ["InGame"] = true, ["Character"] = CharService.Load(db, id) })
                        typeof(GameSession).GetProperty(pair.Key).GetSetMethod(true).Invoke(null, new[] { pair.Value });
                    if (report.scripts == 0)
                    {
                        try { report.hostChecks = CheckHosts(server); }
                        catch (Exception e) { report.hostError = e.ToString(); Debug.LogError("[ScriptApiAudit] host regression: " + e); }
                    }
                    data.level = 30; data.jobId = 0; data.gold = 100000;
                    data.questStarted.Clear(); data.questCompleted.Clear();
                    string name = Path.GetFileNameWithoutExtension(file);
                    int.TryParse(name, out int npc);
                    var session = NpcSession.Talk(npc, name);
                    for (int step = 0; step < 14 && session != null && !session.Closed && !session.Disposed && session.LastError == null && session.Dialog.Visible; step++)
                    {
                        entry.pages++;
                        int selection = -1;
                        if (session.Dialog.Mode == NpcDialogMode.Simple)
                        {
                            var links = DialogFlow.Links(session.Dialog.Content);
                            if (links.Count == 0) break;
                            selection = links[0];
                        }
                        else if (session.Dialog.Mode == NpcDialogMode.GetNumber) selection = session.Dialog.NumberDef;
                        else if (session.Dialog.Mode == NpcDialogMode.GetText) session.Dialog.Input = "Test";
                        session.Reply(1, selection);
                    }
                    entry.error = session == null ? "No script session" : session.LastError ?? "";
                }
                catch (Exception e) { entry.error = e.ToString(); }
                finally { NpcSession.CloseActive(); }
                report.scripts++;
                if (entry.error.Length == 0) report.passed++;
                else { report.failed++; Debug.Log("[ScriptApiAudit] " + entry.script + " :: " + entry.error); }
                if(requested.Contains(entry.script))
                {
                    report.requestedScripts++;
                    if(entry.error.Length==0) report.requestedPassed++; else report.requestedFailed++;
                }
                report.entries.Add(entry);
            }
        }
        finally
        {
            GameServer.Stop(); JsonUtility.FromJsonOverwrite(snapshot, data); overrideField.SetValue(null, previousOverride);
            if (hadMapPreference) PlayerPrefs.SetInt("mxd.fieldmap", savedMap);
            else PlayerPrefs.DeleteKey("mxd.fieldmap");
            PlayerPrefs.Save();
            if (File.Exists(path)) File.Delete(path);
        }
        File.WriteAllText(Path.Combine(Application.dataPath, "../docs/script-api-audit.json"), JsonUtility.ToJson(report, true));
        Debug.Log("[ScriptApiAudit] scripts=" + report.scripts + " passed=" + report.passed + " failed=" + report.failed);
        if (Application.isBatchMode) EditorApplication.Exit(report.failed == 0 && report.hostError.Length == 0 ? 0 : 1);
    }
    static int CheckCommands(GameServer server)
    {
        var table = server.Db.Table("command_info");
        AdminCommands.ScriptCommandDescriptions(server.Db); // initialize the original seed
        var help = table.Find(row => DbStore.Str(row, "syntax") == "help");
        object enabled = help["enabled"], level = help["level"];
        string Query() {
            var engine = new MapleDash.Js.JsEngine(@"function test(){var C=Java.type('org.gms.client.command.CommandsExecutor');
var groups=C.getInstance().getCommandsNameDesc();var result=[];
for(var n=0;n<groups.size();n++){var pair=groups.get(n);var names=pair.getLeft(),descs=pair.getRight();
if(names.size()!=descs.size())throw 'misaligned';for(var i=0;i<names.size();i++)if(names.get(i)=='help')result.push(n+':'+descs.get(i));}
return groups.size()+'|'+result.join(',');}", "commands-check");
            engine.Run(scope => scope.Declare("Java", NpcApi.BuildJava()));
            return MapleDash.Js.JsConvert.ToString(engine.Call("test"));
        }
        try
        {
            string description = AdminCommands.ScriptCommandDescriptions(server.Db)[0].Find(pair => pair.Key == "help").Value;
            if (Query() != "7|0:" + description) throw new Exception("command level/description");
            help["enabled"] = 0L;
            if (Query() != "7|") throw new Exception("disabled command exposed");
            help["enabled"] = 1L; help["level"] = 6L;
            if (Query() != "7|6:" + description) throw new Exception("command rank update");
            return 3;
        }
        finally { help["enabled"] = enabled; help["level"] = level; }
    }

    static int CheckPlayerBridges(GameServer server, NpcSession context)
    {
        var record = GameSession.Character.record;
        int savedPreference = record.partySearch;
        var engine = new MapleDash.Js.JsEngine(@"var D=Java.type('soloMapling.ArtificialPlayer.BotTypes.Blackjack.BlackjackDealerBot');
function preference(){return p.isRecvPartySearchInviteEnabled();}
function join(){return D.joinNearestTable(p);}
function wrong(){return D.joinNearestTable({getId:function(){return -1;}});}", "player-bridges");
        engine.Run(scope => { scope.Declare("Java", NpcApi.BuildJava()); scope.Declare("p", NpcApi.BuildPlayer(context)); });
        void ExpectError(string name) {
            try { engine.Call(name); } catch (MapleDash.Js.JsRuntimeException) { return; }
            throw new Exception("expected missing/wrong player rejection: " + name);
        }
        try
        {
            record.partySearch = 0;
            if (MapleDash.Js.JsConvert.ToString(engine.Call("preference")) != "false") throw new Exception("disabled party preference");
            record.partySearch = 1;
            if (MapleDash.Js.JsConvert.ToString(engine.Call("preference")) != "true") throw new Exception("enabled party preference");
            ExpectError("wrong"); ExpectError("join");
            server.Bots.SynchronizePlayer(GameSession.Character, Profile.LastMapId, 0, 0, GameSession.Channel);
            string actual = MapleDash.Js.JsConvert.ToString(engine.Call("join"));
            if (actual != "当前地图没有可加入的 21 点荷官。请确认 21 点桌功能已开启，并重新进入自由市场。")
                throw new Exception("original no-dealer response: " + actual);
            var actor = server.ActiveBotPlayer(GameSession.CharacterId);
            var near = new Server.Bot.Types.Blackjack.BlackjackDealerBot(new Server.Bot.BotCharacter {
                id = -980001, name = "Near", mapId = Profile.LastMapId, x = 10, y = 0 });
            var far = new Server.Bot.Types.Blackjack.BlackjackDealerBot(new Server.Bot.BotCharacter {
                id = -980002, name = "Far", mapId = Profile.LastMapId, x = 200, y = 0 });
            Server.Bot.BotStore.AddActiveBot(far.chr.id, far);
            Server.Bot.BotStore.AddActiveBot(near.chr.id, near);
            if (!MapleDash.Js.JsConvert.ToString(engine.Call("join")).StartsWith("已加入 Near 的 21 点桌。") ||
                !near.Table.IsAlreadyAtTable(actor) || far.Table.IsAlreadyAtTable(actor))
                throw new Exception("nearest dealer / real player membership");
            int count = near.Table.PlayerCount;
            if (!MapleDash.Js.JsConvert.ToString(engine.Call("join")).StartsWith("你已经在这张 21 点桌里了。") ||
                near.Table.PlayerCount != count) throw new Exception("duplicate table join");
            return 7;
        }
        finally
        {
            record.partySearch = savedPreference;
            var actor = server.ActiveBotPlayer(GameSession.CharacterId);
            if (actor != null) Server.Bot.BotStore.RemoveRespondant(actor);
            Server.Bot.BotStore.RemoveActiveBot(-980001); Server.Bot.BotStore.RemoveActiveBot(-980002);
            server.Bots.RemovePlayer(GameSession.CharacterId);
        }
    }

    static int CheckHosts(GameServer server)
    {
        var record = GameSession.Character.record;
        record.hair = 30300; record.face = 20000; record.guildid = 7; record.partnerId = 9;
        var context = NpcSession.Conversation(0, "Audit", (session, mode) => { });
        string source = @"var P=cm.getPlayer();var E=Java.type('org.gms.server.expeditions.ExpeditionType');
var G=Java.type('org.gms.constants.game.GameConstants');var M=Java.type('org.gms.constants.id.MapId');
function test(){return [P.getHair(),P.getFace(),P.getGuildId(),P.getPartnerId(),
E.ARIANT.getMaxSize(),E.ZAKUM.getMinLevel(),E.LUCID.getMinLevel(),E.ZAKUM===Java.type('org.gms.server.expeditions.ExpeditionType').ZAKUM,
G.getJobBranch(Java.type('org.gms.client.Job').WARRIOR),G.getHallOfFameMapid(Java.type('org.gms.client.Job').WARRIOR),
M.isPartyDojo(925030099),M.isPartyDojo(925030100),M.isPartyDojo(925033804),M.isPartyDojo(925033805),P.getMonsterBook().getTotalCards()].join(',');}";
        var response = server.Scripts.Handle(new ScriptRequest { operation=ScriptOperation.Open, characterId=GameSession.CharacterId,
            name="host-regressions", source=source, bindings=new Dictionary<string, MapleDash.Js.JsValue> {
                ["cm"]=NpcApi.Build(context, 0, 0, false), ["Java"]=NpcApi.BuildJava() } });
        try
        {
            var result = server.Scripts.Handle(new ScriptRequest { operation=ScriptOperation.Call, characterId=GameSession.CharacterId,
                sessionId=response.sessionId, function="test" });
            string actual = MapleDash.Js.JsConvert.ToString(result.value);
            const string expected = "30300,20000,7,9,7,50,220,true,1,102000004,false,true,true,false,0";
            if (actual != expected) throw new Exception("host expected="+expected+" actual="+actual);
            return expected.Split(',').Length + CheckCommands(server) + CheckPlayerBridges(server, context);
        }
        finally
        {
            server.Scripts.Handle(new ScriptRequest { operation=ScriptOperation.Close, characterId=GameSession.CharacterId, sessionId=response.sessionId });
            record.hair=0; record.face=0; record.guildid=0; record.partnerId=0;
        }
    }

}
