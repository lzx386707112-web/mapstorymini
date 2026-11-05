---
name: tms282-reverse
description: TMS282 客户端/服务端逆向与逻辑分析结论沉淀。分析或迁移 282 内容前必读。
---

# tms282-reverse

TMS 282 端（客户端 `~/Documents/mxd/MapleStory`，服务端套件 `~/Documents/mxd/TMS282.5`）的已确定逻辑记录。
规则来源：mxd-android/AGENTS.md 规则 8-12。282 服务端是 swordie 架构 Java 源码（`source/src/main/java/net/swordie/ms`），
**本地套件不包含游戏脚本**（`套件驗證.txt:6` 明示；`data/scripts` 为空，源码及所附 JAR 的
`GAME_SCRIPTS_ENABLED=false`）。有部分 Java 玩法实现，但**不能说原脚本逻辑全部由 Java 替代**；
任务分发仍需外部 `.py/.js`。复现、边界及旧文档纠正见 `docs/tms282-skill/18-advancement-evidence-search.md`。

域内细节见 `references/`：

- **灵魂武器显示反馈（2026-11-05）**：第29篇“确认文字、图标及装备详情反馈”；Notice文字烘焙导致缩放难读，使用既有原版动态确认框；ActiveSkill光效中心(402,267)、32px图标左上(386,251)；新版账本绑定信息补入装备详情并刷新即时属性。Unity面板→传输→数据库回归通过，Android画面仍需用户复验。

- **战斗力（逆向中）**：工作区根`docs/tms282-skill/30-combat-power.md`，5F3C7E0两次完整快照相减→5E82B40→12F9A60，StatCalibration双校正及int64截断；原服务器0.7 magic number不可视为EXE公式，用户明确禁止以攻魔差额替代。

- **新版灵魂武器（进行中）**：`docs/tms282-skill/29-soul-weapon-wz2.md`（实际正文在工作区根docs），原状态布局/被动公式/150级启用与GradeInfo/BossInfo已核，用户允许替换旧开孔并补服务器缺失规则；2026-11-05 用户另行要求移除旧材料及取消不可交易前置，启用不再写永久交易限制，绑定期间仍限制转移；转换/烙印分支/BossFragment/星火战斗力仍有边界，勿宣称完整迁移。

- **核心宝石与符文装备窗口纠正**：`docs/tms282-skill/28-gemstones-symbol-panel.md`，17件真实道具、移除旧83怪10%掉落、282动态124怪2435902与静态双抽取；用户不迁服务器赠礼；已授权按 IMG 重建周任务（两日调查→3颗、UTC+8周四重置、事务去重）。EXE `4A05040`实际用UIEquip/Equip/Symbol，slotPos为左上角，撤回旧UIAuthenticForce“卡片中心”猜测；Grand额外汇总字段已核实为装备实例三字段，采用用户批准TSV补齐显示及收益，215项成长检查通过；非EXE等级公式。

- 当前项目五六转重迁移（2026-11-04）：`docs/tms282-skill/17-project-advanced-remigration.md`，
  仅 17 条已有终转路线，目录漏项、当前 VMatrix 新路径与原版任务证据缺口；任务证据补齐前不实施。
- 五六转任务深查（2026-11-04）：`docs/tms282-skill/18-advancement-evidence-search.md`，
  当前客户端 29,235 张数字地图扫描、剧情图/Graph 候选目标、三份 Lua 解码、JAR 缺脚本与停用证据；
  纠正 `state=1` 被误写为完成、五转三女神并行任务遗漏及六转伪线性链。

- `lucid-boss.md` — 露希妲（普通 boss 副本 + 變身燃燒 LucidDream）完整逻辑
- `will-boss.md` — **威爾**（2026-11-05）：三阶段 + 月光计 + P1 双次元门槛（Tms282WillPhase1）、
  242 技能族、282 opcode 0x086C..0x0881、BossWill.img 段表（0=easy鏡面/1=normal 8880340s/2=hard 8880300s/3=easy）；
  **Easy 与 Normal 同入口/同战斗图，实际召 Normal 8880340 系（不是"无召唤"）**；Easy 是官方正式难度
  （16.5 兆，mob 用 8880360 系 lv235，套件零引用）、官方三档 16.5/25.2/126 兆；
  `GameConstants` 与 `BossHelper` 对 8880342 的难度归属自相矛盾（BossHelper 对）；
  normal 车道 mob 无 `_Canvas` 但 `Packs/Mob_00000.ms` 内有完整 img（MsUnpack 已证实，非缺资源）。
