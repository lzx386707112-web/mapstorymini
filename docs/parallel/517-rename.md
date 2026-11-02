# 517 宠物改名（现金 517xxxx）原确认窗与校验逆向与迁移

日期：2026-10-30
EXE 身份：`BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase 0x400000（本轮所有地址仅对此身份有效）。
服务端身份：`gms-server/src/main/java/org/gms/net/server/channel/handlers/UseCashItemHandler.java` SHA256 `ebd7efbe79c269524fba0b6cf5a60779ad33311d333081e6b2b5bfefe7dac34c`（与 skill 记录一致）。
实现文件：`Assets/Scripts/NativeRenameWindow.cs` + 同名 `.meta`（新建，未改任何已有文件）。
复现工具：`tools/inspect_beidou_client.py --fn <va>`、`imgdump.py`、字符串池 `/tmp/beidou-string-pool.json`、`docs/cash-client-routes.json`。

## 1. 入口与函数地址（递归下降实证）

普通现金双击分派链：`4EFD25` → `4EFEA7` → `4863D5`（非 0）→ `A0A63F`；USE_CASH 跳转表 **`0xA0EAD4`**，下标 = cashType-12（`0xA0A6D9: add eax,-0xC; cmp eax,0x39`）。**跳转表下标 5（cashType 17）→ `0xA0BA15`**（本轮直接扫表核实；`docs/cash-client-routes.json` 里 517 系唯一一件 `5170000` 的 cashType 即 17）。

| VA | 角色 | 关键证据 |
| --- | --- | --- |
| `0xA0BA15..0xA0BA50` | 取宠物 | `0x42861d`/`0x425d0b` 取上下文 → `0x46d2d5(esi, 0)` 得宠物对象 esi；`cmp esi,ebx(0); je 0xa0eaac` **取不到静默退出** |
| `0xA0BA56..0xA0BA84` | 「已有名字」判定 | `0x5cf63e(esi+0xC)` + `0x4283f8` → 当前名串 `[ebp+0x10]`；`0x5392d4([ebp+0x10], esi+0x28)`，非 0 = 已有名 |
| `0xA0BA86..0xA0BAB6` | 串 793 确认 | `0x79e805(0x319)` 取串 → `0x992BFD` YesNo；`cmp eax,6; je 0xa0babb` 答 6（Yes）才继续，否则清理退出 |
| `0xA0BABB..0xA0BB52` | 建输入框 | `0x402603(0x6E8)` new 对象 → `0x9A31AD` 初始化；`0x79E805(0x31A)` 正文串；`0x9AC552` 设文本；`0x428688(obj, 0, 4, 0xC, 0, 1)` → **`0x9AC7CB` CUtilDlgEx type3/A4=1，min=4、max=12**；`0x4EDBA1` 模态运行，返回 1 = 提交 |
| `0xA0BB5A..0xA0BB83` | 读回 + trim | `0x519E3A` 读编辑框文字 → `[ebp+8]`；`0x538A95/0x538A83`（trim/检最小长度，即 skill 记录的 `0x9AB50A` 族），失败跳 `0xa0bd0e` → 串 795 |
| `0xA0BB89..0xA0BBB4` | 过滤 | `0x416674(GetBuffer 0x400)` → **`0x7A06F3`**（`0xa0bb9a`）；返回 0 → 串 0x110=272「Cursing is not allowed.」（`0x9929DD` 显示） |
| `0xA0BBC6..0xA0BC21` | 两次判不等 | 串 796 前的 `0x5392d4`（`0xa0bc07`）与 `0x4cc68b`（`0xa0bc1a`，参数=[ebp-0x24]=esi+0x28）任一命中 → `0xa0bcea` 串 795「That name is not available.」 |
| `0xA0BC2F..0xA0BCA1` | 串 796 确认 | `0x445B4B(&out, 0x31C, name)` 格式化 → `0x992BFD` YesNo；非 6 退出 |
| `0xA0BCA3..0xA0BCE5` | 发请求 | `0x46F3CF` 复制名字 → `0xA0E9EC` → `0xA0A8F1`：`0x485BF7(player, 0x1F4=500)` 操作锁；`0xA0EA53` 构包（串 0x901=2305 **UseShopItem**、`0x41468C`、`0x9899B1` 100ms 延迟） |

## 2. 过滤链（`0x7A06F3` / `0x7A04C0` / `0x7A0540` / `0x7A0432` / 忽略字符表）

`0x7A06F3(name, maxlen, outCensored)`（`0x7a06f3..0x7a09a1`，236 条）：

1. 首次调用把 **`Etc/Curse.img`**（串 0x592=1426）词表装入全局 `0xBF0D1C`（计数在 `[-4]`），并为每词建模板 `0xBF0D18`（词内 `'*'`（串 0xAF21E4）替换为空串 0xAF1734）。实测 `Data/Etc/Curse.img` 共 **572 条** leetspeak 词，**无一含 `'*'`**，通配路径实际不触发。
2. `0x7A04C0(name, maxlen)` 校验（`0x7a04c0..0x7a053f`）：
   - `0xA61490(name)` 长度 **>= 0x3FF 即失败**（`0x7a04d3`）。
   - 逐字节：`<0x20` 的控制字节**仅允许 `\r`(0x0D) 与 `\n`(0x0A)**，其余失败（`0x7a04e3..0x7a04ed`）。
   - `0x7A03F0(name, buf, [0xBE223C], maxlen)` 复制并**剔除忽略字符表命中字节**（`0x7a0328` 逐字节查表）。
   - 对剔除后文本逐词 `0x7A038E(buf, word)` 子串匹配：逐字节过 `0x7A0302`（大小写不敏感），扫描推进用 IAT **`0xAF0064`（`_ismbblead` 系多字节前导字节检测）成对跳过双字节字符**——中文整字不会与 ASCII 词部分匹配。任一词命中 → 校验失败（返回 0）。
3. 校验失败 → `0x7A0540(name, maxlen)` 打码（`0x7a0540..0x7a06f2`）：同样先做控制字节/长度检查（失败返回 0 → `0x7A06F3` 返回 0 → 原端显示串 272）；然后在剔除后的文本里找词，**命中段重建为「前缀 + 词表词模板 + 后缀」并循环至无命中**（`0x7a0609..0x7a06bd`），最后 `0x7A0432(buf, name, [0xBE223C])` 把忽略字符还原进结果并写回调用方的缓冲。打码成功 `0x7A06F3` 置 `[ebp+0x10]=1`（`0x7a0983`）后**仍返回 1 接受**。
4. **忽略字符表**：`0xBE223C` 指向 `0xB3726C`，实测 17 字节：
   `" _-:\t^.,*/;!\'\"`+"`（空格、`_`、`-`、`:`、TAB、`^`、`.`、`,`、`*`、`/`、`;`、`!`、`\`、`'`、`"`、`` ` ``、`+`）。`*` 在表内，用户输入的星号先被剔除再匹配。

