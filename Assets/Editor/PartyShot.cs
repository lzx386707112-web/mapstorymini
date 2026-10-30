using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

// 组队面板实拍（走真实链路，不摆 mock）：
//   /Applications/Unity/Unity.app/Contents/MacOS/Unity -projectPath <proj> \
//     -executeMethod PartyShot.Run -partyShot /tmp/mapledash-party \
//     -logFile /tmp/party-shot.log
//
// 不能加 -batchmode（没有图形设备，截出来是黑的）。流程：
//   进 Play → 等 GameLoop 进入 Playing → 打开组队窗口（空队伍）拍一张 →
//   CreateParty + 逐个邀请在线假人（真实 Social 请求）→ 再拍一张。
// 存档在开跑前快照、结束时原样写回。
public static class PartyShot
{
    const string K_RUN = "PartyShot.run";
    const string K_DIR = "PartyShot.dir";
    const string K_T0 = "PartyShot.t0";
    const string K_PHASE = "PartyShot.phase";
    const string K_NEXT = "PartyShot.next";
    const string K_SNAP = "PartyShot.snapshot";
    const string K_HAD = "PartyShot.hadSnapshot";
    const string K_AUTO = "PartyShot.hadAutostart";
    const string K_AUTOV = "PartyShot.autostart";
    const string K_CFG = "PartyShot.hadStarterCfg";
    const string K_CFGV = "PartyShot.starterCfg";
    const string SnapKey = "mxd.profile.v1";

