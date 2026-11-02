# 角色资料与自动分配 AP

## 身份与复现

`clien/BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。
复现使用 `tools/inspect_beidou_client.py <exe> --fn <VA>`；资源使用 `tools/imgdump.py UI/UIWindow.img UserInfo --depth 3`。

## CUIUserInfo 基础页

- OnCreate `0x8FECB1`；布局切换 `0x8FF863`；按钮创建 `0x8FFE3B`；绘制 `0x901185`；按钮事件 `0x8FF5B0`。
- mode 0：`UI/UIWindow.img/UserInfo/backgrnd`，275×199。其余 mode 1/2/3 分别为 275×400、275×362、275×404；基础页不等于扩展页已迁移。
- 纸娃娃 `0x903FC7..0x90402E`：脚底 (55,130)。名字中心 x=52、y=144；等级 (160,77)、职业 (160,96)、人气 (160,113)、家族 (160,131)。名字 font 0，其余主要 font 1，职业 font 44。婚姻图 `Marry` 在 (10,30)。
- 基础按钮：BtItem (208,22)、BtWish (208,41)、BtFamily (105,23)、BtParty (149,23)、BtTrade (149,42)。底部 y=177，BtTamingShow x=5、BtPetShow x=93、BtBookShow x=145、BtExceptionShow x=199。
- 服务端 `CharInfoRequestHandler.java` 要求目标在同一地图，允许自己；不能跨地图查询假人。

2026-11-01纠正：旧文把(105,23)误记为赠送、底部x199误记为学院；字符串池a0e/a13及8FFE3B/8FF5B0证实分别为学院和宠物排除列表。赠送实际在愿望子窗的每一行。当前扩展迁移及明确缺口见[五项修复与角色扩展](five-fixes-character-expansion-2026-11-01.md)，不能沿用旧的“仅基础页”或声称完整还原。

## CUIStat 自动 AP

- 创建 `0x8C4C7F` / `0x8C79F5`；按钮分派 `0x8C522F`；自动分配 `0x8C8692` → `0x7939BA`。
- AP 箭头在 x=153（`0x8C7B19..0x8C7CDA`），不是 x=185。
- `0x8C5845`：有 AP 才启用。job 500 使用 BtAuto1/BtAuto2，其余支持职业使用 BtAuto。原按钮坐标 (97,197)；海盗两个分别 (97,197)/(97,217)。
- `0x7939BA` 返回两条有序分配：先补副属性目标，再补主属性，`0x8C8692` 依次按剩余 AP 截断，剩余加到最后一项。确认字符串 1951/1952；确认结果 6 才发送 `0xA23B3D`。
- 战士 DEX 目标 Lv≤30 为 2Lv，否则 Lv+30；法师 LUK=Lv+3；弓手 STR=Lv+5，弩手 STR=Lv；盗贼 DEX 在 Lv≤40 为 2Lv，否则 Lv+40；拳手 DEX 在 Lv≤20 为 2Lv，否则 Lv+20。上述主属性目标为 5Lv+12−副属性目标。枪手 STR=Lv、DEX=4Lv+12；普通新手 DEX=Lv、STR=4Lv+12。
- 默认服务端 `use_server_auto_assigner=false`，不要把可选的服务端自动算法当作默认客户端算法。

当前 `Profile.BuildAutoApPlan` 为纯计算，`AssignAutoAp` 校验 AP/32767 上限后一次提交；StatWindow 弹原确认流程。尚需核实 `0x7939BA` 所读 CBasicStat 四维与附加装备属性的边界；目前使用存档基础四维。不要将已编译解释为实机操作验收。

## 剑士两技能的近战命中时序

用户复现为剑士 1001004 强力攻击、1001005 群体攻击。两技能原 IMG 无 action 节点，使用武器攻击动作。旧实现走 atk 原始帧速，Cast 无 impact 计划时立即命中；与原端不同。

近战 `0x9537D5..0x9556A8`；`0x954CF0 → 0x78DF87` 为伤害计算相关；`0x954D07 → 0x75BF50` 是特定技能伤害倍率调整（遍历 15 个伤害值），并非通用命中时钟。

本次新增确定证据：

- 动作表 `0xBEC620 + action*24`：+0xC 原始总时长，+0x10 命中比例分子；普通动作解析 `0x4072EB` 累加 delay，`0x40737A..0x40738A` 将分子写为总长减最后一帧。引用动作/负 delay 是另一分支（`0x406F91..0x406FA3`），不能把普通公式无条件推广到所有技能。
- `0x454776..0x454837` 逐帧以整数 `delay*(clamp(speed,2,10)+10)/16` 换算，再求动作总长。
- `0x9542C0..0x9542DE`：基础命中时长 = 已换算动作总长 × 表+0x10 / 表+0xC，整数除法；`0x9427C4` 对两剑士 ID 不覆写。
- `0x9548E5..0x954907`：目标位置x减攻击源x取绝对值，乘 VA `0xAFE808` double **1.5**，经 `0xA62018` 的整数转换，加基础命中时长。`0x969A33..0x969A41` 普通技能入口调用近战时四个附加参数为 0（无额外源x偏移）。因此不同横向距离的命中时间确实不同。
- 示例 Body swingO1 原始 300/150/350ms：speed6 基础450ms；speed2逐帧225/112/262，总599，基础 floor(599×450/800)=336ms；横向100px再加150ms。
- `0x955509 → 0x957830` 的三分之一动作时间是条件后续技能调度，`0x957830` 会遍历技能关联数组并写 +0x2AF4..2B00，**不是普通 effect 播放时间**，不要据此把两技能 effect 延迟三分之一。

当前只对 1001004/1001005 接入逐帧攻速、原动作比例和距离延迟；开始施放时固定目标，延迟到期后才走受击/数字/命中特效入口，切图沿用 CancelActions 取消。仍未完成普通攻击、其他职业、随机动作分支、残影、完整攻击音效/effect 时序，以及原端服务器HP结算与客户端表现分离的完整对照，不宣称全部手感 1:1。

本轮验证及复现入口见 `docs/skill-regression/2026-10-30-feedback.md`；新增纯 AP 检查12组通过、服务端664/0、Unity近战公式/Perion枫叶定向检查通过，不代表 Android 实机验收。


## 2026-10-31反馈补查

自动AP确认字体22/23与中文迁移见 [本次确认记录](field-feedback-2026-10.md)。保留其中的原端身份、地址、资源参数与验证边界，不将静态资源扫描等同于实机验收。

## 职业称呼与能力值窗职业行（2026-11-01 定案）

**职业名唯一权威 = 源端 i18n `job.name.<id>`，三处证据互相印证，旧 `UI/JobSkills.json` 的表是错的。**

- `message_zh_CN.properties`：200 魔法师 / 210 法师（火毒）/ 211 巫师（火毒）/ 212 魔导师（火毒）/ 220-222 冰雷同理；311 射手 / 312 神射手 / 321 游侠 / 322 箭神；412 隐士 / 422 侠盗 / 520 火枪手。
- `String/Skill.img` 的 `bookName` 与 i18n 一一对应（311 book=「射手技能」、321 book=「游侠技能」、211 book=「巫师（火毒）技能」、212 book=「最高级火, 毒魔法」），可当交叉校验。
- `BeiDou.exe` 字符串池 32..50（Wizard/Mage/Arch Mage/Ranger/Bowmaster/Sniper/Marksman…）的中文列与 i18n 一致。

旧 `JobSkills.json` 把魔法师线整体**提前一阶**（210 写作「巫师(火毒)」、211「魔导师(火毒)」、212 自造「大魔导师(火毒)」），弓手线把 311/321/322 全错位（游侠/狙击手/神箭手）。`tools/export_jobs.py` 以前会用它覆盖 i18n，已删除该覆盖逻辑。

**能力值窗（CUIStat::Draw `0x8C59FF`）的职业行是图，不是文本：**

- `0x8C5BD3` 在 y=`0x37`(55) 处 `call 0x8C7FB6`（同一函数 `0x8C7FC0` 的 SEH 入口），由它按 jobId 加载两张图。
- 素材格式（宽字符串）：`main` @VA `0xB3BA04`、`sub/%d` @VA `0xB3BA10`；`main/premium` @VA `0xB3B9C8`、`sub/premium/%d` @VA `0xB3B9E4`。
- jobId 拆分：`eax = jobId / 1000`（千位，骑士团=1）、`百位 = (jobId % 1000) / 100`。百位 == 9（GM 900/910）走 `main/premium` 分支，并用字符串池 5541「GM」/2995「gamemaster」画两行文本；常规职业走 `main/<百位>` + `sub/<百位>/<jobId>`。
- 图尺寸/坐标：`main` 11 高画在 (62,53)，`sub` 10 高画在 (64 起点)。`Stat/Job/main` 只有 0..5（新手/战士/魔法师/弓箭手/飞侠/海盗），`sub` 每组是该系全部职业 id；`premium` 两组在北斗素材里是**空子属性**（无 canvas）。
- 因此**骑士团（1100..1512）、初心者 1000、GM 9xx 在原版就没有图** → 迁移端回退一行文本 `JobDefs.NameOf(jobId)`（`StatWindow`）。
- 之前迁移版的 bug：按 `Profile.cls` 猜四转（`mage→212`），未二转法师也显示「火毒魔导师」。必须用 `Profile.JobId`。校验入口 `tools/audit_job_names.py`（`--check` 有差异返回 1）。

后台技能图鉴 `AdminCatalogData.JobNames` **不要“修正”**：它与源端 `SkillCatalogService.java` 的 `JOB_NAMES` 逐条一致（102/102），源端自己就写「大魔导师(火毒)/箭神/夜使者/风行者」，只影响后台分类展示。
