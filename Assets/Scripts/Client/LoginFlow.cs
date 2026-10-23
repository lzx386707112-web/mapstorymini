using System;
using System.Collections.Generic;
using UnityEngine;

// 登录流程（需求 1：替换预设角色，走正规 登录→选区→建号→选角 流程）。
//
// 状态机与源端客户端一致：
//   Login → ServerSelect → ChannelSelect → CharSelect → CharCreate → （进游戏）
// 每一步都走 GameSession（ITransport），单机是进程内服务端，联机只换 Transport。
//
// 界面用 OnGUI 即时模式，和 Lobby / SkillWindow 同风格；中文文案。

public enum LoginStage { Login, ServerSelect, ChannelSelect, CharSelect, CharCreate }

public class LoginFlow : MonoBehaviour
{
    static LoginFlow inst;
    public static LoginFlow I => inst;

    LoginStage stage = LoginStage.Login;
    string account = "", password = "", charName = "";
    string status = "";
    int worldIndex, channelIndex;
    List<WorldEntry> worlds = new List<WorldEntry>();
    List<ChannelEntry> channels = new List<ChannelEntry>();
    CharListResponse charList;
    int slotToDelete = -1;

    // 建号选项（默认值 = 源端 ItemConstants 白名单第一项）
    int jobGroup = CharFactory.JobGroupAdventurer;   // 0 骑士团 / 1 冒险家
    int gender;                                       // 0 男 / 1 女
    int face = 20000, hair = 30300, hairColor, skinColor;
    int top = 1040002, bottom = 1060002, shoes = 1072001, weapon = 1302000;

    static readonly int[] FacesM = { 20000, 20001, 20002 };
    static readonly int[] FacesF = { 21000, 21001, 21002 };
    static readonly int[] HairsM = { 30290, 30300, 30310 };
    static readonly int[] HairsF = { 31000, 31040, 31050 };
    static readonly int[] HairColors = { 0, 2, 3, 7 };
    static readonly int[] Skins = { 0, 1, 2, 3 };
    static readonly int[] TopsM = { 1040002, 1040006, 1040010 };
    static readonly int[] TopsF = { 1041002, 1041006, 1041010, 1041011 };
    static readonly int[] BottomsM = { 1060002, 1060006 };
    static readonly int[] BottomsF = { 1061002, 1061008 };
    static readonly int[] Shoes = { 1072001, 1072005, 1072037, 1072038 };
    static readonly (int id, string name)[] Weapons =
    {
        (1302000, "单手剑"), (1322005, "单手钝器"), (1312004, "单手斧"),
    };

    GUIStyle _title, _label, _small, _btn, _field, _err;

    void Awake() { inst = this; }

    void Start()
    {
        GameSession.Boot();
        // 编辑器 / WebGL 自动化：autostart=1 时跳过 UI 直接进游戏。
        // 沿用旧的 autostart 开关（PlaySmokeTest / HairShot / NativeMapBrowser / WebBoot 都在写它），
        // 这些工具原本是「跳过大厅直接出战」，现在等价成「跳过登录直接进图」。
        if (PlayerPrefs.GetInt("autostart", 0) == 1 && TryAutoLogin())
        {
            EnterGame();
            return;
        }
        stage = LoginStage.Login;
    }

