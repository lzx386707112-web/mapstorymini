# 任务面板、任务告知及截图重建训练任务

核实日期：2026-10-28。二进制：`BeiDou-Server/clien/BeiDou.exe`，ImageBase400000，SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。

## 已确定的原端显示

- 列表 OnCreate `87EC3F`、Draw `883126`；详情 OnCreate `87B3EF`、Draw **87BAE7**、背景与滚动 `87C388`、portrait `87C5E1`、按钮 `87D8C5`。`87FB86`是列表鼠标处理，不能误认作详情Draw。
- `UI/UIWindow.img/Quest/backgrnd`245×396，详情backgrnd2/backgrnd4为305×396。详情相邻列表右侧。窗口坐标均为局部原始像素。
- 三页签控件(3,23)，宽239，mode1。87EC3F设置控件+34=1、+38=65；标签取Quest/Tab/enabled、disabled各0..2。
- CCtrlTab `4DE15F` mode1 leading4、gap8、trailing4；固定宽65时填充段宽57。`4DE4D0`字符串2655=UI/Basic.img/Tab，mode1追加UTF16“2”，所以是**Basic.img/Tab2**。`4DE572`字符串2656..2659为left%d/middle%d/right%d/fill%d。`4DD903`左右/填充**state1选中、0未选中**（本次按汇编纠正旧记录，见下文）；middle0普通、1前项选中、2后项选中；标签中心定位，mode1额外y+2；选中取传入的第二张图，即Quest/Tab/disabled，未选中取enabled。标签不是完整按钮，不能只画文字图。
- 列表滚动条(225,50)高309，14行，y50+22i。组底色FF9FB5CE矩形(10,y-1,208,19)；选中任务FF396093矩形(28,y,192,19)。这是已核实原端FillRect，允许照搬。
- 详情标题(35,40)、等级(23,60)、NPC脚底(242,110)。正文绘制原点(17,122)、测量宽265、内缩1；有summary时正文控件高120、summary原点(17,248)、高110，其余正文控件高243。正文裁剪(18,123,263,控件高-2)，summary裁剪(18,249,263,108)。旧123/237高度是错误迁移参数，已纠正。VScr4 x283。放弃(242,372)、告知(150,372)。QuestInfo数值页按状态取，不能把缺号页压缩后当状态索引。
- 告知栏OnCreate `885165`、Draw `885EB8`；Basic/QuestAlarm max223×25、min223×20、center223×18、bottom223×5；标题(25,2)，行高18；最多5项，自动登记超时10分钟。按钮使用原端BtQ/BtAuto及Basic/BtClose/BtMin/BtMax/BtClose2。
- 完成特效 `93806E..938111`：93809E取字符串2313=`Effect/BasicEff.img/QuestClear`并通过439750挂角色origin；字符串2317=QuestClear，通过9899B1播放音量100。动画0..10共11帧，每帧100ms，声音Game/QuestClear长2411ms。对应服务端Quest.java showSpecialEffect(9)。

复现：`tools/inspect_beidou_client.py <exe> --start <上述VA> --end <范围终点>`；工具身份/字符串池方法见binary-and-tools.md。源码入口：QuestWindow.cs、QuestAlarm.cs、QuestNotice（QuestLogic.cs）、NpcApi.showQuestCompleteEffect、DialogFlow.Measure margin参数。

## 用户授权截图重建：不是原端数据发现

当前clien的Quest IMG、备份、服务端XML和脚本查得的8142是旧“透德的打猎方法”：透德接、彼特交、两种怪各1只、两种掉落各1件。这**不是**用户截图里的两条独立任务。用户确认截图就在当前clien运行，数据差异原因仍未确定；不能再将8142回归通过当作截图任务正确的证据。

用户随后明确授权：“不行你就按照我截图写对应的脚本好了”。据此建立独立本地任务，不修改原端脚本原件：

| 本地ID（非源端号） | 名称 | 接取/交付NPC | 唯一目标 |
| --- | --- | --- | --- |
| 900001 | 透德的打猎法 | 9101002 | 9409001打鼓兔子×5 |
| 900002 | 彼特的打猎法 | 9101003 | 9409000小白雪鬼×5 |

截图显示等级10以下；可以同时接取，各自回本NPC交付，没有旧衣服/掉落/前置要求。彼特含冰封雪域引言→八只手怪物故事→接受页→接取确认。可见对白按截图录入。未拍到的完成对白使用简短重建确认；奖励未在截图展示；用户2026-10-28明确指定两条各奖励50经验，现以JSON的exp字段接入completeAct，供任务定义读取；交付脚本调用qm.gainExp(50)实际发放。这是用户指定值，不是源端已核实值。

