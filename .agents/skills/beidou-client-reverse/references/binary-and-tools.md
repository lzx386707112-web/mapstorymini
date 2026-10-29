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

### 线性 sweep 会失步（2026-10-29 定案，别再踩）

`tools/inspect_beidou_client.py --start/--end` 走的是 capstone 线性 sweep + `skipdata=True`。
在**带 switch 跳转表的 VC++ 大函数**里它会漂：指令边界错位，之后所有 `[reg+disp]` 都是假的。
实例：怪物攻击解析器 `0x67FD81` 曾因此得出「`[ebp-0x18]` 在整个函数里只被 `ebx`(=0) 写过两次，
却被当指针解引用」这种不可能结论，进而错判「偏移表不可信」并写进过文档。

改用递归下降（BFS 基本块，跟随 `jcc`/`jmp`/`jmp [reg*4+table]`）：

```sh
python tools/inspect_beidou_client.py <exe> --fn 0x67FD81                        # 整函数
python tools/inspect_beidou_client.py <exe> --fn 0x67FD81 --start A --end B      # 只看一段
python tools/inspect_beidou_client.py <exe> --fields 0x67FD81 --pool pool.json   # 字段名→结构偏移
```

`--fn` 会打印指令数与覆盖区间，并标出 `!! uncovered` 空洞；空洞为 0 才说明控制流完整。
`--fields` 的识别模式是 `push <串id>; push <dst>; call 0x79E805`（取串函数），
把每个字段名与**紧随其后**的结构写入配对 —— 这是「不靠猜」导出偏移表的正解。

教训：看到「同一块内存被两种不相容写法使用」，先怀疑自己的反汇编，再怀疑二进制。
`0x67FD81` 的真实答案是**按 `type` 互斥的 union**，不是解码错误。

BeiDou 的 EH 入口常为 `mov eax,metadata; call 0xA60B98`，反编译器可能无法还原 EBP 栈帧。曾在**临时分析副本**将这10字节替换为 `55 8B EC 6A FF 6A 00 6A 00 90`，模拟 EBP 与三个 EH 槽；这只是辅助反编译办法，不是原端行为或准确函数签名的证据。原文件保持不变，输出参数仍必须用原汇编核对。

旧 `Library` dump 的字符串注释可能多行，不能把每行都当汇编指令；只解析以合法十六进制地址开头的指令行。此前误认地址的修正已记录到各模块（例如 CSList 构造函数与家族 OnButtonClick），不得再按旧猜测定位。

## 先翻 Compat DLL：它们是 exe 的导览图（2026-10-29 定案）

`clien/` 下除了原版 `BeiDou.exe`，还有一批**北斗自己加的补丁 DLL**。它们的工作方式是
「校验原字节 → `VirtualProtect` → 写 `E9 rel32` 跳转 → 跳回原地址」，所以 hook 体里
**直接写着 exe 里被拦的函数地址**，且用 `push <原地址>; ret` 这种蹦床表示「回到原流程」。

这比在 8.5MB 的 exe 里盲扫快一个量级，而且能直接暴露**你之前不知道存在的代码区**
（例如怪物/角色动作层 `0x97xxxx`、`0x98xxxx`，此前只盯着 `0x66xxxx`）。

识别方法：导出表名 + 字符串。导出名往往就是意图：

| DLL | 值得看的导出 |
| --- | --- |
| `IndexedDamageNumberCompat.dll` | `HookDamageMonsterNumber` / `HookLocalCloseRangeLookup` / `HookSkipLocalAttackByte` / `HookSkipLocalMoveAction` |
| `BeiDouSkillCompatCore.dll` | `HookBishopMagicAoeClassifier` / `HookSummonAttackClassifier` / `HookSummonBehaviorClassifier` / `AssignNativeRangedProjectileTarget` |
| `DawnWarriorSkillCompat.dll` | `HpMpFuseShort` / `HpMpFuseLong` |

```python
# 导出名 + RVA（注意：e.address 是 RVA，VA = ImageBase + RVA）
pe = pefile.PE(dll, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXPORT']])
for e in pe.DIRECTORY_ENTRY_EXPORT.symbols:
    print(e.name, hex(pe.OPTIONAL_HEADER.ImageBase + e.address))
```

```sh
# 一次导出「导出名 → VA → 它 hook 的 exe 地址」
python tools/pe_module.py <clien>/IndexedDamageNumberCompat.dll
```

输出长这样（自动收集 `push <exe 地址>; ret` 与 `mov eax,<exe 地址>` 两类立即数）：