- `gloom-boss.md` — **戴斯克/Gloom**（2026-11-05）：套件无状态机/召唤（结构性断链），mob=8644650(N)/8644655(C)
  lv255、**HP=957,809,713 / 3,064,991,082（dat 与 MOB_HP 同值，合成曲线非官方；官方 26T / 114.6T）**、
  三技能全 onlyOtherSkill（213=AreaWarning、186=AreaForce 都在枚举内，252=BlackMageGloom 有枚举无实现）；
  内部代号 dusk、无 Etc img/无开场演出、WILL_DUSK_GAUGE 是死包；
  **落地口径已定**（血量用官方 26T/114.6T + 自建触发循环：主触手 55s → 激光 +20s → 雾暴 30s → 陨石 25s）。
- `darknell-boss.md` — **頓凱爾/Darknell**（2026-11-05 四轮）：**§0 是全套神秘河 boss 的「冲突裁决原则」**
  （客户端表现 > 客户端数据 > 官方机制 > 自建）；套件无状态机/召唤，mob=8645009(N)/8645066(H) lv265、
  **HP=1,225,996,433 / 2,451,992,866（dat + BOSS_CONTEXT_HP；官方 26T / 160T）**；
  **`AreaWarning 0..9 = 5 精英 Boss × 2 招`**（eliteBossAir 只落 5/7、ball 只落 1/2、同色配对三重互锁，
  预警 2790–3870ms；3=Brain Scrambler / 8=Octopunch 由帧长定序）；精英出场**每掉 1/3 血 +1 只**
  （N: >66%1 / 33–66%2 / <33%3；H: 开局 2 / <50%3），间隔自建 20s；
  **`260-1..4 = 召唤剑`**（N→8645010/8645011；H→8645066 本体外观/8645067 隐形），
  判据是 `MobSkillStat.v`(ordinal 28) 槽——263/264 的 v 为空，只有 260 有值；
  **堕落勇士 = 8645003(N, 100 亿)/8645069(H, 300 亿)**，由场类召唤（本体 skill 里没有）；
  `fieldType=204` 是**枚举缺口**（FieldType 上限 152，Will 182/Lucid 147/Gloom 262·202/黑魔法师 199/
  Seren 271 同样超限，`getByVal` 全 null）；`world/boss/` 只有 Will/Seren/SerenFight/Lucid 四个相关类，
  **Gloom/黑魔法师/Darknell 三者都没有专属类**，只剩击杀计数 + 通用通关；BossDunkel.img 265 完整 463 canvas、282 只剩 140 行骨架；
  与黑魔法师共用 preQuest 35815；掉落表齐全。
- `seren-boss.md` — **受選的賽蓮/Seren**（2026-11-05）：车道=地图千位（hard 8880600/normal 8880630/extreme 8880650），
  P2 核+HpLinkMob 分时身体（offset 1/5/7/10）、太阳计+时钟（**套件 108/72/108/72 度 @120s 全错；官方 420s、
  四段 **120/130/40/130** 秒（度数 103/111/34/112），且**段长会被诅咒动态扣减**：午夜 −= 40s/(人数×3+5)）；
  **太阳计官方 8 条套件几乎全错**（Normal 上限 1200、满计眩晕 5s、午夜扣减上限 35 次、整周期回补、
  归零全队失败、午夜形态不涨计、P1→P2 清零；套件只对上 H/E 的 1000）；
  **黎明形态是「打护盾」不是打血**（护盾=下轮正午回血量，黎明受击加厚），每段末 Ray of Light（一人中→全队 1000%）；
  **形态专属技已坐实：263=正午、264=P1 主技+夕阳、260=午夜、黎明无技**（wz282 skill 节点，wz265 层全无）；
  **260 跨 boss 共用**（Darknell lv1–4／Seren lv5–7，实现须按 templateId 分流）；
  套件常量 SEREN_EXTREME_FIGHT_MAP=410002080 是 bug（该图 fieldType=0 的 hard 尾图，实际 P2=410002260）；
  **HP 分档：P1 本体=dat 21 亿占位须自配，P2 核=15.7/31.4/86.3 亿**（BossHpConstants）；
  `Seren.spawn` 零调用方且 `firstenter_4100021x0` 会压掉 XML first-enter ⇒ 现成套件进场空场。
  **8880342/8880344 撞号警示**：本项目星火挂的「塞伦 8880342/8880344」与 282 威爾 P3 本体/P1 上半身同号。
