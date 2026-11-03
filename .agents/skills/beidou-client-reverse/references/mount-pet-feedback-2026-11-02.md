# 潮流前线骑宠、商城药包与拖拽绑定（2026-11-02）

## 身份和原端依据

- EXE SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase400000，本次工具已核对。
- 原`npc/坐骑领取.js` SHA256 `99ad49d2fd3cf14acc8b11372fd3049165e645aab4e85e1973153746adb5dac2`：9900009潮流前线调用，38组190坐骑/191鞍具，教授1004/10001004/20001004，未修改脚本。
- `StatEffect.java` SHA256 `653e45de6da0c018fa1044261211ac186018c8bdeaf447006471e811adf8ac2d`：isMonsterRiding1725、applyBuffEffect1373..1455，普通骑乘取EQUIPPED -18，MONSTER_RIDING来源骑兽技能。特殊193技能坐骑/战舰另有分支，不扩大本次支持范围。
- `BodyPart.java` SHA256 `98491a35edce23b5377de1af31752e8d6b4683b3f54ee29d5dc92ee84db89c55`：18骑兽、19鞍具。EXE POINT表`BE2260+(bodypart-1)*8`，18=(5,233)、19=(38,233)，是装备窗客户区坐标，对应现有GridX5/GridY35/Step33的第6行前两格。
- `UI/UIWindow.img` SHA256 `b0a92de9487c221601c2b1e13cb0ab37a05915994231ce140cc5a171e5bfd423`：Equip/backgrnd175×304，FullBackgrnd208×291，origin(0,0)。补原槽位，未新增自绘素材。
- 骑乘合成复用407925..407992、455826..45583B（见[假人系统](solomapling.md)）：骑手sit，绳梯rope，以navel对齐，原tamingMobRear/Mid/Front深度。鞍具节点`Character/TamingMob/<191八位ID>.img/<190七位ID>/<action>/<frame>`，不能把191当单独坐骑。
- `01902033.img` SHA256 `b8ed60ace6a4f3bbb153fc8bbd0a9503ba827c2f987ccf212bacfa4f1349e2af`：rope/{0,1}/delay为UOL `../../ladder/{0,1}/delay`；直接转int会FormatException，应先Resolve再取数值。

## 商城和补药

Commodity.img SHA256 `ca59b19dd4e510e22fbd0015522a6f3c680fa1978166546a739ca7e319a36e0d`；Category.img SHA256 `378e2435e8de8c64290d1a478fe53addabd124ed08df372b4496c7bcd17e30a1`。

| 原商品 | 在售SN | 原价格/数量/期限 | 分类 |
| --- | --- | --- | --- |
| 1812002 自动服用HP药水包 | 60100036 | 1800/1/90天 | 6/1 宠物装备 |
| 1812003 自动服用MP药水包 | 60100038 | 1800/1/90天 | 6/1 宠物装备 |

同ID另有10001105/10001113等未上架记录，不能全部强制上架。真实NativeCashService.Read+原目录回归确认上述在售商品进入列表；本次只在原宠物装备分类排到首屏，保留原SN、售价、期限、购买/仓库和后台上下架规则。

药品格仍是宠物子窗(46,11)/(112,11)，绑定91/92/type7，能力装备-24/-25，原端依据见[宠物记录](pet-field-and-equipment.md)。CarryPointerStep原只在第二次Pressed时调用DropPetPotion，拖入后Released不绑定；现补松手路径，不自动选药或改警戒比例。绑定后比例0会提示在系统设置调整；无能力装备/药水类型错误也提示原因。

## 当前实现和验证

