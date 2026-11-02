using UnityEngine;

// 原端「传送石 / 缩地功能」窗：cashType22 → 0xA0CAB0（USE_CASH 分派，跳转表 0xA0EAD4[10]）。
// 0xA0CAB0：isVIP = (itemid/1000 != 5040)（5040000..5040004 普通石 / 5041000 VIP 石），
//           调 0xa0a4aa(请求对象, isVIP)；0xa0a4aa 建模态对话框（类 0xbf0b00，vtable 0xb39900，
//           构造 0x8390d1，资源串 2944），0x4edba1 返回 1 表示确认，结果由 0x83a668 读回。
// 资源（字符串池 id 2944..2948，尺寸实测）：
//   Teleport/backgrnd  175x309  窗口底图（上「地图目录」列表 / 下「角色移动」名字输入）
//   Teleport/select    158x17   选中行高亮（蓝条）
//   Teleport/BtRegister 47x18  「增加」按钮（登记当前地图）
//   Teleport/BtDelete  47x18   「删除」按钮
//   Teleport/BtMove    47x18   「移动」按钮（确认传送）
// 控件（OnCreate 0x8392c1 的 push 常量对，调用约定 (this, id, x, y, ?, &out)）：
//   BtRegister id=2000 @(6,285)；BtDelete id=2001 @(56,285)；BtMove id=1 @(120,285)
//   关闭 UI/Basic.img/BtClose（串 5577）id=2 @(156,6) 12x12
//   名字编辑框 id=1000 @(34,250) 宽 120 高 13，字号 12
//   VIP 专用滚动条 id=3000（仅 isVIP 创建，参数 (3000,1,3,155,80,98,0)）——
//   该控件类 0x4d6cd3 未逆向，暂用工程内通用 VScr4（15 宽）放在 (155,80) 高 98，属推断。
// 列表（Draw 0x8398d5）：
//   计数文字 "[%d/%d]"（串 2082）右对齐 x=165、y=38；
//   5 行可见，行距 18：行名文字 (15, 87+18k)，选中行先画 select (9, 84+18k)；
//   条目来自角色身上的传送列表：普通 5 项（角色 +0x563）、VIP 10 项（+0x577），
//   空槽哨兵 999999999（0x3b9ac9ff，与服务端 MapId.NONE 同值）。
// 按钮语义（OnButton 0x839d82）：
//   2000 → 0x83a084 登记：满(2953) → 当前图已存在(2954) → 当前图无效(2952) → 确认串 2957 → Yes
//          后 0xa261bc(player, 1, 0, isVIP)（服务端 TrockAddMapHandler type=1 取第一个空槽）。
//   2001 → 0x83a2a0 删除：需选中 → 确认串 2958 → Yes 后 0xa261bc(player, 0, mapid, isVIP)。
//   1    → 0x83a3c2(1) 移动：选中行 → 当前图检查(2955) → 确认串 2959 → 传送该地图；
//          未选中 → 读名字编辑框：空/非法(串 807) / 自己(2951) → 请求按名字传送。
//   2    → 关闭。
// 打开前置检查（0xa0a4aa）：死亡状态（串 397，本工程按 hp<=0 判定）、
// 当前图 fieldLimit 含 0x40(CANNOTVIPROCK) 或 0x2 → 串 2950「You cannot teleport to this map.」。
// 服务端语义（UseCashItemHandler.java 132..171）：
//   包序 readByte(vip) + (!vip ? readInt(mapId) : readString(name))；
//   先 remove 道具，成功才不退；!vip 要求 mapId/1e8 == 当前图/1e8（同大陆）；
//   目标图 !CANNOTVIPROCK(0x40) 且 (forcedReturnId==999999999 || isMapleIsland[0..2000001])；
//   vip 按名字找频道内角色，GM 只能被不低于自己 GM 的目标使用；失败 addById 退款。
// 提示文案：源端此构建的 EXE 字符串池 2949..2959 为英文；控件/按钮中文来自原端 IMG 资源，
// 服务端中文提示沿用 i18n/message_zh_CN.properties（本项目已合并的官方中文）。
// 本工程边界：传送列表持久化在 Profile 的 questProgress KV（key "trock"/"trockVip"，questId 0），
// 原端是角色表字段（trocks/vipTrocks）；schema 补丁见 docs/parallel/504-teleport.md。
public static class NativeTeleportWindow
{
    const string Root = "UI/UIWindow.img/Teleport/";
    const int Empty = 999999999;          // 0x3b9ac9ff：空槽哨兵（角色列表初始值 / MapId.NONE）
    const int VisibleRows = 5;            // Draw 循环 edi..edi+5
    const int RegularSlots = 5;           // 角色偏移 +0x563，5 项
    const int VipSlots = 10;              // 角色偏移 +0x577，10 项
    // 登记无效图（0x83a084）：mapid/1e8==0（彩虹岛小图无 1e8 段）或 (mapid/1e6)%100==9（x09 段活动图）
    static bool Registrable(int mapId)
        => mapId / 100000000 != 0 && (mapId / 1000000) % 100 != 9;

