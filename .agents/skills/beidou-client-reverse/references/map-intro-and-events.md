# 地图过场与事件实例（2026-10-29）

## 身份与原端证据

BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，base0x400000。
Shape2D.dll SHA256 `5e9b27d392b4a373fcb21d8b188c8b6864c1fd60fc33563ae24c782b26d97e43`，base0x51400000。
原文件均位于 Beidou-Server/clien；服务端位于 gms-server。

showIntro 发 SHOW_ITEM_GAIN_INCHAT subtype0x12，EXE `9386D7..93877F`。
调用 `439E00` 解析器时 basez=0xC00614A4，默认x/y来自玩家+11A4向量（403CB7/+20、403CDE/+28）。
`439E00..43A8C1`：type+0、visual+4、sound+8、start+0C=now+start；
type0 x/y在+10/+14，dx/dy是加法偏移，x1/y1在+18/+1C，duration+24，z+20=basez+节点z。
type2 field+38；type1有duration/width/height/probability；type3装备appearance1..51；type4 action；type5 sound；type6 x。

执行入口是 **43A8C4**，不是43A8C2；严格 now>start。type0→43ABDE、type2→43AA44→53035D换图、type5→43A935声音。
visual容器选择**一个随机数字子节点**作为帧属性传给4398F6，不是对子节点再循环生成多个图层。
4398F6→43EA3E 创建无origin/overlay图层，世界坐标；兼容源码
`tool/client-debug/weather-compat/src/weatherfx.h`103..113也明确无origin/overlay由引擎随camera移动。
初始x/y经439DA0→向量vtable+44；定时x1/y1经43AE59→vtable+94，结束时刻now+duration。

Shape2D向量vtable5140C0FC：+44→514075C9，无aux时把x/y加到向量+1C/+20；有aux转514075F5。
+94→514083CF..5140844D，无时间也做加法；有时间创建aux51407FE1→5140833B入队，
aux+10/+14存参数，+18/+1C为基准/结束时间。因此x1/y1是**位移**而非绝对终点。
aux构造5140806D设置vtable5140D8FC，+8→5140809F更新函数：51408122..5140813E使用32位imul/idiv，
位移=distance*(now-start)/(end-start)，向零截断；elapsed<=0不动，结束时直接加完整位移。
当前运行时已按此整数插值修正，并覆盖正/负非整数进度及端点断言。
复现：`tools/pe_module.py <Shape2D.dll> 514083CF`、`... 514075C9`；EXE用inspect_beidou_client.py --fn。

## 原始 IMG 与迁移

Effect/Direction3.img/goAdventure/Scene0、Scene1（男/女）：
cmd0 start0 y=-511，y1=722，duration10000，男/女visual；cmd1 start10000 y211同visual；
cmd2文字start10000 (0,0)；cmd3 type2 start14200 field10000。
地图0 VR=(-400,-300,400,300)，return10000，onUserEnter=goAdventure。

goLith/Scene0、Scene1：back1 start0 x1=50 duration3000 z3；cloud200/z2；light300/z3；shine0/z2；
男/女start0 (-400,200)，位移(400,-200)，duration4500/z3；Whistle4000；文字4500；field104000000/start5000。
图片按原canvas origin/delay/alpha，不自绘、不缩放；过场拥有输入锁并在换图/换角色清理。

NativeIntroTimeline、NativeMapIntro 接入type0/2/5，启动前预检全部图片/声音；其他类型显式拒绝。
NativeMapScripts 路由goAdventure/goLith，NpcApi.showIntro走相同内核。
地图0只有成功启动原过场后才关闭旧returnMap快捷兜底。
当前Unity sorting映射500+z用于过场高于地图/角色；原端全局z排序到Unity的完整映射仍未验证。
`tools/export_sound.py --direction-only` 从原Direction*.img导出20个原MPEG，SoundIndex新增原路径键。
原始音频在兄弟MapleDashResources/Data/Sound/Direction；可用该脚本重建。

## EventManager / EventInstanceManager 合同

源端Java同名类：每manager共用一个脚本engine；setup(level,lobbyId)→设置leader→注册玩家→playerEntry→afterSetup。
注册必须先建立membership；playerUnregistered在移除membership之前调用。
startEventTimer发秒数时钟，timeout先dismissclock再scheduledTimeout。
dispose回调先于disposed标志，之后清玩家/定时任务/地图。
**em.disposeInstance仅延迟释放manager中的名字/lobby，不等于eim.dispose**；
SQL V1.7.0__create_game_config.sql event_lobby_delay默认10秒。
getInstanceMap必须独立map manager，不得返回全局共享map冒充隔离。
resetPQ→resetMapObjects→clearMapObjects/restoreMapSpawnPoints/instanceMapFirstSpawn(difficulty,isPQ)，
包含reactor等状态，不能简化成清全局怪。
monsterKilled(mob,eim,hasKiller)在移除登记怪后调用；最后一个触发allMonstersDead；
monsterValue(eim,mobId)回调结果累计角色击杀计数。这些生命周期目前未接线。

NativeEventScriptService 已接原脚本engine、属性、单角色startInstance、membership、
setup/entry/afterSetup、换图回调、schedule/cancel、事件计时/超时、dispose及延迟释放名字。
NativeEventScripts 按角色维持宿主，NpcApi cm/player/client暴露真实事件对象；
player.changeMap修正为使用捕获session，脱离NPC会话也能换图。
未支持API严格报错，不以空实现假成功。

**运行时独立副本地图尚未接通**：InstanceMap委托刻意未配置，getInstanceMap明确报缺口；
时钟包只有宿主回调，原图时钟UI尚未迁移。组队/远征队、lobby信号量/超时、掉线/死亡/怪物登记回调、
交通事件channel/map/船只状态API仍缺；Cabin.js与Boats.js确实存在，不可宣称全部事件脚本已可运行。

## 验证范围

script-harness selftest 28 passed/0 failed：原3rdJob_warrior.js不改源码，
测试宿主提供独立map fixture，覆盖进入/1200秒计时/超时退出105070001/销毁；
两个实例map handle独立；回调顺序、重复注册、取消任务、未知API报错、10秒后释放名字。
这不是Unity独立副本地图已可用的证据。
NativeGameplayNodeChecks 在隔离Unity exit0：男女goAdventure原图尺寸800px、严格起点/14200ms换图、
中途位移、输入锁和图片清理；男女goLith初始并行图层和声音索引；540飞行oracle。
NPC全量889脚本：769成功/47既有runtime fail/73既有JavaInterop，不是全通过；
资源包2779条/npc889/quest470/parseFail0/mangled0；未构建或验收APK。

## 2026-10-29 宿主与审计补充

EventManager原属channel，不应因没有渲染中的field就返回null。NativeEventScripts改为独立 `[NativeEventScripts]` 宿主（Play时DontDestroyOnLoad），换角色清理；事件引擎复用NativeScriptService的GameConfig/Extend/Gachapon显式Java注册，getChannelServer通过委托注入NpcApi真实channel桥。
getTransportationTime已按EventManager.java1262→World.java441→NumberTool.java36接实际world_prop.travel_rate（float乘积、向零截断/Java夹取）；未知world显式报错。未接交通船只状态、独立地图及可视时钟仍保持原缺口。

上节889/47/73为旧Mock统计，不能视为120个已确认游戏故障。最新正式宿主审计为889/829通过/60失败，原120项106通过14失败；固定地图1000000、无事件上下文，仅首条交互路径。selftest现40通过0失败（含交通倍率与原warrior独立fixture），不等于Unity独立副本或Android已验收。详见npc-dialog与docs/script-api-audit.md。
