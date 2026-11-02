# GM 聊天命令（服务端源码依据）

2026-10-31。此模块依据 Java 服务端，不新增 EXE 绘制/地址结论，不修改原资源。

## 身份与调用链

- `CommandsExecutor.java` SHA256 `051a9735fecb3e101d42d0720359bd7b3560b20355eef2f71482ed9fbcef41c2`
- `commands/gm2/ItemCommand.java` SHA256 `48fbf947dd990b86ccd87912a6ba10ec6da1e918ca2061cbf2213f60ad754818`
- `commands/gm4/ProItemCommand.java` SHA256 `601f4f7ed96078fbae3e208d628b45f8c693094c4dd4c6d1d448b79fc4f5e118`

`CommandsExecutor.isCommand`：普通角色只认 `@`，GM认 `@/!`。`handleInternal`先监狱/禁用玩家命令检查，再移除前缀、以空格拆命令名/参数，命令名和参数小写，但公告/音乐等保留 `lastCommandMessage` 原文。`CommandService.loadCommands`按当前rank 0..6装载enabled行，语法首条获胜；当前level是权限依据，default_level用于寻找Java类。

## 已迁移合同

- 原200条种子/描述移到 `NativeCommandCatalog`，后台和聊天共享数据，禁止两份种子漂移。
- `NativeSocialService.Handle(SendChat)`→`NativeGmCommands.Execute`（纯C#）→已授权才调用`NativeGmCommandHost`。命令结果通过SocialResponse独立返回，禁止广播到假人聊天或回显普通聊天。
- 角色修改回写数据库后 `ReloadAfterCommand`更新同一个FullCharacter的内容，保留会话对象identity及tradeRevision，避免地图脚本/活动因误判换角而重置。再ApplyProgression同步实际战斗属性/HP/MP/外观。原HP=0应走DeathFlow。
- GM发物品使用 `BotItemCatalog`（源IMG字段）+ `TradeItemRules.Receive`预检/堆叠 + `BotTradeService.Persist`，容量失败前不修改数据库；保留装备原属性、签名、flag（UNTRADEABLE=0x08, ACCOUNT_SHARING=0x100, SANDBOX=0x40）。proitem设置14项属性及不可交易，seteqstat只改正槽位装备。
- goto目的地及音乐列表取源GameConstants/MapId；spawn通过FieldDirector.SpawnNativeMob，BossHP取NativeMobController的long值；不造地图/怪物/技能素材。
- 原 `ReloadMapCommand`实际重建当前地图并刷新怪物，`ReloadPortalsCommand`清传送脚本，不等于重载地图；`clearquest`清任务定义，不是删玩家任务进度。

## 验证与剩余边界

完整逐项状态及剩余入口见 `docs/gm-command-runtime-audit.md`。服务端703项回归通过；Unity实际聊天链路检查通过（属性存档、即时物品、职业、Boss生成/长HP、buff、任务）。此处不表示所有200入口完整迁移，也不等于Android实机验收。

## 2026-10-31 第二批：200 个入口全部有落点

### 玩家反馈的「@//job/、@//maxskill/ 没生效」根因

- **斜杠容错**：手机键盘上把指令表里的 `@job` 敲成 `@//job/ 100`、`@job/100` 时，
  旧解析只按空格切分，`syntax` 变成 `//job/`，直接回「命令不存在或已停用」。
  `NativeGmCommands.Execute` 现在先 `TrimStart('/')`，再用 `{' ','/'}` 切命令名；
  `raw` 保留原文（`!music Bgm00/SleepyWood`、`!notice` 依赖它）。
- **异常不再被吞**：旧代码只 catch `ArgumentException`/`OverflowException`，
  场景宿主抛 `NullReferenceException`（缺 WZ 层、地图未就绪）时聊天里什么都不会出现。
  现在兜底 `catch(Exception)` → `指令执行失败：<类型> <消息>`。
- **`!maxskill` 不再依赖 `String/Skill.img`**：旧写法枚举 `String/Skill.img` 的节点名，
  真机直读索引里没有该层时抛「缺少 String/Skill.img」。现按源端语义改为
  「新手技能 + 当前职业 `JobDef.allSkills`」，`!search skill` 同一处兜底也一并改掉。
- 权限不足的提示带等级：`没有使用此命令的权限：job 需要 GM2，当前 GM0`（便于判断是不是 GM 等级不够）。

### 代码落点

- `NativeGmCommands`（Server/World，纯 C#/DB）：staff、changel、equipa、ranks、ban/unban、ignore(d)、
  set、servermessage、add/remove world/channel、warpworld、supplyratecoupon、toggleexp、expeds、iplist 等。
