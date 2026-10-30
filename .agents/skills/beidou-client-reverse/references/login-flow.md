# 登录链路与弹窗（新账号确认 / 性别 / 开源声明）

## 二进制身份

`BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。

## 登录确认与性别（历史结论及边界）

- `LOGIN_STATUS` 成功包体（`PacketCreator.getAuthSuccess`）：`int 0` + `short 0` + `accId(4)` + **`gender(1)`** + GM 两字节 + 账号名…；性别紧跟账号 id。`accounts.gender` 的默认值是 **10**（`V1.0.0__create_accounts.sql:19`，`NOT NULL DEFAULT '10'`），表示「还没选性别」，自动注册的 INSERT 不写这列，所以新号天然是 10。
- `SET_GENDER`（`SetGenderHandler.java:28-38`）：**外层就判 `c.getGender() == 10`**，定过再发包直接忽略；`confirmed == 0x01` 才写 `readByte()` 的 0/1，随后**重发 `getAuthSuccess`** 并 `registerLoginState`；`confirmed != 0x01` 直接关会话。
- `0x5fc170`：处理登录回包，`loginok == 7`（需补充资料）时跳到 `0x60eb58`。
- `0x60eb58`：Notice 显示函数。调用 `0x51f9ad` 时压入的常量是 `0x16a`(362) / `0xdb`(219)，即弹窗纸面尺寸；同函数内还有 `push 3` / `push 1` 决定按钮组（mode 0 = 是/否，1 = 单个确定；两种 mode 的分派条件**待核实**）。
- `0x60eb58` 建对象用 `push 0x10c` + `operator new(0xbf0b00)`，构造函数是 **`0x60ec5e`**：基类 `0x51f887` 之后固定建**三个子控件** —— `+0xe0`、`+0xe8` 调同一个构造函数 `0x428925`（两颗同类按钮），`+0xf0` 调 `0x473def`（正文）。这只能说明 Notice 类拥有两个按钮，不能证明新账号必定先弹 Notice/text/6；旧记录将类结构推成触发顺序，结论过度。
- `0x613993`：Gender 对话框构造，`Gender/scroll/0` 与 `Gender/text/1` 都在其中。

复现：`tools/inspect_beidou_client.py BeiDou.exe --start 0x60eb58 --end 0x60ed58`，看 `0x60ebf8..0x60ec0c` 的 push 序列。

## 已核实：素材与坐标

| 用途 | 节点 | 事实 |
| --- | --- | --- |
| 提示纸面 | `UI/Login.img/Notice/backgrnd/0` | 362×219，origin 0,0，另有 1/2 两种配色 |
| 提示正文 | `UI/Login.img/Notice/text/<n>` | 全部 **221×109**，图里自带纸面底纹和中文，所以必须按像素贴、不能居中瞎摆 |
| 新账号文案 | `Notice/text/6` | 「可以使用的账号。确定要用这个账号，请按下“确定”按钮。」相对纸面 **(119,21)** —— 用纹理相关性在纸面上搜最优对齐得出（median abs diff 3.15，次优 4.90），目录里量到的 (70,55) 居中位置是错的 |
| 声明卷轴 | `UI/Login.img/TOS/0` | 431×426，origin (215,213) → 放屏幕中心；右下自带两块橙色按钮底板 |
| 声明按钮 | `UI/Login.img/BtOk`、`BtCancel` | 75×32，相对卷轴 (266,374) / (340,374) |
| 声明正文 | `String/EULA.img/EULA/Text00…` | `#e`/`#n` 是粗体开关；换行是**字面的 `\r\n` 四个字符**，不是控制字符 |
| 滚动条 | `UI/Basic.img/VScr6` | 12×12；只有 base 平铺，thumb0/1 单张绘制，见下方纠正 |
| 性别卷轴 | `UI/Login.img/Gender/scroll/0`（两帧）、`Gender/text/0`、`BtYes`/`BtNo` | 见 `docs/login-ui-source-analysis.md` 5.1 |

`NoticeScroll1` / `NoticeScroll2` 是**另一种**带滚动条和 OK 的卷轴，不用于开源声明，别再拿它们拼 EULA。

## 待核实

- Notice mode 0/1 的选择条件（`0x60f799` 是 mode 接收方，往上追调用点的 `push` 常量即可）。结构上已知构造函数固定建两颗同类型按钮，所以「可以使用的账号」是 确定/取消 并排（(106,168) / (181,168)，单按钮时 (143,168) 才是居中）；尚未核到「哪个 mode 会让其中一颗失效」。
- 声明弹窗是否响应 Esc。当前实现按「必须滚到底点确定」处理，不响应 Esc。

## 当前迁移与验证边界

