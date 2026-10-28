# 聊天窗与系统消息：包结构、类型表、落点与配色

核实快照：2026-10-27。二进制身份见 [binary-and-tools.md](binary-and-tools.md)
（`BeiDou.exe` SHA256 `1b91cf5b…34b2`，ImageBase `0x400000`）。

底栏几何见 [statusbar-layout.md](statusbar-layout.md)，本文只讲"消息怎么进来、按什么规则显示"。

---

## 一、客户端只认一个包：`SERVERMESSAGE`

源端没有"系统消息包""聊天包""公告包"之分，**全部走同一个 opcode**，
首字节是 `type`，其后是正文：

```
SERVERMESSAGE
  byte   type
  <正文，按 type 有不同附加字段，见下>
```

所以客户端的显示逻辑必然是"读 type → 选落点"，而不是"按 opcode 分流"。
这一点决定了移植时的架构：**不要给每种提示各开一个 API，要统一收进一个
`Drop(type, text)`**（本工程对应 `NativeSystemMessage.Drop`）。

### 各 type 的附加字段（客户端解码顺序）

| type | 名称 | 附加字段 | 单机是否需要 |
| --- | --- | --- | --- |
| 0 | `[Notice]` | 无 | 需要 |
| 1 | `Popup` | 无 | 需要（弹窗） |
| 2 | `Megaphone` | 频道名、发送者名、世界/频道标记 | 不需要（无跨频道） |
| 3 | `SuperMegaphone` | 同上 + 增益物品名/数量 | 不需要 |
| 4 | `Scrolling` | 无 | 不需要（服务端 0 处调用） |
| 5 | `Pink` | 无 | 需要 |
| 6 | `Lightblue` | 无 | 需要 |

---

## 二、服务端四条调用链（都已逐行核实）

所有系统提示最终都收敛到 `PacketCreator.serverNotice(type, msg)`：

```java
// Character.java
public void dropMessage(int type, String message) {
    getClient().sendPacket(PacketCreator.serverNotice(type, message));
}

// Character.java:7198 —— 最常用的那条捷径
public void message(String m) {
    dropMessage(5, m);
}

// AbstractPlayerInteraction.java:699
public void playerMessage(int type, String message) {
    c.sendPacket(PacketCreator.serverNotice(type, message));
}

// AbstractPlayerInteraction.java:707
public void dropMessage(int type, String message) {
    getPlayer().dropMessage(type, message);
}

// AbstractPlayerInteraction.java:711
public void mapMessage(int type, String message) {
    // 发给整张地图的所有玩家
}
```

**结论 1**：脚本里的 `message("...")` **不是**普通提示，它硬编码 type 5（粉色）。
所以脚本里那些"你不能再往前了""封印挡住了门"在原端都是**粉色**，不是白色也不是蓝色。
本工程原先把它当白色 Toast，是错的。

**结论 2**：`dropMessage` / `playerMessage` / `mapMessage` 三个 API 签名完全一致
（`(type, message)` 两参 / `(message)` 单参），单参时等价 type 0。
所以移植时三个 API 可以共用一个辅助函数（本工程 `NpcApi.DropNotice`）。

**结论 3**：单机没有"整图广播"的概念（只有自己一个玩家），
`mapMessage` 退化为发给自己，落点不变。

---

## 三、类型表与实际调用分布

`PacketCreator.serverNotice` 的注释给了名字，实际调用统计给了权重：

| type | 名称 | 服务端调用 | 脚本调用 | 落点 | 语义 |
| --- | --- | --- | --- | --- | --- |
| 0 | `[Notice]` | 15 | 1 | 聊天窗（浅蓝） | 通用公告 |
| 1 | `Popup` | 164 | 18 | **居中弹窗**（点 OK 关） | 严重错误：背包已满、未知错误 |
| 2 | `Megaphone` | 2 | — | 喇叭 | 跨频道，不接 |
| 3 | `SuperMegaphone` | 4 | — | 超级喇叭 | 跨频道，不接 |
| 4 | `Scrolling` | 0 | 0 | 顶部滚动 | 服务端从未调用，可不实现 |
| 5 | `Pink` | 大量 | 143 | 聊天窗（**粉色**） | 阻止 / 失败 |
| 6 | `Lightblue` | 大量 | 73 | 聊天窗（浅蓝） | 条件 / 说明 |

脚本抽样得到的分工规律（`scripts/` 下逐条读过）：

- **type 5** —— 阻止与失败：「你不能再往前了」「封印挡住了门」
- **type 6** —— 条件与说明：「需要传送卡才能启动这个传送门」
- **type 1** —— 严重错误：「背包已满」「未知错误」

