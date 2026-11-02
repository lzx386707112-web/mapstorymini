# 聊天 Enter、商城宠物与地图跟随验证（2026-10-31）

## 本次修改

- 屏幕 Enter 直接排队到聊天绘制阶段，物理 Return/KeypadEnter 接同一消费入口，避免虚拟键边沿在 EndFrame 清空；打开和发送关闭各消费一次，保留 NPC/模态输入所有权。
- 聊天绘制移出底栏贴图的早退分支；Android 使用 TouchScreenKeyboard，尚无连接设备验证输入法差异。
- 宠物 getPet 等无物品ID操作不再被普通物品校验误拒绝；地图依据权威召唤状态创建/回收实际原IMG精灵。
- 跟随接原端追赶/停步距离、主人移动跳跃参数、平台/斜坡/墙、不同foothold group上跳、绳梯/脱离等待、距离回位及三宠编队；服装与宠物共用动作帧。
- 宠物装备按原端专用负槽位及原IMG兼容表处理；金币磁铁和道具袋接实际拾取入库与reason5飞向宠物。
- 此前商城存取修复补齐pets/petignores与cash_items.petid，取出刷新权威背包，持久化保留现金道具身份。

## 已执行验证

- 隔离Unity 2022.3.62f3c1：NativePetSceneChecks **41项通过**，进程退出0。日志：scene-checks.log。
- 覆盖真实宠物使用、查询、原IMG精灵、追赶停步、服装兼容/穿脱显示、磁铁/道具袋本地入库、卸下停止拾取、上层台阶、主人正下方不同group起跳、绳梯、200ms脱离、回位、切图重建、真实VirtualControls.SendButton→EndFrame→OnGUI聊天开关、收回、三宠编队。
- 另走实际GroundItemBridge与MapItemPickup：权威金币及Profile各增13、petIndex/拾取者身份、动画回调目标为对应宠物、拒绝他人物品归属。
- 商城/存档等NativeSystemChecks离线定向检查 **78通过，0失败**，包含宠物购买/存入/取出/重开存档同petid和亲密度。
- dotnet Unity编译检查 **0错误0警告**；最终场景脚本另由Unity实际编译并运行通过。
- pet-clothing.png已目视确认原宠物及服装叠加。chat-enter.png为隔离场景焦点证据；批处理GameView尺寸/位置影响捕获，不能当正式整屏布局验收。

## 尚未完成 / 未验证

- Android adb devices无连接设备；未做APK实机Enter、输入法、触摸或宠物路线验收。
- 不声称完整原EXE逐帧1:1：完整平台连接端点控制、随机加速、飞行/水域特例、全部互动动作仍未全迁移。
- 自动HP/MP药品绑定与警戒设置、扩大范围/扫落物寻路、过滤配置、自动Buff/异常治疗未完成；能穿戴不等于能力生效。
- 不修改用户正式存档；场景检查使用临时隔离DB、Profile.debugOverride，结束清理。

## APK交付

- Unity IL2CPP / ARM64 构建成功，212秒，0错误。2026-10-31 11:49:38 +08:00。
- `Builds/MapleDash.apk`与另存的`Builds/MapleDash-pet-chat-20261031-1149.apk`，41,355,830字节（39.4MiB）。
- SHA256 `97dadb23141fa96f375849b0cd59ad94c4e230bb3617f7e77ff79e282c81b932`。
- apksigner verify退出0，ZIP完整性通过，包含lib/arm64-v8a/libil2cpp.so。
- 原Data资源按现有工程部署机制独立部署；此APK未把完整Data嵌入包内。
- 构建摘要见apk-build-summary.log；未安装到手机，不宣称实机问题已验收。