- NativeMount接普通1004系骑乘开关、坐骑/鞍具节点匹配；SkillWindow仅在已学习后显示对应新手页的骑兽技能，不白送技能。PlayerCtrl接真实骑乘配方和普通攻击门控；换/卸坐骑或鞍具取消骑乘。装备窗补原骑兽/鞍具格，WzCharacter合成鞍具并解delay UOL。
- 隔离Unity回归 **PASS322、退出0**，日志`docs/pet-review/mount-pet-2026-11-02.log`。覆盖原宠物/拾取/补药、松手绑定、DB重载、实际血蓝恢复/扣瓶、商城两包在售、已学骑兽可见、骑乘开关/卸鞍取消/无鞍拒绝，及脚本38组×6动作（stand/walk/jump/prone/ladder/rope）首帧合成。不是所有动画帧逐帧验收。
- 全脚本dotnet编译0警告0错误；不接正式角色DB，尚未打包安装APK或Android实机验收。最终提示文案变更仅作编译/diff检查。
- **迁移边界**：本次是普通骑乘入口和显示修复；骑乘独立物理参数、疲劳定时任务、特殊193技能坐骑/战舰及全部骑乘技能例外未完成，不能称完整坐骑系统1:1。

## 复现

`rtk proxy python3 tools/imgdump.py Character/TamingMob/01902033.img rope --depth 3`，鞍具同工具读取01912000.img；遍历Commodity的ItemId=1812002/1812003及Category=6。POINT表用pefile按VA减400000读取两项各8字节，以`struct.unpack('<ii',...)`复现。

Unity入口NativePetSceneChecks.Run，在独立项目运行并设MAPLEDASH_IMG_ROOT为本项目MapleDashResources/Data。

## 补充纠错边界

原String/Eqp.img/Eqp/PetEquip确认1822000是宠物名片戒指，1832000是宠物聊天戒指，不能当自动HP/MP能力。当前NativePetItems.EquipmentSlot仍误映射两者到-24/-25，专用槽及旧存档需独立修正；本次只验证1812002/1812003，不将戒指槽当已通过。

## 穿戴失败追查（同日用户复报）

已确定：`NativeInventoryService.EquipmentSlotOf`对所有cash装备统一减100，导致现金Tm/Sd进入-118/-119；`NativeMount.Toggle/Item`、EquipWindow骑宠格只读-18/-19，因而双击后既不显示又拒绝骑乘。Profile与EquipSlot.Worn实际保留两组位置，不是背包物品消失。旧PASS322仅对1902000/1912000实际穿戴，38组遍历仅验素材，未覆盖现金骑宠穿戴，不能作为此路径通过的证据。

原NPC38组中26组坐骑cash=1、其中鞍具既有cash=1也有cash=0（例如1902033/1912026为1/0）；不能只处理现金坐骑与现金鞍具成对的情况。用imgdump读取每组`info/cash`可复现。源`constants/inventory/EquipSlot.java:isAllowed`对cash先检查allow-100，末尾又放行任意负槽，故不能据此推断客户端必定使用-118/-119。源`InventoryManipulator.java:721`仅在dst=-18更新Mount.itemId；`StatEffect.java:1375`仅取-18。

该追查阶段的疑问已由下节原EXE汇编解决；用户选择继续核实原版规则，不授权改为现金骑宠独立骑乘。

## 原EXE现金骑宠规则定案（同日补核）

二进制身份同本文首节。持久化汇编、跳表及字符串证据：`docs/pet-review/cash-mount-original-rules-2026-11-02.txt`。复现使用`tools/inspect_beidou_client.py BeiDou.exe --fn <入口> --start <范围起点> --end <范围终点>`，均按控制流解码。

