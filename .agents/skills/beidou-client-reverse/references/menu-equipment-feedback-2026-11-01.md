# 十二项菜单/装备/转场反馈（2026-11-01）

本记录区分确定的源端语义、当前修正与尚未迁移能力；不得把菜单首屏可打开或编译通过当作玩法完成。

## 身份和可复现证据

BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`，路径 `Beidou-Server/clien/BeiDou.exe`。
源端 Java/JS 为同目录 `gms-server`；IMG 为 `MapleDashResources/Data`。

本轮新定位名牌路径的材料归档于 `docs/evidence/menu-feedback-2026-11-01/`：
`name-tag-fn.txt` 为 `tools/inspect_beidou_client.py <exe> --fn 0x5F0334` 的递归下降结果；另两份为局部线性反汇编，可能首尾失步，不应将其所有行视作函数边界证据。
字符串池2422=`chatBalloon`、2423=`nameTag`、2424=`medalTag`；1303=`UI/NameTag/%d`、1305=`UI/NameTag.img/medal/%d`。
`0x942F9E` 读取装备 info/medalTag，`0x943038` 附近调用共用 `0x5F0334`，kind=1006(0x3EE)。
该函数 `0x5F0574` 取 medal 图集格式串；`0x5F06BB/0733/07AB` 读取三段图，`0x5F0823` 读颜色；字体12px。
`UI/NameTag.img/medal/<tag>/{w,c,e,clr}` 为实际素材，如9：w8×17 origin(8,3)，c5×17 origin(0,3)，e8×17 origin(0,3)。
名牌布局补核：`40B920/40B947/40F09B/40F0C2` 分别取 width/height/originX/originY。
`5F099A` 中段宽度按 c.width 向上取整；总宽=w.originX+中段宽+e.width-e.originX。
`5F0A07..AE5` 基线=max(w/c/e.originY)，总高=基线+max(height-originY)。
`5F0BD2..D36` 先平铺c（x=w.originX，y=基线-c.originY），再w(0,基线-w.originY)，最后e(总宽-e.width,0)。
`5F0DA5..DC9` 文本x=w.originX+(中段宽-文字宽)/2，y=基线-2。
`5F1654/1691` 无图标层origin=(总宽/2,-2)；`5F1979..1A18` 后一非空行在前一层canvas底+1处。
`NativeNameTag` 及 `NpcDialogUi` 已接入名牌戒指和勋章行；尚未完成截图及所有样式验收。

戒指覆盖顺序：`92E964` 循环静态 DWORD 表 `BD8590..BD85A0`=(12,13,15,16)，逐一读可见装备info。
`92EA09` chatBalloon非零时覆盖旧值；`92EA74` nameTag同理；不是首个戒指优先。

聊天布局递归下降证据 `chat-balloon-layout.txt`：函数`4890C4..48A932`。
locals：nw=-30,n=-40,ne=-28,w=-44,c=-7c,e=-4c,sw=-34,s=-38,se=-2c,arrow=-3c,head=-6c。
`48965E..699` 左=max左三图originX；`4896EF..75F` 右=max右三图width-originX。
`48977D..7AF` 上=max上三图originY；`4897CD..88E` 下=max下三图及arrow的height-originY。
`4898EC..935` 最小正文高=max(ne.height-ne.originY+se.originY,nw.height-nw.originY+sw.originY)。
`48993D..55` 普通聊天最多7列n.width，type1005为14列；`489D0E..3E` 单行向上整列后按位OR1（奇数列）。行步长=w.height。
`489E9D..48A1AB` 按列画n/s，中央一列有head/arrow则替代，不是在s上叠加箭头。
`48A1B1..413` 每行依次w、按n.width平铺c、e；`48A413..613` 最后四角。
`489D95..DAA` 主体横坐标=3-正文宽/2-左边距，纵坐标=-总高；`48A754..867` 文本首行y=上边距-1，各行在正文宽内居中。
`NativeChatBalloon` 已接原图合成及上述分块/覆盖/定位；戒指正文已单独按7*n.width像素限宽折行，源端48ED5A转交Canvas Font的GetLongestText，后续trim字符集合在AF2464为" \t\r\n"，不是词边界回退；当前使用项目字体测量，尚未完成全样式视觉验收。
聊天戒指的 info/chatBalloon、名牌戒指的 info/nameTag 确实存在于原装备目录及生成的 Bot/Items.json；伴侣/好友戒指不能据此宣称已支持。

## 已核实的服务端规则与实现

- `src/main/java/org/gms/constants/inventory/EquipSlot.java` 是实际装备位置依据，读取 `info/islot`，现金装备将负位置再减100。
  `Wp/WpSi/WpSp=-11`、`Tm=-18`、`Sd=-19`、`Me=-49`、`Ri=-12`；现金武器=-111，现金戒指=-112/-113/-115/-116。
  `ItemInformationProvider.canWearEquipment` 使用该枚举；旧 BotCustomization 数字分类未覆盖170、190/191，不应继续作为玩家槽位唯一依据。
  当前 `NativeInventoryService.EquipmentSlotOf` 读取同一份原 IMG info 导出数据；`EquipSlot.Worn` 镜像补现金位置及坐骑位置。
- `WzCharacter.CharacterIds` 选同槽现金外观覆盖普通外观，保留真实武器ID作为末项以维持攻击姿势、速度、声音来源；渲染时170点装覆盖真实武器画布。
  170原 IMG 在数字武器家族目录下拥有动作，UOL跨目录；复用源端工具 `tool/wz-python/wzpy/character.py::_render_root` 的数字排序/动作解析办法。
  这是迁移工具的确定行为，不冒称已逆向出 EXE 完整武器兼容限制。未完成全部170武器与全职业动作矩阵验证。
- `scripts-zh-CN/BeiDouSpecial/装备制作/星级强化.js` 的 `validateEquip/isWeapon` 限制 `1302000 <= id < 1493000`；现金武器170不在此范围。脚本本身未另作cash字段判断，tooltip额外隐藏现金星显示是本次用户明确要求。
  tooltip此前给所有装备按等级推算星上限，现清除范围外及现金物品的星/容量。没有重写升级概率或费用。
- `CashOperationHandler.java` 的 `CASH_COUNT=2000`，动作0x06每次扩4格，最高96。原实现误为4000，已更正；成功请求补反馈。
  仓库扩容按钮后续已接入相同原按钮，详见文末；普通仓库存取窗口仍未迁移。
- 原 `BeiDouSpecial/宠物.js` 用 `Pet.createPet` + `InventoryManipulator.addById(client,id,1,null,petId,expiration)`。
  当前补真实 pets 行及运输请求的 petId/expiration，不再以普通 gainItem 丢失宠物身份和90天有效期。
- `Character.getAreaInfos()` 是 `Map<Short,String>`；桥接get/put/containsKey与既有 `updateAreaInfo` 使用同一持久化键；不存在get返回null。
  航海NPC使用32010区存贸易状态；增加Number.toLocaleString以显示原成本，不篡改原JS。
- `Inventory.getSlotLimit/getNumFreeSlot` 按真实栏容量、1..limit已占位置计数，取代固定24。
  `ItemInformationProvider.getEquipStats` 复用真实目录的inc去前缀属性映射，以Java-map风格get/containsKey返回；用于塔奖池属性预览。

`ItemConstants.isNewHair` 使用源端原样复制的 `hair-base-ids.txt`（Resources/Scripts/hair_base_ids.txt），保留0..7色号检查；isAccessory保留原1110000..1139999范围，未知方法仍报错。

## 转场/性能修改边界

`ScreenFade.OnGUI` 保留独立GUI.depth=-20000，每次事件均赋值，物理屏幕全覆盖；之前绘制后恢复depth可能使下一事件与头顶标记混排。
商城开启/关闭通过独立渐变状态机，在完成一帧全黑后才同步加载目录/执行切换；进入地图渐变时拒绝重入。
0.32/0.06/0.32秒仍为项目原适配值，不冒称新核实的EXE时长。Dismiss/销毁恢复输入锁并取消待执行切换。
`NativeEquipmentEffects` 仅在宠物饥饿实际更改库存时重建完整Profile镜像；此前每秒无变化也重建。
这些修正不能代替Android帧时间分析；进图卡顿尚未完成实机验收。
`NativeMedalWindow` 成功佩戴后统一刷新库存/属性镜像，并已接原图装饰勋章名牌，视觉待验收。

## 菜单审计与必须继续的缺口

`Assets/Editor/MenuFeedbackChecks.cs` 为真实GameServer/NpcSession隔离临时库审计，非Mock。
基线 `docs/menu-feedback-audit-2026-11-01-baseline.json`：646条路径、55条错误路径。
遍历主拍卖菜单、航海9390220/9390221、塔2540000、公园三入口、时装暖暖/宠物；最多3次回复，>40项物品目录未逐个购买，未跟进每个目标地图全部NPC/门/击杀/结算。
因此**不能称用户第12项全量验收完成**。

共同的真实缺口：
- 公园29路线：`getInstanceMap/resetPQ` 尚无独立地图状态。见map-intro-and-events，不能把全局地图返回冒充隔离。
- 航海/塔挑战：依赖独立地图、怪物波次/击杀/计时/回调及Java Item等。areaInfos修正只解决贸易对话基础，不等于完成出航。
- 其他拍卖项：HuntTaskAutoPublisher、DatabaseConnection相关业务、转生、EnterMTSHandler等仍有实测宿主缺口。额外仓库、怪物掉落、魔方、Boss成长和Stat的后续接线见文末，不代表这些功能全部路径已验收。
- 聊天/名牌戒指与勋章已接原图绘制，换行及视觉仍需验收；伴侣/好友等现金戒指状态效果尚未完成。

验证结果须以本次最终审计/编译输出更新，不能沿用先前PASS。未构建APK，未做Android实机测试。

## 本轮验证记录

- 隔离 Unity `MenuFeedbackChecks.Run`：646条路径/50条错误路径；与基线相较宠物相关错误和背包/预览接口错误减少，但其余玩法缺口仍然存在。
- 同一入口的定向断言通过：实际运输层穿戴1302000/1702000/1902000/1912000/1112100与负槽身份、现金纸娃娃可合成、宠物真实身份/90天有效期、扩4格扣2000、余额不足不扣款不扩容。这里纸娃娃非空检查不等于全动作视觉验收。
- NPC Mock harness：889条/790成功/27 runFail/72 javaInterop，与旧基线一致；它不运行NpcApi正式桥，因此不能用该结果评价本轮正式Java桥是否接好。
- 脚本包：2779条、889 NPC、470 quest，parseFail=0/mangled=0；没有改源端JS。
- 现金戒指静态目录覆盖532件：含180件chatBalloon、165件nameTag；其中91件聊天样式引用及72件名牌样式引用在当前对应IMG根下未找到（只代表原始路径查找，不等于已排除客户端回退规则）。完整逐物品记录见 `docs/cash-ring-resource-audit-2026-11-01.json`；不得自行绘制缺失素材。

- 最后定向 Unity `MenuFeedbackChecks.RunFocused`：时装暖暖/宠物89条路径，0报错；真实装备/现金/宠物断言及源端发型目录分类断言通过。
- 最终compile-check Rebuild：0 warning/0 error；git diff --check通过。
- 商城打开时BotField停止绘制地图假人气泡/交互层，避免世界UI残留；仍须实际画面对照。

## 后续宿主补齐（继续验证中）

`Character.java:5348 getMaxClassLevel` 按job/1000==1选择qst_max_level，否则mxj_max_level，非正数回退255；新增真实服务端Script操作读取配置，NPC player桥调用。事件setIntProperty按整数写入同一properties，exitPlayer先unregister后playerExit，依据EventInstanceManager.java:292/749。

### 原图合成定向回归

Unity RunFocused 已验证真实IMG nameTag3正文31px→46×18/text(7,2)，medal9→51×17/text(10,1)，chat174→97×81/anchorX50（n.width14，左右32/23，上35，下32）。原图合成PNG保存在同一evidence目录并人工查看三张，尚未覆盖实际带字画面/所有戒指。89条时装/宠物路径仍0报错。

新增NativeScriptItem迁移Item.java三参构造、short数量/位置/flag、owner/expiration及独立copy；四参宠物构造仍明确拒绝，未将Item构造接入等同于塔玩法完成。player.getAbstractPlayerInteraction改为独立于活动NPC的有效宿主，依据Character.java:4630→Client的会话生命周期。菜单审计批处理现在有错误即退出码1，防止脚本报错被退出码0掩盖。

### 航海启动及掉落查询宿主补核

EventManager.java:624/634/646的单人1/2/4参startInstance已保留显式lobby、leader和difficulty（9390220.js:364传route+1）；已占用指定lobby拒绝，不自动挪到别的lobby。组队/远征重载仍未迁移，独立地图缺口仍在。

MonsterInformationProvider.java:170 retrieveDrop按dropperid读取drop_data的itemid/chance/minimum_quantity/maximum_quantity/questid；MonsterDropEntry公开字段为itemId/chance/Minimum/Maximum/questid。NpcApi已接真实DropStore和Java-list/JS数组消费，getMobNameFromId使用原String/Mob.img目录。定位到DropStore.DbProvider此前从未接线，GameServer.Start/StartWith现连接当前数据库并清缓存，避免后台修改与游戏掉落/脚本查询始终使用旧JSON快照。以上新增回归继续执行，不能沿用旧验证结果。

### 额外物品仓库迁移

`ExtraStorage.java`按accountid+characterid加载extrastorage，getItemQuantity/addItem/removeItem还按itemid+type分组；`ExtraStorageUtil.storeItem/takeOutItem`扣背包并存库、或取库后加背包，空间不足恢复仓库。原DDL为V1.8.7__create_extra_storage.sql（inventoryequipmentid自增主键，characterid/accountid/itemid/quantity/type）。已追加同字段到Schema.sql及Unity资源镜像；新增NativeExtraStorage，真实InventoryChange运输层承载读/存/取，使用事务及DeferSaves保持磁盘/内存一致。NPC桥接getExtraStorage().getItemQuantity与storeExtraItem/takeOutExtraItem，仍由原物品仓库系统.js决定种类、30000限制及批量流程。宿主拒绝非正数量/超过short上限或装备存取，避免原short转换溢出和装备元数据丢失。定向Unity回归已通过矿石存取、分类隔离、数量不足、满背包不扣仓库以及四个现金戒指同时穿戴后的非零样式覆盖顺序；89路径0错误。新增仓库后server-check仍726通过/0失败，compile-check0警告0错误。全菜单回归继续执行。

最近一次新增宿主验证（额外仓库改动前）：RunFocused 89路径0错误，独立Item/copy、指定lobby与difficulty、exitPlayer回调、实时DB掉落字段查询均通过；server-check通过726/失败0；compile-check0警告0错误。全菜单648路径47错误，仍有独立副本和其他功能缺口，未完成用户十二项验收。

### 魔方洗练实际宿主（2026-11-01续）

源端`EquipmentCubeManager.java`完整算法迁移到`NativeEquipmentCube`：8种4007000..7原概率/最高阶、四阶1/2/3/3词条和power1/2/4/8；武器池/普通防具池/传说上限防具池；每词条不重复抽属性；HP/MP、双防、双攻、其余属性分别按原范围抽取。版本1按原cubeID推断1/2/3阶，版本2读grade；错误版本/词条数/属性/数值拒绝。apply先扣旧词条再加新词条，所有属性0..32767预检后才整体写入，保留卷轴/星级/owner。黑色魔方只生成预览，脚本选择保留时不改属性。`expandAttribute4`新增到两个Schema镜像，EquipPatch/FlushEquip往返持久化；原脚本没有改动。getCashId采用稳定inventoryitemid标识本地物品身份，保证消耗魔方引起库存快照刷新后仍能执行原sameTarget校验，不冒称与Java自增cashId数值相同。

`MenuFeedbackChecks.CheckCubes`已在隔离Unity通过：确定随机样本升阶、重复apply不叠加、黑色预览不修改旧词条、坏数据拒绝、属性溢出不部分写入；实际原`魔方洗练.js`完成普通魔方消耗/落库和黑色选择保留/消耗，重新CharService.Load读回一致。RunFocused89路径0错误。完整菜单更新为700路径/36错误、pendingRoutes0、deferredCatalogues16（尚未加Boss成长/Stat时的程序集）；不能把16个延后目录算完整购买验收。

### Boss成长脚本与战斗进度统一

发现现有`NativeBossService`只写本地boss_growth，而原`Boss成长系统.js`/`BossDamageGrowth.java`读取永久角色ExtendType21的`boss_growth_kill_<key>`，导致战斗加成与图鉴不一致。现RecordKill和百分比读取统一使用NativeScriptValues的真实extend_value；没有源端键的旧本地boss_growth行在首次读时迁入，保留旧存档。已存在但格式非法的源端值按Java规则视为0，不用旧行覆盖。BossBonus仍缓存5分钟，实际新击杀失效；Quest/Card数量每次读真实表。BossResponse增加三个分项及数量，NpcApi Java类方法读取真实运输响应；常量/纯计算方法按源端公式，invalidate明确调用服务。原战斗NativeMobController的倍率和击杀钩子复用同一服务。定向Unity断言已运行：Papulatus一次=0.1%/1.001、重复通知不叠加、原菜单+0.1%和图鉴(1次)一致；最终完整回归待后续输出更新。

### FieldLimit / Stat枚举

`FieldLimit.java`12个mask及check(bitmask)已逐一迁移；BuildMap/BuildWarpMap.getFieldLimit读原FieldCatalog数据。定向bitmask组合/缺位断言通过。`Stat.java`20个原mask（PET=0x180008）与getByValue/getByString/getBy5ByteEncoding、Java枚举name/ordinal/values/valueOf已显式注册，未知字段仍严格报错。Stat迁移本身不等于`时装属性转移.js`所需ModifyInventory、锁库存、原更新包等能力已经接通。

### 狩猎源端待决差异（尚未迁移）

继续检查`HuntTaskAutoPublisher.java`与原`BeiDouSpecial/任务/狩猎.js`发现：Java `giveBossRewards`实际发放金币/经验/点券/概率物品；但收集任务JS的`submitTask`只gainItem负数扣材料、调用`markTaskCompleted`并展示奖励。Java `markTaskCompleted`明确注释“不执行扣除和奖励逻辑”，方法体仅标记和广播；该JS后续没有gainMeso/gainExp/gainCash，因此原收集任务本身并不发显示的奖励。未擅自增加奖励或改源JS；迁移时需明确区分原端实际逻辑与可能需用户决定的修正规则，不能用虚假成功补齐。

### JDBC查询宿主与已穿戴库存身份（继续回归中）

新增`NativeScriptDatabase`显式连接/预编译语句/ResultSet宿主，参数绑定、列名/1起始列序、游标、关闭资源均有真实状态；未知SQL严格报错。仅支持本次原JS中的六类原SELECT：战力单人装备三表JOIN、按SQL给出的权重SUM/GROUP BY排行、掉落物品多BETWEEN分组200条、按物品倒序概率30条、项链材料DISTINCT/排序、炒股收益extend/characters JOIN排行。权重从原SQL表达式读，不在宿主另定战力权重；JOIN使用当次索引避免角色×全物品×全装备的重复扫描。DropStore.EnsureDatabase让这类直接SQL查询也使用同一首启原掉落种子；不以JSON快照覆盖已有DB。查询宿主不是通用SQL引擎，其他语句仍报错。原脚本未改。

定向原`战力系统.js`回归发现真实问题：本地InventoryDao.Equipped=1、负position；而Java `InventoryType.EQUIPPED=-1`。旧SlotEntry直接比较type，导致战力和所有通过getInventory(-1)读已穿戴装备的脚本恒为空（测试内存装备str12，但原菜单显示战力0）。现SlotEntry按Java库存面映射：-1→本地type1负位置，普通栏只取正位置；iterator遍历真实格位并返回可读写实例，countById/listById按同一栏过滤，替代Profile聚合数量。Job.getById按源Job.java:85未知ID回BEGINNER，复用原枚举显示名；装备getLevel读inventoryequipment.level。后续测试继续，不把未完数据库宿主等同于全部菜单功能完成。

JDBC/已穿戴库存后续定向结果：`/tmp/menu-feedback-jdbc-focused3.log` RunFocused89路径0错误；其中实际原战力菜单已从0恢复到测试装备的1120，进入997排行及角色装备详情均用原SQL得到1120；实时DB掉落绑定参数、按概率排序、列名/列序及已关闭连接/语句拒绝检查通过。该程序集尚未包含其后countById/listById按栏位修正、整型截断和provider标志查询，正在执行新的完整回归。新增provider.isCashItem读取info/cash优先、根cash后备（ItemInformationProvider.java:1732）；isQuestItem/isPartyQuestItem读info/quest/pquest；isBoss经原Mob数据读取，原MonsterInformationProvider.java:269明确对不存在/坏怪数据返回false。Boss成长的实际击杀/额外伤害消息已按源端文本接原聊天系统，保留项目既有long伤害边界，没有将其改回原Java的int伤害上限。


### 菜单审计覆盖纠正与时装转移宿主（继续验证中）

最近完整审计为700条路径/34条错误，队列尚余3条，并非已遍历完；上限提高至3000。发现空的sendSimple排行页被测试错误地回复-1，原脚本按ranking[-1]取值造成伪报错；测试不再给没有链接的Simple/Style页自动发送继续。没有改原JS或掩盖真实宿主错误。

原`时装属性转移.js`依赖`ModifyInventory`的独立copy、`Collections.unmodifiableList`、`Object.freeze`、库存锁和`PacketCreator.modifyInventory`。现按`ModifyInventory.java`保存装备Copy及旧位置，源条目后续变更不能影响已建更新记录；只读Java列表拒绝写方法。实际装备视图支持独立copy，离线copy不登记库存脏行。普通JS对象新增浅冻结，冻结后禁止新增/覆盖/删除自身属性，嵌套对象仍可写；其他freeze对象类型明确拒绝，不用空实现冒充全ECMAScript冻结。库存锁使用实际数据库的可重入Monitor（源Inventory.java:671/675使用可重入锁）；更新包验证记录并刷新真实已持久化装备镜像。`isSamePart`严格照`ItemInformationProvider.java:1669..1727`的原路径分类，不能换成装备槽相等：例如原Accessory类故意共用分类。新增实际原时装转移脚本消耗/两件属性/重新加载回归，结果待本轮输出确认。

### 待核实：现金戒指双人特效入口

归档`ring-pair-first.txt`/`ring-pair-second.txt`/`item-effect-refresh.txt`分别是原EXE递归下降函数`93E8F2..93F094`、`93F097..93F839`、`9433FB..9435A3`。前两者分别调用940CB1传7与12，早期检查this+568和参数byte bit2，并引用ItemEff路径/0及/1；尚未证明其分别属于伴侣/好友戒指，不得用候选文件名当功能结论。9433FB格式化字符串2435=`Effect/ItemEff.img/%d`并操作this+1138/1150向量。字符串2433/2434是同路径的/0和/1；93A057虽也引用它们但传固定5110000，因此不能误标为通用戒指。二进制身份仍为本记录开头SHA256。后续需追940CB1的类型枚举及调用者状态。


时装转移定向结果：Unity `/tmp/menu-feedback-fashion-focused2.log`通过74路径/0错误（空Simple不再误发-1使路径数从89减少）；原脚本真实执行源37后测试改为38力量/123HP/5攻击→目标，源清零、目标覆盖、扣4000313十个，CharService.Load回读一致。ModifyInventory独立copy和旧位置/类型、只读集合拒绝clear、浅freeze保持原值并允许nested写断言通过。第一次运行暴露Editor程序集无法直接访问internal MapContext（compile-check合并程序集未发现），测试改用已有反射Flags调用，第二次Unity编译及执行通过。没有更改运行时访问级别或原JS。

### 商城仓库扩容按钮接线（继续验证中）

源`CashOperationHandler.java:229..254`action0x07 mode0每次2000现金/4格；`Storage.java:73/111`初始4格，按accountid+world共享，最高48。新增原`V1.0.45__create_storages.sql`同字段到两个Schema镜像；NativeCashService ExpandStorage使用现有事务/余额币种，预检容量和余额后一起扣款加格，响应读回真实storageSlots。NativeCashWindow原BtExTrunk复用已有位置/素材，只接请求和成功反馈，没有自行新增界面。新增真实运输层成功、上限/余额不足不扣款不扩容断言，尚待新Unity回归。容量接通不等于普通仓库存取界面迁移完成。


仓库扩容后续验证：Unity `/tmp/menu-feedback-storage-focused.log`74路径/0错误，实际+4/扣2000、account+world存储行、48格上限和余额不足均通过；compile-check0警告0错误，server-check726通过/0失败。原NPC harness仍889/790成功/27runFail/72javaInterop，脚本包2779/parseFail0/mangled0。`git diff --check`通过。普通仓库存取窗口仍未接入。

时装宿主后的完整审计：`/tmp/menu-feedback-fashion-audit.log`688路径/32错误，pendingRoutes0、deferredCatalogues16。错误为29公园独立地图路线及HuntTaskAutoPublisher/EnterMTSHandler/SeedTowerCompat三个Java类；空排行伪回复与时装入口缺类错误已消除。该程序集早于仓库扩容及后续狩猎代码，不沿用为后续PASS。

### 狩猎发布器迁移（继续验证中）

按同名Java实现NativeHuntTasks服务器会话级任务状态，配置从原`狩猎任务配置.json`逐字节打包至Resources/Scripts/hunt_tasks.json，同步工具新增原配置与Data镜像，未改JS。保留启动立即随机发布/3600秒周期、两种任务、过期重置、最先完成标志和跨NPC共享、奖励配置及源端文本。getCurrentTask/getRewardItems等显式宿主供原`任务/狩猎.js`执行；markTaskCompleted只标记/广播，不私自发原JS漏掉的收集奖励。真实怪物死亡钩子接NativeHuntRuntime，只有目标BOSS第一次击杀扣门、依次调用现有金币/经验/点券/物品权威入口；概率物品仍先查USE空格（即使原配置物品4032133为ETC，保留源端行为）。任务状态只在服务器生命周期存内存，未擅自增加永久每日限制。原showEffect(quest/carnival/win)的通用FIELD_EFFECT图片渲染尚未迁移，当前仅原胜利音效/消息/实际奖励；不能宣称该动画或狩猎全部体验已验收。新的实际原JS提交、目标/非目标击杀、重复不领奖/周期刷新回归待输出。


### 停止开发时的最终结论：独立事件地图基础宿主

本节更正此前“独立地图宿主未绑定”的历史描述。二进制仍为本记录开头 SHA256；这部分业务语义直接核对原 Java，不虚构 EXE 函数地址。

- 原 `MapleMap.java:4756/4774/4778/4193`：resetPQ → resetMapObjects → 恢复刷怪点 → instanceMapFirstSpawn，仅首次 mobTime==-1；显式 instanceMapRespawn 才补整个池，私有地图不接普通全局自动刷新。`1750` 清怪关闭刷怪点；`1786/1805` 重置反应堆保持对象身份并恢复 alive/state0，特殊 forceDelayedRespawn 分支仍待完整迁移。
- 原 `EventInstanceManager.java:475/513/1130/1185`：登记/击杀剔除/最后怪回调，setEventCleared 发积分并释放大厅资格，不等于取消实例定时回调；专属物品退出清理。原 `Character.java:9558/3335` 的 fquest 积分商余数换声望，声望夹在±30000；事件等级配置默认8、大厅延迟默认10秒。
- 当前 `NativeEventMap` 按 EIM/原地图ID管理独立 Scope，Scope 从负数递减；脚本看到原ID，掉落/反应堆/几何/假人运行状态使用 StateId。实例重新读地图、复制可变传送门/NPC，保存怪物HP/位置；切换角色和销毁时释放。玩家进入、动态怪生成、死亡、掉落桥已接；没有迁移完整副本计时原图UI、队伍/掉线/死亡全链路及Seed专用玩法。
- 最终 `resetPQ` 使用原反应堆对象复位，保留动态反应堆和OID；定向断言 ForceHit→reset 后同引用同OID、alive/state0。两个953030000实例的怪物/传送门/反应堆/掉落隔离和销毁几何释放通过；实际事件奖励入库、专属物品清理、fquest余数/声望持久化通过。
- Hunt 定向74/0；独立地图接入前完整688/31，接入后完整688/2（pending0、deferred16）。最终反应堆修正后再跑 Unity 定向74/0。完整审计剩 MTS EnterMTSHandler 与 SeedTowerCompat，未掩盖。server726/0；Mock889/790成功/27runFail/72javaInterop；pack2779、parseFail0/mangled0。日志归档 `docs/evidence/menu-feedback-2026-11-01/stop-*.log`。
- FIELD_EFFECT 新证据归档同目录 field-effect-layer/dispatch/origin.txt：EXE 534F92..535176 使用字符串2500 Map/Effect.img/%s、坐标400/260、层C00614A4、alpha255→43E86F；538CAA→全局BEC20C→48BBA5取管理器+DC。origin坐标基准与case3分发表仍未完全证明，不可据此自行补新UI位置。保留原帧origin/delay，狩猎胜利动画仍待接。

按用户最新要求完成当前项后停止，不继续开发下一项。详细问题、后续修改、逐项验收和口径见 [十二项交接文档](../../../../docs/feedback-12-items-handoff-2026-11-01.md)：8项继续处理（1/3/6/7/9/10/11/12），4项代码修正待最终验收（2/4/5/8）；没有Android最终闭环记录。不得把脚本路径不报错当完整玩法通过。
