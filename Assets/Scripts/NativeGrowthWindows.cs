// 282 成长系统的三扇独立窗：徽章（ARC/AUT）、极限属性、内在能力。
//
// 素材来源（tools/ms282/import_growth_ui.py 导入）：
//   UI/UIEquip.img/Equip/Symbol         282 符文主窗、分类与三类徽章卡
//   UI/UIWindow4.img/HyperStat/Window   极限属性窗 185x318
//   UI/UIWindow4.img/AbilitySetting     内在能力窗 248x388
//
// 这三套在 282 里都由客户端绘制、服务端只存状态；本项目把公式结算放在
// NativeGrowthStats（派生属性层），窗口只负责「显示 + 发指令」，
// 任何数值都不在 UI 里另算一遍，避免窗口和面板对不上。
using System;
using System.Collections.Generic;
using System.Globalization;
using MapleDash.Wz;
using UnityEngine;

// 三个窗口共用的原版绘制底座：坐标缩放、原点对齐、按钮三态、字体取色。
internal static class GrowthUi
{
    public static float S = 1f;
    public static Rect R;
    public static bool Repaint => Event.current.type == EventType.Repaint;

    // 新建 GUIStyle 的 textColor 是纯黑，282 的窗口底板全是深色，不显式覆盖就是「看不见的字」。
    public static readonly Color Ink = new Color(.95f, .95f, .95f, 1f);

    public static Rect At(float x, float y, float w, float h)
        => new Rect(R.x + x * S, R.y + y * S, w * S, h * S);

    // 子画布的落位口径：x,y 就是画到窗口里的左上角。282 这些 UI 画布的原点都是
    // 「相对窗口左上角」表达的（AbilityGrade 原点 (-9,-162) 代表它落在 (9,162)），
    // 所以这里绝不能再减一次 origin——减了就整块飘出窗口。
    public static void Image(string root, string rel, float x, float y)
    {
        if (rel == "backgrnd" && MatrixPresentation.Active && GUI.enabled) MatrixPresentation.Regions(root);
        var node = WzAssets.Node(root + rel);
        if (node == null) return;
        var texture = WzAssets.Texture(node);
        if (texture == null) return;
        GUI.DrawTexture(At(x, y, texture.width, texture.height), texture);
    }

    // 子窗/弹窗的坐标系切换：282 的弹窗按钮 origin 是相对「弹窗左上角」的，
    // 直接拿窗口 R 去画会整排落到窗口左上角（V 窗口第一版就踩了这个坑）。
    // Shift 之后弹窗内部一律用资源里的原始坐标，画完 Restore 回窗口坐标系。
    public static Rect Shift(float x, float y, float w, float h)
    {
        var previous = R;
        R = new Rect(previous.x + x * S, previous.y + y * S, w * S, h * S);
        return previous;
    }

    public static void Restore(Rect previous) { R = previous; }

    // 折行文本（技能说明）：Style 是跨窗口共享的缓存，画完要把 wordWrap 复原，
    // 否则后面所有单行文本会跟着折。
    public static void TextWrapped(string root, string text, float x, float y, float w, float h,
        string font, Color? tint = null)
    {
        var style = Style(root, font, TextAnchor.UpperLeft, h, tint);
        bool wrap = style.wordWrap;
        style.wordWrap = true;
        GUI.Label(At(x, y, w, h), text, style);
        style.wordWrap = wrap;
    }

    // 按底边对齐贴图：ARC 的 arcane 标签是 19 高、AUT 的 authentic 是 12 高，
    // 两张卡片要用同一个基线，否则一排卡片看着一行高一降低。
    public static void ImageBottom(string root, string rel, float x, float bottom)
    {
        var node = WzAssets.Node(root + rel);
        if (node == null) return;
        var texture = WzAssets.Texture(node);
        if (texture == null) return;
        GUI.DrawTexture(At(x, bottom - texture.height, texture.width, texture.height), texture);
    }

    // 原版按钮：帧来自 <name>/normal/0 的尺寸与原点，三态贴图同名换目录。
    public static bool Button(string root, string rel)
    {
        var node = WzAssets.Node(root + rel + "/normal/0");
        if (node == null) return false;
        var texture = WzAssets.Texture(node);
        if (texture == null) return false;
        var origin = WzAssets.Origin(node);
        return ButtonAt(root, rel, At(-origin.x, -origin.y, texture.width, texture.height));
    }

    public static bool ButtonAt(string root, string rel, Rect rect)
    {
        bool hover = rect.Contains(Pointer.Pos);
        if (hover && MatrixPresentation.Active)
        {
            var tip = WzAssets.Node(root + rel);
            string value = GUI.enabled ? tip?.Text("ToolTip") : tip?.Text("ToolTip_Disabled");
            if (string.IsNullOrEmpty(value)) value = tip?.Text("toolTipDisabled");
            if (string.IsNullOrEmpty(value)) value = tip?.Text("ToolTip");
            MatrixPresentation.Hover(value);
        }
        bool enabled = GUI.enabled && (!NativeUtilPrompt.Visible || NativeUtilPrompt.Drawing);
        string state = !enabled ? "disabled" : hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal";
        var texture = WzAssets.Texture(WzAssets.Node(root + rel + "/" + state + "/0"))
            ?? WzAssets.Texture(WzAssets.Node(root + rel + "/normal/0"));
        if (texture != null) GUI.DrawTexture(rect, texture);
        if (!enabled || !Repaint || !Pointer.Clicked || !hover) return false;
        Pointer.ConsumeClick();
        AudioManager.Play("UI/BtMouseClick", .7f, false);
        return true;
    }

    public static void Text(string root, string text, float x, float y, float w, string font,
        TextAnchor align = TextAnchor.UpperLeft, float height = 18, Color? tint = null)
    {
        var style = Style(root, font, align, height, tint);
        GUI.Label(At(x, y, w, height), text, style);
    }

    // 深色/彩绘底图上写字：先落一层 1px 黑边，再压正文，免得被星空背景吃掉。
    public static void ShadowText(string root, string text, float x, float y, float w, string font,
        TextAnchor align = TextAnchor.UpperLeft, float height = 18, Color? tint = null)
    {
        var style = Style(root, font, align, height, new Color(0f, 0f, 0f, .85f));
        GUI.Label(At(x + 1, y + 1, w, height), text, style);
        Text(root, text, x, y, w, font, align, height, tint);
    }

    // 三扇窗每帧要画几十条字，每次 new GUIStyle 就是几十个对象的 GC 压力。
    // 按「字号 + 对齐 + 颜色」缓存，换字体（UiKit 重载）或换缩放时整表清掉。
    static readonly Dictionary<long, GUIStyle> styles = new Dictionary<long, GUIStyle>(32);
    static Font cachedFont;

