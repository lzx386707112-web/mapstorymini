# SoloMapling 假人系统未完成项与接续实施说明

记录日期：2026-10-27。范围：北斗源端 SoloMapling → MapleDash Android 本地服务端及游戏显示。

**当前结论：系统未完成，不具备完整交付条件。** 此文是接续工作说明，不是发布说明。用户要求完整迁移，包括商人、组队战斗和前端显示；这些仍全部属于任务范围，不能因为本次记录文档就被取消。

## 1. 状态如何判断

- **已接入、待完整验收**：有实际运行入口或真实实体消费者，但只验证了明确列出的路径。
- **部分迁移**：已有逻辑、资源、协议或辅助类，仍缺必要业务、调用者或显示。
- **确认占位**：当前类型落到 `GenericBot`，或方法恒返回空；不能计为已迁移完成。
- **待逐项复核**：旧清单的映射尚未按当前代码核对；不能据此断言完全没有实现，也不能据此关闭任务。
- **源端本身无行为/异常**：保留原端行为，不擅自发明实现；需要与真正未迁移区分。

当前 `BotTypeManager.CreateAndSetBot` 的17种枚举中，8种已映射具体行为，9种仍落入 `GenericBot.UpdateState()` 的站桩占位。**8/17是类型映射数，不是系统完成率，也不代表8种已全部验收。** 8种中买卖商人共用一个 `MerchantBot`，不可按类文件数重新计算。

相关资料职责：

| 资料 | 用途 | 注意事项 |
| --- | --- | --- |
| 本文 | 当前缺口、影响、接续顺序和关闭条件 | 后续每完成一项同步更新 |
| [迁移源文件清单](solomapling-port-manifest.json) | 源文件SHA256、C#映射、保守状态 | 是历史映射；未逐项复核的标签不构成完整审计 |
| [逆向与迁移依据](../.agents/skills/beidou-client-reverse/references/solomapling.md) | 确定逻辑、原端身份、地址、验证边界 | 按时间追加，前文333/368/425等是历史检查点 |
| [早期总体分析](../../docs/solomapling-analysis.md) | 模块划分和原始迁移计划 | 不是当前完成证明，旧数量和mock client描述等应以新依据为准 |

本地路径：

- 项目：`/Users/lizixian/Documents/mxd/mxd-android/MapleDash`
- 源Java：`/Users/lizixian/Documents/mxd/Beidou-Server/gms-server/src/main/java`
- 源SoloMapling：上述目录内 `soloMapling/`
- 源客户端：`/Users/lizixian/Documents/mxd/Beidou-Server/clien/BeiDou.exe`
- 原资源：`/Users/lizixian/Documents/mxd/mxd-android/MapleDashResources/Data`
- 源脚本：`/Users/lizixian/Documents/mxd/Beidou-Server/gms-server/scripts-zh-CN`

## 2. 17种类型逐项状态

以下源类型路径均相对 `soloMapling/ArtificialPlayer/BotTypes/`；当前实现路径相对 `Assets/Scripts/Server/Bot/`。

