using System;
using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// 怪物手册（原端 CMonsterBook）。整套坐标按 BeiDou.exe 还原，素材全部取自
// UI/UIWindow.img/MonsterBook（backgrnd / cardSlot / infoPage / select / cover /
// fullMark / arrowLeft / arrowRight / BtClose / BtSearch / LeftTab / LeftTabInfo / RightTab）。
//
// 原端依据（BeiDou.exe，SHA256 1b91cf5b…，ImageBase 0x400000）：
//   0x861E99 窗口构造，依次调用 0x86256D → 0x862B13 → 0x863717 → 0x8637A8 → 0x8638AE → 0x867BE6
//   0x86256D 建子控件，坐标即原端像素（参数序 parent, id, x, y, w, h, &anchor）：
//       BtClose   id 1000 (429, 8)          BtSearch  id 2000 (175, 29)
//       arrowLeft id 2001 (48, 285)         arrowRight id 2002 (185, 285)
//       arrowLeft 第二组 id 2003 (270, 285) arrowRight 第二组 id 2004 (407, 285)
//       搜索输入框 id 2005 (49, 30, 120, 15)，与 BtSearch 同一行
//       左书签栏  id 2006 rect(-7, 25, 50, 305)：57 宽 280 高 → 10 项（9 页 + 手册信息），步距 28
//       右书签栏  id 2007 rect(439, 25, 506, 220)：67 宽 195 高 → 4 项，步距 49
//   0x861F26 开窗把左栏选中项置 9 = 手册信息页（infoPage）
//   0x863DF1 左页渲染：左栏 ==9 画 infoPage(170x231)，否则画 cardSlot(174x256) 上的 25 张卡，
//            并用 UI/Basic.img/ItemNo 画卡面数字
//   0x863717 注册命中框：25 格卡槽 = (8 + 33*col, 31 + 45*row, 27x38)；
//            另 20 格 = (38 + 36*col, 60 + 36*row, 32x32)，即右页战利品网格
//   0x8637A8 建 9 页 × 25 张（idiv 0x19）
//   0x865220 悬停/点击分派：index/25 定位页、index%25 取该页格子
//   0x865782 战利品页用 UI/UIWindow.img/IconBase/0 画图标底座
//
// 竖直基准：左书签栏顶 = 内容顶 = y 25；卡槽 256 高 → 底 281，两对翻页箭头都在 y 285。
// 水平基准：卡槽 174 宽居两条 7px 外边距（48/185 的箭头正好各留 7）；infoPage 170 宽居中
// 得 43；右页从书脊（234~240）之后 241 起，208 宽含 174 正文 + 两侧留白。
// select / cover（31x42，origin (2,40)）与 fullMark（7x11）都按卡面左上 -2,-2 贴图。
public static class NativeMonsterBook
{
    public static bool Visible;

    const float WinW = 475, WinH = 349;
    const float TabOutL = 7, TabOutR = 31;      // 书签伸出书本外：左 -7、右到 506
    const float GroupW = WinW + TabOutL + TabOutR;
    const int Pages = 9, Slots = 25;            // 原端 9 页 × 25 张
    const string Root = "UI/UIWindow.img/MonsterBook/";

    // 左页
    const float LeftX = 41, TopY = 25;         // cardSlot / infoPage / 右页共同的内容顶边
    const float SlotX0 = 8, SlotY0 = 31;        // cardSlot 内首格（0x863717 循环一）
    const float SlotW = 27, SlotH = 38, SlotDX = 33, SlotDY = 45;
    const float InfoX = 43;                     // infoPage 170x231，在 174 宽的卡槽里居中
    const float ArrowY = 285;                   // 两组箭头同一行
    const float SearchX = 49, SearchY = 30, SearchW = 120, SearchH = 15;
    // 右页：书脊（234~240）之后 241 起。战利品网格 4x5 的 38/60、36 步距、32 格来自 0x863717 循环二。
    const float RightX = 241, RightW = 208, RightH = 256;
    const float RewardX = 38, RewardY = 60, RewardStep = 36, RewardSize = 32;
    const float RightTabRight = 506, RightTabW = 52, RightTabSelW = 66, RightTabY = 25, RightTabDY = 49;

