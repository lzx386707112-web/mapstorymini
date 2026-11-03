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

## ⑤ 组队血条（2026-11-02 后续纠正）

旧结论将 `ShowPartyHP → 0x9212E4 → 0x91F001` 的独立队伍 HP 窗口与头顶血条混淆。
`PartyHP` 缺节点不能用于推导头顶条缺资源，旧 42×5 自绘尺寸/颜色也无源端依据，已替换。

真正头顶条由 EXE 代码绘制，不需要 IMG：

- 同二进制身份：`0xA3EE98..0xA3EF08` 遍历旧/新队伍六名成员，排除自己后经 `0x971709`
  查远端 CUser，调用 `0x9421B2(0/1)` 隐藏/显示。`this+0x568 != 0` 不创建远端条。
- `0x9421B2` 创建 `this+0x1F7C` 的 **52×10** 图层；绑定角色坐标向量（`+0x1140/+0x1150`），
  `0x9423DA..E1` 相对偏移 **(-26,-89)**，即脚底上方89原始像素。
- `0x942442` 清画布后依次 DrawRectangle：`(0,0,52,10,FF000000)`、
  `(1,1,50,8,FFFFFFFF)`、`(2,2,48,6,FF000000)`、
  `(3,3,width,4,FFFF0000)`，最后 `(3,6,width,1,FFA00300)` 暗红底边。
- HP 更新函数 `0x9839EA`：`+0x11A0=HP*100/MaxHP`；`+0x1F80=HP*46/MaxHP`
  （64位整数除法），再调用 `0x942442` 重画。
- 复现：`tools/inspect_beidou_client.py <exe> --fn 0x9421B2`、`--fn 0x942442`、
  `--fn 0x9839EA`；组队更新片段 `--fn 0xA3E80D`。
- MapleDash `NpcDialogUi` 已按上述布局/颜色画远端队友条，数据用 BotCharacter.hp/maxHp；
  假人仍未接受伤结算，当前满条。尚未完成 Android 实机画面验收。

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

## ⑥b 假人技能缺动作/缺释放与命中特效（2026-11-03 修复）

### 根因

- 假人技能视觉全部来自 `Skill/<职业>.img/skill/<id>`：`effect/effect0/ball/hit/prepare/special`
  **优先生效的是 `level/<等级>/` 节点**，根节点常常一个都没有；`action` 更是只在等级节点
  （1311001 的 `level/30/action=burster2` 是标量字符串，不是候选列表）。
- 旧 `BotField.PlaySkillEffect` 只拼 `Skill/<class>.img/skill/<id>/effect` 根路径、且从不切动作：
  根上没有 effect 的技能整段视觉为空，所有技能都没有技能动作 —— 即用户反馈的「有些技能缺动作、
  缺释放特效、缺击中特效」。
- `BotCombatField.Hit` 只拿到 `skill.id`，等级在适配层被丢掉，无法拼等级节点。

### 修复

- `BotCombatField.Hit` 把 `skill.level` 一并交给表现层。
- `BotField.PlaySkillCast(actor,id,level,target)` 复用主角同一条 `NativeSkillVfx.Key(id,level)` 解析：
  动作 `skill_<id>@<level>`（`WzCharacter.Pose` 解析等级/根、标量/列表），无 action 节点回退 `atk`；
  分支次序对齐 `SkillVisuals.CastVisuals`：`prepare` → 等前导时长 → `effect`/`effect0`（`z<0` 落到
  施法者后层 299）→ `affected` → `special`（start/interval/count/x/y/duration，2111002 爆炸柱）→
  `ball`（1.5ms/px 飞向目标）→ `hit`（`pos=2` 取头顶，其余取身体 Sprite 中心，跟随目标）。
- `BotPartyGrindService` 本来就是「有技能只广播技能包」，表现层现在补齐同一套原 IMG。

### 边界

- `effect1/2/3`、`keydown/end/finish`、`mob`（眩晕/封印状态层，只有真中状态才画）不无条件播放，
  与主角口径一致；假人战斗无状态效果，故 `mob` 不画。
- 假人伤害仍由服务端即时结算，`hit` 与伤害数字同帧，未做原 EXE 的命中帧对齐。

### 验证

`tools/unity-compile-check` 0 error；`BotPresentationChecks.Run` PASS（1311001 等级动作 burster2 +
effect、1001005 无 action 回退 atk 且根 effect、2111002 等级 prepare）。
全量审计：假人偏好表 35 个 (技能,等级) 组合全部能解析出视觉节点，14 个技能原端本就没有 action 节点
（按源端回退武器普攻姿势），旧实现会让其中 3 个（1111005/3001004/4001334）与全部无根 effect 的技能整段无特效。
