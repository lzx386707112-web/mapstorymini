# 宠物地图与装备（2026-10-31）

## 身份、证据与复现

`Beidou-Server/clien/BeiDou.exe` SHA256
`1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase400000。
汇编留存在 `docs/reverse-evidence/pet-follow-2026-10-31.asm`；
`tools/inspect_beidou_client.py <exe> --fn <VA>` 可复现。
不要将装备UI的7FE299当作地图宠物类。

## 原端已核实

- CPet ctor9391CE，vtable AFD5E8/AFD5C4/AFD5C0；Init703314，Update70403D。
  7034B4→9C406B→9C4020创建大小278h的CVecCtrlPet；vtable B3E8A8，
  Shape2D子表B3E7F8。9C40C0注册owner；每30ms调用9B16E8/9B1928推进。
- 跟随控制9C4122。9C4422..9C4555：宠物中心±300/±200px框外，
  检查主人落脚点±50px后回位。70390C复制主人CLifeMoveParam的+C..+84
  secured double，包括移动/跳跃参数。9C488A..9C499E按不同foothold group、
  主人在上方、宠物x位于主人group的水平边界内判断跳跃；9C4ED0是连接端点分支。
  9C4375..9C4405离开悬挂等待200ms。70537D(type1)使用
  `Effect/PetEff.img/Basic/Teleport`，不是自绘闪光。
- 95ACFD..95AD55以及95AD6B设置编队：一宠mode0，二宠mode1/2，
  三宠mode5/3/4，经704BD8→9C50C1写vector+260。
  9C4797..9C4827：默认距离80px；mode5=60；朝左mode1/3目标x−160，
  朝右mode2/4目标x+160；朝右mode3再x−40且距离70，朝左mode4再x+40且距离70。
  9C4B76..9C4BD1保留停步/追赶方向，不是每帧贴主人。
- 动作70458C→703C36→40E1A5。45EEB5初始化BEC21C动作名表：
  move/stand0/stand1/jump/fly/hungry/rest0/rest1/hang。70462F饱食<=50取hungry。
  703DB3..703DF6按宠物序号取主人服装ID，共用宠物动作/帧号。
- `SpawnPetProcessor.java`出生点主人y−12px，findBelow；skill8允许三宠，
  否则召唤新宠会收回旧宠。蛋5000028/5000047首次孵化不召唤。
  `ItemConstants.isPet`仍为/1000==5000，不能按新版IMG擅自扩展。

## 原IMG与槽位

`Item/Pet/<7位ID>.img/<action>/<frame>`：原canvas origin和delay；
`Character/PetEquip/<8位ID>.img/<petID>/<action>/<frame>`：服装同动作同帧。
180类root下的宠物ID节点是兼容表。`tools/export_pet_item_rules.py`导出兼容表及各IMG SHA256。

4F0D83跳表4F0E48，下标itemID−1812000；服装表BD8608：

| 物品 | 宠物0/1/2负槽位 |
| --- | --- |
| 180服装 | -14/-30/-38 |
| 1812000金币磁铁 | -23/-34/-42 |
| 1812001道具袋 | -22/-33/-41 |
| 1812002自动HP | -24共享 |
| 1812003自动MP | -25共享 |
| 1812004范围 | -26/-35/-43 |
| 1812005扫落物 | -27/-36/-44 |
| 1812006拾他人 | -28/-37/-45 |
| 1812007过滤 | -46/-47/-48 |

1822000/1832000另见4F0A43专用分支。1812008/9不在此EXE跳表内，不能猜槽。
2026-11-02补核：1822000/1832000是名片/聊天戒指，当前运行时将其映射到-24/-25不正确，不能当自动药包；见[纠错边界](mount-pet-feedback-2026-11-02.md)。
当前多宠穿戴入口复用已有宠物装备子窗选中宠物；未重建原端4F09E5多候选对话框。

## 拾取调用链

705C5D→50483A：脚点半开矩形[x−25,x+25)×[y−50,y+10)，只取可见状态3落物。
drop+54是上次请求时间，3000ms重试，非首次等待3秒。
5048E5..504937检查道具/金币能力；504949..504985保留15s归属门控；请求705C7C。
服务端`PetLootHandler`检查实际召唤、磁铁/道具袋和忽略表，然后
`Character.pickupItem(ob,petIndex)`走原背包/空间/归属/400ms校验。
reason5和petIndex必须使拾取动画飞向对应宠物，不能回退人物目标。

## 当前迁移、验证及边界

- 原来只有summoned DB标记，没有地图精灵；现由NativePetField接地图生命周期、
  原IMG动画、共同物理/平台/墙、追赶停步、跳跃、悬挂、回位和三宠编队。
- getPet的itemID=0被NativeInventoryService通用校验提前拒绝，导致UI和地图均
  读不到已召唤宠物；现只为明确的宠物无ID操作放行，不放开普通无ID物品请求。
- EquipLoadout和server接专用槽/兼容校验。Profile.EquipOfSlot对宠物槽读取权威
  inventory，避免混进人物纸娃娃及普通属性镜像。
- GroundItem接磁铁/道具袋；绑定落物通过既有MapItemPickup入库，
  GroundItemBridge的reason5目标改为实际宠物脚点。
- **仍有边界**：未逐指令移植完整平台连接路由、随机加速追赶、飞行/水域特例、
  所有宠物互动和聊天动作。当前复用现有PlayerCtrl碰撞求解器，不称所有地图逐帧1:1。
- **装备未完成项**：范围/扫落物目标寻路、
  自动Buff/异常治疗。可穿戴不代表这些能力已生效；不能把槽位PASS称全装备PASS。
- NativePetSceneChecks隔离DB/角色：首轮23项，含绳梯/三宠29项，再加金币/道具
  实际入库和卸下停止拾取34项，再加主人正下方不同foothold group起跳35项通过；
  再补实际GroundItemBridge绑定掉落入库、reason5宠物目标和他人归属拒绝，共41项通过。
  最终日志见docs/pet-review/scene-checks.log，汇总见docs/pet-review/verification.md。
  编译0错误0警告；尚非Android实机及原端并排录屏验收。

过滤配置UI及实际拾取过滤已于2026-11-01新增，见[five-fixes-character-expansion](five-fixes-character-expansion-2026-11-01.md)；不扩大为全装备能力完成。

## 自动补药服务端依据

- `org/gms/client/processor/action/PetAutopotProcessor.java` SHA256 `f4237c6f69214dad6747173e40214f6776d7aac4a6793155911e17750a569a8f`。
- `org/gms/client/Character.java` SHA256 `b72a138f09a8dadbb88faad54c2f14c1668d74608f1a59b1c394af55b0bce62a`。

Character.applyHpMpChange（当前8951..8986）在use_server_auto_pot或use_compulsory_auto_pot时，
HP/MP减少并低于对应警戒比例后读取keymap 91/92绑定药品，找到真实USE槽后调用
PetAutopotProcessor.runAutopotAction。Processor不是任意消耗一瓶：核对槽位/ID、死亡及全满拒绝，
HP/MP固定恢复优先，百分比取ceil(max×rate)；服务端/强制模式按不足警戒量ceil除恢复量，
HP/MP双恢复取较大所需数量，跨同ID堆叠消耗。仅穿1812002/3并不足以补齐绑定和警戒设置。
客户端原绑定槽/警戒UI仍待核实；本轮不凭猜测添加新UI或默认绑定药品。

## 2026-10-31 四项反馈：自动补药接线

EXE身份沿用本页首部。新增证据 `docs/reverse-evidence/pet-autopot-2026-10-31.asm`。
`8011FA`严格内部命中 `(46,11)..(78,43)` 返回200（HP），`(112,11)..(144,43)`返回201（MP）；
这两格与-24/-25自动补药装备格不同。`801474`绘制/装备检查时无对应能力会清空绑定；
`7FF0D6`检查HP能力和药品spec，`7FF2E5→58E03C`保存物品ID；MP对应`7FF5FE→58E0A8`。
Java KeymapChangeHandler SHA256 `834a5a5b516c0e1ea617225e1a62e1294d16915ac51700bc3bd43c649893f66a`；
StatEffect SHA256 `653e45de6da0c018fa1044261211ac186018c8bdeaf447006471e811adf8ac2d`。
`58E03C`写keymap对象+3C0并发0x87/mode1，MP+3C4/mode2；Java KeymapChangeHandler存91/92/type7。

当前接线：EquipWindow两药品格复用已核实坐标、原IMG图标/origin；NativeInventoryActions携带放下绑定，
KeyMap保留type7特殊键并持久化（不能经普通扫描码转换丢弃）。双击药品格清除绑定。
NativePetAutopot由地图帧检查HP/MP减少，正常客户端模式需已召唤宠物及对应装备，使用既有ClientSettings警戒比例；
server/compulsory模式分别取客户端警戒/配置比例，按PetAutopotProcessor固定量优先或百分比ceil计算瓶数，
跨同ID堆叠消耗，实际效果复用UseStatPotion（炼金、僵尸减半、状态与存档），死亡/全满/缺药不消耗。
默认警戒0沿用现有设置，不擅自指定比例；可在系统设置调整HP/MP警报。
验证结果另见 `docs/pet-review/four-feedback-verification.md`，尚非Android实机验收。


## 2026-10-31 七项反馈补核

95B9A4/95BBE0逐次资源损失判定与单瓶语义：见 [七项玩法补核](seven-gameplay-feedback-2026-10-31.md)。该记录区分原端结论、本次修复与尚未验证边界。
