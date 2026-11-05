using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

// 技能窗「显示范围」回归检查（无渲染，batchmode 可跑）。
//
// 起因（2026-10-31 玩家反馈）：「用完 !maxskill，技能窗把二三转技能也显示出来了，我明明没转职」。
// 定位结论：技能窗的页签按 Profile.JobId 的职业链取书（新手 → 当前职业），本身没问题；
// 真正的原因是 PlayerPrefs 里残留了冒烟脚本的调试键 debug_cls=warrior，
// 每次启动把职业强改成 110（剑客·二转），于是新手看到「新手/剑士/剑客」三本书。
// 这里锁两件事：
//   1) 新手（job 0）跑完 !maxskill，技能窗只能有 1 个页签、且不含任何非新手技能；
//   2) debug_cls 是「读一次就删」的一次性覆盖，第二次加载必须回到存档职业。
public static class SkillPanelChecks
{
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Set(string name, object value) => typeof(GameSession).GetProperty(name).SetValue(null, value);
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;

    static List<Dictionary<string, object>> TabsOf(int jobId) => SkillBook.Tabs(SkillBook.TreeForJob(jobId));

    static string Describe(List<Dictionary<string, object>> tabs)
    {
        var parts = new List<string>();
        for (int i = 0; i < tabs.Count; i++)
        {
            var ids = new List<string>();
            if (tabs[i].TryGetValue("skills", out var raw) && raw is List<object> list)
                foreach (var o in list) ids.Add(o.ToString());
            parts.Add("tab" + i + "(job=" + Art.Int(tabs[i], "job") + ")=" + string.Join(",", ids));
        }
        return string.Join(" | ", parts);
    }

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "mxd-skill-panel-" + Guid.NewGuid().ToString("N") + ".db");
        var oldProfile = typeof(Profile).GetField("data", Flags).GetValue(null);
        var oldOverride = typeof(Profile).GetField("debugOverride", Flags).GetValue(null);
        var priorLoop = GameLoop.I;
        var go = new GameObject("SkillPanelChecks");
        try
        {
            // —— 1) 新手 + !maxskill：技能窗只能有「新手」一本书 ——
            typeof(Profile).GetField("data", Flags).SetValue(null, null);
            PlayerPrefs.DeleteKey("debug_cls");
            typeof(Profile).GetField("debugOverride", Flags).SetValue(null, false);
            var fresh = new PlayerProfileData();
            typeof(Profile).GetField("data", Flags).SetValue(null, fresh);
            typeof(Profile).GetField("debugOverride", Flags).SetValue(null, true);

            var db = DbStore.Open(path);
            var login = LoginService.Login(db, "admin", "skill-panel");
            var created = CharFactory.CreateCharacter(db, login.accountId, 0, "技能审计", 1,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var server = GameServer.StartWith(db);
            server.Social.Commands.Host = NativeGmCommandHost.Execute;
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", login.accountId);
            Set("CharacterId", created.characterId); Set("Character", CharService.Load(db, created.characterId));
            Set("InGame", true);
            var loop = go.AddComponent<GameLoop>(); GameLoop.I = loop;
            var playerObject = new GameObject("SkillPanelPlayer"); playerObject.transform.SetParent(go.transform);
            loop.player = playerObject.AddComponent<PlayerCtrl>(); loop.field = go.AddComponent<FieldDirector>();
            typeof(FieldDirector).GetProperty("Map").SetValue(loop.field, FieldCatalog.GetMap(100000005));
            GameSession.ApplyToProfile(); loop.player.ApplyProgression();
            Profile.LastMapId = 100000005;

            Require(Profile.JobId == 0, "新角色必须是新手（job 0），实际 " + Profile.JobId);
            Require(TabsOf(Profile.JobId).Count == 1, "新手技能窗只能有 1 个页签，实际：" + Describe(TabsOf(Profile.JobId)));

            Require(BottomBar.SubmitChat("!maxskill", NativeChatChannel.All), "!maxskill 通过真实聊天链路");
            var after = TabsOf(Profile.JobId);
            Require(after.Count == 1, "新手跑完 !maxskill 仍只能有 1 个页签（不能出现转职技能书），实际：" + Describe(after));

            // 页签里的每一行都必须属于该页签的职业（源端技能窗不会把别家技能塞进来）
            foreach (var tab in after)
            {
                int tabJob = Art.Int(tab, "job");
                if (!(tab.TryGetValue("skills", out var raw) && raw is List<object> list)) continue;
                foreach (var o in list)
                {
                    var d = SkillBook.Get(int.Parse(o.ToString()));
                    Require(d == null || d.job == tabJob, "页签 job=" + tabJob + " 里出现了 job=" + (d == null ? -1 : d.job) + " 的技能 " + o);
                }
            }
            // 数据层也不许把二三转技能学进来（源端是全表打满，我们只打当前职业线）
            Require(SkillProgress.LevelOf(1100000) == 0, "新手 !maxskill 不能学到 1100000（剑客·二转）");
            Require(SkillProgress.LevelOf(1110000) == 0, "新手 !maxskill 不能学到 1110000（勇士·三转）");
            Require(SkillProgress.LevelOf(2000000) == 0, "新手 !maxskill 不能学到 2000000（法师·一转）");
            Require(SkillProgress.LevelOf(1000) > 0, "新手 !maxskill 至少要打满蜗牛投掷术");

            // —— 2) debug_cls 必须一次性消费（否则职业会被永久劫持） ——
            // 用真实存档当基准，只读不写：覆盖生效的职业是 110（warrior→剑客·二转），
            // 键被删掉之后再加载必须回到存档里那个职业。
            int baselineJob = LocalProfileStore.Load().jobId;
            PlayerPrefs.SetString("debug_cls", "warrior");
            PlayerPrefs.Save();
            typeof(Profile).GetField("data", Flags).SetValue(null, null);
            typeof(Profile).GetField("debugOverride", Flags).SetValue(null, false);
            Require(Profile.JobId == 110, "debug_cls=warrior 应当覆盖成 job 110，实际 " + Profile.JobId);
            Require(!PlayerPrefs.HasKey("debug_cls"), "debug_cls 消费后必须从 PlayerPrefs 删除");

            typeof(Profile).GetField("data", Flags).SetValue(null, null);
            typeof(Profile).GetField("debugOverride", Flags).SetValue(null, false);
            Require(Profile.JobId == baselineJob, "第二次加载必须回到存档职业 " + baselineJob + "，实际 " + Profile.JobId);

            int expectedTabs = 0;
            for (int job = baselineJob; job >= 0; job = JobDefs.ParentOf(job)) expectedTabs++;
            Require(TabsOf(Profile.JobId).Count == expectedTabs,
                "职业 " + baselineJob + " 的技能书页签应为 " + expectedTabs + " 本，实际：" + Describe(TabsOf(Profile.JobId)));

            Debug.Log("SKILL_PANEL_PASS beginner_tabs=1 maxskill_own_job_only debug_cls_one_shot");
        }
        finally
        {
            PlayerPrefs.DeleteKey("debug_cls");
            UnityEngine.Object.DestroyImmediate(go); GameLoop.I = priorLoop; Set("InGame", false); GameServer.Stop();
            typeof(Profile).GetField("data", Flags).SetValue(null, oldProfile);
            typeof(Profile).GetField("debugOverride", Flags).SetValue(null, oldOverride);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
