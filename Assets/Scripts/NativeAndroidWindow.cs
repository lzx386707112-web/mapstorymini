using UnityEngine;

// 机器人换装面板 —— 282 的装备窗把机器人本体/心臟和 14 个 AP 时装槽画在单独一页，
// 但那页的美术在 GMS083 底图上并不存在（UI.wz 没有随之迁移），所以这里不改原底图，
// 另开一个自绘的小面板贴在装备窗右侧：机器人 / 心臟 / 12 个 AP 时装槽。
//
// 槽位语义全部来自 NativeAndroidService（Aslot 常量 + ApAccepts），这里只负责摆放与命中。
public static class NativeAndroidWindow
{
    public static bool Visible;

    // 14 格排 3 列 = 5 行：24 + 4*45 + 28(格) = 232，标签再占 12px 到 244，
    // 底部原因行要留在 257..270，所以 H 必须 ≥ 272（原来 256 会让第 5 行的「戒指」
    // 标签和「請安裝心臟」压在一起）。
    const int W = 152, H = 272;
    const int GridX = 10, GridY = 24, ColPitch = 48, RowPitch = 45, Cell = 28;

    // 机器人本体与心臟在 BodyPart 里是 53/54，本工程落到 -32/-54；后面 12 格是 AP 时装槽。
    static readonly int[] Slots =
    {
        NativeAndroidService.RobotSlot, NativeAndroidService.HeartSlot, NativeAndroidService.ApHat,
        NativeAndroidService.ApFaceAccessory, NativeAndroidService.ApTop, NativeAndroidService.ApBottom,
        NativeAndroidService.ApShoes, NativeAndroidService.ApGloves, NativeAndroidService.ApCape,
        NativeAndroidService.ApWeapon, NativeAndroidService.ApRing, NativeAndroidService.ApRing2,
        NativeAndroidService.ApRing3, NativeAndroidService.ApRing4,
    };
    static readonly string[] Names =
    {
        "機器人", "心臟", "帽子",
        "臉飾", "上衣", "下裝",
        "鞋", "手套", "披風",
        "武器", "戒指", "戒指",
        "戒指", "戒指",
    };

    static float S => BottomBar.S;
    static GUIStyle label, title;
    static string reason = "";
    static double reasonAt;

    public static void Toggle() => Visible = !Visible;

    /// <summary>命中换装格 → 回传槽位号（-32 / -54 / 1200..1213）；装备窗拖拽走这条。</summary>
    public static bool SlotAt(Vector2 pointer, out int slot)
    {
        slot = 0;
        if (!Visible || !GameSession.InGame) return false;
        var r = RectOf();
        for (int i = 0; i < Slots.Length; i++)
            if (DropRect(r, i).Contains(pointer)) { slot = Slots[i]; return true; }
        return false;
    }

