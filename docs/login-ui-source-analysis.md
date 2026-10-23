# 源端登录 / 选区 / 选角 / 建号 UI 分析（2026-10-23）

分析对象仅限本机源端目录：

- 服务端：`/Users/lizixian/Documents/mxd/BeiDou-Server/gms-server`
- 客户端：`/Users/lizixian/Documents/mxd/BeiDou-Server/clien/BeiDou.exe`
- 客户端补丁：`clien/ijl15.dll`
- 客户端配置：`clien/config.ini`
- 客户端资源：`clien/Data`（与 `MapleDashResources/Data` 字节一致）

> 结论以源端 exe/DLL/Java/IMG 和源端实机截图为准，不再引用外部复刻工程。

## 1. 服务端登录链路

| 步骤 | 客户端包 | 服务端处理器 | 关键行为 |
|---|---|---|---|
| 1 | `LOGIN_PASSWORD` | `LoginPasswordHandler.handlePacket`（`net/server/handlers/login/LoginPasswordHandler.java:55`） | 读 `login/pwd/skip6/hwid4`；账号不存在且 `automatic_register` 为真时自动注册（`:75`） |
| 2 | `SERVERLIST_REQUEST` | `ServerlistRequestHandler.handlePacket`（`ServerlistRequestHandler.java:37`） | 逐个大区下发 `getServerList`（名称/flag/事件文案/频道列表），再发 `getEndOfServerList`、`selectWorld(0)`、推荐大区 |
| 3 | `CHARLIST_REQUEST` | `CharlistRequestHandler.handlePacket`（`CharlistRequestHandler.java:35`） | 读 world、channel+1，设置 `Client.world/channel`，`sendCharList(world)`（`:54`） |
| 4a | `CHECK_CHAR_NAME` | `CheckCharNameHandler`（`CheckCharNameHandler.java:33`） | 名字可用性返回 `charNameResponse` |
| 4b | `CREATE_CHAR` | `CreateCharHandler.handlePacket`（`CreateCharHandler.java:40`） | 包体 `name, job, face, hair, hairColor, skinColor, top, bottom, shoes, weapon, gender`；逐项走 `isNewCharDefault*` 白名单（`:55-90`）；job=0 骑士团 / 1 冒险家 / 2 战神 |
| 5 | `CHAR_SELECT` | `CharSelectedHandler.handlePacket`（`CharSelectedHandler.java:53`） | 校验角色归属，回 `getServerIP(ip, port, charId)`（`:105`） |
| 6 | 连频道 `PLAYER_LOGGEDIN` | `PlayerLoggedinHandler` | 下发全量角色数据（属性/背包/技能/任务/键位） |

角色列表包结构（`PacketCreator.getCharList`，`util/PacketCreator.java:901`）：

- `status(byte)`
- `count(byte)` + 每角色 `addCharEntry`：stats + look + rank
- `addCharStats`：等级、职业、STR/DEX/INT/LUK、HP/MP、AP/SP、EXP、人气、地图、出生点
- `addCharLook`：性别、肤色、脸、头发、装备（含武器）、宠物
- `characterslots(int)`：每账号角色位

本项目的 `GameSession` / `InProcessTransport` 已按同一语义实现；本次不改服务端，只修 UI。

## 2. 客户端布局机制（源端）

`clien/config.ini`：

```ini
width=800
height=600
EzorsiaV2WzIncluded=true
CustomLoginFrame=true
bigLoginFrame=true
```

`clien/ijl15.dll` 中的实际分支（地址为 DLL 基址 `0x10000000`）：

- `fcn.10015230` 读取 `CustomLoginFrame` → `0x1003651b`
- 同一函数读取 `bigLoginFrame` → `0x1003656b`
- `fcn.10010110`：
  - `bigLoginFrame != 0` 时**跳过** 0x10011f8d–0x10012060 的小框布局补丁，直接使用 exe 原生大框布局
  - `CustomLoginFrame != 0` 时把登录 frame 的偏移改成 `-frameWidth/2, -frameHeight/2`，即 frame 以窗口中心为锚点
