# 皮奥任务箱子与 Reactor

2026-10-29。本模块覆盖地图1000000、Reactor2001、任务1008；不代表全服所有Reactor事件已迁移。

## 原文件身份

- BeiDou.exe：SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。
- Map/Map/Map0/001000000.img：`c79edc6ae6dd6a752d5999383317e745df12386bb5ff6823ebf765603bbeaf6d`。
- Reactor/0002001.img：`8982f4bcc290074b150baaf081f8437612f8f920d42289cd48e6d264fcef0705`。
- 源服 scripts-zh-CN/reactor/2001.js：`b294280bec13376beaa124b9fed824feac5c5277fce1eba25f7bf6ffb3642df8`。
- org/gms/server/maps/Reactor.java：`204d12f0fe694085dd8b8cb55098b6e6eb39e868e14f5f322221c6f7222cf66d`。
- org/gms/scripting/reactor/ReactorActionManager.java：`fb9eaeccc3f9eef66b5cf9638d3143fad1beb84ad093914a4b051af799187d0c`。
- V1.0.58__reactordrops_insert_data.sql：`694f10739cbbdf3c1369eecb50e85db9805b21d180630361340566f9a771d018`；V1.0.59__reactordrops_update_quest_reactor_items.sql：`8a4f17539d9a845b7fcac9bc064e8668f487d1510afc17066de4da701dea5f36`。

## EXE 普攻选箱证据

`7356C7..735AB6` 遍历Reactor状态事件，`73578C..735797`接受type0..4；当前迁移仅使用2001的type0。
`7357C8→440C54`读绘制层的左上向量，`735867→439DCB`读右下向量；包装分别调用层虚表+C8/+CC，
随后通过向量虚表+8C把两个向量写进连续RECT。`735925→95719B`把候选矩形交给真人普攻选择，
返回索引后`735A13`创建0xCD包，带oid/姿态/延迟/skill0；不是任意半径自动伤害。

`95719B..95782B`为这条选箱调用的真人普攻执行入口：`95749B..9574BA→414440`按当前武器与动作取得攻击RECT；
`9574E8..957509`在朝右时翻转左右并交换，`957513`把RECT偏移到角色脚点；`95755D..95757A`排空矩形并调用IntersectRect。
`414440`从动作数据读取RECT（动作数据每项0x30字节，`414499..4144A6→4144D4`读取四个分量）；
不能用残影PNG透明边缘或固定距离代替。资源例：`Character/Afterimage/swordOL.img/0/swingO1/{lt,rb}`为(-88,-62)/(-18,-6)，
`proneStab`是(-74,-11)/(-15,1)。`414456`的特殊动作号0x37不是proneStab，禁止把其固定框套到蹲攻击。

复现使用项目`tools/inspect_beidou_client.py <exe> --fn <上述入口>`及`tools/imgdump.py Character/Afterimage/swordOL.img 0/proneStab --depth 2`；
必须使用原始指令、递归下降入口，不能根据失步的线性反汇编推断字段。

## 地图、素材与服务端状态

原图5个2001箱子：(610,254,90秒)、(324,-78,100秒)、(1529,132,90秒)、(231,253,180秒)、(1947,-167,90秒)，f均0。
箱子state0站姿64×45，origin=(33,23)；state1/2/3站姿UOL引用state0；hit1/2引用hit0，两帧各150ms，末次破坏hit七帧各150ms。
state4是1×1透明图，事件state0→1→2→3→4。布局使用原地图像素坐标和Canvas origin，不附加脚底偏移。

源服`ReactorHitHandler.handlePacket→Reactor.hitReactor:383`锁定活动实例，type0推进event.next；**赋新state后再次查next**，
next<当前state表示结束。2001进入4后next=-1，`MapleMap.destroyReactor→Reactor.destroy/delayedRespawn`置dead，按reactorTime毫秒排重生，
重生reset为state0。原`CustomReactor`及OPQ专用四击代码是另一路，不能用新普通玩家入口替代这些显式掉落路径。

## 原JS与SQL掉落、拾取

2001.js原样执行`rm.dropItems(true,2,8,15,1)`。ReactorActionManager:230先1/2概率金币，再逐行用玩家dropRate/chance判定，
不足minItems用金币补齐；物品数量均1。SQL八行：4031161/4031162各chance1、quest1008；2010009/2、2010000/4、2000000/4、
2000001/7、2000002/10、2000003/15，后六行quest=-1。Quest/Check.img/1008要求两种任务物品各1。

ReactorActionManager:88把普通/当前需要的任务/其他任务条目分组三次shuffle；偶数下标组倒序后接奇数组。
dropItems:152喷出起点=reactorX-(条目数为偶数?12:0)-12×条目数，随后每件+25px；TimerManager.register(r,200)初始delay=0、周期200ms。
金币=(int)((random×(max-min)+min)×世界mesoRate)，按NumberTool.doubleToInt封顶Int32.MaxValue。
物品走MapleMap.dropFromReactor/spawnDrop，**questid须在Spawned广播之前写入MapItem**；真人按Character.needQuestItem筛显示与拾取。
未接任务只允许start item requirement；进行中按complete item count，完成后拒绝。没有对应complete要求时原服返回Int32.MaxValue。

迁移：`NativeReactorDrops`从SQL导出JSON读取八行，不在C#写任务掉落常量；`NativeReactorScriptService`仅装配五参数宿主，不改JS。
`MapReactorService.HitPlayer`保留原type0状态转换；`PlayerCtrl`使用现有普攻前摇结算一次命中。
`NativeReactorView`解析hit根UOL、播原hit后隐藏dead实例；复活重置站姿。
`GroundItemBridge`使用任务筛选、原箱子坐标作为掉落起点；权威拾取成功后通过GameSession同步背包镜像及金币增量，避免数据库已有道具但任务面板仍显示未收齐。

## 验证与边界

定向入口`NativeReactorChecks.Run`：隔离Unity项目、临时DB及内存Profile，真实地图/武器/Canvas/UOL、背向/远距拒绝、PlayerCtrl前摇四击、
原JS执行、200ms喷出、任务标签、真实GroundItemBridge拾取/重复拒绝/任务数量封顶、DB重读、Profile同步、Quest.wz交付及90秒刷新边界。
最终结果记录在`docs/skill-regression/reactor-checks.txt`。不构建APK、不声称Android触控或全服Reactor验收。

当前只迁移2001掉落表和原五参数dropItems；其他重载/未导出的表显式报错。type2方向、activeSkill、type100及其他事件完整状态机不是本次完成范围。
普通攻击范围继续使用当前实际动作选择和原lt/rb；原EXE所有随机武器动作/网络时延不属于此次专门验证。
