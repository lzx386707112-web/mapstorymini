---
name: tms282-reverse
description: TMS282 客户端/服务端逆向与逻辑分析结论沉淀。分析或迁移 282 内容前必读。
---

# tms282-reverse

TMS 282 端（客户端 `~/Documents/mxd/MapleStory`，服务端套件 `~/Documents/mxd/TMS282.5`）的已确定逻辑记录。
规则来源：mxd-android/AGENTS.md 规则 8-12。282 服务端是 swordie 架构 Java 源码（`source/src/main/java/net/swordie/ms`），
**无 js 脚本**（`data/scripts` 为空），原 js 的逻辑全部以 Java 分发实现（`BossManager`/`ScriptManagerImpl`/`CustomFieldScripts`）。

域内细节见 `references/`：

- `lucid-boss.md` — 露希妲（普通 boss 副本 + 變身燃燒 LucidDream）完整逻辑
- `skill-system.md` — **技能子系统**：数据双层拆分（`.ms` 定义层 + `_Canvas` 像素层）、
  `common` 公式 schema vs `level` 旧 schema、公式 DSL 的 `logN` 取整语义、25 族特效层、
  MCV0（VP9 双流 + alpha）容器、EXE 关键函数 RVA（`0x589d140`/`0xdc5c80`/`0x111e390`…）、
  两套框架（legacy `SkillSystem::` vs `WZ2`）、5 条已证伪假设；另含**五/六转任务链与技能获取路径**
  （V 矩阵核心宝石→核心、HEXA 核心前置 `reqForActivation`、索尔艾尔达消耗）与**星力要点**
  （任务 `1481` 的 `totalStarForce=100`、279 概率表、费用公式、4 个服务端缺口）。
  **五/六转共 1,618 条技能，全量正文在 `mxd-android/docs/tms282-skill/`（8 篇 + 5 份数据集）**，
  本 skill 只留开工所需事实。

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
