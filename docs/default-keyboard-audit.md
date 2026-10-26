# 默认键位实现核对

日期：2026-10-23。范围为 `KeymapDao.DefaultKeys/Types/Actions` 的40项。此表**不是全部功能完成的声明**，按键进入函数与完整系统迁移分开核对。

| 默认键 | 功能 | 当前实现边界 |
| --- | --- | --- |
| E | 装备窗 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| F7 | 发呆 | 已有：NativeKeyActions → PlayerCtrl.Express → SpriteAnim → 原WZ表情合成；本次未做真机视觉验收 |
| 1 | 对所有人 | 新增：切换真实频道、消息127字符/200ms限制、成员资格及离线检查；消息只在当前单机会话显示 |
| I | 道具 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| 2 | 对组员 | 新增：切换真实频道、消息127字符/200ms限制、成员资格及离线检查；消息只在当前单机会话显示 |
| 3 | 对好友 | 新增：切换真实频道、消息127字符/200ms限制、成员资格及离线检查；消息只在当前单机会话显示 |
| 4 | 对家族 | 新增：切换真实频道、消息127字符/200ms限制、成员资格及离线检查；消息只在当前单机会话显示 |
| 5 | 对联盟说 | 新增：切换真实频道、消息127字符/200ms限制、成员资格及离线检查；消息只在当前单机会话显示 |
| Q | 任务 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| W | 大地图 | 新增：原WZ区域图、地图点、当前区域、区域链接 |
| R | 好友 | 新增：离线申请/接受/分组/删除与持久化 |
| P | 组队 | 新增：本地队伍创建/解散，10级限制；没有多人同步 |
| LeftBracket | 界面 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| RightBracket | 快捷键 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| S | 能力值 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| G | 家族 | 已接入：创建成员确认、邀请/退出/驱逐、权限、职位/公告/徽章、扩容及持久化；保留源端人数/金币条件 |
| H | 悄悄话 | 新增：切换真实频道、消息127字符/200ms限制、成员资格及离线检查；消息只在当前单机会话显示 |
| K | 技能 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| L | 讯息 | 新增：本地会话打开/关闭、离线邀请拒绝；没有多人收发 |
| Quote | 对话板 | 已接入：共享聊天记录、频道/悄悄话输入与校验；现金黑板另走537类道具，不把道具所有权作为查看聊天记录的条件 |
| Backslash | 键盘设定 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| Z | 捡取 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| X | 坐下 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| C | 聊天 | 新增：切换真实频道、消息127字符/200ms限制、成员资格及离线检查；消息只在当前单机会话显示 |
| M | 小地图 | 新增：原miniMap画布、NPC/门/玩家定位、折叠/隐藏 |
| Alt | 跳跃 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| F1 | 受伤 | 已有：NativeKeyActions → PlayerCtrl.Express → SpriteAnim → 原WZ表情合成；本次未做真机视觉验收 |
| F2 | 微笑 | 已有：NativeKeyActions → PlayerCtrl.Express → SpriteAnim → 原WZ表情合成；本次未做真机视觉验收 |
| F3 | 苦恼 | 已有：NativeKeyActions → PlayerCtrl.Express → SpriteAnim → 原WZ表情合成；本次未做真机视觉验收 |
| F4 | 哭泣 | 已有：NativeKeyActions → PlayerCtrl.Express → SpriteAnim → 原WZ表情合成；本次未做真机视觉验收 |
| F5 | 生气 | 已有：NativeKeyActions → PlayerCtrl.Express → SpriteAnim → 原WZ表情合成；本次未做真机视觉验收 |
| F6 | 惊讶 | 已有：NativeKeyActions → PlayerCtrl.Express → SpriteAnim → 原WZ表情合成；本次未做真机视觉验收 |
| Space | NPC 对话 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| B | 怪物手册 | 新增：原卡片目录/说明/地图、拾取收集、5张上限、等级/封面落库 |
| Ctrl | 攻击 | 已有功能入口；本次未对整个子系统做完整原端一致性验收 |
| 6 | 对夫妻说 | 新增：切换真实频道、消息127字符/200ms限制、成员资格及离线检查；消息只在当前单机会话显示 |
| O | 寻找组队 | 新增：队长注册/停止搜索、等级范围校验；没有在线匹配 |
| F | 学院 | 已接入：关系树、邀请/接受/合并/分离、家训、声望、每日特权、召集/传送和经验/掉落倍率；保留在线与地图条件 |
| BackQuote | 商城 | 已接入：原Commodity/CashPackage及数据库覆写、三类余额、购买/赠礼/领取、现金仓库、有效期、愿望清单和栏位扩充；特殊道具使用子系统仍需逐项迁移 |
| Semicolon | 勋章 | 已接入：原QuestInfo目录、现有任务/脚本挑战及领奖、持有校验、-49装备槽、存档重读；不表示全部勋章挑战子系统均已验收 |

