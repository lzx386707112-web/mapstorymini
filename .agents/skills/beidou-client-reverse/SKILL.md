---
name: beidou-client-reverse
description: 复用并维护 BeiDou.exe 及配套 DLL 的已核实功能模块逻辑，用于北斗客户端 UI、角色操作、动作时序、地图与渲染迁移及继续逆向，避免重复分析已确定的函数和资源关系。
---

# BeiDou 客户端逆向知识

本 skill 随 MapleDash 项目维护。项目根目录是本目录向上三级；源端通常在项目根目录的 `../../Beidou-Server`。机器路径变化时先定位实际文件。

## 使用入口

开始分析或修改原端相关功能前，先查下表，只读取当前模块的 reference。已确定结论直接复用；只对记录缺口、矛盾或不同二进制版本继续逆向。不要因为临时反编译文件不在了就从头分析。

| 当前问题 | 读取记录 |
| 282星级等级分档A08930、206个特殊ID覆盖与可复现导出 | [282星力](../../../../docs/tms282-skill/08-star-force.md) |
| 拍卖角色成长282星力/主附潜能/痕迹入口、90%/70%赋予卷与待选结果恢复 | [强化入口](../../../../docs/tms282-skill/16-enhancement-entry.md) |
| 现金装备显示、时装武器双击卸下/装备窗单击拾取、背包双击、戒指气泡及名牌 | [装备窗交互与戒指](references/ring-nameplate-equip-drag-2026-11-03.md) |
| 仓库/商店Tab3、确认/Notice布局、脚本API、滚动空轨与MTS菜单入口纠正及未完边界 | [菜单反馈补核](references/menu-feedback-2026-11-02.md) |
| 潮流前线38组骑宠/鞍具、现金-118/-119覆盖及普通基础装备前置（原EXE已核实）、已学骑兽入口、原装备格、1902033绳索delay UOL、商城药包首屏及拖药松手绑定 | [骑宠与药包反馈](references/mount-pet-feedback-2026-11-02.md) |
| 商城扩容、现金武器/坐骑/勋章槽位、星显示、原图聊天/名牌戒指与勋章行、十二项菜单审计、独立副本基础宿主及剩余验收 | [十二项反馈](references/menu-equipment-feedback-2026-11-01.md) |
| 100亿后拾金币截断、V/VI全职业目录/参数核对、174个物理技能mad误分类、圣骑双MCV、滚动条抢行、双层拖拽图标与键位窗共存 | [五项补核](references/five-issues-followup-2026-11-01.md) |
| 快速移动/二段跳特效、偷学滚动/穿透、金币发放、假人悬空、UserInfo扩展与学院关系树、宠物排除入口及明确未完项 | [2026-11-01五项修复与角色扩展](references/five-fixes-character-expansion-2026-11-01.md) |
| 选角重复写盘卡顿、蜗牛任务告知、技能中心五入口/技能键位语义、后台资料查询与图鉴JSON | [2026-11-01五项反馈](references/login-skill-admin-feedback-2026-11-01.md) |
| 蜗牛/远程选敌、怪物重叠、吸怪卡顿、NPC不可见会话、物理MISS、血蓝警告与宠物补药七项反馈 | [2026-10-31七项玩法补核](references/seven-gameplay-feedback-2026-10-31.md) |
| GM聊天命令前缀/权限/后台开关、真实执行/存档同步与逐项迁移边界 | [GM命令](references/gm-commands.md) |
| 自动AP白字、背包ItemNo、右下获得队列、Boss全图清单/血条与脚本注册、音频/进场阻塞本次修正、**全地图264首BGM导出/登记三段链路、`BGM06`大小写笔误、52个文件的80/82字节非MPEG前缀剥离、7首源端缺失** | [2026-10反馈补查](references/field-feedback-2026-10.md) |
| --- | --- |
| 建号中文名字、长度规则、重名与错误提示 | [角色命名](references/character-name.md) |
| 登录包序、新账号声明→性别、Notice/TOS/VScr6 素材与坐标、单张滑块与鼠标捕获纠正 | [登录链路与弹窗](references/login-flow.md) |
| 组队头顶血条代码绘制52×10/脚底(-26,-89)、纠正PartyHP窗口混淆 | [六项反馈及血条纠正](references/six-fixes-meso-drop-party-hp-2026-11-02.md) |
| SoloMapling 假人生成、调度、地图索引、迁移缺口、地面掉落权威服务、野外伴侣全图分工/Boss优先/巡逻聊天/拾取暂避与切图跟随（2026-11-03） | [假人系统](references/solomapling.md) |
| 文件版本、地址、字符串池、复现工具（递归下降 `--fn` / 字段映射 `--fields` / 任意 DLL 的 `pe_module.py`）、**先翻 Compat DLL 拿 exe 入口**、反编译陷阱（线性 sweep 会失步） | [二进制与工具](references/binary-and-tools.md) |
| ijl15/2ijl15 职责、手机用版本差异、分辨率补丁与 Android 适配价值 | [DLL 与手机适配](references/ijl15-mobile-adaptation.md) |
| 自定义DLL身份、V/VI视频表版本差异、MCV双流/Android解码、Boss标记选片、数字首位/后续位与Miss、伤害皮肤、V/VI回放/雷枪12按、追击/雷枪numeric动作、天气粒子/地面/声音、skillback合同与迁移边界 | [自定义功能 DLL](references/custom-dll-systems.md) |
| 按键组合、重复权、普通跳/下跳、foothold传送带force/drag、地空积分/x86对照、台阶直角墙脚碰撞、普攻攻速、绳梯左右跳初速/方向门控、二段跳三ID原始冲量与待核门控、高处跌落与跌落伤害（890px 公式/掉出地图不下吸） | [输入与物理](references/input-and-physics.md) |
| 站立/警戒计时、notAttack训练怪接触豁免、接触受击/冲量/免疫、身体框、隐身接触过滤、STANCE概率/地图限制、INVINCIBLE物理减伤、多段飘字与尚未对齐的边界 | [角色受击与飘字](references/character-reactions-and-damage.md) |
| 玩家死亡悬浮origin/2000ms旋转/2500ms渐显、20帧墓碑/600px与880ms下落/2200ms确认窗、Notice0和原BtOK2 | [死亡与复活](references/death-and-respawn.md) |
| 怪物模板字段与移动分类、攻击info字段与偏移（含按 `type` 分派的 `+0x28` union 布局）、**四种攻击形态的执行内核（矩形持续检测/圆 r+10/弹道逐点/多波位掩码）**、MobSkill.img类型表、放技能触发链/冷却/HP门槛、疾病与反伤、全屏/场景技能节点、召唤与治疗公式 | [怪物技能与攻击](references/monster-skills.md) |
| 怪物掉落抛物线/悬浮/旋转、金币四档四帧、拾取键范围、700ms弧线、假人拾取动画及野外代玩家入包/获得提示（2026-11-03） | [掉落与主动拾取](references/drop-and-pickup.md) |
| 小地图模式、视口、标记、大地图边框与定位 | [地图窗口](references/map-windows.md) |
| 商城子窗口、商品列表、坐标与素材、现金背包身份/宠物存取持久化 | [现金商城](references/cash-shop.md) |
| 地图宠物CVecCtrlPet、三宠编队、动作/服装同帧、宠物装备槽/药品绑定与自动HP/MP、实际拾取与边界 | [宠物地图与装备](references/pet-field-and-equipment.md) |
| 九转涅槃上限10、原JS确认链、永久次数、按等级退职业与源端未处理分支 | [九转涅槃](references/rebirth.md) |
| 普通NPC商店全量绑定、开店与交易、原版控件布局、店主/柜台深度 | [普通NPC商店](references/npc-shops.md) |
| 快递 Duey：NPC 9010009 / 533 券入口、getFee 六档、寄件扣费顺序、不可交易与 karma、取件错误码、30 天过期、Delivery 窗口素材与取不到的坐标 | [快递 Duey](references/duey.md) |
| 雇佣商店：503 许可/自由市场开店、营业态与上架顺序、成交扣费、MerchantMesos 提取与寄存衰减、Fredrick 取回、假人顾客镜像 | [雇佣商店](references/hired-merchant.md) |
| 制作 Maker：配方取 ItemMake.img（不取源端 SQL，两版 req_meso 不一致）、845 条配方导出、makerAction 的 type 0/3/4、六个状态码、宝石去重与槽位、刺激剂 90%、分解与结晶合成免费 | [制作 Maker](references/maker.md) |
| 排行榜：榜单(getWorldRankPlayers 三条排序/前50) 与 名次(updateRanking 再加 fame/meso) 是两套、`rankMove` 累加规则（lastlogin<lastUpdate 才保留）、`use_whole_server_ranking` 默认 false、5min/1h 周期、`Ranking/backgrnd` 295x335 且 Tab 只有 0 号板 | [排行榜](references/ranking.md) |
| 个人商店：514xxxx 营业执照（`isPlayerShop`）、CREATE 4/5 靠 id 分派、纯内存不落库（无 Fredrick/过期）、许可在 OPEN_STORE 才扣、成交直接进身上金币、卖空自动关店、取回后列表前移、`PersonalShop/backgrnd` 525x553 与反汇编坐标 | [个人商店](references/player-shop.md) |
| 猫头鹰搜索：523xxxx（`itemType==523`）、搜到才扣券、结果单价升序截 200、推荐位 <5 种给固定 11 件 / ≥5 种按次数前 10、`use_enforce_item_suggestion` 默认 false、跳店消息码 1/3/18、窗口素材四页与坐标 | [猫头鹰搜索](references/owl-search.md) |
| 仓库 Trunk：`sendStorage(client,npcId)` 是 25 个 NPC 脚本唯一真实调用、15 级门槛、费用 `info/trunkPut|trunkGet` 默认 100/0（9030100=500/1000，全量导出 TrunkFees.txt）、错误码 0x0A/0x0B/0x0C/0x11、可充能整组入仓、婚戒禁入、karma 转不可交易、`use_storage_item_sort` 默认 false、上限 48 格、`Trunk/backgrnd` 463x318 双栏 | [仓库](references/storage.md) |
| 家族列表、按钮分派、职位、对话框、公告板、Tab | [家族与 Tab](references/guild-and-tabs.md) |
| Point/Linear 采样、玩家/怪物重叠z顺序与闪烁、虚拟控制层、已有验证范围 | [渲染与验证](references/rendering-and-validation.md) |
| 哪些 UI 尚未替换、原端与移动适配的区别 | [UI 迁移审计](references/ui-audit.md) |
| 双击角色资料基础页、自动 AP 分配、能力值窗职业图与职业称呼口径 | [角色资料与自动 AP](references/character-info-and-auto-ap.md) |
| 底栏 71 高分组、11 个按钮坐标、通知槽、客户区 578、HP/MP 700ms向量位移、EXP即时更新及tempExp暂存经验语义 | [底栏布局](references/statusbar-layout.md) |
| 聊天发送/回显、Android输入法Done与Enter时序、系统消息类型及主角气泡接入 | [聊天消息](references/chat-and-system-messages.md) |
| 聊天窗（Messenger）三模式尺寸、标题栏三键 x=276/248/262 与 y=6、滚动条 273/148/154、11 行×13、输入框 9/318/207/15、ENTER 249/317、三座位 94 间距与 NameBar | [聊天窗](references/messenger-window.md) |
| 相机 VR、分辨率补丁、背景滚动；技能窗、ID施放表现与升级动画迁移 | [地图与技能窗入口](references/map-and-skill-window.md) |
| 蜗牛投掷术level下ball/hit、全技能节点/UOL/等级缓存、scalar动作、固定伤害、按住/蓄力原端分类与松键/持续射击时序及迁移缺口 | [技能视觉节点与公共解析](references/skill-visual-nodes.md) |
| 全技能/地图/怪物节点枚举、召唤自身帧与攻击框、HP消耗/元素、链接图规则/刷怪间隔、选敌身体框、地图进场脚本合同与未迁移边界 | [玩法节点覆盖](references/gameplay-node-coverage.md) |
| 皮奥任务箱子、Reactor原地图生成、普攻残影选箱、四击状态机、原JS/SQL掉落、任务物品拾取与刷新 | [任务箱子与Reactor](references/reactor-boxes.md) |
| showIntro/goAdventure/goLith 世界坐标过场、Shape2D位移、原始声音、事件宿主生命周期、startInstance 重载/实例回收与怪物公园六图闭环 | [地图过场与事件实例](references/map-intro-and-events.md) |
| 右上角辅助技能状态栏、32px 布局、剩余时间遮罩与迁移缺口 | [临时状态栏](references/temporary-stat-view.md) |
| 数量弹窗白字/2px正文边距、compact分支纠正（9A8716：右下Basic确定+取消，非左下结束对话）、鼠标消息分派、键位窗/技能窗/组合栏的 DragStart 与放下语义、背包道具丢到地上（ITEM_MOVE action=0）、**背包格到格移动/合并/互换（ITEM_MOVE action>0，0x4F3057 同页+非同格才发包，Inventory.move 三分支）**、**丢弃/出售数量弹窗的移动端软键盘桥（NativeEditKeyboard：同框不重开/只在 Done·Canceled 收/每事件先读快照，三处输入框统一接入）** | [UI 拖动语义](references/ui-drag-and-drop.md) |
| 事件倒计时 CLOCK 面板：258×58 底图/小时底图、四数字槽 86/112/179/205、y=12、前导零与 3600 秒切换 | [事件倒计时](references/event-timer-clock.md) |
| 废都103000800长绳Waypoint死点/24条跨层追怪与攻击回调验证/近身攻击不被收券抢占、假人导航/视图统一BotMapId、战斗/掉落保留负Scope（两次编号方案冲突纠正）/六人资格上限缺键/跨层假人追怪/券与通行证掉落补核、废都进图卡顿（导航后台构建/分帧生成/829ms离线基线）、组队任务招募/阶段/领奖 NPC：`em.getEligibleParty` 未绑定导致的「对话脚本出错」、`party.toArray`、`em.getMonster`、`map.getPlayers`/`spawnAllMonsterIdFromMapSpawnList`、组队搜索开关与副本公告、假人陪跑的队伍编号 vs 角色 id 混用 | [组队任务接口](references/party-quest.md) |
| Say.img无JS问答ask/stop选错分支、NPC Java导入/日期/global this/命令元数据/21点入口与正式API审计口径、三处源脚本授权覆盖、ijl15对话正文480高度补丁、NPC 对话窗口精确布局/字体/按钮、VScr4、`#L` 图文、`#y/u`任务引用、`#q/s`技能名/图、`#@`NPC名、`#W`原图与atoi参数审计、sendPrev 上一页+确定/对话键任务菜单与接受页推进、灯泡 autoStart/日期筛选、NPC进行中QuestIcon1/stop对白与完成条件击杀计数、截图问候入口/交付奖励10000→4→1、fieldType82青苹果外观、建号路线与远程任务脚本 | [NPC 对话窗口](references/npc-dialog.md) |
| 任务键/完成通知入口自动选择与详情展开、任务三页签/详情summary/原端Tab2布局、任务告知、达成目标FadeYesNo红气泡、QuestClear及用户授权截图重建训练任务 | [任务窗口与告知](references/quest-window-and-alarm.md) |
| 传送门 pt 触发方式、NPC 脚下名牌宽+5/高16与Canvas字体高度/透明四角、聊天气泡九宫格与时长、新手训练营 0~4 图、`cm.warp` 目标图不存在的语义、**换图 Enter 异常导致黑幕锁死（出租车黑屏）的修复与出租车链路逐项排除** | [传送门/名牌/气泡](references/field-portal-nametag-bubble.md) |
| 五转女神条件/永久标记/2029006面板脚本缺失回退链路及授权改为技能窗独立五转页签、装备槽位/吊坠/轮回图腾、NPC物品/原item脚本、怪物吸星大法与全物品入口审计 | [物品使用与装备槽位](references/inventory-use-and-equipment.md) |
| 背包/装备窗物品悬浮详情：ToolTip 图集、名称/描述来源、装备desc测量/追加绘制及282机器人原文回退、属性/需求字段、配色与帧绘制；背包整理/合并（ITEM_SORT2/ITEM_SORT、PairedQuicksort、move 的 owner/充能分支、BtGather/BtSort 二态） | [背包与悬浮详情](references/inventory-and-tooltip.md) |
| 怪物手册：窗口 475x349、子控件表（两对翻页箭头 48/185 与 270/407、搜索框 49/30/120/15、两条书签栏 -7/25 与 439/506）、卡槽 5x5 命中框 8/31/33/45/27x38、战利品 4x5 网格 38/60/36/32、LeftTab 九套配色、infoPage 170x231 | [怪物手册](references/monster-book.md) |

