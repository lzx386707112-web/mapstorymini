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