实现在 `Assets/Scripts/Client/LoginFlow.cs`（`DrawNewAccountDialog` / `DrawNoticeDialog` / `BuildEulaLayout` / `DrawEulaScrollBar`），完整界面依据见 `docs/login-ui-source-analysis.md` 5.1。

**性别写库必须带 accountId**：性别框在 `EnterWorldSelect` 之前弹，那时 `GameSession.BeginSession` 还没跑、`AccountId` 仍是 0，用会话 id 调 `SetGender` 会静默返回 false（库里还是 10、下次登录又弹「可以使用的账号」，看着像「性别没记住」）。`GameSession.SetGender(int accountId, int gender)` 为准，调用方传 `LoginResponse.accountId`；服务端 `LoginService.SetGender` 同样先读库，非 10 直接返回原值。

单位边界：`DrawTexTiledV` 的步进必须乘 `s`（`R()` 出来的是设计稿 × s 的虚拟坐标）。旧记录关于“滑块12px对齐后平铺”的说法已被下方原端绘制函数证据推翻，滑块只画一张。

滚动/键盘输入只在 `Update` 里读：`OnGUI` 每帧会跑 Layout/Repaint/输入事件多趟，放在里面滚一次会变成两三次。

2026-10-28 Android 启动卡 100% 排查：`BuildEulaLayout` 逐个读取 `Text00…` 时必须显式传 `root.Text(key, null)`，以缺失节点结束。`ImgNode.Text` 的默认 fallback 是空串，使用默认参数却判 `t == null` 会在末项后无限循环并阻塞 Unity 主线程。卸载重装使 `login_notice_ok` 恢复未同意，首次绘制声明触发；手机日志显示假人预热完成、`GameLoop.BuildBackground` 完成后无后续，并出现 Unity pause timeout。此为迁移代码缺陷，未改变上述原端布局/流程结论。`LoginFlowChecks` 已补实际调用 `BuildEulaLayout` 的返回及非空排版检查；Android 修复包实机验证需单独记录。

回归入口：`LoginFlowChecks.Run`（`-batchmode -quit -executeMethod LoginFlowChecks.Run`）直接在编辑器里 new 一个 `LoginFlow`、走它自己的 `DoLogin → ConfirmNewAccount → ConfirmNewGender → EnterWorldSelect`，覆盖弹窗顺序 / 库里的 gender / 性别不可二次修改 / autostart 自动补 0 / `Notice/text/6` 素材存在。2026-10-28 加入首次声明排版返回检查后，Unity batchmode 实跑 24 项全过（声明排版 21 行，进程退出码 0）；未打 Android 修复包，尚未实机验证修复结果。
`LoginUiReview.Run` 是编辑器播放模式截图脚本，需要图形会话；**batchmode 下它不渲染**（`WaitForEndOfFrame` 不回调，协程停在第一次截图前，日志只剩 `GameLoop.Start`），不能当作通过依据。

## 2026-10-28 声明弹窗纠正（本次）

用户提供原端新账号声明截图并明确：只在创建新账号时出现。迁移实现据此改为未完成注册账号（gender=10）登录成功 → TOS 声明 → 确定 → 性别 → 选区；取消声明回到登录并清 pending，不删除已自动注册账号。老号、设备首次启动不弹声明，不再读写设备级 login_notice_ok。保留 gender=10 的重试入口，取消后重连不会跳过声明。此触发策略来自用户明确要求；原端完整 LOGIN_STATUS/TOS 分派仍不能由 Notice 类构造反推。

同 SHA256 的新反汇编证据：

- `0x62130e` 为 TOS 初始化；`0x62132d` push 字符串池 3028=`UI/Login.img/TOS/0`；`0x6213da..0x6213e8` 建确定按钮 (266,374)，`0x621470..0x62147a` 建取消按钮 (340,374)。
- `0x621875..0x621894` 创建滚动条，参数栈为 parent, id=1000, vertical=1, type=5, x=390,y=40,height=317；`0x4d6e36..0x4d6e40` type5 选字符串后缀“6”，即 VScr6。
- `0x621bf7..0x621c09` 正文裁切参数 x=36,y=41,w=353,h=308；`0x6214a7` 文字排版宽度350。原先 (28,36,358,318) 没有原端代码依据，已纠正裁切区域。
- 滚动类 `0x4d74e4` 绘制：`0x4d7747..0x4d791d` 逐块绘制 base；`0x4d7b53..0x4d7b96` 读取 thumb 原图尺寸，以 `(length - 2*arrowSize - thumbSize)*position/(range-1)+arrowSize` 定位；thumb 为**单张**，不存在可见高/内容高比例伸长。旧“按比例并对齐12后平铺滑块”结论错误。VScr6 所有贴图宽高均12，origin(0,0)。
- `0x4d7e4d` 在控件鼠标按下后捕获鼠标、命中具体部件；`0x4d7e94..0x4d7ece` 分派箭头/轨道/滑块；不读取全屏手指位移。迁移删除 HandleEulaScrollInput 的全屏 touch.deltaPosition 滚正文分支，只允许从 thumb 内按下开始拖动。
- `0x621e19..0x621e25` 源端正文位移为 scrollPos*5；`0x621e2b..0x621e47` 对比确认阈值启用确定按钮。迁移目前仍用内容排版高度决定归一化滚动范围，尚未完整复刻原端文本排版器/离散range公式，不声称逐像素完全一致。

