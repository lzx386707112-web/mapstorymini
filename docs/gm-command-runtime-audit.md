# GM 聊天指令覆盖审计

2026-10-31 第二批：`NativeSocialService → NativeGmCommands`（纯数据）→ `NativeGmCommandHost`（场景）三段已全部接线，
200 个语法入口不再有「缺少原端运行模块」的死路。本文按**实际行为**分类，不再逐条罗列源端类名
（类名/权限仍在 `NativeCommandCatalog` 的 Seed 里，后台可改 level/enabled）。

## 输入与鉴权（本轮修复）

- 前缀沿用源端：玩家 `@`，GM `@` 或 `!`。
- **斜杠容错**：`@job`、`@/job/`、`@//job/ 100`、`@job/100` 现在解析成同一个 `job`。
  以前多打两个斜杠就是「命令不存在或已停用：//job/」，玩家看到的就是「指令没生效」。
- **异常不再被吞**：`NativeGmCommands.Execute` 补了兜底 `catch(Exception)`，
  场景宿主里任何未预料异常都会变成聊天窗里的一行错误，而不是「点了没反应」。
- 权限不足的提示带上当前等级：`没有使用此命令的权限：job 需要 GM2，当前 GM0`。
- 语法首条按 rank 0..6 解析，`command_info` 的 enabled/level 仍然即时生效。

## 已实现（有真实效果）

**查询/展示**：`help` `commands` `帮助` `time` `uptime` `credits` `droplimit` `rates` `showrates` `倍率`
`online` `online2` `points` `whereami` `pos` `bosshp` `mobhp` `loot` `equiplv` `装备信息` `ranks` `id` `search`
`whatdropsfrom` `whodrops` `gacha` `gachalist` `扭蛋奖励` `expeds` `debug` `checkdmg` `showsessions` `iplist` `getacc`
`mapPlayers` `inMap` `ignored`

**角色数值**：`str/dex/int/luk`（含中文别名）、`ap` `sp` `fame` `heal` `healperson` `healmap` `hpmp` `maxhpmp`
`setstat` `maxstat` `level` `levelpro` `job`（`Profile.SetJob` + 立即刷新外观/战斗属性）、`maxskill` `resetskill`
（改为按「新手技能 + 当前职业技能」打满，不再枚举 `String/Skill.img`——真机直读索引里没有这一层时旧写法会直接抛错）、
`face` `hair` `toggleexp`（`Profile.GrantExperience` 读 `exp_enabled` 直接短路）

**物品/金钱**：`item` `drop` `proitem` `seteqstat` `recharge` `clearslot` `setslot` `clearsavelocs`
`givems` `givenx` `givevp` `giverp` `itemvac` `forcevac`

**地图/场景**：`warp` `goto` `whereami` `jail` `unjail` `warpmap` `warparea` `warptolife`（在已解析地图里找生物）
`spawn` `zakum` `horntail`（主体 8800000/8810000）`pmob` `pmobremove` `killall` `killmap` `healmap`
`npc` `pnpc` `playernpc` `pnpcremove` `playernpcremove` `eraseallpnpcs`（动态 NPC 走 `MapRuntime.npcs` + `SyncDynamicNpcs`）
`openportal` `closeportal`（`FieldPortal.enabled`，`FieldDirector.FindPortal` 会跳过关闭的门）
`reloadmap` `reloadportals` `reloadevents` `reloaddrops` `reloadshops` `reloadhunt` `night` `music` `mutemap`

**Buff/技能**：`buff` `buffme` `buffmap` `empowerme` `hide` `unhide` `debuff`（`NativeBuffs.Dispel`）

**任务**：`startquest` `completequest` `resetquest` `clearquestcache` `clearquest`

**会话/服控**：`dc` `dcall` `shutdown` `saveall` `setgmlevel` `ban` `unban` `ignore` `ignored` `set` `servermessage`
`addchannel` `addworld` `removechannel` `removeworld` `warpworld` `supplyratecoupon` `togglecoupon`
`showpackets` `monitor` `monitors` `reportbug` `gm` `changel` `enableauth` `notice` `rip` `dispose` `解卡` `unbug`

**假人开发（gm4 子集）**：`bot`（生成一只 / `bot remove` 清掉本图）、`move`（`!move <名字> <x> <y>`）、
`env`（配额开关 + 数量），全部走 `BotGeneration` / `BotRegistry`，没有另造一套假人。

**计时**：`timer` `timermap` `timerall` 记在 `GmTimers`，到时在聊天窗提示（源端是倒计时条，本移植暂无计时器 UI）。

## 明确不适用（会返回原因，不假装成功）

| 指令 | 原因 |
|---|---|
| `fly` | 移动物理只有走/跳/爬，没有飞行状态 |
| `mobskill` `bomb` | 怪物技能只有自动施放链路，没有「指定技能」入口；炸弹技能无伤害结算 |
| `maxenergy` | 道场系统未迁移 |
| `seed` `fishdrop` | 妙月种子道具集 / 钓鱼系统未迁移 |
| `mylawn` | 地图归属是多人玩法 |
| `pe` `devtest` | 需要抓包文件与源端测试脚本入口 |
| `startevent` `endevent` `startmapevent` `stopmapevent` `joinevent` `leaveevent` | 活动（GameEvent）管理未迁移；野外 BOSS 事件由地图脚本自行触发 |
| `cake` | 没有巨型蛋糕 BOSS 的素材/血量配置（`!spawn` 可刷普通怪） |
| `spawnallpnpcs` | 玩家 NPC 用真实角色 id 生成，假人不是 NPC 素材 |
| `opq` `reactor` `test` `tradebot` `fmbot` `betafmshop` | 源端 gm4 开发测试脚本，本移植没有对应入口 |
| `togglewhitechat` | 聊天窗没有 GM 频道白字分支 |
| `showmovelife` | 生物移动是本地状态机，没有移动包可打印 |

`summon/reach/follow` 在单服没有第二个在线真人时给出明确提示（源端也要求目标在线）。

## 验证

- `tools/unity-compile-check`：`dotnet build -t:Rebuild` 0 warning / 0 error。
- `GmCommandSceneChecks.Run`（Unity batchmode，真实聊天→transport→鉴权→场景宿主）通过，新增断言：
  `@//job/ 200` 斜杠容错、`!maxskill` 真的抬技能等级、`!resetskill`、`!healmap`、`!timer`。
- 未做 Android 实机验收，也没有出 APK。
