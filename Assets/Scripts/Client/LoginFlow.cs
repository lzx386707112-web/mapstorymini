using System;
using System.Collections.Generic;
using UnityEngine;

// 源端登录流程（gms-server LoginServer 链路）：
//   Login → WorldSelect → CharSelect → RaceSelect → CharCreate → 进入频道
//
// 布局依据 docs/login-ui-source-analysis.md：
//   · 源端 bigLoginFrame=true 使用 1280×720 设计稿，整组按 screenHeight/720 缩放并居中
//   · 背景是 Map/Back/login.img/back/11 的不同相机高度裁切（登录 y≈1792，选区/选角 y≈592）
//   · 建号背景是 back/16；所有面板/按钮直接读 UI/Login.img 与 Map/Obj/login.img
// 不在 OnGUI 里画色块面板，也不导出 PNG。

public enum LoginStage { Login, WorldSelect, CharSelect, RaceSelect, CharCreate }

public class LoginFlow : MonoBehaviour
{
    static LoginFlow inst;
    public static LoginFlow I => inst;

    const float DW = 1280f, DH = 720f;
    const float LoginCameraY = 1792f;
    const float WorldCameraY = 1192f;
    const float TreeCameraY = 592f;
    const float RaceCameraY = 0f;

    const string UI = "UI/Login.img/";
    const string BACK = "Map/Back/login.img/";
    const string OBJ = "Map/Obj/login.img/";
    const string VERSION = "Ver. 0.83";

    LoginStage stage = LoginStage.Login;
    string account = "", password = "", charName = "";
    string status = "";
    bool saveId;

    List<WorldEntry> worlds = new List<WorldEntry>();
    List<ChannelEntry> channels = new List<ChannelEntry>();
    CharListResponse charList;
    int worldIndex = -1, channelIndex;
    int selectedSlot, page;
    int slotToDelete = -1;
    bool scrollOpen;
    bool noticeOpen, genderOpen;
    // 登录失败提示框：源端收到 LOGIN_STATUS(reason != 0) 就弹 UI/Login.img/Notice，
    // 不能只写一行小字（浅色小字压在草地上基本看不见，玩家会以为「点了没反应」）。
    bool alertOpen;
    string alertText = "";
    string alertImage;   // Notice/text/<n>；null 表示这个原因没有原图，退回文字
    string alertExtra;   // 追加一行（临时封停的解除时间）
    float noticeScroll;
    int newGender;
    bool focusAccount;
    string focusedControl = "";
    TouchScreenKeyboard loginKeyboard;
    float keyboardOpenedAt;
    bool keyboardPassword;
    bool keyboardCharName;
    GUIStyle loginField, charNameField;

    int racePick;
    int createStep;
    int jobGroup = CharFactory.JobGroupAdventurer;
    int gender, face, hair, hairColor, skinColor, top, bottom, shoes, weapon;
    int editRow;

    static readonly int[] FacesM = { 20000, 20001, 20002 };
    static readonly int[] FacesF = { 21000, 21001, 21002 };
    static readonly int[] AranFacesM = { 20100, 20401, 20402 };
    static readonly int[] AranFacesF = { 21700, 21201, 21002 };
    static readonly int[] HairsM = { 30290, 30300, 30310 };
    static readonly int[] HairsF = { 31000, 31040, 31050 };
    static readonly int[] HairColors = { 0, 2, 3, 7 };
    static readonly int[] Skins = { 0, 1, 2, 3 };
    static readonly int[] TopsM = { 1040002, 1040006, 1040010 };
    static readonly int[] TopsF = { 1041002, 1041006, 1041010, 1041011 };
    static readonly int[] BottomsM = { 1060002, 1060006 };
    static readonly int[] BottomsF = { 1061002, 1061008 };
    static readonly int[] Shoes = { 1072001, 1072005, 1072037, 1072038 };
    static readonly int[] Weapons = { 1302000, 1322005, 1312004 };
    static readonly int[] AranTop = { 1042167 };
    static readonly int[] AranBottom = { 1062115 };
    static readonly int[] AranShoes = { 1072383 };
    static readonly int[] AranWeapon = { 1442079 };

    float s = 1f, ox, oy, styleScale = -1f;
    bool modal;
    GUIStyle _label, _small, _tiny, _brown, _brownC, _error, _field, _linkShadow, _version, _hint, _row;
    GUIStyle _noticeTitle, _noticeText, _noticeUrl;
    GUIStyle _alert, _alertSmall;
    GUIStyle _genderValue;
    sealed class UiAnimation
    {
        public Texture2D[] Frames;
        public float[] Durations, Alpha0, Alpha1;
        public Vector2[] Origins;
        public float Duration;
    }
    readonly Dictionary<string, UiAnimation> uiAnimations = new Dictionary<string, UiAnimation>();
    readonly Dictionary<string, float> animationStarts = new Dictionary<string, float>();
    float pageStartedAt, worldScrollStartedAt, genderStartedAt, selectionStartedAt;
    bool genderWasOpen;
    int animatedSlot = -1;
    LoginStage animationStage = LoginStage.Login;
    readonly Dictionary<string, Sprite> previewSprites = new Dictionary<string, Sprite>();
    readonly Dictionary<int, string> equipNames = new Dictionary<int, string>();
    float lastSlotClickAt = -10f;
    int lastSlotClick = -1;

    void Awake() { inst = this; }

    void Start()
    {
        GameSession.Boot();
        saveId = PlayerPrefs.GetInt("login_save_id", 0) == 1;
        if (saveId) account = PlayerPrefs.GetString("login_account", "");

        int auto = PlayerPrefs.GetInt("autostart", 0);
        if (auto == 1)
        {
            bool ok = TryAutoLogin();
            Debug.Log("[LoginFlow] autostart=1 autoLogin=" + ok);
            if (ok) { GameLoop.I.EnterGameFromLogin(); return; }
        }
        newGender = Mathf.Clamp(PlayerPrefs.GetInt("login_new_gender", 0), 0, 1);
        noticeOpen = PlayerPrefs.GetInt("login_notice_ok", 0) == 0;
        stage = LoginStage.Login;
    }

    void OnDestroy() { if (inst == this) inst = null; }

    void OnDisable()
    {
        if (loginKeyboard != null) loginKeyboard.active = false;
        loginKeyboard = null;
    }

    bool TryAutoLogin()
    {
        var res = GameSession.Login("local", "local");
        if (res.code != LoginResultCode.Ok) { Debug.LogWarning("[LoginFlow] auto login code=" + res.code); return false; }
        GameSession.BeginSession(res.accountId, res.accountName, 0, 1);
        var server = GameSession.ServerList();
        int world = server.worlds.Count > 0 ? server.worlds[0].id : 0;
        GameSession.BeginSession(res.accountId, res.accountName, world, 1);

        var list = GameSession.CharList(world, 1);
        if (list.characters.Count == 0)
        {
            var mk = GameSession.CreateChar(new CreateCharRequest
            {
                name = "AutoHero",
                job = CharFactory.JobGroupAdventurer,
                face = 20000, hair = 30300, hairColor = 0, skinColor = 0,
                top = 1040002, bottom = 1060002, shoes = 1072001, weapon = 1302000,
                gender = 0,
            });
            if (mk.code != 0) { Debug.LogWarning("[LoginFlow] auto create code=" + mk.code + " " + mk.error); return false; }
            list = GameSession.CharList(world, 1);
        }
        if (list.characters.Count == 0) { Debug.LogWarning("[LoginFlow] auto login: no character"); return false; }
        bool selected = GameSession.SelectChar((int)list.characters[0].id);
        if (!selected) Debug.LogWarning("[LoginFlow] auto login: select char failed");
        return selected;
    }

    // —————————————————— 系统键 ——————————————————

