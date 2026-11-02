using System.Collections.Generic;
using UnityEngine;

// 原端宠物改名/取名（现金 517xxxx）确认窗与校验，全部复刻 BeiDou.exe 的 CUtilDlg 弹窗流程。
// EXE 身份：SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2，ImageBase 0x400000。
//
// 入口：普通现金双击 4EFD25 → 4863D5 → A0A63F，USE_CASH 跳转表 0xA0EAD4[下标=cashType-12]，
// cashType17（5170000，cash-client-routes.json 唯一一件 517 系）→ 0xA0BA15。流程：
//   1. 0x46d2d5 取宠物（领头槽位，对应服务端 getPet(0)）；取不到静默退出（0xa0ba4e je 0xa0eaac）。
//   2. 0x5392d4(模板名, 当前名) 不等 → 弹串 793「The pet already has a name.\r\nWill you change it?」
//      （0x992BFD YesNo，答 6 才继续）；相等（未取名的新宠物）直接进输入框。
//   3. CUtilDlgEx type3/A4=1（0x9AC7CB）：正文串 794「Please name your pet.」，
//      构造参数 push 4 / push 0xC（0xa0bb1e..0xa0bb24）= min4 / max12 原生字节
//      （0x9AB50A 提交 trim 两端并检最小长度，编辑控件检最大原生字节长度，见 skill
//      compact-input-controls 归档）。几何由已核实的 NativeUtilPrompt.Text 承接。
//   4. 0x7A06F3(name) 过滤链（0xa0bb9a）：
//        0x7A04C0 校验——strlen<0x3FF；控制字节仅允许 \r\n（0x7a04e3..0x7a04ed）；
//        经 0x7A03F0 按忽略字符表 0xBE223C→0xB3726C 剔除后，逐词做大小写不敏感、
//        MBCS 前导字节感知（IAT 0xAF0064=_ismbblead 系）的子串匹配（0x7A038E），
//        词表来自 Etc/Curse.img（0x7a06f3 读串 1426 装入 0xbf0d1c，共 572 条，无 '*' 通配词）。
//        校验失败 → 0x7A0540 把命中段替换为等长星号（模板=词表词，'*'→空串），
//        0x7A0432 还原忽略字符；打码成功仍接受（0x7a0983 置标志），失败才拒绝。
//   5. 0x5392d4 / 0x4cc68b 两次不等判定（0xa0bc03/0xa0bc1a）：新名等于当前名或模板名
//      → 串 795「That name is not available.」。
//   6. 串 796「Do you want to name your pet\r\n'%s'?」确认（0x445B4B 格式化 + 0x992BFD）。
//   7. Yes → 0x485BF7 操作锁 500ms → 发 UseShopItem（串 0x901）。
// 服务端（UseCashItemHandler.java 408..425，SHA256 ebd7efbe…dac34c）：
//   getPet(0)==null → enableActions 不消耗；readString 后 setName→saveToDb→forceUpdateItem
//   →broadcast changePetName→enableActions→最后 remove。长度/Curse 校验全在客户端。
//
// 待核实（不得当作已解）：
//   - 「原生字节长度」的编码：原端按 MBCS 字节计数，本工程按「ASCII 1 字节、其余 2 字节」近似
//     （GBK 对常用中文字符即 2 字节；Android IL2CPP 无 GBK 代码页，JsValue.cs 有 1252 同类坑）。
//   - 输入框字符上限差异：NativeUtilPrompt（禁止修改）按字符数限长，中文最多可输入 12 字
//     （原端编辑控件 12 字节只允许 6 字），超宽在提交回调里拒绝并重开输入框。
//   - 0x5392d4/0x4cc68b 的「与模板名/当前名判不等」为汇编调用序推断，未逐条复核字符串语义。
//   - 操作锁 0x485BF7(500) 本工程无对应设施，未实现。
public static class NativeRenameWindow
{
    const int ItemFamily = 517;
    const int MinBytes = 4;   // 0xa0bb22 push 4
    const int MaxBytes = 12;  // 0xa0bb20 push 0xC
    // 0xBE223C → 0xB3726C 的忽略字符表（17 字节，NUL 结尾，实测 dump）。
    const string IgnoreChars = " _-:\t^.,*/;!\\'\"`+";

    // EXE 字符串池原文（此构建为英文；\r\n 在 Unity Label 里按 \n 渲染）。
    const string HasName = "The pet already has a name.\nWill you change it?";   // 串 793
    const string PleaseName = "Please name your pet.";                           // 串 794
    const string NotAvailable = "That name is not available.";                   // 串 795
    const string ConfirmName = "Do you want to name your pet\n'%s'?";            // 串 796
    const string Cursing = "Cursing is not allowed.";                            // 串 272

    static int itemId, itemPosition;
    static InventoryEntry pet;
    static string templateName, currentName;
    static readonly List<string> curseWords = new List<string>();

    public static void Open(int id, int inventoryPosition = 0)
    {
        if (id / 10000 != ItemFamily) return;
        // 领头宠物 = 已召唤宠物槽 0（NativePetItems.GetPet 的槽序 = 服务端 getPet(0)）。
        var response = GameSession.Net.ChangeInventory(new InventoryChangeRequest
        { characterId = GameSession.CharacterId, itemId = id, getPet = true, petSlot = 0 });
        if (response.pet == null) return;   // 原端 0x46d2d5 取不到静默退出；服务端同样不消耗
        pet = response.pet;
        itemId = id; itemPosition = inventoryPosition;
        // 模板名来源与 Dao.cs 建宠物行 / NativePetItems.Evolve 同源：BotItemCatalog 默认名。
        templateName = Server.Bot.BotItemCatalog.Shared.Name(pet.itemId) ?? "";
        currentName = pet.petData != null && pet.petData.TryGetValue("name", out var n)
            ? n?.ToString() ?? "" : "";
        if (currentName != templateName)    // 已有自定义名才询问（0x5392d4 判不等）
            NativeUtilPrompt.Confirm(HasName, ok => { if (ok) ShowInput(); });
        else ShowInput();
    }