```
HookLocalCloseRangeLookup  6b341062  -> exe 0097251a, 00972626
HookSkipLocalMoveAction    6b3410a5  -> exe 00980477, 009804bb
```

拿到 exe 地址后，用 `tools/inspect_beidou_client.py --fn <地址>` 继续递归下降。

**别手抄这张映射**：第一次手抄就把 `SkipLocalAttackByte` / `SkipLocalMoveAction`
两条弄反了，直到工具自动跑出来才发现。

**别把导出地址当 VA**：这几次都是 RVA，`00001968` 看着像段内偏移而不是 `0x67281968`。
混淆这两者会反汇编到空内存。

`IndexedDamageNumberCompat.dll` 的字符串还直接给出了补丁点的语义与失败条件，
例如 `ERROR: CLOSE_RANGE_ATTACK lookup bytes do not match this BeiDou.exe` ——
这句话本身就说明「存在一个叫 close-range-attack lookup 的函数，且作者知道它在哪」。
**遇到「某个逻辑在 exe 里怎么都找不到」时，先 grep 全部 DLL 的字符串。**

## 客户端中文从哪来：ijl15.dll 的字符串池替换（2026-10-29 核实）

**结论先行**：EXE 字符串池里的英文常量在运行时会被 `clien/ijl15.dll` 逐条换成中文。所以"源端某处显示英文"不能直接推断成"源端没本地化"，必须先查这张表。

- 二进制：`clien/ijl15.dll`，ImageBase `0x10000000`，导入表只有 KERNEL32/USER32/MSVCP140/IMM32/VCRUNTIME140 和几个 CRT 转发，**没有 GetPrivateProfile***；`config.ini` 由 `.bdcfg` 节里一段自解压 shellcode 用 `fopen_s/fgets/strtol` 自己解析（只读 `serverip_address`、`serverip_port`）。
- `config.ini` 的 `SwitchChinese` 键在当前构建里**没有任何模块引用**（全目录 ascii + UTF-16 搜索均 0 命中）。真正的开关是 DLL 内的字节 `0x1003657C`，该构建镜像里初值就是 `1`，即中文默认开启。所以调这个配置不会改变语言，别被它误导。
- 中文表：`.rdata` 起 `0x10036948`，元素 `{int id; std::string 中文}`，`sizeof(std::string)=0x18` 故步长 `0x1C`，末 `0x10043AA0`，共 **1914 槽 / 实际 1865 条**（部分槽复用同一对象或未写 id）。
- 应用方式：`GetString` 被 hook（查表代码 `0x10013D68`）——`cmp edi,[eax]` 拿调用方传进来的**字符串池 id** 比表里的 id，命中后按 SSO/堆指针规则取字符串（`add eax,4` 后 `[eax+0x14]` 是 capacity，`<0x10` 用内联缓冲）。id 空间与 EXE 字符串池完全一致，1865 条**全部**能在池里找到英文原文，可逐条对账。
- 另有三种直接补丁：`0x100165C0(dstExe, 中文cstr)` 整串覆盖、`0x10016160(dstExe, srcStrObj, len)` 按长度覆盖、`0x100163E0(dstExe, byte)` 改单字节（改的是代码，不是文案）。这些调用都被 `cmp byte ptr [0x1003657C],0 / je` 包住。

复现（解析脚本思路，不依赖线性反汇编——`.text` 在 `0x1000121C` 附近的 `c7 05` 数据会被 capstone 解错位，必须走字节模式）：

1. 扫 `.text` 里 `B9 <imm32>` / `8D 0D <imm32>`，imm32 落在 `0x10036948..0x10043AA0` 即为表对象引用；
2. 每个对象向前找最近的 `68 <imm32>`（`.rdata`/`.data` 指针）作字符串起点，**按 NUL 截断**取长度（不能信 `push imm` 的那个长度，容易抓到上一条的）；
3. `C7 05 <addr> <imm32>` 即 id 写入，`idAddr = strObj - 4`；
4. 与 `tools/inspect_beidou_client.py --strings` 导出的英文池按 id join。

导出结果：`references/string-pool-zh.json`（1865 条 `{id,en,zh}`，233 KiB）。

已知空缺（源端也保持英文，迁移时不要自作主张翻译）：`[Current Level %d]`(674)、`[Next Level %d]`(675)、`HP :`(2046)、`MP :`(2047)、` CLAW`(614)、`Unique Equipped Item`(5275)、`Movement is possible only within the account`(5197)。装备属性行的完整中文见 `inventory-and-tooltip.md`。
