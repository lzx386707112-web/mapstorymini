# 十二项菜单反馈补核（2026-11-02）

## 身份与证据

源文件 `Beidou-Server/clien/BeiDou.exe`，ImageBase `0x400000`，SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。
证据归档 `docs/menu-feedback-2026-11-02/`。使用 `tools/inspect_beidou_client.py <exe> --fn <VA>` 递归反汇编；不能将中途地址线性反汇编当作完整函数。

## 已核实的布局纠正

- Trunk OnCreate `0x7C618E`、Draw `0x7C76FD`。按钮 Exit(154,15)、Get(154,35)、Put(384,15)、Sort(154,55)、OutCoin(5,294)、InCoin(236,294)。左栏5行 y87+40i；右栏4行 y127+40i。右栏 tab (235,92), width222；滚动条左(210,88),h197，右(441,127),h158。余额 y296，右对齐终点190/418。NPC锚点(53,76)，玩家(288,75)。坐标均相对窗口。
- 商店 NPC `0x754860` 锚点(56,76)，玩家 `0x754779` (285,76)，tabs y95。
- **纠正 style2=Tab3，不是 Tab2**：`0x4DE4D0` type1追加2、type2追加3。`UI/Basic.img/Tab3` 两端6、中接12、height21；`0x4DE15F` 布局 inner=floor((width-(n-1)*12-12)/n)，余宽逐一分给前面的格。固定宽度 inner=fixedWidth-8。`0x4DD903` 绘制，标签 y=10-floor(height/2)+2。旧 Tab2 4/8px 图会产生接缝。
- UtilDlg `0x992BFD→0x8D51D4→0x992EDA` 默认居中。Draw `0x99311B` 文本行距14，middle=max(2,floor(lines*14/20)+1)，y=24+trunc((middle*20-lines*14)/2)，每行在 x15,width234 内居中。换行宽200。OnCreate `0x991899` type1000 用 Notice3，确定(205,h-30)；type1001 YesNo3，确定155、取消205。
- 滚动条 `0x4D7D60→0x4D7E4D→0x4D8189` 点击空轨 delta=sign*max(16,range/16整数)。NPC滚动单位8px，range=floor((content-body)/8)+1。当前移植点击空轨；不据此宣称按住重复完整对齐。

## 脚本/API

源脚本保持原样，只有用户要求删除重复入口的 `9900001.js` 使用 override。
血衣最后配方7项分别300,50,50,20,5,5,1，材料4000313,4000152,4000151,4005004,4011007,4021009,1012190。解释器补 Array instanceof/Array.from/Map/Number.isInteger，修复数组与去重流程。
一键出售依赖 ShopFactory.sell、UNDEFINED、ii.getPrice；删除道具按真实库存槽移除。掉落查询 getMobs/getAllMonsters 返回 JavaList，并提供怪物名字、等级、Boss属性。
每日签到逐项预检不能保证后续发奖均有空间；源 `AbstractPlayerInteraction.java:668` gainItem 背包满调用 dropMessage(1)，type1须为持续 Notice 而非 Toast。

## MTS：纠正旧结论及迁移缺口

`9900001.js` 拍卖系统直接 Java.type(EnterMTSHandler).enterMTS(client)。服务端 `EnterMTSHandler.java:51` handlePacket 才检查 use_mts；第59行静态 enterMTS 不检查。事件实例/迷你地城/CANNOTMIGRATE/死亡/等级检查后，openCashShop(c,true) 进入 MTS。不能用开关关闭解释成菜单自循环。

客户端资源 `UI/ITC.img`。stage Init `0x59E771`：角色子窗(0,0,256,200)，未售(0,200,256,110)，中转(0,310,256,108)，背包(0,418,256,180)，顶栏(272,17,509,78)，说明(273,98,509,48)，列表(273,145,509,365)，底栏(255,531,545,56)。角色窗 OnCreate `0x5B0B8C`，Base/Preview/0 在(23,34),213x109。`0x59E63E` 是析构，不能误标构造。

服务端 MTSHandler：金币模式，比例10000，税10%，挂牌5000，价格110..180000，有效7日；购物车/撤单/中转领取/分页搜索均为独立业务。**当前尚未迁移 ITC 界面与交易系统，第11项未完成**。只移除错误菜单回环与静默成功，缺口明确报错；不得声称已修复拍卖。

## 传送与待核实

GenericPortal→Character.changeMap→getWarpToMap SET_FIELD携带地图ID/目标portal。104000000 的 h_west01(pt10) 与 h_east01 同图。当前同图走直接定位、跨图保留淡入淡出。**EXE SET_FIELD同图分支、相机具体行为尚待核实**，不能把服务端条件等同完整EXE验证。
对话未知# token已避免吞掉正文；原端对未知中文token是否保留#显示尚待完整核实。数量输入布局已有9AC732/9AC7CB→9ACD54、9A83F6依据，本轮尚缺截图对齐验收。

## 实际验证与边界

脚本运行时43项通过；打包2779脚本 parseFail0/mangled0；完整mock执行保留27 runFail及72 JavaInterop基线，不能声称全量脚本通过。
Unity隔离批处理 NativeMenuFeedbackChecks 12项通过：原血衣配方、真实槽出售/删除、签到满包、活怪列表及掉落、兑换正文、持续Notice。日志见归档。Unity编译已过；此记录不替代后续代码变更的编译检查。
**尚未验收**：Android实机、仓库双击真实指针回归、窗口截图、滚动条点击、传送表现、完整MTS。不能将已加代码等同十二项全部完成。