    static GUIStyle Style(string root, string font, TextAnchor align, float height, Color? tint)
    {
        var node = string.IsNullOrEmpty(font) ? null : WzAssets.Node(root + font);
        int size = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(12, node?.Int("fontSize", 12) ?? 12) * S));
        Color color = tint ?? Ink;
        string hex = node?.Text("fontColor");
        if (tint == null && !string.IsNullOrEmpty(hex)
            && uint.TryParse(hex, NumberStyles.HexNumber, null, out uint packed))
            color = new Color32((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed, (byte)(packed >> 24));

        if (!ReferenceEquals(cachedFont, UiKit.Font)) { styles.Clear(); cachedFont = UiKit.Font; }
        long key = ((long)size << 48) ^ ((long)(int)align << 40) ^ ((long)(int)(color.r * 255) << 32)
                 ^ ((long)(int)(color.g * 255) << 24) ^ ((long)(int)(color.b * 255) << 16) ^ (long)(int)(color.a * 255);
        if (styles.TryGetValue(key, out var cached)) return cached;
        var style = new GUIStyle
        {
            font = UiKit.Font, fontSize = size, fontStyle = FontStyle.Bold,
            alignment = align, wordWrap = false, clipping = TextClipping.Overflow,
        };
        style.normal.textColor = color;
        styles[key] = style;
        return style;
    }

    // 数字位图（textUp/0..9、textDown/0..9）：282 的等级/增量是像素数字，不是字体。
    public static float Digits(string root, string folder, int value, float x, float y)
    {
        string text = Mathf.Abs(value).ToString(CultureInfo.InvariantCulture);
        foreach (char digit in text)
        {
            var node = WzAssets.Node(root + folder + "/" + digit);
            if (node == null) continue;
            var texture = WzAssets.Texture(node);
            if (texture == null) continue;
            GUI.DrawTexture(At(x, y, texture.width, texture.height), texture);
            x += texture.width;
        }
        return x;
    }

    // 纯色罩层：彩绘底图上要读数字就压黑（Plate），要标选中就提亮（Veil 白）。
    public static void Veil(float x, float y, float w, float h, Color color)
    {
        var saved = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(At(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = saved;
    }

    public static void Plate(float x, float y, float w, float h, float alpha = .55f)
        => Veil(x, y, w, h, new Color(0f, 0f, 0f, alpha));

    // 拖动条：点标题栏拖窗口，位置按未缩放坐标存，窗口换了缩放也不会跳。
    // handle / rightInset 让大窗把整条标题栏都做成抓手（默认仍是窄条 + 右侧留 30px 给按钮）。
    //
    // 触摸必须单独接一条：Pointer 把手指当成「相对光标 + 长按才等于按下」，抬手那一帧才
    // 给一次 Pressed。所以按住标题条直接拖，只会把光标拖走、窗口一动不动（安卓模拟器里
    // 鼠标事件也走触摸，表现一样）。这里直接读 Input.GetTouch：落指在标题条上就从那一帧
    // 起整段接管，按手指的 delta 搬窗口；Pointer 同时按同样的 delta 移动光标，
    // 于是光标始终黏在标题条的同一个点上，手感与鼠标一致。
    // owner：窗口令牌。同一帧里后画的窗口更靠上层，最后命中的那个才接管。
    static object dragOwner;
    static int dragFinger = -1;

    public static bool Drag(ref bool dragging, ref bool positioned, ref Vector2 position, ref Vector2 grab,
        float width, float height, Func<Rect> window, float handle = 28f, float rightInset = 30f,
        object owner = null, float slop = -1f)
    {
        if (!Repaint || !GUI.enabled || Pointer.Suppressed) return false;
        var strip = At(0, 0, width - rightInset, handle);
        var size = new Vector2(width, height);

        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Began || !strip.Contains(TouchPoint(touch.position))) continue;
                // 落指在摇杆/攻击键上：这一整段手势归 HUD，不能顺手把窗口拖走。
                if (VirtualControls.HitsAtScreen(touch.position)) continue;
                dragOwner = owner; dragFinger = touch.fingerId; dragging = true;
                if (!positioned) { position = window().position / S; positioned = true; }
            }
            if (owner != null && dragOwner == owner && dragFinger >= 0)
            {
                bool alive = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.fingerId != dragFinger) continue;
                    if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) break;
                    alive = true;
                    // 触摸的 deltaPosition 是屏幕像素、y 轴朝上；换算回窗口的 IMG 坐标。
                    if (touch.phase != TouchPhase.Began)
                        position = NativeUi.ClampWindowPosition(
                            position + new Vector2(touch.deltaPosition.x, -touch.deltaPosition.y)
                                       / Mathf.Max(.0001f, UiKit.Scale * S), size, S);
                    break;
                }
                if (!alive) { dragOwner = null; dragFinger = -1; dragging = false; Pointer.ConsumeClick(); }
                return true;
            }
        }
        else { dragOwner = null; dragFinger = -1; }

        // 鼠标（含安卓/模拟器外接的真鼠标）：按下那帧抓住。用 PressPosition 兜住
        // 「Pressed 已被本帧更早的窗口消费掉」的情况——按住仍算数。
        if (!dragging && (Pointer.Pressed || Pointer.Down)
            && strip.Contains(Pointer.Pressed ? Pointer.Pos : Pointer.PressPosition))
        {
            dragging = positioned = true;
            position = window().position / S;
            grab = Pointer.Pos / S - position;
            Pointer.ConsumeClick();
            return true;
        }
        if (!dragging && slop > 0f && BodyDrag(ref dragging, ref positioned, ref position, ref grab,
                size, window, owner, slop)) return true;
        if (!dragging) return false;
        if (Pointer.Down) position = NativeUi.ClampWindowPosition(Pointer.Pos / S - grab, size, S);
        else { dragging = false; Pointer.ConsumeClick(); }
        return true;
    }

    // slop > 0：窗口内任意一处按下，位移超过 slop 才把这次「点击」升级成「拖动」。
    // 只给 V/HEXA 这种大面板用——标题条那 46px 不一定好按，整块面板都能抓更稳。
    // 不能给触摸用：触摸的位移本来就是用来挪光标的，抢成拖动会让点选变糊。
    static object slopOwner;
    static Vector2 slopFrom;

    static bool BodyDrag(ref bool dragging, ref bool positioned, ref Vector2 position, ref Vector2 grab,
        Vector2 size, Func<Rect> window, object owner, float slop)
    {
        if (Pointer.Pressed && window().Contains(Pointer.Pos)) { slopOwner = owner; slopFrom = Pointer.Pos; }
        if (!Pointer.Down) { slopOwner = null; return false; }
        if (slopOwner != owner || (Pointer.Pos - slopFrom).sqrMagnitude <= slop * slop) return false;
        // 以当前点重设抓点，窗口不会因为先走了 slop 那几步而跳一下。
        dragging = positioned = true;
        position = window().position / S;
        grab = Pointer.Pos / S - position;
        slopOwner = null;
        Pointer.ConsumeClick();
        return true;
    }

    // 屏幕像素 → UiKit 坐标（与 Pointer 的鼠标换算同一口径）。
    static Vector2 TouchPoint(Vector2 screen)
        => new Vector2(screen.x / UiKit.Scale, (Screen.height - screen.y) / UiKit.Scale);
}

