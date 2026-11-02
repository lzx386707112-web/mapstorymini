using UnityEngine;
using System.Collections.Generic;

// 原端「黑板 / ad board」现金道具 5370000/5370001（cashType 32 → 0xA0CE2E）。
// EXE 身份 SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2，ImageBase 0x400000。
//
// 输入（0xA0CE2E）：CUtilDlgEx type3（复用已核实布局的 NativeUtilPrompt.Text），
//   串 0x56F "Enter a note."，0x9AC7CB(this,0,0x28,0,0) → 最大 40 字、最小 0。
//   提交后过 0x7A06F3 脏话过滤（与改名卡同一过滤器，本项目尚未迁移，见文档待核实）；
//   过滤后为空 → 串 0x110 "Cursing is not allowed."。
//   地图限制：0x437A0C 上下文 + 0xA0ECA4 读 [ctx+0x160]，非 0 → 串 0x114
//   "You can't use it here in this map."，直接返回不弹输入框。服务端同规则：
//   UseCashItemHandler itemType==537 → GameConstants.isFreeMarketRoom(mapId)
//   （map/1000000==910 && map>910000000，MapId.FM_ENTRANCE=910000000）拒绝。
//
// 服务端语义（源端为准，不得编造）：
//   UseCashItemHandler 492..500：setChalkboard(text) + broadcast useChalkboard(chr,false) +
//   enableActions；remove() 被注释——**使用不消耗道具，重复发送也不重复消耗**。
//   CloseChalkboardHandler（Recv CLOSE_CHALKBOARD）：setChalkboard(null) + broadcast(close)。
//   MapleMap 换图：持有黑板进 FM 房间 → setChalkboard(null)；否则重新广播（黑板跨图保留）。
//   InventoryManipulator 932：黑板道具数量归零时清空黑板。
//   本项目的内嵌服务端已有同一套规则（Server/World/NativeCommunityService.cs 的
//   CommunityOperation.SetBoard/CloseBoard），本类只通过该通道提交，不改服务端文件。
//
// 显示（0x48A935 type1003 → 0x4890C4 布局）：资源 UI/ChatBalloon.img/adboard/<style>，
//   style 取自物品使用的子索引（0xA611BF 按 10 进制转字符串），只有 0 一套。
//   九宫格 canvas（imgdump 实测；0x4890C4 的度量函数 0x40B920=宽、0x40B947=高、
//   0x40F09B=origin.x、0x40F0C2=origin.y，均经九宫格拼合约束反解验证）：
//     nw 18x19 origin(18,19)   ne 18x19 origin(0,19)   sw 18x19 origin(18,0)
//     se 18x19 origin(0,0)     n  14x19 origin(0,19)   s  14x19 origin(0,0)
//     w  18x14 origin(18,0)    e  18x14 origin(0,0)    c  14x14 origin(0,0)
//     arrow 14x23 origin(0,0)  clr=-1（文字白色；普通气泡 clr=-16777216 黑字）
//   布局公式（0x489D44..0x489D90，type==1003 分支，结果存 this+0x5c/0x60/0x2c/0x30）：
//     leftW   = max(origin.x(nw), origin.x(sw), origin.x(w)) = 18
//     rightW  = max(width(se)-origin.x(se), width(arrow)-origin.x(ne), width(e)-origin.x(e)) = 18
//     cell    = width(n) = 14（列宽）；rowH = height(w) = 14（行高）
//     topH    = max(origin.y(ne), origin.y(nw), origin.y(n)) = 19
//     bottomM = max(18, height(arrow)-origin.y(arrow)=23, height(s)-origin.y(s)=19, …) = 23
//               （arrow 比 19px 下边框多出 4px 尾尖，sprite 必须多留 4px）
//     maxCols = 14（0x489937..0x489952：type 1005 → 5，黑板 1003 → 14）
//   换行（0x489AC8 循环剥行）：0x48ED5A 量出 14 格（196px）内放得下的字符数，
//     从该处向前找空格/换行断行（0x489B66 向后扫 0xAF2464 字符类），找不到则硬断（0x489B9C）。
//   单行时（0x489CC2 rows==1 分支）：cols = ceil(文本宽/14) 再 `or 1` 强制奇数
//     （0x489D08..0x489D34），innerW = cols*14；多行时 cols = 14，innerW = 196。
//   总宽 = innerW + leftW + rightW；总高 = topH + rowH*行数 + bottomM。
//   锚点（0x489D6C..0x489D90）：x = 3 - innerW/2 - leftW（以角色为中心右移 3px），
//     y = -(rowH*行数 + bottomM)。原端锚点与角色头顶的精确对应待核实；
//     本项目沿用 BotField 气泡已验证的约定：尾尖贴角色头顶。
//   绘制顺序：九宫格按 origin 合成一张整图（角四块 → 四边 → 中央平铺），
//     arrow 最后画在底边中央列（cols 强制奇数使 cols/2 恰为正中列，0x489EC9..0x489ED4 分支）；
//     arrow 的上 19 行与 s 完全相同（像素对比核实），整体压在 s 之上，尾尖超出下边框 4px。
//   文字：白色（clr=-1），Canvas#Font（本项目用 UiKit.Font 12px），画在 inner 区
//     (leftW, topH, innerW, 行数*14) 内，逐行左对齐（水平对齐方式待核实，格宽余量 <14px）。
public static class NativeAdBoard
{
    const string Root = "UI/ChatBalloon.img/adboard/0/";
    const int MaxChars = 40;          // 0xA0CE2E：0x9AC7CB 的 0x28
    const int MaxCols = 14;           // 0x489937：0xE
    const int Cell = 14;              // width(n)
    const int RowH = 14;              // height(w)
    const int LeftW = 18, RightW = 18, TopH = 19, BottomM = 23;

