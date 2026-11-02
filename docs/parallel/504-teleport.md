# 504 传送石（缩地功能 / Map Transfer）窗口逆向与迁移

日期：2026-10-30
EXE 身份：`BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase 0x400000（本轮所有地址仅对此身份有效）。
实现文件：`Assets/Scripts/NativeTeleportWindow.cs`（新建，未改任何已有文件）。
复现工具：`MapleDash/tools/inspect_beidou_client.py --fn <va>`（递归下降）、`imgdump.py` / `imgpng.py`。

## 1. 逆向确认的函数地址与指令依据

现金双击分派链（背包双击）：`4EFD25` → `4EFE04` → `48645B`（cashType）→ cashType22 → **`0xA0CAB0`**（跳转表 `0xA0EAD4`，下标 = cashType-12，`0xA0A6D9: add eax,-0xC; cmp eax,0x39`）。跳转表目标是大函数 `0xA0A6xx..0xA0EAD1` 的内联标号，AP（`0xA0C427`）/SP（`0xA0C6DD`）同属这一函数，与 skill 里既有记录一致。

| VA | 角色 | 关键证据 |
| --- | --- | --- |
| `0xA0CAB0..0xA0CADB` | cashType22 入口 | `idiv ecx(1000); cmp eax,0x13B0(5040); setne cl` → **isVIP = (itemid/1000 != 5040)**；`call 0xA0A4AA` |
| `0xA0A4AA..0xA0A63C` | 模态对话框运行器 | 死亡检查（串 0x18D=397）；`[field+0x144]` 位 6 / 位 1 检查（串 0xB86=2950）；`0x403065(0xBF0B00)` 建对象；`0x8390D1(obj, isVIP)` 初始化；`0x4EDBA1` 返回 1 = 确认；确认后 `0x485BF7(this,500)` 操作锁，失败串 297「Other requests are in process.」，成功走 `0xA0EA53..0xA0EA9F` 发请求（'UseShopItem' 节点 + 100ms 延迟） |
| `0x8390D1` | 对话框构造 | vtable `0xB39900/0xB398B4/0xB398B0`；串 0xB80=2944（Teleport/backgrnd）→ `0x4EDAEF` 按 (800-w)/2,(600-h)/2 居中；isVIP 存 `[this+0xF0]` |
| `0x8392C1..0x8398D2` | OnCreate | 见 §3 控件表；末尾从角色数据拷贝传送列表：**普通 5 项 `[char+0x563]`、VIP 10 项 `[char+0x577]`**，`neg/sbb/and 5; add 5` 定容量，空槽哨兵 `0x3B9AC9FF`(999999999)，有效数存 `[this+0xB4]` |
| `0x8398D5..0x839D51` | Draw | 计数文字 `0x414576(target, 165-文本宽, 38)`，格式串 0x822? 实为 0x1597?——见下；行循环 `add [ebp-0x10],0x12`（行距 18），行名 `0x4284A8(canvas, 0xF, y)` + `0x427138` 绘制；选中行画 select（x=9、y=0x54+18k） |
| `0x839D82` | OnButton | `id-0x7D0`: 0→`0x83A084`(登记)、1→`0x83A2A0`(删除)；`id==1`→vtable+0x34=`0x83A3C2`(移动/确认)、`id==2`→`0x83A3C2`(关闭) |
| `0x83A084` | 登记 | 满→串 2953；当前图重复→2954；`mapid/1e8==0` 或 `(mapid/1e6)%100==9` 无效→2952；确认串 0xB8D=2957；Yes→`0xA261BC(player, 1, 0, isVIP)`（无槽位参数，服务端取第一个空槽） |
| `0x83A2A0` / `0x83A392` | 删除 | 需选中（`[esi+0xB0]<0` 直接返回）；确认串 0xB8E=2958；Yes→`0xA261BC(player, 0, mapid, isVIP)`，`or [esi+0xB0],-1` 清选中 |
| `0x83A3C2` | 移动/关闭 | arg==1：选中≥0 → 当前图检查（`0x538E56` vs 槽值，等→2955）→ 确认串 0xB8F=2959 → Yes；选中<0 → 编辑框文字 `0x79FC74`(atoi)==0 → 串 0x327=807「The character name is invalid.」；`0xA6C440(player,name)!=0` → 2951「You cannot target yourself...」 |
| `0x83A668` | 结果读回 | 编辑框文字写入请求名字字段；选中≥0 → mapId，否则 999999999。**名字非空时优先于选中行** |
| `0xA261BC` | 客户端请求 | (player, op, mapId, isVIP)：op=1 登记、op=0 删除 → 服务端 `TrockAddMapHandler`（type 0x00 删 / 0x01 登记，vip byte，type 0 时多一个 int mapId） |

注：Draw 里计数文字格式串为 0x822(2082) `[%d/%d]`（`0x445B4B(&out, fmt, [this+0xB4], 5|10)` 格式化后经 `0x414576(target, 165-宽, 38)` 定位绘制）。OnCreate 里 `0x1597`(5527)='Arial' 是名字编辑框的字体样板（`0x414617` 字体结构，字号 12、颜色 0xFF000000），与计数文字无关。

## 2. 资源节点与像素尺寸（imgdump 实测）

均在 `UI/UIWindow.img/Teleport/`（字符串池 2944..2948），origin 全部 (0,0)：

| 节点 | 尺寸 | 用途 |
| --- | --- | --- |
| `backgrnd` | 175x309 | 窗口底图：标题「缩地功能」、上半「地图目录」列表区、下半「角色移动」+「请你输入对方的角色名」+ 白底输入框、底部按钮槽 |
| `select` | 158x17 | 选中行蓝色高亮条 |
| `BtRegister/normal|pressed|disabled|mouseOver/0` | 47x18 | 「增加」 |
| `BtDelete/...` | 47x18 | 「删除」 |
| `BtMove/...` | 47x18 | 「移动」 |
| `UI/Basic.img/BtClose`（串 5577） | 12x12 | 右上关闭 |

窗口尺寸 175x309，共享例程居中于 (312, 145)（800x600 口径）。

## 3. 控件坐标表（OnCreate `0x8392C1` push 常量对，约定 `(this, id, x, y, ?, &out)`）

| 控件 | id | 坐标 (x,y) | 尺寸 | 依据 |
| --- | --- | --- | --- | --- |
| BtRegister（增加） | 2000 | (6, 285) | 47x18 | `push 6; push 0x7D0` |
| BtDelete（删除） | 2001 | (56, 285) | 47x18 | `push 0x38; push 0x7D1` |
| BtMove（移动） | 1 | (120, 285) | 47x18 | `push 0x78; push 1` |
| 关闭 BtClose | 2 | (156, 6) | 12x12 | `push 0x9C; push 2`，串 5577 |
| 名字编辑框 | 1000 | (34, 250) | 120x13 | `push 0x22; push 0xFA; push 0x78; push 0xD`，字号 12（style 0xC） |
| VIP 滚动条 | 3000 | (155, 80) | 高 98 | `push 0x9B(155); push 0x50(80); push 0x62(98)`，`[+0x34]=0xA5(165)`，`0x4D7DAC(obj,6)` |

列表绘制（Draw `0x8398D5` 常量）：

| 项 | 值 |
| --- | --- |
| 可见行数 | 5（`edi..edi+5` 循环） |
| 行距 | 18（`add [ebp-0x10], 0x12`） |
| 行名文字 | (15, 87+18k)，左对齐 |
| 选中高亮 select | (9, 84+18k)，158x17 |
| 计数文字 `[%d/%d]` | 右缘对齐 x=165，y=38（`0x414576(target, 0xA5-宽, 0x26)`） |
| 容量 | 普通 5（角色 +0x563）、VIP 10（+0x577），空槽 999999999 |

tooltip（`0x4C10F7`，本实现未接）：BtRegister → 2960/2961（VIP 用 RegisterEx）、BtDelete → 2962、BtMove → 2963，均为 `String/ToolTipHelp.img/Game/Button/MapTransfer/*`。

## 4. 原端校验规则汇总

客户端（打开时，`0xA0A4AA`）：
1. 死亡状态 → 串 397「You can't do it while you're dead.」（EXE 英文原文；本工程按 `player.hp<=0` 判定）。
2. 当前图 `[field+0x144]` 位 6（0x40=CANNOTVIPROCK）或位 1（0x2）置位 → 串 2950「You cannot teleport to this map.」。

登记（`0x83A084`，客户端侧）：满(2953) → 当前图已在列表(2954) → `mapid/1e8==0`（彩虹岛 6 位小图）或 `(mapid/1e6)%100==9`（x09 段活动图，如 109040000）无效(2952) → 确认串 2957；服务端 `TrockAddMapHandler type=1` 还要求当前图无 CANNOTVIPROCK，否则「You may not save this map.」。

传送（服务端 `UseCashItemHandler.java` 132..171，504 分支）：
- 包序：`readByte(vip)` + (!vip ? `readInt(mapId)` : `readString(name)`)。
- **先 `remove(c, position, itemId)` 扣道具**，失败才 `InventoryManipulator.addById(c, itemId, 1)` 退款（净效果：失败不扣）。
- 普通石（vip=false）：要求 `itemId/1000 >= 5041` 或 `mapId/1e8 == 当前图/1e8`（**同大陆限制**），否则 message1「您无法通过这个传送石在大陆之间传送。」。
- 目标图：`!FieldLimit.CANNOTVIPROCK(0x40)` 且 (`forcedReturnId == MapId.NONE(999999999)` 或 `MapId.isMapleIsland(mapId)`（范围 [0, 2000001]）），否则 error1「找不到玩家或者您试图传送到一个非法的位置。」。
- VIP 按玩家名（**玩家名校验在服务端**）：`getCharacterByName`，找不到 → message3「在这个频道中找不到玩家。」；目标图同上校验 → message2「您无法传送到这个地图。」；GM 保护 `!victim.isGM() || victim.gmLevel() <= player.gmLevel()`，违规按 error1 处理。
- 成功：`forceChangeMap(targetMap, targetMap.getRandomPlayerSpawnpoint() / findClosestPlayerSpawnpoint(victim.getPosition()))`。

客户端名字路径前置（`0x83A3C2`）：空/atoi==0 → 807「- The character name is invalid.」；`0xA6C440(player, name)` 命中 → 2951「You cannot target yourself for the teleport.」（该函数精确语义未逆向，见待核实）。

## 5. 实现说明与推断项

已按原端实现：窗口布局/控件坐标、列表 5 行 18px 行距、`[%d/%d]` 计数右对齐 (165,38)、select 高亮 (9,84+18k)、三按钮 + BtClose + 名字编辑框 (34,250,120,13)、普通 5 / VIP 10 槽、空槽 999999999、登记/删除/移动的确认弹窗文案与顺序（`NativeUtilPrompt.Confirm` 复刻原端 UtilDlgEx YesNo）、服务端全套校验（同大陆 / CANNOTVIPROCK / forcedReturn / 蘑菇岛 / 玩家名 / GM 保护 / 失败不扣道具）、打开前置检查（死亡 / 0x40 / 0x2）。

推断项（代码注释已标注）：
1. **VIP 滚动条**：原端控件类 `0x4D6CD3` 未逆向，暂用工程内 `NativeUi.Scroll4`（VScr4 通用件，聊天窗/任务窗同款）放 (155,80) 宽 15 高 98。
2. **行点击选中**：原端鼠标路径（写 `[esi+0xB0]` 的具体处理函数）未逆向，按 `0x83A668` 读回语义复现为「点击行即选中」，命中区取 select 图 (9,84+18k,158,17)。
3. **编辑框最大长度 12**：原端编辑控件 max length 未解出（对照建号名长规则暂取 12）。
4. **`0xA6C440`**（2951 的触发条件）未逆向；实现为「名字 == 自己」拒绝。
5. **计数文字 y=38**：`0x414576(target, 165-宽, 38)` 的第二参数按 stdcall 序读作 y，未逐位复核。
6. **文字字体**：原端用字体表 0/1/34（`0x98A707` 键），实现统一用 `UiKit.Font` 字号 12，视觉以实拍核对为准。
7. **地图名查询**：复用工程内 `NativeMapNames.Of`（遍历 String/Map.img 大陆分组），原端 `0x5CF792` 的精确分区函数未解。

## 6. 待核实清单

- [ ] VIP 滚动条控件 `0x4D6CD3` 的绘制与几何（顺带确认 x=155/80/98 常量语义）。
- [ ] `0xA6C440` 的完整语义（是「查到自己」还是「查到同图玩家」）。
- [ ] `0x4CC512(edit, 0xAF1734)` 是否清空编辑框：决定「选中行 + 编辑框残留名字」时原端实际按地图还是按名字传送（实现取选中行优先）。
- [ ] 原端行点击/滚动条鼠标处理（`0x9E04C9` → `0x9E44FE` 通用路径）与 `select` 的实际交互细节。
- [ ] 编辑框最大长度与非法字符过滤（原端 `0x9AB50A` 系列规则）。
- [ ] tooltip（2960..2963）未实现。
- [ ] Android 实机视觉验收（本实现未运行 Unity，坐标全部来自反汇编常量与 IMG 实测像素）。
- [ ] EXE 文案 2949..2959 为英文（源端此构建为英文）；若需统一中文，需用户决定是否采用服务端 i18n 文案（本实现采用）。

## 7. 持久化 schema

原端传送列表是**角色字段**：普通 5 项（角色对象 +0x563）、VIP 10 项（+0x577），int 数组，空槽 999999999（源端 `Character.trocks` / `vipTrocks`）。

当前实现（不改已有文件）：借 `PlayerProfileData.questProgress` 通用 KV 存 `questId=0, key="trock"/"trockVip"`，值为逗号分隔 mapId（未填槽以 999999999 表示）。这是临时桥接，等价于服务端 `Character.addTrockMap/deleteFromTrocks` 的本机权威化。

后续若要落真实列（由用户合入）：

```sql
-- Schema.sql 追加（V1.x 增量迁移）
ALTER TABLE characters ADD COLUMN trocks     TEXT NOT NULL DEFAULT '999999999,999999999,999999999,999999999,999999999';
ALTER TABLE characters ADD COLUMN vipTrocks  TEXT NOT NULL DEFAULT '999999999,999999999,999999999,999999999,999999999,999999999,999999999,999999999,999999999,999999999';
```

```csharp
// CharactersDao.cs 追加（FromRow/Save 各一行）
c.trocks    = S(r, "trocks");
c.vipTrocks = S(r, "vipTrocks");
// CharRecord 增加两个 string 字段；NativeTeleportWindow.LoadSlots/SaveSlots 改读写
// GameSession.Character.record.trocks / vipTrocks 并经 SaveCharacter 持久化。
```

## 8. 接入补丁（不改文件，由用户合入）

### 8.1 `MapleDash/Assets/Scripts/NativeInventoryActions.cs`

锚点（第 242 行）：

```csharp
            if(itemId==5050000) {NativeResetWindow.Open(itemId,inventoryPosition);return null;}
```

在其后插入一行：

```csharp
            if(itemId/10000==504) {NativeTeleportWindow.Open(itemId,inventoryPosition);return null;}
```

### 8.2 `MapleDash/Assets/Scripts/GameWindows.cs`

锚点 A（第 652 行，`AnyModalVisible`/面板汇总表达式尾部）：

```csharp
        NativeVegaWindow.Visible || NativeResetWindow.Visible || NativeTradeWindow.Visible || NativeUtilPrompt.Visible || NativeCharacterMenu.Visible || NativeMarketWindow.Visible || NativeNpcShopWindow.Visible;
```

把其中 `NativeResetWindow.Visible ||` 改为 `NativeResetWindow.Visible || NativeTeleportWindow.Visible ||`：

```csharp
        NativeVegaWindow.Visible || NativeResetWindow.Visible || NativeTeleportWindow.Visible || NativeTradeWindow.Visible || NativeUtilPrompt.Visible || NativeCharacterMenu.Visible || NativeMarketWindow.Visible || NativeNpcShopWindow.Visible;
```

锚点 B（第 741 行，`CloseTop()` 内）：

```csharp
        if (NativeResetWindow.Visible) { NativeResetWindow.Close(); return true; }
```

在其后插入：

```csharp
        if (NativeTeleportWindow.Visible) { NativeTeleportWindow.Close(); return true; }
```

锚点 C（第 791 行，绘制序列）：

```csharp
        NativeResetWindow.Draw();
```

在其后插入：

```csharp
        NativeTeleportWindow.Draw();
```

## 9. 验证状态

- 未运行 Unity / dotnet build（按任务约束）；代码仅做签名静态核对（`NativeUi.Button/Scroll4/ClampWindowPosition`、`Art.Tex`、`Pointer.*`、`Profile.*`、`UiKit.Fill/Font/W/H`、`GameSession.Server.Db/Character`、`FieldCatalog.GetMap`、`NativeMapNames.Of`、`NativeUtilPrompt.Confirm`、`CharactersDao.FromRow`、`DbStore.Eq/Table` 均按当前源码签名核对）。
- 无运行回归；合入后建议在隔离工程跑一次 `unity-compile-check` + `InventoryPortalChecks.Run`（504 用例：普通石同图拒绝 / 跨大陆拒绝 / VIP 名字找不到 / 失败不扣道具 / 登记-删除-移动闭环）。
