# 怪物手册（MonsterBook）

窗口标识：原型字符串 id 2594 `MonsterBook`（`0x92C660` 的 archetype 表；窗口类名同源）。
本节所有地址基于 `clien/BeiDou.exe`，SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。版本变化必须重新核对。

## 函数范围（VA）

| 地址 | 指令数 | 职责 |
| --- | --- | --- |
| `0x861E99` | 75 | 窗口构造：`0x92C2E8`（基类）→ `0x86256D` → `0x862B13` → `0x863717` → `0x8637A8` → `0x8638AE` → `0x867BE6`；末尾分支里 `0x8603DF(9)` 把左书签栏选中项置 9 |
| `0x86256D` | 350 | 建全部子控件（坐标即原端像素） |
| `0x862B13` | 1065 | 右页 + 右书签（动态拼 `RightTab` 等节点名，只有 `Canvas` 字符串） |
| `0x863717` | 67 | 注册命中框（25 格卡槽 + 20 格战利品） |
| `0x8637A8` | 89 | 建 9 页 × 25 张（`idiv 0x19`） |
| `0x8638AE` | 319 | 文字/字体对象（`Canvas#Font` + `BA`），带 6 个 DC 的初始化循环 |
| `0x863DF1` | 1741 | 左页渲染：`[this+0xad8]+0x34` 的索引 `cmp 9` → infoPage，否则 cardSlot |
| `0x865220` | 489 | 悬停/点击分派：`index/0x19` 定页、`index%0x19` 取格，读同一张 16 字节矩形表 |
| `0x865782` | — | 战利品页用 `UI/UIWindow.img/IconBase/0` 当图标底座 |

复现：`python3 tools/inspect_beidou_client.py <exe> --fn 0x86256D`（线性 `--start/--end` 在 `0x862Axx` 之后会失步，必须用 `--fn`）。

## 子控件表（`0x86256D`，参数序 `parent, id, x, y[, w, h], &anchor`）

| id | 控件 | 坐标 / 矩形 | 素材（池 id） |
| --- | --- | --- | --- |
| 1000 | BtClose | (429, 8) | `UI/UIWindow.img/MonsterBook/BtClose` (2585) |
| 2000 | BtSearch | (175, 29) | `.../BtSearch` (2586) |
| 2001 | arrowLeft | (48, 285) | `.../arrowLeft` (2587) |
| 2002 | arrowRight | (185, 285) | `.../arrowRight` (2588) |
| 2003 | arrowLeft（第二组） | (270, 285) | 2587 |
| 2004 | arrowRight（第二组） | (407, 285) | 2588 |
| 2005 | 文本输入控件 | (49, 30) 120×15 | 走工厂槽 `[eax+8]`，与聊天窗输入框 (9,318,207,15) 同一个槽 |
| 2006 | 左书签栏 | L=-7, T=25, R=50, B=305（57×280 → 10 项，步距 28） | `.../LeftTab` (2589) + `.../LeftTabInfo` (2590) |
| 2007 | 右书签栏 | L=439, T=25, R=506, B=220（67×195 → 4 项，步距 49） | `.../RightTab` (2591) |

要点：

- 矩形控件（`[eax+0x20]`）的 x/y/w/h 是先把 `L/T/R/B` 写进栈上结构体再 `push` 结构体地址，所以
  `2006`/`2007` 看到的是 `50`/`305`、`506`/`220` 这种**右/下边界**，不是宽高。
- 有两对箭头，别只画一对：`48/185` 在卡槽面板正下方翻卡页；`270/407` 在右页正下方，属于右页自己的换卡按钮。
- `2005` 与 `BtSearch` 是同一行、同一块区域：cardSlot 图顶部的白条（相对 y 5~18、宽约 124）就是输入框所在位置。
  因此**卡槽页页头没有页码文字**，不要自行加 “1 - 25” 这类标签。

## 命中框（`0x863717`）

`0x863717` 往 `this+0xb2c` 起、每条 16 字节（4 个 int）的矩形表里写两组：