    void Update()
    {
        if (GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Login) return;
        if (loginKeyboard != null)
        {
            if (keyboardCharName) charName = loginKeyboard.text;
            else if (keyboardPassword) password = loginKeyboard.text;
            else account = loginKeyboard.text;

            bool leftField = keyboardCharName
                ? (stage != LoginStage.CharCreate || createStep != 0)
                : (stage != LoginStage.Login);
            // 只在「玩家明确收键盘」时销毁它（Done / Canceled）。
            // 以前按「status != Visible 就关」处理，但 Android 上键盘刚弹出的头几帧
            // status 还不是 Visible（切前后台也会短暂 LostFocus），等于刚弹出来就被自己收走，
            // 表现就是「双击输入框不起键盘」。
            var st = loginKeyboard.status;
            bool closedByUser = st == TouchScreenKeyboard.Status.Done ||
                                st == TouchScreenKeyboard.Status.Canceled;
            if (leftField || closedByUser)
            {
                Debug.Log("[Login] 收起键盘 leftField=" + leftField + " status=" + st +
                          " held=" + (Time.unscaledTime - keyboardOpenedAt).ToString("0.0") + "s");
                loginKeyboard.active = false;
                loginKeyboard = null;
            }
        }
        // 悬浮球/菜单是覆盖层：按住它或面板打开时，登录页的键盘导航先让位
        if (GameSideMenu.Blocking || VirtualControlsEditor.Visible) return;
        if (noticeOpen)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f) noticeScroll = Mathf.Clamp01(noticeScroll - wheel * 0.22f);
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.PageDown))
                noticeScroll = Mathf.Clamp01(noticeScroll + 0.2f);
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.PageUp))
                noticeScroll = Mathf.Clamp01(noticeScroll - 0.2f);
            // 手机上拖动声明正文即可滚动（Pointer 的触摸是相对光标，不能依赖 Down 才响应）。
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Moved)
                    noticeScroll = Mathf.Clamp01(noticeScroll - t.deltaPosition.y / (200f * UiKit.Scale));
            }
            return;
        }
        if (genderOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) genderOpen = false;
            return;
        }
        if (alertOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) alertOpen = false;
            return;
        }
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (slotToDelete >= 0) { slotToDelete = -1; return; }
        switch (stage)
        {
            case LoginStage.WorldSelect:
                GameSession.Logout();
                stage = LoginStage.Login;
                account = ""; password = ""; status = "";
                break;
            case LoginStage.CharSelect:
                stage = LoginStage.WorldSelect;
                break;
            case LoginStage.RaceSelect:
                stage = LoginStage.CharSelect;
                break;
            case LoginStage.CharCreate:
                stage = LoginStage.RaceSelect;
                break;
            case LoginStage.Login:
                Application.Quit();
                break;
        }
    }

    // —————————————————— OnGUI ——————————————————

    void OnGUI()
    {
        if (GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Login) return;
        // 编辑器是全屏覆盖层，登录控件和快捷键在编辑期间暂停。
        if (VirtualControlsEditor.Visible) return;
        GUI.depth = -100;
        UiKit.Begin();
        Metrics();
        EnsureStyles();
        modal = noticeOpen || genderOpen || alertOpen || slotToDelete >= 0;
        LogReleaseOnce();
        if (animationStage != stage)
        {
            animationStage = stage;
            pageStartedAt = Time.unscaledTime;
            animationStarts.Clear();
        }

        if (genderOpen && !genderWasOpen) genderStartedAt = Time.unscaledTime;
        genderWasOpen = genderOpen;
        if (animatedSlot != selectedSlot)
        {
            animatedSlot = selectedSlot;
            selectionStartedAt = Time.unscaledTime;
        }

        switch (stage)
        {
            case LoginStage.Login: DrawLogin(); break;
            case LoginStage.WorldSelect: DrawWorldSelect(); break;
            case LoginStage.CharSelect: DrawCharSelect(); break;
            case LoginStage.RaceSelect: DrawRaceSelect(); break;
            case LoginStage.CharCreate: DrawCharCreate(); break;
        }

        if (stage == LoginStage.Login && !modal && !GameSideMenu.Blocking) HandleLoginKeys();
        DrawVersion();
        if (slotToDelete >= 0) DrawDeleteDialog();
        if (genderOpen) DrawGenderDialog();
        if (alertOpen) DrawAlertDialog();
        if (noticeOpen) DrawNoticeDialog();
        DrawLetterbox();
    }

    // 真机排查用：每次按下/释放记一行，能直接看出光标落在哪、有没有被 modal 挡住。
    // 一帧最多一条，正常游戏不受影响。
    static int releaseLogFrame = -1;
    void LogReleaseOnce()
    {
        if (Event.current.type != EventType.Repaint || releaseLogFrame == Time.frameCount) return;
        releaseLogFrame = Time.frameCount;
        if (Pointer.Released || Pointer.Pressed)
            Debug.Log("[Login] " + (Pointer.Released ? "release" : "press") +
                      " pos=" + Pointer.Pos.ToString("0") +
                      " press=" + Pointer.PressPosition.ToString("0") +
                      " modal=" + modal + " virtual=" + Pointer.UsesVirtualCursor +
                      " dragged=" + Pointer.Dragging);
    }

    void DrawLetterbox()
    {
        if (ox > 0f) UiKit.Fill(new Rect(0, 0, ox, UiKit.H), Color.black);
        if (ox + DW * s < UiKit.W) UiKit.Fill(new Rect(ox + DW * s, 0, UiKit.W - ox - DW * s, UiKit.H), Color.black);
        if (oy > 0f) UiKit.Fill(new Rect(0, 0, UiKit.W, oy), Color.black);
        if (oy + DH * s < UiKit.H) UiKit.Fill(new Rect(0, oy + DH * s, UiKit.W, UiKit.H - oy - DH * s), Color.black);
    }

    void Metrics()
    {
        s = Mathf.Min(UiKit.W / DW, UiKit.H / DH);
        if (s <= 0f) s = 1f;
        ox = (UiKit.W - DW * s) / 2f;
        oy = (UiKit.H - DH * s) / 2f;
    }

    Rect R(float x, float y, float w, float h) => new Rect(ox + x * s, oy + y * s, w * s, h * s);

    // 步骤页签、左侧菜单、版本号都画在 1280×720 设计稿内（源端不随窗口宽度拉伸）。
    void DrawPageAt(string path, float offsetX, float y)
    {
        var tex = Tex(path);
        if (tex != null) DrawTex(tex, R(offsetX, y, tex.width, tex.height));
    }

    void DrawSideMenu(bool showWorldMap)
    {
        if (showWorldMap && Button(UI + "WorldSelect/BtViewAll", R(0, 125, 99, 35)))
            status = "世界地图尚未开放";
        if (Button(UI + "ViewAllChar/BtVAC", R(0, 374, 97, 34)))
            status = "查看所有角色尚未开放";
        if (Button(UI + "Common/BtStart", R(0, 425, 98, 35)))
        {
            if (stage == LoginStage.WorldSelect)
            {
                GameSession.Logout();
                stage = LoginStage.Login;
                account = ""; password = ""; status = "";
            }
            else
            {
                scrollOpen = false;
                stage = LoginStage.WorldSelect;
                status = "";
            }
        }
    }

    void EnsureStyles()
    {
        // 光标/选区颜色每帧兜底设置：首帧 GUI.skin 可能还没就绪。
        if (GUI.skin != null)
        {
            GUI.skin.settings.cursorColor = Color.white;
            GUI.skin.settings.selectionColor = new Color(0.55f, 0.75f, 1f, 0.45f);
            GUI.skin.settings.cursorFlashSpeed = 1f;
        }
        if (Mathf.Approximately(styleScale, s) && _label != null) return;
        styleScale = s;
        _label = Style(14, TextAnchor.MiddleLeft, new Color(0.95f, 0.96f, 1f));
        _small = Style(12, TextAnchor.MiddleLeft, new Color(0.86f, 0.89f, 0.95f));
        _tiny = Style(11, TextAnchor.MiddleCenter, new Color(0.96f, 0.97f, 1f));
        _brown = Style(11, TextAnchor.MiddleLeft, new Color(0.28f, 0.16f, 0.07f));
        _brownC = Style(11, TextAnchor.MiddleCenter, new Color(0.28f, 0.16f, 0.07f));
        _row = Style(12, TextAnchor.MiddleCenter, new Color(0.12f, 0.08f, 0.03f));
        _row.clipping = TextClipping.Clip;
        _error = Style(12, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.5f));
        _linkShadow = Style(12, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.85f));
        _version = Style(9, TextAnchor.MiddleLeft, new Color(0.16f, 0.17f, 0.19f));
        _hint = Style(12, TextAnchor.MiddleLeft, new Color(0.29f, 0.31f, 0.35f));
        _noticeTitle = Style(12, TextAnchor.UpperLeft, new Color(0.29f, 0.17f, 0.12f));
        _noticeTitle.wordWrap = true;
        _noticeText = Style(11, TextAnchor.UpperLeft, new Color(0.33f, 0.20f, 0.14f));
        _noticeText.wordWrap = true;
        _noticeUrl = Style(12, TextAnchor.UpperLeft, new Color(0.33f, 0.20f, 0.14f));
        _alert = Style(13, TextAnchor.MiddleCenter, new Color(0.30f, 0.15f, 0.06f));
        _alert.wordWrap = true;
        _alertSmall = Style(12, TextAnchor.MiddleLeft, new Color(0.30f, 0.15f, 0.06f));
        _genderValue = Style(12, TextAnchor.MiddleLeft, new Color(0.10f, 0.09f, 0.08f));

        _field = new GUIStyle(GUI.skin.textField)
        {
            font = UiKit.Font,
            fontSize = Mathf.RoundToInt(14 * s),
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(Mathf.RoundToInt(5 * s), 2, 0, 0),
        };
        var clear = ClearTex();
        _field.normal.background = clear;
        _field.focused.background = clear;
        _field.hover.background = clear;
        _field.active.background = clear;
        _field.onNormal.background = clear;
        _field.onFocused.background = clear;
        _field.onHover.background = clear;
        _field.onActive.background = clear;
        _field.border = new RectOffset();
        _field.normal.textColor = Color.white;
        _field.focused.textColor = Color.white;
        _field.hover.textColor = Color.white;
        _field.active.textColor = Color.white;
        loginField = new GUIStyle(_field)
        {
            padding = new RectOffset(Mathf.RoundToInt(5 * s), 0, 0, 0),
            contentOffset = new Vector2(0f, -2f * s),
        };
        charNameField = new GUIStyle(loginField)
        {
            alignment = TextAnchor.MiddleLeft,
            contentOffset = Vector2.zero,
        };
    }

    GUIStyle Style(int size, TextAnchor anchor, Color color)
    {
        var st = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(8, Mathf.RoundToInt(size * s)),
            alignment = anchor,
            clipping = TextClipping.Overflow,
        };
        st.normal.textColor = color;
        return st;
    }

    // —————————————————— IMG 绘制 ——————————————————

    static Texture2D clearTex;
    static Texture2D ClearTex()
    {
        if (clearTex != null) return clearTex;
        clearTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        clearTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
        clearTex.Apply();
        return clearTex;
    }

    static Texture2D Tex(string path) => Art.Tex("wz://" + path);
    static bool Has(string path) => WzAssets.Node("wz://" + path) != null;

    static void DrawTex(Texture2D tex, Rect rect, float alpha = 1f)
    {
        if (tex == null) return;
        var old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
        GUI.color = old;
    }

    // 按 IMG 像素区域贴图；uv 以纹理左下角为原点。
    static void DrawTexUv(Texture2D tex, Rect rect, Rect uv, float alpha = 1f)
    {
        if (tex == null) return;
        var old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTextureWithTexCoords(rect, tex, uv);
        GUI.color = old;
    }

    static Rect UvRect(Texture2D tex, float x, float y, float w, float h)
        => new Rect(x / tex.width, 1f - (y + h) / tex.height, w / tex.width, h / tex.height);

    // 只拉伸纸面中段，左右描边和底边按原像素绘制。
    void DrawSliced(Texture2D tex, Rect dst, Rect src, float left, float right, float bottom)
    {
        if (tex == null) return;
        float bodyH = src.height - bottom;
        float dstBodyH = dst.height - bottom;
        float midW = dst.width - left - right;
        DrawTexUv(tex, new Rect(dst.x, dst.y, left, dstBodyH), UvRect(tex, src.x, src.y, left, bodyH));
        DrawTexUv(tex, new Rect(dst.x + left, dst.y, midW, dstBodyH), UvRect(tex, src.x + left, src.y, src.width - left - right, bodyH));
        DrawTexUv(tex, new Rect(dst.x + dst.width - right, dst.y, right, dstBodyH), UvRect(tex, src.x + src.width - right, src.y, right, bodyH));
        DrawTexUv(tex, new Rect(dst.x, dst.y + dstBodyH, left, bottom), UvRect(tex, src.x, src.y + bodyH, left, bottom));
        DrawTexUv(tex, new Rect(dst.x + left, dst.y + dstBodyH, midW, bottom), UvRect(tex, src.x + left, src.y + bodyH, src.width - left - right, bottom));
        DrawTexUv(tex, new Rect(dst.x + dst.width - right, dst.y + dstBodyH, right, bottom), UvRect(tex, src.x + src.width - right, src.y + bodyH, right, bottom));
    }

    void DrawTexTiledV(Texture2D tex, Rect rect, float alpha = 1f)
    {
        if (tex == null) return;
        for (float y = 0; y < rect.height; y += tex.height)
        {
            float h = Mathf.Min(tex.height, rect.height - y);
            DrawTexUv(tex, new Rect(rect.x, rect.y + y, rect.width, h), UvRect(tex, 0, 0, tex.width, h), alpha);
        }
    }

    void DrawAt(string path, float x, float y, bool centered = false, float alpha = 1f)
    {
        var tex = Tex(path);
        if (tex == null) return;
        Vector2 o = centered ? Art.Origin("wz://" + path) : Vector2.zero;
        DrawTex(tex, R(x - o.x, y - o.y, tex.width, tex.height), alpha);
    }

    void DrawFull(string path)
    {
        var tex = Tex(path);
        if (tex != null) DrawTex(tex, R(0, 0, DW, DH));
    }

    // 源端登录场景的云层会缓慢横向漂移；素材是 Map/Back/login.img/back/27（纯 alpha 云图）。
    void DrawClouds(float y, float speed, float alpha)
    {
        var tex = Tex(BACK + "back/27");
        if (tex == null) return;
        float drift = Mathf.Repeat(Time.unscaledTime * speed, tex.width);
        for (int i = -1; i < 3; i++)
            DrawTex(tex, R(i * tex.width - drift, y, tex.width, tex.height), alpha);
    }

    void DrawBalloon(string prefix, float x, float y, float w, float h)
    {
        var nw = Tex(prefix + "nw"); var n = Tex(prefix + "n");
        var ne = Tex(prefix + "ne"); var wTex = Tex(prefix + "w");
        var c = Tex(prefix + "c"); var e = Tex(prefix + "e");
        var sw = Tex(prefix + "sw"); var sTex = Tex(prefix + "s");
        var se = Tex(prefix + "se");
        if (nw == null || n == null || ne == null || wTex == null || c == null || e == null || sw == null || sTex == null || se == null)
            return;

        float left = wTex.width, right = e.width, top = nw.height, bottom = sTex.height;
        float midW = Mathf.Max(0, w - left - right), midH = Mathf.Max(0, h - top - bottom);
        DrawTex(nw, R(x, y, nw.width, nw.height));
        DrawTex(ne, R(x + w - ne.width, y, ne.width, ne.height));
        DrawTex(sw, R(x, y + h - sw.height, sw.width, sw.height));
        DrawTex(se, R(x + w - se.width, y + h - se.height, se.width, se.height));
        if (midW > 0)
        {
            DrawTex(n, R(x + left, y, midW, n.height));
            DrawTex(sTex, R(x + left, y + h - sTex.height, midW, sTex.height));
        }
        if (midH > 0)
        {
            DrawTex(wTex, R(x, y + top, wTex.width, midH));
            DrawTex(e, R(x + w - e.width, y + top, e.width, midH));
            DrawTex(c, R(x + left, y + top, midW, midH));
        }
    }

    void DrawTabHint()
    {
        // 源端这条提示是客户端自绘的系统气泡（非 IMG 素材），样式是浅灰圆角框。
        const float x = 507, y = 108, w = 232, h = 33;
        Rect box = R(x, y, w, h);
        UiKit.Fill(new Rect(box.x, box.y + 1.5f * s, box.width, box.height), new Color(0.42f, 0.48f, 0.55f, 0.28f));
        UiKit.Fill(box, new Color(0.86f, 0.89f, 0.94f, 0.95f));
        Label(R(x + 11, y, w - 20, h), "可以用Tab对菜单进行操作。", _hint);
    }

    UiAnimation Animation(string path)
    {
        if (uiAnimations.TryGetValue(path, out var cached)) return cached;
        var textures = new List<Texture2D>();
        var durations = new List<float>();
        var origins = new List<Vector2>();
        var alpha0 = new List<float>();
        var alpha1 = new List<float>();
        float total = 0f;
        var root = WzAssets.Node("wz://" + path);
        if (root != null)
            for (int i = 0; ; i++)
            {
                var node = root.Get(i.ToString());
                if (node == null) break;
                var tex = Tex(path + "/" + i);
                if (tex == null) break;
                textures.Add(tex);
                // IMG delay is milliseconds, not a fixed frame rate. Zero-delay frames
                // still need a finite duration so a looping sequence can advance.
                float duration = Mathf.Max(1, node.Int("delay", 100)) / 1000f;
                durations.Add(duration); total += duration;
                origins.Add(Art.Origin("wz://" + path + "/" + i));
                alpha0.Add(node.Int("a0", 255) / 255f);
                alpha1.Add(node.Int("a1", 255) / 255f);
            }
        cached = new UiAnimation { Frames = textures.ToArray(), Durations = durations.ToArray(),
            Origins = origins.ToArray(), Alpha0 = alpha0.ToArray(), Alpha1 = alpha1.ToArray(), Duration = total };
        uiAnimations[path] = cached;
        return cached;
    }

    int AnimationFrame(UiAnimation clip, float elapsed, bool loop, out float alpha)
    {
        alpha = 1f;
        if (clip.Frames.Length == 0) return -1;
        float time = loop ? Mathf.Repeat(Mathf.Max(0, elapsed), clip.Duration) : Mathf.Max(0, elapsed);
        for (int i = 0; i < clip.Frames.Length; i++)
        {
            if (time < clip.Durations[i] || i == clip.Frames.Length - 1)
            {
                alpha = Mathf.Lerp(clip.Alpha0[i], clip.Alpha1[i], Mathf.Clamp01(time / clip.Durations[i]));
                return i;
            }
            time -= clip.Durations[i];
        }
        return -1;
    }

    // Keep frame origins aligned even when successive canvases have different sizes.
    void DrawAnimationAt(string path, float x, float y, bool centered = false,
        bool loop = true, float startedAt = -1f)
    {
        var clip = Animation(path);
        int frame = AnimationFrame(clip, Time.unscaledTime - (startedAt < 0 ? pageStartedAt : startedAt), loop, out float alpha);
        if (frame < 0) return;
        Vector2 origin = clip.Origins[frame] - (centered ? Vector2.zero : clip.Origins[0]);
        var tex = clip.Frames[frame];
        DrawTex(tex, R(x - origin.x, y - origin.y, tex.width, tex.height), alpha);
    }

    void DrawState(string basePath, string state, Rect rect, bool animate = true)
    {
        string path = basePath + "/" + state;
        var clip = Animation(path);
        if (clip.Frames.Length == 0 && state != "normal")
        {
            path = basePath + "/normal";
            clip = Animation(path);
        }
        if (clip.Frames.Length == 0) { DrawTex(Tex(path), rect); return; }
        if (!animationStarts.TryGetValue(path, out float start))
            animationStarts[path] = start = Time.unscaledTime;
        int frame = AnimationFrame(clip, animate ? Time.unscaledTime - start : 0f, false, out float alpha);
        var tex = clip.Frames[frame];
        Vector2 delta = clip.Origins[frame] - clip.Origins[0];
        float sx = rect.width / clip.Frames[0].width, sy = rect.height / clip.Frames[0].height;
        DrawTex(tex, new Rect(rect.x - delta.x * sx, rect.y - delta.y * sy, tex.width * sx, tex.height * sy), alpha);
        // Leaving a state restarts its entry animation on the next hover/press.
        foreach (string other in new[] { "normal", "mouseOver", "pressed", "disabled" })
            if (other != state) animationStarts.Remove(basePath + "/" + other);
    }

    bool Button(string basePath, Rect rect, bool enabled = true, bool popup = false, bool animate = true)
    {
        bool hover = rect.Contains(Pointer.Pos);
        string state = !enabled ? "disabled" : hover && Pointer.Down ? "pressed" : hover ? "mouseOver" : "normal";
        DrawState(basePath, state, rect, animate);
        return Clicked(rect, enabled, popup);
    }

    // 登录页从头到尾都是「鼠标」口径：虚拟光标指到哪儿就是哪儿，不用手指落点。
    // 曾经把判定点换成触摸落点（TapPos），结果玩家把光标拖到按钮上再点，
    // 判定点落在手指那边 → 所有按钮和输入框全部点不动，登录失败也弹不出提示。
    bool Clicked(Rect rect, bool enabled = true, bool popup = false)
    {
        if (!enabled) return false;
        if (modal && !popup) return false;
        if (Event.current.type != EventType.Repaint) return false;
        if (!Pointer.Released || !rect.Contains(Pointer.Pos) || !rect.Contains(Pointer.PressPosition)) return false;
        AudioManager.Play("UI/BtMouseClick", 0.7f, false);
        return true;
    }

    bool TextLink(Rect rect, string text)
    {
        bool hover = rect.Contains(Pointer.Pos);
        GUI.Label(new Rect(rect.x + s, rect.y + s, rect.width, rect.height), text, _linkShadow);
        GUI.Label(rect, text, hover ? _label : _small);
        return Clicked(rect);
    }

    void Label(Rect rect, string text, GUIStyle style)
    {
        if (!string.IsNullOrEmpty(text)) GUI.Label(rect, text, style);
    }

    void DrawNameTag(float centerX, float topY, string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var left = Tex(UI + "CharSelect/nameTag/1/0");
        var mid = Tex(UI + "CharSelect/nameTag/1/1");
        var right = Tex(UI + "CharSelect/nameTag/1/2");
        if (left == null || mid == null || right == null) return;

        float textW = _tiny.CalcSize(new GUIContent(name)).x / s + 8;
        float total = Mathf.Max(left.width + right.width + mid.width, textW);
        float x = centerX - total * 0.5f;
        DrawTex(left, R(x, topY, left.width, left.height));
        float fill = total - left.width - right.width;
        int tiles = Mathf.Max(1, Mathf.CeilToInt(fill / mid.width));
        for (int i = 0; i < tiles; i++)
            DrawTex(mid, R(x + left.width + i * mid.width, topY, Mathf.Min(mid.width, fill - i * mid.width), mid.height));
        DrawTex(right, R(x + total - right.width, topY, right.width, right.height));
        GUI.Label(R(x, topY + 1, total, mid.height), name, _tiny);
    }

    void DrawVersion()
    {
        GUI.Label(R(1063, 8, 90, 16), VERSION, _version);
    }

    void DrawWorldBackground(float cameraY)
    {
        UiKit.Fill(new Rect(0, 0, UiKit.W, UiKit.H), Color.black);
        // back/11 的树/地面之外是透明区。源端这三个场景的晴空是客户端清屏色（不是 back/29 的灰白云），
        // 云层则来自 back/27。
        Color sky = Mathf.Abs(cameraY - RaceCameraY) < 1f
            ? new Color(0.22f, 0.44f, 1f)
            : Mathf.Abs(cameraY - WorldCameraY) < 1f
            ? new Color(0.58f, 0.74f, 1f)
            : new Color(0.35f, 0.54f, 1f);
        UiKit.Fill(R(0, 0, DW, DH), sky);
        if (Mathf.Abs(cameraY - RaceCameraY) < 1f)
        {
            DrawClouds(-180, 5f, 0.95f);
            DrawClouds(515, 5f, 0.95f);
        }
        else if (Mathf.Abs(cameraY - WorldCameraY) < 1f)
            DrawClouds(250, 5f, 0.55f);
        else
            DrawClouds(-10, 5f, 0.95f);
        DrawAt(BACK + "back/11", 0, -cameraY);
    }

    void DrawCreateBackground()
    {
        UiKit.Fill(new Rect(0, 0, UiKit.W, UiKit.H), Color.black);
        UiKit.Fill(R(0, 0, DW, DH), new Color(0.22f, 0.44f, 1f));
        DrawClouds(215, 6f, 0.95f);
        DrawAt(BACK + "back/16", -5, -26);
    }

    // —————————————————— 登录 ——————————————————

    void DrawLogin()
    {
        DrawWorldBackground(LoginCameraY);
        // 源端 logo 有呼吸/闪光动画，截帧呈半透明并压在光束下面。
        DrawAnimationAt(OBJ + "Title/logo/0", 431, 81, loop: false);
        DrawLoginEffects();
        DrawFull(UI + "Common/frame");

        DrawAt(OBJ + "Title/signboard/0/0", 563, 270);

        if (Button(UI + "Title/BtLogin", R(835, 283, 95, 45))) DoLogin();
        if (Button(UI + "Title/BtNew", R(636, 410, 92, 38))) NewAccount();
        if (Button(UI + "Title/BtHomePage", R(736, 408, 93, 38))) status = "请访问 BeiDouMS 官网";
        if (Button(UI + "Title/BtQuit", R(836, 408, 81, 40))) Application.Quit();
        if (Button(UI + "Title/BtLoginIDSave", R(659, 362, 51, 23))) ToggleSaveId();
        if (Button(UI + "Title/BtLoginIDLost", R(766, 360, 59, 23))) status = "请到论坛找回账号";
        if (Button(UI + "Title/BtPasswdLost", R(848, 362, 56, 23))) status = "请到论坛找回密码";
        DrawAt(UI + "Title/check/" + (saveId ? 1 : 0), 640, 362);

        DrawLoginField(R(690, 296, 135, 27), false);
        DrawLoginField(R(690, 322, 135, 27), true);
        if (focusAccount && !modal && !Application.isMobilePlatform)
        {
            GUI.FocusControl("login_account");
            focusAccount = false;
        }
        if (!modal) focusedControl = GUI.GetNameOfFocusedControl();

        if (!string.IsNullOrEmpty(status))
            Label(R(563, 520, 368, 24), status, _error);
    }

    static readonly Vector2[] LoginEffectAt =
    {
        new Vector2(766, 0), new Vector2(906, 36), new Vector2(906, 15),
        new Vector2(990, 0), new Vector2(905, 0), new Vector2(912, 2),
    };

    void DrawLoginEffects()
    {
        for (int i = 0; i < LoginEffectAt.Length; i++)
        {
            DrawAnimationAt(UI + "Title/effect/" + i, LoginEffectAt[i].x, LoginEffectAt[i].y);
        }
    }

    void DrawLoginField(Rect rect, bool secure)
    {
        string control = secure ? "login_password" : "login_account";
        string value = secure ? password : account;
        // 手机端不创建原生 TextField：光标点到输入框就起键盘。
        // 判定走的是鼠标口径（Pointer 光标，见 Clicked），单击即可 —— 源端也是单击聚焦、
        // 双击只是「选中一个词」；原来要求 Pointer.DoubleClicked，等于非要双击才给键盘。
        if (Application.isMobilePlatform)
            GUI.Label(rect, secure ? new string('*', value.Length) : value, loginField);
        else
        {
            GUI.SetNextControlName(control);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = !modal;
            if (secure) password = GUI.PasswordField(rect, password, '*', 20, loginField);
            else account = GUI.TextField(rect, account, 13, loginField);
            GUI.enabled = oldEnabled;
        }
        if (!Clicked(rect)) return;
        if (Application.isMobilePlatform)
        {
            Debug.Log("[Login] 点击输入框 secure=" + secure);
            OpenKeyboard(value, secure ? 20 : 13, secure, false);
        }
        else if (Pointer.DoubleClicked) GUI.FocusControl(control);
    }

    // 起系统键盘。maxLength 跟源端输入框一致：账号 13、密码 20、角色名 13。
    void OpenKeyboard(string value, int maxLength, bool secure, bool forCharName)
    {
        int target = forCharName ? 2 : secure ? 1 : 0;   // 0 账号 / 1 密码 / 2 角色名
        int current = keyboardCharName ? 2 : keyboardPassword ? 1 : 0;
        // 键盘已经为目标输入框开着：什么都别动。
        // 关掉再重开在 Android 上是真会失败的（open 返回非空但键盘不弹），
        // 所以「再点一下输入框」不能走 active=false + 重开。
        if (loginKeyboard != null && loginKeyboard.active && current == target) return;
        if (loginKeyboard != null) loginKeyboard.active = false;
        keyboardCharName = forCharName;
        keyboardPassword = !forCharName && secure;
        loginKeyboard = TouchScreenKeyboard.Open(value, TouchScreenKeyboardType.Default,
            false, false, secure, false, "", maxLength);
        keyboardOpenedAt = Time.unscaledTime;
        Debug.Log("[Login] 起键盘 char=" + forCharName + " secure=" + secure +
                  " keyboard=" + (loginKeyboard != null ? "ok" : "null"));
    }

    void HandleLoginKeys()
    {
        if (Event.current.type != EventType.KeyDown) return;
        if (Event.current.keyCode != KeyCode.Return && Event.current.keyCode != KeyCode.KeypadEnter) return;
        string focus = GUI.GetNameOfFocusedControl();
        if (focus != "login_account" && focus != "login_password") return;
        Event.current.Use();
        DoLogin();
    }

    void ToggleSaveId()
    {
        saveId = !saveId;
        PlayerPrefs.SetInt("login_save_id", saveId ? 1 : 0);
        if (!saveId) PlayerPrefs.DeleteKey("login_account");
        PlayerPrefs.Save();
    }

    void NewAccount()
    {
        newGender = Mathf.Clamp(PlayerPrefs.GetInt("login_new_gender", 0), 0, 1);
        genderOpen = true;
    }

    void ConfirmNewGender()
    {
        PlayerPrefs.SetInt("login_new_gender", newGender);
        PlayerPrefs.Save();
        genderOpen = false;
        account = ""; password = ""; status = "输入新账号和密码，点“连接”将自动注册";
        focusAccount = true;
    }

    void DoLogin()
    {
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
        {
            SetAlert("账号不能为空。请输入正确的账号和密码。", AlertEmptyImage);
            return;
        }
        if (account.Trim().Length > 13)
        {
            // 源端客户端输入框本身限 13 字符，服务端不做长度校验（LoginPasswordHandler 直接 readString）。
            SetAlert("账号不能超过 13 个字符，请确认后再输入。", null);
            return;
        }

        var res = GameSession.Login(account.Trim(), password);
        Debug.Log("[Login] account=" + account.Trim() + " code=" + res.code);
        if (res.code != LoginResultCode.Ok)
        {
            ShowAlert(res);
            return;
        }

        if (saveId)
        {
            PlayerPrefs.SetString("login_account", account.Trim());
            PlayerPrefs.SetInt("login_save_id", 1);
            PlayerPrefs.Save();
        }

        status = "";
        GameSession.BeginSession(res.accountId, res.accountName, 0, 1);
        worlds = GameSession.ServerList().worlds;
        if (worlds.Count == 0) { status = "没有可用大区"; return; }

        scrollOpen = false;
        worldIndex = -1;
        stage = LoginStage.WorldSelect;
    }

    // —————————————————— 登录失败提示（源端 UI/Login.img/Notice） ——————————————————

    // 文案/图取自客户端 UI/Login.img/Notice/text/<n> 的原图（tools/imgpng.py 导出对着抄），
    // 编号就是源端 LOGIN_STATUS 里那个 reason 字节所对应的提示条目。
    // 有原图的直接用原图（字体就是客户端那套点阵字），没有的才退化成文字。
    const string AlertEmptyImage = "4";   // 账号不能为空。请输入正确的账号和密码。

    void ShowAlert(LoginResponse res)
    {
        switch (res.code)
        {
            case LoginResultCode.WrongPassword:   // 源端 loginok = 4
                SetAlert("密码错误！请确认！", "3");
                break;
            case LoginResultCode.NotRegistered:   // 源端 loginok = 5
                SetAlert("账号不正确。请确认后再输入。", "2");
                break;
            case LoginResultCode.Banned:
                // 源端 getTempBan 会把到期时间一起发下来，永久封停没有时间。
                if (string.IsNullOrEmpty(res.banUntil)) SetAlert("您的账号已被封停。", "16");
                else SetAlert("您的账号已被封停。", "16", "解除时间：" + res.banUntil);
                break;
            case LoginResultCode.Blocked:
                SetAlert("该 IP 已被封停，无法连接。\n请联系管理员。", null);
                break;
            case LoginResultCode.AlreadyLoggedIn: // 源端 loginok = 7（本地是单机，正常走不到）
                SetAlert("该账号已登陆！请输入其它账号。", "5");
                break;
            case LoginResultCode.RegisterLimit:
                SetAlert("该 IP 注册的账号数已达上限。\n请稍后再试。", null);
                break;
            case LoginResultCode.Error:           // 源端 loginok = 6
                SetAlert("系统错误，无法连接！请稍后再试。", "15");
                break;
            default:                              // 源端 loginok = 14 等
                SetAlert("无法连接游戏服务器。请稍等后再试。", "22");
                break;
        }
    }

    void SetAlert(string text, string image, string extra = null)
    {
        alertText = text;
        alertImage = image;
        alertExtra = extra;
        alertOpen = true;
        status = "";   // 弹窗已经说明原因，不再在草地上叠一行小字
    }

    // 源端登录页的提示框：Notice/backgrnd/0（362×219 纸面）+ Notice/text/<n>（图文）+ BtYes「确定」。
    // 图文块的原始贴图偏移是 (119,21)：蘑菇挂件左侧占位，正文从它右边开始。
    void DrawAlertDialog()
    {
        UiKit.Fill(new Rect(0, 0, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.35f));
        float x = DW / 2 - 181, y = DH / 2 - 109;
        DrawAt(UI + "Notice/backgrnd/0", x, y);
        if (!string.IsNullOrEmpty(alertImage) && Has(UI + "Notice/text/" + alertImage))
            DrawAt(UI + "Notice/text/" + alertImage, x + 119, y + 21);
        else
            Label(R(x + 121, y + 48, 219, 78), alertText, _alert);
        if (!string.IsNullOrEmpty(alertExtra))
            Label(R(x + 121, y + 96, 219, 22), alertExtra, _alertSmall);
        if (Button(UI + "Notice/BtYes", R(x + 143, y + 168, 75, 32), true, true)) alertOpen = false;
    }

    // —————————————————— 选区 ——————————————————

    void SelectWorld(int i)
    {
        if (i < 0 || i >= worlds.Count) return;
        worldIndex = i;
        channelIndex = 0;
        var w = worlds[i];
        GameSession.BeginSession(GameSession.AccountId, GameSession.AccountName, w.id, 1);
        channels = WorldService.Channels(GameSession.Server.Db, w.id);
        if (!scrollOpen) worldScrollStartedAt = Time.unscaledTime;
        scrollOpen = true;
    }

    void DrawWorldSelect()
    {
        DrawWorldBackground(WorldCameraY);
        DrawFull(UI + "Common/frame");
        DrawPageAt(UI + "Common/step/1", 0, 33);

        if (scrollOpen) DrawAnimationAt(UI + "WorldSelect/scroll/0", 396, 139, loop: false, startedAt: worldScrollStartedAt);
        else DrawAt(UI + "WorldSelect/scroll/0/0", 396, 139);
        DrawAt(OBJ + "WorldSelect/signboard/0/0", 338, -2);
        if (scrollOpen && worldIndex >= 0 && Time.unscaledTime - worldScrollStartedAt >= 0.35f) DrawWorldContent();

        // 源端绳架上固定挂 15 块木板，有世界数据的显示世界名，其余是空木板模板。
        for (int i = 0; i < 15; i++)
        {
            float x = 472 + 28 * i, y = 164;
            if (i < worlds.Count)
            {
                int id = worlds[i].id;
                string path = Has(UI + "WorldSelect/BtWorld/" + id) ? UI + "WorldSelect/BtWorld/" + id : UI + "WorldSelect/BtWorld/t0";
                if (Button(path, R(x, y, 28, 95))) SelectWorld(i);
            }
            else
            {
                DrawEmptyWorldTab(x, y);
            }
        }

        DrawSideMenu(true);
        if (!string.IsNullOrEmpty(status)) Label(R(400, 60, 400, 22), status, _error);
    }

    // 空世界位在源端只露一块素木板 + 顶部绳结；t20 模板自带世界徽记/韩文，
    // 直接整块画会和源端不符，只取绳结和徽记下方的干净木纹两段 IMG 像素。
    void DrawEmptyWorldTab(float x, float y)
    {
        var tex = Tex(UI + "WorldSelect/BtWorld/t20/normal/0");
        if (tex == null) return;
        DrawTexUv(tex, R(x, y, 26, 20), UvRect(tex, 0, 0, 26, 20));
        DrawTexUv(tex, R(x, y + 20, 26, 75), UvRect(tex, 0, 88, 26, 1));
    }

    void DrawWorldContent()
    {
        const float sx = 396, sy = 139;
        var world = worlds[worldIndex];
        DrawAt(UI + "WorldSelect/BtWorld/chBackgrn", sx + 39, sy + 158);

        string worldImg = UI + "WorldSelect/world/" + world.id;
        if (Has(worldImg)) DrawAt(worldImg, sx + 73, sy + 141);
        else Label(R(sx + 73, sy + 141, 240, 40), world.eventMessage, _brown);

        // 源端固定绘制 20 个频道位（4 列×5 行）；本地只有前 3 个频道可用，其余用 disabled 贴图。
        int usableChannels = Mathf.Min(3, channels.Count);
        for (int i = 0; i < 20; i++)
        {
            float cx = sx + 74 + (i % 4) * 94, cy = sy + 211 + (i / 4) * 32;
            Rect cr = R(cx, cy, 92, 30);
            bool usable = i < usableChannels;
            bool enter = false;
            if (usable && Clicked(cr))
            {
                channelIndex = i;
                enter = Pointer.DoubleClicked;
            }
            else if (!usable && Clicked(cr)) status = "该频道尚未开放";
            var tex = Tex(UI + "WorldSelect/channel/" + i + "/" + (usable ? "normal" : "disabled"));
            if (tex != null) DrawTex(tex, cr);
            else GUI.Label(cr, "CH." + (i + 1), _brown);
            // 频道负载条：源端是客户端直接绘制的蓝色小进度条，非 IMG 素材。
            if (usable)
                UiKit.Fill(R(cx + 6, cy + 14, 8, 9), new Color(0.24f, 0.44f, 0.78f, 0.95f));
            if (usable && i == channelIndex)
            {
                var color = new Color(1f, 0.66f, 0.12f, 1f);
                UiKit.Fill(new Rect(cr.x, cr.y, cr.width, 2f * s), color);
                UiKit.Fill(new Rect(cr.x, cr.yMax - 2f * s, cr.width, 2f * s), color);
                UiKit.Fill(new Rect(cr.x, cr.y, 2f * s, cr.height), color);
                UiKit.Fill(new Rect(cr.xMax - 2f * s, cr.y, 2f * s, cr.height), color);
            }
            if (enter) { EnterCharSelect(); return; }
        }

        if (Button(UI + "WorldSelect/BtGoworld", R(sx + 360, sy + 358, 140, 41))) EnterCharSelect();

        if (!string.IsNullOrEmpty(world.recommendMessage))
        {
            float bx = sx - 108, by = sy + 163;
            DrawBalloon(UI + "WorldNotice/BalloonForLoginProcess/", bx, by, 186, 76);
            var title = Tex(UI + "WorldNotice/BalloonForLoginProcess/Title");
            if (title != null) DrawTex(title, R(bx + 8, by + 9, title.width, title.height));
            Label(R(bx, by + 40, 186, 24), world.recommendMessage, _row);
        }
    }

    void EnterCharSelect()
    {
        if (worldIndex < 0 || channels.Count == 0) return;
        var w = worlds[worldIndex];
        var ch = channels[Mathf.Clamp(channelIndex, 0, Mathf.Min(3, channels.Count) - 1)];
        GameSession.BeginSession(GameSession.AccountId, GameSession.AccountName, w.id, ch.channel);
        charList = GameSession.CharList(w.id, ch.channel);
        selectedSlot = charList.characters.Count > 0 ? 0 : -1;
        page = 0;
        status = "";
        stage = LoginStage.CharSelect;
    }

    // —————————————————— 选角 ——————————————————

    int Slots => charList != null ? Mathf.Max(1, charList.characterSlots) : 3;
    int PageCount => Mathf.Max(1, (Slots + 2) / 3);
    CharRecord CurrentChar()
    {
        if (charList == null || selectedSlot < 0 || selectedSlot >= charList.characters.Count) return null;
        return charList.characters[selectedSlot];
    }

    void DrawCharSelect()
    {
        DrawWorldBackground(TreeCameraY);
        DrawFull(UI + "Common/frame");
        DrawPageAt(UI + "Common/step/2", 0, 33);
        DrawPageAt(UI + "Common/selectWorld", 0, 96);
        DrawWorldPlateText();

        DrawCharSlots();

        DrawAt(OBJ + "CharSelect/signboard/0/0", 795, 189);
        bool has = CurrentChar() != null;
        bool canNew = charList != null && charList.characters.Count < Slots;
        if (Button(UI + "CharSelect/BtSelect", R(826, 207, 101, 30), has)) EnterGame();
        if (Button(UI + "CharSelect/BtNew", R(826, 245, 101, 35), canNew)) { racePick = 0; stage = LoginStage.RaceSelect; }
        if (Button(UI + "CharSelect/BtDelete", R(826, 293, 101, 43), has)) slotToDelete = selectedSlot;

        DrawTabHint();
        DrawSideMenu(false);

        if (PageCount > 1)
        {
            Rect left = R(306, 300, 86, 74), right = R(627, 301, 89, 73);
            DrawAt(UI + "CharSelect/pageL/" + (left.Contains(Pointer.Pos) && page > 0 ? 1 : 0) + "/0", 306, 300);
            DrawAt(UI + "CharSelect/pageR/" + (right.Contains(Pointer.Pos) && page < PageCount - 1 ? 1 : 0) + "/0", 627, 301);
            if (Clicked(left) && page > 0) ChangePage(page - 1);
            if (Clicked(right) && page < PageCount - 1) ChangePage(page + 1);
            Label(R(535, 390, 100, 20), (page + 1) + " / " + PageCount, _brown);
        }

        if (!string.IsNullOrEmpty(status)) Label(R(400, 60, 400, 22), status, _error);
    }

    void DrawWorldPlateText()
    {
        string worldName = worlds.Count > 0 && worldIndex >= 0 ? worlds[worldIndex].eventMessage : "";
        int ch = channels.Count > 0 ? channels[Mathf.Clamp(channelIndex, 0, channels.Count - 1)].channel : 1;
        Label(R(90, 108, 120, 20), worldName, _label);
        Label(R(90, 130, 120, 18), "CH." + ch, _small);
    }

    void ChangePage(int next)
    {
        page = Mathf.Clamp(next, 0, PageCount - 1);
        selectedSlot = page * 3;
        if (charList != null && selectedSlot >= charList.characters.Count) selectedSlot = charList.characters.Count - 1;
    }

    void DrawCharSlots()
    {
        var chars = charList?.characters ?? new List<CharRecord>();
        for (int i = 0; i < 3; i++)
        {
            int idx = page * 3 + i;
            float x = 492 + 125 * i, y = 429;
            if (idx < chars.Count)
            {
                var rec = chars[idx];
                bool selected = idx == selectedSlot;
                if (selected) DrawAnimationAt(UI + "CharSelect/effect/0", x, y - 330, true, startedAt: selectionStartedAt);
                DrawAnimationAt(UI + "CharSelect/character/0", x, y, true);
                DrawCharacter(CharPreview(rec), x, y, 1f);
                DrawNameTag(x, y + 2, rec.name);
                if (selected) DrawCharInfo(rec, x);
            }
            else if (idx < Slots)
            {
                DrawAt(UI + "CharSelect/character/1/0", x, y, true);
            }

            Rect hit = R(x - 25, y - 80, 50, 80);
            if (Clicked(hit))
            {
                if (lastSlotClick == idx && Time.unscaledTime - lastSlotClickAt < 0.35f) EnterGame();
                lastSlotClick = idx; lastSlotClickAt = Time.unscaledTime;
                if (idx < chars.Count) selectedSlot = idx;
            }
        }
    }

    void DrawCharInfo(CharRecord rec, float slotX)
    {
        float sx = slotX - 120, sy = 195;
        DrawAnimationAt(UI + "CharSelect/scroll/0", sx, sy, loop: false, startedAt: Mathf.Max(pageStartedAt, selectionStartedAt));
        if (Time.unscaledTime - Mathf.Max(pageStartedAt, selectionStartedAt) < 0.35f) return;
        DrawAt(UI + "CharSelect/charInfo", sx + 18, sy + 30);

        float x = sx + 18, y = sy + 30;
        Label(R(x + 43, y + 2, 60, 16), JobDefs.NameOf(rec.job), _brown);
        Label(R(x + 43, y + 20, 50, 16), rec.level.ToString(), _brown);
        Label(R(x + 132, y + 20, 40, 16), rec.fame.ToString(), _brown);
        Label(R(x + 43, y + 38, 50, 16), rec.str.ToString(), _brown);
        Label(R(x + 132, y + 38, 40, 16), rec.int_.ToString(), _brown);
        Label(R(x + 43, y + 56, 50, 16), rec.dex.ToString(), _brown);
        Label(R(x + 132, y + 56, 40, 16), rec.luk.ToString(), _brown);
        Label(R(x + 14, y + 101, 183, 16), "排名 " + rec.rank + "    人气 " + rec.fame, _brownC);
    }

    Sprite CharPreview(CharRecord rec)
    {
        string[] ids = GameSession.PreviewAppearance(rec);
        return AnimatedPreview(ids);
    }

    void EnterGame()
    {
        var rec = CurrentChar();
        if (rec == null) return;
        if (GameSession.SelectChar((int)rec.id)) { status = ""; GameLoop.I.EnterGameFromLogin(); }
        else status = "角色读取失败";
    }

    /// <summary>
    /// 被后台强制下线（封停）后回到登录页并显示原因。
    /// 与 ExitToCharSelect 的区别：这里连账号登录态一起清掉，所以不能停在选角屏。
    /// </summary>
    public void ForceLogout(string message)
    {
        account = "";
        password = "";
        charName = "";
        charList = null;
        worlds.Clear();
        channels.Clear();
        worldIndex = -1;
        channelIndex = 0;
        slotToDelete = -1;
        stage = LoginStage.Login;
        status = string.IsNullOrEmpty(message) ? "" : message;
    }

    public void BackToCharSelect()
    {        slotToDelete = -1;
        status = "";
        if (GameSession.AccountId > 0)
        {
            if (worlds.Count == 0)
            {
                worlds = GameSession.ServerList().worlds;
                worldIndex = worlds.Count > 0 ? 0 : -1;
                if (worldIndex >= 0) SelectWorld(worldIndex);
            }
            charList = GameSession.CharList(GameSession.World, GameSession.Channel);
            selectedSlot = charList.characters.Count > 0 ? 0 : -1;
        }
        page = 0;
        stage = LoginStage.CharSelect;
    }

    void DrawDeleteDialog()
    {
        UiKit.Fill(new Rect(0, 0, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.55f));
        var rec = CurrentChar();
        DrawAt(UI + "Notice/backgrnd/0", DW / 2 - 181, DH / 2 - 109);

        string name = rec != null ? rec.name : "";
        Label(R(DW / 2 - 150, DH / 2 - 70, 300, 24), "确定删除角色「" + name + "」？", _brown);
        Label(R(DW / 2 - 150, DH / 2 - 38, 300, 20), "删除后角色数据不可恢复。", _brown);

        if (Button(UI + "Notice/BtYes", R(DW / 2 - 100, DH / 2 + 50, 81, 41), true, true))
        {
            if (rec != null) GameSession.DeleteChar((int)rec.id);
            slotToDelete = -1;
            charList = GameSession.CharList(GameSession.World, GameSession.Channel);
            selectedSlot = charList.characters.Count > 0 ? 0 : -1;
            status = "";
        }
        if (Button(UI + "Notice/BtNo", R(DW / 2 + 20, DH / 2 + 50, 81, 41), true, true)) slotToDelete = -1;
    }

    // —————————————————— 弹窗：性别 / 开源声明 ——————————————————

    void DrawGenderDialog()
    {
        DrawAnimationAt(UI + "Gender/scroll/0", 595, 159, loop: false, startedAt: genderStartedAt);
        if (Time.unscaledTime - genderStartedAt < 0.35f) return;
        DrawAt(UI + "Gender/text/0", 617, 185);

        Label(R(617 + 134, 185 + 76, 60, 22), newGender == 0 ? "男" : "女", _genderValue);
        if (Clicked(R(617 + 126, 185 + 72, 98, 28), true, true))
            newGender = newGender == 0 ? 1 : 0;

        if (Button(UI + "Gender/BtYes", R(785, 310, 75, 32), true, true)) ConfirmNewGender();
        if (Button(UI + "Gender/BtNo", R(857, 310, 75, 32), true, true)) genderOpen = false;
    }

    const float NoticeX = 435f, NoticeY = 0f, NoticeW = 403f, NoticeH = 414f;
    const float NoticeViewX = 469f, NoticeViewY = 44f, NoticeViewW = 322f, NoticeViewH = 308f;
    const float NoticeContentH = 370f;

    void DrawNoticeDialog()
    {
        // 源端是客户端自绘的长卷，这里只用 IMG 像素拼：Gender 卷轴的空纸面 + NoticeScroll1 的卷轴头
        // + Basic.img 的滚动条 + Gender 的确定/取消按钮。
        DrawSliced(Tex(UI + "Gender/scroll/0/1"), R(NoticeX, NoticeY + 34, NoticeW, NoticeH - 34),
            new Rect(8, 24, 338, 170), 6, 6, 10);
        DrawTex(Tex(UI + "NoticeScroll1/0"), R(NoticeX - 6, NoticeY, NoticeW + 24, 38));

        Rect view = R(NoticeViewX, NoticeViewY, NoticeViewW, NoticeViewH);
        GUI.BeginGroup(view);
        float top = -noticeScroll * (NoticeContentH - NoticeViewH) * s;
        Label(new Rect(0, top + 9 * s, view.width, 22 * s), "《BeiDou》开源声明", _noticeTitle);
        Label(new Rect(0, top + 44 * s, view.width, 44 * s),
            "本项目为开源项目，所有商业行为与BeiDou团队无关，非商业用途均可免费下载使用，请谨防受骗。", _noticeText);
        Label(new Rect(0, top + 104 * s, view.width, 22 * s),
            "开源项目地址：https://github.com/BeiDouMS/BeiDou-Server", _noticeUrl);
        Label(new Rect(0, top + 278 * s, view.width, 22 * s), "- BeiDouMS开发团队", _noticeText);
        Label(new Rect(0, top + 299 * s, view.width, 22 * s), "- 2024年05月22日", _noticeText);
        GUI.EndGroup();

        // 滚动条在源端止于按钮上方；拉到底也不能压住“取消”的字。
        float barX = NoticeX + NoticeW - 24, barY = NoticeY + 37, barH = 320;
        bool onBar = R(barX, barY, 12, barH).Contains(Pointer.Pos);

        bool read = noticeScroll >= 0.985f;
        if (!onBar && Button(UI + "Gender/BtYes", R(691, 377, 75, 32), read, true))
        {
            PlayerPrefs.SetInt("login_notice_ok", 1);
            PlayerPrefs.Save();
            noticeOpen = false;
        }
        if (!onBar && Button(UI + "Gender/BtNo", R(766, 377, 75, 32), true, true)) noticeOpen = false;

        // 源端滚动条压在取消按钮上，后画才能露出底部的滑块/箭头。
        DrawNoticeBar(barX, barY, barH);
    }

    void DrawNoticeBar(float x, float y, float h)
    {
        const string root = "UI/Basic.img/VScr6/enabled/";
        const float w = 12f, thumbH = 20f;
        DrawTexTiledV(Tex(root + "base"), R(x, y + 12, w, h - 24));
        DrawTex(Tex(root + "prev0"), R(x, y, 12, 12));
        DrawTex(Tex(root + "next0"), R(x, y + h - 12, 12, 12));
        DrawTex(Tex(root + "thumb0"), R(x, y + 12 + noticeScroll * (h - 24 - thumbH), w, thumbH));
        Rect track = R(x, y + 12, w, h - 24);
        if (Pointer.Down && track.Contains(Pointer.Pos))
            noticeScroll = Mathf.Clamp01(Mathf.InverseLerp(track.y, track.yMax, Pointer.Pos.y));
        if (Clicked(track, true, true))
            noticeScroll = Mathf.Clamp01(Mathf.InverseLerp(track.y, track.yMax, Pointer.Pos.y));
        if (Clicked(R(x - 4, y, w + 8, 14), true, true)) noticeScroll = Mathf.Clamp01(noticeScroll - 0.2f);
        if (Clicked(R(x - 4, y + h - 14, w + 8, 14), true, true)) noticeScroll = Mathf.Clamp01(noticeScroll + 0.2f);
    }

    // —————————————————— 建号：职业 ——————————————————

    void DrawRaceSelect()
    {
        DrawWorldBackground(RaceCameraY);
        DrawFull(UI + "Common/frame");
        DrawPageAt(UI + "Common/step/3", 0, 33);
        DrawPageAt(UI + "Common/selectWorld", 0, 96);
        DrawWorldPlateText();

        DrawAt(UI + "RaceSelect/textGL/0", 646, 111, true);

        // 图1按 1280×720 书框归一化：骑士团在左、冒险家居中、战神在右。
        // 数组仍以职业编号索引，避免视觉顺序改变创建角色时的 jobGroup。
        int[] widths = { 223, 209, 181 };
        float[] cardX = { 536, 344, 752 };
        float[] cardY = { 130, 122, 124 };
        for (int i = 0; i < 3; i++)
        {
            Rect panel = R(cardX[i], cardY[i], widths[i], 222);
            if (Clicked(panel)) racePick = i;
            string node = i == 0 ? "normal/BtNormal" : i == 1 ? "knight/BtKnight" : "aran/BtAran";
            string anim = i == 0 ? "normal/OnAnimation" : i == 1 ? "knight/OnAnimation" : "aran/OnAnimation";
            DrawAt(UI + "RaceSelect/" + (i == racePick ? anim : node + (panel.Contains(Pointer.Pos) ? "/mouseOver" : "/normal")) + "/0", cardX[i], cardY[i]);
        }

        string desc = racePick == 0 ? "normal/text/0" : racePick == 1 ? "knight/text/0" : "aran/text/0";
        var descTex = Tex(UI + "RaceSelect/" + desc);
        if (descTex != null)
            DrawTex(descTex, R(360, 352, descTex.width, descTex.height));

        if (Button(UI + "RaceSelect/BtSelect", R(360f + 412f, 352f + 111f, 73f, 29f)))
        {
            jobGroup = racePick == 0 ? CharFactory.JobGroupAdventurer
                      : racePick == 1 ? CharFactory.JobGroupCygnus
                      : CharFactory.JobGroupAran;
            ResetAppearance();
            createStep = 0;
            charName = "";
            status = "";
            stage = LoginStage.CharCreate;
        }
        DrawSideMenu(false);
    }

    // —————————————————— 建号：外观 ——————————————————

    void ResetAppearance()
    {
        gender = Mathf.Clamp(PlayerPrefs.GetInt("login_new_gender", 0), 0, 1);
        editRow = 0;
        face = FaceList[0]; hair = HairList[0]; hairColor = HairColors[0]; skinColor = Skins[0];
        top = TopList[0]; bottom = BottomList[0]; shoes = ShoesList[0]; weapon = WeaponList[0];
    }

    int[] FaceList => jobGroup == CharFactory.JobGroupAran
        ? (gender == 0 ? AranFacesM : AranFacesF)
        : (gender == 0 ? FacesM : FacesF);
    int[] HairList => gender == 0 ? HairsM : HairsF;
    int[] TopList => jobGroup == CharFactory.JobGroupAran ? AranTop : (gender == 0 ? TopsM : TopsF);
    int[] BottomList => jobGroup == CharFactory.JobGroupAran ? AranBottom : (gender == 0 ? BottomsM : BottomsF);
    int[] ShoesList => jobGroup == CharFactory.JobGroupAran ? AranShoes : Shoes;
    int[] WeaponList => jobGroup == CharFactory.JobGroupAran ? AranWeapon : Weapons;

    void DrawCharCreate()
    {
        DrawCreateBackground();
        DrawFull(UI + "Common/frame");
        DrawPageAt(UI + "Common/step/3", 0, 33);
        DrawPageAt(UI + "Common/selectWorld", 0, 96);
        DrawWorldPlateText();

        if (createStep == 0)
        {
            DrawAt(UI + "NewChar/charName", 720, 155);
            // charName 原图内名称槽：(28,103)，148×24；点击区与文字共用。
            Rect nameRect = R(720 + 28, 155 + 103, 148, 24);
            if (Application.isMobilePlatform) GUI.Label(nameRect, charName, charNameField);
            else
            {
                GUI.SetNextControlName("char_name");
                charName = GUI.TextField(nameRect, charName, 13, charNameField);
            }
            if (Clicked(nameRect))
            {
                if (Application.isMobilePlatform) OpenKeyboard(charName, 13, false, true);
                else if (Pointer.DoubleClicked) GUI.FocusControl("char_name");
            }

            if (Button(UI + "NewChar/BtYes", R(747, 333, 81, 41))) ConfirmCharName();
            if (Button(UI + "NewChar/BtNo", R(821, 333, 81, 41))) stage = LoginStage.RaceSelect;
        }
        else
        {
            DrawAt(UI + "NewChar/charSet", 720, 155);
            for (int i = 0; i < 9; i++)
            {
                float y = 261 + 17 * i;
                DrawAt(UI + "NewChar/avatarSel/" + i + "/normal", 731, y);
                Label(R(800, y, 120, 17), RowValue(i), _row);
                if (Clicked(R(731, y, 200, 17))) editRow = i;
                if (Button(UI + "NewChar/BtLeft", R(792, y + 1, 15, 16))) CycleRowAt(i, -1);
                if (Button(UI + "NewChar/BtRight", R(923, y + 1, 15, 16))) CycleRowAt(i, 1);
            }

            if (Button(UI + "NewChar/BtYes", R(757, 485, 81, 41))) DoCreate();
            if (Button(UI + "NewChar/BtNo", R(832, 485, 81, 41))) createStep = 0;
        }

        var ids = AppearanceIds(skinColor, face, hair + hairColor, top, bottom, shoes, weapon);
        // 部件按 IMG 原生像素绘制，只应用与场景一致的整体 UI 缩放。
        DrawCharacter(AnimatedPreview(ids), 636, 398);

        DrawSideMenu(false);

        if (!string.IsNullOrEmpty(status)) Label(R(300, 600, 450, 22), status, _error);
    }

    void ConfirmCharName()
    {
        if (!GameSession.NameFormatOk(charName)) { status = "角色名需 4-13 位英文数字"; return; }
        var check = GameSession.CheckCharName(charName);
        if (!check.available) { status = check.reason == -2 ? "该名字已被使用" : "角色名不合法"; return; }
        status = "";
        createStep = 1;
    }

    string RowValue(int row)
    {
        switch (row)
        {
            case 0: return EquipName(face);
            case 1: return EquipName(hair);
            case 2: return HairColorName(hairColor);
            case 3: return SkinColorName(skinColor);
            case 4: return EquipName(top);
            case 5: return EquipName(bottom);
            case 6: return EquipName(shoes);
            case 7: return EquipName(weapon);
            default: return gender == 0 ? "男" : "女";
        }
    }

    void CycleRowAt(int row, int dir)
    {
        editRow = row;
        CycleRow(dir);
    }

    void CycleRow(int dir)
    {
        if (editRow == 0) face = Cycle(FaceList, face, dir);
        else if (editRow == 1) hair = Cycle(HairList, hair, dir);
        else if (editRow == 2) hairColor = Cycle(HairColors, hairColor, dir);
        else if (editRow == 3) skinColor = Cycle(Skins, skinColor, dir);
        else if (editRow == 4) top = Cycle(TopList, top, dir);
        else if (editRow == 5) bottom = Cycle(BottomList, bottom, dir);
        else if (editRow == 6) shoes = Cycle(ShoesList, shoes, dir);
        else if (editRow == 7) weapon = Cycle(WeaponList, weapon, dir);
        else
        {
            gender = gender == 0 ? 1 : 0;
            face = FaceList[0]; hair = HairList[0];
            top = TopList[0]; bottom = BottomList[0];
        }
    }

    static int Cycle(int[] list, int value, int dir)
    {
        int index = Array.IndexOf(list, value);
        if (index < 0) index = 0;
        index = (index + dir + list.Length) % list.Length;
        return list[index];
    }

    static string[] AppearanceIds(int skin, int face, int hairWithColor, int top, int bottom, int shoes, int weapon)
    {
        return new[]
        {
            Id8(2000 + skin),
            Id8(12000 + skin),
            Id8(face),
            Id8(hairWithColor),
            Id8(top), Id8(bottom), Id8(shoes), Id8(weapon),
        };
    }

    static int DefaultTop(int gender, int job) => job == CharFactory.JobGroupAran ? AranTop[0] : (gender == 0 ? TopsM[0] : TopsF[0]);
    static int DefaultBottom(int gender, int job) => job == CharFactory.JobGroupAran ? AranBottom[0] : (gender == 0 ? BottomsM[0] : BottomsF[0]);
    static int DefaultShoes(int job) => job == CharFactory.JobGroupAran ? AranShoes[0] : Shoes[0];
    static int DefaultWeapon(int job) => job == CharFactory.JobGroupAran ? AranWeapon[0] : Weapons[0];

    static string Id8(int id) => id.ToString("D8");

    // —————————————————— 原版名称 ——————————————————

    string EquipName(int id)
    {
        if (equipNames.TryGetValue(id, out var hit)) return hit;
        string category = EquipCategory(id);
        string name = null;
        if (category != null)
        {
            var node = WzAssets.Node("String/Eqp.img/Eqp/" + category + "/" + id);
            name = node?.Text("name");
        }
        if (string.IsNullOrEmpty(name)) name = id.ToString();
        equipNames[id] = name;
        return name;
    }

    static string EquipCategory(int id)
    {
        int type = id / 10000;
        switch (type)
        {
            case 2: return "Face";
            case 3: return "Hair";
            case 100: return "Cap";
            case 104: return "Coat";
            case 105: return "Longcoat";
            case 106: return "Pants";
            case 107: return "Shoes";
            case 108: return "Glove";
            case 109: return "Shield";
            case 110: return "Cape";
            default:
                return type >= 130 && type <= 160 ? "Weapon" : null;
        }
    }

    static string HairColorName(int color)
    {
        switch (color)
        {
            case 0: return "黑色";
            case 2: return "红色";
            case 3: return "橙色";
            case 7: return "绿色";
            default: return color.ToString();
        }
    }

    static string SkinColorName(int color)
    {
        switch (color)
        {
            case 0: return "浅色肌肤";
            case 1: return "小麦肌肤";
            case 2: return "深色肌肤";
            case 3: return "古铜肌肤";
            default: return color.ToString();
        }
    }

    // —————————————————— 角色预览 ——————————————————

    Sprite AnimatedPreview(string[] ids)
    {
        string appearance = string.Join("_", ids);
        int frame = WzCharacter.AppearanceFrame(ids, Time.unscaledTime - pageStartedAt);
        string key = appearance + "/" + frame;
        if (!previewSprites.ContainsKey(key)) AddPreview(key, ids, frame);
        return previewSprites.TryGetValue(key, out var sprite) ? sprite : null;
    }

    void AddPreview(string key, string[] ids, int frame)
    {
        if (previewSprites.ContainsKey(key)) return;
        try
        {
            var sprite = WzCharacter.Appearance(string.Join("_", ids), ids, frame);
            previewSprites[key] = sprite;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LoginFlow] 角色预览失败 " + key + ": " + e.Message);
            previewSprites[key] = null;
        }
    }

    void DrawCharacter(Sprite sprite, float feetX, float feetY, float scale = 1f)
    {
        if (sprite == null) return;
        float w = sprite.rect.width * scale, h = sprite.rect.height * scale;
        // Sprite.pivot 是像素坐标，不是 0..1。
        float px = sprite.pivot.x / sprite.rect.width;
        float py = sprite.pivot.y / sprite.rect.height;
        float dx = feetX - px * w;
        float dy = feetY - (1f - py) * h;
        DrawTex(sprite.texture, R(dx, dy, w, h));
    }

    void DoCreate()
    {
        if (!GameSession.NameFormatOk(charName)) { status = "角色名需 4-13 位英文数字"; return; }
        var check = GameSession.CheckCharName(charName);
        if (!check.available) { status = check.reason == -2 ? "该名字已被使用" : "角色名不合法"; return; }

        var res = GameSession.CreateChar(new CreateCharRequest
        {
            name = charName,
            job = jobGroup,
            face = face, hair = hair, hairColor = hairColor, skinColor = skinColor,
            top = top, bottom = bottom, shoes = shoes, weapon = weapon,
            gender = gender,
        });
        if (res.code != 0) { status = res.error ?? ("创建失败（" + res.code + "）"); return; }

        status = "";
        charList = GameSession.CharList(GameSession.World, GameSession.Channel);
        selectedSlot = charList.characters.Count > 0 ? charList.characters.Count - 1 : -1;
        page = Mathf.Max(0, selectedSlot / 3);
        stage = LoginStage.CharSelect;
    }
}
