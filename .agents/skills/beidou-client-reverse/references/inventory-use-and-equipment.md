# 物品使用与装备槽位（2026-10-30）

## 源端身份与复现

源根 `~/Documents/mxd/Beidou-Server/gms-server`，本模块业务以Java/原JS为依据；未新增EXE业务函数结论。

| 文件 | SHA256 |
| --- | --- |
| constants/inventory/ItemConstants.java（src/main/java/org/gms/下） | c67a8386326aab8bb184fe6430adacc4bceb2893ef9f648a771278f143e26605 |
| client/inventory/BodyPart.java | 98491a35edce23b5377de1af31752e8d6b4683b3f54ee29d5dc92ee84db89c55 |
| net/server/channel/handlers/UseItemHandler.java | c21e8c80787be722b6f9014c709e479c853e0c19997cd65259d30c85616831dc |
| server/life/MonsterVacCompat.java | ff7aa798d57cb1e2785c2d74c12de0759d34827fd675f4ba3714a3de2a9507f5 |
| scripts-zh-CN/item/consume_2431158.js | 023c25b03793db52e52282d33dc88a3b8f6dacc062afa3163a36c1826d219c05 |

`tools/audit_inventory_functions.py`扫描复制的原IMG生成`docs/inventory-function-audit.json`；报告是静态入口覆盖，和运行验证分开。

## 已确定装备规则

`ItemConstants.getEquipSlotType`优先1189999→TOTEM53；再134/135→副手51；普通武器范围1302000≤id<1493000→11；
普通映射100..110为原帽/脸/眼/耳/上装/裤/鞋/手/盾/披风，111戒指12、112吊坠17、113腰带50、114勋章49、115肩20、118徽章55、119纹章56、167心脏54。
数据库EQUIPPED位置取负数；戒指有-12/-13/-15/-16。BotCustomization.EquipSlotType已有此源端翻译，NativeInventoryService复用同一路径。
1189999是轮回碑石，不能按118泛化为徽章。`Character.equippedItem`10146起在穿戴时授予Beginner.FRENZY_TOTEM=1016 Lv1，若已学不重置；unequippedItem不删除此技能。
`Character.equipPendantOfSpirit`是每小时任务、最多3档；现已迁移至 NativeEquipmentEffects：会话穿戴计时每小时一档、最多三档，经验按 gain/10 整数取整后乘档位，卸下清零；离线不累加。
装备扩充后的职业/性别/唯一戒指等完整源端限制、现金专用穿戴仍有先前记录的边界；来源目录167模板在Accessory与Android均存在，当前沿用Accessory/info。

## 物品入口

`UseItemHandler.handleItemUsage`把isNpc物品交给handleShortcutMenu；`ItemInformationProvider.isNpc/isRemove`读取spec/isNpc、spec/remove。
info来自spec/npc与spec/script；脚本名可空（2029001快捷菜单npc9900001），走NPCScriptManager，不能当作普通药或强制寻找item脚本。
`spec/isNpc=1`与`spec/script`（ItemScriptManager）是两条不同路由；后者必须原样执行`item/<script>.js`。
2029006使用命名NPC脚本，2029001不消耗；是否消耗取原remove字段。

## 怪物吸星大法

原`Item/Consume/0243.img/02431158/spec`：npc9900001、time600000、pdd1、script=consume_2431158。
原JS只在`im.getPlayer().startMonsterVac()`成功时gainItem(2431158,-1)，随后dispose；重复启动不扣道具。
`Character.startMonsterVac`固定当前地图和角色脚下PointBelow减1px，默认10分钟（monster_vac_duration_minutes可覆盖，非正回落10），100ms周期；活动中拒绝重叠。
`MapleMap.pullMonstersTo`过滤死亡/fake/boss，朝固定点每次150px，8px内不再移动；原广播MoveLife duration130ms，默认眩晕2500ms，可用monster_vac_stun_enabled关闭。
状态图取`Skill/111.img/skill/1111008/mob`三帧，pos2头顶、repeat1；必须原IMG，不自行画星星。
换图/到期/stopMonsterVac停止任务、解除本系统怪物锁和本物品增益。当前迁移在NativeMonsterVac与EnemyUnit/NativeMobController接线，Script宿主同时绑定Character与cm入口。

迁移边界：Unity直接更新怪物坐标，未复现130ms网络封包插值；当前EnemyUnit没有源端fake实体标记，普通本图实体按存活/非Boss过滤。
眩晕原图已接入，但源端所有MonsterStatus覆盖/冲突和refreshVisual封包语义尚未逐项迁移。原端整数Point逐步取整与Unity浮点步进亦非逐位等价。
轮回1016已连接原图腾召唤图、20分钟冷却、10分钟地图效果；MapSpawner复制mobTime>=0的刷怪点并将延迟乘0.5，换图/到期清理；实际地图视觉与Android实机仍待验证。

## 验证入口

`InventoryPortalChecks.Run`使用隔离DbStore和替换Profile，检查真实装备槽/镜像、戒指互不覆盖、图腾授技、卸装、type2读写、携带放下不误使用、原NPC菜单、原物品JS扣除/重复拒绝、明珠港原IMG隐藏门、非Boss吸怪与换图解锁。
编译与脚本检查的本轮最终结果见`docs/skill-regression/inventory-portal-2026-10-30.txt`。没有Android实机验收。