这条规律很有用：**同一句话用哪个 type，决定了它是"粉色一行字"还是"必须点 OK 的弹窗"**，
移植时不能统一降级成 Toast。

---

## 四、`[系统]` 前缀不是客户端加的

聊天窗里那句

```
[系统] 客户端上次闪退可能是地图【小蘑菇】不存在或异常，现已将你转移到【小蘑菇】。
```

前缀来自**服务端 i18n 资源**，不是客户端拼的：

- `Message_zh_CN.properties` 第 1107 行：
  `SystemRescue.map.message1=[\u7CFB\u7EDF] 客户端上次闪退…`
- `BeiDou.exe` 里 `[系统]` / `系统` / `[组队]` / `所有人` 等中文串，
  在 **UTF-16LE / GBK / UTF-8 三种编码下全部 0 命中**。

**推论**：客户端不持有任何中文文案，聊天行显示什么完全由服务端下发。
移植时若要复现这句话，必须在**下发处**带上前缀，而不是在客户端 `AddChat` 里拼。

---

## 五、聊天行的格式串（客户端确实拼过的东西）

客户端唯一自己拼的是"发送者 : 内容"这一行：

| 串 | 文件偏移 | VA | 引用点 |
| --- | --- | --- | --- |
| `%s : %s` | `0x6f1618` | `0xAF1618` | `0x45B756` |
| `%s:%s` | `0x6f328c` | `0xAF328C` | 未逐一定位 |
| `[%s]` | — | — | 6 处（频道名前缀） |

`0x45B756` 处反汇编：

```asm
push 0xaf1618        ; "%s : %s"
call 0x445b4b        ; 格式化
; 前面 5 个 push 的参数来自 [esi+0x250] .. [esi+0x260]
```

即"频道标记 / 发送者 / 内容"三段来自角色对象偏移 `0x250..0x260` 的字段。
**注意 `%s : %s` 中间是"空格冒号空格"**，不是 `%s: %s` 也不是 `%s:%s` —— 排版细节要对齐。

### 地址换算依据

`.text` 段 VA=`0x1000` / raw=`0x1000`，`.rdata` 段 VA=`0x6F0000` / raw=`0x6F0000`。
两段的 **file offset 都等于 VA − 0x400000**（ImageBase），所以上面表格里
`0x6f1618` ↔ `0xAF1618` 是直接加减 `0x400000` 得到的，不需要查节表重定位。

---

## 六、配色

### 粉色（type 5）—— 有截图实测

源端 10-27 截图里那句 `[系统] 客户端上次闪退…` 的字形像素：

- 字形像素均值 **RGB(204,171,173)**，通道比 **R:G:B ≈ 255:209:207**
- 背景（红框外）**RGB(2,3,3)**

本工程取 `#FFD0D0`（255,208,208），按字形均值的通道比还原——
因为截图字形是抗锯齿后的混合值，直接用 204 会偏暗。

### 浅蓝（type 0 / 6）—— **没有截图，按类型名推定**

`Lightblue` 只有名字，没有截图证据。本工程暂取 `#AAD7FF`（170,215,255）。
**这一项是待核实项**，见第八节。

### 白色（玩家自己发的）

`Color.white`。玩家聊天行不加前缀色，靠 `%s : %s` 的两段结构区分。
2026-10-27 展开面板截图核对：**所有人频道没有频道前缀**，就是「ieskk : 11222」。

### 黄色（[欢迎] 登录问候，2026-10-27 补）

展开面板截图第一行：`[欢迎] 欢迎光临冒险岛ONLINE世界。`，字形实测 R:G:B ≈ 255:256:96
→ 本工程取 `#FFFF60`。这条文案在服务端 Java/i18n/脚本与客户端字符串池（5658 条）里
都搜不到，属于源端新手引导链路的客户端侧问候；本工程在 `GameSession.SelectChar`
进游戏时由客户端补发。

---

## 七、本工程实现对照

| 源端 | 本工程 |
| --- | --- |
| `SERVERMESSAGE` + type 分派 | `NativeSystemMessage.Drop(type, text)` |
| type 0 / 5 / 6 → 聊天窗 | `BottomBar.AddChat(text, color)` |
| type 1 → 居中弹窗 | **暂退回 `BottomBar.Toast`**（缺版面参数，见第八节） |
| type 2 / 3 → 喇叭 | 不接（单机无跨频道） |
| type 4 → 滚动 | 不接（服务端 0 处调用） |
| `Character.message(m)` → `dropMessage(5, m)` | `NpcApi` 的 `message` 直接 `Drop(5, …)` |
| `dropMessage` / `playerMessage` / `mapMessage` | `NpcApi.DropNotice(c)`，按参数个数分流 |
| 组队/好友失败走 `serverNotice(5, …)` | `NativeSocialWindow.Run` 里 `Drop(5, result.error)` |

