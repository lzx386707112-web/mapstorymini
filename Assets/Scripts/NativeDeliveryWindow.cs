using System;
using System.Collections.Generic;
using UnityEngine;

// 快递窗口。源端对应关系：
//   NPCTalkHandler：npc.getId() == NpcId.DUEY(9010009) → DueyProcessor.dueySendTalk(c, false)   → 收件 + 寄件
//   UseCashItemHandler：itemType == 533（快速投递券 533xxxx）→ dueySendTalk(c, true)           → 只有寄件
//   取件失败提示用客户端字符串 3870/3872/3876/3893/3896/3908
//
// 版式说明：UIWindow.img/Delivery 的底图存在（backgrnd 290x348、Tab、receiveTB、sendTB 等），
// 但客户端 EXE 的字符串池里没有 "UI/UIWindow.img/Delivery/..." 这类字面量（窗口路径由
// "UI/UIWindow.img/%s/backgrnd" 拼接），所以控件坐标取不到反汇编依据。这里只在底图空区内
// 重排控件；所有交互字段与源端请求一一对应，不含额外的本地规则。
public static class NativeDeliveryWindow
{
    const string Root = "UI/UIWindow.img/Delivery/";
    const float W = 290, H = 348;
    const int RowsVisible = 6;

    static bool visible, quickOnly;
    static int tab;                       // 0 收件 / 1 寄件
    static List<DueyPackageData> packages = new List<DueyPackageData>();
    static string recipient = "", mesosText = "", message = "";
    static int selType, selPos, selQty;
    static float recvScroll, sendScroll;
    static Vector2 position = new Vector2(250, 80), grab;
    static bool dragging;
    static int generation;
    static float notifyTimer = 3f;

    public static bool Visible => visible;

    public static void Open(bool quick)
    {
        if (!GameSession.InGame) return;
        Reset();
        quickOnly = quick;
        tab = quick ? 1 : 0;
        var res = Request(DueyOperation.Open);
        if (!res.ok) { BottomBar.Toast(res.error); Reset(); return; }
        packages = res.packages ?? new List<DueyPackageData>();
        visible = true;
    }

    public static void Close() => Reset();

    public static void Reset()
    {
        visible = false; quickOnly = false; tab = 0;
        packages = new List<DueyPackageData>();
        recipient = ""; mesosText = ""; message = "";
        selType = selPos = selQty = 0;
        recvScroll = sendScroll = 0;
        generation++;
    }

    static DueyResponse Request(DueyOperation op, int packageId = 0, int mesos = 0, bool quick = false)
    {
        if (!GameSession.InGame) return new DueyResponse { ok = false, error = "请先选择角色" };
        GameSession.SaveToDb();
        var res = GameSession.Net.Duey(new DueyRequest {
            characterId = GameSession.CharacterId, operation = op, packageId = packageId,
            inventoryType = selType, itemPosition = selPos, itemQuantity = selQty,
            mesos = mesos, recipient = recipient, message = message, quick = quick });
        bool touched = res.inventory != null || res.meso >= 0;
        if (res.inventory != null) { GameSession.Character.inventory = res.inventory; GameSession.ApplyInventoryToProfile(); }
        if (res.meso >= 0) { GameSession.Character.record.meso = res.meso; Profile.Data.gold = res.meso; }
        if (touched) Profile.Save();
        return res;
    }

    static void Refresh()
    {
        var res = Request(DueyOperation.Open);
        if (res.ok) packages = res.packages ?? new List<DueyPackageData>();
    }

    // ————————————————— 动作 —————————————————

    static void ClaimPackage(DueyPackageData data)
    {
        var res = Request(DueyOperation.Claim, data.packageId);
        if (!res.ok) { BottomBar.Toast(res.error); return; }
        packages = res.packages ?? new List<DueyPackageData>();
        BottomBar.Toast("已领取快递");
    }

    static void RemovePackage(DueyPackageData data)
    {
        int version = generation;
        NativeUtilPrompt.Confirm("要丢弃这个快递吗？\n里面的东西会一起消失。", yes =>
        {
            if (!yes || version != generation) return;
            var res = Request(DueyOperation.Remove, data.packageId);
            if (!res.ok) { BottomBar.Toast(res.error); return; }
            packages = res.packages ?? new List<DueyPackageData>();
        });
    }

