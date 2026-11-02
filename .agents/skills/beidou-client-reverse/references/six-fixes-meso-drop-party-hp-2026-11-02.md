# 六项反馈修复：丢金币链路 + 组队血条 + 假人打怪（2026-11-02）

核实快照：2026-11-02。二进制身份沿用 [binary-and-tools](binary-and-tools.md)（SHA256 `1b91cf5b…f134b2`，ImageBase `0x400000`）。
用户反馈 6 项：①背包 BtCoin 没反应 ②物品拖出背包不丢 ③丢金币/物品没有从身上抛出的动画 ④丢金币弹窗样式不对 ⑤组队头上没有血条 ⑥假人不走近就打到、全是普攻。

## ① BtCoin 点击 → 丢金币弹窗（已核实 + 已实现）

### 已核实的 EXE 流程（0x81D996，按钮 id 0x7D2 创建于 0x81CD74）

1. 状态 0x12/0x13（死亡）→ 字符串 66「死亡状态中不能执行该操作」（zh 表同文）。
2. 管理账号检查 → 字符串 775「管理账号不能扔掉金币。」。
3. 创建 CUtilDlgEx 单例（0xbf0b00）→ GetString 0x14a0(5280，zh「扔掉多少？」)→ Init type=2（0x9ac552）。
4. `0x9AC732(10, 10, min(meso,0xC350), 0, 10, 0)`：初始值 10、最小 10、上限 min(金币,50000)。
5. `0x9A3E38` 模态 → `0x4EDBA1` 确认 → `0xA23DE5(CUserLocal,[dlg+0xD8])` 发送 MESO_DROP(0x5E)。

### 服务端权威（MesoDropHandler.java）

- `!isAlive` → return；`meso<=player.getMeso() && meso>9 && meso<50001` → `gainMeso(-meso)` →
  `spawnMesoDrop(meso, player.getPosition(), player, player, true, (byte)2)`。
- GM 预检：`isGM() && gmLevel() < GameConfig server/GM/minimum_gm_level_to_drop`（V1.7.0 默认 **4**）。
- `attemptCatchFish(meso)` 分支（钓鱼场消失金币）无对应系统，未迁移，按普通 spawnMesoDrop 记录。

### MapleDash 实现

- `GameWindows.cs` InvenWindow：BtCoin 改 `NativeUi.Button`（素材四态齐全 normal/pressed/disabled/mouseOver）→ `NativeInventoryActions.PromptDropMeso()`。
- `NativeInventoryActions.cs`：死亡拦截（文案 66）→ GM 拦截（`record.gm>0 && gm<minimum_gm_level_to_drop`，文案 775）→ `NumberLong("扔掉多少？",10,10,min(gold,50000))`。
- `NativeInventoryService.DropMeso`：`dropMeso` 请求字段；服务端校验 hp/gm/数量 → `character.meso-=meso` → `SpawnMesoDrop(...,true,2)`；`InventoryChangeResponse.meso` 回权威金币。

## ② 拖出背包松手丢弃（源端无此交互，按用户要求新增）

源端只有「点击拿起 → 点击放下」一种口径（ITEM_MOVE action==0）。本项目在 `CarryPointerStep` 增加
Released 分支：`carriedFromInventory && FieldAcceptsDrop(落点)` 时丢弃，窗口/底栏/虚拟键上松手不丢。
丢弃失败（弹窗取消）恢复携带。见 [ui-drag-and-drop](ui-drag-and-drop.md)。

## ③ 本地玩家掉落抛出动画（已核实 + 已实现）

`GroundItemBridge.OnSpawned` 的 from 此前只查 `BotRegistry.Get(dropperId)`，本地玩家不在注册表 →
from 退化为落点，抛物线没有起点。修复：`dropperId==ownerCharacterId==自己` 时用 `player.FeetPos`
（源端 spawnMesoDrop/spawnItemDrop 的 position 都是 `Character.getPosition()`）。

## ④ 丢金币弹窗样式（已核实）

源端金币丢弃、交易放金币（0x7C37CA）、物品数量确认是**同一个 CUtilDlgEx type=2 对话框**：
`NativeUtilPrompt` 的 Notice3/t+c + Notice4/s 切片 + `UtilDlgEx/BtOK|BtClose` 结构已与之一致。
本次修复对齐的是参数与文案（「扔掉多少？」、初始 10、下限 10、上限 min(meso,50000)），物品丢弃
数量弹窗保持字符串 885 的「扔掉多少？」同款。type-2 画布 chrome 的反汇编细节（0x9A83F6 画白底
黑边）与 Notice3 切片在 720 高下肉眼一致，未再追。

## ⑤ 组队血条（ShowPartyHP，逆向定论）

### 证据链（用户坚持「肯定有」是对的）