    // 串 0x56F "Enter a note."。BeiDou 的 zh 串表未随 EXE 字符串池导出，此处用意译，
    // 原文以注释保留。
    const string Prompt = "请输入留言内容。";            // "Enter a note."
    const string BlockedText = "您无法在这里使用黑板。";  // 串 0x114，与源端 i18n message6 同义

    static string boardText;      // 服务端确认后的显示文本（对应 chr.chalktext）
    static GUIStyle textStyle;

    public static string Text => boardText ?? "";
    public static void Clear()
    {
        boardText = null;
        if (!GameSession.InGame) return;
        NativeCommunityWindow.Request(CommunityOperation.CloseBoard);  // 失败时 Request 内部已 Toast
    }

    // 0xA0CE2E 入口：cashType 32。物品不在此消耗（服务端 remove() 被注释）。
    public static void Open(int itemId)
    {
        if (itemId / 10000 != 537) return;
        // 0xA0ECA4 地图检查：FM 房间直接提示并返回，不弹输入框（服务端同一规则兜底）。
        int mapId = GameLoop.I != null && GameLoop.I.field != null && GameLoop.I.field.Map != null
            ? GameLoop.I.field.Map.id : 0;
        if (mapId / 1000000 == 910 && mapId > 910000000) { BottomBar.Toast(BlockedText); return; }
        NativeUtilPrompt.Text(Prompt, boardText ?? "", 0, MaxChars, Submit);
    }

    // 确认后等价于原端发 USE_CASH_ITEM 携带文本：服务端 setChalkboard + 广播、不扣道具。
    // 本项目经 NativeCommunityService.SetBoard（同一语义：FM 拒绝、须持有 537、不消耗）。
    static void Submit(string value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return;   // 原端空串经 0x7A06F3 后报 0x110；此处直接不入库
        NativeCommunityWindow.InputText = text;
        // 响应经 Handle→Read 全量回带，SetBoard 成功后 response.board 即新文本；
        // 失败时 Request 内部已 Toast，此处不再重复。
        var response = NativeCommunityWindow.Request(CommunityOperation.SetBoard);
        if (response.ok) boardText = response.board;
    }