数据：Assets/Resources/Quests/screenshot_training.json；脚本：content/script-overrides/quest/900001.js、900002.js。sync_npc_scripts.py保留源端逐字节原件，再合并显式截图override，生成Data/scripts和Resources包及来源哈希。QuestCatalog登记这两个本地定义，移除两NPC菜单中的8142绑定，8142定义保留供旧存档读取，不自动转换已有8142进度。

## 验证与迁移边界

QuestNpcChecks.CheckTrainingQuestProgress通过实际NpcSession+JS执行接取/交付，验证同时接取、过程对白、4/5拦截、两种怪不串计数、5封顶、NPC不串交付、完成后不可重复领取、等级门槛。CheckNinaCandyChain验证妮娜棒棒糖发放/恢复及不重复；CheckQuestPresentation验证真实击杀登记告知与QuestClear动画/直接脚本接口。

本次最终Unity回归24245通过、0失败（包含实际IMG怪物名字校验：9409001打鼓兔子、9409000小白雪鬼；分类20彩虹岛）。编译检查0错误0警告。脚本包2779、npc889、quest470，parseFail0；NPC广泛run仍有既存47runFail/73javaInterop，与修改前基线相同，不是全量脚本运行通过。

显示已接三页签、正文和summary独立滚动、目标进度、原端按钮及完成动画声音。告知栏目前仅运行时保存，未保存跨重启追踪列表；目标颜色/自动按钮勾选状态及部分非击杀目标尚未完整迁移。尚未完成Android实机验收。布局视觉检查通过临时隔离Unity工程截图，不代表原地图端到端实机验收。


## 2026-10-28 按源端参数修正布局（补核）

身份仍为上文SHA256；本次使用inspect_beidou_client.py核对原始汇编，未用反编译伪代码覆盖状态布尔值。

### 页签状态纠正

- `4DD957..4DD969`：比较控件+3C（选中索引）与0，`sete al`，传给4DE572(part0,state)。因此左边状态1才是第一项选中。同理右边、fill均传“当前项==选中项”。先前记录0选中是错的，不能沿用。
- `87F160`载入字符串2664(enabled)，保存到局部-3C；`87F23A`载入2665(disabled)，保存到-44。`87F377..87F3AF`先push disabled、后push enabled，进入4DE0E2；`4DE100..4DE11D`分别写item+14/+18；4DD903选中取+18，未选中取+14。因此素材命名enabled/disabled不能被理解成选中/未选中。
- 字图坐标遵循整数除法：x=start+57/2-width/2；y=23+19/2-height/2+2。不能把两个整数除法合并为(57-width)/2，奇数图宽会差1像素。

### 列表和正文

- `883126`分类行FillRect(10,49+22i,208,19)、文字(30,53+22i)；分类折叠控件来自字符串1261=Basic/BtMin、1262=Basic/BtMax，位置(13,52+22i)。此前用Quest/icon0/1显示了灰叹号，现纠正为原端加减按钮。
- 任务行文字(30,52+22i)，截断宽192；选中底(28,50+22i,192,19)。旧文字y+4已改为+2；普通进行中使用Quest/icon3蓝色圆圈，完成可交付用icon2箭头，已完成icon4。尚未完整迁移new/期限/父任务状态动画分支。
- `87BACC..87BAE6`：默认EAX=0x78(120)，若详情数据+38为空/零长度则改为0xF3(243)。不能凭背景留白猜正文高度。
- `882625..88266C`、`8827A0..8827E0`：正文和summary各自新建文本helper，+0C=1（内缩）、+10=0x109(265，测量宽)。`99DD54`折行条件是x+内缩+run宽 <= helper+10，实际可用文字宽263。
- `87BAE7`在(18,123,263,bodyHeight-2)和(18,249,263,108)裁剪，但绘制run仍加(17,122)/(17,248)，不能把裁剪原点当内容原点再额外平移1px。迁移在BeginClip后减回该1px。
- `8818DF`顺序取summary(5655)、demandSummary(5625)、rewardSummary(5650)，每个非空字段作为独立文本块；存在奖励时先加入单空格块及`#Wreward#`，最后在整个列表头插入`#Wsummary#`。`99DD54`case16通过字符串5585=`UI/UIWindow.img/Quest/%s`引用原IMG。当前用等价#f引用原图，各块单独测量并累加高度，不用手插换行拼成一大块。
- `87DCA9`滚动range=max(1,trunc((各块高总和+块数*7-视口高)/8)+1)，位置×8。正文range计算固定使用120（函数中硬编码，纵使无summary正文控件高243），summary使用110；当前已按原常量分别实现，不把控件高度擅自代入range计算。
- `988CF3`长任务名截断（标题150、列表192）先预留字符串2192=`..`的宽度，再截到可容纳长度，不能直接剪掉末尾。Unity测量仍使用项目中文替代字体，不宣称与Windows Arial字形逐像素相同。
- `87D8C5`只有进行中/已完成才创建放弃(242,372)和告知(150,372)，已完成禁用；未接任务另创建BtMarkNpc(217,372)。当前已去掉未接任务错误显示的两个禁用按钮；BtMarkNpc所连原端NPC查找对话框（87E769→9E9823/9EB366）尚未迁移，未加入空按钮。