    static int itemId, itemPosition, selected, scrollTop;
    static bool vip, opened;
    static readonly int[] slots = new int[VipSlots];
    static string nameInput = "";
    static Vector2 pos, grab;
    static bool dragging, centered;

    public static bool Visible => opened;
    static float S => BottomBar.S;
    static int Capacity => vip ? VipSlots : RegularSlots;
    static int ValidCount { get { int n = 0; for (int i = 0; i < Capacity; i++) if (slots[i] != Empty) n++; return n; } }
    static Vector2 Size => new Vector2(175, 309);
    static Rect Window => new Rect(pos.x * S, pos.y * S, Size.x * S, Size.y * S);
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static void Image(Rect r, string node, float x, float y)
    {
        var tex = Art.Tex("wz://" + Root + node);
        if (tex != null) GUI.DrawTexture(At(r, x, y, tex.width, tex.height), tex);
    }
    static int CurrentMapId => GameLoop.I != null && GameLoop.I.field != null && GameLoop.I.field.Map != null
        ? GameLoop.I.field.Map.id : Profile.LastMapId;
    static int FieldLimit => GameLoop.I != null && GameLoop.I.field != null && GameLoop.I.field.Map != null
        ? GameLoop.I.field.Map.fieldLimit : 0;

    // ————— 传送列表持久化（Profile questProgress KV，见文件头边界说明） —————
    static string Key => vip ? "trockVip" : "trock";
    static void LoadSlots()
    {
        for (int i = 0; i < VipSlots; i++) slots[i] = Empty;
        var parts = Profile.QuestProgress(0, Key, "").Split(',');
        for (int i = 0; i < parts.Length && i < Capacity; i++)
            if (int.TryParse(parts[i].Trim(), out int id) && id > 0) slots[i] = id;
    }
    static void SaveSlots()
    {
        var ids = new int[Capacity];
        for (int i = 0; i < Capacity; i++) ids[i] = slots[i];
        Profile.SetQuestProgress(0, Key, string.Join(",", ids));
    }

    public static void Open(int id, int inventoryPosition = 0)
    {
        if (id / 10000 != 504) return;
        // 0xa0a4aa 前置检查 1：死亡状态（串 397 "You can't do it while you're dead."）
        if (GameLoop.I != null && GameLoop.I.player != null && GameLoop.I.player.hp <= 0f)
        { BottomBar.Toast("You can't do it while you're dead."); return; }
        // 前置检查 2：0x536575 + field+0x144 位 6(0x40 CANNOTVIPROCK)/位 1(0x2) → 串 2950
        int limit = FieldLimit;
        if ((limit & 0x40) != 0 || (limit & 0x2) != 0)
        { BottomBar.Toast("You cannot teleport to this map."); return; }
        itemId = id; itemPosition = inventoryPosition;
        vip = id / 1000 != 5040;          // 0xA0CAB0 的 setne
        selected = -1; scrollTop = 0; nameInput = "";
        dragging = false; centered = false;
        LoadSlots();
        opened = true;
        pos = new Vector2(300, 160);
        Center();
    }
    public static void Close()
    {
        opened = false; itemId = 0; selected = -1; scrollTop = 0; nameInput = "";
        dragging = false; centered = false;
    }
    // 共享创建例程 0x4EDAEF 按 (800-w)/2,(600-h)/2 居中；S 没量准时等绘制帧再算。
    static void Center()
    {
        if (S < 0.3f) return;
        pos = NativeUi.ClampWindowPosition(new Vector2((UiKit.W / S - Size.x) / 2, (UiKit.H / S - Size.y) / 2), Size, S);
        centered = true;
    }

