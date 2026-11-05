# 个人商店 PlayerShop（514xxxx 营业执照）

源端 `gms-server`：

| 位置 | 作用 |
| --- | --- |
| `server/maps/PlayerShop.java` | 店铺对象：3 个访客位、16 格货架、成交记录、黑名单、聊天记录 |
| `server/maps/PlayerShopItem.java` | `item` / `bundles` / `price` / `doesExist` 四字段 |
| `net/server/channel/handlers/PlayerInteractionHandler.java` | `CREATE`(4/5)、`ADD_ITEM`、`REMOVE_ITEM`、`OPEN_STORE`、`BUY`、`BAN_PLAYER`、`CHAT`、`EXIT` |
| `client/Character.java closePlayerShop()` | 关店把货退回背包 |
| `constants/inventory/ItemConstants.java` | `isPlayerShop: itemId / 10000 == 514`、`isHiredMerchant: /10000 == 503` |

本项目：`Assets/Scripts/Server/World/NativePlayerShopService.cs` +
`Assets/Scripts/NativePlayerShopWindow.cs`，回归 `tools/server-check/PlayerShopChecks.cs`。

## 一个 CREATE 包分两个系统

`CREATE`(0x00) 的 `createType` 4/5 **都走同一段代码**，真正分派靠营业执照 id：

```java
if (ItemConstants.isPlayerShop(itemId))        new PlayerShop(...)   // 514xxxx
else if (ItemConstants.isHiredMerchant(itemId)) new HiredMerchant(...) // 503xxxx
```

所以双击 514 开个人商店、双击 503 开雇佣商店，客户端不用区分 createType。

## 与雇佣商店的三处关键差异（别照抄 NativeMerchantService）

1. **纯内存对象，不落库**。源端 `PlayerShop` 没有数据库表；`closePlayerShop` 不写任何寄存，
   也就**没有 Fredrick 取回、没有过期、没有每天 1% 衰减**。掉线 / 换图 / 关窗即关店。
   `Character.changeMap` 里那句 `closePlayerInteractions()` 就是换图关店的来源。
2. **许可在开张时才扣**。`CREATE` 只做 `countById(itemId) >= 1` 校验（不消耗）；
   `OPEN_STORE`(0x0B) 在 `use_erase_permit_on_open_shop`（默认 **true**）时才
   `removeById(CASH, itemId, 1)`，扣失败**静默忽略**（`catch (RuntimeException)` 空块）。
   雇佣商店相反：**永远不消耗**许可。
3. **成交金额直接进店主身上的金币**（`owner.gainMeso`），不走 `characters.MerchantMesos`。

## `ADD_ITEM`(0x16 / 0x21) 校验顺序

包序：`ivType(byte) → slot(short) → bundles(short) → perBundle(short) → price(int)`。

1. `item == null || item.isUntradeable()` → 拒绝。
   `isUntradeable` = `flag & 0x08` **或** (`dropRestricted` 且没有因果剪刀标记
   `flag & (装备 0x10 / 其它 0x02)`)。
2. `isUnmerchable(itemId)` → 拒绝。= `use_enforce_unmerchable_cash && 现金道具`
   或 `use_enforce_unmerchable_pet && 宠物`。
3. 可充能（`itemId/10000 == 207` 或 `233`）→ `perBundle = bundles = 1`；
   否则 `item.quantity < bundles * perBundle` → 拒绝。
4. `perBundle <= 0 || perBundle * bundles > 2000 || bundles <= 0 || price <= 0` → 拒绝
   （源端还会 `AutobanFactory.PACKET_EDIT` + 直接断开连接）。
5. `shop.isOpen()` → 拒绝；`items.size() >= 16` → 拒绝。
6. 从背包扣 `bundles * perBundle`（可充能整组扣）。

## 取回 / 关店

- `REMOVE_ITEM`(0x1B) 与 `PlayerShop.takeItemBack`：**只有关店状态**能取回，
  数量 = `每份数量 × 份数`，取回后这一格 `items.remove(slot)` —— **后面的格子前移**，
  不像雇佣商店留空位（那边是 `merchantitems.slot` 定号）。