- `fcn.10013c10` 按分辨率选择 frame：
  - `UI/MapleEzorsiaV2wzfiles.img/Common/frame1024` 1024×768
  - `frame1280` 1280×720
  - `frame1366` 1366×768
  - `frame1600` 1600×900
  - `frame1920` 1920×1080（图片实际是 1920×1080 画布中央放 1280×720 书框）

### 设计稿模型（由源端截图反推并交叉验证）

源端截图尺寸 1920×869。对 UI 素材做带透明遮罩的归一化互相关，得到：

- 统一缩放：`s = 869 / 720 ≈ 1.207`
- 设计稿：**1280×720**
- 屏幕映射：`screen = design × s + ((1920 - 1280×s)/2, 0) ≈ design × 1.207 + (187.5, 0)`
- 书框占 x≈188..1731、y=0..869，两侧 188px 黑边；**frame 不要铺满 1920 宽**。

验证样本：

| 元素 | 原生尺寸 | 截图尺寸 | 结论 |
|---|---|---|---|
| `WorldSelect/scroll/0/3` | 513×401 | 约 616×481 | ×1.207 |
| `Obj/login.img/CharSelect/signboard/0/0` | 133×238 | 约 160×287 | ×1.207 |
| `Title/BtLogin/normal/0` | 95×45 | 约 111×54 | ×1.207 |
| `NewChar/charSet` | 225×377 | 约 270×452 | ×1.207 |

因此实现时只需按 1280×720 设计坐标排布，整组乘 `s` 并居中；不要把 800×600 的 v83 坐标直接拿来用。

## 3. 背景资源与相机

### 3.1 登录 / 选区 / 选角共用背景

`Map/Back/login.img/back/11` 是 1280×2540 的整张树景（巨树、树屋、木桥、草地）。源端不是按整图缩放，而是按相机高度裁切 1280×720：

| 界面 | 相机 y（1280×720 视口） | 匹配依据 |
|---|---|---|
| 登录 | 约 1792 | 巨树底部大蘑菇 patch，`NCC=0.992 @ x1.2` |
| 选角 / 种族选择 | 约 592 | 树屋/绿门 patch，`NCC=0.949 @ x1.2`；桥面 patch 落点 (793,483) |
| 选区 | 约 1192 | 树干 patch 落点 (190,10)，树屋 patch 落点 (371,191)，与 1192 公式误差 <2px |

即：

```
draw back/11 at design (0, -cameraY), native 1280×2540
```

登录画面：巨树+草地；选角：树屋+木桥；选区：树干中段+远山。不要再用 `back/16` 当登录背景。

天空不是 `back/29`：源端截图里选区/选角的晴空是饱和蓝（选区约 RGB 148,189,255，选角约 RGB 89,138,255，建号约 RGB 56,112,255），`back/29` 是灰白云，与截图不符。晴空取客户端清屏色，云层用 `back/27`（纯 alpha 白云图）缓慢横向漂移；`back/9` 是单朵带阴影云，可作为近景补充。

### 3.2 建号背景

建号截图与 `Map/Back/login.img/back/16`（1280×720 树屋+蘑菇灯）匹配：

```
screen = 1.2 × src + (186, -29)
=> design = src + (-5, -26)
```

即 `draw back/16 at design (-5,-26)`。

## 4. 每屏素材与布局表（1280×720 设计坐标）

坐标均为**素材左上角**。UI 素材按原生尺寸绘制，整组乘 `s`。

### 4.1 登录

背景：`back/11` 相机 y=1792。frame：`UI/Login.img/Common/frame`（1280×720，按设计稿缩放居中，两侧留黑边，**不要铺满窗口**）。