地图坐标/脚底/平台/绳梯的运行时约定另见 [mapledash-map-port](../mapledash-map-port/SKILL.md)。服务端和脚本语义仍以源端服务端/脚本为依据，不能用 EXE 的显示行为替代服务端规则。

物品功能本次停止扩展，后续从 [续接清单](../../../docs/inventory-functions-next-session.md) 与物品reference继续，勿将静态分类当作已验证完成。

全局迁移缺口总账（哪些 handler/窗口/定时任务/系统还没过来）见 [未迁移系统总账](../../../docs/unmigrated-systems-2026-11-02.md)；开工前先查该表，不要重复盘点。

## 结论使用边界

- 地址仅对应记录中的 SHA256/ImageBase；首次使用当前文件或文件版本变化时核对身份。版本不一致时保留旧结论并另列新版本，不能直接套地址。
- 区分原端已确定逻辑、待核实线索、项目当前实现、移动端适配及实际验证。已确定函数不等于模块完整迁移；Unity 回归通过不等于 Android 实机验收。
- IMG 节点、origin、布局参数、函数调用链优先于截图推测。原端经核实的代码绘制可严格照搬，禁止自行设计；当前无章节模式。
- 反编译伪代码只能辅助理解。涉及参数、状态位、除法取整、this 子对象偏移或虚表分派时，使用已记录的汇编证据；不要用猜测覆盖确定结论。