| 类型枚举 | 源端 | 当前实际实现 | 未完成重点 |
| --- | --- | --- | --- |
| SOCIAL_BOT | SocialBot.java | Types/SocialBot.cs | 头顶气泡、原交互提示显示；反刷/告别等全路径验收 |
| TOWN_WANDERER_BOT | TownWandererBot.java | Types/TownWandererBot.cs | 原城镇对白文件未恢复可解析文本；外观/气泡完整显示 |
| FM_BOT | FMBot.java | Types/FMBot.cs | 店铺生产者、完整环境填充、椅子/坐骑/发言显示；不能重启源端已不再调用的旧购物分支 |
| SELLING_MERCHANT_BOT | SellingMerchantBot.java | Types/MerchantBot.cs，卖出模式 | 原交易UI、实际商店链路、完整商品业务运行验收 |
| BUYING_MERCHANT_BOT | BuyingMerchantBot.java | Types/MerchantBot.cs，买入模式 | 真人出售操作界面、完整收购/库存/报价路径验收 |
| NX_MERCHANT_BOT | NXMerchantBot.java | Types/NXMerchantBot.cs | 信使邀请/输入状态/交付显示，完整成交到兑换流程验收 |
| SCROLL_BOT | ScrollingBot.java | Types/ScrollingBot.cs | 物品喇叭UI、原商品交互；卷轴动画和音效已接，不再列为完全缺失 |
| COMPANION_GRIND_BOT | CompanionGrindBot.java | Types/CompanionGrindBot.cs | 完整角色属性、远程/魔法动作、状态倍率、掉落归属、实际游戏全流程 |
| DICE_BOT | DiceBot.java | **GenericBot占位** | 押注消息、原地面赌注识别、双骰、输赢、派彩、盗回赌注处罚及显示 |
| TUTORIAL_BOT | TutorialBot.java | **GenericBot占位** | 新人识别、原对白、分支、赠礼交易、帽子选择、武器掉落/强化/归还、送行 |
| GACHA_BOT | GachaBot.java | **GenericBot占位** | 原反应堆轮盘、奖池、事件反应、掉落回收、录制移动与显示 |
| HENESYS_BOT | HenesysBot.java | **GenericBot占位** | 原城镇FSM、概率/冷却、地图连接、拥挤权重选图、JQ转换 |
| HENESYS_JQ_BOT | HenesysJQBot.java | **GenericBot占位** | 原录制路线、失败层级/成功权重、恢复休息、离图及类型转换 |
| GAME_ZONE_HOST_BOT | GameZoneHostBot.java | **GenericBot占位** | 入图欢迎、点名交互、饮料选择/掉落、超时、告别 |
| BLACKJACK_DEALER | Blackjack/BlackjackDealerBot.java等 | **GenericBot占位** | 牌组/规则/AI/桌位、赌注、发牌决策、结算及牌面显示 |
| DROP_GAME_BOT | DropGameBot.java等 | **GenericBot占位** | 掉落小游戏FSM、奖池、围观者、物品所有权/回收/清理及显示 |
| OPQ_BOT | OPQ/OPQBot.java等 | **GenericBot占位** | 招募、组队队列、共享上下文、流程编排、消息、地图阶段和退出清理 |

`Types/AmbientMapBot.cs`已存在，但不是上述17枚举中的第18种已接入类型。此前源树核查未发现构造调用者；不能为增加数量而擅自自动生成。

## 3. 商人业务与交易：还缺什么

### M01 原版交易窗口与邀请交互【未完成】

已经具备：

- `Trade/BotTradeService.cs`：双边会话、真实库存/金币托管、税费、锁定、取消返还、事务结算。
- `Trade/BotTradeProtocol.cs`：Read、Invite、OfferItem、OfferMeso、Confirm、Cancel、Chat、Accept。
- 真人发起邀请进入假人业务队列；假人主动邀请可由真人经协议接受。
- 接受校验当前邀请方和接收身份；原30秒轮第6轮保留、第7轮过期；过期访问取消双边会话。
- `GameSession`、存档协议和真人同步的交易修订保护，防止旧快照补回货款或抹掉到账商品。

仍缺：地图假人选择入口、原邀请接受/拒绝界面、交易两边物品格与数量、金币输入、锁定按钮、物品详情、交易聊天、成功/取消/失败反馈，以及界面关闭与服务端取消的统一接线。

影响：后端测试能成交，不代表玩家已经能在游戏内完成交易。

关闭条件：按原IMG节点及EXE布局实现，实际游戏内完成买入/卖出/主动邀请三条流程；验证取消、超时、满包、锁定后操作、离图、换频道、退出和重复请求均不丢失或复制物品/金币。完整核实服务端恢复能力，不以当前快照回归代替全部保存/重开验收。

### M02 市场店铺与议价系统【未完成，旧清单多项待复核】

源目录：`soloMapling/FreeMarket/`，重点为 `ArtificialFreeMarket`、`HiredMerchantArtificial`、`HiredMerchantAdapter`、`PlayerShopAdapter`、`ShopKeeper`、店铺信息/标题/类型管理，以及 `ShopOfferSystem/`。

当前商品池、估价、买卖商人FSM与市场共用调度已经存在；这些不等于店铺创建、上架、购买、补货、店主状态、营业收入、关店及议价已完整迁移。`MarketBotAmbient.KeepsShopChair`仍需实际店铺生产者。

