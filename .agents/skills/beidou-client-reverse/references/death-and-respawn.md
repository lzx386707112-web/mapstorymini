# 玩家死亡、墓碑与复活确认（2026-10-30）

## 身份与原端调用链

BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。
`0x95AF4E`调用`0x92FD5A`死亡墓碑表现；`0x92FD5A`读取用户位置、查询落脚点并记录用户+1FA4/+1FA8。
`0x930545`读字符串2181 `Effect/Tomb.img/fall`，图层写用户+1F9C、附着用户+1150；
`0x930682..0x93069C`设置起始y为目标y-600（原地图坐标向下）；
`0x930710..0x930763`以图层当前时间+0x370=880ms移动到最终目标位置。
`0x9307AE`读取Tombstone声音名；`0x9308AB`读取land。声音调用已确定，但本次没有新增声音导出。

`0xA066F3`仅在上下文+3520为0时写当前时刻（0x987257）；不是创建弹窗函数。
上下文更新`0xA0343A..0xA0345C`在当前时间减该值>0x898=2200ms时，
调用`0xA14AFC`，该单例创建器`0xA14B2C`调用`0x897B5F`，随后清+3520。
`0x897B5F`创建286×146窗口，原800×600坐标(257,97)。

## IMG及控件

- 墓碑 `Effect/Tomb.img/fall/0..19`：0..11每帧80ms、12..14每帧100ms、15..19每帧150ms，总2010ms。
- `land/0` UOL指向`../fall/19`；fall19为40×43，origin(20,43)。所有帧必须各自使用原origin，不能统一中心/底部pivot。
- 普通死亡背景是 `UI/UIWindow.img/Notice/0`（286×146），中文说明已烘焙在Canvas中，不另画标题/边框/经验损失/回城文字。
- `0x897C94` OnCreate在普通分支`0x89810F..0x89812B`读取Notice/0，
  `0x8981B0..0x8981B6`以控件ID1、局部(124,115)创建 `UI/Basic.img/BtOK2`（47×19）。
- `0x8981E1`分派ID1到`0x898224(0)`，调用`0x53035D`换图后清理；`0x898203`包含600000ms自动确认。
- Notice1/4及复活buff特殊分支仍有未迁移边界；Notice2轮回道具后续迁移见本文末补充。

## MapleDash修复及边界

DeathFlow改为全20帧原始delay/origin、600px/880ms下落、播完保持land、2.2秒后Notice/0+BtOK2。
窗口沿原800×600中心偏移适配当前视口及BottomBar.S缩放；不是重新设计布局。
移除GameHUD旧“倒下了/即将重来”遮罩。原实现误用raise/backgrnd/top（目录节点，且raise属于升级窗口），并只播8帧且永远不切land。
服务端经验/护符结算及确认后GameSession.Respawn链路沿用原有实现，本次未重写规则。
墓碑位置沿当前玩家脚底；原端空中死亡的多次foothold探测/横向±15选择尚未完整移植，不能声称所有空中落点一致。

复现：`tools/inspect_beidou_client.py <exe> --fn 0x92FD5A`、`--fn 0x897B5F`、`--fn 0x897C94`、
`--fn 0xA066F3`、`--start 0xA0343A --end 0xA0345E`；
`tools/imgdump.py Effect/Tomb.img fall --depth 3`、`tools/imgdump.py UI/UIWindow.img Notice --depth 2`。
验证入口 `NativeGameplayFixChecks.Run`检查实际IMG、600px起点、880ms落地、land及确认阶段，不代替Android实机或特殊复活道具验收。


### 本次实际验证（2026-10-30）

Unity批处理 `NativeGameplayFixChecks.Run`退出0：1009..1015全部原IMG选项、错误反馈/不交付/经验不变、正确翻页；绳底下方带向上分量不抬升及正常抓取；3010000/3010001效果及攻击/死亡/换图清理；1010000实际Portal_1原图（missingArt=0）；墓碑600px起点/880ms落地/land保持/2.2s后确认阶段，Notice0与BtOK2可加载。
`RopeJumpChecks.Run`退出0：绳/梯左右30/60/120Hz、无方向不脱手、0.5/1.3初速、朝向和未暂停jump动作。
compile-check Rebuild：0错误0警告；脚本包2779条、parseFail0/mangled0。Mock全量NPC运行889条790通过，parseFail0、runFail27、javaInterop72，非全量通过；这不是本次Say问答的验收替代。
没有Android实机或复活道具特殊分支验收。任务按键面板差异等待用户进一步说明，未猜改布局。摘要同时保存在 `docs/skill-regression/gameplay-fixes-2026-10-30.txt`。