- **符文系统**（2026-11-04，正文 `docs/tms282-skill/27-rune-system.md`）：9 种古代之輪
  （RuneType 0..8）、生成条件（有怪/非 boss/非城镇/`feature.rune`）、重生 5min/冷却 4min、
  CP0580-0582 + 0x04FE/0x0547 挑战协议（48B 密文、tokenB 逐动作常量表、60s 过期、换图清除）、
  逐类型 buff（含 CTS852 稜镜召唤、reason 必须精确 80002889、CP04B4 临时技能权限不出图）、
  经验与专有效果分离（80002280=100%/80003910=200%）。
  回归补遗：符文技能全在 `Packs/Skill_00007.ms` 分组 `Skill/8000NN.img`（带 `runeSkill` 标志），
  官方文案/数值表见 27 §8；联动含 FixCoolTime 冷却改写（Char:8592）、Evan 輪之堅持
  （runeBuffTimerR 服务端未实现）、V 矩阵双路径（新引擎 cond=rune + legacy 400007006/7/11）、
  任务 runeAct、落雷攻击包 maximumMobCount 特例、BLOCKED_RUNE_MAPS 多重语义复用；死代码与
  「挑战 UI 不在旧 WZ 树、推断 EXE 原生绘制」见 27 §9/§7；超越之輪 5s vs 文案 2s 冲突待 EXE 核。
  本项目 Assets 无任何 rune 实现。
- `skill-system.md` — **技能子系统**：数据双层拆分（`.ms` 定义层 + `_Canvas` 像素层）、
  `common` 公式 schema vs `level` 旧 schema、公式 DSL 的 `logN` 取整语义、25 族特效层、
  MCV0（VP9 双流 + alpha）容器、EXE 关键函数 RVA（`0x589d140`/`0xdc5c80`/`0x111e390`…）、
  两套框架（legacy `SkillSystem::` vs `WZ2`）、5 条已证伪假设；另含**五/六转任务链与技能获取路径**
  （V 矩阵核心宝石→核心、HEXA 核心前置 `reqForActivation`、索尔艾尔达消耗）与**星力要点**
  （任务 `1481` 的 `totalStarForce=100`、279 概率表、费用公式、4 个服务端缺口）。
  **五/六转共 1,618 条技能，全量正文在 `mxd-android/docs/tms282-skill/`（8 篇 + 5 份数据集）**，
  本 skill 只留开工所需事实。**§9 技能「子状态 id」**：同名技能族里 `invisible=1`、
  只有 desc 没有 h、level/1 只有 time 的 buff id 是母技能施放后挂上的内部状态，
  不是可按技能（12 条，先前被误计为「无 stat buff」）；母技能 h 与字段投影是判据。
  **§10 周期领域**：其中 4 条（殛冻领域 / 箭影纷飞 / 禁咒之符 / 暗黑天魔VI）按状态
  `subTime` 周期结算同族伤害体，周期与 `damage/attackCount/mobCount` 在母技能 h 和
  level 行里 30 级逐行自证；表 `NativeFieldZones.json`、守卫 `check_field_zones.py`。
  **§11 剩余缺口定性**：`1241501` 圣域展开是第 13 条子状态（原始节点无 info/effect/level，
  `cooltime` 只是母技能投影；再发动冷却取伤害体 `cooltimeMS=500` 而非母技能 `t=0`）；
  `4141003` 刺客刻印VI 是标记按敌人个体记的两段式 proc；`500004073` 是字段覆盖核心
  （`NativeSkillOverrides.json`）。
  **§12 最后 6 条**：`5141011` 海龙石（资源/消耗门/突击伤害体）、`400041001` 散式投掷
  （追加方向）、`400041061` 飞闪起爆符（符咒主被动互斥）、`5141001` 超级闪连杀
  （`BeginCast` 真替换）、`2141502` 元素吸收（锁满 10 层 → FinalDamage +30）、
  `4241005` 黑暗面（隐身回避 `TakeBossDamage` + 解除后冷却），统一走
  `NativeSkillMechanics.json` / `check_skill_mechanics.py`。
  **§13 实用的战斗命令**（最后一条）：`400001004` 不是属性 buff —— 时效侧只有
  `time`（183..270 秒）+ `x=1`（所有技能等级 +1），被动 `asrR`（1..6% 状态异常抗性）
  走 `NativeSkillEffects` 的 `ASR` 键、由 `NativeMobDebuffs` 按百分比抵抗。
  等级加成判定在 `NativeCombatOrders.cs`（四转可越过技能册上限、其余只到最大等级；
  初心者/共通、5/6 转、V 与强化核心不动），`SkillProgress.LevelOf` = 存档等级 + 加成，
  加点/洗点/技能册一律读 `BaseLevelOf`。至此「buff 无 stat」清零。

