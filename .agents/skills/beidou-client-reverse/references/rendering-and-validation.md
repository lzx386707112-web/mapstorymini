# 纹理采样与移动端输入验证

核实快照：2026-10-26。原端身份见 [binary-and-tools.md](binary-and-tools.md)。
原端逻辑的确定程度与运行时迁移状态分开记录；“尚未迁移”不代表需要重新逆向已确认部分。

## 虚拟输入与渲染验证

已改：虚拟键移入固定 GUI.depth=−300 的 InputBridge.OnGUI，在游戏窗口之后绘制；原生光标/换图黑场保留专用层级。
鼠标指针编号−2不能用>=0判断占用；唯一空闲编号是−1。取消、输入门控关闭或失焦时释放按键/摇杆。
Unity batch `NativeKeyboardChecks.Run`：214通过，0失败；包含鼠标摇杆与按钮的实际注入/释放回归。
dotnet隔离输出编译：0警告、0错误。尚未做 Android 多指/重叠窗口实测。

`verify_native_characters.py`：158帧原始RGBA和脚底相对位置与原合成器完全一致（使用现有Unity快照）。
这排除了所测帧的像素/轴心合成误差，不证明最终手机显示无锯齿。
当前相机768逻辑像素，屏幕高度通常与它不同；人物 world scale=1 不代表最终显示像素倍率=1。

继续核实了 `Gr2D_DX8.dll`，SHA256 `ab1c942229af33da04d913ed1157ab322bff0812f7368bcf745d64b98698d34d`，ImageBase `0x50400000`。
Direct3D8 的 `SetTextureStageState` vtable offset 为 `0xFC`（由本机 mingw d3d8.h 核对）；MAGFILTER=16、MINFILTER=17，POINT=1、LINEAR=2。
`0x5040E2BF` 允许图层明确指定 filter；默认−1分支 `0x5040E2FD` 比较源/目标宽高并检查变换：
满足原尺寸/无变换条件时 `0x5040E346`/`0x5040E356` 设置POINT；其余 `0x5040E377`/`0x5040E387` 设置LINEAR。

据此在 `SpriteAnim.LateUpdate` 对人物按最终正交相机像素倍率选择采样：1:1保留Point，缩放时Bilinear，分辨率改变时重新选择。
没有修改IMG像素、原点、人物尺寸或全局UI贴图采样。当前适配只覆盖项目中无旋转的正交人物图层，不等于整个原端Gr2D所有变换分支。
`NativeCharacterSamplingChecks.Run` 已验证同一缓存帧的1:1→1.40625倍→1:1采样切换，保存并检查了两张Unity渲染图：
`Library/UiInputAnalysis/sampling-192.png`、`sampling-270.png`。人物有正常显示，没有发现新增透明黑框；Android实际画面仍待实测。
