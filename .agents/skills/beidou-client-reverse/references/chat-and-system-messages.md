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

## 八、待核实项（按项目规则「不猜」，先记录不实现）

1. **type 1 的居中弹窗版面**
   - 素材候选一：`UI/Basic.img/Notice`（264×132 单张，图上自带 OK 按钮框）
   - 素材候选二：`UI/Basic.img/Notice2`（`t` 258×40 / `c` 258×20 / `s` 258×47 九宫格），
     `NativeUi.Panel` 已在用这套
   - `BeiDou.exe` 里**查不到** `UI/Basic.img/Notice` 字面量（客户端用哈希/索引解析节点名），
     静态分析坐实不了 → 需要用户拍板用哪套 + 文字区坐标 + OK 按钮坐标
2. **type 0 / 6 浅蓝的具体色值** —— 只有类型名 `Lightblue`，无截图
3. **`FontMemo/0..9` 未读计数的绘制位置** —— 反汇编里定位不到