运行时代码：QuestWindow.cs、ScreenshotTrainingQuests.cs（把截图专用“完成后对话”行放回截图任务的demandSummary，避免给所有原WZ任务强插该行）。所有面板底图、按钮、页签和标题IMG都直接引用源资源；选中行矩形沿用已核实FillRect参数。

视觉复核使用隔离Unity工程，覆盖两任务同时进行、已完成、可开始、长正文2239有奖励简述四种状态，原始尺寸1280×720；现有任务回归24245项通过、0失败；最终布局代码编译0错误0警告，脚本打包parseFail0，git diff --check通过。全量NPC脚本运行仍为既存47项runFail、73项javaInterop。未打包APK，未做Android实机布局验收。NPC查找、部分期限/父任务图标与拖动等完整行为不因本次布局修正而声称已实现。


## 2026-10-28 目标达成的右下角红色气泡

EXE身份仍为本文SHA256。此次明确区分：目标达成、可交付时的FadeYesNo，与交付后角色身上的QuestClear不是同一路。

- `523408..52363C`构造FadeYesNo type7，questId存this+E8；非零任务号用字符串1293=`UI/UIWindow.img/FadeYesNo/icon7`（信封15×12），背景字符串1298=`.../backgrnd4`（154×44）。窗口实际创建155×44，z10。零任务号走新任务icon6，本次不扩展该分支。
- `52358B..5235ED`原生位置x464、y508−5×全局存活窗数；this+7C/+80/+84=0/255/0，渐入1000ms、到期30000ms、渐出1000ms，起终坐标相同。`51F93D`是上述字段赋值，`51FB31`将到期时刻设置为now+30000，`51FB72/51FC87`推进渐变状态。旧HUD曾按原800×600的距右336px、距底92+5×窗数px适配视口，宽屏下与左侧底栏错位；2026-10-29按用户标注改为跟随通知槽，见下节。没有声称完成DLL宽屏位置补丁迁移。
- `520DE0..520EFE`非零任务号标题取字符串771（Quest Complete!），绘制(27,7)，任务名绘制(27,20)。`521190..5211A4`任务名截断宽100（988CF3的双点规则）。`5205CF..520624`字体Arial、12px、颜色−1（白色）。`5223FD..522457`信封在x6、y=(37−图高)/2；`5203BB`type7按钮x136，BtOK y7、BtCancel y20。`52268E..5226BA`确认仅当任务仍进行中，打开对应任务的进行中详情。
- 触发证据：物品数量跨需求阈值路径`A1F685..A1F87E`，其中`A1F76B..A1F783`核对旧值低于需求、新值达到需求、全部完成条件成立，`A1F857`调用523408；任务进度更新路径`A2139A..A21424`重查完整条件后调用同一构造。`A247CF`相同type7+任务号去重，多个窗口错开5px，移除后`52459C`其余窗下移5px。

本工程原QuestNotice.Push只保存文字和播放QuestClear，HUD从未读取其Active；并没有红气泡绘制链。现已在真实OnMobKilled更新前后比较CanComplete，首次全部目标达成时登记原IMG气泡，独立于任务告知开关；交付移除，确认打开任务详情，取消关闭。两条截图任务分别触发，不在4/5时提前触发，也不重复把交付当作目标达成。当前仅接击杀更新路径；物品、金币及其他条件变化的气泡触发尚未迁移，不宣称全任务通知完整。

本次验证：新增4/5不弹、5/5对应任务登记、交付移除、另一任务独立登记的回归；Unity QuestNpcChecks共24249项通过、0失败。编译0错误0警告，脚本包parseFail0；广泛NPC run仍为既存47runFail/73javaInterop。尚未Android实机验收。
视觉复核：隔离Unity工程通过真实击杀路径生成气泡，1280×720截图已查看，原红色IMG、信封、标题/任务名及确认/关闭按钮可见于右下区域。fixture背景为登录画面，不等于原地图端到端测试。


