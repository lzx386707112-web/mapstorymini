# 地面掉落与主动拾取（2026-10-28）

## 身份与入口

- `clien/BeiDou.exe`：SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。下述 EXE 地址均属此版本。
- `clien/Shape2D.dll`：SHA256 `5e9b27d392b4a373fcb21d8b188c8b6864c1fd60fc33563ae24c782b26d97e43`，ImageBase `0x51400000`。
- `gms-server/src/main/java/org/gms/util/PacketCreator.java`：SHA256 `2da68878b0c5127ca4e1a9afd7630925ebee91f5fc621981a066b6ca0df9b474`。
- `MapleDashResources/Data/Item/Special/0900.img`：SHA256 `e6179ba4d28e7126db13d935597885cb82312d449fed90ae4a5c691d5c79fdda`。

`0x5058DB` 按 opcode `0x10C / 0x10D` 分派到 enter `0x505900`、leave `0x506590`。`PacketCreator.dropItemFromMapObject:1826` 携带 mode、objectId、meso flag、金额或物品 ID、owner、dropType、最终坐标、dropper ID；mode != 2 时另带来源坐标和 short 延迟。mode 2 是直接出现在地面，不应重播抛出动画。服务端正常掉落先 mode 1，后续进入视野会发 mode 2。

## 已确定的原端状态与坐标

以下字段相对 drop 对象，不是 pool：

| 偏移 | 语义 |
| --- | --- |
| `+0`, `+0xC` | secured source Y / X，读取 `0x42873D` |
| `+0x1C`, `+0x1D` | enter mode / 已确认可见标记 |
| `+0x20/24/28/2C` | drop ID / owner ID / dropper ID / dropType |
| `+0x30/34` | meso flag / 金额或物品 ID |
| `+0x38/3C` | 图层 / 图层原点向量 |
| `+0x40/48/4C/50/54` | 开始时间 / 状态 / 累计 ms / 抛出阶段时长 / 上次请求时间 |
| `+0x60` | 地面悬浮相位 |
| `+0x68/74` | secured target Y / X |

- 坐标为原端像素、Y 向下。`0x505F0A..0x505F5A` 把 source/target Y 各减 `layer.height / 2`（整数）；`0x506054..0x506090` 把图层相对向量位置设为 `(-width/2,-height/2)`。
- `0x505F6E..0x505F8B`：z = `10*(foothold.layer*3000 - foothold.page) - 0x3FFF8AD1`，即角色/怪物记录中的 **K+9**，普通人物 K+2、普通怪物 K+1。不能继续使用本项目旧的掉落 order 290，把物品压到角色身后。
- `0x504BFF` 是 drop pool update；状态 0 等待开始时间、1 抛出、2 下落到更低平台、3 地面悬浮、4 特殊爆炸移除。状态 1/2 每次推进 **30ms**。
- 普通状态 1：`t = elapsedMs * .001`；`Y = round(sourceY - 400*t + 400*t²)`。X 为 `round(sourceX + (targetX-sourceX)/2 * (min(ms*.002,1) + clamp((ms-500)/(duration-500),0,1)))`，500ms 前第二项为 0。`0x505117..0x505229`；负数 round 是 `trunc(v-.499999999)`，正数 `trunc(v+.5)`。
- `0x5064FF..0x50658F`：若 sourceY <= targetY，duration=1000ms；否则 `min(1000,(trunc(sqrt((targetY+100-sourceY)*2.5))+1)*30+500)`。CRT `0xA62018` 是向零截断到 int64；无效 sqrt 的 integer-indefinite 低 DWORD 为 0，对应后续 530ms，不能把 NaN 直接当 1000。
- 状态 1 达时长后：sourceY >= targetY 就落地；否则状态 2 从 sourceY 开始以 **400px/s** 向下，碰到 targetY 截到落点（`0x504E9C..0x504FD4`、`0x50522F..0x50536D`）。**没有随机水平散射、重力2000、0.34恢复系数或两次弹跳**；这些是旧迁移代码自创注释，已删除。
- 状态 3 `0x504D98..0x504E62`：每 tick phase += `.0942477`，Y = `trunc(targetY + 3*sin(phase))`，X 固定。无呼吸缩放。
- 道具抛出时 `0x506142..0x506182` 对向量 Rotate(0,300ms)，落地 `0x505334..0x505340` 复位角度0。Shape2D 的 vector vtable `0x5140C0FC/0x5140C2F0`，`+0xA0 → 0x51408A50`；零角分支 `0x51408B08..0x51408B45` 是 `(now-start)%period * 2π/period`，确认是300ms每圈，不是补间到0。金币不走这个旋转调用。
- DropItem 音效在状态0开始播放，不是落地弹跳声；`0x505629..0x5056A2` 对 mode1 用全局300ms门控。PickUpItem `0x506753..0x5067A0` 只在 picker ID 为本地玩家时播放。

