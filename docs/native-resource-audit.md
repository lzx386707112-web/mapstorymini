# 原版 IMG 显示审计（2026-10-22）

目标是当前 BeiDou.exe + ijl15.dll 的实际行为，不以经典旧版截图替代，也不以“加载了原图”代表界面已还原。

## 已确认与修改

- 加载背景：用户提供的 aa.jpg，打包为 LoadingBackground.bytes；底部固定进度区域，按 100ms 更新百分比。
- 启动：不创建 Lobby，直接进地图，职业新手；不清空等级、经验、金币和物品。
- 手机虚拟按键：按用户要求保持现有实现，属于明确保留的操作覆盖层。
- 图像管线：地图 NativeMapView、人物 WzCharacter、怪物 WzAssets.MobClip、UI WzAssets.Texture 直接读取外置 Data。Resources 中没有旧转换 PNG/JPG；LoadingBackground.bytes 是指定例外。
- 清理：移除旧 Art 下空目录和孤立 meta、无引用的鼠标 manifest；原 Art/manifest 中运行时使用的 stage/damage 等配置保留为 NativePresentation.json。旧人物/怪物帧表只保存在 Editor/Fixtures/LegacyArtManifest.json 作回归依据，不进入 Player。
- 人物：原版纸娃娃部件合成；取消自定义衣服染色层。人物、怪物资源缺失不再显示方块。怪物不再套用自定义精英颜色。
- 能力值、装备、背包：直接使用 UI/UIWindow.img/Stat、Equip、Item 的底板；装备图标读 Character/.../info/icon。这里只迁移现有数据，不代表装备槽、AP、商城等完整业务已移植。
- 技能：底板、图标、行背景、加号、冷却和页签来自 IMG；倍率统一到 HUD，标题恢复为职业名，加号 x131、首行 y119、行距40，依据 exe 0x008AAD3C–0x008AADA8。新手技能从 Skill/000.img 与 String/Skill.img 读取后纳入轻量配置。
- 能力值文字坐标依据 exe CUIStat::Draw 0x008C59FF：x61，y33/55/79/96/115/133/151/169；四属性 y244/262/280/298。原版加点按钮位置 x153，y117/135/247/265/283/301；详情按钮 (124,324)。尚未实现的加点显示原版 disabled 态。
- 鼠标：读取 Basic.img/Cursor，缺失状态回退0；桌面通过 Cursor.SetCursor，手机继续绘制原版触控指针。GameHUD 每轮重置自身 GUI.depth，避免被其他 OnGUI 行为的深度污染。

## 地图 40001 的明确错误

旧实现把 tile.zM*100 与 obj.z 直接混排，遗漏 tile artwork.z 及两种对象的不同深度基数。
原端 weather-compat/src/weather.cpp 中对 exe 的记录：

```
tile: artwork.z + 10*(3000*layer-zM) - 0x3FFFB1EA   // 0x0063A91F
obj:  obj.z + 30000*layer - 0x3FFFF830               // 0x0063C289
```

当前按此公式全局稳定排序。Map/Obj/acc1.img/midForest/artificiality/1 是木桩，本应局部被斜坡遮住；错误排序使其矩形范围暴露。实际运行截图已验证该处恢复遮挡，不是删掉物件或补画遮盖。

## 验证及边界

- verify_direct_img.py：24 IMG，8556 Canvas 全部 RGBA 哈希与 wzpy 一致，格式1/2/513。
- verify_native_characters.py：158人物动作帧 RGBA 与脚底相对位置一致。比较基准为源端 compositor，不是北斗运行画面。
- NativeUiReview：实际 Play 直接进地图/新手/无 Lobby，原版窗口、按钮、技能及鼠标节点可加载；导出实际 IMGUI 截图。
- Android loader 独立 UNITY_ANDROID 编译通过；本轮未重新构建 APK、未真机验证。
- 不宣称北斗实际 UI 已逐像素完全一致。HUD 分辨率缩放、聊天、快捷栏分页、工具提示、窗口行为和全部客户端动态绘制尚未逐项与正在运行的北斗画面对比。
- EnemyUnit 的普通怪物血条仍是程序绘制，不是 IMG；需要按原端普通怪物血条绘制逻辑迁移，不能拿 Boss MobGage 图伪装成普通怪物血条。
- 地图主体当前仍统一置于角色后方；人物/怪物与不同地图层的动态遮挡未完整迁移。这次修正的是地图 tile/obj 内部排序。