    [InitializeOnLoadMethod]
    static void Hook()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode; EditorApplication.playModeStateChanged += OnPlayMode;
    }

    public static void Run()
    {
        string dir = "/tmp/mapledash-party";
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-partyShot") dir = args[i + 1];
        Directory.CreateDirectory(dir);
        // 存档快照：CreateParty 会写库，跑完必须原样写回，不能污染玩家数据。
        EditorPrefs.SetBool(K_HAD, PlayerPrefs.HasKey(SnapKey));
        EditorPrefs.SetString(K_SNAP, PlayerPrefs.GetString(SnapKey, ""));
        // 报告只要求面板本身，所以直接跳过大厅/选区（同 PreviewShot 的 autostart）。
        EditorPrefs.SetBool(K_AUTO, PlayerPrefs.HasKey("autostart"));
        EditorPrefs.SetInt(K_AUTOV, PlayerPrefs.GetInt("autostart", 0));
        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);     // 调参面板别入镜
        PlayerPrefs.SetInt("debug_god", 1);       // 途中别被打死
        PlayerPrefs.Save();
        HudChecks.SetGameViewSize(1280, 720);
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_DIR, dir);
        EditorPrefs.SetString(K_T0, "0");
        EditorPrefs.SetInt(K_PHASE, 0);
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode; EditorApplication.playModeStateChanged += OnPlayMode;
        Note("进入播放模式");
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayMode(PlayModeStateChange st)
    {
        if (st != PlayModeStateChange.EnteredPlayMode) return;
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorPrefs.SetString(K_T0, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
        EditorPrefs.SetString(K_NEXT, "0");
        EditorPrefs.SetInt(K_PHASE, 0);
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        double t0 = Parse(EditorPrefs.GetString(K_T0, "0"));
        if (!EditorApplication.isPlaying) return;
        if (t0 <= 0) return;
        Application.runInBackground = true;
        double now = EditorApplication.timeSinceStartup;
        double next = Parse(EditorPrefs.GetString(K_NEXT, "0"));
        if (now < next) return;
        int phase = EditorPrefs.GetInt(K_PHASE, 0);
        switch (phase)
        {
            case 0:
                if (!Ready()) { if (now - t0 > 150) { Note("等待游戏就绪超时"); Finish(1); } return; }
                // 源端默认 <10 级不能开队（use_party_for_starters）。本号是 1 级新手，
                // 实拍要拍「有队员的面板」就必须临时打开这个开关，Finish 里还原。
                OpenPartyForStarters();
                // 上一轮残留的队伍会污染「空面板」那张，先退队再开窗。
                if (NativeSocialWindow.Request(SocialOperation.Read).party) NativeSocialWindow.Request(SocialOperation.LeaveParty);
                NativeKeyActions.Execute(GameAction.Party);
                Note("打开组队窗口，队伍=" + NativeSocialWindow.Request(SocialOperation.Read).party);
                Step(now + 3.0, 1);
                break;
            case 1:
                Shot("01-party-empty.png");
                Step(now + 1.5, 2);
                break;
            case 2:
                FillParty();
                Step(now + 3.0, 3);
                break;
            case 3:
                Shot("02-party-members.png");
                Step(now + 1.5, 4);
                break;
            default:
                Finish(0);
                break;
        }
    }

    // 真实链路：先自己开队，再逐个邀请在线假人。
    // 注意：假人没有「收到邀请要不要接受」的应答 AI（BotPartyService.Answer 只有真人客户端会调），
    // 邀请会一直挂到超时；所以邀请之后再用同一个队伍表把在线假人入队，
    // 数据仍是真实在线假人（真名/等级/职业/地图），只是省掉了握手这步。
    static void FillParty()
    {
        var state = NativeSocialWindow.Request(SocialOperation.Read);
        if (!state.party)
        {
            state = NativeSocialWindow.Request(SocialOperation.CreateParty);
            Note("建队 ok=" + state.ok + " err=" + state.error + " party=" + state.party + " id=" + state.partyId);
        }
        else Note("已在队伍中 id=" + state.partyId + " 成员=" + state.partyMembers.Count);
        int invited = 0;
        foreach (var bot in Server.Bot.BotRegistry.All().Values)
        {
            if (bot == null || !bot.online || string.IsNullOrEmpty(bot.name)) continue;
            if (bot.id == GameSession.CharacterId) continue;
            if (invited >= 5) break;
            var result = NativeSocialWindow.Request(SocialOperation.InviteParty, bot.name);
            Note("邀请 " + bot.name + " → ok=" + result.ok + " " + result.error);
            invited++;
        }

        int joined = 0;
        var parties = Server.Bot.BotTypeManager.Parties;
        if (parties != null)
        {
            var party = parties.For((int)GameSession.CharacterId);
            Note("队伍 " + (party == null ? "不存在" : "id=" + party.id + " 人数=" + party.members.Count));
            if (party != null)
                foreach (var bot in Server.Bot.BotRegistry.All().Values)
                {
                    if (party.members.Count >= 6) break;
                    if (bot == null || !bot.online || bot.id == GameSession.CharacterId) continue;
                    if (party.members.Contains(bot.id)) continue;
                    if (parties.Join(bot, party.id)) { joined++; Note("入队 " + bot.name + " lv" + bot.level + " map=" + bot.mapId); }
                }
        }
        else Note("BotTypeManager.Parties 未接线，跳过入队");

        var after = NativeSocialWindow.Request(SocialOperation.Read);
        Note("最终成员=" + after.partyMembers.Count + "（邀请 " + invited + "、入队 " + joined + "）");
        // 选中第二个成员：让选中行底色 0xFF244768 也进实拍，不然只能看到未选中态。
        if (after.partyMembers.Count > 1)
        {
            var field = typeof(NativeSocialWindow).GetField("selectedId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            field?.SetValue(null, after.partyMembers[1].id);
        }
    }

    // 只动一条 game_config（源端同名的开队等级开关），不碰玩家存档；Finish 里按快照还原。
    static void OpenPartyForStarters()
    {
        var db = GameSession.Server == null ? null : GameSession.Server.Db;
        if (db == null) { Note("服务端未就绪，跳过 game_config"); return; }
        var row = db.Table("game_config").Find(r => (string)r["config_code"] == "use_party_for_starters");
        if (row == null)
        {
            EditorPrefs.SetBool(K_CFG, false);
            db.Table("game_config").Insert(new DbRow { ["config_code"] = "use_party_for_starters", ["config_value"] = "1" });
            Note("game_config 新增 use_party_for_starters=1（跑完写回空值）");
        }
        else
        {
            EditorPrefs.SetBool(K_CFG, true);
            EditorPrefs.SetString(K_CFGV, (string)row["config_value"]);
            row["config_value"] = "1";
            Note("game_config use_party_for_starters " + EditorPrefs.GetString(K_CFGV) + " → 1");
        }
        db.Save();
    }

    static void RestoreStarterParty()
    {
        var db = GameSession.Server == null ? null : GameSession.Server.Db;
        if (db == null) return;
        var row = db.Table("game_config").Find(r => (string)r["config_code"] == "use_party_for_starters");
        if (row == null) return;
        // 原来没有这行就写空值（GetBool 视为 false），有就还原原值。
        row["config_value"] = EditorPrefs.GetBool(K_CFG, false) ? EditorPrefs.GetString(K_CFGV, "") : "";
        db.Save();
        Note("game_config use_party_for_starters 已还原");
    }

    static bool Ready() => GameLoop.I != null && GameLoop.I.player != null && GameLoop.I.state == GameLoop.GameState.Playing;

    static void Step(double at, int phase)
    { EditorPrefs.SetString(K_NEXT, at.ToString("R", CultureInfo.InvariantCulture)); EditorPrefs.SetInt(K_PHASE, phase); }

    static void Shot(string name)
    {
        string path = Path.Combine(EditorPrefs.GetString(K_DIR, "/tmp/mapledash-party"), name);
        try { ScreenCapture.CaptureScreenshot(path, 1); Note("截图 " + path); }
        catch (Exception e) { Note("截图失败 " + name + "：" + e.Message); }
    }

    static void Finish(int code)
    {
        try
        {
            RestoreStarterParty();
            if (EditorPrefs.GetBool(K_HAD, false)) PlayerPrefs.SetString(SnapKey, EditorPrefs.GetString(K_SNAP, ""));
            else PlayerPrefs.DeleteKey(SnapKey);
            if (EditorPrefs.GetBool(K_AUTO, false)) PlayerPrefs.SetInt("autostart", EditorPrefs.GetInt(K_AUTOV, 0));
            else PlayerPrefs.DeleteKey("autostart");
            PlayerPrefs.Save();
        }
        catch (Exception e) { Debug.LogWarning("[PartyShot] 存档恢复失败：" + e.Message); }

        foreach (var k in new[] { K_RUN, K_DIR, K_T0, K_PHASE, K_NEXT, K_SNAP, K_HAD, K_AUTO, K_AUTOV, K_CFG, K_CFGV }) EditorPrefs.DeleteKey(k);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        Note("完成 code=" + code);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }

    static double Parse(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    static void Note(string message) => Debug.Log("[PartyShot] " + message);
}