1. 25 格卡槽：`x = 8 + 33*col`、`y = 31 + 45*row`、`27×38`（col = i%5，row = i/5，`idiv 5` + `imul 0x21`/`0x2d`）。
2. 20 格：`x = 38 + 36*col`、`y = 60 + 36*row`、`32×32`（col = i%4，row = i/4）——右页战利品网格 4×5。

第一组坐标是**相对 cardSlot** 的（与像素描边量出来的 8/31/33/45 完全一致），`0x865220` 就是拿鼠标位置去查这张表。
第二组的父坐标没在代码里直接给出；按书脊（backgrnd 竖排暗列 234~240）之后 241 起、右页 208 宽算，格子在 279~419，落在纸面内。

## IMG 节点（`UI/UIWindow.img/MonsterBook`）

| 节点 | 尺寸 | origin | 说明 |
| --- | --- | --- | --- |
| `backgrnd` | 475×349 | (0,0) | 书本 + 两页纸；竖暗列 234~240 是书脊，450~458 是右侧阴影 |
| `cardSlot` | 174×256 | (0,0) | 5×5 卡格底板，顶部白条在相对 y 5~18 |
| `infoPage` | 170×231 | (0,0) | 手册信息页（印刷标签 + 顶部 40×47 空卡框 67~106 / 2~49） |
| `select` / `cover` | 31×42 | (2,40) | 选中框 / 封面框，按卡面左上 -2,-2 贴 |
| `fullMark` | 7×11 | (0,0) | 该卡收满 5 张的标记 |
| `arrowLeft` | normal 24×16 / pressed 22×16 / disabled 24×17 / mouseOver 24×16 | | 四种状态 |
| `arrowRight` | normal 23×17 / pressed 23×16 / disabled 23×17 / mouseOver 23×17 | | |
| `BtClose` | 18×16 ×4 态 | | |
| `BtSearch` | 34×17 ×4 态 | | |
| `LeftTab/[0..8]` | normal/mouseOver 20×20、selected 42×20、`new_n`/`new_s` 各 2 帧 | | **9 套配色**：0 红 (170,17,17)、1 橙、2 黄绿、3 绿、4 青、5 蓝、6 紫、7 黑、8 金；每页一套，不是三套轮流 |
| `LeftTabInfo/0` | normal 27×35、selected 39×33 | | 第 10 枚（手册信息） |
| `RightTab/[0..3]` | normal/mouseOver/disabled 52×38、selected 66×39 | | 右对齐到 x=506，`y = 25 + 49*i` |
| `icon/[0..7]` | 22×27 | (0,0) | 8 个不同颜色的小书本图标，用途**待核实**（见下） |
| `ContextMenu` | `t` 80×19、`c` 77×13、`s` 77×7、`BtRegister`/`BtRelease` 85×15 | | 卡片右键菜单（登记/解除封面） |

卡面数字字体：`UI/Basic.img/ItemNo/<digit>`（池 id 1253 `UI/Basic.img/ItemNo`）。战利品底座：`UI/UIWindow.img/IconBase/0`（32×32）。

## 布局换算（画到 475×349 窗口里）

- 竖直：左书签栏顶 = 内容顶 = **y 25**；卡槽 256 高 → 底 281；两对箭头都在 y 285（与卡槽底只差 4px，说明内容顶就是 25 而不是 30）。
- 水平：卡槽 174 宽放在 41（窗口坐标；`0x86256D` 的绝对坐标都是窗口坐标，48/185 的箭头相对卡槽左右各留 7）；infoPage 170 宽居中 → 43；右页从 241 起，208 宽（174 正文 + 两侧留白），右书签 440~506 压住书边但不压正文。
- 书签伸出书本：左 -7、右到 506，所以整个窗口的绘制框是 `-7 .. 506`（513 宽）。

## 迁移状态（`Assets/Scripts/NativeMonsterBook.cs`）

