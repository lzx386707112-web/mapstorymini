using System.Collections.Generic;

public enum NpcDialogMode
{
    None,
    Next,          // 单按钮「继续」  action(1, 0, -1)
    Prev,          // 上一页 + 确定；返回 action(0,0,-1)，确定 action(1,0,-1)
    NextPrev,      // 上一页/下一页
    Ok,            // 确定            action(1, 0, -1)
    YesNo,
    AcceptDecline,
    Simple,        // #L 列表
    GetText,
    GetNumber,
    Style,         // 原 NPC_TALK type7，原样返回 styles 的下标
}

public sealed class NpcDialog
{
    public NpcDialogMode Mode = NpcDialogMode.None;
    public string RawText = "";
    public DialogContent Content = new DialogContent();
    public int NumberDef, NumberMin, NumberMax;
    public string Input = "";
    public int[] Styles = System.Array.Empty<int>();
    public int StyleIndex;
    public bool StyleClothed = true;
    public bool ScrollReset;
    public bool IsMenu;

    // 原端 CUtilDlgEx +0xF4：「当前选项」的链接号（不是下标）。绘制时只有这一项的
    // 行首标记换成 dot1（红），其余仍是 dot0（浅灰）；0x9AC104/0x9AC347 的上下键
    // 只改这个值（夹取、不循环），0x9ABE86 的鼠标命中也会写它。
    // -1 = 没有任何选中项（0x9AC4F6/0x9AC566 在换页/滚动时复位成 -1）。
    // 移动端差异（用户 2026-10-29 要求）：Android 没有鼠标悬停，按 -1 开局等于画面上看不出
    // 当前选的是哪一项，所以菜单一打开就默认落到第一个可选项。改动只影响初值，
    // 上下键改写（0x9AC104/0x9AC347）和鼠标/触摸改写（0x9ABE86）行为照旧。
    public int SelectedLink = -1;

    // 默认选中：按显示顺序的第一个可选项（分组标题 HeaderArt 不算选项）。
    // 必须在 Content 组装完成后调用——NpcField 的程序化菜单是在 Set() 之后才追加 Selections 的。
    public void SelectFirst()
    {
        var links = DialogFlow.Links(Content);
        SelectedLink = links.Count > 0 ? links[0] : -1;
    }

    // Native 9A7DC3: no next-page flag still creates footer OK (9A7EDD).
    public bool HasPagePrevious => Mode == NpcDialogMode.Prev || Mode == NpcDialogMode.NextPrev;
    public bool HasPageNext => Mode == NpcDialogMode.Next || Mode == NpcDialogMode.NextPrev;
    public bool HasFooterOk => Mode == NpcDialogMode.Prev || Mode == NpcDialogMode.Ok;
    public bool CanConfirmWithTalkKey => HasPageNext || HasFooterOk;

    public bool Visible => Mode != NpcDialogMode.None;

    // NPCMoreTalkHandler forwards the last server message type verbatim.
    public int MessageType => Mode == NpcDialogMode.YesNo ? 1 :
        Mode == NpcDialogMode.GetText ? 2 : Mode == NpcDialogMode.GetNumber ? 3 :
        Mode == NpcDialogMode.Simple ? 4 : Mode == NpcDialogMode.Style ? 7 : Mode == NpcDialogMode.AcceptDecline ? 12 : 0;

    public void Set(NpcDialogMode mode, string text)
    {
        Styles = System.Array.Empty<int>(); StyleIndex = 0; StyleClothed = true;
        Mode = mode;
        RawText = text ?? "";
        Content = DialogText.Parse(RawText);
        ScrollReset = true;
        IsMenu = false;
        SelectedLink = -1;
        // 选项菜单打开就选中第一项（原端是 -1，靠鼠标悬停点亮；移动端无悬停，必须给个可见焦点）。
        if (mode == NpcDialogMode.Simple) SelectFirst();
    }

    public void Hide()
    {
        Mode = NpcDialogMode.None;
        RawText = "";
        Content = new DialogContent();
        Input = "";
        SelectedLink = -1;
    }

    public IReadOnlyList<DialogSelection> Selections => Content.Selections;
}