## 2026-10-30 继续迁移（进行中，不能视为全物品已完成）

源端依据：UseSummonBagHandler、SkillBookHandler、ScrollHandler、UseCashItemHandler、DamageCapService、StatEffect、Character.activateCouponsEffects，以及 nxcoupons 两个迁移SQL。SQL身份保存在 Assets/Resources/ItemCouponSchedules.json.sources，由 tools/export_item_coupon_schedules.py 原样导出44条时段规则。

- NativeItemUseEffects：210召唤袋按mob/id与prob独立抽取；228/229按职业、reqSkillLevel/masterLevel及scroll_chance_rolls；技能上限不再依赖持有书籍，未学技能的master也持久化。
- 普通卷轴走 NativeScrollService：原分类、req、升级次数、白卷保护、织女星、混沌/诅咒、GM/config；原装备实例属性参与战斗。白卷/织女星的窗口与动画仍待接线。
- 203城镇卷轴读spec/moveTo、跨大陆条件、目标图随机出生点；2030100回驱逐前位置限5分钟；NativeMobController驱逐先记录来源。
- spec/isHasItem按原hasItemId/Quantity/Time生成带分钟到期的物品，加载/使用/运行时清除到期；26件缺脚本物品经用户决定保持源端不可用并归为源端缺失。
- 原damage_cap_breakthrough.js保持原文，NpcSession保存ItemId，im.getItemId/isItemScript/useDamageCapBreakthroughStone接入；2431152..7增加30万/50万/200万/1000万/1亿/5亿，50%成功，溢出不增但扣石，persist damageCap接攻击/追击。
- 倍率券按SQL星期位掩码与[start,end)小时生效，每个背包行一次而非quantity；use_stack_coupon_rates决定乘积或最大值，掉落券同时影响金币与Boss掉率。
- NativeCashEquipmentService：506取名/封印、550沙漏、552剪刀、557金锤；目标装备原约束、成功才扣现金物品。520钱袋读info/meso，545打开原shop1338成功才扣。
- NativeInventoryActions扩展药品解除疾病、2022450..2 EXP_INCREASE、金字塔BOOSTER/BERSERK和道场/两种恢复药的HPREC/MPREC；NativeBuffs按原mhpRRate/mmpRRate*100毫秒周期恢复。部分尚无完整视觉的状态不能算完成。
- NPC canGenerateItem校验原IMG，gainItem仅真实新增成功才返回物品；canHold/canHoldAll经传输在副本背包用原TradeItemRules模拟容量，不写存档；isNotCanHold按原栏空格数并sendOk+dispose。

实际验证：InventoryPortalChecks.Run 第二轮PASS76，日志 /tmp/mxd-all-items-round2.log（最终归档前须复制入docs/skill-regression）；包含突破石原脚本、伤害上限溢出、倍率券时段排他上界。其后新增现金装备/容量/恢复药代码仅编译检查通过，尚未新一轮Unity验收。全部缺口仍按审计继续，不宣称全部完成。

### 后续验证纠正

第三轮Unity编译受到并发登录页改动影响，未运行；第四轮虽然89项断言通过，但WorldQuest公告路径报player.getWorld/Server.broadcastMessage缺接口，不能算脚本全分支通过。修复了player.getWorld、gainExp/gainMeso、PacketCreator.serverNotice→ScriptNotice→NativeSystemMessage和Server.broadcastMessage的真实本机投递；并对LastError加断言。

第六轮`InventoryPortalChecks.Run` **PASS99**且无“未迁移接口”/脚本异常，证据`docs/skill-regression/inventory-items-round6-2026-10-30.txt`。新增验证：容量查询不发物品/拒绝不存在物品、金锤拒绝已穿戴且不扣除/背包装备成功增加slot与vicious、WorldQuest原材料扣除/金币道具奖励/完成标记/公告、北斗之星数字输入→确认→奖励、skillPoint原JS扣书、237兵法书拒绝超maxLevel/合格时积累gachaexp。

UseSolomonHandler写gachaexp而非立即gainExp，按info/maxLevel（默认256）与Integer.MAX_VALUE判定，NativeInventoryActions与NativeInventoryService已接。源Character.gainGachaExp另有一次最多当前升级需求加下一级需求的领取逻辑，领取输入入口仍待核实，不能说已完成完整暂存经验功能。

Morph逆向新增已确定入口：EXE身份仍上表，`0x40B39D..0x40B91D`读取字符串3533 `Morph/%04d`，缓存原始morph属性，读info/link（961/962）并递归合并非info动作节点。`0x40BA97`调用此缓存，动作名称取`0xBEC568 + actionIndex*4`。这只证明资源加载/链接，不足以确定控制、速度与跳跃覆盖，现已新增原Morph动作渲染与MORPH切换；控制、移动物理覆盖尚未完整验证。


### 继续接线与验证边界（2026-10-30，仍未完成）

原Java身份（路径相对src/main/java/org/gms）：
- net/server/channel/handlers/UseCatchItemHandler.java SHA256 a323e46952f146d684bd708a64def7cbba79250cfc68c615eb5ec62fbb51a847。
- net/server/channel/handlers/ItemRewardHandler.java SHA256 9e4be6c9a9d8c03dabb2af4e8f698c1ba32ef304ae6ff55c6f8deded1d0ba986。
- client/inventory/manipulator/InventoryManipulator.java SHA256 defa04b22c7989773d73b65be6a506de899c6dc9afc5a1ee27b3b1fe534dbc21。
- net/server/channel/handlers/UseCashItemHandler.java SHA256 ebd7efbe79c269524fba0b6cf5a60779ad33311d333081e6b2b5bfefe7dac34c。