| 元素 | 素材 | 设计坐标 | 原生尺寸 |
|---|---|---|---|
| 木牌 | `Map/Obj/login.img/Title/signboard/0/0` | (563, 270) | 368×236 |
| Logo | `Map/Obj/login.img/Title/logo/0/0` | (431, 81)，静态 alpha≈0.62，光束压在上面 | 397×219 |
| 连接 | `UI/Login.img/Title/BtLogin` | (835, 283) | 95×45 |
| 保存账号勾选框 | `UI/Login.img/Title/check/0\|1` | (640, 362) | 18×23 |
| 保存账号 | `UI/Login.img/Title/BtLoginIDSave` | (659, 362) | 51×23 |
| 密码修改 | `UI/Login.img/Title/BtLoginIDLost` | (766, 360) | 59×23 |
| 找回密码 | `UI/Login.img/Title/BtPasswdLost` | (848, 362) | 56×23 |
| 新的帐户 | `UI/Login.img/Title/BtNew` | (636, 410) | 92×38 |
| 网站 | `UI/Login.img/Title/BtHomePage` | (736, 408) | 93×38 |
| 结束 | `UI/Login.img/Title/BtQuit` | (836, 408) | 81×40 |
| 账号输入 | 文本 | (690, 296, 135×27) | 木牌槽内，垂直居中 |
| 密码输入 | 文本 | (690, 322, 135×27) | 木牌槽内，垂直居中 |
| 版本 | `Ver. 0.83`（源端） | (1063, 8)，深灰，约 9px | — |

木牌内槽（素材坐标）：账号 (115,28)–(265,46)，密码 (115,55)–(265,74)。输入文字避开左侧烘焙的“账号/密码”字样。

6 个 `Title/effect` 帧不是同一坐标：模板匹配得到的左上角分别约为 `(766,0) (906,36) (906,15) (990,0) (905,0) (912,2)`，按约 0.9 alpha 叠在 logo 之上。

### 4.2 选区

背景：`back/11` 相机 y≈1192。frame 按设计稿缩放居中。

| 元素 | 素材 | 设计坐标 |
|---|---|---|
| 步骤页签 | `UI/Login.img/Common/step/1` | (0, 33) |
| 悬挂招牌 | `Map/Obj/login.img/WorldSelect/signboard/0/0` | (338, -2) |
| 卷轴（关） | `UI/Login.img/WorldSelect/scroll/0/0` | (396, 139) |
| 卷轴（开） | `UI/Login.img/WorldSelect/scroll/0/3` | (396, 139) |
| 世界木牌 | `UI/Login.img/WorldSelect/BtWorld/<worldId>` | (472 + 28×i, 164)，共 15 格；无世界的格子取 `BtWorld/t20/normal/0` 的绳结（y 0–20）和素木纹（y 88 单行拉伸）拼成空板，避免露出模板自带的世界徽记/韩文 |
| 内容底 | `UI/Login.img/WorldSelect/BtWorld/chBackgrn` | 卷轴 + (39, 158) |
| 世界标题 | `UI/Login.img/WorldSelect/world/<worldId>` | 卷轴 + (73, 141) |
| 频道按钮 | `UI/Login.img/WorldSelect/channel/<i>/<normal\|disabled>` | 卷轴 + (74, 211)，4 列×5 行，固定 20 个 |
| 频道负载条 | 客户端绘制 | 单元格 +(6,14)，8×9 蓝条（仅可用频道） |
| 进入频道 | `UI/Login.img/WorldSelect/BtGoworld` | (756, 497) |
| 公告面板 | `UI/Login.img/WorldNotice/BalloonForLoginProcess/*` + `Title` | (288, 302)，186×76 |
| 查看世界地图 | `UI/Login.img/WorldSelect/BtViewAll` | (0, 125) |
| 查看所有角色 | `UI/Login.img/ViewAllChar/BtVAC` | (0, 374) |
| 回到首页 | `UI/Login.img/Common/BtStart` | (0, 425) |

绘制顺序：卷轴 → `signboard` → 内容 → 世界木牌 → 公告。源端木牌压在卷轴滚筒上，招牌压住滚筒上半部。

### 4.3 选角

背景：`back/11` 相机 y≈592。