// —————————————————— 极限属性（UIWindow4.img/HyperStat/Window） ——————————————————
// 素材实测：backgrnd 185x318(0,0)、backgrnd2 173x304(6,7)、backgrnd3 150x214(13,41)；
// BtReduce 17x16 落在 (11,288)，BtReset 50x16 落在 (123,288)；
// 预设 1/2/3 各 17x16 在 (60|80|100,267)，BtpresetApplication 46x16 在 (126,267)。
// 17 条属性的名字是 statList/800004xx 的位图。
//
// 列表区口径（2026-11-04 重测 backgrnd3 像素）：这张 150x214 的底图里**烘焙了 12 行**，
// 每行行条 16 高、行距 18（条与条之间留 2px 缝），第 i 行条顶 = backgrnd3 的 y0 + i*18，
// 12 行正好铺到 216（最后一行被底图边缘裁掉 2px）。以前按「13 行 × 16」画，每行累积
// 2px 偏差，到第 12 行已经偏了 22px，表现就是「属性名越往下越往下沉、最后一行掉出列表区，
// 选中高亮条也跟着错行」。行条内的横向结构同样是烘焙的：蓝条 0..70（窗口 13..84）、
// LEVEL 标签 71..103、白色数值区 104..148（窗口 117..161）。
public static class NativeHyperStatWindow
{
    const string Root = "UI/UIWindow4.img/HyperStat/Window/";
    // 行内的加点按钮借 AutEquip 的 textUp/+（6x8）。282 原本 outlink 到
    // _Canvas/UIWindow2.img 的 BtLVup，但那张图 57 MiB，为一个加号不值。
    const string PlusRoot = "UI/UIAuthenticForce.img/AutEquip/";
    const float Width = 185, Height = 318;
    // 可见行数/行距/列表顶必须与 backgrnd3 的烘焙行一致：12 行 x 18，行条 16 高。
    const int VisibleRows = 12, RowStep = 18, BarHeight = 16;
    const float ListTop = 41, ListLeft = 13, ListWidth = 150, ListHeight = 214;
    // 白色数值区（窗口坐标）：backgrnd3 的 104..148，换成窗口是 117..161。
    const float LevelLeft = 117, LevelWidth = 44;
    // 数值框是白底，GrowthUi 默认的 Ink 是近白，压上去等于没字。
    static readonly Color LevelInk = new Color(.22f, .22f, .26f, 1f);
    static bool visible, dragging, positioned;
    static Vector2 position, grab;
    static int owner, preset, activePreset, selected, scroll;
    static HyperStatResponse state;
    static string error;

    public static bool Visible => visible;
    public static bool AtPointer => visible && (dragging || Window.Contains(Pointer.Pos));
    static float S => Mathf.Min(BottomBar.S, Mathf.Min((UiKit.W - 24) / Width, UiKit.H * .85f / Height));
    static Vector2 Free => positioned ? NativeUi.ClampWindowPosition(position, new Vector2(Width, Height), S)
        : new Vector2((UiKit.W / S - Width) / 2, (UiKit.H / S - Height) / 2);
    static Rect Window => new Rect(Free.x * S, Free.y * S, Width * S, Height * S);

    public static void Close()
    {
        visible = dragging = positioned = false; state = null; error = null; scroll = 0; selected = 0;
    }

    public static void Open()
    {
        Close();
        NativeSymbolWindow.Close();
        NativeInnerAbilityWindow.Close();
        if (!GameSession.InGame) return;
        visible = true; owner = GameSession.CharacterId;
        Load(0);
    }

    static void Load(int targetPreset)
    {
        // 普通升级和命令改级都按当前等级重算，不依赖逐级发点。
        if (GameSession.Character?.record.level != Profile.level) GameSession.SaveToDb();
        var response = GameSession.Net.HyperStat(new HyperStatRequest
        { characterId = owner, preset = targetPreset, operation = HyperStatOperation.Preview });
        if (response.ok) { state = response; preset = response.preset; error = null; }
        else { error = response.error; BottomBar.Toast(response.error); }
    }

    static int LevelOf(int skillId)
    {
        if (state?.lines == null) return 0;
        foreach (var line in state.lines) if (line.skillId == skillId) return line.slv;
        return 0;
    }

    static string Operate(int skillId, HyperStatOperation operation)
    {
        var response = GameSession.Net.HyperStat(new HyperStatRequest
        { characterId = owner, preset = preset, skillId = skillId, amount = 1, operation = operation, commit = true });
        if (!response.ok) { error = response.error; return response.error; }
        state = response;
        error = null;
        GameSession.ReloadAfterCommand();
        return null;
    }