关闭条件：逐条核对源端活跃调用链，接真实店铺库存、金币与生命周期，再按原UI走通；源端不可达的旧浏览分支记录为不可达，不擅自启用。

### M03 点券商人与信使交付【部分迁移】

已具备原格式兑换码生成、持久化、防重复兑换；type4实际发nxCredit，不能照源注释误称prepaid。信使三人房、邀请、接受/拒绝、消息、typing、离开及超时后端已存在。

仍需：原信使邀请界面、消息和typing完整显示、商人等待/取消/停止中断的实际UI验收；从交易成功一直走到收到码、兑换后账户余额变化及重开持久化。其他兑换类型没有因此完成。

### M04 卷轴商人与物品喇叭【部分迁移】

已具备具体ScrollingBot、原预生成强化结果播放、成功/失败/诅咒原IMG与音效。源listIndex未递增，当前保持第0件行为；显示失败发CURSE，不再次强化装备。

仍需：`BotOutput.Kind.ItemMegaphone`的原版消费者、物品数据展示/详情、原报价进入交易的真实操作。音效只做原字节比对与播放入口接线，未做设备听音验收。

## 4. 组队、战斗、奖励：还缺什么

### C01 基友召集全流程【部分迁移】

已具备：原 `item/consume_2431159.js`执行，已有单人队伍的资格检查，召集5人，成功才扣道具，30分钟效果；原220ms轮、800ms攻击冷却、楼层与目标认领、同频道跟图，离队/换频道/退服/到期清理；真实NativeMobController扣血和假人贡献。

仍需：完整角色属性与战斗能力、技能呈现、状态效果与奖励全链路、实际游戏道具/队伍窗口验收。当前“普通怪伤害真实生效”不能替代完整战斗系统。

入口：`BotPartyGrindService.cs`、`Types/CompanionGrindBot.cs`、`BotPartyGrindProtocol.cs`、`Field/BotCombatField.cs`。

### C02 角色属性、存活与受击【未完成】

当前有职业、等级、四属性、装备原标量和移动属性；尚未完成与源Character一致的装备/套装/被动加成、HP/MP最大值和当前值、完整重算与恢复，以及接触/坠落伤害业务。`BotMovementService.ApplyFallDamage`存在边界，并不等于有实际伤害生产者。

源端原始基础HP/MP和完整属性计算必须核实，不能自行写等级公式。`updateStrDexIntLuk(primary)`给四属性同值是已核实源逻辑，不应自行改成“主属性加点”。

关闭条件：对照源Character与装备数据验证多职业、换装、死亡、恢复及移动受击行为，再接上所有调用者。

### C03 技能动作、投射物与效果【部分迁移】

当前有原职业技能选择、武器兼容/回退、伤害分段、普通武器动作桶、原effect IMG与生命期。**远程/魔法仍以普通挥击显示**，没有完成对应原动作、投射物、命中效果及完整方向/锚点合同。

关闭条件：逐类武器/技能核实原客户端包语义及IMG动作，验证弓/弩/拳套/枪/魔法/近战均不以错误动作或stand回退掩盖缺失，分段数字和伤害归属保持一致。

### C04 经验、状态倍率、掉落归属【部分迁移】

已接实际贡献/MVP、等级资格、common0.8/MVP0.2、队伍加成、Java舍入及合格存活条件；保留源端组队familyRep两次调用和个人一次的区别。

仍需完整HolySymbol、Showdown、EXP_INCREASE、EXP_BUFF及其他实际倍率/装备经验链路；怪物掉落的所有者、队伍拾取、宠物资格与任务条件也未完整对齐。不能继续给真人无条件整份经验或把假人伤害当真人伤害。

入口：`BotPartyExperience.cs`、`FieldDirector.OnEnemyKilled`、`EnemyUnit`；依据：源 `org/gms/server/life/Monster.java`。

### C05 自由市场Boss协作攻击【未迁移】

源：`ArtificialPlayer/BotBossCombatManager.java`。需要聊天触发词、市场Boss查找、最多10名攻击者、编队移动、1200ms攻击、伤害上限/保底、真人伤害归属与退出条件。