聊天历史结构 `BottomBar.ChatLine { string text; Color color; }`，
最多保留 200 行，屏幕上画最后 5 行，收起态条里显示最后 1 行（用该行自己的颜色）。

---

## 八、弹窗素材：字符串池里有什么（2026-10-27 补查）

之前写"`BeiDou.exe` 里查不到 `UI/Basic.img/Notice` 字面量"——**那个结论是错的**，
原因是只搜了明文。节点名全部走**加密字符串池**，用
`tools/inspect_beidou_client.py <exe> --strings out.json` 解出来就能看到：

| 池 id | 串 | 池 id | 串 |
| --- | --- | --- | --- |
| 1914 | `UI/Basic.img/Notice/backgrnd` | 1232 | `DlgNotice` |
| 1255 | `UI/Basic.img/BtOK` | 1256 | `UI/Basic.img/BtCancel` |
| 1257 | `UI/Basic.img/BtOK2` | 1258 | `UI/Basic.img/BtCancel2` |
| 2685 | `UI/Basic.img/Notice3/t` | 2686 / 2687 | `Notice3/c`、`Notice3/s` |
| 2688 | `UI/Basic.img/Notice4/s` | 2689 | `Notice4/s_line` |
| 2690 | `UI/Basic.img/Notice5/s_` | 2691 | `Notice5/s_DelGroup` |
| 2692–2694 | `UI/Basic.img/YesNo3/t,c,s` | 2263 | `BtOK` |
| 890 | `[Notice]` | 2828–2833 | `UI/UIWindow.img/Notice/0..4`、`/btOK` |

WZ 实测尺寸（`tools/imgdump.py UI/Basic.img <node>`，origin 都是 (0,0)，Notice5 除外）：

| 节点 | t | c | s |
| --- | --- | --- | --- |
| `Notice` | — | — | `backgrnd` 264×132 单张 |
| `Notice2` | 258×40 | 258×20 | 258×47 |
| `Notice3` | 266×21 | 266×20 | 266×55 |
| `Notice4` | 266×21 | 266×20 | 266×78（另有 `s_line` 266×84） |
| `Notice5` | 286×22 | 286×1 | `s_` 266×79 origin(133,40)、`s_DelGroup` 266×79 origin(133,39) |

`Notice5` 的 origin 在正中（133,40）→ 它是**以中心点定位**的弹窗皮肤，与"居中弹窗"最吻合。

代码侧已定位的线索（2026-10-27 深挖，比上面那版准）：

**1. 消息入口：type = 包体首字节**

`0xA22785` 处的函数：

```asm
mov  edi, [ebp+8]        ; arg1 = 包体指针
mov  ecx, edi
call 0x4065F3            ; 取一个字节
movzx esi, al
mov  [ebp-0x1C], esi     ; type = pkt[0]
...
00A229B0  cmp  esi, 0xD            ; 14 个分支
00A229B3  ja   0xA23687            ; 越界 → 默认
00A229B9  jmp  dword ptr [esi*4 + 0xA236D3]
```

**这直接证实了"首字节是 type"**，跳转表在 `0xA236D3`（14 项）。

**2. type 0 → 客户端加 `[Notice] ` 前缀**

case 0 在 `0xA229C0`：

```asm
push 0x37A           ; 池 890 = "[Notice]"
call 0x79E805        ; GetString
push 0xAF1E78        ; 字面量 " "（单个空格）
call 0x47C957        ; "[Notice]" + " "
call 0x47375C        ; 再拼上正文
```

即显示文本 = **`"[Notice] " + 正文`**。type 12/13 复用同一段代码（`0xA22AA4`）。
**这是客户端自己加的**，服务端下发的正文里没有这个前缀。

**3. type 1 → 弹窗，皮肤是 `Notice3`，默认标题 `DlgNotice`**

case 1 在 `0xA22B85`，把 5 个参数转给 `0x9929DD`：

```asm
00A229EF  push 0xD8              ; 对象大小 0xD8
00A229FC  call 0x403065          ; new
00A22A03  call 0x8D51D4          ; 构造函数
00A22A95  call 0x992EDA          ; 初始化（6 个参数，ret 0x18）
```

