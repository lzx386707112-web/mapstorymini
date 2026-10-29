using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 登录弹窗顺序的回归（无渲染，batchmode 可跑）：
//   源端顺序 = 连接 → （新号 accounts.gender = 10）Notice「可以使用的账号」→ 确定 → 性别 → 确定（SET_GENDER）
//   → 选区。本脚本直接在编辑器里 new 一个 LoginFlow，走它自己的 DoLogin / ConfirmNewAccount /
//   ConfirmNewGender，验的是真代码路径，不是另写一份判定。
//
//   Unity -batchmode -quit -projectPath <proj> -executeMethod LoginFlowChecks.Run -logFile /tmp/login_flow.log
//
// 判定口径：
//   PASS —— 与源端一致（弹窗顺序、DB 里的 gender、能不能重复改性别、autostart 不卡弹窗）
//   FAIL —— 回归失败
public static class LoginFlowChecks
{
    const string Tag = "[LoginCheck] ";
    static readonly BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
    static int pass, fail;

    static void Check(bool ok, string what, string detail)
    {
        if (ok) pass++; else fail++;
        Log("  " + (ok ? "✓ " : "✗ FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : "  — " + detail));
    }
    static void Log(string s) => Debug.Log(Tag + s);

    static object Field(object o, string name) => o.GetType().GetField(name, Priv).GetValue(o);
    static void SetField(object o, string name, object v) => o.GetType().GetField(name, Priv).SetValue(o, v);
    static void Call(object o, string name)
        => o.GetType().GetMethod(name, Priv, null, Type.EmptyTypes, null).Invoke(o, null);

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash-login-" + Guid.NewGuid() + ".json");
        int keepGender = PlayerPrefs.GetInt("login_new_gender", 0);
        GameObject go = null;
        try
        {
            var db = DbStore.Create(path);
            var server = GameServer.StartWith(db);
            var flags = BindingFlags.Static | BindingFlags.Public;
            typeof(GameSession).GetProperty("Net", flags).GetSetMethod(true).Invoke(null, new object[] { server.Transport });

            // LoginFlow 是 MonoBehaviour：编辑模式没有 Awake，静态实例手动塞进去。
            go = new GameObject("LoginFlowChecks");
            var flow = go.AddComponent<LoginFlow>();
            typeof(LoginFlow).GetField("inst", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, flow);
            SetField(flow, "account", "checknew");
            SetField(flow, "password", "pwd");

            // ── 素材：源端新账号确认框就是 Notice/text/6，别改成自绘文字 ──
            Log("════ 素材（UI/Login.img/Notice） ════");
            string newAccountImage = (string)typeof(LoginFlow).GetField("NewAccountImage",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Check(newAccountImage == "6", "新账号确认用源端原图 Notice/text/6「可以使用的账号」",
                "NewAccountImage=" + newAccountImage);
            Check(WzAssets.Node("wz://UI/Login.img/Notice/text/6") != null, "Notice/text/6 存在", "");
            Check(WzAssets.Node("wz://UI/Login.img/Notice/backgrnd/0") != null, "Notice/backgrnd/0 存在（362×219 纸面）", "");

            // 重装后首次显示声明必须读到末尾并返回，不能把缺失 TextXX 当成空串无限读下去。
            SetField(flow, "_eula", new GUIStyle { fontSize = 12 });
            Call(flow, "BuildEulaLayout");
            var eulaLines = (System.Collections.IList)Field(flow, "eulaLines");
            Check(eulaLines != null && eulaLines.Count > 0,
                "首次声明正文排版完成并返回", "lines=" + (eulaLines != null ? eulaLines.Count : 0));

            foreach (var line in eulaLines)
                foreach (var run in (System.Collections.IList)line.GetType().GetField("runs").GetValue(line))
                    if (((string)run.GetType().GetField("text").GetValue(run)).Contains("\\"))
                        throw new Exception("声明正文仍含未解析的换行转义");
            Check(true, "声明换行不残留反斜杠", "");

            var drag = typeof(LoginFlow).GetMethod("HandleEulaThumbDrag", Priv);
            var thumb = new Rect(100, 20, 12, 12);
            var track = new Rect(100, 20, 12, 200);
            var keepPos = Pointer.Pos;
            var keepPress = Pointer.PressPosition;
            bool keepDown = Pointer.Down, keepPressed = Pointer.Pressed;
            try
            {
                Pointer.Down = Pointer.Pressed = true;
                Pointer.Pos = Pointer.PressPosition = new Vector2(50, 25);
                drag.Invoke(flow, new object[] { thumb, track });
                Pointer.Pressed = false;
                Pointer.Pos = new Vector2(105, 150);
                drag.Invoke(flow, new object[] { thumb, track });
                Check((float)Field(flow, "noticeScroll") == 0f && (float)Field(flow, "eulaBarGrab") < 0f,
                    "在滚动条外按住再移入不会自动滚动", "");
                Pointer.Pressed = true;
                Pointer.Pos = Pointer.PressPosition = new Vector2(105, 25);
                drag.Invoke(flow, new object[] { thumb, track });
                Pointer.Pressed = false;
                Pointer.Pos = new Vector2(105, 150);
                drag.Invoke(flow, new object[] { thumb, track });
                Check((float)Field(flow, "noticeScroll") > 0f, "从滑块内按下可以拖动", "");
                Pointer.Down = false;
                drag.Invoke(flow, new object[] { thumb, track });
                Check((float)Field(flow, "eulaBarGrab") < 0f, "松开停止拖动", "");
            }
            finally
            {
                Pointer.Pos = keepPos; Pointer.PressPosition = keepPress;
                Pointer.Down = keepDown; Pointer.Pressed = keepPressed;
            }

            // ── 1. 新号点连接：先弹「可以使用的账号」，此时还没进性别 ──
            Log("════ 1. 新号连接 → 「可以使用的账号」 ════");
            Call(flow, "DoLogin");
            var pending = (LoginResponse)Field(flow, "pendingNewAccount");
            int accId = pending != null ? pending.accountId : 0;
            Check((bool)Field(flow, "noticeOpen") && !(bool)Field(flow, "newAccountOpen"),
                "新账号显示开源声明，不显示旧确认框", "");
            Check(!(bool)Field(flow, "genderOpen"), "这一步还不能弹性别框", "genderOpen=" + Field(flow, "genderOpen"));
            Check(pending != null && pending.gender == AccountsDao.GenderUnset,
                "服务端 LOGIN_STATUS 带回 gender=10（未选）", "gender=" + (pending != null ? pending.gender : -1));
            Check(AccountsDao.Gender(db, accId) == AccountsDao.GenderUnset, "库里 accounts.gender 还是 10",
                "db=" + AccountsDao.Gender(db, accId));
            Check((LoginStage)Field(flow, "stage") == LoginStage.Login, "仍停在登录页", "stage=" + Field(flow, "stage"));

            // ── 2. 点取消：只关弹窗，账号已经建好，下次连接还会再问一遍 ──
            Log("════ 2. 新账号框点「取消」 ════");
            Call(flow, "CancelNotice");
            Check(!(bool)Field(flow, "noticeOpen") && !(bool)Field(flow, "genderOpen"),
                "取消声明返回登录", "");
            Check(AccountsDao.FindById(db, accId) != null, "账号仍在库里（不会丢号）", "id=" + accId);
            Check(AccountsDao.Gender(db, accId) == AccountsDao.GenderUnset, "性别仍未选", "");
            Call(flow, "DoLogin");
            Check((bool)Field(flow, "noticeOpen"), "未完成注册再次连接仍显示声明", "");

            // ── 3. 点确定 → 性别框 ──
            Log("════ 3. 确定 → 性别框 ════");
            Call(flow, "AcceptNotice");
            Check(!(bool)Field(flow, "noticeOpen") && (bool)Field(flow, "genderOpen"),
                "关掉新账号框、弹出性别框", "");

            // ── 4. 选女 + 确定 → 写库 + 进选区 ──
            Log("════ 4. 性别确定（SET_GENDER） ════");
            SetField(flow, "newGender", 1);
            Call(flow, "ConfirmNewGender");
            Check(AccountsDao.Gender(db, accId) == 1, "accounts.gender 写回 1（女）", "db=" + AccountsDao.Gender(db, accId));
            Check(GameSession.AccountGender == 1, "会话里的 AccountGender 同步为 1", "" + GameSession.AccountGender);
            Check((LoginStage)Field(flow, "stage") == LoginStage.WorldSelect, "进入选区",
                "stage=" + Field(flow, "stage"));
            var worlds = (System.Collections.IList)Field(flow, "worlds");
            Check(worlds != null && worlds.Count > 0, "选区拿到大区列表", "worlds=" + (worlds != null ? worlds.Count : 0));

            // ── 5. 老号再连：不再弹任何框，直接进选区 ──
            Log("════ 5. 老号连接（gender 已定） ════");
            SetField(flow, "stage", LoginStage.Login);
            Call(flow, "DoLogin");
            Check(!(bool)Field(flow, "noticeOpen") && !(bool)Field(flow, "newAccountOpen") && !(bool)Field(flow, "genderOpen"),
                "老账号不弹声明或性别", "");
            Check((LoginStage)Field(flow, "stage") == LoginStage.WorldSelect, "直接进选区", "stage=" + Field(flow, "stage"));

            // ── 6. 性别只能定一次（源端 SetGenderHandler 只在 gender == 10 时处理） ──
            Log("════ 6. 性别不可二次修改 ════");
            bool again = GameSession.SetGender(0);
            Check(!again, "再发 SET_GENDER(男) 被拒", "ok=" + again);
            Check(AccountsDao.Gender(db, accId) == 1, "库里仍是 1（女）", "db=" + AccountsDao.Gender(db, accId));

            // ── 7. autostart：无人值守启动遇到 gender=10 自动补 0，别卡在弹窗上 ──
            Log("════ 7. autostart 自动补性别 ════");
            var auto = (bool)Call_Ret(flow, "TryAutoLogin");
            var autoRow = db.Table("accounts").Find(DbStore.Eq("name", "local"));
            int autoId = autoRow == null ? 0 : (int)Convert.ToInt64(autoRow["id"]);
            Check(auto, "TryAutoLogin 成功进游戏", "ok=" + auto);
            Check(autoId > 0 && AccountsDao.Gender(db, autoId) == 0, "新号性别自动落 0（男）",
                "id=" + autoId + " gender=" + (autoId > 0 ? AccountsDao.Gender(db, autoId) : -1));

            Log("完成  通过=" + pass + " 失败=" + fail);
        }
        catch (Exception e) { Debug.LogError(Tag + "自检自身异常：" + e); fail++; }
        finally
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            typeof(LoginFlow).GetField("inst", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            PlayerPrefs.SetInt("login_new_gender", keepGender);
            PlayerPrefs.Save();
            GameServer.Stop();
            if (File.Exists(path)) File.Delete(path);
            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }

    static object Call_Ret(object o, string name)
        => o.GetType().GetMethod(name, Priv, null, Type.EmptyTypes, null).Invoke(o, null);
}