NativeCatchItems迁移2270000..8原目标/奖励；生命阈值严格小于(maxHP/10整数)*3或4，通用为(maxHP/100整数)*mobHP。袋子仅高HP失败计1秒、速成石满足HP且有容量后尝试计800ms、渔网尝试计3秒，通用高HP失败计useDelay；其余不得共享冷却限制。目标矩形目前按原info/left/right/top/bottom选最近同模板实体，镜像范围/EXE实际选敌尚待核实。原ItemEff动画保留Canvas origin；实际附着高度尚待核实，Ariant分数联动未迁移。

NativeItemUseEffects.RewardBox遵循ItemRewardHandler：prob转signed byte后求和，按顺序每行独立nextInt(total)<prob，不是权重递减轮盘；每行先checkSpace，首成功即停止。装备period保留源端period*60*60*10毫秒（源码TODO疑似bug），不自行纠正。非装备永久，worldMsg替换/name与/item。Effect字段服务端读但未使用。此路由新增60条静态入口，尚不能视为运行验证完成。

InventoryManipulator.addByIdInternal：非充能物逐槽按slotMax合并，owner空/null语义和flag限制；合并会覆盖expiration，新槽按slotMax拆分。充能物单独新槽且保存传入quantity。NativeInventoryService的普通正数量奖励与限时奖励复用AddItems并预检容量，修复原项目无限堆叠。容量查询仍复用TradeItemRules，owner/flag极端边界需与checkSpace进一步对齐。

Character.commitBuffCoupon受show_coupon_buff与非商城状态控制，原StatEffect按spec/expR=1..4、drpR=1..3创建COUPON_EXP/DRP状态。NativeCouponRates返回当前有效券ID，NativeEquipmentEffects同步原图标状态、失效取消；倍率仍由服务端计算，不重复乘入状态。新增图标同步尚未Unity运行验收。

新增回归断言覆盖Morph加载/使用/取消、费洛蒙捕捉野猪奖励、七天封印flag/expiration、普通药品堆叠上限、券图标时段与原奖励箱首行独立抽取。第八轮共享工作区并发编辑导致家族窗口/怪物图鉴编译错误，未获得有效PASS；随后dotnet编译恢复0警告0错误，但不能据此宣称新断言通过。最后有效运行证据仍为第六轮PASS99。


白卷提示原EXE复核：身份仍1b91cf5b…134b2。0x4F4E64..0x4F4EF4明确排除2040727/2041058/2049000..3，0x4F4E99在消耗栏2查询2340000，已有参数非0则沿用，否则0x4F4EBA取串3963，调用0x992BFD；返回6映射ws=2，其余ws=1再调用0xA09221。字符串3963原译“你想使用祝福卷轴吗？”。NativeItemUseEffects.Scroll复用已核实CUtilDlg NativeUtilPrompt，确认后传whiteScroll给服务端；无白卷/排除卷直接请求。提示新接线尚待运行验证。EXE中只有2340000触发询问，服务端兼容2340001见ScrollHandler96..102。

封印到期补核：Character.expirationTask3083..3136先检查LOCK=1，到期清flag并置expiration=-1，不删除物品；宠物到期先取消summoned，use_erase_pet_on_expiration或蜗牛5000054才删除pets/petignores，其余保留物品并置expiration=-1。InventoryDao/NativeCashService同步此优先级。此前统一删到期物品会丢封印装备和普通宠物，已纠正。

宠物基础正在接入：Schema保留V1.0.29 pets/petignores原列，InventoryDao.InsertItem创建宠物行，现金存取保留petid；CharService携带petData。原waterOfLife.js由5180000→NPC1032102打开，cm.getDriedPets按CASH宠物expiration<now且存在pet行列举；原JS setExpiration(Date.now()+90天)经forceUpdateItem持久化，成功才原JS扣水。NativePetItems迁移212食品：500ms门限，选最饿已召唤宠物，饱食+30，原饱食<=75亲密+1，吃撑亲密-1/可降级；524食品按原spec数字子节点匹配，forceEnjoy饱食+100/亲密inc，成功才消耗。tools/export_pet_item_rules.py导出原0524 IMG与ExpTable.pet31项阈值及源哈希。宠物召唤/跟随/饥饿/命名UI与技能仍未完整迁移，不能标记宠物全功能完成。


脚本地图坐标纠正（继续验证中）：原killarmush.js以map106020300的obstacle传送门与角色java.awt.Point.distance<240判定；removethorns.js用106020500/investigate2距离<150。NpcApi旧getPortal返回任意名字且位置等于玩家，现改为FieldCatalog真实名字/索引、缺失返回null，Point提供x/y/getX/getY/distance欧氏距离。角色坐标仍遵守worldX*PPU、-FeetPos.y*PPU。回归fixture必须设置真实坐标，不能PlaceAt被当前地图XMin/XMax夹住后误判业务。