    public static void Draw()
    {
        if (!visible) return;
        if (!GameSession.InGame || owner != GameSession.CharacterId) { Close(); return; }
        if (state == null || state.level != Profile.level) Load(preset);
        var window = Window;
        GrowthUi.S = S;
        GrowthUi.R = window;
        if (GrowthUi.Drag(ref dragging, ref positioned, ref position, ref grab, Width, Height, () => Window,
                owner: typeof(NativeHyperStatWindow))) window = Window;
        GrowthUi.R = window;
        NativeUi.RegisterWindow("NativeHyperStatWindow", window, () => visible);

        GrowthUi.Image(Root, "backgrnd", 0, 0);
        GrowthUi.Image(Root, "backgrnd2", 6, 7);
        GrowthUi.Image(Root, "backgrnd3", 13, 41);
        // 标题条里烘焙的就是「極限屬性」（繁体原图），不要再叠一层简体，会双字。

        var all = HyperStatCatalog.All;
        int maxScroll = Mathf.Max(0, all.Count - VisibleRows);
        scroll = Mathf.Clamp(scroll, 0, maxScroll);
        selected = Mathf.Clamp(selected, 0, Mathf.Max(0, all.Count - 1));
        for (int slot = 0; slot < VisibleRows && scroll + slot < all.Count; slot++)
        {
            int index = scroll + slot;
            var info = all[index];
            float y = ListTop + slot * RowStep;
            // 高亮只罩「行条」这一格（16 高），不能用 RowStep 当高度——否则每行都会
            // 把下面 2px 的缝一起涂黑，行与行看着黏在一起。
            // 颜色用白色提亮而不是压黑：282 的 disabledCover(150x16) 本身就是一块灰罩，
            // 再拿暗色画选中，和「不可加点」的灰态撞在一起；白罩只在蓝条上显效果，
            // 白色数值框几乎不变，不会在 LEVEL 那一格留下灰块。
            if (index == selected)
                GrowthUi.Veil(ListLeft, y, ListWidth, BarHeight, new Color(1f, 1f, 1f, .35f));

            var label = WzAssets.Node(Root + "statList/" + info.skillId);
            var texture = label == null ? null : WzAssets.Texture(label);
            // 名字位图高度不一（10~14），按行条垂直居中，别用固定 y+2 顶对齐，
            // 否则 10 高的 STR 贴条顶、14 高的爆擊傷害顶满条，一列看下来是歪的。
            if (texture != null)
                GUI.DrawTexture(GrowthUi.At(18, y + (BarHeight - texture.height) * .5f, texture.width, texture.height), texture);
            else GrowthUi.Text(Root, info.name, 18, y, 96, "", TextAnchor.MiddleLeft, BarHeight);

            // 等级数字写在白色数值区（117..161）：底图是白底，用默认白色 Ink 会整片看不见。
            // 未加点就不画：底图那一格左边已经烘焙了「LEVEL」当占位，而长破折号「—」
            // 在 UiKit 字体里没有字形，会渲染成豆腐块。
            int level = LevelOf(info.skillId);
            if (level > 0)
                GrowthUi.Text(Root, "Lv" + level, LevelLeft, y, LevelWidth, "",
                    TextAnchor.MiddleCenter, BarHeight, LevelInk);

            GrowthUi.Image(PlusRoot, "textUp/+", 152, y + 4);
            if (GUI.enabled && GrowthUi.Repaint && Pointer.Clicked
                && GrowthUi.At(148, y, 14, BarHeight).Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick();
                // 加号热区被行热区整条包住（148..162 落在 13..163 里），点之前先同步选中，
                // 否则加完点再按 BtReduce 会减到上一次选中的那一行。
                selected = index;
                var message = Operate(info.skillId, HyperStatOperation.Apply);
                if (message != null) BottomBar.Toast(message);
                return;
            }
            if (GUI.enabled && GrowthUi.Repaint && Pointer.Clicked
                && GrowthUi.At(ListLeft, y, ListWidth, BarHeight).Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick(); selected = index;
            }
        }
        if (GUI.enabled && GrowthUi.Repaint && GrowthUi.At(ListLeft, ListTop, ListWidth, ListHeight).Contains(Pointer.Pos)
            && Mathf.Abs(Input.mouseScrollDelta.y) > .01f)
            scroll = Mathf.Clamp(scroll - (int)Mathf.Sign(Input.mouseScrollDelta.y), 0, maxScroll);

        int selectedId = all.Count > 0 ? all[Mathf.Clamp(selected, 0, all.Count - 1)].skillId : 0;
        if (GrowthUi.Button(Root, "BtReduce"))
        {
            var message = Operate(selectedId, HyperStatOperation.Remove);
            if (message != null) BottomBar.Toast(message);
        }
        if (GrowthUi.Button(Root, "BtReset"))
        {
            var message = Operate(0, HyperStatOperation.Reset);
            BottomBar.Toast(message ?? "极限属性已重置");
        }

        // 预设条：1/2/3 切「看哪一套」，BtpresetSelected 标出当前正在用的那套，
        // 套用按钮在「看的就是在用的」时置灰（服务端也会拒，两边口径一致）。
        activePreset = state?.activePreset ?? preset;
        for (int i = 0; i < HyperStatCatalog.Presets; i++)
        {
            GrowthUi.Image(Root, "BtpresetPage" + (i + 1) + "/" + (i == preset ? "checked" : "normal") + "/0",
                60 + i * 20, 267);
            if (GUI.enabled && GrowthUi.Repaint && Pointer.Clicked
                && GrowthUi.At(60 + i * 20, 267, 17, 16).Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick();
                Load(i);
            }
        }
        if (activePreset >= 0 && activePreset < HyperStatCatalog.Presets)
            GrowthUi.Image(Root, "BtpresetSelected/0", 60 + activePreset * 20, 267);
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && activePreset != preset;
        if (GrowthUi.Button(Root, "BtpresetApplication"))
        {
            var response = GameSession.Net.HyperStat(new HyperStatRequest
            { characterId = owner, preset = preset, operation = HyperStatOperation.SwitchPreset, commit = true });
            if (response.ok)
            {
                state = response; activePreset = response.activePreset;
                BottomBar.Toast("已套用预设 " + (preset + 1));
                GameSession.ReloadAfterCommand();
            }
            else BottomBar.Toast(response.error);
        }
        GUI.enabled = wasEnabled;

        // 底图把「POINT」和它的小方框烘焙在 (54..85, 290..300) / (87..116, 289..303)，
        // 这里只补剩余点数；枫币不在这一屏（282 的洗点/切预设费用是在确认框里报的）。
        int total = state?.totalSp ?? 0, spent = state?.spentSp ?? 0;
        GrowthUi.Text(Root, Mathf.Max(0, total - spent).ToString(CultureInfo.InvariantCulture),
            88, 290, 26, "", TextAnchor.UpperLeft, 12, LevelInk);
        // 失败原因偶尔需要留在屏幕上（比如预览请求被拒），压在最后一行上，不抢底图的位置。
        if (!string.IsNullOrEmpty(error))
        {
            GrowthUi.Plate(13, 236, 150, 19, .8f);
            GrowthUi.Text(Root, error, 16, 238, 144, "", TextAnchor.UpperLeft, 13, new Color(1f, .58f, .52f));
        }
    }
}

// —————————————————— 内在能力（UIWindow4.img/AbilitySetting） ——————————————————
// 素材实测：backgrnd 248x388(0,0)；AbilityGrade 230x28 在 (9,162)；
// 三行行底 BtAbility 230x18 在 (9,194)/(9,215)/(9,236)，行内文字 OptionPos (14,197|218|239)；
// BtAbilityLockEff 230x18 与行底同格（锁图标叠在行上）；button:Use 228x36 在 (10,343)；
// preset1/2/3 12x12 在 (177|197|217,263)；荣誉/费用是右对齐数值（pointPos 227,296 / costPos 227,321）。
// 循环器：282 里是背包里点道具，本项目没有道具使用入口，所以当背包里有循环器时
// 在 (14,146) 多一行「使用：名聲值 / 循環器」的切换，走 AbilityOperation.Circulator +
// 两段式确认（confirm/ 子树，確定=采用新结果，取消=保留原样）。
public static class NativeInnerAbilityWindow
{
    const string Root = "UI/UIWindow4.img/AbilitySetting/";
    const float Width = 248, Height = 388;
    static readonly float[] RowY = { 197, 218, 239 };     // vector:OptionPos1..3
    static readonly float[] BackY = { 194, 215, 236 };    // vector:OptionBackPos1..3
    static readonly string[] GradeNodes = { "rare", "epic", "unique", "legendary" };
    static readonly string[] GradeNames = { "稀有", "史诗", "唯一", "传说" };
    static bool visible, dragging, positioned;
    static Vector2 position, grab;
    static int owner, preset, activePreset;
    static bool useCirculator;
    static readonly bool[] locked = new bool[3];
    static AbilityResponse state;
    static string error;

    public static bool Visible => visible;
    public static bool AtPointer => visible && (dragging || Window.Contains(Pointer.Pos));
    static float S => Mathf.Min(BottomBar.S, Mathf.Min((UiKit.W - 24) / Width, UiKit.H * .85f / Height));
    static Vector2 Free => positioned ? NativeUi.ClampWindowPosition(position, new Vector2(Width, Height), S)
        : new Vector2((UiKit.W / S - Width) / 2, (UiKit.H / S - Height) / 2);
    static Rect Window => new Rect(Free.x * S, Free.y * S, Width * S, Height * S);

    public static void Close()
    {
        visible = dragging = positioned = false; state = null; error = null; useCirculator = false;
        for (int i = 0; i < locked.Length; i++) locked[i] = false;
    }