`0x992EDA` 里能读出两个硬参数：

```asm
00A92EF2  mov eax, [ebp+8]
00A92EFC  mov [edi+0x94], eax
00A92F02  mov eax, [ebp+0x18]
00A92F11  mov [edi+0xD4], eax
; 宽度：arg5 != 0 → 0xEA(234)，否则 0xC8(200)
00A92EEF  mov  esi, [ebp+0x1C]
00A92EF5  neg  esi
00A92EF7  sbb  esi, esi
00A92EF9  and  esi, 0x22
00A92F05  add  esi, 0xC8
```

```asm
00A92F77  push 0x4D0             ; 池 1232 = "DlgNotice"
```

`[ebp+0x10]`（标题）为空时用 `GetString(0x4D0)` = **`DlgNotice`** 作默认标题。

**4. 皮肤三段是 `Notice3/t,c,s`**

`0x9924A6` / `0x99258F` / `0x992678` 连续三次 `GetString(0xA7D/0xA7E/0xA7F)`
= `Notice3/t`、`Notice3/c`、`Notice3/s`，分别取画布存进 `[ebp-0x4C]/[ebp-0x54]/[ebp-0x5C]`。
这是弹窗的 OnCreate 加载三段的代码。

**仍缺**：居中/定位的 x、y，文字区矩形，OK 按钮坐标 —— 在绘制函数里，本轮没跟到。
`Notice/backgrnd`(1914) 在 `.text` 里找不到干净的 `push 0x77A`（唯一命中 `0x41C5FE`
的上一字节是 `E8`，属于某条 `call` 的地址操作数，是假阳性），所以 264×132 那套到底谁在用仍未知。

## 九、仍未坐实的项

1. **type 0 / 6 浅蓝的具体色值** —— 只有类型名 `Lightblue`，没有截图。
   本工程暂取 `#AAD7FF`（170,215,255）。尝试过在 `.text`/`.rdata` 里扫颜色常量表，
   噪声太大（大量指针与指令字节被误判），没有定位到"按 type 索引的颜色数组"。
2. **`FontMemo/0..9` 未读计数的绘制位置** —— 反汇编里定位不到。
3. **聊天行最多显示几行、行距多少** —— 2026-10-27 已从 `0x8D4A6D`（CChatLog SetMode）反编译坐实：
   **行高 13**、展开面板可见行数 = 面板高/13、高度钳制 [26,507]、面板底边固定 y=509。
   详见 [statusbar-layout.md](statusbar-layout.md) 的「聊天窗面板」一节。
   收起态条里只显示最后 1 行（用该行自己的颜色）。

## 附：查节点名请先解字符串池

`BeiDou.exe` 的节点名、UI 文案**全部**在加密字符串池里（指针表 `0xBDC9D4`，条数 `0x161A`，
128 位密钥 `0xB001EC`），明文搜索必然 0 命中。本文档早期"查不到 `Notice` 字面量"的结论
就是踩了这个坑。取串的代码形态是：

```asm
lea  eax, [ebp - 0x18]
push 0x77a            ; 池 id
push eax
call 0x79e805         ; GetString
```

所以要找"某个节点在哪被用"，正确做法是**解池拿到 id，再扫 `push <id>` 的立即数**
（`0x68` 后跟 4 字节小端），不是搜字符串。

## 十、聊天落点：`0x4906B5(文本, 类型号)` → `0x8DB070`

三个"进聊天窗"的 type 最终都调同一个函数，第二参是个 WORD 类型号：

| type | 调用 | 类型号 | 前缀 |
| --- | --- | --- | --- |
| 0 | `0x4906B5("[Notice] " + msg, 10)` | 10 | `[Notice] ` |
| 5 | `0x4906B5(msg, 12)` | **12** | 无 |
| 6 | `0x4906B5(msg, 10)` | 10 | 无 |

`0x4906B5` 里 `movzx eax, word ptr [ebp+0xC]` 取的就是这个号，随后转手给
`0x8DB070`（底栏聊天日志的入口，`ecx` = 日志对象）。

**10 与 12 就是"浅蓝 / 粉"的区分点** —— 但颜色常量本身在绘制函数里，本轮没跟到。
所以 `#AAD7FF` 仍然是推定值。

顺带：`0x8DB070` 里对类型号做了 `cmp esi,0xE / 0xF / 0x12 / 0x13` 的判断（14/15/18/19 号
不缩进），说明聊天日志的类型号范围比 0..6 宽得多，**包里的 type 只是它的一个子集**。