## IMG与金币帧序

`0x506BFE..0x506C23` 金币分档是 **<50、<100、<1000、>=1000**，不是旧实现的1000/10000/100000。

`0x506C26` 使用 string1717 格式 `Item/Special/0900.img/%08d`，ID为9000000+tier；string1589=`iconRaw`，最后取帧索引。`0x506E62..0x50813F` 将四张 canvas 按以下时长插入循环：

| 档 | 节点 | 帧0、1、2、3时长（ms） | canvas尺寸与origin |
| --- | --- | --- | --- |
| 0/1 | `09000000/1/iconRaw/0..3` | 80,80,80,80 | 23×24(-5,28)、25×24(-4,28)、23×24(-5,28)、5×24(-14,28) |
| 2 | `09000002/iconRaw/0..3` | 200,200,200,200 | 31×30(-1,32)、32×30(-1,31)、33×30(-1,29)、32×30(-1,31) |
| 3 | `09000003/iconRaw/0..3` | 4000,120,120,120 | 均32×31，origin(0,32) |

证据：`0x507B87/7CDC/7E31/7F86`=80，`0x507594`=200，`0x506F54`=4000，`0x5070AC/7201/738C`=120。没有 IMG delay 节点，不能自行统一成180ms；也不能漏掉第四帧。

普通道具 `0x505CD8..0x505E9F` 从物品数据取 iconRaw（缺失分支取普通canvas）；运行时复用 `NativeItemSource.Node` 定位 `info/iconRaw`/`iconRaw`，再取原icon作为资源回退。不得生成黄色圆圈/灰色方块替代缺失素材。

## 主动拾取与离场动画

- 键位50继续走已核实的 Pressed/Repeated，**不在角色每帧移动中调用拾取**。
- `0x50463C..0x504837` 普通拾取：脚点 `(x,y)` 矩形 `[x-25,x+25) × [y-50,y+10)`；遍历drop list，选择第一个满足可见标记、状态3、归属、重试门控的drop；不是按当前漂浮图标中心求圆形最近距离。`PtInRect` 包含左上、不含右下。
- 请求后 `+0x54=now`；同一个drop需要过3000ms才能重发。`+0x54`初始为now-3000（`0x50633F`），普通拾取还有物品阻止拾取查询；这些客户端请求限制与服务端400ms规则是不同层，不能混为一谈。
- 服务端 `Character.pickupItem` 的400ms、归属、满包、一次入库保持原流程，见 [solomapling.md](solomapling.md)。`removeItemFromMap` reason>=2 包含picker ID；pet reason5还含pet index。源端假人 botLoot 只移除，不授予物品。
- reason2：`0x5067A0 → 0x4360C3` 持有原图层并记录地面中心、picker ID、时间；`0x437991 → 0x44528F → 0x4416DB` 每帧更新。
- `0x441767..0x441795` 目标是拾取者当前 `(feetX,feetY - avatarLayerHeight/2)`，随人物移动更新；若人物不在场/不可见则终止动画。宠物目标单独读取宠物坐标，不能飞向本地玩家兜底。
- `0x4417DD..0x44181D` 总长 **700ms**；alpha在t<=420ms为255，之后 `255 + trunc((420-t)*192/280)`，700ms直接释放（末端不是渐变到0）。
- `0x44191F..0x441974`：`X=trunc(((700-t)*sourceX+t*targetX)/700)`，`Y=trunc(((700-t)*sourceY+t*targetY)/700) + trunc(160*(t-350)²/490000)-40`。两次整数除法分开，不能以float Lerp后统一取整。
- reason0 `0x5067C8..0x506B7D` 恢复原点/角度、给图层canvas安排1000ms到alpha0的离场；reason1静默移除。没有白点炸开或0.12秒缩小。

## 当前迁移与验证边界