    public static void Open()
    {
        Close();
        NativeSymbolWindow.Close();
        NativeHyperStatWindow.Close();
        if (!GameSession.InGame) return;
        visible = true; owner = GameSession.CharacterId;
        useCirculator = FindCirculator() != 0;
        Load(0);
    }

    static void Load(int targetPreset)
    {
        var request = new AbilityRequest
        { characterId = owner, preset = targetPreset, operation = AbilityOperation.Preview };
        request.lockedRows = LockedRows();
        var response = GameSession.Net.InnerAbility(request);
        if (response.ok) { state = response; preset = response.preset; error = null; }
        else { error = response.error; BottomBar.Toast(response.error); }
    }

    static List<int> LockedRows()
    {
        var rows = new List<int>();
        for (int i = 0; i < locked.Length; i++) if (locked[i]) rows.Add(i);
        return rows;
    }

    static string Send(AbilityOperation operation)
    {
        var request = new AbilityRequest
        {
            characterId = owner, preset = preset, operation = operation,
            lockedRows = LockedRows(), commit = true,
        };
        if (operation == AbilityOperation.Circulator) request.circulatorInventoryItemId = FindCirculator();
        var response = GameSession.Net.InnerAbility(request);
        if (!response.ok) { error = response.error; return response.error; }
        error = null;
        state = response;
        GameSession.ReloadAfterCommand();
        return null;
    }

    static long FindCirculator()
    {
        var inventory = GameSession.Character?.inventory;
        if (inventory == null) return 0;
        foreach (var item in inventory)
            if (item.position > 0 && (item.itemId == 2702 || item.itemId == 2703 || item.itemId == 5062)) return item.itemRowId;
        return 0;
    }

    public static void Draw()
    {
        if (!visible) return;
        if (!GameSession.InGame || owner != GameSession.CharacterId) { Close(); return; }
        var window = Window;
        GrowthUi.S = S;
        GrowthUi.R = window;
        if (GrowthUi.Drag(ref dragging, ref positioned, ref position, ref grab, Width, Height, () => Window,
                owner: typeof(NativeInnerAbilityWindow))) window = Window;
        GrowthUi.R = window;
        NativeUi.RegisterWindow("NativeInnerAbilityWindow", window, () => visible);
        GrowthUi.Image(Root, "backgrnd", 0, 0);
        // 底图标题条烘焙的是 "ABILITY SETTING"，中文压在同一行上，先盖掉再写。
        GrowthUi.Plate(8, 5, 152, 20, .62f);
        GrowthUi.Text(Root, "内在能力", 12, 7, 146, "", TextAnchor.UpperLeft, 18);
        // 有待确认结果时整窗转模态：底下的页签/锁行/预设都不该再被点到，
        // 否则玩家会在「看清楚新结果之前」把预设切走。
        if (state?.pending == true) { DrawConfirm(); return; }

        int grade = state?.grade ?? 0;
        string gradeNode = GradeNodes[Mathf.Clamp(grade, 0, 3)];
        GrowthUi.Image(Root, "AbilityGrade/" + gradeNode + "/0", 9, 162);
        // 原版还有一层 layer:abilityGradeLock，但 282 的触发条件没逆出来；
        // 猜一个「低档位就上锁」等于骗玩家，所以不画。

        // 循环器入口：背包里有才出现，避免给玩家一个点了会报错的按钮。
        if (FindCirculator() != 0)
        {
            GrowthUi.Text(Root, (useCirculator ? "消耗：循環器（点击改用名聲值）" : "消耗：名聲值（点击改用循環器）"),
                14, 146, 226, "", TextAnchor.UpperLeft, 13, new Color(.8f, .85f, 1f));
            if (GUI.enabled && GrowthUi.Repaint && Pointer.Clicked && GrowthUi.At(14, 144, 220, 16).Contains(Pointer.Pos))
            { Pointer.ConsumeClick(); useCirculator = !useCirculator; Load(preset); }
        }

        var lines = state?.lines ?? new List<AbilityLine>();
        for (int i = 0; i < 3; i++)
        {
            float back = BackY[i], y = RowY[i];
            int rowGrade = i < lines.Count ? lines[i].grade : grade;
            GrowthUi.Image(Root, "BtAbility/" + GradeNodes[Mathf.Clamp(rowGrade, 0, 3)] + "/0", 9, back);
            // 锁按钮压在行上；点行内任意位置即切锁定（原版同格）。
            GrowthUi.Image(Root, "BtAbilityLockEff/" + (locked[i] ? "lock" : "unlock") + "/normal/0", 9, back);
            // 等级不到的行显示「未解锁」——预览里 lines 仍带着三行数据，不能照抄名字。
            int unlocked = state?.lineCount ?? AbilityCatalog.Lines;
            bool open = i < unlocked && i < lines.Count;
            string label = !open ? "未解锁"
                : string.IsNullOrEmpty(lines[i].name) ? "未洗出" : lines[i].name;
            Color ink = locked[i] ? new Color(.65f, .72f, .9f) : GrowthUi.Ink;
            GrowthUi.Text(Root, label, 14, y, 150, "", TextAnchor.UpperLeft, 16, ink);
            if (open)
                GrowthUi.Text(Root, "Lv" + lines[i].slv, 168, y, 42, "", TextAnchor.UpperRight, 16, ink);
            if (GUI.enabled && GrowthUi.Repaint && Pointer.Clicked && GrowthUi.At(9, back, 230, 18).Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick();
                locked[i] = !locked[i];
                Load(preset);
            }
        }

        // 持有/所需名聲值：底图已经烘焙了中文标签，这里只补右对齐的数值。
        GrowthUi.Text(Root, (state?.honorExp ?? 0).ToString("N0"), 127, 288, 100, "", TextAnchor.UpperRight, 16);
        GrowthUi.Text(Root, (state?.cost ?? 0).ToString("N0"), 127, 313, 100, "", TextAnchor.UpperRight, 16);

        activePreset = state?.activePreset ?? preset;
        for (int i = 0; i < AbilityCatalog.Presets; i++)
        {
            GrowthUi.Image(Root, "button:preset" + (i + 1) + "/" + (i == preset ? "checked" : "normal") + "/0",
                177 + i * 20, 263);
            if (GUI.enabled && GrowthUi.Repaint && Pointer.Clicked
                && GrowthUi.At(175 + i * 20, 261, 14, 14).Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick(); Load(i);
            }
        }
        if (activePreset >= 0 && activePreset < AbilityCatalog.Presets)
            GrowthUi.Text(Root, "使用中 " + (activePreset + 1), 14, 263, 160, "", TextAnchor.UpperLeft, 14,
                new Color(.62f, .88f, .6f));

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !(state?.pending ?? false);
        if (GrowthUi.Button(Root, "button:Use"))
        {
            var message = Send(useCirculator ? AbilityOperation.Circulator : AbilityOperation.Honor);
            if (message != null) BottomBar.Toast(message);
            else if (!useCirculator) BottomBar.Toast("内在能力已重置");
            else Load(preset);
        }
        GUI.enabled = wasEnabled;
        if (GrowthUi.Button(Root, "button:close")) { Close(); return; }
    }