| 元素 | 素材 | 设计坐标 |
|---|---|---|
| 步骤页签 | `UI/Login.img/Common/step/2` | (0, 33) |
| 世界牌 | `UI/Login.img/Common/selectWorld` | (0, 96) |
| 信息卷轴 | `UI/Login.img/CharSelect/scroll/0/3` | 槽位 x − 120, y=195 |
| 属性表 | `UI/Login.img/CharSelect/charInfo` | 卷轴 +(18, 30) |
| 告示牌 | `Map/Obj/login.img/CharSelect/signboard/0/0` | (795, 189) |
| 开始游戏 | `UI/Login.img/CharSelect/BtSelect` | (826, 207) |
| 创建角色 | `UI/Login.img/CharSelect/BtNew` | (826, 245) |
| 删除角色 | `UI/Login.img/CharSelect/BtDelete` | (826, 293) |
| 角色脚底 | 槽位 0/1/2 | (492, 429) / (617, 429) / (742, 429) |
| 空槽剪影 | `UI/Login.img/CharSelect/character/1/0` | 对齐脚底 |
| Tab 提示 | 客户端自绘系统气泡（非 IMG） | (507, 108)，232×33 |
| 查看所有角色 | `UI/Login.img/ViewAllChar/BtVAC` | (0, 374) |
| 回到首页 | `UI/Login.img/Common/BtStart` | (0, 425) |

角色预览由原版部件合成：Body（男 00002000 / 女 00002001）、Head 00012000、Face、Hair（含发色）、上衣/裤/鞋/武器；脚底锚点 = 合成帧 pivot。

属性值列：左列 x+43，右列 x+132（相对 `charInfo`），排行榜行 x+14、y+101 居中。

### 4.4 建号（两步）

背景：`back/16` at design (-5,-26)，下方清屏色约 RGB(56,112,255)，云层 `back/27`。

第一步（角色名）：

| 元素 | 素材 | 设计坐标 |
|---|---|---|
| 步骤页签 / 世界牌 | `Common/step/3` / `Common/selectWorld` | (0, 33) / (0, 96) |
| 角色名木板 | `UI/Login.img/NewChar/charName` | (720, 155) |
| 名称输入 | 文本 | (750, 258, 150×35) |
| 确定 | `UI/Login.img/NewChar/BtYes` | (747, 333) |
| 取消 | `UI/Login.img/NewChar/BtNo` | (821, 333) |

第二步（设定角色）：

| 元素 | 素材 | 设计坐标 |
|---|---|---|
| 设定角色木板 | `UI/Login.img/NewChar/charSet` | (720, 155) |
| 外观行底板 | `UI/Login.img/NewChar/avatarSel/<row>/<normal\|disabled>` | (731, 261 + 17×row)，200×17 |
| 左/右箭头 | `UI/Login.img/NewChar/BtLeft` / `BtRight` | (792, y+1) / (923, y+1) |
| 行数值 | 原版中文名 | (800, y, 120×17) 居中 |
| 确定 | `UI/Login.img/NewChar/BtYes` | (757, 485) |
| 取消 | `UI/Login.img/NewChar/BtNo` | (832, 485) |

两步的角色预览都在脚底 (636, 398)、缩放 1.25。

外观行（源端截图中的顺序与标签）：

| row | 标签 | 选项来源 |
|---|---|---|
| 0 | 脸 | `ItemConstants.isNewCharDefaultFace` 白名单 |
| 1 | 发型 | `isNewCharDefaultHair` |
| 2 | 发色 | `isNewCharDefaultHairColor` |
| 3 | 肤色 | `isNewCharDefaultSkinColor` |
| 4 | 上衣 | `isNewCharDefaultTop` |
| 5 | 裤子 | `isNewCharDefaultBottom` |
| 6 | 鞋子 | `isNewCharDefaultShoes` |
| 7 | 武器 | `isNewCharDefaultWeapon` |
| 8 | 性别 | 男/女 |

战神（job=2）使用固定套装：衣 1042167 / 裤 1062115 / 鞋 1072383 / 武器 1442079；脸型使用 `isNewCharDefaultFace` 的 Aran 分支。

## 5. 验收与剩余差异

已按 10 张源端截图逐屏做带透明遮罩的模板匹配，以下位置为像素级对齐（误差 ≤2 设计像素）：