## 源端依据

- 默认表：源端角色建号默认键位，对应本地 `Server/Db/Dao.cs`。数字1原始默认是“对所有人”，表情原始默认是F1～F7；角色重绑后以角色自己的键位为准。
- 好友：`net/server/channel/handlers/BuddylistModifyHandler.java`、`V1.0.5__create_buddies.sql`。
- 组队：`net/server/world/Party.java:createParty`、`PartyOperationHandler.java`、`PartySearchStartHandler.java`。
- 聊天：`GeneralChatHandler.java`、`MultiChatHandler.java`、`WhisperHandler.java`、`SpouseChatHandler.java`、`MessengerHandler.java`。
- 怪物手册：`client/MonsterBook.java`、`MonsterBookCoverHandler.java`、`V1.0.23__create_monster.sql`；原 `Item/Consume/0238.img` 与 `String/MonsterBook.img`。
- 地图：原 `Map/WorldMap/*.img` 的 `BaseImg/MapList/MapLink`；各地图 `miniMap/canvas/centerX/centerY/mag`；`Map/MapHelper.img` 图标。

## 验证与限制

- 服务层离线回归覆盖好友、组队条件、消息校验、卡片/封面及重开库持久化。
- 本地仅有一个在线角色；不把其他存档角色当在线玩家，不声称支持多人广播、在线邀请或组队战斗。
- 窗口布局为当前Unity适配，使用原WZ画布与数据；尚未完成设备上的视觉/触屏验收。
- 商城不能只搬 `Commodity.img`：原 `CashShop` 还合并数据库 `modified_cash_item`，并依赖现金仓库、现金道具生命周期、礼包、赠送及三类余额。
- 家族/学院使用真实存档实体和在线会话登记。只有一名在线玩家时，原端要求其他在线玩家的创建/邀请/召集不会伪造成功。

## 本轮新增业务（2026-10-23）

- `NativeCommunityService` 翻译 `GuildOperationHandler/Guild`、`FamilyAdd/Accept/Separate/Precepts/UseHandler`、`FamilyEntry`。创建家族沿用配置默认6名同地图合格角色和150万金币，全体确认后扣费。徽章默认500万并校验总部地图。扩容沿用源端阶梯费用，成员上限100。
- 学院沿用每名学长最多2名直属同学、等级差配置、树合并/分离费用、学长声望贡献、每日使用记录、限时倍率和同学召集确认。战斗击杀与升级接入声望，经验/掉落读取限时倍率。源端 `InfoExRequirement/InfoNumberRequirement` 的部分行为本身是空校验，不另外臆造挑战要求。
- `NativeCashService` 通过 `INativeCashCatalog` 从原WZ加载目录，并以nullable数据库字段覆写。购买先校验目录、余额和仓库容量，再在事务内扣款发货；现金仓库按账号/大区/职业群隔离。领取礼物不会重复发货，取出/存入保留有效期和装备属性。恢复读档包含有效期，游戏中检查到期并移除。
- 勋章复用源端编号脚本与 `QuestLogic`，从 `viewMedalItem/medalCategory` 构建目录，持有后才能装备。奖励背包增减改为经传输层落库，避免只写共享Profile造成换角/重启丢失。`getMedalName` 不再返回空字符串。未接入的召唤宠物状态不会被当作满足宠物勋章条件。
- 对话板共享底栏消息历史、频道和发送校验；现金黑板单独沿用 `UseCashItemHandler` 的537类道具所有权、自由市场房间限制、不消耗和关闭逻辑。
- 数据库事务快照改为复制行值，使已存在行的扣款也能回滚；nullable覆写保持null与0的区别。

### 尚未覆盖的源端扩展

本轮实现了上述可操作业务，不声明整个服务端一比一迁移完成：联盟实体/联盟管理、家族BBS/家族战、宠物召唤与喂养、关系戒指、商城改名/转区/仓库扩容等特殊分支、以及依赖这些系统的全部勋章挑战尚未完整迁移或验收。商城道具购买/持有/到期与某一种道具的实际使用效果是不同路径，不把购买成功当作全部现金道具效果已经实现。

### 实际验证

