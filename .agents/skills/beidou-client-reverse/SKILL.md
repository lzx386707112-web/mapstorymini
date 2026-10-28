---
name: beidou-client-reverse
description: 复用并维护 BeiDou.exe 及配套 DLL 的已核实功能模块逻辑，用于北斗客户端 UI、角色操作、动作时序、地图与渲染迁移及继续逆向，避免重复分析已确定的函数和资源关系。
---

# BeiDou 客户端逆向知识

本 skill 随 MapleDash 项目维护。项目根目录是本目录向上三级；源端通常在项目根目录的 `../../Beidou-Server`。机器路径变化时先定位实际文件。

## 使用入口

开始分析或修改原端相关功能前，先查下表，只读取当前模块的 reference。已确定结论直接复用；只对记录缺口、矛盾或不同二进制版本继续逆向。不要因为临时反编译文件不在了就从头分析。

| 当前问题 | 读取记录 |
| --- | --- |
| 建号中文名字、长度规则、重名与错误提示 | [角色命名](references/character-name.md) |
| SoloMapling 假人生成、调度、地图索引、迁移缺口、地面掉落权威服务（MapItem/pickupItem/pickItemDrop） | [假人系统](references/solomapling.md) |
| 文件版本、地址、字符串池、复现工具、反编译陷阱 | [二进制与工具](references/binary-and-tools.md) |
| ijl15/2ijl15 职责、手机用版本差异、分辨率补丁与 Android 适配价值 | [DLL 与手机适配](references/ijl15-mobile-adaptation.md) |
| 自定义DLL身份、V/VI视频表版本差异、MCV双流/Android解码、Boss标记选片、数字首位/后续位与Miss、伤害皮肤、V/VI回放/雷枪12按、追击/雷枪numeric动作、天气粒子/地面/声音、skillback合同与迁移边界 | [自定义功能 DLL](references/custom-dll-systems.md) |
| 按键组合、重复权、普通跳/下跳、foothold传送带force/drag、地空积分/x86对照、台阶直角墙脚碰撞、普攻攻速 | [输入与物理](references/input-and-physics.md) |
| 站立/警戒计时、接触受击/冲量/免疫、身体框、多段飘字与尚未对齐的边界 | [角色受击与飘字](references/character-reactions-and-damage.md) |
| 小地图模式、视口、标记、大地图边框与定位 | [地图窗口](references/map-windows.md) |
| 商城子窗口、商品列表、坐标与素材 | [现金商城](references/cash-shop.md) |
| 家族列表、按钮分派、职位、对话框、公告板、Tab | [家族与 Tab](references/guild-and-tabs.md) |
| Point/Linear 采样、虚拟控制层、已有验证范围 | [渲染与验证](references/rendering-and-validation.md) |
| 哪些 UI 尚未替换、原端与移动适配的区别 | [UI 迁移审计](references/ui-audit.md) |
| 底栏 71 高分组、11 个按钮坐标、通知槽、客户区 578 | [底栏布局](references/statusbar-layout.md) |
| 相机 VR、分辨率补丁、背景滚动；技能窗已定位函数 | [地图与技能窗入口](references/map-and-skill-window.md) |
| 鼠标消息分派、键位窗/技能窗/组合栏的 DragStart 与放下语义 | [UI 拖动语义](references/ui-drag-and-drop.md) |
| NPC 对话窗口精确布局/字体/按钮、VScr4、`#L` 图文、灯泡 autoStart/日期筛选、截图问候入口/交付奖励10000→4→1、fieldType82青苹果外观、建号路线与远程任务脚本 | [NPC 对话窗口](references/npc-dialog.md) |
| 传送门 pt 触发方式、NPC 头顶名字锚点、聊天气泡九宫格与时长 | [传送门/名牌/气泡](references/field-portal-nametag-bubble.md) |
| 背包/装备窗物品悬浮详情：ToolTip 图集、名称/描述来源、属性/需求字段、配色与帧绘制 | [背包与悬浮详情](references/inventory-and-tooltip.md) |

地图坐标/脚底/平台/绳梯的运行时约定另见 [mapledash-map-port](../mapledash-map-port/SKILL.md)。服务端和脚本语义仍以源端服务端/脚本为依据，不能用 EXE 的显示行为替代服务端规则。

## 结论使用边界

- 地址仅对应记录中的 SHA256/ImageBase；首次使用当前文件或文件版本变化时核对身份。版本不一致时保留旧结论并另列新版本，不能直接套地址。
- 区分原端已确定逻辑、待核实线索、项目当前实现、移动端适配及实际验证。已确定函数不等于模块完整迁移；Unity 回归通过不等于 Android 实机验收。
- IMG 节点、origin、布局参数、函数调用链优先于截图推测。原端经核实的代码绘制可严格照搬，禁止自行设计；当前无章节模式。
- 反编译伪代码只能辅助理解。涉及参数、状态位、除法取整、this 子对象偏移或虚表分派时，使用已记录的汇编证据；不要用猜测覆盖确定结论。

## 新发现必须同步归档

每次产生新的确定结论，在本次工作结束前立即更新对应 reference；新模块新增 reference 并更新上表。纠正旧结论时同步修正所有引用，注明旧地址/解释为什么不成立，避免以后再次走错分支。

每项确定结论至少保留：

1. 原文件、SHA256 或所用身份记录，以及 VA/函数范围；结构字段注明基于哪个 this，区分文件偏移与 VA。
2. 触发条件、状态转换、调用链、公式和单位；明确整数取整及默认值。
3. 关联 IMG 路径、节点职责、尺寸、origin 和坐标所属父窗口（适用时）。
4. 可复现的项目工具/命令、关键汇编或常量依据；必要材料不能只放 `/tmp`、被忽略的 `Library` 或聊天记录。
5. 对应运行时代码、已迁移范围、实际验证和剩余缺口。没有新增验证就不能沿用旧 PASS 声称新行为通过。

不确定内容放在明确标记的“待核实”部分，保留下一处应追踪的函数或字段，不把它写成实现要求。发现记录不足时只补查不足部分；AGENTS 要求的用户决策仍照常执行，已有授权不重复询问。

现有 `tools/inspect_beidou_client.py` 作为复现入口，不在 skill 内复制一套分析器。旧文档 `docs/original-ui-input-analysis.md` 已改为本 skill 的索引；新知识只维护一份正文。
