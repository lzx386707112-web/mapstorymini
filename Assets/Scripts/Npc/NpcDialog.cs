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
}

public sealed class NpcDialog
{
    public NpcDialogMode Mode = NpcDialogMode.None;
    public string RawText = "";
    public DialogContent Content = new DialogContent();
    public int NumberDef, NumberMin, NumberMax;
    public string Input = "";
    public bool ScrollReset;
    public bool IsMenu;

    // Native 9A7DC3: no next-page flag still creates footer OK (9A7EDD).
    public bool HasPagePrevious => Mode == NpcDialogMode.Prev || Mode == NpcDialogMode.NextPrev;
    public bool HasPageNext => Mode == NpcDialogMode.Next || Mode == NpcDialogMode.NextPrev;
    public bool HasFooterOk => Mode == NpcDialogMode.Prev || Mode == NpcDialogMode.Ok;
    public bool CanConfirmWithTalkKey => HasPageNext || HasFooterOk;

    public bool Visible => Mode != NpcDialogMode.None;

    // NPCMoreTalkHandler forwards the last server message type verbatim.
    public int MessageType => Mode == NpcDialogMode.YesNo ? 1 :
        Mode == NpcDialogMode.GetText ? 2 : Mode == NpcDialogMode.GetNumber ? 3 :
        Mode == NpcDialogMode.Simple ? 4 : Mode == NpcDialogMode.AcceptDecline ? 12 : 0;

    public void Set(NpcDialogMode mode, string text)
    {
        Mode = mode;
        RawText = text ?? "";
        Content = DialogText.Parse(RawText);
        ScrollReset = true;
        IsMenu = false;
    }

    public void Hide()
    {
        Mode = NpcDialogMode.None;
        RawText = "";
        Content = new DialogContent();
        Input = "";
    }

    public IReadOnlyList<DialogSelection> Selections => Content.Selections;
}