`0x7A06F3` 返回 0 的唯一实际路径：控制字节非法或超长（Curse 命中会被打码放行）。

## 3. min4 / max12 与文本框

- min=4 / max=12 来自 CUtilDlgEx 构造 push 对（`0xa0bb1e..0xa0bb24`：`push 4` / `push 0xC`），是**原生字节长度**（MBCS 字节），不是字符数——skill「紧凑文本框 CUtilDlgEx」归档（compact-input-controls/util-text 等）已确定：`0x9AB50A` 提交时 trim 两端并检最小长度，编辑控件检最大原生字节长度。
- 文本框几何（宽 266、编辑框 `19,height-73,226,15`、BtOK2 `158,height-30`、Cancel2 `208,height-30`、正文 `20,22`）已由 `NativeUtilPrompt.Text` 按原坐标实现，本轮直接复用，未新增绘制。
- 确认弹窗（串 793/796）走 `0x992BFD` = CUtilDlg YesNo，由 `NativeUtilPrompt.Confirm`（YesNo3 切片）承接。

## 4. 字符串池文案（原端此构建为英文）

| 串 id | 原文 | 用途 |
| --- | --- | --- |
| 793 | `The pet already has a name.\r\nWill you change it?` | 已有自定义名时的改名牌确认 |
| 794 | `Please name your pet.` | 输入框正文 |
| 795 | `That name is not available.` | trim 失败 / 与当前名或模板名重复 |
| 796 | `Do you want to name your pet\r\n'%s'?` | 最终确认（`%s` = 过滤后的名字） |
| 272 | `Cursing is not allowed.` | 过滤链拒绝（控制字节/超长） |

## 5. 服务端语义（UseCashItemHandler.java 408..425，itemType==517）

```
Pet pet = player.getPet(0);           // 领头宠物
if (pet == null) { c.enableActions(); return; }   // 不消耗
String newName = p.readString();      // 包里只有新名字（position/itemId 在 handler 头部已读）
pet.setName(newName); pet.saveToDb();
// forceUpdateItem(CASH 背包 pet.getPosition()) 刷新背包
// broadcastMessage(changePetName(player, newName, 1))
c.enableActions(); remove(c, position, itemId);   // 最后扣道具，无失败回滚
```

要点：**服务端不做任何长度/Curse 校验**（全在客户端过滤链）；无宠物时不消耗道具；改名卡与强制改名卡（5170000 之外无第二件 517 系，`Data/Item/Cash/0517.img` 只有 `05170000`，routes JSON 同）——不存在需要分开的第二个 case。

## 6. 实现说明（NativeRenameWindow.cs）

按原端流程复刻，全部弹窗复用 `NativeUtilPrompt`（`Text` = 已核实的 CUtilDlgEx 几何；`Confirm` = YesNo3），本类不自绘窗口：