### 2026-10-28 任务经验核对（源端服务端数据；非 EXE 推断）

- `Quest/Act.img/1033/1/exp=7`、`1034/1/exp=20`，分别是妮娜任务链两步基础奖励。复现：`python3 tools/imgdump.py Quest/Act.img 1033 --depth 4`（1034 同理）。本端 `QuestCatalog.ParseActions` 读取 exp，`QuestLogic.Complete(...,true)` → `Apply` → `Profile.GrantExperience` 发基础数值。
- 源端 `ExpAction.runAction`：`use_quest_rate=false` 用 `gain*Character.getExpRate()`；true 用 `gain*getQuestExpRate()`，再经 `NumberTool.floatToInt`。`Character.hasNoviceExpRate` 在新手职业且 level<11、`use_enforce_novice_exp_rate=true` 时令两个倍率均为1。默认迁移 `V1.7.0__create_game_config.sql` 中这两开关分别 false/true；这是代码及默认配置证据，不等于读取过用户正在运行的源端数据库。
- 本端目前直接发基础值，未完整迁移上述非新手倍率；不能声称所有等级的任务奖励已与源端一致。本次未凭猜测修改倍率。
- `ExpTable.java` SHA256 `f31f6911f7b26a7d01913a00bb8a432fcfcaa581a5afd3a0a5c445d031c5c415` 的201项与本端 JSON 前201项逐项相同。当前核对只确认新手等级曲线，未确认资源额外201..249项。6级需求372、7级560；7+20经验分别约7.26%/4.82%，未跨级时经验条只涨几个百分点可能正常。`StatusBar.DrawGauges` 的EXP文字目前向下取整显示整数百分比，填充用 `Profile.ExpRatio`。
- 截图重建900001/900002未提供奖励证据，注册时未设置 completeAct.exp，当前确实各为0经验；只能确认“尚未还原奖励”，不能视为原端确实没有奖励。已向用户询问实际经验/奖励截图，未擅用不对应的8142奖励。
- 此次为数据与代码路径核对，未修改运行时代码、未执行新的Unity/Android运行验收。


2026-10-28 用户回复“各奖励50吧”：900001/900002各配置50经验，forceCompleteQuest只置完成状态、不执行Act奖励，故两条override在完成后各调用一次gainExp(50)；接取不发放；不自动补发已完成存档的奖励。此前记录的0经验仅描述修改前状态。

50经验变更验证：Unity QuestNpcChecks新增真实JS接取不发经验、透德交付50、两条合计100三项断言；编译0错误0警告，脚本包2779条parseFail=0。NPC全量运行仍有既存47 runFail/73 javaInterop。未打包APK或做Android实机验收。


### 2026-10-29 用户指定气泡尾巴对齐便签槽

- 用户截图明确要求尾巴对准装备按钮左侧的便签按钮；这是用户指定的定位修正，不是新增的DLL逆向结论。原端EXE身份及type7函数地址仍见上文。
- 重新用 `tools/imgdump.py UI/UIWindow.img FadeYesNo/backgrnd4 --depth 2` 和 `tools/imgpng.py` 核对原素材：154×44、origin=(0,0)，尾尖局部坐标(140,43)。底栏已核实通知槽x573、宽42，便签iconMemo在x599、宽14，其中心为x606（通知槽内33）。
- `QuestNotice.DrawReady` 使用 `BottomBar.NotifyBoxRect()`，气泡左上角为 `(槽x+(33−140)×S, 槽y−(43+5×存活窗序)×S)`；尾尖水平对齐便签中心，垂直在槽顶上方保留原5px错层。不再依赖屏幕右缘，不改素材、内容或触发条件。
- 验证：原IMG导出目视核对，1040×260、1280×720、1920×1080、2400×1080坐标公式检查及diff检查通过；compile-check Rebuild 0错误0警告，脚本包2779条parseFail=0，全量NPC run仍为既存47 runFail/73 javaInterop。未运行Unity画面或Android实机验收。本节不沿用此前运行回归作为新定位的视觉验收。


## 2026-10-30 任务键与完成通知入口差异：自动选择/详情展开

身份仍为本文完整SHA256。全工程入口核对：NativeKeyActions.Execute(GameAction.Quest)、
ShortcutWindow的BtQuest、QuestAlarm/BtQ均调用同一QuestWindow.Toggle；
FadeYesNo确认及告知栏任务行调用QuestWindow.Select。没有第二套玩家任务窗口。
旧Toggle只反转Visible，首次selectedId=0导致只出现245×396列表；
Select额外设置状态页和selectedId，因此通知出现245+305双面板。隐藏详情后Toggle亦未恢复详情，
通知定位还没有展开折叠分类/滚动到任务行。不是原IMG存在两种不同任务面板。