    // ———————————————— 地图内显示 ————————————————

    static Texture2D artNw, artNe, artSw, artSe, artN, artS, artW, artE, artC, artArrow;
    static bool artTried;

    static void EnsureArt()
    {
        if (artTried) return;
        artTried = true;
        artNw = Tex(Root + "nw"); artNe = Tex(Root + "ne");
        artSw = Tex(Root + "sw"); artSe = Tex(Root + "se");
        artN = Tex(Root + "n"); artS = Tex(Root + "s");
        artW = Tex(Root + "w"); artE = Tex(Root + "e");
        artC = Tex(Root + "c"); artArrow = Tex(Root + "arrow");
    }

    static Texture2D Tex(string path)
    {
        try { var node = WzAssets.Node(path); return node == null ? null : WzAssets.Texture(node); }
        catch (System.Exception) { return null; }
    }

    // 原端头顶 UI 的绘制基准是 768 高虚拟屏（BotField 气泡/名牌同一套），缩放交给 GUI.matrix。
    const float ArtBaseHeight = 768f;

    public static void Draw()
    {
        if (!GameSession.InGame || string.IsNullOrEmpty(boardText)) return;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        var cam = Camera.main;
        if (player == null || cam == null) return;
        EnsureArt();
        if (artNw == null || artC == null) return;

        // 折行与格数（0x489AC8 剥行 + 0x489CC2 单行定宽）。
        if (textStyle == null)
            textStyle = new GUIStyle { font = UiKit.Font, fontSize = 12, wordWrap = false, padding = new RectOffset() };
        var lines = Wrap(boardText, MaxCols * Cell);
        int rows = lines.Count;
        float textW = 0f;
        foreach (var line in lines) textW = Mathf.Max(textW, textStyle.CalcSize(new GUIContent(line)).x);
        int cols = rows == 1 ? Mathf.CeilToInt(textW / Cell) | 1 : MaxCols;   // `or cols,1` 强制奇数
        int innerW = cols * Cell, innerH = rows * RowH;
        int w = innerW + LeftW + RightW, h = TopH + innerH + BottomM;

        var body = Body(w, h, innerW, innerH, cols);

        // 锚点：x = 3 - innerW/2 - leftW（0x489D6C，即以角色为中心右移 3px）；
        // y 按本项目已验证约定：尾尖贴站立帧包围盒顶。
        var renderer = player.GetComponentInChildren<SpriteRenderer>();
        var sp = cam.WorldToScreenPoint(new Vector3(player.transform.position.x,
            player.transform.position.y + (renderer != null && renderer.sprite != null ? renderer.sprite.bounds.max.y : 0.55f), 0f));
        if (sp.z < 0f) return;
        float s = Mathf.Max(0.35f, UiKit.H / ArtBaseHeight);
        float anchorX = sp.x / UiKit.Scale / s, anchorY = (Screen.height - sp.y) / UiKit.Scale / s;
        var rect = new Rect(anchorX + 3f - w * 0.5f, anchorY - h, w, h);
        float viewW = UiKit.W / s, viewH = UiKit.H / s;
        if (rect.xMax < -40f || rect.x > viewW + 40f || rect.yMax < -40f || rect.y > viewH + 40f) return;

        UiKit.Begin();
        var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(s, s, 1f));
        if (body != null) GUI.DrawTexture(rect, body);
        // 文字白色（clr=-1），逐行画进 inner 区；行高 14，格宽余量 <14px。
        textStyle.normal.textColor = Color.white;
        textStyle.alignment = TextAnchor.UpperLeft;
        for (int i = 0; i < rows; i++)
            GUI.Label(new Rect(rect.x + LeftW, rect.y + TopH + i * RowH, innerW, RowH), lines[i], textStyle);
        GUI.matrix = matrix;
    }

    // 原端换行：先量 14 格内放得下的字符数，再从该处向前找空格/换行断行，找不到硬断
    // （0x48ED5A + 0x489B66 向后扫 + 0x489B9C 硬断；断出的分隔符被丢弃）。
    static System.Collections.Generic.List<string> Wrap(string text, int maxPixels)
    {
        var lines = new System.Collections.Generic.List<string>();
        var rest = text ?? "";
        while (rest.Length > 0)
        {
            int fits = 0, width = 0;
            while (fits < rest.Length)
            {
                int next = width + Mathf.CeilToInt(textStyle.CalcSize(new GUIContent(rest[fits].ToString())).x);
                if (fits > 0 && next > maxPixels) break;
                width = next; fits++;
            }
            int cut = fits;
            if (fits < rest.Length && fits > 0)
            {
                int space = rest.LastIndexOfAny(new[] { ' ', '\n' }, fits - 1, fits);
                if (space >= 0) cut = space;                      // 分隔符丢弃（rest 跳过它）
            }
            if (cut <= 0) cut = Mathf.Max(1, fits);               // 兜底，别死循环
            lines.Add(rest.Substring(0, cut));
            rest = cut < rest.Length && rest[cut] == ' ' ? rest.Substring(cut + 1) : rest.Substring(cut);
        }
        if (lines.Count == 0) lines.Add("");
        return lines;
    }

    // 九宫格先合成一张整图再画（同 BotField：分块画会在非整数缩放下出 1px 缝）。
    static readonly Dictionary<int, Texture2D> bodies = new Dictionary<int, Texture2D>();

    static Texture2D Body(int w, int h, int innerW, int innerH, int cols)
    {
        int key = (w << 16) | h;
        if (bodies.TryGetValue(key, out var cached) && cached != null) return cached;
        if (bodies.Count > 24)
        {
            foreach (var texture in bodies.Values) if (texture != null) Object.Destroy(texture);
            bodies.Clear();
        }
        var pixels = new Color32[w * h];                          // 默认全透明
        Blit(artNw, pixels, w, h, 0, 0);                          // 角四块（origin 恰好对齐四角）
        Blit(artNe, pixels, w, h, w - 18, 0);
        Blit(artSw, pixels, w, h, 0, TopH + innerH);
        Blit(artSe, pixels, w, h, w - 18, TopH + innerH);
        for (int x = LeftW; x < LeftW + innerW; x += 14)          // 上/下边带按 14px 平铺
        {
            Blit(artN, pixels, w, h, x, 0);
            Blit(artS, pixels, w, h, x, TopH + innerH);
        }
        for (int y = TopH; y < TopH + innerH; y += 14)            // 左/右边带与中央按 14px 平铺
        {
            Blit(artW, pixels, w, h, 0, y);
            Blit(artE, pixels, w, h, w - 18, y);
            for (int x = LeftW; x < LeftW + innerW; x += 14)
                Blit(artC, pixels, w, h, x, y);
        }
        // 箭头画在正中列（cols 为奇数，cols/2 恰为中央），上 19 行与 s 相同、尾尖多出 4px。
        Blit(artArrow, pixels, w, h, LeftW + (cols / 2) * Cell, TopH + innerH);
        var body = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = "AdBoard_" + w + "x" + h,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        body.SetPixels32(pixels); body.Apply();
        bodies[key] = body;
        return body;
    }

    // 把切图按原样画进整图；top 是自上而下的行号，越界自动裁掉。
    static void Blit(Texture2D piece, Color32[] dst, int dw, int dh, int x, int top)
    {
        if (piece == null) return;
        var src = piece.GetPixels32();
        int sw = piece.width, sh = piece.height;
        for (int ty = 0; ty < sh; ty++)
        {
            int y = top + ty;
            if (y < 0 || y >= dh) continue;
            int row = (dh - 1 - y) * dw;                          // GetPixels32 自下而上
            int sy = sh - 1 - ty;                                 // 源图同样自下而上
            for (int tx = 0; tx < sw; tx++)
            {
                int col = x + tx;
                if (col < 0 || col >= dw) continue;
                dst[row + col] = src[sy * sw + tx];
            }
        }
    }
}