**武器进化路线**（09 文档，2026-11-03）：
- 武器段：1212=露米娜丝闪耀魔杖、1222=天使破坏者灵魂射手、1232=恶魔复仇者魔剑(Desperado)、
  1262=凯内西斯 Psy-limiter、1282=伊利恩 Lucent Gauntlet、1562/1572=神之子琉/璃、1582=爆破手 Arm Cannon、
  1592=琳 Ancient Bow、1254/1292=阴阳扇/仙扇(Kanna/Hoyoung)、1404=卡莉 chakram；130-160=冒险家通用段。
  121-129 段 265 已存在，非 282 新增。
- 进化链：Dragon(110 黑龙王)→Briser(120 森兰丸)→Jaihin(130 扎昆/PB/HT)→Utgard(140 hard)→
  天照/月讀(150 森兰丸HELL)/夫尼爾(150 CRA)→航海師 AbsoLab(160 戴米安/洛图斯)→神祕冥界幽靈+創世(200)→命運(250)。
- Lv150 特例：任务 31848→31850 赫力席母解咒链，被詛咒的凱撒利恩(pad250)→解放的凱撒利恩(pad400 tuc2)。
- 命運(250) 每型号双变体：tuc8+only1 绑定 / tuc9 可交易，服务端配置必须区分 itemId。
- 数据：`docs/tms282-skill/data/weapons.tsv`(7840 件)、`weapon-drops.tsv`、`itemmake.tsv`(217)、
  `quest-weapon-rewards.tsv`(521)；工具 `tools/ms282/export_weapons.py`、`export_quest_itemmake.py`
  （坑：Act/item 下是序号节点，itemId 在 id 字段；ItemMake 节点名是十进制）。
- 缺口：Lv200/250 武器获取渠道、商店货架（无 shops 数据）、波賽頓(160) 来源——待核。

**机器人玩法**（10 文档，2026-11-03）：
- 双件套：01662xxx 机器人（槽 53，268 件）+ 01672xxx 機器人心臟（槽 54，56 件，唯一属性源，可卷/可潜能）。
- 装配规则 `Char.initAndroid`：心臟 grade ≤ 机器人 grade + 3（与道具文案「相同或提高」不一致）；缺模板静默不显示。
- 外观：Etc/Android/NNNN.img 模板（costume skin/hair/face 池随机 + action 台词）；282 客户端 hair/face 要全 id
  （`Tms282AndroidLook.resolve` 按余数还原），简化 id 直接崩。14 个 AP 槽 1200..1213 放机器人服装。
- 协议：CP02FE/FF/011B/025E 上行，LP044F-453/0481 下行；LP044F 必须带 AUX 帧 + 装备时 200ms 延时广播，
  否则 282 客户端 0xE06D7363 崩（android-trace/EQUIP-CRASH.md）。
- 出处：Lotus→史烏機器人、Damien→戴米安機器人、任务 38743→阿莎機器人+鋰之心；56 件心臟零获取记录，复刻需自配。
- 数据 `docs/tms282-skill/data/android-{items,hearts,templates}.tsv` + 工具 `tools/ms282/export_androids.py`。

**潜能/魔方**（14 文档，2026-11-03，正文 `docs/tms282-skill/14-potential-cube.md`）：
- 词条主表 `Data/Item/ItemOption.img` 743 条：`info{string,optionType?,reqLevel?} + level/1..20|25`；
  **无 potentialGrade**，grade 由 id 段定（同词条 10xxx/20xxx/30xxx/40xxx 四份拷贝按万位定阶；
  060xxx level 记录 fixedGrade=1/3/5/7=rare/epic/unique/legendary）；070xxx 120 条为 282 新段。
- 282 `Data/` 目录式 WZ 字符串只做经典 XOR（`0xaa+i`），**不叠加 AES key**；解析器
  `tools/ms282/imglib_282_data_dir.py`，导出样例 `docs/tms282-skill/data/itemoption-entries.json`。
- grade 四档（无 uncommon）；保底表 `Etc/CubeCeiling.img`（red 25/83/500、black 10/42/107、
  additional 31/76/214）；费用表 `Etc/potentialCostTable.img`（傳播者路径）。
- 魔方 id：现金 05062000~（红 5062009/黑 5062010/恢復 5062030 比较型）、附加 50625xx、
  消耗品 2711xxx（工匠/名匠/卡勒馬系）、附加 273xxxx；282 新 UI `UICubeWz2.img` 8 窗口。