    static void ConfirmSend(int quantity)
    {
        int mesos;
        if (!int.TryParse(mesosText, out mesos) || mesos < 0) mesos = 0;
        if (string.IsNullOrEmpty(recipient)) { BottomBar.Toast("请填写收件人"); return; }
        if (quantity <= 0 && mesos <= 0) { BottomBar.Toast("请选择物品或填写金币"); return; }
        selQty = quantity;
        int fee = NativeDueyService.GetFee(mesos) + (quickOnly ? 0 : 5000);
        int version = generation;
        string text = "收件人 " + recipient + "\n金额 " + mesos.ToString("N0") + " 金币\n服务费 " + fee.ToString("N0") + " 金币"
            + (quickOnly ? "\n并消耗 1 张快速投递券" : "") + "\n确定寄出吗？";
        NativeUtilPrompt.Confirm(text, yes =>
        {
            if (!yes || version != generation) return;
            var res = Request(DueyOperation.Send, 0, mesos, quickOnly);
            if (!res.ok) { BottomBar.Toast(res.error); return; }
            BottomBar.Toast("已寄出");
            message = ""; mesosText = ""; selType = selPos = selQty = 0;
        });
    }

    static void SendSelected()
    {
        if (selQty <= 0)
        {
            // 只寄金币
            int mesos;
            if (!int.TryParse(mesosText, out mesos) || mesos <= 0) { BottomBar.Toast("请选择物品或填写金币"); return; }
            ConfirmSend(0);
            return;
        }
        if (selQty <= 1) { ConfirmSend(1); return; }
        int max = selQty;
        NativeUtilPrompt.Number("要寄出多少个？", 1, 1, max, q => ConfirmSend(q));
    }

    // 到件通知：源端 showDueyNotification 只在角色在线时发一次（checked 清零）。
    public static void Tick()
    {
        if (!GameSession.InGame) return;
        notifyTimer -= Time.unscaledDeltaTime;
        if (notifyTimer > 0) return;
        notifyTimer = 10f;
        var res = GameSession.Net.Duey(new DueyRequest { characterId = GameSession.CharacterId, operation = DueyOperation.Notify });
        if (res.pending == null) return;
        BottomBar.Toast((res.pending.quick ? "快速投递到件" : "快递到件") + "：寄件人 " + res.pending.sender);
        if (visible) Refresh();
    }

    // ————————————————— 绘制 —————————————————

