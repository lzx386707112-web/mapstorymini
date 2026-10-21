# 原客户端地图显示处理核对

此处以本地原端的代码、WZ 数据和二进制为依据，截图仅用来验收。
原端完整 CMapLoadable / resolution.cpp 源码不在 mxd 目录；兼容源码给出了函数地址，
以下相机与分辨率结论进一步通过本地实际加载的 EXE / DLL 反汇编核对。

## 已核实的依据

- `clien/config.ini`：本地保存值为 800×600；注释列出 1280×720、1366×768、1600×900、
  1920×1080 等模式。因此不能把截图估算的 1366×768 宣称为该客户端唯一分辨率。
  本项目保持 768 像素的逻辑高度，手机全屏按实际宽高比扩展横向视野，地图与人物共用像素单位。
- `tool/client-debug/weather-compat/src/wvs/field.h`：CMapLoadable::m_rcViewRange 位于 +0xF0，
  RestoreViewRange=0x00641EF1，MakeBack=0x0063CD4E。
- `BeiDou.exe` 0x00641EF1..0x0064211D：读取地图 VR 四边；默认值来自 field-space
  边界，依次 -20、-60、+20、+100；左右内缩 400，上下内缩 300；
  若内缩后左右/上下反转，将该轴的两边都设为平均值。
- `ijl15.dll` 0x10011DF5..0x10011E8F：以运行时宽/高的一半分别改写
  0x00641F62、0x00641FC9、0x00642090，证明边界随逻辑分辨率变化，不能锁死 800×600。
- `BeiDou.exe` 0x00437B32..0x00437B39：相机向量 RelMove 的参数 x=0、y=-50。
  当前实现改为双轴跟随、中心高出目标 50 个地图像素，移除自行添加的向前偏移。
- `weather-compat/src/weathersplash.cpp` ReadCamera：从 IWzGr2D.center 读世界相机坐标，
  注释指出动画更新按 CField::m_rcViewRange 裁剪。
- `weather-compat/src/weatherfx.cpp` 365..400：type=4 的 back 独立自动滚动，
  按 20000/abs(rx) ms 移动 100px，即 -5*rx px/s；MakeGrid 以 Gr2D.center 为锚平铺。
  EXE 0x0063DC4A..0x0063E1C2 可核对 type 4/6 的横向滚动和 type 5/7 的纵向滚动分支。
- WZ back 的 x/y、origin、rx/ry、cx/cy、type、alpha、front 均直接导出。
  背景保持独立图层，由相机坐标驱动，不再静态烘焙、镜像或涂抹填补。
- WZ tile/obj 使用原始位置和各自 canvas.origin；foothold 导出整图范围。
  VR 不再由“最长平地”的截图宽度替代，且原地图下方不再人为重复填土。

## 验证范围与仍有的限制

本次覆盖地图坐标、逻辑视野、双轴相机范围、背景平铺/视差/滚动。
相机插值暂保留项目现有指数平滑，未确认与原端 Shape2D 的插值曲线完全相同。
地图 tile/obj 仍烘焙为静态主体图，动画摆件与按角色所在地图层的遮挡尚未迁移。
因此这些修改不能等同于整个地图渲染器已经完全 1:1。

## 对照文件哈希

- BeiDou.exe: `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`
- ijl15.dll: `87c0d58d53b4294acf53f053191c67739d1fa1ab2e9532fd764ba6b251f85c7e`


## 全屏、输入与跳跃修正（2026-10-21）

- 全屏 Camera.rect=(0,0,1,1)，逻辑高度仍为 768px。每帧清除整个画面，
  避免之前窄视口两侧保留大厅 GUI / 角色等旧内容。手机显示宽度决定逻辑地图宽度。
- TouchPad.Update 采集输入，OnGUI 只绘制；圆形使用独立透明贴图，不再由重叠矩形拼接。
  按 fingerId 持有摇杆，另一只手可跳跃，失焦/暂停/手指取消时清除输入。
  原地图关闭原型的五条深度层，纵向拖动不会再把角色推离 foothold。
- 直接读取原 Map/Physics.img：walkSpeed=125、jumpSpeed=555、gravityAcc=2000、fallSpeed=670，
  除以 40 PPU 转为世界单位。数值保存在 Resources/Physics.json，wz_actions.py 可重新导出。
  理论基础跳高约 77px，因此 60px 台阶属于可跳高度。
- 导出保留竖直 foothold 作为台阶墙。脚底越过墙顶即可横向穿过；空中独立积分，
  下降扫过平台时落地，走出平台转为下落。物理以最多 8ms 的步长积分，覆盖 30/60/120Hz。
- 原武器 info.walk / info.stand 决定 walk1/2、stand1/2，避免把两套动作串接成一个循环。
  各动作使用原身体帧和 delay，空中切 jump；移除普攻和施法入口随意设置的 1.3～1.5 倍播放速度。
  攻击动作未播完时不被下一轮自动普攻截断。导出脚本：tools/wz_actions.py。

范围：基础姿势、帧顺序、帧延时和基础跳跃参数来自原端；武器攻速分档、
攀爬/游泳、水平加速与摩擦尚未完整迁移，不能据此声称整套角色控制器已完全 1:1。

技能角色姿势也按原 Skill IMG 的 action 列表导出：突进 rush2、虎咆哮/防御崩坏 alert3、
剑影分身 brandish1 等；包含原 action 列表里的重复段，治疗/增益也进入对应角色姿势。
原帧 delay 可能为负值，播放时取绝对时长，不能用 max(40,delay) 把 -300ms 截成 40ms。
缺少 action 的自定义技能仍退回职业普攻姿势，未凭空推测原端特殊分派。