    // 两段式确认（原版 confirm/ 子树）：循环器摇完先给玩家看新结果，
    // 確定 = 采用，取消 = 保留原样并清掉待确认。
    static void DrawConfirm()
    {
        // 先取候选再动 GrowthUi.R/S：中途 return 会把缩放留在确认窗上，主窗下一帧就画错。
        var candidate = state?.candidate;
        if (candidate == null) return;
        const float cw = 342, ch = 389;
        float cs = Mathf.Min(BottomBar.S, Mathf.Min((UiKit.W - 24) / cw, UiKit.H * .95f / ch));
        var rect = new Rect((UiKit.W - cw * cs) / 2, (UiKit.H - ch * cs) / 2, cw * cs, ch * cs);
        var savedRect = GrowthUi.R;
        float savedScale = GrowthUi.S;
        GrowthUi.R = rect;
        GrowthUi.S = cs;
        NativeUi.RegisterWindow("NativeAbilityConfirm", rect, () => true);
        // 底图顶部/底部已经烘焙了「變更的角色內在潛能」「確定要重設…嗎？」，
        // 所以这里只在中间那块空面板里写一句「这是哪套预设的新结果」。
        GrowthUi.Image(Root, "confirm/backgrnd", 0, 0);
        GrowthUi.Text(Root, "预设 " + (preset + 1) + " 的新结果", 36, 92, 270, "", TextAnchor.UpperCenter, 15);

        int grade = state.grade;
        foreach (var line in candidate) grade = Math.Max(grade, line.grade);
        GrowthUi.Image(Root, "confirm/AbilityGrade/" + GradeNodes[Mathf.Clamp(grade, 0, 3)] + "/0", 35, 176);
        GrowthUi.Text(Root, "新 " + GradeNames[Mathf.Clamp(grade, 0, 3)], 44, 182, 200, "", TextAnchor.UpperLeft, 16);
        float[] backY = { 208, 228, 248 }, textY = { 210, 230, 250 };
        for (int i = 0; i < 3; i++)
        {
            int rowGrade = i < candidate.Count ? candidate[i].grade : grade;
            GrowthUi.Image(Root, "confirm/Ability/" + GradeNodes[Mathf.Clamp(rowGrade, 0, 3)] + "/0", 36, backY[i]);
            string text = i < candidate.Count ? candidate[i].name + "   Lv" + candidate[i].slv : "—";
            GrowthUi.Text(Root, text, 44, textY[i], 256, "", TextAnchor.UpperRight, 16);
        }

        if (GrowthUi.Button(Root, "confirm/button:Ok"))
        {
            Choose(true);
        }
        else if (GrowthUi.Button(Root, "confirm/button:Cancel"))
        {
            Choose(false);
        }
        GrowthUi.R = savedRect;
        GrowthUi.S = savedScale;
    }

    static void Choose(bool accept)
    {
        var response = GameSession.Net.InnerAbility(new AbilityRequest
        { characterId = owner, preset = preset, operation = AbilityOperation.Choose, accept = accept, commit = true });
        if (!response.ok) { error = response.error; BottomBar.Toast(response.error); return; }
        state = response;
        error = null;
        GameSession.ReloadAfterCommand();
        BottomBar.Toast(accept ? "已采用新的内在能力" : "已保留原来的内在能力");
    }
}