资源实读纠正：当前 String/EULA.img 含**双反斜杠**的换行序列（Python repr 为 `\\\\r\\\\n`），旧解析只识别单反斜杠序列会残留字符并漏换行。迁移先归一化双/单反斜杠换行，再排版，保留资源正文，不自行改写文案。

复现：`/tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py <BeiDou.exe> --start 0x62130e --end 0x621929`；同工具查看 `0x4d74e4..0x4d7d2d`、`0x621ba7..0x621e65`；节点用 `python3 tools/imgdump.py UI/Basic.img VScr6 --depth 3` 与 `String/EULA.img EULA --depth 1`。工具本体在仓库中；Python 环境路径仅本机运行依赖。

滚动增量补证：`0x4d8145` 箭头 position += ±1；`0x621e19` 转正文像素乘5，迁移箭头改为5px。`0x4d8189..0x4d81a3` 轨道翻页为 `max(16, range/16)` 个位置单位，迁移按排版范围换算后应用该步长。

本次验证：Unity batchmode `LoginFlowChecks.Run` 最终28项全过，退出码0（日志 `/tmp/mapledash-login-statement-final.log`）；覆盖新账号声明/取消/确认到性别、老账号不弹、正文换行、条外按住移入不滚动、滑块拖动/松开。`git diff --check` 通过。未构建APK、未实机/图形渲染验收，本次不沿用旧截图PASS。

## 2026-10-30 选区频道样式 / 选角选中特效（已按 exe 常量落地）

### 频道格与选中标记

- `0x6299e2` 是 `GetChannelRect(int index, RECT*)`，纯几何：`left = (i%4)*94 + 35`、`top = (i/4)*32 + 57`、宽 92、高 30（常量 `0x5e`/`0x23`/`0x5c`/`0x20`/`0x39`/`0x1e`）。相对内容区原点，与迁移里 `sx+74/cy+211`（sx=396,sy=139）等价，别再当成新坐标系。
- `0x6289a4` 是频道区的窗口过程：`0x201`(LBUTTONDOWN)/`0x203`(LBUTTONDBLCLK) → `0x629da5` 命中测试（逐格调 `0x6299e2` + `PtInRect`）→ `0x629cbd(index)` 选中；双击再多调 `0x629d0a` 进频道。
- `0x629a2e` 是 `SelectChannel(int idx)`：**`if (this+0xdc == idx) return`**，同格再点不重建动画；`idx < 0` 时把 `+0xec` 清空（即选中标记消失）。
- 选中标记素材是 `UI/Login.img/WorldSelect/channel/chSelect`（**不是自绘描边**）：4 帧 23×11 / 38×21 / 42×33 / 59×33，delay `100/50/50`（末帧无 delay），origin 分别是 `(12,-6)/(27,4)/(29,16)/(29,16)`，即每帧中心≈锚点 → 用「每帧 origin」居中画。形状是红色手绘圆圈，最后一帧圈住 `CH.n`。
- **锚点 = 格内 `(left+20, top+7)`**：`0x629ab8 mov eax,[ebp-0x34](rect.top) / add eax,7`，`0x629abf mov eax,[ebp-0x38](rect.left) / add eax,0x14`，随后 `0x596572` 组 POINT、`0x629adc push 0x558` 取 `chSelect`。合成验证：帧 3 正好套住 `CH.1`，不加任何额外偏移。
- 播放语义：动画对象挂在 `+0xec`，末帧无 delay ⇒ 播完停在完整红圈（迁移用 `loop:false`），不是每 300ms 重描。

### 频道负载条（IMG，不是自绘色块）

- 素材 `UI/Login.img/WorldSelect/channel/chgauge` 73×9，origin `(36,4)`。
- `0x628c14` 里逐频道绘制（常量位置都在这段）：
  - `level = clamp(load/60 + 1, 1, 14)` —— `0x62907e push 0x3c`、`0x629083 inc eax`、`0x62908d..0x629092` 夹 1..14；
  - 宽度 `= 5*level + (level==14 ? 1 : 0) + 2` → **7..73**（`0x62909d lea eax,[eax+eax*4]`、`0x6290a1 lea eax,[ecx+eax+2]`），最大 73 正好是整条；
  - 位置 `= 格内 (left+7, top+17)`、高 8（`0x6290fd add edx,7`、`0x629100 add ecx,0x11`、`0x6290df push 8`）。