killarmush原showInfo路径`Effect/OnUserEff/normalEffect/mushroomcastle/chatBalloon2`省略.img；AbstractPlayerInteraction.java743..744原样传PacketCreator.showInfo，资源实际为Effect/OnUserEff.img下normalEffect/mushroomcastle/chatBalloon2。隔离Unity第三轮发现旧WzAssets.Node将其当文件导致脚本失败；现仅在已有IMG边界补后缀解析，不新增/绘制资源。第四轮验证中，不能引用旧PASS99声称此分支通过。

MonsterVac停止取消键纠正：NativeBuffs.ApplyItem将物品id转负sourceId，StopVac必须Cancel(-2431158)，此前正id取消不会移除状态。捕捉ExpireNative在Editor验证改用DestroyImmediate，运行时保留原die动画时长后的Destroy，不产生掉落。

原AbstractPlayerInteraction.removeAll895..912先countById移除对应背包全部，再对装备类额外移除EQUIPPED中一件；第四轮无异常但断言发现NpcApi旧接口为空实现。新增NativeInventoryService.removeAll真实持久化并回写Profile，保留原脚本，不在C#重写结界/荆棘业务。第五轮待验证。

隔离工程第五轮InventoryPortalChecks.Run PASS125，日志docs/skill-regression/inventory-items-isolated5-2026-10-30.txt。覆盖Morph原动画/使用取消、捕捉奖励、封印到期解锁不删除、堆叠拆槽、券状态时段、奖励箱、宠物到期玩偶/生命之水90天/食品、无队伍伴侣拒绝、结界远近距离与真实扣物品/任务、荆棘完成任务。无脚本异常/未迁移接口错误；Unity仍有现存JobSystem不可达警告，不代表Android实机验证。


织女星窗口逆向新增（实现前记录，EXE身份仍1b91cf5b…134b2）：0xA0B6A4..B9以itemid%10作为构造mode，0x82B89C构造；mode0用串5396/backgrnd10(178x235,origin0,1)，mode1用5397/backgrnd60(178x235,origin0,0)。vtable B39594：OnCreate82BB83、Draw82C28B、OnButton82CBE2、鼠标82D793。OnCreate从Etc/VegaSpell.img枚举201行item白名单；BtStart串5400于(67,209)，id2000，初始禁用；BtCancel串5577于(158,8)，id2。Draw把EffectArrow/0画(80,36)，装备图标锚(30,157)、卷轴(117,157)；鼠标悬浮框装备x31..56、卷轴x117..142，y117..157。

82CE58接受type1装备且upgradeSlots>0，type2仅204卷；排除2041211、2041212、2049100，还须Etc/VegaSpell.img白名单。按原Item IMG info/success校验mode0=10、mode1=60，两项齐全启用Start；82CBE2再验证卷装兼容(4F5497)，Start发送原type/slot组合，禁用按钮，开始表现。服务端UseCashItemHandler650..711调用原scrollEquipWithId(vegaItem)，消耗卷与咒语，3秒后回写实例并广播强化效果。82C61A进度条y182，x19..158，每秒63px；不是猜测倒计时。82D8D5接收0x40/0x45开始表现，0x41/0x43成功/失败。结果窗构造82DA77：成功SuccessWnd、失败FailWnd(152x159)，创建82DC1F的BtOK(55,135)，Draw82DE6D装备锚(60,97)。主窗计数字图Count：mode0的10→30、mode1的60→90于(31,37)/(39,37)、(123,37)/(131,37)。动画/拖放实现尚未完成，不能据逆向记录声称已接入。


织女星定位补核：共享创建例程0x4EDAEF加载资源后按(800-width)/2、(600-height)/2调用0x9DE4D2，主窗及结果窗均居屏幕中央；此前结果窗作为主窗子区域是错误定位，现按移动视口居中。NativeVegaWindow提交立即同步权威背包与Profile，只将视觉结果延迟三秒，避免动画期间其他保存把旧背包覆盖回数据库；关闭不会丢结算。新增运行回归尚未通过：共享PreviewShot存在重复case43..49及double/float编译错误，隔离第六轮待检查。装备仅供织女星选择，放到窗口外不得落入键盘/快捷栏绑定。

ItemInformationProvider.getItemEffect1384优先specEx、缺失才spec；StatEffect284..286读取0/mobSkill、level、target，1154..1168给疾病，target>0只给同地图其他玩家（排除使用者），不走怪物施法概率。原Consume0202仅2022225/26/27、2022268四件specEx：分别125lv7、124lv8、122lv8、132lv1(target1、consumeOnPickup1)。NativeInventoryActions新增此分派，按原MobSkill时间/x/y和Character.giveDebuff保护规则处理；本机单人会话target1不施加自身疾病，其他真实玩家的联机传播仍无运行环境，不把该边界算作完整联机迁移。新增八项运行断言待验证。


隔离第七轮PASS143，归档docs/skill-regression/inventory-items-isolated7-2026-10-30.txt，无脚本异常/未迁移接口。此轮仅在临时隔离工程排除共享PreviewShot.cs编译错误，未改动共享预览工具；覆盖织女星选择、即时权威结算、动画期间保存不恢复物品、重复提交拒绝，及specEx四件疾病/目标规则。其后新增织女星动画仍须再验证。

