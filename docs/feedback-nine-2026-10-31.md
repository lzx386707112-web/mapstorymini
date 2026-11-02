# 九项反馈处理记录

| 项目 | 本次修改/审计 | 验证边界 |
|---|---|---|
| 自动分配面板 | 依据字体22/23恢复确认白字；能力名称和说明中文；继续用YesNo3原图 | 布局沿用既有原图窗口；未做Android截图验收 |
| 假人空中走 | 录制步行遇断层停止；站走插值贴合可走平台，排除跳跃/绳梯/椅子 | 新增20px断层回归 |
| 地图BGM | 防止销毁淡出中的clip；修复缓存命中协程句柄、双声道停止、暂停时间/静音及文件URI/超时 | 编译通过，未连接手机验证解码/后台恢复 |
| 进场卡顿 | ReactorTemplates/ReactorDrops原先在BotRuntime构造时Root+Join，本次纳入登录期后台预热与就绪门控；增加Field/Enter各阶段及Bot/Preload构造耗时日志，便于下一轮手机日志定位 | 确定阻塞来源已修；没有Android首次进场耗时前后测量，不能认定所有卡顿消除 |
| 轮回石碑闪层 | summoned/stand/die统一固定排序值，消除与人物同层竞争 | 代码路径检查，未实机录屏 |
| 右下获得提示 | 原队列6行×14px、290宽、6秒淡出、白字黑影，连接经验和成功拾取；金币用实际结算额 | 覆盖打怪EXP和本地主动拾取；其他奖励包/队友异步分配尚未全部接入 |
| 背包数量样式 | 原ItemNo数字图，x=slot.left、y=slot.top+20，原图宽度累加，修正展开槽x=8 | 显示样式修复；背包多堆槽位/聚合逻辑另有既有迁移边界 |
| 野外Boss | 全6337图扫描；Boss配置接线、脚本setPosition/addMonsterSpawn、离图保留刷新状态及HP；原MobGage顶栏 | 全量资源解析不等于逐个Boss玩法实测；远程后台战斗、plife热增删、完整副本仍有缺口 |
| admin/聊天/GM | admin账号新旧角色默认GM6；点击输入条拉起手机键盘；完成200条指令语法清单审计 | 后续GM实现已接入105个语法入口，含14个部分接入；其余95个未迁移，详见逐项清单，不能声称全量可用 |

## 可复查材料

- [GM指令逐条清单](gm-command-runtime-audit.md)
- [全地图Boss记录](wild-boss-map-audit.json)：1149个Boss模板，6229条直接life记录，链接地图展开后7281条；解析错误0。包含事件/特殊地图，不把它们都当普通野外。
- [原端地址、资源、调用链及边界](../.agents/skills/beidou-client-reverse/references/field-feedback-2026-10.md)

## 验证

- Unity脚本及Editor代码编译：0 warning、0 error。
- 服务端最终检查：668通过、0失败（含admin新/旧角色权限与假人20px断层）。
- Unity FieldFeedbackChecks：PASS，实际原图野外Boss初次生成、回图HP、回图冷却、脚本刷新点回图恢复。
- Unity FeedbackRegressionChecks：PASS（自动AP数值、技能父页签、攻击时序、勇士部落落叶进场/切换），不是九项全部画面验收。
- NPC harness：889条，parseFail=0，ok=790，runFail=27，javaInterop=72；后两类是宿主接口未覆盖，不报告为全通过。
- 打包脚本检查：2779条，parseFail=0，mangled=0。
- adb未检测到连接的Android设备。

未构建或部署APK；未改源端脚本；保留工作区原有未提交修改。
