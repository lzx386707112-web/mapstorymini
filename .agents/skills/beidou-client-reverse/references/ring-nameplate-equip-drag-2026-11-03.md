# 现金戒指 / 装备窗拖放 / 聊天气泡 / 名牌称号战力（2026-11-03）

用户反馈四条：① 聊天气泡戒指戴了还是默认气泡；② 名牌戒指在装备面板看不到；
③ 名牌戒指戴上后角色脚下应有的「称号 + 战力」没迁移；④ 背包拖装备到装备面板不生效、
双击装备面板不卸下。

客户端哈希 `BeiDou.exe 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，
自定义 DLL `BeiDouSetItemCompat.dll`。

## 一、现金戒指在装备窗不显示（② 的根因）

- 源端现金装备一律落在 **普通槽 -100**：`-1→-101 … -12→-112 … -16→-116 … -49→-149`。
  `NativeInventoryService.EquipmentSlotOf` 的 `slot != 0 && info/cash != 0 ? slot - 100 : slot`
  就是这个规则，`EquipSlot.Worn` 里也列着 -101..-120、-149..-156。
- `EquipWindow` 的格子表 `SmallCells/FullCells` 只写了普通槽号（-12/-13/-15/-16），
  取值却只读 `Profile.EquipOfSlot(普通槽)` ⇒ 现金戒指穿在 -112 而 -12 是空的，
  于是 `id <= 0` 直接跳过绘制，面板上永远是空格子。
- 源端 `0x7FEE65..0x7FEEEC` 是**普通/现金两组数组并行读**（基址间隔 `0x1A0`），现金非空则覆盖普通。
  旧代码只在骑宠 -18/-19 上做了这件事（`NativeMount.VisibleSlot`）。
- 修复：新增 `EquipSlot.CashOf(slot) = slot-100` 与 `EquipSlot.WornSlotOf(cellSlot)`（现金优先、回退普通），
  `EquipWindow.Draw` / `EquipmentAt` 一律走它。`NativeMount.VisibleSlot` 不用动（它的 -18/-19 结果与通用规则一致）。

## 二、装备窗拖放与双击（④）

- 源端装备窗 vtable（`0xB389E8`）：
  - 索引 **21 = `0x7FE4C4` `OnMouseButton`**：只判 `0x201`（左键按下），命中格后按
    `0x1d05d8/0x1d05d9/0x1d05da` 三档分支造 DragStart 载荷并 `0x989588` 派发字符串 `0x737="DragStart"`。
  - 索引 **22 = `0x7FE9BB`**：此前误标双击卸下。2026-11-03重读发现它读取普通/现金物品后调用 `0x8E8252/0x8EBA5B` 并在空格走 `0x8E6E23`，不能据此认定为卸下发包链；具体vtable语义待进一步核实。
  - 背包窗 `0x81D42E` 是「按下即 DragStart」（字符串 1847）。
- 服务端语义仍是一张 `ITEM_MOVE(0x47)`：`action==0` 丢地面、`action>0` 落格、`action<0` 穿戴；
  客户端打包入口 `0xA0900A`。
- 修复：
  - `EquipWindow.SlotAt(pointer, out cellSlot)` 回传底图普通槽号（供落点判定）。
  - `NativeInventoryActions` 新增 `FitsEquipCell`：槽位归一化（现金 +100）后相等，
    四个戒指格（-12/-13/-15/-16）互相收。拖拽口径（`DragEnded && carriedType==1`）与
    点击口径（拾起后点格子）都走 `EquipLoadout.Equip`，位置排在背包格落点**之前**。
  - 历史实现把武器保护判据从 `slot != -11` 改为 `cellSlot != -11`，错误地连现金武器 -111 也拦住。2026-11-03真机反馈后已恢复按实际slot保护 -11，现金覆盖可卸下；此前将现金武器禁止卸下归因原端的结论撤回。

## 三、聊天气泡戒指（①）

- `CUser::UpdateRing (92E964)` 读 `info/chatBalloon`，客户端用字符串 `UI/ChatBalloon.img/`
  + 值拼路径（EXE 字符串池 1388）。`RingStyle` 取四枚戒指**最后一个非 0** 的值，
  现金槽优先（`-12-100` 先读），与名牌 `nameTag` 共用同一套。
- 真问题在合成侧 `NativeChatBalloon.Prepare`：
  - **样式 21 是动画气泡**：根下直接是 `0/1/2` 三个帧子节点（每帧一套 `nw/ne/…`，
    帧内 `delay=150`），九宫格不挂在根上。旧代码只按「根上有没有 `nw`」判定，
    样式 21 一律判成「缺节点」→ 永久 `cache=null` → 回退默认气泡。
  - 旧代码缺节点时是**完全无声**的永久 null 缓存，真机上根本查不出原因。
- 修复：`FrameRoots()` 下钻帧子节点（最多 64 帧）；`Layout` 带 `frames[]`/`delay`，
  `Frame(now)` 按时间取帧，`BotField` 每帧取当前帧绘制；几何一律按第 0 帧算，
  避免动画帧尺寸抖动把气泡抽动。`TextWidth/FontSize` 也走同一套下钻。
  缺素材时 `Debug.LogWarning` 一次（`diagnosed` 集合去重）。
- 素材覆盖现状（`Character/Ring/*.img` × `UI/ChatBalloon.img`）：
  **183 枚**气泡戒指里只有 **92 枚**的值在 IMG 里存在（88 个样式：0..39、47、48、50、58、59、
  116..119、148..156、166..179、181..188、190..204、208..210、221/223/225/227/229/230）。
  缺的 91 枚用的是 41..46/49、113..134、138..140、146/147/149/154/157/165..173、180..258 这些值，
  **源端同一份 IMG 也缺**，所以它们本来就只能是默认气泡——不是本次改动引入的。
  名牌戒指同理：167 枚里 93 枚的 `nameTag` 值在 `UI/NameTag.img` 里存在。
- 验收口径：`debug_balloon=1` 时 `[Bot/Balloon]` 会额外打印 `ring=<样式> 有素材/素材缺失→回退默认`。
- **未解（本轮没改）**：气泡字色 `clr` 的字节序。`NativeChatBalloon` 与 `NativeNameTag` 按 `0xAARRGGBB`
  解释，`NativePetField`（`pet/0` = `0xFF800000`）按 `0xAABBGGRR` 解释并交换 r/b 得到「深蓝字」。
  88 个样式里 30 个是白字（`0xFFFFFFFF`）、25 个是黑字（`0xFF000000`），这两种两种解释等价；
  其余 ~30 个是彩色（如 `174=0xFFD5D4D4`、`33/230=0xFFFF0000`、`21=0xFFFFFC00`），
  两种解释差一次红蓝互换。要改成 BGR 必须先拿到原端截图确认，别凭猜改。

## 四、名牌附加行：称号 + 战力（③）

源端**不是 EXE 本地计算**，分两段：

1. **服务端**（`gms-server`）：`PacketCreator.nameplatePowerUpdate` →
   `SendOpcode.NAMEPLATE_POWER_UPDATE(0x17C)`，载荷 `int characterId; byte titleState; long power`。
   - `titleState = 穿着 nameTag 戒指 ? 系列+1 : 0`；`titleState == 0` 时 power 恒发 0。
   - `isNameTagRing(id)` = `1112000 <= id < 1120000` 且 `info/nameTag` 节点存在。
   - 广播点：登录 `PlayerLoggedinHandler:274`、进图 `MapleMap` 3440/3453、加 Buff 与换装
     `Character` 2965/5282/5291/9780、以及 `setNameplateTitleSeries` 自身。
   - 战力：`(str+dex+int+luk) * max(1, max(watk, magic)) + maxHp/10 + maxMp/20`。
   - 持久化：`ExtendType.CHARACTER_EXTEND` 的 `nameplate_title_series`，
     `needsSaving` 上限 `NAMEPLATE_TITLE_SERIES_COUNT = 9`。
2. **客户端**（`BeiDouSetItemCompat.dll`，导出的只有 `BDS_RegisterPacketHandler`，
   只 import KERNEL32/OLEAUT32/USER32(`wsprintfA/W`)，自己不画，是把格式化结果塞回客户端的名牌文本层）：
   - `.titles` 节（RVA `0x312000`）里既有注入代码也有数据：
     - `0x3120B9` 起 **8 个 dword 阈值**：`10000 / 100000 / 1000000 / 10000000 / 50000000 / 200000000 / 500000000 / 2000000000`；
       循环 `0x312035`：`power >= 阈值` 就 `ecx++`，最多到 8 档。
     - `0x312046`：高位非 0 时 `ecx=8`；高位 `>2` 或 `==2 && low >= 0x540BE400` ⇒ `ecx=9`
       （即第 10 档门槛 **10,000,000,000**）。
     - `0x31206A`：`movzx edx, byte[edi+8]; dec edx; cmp edx,8; jbe` ⇒ **系列 = clamp(titleState-1, 0, 8)**。
     - `0x3120D9` 字符串池 + `0x3123AF` 的 word 偏移表，下标 `系列*10 + 档位`，共 90 条。
     - `0x59BF` 格式串 = `%s || 战斗力：%s`（另有 `0x5047` 的 `战斗力: %s`）。
   - 9 系列 × 10 档（逐字抄进 `NativeNameplate.Titles`）：
     玄幻 / 斗罗 / 龙珠 / 火影 / 海贼 / 死神 / 鬼灭 / 咒术 / 英雄学院。
     注意每系列第 9、10 档文案相同（如斗罗「神帝/神帝」、龙珠「自在极意/自在极意」）。
   - 系列选择走 NPC 9900001 的 `BeiDouSpecial/称号系列选择.js`：
     `cm.getNameplateSeries()` / `cm.setNameplateSeries(series)`（项目里两个都已补）。
- 实现：
  - 新增 `Assets/Scripts/NativeNameplate.cs`（阈值表 / 9×10 表 / 战力公式 / `LineFor` 0.5s 缓存）。
  - `PlayerProfileData.nameplateTitleSeries` 作服务端值的本地镜像；
    `GameSession.SelectChar` 末尾 `NativeNameplate.RefreshFromServer()` 拉一次。
  - `NpcDialogUi.DrawCharacterName` 的 `local` 分支：名牌下面先画称号行，再画勋章行（勋章跟着下移一行），
    几何沿用 `NamePlateRects`（`0x5F12E5`/`0x5F15EC`），底色/字色与角色名牌一致（A0000000 + 白字）。
  - 未迁移：源端把 0x17C 广播给**全图**，所以别人的称号也看得到；本项目只有本地角色有名牌戒指，远端/假人一律不画。

## 五、回归

- `tools/unity-compile-check` 0 error / 0 warning。
- `tools/server-check` 1412 / 0（未改服务端逻辑）。
- `Assets/Editor/MenuFeedbackChecks.cs` 新增断言：现金槽取值（`EquipOfSlot(-12)==0 && EquipOfSlot(-112)==1112100`、
  `WornSlotOf(-12)==-112`、`WornSlotOf(-18)==-18`）、`WearsField`、战力档位 8 个边界 + 第 10 档、
  9×10 称号表首尾、动画气泡样式 21 的 3 帧 / `delay=150`。
- 离线核对（未跑 Unity）：用同一套算法在 python 里复算 `Prepare(174,31,1)` = 97×81 / anchorX=50，
  与既有断言一致；样式 0/1/2/3/21 合成出的气泡形状完整（0 白、1 粉、2 蓝、3 带彩带、21 星形动画帧）。
- 边界：未做 Android 实机验收；装备窗拖放的 z 序仍按「装备窗优先」（与既有卷轴落点判定同口径）。

## 六、其余功能性戒指（同日晚补）

`Character/Ring` 1116 枚里，除 nameTag / chatBalloon 之外还有四类真正影响玩法或表现的字段。
完整盘点表见 `docs/ring-features-2026-11-03.md`，这里只记「数据长什么样 + 判据怎么读」。

### 6.1 `info/addition`（37 枚）

四段并列，可同时出现；每段都可带 `con`：

```
addition
  statinc  { incSTR/incDEX/incINT/incLUK/incMHP/incMMP/incPDD/incMDD/incACC/incEVA/incPAD/incMAD/incSpeed/incJump, con? }
  critical { prob, damage?, con? }      prob = 暴击率 +%，damage = 暴击伤害 +%
  boss     { prob, damage, con? }       prob = 100 才是常驻
  skill    { id, level, con? }          数据缺，见下
```

`con` 三种键，可叠加：

- `lv` / `level`：角色等级门槛。两种写法在数据里都出现，读法是 `con.Int("lv", con.Int("level"))`。
- `job`：**具体 jobId 列表**（`0..n` 各一个 int），不是 reqJob 那种位掩码。
  例：米哈尔 5111/5112；英雄 2111/2112/2215-2218；坚韧战士 111/112/121/122/131/132/1111/1112。
  精确相等即可，别写成 `id/10`（111 与 1112 会串）。
- `weekDay`：`0..n` 各一个字符串，`saturday` / `sunday`。
  周末特殊戒指 1113022-1113026 的 **statinc / critical / boss 三段都带这个条件**。

`addition.skill` 引用 90000000 / 90001001，`Skill/900.img/skill` 只有 9001000/9001001/9001002/9001006
（`Skill/` 目录也没有 60000.img），所以 4 枚带 `skill` 的和 36 枚 `ringOptionSkill`（60013-60018）
一律无数据可播，**不要**把它们当可学技能塞进 `SkillProgress`。

落地：`EquipCatalog.ApplyAddition(itemId, result, ref bossDamage)` 在 `Apply` 末尾调用；
`boss` 百分比复制一份 `setItemBonus` 再并进 `"BossDamage"`（`ComputeBonus` 的返回值可能是缓存实例，
直接改会污染下一次结算）。

### 6.2 `info/effect`（113 枚，94 枚有素材）

同一个字段两种形态：

- `fixed` + `animate` + `pos`（82 枚）：角色身上的循环帧动画，`pos=1` 抬到头顶。
- `follow` + `left/top/right/bottom` + `dx/dy` + `interval` + `delay` + `emission`（31 枚）：
  粒子发射器。区域是**屏幕坐标**（y 向下为正），世界 y 取反；`delay` 是单颗粒子寿命，不是总时长。
  `genOnMove`（10 枚，极光圈）只在角色移动时发射；`theta`（恒 21）是横向摆幅。
- `z` 恒 -1 → 画在角色身后（`SortingOrder-1`）。
- 素材路径是 `info/effect/path` 的字符串本身（`Effect/CharacterEff.img/<id>`），直接喂
  `WzAssets.Node`。94 枚能解析；19 枚（1112500/1112907/1112915/1112924-1112928…）指向的节点源端也缺。
- `Effect/CharacterEff.img` **已在** `NativeAssetIndex.json` 里，不需要再登记。

落地：`Assets/Scripts/NativeRingEffect.cs`（挂在 PlayerCtrl 上，每帧比对四格戒指，换戒指重建）
+ `NativeRingParticle`（跟角色位移同步 + 自身速度）。播放复用 `NativeImgAnimation`
（`repeat=true` 循环，粒子用 `lifetime=delay` 自动回收）。

排查开关：`debug_ringfx=1`（一次有效）→ `[Ring/Fx] ring=<id> path=<...> node=<bool> groups=<n> mode=<fixed|emitter|none>`。

### 6.3 `info/bonusExp`（6 枚）

```
bonusExp
  0 { incExpR: 2 }                      十字旅团降魔戒指
  0 { incExpR: 10, termStart: 0 }       我是新人 / 老手回归（三段 10/20/30）
  1 { incExpR: 20, termStart: 1 }
  2 { incExpR: 30, termStart: 2 }
```

`incExpR` 是百分比。多段是礼包按周递增，本项目没有账号龄基准 → 取第 0 段；多枚同戴取最大值。
接在 `Profile.GrantExperience` 的 `PendantBonus` 之后，先除后乘避免 long 溢出。

### 6.4 做不了的四类

- `option` + `fixedPotential`（40 枚，呱呱/龟龟/喵喵/砰砰）：只有 code+level，属性在 `ItemOption.img`，
  而项目 `Data/Item/` 下没有这个文件（GMS 在 `Item.wz` 根）；`MapleStory/Data/Item/ItemOption.img`
  是 TMS 282 的，`wzpy` 解析出 0 个根节点，用不了。这 40 枚自带的基础 `inc*` 仍然生效，缺的只是潜能那层。
- `level`（150 枚）：`level/info/<n>/exp` 是戒指自身经验表（多数每级 10000），要服务端持久化。
- 倾向经验 / PVP / 人气 / `DAexceedLimit`：对应系统整体不存在（无倾向、无 PVP、无人气值、无 3100 职业）。
- `groupEffectID`（4 枚）：要两个人同时戴同系列戒指才出特效，本项目只有本地角色有戒指。

## 七、装备窗里 5 个「DLL 槽位」的格子样式

折叠态装备窗（`UI/UIWindow.img/Equip/backgrnd`，175×304）上，**副手 / 图腾 / 心脏 / 徽章 / 纹章**
这 5 格不是原版 GMS 就有的槽位 —— EXE 里没有对应的 IMG 槽位、也没有这些中文字符串（GBK/UTF-16
都搜过），当年是 DLL 往窗口上补画的，后来被烘进了位图。烘进去时留下两处**文字**缺陷：

| | 自带格（帽子/盾牌/鞋子/骑兽…） | 这 5 格（原图） |
|---|---|---|
| 字色 | 核心 **#7788AA (119,136,170)** + 一圈**干净的 #DDEEEE (221,238,238) 描边** | 核心 **#6688AA**，描边色散成 #88AABB 一带（发糊） |
| 字形 | 2 字墨迹 **23×11px**，贴格子顶部（y=3..13） | 笔画被描边吃得残缺（墨量只有自带格的 6 成），**无法从位图恢复** |

**几何绝对不能动！** 这 5 格本来就比自带格矮 1～3px（实测：副手/图腾内容 y=200..228、
心脏/徽章/纹章 y=231..261，而同排/同格位的自带格按 33px 步进对齐），格间/格底的白行
（y=229..230、y=262..264）是**源端烘死的分隔线**，不是「残缺」——把它们补成底纹会把
格子之间的缝填没（第三版真机翻车：用户截图里横向缝隙全部消失）。

**排版参数是全图统一的**（20 个自带标签逐一量过）：2 字标签墨迹恒为 x=3..25、y=3..13，即
23×11、左上角钉在格内 (3,3)；字是 **12px 宋体点阵**（1-bit 无抗锯齿），**带 1px 白描边**：
核心外扩 1px（4 邻域，含字内空腔）填 #DDEEEE——拿内置格「武器」做回归，该规则与实际描边
IoU=0.93、零多余像素。描边色不是纯白，(221,238,238) 众数占 84px，其余是 (221,221,238)、
(238,238,238) 等随底纹微变的变体。

底纹是「**逐行不同、横向 4px 严格周期**」的噪声：5 格的干净行 y=18..28 实测 1408/1408 像素满足
`p[x]==p[x+4]`（第一次量出 65% 不一致是因为把文字行也量进去了）。所以补底纹 = **用同一格的
干净行整行复制**（x 相位天然一致），而不是本行平铺——第一版用本行 x=27..30 做种子，那 4 列在
文字行里被字形污染，铺出来满屏竖条纹（真机翻车截图就是它）。

落地：`Assets/Scripts/NativeEquipBackdrop.cs`

- `Texture()` 读 `WzAssets.Node("UI/UIWindow.img/Equip/backgrnd")` 的像素（**必须 Clone，
  `WzAssets.Pixels` 返回的是按 canvas 缓存的那份数组**），逐格两步：① 文字带 y=2..17 整行换
  成本格干净行 `min(28, y+18)`；② 槽位名用**点阵字形表**盖回 (3,3)——先铺**描边层**
  （核心外扩 1px、4 邻域、#DDEEEE），再盖**核心层** #7788AA。**其余像素（白分隔行、
  x=0/31 格线、格底）一律不动。**
- 字形表：**文泉驿点阵宋体 9pt@12px**（Debian `xfonts-wqy` 的 PCF，FreeType 可直接渲染），与位图
  内置标签同一类点阵（「勋章/指环/骑兽」逐位 IoU 0.91~0.98、「帽子/鞍子」≈0.62——同字体不同
  排印的 1px 差）。本地没有 SimSun，macOS 宋体/Songti 在 12px 渲染出来是糊的（IoU≈0），不要试。
- 槽位名**烘进背景图**，运行时不再用 `GUI.Label` 重画——项目字体是 Arial Unicode MS，
  和点阵宋体完全不是一个东西，画上去就是双层字（第二版翻车原因）。
- `EquipWindow.Draw` 折叠态用这张图（展开态是原版 6 列 `FullBackgrnd`，没有这 5 格，不动）。
- 改 `EquipWindow.SmallCells` 的格子位置时，必须同步 `NativeEquipBackdrop.Gaps` 的 row/col。

证据图：`docs/evidence/equip-window-slots-2026-11-03/before-after.png`（上排修补后 5 格、
下排内置格参照）、`cells-6x.png`（5 格特写）、`gaps-check.png`（底部两行修补前后整幅对比，
可核分隔线）、`window-4x.png`（整窗 4×）。

## 八、背包双击的使用/穿戴通道（同日补核，修复「双击没反应」）

- **背包窗 vtable**（`0xB390C4` 起，与装备窗 0xB389E8 同构）：
  idx21 = `0x81D42E` `OnMouseButton`——**只判 0x201**，命中格后 DragStart（按下即拾起）；
  idx22 = `0x81D8AD` **双击处理**——命中格 → 取 `[esi+0x5e0]` 页签 + 格 index 的道具
  （`0x4282f7`），道具为空调 `0x8e6e23`，否则走 vtable `+0x30/+0x2c` 与 `0x8f5b20`
  派发使用/穿戴（装备最终走 `ITEM_MOVE action<0`，dst 由客户端给，打包入口 `0xA0900A`，
  全部 5 个调用方在 `0x4F116A/0x4F193F/0x4F2B84/0x4F3131/0x4F3873`）。
- **关键机制：Windows 对双击的第二击发 `WM_LBUTTONDBLCLK(0x203)` 而不是 `0x201`**，
  所以源端「放回原格」（`0x4F30E6` src==dst 直接返回）只吃普通按下，双击的第二击
  永远到不了丢弃路径，而是整体走 idx22 的使用/穿戴派发。
- **槽位→装备位映射表**（`0xA68229` push 链）：-2/-4/-6/-8/-7/-5/-9/-11/-12/-13/-10/
  -15/-14/-16，默认 -17——戒指四格 -12/-13/-15/-16 都在表里，dst 按身体部位表给。
- 服务端对照（`InventoryManipulator.equip(c, src, dst)`，gms-server）：**dst 由客户端决定**，
  服务端不重推槽位；只做 canWear、overall/裤子与双手武器/盾冲突挪位（case -6/-5/-10/-11）、
  以及同款 GP 戒指 / GW 戒指 / 三种 unique 情侣戒指的互斥拒绝（dropMessage message3）。
  本项目 `NativeInventoryService` 的 line 110-11 戒指组特判与 -6/-5/-10/-11 冲突分支同源。
- **本项目对应 bug**：移植把两击合并进一条 Pointer 流——第一击 PickFromInventory 拾起，
  第二击按下被 `CarryPointerStep` 的「点回原格 = 放回原处」吞掉（swallowDrop），抬起时
  该分支无条件 `ConsumeClick()` 把 `DoubleClicked` 一起清掉；`CarryStep` 在
  `GameHUD.OnGUI:93` 先于 `DrawAll` 执行，物品栏双击处理永远看不到 DoubleClicked
  ⇒ 背包双击穿戴/使用全部无反应（此前穿戴都走拖拽/点击装备格口径，所以没暴露）。
- **修复**：swallowDrop 抬起分支在 `Pointer.DoubleClicked` 时不再 ConsumeClick，
  把双击留给 `InvenWindow.Draw` 的双击处理（`NativeInventoryActions.DoubleClick → TryUse`）。
  源端语义对应：0x203 绕过丢弃路径。
- 回归：unity-compile-check 0 error；server-check 1412/0（服务端未改）。
  实机验收：戴一枚戒指后双击背包中另一枚戒指 → 应落到下一个空戒指格（-13）；
  双击消耗品 → 正常使用。


## 九、角色装备窗时装武器卸下/拾取修正（同日真机反馈）

- 用户确认1662000/1672000现可正常装备；本次新问题明确为**角色装备窗的时装武器**，不是机器人AP格。
- 根因：GameWindows.EquipWindow.Draw以cellSlot=-11拦截双击，连实际显示的现金slot=-111也拦截。EquipLoadout.Unequip原本只保护实际-11；本次恢复UI与该规则一致，双击只卸现金覆盖。
- 单击没有入口是独立迁移缺口。重读同SHA256 EXE `0x7FE4C4`：`0x7FE53C..0x7FE57F`先取-100-bodypart、为空才取-bodypart；`0x7FE909..0x7FE926`将实际负槽写到载荷+0x1C、type1写+0x18；`0x7FE953..0x7FE96B`派发DragStart。此函数未见按武器部位禁拿现金层的判断。
- 原服务端 `org/gms/client/inventory/manipulator/InventoryManipulator.java:776` unequip(src,dst)：dst非负、source存在；目标格已有物品且src<=0则getInventoryFull并返回。不会把目标背包物品换上角色，也不需把普通武器一起脱下。
- 当前实现：PickEquipped保留实际负槽，拿起时不改数据库；再点同装备格取消（第二次松手保留DoubleClicked），点/拖到装备背包空格才经UnequipToInventory发请求。moveTo=0保留旧双击选首个空格；moveTo>0在服务端校验1..equipslots且为空，再原子落该格。拒绝时不摘源物品，右键/Esc/阻塞NPC会话取消载荷。普通-11的既有保护不扩展。
- 复现：点角色时装武器→有原物品图标随指针；再次点击原格取消；双击→现金层回背包、普通武器仍装备；拿起后点装备背包空格→该格收到原实例；占用格/越界拒绝且原穿着不丢。机器人面板交互不属于本次扩展。
- 验证：server-check --equip-unequip **377通过/0失败**，包含现金覆盖卸下保留普通武器、指定空格、占用/越界拒绝及普通双击式卸下。unity-compile-check **0 warning/0 error**；尚未打新APK，不能称触屏拾取/双击已实机通过。


### 用户要求移除重复心脏入口（2026-11-03）

有专用机器人面板后，用户明确要求移除原装备窗心脏格。这是用户授权的布局调整，不是原端行为新结论。SmallCells[6,2]改为空号0，绘制、悬浮、拾取、双击和拖入命中共同使用该表；专用面板及实际-54槽、存档和属性统计保留。FullCells原本没有心脏格。

原`Equip/backgrnd`175×304中该格烘在x71..102、y231..262。NativeEquipBackdrop先修其余四个后补格，再复制同图帽子右侧空白区(71,35,32,32)的原像素覆盖旧心脏格；不修改源IMG，不移动相邻装备格。已经解码原图并检查覆盖预览，心脏图框/文字消失且相邻格保留；不是Android实机截图。背景原始5格的历史测量记录仍有效，但运行时只保留四格修字。