- `EXIT`(0x0A) → `closePlayerShop()`：
  - `bundles >= 2` → 给 `bundles × item.quantity`
  - `bundles == 1 && isExist` → 给 `item` 原样（可充能的充能值就在 quantity 里）
  - 其余（`bundles == 0`，已卖光）→ 什么都不给
- **卖空自动关店**：`if (++boughtnumber == items.size())` → 关店 + 提示
  「Your items are sold out, and therefore your shop is closed.」

本项目的唯一取舍：源端 `addFromDrop` 装不下会把道具**掉在地上**，这里改成
「装不下的留在店里 + 拒绝关店」，让玩家清空背包后重开窗口取回，避免静默丢货。
换图/掉线时如果装不下，店铺对象保留并把 `mapId` 挪到当前图（源端是掉在旧图地上）。

## `canPlaceStore`（开店前置）

- 23000 范围内不能有别的 `PLAYER`（带店主身份的 PlayerShop）或 `HIRED_MERCHANT` 对象 → 错误码 13。
- 最近的**传送门**（`type == TELEPORT_PORTAL(1)` 且 `targetMap` 有效）距离 `< 120` → 错误码 10。
- 非自由市场（`mapId / 1000000 == 910 && mapId > 910000000`）→ 错误码 15。
- 存活 / 棋盘 / 事件副本由 `establishMiniroomStatus` 挡（单机没有棋盘与副本实例，留了接口位）。

## 窗口

`UI/UIWindow.img/PersonalShop`（反汇编结论，见 `solomapling.md`）：

| 素材 | 尺寸 | 用途 |
| --- | --- | --- |
| `backgrnd` | 525x553 | **个人商店**底图（514 用这张） |
| `backgrnd6` | 525x553 | 雇佣商店底图（503 用这张） |
| `backgrnd2` / `backgrnd3` / `backgrnd4` / `backgrnd5` | 233x143 / 233x276 / 203x270 / 267x270 | 子对话框（上架 / 黑名单 / 访客 / 已售出） |
| `select` | 162x35 | 行选中底 |
| `soldout` | 199x35 | 原素材存在；购买窗售罄实际为0x80FFFFFF遮罩，见2026-11-04补核 |
| `bar` | 103x17 | 聊天输入条 |

坐标（与 `NativeMarketWindow` 共用同一套）：

- 商品可视 **5 行**，`y = 157 + 40 * i`
- `select` 在 `(44, y)`，icon 锚点 `(8, y+34)`，名称 `(48, y+1)` 裁 152px
- 列表滚动条 `(211, 158, 15, 197)`
- `BtBuy (144,90)` / `BtExit (144,110)` / `BtInfo·BtItem (144,62)`
- 聊天输入 `(238, 323) 231x15`

按钮图的语义没有反汇编依据（`BtAdd` / `BtItem` / `BtStart` / `BtBan` / `BtBlackList` …），
店主专属动作仍用中文文字按钮，避免猜图意。

## 入口

- 双击 `514xxxx`（营业执照，`String.wz/Cash.img`：5140000「营业执照」、5140001..5140006 各色个人商店）
  → 输入店名 → `CREATE` → 窗口以「准备中」打开 → 上架 → 「开始营业」。
- 点自家店招：`NativeMarketField.TryVisit` 识别 `playerStore && ownerId == 自己` 后
  直接重开自己的窗口（源端 `visitShop` 也不拦店主）。
- `Item/Cash/0514.img` 只有 `info`，**没有 `skin/signboard`**，所以店招走
  `NativeMarketField` 的 0503 默认皮肤兜底。

## 回归

`tools/server-check/PlayerShopChecks.cs`（41 项）：非自由市场 / 无许可 / 重复开店、
上架三类拒绝、不可交易 flag、开张扣执照、营业中锁定、假人成交（手续费 + 直接进身上金币）、
份数递减、卖空自动关店、取回数量与列表前移、关店退道具、背包满拒绝关店、换图关店、
黑名单 / 聊天、掉线退货。
