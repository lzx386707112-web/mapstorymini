# 七项玩法反馈：2026-10-31

本轮按 BeiDou.exe、原服务端和原 IMG 核对并修改确认的移植缺陷，未改 NPC/任务 JS、未自绘/替换 UI 素材、未增加章节模式分支。工作区原有未提交修改继续保留。

## 修改结果

| 反馈 | 确认原因与修改 |
| --- | --- |
| 蜗牛投掷术/其他远程 | 公共弹道曾强制至少600px、脚底±100px选敌。蜗牛改为原端前方50..300px、脚底上方28px发射、20px前向楔形切片与当前IMG身体框；有原lt/rb的技能用对应等级矩形，武器射程/射程被动及弹速统一纠正。目标在施放时选择，避免延迟结束后重新跨平台找目标。原端允许命中后朝合法身体目标略微斜飞，不能把所有远程技能强制成水平线。 |
| 怪物重叠闪烁 | 同order、同Z透明精灵的纹理排序平局随换帧改变；改为固定Pivot和存活期间唯一的主图层微小Z，不再由动画贴图决定前后。保留玩家高于普通怪的order。 |
| 吸怪不飞/攻击卡顿 | 原有130ms插值被每帧GroundAt贴地覆盖；吸怪期间跳过贴地，150px步进改为Java整数WZ像素取整，回收死亡锁定和特效，清理旧击退。接触无敌期间提前跳过堆叠怪碰撞；移除只读倍率/无声望接收者击杀时整库快照与写盘，一次群攻内合并重复保存。 |
| NPC偶现无反应 | 不可见但仍Active的旧会话会永远挡住NPC点击，改为只由可见有效对话阻挡；一次脚本调用内合并重复保存，调用结束前同步落盘。≥100ms的调用记录npc/quest/script/function/ms。仍保留NPC双击、任务标记单击及原脚本状态机。 |
| 碰怪每次弹开 | Incoming原先对正攻击永远至少1点，漏掉物理MISS前置。已加入原端回避公式，MISS不击退、也不进入正伤害受击表情；正伤害保留STANCE/地图限制原逻辑，不额外随机取消击退。 |
| 血蓝频繁闪烁 | 错误的低血每秒循环、末帧长亮与100ms帧延迟。改为原端低于警告线且数值下降时只播一次130ms×5帧。回血或保持低血不循环。 |
| 宠物补药偏晚 | 原按帧比较可漏掉进图首个损失、同帧先损失再回复。普通伤害/Boss伤害/技能HP成本/技能MP消耗改为同步检查，读取权威召唤状态，更新轮询基线避免重复吃药。50%边界按原端严格小于；普通模式每次只用一瓶，server/compulsory模式保持多瓶与配置比率。 |

## 实际执行的验证

- 原 EXE x86 物理回避 oracle：3,600组全部匹配，含职业、等级差、0命中/0回避、概率边界。
- Unity有图形设备 `GameplayFeedbackChecks.Run`：PASS 22,735断言。包含三个原蜗牛技能等级300px、前后/远近/上下平台几何、血蓝警告下降/回血/静止边界、吸怪离地与150px向量、嵌套保存及异常离开落盘、NPC可见性门控。
- 真实IMG怪物渲染：27组stand/walk/hit换帧组合，19,110个完全不透明交叠像素保持固定前景。
- Unity实际宠物场景 `NativePetSceneChecks.Run`：PASS79。新增正好50%不触发、低于50%立即补HP、技能扣MP立即补、轮询不重复、同帧第二次扣MP仍触发五项；使用真实药品、宠物装备、权威库存和绑定。
- `tools/server-check`：通过707 / 失败0。
- `tools/unity-compile-check` `dotnet build -t:Rebuild --no-restore`：0警告、0错误。
- 脚本 `--pack`：2779条，npc889、quest470，parseFail0、mangled0。
- 脚本 `--run`：889条、790成功、parseFail0、runFail27、javaInterop72，退出1；这个独立harness的简化mock尚不支持getCapacity/getId等API和部分Java类，不能称全量运行通过。此次未改解释器/harness或源JS，正式运行的NPC输入门控由Unity定向回归验证。
- `git diff --check`通过。

汇总证据：`docs/skill-regression/seven-feedback-results-2026-10-31.txt`。
逆向正文及复现地址：`.agents/skills/beidou-client-reverse/references/seven-gameplay-feedback-2026-10-31.md`。

## 边界

本轮修复公共路径，并非全技能/全受击状态的1:1宣告：76664D等特殊远程分派、完整临时属性重算/特殊回避被动、魔法回避7929CA仍有待迁移范围，已在逆向skill标明，未靠猜测套物理回避。
吸怪130ms是按原MoveLife时长作本地插值，尚未与原EXE逐帧轨迹对照；关闭眩晕时AI并发/fake实体语义也未全部对齐。
少见脚本/地图资源变化保留按帧补药兜底，尚未所有调用点同步化。
未构建APK、未进行Android实机群怪压力/多指输入或NPC偶现页停顿复现，因此不把代码定向回归当作实机问题已全部消失；慢调用日志用于继续定位具体脚本与耗时。

复现命令（均在MapleDash根目录，按本机RTK规则）：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/native_evasion_oracle.py ../../Beidou-Server/clien/BeiDou.exe --output tools/native-evasion-oracle.json
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -projectPath /Users/lizixian/Documents/mxd/mxd-android/MapleDash -executeMethod GameplayFeedbackChecks.Run -logFile /tmp/mapledash-seven-feedback.log
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -projectPath /Users/lizixian/Documents/mxd/mxd-android/MapleDash -executeMethod NativePetSceneChecks.Run -logFile /tmp/mapledash-seven-pet.log
rtk proxy dotnet run --project tools/server-check
rtk proxy dotnet build tools/unity-compile-check/Check.csproj -t:Rebuild --no-restore
rtk proxy dotnet run --project tools/script-harness -c Release -- --run /Users/lizixian/Documents/mxd/mxd-android/MapleDashResources/Data/scripts/npc
rtk proxy dotnet run --project tools/script-harness -c Release -- --pack /Users/lizixian/Documents/mxd/mxd-android/MapleDash/Assets/Resources/Scripts/npc_scripts.json
```