| 界面 | 元素 | 源端截图落点（1920×869） | 本实现设计坐标 |
|---|---|---|---|
| 登录 | 木牌 | (870,327) | (563,270) |
| 登录 | 连接 | (1195,342) | (835,283) |
| 登录 | 新的帐户 / 网站 / 结束 | (955,495) / (1076,492) / (1196,492) | (636,410) / (736,408) / (836,408) |
| 登录 | 勾选框 | (960,437) | (640,362) |
| 选区 | 步骤页签 | (188,40) | (0,33) |
| 选区 | 世界木牌 | (757,198) | (472,164) |
| 选区 | 世界标题 | (754,338) | (469,280) |
| 选区 | 进入频道 | (1100,600) | (756,497) |
| 选区 | 三个左侧菜单 | (188,151) / (188,452) / (188,513) | (0,125) / (0,374) / (0,425) |
| 选角 | 步骤页签 / 世界牌 | (188,40) / (188,116) | (0,33) / (0,96) |
| 选角 | 告示牌 | (1151,231) | (795,189) |
| 选角 | 信息卷轴 | (637,235) | (372,195) |
| 选角 | 空槽剪影 | (932,517) / (1083,517) | (617,429) / (742,429) |
| 建号 | 角色名板 / 设定角色板 | 两块都在 (1057,187) | (720,155) |
| 建号 | 名称确定/取消 | (1090,402) / (1179,402) | (747,333) / (821,333) |
| 建号 | 外观确定/取消 | (1102,585) / (1192,585) | (757,485) / (832,485) |

弹窗（源端截图 1/3）：

| 弹窗 | 元素 | 源端截图落点（1920×869） | 本实现设计坐标 |
|---|---|---|---|
| 性别 | 卷轴外框 | (906,192) 427×241 | (595,159) 354×200 |
| 性别 | 确定 / 取消 | (1136,376) / (1223,376) | (785,310) / (857,310) |
| 声明 | 外框 | (720,0) 约 487×500 | 素材 (435,0) 403×414，可见纸面约从 441 开始 |
| 声明 | 确定 / 取消 | (1022,454) / (1111,454) | (691,377) / (766,377) |
| 声明 | 右侧滚动条 | x≈1170..1185，止于按钮上方 | x≈814..826，y 37..357（`VScr6` 12 宽），不压“取消” |

数据来自服务端、因此不强行改 UI 去对齐截图的项：

- 大区数量与名称：源端截图有 2 个大区；本地默认库是 1 个大区（未来岛）。界面按 `world_prop` 数据自适应（最多 15 块木牌）。
- 频道：源端截图显示 20 个频道。本地默认库只有 3 个可用频道，界面仍固定画满 20 格：前 3 格用 `channel/<i>/normal` 且可点击，第 4–20 格用 `channel/<i>/disabled`，点击提示“该频道尚未开放”，选频道/进入频道只允许前 3 个。所有 20 组 `channel/0..19/{normal,disabled}` 都是源端 IMG 自带。
- 世界名：源端截图为 BERA / 蓝蜗牛 / 蘑菇仔；本地默认值是世界 0 的 `event_message`（未来岛）。
- 角色属性/排名：源端截图显示“排行不可用”，本地单机有角色排名数据。
- 角色预览外观：随账号角色存档变化。

### 5.1 登录弹窗（已实现）

| 弹窗 | 触发 | 素材与坐标（1280×720 设计稿） |
|---|---|---|
| 性别选择 | 点击“新的帐户” | `Gender/scroll/0/1` (595,159) 354×200；`Gender/text/0` (617,185)； `Gender/BtYes` (785,310) 75×32；`Gender/BtNo` (857,310)；下拉框值 男/女画在 text/0 的空框内 |
| 《BeiDou》开源声明 | 首次启动（`PlayerPrefs.login_notice_ok=0`） | 素材外框 (435,0) 403×414（可见纸面约 441）：`NoticeScroll1/0` 卷轴头（左右各外扩 12），纸面用 `Gender/scroll/0/1` 的空白纸区做 9-slice 拉伸，滚动条用 `Basic.img/VScr6/enabled/{base,thumb0,prev0,next0}`，按钮用 `Gender/BtYes\|BtNo`；确定按钮初始 disabled，滚到底部才可用 |

