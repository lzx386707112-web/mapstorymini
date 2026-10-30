# 物品功能续接记录（2026-10-30）

用户决定：收尾当前修改后停止扩展，剩余下次再改。26 件原服务端缺失脚本道具保持不可用，标记“源端缺失”，不设计替代功能。

## 续接入口

- 先读 `.agents/skills/beidou-client-reverse/SKILL.md` 与 `references/inventory-use-and-equipment.md`，复用已核实地址、资源、服务规则。
- `docs/inventory-function-audit.json` 保存逐件 ID、名称、spec、script、接入状态；`tools/audit_inventory_functions.py` 可重建。
- `docs/cash-client-routes.json` 保存514件现金物品的原EXE分类；`tools/export_cash_client_routes.py` 用Unicorn执行原纯函数，可复现。
- 修改NPC/任务先遵循AGENTS及 `docs/original-gameplay-port.md`；原JS不改，在宿主补真实接口。

## 本批收尾范围

- 真实宠物槽查询、进化、NPC亲密度奖励：`NativePetItems`、`NativeInventoryService`、`NpcApi`。原NPC1032102脚本已验证：新petId，继承等级、亲密度、饱食度、自定义名字，刷新90天有效期；默认名字随新模板。
- `QuestCatalog`/`QuestLogic`读取原pettameness奖励，仅给领头宠物。宠物种类及最高亲密度条件分别检查。原quest4647宠物点心消费与技能8已验证。
- 饥饿计时增加每角色已处理轮次，解决首角色更新全局分钟后其他角色跳过的问题，同分钟不重复扣减。
- 515/522/525/538/542/546/549普通双击沿原客户端无请求行为保留道具，不再错误提示“尚未接入”，不伪造打开NPC。519原客户端也无请求。
- `NativeUtilPrompt.Text`为已核实原布局的输入框基础，没有物品调用。中文原生字节长度/过滤未完成，不算改名或黑板已接入。

## 数量与完成口径

| 状态 | 道具数量 | 意义 |
| --- | ---: | --- |
| 未接现金入口 | 225 | 下一批继续开发/核对用途 |
| 未接特殊消耗品 | 82 | 含活动、拾取、被动和占位，不能全部按普通药品扣除 |
| NPC/任务券业务待逐项验证 | 158 | 普通双击无请求已核实，不代表对应业务全完成 |
| 源端缺失脚本 | 26 | 用户决定保留不可用，不编造 |

307为未接道具数，不是独立功能数，多件共用一个实现。39条item脚本已有入口但事件/地图/队伍分支仍需验证；生命之水单列。静态分类不能替代运行验证。

## 下次工作清单

| 功能 | 原依据与续接位置 | 待办 |
| --- | --- | --- |
| AP/SP重置505 | UseCashItemHandler173..235、AssignAPProcessor.APResetAction、AssignSPProcessor.canSPAssign | 原窗口、技能门槛、技能宏清理、真实持久化 |
| 传送石504 | UseCashItemHandler132..171 | 地点记录、原窗口、同大陆/VIP/玩家名、地图限制与失败退款 |
| 持续特效501 | A24530→93B6A7、item-effect-client日志 | action/follow/tamingMob/spectrum/interval/alpha挂载、取消、显示 |
| 飞行道具502 | 分类10→A1DD28默认返回 | 普通双击无操作不等于被动弹道表现完成，追装备/攻击表现入口 |
| 商店503/514、猫头鹰523、远程雇佣商547、快递533 | 原专用客户端/服务处理器 | 原窗口、商店搜索交易与生命周期 |
| 喇叭507/539、天气512、风筝508、便条509 | UseCashItemHandler | 原输入与显示资源、广播范围、消耗；512还应用stateChangeItem |
| 改名517 | A0BA15、UseCashItemHandler408..424 | 领头宠物、min4/max12原生字节、Curse过滤、原确认窗 |
| 黑板537 | A0CE2E、93760D→48E298→48A935(type1003)→4890C4 | max40文本、adboard九宫格完整origin/文字布局、FM房间拒绝、重复不消费 |
| 角色卡543、名字/世界更换540 | 原UseCashItemHandler、EXE分派 | 专用窗口、建号/账户槽位、完整校验 |
| 礼包553 | cashType69→A0A8F1发送请求 | 服务端无553分支，需核查原请求/资源及禁用语义，不能标客户端无操作 |
| 孵化器5060002 | UseCashItemHandler275起 | 选择、材料、奖励与容量规则 |
| 外观/NPC/任务券158件 | 515/522/525/538/542/546/549、原NPC/Quest IMG/JS | 逐项走原脚本；沙龙、进化、点心仅验证了部分 |
| 其他现金511/513/5281000/555/559/599 | cash-client-routes分类 | 死亡保护/扩充/销售等独立入口；cashType0不能统一判不可用 |
| 特殊消耗品82件 | audit JSON、UseItemHandler、StatEffect、客户端普通背包入口 | CP/BFSkill/eventPoint/ghost、无spec占位、鱼饵/白卷等逐类核对 |

