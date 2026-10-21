---
name: mapledash-map-port
description: 将冒险岛原客户端 WZ 地图迁移到 MapleDash Unity 项目，复用地图显示比例、相机、人物脚底锚点、foothold 碰撞与绳梯动作。适用于添加或替换地图、修复踩地偏移、上层平台与攀爬问题；以原端代码和数据为依据。
---

# MapleDash 原地图与角色迁移

将新地图接入现有地图/角色管线，保留原地图几何、素材原点和动作时序。截图用于验收，不能替代原代码、WZ 数据或二进制证据。

## 先定位当前实现

项目根目录为本 skill 所在目录向上三级（包含 Assets、tools 的目录）；本机路径为 `/Users/lizixian/MapleDash`。原端在 `/Users/lizixian/Documents/mxd/BeiDou-Server`。其他环境先定位对应路径；这些是当前安装位置，不是跨机器常量。读取目标项目 AGENTS.md 及适用约束。本机 shell 遵循 `/Users/lizixian/.codex/RTK.md`，命令以 `rtk` 开头。

- 新地图接入：读 [迁移流程](references/migration.md)。
- 坐标、渲染、脚底、动作、绳梯问题：读 [实现约定](references/implementation.md) 的对应部分。
- 需要原端相机/背景依据时，再读项目 `docs/original-map-rendering.md`；其中较早的“攀爬未迁移”描述已被文末绳梯实现说明更新。

先检查现有代码和未提交改动，再按目标地图增量修改。复用项目 `tools/wzmap.py`、`tools/wz_stage.py`、`tools/wz_actions.py` 和运行时；不为每张新地图另写控制器，不复制整套脚本到 skill 形成第二份实现。

## 必须保持的约定

- 地图、角色和物理共用像素单位及 PPU；用完整 foothold 和 ladderRope 数据，不用截图描碰撞线。
- tile/obj 各自使用 canvas.origin。人物使用解析后的原生 Body/body 原点；不能用武器/头发不透明边界猜脚底，也不能靠整体抬高人物掩盖坐标错误。
- 相机 VR 与导出图片边界分开；手机全屏扩展水平视野，不单独缩放人物。
- 保留竖直 foothold 墙、斜坡、上下层平台、绳梯上端退出标记；不可把活动区域压成最下方一条平地。
- 导出动作沿用武器选择的 stand/walk、原动作帧和绝对 delay；攀爬静止时动画必须可暂停。
- 区分 WZ/代码已核实的内容与本项目暂定行为。当前实现尚不等于完整原客户端 1:1。

## 完成与交付

新地图要同时验证数据导出、运行时接线、实际物理和画面。使用迁移流程里的定向验证；文档/配置小改不必启动 Unity。不要默认构建 APK、部署或修改原客户端。

说明修改了哪张地图、复用了哪些逻辑、实际通过哪些检查，以及未验证的手机手感/路线/原端行为。只验证落地不能声称已经走通所有平台路线。
