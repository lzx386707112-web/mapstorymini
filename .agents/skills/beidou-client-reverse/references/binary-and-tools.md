# 二进制身份与复现工具

核实快照：2026-10-26。原端身份见 [binary-and-tools.md](binary-and-tools.md)。
原端逻辑的确定程度与运行时迁移状态分开记录；“尚未迁移”不代表需要重新逆向已确认部分。

## 输入文件与复现

客户端：`../Beidou-Server/clien/BeiDou.exe`（从工作区根目录计算）。
SHA256：`1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。
下列地址均为该文件的虚拟地址，ImageBase 为 `0x400000`。
分辨率补丁 `ijl15.dll` 也会改变布局；不能把 EXE 的 800×600 当成所有运行配置。

`tools/inspect_beidou_client.py` 不修改被分析二进制，支持按虚拟地址反汇编、DWORD 引用定位、字符串池解密。`--strings PATH` 会将解码字符串池写到指定 JSON，并不是给反汇编自动加注释。
依赖 `pefile`、`capstone`；本次隔离环境是 `/tmp/mapledash-reverse-venv`。
例如（遵循本机 RTK 规则）：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x94c383 --end 0x94c4d9
```

字符串池：指针表 `0xBDC9D4`，条数 `0x161A`；128 位密钥 `0xB001EC`。
每条首字节控制密钥循环左移，其余字节异或，异或结果为零时保留原字节，cp949 解码。
ANSI GetString `0x79E993`，wide wrapper `0x79EA63`。
本机完整中间产物保存在忽略的 `Library/UiInputAnalysis/`，不作为唯一可复现依据。

## 持久复现方法与陷阱

命令工作目录为 MapleDash 项目根目录。临时 venv 不存在时，用当前可用 Python 环境安装 `pefile`、`capstone` 后运行同一项目工具；WZ 工具另需要其自身依赖，不能假设系统 Python 和逆向 venv 的库相同。

`--xref VALUE` 搜索 DWORD 字节命中，包含数据和其他指令中间的假阳性，必须从有效指令边界复核。字符串池 ID（十进制）不等于函数 VA。PE 读取 VA 时先减 ImageBase 转 RVA，不能把 VA 直接当文件偏移。

已核实的普攻逐帧整数计算关键指令，可在 `0x454776..0x4547E8` 重现：

```asm
00454779 cmp eax, 2
00454784 jl  0x454789       ; 上界比较之前为 cmp eax, 0xA
00454795 add eax, 0xA      ; clamp(speed,2,10) + 10
004547DA imul eax, ecx     ; 原 frame delay 毫秒
004547DD push 0x10
004547DF cdq
004547E0 pop ecx
004547E1 idiv ecx          ; 每帧整数除16
```

只解指定连续函数范围，不要将相隔很远的地址一起交给 CFG 分析（会扩展成巨大连续区域）。angr 不加载外部库即可分析原代码；缺 Unicorn 本机库不影响本次仅使用的静态分析。

BeiDou 的 EH 入口常为 `mov eax,metadata; call 0xA60B98`，反编译器可能无法还原 EBP 栈帧。曾在**临时分析副本**将这10字节替换为 `55 8B EC 6A FF 6A 00 6A 00 90`，模拟 EBP 与三个 EH 槽；这只是辅助反编译办法，不是原端行为或准确函数签名的证据。原文件保持不变，输出参数仍必须用原汇编核对。

旧 `Library` dump 的字符串注释可能多行，不能把每行都当汇编指令；只解析以合法十六进制地址开头的指令行。此前误认地址的修正已记录到各模块（例如 CSList 构造函数与家族 OnButtonClick），不得再按旧猜测定位。