    /// <summary>无 UI 自动登录：local/local → 0 区 1 频道 → 第一个角色（没有就建一个）。</summary>
    bool TryAutoLogin()
    {
        var res = GameSession.Login("local", "local");
        if (res.code != LoginResultCode.Ok) return false;
        GameSession.BeginSession(res.accountId, res.accountName, 0, 1);
        var worlds = GameSession.ServerList().worlds;
        int world = worlds.Count > 0 ? worlds[0].id : 0;
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
            if (mk.code != 0) return false;
            list = GameSession.CharList(world, 1);
        }
        return list.characters.Count > 0 && GameSession.SelectChar((int)list.characters[0].id);
    }

    void OnDestroy() { if (inst == this) inst = null; }

    void OnGUI()
    {
        if (GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Login) return;
        UiKit.Begin();
        EnsureStyles();
        UiKit.Fill(new Rect(0, 0, UiKit.W, UiKit.H), new Color(0.05f, 0.07f, 0.11f));
        UiKit.Fill(new Rect(0, 0, UiKit.W, 4f), new Color(0.5f, 0.8f, 1f));

        switch (stage)
        {
            case LoginStage.Login: DrawLogin(); break;
            case LoginStage.ServerSelect: DrawServerSelect(); break;
            case LoginStage.ChannelSelect: DrawChannelSelect(); break;
            case LoginStage.CharSelect: DrawCharSelect(); break;
            case LoginStage.CharCreate: DrawCharCreate(); break;
        }

        if (slotToDelete >= 0) DrawDeleteConfirm();
    }

    void EnsureStyles()
    {
        if (_title != null) return;
        _title = new GUIStyle { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.97f, 1f) } };
        _label = new GUIStyle { fontSize = 16, normal = { textColor = new Color(0.85f, 0.88f, 0.94f) } };
        _small = new GUIStyle { fontSize = 13, normal = { textColor = new Color(0.7f, 0.74f, 0.82f) } };
        _btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        _field = new GUIStyle(GUI.skin.textField) { fontSize = 16 };
        _err = new GUIStyle { fontSize = 14, normal = { textColor = new Color(1f, 0.55f, 0.5f) } };
    }

    // —————————————————— 登录 ——————————————————

    void DrawLogin()
    {
        float cx = UiKit.W / 2f - 190f;
        GUI.Label(new Rect(cx, 150f, 380f, 40f), "冒险岛 · 登录", _title);
        GUI.Label(new Rect(cx, 196f, 380f, 22f), "账号不存在时将自动注册（与源端 automatic_register 一致）", _small);

        GUI.Label(new Rect(cx, 240f, 90f, 28f), "账号", _label);
        account = GUI.TextField(new Rect(cx + 92f, 238f, 288f, 28f), account, 13, _field);
        GUI.Label(new Rect(cx, 282f, 90f, 28f), "密码", _label);
        password = GUI.PasswordField(new Rect(cx + 92f, 280f, 288f, 28f), password, '*', 20, _field);

        if (GUI.Button(new Rect(cx, 330f, 380f, 40f), "登 录", _btn)) DoLogin();
        if (!string.IsNullOrEmpty(status))
            GUI.Label(new Rect(cx, 380f, 380f, 24f), status, _err);
    }

    void DoLogin()
    {
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
        { status = "请输入账号和密码"; return; }
        var res = GameSession.Login(account.Trim(), password);
        if (res.code != LoginResultCode.Ok)
        {
            status = res.code == LoginResultCode.WrongPassword ? "密码错误" : "登录失败（" + res.code + "）";
            return;
        }
        status = "";
        GameSession.BeginSession(res.accountId, res.accountName, 0, 1);
        worlds = GameSession.ServerList().worlds;
        stage = LoginStage.ServerSelect;
    }

    // —————————————————— 选区 ——————————————————

    void DrawServerSelect()
    {
        float cx = UiKit.W / 2f - 300f;
        GUI.Label(new Rect(cx, 90f, 600f, 40f), "选择大区", _title);
        float y = 150f;
        for (int i = 0; i < worlds.Count; i++)
        {
            var w = worlds[i];
            string flag = w.flag == 1 ? "［活动］" : w.flag == 2 ? "［新区］" : w.flag == 3 ? "［热门］" : "";
            string label = (i + 1) + " 区  " + w.eventMessage + flag + "    频道 " + w.channelSize + "    经验 x" + w.expRate;
            if (GUI.Button(new Rect(cx, y, 600f, 46f), label, _btn))
            {
                worldIndex = i;
                channels = WorldService.Channels(GameSession.Server.Db, w.id);
                GameSession.BeginSession(GameSession.AccountId, GameSession.AccountName, w.id, 1);
                stage = LoginStage.ChannelSelect;
            }
            y += 56f;
        }
        GUI.Label(new Rect(cx, y + 10f, 600f, 40f), worlds.Count > 0 ? worlds[0].serverMessage : "", _small);
        if (GUI.Button(new Rect(cx, y + 40f, 600f, 40f), "返回登录", _btn))
        {
            GameSession.Logout();
            stage = LoginStage.Login;
            account = ""; password = "";
        }
    }

    void DrawChannelSelect()
    {
        float cx = UiKit.W / 2f - 300f;
        GUI.Label(new Rect(cx, 90f, 600f, 40f), "选择频道", _title);
        float y = 150f;
        for (int i = 0; i < channels.Count; i++)
        {
            if (GUI.Button(new Rect(cx, y, 600f, 46f), "频道 " + channels[i].channel, _btn))
            {
                channelIndex = i;
                GameSession.BeginSession(GameSession.AccountId, GameSession.AccountName,
                    channels[i].worldId, channels[i].channel);
                charList = GameSession.CharList(channels[i].worldId, channels[i].channel);
                stage = LoginStage.CharSelect;
            }
            y += 56f;
        }
        if (GUI.Button(new Rect(cx, y + 10f, 600f, 40f), "返回大区", _btn)) stage = LoginStage.ServerSelect;
    }

    // —————————————————— 角色选择 ——————————————————

    void DrawCharSelect()
    {
        float cx = UiKit.W / 2f - 340f;
        GUI.Label(new Rect(cx, 60f, 680f, 40f), "选择角色", _title);
        GUI.Label(new Rect(cx, 100f, 680f, 22f),
            "账号 " + GameSession.AccountName + " · 角色位 " + (charList?.characters.Count ?? 0) + "/" + (charList?.characterSlots ?? 3), _small);

        float y = 140f;
        var chars = charList?.characters ?? new List<CharRecord>();
        for (int i = 0; i < (charList?.characterSlots ?? 3); i++)
        {
            bool has = i < chars.Count;
            string label = has
                ? (i + 1) + ".  " + chars[i].name + "   Lv." + chars[i].level + "   " + JobDefs.NameOf(chars[i].job)
                : (i + 1) + ".  （空）创建新角色";
            if (GUI.Button(new Rect(cx, y, 460f, 52f), label, _btn))
            {
                if (has)
                {
                    if (GameSession.SelectChar((int)chars[i].id)) EnterGame();
                    else status = "角色读取失败";
                }
                else OpenCreate();
            }
            if (has && GUI.Button(new Rect(cx + 470f, y, 90f, 52f), "删除", _btn))
                slotToDelete = (int)chars[i].id;
            y += 62f;
        }
        if (GUI.Button(new Rect(cx, y + 16f, 270f, 40f), "注销账号", _btn))
        {
            GameSession.Logout();
            stage = LoginStage.Login;
            account = ""; password = "";
        }
        if (GUI.Button(new Rect(cx + 282f, y + 16f, 270f, 40f), "返回大区", _btn))
        {
            worlds = GameSession.ServerList().worlds;
            stage = LoginStage.ServerSelect;
        }
    }

    /// <summary>战斗中「更换角色」回到选角屏：账号仍登录，重新拉一次角色列表。</summary>
    public void BackToCharSelect()
    {
        slotToDelete = -1;
        status = "";
        if (GameSession.AccountId > 0)
            charList = GameSession.CharList(GameSession.World, GameSession.Channel);
        stage = LoginStage.CharSelect;
    }

    // 删除确认（源端 DELETE_CHAR 需要二次确认 + 生日验证码；单机只保留确认这一步）
    void DrawDeleteConfirm()
    {
        var w = 420f;
        float x = UiKit.W / 2f - w / 2f, y = UiKit.H / 2f - 80f;
        UiKit.Fill(new Rect(x, y, w, 160f), new Color(0.12f, 0.14f, 0.19f));
        UiKit.Fill(new Rect(x, y, w, 4f), new Color(0.8f, 0.4f, 0.35f));
        GUI.Label(new Rect(x + 20f, y + 24f, w - 40f, 26f), "确定删除该角色？", _label);
        GUI.Label(new Rect(x + 20f, y + 54f, w - 40f, 22f), "删除后角色数据不可恢复。", _small);
        if (GUI.Button(new Rect(x + 20f, y + 100f, 170f, 40f), "确认删除", _btn))
        {
            GameSession.DeleteChar(slotToDelete);
            slotToDelete = -1;
            charList = GameSession.CharList(GameSession.World, GameSession.Channel);
        }
        if (GUI.Button(new Rect(x + w - 190f, y + 100f, 170f, 40f), "取消", _btn)) slotToDelete = -1;
    }

    void OpenCreate()
    {
        charName = "";
        jobGroup = CharFactory.JobGroupAdventurer;
        gender = 0;
        face = FacesM[0]; hair = HairsM[0]; hairColor = HairColors[0]; skinColor = Skins[0];
        top = TopsM[0]; bottom = BottomsM[0]; shoes = Shoes[0]; weapon = Weapons[0].id;
        status = "";
        stage = LoginStage.CharCreate;
    }

    // —————————————————— 建号 ——————————————————

    void DrawCharCreate()
    {
        float cx = UiKit.W / 2f - 340f;
        GUI.Label(new Rect(cx, 40f, 680f, 36f), "创建角色", _title);

        // 职业群（源端 CREATE_CHAR 的 job：0 骑士团 / 1 冒险家 / 2 战神）
        GUI.Label(new Rect(cx, 84f, 90f, 26f), "职业群", _label);
        int ng = GUI.SelectionGrid(new Rect(cx + 96f, 82f, 420f, 30f), jobGroup,
            new[] { "骑士团（新手村 130030000）", "冒险家（新手村 10000）" }, 2);
        if (ng != jobGroup)
        {
            jobGroup = ng;
            // 切职业群时把外观拉回该群默认（源端白名单对 0/1 通用，这里只刷新默认武器）
            weapon = Weapons[0].id;
        }

        GUI.Label(new Rect(cx, 124f, 90f, 26f), "性别", _label);
        int g = GUI.SelectionGrid(new Rect(cx + 96f, 122f, 240f, 30f), gender, new[] { "男", "女" }, 2);
        if (g != gender)
        {
            gender = g;
            face = (g == 0 ? FacesM : FacesF)[0];
            hair = (g == 0 ? HairsM : HairsF)[0];
            top = (g == 0 ? TopsM : TopsF)[0];
            bottom = (g == 0 ? BottomsM : BottomsF)[0];
        }

        GUI.Label(new Rect(cx, 164f, 90f, 26f), "角色名", _label);
        charName = GUI.TextField(new Rect(cx + 96f, 162f, 300f, 28f), charName, 13, _field);
        bool nameOk = GameSession.NameFormatOk(charName);
        GUI.Label(new Rect(cx + 402f, 166f, 220f, 24f),
            charName.Length == 0 ? "4-13 位英文数字" : nameOk ? "" : "格式不正确", nameOk || charName.Length == 0 ? _small : _err);

        float y = 206f;
        y = Row(cx, y, "脸型", (gender == 0 ? FacesM : FacesF), v => face = v);
        y = Row(cx, y, "发型", (gender == 0 ? HairsM : HairsF), v => hair = v);
        y = Row(cx, y, "发色", HairColors, v => hairColor = v);
        y = Row(cx, y, "肤色", Skins, v => skinColor = v);
        y = Row(cx, y, "上衣", (gender == 0 ? TopsM : TopsF), v => top = v);
        y = Row(cx, y, "下装", (gender == 0 ? BottomsM : BottomsF), v => bottom = v);
        y = Row(cx, y, "鞋子", Shoes, v => shoes = v);

        GUI.Label(new Rect(cx, y, 90f, 26f), "武器", _label);
        var names = new string[Weapons.Length];
        for (int i = 0; i < Weapons.Length; i++) names[i] = Weapons[i].name;
        int wi = 0;
        for (int i = 0; i < Weapons.Length; i++) if (Weapons[i].id == weapon) wi = i;
        wi = GUI.SelectionGrid(new Rect(cx + 96f, y - 2f, 420f, 30f), wi, names, 3);
        weapon = Weapons[wi].id;
        y += 40f;

        if (GUI.Button(new Rect(cx, y + 8f, 300f, 42f), "创建", _btn)) DoCreate();
        if (GUI.Button(new Rect(cx + 312f, y + 8f, 200f, 42f), "返回", _btn)) stage = LoginStage.CharSelect;
        if (!string.IsNullOrEmpty(status))
            GUI.Label(new Rect(cx, y + 58f, 680f, 24f), status, _err);
    }

    float Row(float cx, float y, string label, int[] options, Action<int> set)
    {
        GUI.Label(new Rect(cx, y, 90f, 26f), label, _label);
        var names = new string[options.Length];
        for (int i = 0; i < options.Length; i++) names[i] = options[i].ToString();
        int cur = 0;
        for (int i = 0; i < options.Length; i++)
        {
            int v = options[i];
            if ((label == "脸型" && v == face) || (label == "发型" && v == hair) || (label == "发色" && v == hairColor) ||
                (label == "肤色" && v == skinColor) || (label == "上衣" && v == top) || (label == "下装" && v == bottom) ||
                (label == "鞋子" && v == shoes)) cur = i;
        }
        int pick = GUI.SelectionGrid(new Rect(cx + 96f, y - 2f, 420f, 30f), cur, names, options.Length);
        set(options[pick]);
        return y + 40f;
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
        if (res.code != 0) { status = res.error ?? "创建失败（" + res.code + "）"; return; }
        charList = GameSession.CharList(GameSession.World, GameSession.Channel);
        stage = LoginStage.CharSelect;
    }

    // —————————————————— 进游戏 ——————————————————

    void EnterGame()
    {
        GameLoop.I.EnterGameFromLogin();
    }
}