- 两个弹窗都是模态：底层按钮、输入框、回车登录全部被拦截；性别弹窗可用 Esc 关闭。
- 声明正文按源端截图逐行排：标题、两行正文、GitHub 地址、空行、开发团队、日期；滚动范围与源端一致（初始只露出团队行，滚到底露出日期、确定变亮）。
- 声明滚动：鼠标滚轮、PageUp/PageDown、方向键、点击滚动条箭头、点击滑轨跳转、鼠标拖动、手机上下滑动正文均可；滚到 98.5% 即视为到底，“确定”变亮。滚动条在按钮之后绘制且止于按钮上方，底部箭头/滑块不会压住“取消”的字。
- 输入框：`GUI.skin.settings.cursorColor` 设为纯白、`cursorFlashSpeed=1`，点击账号/密码框可获得可见的闪烁光标；选中文本为淡蓝高亮。
- 性别确认会写入 `PlayerPrefs.login_new_gender`，并作为之后 `ResetAppearance` 的默认性别；同时清空账号/密码输入并聚焦账号框。
- 声明点“确定”写入 `PlayerPrefs.login_notice_ok=1`；点“取消”只关闭本次，下次启动仍会提示。
- 验收：`LoginUiReview` 会先重置 `login_notice_ok` 截“声明顶部”，再用滚动条下箭头连点 6 次到底并截“声明底部”，然后点“确定”关闭；之后点“新的帐户”截“性别/女”。当前实测日志：`notice arrow scroll=1`、`notice confirm click=True`、`gender confirm click=True newGender=1 pref=1`、`account field focus=login_account`（输入框焦点与光标）、`channel locked click index=0 pick CH.2 index=1`。

### 5.2 本轮回归结果（2026-10-23）

- 编译：`tools/unity-compile-check dotnet build -t:Rebuild`，0 warning / 0 error。
- UI 验收：`LoginUiReview.Run` 截图 `/tmp/mapledash-login-{notice,notice-bottom,gender,gender-female,login-caret,login,world,world-open,charselect,raceselect,charcreate,charcreate-look}.png`，全部 PASS；断言包括：`notice arrow scroll=1`（滚动条下箭头能滚到底）、`notice confirm click=True`（到底后确定可点）、`gender confirm click=True newGender=1 pref=1`、`account field focus=login_account`（账号框点击后拿到焦点、截图中可见白色光标）、`channel locked click index=0 pick CH.2 index=1`。
- 实机：`PlaySmokeTest.Run -smokeMap 10000` 自动登录成功，进入 10000（刷怪点 0、传送门 8），30 秒正常退出，无异常；首个开源声明弹窗不影响 `autostart` 自动化。

## 6. 落地约束

1. 所有位图继续走 `WzAssets` / `Art.Tex("wz://…")` 直接读原始 IMG；不导出 PNG，不绘制替代色块。
2. 坐标按本文件第 4 节；`UiKit` 已把虚拟高度固定为 720，设计稿 1280×720 在虚拟坐标里按 1:1 排布，再整体乘 `Screen.height/720`。**frame 必须按设计稿画，两侧留黑边，不能铺满窗口**。
3. 背景按第 3 节相机 y 绘制 `back/11` / `back/16`；晴空用清屏色，云层用 `back/27`。
4. 角色预览使用原版部件合成；脚底锚点按合成帧 pivot。
5. 服务端流程与包序不变（本文件第 1 节），只改客户端表现层。


## 7. 职业选择 / 建角再次审计（2026-10-23）

本节区分原始资源事实与既有移植布局推断，不能用前面的“已验收”描述代替本轮真机验证。

### 资源链路

- `LoginFlow.Tex` → `Art.Tex("wz://…")`，职业卡、说明板、选择按钮、角色名木板、外观木板、左右箭头均读取客户端原始 `UI/Login.img`。
- `DrawAt` 以原图宽高绘制，`Button/DrawState` 则使用代码指定矩形；因此用了 IMG 仍可能因矩形坐标、大小、origin 使用错误而错位。
- `DrawCharacter` → `WzCharacter.Appearance` → `Compose`：读取身体、头、脸、头发及装备 IMG，按部件锚点和层次合成纹理。不是手画人物，也不是单张完整人物 IMG。
- 外观名称、输入文字、状态提示由 GUI 文本绘制；天空清屏、频道负载条、频道选中框由代码绘制。并非所有屏幕像素都来自 IMG。

