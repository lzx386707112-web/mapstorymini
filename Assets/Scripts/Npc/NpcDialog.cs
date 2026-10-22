using System.Collections.Generic;

public enum NpcDialogMode
{
    None,
    Next,          // 单按钮「继续」  action(1, 1, -1)
    Prev,          // 单按钮「返回」  action(0, 1, -1)
    NextPrev,      // 上一页/下一页
    Ok,            // 确定            action(-1, 0, -1)
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

    public bool Visible => Mode != NpcDialogMode.None;

    public void Set(NpcDialogMode mode, string text)
    {
        Mode = mode;
        RawText = text ?? "";
        Content = DialogText.Parse(RawText);
        ScrollReset = true;
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
