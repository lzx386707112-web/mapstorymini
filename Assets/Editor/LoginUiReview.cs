using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 临时验收：逐屏截取登录 → 选区 → 选角 → 建号，核对界面确实由原版 IMG 组成。
public static class LoginUiReview
{
    public static void Run()
    {
        SessionState.SetBool("LoginUiReview", true);
        HudChecks.SetGameViewSize(1920, 869);
        SessionState.SetInt("LoginUiReview.auto", PlayerPrefs.GetInt("autostart", 0));
        SessionState.SetInt("LoginUiReview.save", PlayerPrefs.GetInt("login_save_id", 0));
        SessionState.SetString("LoginUiReview.acc", PlayerPrefs.GetString("login_account", ""));
        SessionState.SetInt("LoginUiReview.notice", PlayerPrefs.GetInt("login_notice_ok", 0));
        SessionState.SetInt("LoginUiReview.gender", PlayerPrefs.GetInt("login_new_gender", 0));
        PlayerPrefs.SetInt("autostart", 0);
        PlayerPrefs.SetInt("login_save_id", 0);
        PlayerPrefs.SetInt("login_notice_ok", 0);
        PlayerPrefs.SetInt("login_new_gender", 0);
        PlayerPrefs.Save();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (!SessionState.GetBool("LoginUiReview", false) || !EditorApplication.isPlaying || LoginFlow.I == null) return;
        SessionState.SetBool("LoginUiReview", false);
        new GameObject("Login UI review").AddComponent<LoginUiReviewProbe>();
    }
}

public class LoginUiReviewProbe : MonoBehaviour
{
    const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