### 本轮直接读取原始 IMG 的结果

| 节点 | 尺寸 | origin | 发现 |
|---|---|---|---|
| `RaceSelect/{normal,knight,aran}/text/0` | 579×163 | (0,0) | 说明文字与星级已烘焙在图内，右下留有选择按钮槽 |
| `RaceSelect/BtSelect/normal/0` | 73×29 | (36,14) | 旧代码画在 (600,540)，脱离说明板的右下槽 |
| `RaceSelect/textGL/0` | 182×39 | (91,19) | 标题使用 origin 锚定 |
| `NewChar/charName` | 201×224 | (0,0) | 木板及名称槽来自原图 |
| `NewChar/charSet` | 225×377 | (0,0) | 中部留白由 avatarSel 行、选项文本和箭头填入 |
| `NewChar/avatarSel/0..8` | 200×17 | (0,0) | 各行是原图，选项值为动态文本 |

### 修正

1. 选择按钮按说明板局部坐标约 (412,111) 放入右下槽；说明板仍为设计坐标 ((1280−579)/2,430)。这是依据 IMG 留白校正，不声称为反编译得到的原生窗口坐标。
2. 手机角色名不再创建会直接响应触摸的 GUI.TextField，统一使用相对鼠标 Pointer 双击显式打开系统键盘；键盘内容同步回角色名，离开名称步骤时关闭。
3. 预览原先错误地按性别选择身体 00002000/00002001，并把头固定为 00012000，且遗漏肤色。现身体采用 2000+skin、头采用 12000+skin，建角使用所选肤色，选角使用角色记录肤色。

### 仍需明确的限制

- 本机 clien 目录没有职业选择/建角客户端 C++ 源码；Java 服务端可核查职业编号、创建字段和默认外观校验，不能证明 UI 坐标。完整原生窗口坐标仍需对 EXE/DLL 反汇编或源端完整截图比对。
- 当前职业卡三列间距、建角角色脚底 (636,398) 和 1.25 倍缩放依然是移植端布局参数，本轮没有足够证据重新确定它们。
- `CharPreview` 对已创建角色的衣服/武器仍使用默认套装，未从实际装备还原；因此它也不是存档角色完整外观的严格复现。本轮未扩展数据接口。
- 本轮做了真实 IMG 读取、尺寸/origin 核查、代码路径检查及 diff 检查；未运行 Unity/Android 视觉验收。不能把它描述为已经完全还原源端。


## 8. 建角人物比例复核（源端/移植截图）

本轮对照用户提供的两张运行截图；两图裁切尺寸、角色外观和步骤不同，先用原始木板尺寸归一化，不能直接比较截图内人物的绝对像素高度。

| 量 | 源端（设定角色） | 移植端（角色名称） |
|---|---|---|
| 原始板体宽度（不含顶部金色挂杆） | charSet 约225像素 | charName 约201像素 |
| 截图板体宽度 | 约360像素 | 约300像素 |
| 推算截图缩放 | 约1.60 | 约1.49 |
| 截图人物头顶至脚底 | 约103像素 | 约133像素 |
| 归一化人物高度 | 约64像素 | 约89像素 |

测量为截图边界估算，非反编译常量；人物发型、脸型不同，也会改变可见包围盒。此表证明相对场景比例存在偏差，不能据此精确断言原生引擎使用某个浮点倍率。

代码中的确定事实：

- 背景、木板通过 R 映射到同一设计坐标，再由 UiKit 整体缩放。
- 建角 DrawCharacter 额外传入 1.25，人物宽、高及 pivot 偏移都再次放大25%；选角没有这个倍率。
- WzCharacter.Compose 的纹理来自各原生 IMG 的实际宽高及部件坐标，Sprite pivot 由合成包围盒计算；DrawCharacter 以 pivot 对齐脚底。
- WzAssets.Upload 使用 Point 采样；原生客户端在用户截图中的采样及最终截图缩放方式尚未核实，没有把 Point 改为 Bilinear 的证据。