- 即「按负载从左裁一段」，空频道只露蓝色短段，满负载延伸到橙色 —— 不是固定小蓝块。
- `load` 来自服务端 `Channel.getChannelCapacity() = ceil(在线人数 / channel_capacity * 800)`，由 `PacketCreator.getServerList` 每频道 `writeInt`。迁移 `ChannelEntry.load` 恒 0（没有频道级在线集合），公式在 `LoginFlow.ChannelGaugeWidth`。

### 选角选中特效

- `0x5f6482` = `SetSelectedSlot(int idx)`：`idx < 0` 时把 `+0x1cc`、`+0x1d0` 都清空；`[+0x168] != 5` 走主路径（网格 125），`== 5` 走 `ViewAllChar`（网格 117、贴 `ViewAllChar/Select`）。**两个 effect 都挂在主路径上**：
  - `0x5f6518 push 0x521` → `UI/Login.img/CharSelect/effect/0` → `+0x1d0`（星点，6 帧）
  - `0x5f6653 push 0x522` → `UI/Login.img/CharSelect/effect/1` → `+0x1cc`（光柱，5 帧）
- 锚点由素材自证：底边 = `(-origin.y) + height`。`effect/1` 末帧 `0+337`；`effect/0` 首帧 `205+132 = 337`。两条序列底边同时落在脚点上 ⇒ **锚点 = (槽位 x, 脚点 y − 337)**，x 方向两套 origin 中心都≈0。
- 播放语义：`effect/1` delay `150/100/100/100/3000` → 循环（亮 3 秒再重升）；`effect/0` delay `300/120/120/120/120/0` 且末帧 `a1=0` → 一次性淡出，`loop:false` 停在不可见末帧。
- 角色 stand 动作用客户端统一时钟（所有槽位同相位），不要拿「进页时间」当起点，否则翻页会打回第 0 帧。
- 名牌两套：`nameTag/0` 高 20（半透明灰）+ 段宽 8/9/7，`nameTag/1` 高 22（实心黑）+ 8/9/10 ⇒ 选中用 `/1`、未选中用 `/0`。

### 复现

```
python3 tools/inspect_beidou_client.py <BeiDou.exe> --fn 0x6299e2      # 频道 RECT
python3 tools/inspect_beidou_client.py <BeiDou.exe> --fn 0x629a2e      # SelectChannel + chSelect 锚点
python3 tools/inspect_beidou_client.py <BeiDou.exe> --fn 0x628c14      # 每频道 normal/disabled + chgauge
python3 tools/inspect_beidou_client.py <BeiDou.exe> --fn 0x5f6482      # SetSelectedSlot + 两套 effect
python3 tools/imgdump.py UI/Login.img WorldSelect/channel/chSelect --depth 1
```

回归：`LoginFlowChecks.Run` 第 8 节断言锚点常量、chgauge 宽度公式（7 / 32 / 73）与两套 effect 的底边 337。

### 实拍核对（2026-10-30，`LoginUiReview` 16 张）

- 选区：红圈（`chSelect` 末帧）套住 `CH.1`、三个可用频道槽内有蓝色短条（level 1 → 宽 7），位置对。
- 选角：`charselect-effect.png` 里光柱确实在屏上（x 756..806，与锚点算出的 754..808 一致），但**只露出卷轴上沿以上的一段**。原因不是 bug：
  - 光柱素材自身 alpha 上亮下淡（帧 2 各段均值 175 / 144 / 89 / 32），越靠脚越淡；
  - 角色信息卷轴（`CharSelect/scroll` + `charInfo`）压在其中段，角色本体又盖住最窄的下段。
  - 源端两个 effect 在 `SetSelectedSlot` 里创建（`+0x1cc` / `+0x1d0`），卷轴/charInfo 在另一处创建；**绘制先后未从 exe 确证**，现状按「特效在卷轴之下」绘制。若后续要更醒目，需先确证源端层序再改，不要凭观感翻。
- `charselect-effect.png` 的卷轴是空白，不是回归：钉 `selectionStartedAt` 会连卷轴展开动画一起重播，`DrawCharInfo` 前 0.35s 按源端语义不画文字。
- 该轮 `LoginFlowChecks.Run` 因并发会话把 `Assets/Editor/PreviewShot.cs` 改到一半（`Ready/SetupEquip…` 未定义，Editor 程序集编译失败）未能重跑，非本次改动引起。