    IEnumerator Start()
    {
        Application.runInBackground = true;
        yield return new WaitForSecondsRealtime(1.5f);

        var flow = LoginFlow.I;
        if (flow == null || GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Login)
        {
            Debug.LogError("[LoginUiReview] 登录界面没有启动");
            EditorApplication.Exit(1);
            yield break;
        }

        // Verify resource timing and alpha interpolation, not just that a screen renders.
        var animation = typeof(LoginFlow).GetMethod("Animation", Priv).Invoke(flow,
            new object[] { "UI/Login.img/Title/effect/0" });
        var sample = typeof(LoginFlow).GetMethod("AnimationFrame", Priv);
        object[] firstSample = { animation, 0.75f, true, 0f };
        object[] secondSample = { animation, 2.25f, true, 0f };
        int firstFrame = (int)sample.Invoke(flow, firstSample);
        int secondFrame = (int)sample.Invoke(flow, secondSample);
        if (firstFrame != 0 || secondFrame != 1 || Mathf.Abs((float)firstSample[3] - 120f / 255f) > 0.001f)
            throw new System.Exception("Native login animation timing/alpha mismatch");
        Debug.Log("[LoginUiReview] native delay/alpha PASS");

        // Pointer.Pos 使用 UiKit 的虚拟坐标（Screen/Scale），不是物理像素。
        float uiScale = Mathf.Clamp(Screen.height / 720f, 0.5f, 4f);
        float vw = Screen.width / uiScale, vh = Screen.height / uiScale;
        float s = Mathf.Min(vw / 1280f, vh / 720f);
        float offX = (vw - 1280f * s) / 2f;
        float offY = (vh - 720f * s) / 2f;
        Vector2 Design(float x, float y) => new Vector2(offX + x * s, offY + y * s);

        // 直接走源端登录，避免依赖输入焦点；先截图确认输入框文字压在木牌上
        typeof(LoginFlow).GetField("account", Priv).SetValue(flow, "local");
        typeof(LoginFlow).GetField("password", Priv).SetValue(flow, "local");
        yield return new WaitForSecondsRealtime(0.5f);

        yield return Shot("notice");

        // 用滚动条底部的下箭头滚到底（卷轴 431×426 居中 → 滚动条 x=815..827, y=187..504，
        // 下箭头中心 = 425+390+6, 147+40+317-6）。
        for (int i = 0; i < 8; i++)
        {
            Pointer.TestPos = Design(821, 498);
            Pointer.PressPosition = Pointer.TestPos.Value;
            Pointer.TestClick = true;
            yield return null; yield return null;
            Pointer.TestClick = false;
            yield return null;
            Pointer.TestPos = null;
            yield return new WaitForSecondsRealtime(0.12f);
        }
        float scrolled = (float)typeof(LoginFlow).GetField("noticeScroll", Priv).GetValue(flow);
        Debug.Log("[LoginUiReview] notice arrow scroll=" + scrolled);
        yield return Shot("notice-bottom");

        Pointer.TestPos = Design(728, 537);   // 开源声明 确定（卷轴右下橙色底板 266,374 + 75×32）
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return null;
        Pointer.TestPos = null;
        yield return new WaitForSecondsRealtime(0.5f);
        bool noticeClosed = !(bool)typeof(LoginFlow).GetField("noticeOpen", Priv).GetValue(flow);
        Debug.Log("[LoginUiReview] notice confirm click=" + noticeClosed);
        if (!noticeClosed) typeof(LoginFlow).GetField("noticeOpen", Priv).SetValue(flow, false);

        // 新账号流程：连接成功后先弹「可以使用的账号」，点确定才进性别选择。
        // 这里直接摆出弹窗（真实路径要名字没注册过才会走到，会往库里塞测试账号）。
        typeof(LoginFlow).GetField("newAccountOpen", Priv).SetValue(flow, true);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Shot("newaccount");

        Pointer.TestPos = Design(602, 435);   // 可以使用的账号 确定
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return null;
        Pointer.TestPos = null;
        yield return new WaitForSecondsRealtime(0.6f);
        yield return Shot("gender");

        Pointer.TestPos = Design(792, 271);   // 性别下拉框
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return null;
        Pointer.TestPos = null;
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Shot("gender-female");

        Pointer.TestPos = Design(822, 326);   // 性别 确定
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return null;
        Pointer.TestPos = null;
        yield return new WaitForSecondsRealtime(0.5f);
        bool genderClosed = !(bool)typeof(LoginFlow).GetField("genderOpen", Priv).GetValue(flow);
        Debug.Log("[LoginUiReview] gender confirm click=" + genderClosed
            + " newGender=" + typeof(LoginFlow).GetField("newGender", Priv).GetValue(flow)
            + " pref=" + PlayerPrefs.GetInt("login_new_gender", -1));
        if (!genderClosed) typeof(LoginFlow).GetField("genderOpen", Priv).SetValue(flow, false);

        // 性别弹窗确认会清空输入框，重新填入再走登录。
        typeof(LoginFlow).GetField("account", Priv).SetValue(flow, "local");
        typeof(LoginFlow).GetField("password", Priv).SetValue(flow, "local");
        yield return new WaitForSecondsRealtime(0.4f);

        // 账号输入框：真实点一下，确认焦点拿到（光标颜色由 LoginFlow 设定）。
        Pointer.TestPos = Design(750, 309);
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return null;
        Pointer.TestPos = null;
        yield return new WaitForSecondsRealtime(0.15f);
        object focus = typeof(LoginFlow).GetField("focusedControl", Priv).GetValue(flow);
        Debug.Log("[LoginUiReview] account field focus=" + focus);
        yield return Shot("login-caret");

        yield return Shot("login");

        // 先走真实指针点击（验证 OnGUI 命中区域），失败再退回反射直接登录。
        Pointer.TestPos = Design(882, 305);   // 连接按钮中心
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return new WaitForSecondsRealtime(0.6f);
        bool clicked = (LoginStage)typeof(LoginFlow).GetField("stage", Priv).GetValue(flow) == LoginStage.WorldSelect;
        Pointer.TestPos = null;
        Debug.Log("[LoginUiReview] login button click=" + clicked);
        if (!clicked) typeof(LoginFlow).GetMethod("DoLogin", Priv).Invoke(flow, null);
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot("world");

        // 选区默认是卷起的；点世界木板后展开频道卷轴。
        Pointer.TestPos = Design(486, 211);
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return new WaitForSecondsRealtime(0.8f);
        Pointer.TestPos = null;
        if ((int)typeof(LoginFlow).GetField("worldIndex", Priv).GetValue(flow) < 0)
            typeof(LoginFlow).GetMethod("SelectWorld", Priv).Invoke(flow, new object[] { 0 });
        yield return new WaitForSecondsRealtime(0.6f);
        yield return Shot("world-open");

        // 频道格：固定 20 个，只有前 3 个可用。CH.4 点击应被拒绝，CH.2 可选中。
        Pointer.TestPos = Design(798, 365);
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return null;
        Pointer.TestPos = null;
        yield return new WaitForSecondsRealtime(0.3f);
        int afterLocked = (int)typeof(LoginFlow).GetField("channelIndex", Priv).GetValue(flow);

        Pointer.TestPos = Design(610, 365);
        Pointer.PressPosition = Pointer.TestPos.Value;
        Pointer.TestClick = true;
        yield return null; yield return null;
        Pointer.TestClick = false;
        yield return null;
        Pointer.TestPos = null;
        yield return new WaitForSecondsRealtime(0.3f);
        int afterPick = (int)typeof(LoginFlow).GetField("channelIndex", Priv).GetValue(flow);
        Debug.Log("[LoginUiReview] channel locked click index=" + afterLocked + " pick CH.2 index=" + afterPick);

        // 没有角色时临时建一个，截完删掉
        int createdId = 0;
        var list = GameSession.CharList(GameSession.World, GameSession.Channel);
        if (list.characters.Count == 0)
        {
            var mk = GameSession.CreateChar(new CreateCharRequest
            {
                name = "UiReview01",
                job = CharFactory.JobGroupAdventurer,
                face = 20000, hair = 30300, hairColor = 0, skinColor = 0,
                top = 1040002, bottom = 1060002, shoes = 1072001, weapon = 1302000,
                gender = 0,
            });
            createdId = mk.characterId;
        }

        typeof(LoginFlow).GetMethod("EnterCharSelect", Priv).Invoke(flow, null);
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot("charselect");
        yield return new WaitForSecondsRealtime(0.27f);
        yield return Shot("charselect-animated");

        typeof(LoginFlow).GetField("racePick", Priv).SetValue(flow, 0);
        typeof(LoginFlow).GetField("stage", Priv).SetValue(flow, LoginStage.RaceSelect);
        yield return new WaitForSecondsRealtime(0.8f);
        yield return Shot("raceselect");

        typeof(LoginFlow).GetMethod("ResetAppearance", Priv).Invoke(flow, null);
        Debug.Log("[LoginUiReview] create gender=" + typeof(LoginFlow).GetField("gender", Priv).GetValue(flow)
            + " pref=" + PlayerPrefs.GetInt("login_new_gender", -1));
        typeof(LoginFlow).GetField("createStep", Priv).SetValue(flow, 0);
        typeof(LoginFlow).GetField("stage", Priv).SetValue(flow, LoginStage.CharCreate);
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot("charcreate");

        typeof(LoginFlow).GetField("createStep", Priv).SetValue(flow, 1);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return Shot("charcreate-look");
        yield return new WaitForSecondsRealtime(0.27f);
        yield return Shot("charcreate-look-animated");

        if (createdId > 0) GameSession.DeleteChar(createdId);
        PlayerPrefs.SetInt("autostart", SessionState.GetInt("LoginUiReview.auto", 0));
        PlayerPrefs.SetInt("login_save_id", SessionState.GetInt("LoginUiReview.save", 0));
        PlayerPrefs.SetString("login_account", SessionState.GetString("LoginUiReview.acc", ""));
        PlayerPrefs.SetInt("login_notice_ok", SessionState.GetInt("LoginUiReview.notice", 1));
        PlayerPrefs.SetInt("login_new_gender", SessionState.GetInt("LoginUiReview.gender", 0));
        PlayerPrefs.Save();
        Debug.Log("[LoginUiReview] PASS");
        EditorApplication.Exit(0);
    }

    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot("/tmp/mapledash-login-" + name + ".png");
        yield return new WaitForSecondsRealtime(0.6f);
        Debug.Log("[LoginUiReview] shot " + name);
    }
}