- 槽位分类函数`4606A0`，`460A38`跳表将190映射`4607F9`写bodypart18、191映射`460807`写19。穿戴入口`4F1C2D`，`4F230B..4F233A`调用`4F2CBB`判断现金装备，现金目标=-100-bodypart，非现金=-bodypart，再经`4F2B77..4F2B84`发到`A0900A`（ITEM_MOVE0x47）。故现金-118/-119是正确位置，不能强制改到普通-18/-19。
- `4F2CBB`先调用`5D4A2B`读info/cash（字符串2037=`cash`），或检查物品实例+0x18/+0x1C的64位现金身份是否非零；本项目当前槽位依据info/cash。
- **现金装备不能替代普通基础装备**：`4F292D..4F2965`现金骑兽要求CharacterData+0x17B的普通骑兽存在，否则串873=`You must have a tamed monster\r\nin order to equip this.`；`4F29EC..4F2A39`现金鞍具要求+0x183的普通鞍具存在，否则串874。并非cash标记坐骑统一穿普通槽的例外。
- 对应CharacterData普通装备指针为+0xEB+bodypart*8；现金为+0x28B+bodypart*8，因此普通18/19分别+17B/+183、现金18/19分别+31B/+323。
- **骑兽技能校验**入口`966F7A`，`968B9D..968BB4`先要求普通18/19均有装备；`968BBA..968C08`再校验现金18/19：有一件没另一件即拒绝，两者都存在时调用鞍具模板`4B1113(mountId)`校验匹配。`968DF7/968E26`使用串3818=`You can ride the tamed cash monster when it is equipped with a Cash Saddle.`。普通基础装备与现金外观是两层，并非有现金就跳过普通检查。服务端StatEffect仅用普通-18作为Mount状态ID，与客户端规则一致。
- **同一装备格现金优先显示**：装备窗Draw`7FEC81`，`7FEE65..7FEEEC`并行读取普通/现金数组（间隔0x1A0），现金非空则选现金，空才选普通；仍使用原POINT坐标，第18/19格位置不变。此次仅迁移骑宠格的现金覆盖、命中和卸装，不冒称其他现金格全部核对完成。
- `ijl15.dll`静态966F7A引用在数据10043AA0，代码10014B67是间接调用原施放函数（参数0x3EBA9E），不是以该指针安装骑乘条件补丁的证据；未发现上述骑乘分支地址的静态引用。此检查不等于穷尽所有动态补丁。

原NPC资源的边界：例如1902033(cash1)/1912026(cash0)是混合标记，NPC“配套发放”不证明可作为现金成套骑乘；不改IMG标记、不改原JS，不自动发普通基础装备，不凭空把普通鞍具放到现金槽。这类源数据与原校验的矛盾单独保留，不能用38组首帧合成PASS推断38组都可直接骑。

本次实现：NativeMount按普通基础装备+现金匹配层校验，骑乘外观选择现金18/19；EquipLoadout对新现金穿戴做基础/配套检查并在四槽换装时取消骑乘；EquipWindow原两个格的绘制、命中、卸装统一选实际现金负槽。旧存档已穿戴现金但无基础装备的状态不删物品，可显示、卸下，施放时准确提示缺少普通基础装备。

### 本次验证结果

隔离Unity第三轮`NativePetSceneChecks.Run` **PASS341、退出0**，日志`docs/pet-review/cash-mount-original-rule-2026-11-02.log`；较旧PASS322新增19条现金路径断言：双击入口缺基础拒绝且不扣物品、实际普通+现金四槽穿戴、仅现金骑兽拒骑、不匹配现金鞍具拒绝、匹配覆盖骑乘、DB重载保留四槽、两个格命中实际现金条目、卸现金鞍具取消/显露普通鞍具、卸普通骑兽取消/拒骑、旧无基础现金状态重载仍可见及可无损卸下。双击入口指NativeInventoryActions.TryUse实际分派，非Android触屏自动化。旧38组×6动作首帧合成仍执行；没有声明38组实际均可骑。

第一轮被既有截图代码（end-of-frame后又yield null才Capture）导致null纹理中断，已改为实际帧末截图；第二轮发现新双击测试取Profile.Attributes而旧fixture只把200级写入独立attrs，修正fixture的Profile级别/属性同步，并把缺基础拒绝断言收紧为准确错误文案，避免通用失败假阳性。第三轮通过。生产代码全脚本dotnet编译0警告0错误，最终测试fixture经Unity重编译通过；git diff --check通过。未构建APK，未做Android实机验证。

普通打底示例由原`String/Eqp.img/Eqp/Taming`核对：1902000=小浣猪、1912000=皮鞍子；仍须满足原穿戴等级等要求，不绕过门槛。