## 已接模块仍有边界

- 宠物：角色宠物视觉、原动作/跟随/拾取、完整技能未迁移；lead=true shiftPetsRight未接，登录初始槽序仍来自DB顺序。进化满背包失败路径还须与原端对齐：当前拒绝时保留旧宠物，不称该分支完全还原。
- 变身：原Morph动画已接，物理/控制约束未全对齐。
- 捕捉：facing、原效果挂点、事件计分等未全对齐。
- 疾病：单人目标规则已验证，多人传播与完整状态表现未验收。
- 卷轴/织女星：结算及部分原窗口动画已接，锁定装备确认、当前结果/诅咒路径等仍需补齐。白卷2340000/2340001客户端提示差异待核实。
- 沙龙：真实外观ID和type7样式已接，长文滚动、预览层等未完全一致。
- 死亡轮回5510000：已验证同地图/50HP/MP保持/扣物/最近出生点；特殊field虚表身份、复活buff优先级与数量文字仍缺。
- 其他item JS的事件、队伍、地图条件继续验证，不能以有接口称全分支完成。
- 尚未APK打包、Android实机交互及各原图窗口截图视觉验收。

## 验证记录及复现

收尾前有效基线：隔离31 `InventoryPortalChecks.Run PASS199`，日志 `docs/skill-regression/inventory-items-isolated31-2026-10-30.txt`。最终收尾结果在下方追加。

隔离32失败是测试使用不存在的5250000作为种子，返回失败后仍写null inventory。已换真实5251000并先断言种子成功；新增5460000测试种子后点心任务应只扣1、保留1，断言相应修正。这不是产品路径新增丢物问题。

- 编译：`rtk proxy dotnet build -t:Rebuild MapleDash/tools/unity-compile-check/Check.csproj --nologo`。
- NPC harness pack2779/npc889/quest470，parseFail0/mangled0。
- NPC目录mock run790ok/27runFail/72javaInterop，是既有mock限制，不能说全量通过。
- Unity隔离工程 `/tmp/mxd-inventory-verify/MapleDash`：同步Assets，仅保留两个物品Editor检查，其余Editor工具只在临时工程排除，运行时代码全编译。不要删共享工程其他修改，不要终止其他Unity进程。

## 逆向断点与证据

EXE SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase400000。

- 普通现金背包双击4EFD25：type5分支4EFE04→48645B，8..11/38→A1DD28；其余4863D5非0→A0A63F，0→486845→A1DC5B（仅4/5/6/59处理）。证据inventory-cash-use/special-cash-route日志。
- 黑板布局 `docs/skill-regression/adboard-layout-full-2026-10-30.txt`。资源 `UI/ChatBalloon.img/adboard/0`，4890C4读取各canvas origin/size；1003偏移489D6C为3-innerWidth/2-leftWidth、-height。禁止猜套普通聊天框。
- 改名过滤7A06F3先7A04C0检控制字节/长度/Curse；失败后7A0540替换匹配为星号，7A0432恢复忽略字符。BE223C忽略字符见物品reference。中文编码及原生字节长度尚未确证。
- 文本框原尺寸与地址、未完成调查统一归档物品reference及compact-input-controls/util-text等日志，不从头逆向。


## 最终收尾结果

隔离33 `InventoryPortalChecks.Run PASS215`，日志 `docs/skill-regression/inventory-items-isolated33-2026-10-30.txt`。包括宠物进化、亲密度、跨角色饥饿、宠物任务条件、领头任务奖励、原点心任务及8类券普通双击不消费/不误开流程；无脚本异常/未迁移接口错误。未进行Android实机验证。按用户指示，到此停止扩展。