运行时：`NativeDropMotion` 保留纯像素算式，`GroundItem` 使用原IMG、30ms状态轨迹、悬浮、旋转与700ms主动拾取动画。怪物掉落由 `FieldDirector.DropLoot` 显式传来源脚点、服务端 `CalcDropPos` 落点；`GroundItemBridge` 从已知dropper actor取来源；服务端离场携带pickerId，桥不再先 SetActive(false)。离场显示保留在All以便换图清理，但不再参与拾取/数量统计。预加载地面物品可通过 `alreadyOnGround` 入口直放。

当前边界（不能称为完整所有掉落类型1:1）：

- dropType3特殊720px/s支路、爆炸reason4、消失型spawn mode3、宠物拾取显示还未完整迁移；宠物/不存在的picker不会冒充本地真人。原端请求级3秒重试在本地同步服务模型中未加入额外等待；未接完整物品阻止拾取表。
- 角色图层当前由纸娃娃合成sprite对应，普通拾取目标复用其高度；特殊变身的完整avatar层差异仍受现有角色合成边界限制。
- 怪物掉落现有本地奖励路径仍为Profile入库；本次未把它另行改造为完整权威MapItem生成链。权威绑定掉落继续保留原有400ms/归属/背包验证。
- 金币及道具以原canvas第一帧居中建立向量，并保留后续frame origin差。2026-10-28 按用户明确指定的“金币高度正确，道具向金币对齐”修正落地基线：小金币原图origin.y=28、height=24，对应脚点上方4px的canvas底边；普通物品复用这条基线。金币各档保留各自第一帧origin.y-height的间距，不再被居中补偿降到脚点。此为用户指定的迁移显示对齐，不宣称Gr2D全部canvas边界计算已独立复刻。声音空间衰减沿用现有AudioManager适配。
- 普通每帧tick在本项目由实际elapsed/30计算，不依赖Unity帧率；极端卡顿后的补tick方式与原端调度器未独立实机比较。

复现：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x504BFF --end 0x5056AF
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x4416DB --end 0x441A9F
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/native_drop_oracle.py ../../Beidou-Server/clien/BeiDou.exe --output tools/native_drop_oracle.json
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -executeMethod GroundItemChecks.Run -logFile /tmp/mapledash-ground-item.log
```

`native_drop_oracle` 执行原EXE的72组拾取整数轨迹，另执行48组普通抛出x87计算（仅替换secured-int getter，CRT原样执行）；固定身份结果保存在工具旁JSON。Unity检查比较原指令结果、真实四档四帧和装备IMG、矩形边界、下层落地、人物Step不自动拾取、失败不移除、成功同步事件仍显示、动画期间不重复请求、无缩放/FxLife。服务端回归537通过/0失败；编译检查通过。尚未Android实机与原端并排录屏验收，不能据逻辑回归声称全部视觉边界已验收。

### 道具/金币高度错位补查与回归

旧提交的`ItemSprite`使用`pivot=(.5,.5)`，金币却走保留IMG origin的`WzAssets.Sprite`；相同transform落点会让32px道具下沉16px，小金币底边却在脚点上方4px。上一轮统一居中虽消除了混用轴心，但也把金币底边降到了脚点，未满足用户指定的金币高度参照；本次补上由金币原图数据计算的clearance，没有手写整体抬高像素。

补查DLL身份`Gr2D_DX8.dll` SHA256 `ab1c942229af33da04d913ed1157ab322bff0812f7368bcf745d64b98698d34d`：`0x50404195`为CreateLayer实现，`0x5040422F..4242`转入`0x5040992B`初始化；`0x504067E0`写layer vtable `0x5042E3F0`。其+B8/+C0分别为`0x5040A12F/0x5040A208`，读取layer+0x40尺寸向量，经`0x5040D47B/D4A2`取向量宽高，不能直接把任意IMG texture尺寸称为已核实的layer尺寸。`0x5040ABEF` InsertCanvas还调用`0x5040B26C`切帧调整，完整origin/尺寸联动仍待核实；不能据此宣布原端全部层边界已还原。

`GroundItemChecks.Run`新增真实IMG的帽子1002000、药水2000000、蜗牛壳4000000与小金币，在同一脚点、相同悬浮相位0/240/600/1200ms检查SpriteRenderer实际世界边界底线一致；另独立断言金币初始底线为脚点上方4px。这是几何回归，不是Android截图或GPU像素验收。

本轮实际Unity batch检查通过（退出码0）：上述12组基线比较、金币4px参照断言及既有72组拾取/48组抛出oracle回归。奇数canvas高度另补齐整数halfHeight与Unity浮点center间的半像素差，防止药水出现0.5px下沉；未改IMG像素。