织女星特效helper0x43C186（604条指令）证据docs/skill-regression/vega-effects-helper-2026-10-30.txt：mode0字面量AF0FC8=EffectSpelling锚(85,114)、2500ms定时；mode1 AF101C=EffectSuccess锚(79,71)；mode2 AF106C=EffectFail锚(6,45)；mode3 AF10B8=EffectArrow锚(91,41)；mode4 AF1104=EffectTwinkling锚(85,114)、2500ms。原Arrow直接Canvas copy坐标(80,36)是左上角，不能再减origin(11,5)，现纠正为动画锚91,41减origin。Spelling五帧各500ms，Twinkling15帧各50ms；成功18帧90/120ms，失败六帧150ms；沿原节点origin/delay播放。Sound/UI.img包含VegaSuccess/Fail/Twinkling，旧SoundIndex未导出；export_sound.py现补三条并原字节导出，未自行合成。原0x82D9D8以mode4、0x82D984以mode0开始，结果窗82DCFC按成功1/失败2调用helper。


隔离第八轮PASS143（同样排除共享PreviewShot），证据docs/skill-regression/inventory-items-isolated8-2026-10-30.txt；原特效代码已编译且业务回归通过，未进行窗口截图视觉验收。音乐盒UseCashItemHandler397..399仅广播musicChange("Jukebox/Congratulation")并消耗一个；已将原Sound/Jukebox.img/Congratulation MP3载荷67秒原样导出（654.6KB）接AudioManager.PlayBgm，换图沿既有地图BGM恢复，不添加自定义界面。该新增分支尚待运行验收。


现金表情5160000..14复用PlayerCtrl.Express(id-5159992)：FaceExpressionHandler校验拥有道具、不扣除；现物品入口接入原已实现表情链路，仍遵循SpriteAnim两秒门限和五秒回默认脸。原服务端SpawnPetProcessor54..96：5000028/47拒绝已有id+1，否则取Pet IMG info/evol1创建新pet并替换原物品，保留expiration，本次不召唤；普通宠物双击切summoned，无技能8先收回旧宠物，最多三只。新增NativePetItems.Toggle与真实持久化，规则导出含357个Pet IMG的hungry/evol1与逐文件哈希；只完成服务端切换及孵化，角色宠物视觉/跟随/技能/饥饿调度尚未接完，不能算宠物已完成。原runFullnessSchedule9437..9457减info/hungry，<=5改15并收回；World1462..1524按pet_exhaust_count轮次调度。新断言尚在第九轮隔离执行。

现金持续特效继续逆向，不能按猜测挂载：原0x93B6A7读取action/follow/tamingMob/spectrum/interval/delay/alpha，资源串3139 Item/Cash/0501.img/%08d/effect，action决定0x93BEB9与0x93C218分派；后者资源串3589附加当前动作子节点、读loose/fixed。证据docs/skill-regression/item-effect-client-2026-10-30.txt；挂载函数仍待查，暂未写显示实现。

第九轮隔离PASS152，docs/skill-regression/inventory-items-isolated9-2026-10-30.txt；包括音乐盒消费/表情不消费、宠物召回/召唤持久化、到期拒绝。隔离排除共享PreviewShot，未运行Android。后续织女星将Screen改UiKit虚拟坐标，避免GUI矩阵二次偏移；此小改尚待检查。


### 轮回复活与外观券修正（2026-10-30，继续迁移）

现金分类器的可复现工具为 tools/export_cash_client_routes.py，使用 Unicorn 执行同一 SHA256 原EXE的纯函数48645B/4863D5，结果 docs/cash-client-routes.json（514行）。cashType=0不能统称不可用：其中包括NPC外观券、死亡轮回和被动道具的独立入口；519虽cashType28，但普通现金分派落默认无操作。完整现金分派证据 docs/skill-regression/cash-use-client-2026-10-30.txt。

死亡轮回5510000：原EXE 897C94..8981DD（docs/skill-regression/revive-item-dialog-2026-10-30.txt）、8981E1按钮6/7与898224→53035D发复活请求；Notice/2为286×146，BtYes2(92,115)、BtNo2(148,115)，600秒后否。4A95BF门控按特殊field值和地图前缀排除；字段虚表身份仍须进一步核实，暂不作为完全对齐证明。服务端ChangeMapHandler90..118在持有轮回且请求使用时扣一个，HP50、MP保持、当前图findClosestPlayerSpawnpoint；MapleMap3107只取pt0..1且tm999999999的最近点。现ChangeMapRequest/DeathFlow/FieldMapInfo接入此流程；SaveToDb死亡HP允许0，避免旧Mathf.Clamp下限1让服务端误认存活。隔离第十一轮PASS158，证据docs/skill-regression/inventory-items-isolated11-2026-10-30.txt，新增六项覆盖同图/HP/MP/扣物/最近出生点及原素材。Unity ILPP启动曾报连接异常后重试成功，未发生业务脚本异常。未验证Android死亡交互、复活buff优先级与剩余数量文字。

NPCConversationManager379..397的setHair/setFace/setSkin写真实ID、updateSingleStat并equipChanged。NpcApi旧setHair错误映射通用发型序号，setFace/setSkin为空；现保存CharRecord真实hair/face/skincolor并RefreshFieldAppearance/SaveToDb。新增持久化断言待验证。sendStyle仍是错误的通用文字菜单，正在迁移原生type7样式选择，不得据setter修正称外观券已完整完成。

织女星UOL帧时长修正：动画使用WzAssets.Frames及frame.Resolve().Int(delay)，避免Arrow引用帧3..5错误退回100ms；此改与外观setter断言尚未取得有效新运行证据，共享预览/家族检查处于并发编辑。