- `tools/server-check` 本轮178项通过、0失败，含独立临时库的家族/学院/黑板/商城及勋章持有/装备回归，以及商城外原NPC脚本的点券奖励和余额不足回滚。
- 原NPC的 `cm.getCashShop()` / `player.getCashShop()` 已接入账号三类余额读取与增减，失败不形成负余额。
- Unity真实资源检查15项通过，键盘检查209项通过；原商城目录9074条（原文件存在两个同名9063节点，商品SN不同，均保留）。最新全量C#重编译0错误、0警告。
- 已运行项目要求的全量NPC脚本与打包检查：存在既有失败（NPC：7语法失败、27运行失败、70 Java互操作；打包1183条中13条语法失败），不能报告全绿。单独原 `medalQuest.js --trace` 通过。
- 无APK构建或真机触屏/视觉验收。

## 本轮按键完整性复核（2026-10-26）

上一张表只核到「按键能进到哪个入口」。这一轮按用户要求逐键核对**行为是否真的对**，发现两处确定的错，其余 38 项逐条走了一遍调用链未发现新的断点。

### 修掉的 1：Esc 不会关面板

- 原实现：`GameAction.Menu`（Esc）只做 `GameMenuWindow.Toggle()`，且整段动作派发被 `playing` 卡住（角色倒地、对话中、读图后都不响应）。面板开着时按 Esc 会在面板上再叠一个系统菜单。
- 现在：`GameWindows.TrackPanels()` 在绘制阶段观测各窗口的可见性变化，维护「打开顺序」；`GameWindows.CloseTop()` 关掉最后打开的那一个。`NativeKeyActions` 的 `Menu` 分支先尝试 `CloseTop()`，没有面板才开系统菜单（且要求已有角色，登录/读图阶段不弹）。
- Esc 的让路规则：键位窗、虚拟键编辑器、侧滑菜单、聊天输入、频道下拉、NPC 对话各自消费 Esc，`InputBridge` 在这些层不重复派发，避免一次按键关两层。
- 顺带把 `Menu` 从 `playing` 门控里提出来：面板开着 / 角色倒地时也必须能关窗。登录阶段由「没有角色」这一条挡住。
- 这是用户明确要求的产品行为；原端 Esc 只对应系统菜单，关窗靠各窗口的 X。已记入 `beidou-client-reverse/references/input-and-physics.md`，不当作原端逻辑。

### 修掉的 2：↓ + 跳跃不下跳

- 原实现：跳跃分派只看 `jumpReq`，按住 ↓ 时仍然是普通向上跳。
- 现在：`PlayerCtrl.BeginDownJump` 按源端 `0x94C4F8` 做门控——地图 `fieldLimit` 的 `CANNOTJUMPDOWN(0x20000)`、支持段的 `forbidFallDown`、必须站在可走段上、且该点下方还有严格更低的落脚点；通过后竖直速度取 `-jumpSpeed*0.35355339`（常量 `0xB3E3A0`），本帧清横向位移，并把该段记成 `dropSegment`，`FindLanding` 跳过它，落到别的段后清空（对应 `0x9B4963`）。
- 数据链补全：`FieldFoothold.forbidFallDown` ← `NativeMapView.LoadMap` 读 WZ `foothold/*/*/*/forbidFallDown`，`Art.Stage.ForbidFallDown` 与 `Footholds` 同序，经 `PlayerCtrl.SetTerrain(t, forbidFallDown)` 交给角色。全量统计：6337 张图 394859 条 foothold，225 张图共 2734 条段带该属性（另有 `cantGo` 2317、`cantThrough` 1711 尚未建模）。
- 注意：地图 IMG 的属性名是 XOR 加密的，`strings`/grep 查不到，必须用 WZ 解析器读（第一版误判为「数据里没有」就是踩了这条）。

### 逐键复核结论（未改动的 38 项）

