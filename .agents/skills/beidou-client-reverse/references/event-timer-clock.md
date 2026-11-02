# 事件倒计时（CLOCK）面板（2026-11-02）

结论只对应 `clien/BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`（地址均为 VA）。复现入口 `tools/inspect_beidou_client.py`（`--fn` 以外的大函数建议用 `--start/--end` 分段核对指令边界）。

## 包与服务端

- `SendOpcode.CLOCK`：`PacketCreator.getClock(Number time)` 首字节 `2` + int 秒 = 倒计时；`getClockTime(h,m,s)` 首字节 `1` = 当前时刻。`removeClock()` → `STOP_CLOCK` 首字节 `0`。
- `EventInstanceManager.startEventTimer / restartEventTimer / dismissEventTimer` 是唯一来源；restart = stop + start，所以加时也是整包重发，客户端只要按新秒数重新本地倒数。

## 图层与素材（已核实）

| 项 | 值 | 依据 |
| --- | --- | --- |
| 建层 | `CreateLayer(0x10F, 0x1E, 0x102, 0x3A)` = (271, 30, 258, 58) | `0x5362A9 push 0x3A` / `0x5362AB push 0x102` / `0x5362B0 push 0x1E` / `0x5362B2 push 0x10F` → `call 0x9DE4D2` |
| 底图 | `Map/Obj/etc.img/timer/backgrnd` 258×58 | 描述符 ctor `0x52BF8C` `push 0xB33`（字符串池 2867） |
| 小时底图 | `Map/Obj/etc.img/timer/backgrndhour` 258×58 | `0x49A5xx`（hour 分支内取字符串池 3787） |
| 数字字模 | `Map/Obj/etc.img/clock/fontTime/{0..9}` 各 26×35 | `0x52BFB9 push 0xADF`（字符串池 2783） |
| 冒号 | `clock/fontTime/comma` 17×35；`am`/`pm` 22×20 | 只给 CLOCK type 1（当前时刻）用，倒计时不用 |

x=271 正好是 `(800-258)/2`：**原版按 800 宽画布居中**。北斗把客户区放宽到 1281×578 后这条常量没有跟着改，所以按设计意图理解为「顶部居中、距顶 30px」，与 `NativeBossHpBar` 的 800 空间口径一致。y=30 是图层顶边，不是底图内边距。

## 绘制（`0x49A180` 起，`ebx` = 计时器对象）

- `[ebx+0x6C]` = 是否启用；`[ebx+0x80]` = 总秒数（`0x49AE6A` 写入）；`[ebx+0x7C]` = 结束时刻 = `GetTickCount()`（`[0xBF060C]`）+ 秒×1000（`0x49AEEE`，仅在 `[ebx+0x78]==0` 时启动一次）。
- 底图在图层内画在 `(0,0)`：`0x49A2AF/0x49A2B2` 两次 `push 0` 后走 `vtable+0x80`。
- 满 1 小时换 hour 底图：`0x49A196 cmp eax,0xE10`（3600）/ `0x49A19E jge 0x49A53A`。hour 分支显示 `HH 小时 MM 分`，否则 `MM 分 SS 秒`。两种底图上的「分 / 秒 / 小时」都是**印在画布里**的（countdown：分≈x152..162、秒≈x237..246；hour：小时≈147..172、分≈234..246）。
- 四个数字槽的 x 存在计时器对象 `+0x8C / +0x90 / +0x94 / +0x98`，来源是描述符 `+0x1C..+0x28`：`0x52BFE5`=`0x56`(86)、`0x52BFEC`=`0x70`(112)、`0x52BFF3`=`0xB3`(179)、`0x52BFFA`=`0xCD`(205) —— 即「高位十位 / 高位个位 / 低位十位 / 低位个位」四格，字宽 26 时 86+26=112、179+26=205 正好相邻。
- 数字 y = `0x0C`(12)：hour 与 countdown 两条支路都是 `push 0xC`（`0x49A6FF / 0x49A789 / 0x49A813 / 0x49A89C`）。35 高的字在 58 高的底图里上 12 下 11，和底图「分」的墨迹底边对齐。
- **前导零不画**：`0x49A30C test esi,esi / je`（分钟十位为 0 跳过）、`0x49A6AF`（小时十位）。个位与后两位无条件画，所以 5 分 30 秒显示成「5␣分 30 秒」，不是「05 分」。
- 描述符 ctor `0x52BF42` 的其余字段：`+0`=1、`+4`=0xFFFFFF、`+8`=-1、`+0xC`=0xE、`+0x10`=1。**倒计时建层前把 `+4` 改成 `0xFF000040`、`+8` 改成 `0xFFFFFF20`（`0x53625F/0x536266`）**：字模本身已是奶油色（`0xFFFFCC`），这组「深蓝底 + 亮黄」到底是描边还是调制色**尚未核实**，运行时按素材原色直绘，不自行着色。

## 迁移状态

- `Assets/Scripts/NativeEventClock.cs`：底图 + 四槽数字 + 前导零规则 + 3600 秒切换 hour 底图 + 停表规则（本地数到 0 自收，源端到点由服务端发 STOP_CLOCK）。
- 版面验证：把 `timer/backgrnd` 与 `clock/fontTime/3,0,4,5` 在 86/112/179/205、y=12 合成后与底图上的「分/秒」对位正确（`tools/imgpng.py` 导出的是 2 倍图，别把 516×116 当成画布尺寸）；hour 底图同样对位。
- 未迁移 / 未核实：CLOCK type 1（当前时刻，`am/pm`+`comma` 那条支路）；`0x545C8B` 里按 `0x4065F3` 返回类型 2 建的另一版计时器（`CreateLayer(0x124, 0x1E, 0x102, 0x49)`，258×73）用哪套素材、与 258×58 版的区别；`+4/+8` 两个颜色字段的实际用途。均无 Android 实机对比录屏。

复现：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x52BF42
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x536210
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x49A180 --end 0x49A2F0
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/imgdump.py ../MapleDashResources/Data/Map/Obj/etc.img
```