- **选项**：EXE `0x49F1AC` = `IsShowPartyHPEnabled()`，选项名 `ShowPartyHP`（字符串 4433），
  `0x49EDDB(2, name, default=1)` —— **默认开启**。四处在登录/进图/组队事件检查
  （0xA03E00、0xA03F20、0xA3E80D、0xA3EC48）。
- **控件**：开启时 `0x9212E4` 在单例 `0xBF0B00` 上创建 0x91F001 类（size 0x5E0）。
  构造加载 `UI/UIWindow.img/UserList/Party/PartyHP` 的**九宫格子节点**
  （字符串 1394-1402：`nw/n/ne/e/w/sw/s/se`）+ `GaugeBar/graduation/bar`（4422-4424）+ `gauge`（1960）。
  绘制函数 0x91FADA（953 条指令）逐件画 [this+0x5B0..0x5DC] 的 canvas。
- **素材缺失**：BeiDou 的 GMS083 数据 21 个 UI img 里都没有 `PartyHP/BtHP` 节点（也无 UIWindow2.img），
  全 WZ 二进制 grep 不到 `ShowPartyHP` 节点 —— EXE 引用它说明**原版 v083 有**，属 BeiDou 数据导出缺失。
- 服务端同源证据：`Character.updatePartyMemberHPInternal` → `UPDATE_PARTYMEMBER_HP(0xC9)`（cid,curhp,maxhp）
  发同图队友；源端 PartyGrindService.spawnFor 里 `leader.receivePartyMemberHP(); bot.updatePartyMemberHP();`。
- 设置窗口下拉框：0x994730/0x994765（'Mark HP only' 4190 / 'Mark name and HP' 4191）是另一个
  设置项（BtOK2/BtCancel2 对话框），与头顶血条无关；名牌绘制（0x942DCC）只画 名字/公会/勋章。

### MapleDash 实现（自绘，已在会话中向用户说明素材缺失）

- `NpcDialogUi.DrawCharacterNames`：每帧取 `GameServer.Local.Social.Parties.For(self)` 成员集合；
  名牌正上方画 42×5 血条（深底 + `#C62222` 红色填充，按 StatusBar.img/gauge 的 HP 红条语义自绘）。
- 数据源：本地玩家 `player.hp/hpMax`（实时）；假人 `BotCharacter.hp/maxHp`（新字段，恒满 ——
  假人目前无受伤结算，战斗接入后在字段上更新）。

## ⑥ 假人打怪真实规则（源端对照 + 修正）

### 源端事实

- 源端 `CompanionGrindBot.java`：`ENGAGE_X/Y=120`、`SWING_CD_MS=800`、`LEASH_X=280`（站桩）、
  选怪 `LEADER_LEASH_X=560`（PartyGrindService）。**C# 的 120/120 是源端原样**，但源端这个
  120 不分职业 —— 近战站在 120px 外也出刀，正是用户抱怨的「没走到身边就打到」。
- 源端 `BotAttack.grindHit`：`resolveGrindSkill(job, weaponType)` 出技能 → 只 `broadcastAttack`
  （showSkillVisual 特效 + CLOSE/RANGED/MAGIC 对应包 + 伤害数字）；**无技能才** visibleHit 普攻。
  C# 旧实现总是先 `BasicSwing` 再叠技能特效 —— 转写偏差，观感全是普攻。

### 修复

- `BotAttack.EngageRange(job, weapon)`：按 GrindSkill 类别分档 —— CLOSE（近战/拳套）70×50
  （身位内、同层才出刀）；RANGED/MAGIC 380×120（射程内停步不贴脸）。数值为客户端攻击框语义
  的近似档位（源端无逐武器 range 数据），已记录为待核实。
- `BotPartyGrindService.Strike`：出手前服务端二次校验距离（拦瞬移边缘帧）；`skill.id==0` 才
  `BasicSwing`，对齐源端 grindHit 分支。
- `CompanionGrindBot.UpdateState`：停步判定改用 EngageRange。

## 验证边界

- `tools/unity-compile-check` 0 error；`tools/server-check` 1206/0 全过（含 DropChecks、PQ、交易）；
  `script-harness --self-test` 40/0；`--run` 880 个脚本 ok=778 / runFail=29 / javaInterop=72 /
  parseFail=1 为**既有迁移度基线**（与本次改动无关，失败均为 Java interop 未迁移类与 cm mock 覆盖）。
- server-check/script-harness csproj 补排了三个引用 Unity 类型的 Server 文件
  （NativeBotBossCombat / NativeDojoService / NativeMentorshipService），并把
  `NativeEventScriptService.getMapFactory` 的 `NpcApi.BuildWarpMap` 依赖改为 `WarpMapFactory`
  注入委托（分层违规修复，NativeEventScripts 装配处注入）——这两处是此前会话遗留的离线工程损坏。
- 头顶血条为自绘（源素材缺失）；EngageRange 档位无逐武器源端数值。均未在 Android 实机验收。