特别注意：源Boss业务把伤害归给可用真人，与普通基友刷怪的假人贡献路径不同；不能直接复用后者而丢失归属语义。当前有技能选择辅助函数不等于Boss管理器已接线。

### C06 队伍招募、OPQ与社交显示【部分基础服务，业务未完成】

`BotPartyService`已接真实partyId、最多6人、邀请/应答/超时、离队/解散和移动疾行来源。`NativeSocialWindow`显示实际成员人数/队长并刷新。

仍需完整招募管理、原队伍队列调用者、OPQ流程与地图编排；原邀请通知的接受/拒绝显示、成员状态、队伍交互也要验收。不能给所有假人擅自加自动接受组队逻辑。

## 5. 地图环境、移动与掉落依赖

### E01 完整三波启动【未完成】

源 `EnvironmentManager.marketEnvironmentStartup`：

1. 第一波：市场房间填充、入口分配假人、市场社交假人。
2. 第二波：射手村Gacha、Blackjack桌、OPQ大厅。
3. 第三波：城镇游荡者。

随后还有按配置启动的活动、装饰队列、装备检查及环境配额结束。当前城镇生成器、观察者、市场调度均已有实现，但完整三波启动未接通。不能仅启动第三波就称自动环境完整。

需补配置开关、一次性启动/失败重试、配额、跨波次依赖、退出清理；检查自动入口而非只测手动创建。

### E02 原录制导航、移动锁与跟随【部分迁移】

已有地图几何、物理/导航、BIN/CSV回放、观察者节流、原微步/转身与地图切换清理。仍缺完整MovementCommands锁/优先级、pathFinderAware行为、完整跟随/旅行/移动技能与受击，以及客户端原移动包插值消费。

Henesys/JQ/教程等依赖原录制和导航，不能用任意随机移动、瞬移或通用AI替换后称已按源迁移。主线程适配允许将原阻塞等待改为可取消时序，但必须保留先后与取消语义。

### E03 地面物品服务【明确缺口】

`BotLogic.CheckForItemsOnFloor`仍返回空列表。`DropCommands`原掉落、投掷、所有者、永久所有者、数量、到期、宽限回收及按对象拾取未完整迁移。当前 `GroundItem`并非这套完整服务，仍含既有本地效果/图标回退，不能未经核实直接当作原版假人掉落实现。

影响：Dice赌注、教程武器归还、GameZone饮料、DropGame、Gacha轮盘回收均不能形成业务闭环。

关闭条件：存在权威地图物品ID与实际库存来源，保留装备完整属性、所有权/拾取/过期规则，真实拾取和回收无复制/丢失；前端用原IMG显示，不新增自绘占位。

### E04 反应堆、奖池、活动和支撑服务【未完成或待复核】

源 `MapVFX/CustomReactor`、`itemPool/GachaFillerSystem`、`ConversationManager`、`ConversationScript`、`SocialHotPotatoManager`、装饰队列/装备检查器，以及赌场/商店支撑。逐项先核实源端实际启动条件与调用者，再移植并接入环境生命周期。

调试/性能/测试辅助也在源文件清单内，必须最终标明“移植/平台适配/源端无调用者/仅开发工具”，不能把工具文件未映射等同于玩家业务缺失，也不能无说明删除。

## 6. 前端显示与原资源：还缺什么

“前端”在这里指玩家在地图和游戏窗口里实际看到、点击到的部分，不是另做一个网页。