- 服务端双路径：265 遗留 `CubeHandler`（含紫方块安全洞：handler 仍注册且信任客户端 option 列表）
  + **282 原生 CP 0x0780 → Tms282CubeRuntime**（规则族 family()、先落 journal 再发包、
  `char_cube282_pending/choice` 表、比较型二选一、现金基础魔方免鉴定费、眼光减免）。
- 升阶率生效值 = `ItemConstants:122-142` per-1000 内建数组（红 15/6/2.5、黑 15/12/5、
  BONUS_BRIGHT 20/15/7.5）；273 TSV 表被 `cube-settings.tsv` 默认停用。
- 落库 `equips.options` 七值 CSV（o1..3,b1..3,anvil，负值=隐藏），grade 是派生冗余列；
  附加潜能行 = options[3..5]，洗魔方永远不加第 3 行（thirdLineChance=0）。
- 黑魔方词条锁定(preventItem)、保底(pity)、选词条、6 行、閃炫 282 服务端均**未实现**；
  記憶 5062090/5062500/5062523 有数据无 handler。
- 本项目 `NativeEquipmentCube.cs` 是简化自制（4007000-4007007 自造 id + expandAttribute4 JSON），
  迁移 = 完全重写；存储 schema 与魔方范围**需用户拍板**。

- 五转核心槽图标错位及用户授权居中：`docs/tms282-skill/20-v-hexa-panel.md` §7.6；普通/特殊槽中心(29,28)/(33,28)，不将IMG位置向量解释冒称为已核实EXE算法。

- 五六转弹窗穿透与功能审计：`docs/tms282-skill/20-v-hexa-panel.md` §7.7；不能把布局/事务PASS当作技能属性已生效，核心等级同步、HEXA前置与属性玩法仍有明确缺口。

- 五六转核心功能补齐与源端错误纠正（2026-11-04）：`docs/tms282-skill/20-v-hexa-panel.md` §7.8；技能等级投影、reqForActivation保留ID、属性三线/保存/应用/费用及用户批准职业倍率；op8是二级密码不是改名，连击5秒复位已接，符文事件与完整演出仍有边界。

- V/HEXA 帮助与表现补充（2026-11-04）：`docs/tms282-skill/20-v-hexa-panel.md` §7.9；用户授权重编分页帮助，原按钮tooltip、技能/特殊/属性核心悬浮、HEXA激活/强化/属性结果窗；selected仅单帧，activatable六帧。粒子及未定锚点特效仍有边界。

- 五六转主面板穿透复报（2026-11-04）：`docs/tms282-skill/20-v-hexa-panel.md` §7.10；早期HUD在DrawAll之前处理输入、AnyVisible漏矩阵、主窗空白未消费；完整Pointer mask与10项行为检查，实机仍待验收。

- 全局确认框无法点击复报（2026-11-04）：`docs/tms282-skill/20-v-hexa-panel.md` §7.11；矩阵ownsPointer先吞掉YesNo点击，修为全局prompt独占最外层mask；生产DrawAll分发6场景通过，移除修复可复现旧错。

- 成长属性显示联查（2026-11-05）：`docs/tms282-growth-systems.md` 末节；内在/极限/称号常规加成统一结算，StatWindow改读PlayerCtrl最终属性以包含临时增益；特殊内在unsettled与速度/跳跃显示仍有明确边界。

- 极限属性命令直升级缓存（2026-11-05）：`docs/tms282-growth-systems.md` 末节；200级未分配339点，面板按等级变化重读、预览前同步等级，无需逐级发点。

- 极限点数复报与符文卸下（2026-11-05）：第28篇末节，原POINT白底+近白字的显示遗漏已修；符文槽改读会话库存，接右键/背包拖拽卸下，220项成长检查通过，未真机验收。

- 星火/灵魂按钮与完整性审计（2026-11-05）：`docs/tms282-skill/16-enhancement-entry.md`末节；材料框/高亮缺页前缀、双击入口、星火数值补全与黑星火journal恢复已修；SoulMP/灵魂技能、实际攻击力比较及原版二选一布局仍有明确缺口。

- **截图指定装备详情卡（2026-11-05）**：beidou-client-reverse的`references/inventory-and-tooltip.md`末节；原UIToolTip 324px皮肤、36张Canvas转GMS/ARGB4444，导入身份/节点报告在根docs/tms282-skill/data/equipment-tooltip-import.json。截图适配不冒称EXE完整布局复刻。

- 手机超长装备/套装详情自动右侧续栏（2026-11-05，用户授权布局）：beidou-client-reverse/references/inventory-and-tooltip.md末节；原素材、按行分栏、整组夹屏，720/600高模拟与手机验收边界分开记录。
