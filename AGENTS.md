# 项目协作说明

## BeiDou 逆向知识 skill

分析或修改原端相关功能前，先读 [beidou-client-reverse](.agents/skills/beidou-client-reverse/SKILL.md)，按模块复用已确定的函数、资源与逻辑。逆向有新发现或纠正旧结论时，在本次工作结束前更新相应 reference 及索引；区分确定逻辑、待核实线索和当前迁移状态，避免每次重新逆向。完整要求见上级 `AGENTS.md` 第 7 条。

## 地图与角色迁移 skill

处理原版 WZ 地图迁移、地图显示比例与相机、人物脚底锚点、平台碰撞或绳梯动作时，先读取项目内的 skill：

[mapledash-map-port](.agents/skills/mapledash-map-port/SKILL.md)

按任务需要读取该 skill 引用的实现约定和迁移流程。其他 IDE 即使不支持自动发现 skill，也可直接读取这些 Markdown 文件。

本机额外 shell 约定在 `/Users/lizixian/.codex/RTK.md`；该文件存在时遵循，其他环境使用当地可用工具。

## NPC 脚本与任务系统

改 `Assets/Scripts/Scripting/`（内嵌 ES5 解释器）、`Assets/Scripts/Npc/`、`Assets/Scripts/Quest/`
或动源端脚本时，先读 `docs/original-gameplay-port.md` 的「NPC 脚本与任务系统」一节。

三条硬约束：

1. **脚本原样执行**。源端 `BeiDou-Server/gms-server/scripts-zh-CN/{npc,quest}/*.js` 是唯一事实，
   不要在 C# 里重写脚本逻辑，也不要改脚本内容。改完脚本跑
   `python3 tools/sync_npc_scripts.py --clean`（同时产出 `Data/scripts/**` 与
   `Assets/Resources/Scripts/npc_scripts.json`）。
2. **`Scripting/` 不许引用 UnityEngine**。这一层要能被 `tools/script-harness` 用 dotnet 单独编译，
   也是 IL2CPP 上唯一能保证不炸的写法（不靠 JIT/反射/第三方 JS 引擎）。
3. **任务数值只从 `Quest.wz` 读**，不要手写等级/奖励常量；语义以服务端 `Quest.java` +
   `requirements/*` + `actions/*` 为准。

改完必须过的两道验证：

```bash
cd MapleDash/tools/script-harness && dotnet run -c Release -- --run   ../../MapleDashResources/Data/scripts/npc
cd MapleDash/tools/script-harness && dotnet run -c Release -- --pack   ../../Assets/Resources/Scripts/npc_scripts.json
cd MapleDash/tools/unity-compile-check && dotnet build -t:Rebuild      # Assets/Scripts + Assets/Editor，0 error 才算过
```

## 机器人（Android）玩法

动 `NativeAndroid*.cs`、`Wz/AndroidLook.cs`、`NativeInventoryService.EquipmentSlotOf`、
`EquipLoadout` 的槽位表，或往 `Assets/Resources/Bot/AndroidTemplates.json` 加模板之前，
先读 `docs/tms282-skill/11-android-migration.md`（§8 是真机走查清单）。

五条硬约束：

1. **282 客户端 img 是 BMS 密钥，本项目读 GMS 密钥**。新增 282 资源必须先用
   `tools/ms282/bms_to_gms.py` 等长 XOR 转码（偏移不变，间接字符串仍然有效），
   再用 `tools/ms282/import_androids.py` 按 `_outlink` 闭包导入。
2. **机器人本体 -32（`islot "An"`）、心臟 -54（`"Ht"`）不做 `slot-100` 现金槽换算**。
   282 把两者标了 `cash=1`，但 BodyPart 还是 53/54，走现金换算会变成 -132/-154。
3. **AP 时装槽是 1200..1207 + 1210..1213**（1208/1209 是空号，APRing 从 1210 起）。
   判断用 `NativeAndroidService.IsApSlot`，别自己写区间。
4. **外观合成只能 `WzCharacter.Register(key, ids)`**，不要把机器人的 id 灌进职业配方表；
   `ids[0]` 必须是 body、最后一项是 weapon（`Pose()`/`Placements()` 按这两端取动作）。
5. **心臟卷轴（2616/2643 段）的适用范围只认 `req` 与 `reqScope`，且必须服务端判**
   （`BotItemCatalog.RequiredEquips/ScopedTo`）。客户端传的 `scrollRequirements` 只是副本，
   GM/脚本/老客户端不会带。100% 成功的卷照样扣 1 次 tuc（源端 `useTuc=!reset`）。
   心臟格 -54 **不在装备窗底图里**，砸卷要拖到 `NativeAndroidWindow` 的「心臟」格。

改完必须过：

```bash
cd MapleDash && python3 tools/ms282/check_android_look.py   # 248 个模板逐个验证能拼出纸娃娃
cd MapleDash && python3 tools/ms282/import_heart_scrolls.py  # 心脏卷轴图标链（dry-run，不写盘）
cd MapleDash && python3 tools/check_scroll_effect.py         # 砸卷成功/失败特效应与音效链
cd MapleDash && dotnet build tools/server-check/MapleDash.ServerCheck.csproj && dotnet run --project tools/server-check --no-build
cd MapleDash && dotnet build tools/unity-compile-check/Check.csproj
```