- `NativeGmCommandHost.cs`（原文件）：地图/实体类旧分支。
- **`NativeGmCommandHost.Commands.cs`（新文件，partial）**：`Extended(...)` 承接其余全部入口——
  gacha、jail/unjail、warpmap/warparea/warptolife、debuff、healmap/killmap、itemvac/forcevac、
  npc/pnpc/playernpc/pmob 系列、zakum/horntail、openportal/closeportal、night、mutemap、togglecoupon、
  dc/dcall/shutdown、showsessions、showpackets/monitor(s)、reloaddrops/reloadshops、bot/move/env、timer 三兄弟、rip。
- 新增支撑：`FieldPortal.enabled`（`Usable` 与 `FieldDirector.FindPortal` 都看它）、
  `GmTimers`（`GameHUD` 每帧 tick 一次，frameCount 去重）、`GameMute`（`BottomBar.SubmitChat` 拦普通聊天）、
  `GmDiagnostics.PacketLog`、`BotField.TryFeet`（角色名牌取假人脚点）。
- `Profile.GrantExperience` 读 `exp_enabled`，`!toggleexp` 才真的停经验。

### 仍不适用的入口

`fly`(无飞行物理) `mobskill`/`bomb`(无指定技能入口) `maxenergy`(无道场) `seed`/`fishdrop`(无对应系统)
`mylawn`(单人无归属) `pe`/`devtest`(无抓包/测试脚本) `startevent` 系列(活动管理未迁移) `cake`(无素材配置)
`spawnallpnpcs`(假人不是 NPC 素材) `opq`/`reactor`/`test`/`tradebot`/`fmbot`/`betafmshop`(源端开发测试)
`togglewhitechat`(无 GM 频道白字) `showmovelife`(无移动包)。
这些返回具体原因，不假装成功。

## 2026-10-31 第三批：`!maxskill` 之后技能窗「多出转职技能书」

### 玩家现象

「用了 maxskill，技能窗把二三转的技能也显示出来了，我明明没转职。」

### 先分清两层，别急着改 maxskill

1. **源端 maxskill 的数据语义是全表打满**：`commands/gm2/MaxSkillCommand.java` 遍历
   `String.wz/Skill.img` 的**每一个**子节点调 `player.changeSkillLevel(skill, maxLevel, maxLevel, -1)`，
   连别的职业的技能也一起塞进角色技能表；之后再把 `5001005`（战神/传说坐骑）或 `21001001`
   用 `-1` 删掉。`ResetSkillCommand` 同构（等级 0、masterLevel 保留）。
   所以「数据里有二三转技能」在源端本来就成立，**不是**显示 bug。
2. **技能窗只显示自己职业线的书**：`SkillWindow.Line => SkillBook.TreeForJob(Profile.JobId)`
   （`ParentOf` 父链：新手 → 一转 → 二转 → …）。新手只有 1 个页签。
   本工程反而比源端更严格：`!maxskill` 只打「新手 + 当前职业 `allSkills`」。

### 真根因：PlayerPrefs 里残留 `debug_cls`

- `PlaySmokeTest` 用 `PlayerPrefs.SetString("debug_cls", cls)` 覆盖本次运行职业，
  只在 `Finish()` 里删；跑到一半被打断（编辑器崩溃、直接退出、截图脚本 abort）就永久留下。
- `Profile.Load` 旧写法每次启动都读它 → 职业被钉成 `debug_cls`（`warrior` 经 `LegacyCls` = **110 剑客·二转**），
  于是新手看到「新手 / 剑士 / 剑客」三本书 + `!maxskill` 把剑客技能打满；
  更要命的是 `debugOverride = true` 让 `Profile.Save()` 全部空转，**玩家进度压根不落盘**。
- 修法：与 `debug_level` 一致——读到就 `DeleteKey` + `PlayerPrefs.Save()`，打一行 `LogWarning` 说明本次被覆盖、不落盘。
  证据与复现：`defaults read unity.MapleDash.未来岛 debug_cls` 有值即中招。

回归：`Assets/Editor/SkillPanelChecks.cs`（`SkillPanelChecks.Run`，batchmode）
断言「新手 job 0 跑完 `!maxskill` 仍只有 1 个页签、页签内无跨职业技能行、1100000/1110000/2000000 等级为 0、
1000 已满」以及「`debug_cls` 覆盖生效一次后键被删除、二次加载回到存档职业」。