## 2026-10-30 补核：墓碑与死亡角色不是同一个脚底锚点

二进制身份沿本文首段；新增Shape2D.dll身份及Rotate语义见custom-dll-systems.md“原生粒子与Shape2D证据”。
`92FF77..92FF84`创建角色死亡origin A=(x,y−20)，`92FFCC..93000C`令A Rotate(0,2000ms)；
`93003C..930073`把B的origin接A，`9300EB..9300F4`把B设为(−10,0)。
`93013C..930167`通过user+11A4→538A73→6D176B→6D174F把移动对象origin接到B；
6D176B只是非空指针减0xC的适配，不是另一个动画函数，6D174F写对象+1C。
Shape2D `51406FA2..51407045`对父旋转后的子坐标按正负各加/减0.5再转整数。
原地图y向下，Unity视觉层相对脚底为(−10 cos(πt),20+10 sin(πt))/PPU，旋转项先按上述规则取整。
`930187..930192`取now+2500；`9303A5..93047F`令主avatar alpha由0到255，后层同样处理。
此前仅令角色dead贴在墓碑脚底，会造成两者交叠，单改sortingOrder不能还原这个布局。

`930517..93056C`把fall层挂user+1150，传默认z=−1、alpha255；
`452195`以avatar+10C8取主图层，而user avatar基址为user+88，故这里确实是主avatar层。
`43EC41..43EC8F`读取Canvas z覆盖默认值；Tomb/fall帧z=0，不能将helper的−1误记为最终墓碑z。
Unity保留墓碑在角色之后绘制（SortingOrder+1）并跟随角色地图层，角色视觉独立悬浮、2500ms渐显；
复活/离开死亡流程时清零视觉偏移、恢复白色。墓碑继续使用各帧origin、600px/880ms和原20帧delay。
这不是空中死亡foothold落点探测的完整迁移；Gr2D所有overlay/alpha合成尚无逐像素oracle。
复现：inspect_beidou_client.py <exe> --fn 0x92FD5A / --fn 0x6D176B / --fn 0x6D174F；
相关Shape2D复现入口及身份见custom-dll-systems.md。InteractionRegressionChecks.Run新增实际角色视觉origin、半周位置、alpha端点和清理断言。

### 本轮验证范围（2026-10-30 第二批反馈）

隔离Unity工程InteractionRegressionChecks.Run退出0：真实Quest需求下怪物/全局任务掉落四阶段、普通0/−1掉落、实际NpcSession及InputBridge的Esc动作关闭与菜单不穿透、按上门原地松开/再按、触碰门防循环、HP/MP 700ms中点、死亡视觉origin/旋转/alpha及清理。
NativeGameplayFixChecks.Run退出0：包括原IMG墓碑600px起点、880ms落地、land保持和2.2秒确认阶段。
compile-check Rebuild为0错误0警告；脚本包2779条parseFail0/mangled0。全量NPC Mock运行889条790通过，27 runFail及72 Java interop缺口与此前基线一致，不能称全量脚本验证通过。
死亡层叠尚无EXE逐帧画面oracle；未打包APK、未做Android实机验收。EXP主条方案仍待用户选择，不以HP/MP通过代替EXP验证。


## 原地复活术5510000补充（2026-10-30）

EXE身份同本文首段。897C94..8981DD选择Notice/2（286×146），BtYes2(92,115)、BtNo2(148,115)，600秒超时否；8981E1按钮6/7→898224→53035D。客户端4A95BF特殊field门控仍有虚表身份未核实边界。服务端ChangeMapHandler90..118：持有轮回且请求使用则扣1，HP50、MP不变、当前图最近可用出生门；MapleMap3107限定pt0..1且tm999999999。DeathFlow、ChangeMapRequest/Protocol、GameSession.Respawn接入；死亡存档HP下限改0，避免保存成1而拒绝复活。原轮回与复活buff优先级、数量文字及Android交互未完成验收。证据docs/skill-regression/revive-item-dialog-2026-10-30.txt，业务回归inventory-items-isolated11-2026-10-30.txt PASS158；后续isolated20 PASS182亦覆盖该分支，不代替视觉/实机验证。完整逐条物品进度见inventory-use-and-equipment.md。