1. `Open(id, position)`：`id/10000==517`；经 `GameSession.Net.ChangeInventory(getPet=true, petSlot=0)` 取**领头宠物**（`NativePetItems.GetPet` 槽序 = 服务端 `getPet(0)`）；取不到静默返回（不消耗）。
2. 模板名 = `Server.Bot.BotItemCatalog.Shared.Name(pet.itemId)`（与 `Dao.cs:195` 建宠物行、`NativePetItems.Evolve` 的默认名同源）；当前名 = `petData["name"]`。二者不等 → 串 793 确认。
3. 输入框：串 794；提交回调里 trim、**字节长度 4..12 校验**（ASCII 1 字节、其余 2 字节的 GBK 近似，见待核实）、过滤链（串 272 拒绝 / Curse 命中等长星号打码后放行）、与当前名及模板名判不等（串 795）、串 796 确认。
4. Curse 词表运行时从 `WzAssets.Node("Etc/Curse.img")` 枚举（原端同为首次使用时装载），不硬编码 572 条词。
5. `Apply`：`pets` 表该 petid 行写 `name` → `db.Save()` → 会话背包 `petData["name"]` 同步 → `ApplyInventoryToProfile` → `Profile.RemoveItem`（服务端顺序：先改名后扣道具）→ `Profile.Save()`。`changePetName` 广播无场景宠物视觉可承接（宠物跟随/名牌未迁移），略。

## 7. 接入补丁（不改文件，由用户合入）

### 7.1 `MapleDash/Assets/Scripts/NativeInventoryActions.cs`

锚点（第 242 行）：

```csharp
            if(itemId==5050000) {NativeResetWindow.Open(itemId,inventoryPosition);return null;}
```

在其后插入一行：

```csharp
            if(itemId/10000==517) {NativeRenameWindow.Open(itemId,inventoryPosition);return null;}
```

（5170000 不落入该行之前的任何分支：241 行 Vega 是 5610000/5610001，244 行 `itemId/1000==5000` 只接宠物本体 5000xxx，517 系此前落到末尾的现金装备/无操作兜底。）

### 7.2 `GameWindows.cs` —— **无需改动**

改名流程全部由 `NativeUtilPrompt` 呈现：`NativeUtilPrompt.Draw()` 已在绘制序列（第 795 行）、Esc 关闭已接（第 739 行）、弹窗打开期间其余 UI 已被 `GUI.enabled &= !NativeUtilPrompt.Visible`（第 760 行）禁用。`NativeRenameWindow` 无自绘内容，不注册 `Visible`/`Draw`。

## 8. 推断与待核实

- [ ] **中文编码**：原端编辑控件按 MBCS 字节计数（`_ismbblead` IAT 佐证），但此构建对应代码页（GBK/CP949）未从二进制确证。实现用「ASCII 1 字节、其余 2 字节」近似——对常用中文字符与 GBK 一致；Android IL2CPP 无 GBK 代码页（`JsValue.cs` 有 1252 同类坑），不能直接 `Encoding.GetEncoding(936)`。
- [ ] **输入框字符上限差异**：`NativeUtilPrompt`（禁止修改）按字符数限长，中文最多可键入 12 字（原端 12 字节只允许 6 字）；超宽在提交回调拒绝并重开输入框（带回原文），交互等价于原端「提交按钮无效」。`min` 传 1 是为放行 2~3 字中文名（4~6 字节在原端合法），实际下限由回调按字节执行。
- [ ] **`0x5392d4` / `0x4cc68b` 字符串语义**：按调用序与文案推定为「模板名≠当前名 → 793」「新名≠当前名且≠模板名，否则 795」，未逐条复核两个函数内部的比较方向。
- [ ] **模板名来源**：`BotItemCatalog.Shared.Name` 与建宠物行默认名同源（`Dao.cs:195`），但原端 `0x46d2d5`/`esi+0x28` 的确切对象字段（模板名 vs 当前名）未复核。
- [ ] **星号模板细节**：`0xBF0D18` 模板构建涉及 .NET/COM 调用（`0x474414/0x4744C9/0x428D30`），按「词表词、`'*'`→空串」与最终星号效果推定；词表无 `'*'`，实际无差异。
- [ ] **操作锁 `0x485BF7(player,500)`**（「Other requests are in process.」）：本工程无对应设施，未实现。
- [ ] **795 在 trim 失败时也会出现**（`0xa0bb83 jne 0xa0bd0e`）：本实现 trim 失败（空串）走字节下限重开输入框，未显式提示 795，与原端弹窗文案略有出入。
- [ ] Unity 编译与运行回归未执行（任务约束不运行 Unity/dotnet build）；合入后建议隔离工程跑 `unity-compile-check` + `InventoryPortalChecks.Run`（用例：无召唤宠物不消耗、2 字中文名可过、Curse 命中打星、与当前/模板名重复 795、改名后 `pets.name` 落库且道具扣 1）。