修正：移除建角专有1.25倍率，采用原始合成帧大小，仅随场景整体缩放。脚底仍为(636,398)，缩小从脚底向上发生，不移动角色站立点。相对于修改前，宽高均变为80%。

本轮未声称完成源端EXE的角色层zoom/采样参数反编译，也未完成同外观、同分辨率运行截图回归。后续精确比较应使用同肤色、脸型、发型、装备和站立帧，避免把外观差异当作滤镜差异。


## 10. 职业群选择截图校正（2026-10-23）

依据用户提供的源端与移植端 1920×869 截图，以书框区域 (188,0)–(1732,869) 归一化到 1280×720：

- 源端使用 `Map/Back/login.img/back/11` 顶部区域（cameraY=0），不是选角页 cameraY=592 的橙色树屋/吊桥区域。职业页单独采用蓝色天空及上下云层；其他页面保留原裁切。
- 职业排列为左骑士团、中冒险家、右战神；职业索引与建号 jobGroup 映射不变。卡片左上角分别约 (344,122)、(536,130)、(752,124)。
- 标题 origin 锚点 (646,111)，说明板左上角 (360,352)，选择按钮随说明板放置于 (772,463)。删除源端图中没有的底部“返回选角”文字，Esc 仍可返回。
- 校验：直接解码原始 IMG 离线合成预览，核对树干、卡片与说明板位置；非 Unity/Android 实机截图。云层横向位置随动画时间变化，截图不能确定原生动画相位。未执行构建。


## 11. 角色设定颜色与登录动画审计（2026-10-23）

### 对比依据

直接读取 BeiDou `clien/Data` 的 `UI/Login.img`、`Map/Obj/login.img`、`Map/Back/login.img`；三者 SHA-256 与移植资源完全一致。目录中登录界面为 EXE/DLL 二进制，未找到可直接审阅的客户端窗口 C++ 实现。Java 服务端登录处理不能作为动画时序证据；本轮没有声称已反编译恢复所有原生窗口过渡。

### 查明并修复

- `NewChar/avatarSel/*/normal` 是浅米色，`disabled` 是棕色。旧逻辑把非当前行全部禁用，和源端截图不符；现在可编辑行全部使用 normal。选项字从15降至12设计像素，并裁切到值区域，避免长名称溢出箭头。
- 登录光束原始 delay 为 1000–3000ms，且含 a0/a1；旧逻辑每秒6帧且固定透明度，完全丢失呼吸变化。现在按毫秒累计选帧并在帧内插值 alpha。
- logo 原始首帧8000ms、a0=0/a1=255，repeat=-1；恢复一次淡入并保持末帧。
- 通用 UI 动画读取各帧 origin 并保持锚点；按钮悬停按40/40/100ms等原始时长播放一次，停在末帧，离开状态后重新进入可重播，避免固定10fps及把不同尺寸帧强行拉伸到相同矩形。
- 世界选择卷轴、性别卷轴、选角资料卷轴恢复资源序列展开；展开期间隐藏尚未容纳的内容。选角切换时重新播放资料卷轴。
- 选角光效与8帧脚下光圈恢复播放；人物预览使用武器 info/stand 对应站姿及身体各帧 delay，帧合成继续使用原始部件锚点。
- 职业群 OnAnimation 仅一帧；标题/世界/选角 signboard 也仅一帧，未添加无资源依据的摇摆动画。背景云层保留既有漂移参数；原生相机跨页移动、关闭卷轴、鼠标移动轨迹和角色眨眼的精确时序仍需源端视频/运行观测或反汇编，本轮没有虚构数值。

### 验证

Unity `LoginUiReview.Run` 完整运行 PASS：声明确认、性别确认、登录、频道锁定/切换均通过；新增原始光束0.75s与2.25s采样断言，确认帧号与alpha=120/255。保存选角、建角相隔时间的截图，裁切区域存在像素变化。实际 Unity 截图确认外观面板全部恢复 normal 浅米色。字号最后调整后另行C#编译检查。未构建或安装Android包，未声称与源端整段动画逐帧一致。
