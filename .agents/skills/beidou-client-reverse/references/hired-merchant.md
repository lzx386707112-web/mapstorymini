# 雇佣商店 HiredMerchant

日期：2026-11-02。源端 `gms-server`（`PlayerInteractionHandler` + `HiredMerchant` + `FredrickProcessor`）。

## 一、可达性核对（先做这一步，别照 877 行全抄）

`PlayerInteractionHandler.java:236-268` 的 CREATE 分支里
`else if (createType == 4 || createType == 5)` 就是开玩家商店：
`ItemConstants.isPlayerShop(itemId)` = `514xxxx`（个人商店 PlayerShop）、
`isHiredMerchant(itemId)` = `503xxxx`（雇佣商店 HiredMerchant）。
开店要求：地图是自由市场房间（`mapId/1000000 == 910 && mapId > 910000000`，否则 MiniRoomError 15）、
没有已开的店、**持有**许可道具（`countById >= 1`，**不消耗**）、描述字符串。

> 教训：用 `grep "a\|b"` 在 macOS/rtk 下会静默返回空，一度误判「源端没有 new HiredMerchant」。
> 一律用 `rtk proxy grep -rn -E "a|b"`。

## 二、状态机（PlayerInteractionHandler 操作码 → 本项目枚举）

| 源端 | 码 | 行为 | 本项目 |
| --- | --- | --- | --- |
| CREATE(0) | 0x00 | 自由市场开店，`hiredmerchants` 行 + `HasMerchant=1` | `MerchantOperation.Create` |
| ADD_ITEM(0x16) | — | 营业中拒绝（message11）；现金道具开过张后拒绝（message12）；最多 16 格；`perBundle*bundles<=2000` | `AddItem` |
| TAKE_ITEM_BACK(0x26) | — | 只有停业能取回，数量 = 每份 × 份数 | `TakeItemBack` |
| MERCHANT_MESO(0x2B) | — | `Character.withdrawMerchantMesos()` | `WithdrawMesos` |
| MERCHANT_ORGANIZE(0x28) | — | 提金币 + 清空卖空格；空了直接关店 | `Organize` |
| CLOSE_MERCHANT(0x29) | — | 关店，能装下就退货，装不下留寄存 | `Close` |
| MAINTENANCE_OFF(0x27) | — | 有货 → `open=published=true`；没货 → 直接关店 | `MaintenanceOff` |
| EXIT(0x0A) | — | `closeHiredMerchant(true)`：**只有店主且货架为空**才删店，否则只离开窗口 | 窗口 `Close()` |
| BUY(0x17)/MERCHANT_BUY(0x22) | — | 价格×份数，卖价先扣 `Trade.getFee` 再进店主 | `BotPurchase` |
| VIEW_VISITORS / VIEW_BLACKLIST / ADD / REMOVE | — | 访客历史与黑名单 | `AddBlacklist`/`RemoveBlacklist`（只在内存里，关店即失效，与源端一致） |

`open` 的语义是「营业中」：刚建店是 `false`（准备中，可编辑货架），`MAINTENANCE_OFF` 或店主离开窗口
才变成 `true`。**营业中不能上架也不能取回**。

## 三、金币

- 成交：`proceeds = price - Trade.getFee(price)`，累加到 `characters.MerchantMesos`（截到 int）。
- 提取：`Character.withdrawMerchantMesos()`。净额 = `merchantmeso × (100 - 寄存天数) / 100`，
  寄存天数来自 `fredstorage.timestamp`，最多 100 天 —— 也就是 Fredrick 每天抽 1%。
- 负数（店铺欠款）：抵消逻辑照抄（`playerMeso + merchantMeso < 0` 时把人物金币清零，欠款留店里）。

## 四、Fredrick 寄存

- 关店时背包装不下的货 + 没提走的金币 → `fredstorage(cid, daynotes, timestamp)`；取空后删登记。
- `FredrickHandler` 0x1A = 取回：先 `checkSpotsAndOwnership` 全量试算（唯一道具用 `info/only` 区分
  0x22/0x20），再看金币上限 0x1F / 欠款 0x21，最后 `withdrawMerchantMesos` + 删物品行 → 0x1E。
- 提醒节奏 `dailyReminders = {2,5,10,15,30,60,90,∞}` 天，`>100` 天连物品带金币一起清。
- 本项目入口：NPC `9030000`（`NpcId.FREDRICK`）→ `NativeMerchantWindow.Retrieve()`。

## 五、持久化

| 源端 | 本项目 |
| --- | --- |
| `inventoryitems`（`type=ItemFactory.MERCHANT=6`）+ `inventorymerchant.bundles` | `inventoryitems`（`characterid=-2` 表示店内存货，快递用 -1）+ `merchantitems(slot,bundles,price)` |
| `characters.MerchantMesos` / `HasMerchant` | 同名，已存在 |
| `fredstorage(cid,daynotes,timestamp)` | 同名（timestamp 存毫秒） |
| `HiredMerchant.sold`（内存） | `merchantsold` 表 |

注意本项目 `inventoryitems.type` 已经表示「道具大分类」（`itemId/1000000`），所以不能沿用源端的
`type=6` 当「商店存货」标记。

## 六、假人顾客（单机里唯一的「别人」）

`NativeMerchantService.SyncMarket` 把店主自己的店镜像成 `Server.Bot.MarketShop`
（`playerStore=true` + `playerSell` 回调），于是市场里现有的假人逛店/购买逻辑会直接来买：
`MarketShopService.Buy` 里 `playerOwned` 分支先走 `CanBuyPlayerStore` 预检，再
`trades.Purchase` 给假人发货，最后 `playerSell(merchantSlot, quantity)` 把实收写回
`MerchantMesos`。`playerStore` 且 `playerSell == null` 的仍是原来的「假人开的玩家式店铺」。

## 七、窗口

店主窗口用 `UI/UIWindow.img/PersonalShop`（`backgrnd6` 525×553，与市场窗口同一张底图）；
店主专属按钮在 `UI/UIWindow.img/EntrustedShop` 只有 `BtArrange / BtArrange2 / BtArrange_3 / BtCoin`
四张图、没有底图也没有文字，按位置猜语义风险高，所以本项目用中文文字按钮，动作与上表一一对应。

`结束营业`（`MerchantOperation.SetOpen`）是移植补充：源端要回到「可编辑货架」只能整店关掉重开，
移植里给了单向的运营开关，便于改价；除此之外语义与源端一致。

## 八、回归

`tools/server-check/MerchantChecks.cs`（38 项）：非自由市场/无许可/重复开店、商店盒三态、
上架参数四类拒绝、营业中禁改、假人成交（含手续费）、提金币、取回数量、空店关店、背包满寄存、
15+16 寄存取回、10 天衰减到 90%、`hired_merchants_exist_time` 过期删店。

## 282 强化迁移联动（2026-11-03）

`NativeMerchantService.AddItem` 原来直接复制装备 payload，未经过交易规则，因此可绕过 282 魔方待选/装备痕迹保护。新增在 `WriteEntry` 和扣背包之前调用 `NativePotentialService.BlocksTransfer`，拒绝时不创建 merchantitems/payload、不移除原装备。此处是项目 282 状态保护修复，不是新增 BeiDou EXE 推断。`PotentialChecks` 已覆盖两个拒绝分支；普通商店的原有上架规则不变。