| 项目 | 当前已有 | 必须补齐或核实 |
| --- | --- | --- |
| 假人纸娃娃 | 原IMG、脚底、基本移动/朝向/绳梯/表情、现金武器覆盖 | 饰品、vslot、完整遮挡层级及复杂外观 |
| 姓名与名牌 | 已有原端调查线索 | 普通姓名锚点、字体、颜色、名牌皮肤/层级及生命周期 |
| 聊天气泡 | Chat已入现有聊天记录，Bubble不写记录 | 九宫格、文字测量/换行、箭头、头顶锚点、淡出、替换清理 |
| 椅子 | 状态与sit姿势 | 椅子原IMG、本体/人物相对坐标、层级及起身清理 |
| 坐骑 | 后端ID/技能/活动状态 | 原坐骑动作、骑手/坐骑合成、方向、上下骑清理 |
| 卷轴 | 成功/失败/诅咒原IMG、音效入口 | 游戏实际观感与设备听音；不再重复列为未接消费者 |
| 战斗技能 | 基础挥击与部分effect | 远程/魔法原动作、投射物、锚点/方向、命中效果 |
| 交易 | 真实协议和结算服务 | 邀请、物品格、金币、锁定、聊天、结果、关闭/取消 |
| 信使/队伍 | 真实后端成员状态，现有窗口值刷新 | 原版布局与邀请通知、typing、消息及完整操作验收 |
| 提示/黑板/喇叭 | Hint、Chalkboard、Guide等结构输出 | 逐一审计实际消费者；不能因能发事件就算玩家可见 |
| 物品喇叭 | ItemMegaphone携带物品副本 | 原UI、物品详情及生命周期 |
| 交互入口 | 聊天点名后端分派 | 地图选择、操作入口与目标一致性；完整点击流程 |

布局修改前先查逆向skill；只用原IMG，已确认由原代码绘制的细节才可照搬。找不到原布局不能自行设计。`NativeSocialWindow`虽然加载原背景，但内部仍可见GUILayout控件，不能据此称原版窗口1:1迁移完成。

气泡已核实普通聊天路径5000ms显示字段，但默认淡出时间、全文字几何和普通姓名锚点仍待查；这些属于可继续分析的技术缺口，不是要求用户先提供另一份源端的前置条件。地址及二进制身份见逆向记录，不在本文复制另一份汇编结论。

## 7. 城镇YAML、资源和脚本边界

- 929份资源已保留原字节/base64/SHA256，440份BIN包含7441帧；6336张地图几何已导出。这些证明资源读取覆盖，不证明业务已迁完。
- `TownWandererDialogue.yaml`扩展名虽是yaml，当前这份实际字节不能按普通UTF-8/YAML解析；源读取器没有发现解密，源Town逻辑捕获异常并跳过发言。文件未改写，普通字节可以直接携带，但对白读取仍需要解析。不能伪造台词，也不能把本文件问题当作其他迁移都无法继续的理由。
- 原道具脚本应继续原样执行；2431159已接成功后扣道具和重复使用失败不扣。不能在C#重写原JS逻辑。
- 上次NPC目录检查：889解析，768执行、48宿主运行失败、73 Java互操作分类；打包2777份parseFail0。不同计数不是同一口径，不能直接相加作为通过率。
- 48项失败尚需按具体宿主API和假人调用链分类。它们不一定都是本次假人迁移引入，但涉及目标流程的失败必须解决；“解析通过”不等于脚本业务可运行。

## 8. 建议实施顺序与关闭条件

以下是依赖顺序，不是缩减范围；所有未完成项最终都要处理。

| 顺序 | 工作包 | 先决条件 | 能关闭该工作包的证据 |
| --- | --- | --- | --- |
| 1 | 权威地面物品、角色属性/受击、移动锁 | 逐条读源服务与调用链 | 实体/库存/生命值真实变化，取消与过期无悬挂 |
| 2 | 交易/信使/队伍原UI与地图交互 | 完整原布局依据、现有协议 | 玩家在游戏内完成买卖/接受邀请/交付/退出 |
| 3 | 姓名/气泡/椅子/坐骑/技能显示 | 原IMG节点与EXE锚点/时序 | 原资源真实显示，无自绘回退、无残留 |
| 4 | 九类Generic具体业务 | 对应掉落/导航/赌场/队伍服务 | 每类完整FSM接生产入口，正常/失败/超时/退出可复现 |
| 5 | 店铺、议价、奖池、Boss和可配置活动 | 权威库存、物品/怪物服务 | 实际业务闭环及持久化验证 |
| 6 | 三波环境启动与装饰维护 | 各波依赖类型和服务可用 | 自动启动、失败重试、配额、观察者/退服清理验证 |
| 7 | 全系统Play与Android验收 | 上述实现完成 | 验收矩阵逐项通过，明确设备与资源包版本 |

## 9. 已有验证与尚未验证

最近代码检查点，来源为前一轮已执行结果；**本次文档整理不重复跑构建，也不增加通过项**：

