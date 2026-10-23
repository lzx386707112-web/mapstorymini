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

- `tools/server-check` 本轮176项通过、0失败，含独立临时库的家族/学院/黑板/商城及勋章持有/装备回归。
- 已运行项目要求的全量NPC脚本与打包检查：存在既有失败（NPC：7语法失败、27运行失败、70 Java互操作；打包1183条中13条语法失败），不能报告全绿。单独原 `medalQuest.js --trace` 通过。
- 无APK构建或真机触屏/视觉验收。