    static GUIStyle Label(bool right = false)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = 11, padding = new RectOffset(), clipping = TextClipping.Clip,
            alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft, wordWrap = false };
        style.normal.textColor = Color.black; return style;
    }

    static GUIStyle Field()
    {
        var style = new GUIStyle(GUI.skin.textField) { font = UiKit.Font, fontSize = 11, padding = new RectOffset(3, 3, 1, 1) };
        style.normal.textColor = Color.black; style.focused.textColor = Color.black; return style;
    }

    static GUIStyle Small()
    {
        var style = new GUIStyle(GUI.skin.button) { font = UiKit.Font, fontSize = 10, padding = new RectOffset(2, 2, 1, 1) };
        return style;
    }

    static void Image(string node, float x, float y)
    {
        var texture = Art.Tex("wz://" + node);
        if (texture != null) GUI.DrawTexture(new Rect(x, y, texture.width, texture.height), texture);
    }

    static void Icon(int itemId, float x, float foot)
    {
        var icon = NativeItemSource.IconNode(itemId); if (icon == null) return;
        var texture = WzAssets.Texture(icon); if (texture == null) return;
        var origin = WzAssets.Origin(icon);
        GUI.DrawTexture(new Rect(x - origin.x, foot - origin.y, texture.width, texture.height), texture);
    }

    static bool Hit(Rect rect, Vector2 pointer)
    {
        if (!GUI.enabled || Event.current.type != EventType.Repaint || !Pointer.Released || !rect.Contains(pointer)) return false;
        Pointer.ConsumeClick(); return true;
    }

    // Delivery/Tab：0 = 收件、1 = 寄件（2 号位在这版数据里没有对应标签，未使用）。
    static void Tabs(Vector2 p)
    {
        for (int i = 0; i < 2; i++)
        {
            var texture = Art.Tex("wz://" + Root + "Tab/" + (tab == i ? "disabled/" : "enabled/") + i);
            if (texture == null) continue;
            float x = 12 + i * 68;
            GUI.DrawTexture(new Rect(x, 22, texture.width, texture.height), texture);
            if (Hit(new Rect(x, 22, texture.width, texture.height), p)) tab = i;
        }
    }

    static List<InventoryEntry> Sendable()
    {
        var result = new List<InventoryEntry>();
        var character = GameSession.Character;
        if (character == null) return result;
        foreach (var item in character.inventory)
        {
            if (item.position <= 0 || item.inventoryType == 5 || item.inventoryType <= 0) continue;
            result.Add(item);
        }
        result.Sort((a, b) => a.inventoryType != b.inventoryType ? a.inventoryType.CompareTo(b.inventoryType) : a.position.CompareTo(b.position));
        return result;
    }

    static void ReceiveTab(Vector2 p, float top)
    {
        GUI.Label(new Rect(14, top, 200, 14), "共 " + packages.Count + " 个快递", Label());
        float y0 = top + 16;
        int max = Math.Max(0, packages.Count - RowsVisible);
        recvScroll = NativeUi.Scroll4(new Rect(W - 18, y0, 15, H - y0 - 16), new Rect(10, y0, W - 32, H - y0 - 16), recvScroll, max, 1, p);
        for (int row = 0; row < RowsVisible; row++)
        {
            int index = row + (int)recvScroll;
            if (index >= packages.Count) break;
            var data = packages[index];
            float y = y0 + row * 44;
            if (data.item != null) Icon(data.item.itemId, 22, y + 32);
            else { var meso = Art.Tex("wz://" + Root + "icon1"); if (meso != null) GUI.DrawTexture(new Rect(12, y + 14, meso.width, meso.height), meso); }
            GUI.Label(new Rect(40, y + 2, 150, 14), data.sender, Label());
            GUI.Label(new Rect(40, y + 17, 150, 14), (data.mesos > 0 ? data.mesos.ToString("N0") + " 金币" : "无金币")
                + (data.item != null ? "  ·  " + ItemCatalog.Name(data.item.itemId) + " x" + data.item.quantity : ""), Label());
            if (!string.IsNullOrEmpty(data.message)) GUI.Label(new Rect(40, y + 30, 170, 13), data.message, Label());
            if (GUI.Button(new Rect(200, y + 8, 44, 18), "领取", Small())) { ClaimPackage(data); return; }
            if (GUI.Button(new Rect(246, y + 8, 16, 18), "×", Small())) { RemovePackage(data); return; }
        }
    }

    static void SendTab(Vector2 p, float top)
    {
        var field = Field();
        GUI.Label(new Rect(14, top + 2, 50, 14), "收件人", Label());
        recipient = GUI.TextField(new Rect(64, top, 200, 18), recipient ?? "", 13, field);
        GUI.Label(new Rect(14, top + 26, 50, 14), "金币", Label());
        mesosText = GUI.TextField(new Rect(64, top + 24, 90, 18), mesosText ?? "", 10, field);
        GUI.Label(new Rect(14, top + 50, 50, 14), "留言", Label());
        message = GUI.TextField(new Rect(64, top + 48, 200, 18), message ?? "", 100, field);

        var items = Sendable();
        float listTop = top + 74;
        float listBottom = H - 40;
        GUI.Label(new Rect(14, listTop - 14, 200, 13), "选择要寄出的道具（不选则只寄金币）", Label());
        int slots = Math.Max(1, (int)((listBottom - listTop) / 20));
        sendScroll = NativeUi.Scroll4(new Rect(W - 18, listTop, 15, listBottom - listTop),
            new Rect(10, listTop, W - 32, listBottom - listTop), sendScroll, Math.Max(0, items.Count - slots), 1, p);
        for (int row = 0; row < slots; row++)
        {
            int index = row + (int)sendScroll;
            if (index >= items.Count) break;
            var item = items[index];
            float y = listTop + row * 20;
            bool selected = item.inventoryType == selType && item.position == selPos;
            if (selected) GUI.Box(new Rect(10, y, W - 32, 19), "");
            Icon(item.itemId, 22, y + 16);
            GUI.Label(new Rect(40, y + 2, 150, 14), ItemCatalog.Name(item.itemId) + (item.quantity > 1 ? " x" + item.quantity : ""), Label());
            if (Hit(new Rect(10, y, W - 32, 19), p))
            {
                if (selected) { selType = selPos = selQty = 0; }
                else { selType = item.inventoryType; selPos = item.position; selQty = item.quantity; }
            }
        }
        if (GUI.Button(new Rect(W - 76, H - 32, 62, 20), "寄出", Small())) SendSelected();
    }

    public static void Draw()
    {
        if (!visible) return;
        if (!GameSession.InGame) { Reset(); return; }
        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position, new Vector2(W, H), scale);
        NativeUi.RegisterWindow("NativeDeliveryWindow", new Rect(position.x * scale, position.y * scale, W * scale, H * scale), () => Visible);
        var pointer = Pointer.Pos / scale; var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x, position.y, W, 16).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, new Vector2(W, H), scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, W, H));
            try
            {
                var p = pointer - position;
                Image(Root + "backgrnd", 0, 0);
                GUI.Label(new Rect(12, 4, 200, 14), quickOnly ? "快速投递" : "快递", Label());
                if (NativeUi.Button(new Rect(W - 22, 4, 16, 16), "UI/UIWindow.img/BtUIClose2", p)) { Close(); return; }
                if (!quickOnly) Tabs(p);
                float top = 42;
                if (tab == 0 && !quickOnly) ReceiveTab(p, top); else SendTab(p, top);
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }
}
