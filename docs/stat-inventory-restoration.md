# 角色能力与背包还原

2026-10-22。按用户确认：“新手不能加点”指AP，保留新手技能的原规则。

## 分析依据

- BeiDou.exe `CUIStat::Draw` 0x008C59FF：基础信息x61，y33/55/79/96/115/133/151/169。
- 0x008C617E–0x008C61A8：新手职业、等级≤10走说明面板分支。
- 0x008C62AF / 0x008C62B5：`Stat/basicStat`绘制于(8,195)，不是猜测的(12,202)。
- `UI/UIWindow.img/Stat/backgrnd` 208×347；`backgrnd2` 218×203，详情面板按截图底部对齐，y偏移144。
- `Stat/BtDetail`实际46×19，旧实现拉伸为75×18，已修正；职业图标使用Job/main与Job/sub原图。
- 背包 `Item/backgrnd`175×289、`FullBackgrnd`603×289、`disabled`32×32。
  展开布局为四组4列×6行，共96格，前三个容量区块不因展开而解锁。
- 标题栏`BtFull/BtSmall`负责展开/收起，`BtGather/BtSort`负责整理按钮，`BtCashshop`为“扩充道具栏”。

## 服务端规则与实现

`org/gms/client/Character.java:levelUp`：启用use_auto_assign_starters_ap时，在升级前等级<11且为新手，
自动消费5AP；旧等级<6全加STR，否则4STR+1DEX。与客户端“≤10不能手动分配”分开处理。
`V1.7.0__create_game_config.sql`默认启用此规则，max_ap=32767。

本地LevelSystem按此分配；Profile.AssignAp拒绝新手≤10级、无AP、非法属性和达到上限的请求。
转职后四项基础属性按钮消费1AP并保存；战斗重新读取已分配属性。
旧版未分配的新手AP按前10次升级额度迁移，不清空等级、经验和其他成长；标记保证不重复。
已转职旧存档缺少其新手成长历史，因此不猜测扣除既有AP。

`Character.canGainSlots/gainSlotsInternal`上限96。`CashOperationHandler`的0x06：普通购买2000点券+4格，
指定商城道具模式+8格。展开视图不修改容量、不消费金币。
当前恢复大小视图切换、分类、锁定格、已有武器和掉落物显示、数量、装备点击、金币与整理排序。
不在背包重复显示当前装备中的一把武器；其余物品使用FieldCatalog的原图。

## 验证与边界

PanelRestorationChecks使用独立内存角色，不写测试角色到真实存档：
- 新手Lv1→6：STR12→37、AP0；→11：STR57、DEX10、AP0；转职后升级获得5AP。
- 旧存档AP迁移与重复调用检查。
- 新手手动AP被拒绝；转职后加点成功；AP不足与32767上限被拒绝。
- Unity播放模式真实截图：stat-window-beginner-restored.png、stat-window-job-restored.png、
  inventory-small-restored.png、inventory-full-restored.png。

尚未完成的原服系统没有假装可用：点券钱包/商城购买扩容尚未接入，扩充按钮说明原规则；
HP/MP的AP转换按钮仍禁用（该逻辑涉及职业被动和洗血计数）；自动推荐AP按钮未接入。
非装备分类容量目前24，特殊96，装备读现有equipmentSlots。背包物品模型仍为原有本地聚合记录，
未迁移原服完整装备实例、逐堆叠上限和位置协议。
详情数值读取当前本地角色与武器；衣裤等完整装备加成未迁移，因此不伪造截图的7点物防/4点魔防。
职业原图当前资源为中文，参考截图BEGINNER为英文；动态字体沿用项目字体，非Windows逐像素字形。
未构建APK或做安卓设备验收。

## 11级参考图修正

按新增11级原端截图，六个加点箭头统一右移到x185（原实现x153偏左32px），
y仍为117/135/247/265/283/301；绘制与点击矩形使用同一常量。
AP数字在(61,213)的26px框内右对齐；自动分配禁用按钮置于(97,197)，显示73×35。
未转职新手的INT/LUK标签覆盖原始Disabled/INT、Disabled/LUK素材。
原exe静态坐标与该截图不同，此处以用户指定的运行截图为准。
自动分配按钮继续保持禁用，不在本次位置修正中引入新的分配逻辑。
