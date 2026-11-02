# 537 黑板 / 广告牌（现金 5370000/5370001）原端布局与迁移

日期：2026-10-30
EXE 身份：`BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase 0x400000（本轮所有地址仅对此身份有效）。
服务端身份：`gms-server/src/main/java/org/gms/net/server/channel/handlers/UseCashItemHandler.java`（与 skill 记录一致）。
实现文件：`Assets/Scripts/NativeAdBoard.cs` + 同名 `.meta`（新建，未改任何已有文件）。
复现工具：`tools/inspect_beidou_client.py --fn <va>`、`imgdump.py`、字符串池 `/tmp/beidou-string-pool.json`、
本轮完整布局解包 `docs/skill-regression/adboard-layout-full-2026-10-30.txt`（0x4890C4 全汇编）与
`docs/skill-regression/adboard-balloon-2026-10-30.txt`（0x48A935 type 分派）。

## 1. 入口与函数地址（递归下降实证）

USE_CASH 跳转表 **`0xA0EAD4`**，下标 = cashType-12（与 504/517 同表）；**下标 20（cashType 32）→ `0xA0CE2E`**。

| VA | 角色 | 关键证据 |
| --- | --- | --- |
| `0xA0CE2E..0xA0CE6x` | 忙检查 | `0xA14A1C` 非 0 → 直接返回 |
| `0xA0ECA4` | 地图限制 | 读 `[ctx+0x160]`，非 0 → 串 0x114「You can't use it here in this map.」，**不弹输入框**；串池 id 0x114=276 |
| `0xA0CE8x..` | 建输入框 | CUtilDlgEx **type3**，正文串 0x56F=1119「Enter a note.」，`0x9AC7CB(this, 0, 0x28, 0, 0)` → **min=0、max=40**（0x28） |
| `0x7A06F3` | 脏话过滤 | 与改名卡同一过滤器（详见 517-rename.md 第 2 节）；返回 0 → 串 0x110=272「Cursing is not allowed.」；**过滤后为空同样报 272** |
| `0xA0EA53` | 发请求 | USE_CASH_ITEM 构包，携带过滤后的文本 |

气泡分派：`0x93760D` → `0x48E298`（建气泡对象）→ `0x48A935` 按 type 分派 → **1000..1005 共用 `0x4890C4`**；
type 1000=普通、1001='npc'、1002='pet'、**1003='adboard'**、1004='dead'、1005=maxCols=5 的短气泡。
style 参数经 `0xA611BF` 十进制转字符串（物品子索引），**黑板只有 0 一套** → 资源 `UI/ChatBalloon.img/adboard/0/`。

## 2. 九宫格 canvas（imgdump 实测）

| 节点 | 尺寸 | origin | 说明 |
| --- | --- | --- | --- |
| nw | 18x19 | (18,19) | 左上角 |
| ne | 18x19 | (0,19) | 右上角 |
| sw | 18x19 | (18,0) | 左下角 |
| se | 18x19 | (0,0) | 右下角 |
| n | 14x19 | (0,19) | 上边带 |
| s | 14x19 | (0,0) | 下边带 |
| w | 18x14 | (18,0) | 左边带 |
| e | 18x14 | (0,0) | 右边带 |
| c | 14x14 | (0,0) | 中央平铺 |
| arrow | 14x23 | (0,0) | 尾尖（比下边框多出 4px 下探） |
| clr | -1 | — | 文字颜色：白（普通气泡为 -16777216 黑） |

四个度量虚函数经九宫格拼合约束唯一反解：**`0x40B920`=宽(vtbl+0x40)、`0x40B947`=高(vtbl+0x48)、
`0x40F09B`=origin.x(vtbl+0x6C)、`0x40F0C2`=origin.y(vtbl+0x74)**（初判全当宽高会导致 topH/bottomM 不对称，
后被 `0x4898CD` 处槽位重赋值 `[ebp-0x10]=ne / [ebp-0x20]=se / [ebp-0x14]=nw` 证伪后重解）。

## 3. 布局公式（0x489D44..0x489D90，type==1003 分支）

```
leftW   = max(origin.x(nw), origin.x(sw), origin.x(w))          = 18
rightW  = max(width(se)-origin.x(se), …)                        = 18
cell    = width(n)                                              = 14   （列宽）
rowH    = height(w)                                             = 14   （行高）
topH    = max(origin.y(ne), origin.y(nw), origin.y(n))          = 19
bottomM = max(…, height(arrow)=23, height(s)=19, …)             = 23   （含 arrow 下探 4px）
maxCols = 14（0x489937..0x489952；type 1005 → 5）
```

- **单行**（0x489CC2 rows==1 分支）：`cols = ceil(文本像素宽/14) | 1` 强制奇数（0x489D08..0x489D34），`innerW = cols*14`。
- **多行**：`cols = maxCols = 14`，`innerW = 196`。
- 总宽 `= innerW + leftW + rightW`；总高 `= topH + rowH*行数 + bottomM`。
- 锚点（0x489D6C..0x489D90，存 this+0x2c/0x30）：`x = 3 − innerW/2 − leftW`（以角色为中心右移 3px）、
  `y = −(rowH*行数 + bottomM)`。
- 绘制顺序：角四块 → 四边带 → 中央平铺；**arrow 最后画在底边中央列**（0x489EC9..0x489ED4 分支，
  单行强制奇数使 cols/2 恰为正中列；多行 cols=14 时 arrow 偏 7px 属原端行为，忠实保留）。
  arrow 上 19 行与 s 完全相同（像素逐行对比核实），整体压 s 之上、尾尖超出下边框 4px。
- 文字：白色（clr=-1），画在 inner 区 `(leftW, topH, innerW, 行数*14)` 内，逐行、行高 14。

## 4. 40 字上限与换行规则

- **max=40**：`0x9AC7CB(this, 0, 0x28, 0, 0)` 的 0x28。按 CUtilDlgEx 惯例（517 文档第 3 节同款）是原生
  字节长度语义；本项目 `NativeUtilPrompt.Text(prompt, value, 0, 40, …)` 的 max 即 TextField 字符上限，
  中文按字符计数（差异见第 8 节待核实）。
- **换行**（0x489AC8 剥行循环）：`0x48ED5A` 量出 14 格（196px）内放得下的字符数 → `0x489B66` 从该处
  向前找空格/换行（字符类 `0xAF2464`）断行、分隔符丢弃 → 找不到则硬断（0x489B9C）。
- 提交时 `0x9AB50A` trim 两端（skill 已归档，与 517 同一族）。

## 5. 服务端语义（源端为准，全部从 Java 源码核实）

```
UseCashItemHandler itemType==537（492 行起）：
  GameConstants.isFreeMarketRoom(mapId)（map/1000000==910 && map>910000000，FM_ENTRANCE=910000000）→ 拒绝；
  player.setChalkboard(text) + 广播 useChalkboard(chr, false) + enableActions；
  remove() 被注释 —— 使用不消耗道具。