后续有效回归：隔离14 PASS161新增真实hair/face/skin持久化；隔离15 PASS166新增原1012103两阶段样式/发色流程、style原数组/type7、前后循环/preview真实ID。证据分别docs/skill-regression/inventory-items-isolated14-2026-10-30.txt、inventory-items-isolated15-2026-10-30.txt。完整dotnet编译当时0警告0错误，原NPC harness pack2779解析0失败；mock --run仍790ok/27runFail/72javaInterop，不能说全量脚本通过。隔离14开始仅保留物品两个Editor检查，其余共享Editor工具在临时工程排除，运行时代码仍全部编译；未修改共享工具。

新增源端特殊入口：ScriptedItemHandler.handleWorldQuest88..153在2430680优先于getScriptedItemInfo拦截，不执行WorldQuest.js；20项2430681..700各权重5，5项2430701..705各1，总105；1秒角色冷却，存在性校验后抽取，checkSpace之前记录冷却，容量不足亦进入冷却；成功增奖励1再扣指定槽原物1并发type5消息。现NativeInventoryService.worldQuestDraw接权威背包并由NativeInventoryActions优先路由，保留原JS不改。2260000原StatEffect1072..1073以spec/incFatigue=-30加到坐骑疲劳，Mount.setTiredness88..92只下限0；已持久化。隔离16 PASS171，证据docs/skill-regression/inventory-items-isolated16-2026-10-30.txt（新增抽取/冷却3项、疲劳2项）。

宠物饥饿：World1462..1524、Character9437..9457、PetDataFactory63..74及V1.7.0配置：pet_exhaust_count默认3，pets_never_hungry=false，gm_pets_never_hungry=true。按全局60秒轮次注册时初值count-1（距上次tick>55秒为count-2），每tick加1等于count才消耗info/hungry（默认1），fullness<=5重置15并收回，不扣宠物道具；原提示“您的宠物饿了！给它一些宠物食物以保持它的健康！”。NativePetItems.Tick接入现有会话每秒服务端检查，EndSocialSession释放该角色调度状态，离线不补计饥饿。隔离17 PASS173，证据docs/skill-regression/inventory-items-isolated17-2026-10-30.txt，新增未到分钟不扣/到期回家与15饱食、不删宠物。此后新增EndSession清理只做静态检查尚无新PASS。现仍未迁移宠物跟随、原端petSlot顺序/lead与全部技能，不称全宠物完成。


隔离20回归PASS182，证据docs/skill-regression/inventory-items-isolated20-2026-10-30.txt，无脚本异常/未迁移接口。新增覆盖2431151原JS实际目的地105040300（注释910700000不作为业务依据）、2435719 core_gemstone_inert原JS仅enableActions/dispose且不消耗。修复NpcApi缺失im.enableActions：AbstractPlayerInteraction1194..1195仅发送PacketCreator.enableActions，本地执行没有网络动作锁，沿已有player.enableActions空操作语义接入；不是改写原脚本。旧测试通过Active?.LastError漏掉已关闭失败会话，改为检查UseItem返回的真实session.LastError。

宠物食品槽位校正：原PetFoodHandler读取pos并校验USE槽、itemId/quantity，再removeFromSlot；NativeInventoryActions现在传sourcePosition，NativePetItems.Feed校验相应USE/CASH槽且只扣该堆叠。无匹配槽不扣食品、不改变饱食。食品500ms冷却键改为(DbStore,cid)，EndSession释放，避免隔离存档同cid互相限制。PASS182新增错误槽拒绝和指定堆叠50→80饱食/扣1验证。最新完整dotnet编译0警告0错误；NPC pack2779 parseFail0/mangled0，mock run仍790ok/27runFail/72javaInterop，不称全量通过。


隔离21 PASS184（docs/skill-regression/inventory-items-isolated21-2026-10-30.txt）增加多宠物召唤后修改背包位置仍按会话宠物槽选取524食品。原Character.addPet796..809从首个空位加入，removePet7953..7977按shift_left收拢；NativePetItems缓存(DbStore,cid)宠物ID顺序，每次与summoned真实行调和，EndSession释放。登录初始沿CharService数据库返回顺序；lead=true的shiftPetsRight9351尚未接入界面，跟随/技能仍未完成。519宠物技能券cashType28明确落A0EAAC原客户端无操作，现普通双击保持无操作、不消费，并在静态审计列source-disabled:cash-client-no-op，不伪造技能习得。后续新增该入口尚未运行验证。


隔离22 PASS188，归档 docs/skill-regression/inventory-items-isolated22-2026-10-30.txt；新增2431159原伴侣脚本建队成功后召唤5只、活动期间重复不消费，519原无操作分支亦已编译（未单独行为断言）。用户确认26件源端缺失脚本道具保留不可用并标注源端缺失。

宠物进化原服务端核实：AbstractPlayerInteraction.java546..584 evolvePet获取召唤slot，调用gainItem(after,1,false,true,90天,from)，再删除旧宠物道具，返回局部evolved始终null。gainItem610..638新建petId，继承level/tameness/fullness；名字等于旧模板名则换新模板名，否则保留自定义名；summoned=true；有效期当前时间+90天。NPC1032102.js原样决定等级15/5380000/龙5000029门槛及随机30..33。本次NativePetItems.Evolve经ChangeInventory传输、NpcApi cm/player.getPet与evolvePet开始接入；dotnet0警告0错误，隔离23原脚本运行验证中，尚不能称回归通过。宠物视觉/跟随边界不变。