    static SocialResponse state;
    static readonly Dictionary<int, ImgNode> cards = new Dictionary<int, ImgNode>();
    static readonly List<int> ids = new List<int>();
    static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
    static int page = Pages;                    // 0..8 卡页，9 = 手册信息页（原端开窗默认）
    static int rightTab;                        // 0 基本情报 / 1 怪物情报 / 2 战利品 / 3 出现地图
    static int selected;                        // 当前卡片 cardId
    static string search = "";
    static bool searching, focus;                // searching = 输入框在编辑，focus = 本帧请求焦点
    static bool windowDrag, contentDrag;
    static float dragGrab, dragBase, contentScroll;
    static Vector2 position = new Vector2(-1, 32);

    static void Ensure()
    {
        if (cards.Count > 0) return;
        var file = WzAssets.FileRoot("Item/Consume/0238.img");
        if (file == null) return;
        foreach (var item in file.Children)
            if (int.TryParse(item.Name, out int id) && item.Int("info/monsterBook") != 0)
            { cards[id] = item; ids.Add(id); }
        ids.Sort();
    }

    public static void Toggle()
    {
        Visible = !Visible;
        if (!Visible) return;
        Ensure();
        state = NativeSocialWindow.Request(SocialOperation.Read);
        contentScroll = 0;
        // 原端开窗落在「手册信息」页；数据没回来时退回第一页，避免空白。
        page = state != null && state.ok ? Pages : 0;
        selected = state != null && state.bookCover != 0 ? state.bookCover : 0;
        // 没有登记封面时右页先显示第一张已收录的卡，右页前后箭头才有起点。
        if (selected == 0 && state != null && state.ok)
        { var list = Collected(); if (list.Count > 0) selected = list[0]; }
    }