// —————————————————— 徽章（UIAuthenticForce.img/AutEquip） ——————————————————
// 282 里徽章不是普通装备：模板没有 islot，源端另存 getArcaneSymbols/getSacredSymbols，
// 本项目接成负槽段 -201..（ARC）/-211..（AUT）/-221..（Grand），见 SymbolCatalog.SlotOf。
// 窗口用 AUT 素材的 6 槽布局（AutEquip/backgrndExtend 189x330 + slotPosExtend），
// 282 EXE 4A05040: UIEquip/Equip/Symbol, slotPos is the card's top-left.
public static class NativeSymbolWindow
{
    const string Root = "UI/UIEquip.img/Equip/Symbol/";
    const float Width = 228, Height = 339;
    static bool visible, dragging, positioned;
    static Vector2 position, grab;
    static int owner;
    static SymbolKind category;
    static readonly List<InventoryEntry> worn = new List<InventoryEntry>();
    public static bool Visible => visible;
    public static bool AtPointer => visible && (dragging || Pointer.HitsWindow(Window));
    static float S => Mathf.Min(BottomBar.S, Mathf.Min((UiKit.W - 24) / Width, UiKit.H * .90f / Height));
    static Vector2 Free => positioned ? NativeUi.ClampWindowPosition(position, new Vector2(Width, Height), S)
        : new Vector2((UiKit.W / S - Width) / 2, (UiKit.H / S - Height) / 2);
    static Rect Window => new Rect(Free.x * S, Free.y * S, Width * S, Height * S);
    static string Branch => Root + (category == SymbolKind.Arcane ? "ArcEquip/"
        : category == SymbolKind.Authentic ? "AutEquip/" : "GrandAutEquip/");
    static Vector2 Vector(string root, string key)
    {
        var node = WzAssets.Node(root + key);
        return node != null && node.TryVector("", out int x, out int y) ? new Vector2(x, y) : Vector2.zero;
    }
    static Vector2 Constant(string key) => Vector(Branch, "constants/vector:" + key);
    static Vector2 Size(string root, string key)
    {
        var t = WzAssets.Texture(WzAssets.Node(root + key));
        return t == null ? Vector2.zero : new Vector2(t.width, t.height);
    }
    // EXE bitmap-string renderer: alignment 1 = right, 2 = centre; respects glyph origins.
    static void Glyphs(string root, string text, float x, float y, bool centre = false)
    {
        float width = 0;
        foreach (char c in text) width += Size(root, c.ToString()).x;
        x -= centre ? (int)(width / 2) : width;
        foreach (char c in text)
        {
            var n = WzAssets.Node(root + c);
            var origin = WzAssets.Origin(n);
            GrowthUi.Image(root, c.ToString(), x - origin.x, y - origin.y);
            x += Size(root, c.ToString()).x;
        }
    }
    public static void Close() { visible = dragging = positioned = false; worn.Clear(); }
    public static void Open()
    {
        Close(); NativeHyperStatWindow.Close(); NativeInnerAbilityWindow.Close();
        if (!GameSession.InGame) return;
        owner = GameSession.CharacterId; category = SymbolKind.Arcane; visible = true; Refresh();
    }
    static void Refresh(bool normalize = true)
    {
        worn.Clear();
        var inventory = GameSession.Character?.inventory;
        bool normalized = false;
        if (inventory != null)
            foreach (var item in inventory)
                if (item.position < 0 && SymbolCatalog.IsSymbol(item.itemId))
                {
                    worn.Add(item);
                    if (normalize && item.symbolLevel == 0)
                        normalized |= GameSession.Net.Symbol(new SymbolRequest
                        { characterId = owner, inventoryItemId = item.itemRowId, operation = SymbolOperation.Preview }).ok;
                }
        if (normalized) { GameSession.ReloadAfterCommand(); Refresh(false); }
    }
    static void Operate(InventoryEntry item, SymbolOperation operation)
    {
        var preview = GameSession.Net.Symbol(new SymbolRequest
        { characterId = owner, inventoryItemId = item.itemRowId, operation = SymbolOperation.Preview });
        if (!preview.ok) { BottomBar.Toast(preview.error); return; }
        string question = operation == SymbolOperation.LevelUp
            ? "升级「" + SymbolCatalog.Of(item.itemId).name + "」需要 " + preview.mesoCost + " 枫币，是否继续？"
            : "将背包中符合条件的同款符文合并到「" + SymbolCatalog.Of(item.itemId).name + "」？";
        NativeUtilPrompt.Confirm(question, yes =>
        {
            if (!yes || !visible || owner != GameSession.CharacterId) return;
            var response = GameSession.Net.Symbol(new SymbolRequest
            { characterId = owner, inventoryItemId = item.itemRowId, operation = operation, commit = true });
            if (!response.ok) { BottomBar.Toast(response.error); return; }
            GameSession.ReloadAfterCommand(); Refresh();
        });
    }
    static int Stat(InventoryEntry item, string name)
    {
        switch (name)
        {
            case "str": return item.str; case "dex": return item.dex;
            case "int": return item.int_; case "luk": return item.luk; default: return item.hp;
        }
    }
    static void Summary()
    {
        int job = Profile.JobId;
        bool xenon = NativeSymbolService.IsXenon(job);
        var name = Constant(xenon ? "statNamePosXenon" : "statNamePos");
        var number = Constant(xenon ? "statNumberPosXenon" : "statNumberPos");
        int force = 0;
        foreach (var item in worn) if (SymbolCatalog.Of(item.itemId)?.kind == category) force += item.symbolInc;
        GrowthUi.Image(Branch, "textUp/force", name.x, name.y);
        Glyphs(Branch + "textUp/", "+" + force, number.x, number.y);
        if (category == SymbolKind.Grand)
        {
            int grandGap = WzAssets.Node(Branch + "constants")?.Int("lineGap", 20) ?? 20;
            string[] rates = { "ExpRate", "MesoRate", "DropRate" };
            string[] labels = { "exp", "meso", "drop" };
            for (int i = 0; i < rates.Length; i++)
            {
                name.y += grandGap; number.y += grandGap;
                int sum = 0;
                foreach (var item in worn)
                    if (item.position == SymbolCatalog.SlotOf(item.itemId))
                        sum += SymbolCatalog.GrandBonus(item.itemId, item.symbolLevel, rates[i]);
                GrowthUi.Image(Branch, "textUp/" + labels[i], name.x, name.y);
                Glyphs(Branch + "textUp/", "+" + sum + "%", number.x, number.y);
            }
            return;
        }
        string[] stats = xenon ? new[] { "str", "dex", "luk" }
            : new[] { NativeSymbolService.IsDemonAvenger(job) ? "hp" : NativeSymbolService.MainStatColumn(job) };
        int gap = WzAssets.Node(Branch + "constants")?.Int(xenon ? "lineGapXenon" : "lineGap", 20) ?? 20;
        foreach (string stat in stats)
        {
            name.y += gap; number.y += gap;
            int sum = 0;
            foreach (var item in worn) if (SymbolCatalog.Of(item.itemId)?.kind == category) sum += Stat(item, stat);
            GrowthUi.Image(Branch, "textUp/" + stat, name.x, name.y);
            Glyphs(Branch + "textUp/", "+" + sum, number.x, number.y);
        }
    }
    public static void Draw()
    {
        if (!visible) return;
        if (!GameSession.InGame || owner != GameSession.CharacterId) { Close(); return; }
        Refresh(false); // 背包卸下/重戴后同步卡片，不保留旧库存引用。
        bool ownsPointer = AtPointer;
        GrowthUi.S = S; GrowthUi.R = Window;
        GrowthUi.Drag(ref dragging, ref positioned, ref position, ref grab, Width, Height, () => Window,
            handle: 12f, rightInset: 0f, owner: typeof(NativeSymbolWindow));
        GrowthUi.R = Window;
        NativeUi.RegisterWindow("NativeSymbolWindow", Window, () => visible);
        GrowthUi.Image(Root, "backgrnd", 0, 0);
        var tab = Vector(Root, "tab:categoryTab/lt");
        for (int i = 0; i < 2; i++)
        {
            string key = "tab:categoryTab/" + ((i == 0) == (category == SymbolKind.Arcane) ? "selected/" : "normal/") + i;
            var size = Size(Root, key);
            GrowthUi.Image(Root, key, tab.x + i * size.x, tab.y);
            if (GUI.enabled && GrowthUi.Repaint && Pointer.Clicked && GrowthUi.At(tab.x + i * size.x, tab.y, size.x, 27).Contains(Pointer.Pos))
            { Pointer.ConsumeClick(); category = i == 0 ? SymbolKind.Arcane : SymbolKind.Authentic; }
        }
        var child = Vector(Root, "vector:childLT");
        var saved = GrowthUi.Shift(child.x, child.y, 184, 272);
        GrowthUi.Image(Branch, "backgrnd", 0, 0);
        Summary();
        if (category != SymbolKind.Arcane)
        {
            var next = Vector(Branch, "nextPos"); var size = Size(Branch, "button:next/normal/0");
            if (GrowthUi.ButtonAt(Branch, "button:next", GrowthUi.At(next.x, next.y, size.x, size.y)))
                category = category == SymbolKind.Authentic ? SymbolKind.Grand : SymbolKind.Authentic;
        }
        int count = category == SymbolKind.Grand ? 3 : 6;
        for (int i = 0; i < count; i++)
        {
            int id = category == SymbolKind.Arcane ? 1712001 + i : category == SymbolKind.Authentic ? 1713000 + i : 1714000 + i;
            var item = worn.Find(e => e.itemId == id);
            var slot = Vector(Branch, "slotPos/" + i);
            var card = Size(Branch, "box");
            GrowthUi.Image(Branch, "box", slot.x, slot.y);
            if (item == null) continue;
            var icon = NativeItemSource.Icon(item.itemId);
            var offset = Constant("slotIconPos");
            // EXE 4A062AC: x=(cardWidth-32)/2+slotIconPos.x, bottom=39+slotIconPos.y.
            if (icon != null) GUI.DrawTexture(GrowthUi.At(slot.x + (int)((card.x - 32) / 2) + offset.x,
                slot.y + 7 + offset.y, 32, 32), icon, ScaleMode.ScaleToFit);
            var levelPos = Constant("slotLevelPos");
            Glyphs(Branch + "textDown/", "v" + Math.Max(1, item.symbolLevel), slot.x + levelPos.x, slot.y + levelPos.y, true);
            var info = SymbolCatalog.Of(id);
            if (item.symbolLevel >= info.maxLevel)
            {
                var max = Constant("slotMaxPos");
                var n = WzAssets.Node(Branch + "base2"); var origin = WzAssets.Origin(n);
                GrowthUi.Image(Branch, "base2", slot.x + max.x - origin.x, slot.y + max.y - origin.y);
            }
            else if (info.CanLevelUp(item.symbolLevel, item.symbolExp))
            {
                var button = Constant("slotButtonPos"); var size = Size(Branch, "button:enchant/normal/0");
                if (GrowthUi.ButtonAt(Branch, "button:enchant", GrowthUi.At(slot.x + button.x, slot.y + button.y, size.x, size.y)))
                    Operate(item, SymbolOperation.LevelUp);
            }
            else
            {
                var gauge = Constant("slotGaugePos"); var size = Size(Branch, "canvas:gauge");
                int pixels = Math.Max(1, (int)(size.x * item.symbolExp / (float)info.GrowthCost(Math.Max(1, item.symbolLevel))));
                var texture = WzAssets.Texture(WzAssets.Node(Branch + "canvas:gauge"));
                if (texture != null) GUI.DrawTextureWithTexCoords(GrowthUi.At(slot.x + gauge.x, slot.y + gauge.y, pixels, size.y),
                    texture, new Rect(0, 0, pixels / size.x, 1));
            }
            var rect = GrowthUi.At(slot.x, slot.y, card.x, card.y);
            if (GUI.enabled && GrowthUi.Repaint && Pointer.DragStarted && rect.Contains(Pointer.PressPosition))
                NativeInventoryActions.PickEquipped(item.itemId, item.position);
            if (GrowthUi.Repaint && rect.Contains(Pointer.Pos))
            {
                ItemTooltip.SetHover(item.itemId, false, item);
                if (GUI.enabled && Pointer.RightPressed)
                {
                    Pointer.RightPressed = false;
                    string error = EquipLoadout.UnequipToInventory(item.position);
                    if (error != null) BottomBar.Toast(error);
                    else Refresh();
                    break;
                }
                if (GUI.enabled && Pointer.Clicked)
                { Pointer.ConsumeClick(); Operate(item, SymbolOperation.MergeAll); }
            }
        }
        GrowthUi.Restore(saved);
        if (ownsPointer && GrowthUi.Repaint && Pointer.Clicked) Pointer.ConsumeClick();
    }
}

