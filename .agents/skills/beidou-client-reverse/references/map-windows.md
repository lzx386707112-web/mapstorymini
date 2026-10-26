# 小地图与世界地图

核实快照：2026-10-26。原端身份见 [binary-and-tools.md](binary-and-tools.md)。
原端逻辑的确定程度与运行时迁移状态分开记录；“尚未迁移”不代表需要重新逆向已确认部分。

## 小地图

原端 `0x856400`：模式 0=MaxMap，1=MinMap，2=Min；快捷键 `0x859108` 按 `(mode+2)%3` 切换。
原素材 `UI/UIWindow.img/MiniMap`：MaxMap 顶部72/底部15，MinMap 顶部29/底部14；Min 高20。
MaxMap/MinMap 使用 nw/n/ne/w/e/sw/s/se/c 九片，Min 使用 w/c/e 三片。
`0x8588FF`：canvas.width≥280 时视口210，否则原宽；canvas.height≥150 时视口112，否则原高。
`0x8563C1`：MaxMap 宽度容纳视口及两行文字；MinMap 宽度只容纳视口，最小120；Min 使用两行文字宽度总和+80。
字体宽度在 Unity 中由当前中文字体测量，不能宣称与原端 Windows 字体逐像素相同。
`0x8564AD`：视口水平居中。`0x856C6D`：模式0顶部偏移72，否则29。
`0x8583EA`：BtMap 在 W−42/y6；Min 模式 W−44/y4；BtMax 左移14，BtMin 左移27。
`0x857616`：title 复制到(8,10)，当前 IMG 的 title 为1×1。
`0x8576A4`/`0x857B18`：两行文字中心复制 y28、44，x8，有 mapMark 时另加40；相邻坐标是文字阴影。
`0x85817F`：`Map/MapHelper.img/mark/<info.mapMark>` 复制到(6,23)。
`0x85ABBC`：按玩家坐标计算滚动、以 mag 进行整数右移；`0x859313` 是裁切复制，不是将整张地图缩小。

已修改：原素材拼接、三种模式、快捷键循环、视口裁切/滚动、原生按钮与标记/徽标。
未完成：原端文字阴影的逐像素匹配、保存窗口位置/拖动及手机视觉对照。
## 大地图边框核实

`0x9EB594` 创建666×524窗口；`0x9EBA31` 将BaseImg复制到(13,35)，标记和MapLink沿用BaseImg/0的origin。
`0x9EC2F6`至`0x9ECC49`：Border/0左上、1上条、2右上、3左条、4右条、5左下、6下条、7右下；左右7像素、底18像素。
`0x9EB80D`/`0x9EB80F` 创建UI/Basic.img/BtClose于(W−20,6)，原尺寸12×12；title复制到(8,10)。
运行时已删除自行填色底板及默认Unity按钮，使用上述节点和参数；原地图BaseImg保留原尺寸/origin。
用户已明确允许严格照搬已核实的原端代码绘制，禁止自行设计；据 `0x9EC12A..0x9EC143` 补回 IMG 边框下的白色内底 `FillRect(2,2,W-4,H-4,0xFFFFFFFF)`。
当前游戏仅使用原版Field地图模式，小地图直接使用WZ地图坐标；章节模式属于早期小游戏遗留，不再为其增加兼容分支。