    public static bool Collect(int cardId, int quantity)
    {
        if (!GameSession.InGame || GameSession.Net == null) return false;
        Ensure(); if (!cards.ContainsKey(cardId)) return false;
        // Original addCard caps each card at five, and duplicate cards do not add book levels.
        for (int i = 0; i < Mathf.Min(quantity, 5); i++)
        {
            state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId,
                operation = SocialOperation.AddMonsterCard, cardId = cardId });
            if (!state.ok) return false;
        }
        return true;
    }

    static int MobOf(int card) => cards.TryGetValue(card, out var node) ? node.Int("info/mob") : 0;
    static string NameOf(int card) => WzNames.Mob(MobOf(card));
    static int LevelOf(int card) => state != null && state.cards.TryGetValue(card, out int level) ? level : 0;
    static int IdAt(int slot) { int index = page * Slots + slot; return index >= 0 && index < ids.Count ? ids[index] : 0; }

    static Rect IconRect(int slot) => new Rect(
        LeftX + TabOutL + SlotX0 + slot % 5 * SlotDX,
        TopY + SlotY0 + slot / 5 * SlotDY, SlotW, SlotH);

    // 已收录的卡，按书里的顺序（0238.img 编号升序）排；右页前后箭头和「没选中时退到第一张」都用它。
    static List<int> collected;
    static SocialResponse collectedOf;
    static List<int> Collected()
    {
        if (collected != null && ReferenceEquals(collectedOf, state)) return collected;
        collected = new List<int>();
        foreach (int card in ids) if (state != null && state.cards.ContainsKey(card)) collected.Add(card);
        collectedOf = state;
        return collected;
    }
    static int SelectedAt()
    {
        var list = Collected();
        int at = list.IndexOf(selected);
        return at >= 0 ? at : (list.Count > 0 ? 0 : -1);
    }
    // 右页前后箭头：在同一本书里换卡，跨页时左侧卡槽页跟着翻过去。
    static void Pick(int delta)
    {
        var list = Collected();
        if (list.Count == 0) return;
        int at = Mathf.Clamp((SelectedAt() < 0 ? 0 : SelectedAt()) + delta, 0, list.Count - 1);
        selected = list[at];
        int index = ids.IndexOf(selected);
        if (index >= 0) page = index / Slots;
        contentScroll = 0;
    }

    public static void Draw()
    {
        if (!Visible || state == null) return;
        Ensure();
        float s = BottomBar.S;
        if (position.x < 0) position.x = (UiKit.W - GroupW) / 2;
        // 本次窗口位置不落在可视区外（书签伸出书本，按含书签的外框夹取）。
        position.x = Mathf.Clamp(position.x, 0, Mathf.Max(0, UiKit.W - GroupW));
        position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, UiKit.H - WinH));
        var pointer = Pointer.Pos / s;
        var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(s, s, 1));
        try
        {
            var group = new Rect(position.x, position.y, GroupW, WinH);
            // 书皮上方 18px 是唯一没有控件的区域，拖它移动窗口。
            if (GUI.enabled && Event.current.type == EventType.Repaint)
            {
                if (Pointer.Pressed && new Rect(group.x, group.y, GroupW, 18).Contains(pointer))
                { windowDrag = true; dragGrab = pointer.y - group.y; }
                if (windowDrag && Pointer.Down) position.y = pointer.y - dragGrab;
                if (!Pointer.Down) windowDrag = false;
            }
            position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, UiKit.H - WinH));
            group.y = position.y;

            GUI.BeginGroup(group);
            try { Body(pointer - new Vector2(group.x, group.y)); }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }

    static void Body(Vector2 p)
    {
        Image("backgrnd", TabOutL, 0);
        bool cardPage = page != Pages;
        if (cardPage) CardPage(p); else InfoPage();
        RightPage(p);
        Tabs(p);
        // 两组箭头：48/185 翻卡槽的页（只走 9 个卡页），270/407 在右页里前后换卡。
        if (Arrow("arrowLeft", new Rect(TabOutL + 48, ArrowY, 24, 16), page > 0, p)) Turn(-1);
        if (Arrow("arrowRight", new Rect(TabOutL + 185, ArrowY, 23, 17), page < Pages - 1, p)) Turn(1);
        int at = SelectedAt();
        if (Arrow("arrowLeft", new Rect(TabOutL + 270, ArrowY, 24, 16), at > 0, p)) Pick(-1);
        if (Arrow("arrowRight", new Rect(TabOutL + 407, ArrowY, 23, 17), at >= 0 && at < Collected().Count - 1, p)) Pick(1);
        if (NativeUi.Button(new Rect(TabOutL + 429, 8, 18, 16), Root + "BtClose", p)) { Visible = false; return; }
        // 搜索框与 BtSearch 是同一个页头（49,30,120,15 + 175,29），手册信息页没有它们。
        if (cardPage) SearchField(p);
    }

    static void Turn(int delta)
    {
        // 翻页箭头只在 9 个卡页之间走；手册信息页走左边的书签。
        page = Mathf.Clamp(page + delta, 0, Pages - 1);
        contentScroll = 0;
    }

    // ---- 左页 A：25 个卡槽（只画已收录的卡，空格子留 cardSlot 的底框）----
    static void CardPage(Vector2 p)
    {
        Image("cardSlot", LeftX + TabOutL, TopY);
        for (int slot = 0; slot < Slots; slot++)
        {
            int card = IdAt(slot);
            if (card == 0 || !state.cards.ContainsKey(card)) continue;
            var icon = IconRect(slot);
            var node = cards[card].Get("info/icon");
            var texture = node != null ? WzAssets.Texture(node) : null;
            if (texture != null) GUI.DrawTexture(new Rect(icon.x, icon.y, texture.width, texture.height), texture);
            if (state.bookCover == card) Image("cover", icon.x - 2, icon.y - 2);
            if (card == selected) Image("select", icon.x - 2, icon.y - 2);
            int level = LevelOf(card);
            // 收集度画在卡面右下：1..4 用原端数字字体 ItemNo，满 5 张换 fullMark。
            if (level >= 5) Image("fullMark", icon.x + SlotW - 7, icon.y + SlotH - 11);
            else if (level > 0) Digit(level, icon.x + SlotW - 8, icon.y + SlotH - 11);
            var hit = new Rect(icon.x - 2, icon.y - 2, 31, 42);
            if (!hit.Contains(p)) continue;
            bool twice = Pointer.DoubleClicked;
            if (Hit(hit, p))
            {
                selected = card; contentScroll = 0;
                // 原端的右键菜单（ContextMenu/BtRegister、BtRelease）在手机上用双击代替。
                if (twice) SetCover(state.bookCover == card ? 0 : card);
            }
        }
    }

    static void Digit(int value, float x, float y)
    {
        float at = x;
        foreach (char c in value.ToString())
        {
            var texture = WzAssets.Texture("wz://UI/Basic.img/ItemNo/" + c);
            if (texture == null) continue;
            GUI.DrawTexture(new Rect(at, y, texture.width, texture.height), texture);
            at += texture.width;
        }
    }

    // ---- 左页 B：手册信息（infoPage 的印刷行，数值压在冒号右侧）----
    static void InfoPage()
    {
        Image("infoPage", InfoX + TabOutL, TopY);
        int special = 0, touched = 0;
        var pages = new HashSet<int>();
        foreach (var pair in state.cards)
        {
            if (!cards.ContainsKey(pair.Key)) continue;
            int index = ids.IndexOf(pair.Key); if (index >= 0) pages.Add(index / Slots);
            try { if (WzAssets.MobArtRoot(MobOf(pair.Key)).Int("info/boss") != 0) special++; }
            catch (Exception) { }
        }
        touched = pages.Count;
        Value(108, 77, state.bookLevel.ToString());
        Value(103, 130, state.cards.Count.ToString());
        Value(90, 161, special.ToString());
        Value(90, 182, (state.cards.Count - special).ToString());
        Value(64, 218, touched.ToString());
        // 书签封面：infoPage 顶部 40x47 的空卡框，卡面 27x38 居中
        if (state.bookCover != 0 && cards.ContainsKey(state.bookCover))
        {
            var node = cards[state.bookCover].Get("info/icon");
            var texture = node != null ? WzAssets.Texture(node) : null;
            if (texture != null) GUI.DrawTexture(new Rect(TabOutL + InfoX + 73, TopY + 6, texture.width, texture.height), texture);
        }
    }

    static void Value(float x, float y, string value) => GUI.Label(
        new Rect(TabOutL + InfoX + x, TopY + y, 70, 14), value, Style(11, TextAnchor.MiddleLeft));

    // ---- 两条书签栏 ----
    static void Tabs(Vector2 p)
    {
        for (int i = 0; i <= Pages; i++)
        {
            bool current = page == i;
            float y = 25 + 28 * i;
            if (i == Pages)
            {
                // 手册信息书签：未选中 27x35、选中 39x33（IMG 原始尺寸），与其他 9 枚并排。
                var info = new Rect(TabOutL - 7, y, current ? 39 : 27, current ? 33 : 35);
                Paint(Root + "LeftTabInfo/0", info, current, p);
                if (!current && Hit(info, p)) { page = Pages; contentScroll = 0; }
                continue;
            }
            // IMG 里 LeftTab 有 9 套配色（0..8，红/橙/黄绿/绿/青/蓝/紫/黑/金），每页一套；
            // 选中态 42x20、普通 20x20，未读数走 new_n/new_s（2 帧）。
            var rect = new Rect(TabOutL - 7, y, current ? 42 : 20, 20);
            Paint(Root + "LeftTab/" + i, rect, current, p);
            // 第 8 套是黑纸，深色页码看不清，改浅色。
            GUI.Label(new Rect(rect.x + 1, y + 1, current ? 26 : 18, 13), (i + 1).ToString(),
                Style(10, TextAnchor.MiddleCenter, i == 7));
            if (!current && Hit(rect, p)) { page = i; contentScroll = 0; }
        }
        for (int i = 0; i < 4; i++)
        {
            bool current = rightTab == i;
            float width = current ? RightTabSelW : RightTabW;
            var rect = new Rect(TabOutL + RightTabRight - width, RightTabY + RightTabDY * i, width, current ? 39 : 38);
            Paint(Root + "RightTab/" + i, rect, current, p);
            if (!current && Hit(rect, p)) { rightTab = i; contentScroll = 0; }
        }
    }

    static void Paint(string path, Rect rect, bool selected, Vector2 p)
    {
        string stateName = selected ? "selected/0" : rect.Contains(p) && Pointer.Down ? "mouseOver/0" : "normal/0";
        var texture = Art.Tex("wz://" + path + "/" + stateName);
        if (texture != null) GUI.DrawTexture(new Rect(rect.x, rect.y, texture.width, texture.height), texture);
    }

    static bool Arrow(string node, Rect rect, bool enabled, Vector2 p)
    {
        string stateName = !enabled ? "disabled/0" : rect.Contains(p) && Pointer.Down ? "pressed/0" : "normal/0";
        var texture = Art.Tex("wz://" + Root + node + "/" + stateName);
        if (texture != null) GUI.DrawTexture(new Rect(rect.x, rect.y, texture.width, texture.height), texture);
        return enabled && Hit(rect, p);
    }

    // ---- 右页：四个书签各自的正文，整块按 208x256 裁切（书脊右侧 241 起）----
    static void RightPage(Vector2 p)
    {
        int card = RightCard();
        if (card == 0) return;
        var clip = new Rect(TabOutL + RightX, TopY, RightW, RightH);
        float maximum = Mathf.Max(0, ContentHeight(card) - RightH);
        GUI.BeginGroup(clip);
        try
        {
            var q = p - new Vector2(clip.x, clip.y);
            contentScroll = Drag(new Rect(0, 0, RightW, RightH), q, contentScroll, maximum);
            switch (rightTab)
            {
                case 1: Episode(card, contentScroll); break;
                case 2: Rewards(card, contentScroll); break;
                case 3: Areas(card, contentScroll); break;
                default: BasicInfo(card); break;
            }
        }
        finally { GUI.EndGroup(); }
    }

    static int RightCard()
    {
        if (selected != 0 && state.cards.ContainsKey(selected)) return selected;
        var list = Collected();
        return list.Count > 0 ? list[0] : 0;
    }

    // 基本情报：名字居中、立绘居中，下面是四行数值。原端这几行的具体坐标尚未在代码里定到，
    // 只确定它们画在右页正文里；数值列左边界对齐战利品网格的 38（0x863717 循环二）。
    static void BasicInfo(int card)
    {
        int mob = MobOf(card);
        GUI.Label(new Rect(0, 4, RightW, 16), WzNames.Mob(mob), Style(12, TextAnchor.MiddleCenter));
        try
        {
            var root = WzAssets.MobArtRoot(mob);
            var frames = WzAssets.Frames(root.Get("stand"));
            if (frames.Count > 0)
            {
                var texture = WzAssets.Texture(frames[0]);
                if (texture != null)
                {
                    // 立绘不放大，超过 160x100 才等比缩进框里
                    float k = Mathf.Min(1f, 160f / texture.width, 100f / texture.height);
                    float w = texture.width * k, h = texture.height * k;
                    GUI.DrawTexture(new Rect((RightW - w) / 2, 24 + (100 - h) / 2, w, h), texture);
                }
            }
            float y = 132;
            Line(38, y, "Level : " + root.Int("info/level"));
            Line(38, y + 18, "HP : " + root.Int("info/maxHP"));
            Line(38, y + 36, "MP : " + root.Int("info/maxMP"));
            Line(38, y + 54, "EXP : " + root.Int("info/exp"));
        }
        catch (Exception) { }
    }

    static void Line(float x, float y, string text) => GUI.Label(new Rect(x, y, RightW - 48, 16), text, Style(11, TextAnchor.MiddleLeft));

    static void Episode(int card, float scroll) => GUI.Label(
        new Rect(38, 6 - scroll, RightW - 66, ContentHeight(card)), EpisodeText(card), Wrap());

    // 战利品：原端是 4x5 的 32x32 图标格（0x863717 循环二的 38/60、36 步距），底座用 IconBase/0。
    static void Rewards(int card, float scroll)
    {
        var list = Children(card, "reward");
        if (list.Count == 0) { GUI.Label(new Rect(38, 90, RightW - 66, 40), "没有登记战利品", Wrap()); return; }
        var plate = WzAssets.Texture("wz://UI/UIWindow.img/IconBase/0");
        for (int i = 0; i < list.Count && i < 20; i++)
        {
            float x = RewardX + i % 4 * RewardStep, y = RewardY + i / 4 * RewardStep - scroll;
            if (plate != null) GUI.DrawTexture(new Rect(x, y, RewardSize, RewardSize), plate);
            var icon = NativeItemSource.IconNode(list[i]);
            var texture = icon != null ? WzAssets.Texture(icon) : null;
            if (texture != null)
                GUI.DrawTexture(new Rect(x + (RewardSize - texture.width) / 2, y + (RewardSize - texture.height) / 2, texture.width, texture.height), texture);
        }
    }

    static void Areas(int card, float scroll)
    {
        var list = Children(card, "map");
        if (list.Count == 0) { GUI.Label(new Rect(38, 90, RightW - 66, 40), "没有登记出现地图", Wrap()); return; }
        for (int i = 0; i < list.Count; i++)
        {
            float y = 8 + i * 18 - scroll;
            if (y > RightH || y + 18 < 0) continue;
            GUI.Label(new Rect(38, y, RightW - 66, 16), WzNames.Map(list[i]), Style(11, TextAnchor.MiddleLeft));
        }
    }

    static ImgNode Info(int card) => WzAssets.FileRoot("String/MonsterBook.img")?.Get(MobOf(card).ToString());

    static string EpisodeText(int card)
    {
        var node = Info(card)?.Get("episode");
        var value = node?.Value;
        return value == null ? "" : Convert.ToString(value).Replace("\\n", "\n");
    }

    static List<int> Children(int card, string name)
    {
        var result = new List<int>();
        var node = Info(card)?.Get(name);
        if (node == null) return result;
        foreach (var child in node.Children)
            if (child.Value != null) { try { result.Add(Convert.ToInt32(child.Value)); } catch (Exception) { } }
        return result;
    }

    static float ContentHeight(int card)
    {
        if (rightTab == 1)
        {
            var text = EpisodeText(card);
            if (string.IsNullOrEmpty(text)) return RightH;
            return Mathf.Max(RightH, Wrap().CalcHeight(new GUIContent(text), RightW - 16) + 12);
        }
        if (rightTab == 2) return RightH;              // 战利品是 4x5 固定网格，不滚动
        if (rightTab == 3) return Mathf.Max(RightH, 12 + Children(card, "map").Count * 18);
        return RightH;
    }

    // 手机上滚轮不存在：内容区上下拖动即滚动；没拖动的抬手才算点击。
    static float Drag(Rect view, Vector2 p, float value, float maximum)
    {
        if (maximum <= 0) { contentDrag = false; return 0; }
        if (Event.current.type != EventType.Repaint) return value;
        if (Pointer.Pressed && view.Contains(p)) { contentDrag = true; dragGrab = p.y; dragBase = value; }
        if (contentDrag)
        {
            if (Pointer.Down) value = Mathf.Clamp(dragBase - (p.y - dragGrab), 0, maximum);
            else contentDrag = false;
        }
        return value;
    }

    // ---- 搜索：原端 (49,30,120,15) 的输入框 + (175,29) 的 BtSearch，就在卡槽白条上 ----
    static void SearchField(Vector2 p)
    {
        var rect = new Rect(TabOutL + SearchX, SearchY, SearchW, SearchH);
        if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed)
        {
            if (rect.Contains(p)) { searching = true; focus = true; }
            else if (searching) { searching = false; Apply(); }
        }
        if (focus && Event.current.type == EventType.Repaint) { GUI.FocusControl("MonsterBookSearch"); focus = false; }
        if (searching)
        {
            GUI.SetNextControlName("MonsterBookSearch");
            string next = GUI.TextField(new Rect(rect.x + 2, rect.y + 1, rect.width - 4, rect.height - 2),
                search, 20, Style(11, TextAnchor.MiddleLeft));
            if (next != search) search = next;
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            { searching = false; Apply(); }
        }
        else if (search.Length > 0)
            GUI.Label(new Rect(rect.x + 2, rect.y + 1, rect.width - 4, rect.height - 2),
                search, Style(11, TextAnchor.MiddleLeft));
        // 按钮紧挨输入框：有字就跳过去，没字就把光标放进框里。
        if (NativeUi.Button(new Rect(TabOutL + 175, 29, 34, 17), Root + "BtSearch", p))
        {
            if (search.Length == 0) { searching = true; focus = true; }
            else { searching = false; Apply(); }
        }
    }

    // 命中即跳到该卡所在页并选中；没有命中停在原页。
    static void Apply()
    {
        if (string.IsNullOrEmpty(search)) return;
        for (int i = 0; i < ids.Count; i++)
        {
            string name = NameOf(ids[i]);
            if (name == null || name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            page = i / Slots; selected = ids[i]; contentScroll = 0;
            return;
        }
        BottomBar.Toast("找不到名字含有「" + search + "」的怪物卡片");
    }

    static void SetCover(int card)
    {
        if (!GameSession.InGame || GameSession.Net == null) return;
        state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId,
            operation = SocialOperation.SetBookCover, cardId = card });
        if (!state.ok) { BottomBar.Toast(state.error); return; }
        // Preserve this server-owned field when the existing character snapshot is saved.
        GameSession.Character.record.monsterbookcover = state.bookCover;
    }

    // ---- 绘制小工具 ----
    static void Image(string node, float x, float y)
    {
        var texture = Art.Tex("wz://" + Root + node);
        if (texture != null) GUI.DrawTexture(new Rect(x, y, texture.width, texture.height), texture);
    }

    static bool Hit(Rect rect, Vector2 p)
    {
        if (!GUI.enabled || Event.current.type != EventType.Repaint || !Pointer.Released || !rect.Contains(p)) return false;
        Pointer.ConsumeClick(); return true;
    }

    static GUIStyle Style(int size, TextAnchor anchor, bool light = false)
    {
        int key = size * 16 + (int)anchor + (light ? 1000 : 0);
        if (styles.TryGetValue(key, out var style)) return style;
        style = new GUIStyle { font = UiKit.Font, fontSize = size, padding = new RectOffset(),
            alignment = anchor, clipping = TextClipping.Clip, richText = false };
        style.normal.textColor = light ? new Color(0.97f, 0.95f, 0.88f) : new Color(0.22f, 0.15f, 0.1f);
        styles.Add(key, style);
        return style;
    }

    static GUIStyle wrap;
    static GUIStyle Wrap()
    {
        if (wrap != null) return wrap;
        wrap = new GUIStyle { font = UiKit.Font, fontSize = 11, wordWrap = true, padding = new RectOffset(),
            clipping = TextClipping.Clip, richText = false };
        wrap.normal.textColor = new Color(0.22f, 0.15f, 0.1f);
        return wrap;
    }
}