// V/HEXA presentation shares the verified native tooltip frame and skill tooltip renderer.
internal static class MatrixPresentation
{
    public static bool Active { get; private set; }
    static string text;
    static SkillData skill;
    static int level;
    public static void Begin() { Active = true; text = null; skill = null; }
    public static void Hover(string value) { if (!string.IsNullOrEmpty(value)) text = value; }
    public static void Details(string name, string desc, int current)
    { skill = null; text = name + "  Lv" + current + "\n" + desc; }
    public static void Skill(int[] skills, int current, string name, string desc)
    { Details(name, desc, current); if (skills != null && skills.Length > 0) { skill = SkillBook.Get(skills[0]); level = current; } }
    public static void Draw()
    {
        Active = false;
        if (!GrowthUi.Repaint || NativeUtilPrompt.Visible) return;
        if (skill != null) { SkillWindow.DrawSkillData(skill, Pointer.Pos, level); return; }
        if (string.IsNullOrEmpty(text)) return;
        float s = GrowthUi.S, w = 300 * s;
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.Max(9, Mathf.RoundToInt(12*s)), wordWrap = true };
        style.normal.textColor = Color.white;
        text = text.Replace("\\n", "\n");
        float h = style.CalcHeight(new GUIContent(text), w - 20*s) + 20*s;
        var rect = new Rect(Mathf.Clamp(Pointer.Pos.x + 16*s, 4, Mathf.Max(4, UiKit.W-w-4)),
            Mathf.Clamp(Pointer.Pos.y + 14*s, 4, Mathf.Max(4, UiKit.H-h-4)), w, h);
        ItemTooltip.DrawFrame(rect,s);
        GUI.Label(new Rect(rect.x+10*s,rect.y+10*s,w-20*s,h-20*s),text,style);
    }
    public static void Regions(string root)
    {
        var node = WzAssets.Node(root);
        if (node == null) return;
        foreach (var child in node.Children)
        {
            if (!child.Name.StartsWith("tooltip:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!child.TryVector("lt", out int x, out int y) || !child.TryVector("rb", out int r, out int b)) continue;
            if (GrowthUi.At(x,y,r-x,b-y).Contains(Pointer.Pos)) Hover(child.Text("text"));
        }
    }
    public static void Animation(string path, float x, float y)
    {
        // IMG frame delays are milliseconds; each canvas keeps its own origin.
        int total = 0, count = 0;
        for (; count < 128; count++)
        {
            var frame = WzAssets.Node(path + count);
            if (frame == null) break;
            total += Mathf.Max(1, frame.Int("delay", 100));
        }
        if (total == 0) return;
        int time = (int)(Time.realtimeSinceStartup * 1000) % total;
        for (int i = 0; i < count; i++)
        {
            var frame = WzAssets.Node(path + i);
            time -= Mathf.Max(1, frame.Int("delay", 100));
            if (time >= 0) continue;
            var origin = WzAssets.Origin(frame);
            GrowthUi.Image(path, i.ToString(CultureInfo.InvariantCulture), x - origin.x, y - origin.y);
            break;
        }
    }
    public static void Help(bool hexa)
    {
        NativeUtilPrompt.Pages(hexa ? "HEXA 矩阵帮助" : "V 矩阵帮助", hexa ? new[] {
            "启用 HEXA 核心可获得六转技能与能力值。强化核心可提升效果。技能页分为技能、精通、强化、共用四类核心。",
            "选中核心可查看等级与所需材料。激活需满足前置技能条件；气息与碎片由服务端检查，成功后同步技能。强化窗可逐级选择或选择当前材料能负担的等级。",
            "HEXA 技能不受冷却时间初始化、增益持续时间增加、召唤兽持续时间增加效果影响。除部分技能外，会套用被动超技能与五转强化核心效果。",
            "能力值页先激活核心，再选择三项不同能力。攻击力与魔法攻击力不能同时选择。强化随机提升一条能力，总等级上限20，每条上限10。",
            "保存槽会保存完整能力类型与等级。可切换套用强化槽或保存槽；保存与套用是不同操作。变更能力每次100万金币，保留各条等级。",
            "正常初始化要求总等级至少10，费用1000万金币。无法还原三条分配的旧记录可免费初始化。初始化后重新选择与强化。主属性效果按职业换算。",
            "资源区可查看索尔艾尔达气息与碎片；转换入口按原材料种类兑换，费用与数量以窗口显示和服务端校验为准。"
        } : new[] {
            "本项目沿用源端 V 点矩阵：技能核心4格、强化核心6格、共用核心19格。点击核心打开强化窗口，分配 V 点后才会提升对应技能。",
            "强化窗显示当前等级、目标等级和所需 V 点。可逐级增加、选择当前点数能负担的等级，或撤销本次选择。确认成功后显示等级变化。",
            "V 点转换会消耗 V 点兑换核心宝石。背包使用核心宝石可获得 V 点，部分宝石首次使用会直接开启指定核心。数量和收益均由服务端核实，V 点上限9999。",
            "特殊核心需先登记，再点击启用或停用。同一时间只能启用一个特殊核心；其触发条件、效果与冷却以核心悬浮说明为准。",
            "重置入口会先提示金币费用并要求确认，再返还已分配 V 点。五转技能不受冷却时间初始化、增益持续时间增加、召唤兽持续时间增加效果影响。"
        });
    }
}