    public static void Draw()
    {
        if (!Visible) return;
        if (!GameSession.InGame || GameSession.Character == null || !EquipWindow.Visible) { Visible = false; return; }
        var r = RectOf();
        NativeUi.RegisterWindow("AndroidWindow", r, () => Visible);
        UiKit.Fill(r, new Color(.075f, .085f, .115f, .97f));

        if (title == null)
            title = new GUIStyle(GUI.skin.label) { font = UiKit.Font, fontSize = 12, alignment = TextAnchor.MiddleLeft };
        if (label == null)
            label = new GUIStyle(GUI.skin.label) { font = UiKit.Font, fontSize = 10, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = new Color(.85f, .9f, 1f);
        label.normal.textColor = new Color(.62f, .68f, .8f);
        GUI.Label(new Rect(r.x + 6 * S, r.y + 3 * S, 90 * S, 16 * S), "機器人", title);
        if (NativeUi.Button(new Rect(r.xMax - 17 * S, r.y + 4 * S, 12 * S, 12 * S), "UI/Basic.img/BtClose"))
        { Visible = false; return; }

        for (int i = 0; i < Slots.Length; i++)
        {
            int slot = Slots[i];
            var cellRect = CellRect(r, i);
            UiKit.Fill(cellRect, new Color(0f, 0f, 0f, .45f));
            int id = Profile.EquipOfSlot(slot);
            if (id > 0)
            {
                var icon = EquipWindow.ArmorIcon(id);
                if (icon != null) GUI.DrawTexture(cellRect, icon, ScaleMode.ScaleToFit);
            }
            GUI.Label(new Rect(cellRect.x - 8 * S, cellRect.yMax, cellRect.width + 16 * S, 12 * S), Names[i], label);
            if (Event.current.type != EventType.Repaint) continue;
            if (cellRect.Contains(Pointer.Pos) && id > 0) ItemTooltip.SetHover(id, true);
            // 双击脱下：机器人本体/心臟/时装都只是装备位，卸下后服务端放回背包空格。
            if (id > 0 && Pointer.DoubleClicked && cellRect.Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick();
                EquipLoadout.Unequip(slot);
                if (Profile.EquipOfSlot(slot) == 0) BottomBar.Toast("已脱下" + NameOf(slot));
            }
        }

        // 装了机器人但显示不出来时把原因写在面板底部（缺心臟 / 心臟等级过高 / 缺外观模板）。
        if (Time.unscaledTime >= reasonAt)
        {
            reasonAt = Time.unscaledTime + .5f;
            reason = NativeAndroidService.Resolve(GameSession.CharacterId, Profile.EquipOfSlot).reason;
        }
        if (!string.IsNullOrEmpty(reason))
        {
            label.normal.textColor = new Color(1f, .62f, .55f);
            label.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(r.x + 4 * S, r.yMax - 15 * S, (W - 8) * S, 13 * S), reason, label);
            label.normal.textColor = new Color(.62f, .68f, .8f);
        }
    }

    public static string NameOf(int slot)
    {
        int index = System.Array.IndexOf(Slots, slot);
        return index >= 0 ? Names[index] : "装备";
    }

    static Rect RectOf()
    {
        var size = EquipWindow.Size;
        var pos = EquipWindow.Position;
        // 与 ClampWindowPosition 用同一口径算可用宽（它内部是 UiKit.W / scale - size.x），
        // 否则小屏上会出现「以为放得下、钳回来盖住装备窗」。
        bool fits = pos.x + size.x + 2 + W <= UiKit.W / S;
        // 右边放不下就翻到左侧：宁可挨着，也别压在装备窗上（那样两边的格子都点不准）
        var target = fits ? new Vector2(pos.x + size.x + 2, pos.y) : new Vector2(pos.x - W - 2, pos.y);
        var origin = NativeUi.ClampWindowPosition(target, new Vector2(W, H), S) * S;
        return new Rect(origin.x, origin.y, W * S, H * S);
    }

    static Rect CellRect(Rect r, int index)
        => new Rect(r.x + (GridX + index % 3 * ColPitch) * S,
                    r.y + (GridY + index / 3 * RowPitch) * S, Cell * S, Cell * S);

    // 每个部位的接收区包含图标、下方名称与行间留白；按现有列距/行距划分，
    // 相邻部位不重叠。绘制仍使用原来的 CellRect，不改变面板布局。
    static Rect DropRect(Rect r, int index)
    {
        var cell = CellRect(r, index);
        return new Rect(cell.x - (ColPitch - Cell) * .5f * S, cell.y,
            ColPitch * S, RowPitch * S);
    }

    /// <summary>装备窗底部的入口按钮（自绘，没有对应 WZ 图元）。</summary>
    public static bool ToggleButton(Rect rect, bool on, string text)
    {
        UiKit.Fill(rect, on ? new Color(.24f, .44f, .72f, .95f) : new Color(.13f, .15f, .19f, .95f));
        if (label == null)
            label = new GUIStyle(GUI.skin.label) { font = UiKit.Font, fontSize = 10, alignment = TextAnchor.MiddleCenter };
        var old = label.normal.textColor;
        label.normal.textColor = on ? Color.white : new Color(.72f, .78f, .9f);
        GUI.Label(rect, text, label);
        label.normal.textColor = old;
        return Event.current.type == EventType.Repaint && Pointer.Pressed && rect.Contains(Pointer.Pos);
    }
}