    public static void Draw()
    {
        if (!Visible) return;
        if (!centered) Center();
        pos = NativeUi.ClampWindowPosition(pos, Size, S);
        var r = Window;
        NativeUi.RegisterWindow("NativeTeleportWindow", r, () => NativeTeleportWindow.Visible);
        var pointer = Pointer.Pos / S;
        if (Event.current.type == EventType.Repaint && Pointer.Pressed && At(r, 0, 0, 150, 22).Contains(Pointer.Pos))
        { dragging = true; centered = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down)
        {
            pos = NativeUi.ClampWindowPosition(Pointer.Pos / S - grab, Size, S);
            r = Window;
        }
        if (!Pointer.Down) dragging = false;

        Image(r, "backgrnd", 0, 0);
        if (NativeUi.Button(At(r, 156, 6, 12, 12), "UI/Basic.img/BtClose", pointer)) { Close(); return; }

        // 计数 "[%d/%d]"（串 2082）：0x414576(target, 165-文本宽, 38) → 右缘对齐 x=165、y=38。
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * S), alignment = TextAnchor.MiddleRight };
        style.normal.textColor = Color.black;
        GUI.Label(At(r, 113, 36, 52, 16), "[" + ValidCount + "/" + Capacity + "]", style);

        // 列表：行名 (15, 87+18k)，选中高亮 select (9, 84+18k)，158x17。
        var listStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * S) };
        listStyle.normal.textColor = Color.black;
        int maxScroll = Mathf.Max(0, Capacity - VisibleRows);
        if (vip)
        {
            // VIP 滚动条（原端 id=3000，几何为推断）：工程通用 VScr4（聊天窗/任务窗同款 15 宽）。
            var value = NativeUi.Scroll4(At(r, 155, 80, 15, 98), At(r, 9, 80, 158, 95),
                scrollTop, maxScroll, 1, pointer);
            scrollTop = Mathf.RoundToInt(value);
        }
        for (int k = 0; k < VisibleRows; k++)
        {
            int i = scrollTop + k;
            if (i >= Capacity) break;
            int mapId = slots[i];
            if (mapId == Empty) continue;
            if (i == selected) Image(r, "select", 9, 84 + 18 * k);
            var row = new GUIStyle(listStyle) { alignment = TextAnchor.MiddleLeft };
            GUI.Label(At(r, 15, 87 + 18 * k, 140, 16), NativeMapNames.Of(mapId), row);
            // 行点击选中：原端鼠标路径（+0xB0 选中索引）已按 0x83a668 读回语义复现。
            // 确认弹窗（NativeUtilPrompt）打开期间不接管点击，与按钮的 enabled 门控一致。
            if (Event.current.type == EventType.Repaint && Pointer.Clicked && !NativeUtilPrompt.Visible
                && At(r, 9, 84 + 18 * k, 158, 17).Contains(pointer))
            { selected = i; Pointer.ConsumeClick(); }
        }

        // 名字编辑框 id=1000 @(34,250) 120x13（0x8392c1 常量对）。
        var editRect = At(r, 34, 250, 120, 13);
        UiKit.Fill(editRect, Color.black);
        UiKit.Fill(new Rect(editRect.x + 1, editRect.y + 1, editRect.width - 2, editRect.height - 2), Color.white);
        var editStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * S) };
        editStyle.normal.textColor = Color.black;
        editStyle.alignment = TextAnchor.MiddleLeft;
        nameInput = GUI.TextField(new Rect(editRect.x + 2, editRect.y + 1, editRect.width - 4, editRect.height - 2),
            nameInput, 12, editStyle);

        if (NativeUi.Button(At(r, 6, 285, 47, 18), Root + "BtRegister", pointer)) Register();
        if (NativeUi.Button(At(r, 56, 285, 47, 18), Root + "BtDelete", pointer)) Delete();
        if (NativeUi.Button(At(r, 120, 285, 47, 18), Root + "BtMove", pointer)) Move();
    }

    // ————— BtRegister（0x83a084）：登记当前地图到第一个空槽 —————
    static void Register()
    {
        int current = CurrentMapId;
        if (ValidCount >= Capacity) { BottomBar.Toast("Your teleport list is full.\nPlease delete an entry before trying again."); return; }
        for (int i = 0; i < Capacity; i++)
            if (slots[i] == current) { BottomBar.Toast("You have already entered this map."); return; }
        if (!Registrable(current)) { BottomBar.Toast("This map is not available to enter for the list."); return; }
        // 服务端 TrockAddMapHandler type=1：当前图 CANNOTVIPROCK 时拒绝（"You may not save this map."）。
        var map = FieldCatalog.GetMap(current);
        if (map == null || (map.fieldLimit & 0x40) != 0) { BottomBar.Toast("You may not save this map."); return; }
        string name = NativeMapNames.Of(current);
        NativeUtilPrompt.Confirm("Will you enter this map in your teleport list?\n[" + name + "]", ok =>
        {
            if (!ok) return;
            for (int i = 0; i < Capacity; i++)
                if (slots[i] == Empty) { slots[i] = current; break; }
            SaveSlots();
        });
    }

    // ————— BtDelete（0x83a2a0）：删除选中项（未选中时原端直接返回） —————
    static void Delete()
    {
        if (selected < 0 || selected >= Capacity || slots[selected] == Empty) return;
        int mapId = slots[selected];
        string name = NativeMapNames.Of(mapId);
        NativeUtilPrompt.Confirm("Will you delete this map from the\n teleport list?\n[" + name + "]", ok =>
        {
            if (!ok) return;
            slots[selected] = Empty;      // 0x83a392：删除后 or [esi+0xB0], -1
            selected = -1;
            SaveSlots();
        });
    }

    // ————— BtMove（0x83a3c2(1)）：选中行走地图传送，否则按名字传送 —————
    // 原端 0x83a668 会把编辑框名字与选中 mapId 同时写回，0xa0a4aa 里名字非空时优先；
    // 但地图路径确认后 0x4CC512(edit, 0xAF1734) 疑似清空编辑框，此处按「选中行优先」实现。
    static void Move()
    {
        if (selected >= 0 && selected < Capacity && slots[selected] != Empty)
        {
            int mapId = slots[selected];
            if (mapId == CurrentMapId) { BottomBar.Toast("It's the map you're currently on."); return; }
            string name = NativeMapNames.Of(mapId);
            NativeUtilPrompt.Confirm("Will you teleport to this map?\n[" + name + "]", ok => { if (ok) Execute(mapId, null); });
            return;
        }
        // 名字路径（编辑框）：空/非法 → 串 807；自己 → 串 2951。
        string input = nameInput != null ? nameInput.Trim() : "";
        if (input.Length == 0 || !NameValid(input)) { BottomBar.Toast("- The character name is invalid."); return; }
        if (input == Profile.playerName) { BottomBar.Toast("You cannot target yourself\nfor the teleport."); return; }
        var victim = FindCharacter(input);
        if (victim == null) { BottomBar.Toast("在这个频道中找不到玩家。"); return; }
        Execute(victim.map, input);
    }
    // 0xa6c440 的精确判定未逆向；此处按「非空、长度合法」复现，原端语义待核实。
    static bool NameValid(string name)
    {
        foreach (char c in name) if (c < 0x20) return false;
        return true;
    }
    static CharRecord FindCharacter(string name)
    {
        var server = GameSession.Server;
        if (server == null) return null;
        var row = server.Db.Table("characters").Find(DbStore.Eq("name", name));
        return row == null ? null : CharRecord.FromRow(row);
    }

    // ————— 传送执行（UseCashItemHandler.java 132..171 的本机权威等价） —————
    // 服务端顺序是 remove → 判定 → 失败 addById 退款（净效果：失败时数量不变）。
    // 本机按净效果实现：全部判定通过才扣道具，避免拆堆叠的副作用。
    static void Execute(int mapId, string targetName)
    {
        int current = CurrentMapId;
        bool byName = targetName != null;
        var victim = byName ? FindCharacter(targetName) : null;
        if (byName)
        {
            if (victim == null) { BottomBar.Toast("在这个频道中找不到玩家。"); return; }
            // 源端 thanks Yoboes：非 GM 不能用石头摸到 GM（victim.gmLevel() <= player.gmLevel() 才放行）。
            if (victim.gm > 0 && !GmAtLeast(victim.gm))
            { BottomBar.Toast("找不到玩家或者您试图传送到一个非法的位置。"); return; }
        }
        else
        {
            // 同大陆限制：itemId/1000>=5041（VIP 石）豁免，否则要求前 8 位段相同。
            if (itemId / 1000 < 5041 && mapId / 100000000 != current / 100000000)
            { BottomBar.Toast("您无法通过这个传送石在大陆之间传送。"); return; }
        }
        var map = FieldCatalog.GetMap(mapId);
        if (map == null || (map.fieldLimit & 0x40) != 0
            || (map.forcedReturn != Empty && !(mapId >= 0 && mapId <= 2000001)))
        { BottomBar.Toast(byName ? "您无法传送到这个地图。" : "找不到玩家或者您试图传送到一个非法的位置。"); return; }
        if (!Profile.RemoveItem(itemId, 1, itemPosition)) return;
        Close();
        if (GameLoop.I != null && GameLoop.I.field != null) GameLoop.I.field.WarpTo(mapId);
    }
    static bool GmAtLeast(int victimGm)
    {
        var self = GameSession.Character;
        int myGm = self != null && self.record != null ? self.record.gm : 0;
        return victimGm <= myGm;
    }
}