### 补核的原端状态链

- `0x87FB2F..0x87FB84`：控件2000、通知500的页签变更调用`0x87FD4F`。
- `0x87FD57..0x87FD6B`：清列表this+5C4为-1，写页签this+5C0，再重建列表/滚动范围。
- 进行中页`0x87FD78..0x87FD85`先尝试`0x882932`、再`0x88296E`（this+5A4记录的上次进行中任务）。
  `0x882932`消费任务管理器+270队列（`0x71DF05/0x71DF1C`），队列完整写入语义尚未逆向；
  不将其擅自解释成“所有可交付任务”或“最近完成任务”。本次没有新增该独立原生队列。
- `0x87FDA1..0x87FDB8`仍无选中时调用`0x882A71`，从当前页列表跳过分类行并选首个任务；
  `0x882B2E`先展开分类（`0x882CA6`）、找到任务行后`0x882C23`调用`0x8846F2`写this+5C4，
  `0x882C3E`调用`0x882C8A`同步滚动位置。选择进行中任务时`0x884743`保存任务号到+5A4。
- `0x884390..0x88439C`：Update发现有选中行且没有详情对象this+614时，调用`0x88422C(1)`；
  后者`0x884287..0x8842C5`构造详情（`0x87B155`）。因此自动选中后下一次Update就打开详情，不是必须手点任务行。
- OnCreate `0x87FAA8..0x87FAE2`：无有效初始页签时先选进行中，若+5C4仍-1再选可开始。
  有有效显式页签则遵循该页，不据此声称空的已完成页也要跳可开始。

### 当前修复

QuestWindow.Toggle打开时重建列表、恢复有效选中任务，否则自动选择首项并打开原详情；
进行中空列表回到可开始。切换页签复用同一选择流程。Select会展开目标分类、重置正文滚动并让对应列表行可见。
保持原245/305×396、Tab2/VScr4/背景和各节点布局；无新自绘面板。新实现没有补齐原生任务更新队列、拖动动画或所有任务排序优先级。

复现：`inspect_beidou_client.py <exe> --fn 0x87FD4F`、`--fn 0x882A71`、`--fn 0x882B2E`、
`--fn 0x8846F2`、`--fn 0x884335`、`--fn 0x88422C`，以及`--fn 0x87EC3F`末尾。
验证入口：QuestWindowEntryChecks.Run，使用隔离Profile和真实OnGUI任务键/FadeYesNo确认点击，
检查两入口选中任务/页签/详情一致、空进行中回退、已完成页自动选中、隐藏详情后重开以及折叠/视口外任务定位。
截图输出docs/hud-review/quest-key-entry.png、quest-notice-entry.png。

本次实际验证：QuestWindowEntryChecks.Run在Unity真实Play/OnGUI下退出0并PASS；
上述入口、页签、详情恢复与任务可见性断言均通过。两张1280×720截图已目视复核，窗口区域(365,146)..(915,542)
像素比较一致；背景是隔离fixture的自动登录画面，不代表地图端到端或Android实机验收。
compile-check Rebuild为0错误0警告；脚本包2779条parseFail0/mangled0；Mock全量NPC仍为790通过、27runFail、72javaInterop，未冒称全量脚本通过。
此前death-and-respawn/回归摘要里“任务面板等待补充”的状态由本节后续分析与修复取代。

## 2026-10-30 任务告知鼠标长按拖动

沿本文已核实QuestAlarm IMG/223px布局与按钮坐标，不改素材。原实现每次Draw强制贴右上，没有保留拖动状态。
现保存窗口位置，用标题局部(25,0,114,20)捕获按下，持续按住时跟随Pointer位移，释放后结束并消费点击；
该区域避开原BtQ/BtAuto/最大化/关闭按钮，位置按现有NativeUi.ClampWindowPosition约束视口。
这段捕获是Unity输入迁移，不是新发现的EXE精确DragStart矩形；原CWnd鼠标捕获语义见ui-drag-and-drop.md。
QuestWindowEntryChecks.Run新增实际Repaint下按住、移动、释放和按钮区域不误拖的断言。

本轮实际验证：隔离Unity QuestWindowEntryChecks.Run退出0，QuestAlarmDragChecks明确PASS长按标题移动、释放停止、按钮区域不误拖；既有任务键/通知入口回归亦PASS。首次失败为浮点精确比较，改为0.01像素容差；后续截图目录缺失仅修fixture自动建目录。未做Android实机验收。