| 键 | 功能 | 结论 |
| --- | --- | --- |
| F1～F7 | 7 个表情 | 通：`NativeKeyActions → PlayerCtrl.Express → SpriteAnim.Express → WzCharacter.ExpressionFrame` 真的把当前帧换成原 WZ `Face/<表情>`（本轮把节流改成原端核实值 2 秒，见下节） |
| Alt | 跳跃 | 通；新增 ↓+Alt 下跳 |
| Ctrl | 攻击 | 通；无目标也挥砍，快捷格占用同键时让位给技能 |
| Z / X / Space | 拾取 / 坐下 / NPC 对话 | 通（Space 与 ↑ 都能对话，对话里 Space/Enter 翻页、Esc 取消） |
| 方向键 | 移动 / 爬绳 / 传送门 | 通（↑ 进门与对话，↑↓ 爬绳，Terrain 存在时不做深度层切换） |
| E I S K | 装备 / 道具 / 能力值 / 技能 | 通；本轮起 Esc 可关 |
| \ | 键盘设定 | 通；窗口自己吃 Esc（含快捷格子窗与确认框） |
| Q W M | 任务 / 大地图 / 小地图 | 通；大地图本轮起 Esc 可关 |
| R P L O F G ; | 好友 / 组队 / 讯息 / 找组 / 学院 / 家族 / 勋章 | 通；社交与家族类窗口本轮起 Esc 可关 |
| B ` | 怪物手册 / 商城 | 通；商城关窗走 `NativeCashWindow.Reset()` 才会把 `timeScale` 还回去 |
| [ ] | 界面 / 快捷键 | 通（界面是整层 HUD 隐藏，快捷键是底部快捷菜单） |
| 1～6 H C ' | 各频道聊天 / 聊天 / 对话板 | 通 |

### 本轮验证

- 全量 C# 重编译 0 error / 0 warning（`tools/unity-compile-check`）。
- `NativeKeyboardChecks.Run` 222 项通过、0 失败（新增 Esc 分层关闭 5 项、`forbidFallDown` 读取 1 项）。
- `NativeCombinationChecks.Run` 通过：重复时钟、虚拟释放、↓+攻击、攻击后禁跳、跳后攻击、表情合成、下跳四态（穿下去/落到下层/无落点不下跳/forbidFallDown 不穿）。
- `MovementChecks.Run` 通过：原图绳梯与上层出口、全部 foothold 落地、30/60/120Hz 步长、悬崖与触摸归属。
- `FieldChecks.Run` 1684 通过 / 19 失败，失败项全部落在本轮未改动区域（底栏平铺与大按钮间距、转职页签、快捷格显示键、AP 分配、4 处 foothold 端点跟随）。最后一项属 `FollowGround` 判定，本轮只给 `FindLanding` 加了默认 `-1` 的跳过索引（等价原逻辑），未改 `FollowGround`。
- 无 APK 构建，无真机触屏/视觉验收：下跳与 Esc 都还需要在手机上看一眼实际手感与端序。

## 表情链路复核（2026-10-26 二轮，按 exe/IMG/服务端/脚本取证）

用户反馈「把表情移到 1/2/3 之后点了没反应」。这一轮不再从 Unity 侧猜，先按 `AGENTS.md` 的要求把四层依据取全，再改。

### 取证结果

| 层 | 依据 | 结论 |
| --- | --- | --- |
| 客户端 EXE | `0x94F2ED` 键位分派、`0x94F4D1` type6 分支、`0xA24470` SendEmotion、`0x451CF6` SetEmotion、静态名表 `0xBEC284`（初始化 `0x45E8E8..0x45EDFA`） | 键位表**按扫描码索引**（一码一条记录），type6 的 `data-0x63` 就是表情号；客户端节流 **2000ms**（`< 0x7D0` 丢弃）；合法 `0..22`；先本地换脸再发 0x33 包；type6 没有动作/状态门控 |
| IMG | `UI/UIWindow.img/KeyConfig/icon`（40 个：0..27、50..54、**100..106**）、`KeyConfig/key/<扫描码>`、`Character/Face/<id>.img` 的 27 个表情节点 | 100..106 就是 7 个基础表情的托盘图标 → 表情 1..7 |
| 服务端 | `RecvOpcode.FACE_EXPRESSION(0x33)`、`FaceExpressionHandler`、`Character.changeFaceExpression`、`PacketCreator.facialExpression` | `emote>7` 需要背包有 `5159992+emote`（5160000..5160014）；`emote<1` 丢弃；广播用 `false`（**不发给自己**），自己的脸只能靠客户端本地设置；1500ms 只是广播节流 |
| 脚本 | `NPCConversationManager`、`scripts-zh-CN` 全量检索 | **没有表情接口**，表情只有输入一条路径，脚本不会驱动表情 |

表情名表（23 项，顺序以 exe 为准，不能按 `Face.img` 节点罗列顺序推）：

`blink, hit, smile, troubled, cry, angry, bewildered, stunned, vomit, oops, cheers, chu, wink, pain, glitter, blaze, shine, love, despair, hum, bowing, hot, dam`

其中 14..18 是 `glitter/blaze/shine/love/despair`，而 `Face.img` 节点顺序是 `glitter/despair/love/shine/blaze`，两者不同。

### 改动

- `SpriteAnim`：名表换成上面 23 项；节流 `1.5s → 2.0s`；新增 `MaxEmote=22`、`EmoteDuration=5s`、`ClearExpression()`（表情 0 = 回默认脸）。
- `PlayerCtrl.Express`：`0` 回默认脸；`1..22` 合法；`8..22` 必须有 `5159992+emote`（对齐服务端门控），否则不显示。
- **键位一键一职**（原端 keymap 一码一行）：新增 `KeyMap.Claim`（读档去重）、`KeyMap.Apply`（整表应用去重）、`KeyMap.ExportCharacter`（固定顺序 + 按扫描码去重）。
  这条是「点了没反应」的根因候选：1 号键默认是「对所有人聊天」，若和表情共键，按 1 会先开聊天并抢走焦点，`playing` 变 false，之后所有动作都不再派发。
- `VirtualControls.ActionDisplayName`：按键位表中文名显示，屏上按键按下时的回执从「1 → FaceSmile」变成「1 → 微笑」/「1 → 对所有人」，真机上能直接看出键是否对上。

### 验证（真实游玩环境）

- `ExpressionChecks.Run` PASS：7 个基础表情逐像素差异 90/190/133/141/220/161/142；节流 1.6s 被挡、2.1s 放行；名表 23 项逐项一致；`0/23/-1` 拒绝；
  8 号无道具时不显示、塞入 `5160000` 后换成 `vomit`（顺带证明 8=vomit）；改绑 1/2/3 后键位表无重复扫描码，按下 1 换成 `smile`。
- 全量 C# 重编译 0 error / 0 warning。

### 真机仍然「没反应」时的三个检查点

1. 屏上按键按下时有没有出现「虚拟键 → 1 → 微笑」的浮条：没有 → 按键没被喂进游戏（编辑器还开着、侧滑菜单压着、或按钮没吃到触摸）；有但显示「1 → 对所有人」→ 按钮的键和表情的键不是同一个。
2. 按钮绑的键必须等于表情绑的键。把表情从 F1~F7 挪到 1/2/3 之后，之前绑 F1~F7 的按钮不会自动跟着改。
3. 连按两次会被 2 秒节流吞掉；表情只换脸（原生尺寸约 15×12 像素），对着头部看。

## 键位窗「搬功能」交互（2026-10-26 三轮）

用户反馈：键盘设定窗里把功能按钮挪到别的键，现在要「按住拖动」才算数（触屏还得先长按）。
要求与原端一致——**只有一种移动操作**：悬停在功能上变抓手，点一下按钮跟到鼠标上，在目标键位再点一下就落下。

### 依据

| 项 | 依据 | 结论 |
| --- | --- | --- |
| 光标素材 | `UI/Basic.img/Cursor` 共 14 组；`11` = 22×21 张开的手，`12` = 25×23 握拳 | 原图自带「可以抓」和「正抓着」两态，不需要自绘 |
| 原端表现 | 用户提供的原端截图：悬停在功能图标上是张开手，抓着「Alt 跳跃」时是握拳 | 悬停 = `Cursor/11`，抓着 = `Cursor/12` |
| 本地旧实现 | `KeyConfigWindow` 在 `Pointer.Pressed` 拿起、`Pointer.Released` 放下；触屏要先按住 350ms 才产生左键按下（`InputHub.ReadDevice`） | 于是表现为「长按才能拖」，一次轻点等于原地放下 |

### 改动

- `KeyConfigWindow`：搬运改成「一次按下只做一件事」——手上有功能就放下，没有就拿起。
  两个动作都走 `Pointer.Pressed`，**不再依赖 `Pointer.Dragging`**，触屏单击即可，不用长按。
- 拿起后图标跟着光标走（松手也还在手上），原来那一格（键位或托盘）先空出来。
- 落到键位 = 换过去，原占这个键的功能回托盘（一键一职）；落到下方托盘区 = 收回托盘；
  点窗口空白 = 取消这次搬运，并且吞掉这次抬手，免得顺手按到底部按钮。
- Esc：手上有功能时只取消搬运，不关窗；手上空着才关窗。
- `NativeCursor`：新增 `Request(StateGrab / StateHold)`，窗口在 OnGUI 里登记，
  桌面走 `Cursor.SetCursor`、手机走自绘，两条路同一个来源。
- 双击「把功能退回托盘」的旧路径删除——退托盘现在也是同一次搬运（拿起 → 点托盘）。

### 验证

- 回归 `NativeKeyboardChecks.CheckKeyConfigCarry`：点一下拿起（键位不变）、再点一下落到 1 号键、
  被挤走的功能回托盘、一个键只留一个功能、其他键位不受影响、点托盘收回、Esc 取消搬运不改键位。

### 未核实

- 原端 `BeiDou.exe` 里键位窗的点击函数没有定位到，这条交互是**按用户指定的产品行为 + 原图光标素材**实现的，
  不声明「已证明与 EXE 完全一致」。
- 手机上的手感（单击拿起、点目标放下）尚未真机验收。