| 检查 | 结果 | 能证明什么 / 不能证明什么 |
| --- | --- | --- |
| tools/server-check | 478通过，0失败 | 已编码路径回归；不是全部源业务或实机通过 |
| tools/unity-compile-check | 0警告、0错误 | 全脚本编译；不是运行行为全部正确 |
| Unity BotPresentationChecks | PASS | 原纸娃娃/移动/朝向/表情/挥击/现金武器/频道清理，技能/卷轴effect及过期，原怪物HP扣除与贡献、跨频道旧攻击拦截 |
| 原卷轴音频 | 已定向导出、原负载逐字节一致 | 不等于播放设备实际有声或首次加载不丢声 |
| NPC脚本harness | 仍有48宿主运行失败 | 不能声称全部NPC/任务/假人脚本运行通过 |
| Unity Play完整业务 | 未完成 | 尚无系统级正常/异常路径验收 |
| Android APK/实机 | 未完成 | 未在本次迁移构建/安装验收 |

临时检查日志曾位于 `/tmp/mapledash-solomapling-check.log`、`/tmp/mapledash-solomapling-unity.log`、`/tmp/mapledash-bot-presentation.log`；临时日志可能被清理，不能当作唯一可复现材料。永久入口为代码内检查与工具：

```sh
# 以下前3条从MapleDash项目目录执行；本机所有命令以rtk开头。
rtk proxy dotnet run --project tools/server-check --no-restore
rtk proxy dotnet build tools/unity-compile-check/Check.csproj -t:Rebuild --no-restore
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit -projectPath /Users/lizixian/Documents/mxd/mxd-android/MapleDash -executeMethod BotPresentationChecks.Run -logFile /tmp/mapledash-bot-presentation.log

# 脚本harness从tools/script-harness目录执行；实际资源在项目外层。
rtk proxy dotnet run -c Release -- --run /Users/lizixian/Documents/mxd/mxd-android/MapleDashResources/Data/scripts/npc
rtk proxy dotnet run -c Release -- --pack ../../Assets/Resources/Scripts/npc_scripts.json
```

APK构建与安装并非本文已执行动作；后续需单独按项目构建约定执行和记录。

## 10. 完整交付验收矩阵

以下全部未勾选；已有局部回归不自动代替系统验收：

- [ ] 17种枚举均有源端对应的实际业务，无Generic站桩替代。
- [ ] 每类正常、拒绝/失败、超时、重复请求、停止/离图/退服清理可复现。
- [ ] 自动环境完整三波启动、失败可重试，无重复启动或孤儿假人。
- [ ] 商人买卖、收购、主动邀请、店铺/议价、NX交付与兑换全链路可操作。
- [ ] 交易库存、金币、属性、物主与税费正确，取消/满包/重开不丢失或复制。
- [ ] 召集5人、30分钟、换图/换频道/解散/退出符合源语义。
- [ ] 角色完整属性/存活/恢复/受击、职业技能、经验状态倍率和掉落归属正确。
- [ ] Boss聊天触发、攻击编队、伤害归属与终止条件符合源端。
- [ ] 骰子/Blackjack/DropGame/Gacha/教程/招待/城镇/JQ/OPQ全部形成闭环。
- [ ] 姓名、气泡、提示、椅子、坐骑、喇叭、交易/社交UI均有原资源及布局依据。
- [ ] 原移动插值、攻击动作、投射物、效果、声音不以通用替代掩盖缺口。
- [ ] 目标业务涉及的脚本宿主失败全部关闭，原JS未被C#改写。
- [ ] Play完整流程验收与Android实机验收完成，记录具体版本/设备/结果。
- [ ] 源文件清单逐项复核：实际迁移、平台适配、源端空实现、不可达代码、开发工具各有依据。

## 11. 后续维护方式

每次接续先读本文和逆向记录，再检查当前代码；不要重做已完成的卷轴消费者、范围查询或交易接受入口。每完成一个工作包，更新具体缺口、目标文件、真实验证及剩余边界，不能只追加“若干测试通过”。

新增逆向结论仍写入项目 `beidou-client-reverse` skill，本文只链接结论并描述缺口。迁移清单若出现有代码却标not_ported，应核对并改partial；只有确认整个源API、调用者、显示与验收后才能标完整，不能按“类已创建”关闭。