CloseChalkboardHandler（Recv CLOSE_CHALKBOARD）：setChalkboard(null) + 广播收起。
MapleMap 换图：进 FM 房间清空，否则重新广播（黑板跨图保留）。
InventoryManipulator 932：黑板道具数量归零时清空黑板。
ItemId：CHALKBOARD_1=5370000 / CHALKBOARD_2=5370001；i18n message6 zh =「您无法在这里使用黑板。」
```

本项目内嵌服务端已有同语义通道：`Server/World/NativeCommunityService.cs` 的
`CommunityOperation.SetBoard`（231 行：FM 房间 910000001..910000022 拒绝、须持有 537、255 上限、不消耗）/
`CloseBoard`（237 行）；响应经 `Handle`（99..111 行）总是走 `Read(...)` 全量回带，**SetBoard 成功后
`response.board` 即新文本**（318 行 `boards.TryGetValue → result.board`）。客户端显示数据取自该回带值，
与源端 `chr.chalktext` 同语义。

## 6. 实现（Assets/Scripts/NativeAdBoard.cs）

- 输入：`Open(itemId)`（537 检查 → FM 客户端预检弹 Toast → `NativeUtilPrompt.Text(Prompt, boardText, 0, 40, Submit)`）。
  Prompt 用意译「请输入留言内容。」（原端英文串 0x56F，zh 串表未随字符串池导出）。
- 提交：`Submit`（trim → 空串不入库 → `NativeCommunityWindow.InputText = text` →
  `Request(CommunityOperation.SetBoard)` → `boardText = response.board`）。失败由 Request 内部 Toast。
- 收起：`Clear()` → `Request(CommunityOperation.CloseBoard)`。
- 显示：`Draw()` 按 BotField 气泡同一套约定 —— 768 高虚拟屏缩放（`UiKit.H/768`）、
  尾尖贴 `SpriteRenderer.bounds.max.y`（站立帧包围盒顶）、`UiKit.Begin()` + GUI.matrix 缩放；
  `Wrap()` 复刻原端换行（像素计量 + 向前找空格/换行 + 硬断兜底）；
  `Body()` 按九宫格合成一张整图（同 BotField：分块画在非整数缩放下出 1px 缝），arrow 最后压底边中央列；
  文字白色、UpperLeft、行高 14 逐行画进 inner 区。
- 离线合成验证：PIL 按 1x 合成 246x56（cols=15）整图，四角圆角边框连续、板面平整、尾尖居中超出下边框 4px，视觉核对通过。

## 7. 接入补丁（未动任何已有文件，接入时按锚点行号加两处）

1. `Assets/Scripts/NativeInventoryActions.cs:242`：
   ```csharp
   if(itemId==5050000) {NativeResetWindow.Open(itemId,inventoryPosition);return null;}
   ```
   在该行之后插入：
   ```csharp
   if(itemId/10000==537) {NativeAdBoard.Open(itemId);return null;}   // 黑板：现金分派 cashType 32 → 0xA0CE2E
   ```
2. `Assets/Scripts/GameWindows.cs:774`：
   ```csharp
   NativeCommunityWindow.DrawBoard();
   ```
   在该行之后新增一行（或改为直接替换；旧占位框与新黑板同画会重叠）：
   ```csharp
   NativeAdBoard.Draw();   // 黑板：原端 0x48A935 type1003 → 0x4890C4 气泡布局
   ```
   输入框无需额外接入：`GameWindows.cs:795` 已有 `NativeUtilPrompt.Draw();`。
   换图清理可选：在换图回调处（服务端 Read 回带 board 为空时）`boardText` 自动为空串即不显示，无需改动。

## 8. 待核实清单

| 项 | 状态 |
| --- | --- |
| 锚点与角色头顶的精确对应 | 待核实（推断）：原端 `x=3−innerW/2−leftW, y=−(rowH*行数+bottomM)` 的绘制原点是角色哪个像素，本项目沿用 BotField 已验证的「尾尖贴站立帧包围盒顶」约定 |
| 文字水平对齐 | 待核实（推断）：原端气泡文字绘制函数未逐条反编译，格宽余量 <14px，按 UpperLeft 实现 |
| 40 字按字节还是按字符 | 待核实（推断）：0x28 在 CUtilDlgEx 是原生字节长度语义（MBCS），中文约 20 字；本项目 NativeUtilPrompt 按字符数，放宽了一倍 |
| 脏话过滤 0x7A06F3 | 未迁移：与改名卡同一过滤器，本项目两条链路都未接；空串报 272 的分支简化为「不入库」 |
| prompt 文案 | 意译：串 0x56F 原文「Enter a note.」，zh 串表未随 EXE 字符串池导出 |
| 多行 cols=14 时 arrow 偏 7px | 原端行为（cols 非奇数时 cols/2 非正中），忠实保留 |
| 服务端广播显示他人黑板 | 未实现：内嵌服务端 SetBoard 只回带自己的 board；源端 useChalkboard 广播他人文本的通道本项目未接，待后续任务 |