已按本节重写：backgrnd 475×349 + 两条书签栏（9 页 + 信息页 / 4 个右书签）+ cardSlot 5×5（27×38、槽距 33×45，`select`/`cover`/`fullMark`/`ItemNo` 数字）+ infoPage 五项数值 + 两对箭头 + 顶部搜索框与 BtSearch + 战利品 4×5 网格。

数据侧：`SocialOperation.Read` 拿 `bookLevel/bookCover/cards`；卡片表来自 `Item/Consume/0238.img`（`info/monsterBook != 0`，`info/mob`、`info/icon` 27×38）；文案来自 `String/MonsterBook.img/<mob>/episode|map|reward` 与 `String/Mob.img/<mob>/name`；数值来自 `Mob/<7位>.img/info`。

验证：`tools/unity-compile-check` 0 error；用原端 IMG 素材按上述像素坐标离线合成三张对照图（卡页/信息页/战利品页）确认卡面落在格子里、数值压在印刷冒号右侧、两组箭头与网格/右页对齐。

**已实机（编辑器 Play 模式 + ScreenCapture）实拍三张**（`docs/preview/20-book-info.png`、`21-book-cards.png`、`22-book-drops.png`，
`PreviewShot.Run -previewStart 43`，真实存档 + 40 张真卡）：

- 内容顶 y=25 与卡片栅格、两条书签栏、BtClose、搜索框 + BtSearch 的绝对坐标全部对得上（把箭头中心换算回设计坐标得
  55/192/277/414，与 `0x86256D` 的 48/185/270/407 + TabOutL 7 一致）。
- 箭头四态实测：卡页 0 时左页 `arrowLeft` 灰（disabled）、`arrowRight` 金（normal）；右页一对同理按 `SelectedAt()` 判定。
  即 **金=normal、灰=disabled**，别把两态画反。
- 卡槽页顶部白条就是搜索框（49,30,120,15），BtSearch 34×17 紧贴其右；手册信息页没有这一行。
- infoPage 五个数值都压在印刷冒号右侧；顶部 40×47 空卡框在没有封面时留空。
- 战利品页 20 个 32×32 底座 + 图标落在右页纸面内，不压书脊、不进右书签。

实拍口径：`Unity -projectPath MapleDash -executeMethod PreviewShot.Run -previewShots <dir> -previewWidth 1280 -previewHeight 720 -previewStart 43 -batchmode -logFile <log>`
（`-batchmode` 下 IMGUI 仍能进 ScreenCapture，实测可用；不加 `-batchmode` 反而起不来）。


## 待核实

- 右页正文的精确坐标：`0x862B13` 1065 条指令尚未逐条解完。它只引用 `Canvas`（池 956）三次、没有引用
  `Level : ` 这类标签字符串，说明这一层是在拼对象/位图，不是直接画字；"名字居中 + 立绘 + Level/HP/MP/EXP 四行（x=38）"
  仍是从战利品网格左边界推的，不是代码确认值；`String/MonsterBook.img` 的 episode/map/reward 三页排法同理。
- 左书签上的**页码数字**没有证据：`LeftTab/*/normal/0` 是纯色纸条（20×20，无印字），原端要么在书签控件内画数字、
  要么根本不画。当前移植用 10px 字体画 1..9（第 8 枚黑纸改浅色，否则看不见），属于**未核实**；书签控件绘制函数
  定位到 `0x860623` 一带（`push 0xa1d` = LeftTab 池 id 2589 在 `0x860879`），但函数边界被线性反汇编带偏，未解完。
- `270/407` 那对箭头的语义：几何上属于右页，实现按“在整本书的已收录卡里前后换卡、跨页时左页跟着翻”处理；代码里还没有定到它的点击处理分支。
- `icon/[0..7]`（22×27 小书本，8 种颜色）没有任何已定位的引用点，不知道画在哪。
- `LeftTab` 的 `new_n`/`new_s`（2 帧）是本页有未读卡时的闪烁态，客户端何时切换未定。
- `ContextMenu/BtRegister`、`BtRelease` 是卡片的右键菜单；当前移植用双击代替，菜单窗口本身没做。