隔离23 PASS191，docs/skill-regression/inventory-items-isolated23-2026-10-30.txt，原1032102进化通过：扣5380000、旧5000029删除、新5000030..33不同petId，15级/1800亲密度/73饱食/自定义名字与90天有效期保留。无脚本异常/未迁移接口。后续gainTameness接入NPCConversationManager432..438遍历已召唤宠物，Pet188..230 incFullness0允许100饱食加亲密，inc<=0不减，封顶30000并按原表升级；该后续修改未包含在PASS191。

紧凑文本框CUtilDlgEx证据归档compact-input-controls/util-text/util-mode/style-background/util-button-2026-10-30.txt：同EXE，9AC7CB类型3、A4=1；宽266，Notice3/t,c+Notice4/s，正文20,22，总高textHeight+99；9A8716编辑框19,height-73,226,15，padding2；BtOK2为158,height-30，Cancel2为208,height-30。9AB50A提交trim两端并检最小长度，编辑控件检最大原生字节长度。NativeUtilPrompt.Text基础窗口已沿此坐标纠正，未接具体物品；非ASCII字节长度/过滤仍待核实，不称改名/黑板完成。


隔离25 PASS193，日志inventory-items-isolated25-2026-10-30.txt，新增NPC gainTameness真实+5持久化与负值无操作；隔离27 PASS195，日志inventory-items-isolated27-2026-10-30.txt，新增同DbStore两角色共享分钟皆处理、同分钟不重复，5000000原PetItemRules hungry=2（50→48），不是测试最初错误假设的1。HungerClock新增每角色已处理轮次，保留全局分钟基准，解决先tick角色使其余跳过的缺陷。最新dotnet编译0警告0错误。

后续补PetRequirement.java56..72与MinTamenessRequirement.java52..68：前者任一召唤宠物模板命中，后者所有召唤宠物最高亲密>=门槛，两条件独立，不要求同一宠物。旧QuestLogic无条件拒绝占位改为通过传输读取真实slot0..2。新增召唤/背包持有区分验证待执行；不沿用PASS195宣称此后修改已验证。


隔离28 PASS197，inventory-items-isolated28-2026-10-30.txt验证召唤宠物通过实际QuestLogic条件、相同宠物仅在现金背包则拒绝。之后新增Quest/Act节点pettameness：QuestActionType82及PetTamenessAction49..64只取chr.getPet(0)，无领头则无操作；与NPC gainTameness奖励所有召唤宠物不同。QuestCatalog读取原IMG pettameness并通过petLeaderOnly请求接入，验证中。

黑板537调查继续：原UseCashItemHandler492拒绝FMroom，设置chalkboard并广播，不消耗。EXE cashType32从A0CE2E起CUtilDlgEx type3/A4=1，串1391 Enter a note，最大40；已有黑板先询问清除。角色93760D处理接收，937745→48E298→48A935(type1003,duration0)→4890C4，资源UI/ChatBalloon.img/adboard/0。489D6C(type1003)的水平偏移3-innerWidth/2-leftWidth，竖直-height；九宫格各项origin参与布局，尚未完整解出，禁止以普通聊天九宫格猜画。原始汇编adboard-layout-full/adboard-balloon/balloon-nine-2026-10-30.txt归档；功能仍未迁移。


隔离31 PASS199，inventory-items-isolated31-2026-10-30.txt：新增QuestActions pettameness只奖励领头（+7，不改随从）；原quest4647.js使用5460000完成任务并teachSkill8真实通过。测试NPC从原Quest/Check/4647/1/npc读取1012006，旧测试1032005导致被正常NPC门控拒绝，不能改业务绕过。

普通现金双击入口进一步确定（同EXE）：4EFD25按inventorytype5，4EFE04→48645B，8..11/38走A1DD28；否则4EFEA7→4863D5，非0走A0A63F；为0则4EFEE2→486845，非0走A1DC5B。A1DC5B只处理分类4/5/6/59，其余直接返回。故515(1/2/3/0)、522(39)、525(35/36)、538(41)、542(54)、546(57)、549(58)普通双击均无请求，不应误报特殊功能未接入或伪造打开NPC。NativeInventoryActions现保留道具无操作；其真实NPC/任务业务仍需逐项接完，审计158件标npc-route-needs-runtime-check，不等于158件功能已完成。证据inventory-cash-use/special-cash-route-2026-10-30.txt；分类由cash-client-routes.json及原纯函数模拟复现。新增普通双击不消费断言验证中。


用户于本次收尾决定停止新增功能，下次继续。隔离33 PASS215，日志docs/skill-regression/inventory-items-isolated33-2026-10-30.txt，无脚本异常/未迁移接口；包含8类券真实种子普通双击不消费/不误开流程、原4647点心只扣1、宠物服务及任务奖励回归。隔离32为测试种子5250000不存在导致null，改真实5251000并校验seed.ok；并调整额外5460000种子后数量断言。完整待办与原端调查断点见docs/inventory-functions-next-session.md；未接现金225/消耗82、NPC任务券158待逐项验证、源缺失26按用户决定保留；不称全功能完成，不称Android已验收。


