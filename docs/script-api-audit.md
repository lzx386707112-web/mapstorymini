# NPC 正式 API 审计（2026-10-29，未完成）

原 Mock 的 47 RUN + 73 JAVA 是固定追踪清单，不是 120 个已确认游戏故障。Mock 未接正式 Java 类且旧回复序列不符合 dispose/选项协议。

正式宿主结果：889 条 / 829 通过 / 60 失败；原清单 120 条 / 106 通过 / 14 失败；直接宿主断言 25 通过。

## 复现与边界

在项目根目录执行 Unity 的 `-executeMethod ScriptApiAudit.Run`，结果写入 `docs/script-api-audit.json`。测试只运行 NPC 首条菜单路径，最多 14 次回复；角色 30 级、job0、100000 金币，起始地图1000000，没有渲染场景、队伍或事件实例。每条重新装载角色快照和地图偏好，结束还原独立的 mxd.fieldmap 偏好。失败退出码1是有剩余项，不是编译失败。

无错误不等于全部选项通过；缺事件/队伍/地图前置条件的脚本需在对应真实场景另验。不得用假事件、空成功接口或改脚本分支消除这些失败。

## 原清单剩余项

| 脚本 | 当前阻点 | 下一项必要工作 |
| --- | --- | --- |
| 1063017.js | `Unmigrated server API: map.910510202.spawnMonsterOnGroundBelow` | 远程地图怪物权威状态与场景同步；不能在当前地图代刷。 |
| 2010007.js | `Unmigrated server API: player.genericGuildMessage` | 按原客户端GUILD_OPERATION code1接家族创建输入与后端校验。 |
| 2091005.js | `Unmigrated server API: channelServer.lookupPartyDojo` | 接完整武陵道场分配/队伍预约/地图重置，补实际道场场景用例。 |
| 2101015.js | `Unmigrated server API: player.getAriantColiseum` | 接真实AriantColiseum登记/积分与空状态，不把未接系统固定返回null。 |
| 2101016.js | `Unmigrated server API: player.getAriantColiseum` | 同上，验证结算/积分奖励分支。 |
| 2112000.js | `TypeError: cannot read property 'isEventCleared' of null` | 组队副本实际事件上下文及原事件脚本验证。 |
| 2112010.js | `TypeError: cannot read property 'isEventCleared' of null` | 组队副本实际事件上下文及原事件脚本验证。 |
| 9000018.js | `Unmigrated server API: cm.getConnection` | 源仓库仅脚本引用npccashshopdajieda，无建表/商品资料。用户确认无备份，要求暂按源端保留；不建空商品表、不改脚本。 |
| 9000019.js | `Unmigrated server API: org.gms.util.PacketCreator.openRPSNPC` | 原猜拳窗口/按钮/状态包分析与RPS后端，不以Toast替代。 |
| 9000055.js | `Java interop: class not migrated: soloMapling.ArtificialPlayer.BotBossCombatManager` | 已接21点真实玩家身份与最近牌桌；仍需迁移BotBossCombatManager及Boss召唤分支。 |
| 9040006.js | `TypeError: cannot read property 'getState' of null` | 在990000500真实watergate reactor与家族副本上下文验证；forceHit应设置指定状态而非自增。 |
| 9900002.js | `Java interop: class not migrated: soloMapling.ArtificialPlayer.BotBossCombatManager` | 按原BotBossCombatManager迁移集火筛选、移动、伤害归属与定时任务清理。 |
| cpqchallenge.js | `TypeError: cannot read property 'getMembers' of null` | 怪物嘉年华真实挑战队伍/邀请上下文，禁止伪造party。 |
| cpqchallenge2.js | `TypeError: cannot read property 'getMembers' of null` | 同上。 |

## 本轮已验证修改

- Java导入/类身份、Calendar/Date、全局this；ExtendUtil持久化与枚举；真实好友容量与卡片种类。
- Channel存值与QuestStatus瞬时customData；原地图/职业/ExpeditionType常量；真实当前地图怪物/reactor读取。
- 原Field音符/Party音效导出，sound/music命令在包消费时播放。
- Gachapon查询与抽奖包含有效公共奖池；CommandsExecutor复用命令表/i18n并按等级/启用配置分组。
- EventManager独立于渲染field的宿主、共享Java注册与channel桥；交通时间读取真实world travel_rate。
- 用户授权的三处最小脚本覆盖：2040022补return、2010005初始化access、2143003改CYGNUS。

验证：self-test 40通过0失败；compile-check Rebuild 0错误0警告；脚本包2779条parseFail0/mangled0。完整server-check在既有MarketEconomy.TrailingDigit整数溢出中断，尚非全通过。没有APK实机验收。

## 无数据库备份时的处理

用户确认没有备份，要求能修的继续修、无法还原的暂按源端保留。9000018不新增本地覆盖、不编造商品数据；getConnection明确报告缺失宿主，替代误导的null.prepareStatement错误。其余未迁移的大系统和需要真实副本前置条件的脚本保持现有源JS及显式缺口。

本轮另接isRecvPartySearchInviteEnabled真实状态读取（不包含切换/搜索协调器），以及BlackjackDealerBot.joinNearestTable真实牌桌入口，拒绝未登记或错误玩家。21点入口已做真实成员加入/最近荷官/重复加入检查；9000055仍会在BotBossCombatManager处报告未迁移，不能计作整条NPC修复。