    static void ShowInput() { ShowInput(""); }

    static void ShowInput(string value)
    {
        // 原端 min4/max12 按「原生字节」判定；NativeUtilPrompt 按 UTF-16 字符数判定且不可修改，
        // 这里传 min=1 放行短中文名（2 个汉字=4 字节在原端合法），字节长度在回调里自行校验。
        NativeUtilPrompt.Text(PleaseName, value, 1, MaxBytes, OnInput);
    }

    static void OnInput(string raw)
    {
        string name = raw != null ? raw.Trim() : "";   // 0x9AB50A：提交 trim 两端
        if (ByteLength(name) < MinBytes || ByteLength(name) > MaxBytes)
        { ShowInput(name); return; }                   // 原端编辑控件拒绝超宽输入且文本保留，此处重开并带回
        string filtered;
        if (!Filter(name, out filtered)) { BottomBar.Toast(Cursing); return; }   // 串 272
        if (filtered == currentName || filtered == templateName)
        { BottomBar.Toast(NotAvailable); return; }     // 串 795（0x5392d4 / 0x4cc68b）
        NativeUtilPrompt.Confirm(ConfirmName.Replace("%s", filtered), ok =>
        {
            if (ok) Apply(filtered);
        });
    }

    // 0x7A06F3 → 0x7A04C0 / 0x7A0540 / 0x7A0432 的等价实现；返回 false = 原端拒绝（串 272）。
    static bool Filter(string name, out string result)
    {
        result = name;
        if (name.Length >= 0x3FF) return false;        // 0x7a04d3：strlen<0x3FF
        foreach (char c in name)                       // 0x7a04e3：控制字节仅 \r\n
            if (c < 0x20 && c != '\r' && c != '\n') return false;
        LoadCurse();
        // 0x7A03F0：剔除忽略字符后匹配（0x7A038E 大小写不敏感）。原端按 MBCS 字节扫描、
        // 前导字节成对跳过；本工程按 UTF-16 字符比较，中文整字不会与 ASCII 词部分匹配，结果等价。
        var stripped = new System.Text.StringBuilder();
        var map = new List<int>();
        for (int i = 0; i < name.Length; i++)
            if (IgnoreChars.IndexOf(name[i]) < 0) { stripped.Append(char.ToLowerInvariant(name[i])); map.Add(i); }
        string text = stripped.ToString();
        var chars = name.ToCharArray();
        bool censored = false;
        foreach (string word in curseWords)
        {
            string w = word.ToLowerInvariant();
            if (w.Length == 0) continue;
            int at = 0;
            while ((at = text.IndexOf(w, at, System.StringComparison.Ordinal)) >= 0)
            {
                // 0x7A0540：命中段按词表词等长替换为 '*'；忽略字符留在原位（0x7A0432 还原语义）。
                for (int k = 0; k < w.Length && at + k < map.Count; k++) chars[map[at + k]] = '*';
                censored = true; at += w.Length;
            }
        }
        if (censored) result = new string(chars);
        return true;   // 打码成功仍接受（0x7a0983），此路径只有控制字节/超长才拒绝
    }

    // 原端首次使用时把 Etc/Curse.img 装入全局表（0x7a06f3 读串 1426）；词表 572 条，实测无 '*'。
    static void LoadCurse()
    {
        if (curseWords.Count > 0) return;
        var node = WzAssets.Node("Etc/Curse.img");
        if (node == null) return;
        foreach (var child in node.Children)
            if (child.Value is string word && word.Length > 0) curseWords.Add(word);
    }

    // 「原生字节长度」GBK 近似（待核实，见文件头）：ASCII 1 字节、其余 2 字节。
    // 代理对按 2 个 UTF-16 单元计 4 字节；移动端输入法无法产生此类字符，不影响实际使用。
    static int ByteLength(string s)
    {
        int bytes = 0;
        foreach (char c in s) bytes += c < 0x80 ? 1 : 2;
        return bytes;
    }

    // 服务端 408..425 的本机权威等价：setName→saveToDb→forceUpdateItem→…→最后 remove。
    // 原端 changePetName 广播无场景宠物视觉可承接（宠物跟随/名牌尚未迁移），略。
    static void Apply(string newName)
    {
        var db = GameSession.Server.Db;
        var row = db.Table("pets").Find(x => DbStore.Int(x, "petid") == pet.petId);
        if (row != null) row["name"] = newName;
        db.Save();
        // 本机等价 forceUpdateItem：刷新会话背包里的 petData 快照并落 Profile。
        var entry = GameSession.Character.inventory.Find(x => x.petId == pet.petId);
        if (entry != null && entry.petData != null) entry.petData["name"] = newName;
        GameSession.ApplyInventoryToProfile();
        Profile.RemoveItem(itemId, 1, itemPosition);
        Profile.Save();
        itemId = 0; itemPosition = 0; pet = null; templateName = currentName = null;
    }
}