### AP/SP 洗点窗 505（2026-10-30 新增逆向，EXE 身份仍 1b91cf5b…134b2）

现金使用分派 `0xA0A63F` 的跳转表在 **`0xA0EAD4`**，下标 = `cashType-12`（`0xA0A6D9: add eax,-0xC; cmp eax,0x39`），共 0x3A 项。已核出的键位：22→`A0CAB0`(传送石)、23→`A0C427`(AP)、24→`A0C6DD`(SP)、32→`A0CE2E`(黑板)、34→`A0E9F6`、46..51→`A0D037/A0D472/A0D866/A0DCCA/A0E127/A0E556`、60→`A0A6ED`、69→`A0A8F1`(礼包)。**先读这张表再逆任何现金窗，别在 `0xA0A63F` 主体里找分支。**

AP 分派 `0xA0C427`：`0xA14A1C()` 非 0 时用串 150「不能与别的操作同时进行。」早退；随后依次建**三个窗口对象**（`new 0xBC` → ctor `0xA0EFBE`，`new 0xBC`，`new 0x9C` → ctor `0xA0F1B1`），每个都走共享创建例程 `0x4EDAEF`（按 `(800-w)/2,(600-h)/2` 居中），`0x4EDBA1` 返回 1 表示该页确认。
- 参数对象字段：`+0xB4`=APFrom、`+0xB8`=APTo（`0xA0F019` 写入、`0xA0F0D4` 读回）；`+0x94/0x98` 是确认页的 from/to（`0xA0F1E5`/`0xA0F24A`）。
- 页序：`0xA0F030` 按 `[+0xB4]` 选底图 —— 为 0 用 `Reset/AP/backgrnd`，否则 `Reset/AP/backgrnd2`；`0xA0F1FC` 固定用 `backgrnd3`。
- **请求字段序**：`0xA0C697` 先写 `[ebp+0xC]`（= 第二页选中的 APTo）再写 `[ebp+0x10]`（= 第一页选中的 APFrom），与 `UseCashItemHandler175..235` 的 `readInt()` 顺序（先 APTo 后 APFrom）一致。

资源（字符串池 2928..2933，均在 `UI/UIWindow.img/Reset/AP/`）：
| 节点 | 尺寸 | 用途 |
| --- | --- | --- |
| `backgrnd` | 175x220 | 第 1 页「请你选择想恢复的能力值。」 |
| `backgrnd2` | 175x220 | 第 2 页「你要在哪儿再分配能力值点?」 |
| `backgrnd3` | 175x141 | 确认页「确认这样恢复吗?」 |
| `stat0/<0..5>` | 159x16 | 确认页行（`0x8CD937` 传 0） |
| `stat1/<0..5>` | 159x16 | 第 1/2 页行（`0x8CC915` 传 1） |

行图 6 帧顺序经 `0x8CE3FF` 的位掩码 switch 定案：**HP→0、MP→1、STR(0x40)→2、DEX(0x80)→3、INT(0x100)→4、LUK(0x200)→5**（HP=0x800、MP=0x2000）。行图 x=8（(175-159)/2）。

控件坐标（`0x8CB9CF..0x8CBC80` 页1/2、`0x8CD6F8` 确认页 的 push 常量对；调用约定已由织女星 `(67,209)`/`(158,8)` 校准为 `(this, id, x, y, ?, &out)`）：
- 每行一个箭头按钮 12x12：**x=153，y=74/92/116/134/152/170**（对应 HP/MP/力量/敏捷/智力/运气）。
- 箭头图：`[+0xB4]==0`（第 1 页）用 `Reset/Common/BtDown`，否则用 `Reset/Common/BtUP`（`0x8CB9CF` 的 2926/2927 分支）。
- 关闭 `UI/Basic.img/BtClose` 于 (156,6)；确认页 `BtOK2` (70,117) id=1、`BtCancel2` (119,117) id=2、`BtClose` (156,6) id=2。

**仍待核实**：行内数值/`(-%d)`/`(+%d)` 文字的精确 x（`0x8CC8DE` 里 `0x8CCC21` 起算出文本宽，`0x414576` 设置位置，但常量对未解全）；确认页正文行 y。迁移时数值用行内右侧空白格右对齐，属**推断**，须以实拍视觉核对后再定稿。

服务端语义（`AssignAPProcessor.APResetAction`）：APFrom 为 64/128/256/512 时对应属性必须 ≥5 才能扣 1；HP(2048)/MP(8192) 走 `getHpMpApUsed()<1` 拒绝（「你没有足够的HP、MP属性点用于AP重置。」）及洗血公式；成功后 `addStat(APTo, true)` 加 1，`UseCashItemHandler` 最后才 `remove(c,position,itemId)`，失败不扣道具。

迁移状态：`NativeResetWindow`（当前只接 5050000）+ `Profile.ResetAp`。本工程 `StatWindow` 的 HP/MP 加点箭头常驻禁用、`hpMpUsed` 恒 0，所以 HP/MP 行按源端 hpMpUsed<1 分支拒绝，不伪造洗血。**5050001..3（SP 洗点，cashType24 → `0xA0C6DD`）尚未迁移**；其资源为 `Reset/SP/{backgrnd 175x289, backgrnd1 175x28, backgrnd2 175x289, backgrnd3 175x230, skill0/skill1/line→../../Skill/*, Tab/enabled|disabled}`，字符串 2934..2943。