## 新发现必须同步归档

每次产生新的确定结论，在本次工作结束前立即更新对应 reference；新模块新增 reference 并更新上表。纠正旧结论时同步修正所有引用，注明旧地址/解释为什么不成立，避免以后再次走错分支。

每项确定结论至少保留：

1. 原文件、SHA256 或所用身份记录，以及 VA/函数范围；结构字段注明基于哪个 this，区分文件偏移与 VA。
2. 触发条件、状态转换、调用链、公式和单位；明确整数取整及默认值。
3. 关联 IMG 路径、节点职责、尺寸、origin 和坐标所属父窗口（适用时）。
4. 可复现的项目工具/命令、关键汇编或常量依据；必要材料不能只放 `/tmp`、被忽略的 `Library` 或聊天记录。
5. 对应运行时代码、已迁移范围、实际验证和剩余缺口。没有新增验证就不能沿用旧 PASS 声称新行为通过。

不确定内容放在明确标记的“待核实”部分，保留下一处应追踪的函数或字段，不把它写成实现要求。发现记录不足时只补查不足部分；AGENTS 要求的用户决策仍照常执行，已有授权不重复询问。

现有 `tools/inspect_beidou_client.py` 作为复现入口，不在 skill 内复制一套分析器。旧文档 `docs/original-ui-input-analysis.md` 已改为本 skill 的索引；新知识只维护一份正文。
