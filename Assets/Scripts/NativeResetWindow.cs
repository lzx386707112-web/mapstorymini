using UnityEngine;

// 原端「能力值洗点」窗：cashType23 → 0xA0C427（USE_CASH 分派，跳转表 0xA0EAD4[11]）。
// 三页流程：选恢复来源（0xA0C427 首个 0xBC 对象）→ 选再分配目标（第二个 0xBC 对象）→ 确认（0x9C 对象）。
// 资源（字符串池 id 2928..2933）：
//   Reset/AP/backgrnd   175x220  "请你选择想恢复的能力值。"
//   Reset/AP/backgrnd2  175x220  "你要在哪儿再分配能力值点?"
//   Reset/AP/backgrnd3  175x141  "确认这样恢复吗?"
//   Reset/AP/stat%d/%d  159x16   页1/2 用 stat1（0x8CC915 push 1），确认页用 stat0（0x8CD937 push 0）
// 控件（0x8CB9CF..0x8CBC80 与 0x8CD875 的 push 常量对，(x,y)）：
//   BtUP/BtDown 12x12 每行一个：x=153，y=74/92/116/134/152/170（行序 HP,MP,力量,敏捷,智力,运气）
//   页1 用 BtDown（[this+0xB4]==0 分支），页2 用 BtUP；关闭 BtClose 于 (156,6)
//   确认页 BtOK2 (70,117) id=1、BtCancel2 (119,117) id=2、BtClose (156,6) id=2
// 行图 159x16 以 (175-159)/2=8 居中，贴底图自带色块的顶边 71/89/113/131/149/167（16 高）。
// 数值文字 x 未从汇编解出（0x8CC8DE 里只算出文本宽后调 0x414576 落位），暂按行内右侧空白右对齐到 x=150，
// 避免压住 x=153..165 的箭头；这一处属推断，以实拍视觉核对为准。
// 确认页正文区实测 y=66..113（高 47），16 高的行图居中落在 y=81（同样属推断）。
// 项目边界：本工程 HP/MP 的 AP 投入常驻禁用（GameWindows.StatWindow），hpMpUsed 恒为 0，
// 因此 HP/MP 行按源端 AssignAPProcessor.APResetAction 的 hpMpUsed<1 分支拒绝，不伪造洗血。
public static class NativeResetWindow
{
    const string Root = "UI/UIWindow.img/Reset/AP/";
    // 箭头按钮 y（0x8CB9CF..0x8CBC80 的常量对）；行图比按钮高 3px：
    // 底图 Reset/AP/backgrnd 里六个色块实测顶边 71/89/113/131/149/167（16 高），行图必须贴这个 y，
    // 否则和底图自带的标签叠成两层。
    static readonly int[] RowY = { 71, 89, 113, 131, 149, 167 };
    static readonly int[] ArrowY = { 74, 92, 116, 134, 152, 170 };
    // AssignAPProcessor.APResetAction 的 APFrom/APTo 编码
    static readonly int[] StatBit = { 2048, 8192, 64, 128, 256, 512 };
    static int itemId, itemPosition, stage, from, to;
    static Vector2 pos, grab;
    static bool dragging, centered;
    public static bool Visible => stage != 0;
    static float S => BottomBar.S;
    static Vector2 Size => new Vector2(175, stage == 3 ? 141 : 220);
    static Rect Window => new Rect(pos.x * S, pos.y * S, Size.x * S, Size.y * S);
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static void Image(Rect r, string node, float x, float y)
    {
        var tex = Art.Tex("wz://" + Root + node);
        if (tex != null) GUI.DrawTexture(At(r, x, y, tex.width, tex.height), tex);
    }
    static void Value(Rect r, string text, float y)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(11 * S), alignment = TextAnchor.MiddleRight };
        style.normal.textColor = Color.black;
        // 右端到 x=150：153..165 是箭头按钮的位置，数值不能压上去。
        GUI.Label(At(r, 62, y, 88, 16), text, style);
    }
    public static void Open(int id, int inventoryPosition = 0)
    {
        if (id != 5050000) return;
        itemId = id; itemPosition = inventoryPosition; stage = 1; from = 0; to = 0; dragging = false; centered = false;
        pos = new Vector2(300, 160);
        Center();
    }
    public static void Close() { itemId = 0; stage = 0; from = 0; to = 0; dragging = false; centered = false; }
    // 共享创建例程 0x4EDAEF 把窗口按 (800-w)/2,(600-h)/2 居中；S 还没量准时不落位，等绘制帧再算。
    static void Center()
    {
        if (S < 0.3f) return;
        pos = NativeUi.ClampWindowPosition(new Vector2((UiKit.W / S - Size.x) / 2, (UiKit.H / S - Size.y) / 2), Size, S);
        centered = true;
    }
    static string RowValue(int index)
    {
        var a = Profile.Attributes;
        switch (index)
        {
            case 0: return Mathf.CeilToInt(GameLoop.I != null && GameLoop.I.player != null ? GameLoop.I.player.hpMax : a.maxHp).ToString();
            case 1: return Mathf.CeilToInt(GameLoop.I != null && GameLoop.I.player != null ? GameLoop.I.player.mpMax : a.maxMp).ToString();
            case 2: return a.str.ToString();
            case 3: return a.dex.ToString();
            case 4: return a.intelligence.ToString();
            default: return a.luk.ToString();
        }
    }
    public static void Draw()
    {
        if (!Visible) return;
        if (!centered) Center();
        pos = NativeUi.ClampWindowPosition(pos, Size, S);
        var r = Window;
        var pointer = Pointer.Pos / S;
        if (Event.current.type == EventType.Repaint && Pointer.Pressed && At(r, 0, 0, 150, 22).Contains(Pointer.Pos) && stage != 3)
        { dragging = true; centered = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down)
        {
            pos = NativeUi.ClampWindowPosition(Pointer.Pos / S - grab, Size, S);
            r = Window;
        }
        if (!Pointer.Down) dragging = false;
        if (stage == 3) { DrawConfirm(r, pointer); return; }
        Image(r, stage == 1 ? "backgrnd" : "backgrnd2", 0, 0);
        if (NativeUi.Button(At(r, 156, 6, 12, 12), "UI/Basic.img/BtClose", pointer)) { Close(); return; }
        // BtUP/BtDown 在 Reset/Common 下，不在 Reset/AP 下（字符串池 2926/2927）。
        const string arrows = "UI/UIWindow.img/Reset/Common/";
        string arrow = stage == 1 ? "BtDown" : "BtUP";
        for (int i = 0; i < RowY.Length; i++)
        {
            int stat = StatBit[i];
            // stat1/<i> 不是标签行，是「不可选」的灰化覆盖层：底图自带的六行本来就是彩色的。
            // HP/MP 两行在本工程恒不可选（StatWindow 的 HP/MP 加点箭头常驻禁用，hpMpUsed 恒 0，
            // 源端 AssignAPProcessor.APResetAction 会以「你没有足够的HP、MP属性点用于AP重置。」拒绝）；
            // 第 2 页的来源行也不可再选（0xA0C427 第二个对象带着 +0xB4 进来）。
            bool disabled = i < 2 || (stage == 2 && stat == from);
            Value(r, RowValue(i), RowY[i]);
            if (disabled)
            {
                Image(r, "stat1/" + i, 8, RowY[i]);
                var grey = Art.Tex("wz://" + arrows + arrow + "/disabled/0");
                if (grey != null) GUI.DrawTexture(At(r, 153, ArrowY[i], 12, 12), grey);
                continue;
            }
            if (NativeUi.Button(At(r, 153, ArrowY[i], 12, 12), arrows + arrow, pointer))
            {
                if (stage == 1) { from = stat; stage = 2; }
                else { to = stat; stage = 3; }
                return;
            }
        }
    }
    static void DrawConfirm(Rect r, Vector2 pointer)
    {
        Image(r, "backgrnd3", 0, 0);
        int index = System.Array.IndexOf(StatBit, to);
        if (index >= 0) { Image(r, "stat0/" + index, 8, 81); Value(r, RowValue(index), 81); }
        if (NativeUi.Button(At(r, 70, 117, 47, 19), "UI/Basic.img/BtOK2", pointer)) Confirm();
        if (NativeUi.Button(At(r, 119, 117, 47, 19), "UI/Basic.img/BtCancel2", pointer)) { Close(); return; }
        if (NativeUi.Button(At(r, 156, 6, 12, 12), "UI/Basic.img/BtClose", pointer)) Close();
    }
    // 0xA0C427 尾部只在三页全部确认后才写请求，APTo 先写、APFrom 后写（UseCashItemHandler175..235 的读取顺序）。
    static void Confirm()
    {
        int source = from, target = to, position = itemPosition, id = itemId;
        Close();
        string error = Profile.ResetAp(source, target);
        if (error != null) { BottomBar.Toast(error); return; }
        Profile.RemoveItem(id, 1, position);
    }
}
