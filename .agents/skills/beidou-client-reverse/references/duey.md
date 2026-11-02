# 快递 Duey（PARCEL）

日期：2026-11-02。源端 `gms-server`，客户端 `BeiDou.exe`（SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`）。

## 一、入口（服务端证据）

| 触发 | 源端 | 本项目 |
| --- | --- | --- |
| 点 NPC `NpcId.DUEY = 9010009` | `NPCTalkHandler.java:67` 直接 `DueyProcessor.dueySendTalk(c,false)`，**不查脚本** | `NpcSession.Talk` 顶部 `if (npcId == 9010009)` → `NativeDeliveryWindow.Open(false)`，绕开脚本（该 NPC 在 `npc_scripts.json` 里也没有脚本） |
| 用现金道具 `533xxxx`（快速投递券 5330000） | `UseCashItemHandler.java:491` → `dueySendTalk(c,true)`，**此处不扣券** | `NativeInventoryActions` 里 `itemId/10000==533` → `Open(true)`；券在真正寄出时才扣 |
| 客户端 BYTE 操作码 | `DueyHandler`：`TOSERVER_RECV_ITEM` 拉列表 / `SEND_ITEM` / `REMOVE_PACKAGE` / `CLAIM_PACKAGE` | 单机走 `DueyRequest.operation`（`Open/Quick/Send/Claim/Remove/Notify`） |
| 到件通知 | `PlayerLoggedinHandler.java:519` + `showDueyNotification`（只对在线收件人发，发完 `Checked=0`） | `DueyOperation.Notify` → `TakePendingNotification`，客户端每 10s 查一次并 Toast |

`dueySendTalk(c, quick)` 分别下发 `0x08`（列表）与 `0x1A`（快速寄件窗）。

## 二、费率与校验顺序（`dueySendItem`，一处不能改）

`Trade.getFee(meso)` 六档（整数除法向零截断）：

| 金额区间 | 费率 | 例 |
| --- | --- | --- |
| < 100000 | 0 | 99999 → 0 |
| ≥ 100000 | 0.8% | 100000 → 800 |
| ≥ 1000000 | 1.8% | 1000000 → 18000 |
| ≥ 5000000 | 3% | 5000000 → 150000 |
| ≥ 10000000 | 4% | 10000000 → 400000 |
| ≥ 25000000 | 5% | 25000000 → 1250000 |
| ≥ 100000000 | 6% | 100000000 → 6000000 |

校验顺序（照抄）：GM 等级 → `sendMesos<0` → 留言 > 100 字 → `fee = getFee(mesos)`，非快速 `+5000`、快速要求持有 5330000 → `finalcost = mesos+fee` 合法（`<0 || >int.Max || (amount<1 && mesos==0)`）→ 金币够 → 收件人存在 → 不能同账号 → 快速扣券 → 建包 → **扣金币** → 装箱。

错误码（`DueyProcessor.Actions`）：`0x09 SEND_ENABLE_ACTIONS`（建包/写库失败）、`0x0A NOT_ENOUGH_MESOS`、`0x0B SEND_INCORRECT_REQUEST`、`0x0C NAME_DOES_NOT_EXIST`、`0x0D SAMEACC_ERROR`、`0x12 SUCCESSFULLY_SENT`、`0x13 RECV_UNKNOWN_ERROR`、`0x15 RECV_NO_FREE_SLOTS`、`0x16 RECV_RECEIVER_WITH_UNIQUE`。

**金币是先扣后装箱的**：物品不可寄时金币不退（源端如此），所以「物品被拒绝」这条回归要断言金币已减、物品还在背包。

## 三、物品规则

- 不可寄：`Item.isUntradeable()` = `flag&0x08` 或 (`info/tradeBlock==1` 且没有 karma 位)；以及 `isUnmerchable` = `use_enforce_unmerchable_cash`（默认 true，`itemId/1000000==5`）/ `use_enforce_unmerchable_pet`（默认 true，`itemId/1000==5000`）。
- 装箱前 `KarmaManipulator.toggleKarmaFlagToUntradeable`：karma 位（装备 `0x10`、其它 `0x02`）换成 `0x08`。
- 可充能弹药（`itemId/10000==207`）：**整组从背包扣走**，但装箱数量仍取请求里的 `amount`（`setQuantity(amount)`）。这是源端的原始行为，照抄不修。
- 取件空间不足时，`isPickupRestricted(itemid)`（= `info/only == 1`）且背包已有一件 → `0x16`，否则 `0x15`。

## 四、表与过期

`V1.0.9__create_dueyi.sql` → 本项目 `Schema.sql` 的 `dueypackages`（packageid/receiverid/sendername/mesos/sendtime/message/checked/type）与 `dueyitems`（id/packageid/inventoryitemid）。
`DueyFredrickTask` 每轮跑 `runDueyExpireSchedule` → `NativeDueyService.Expire(db, now)` 清 30 天前的包裹（连 `dueyitems` 指向的 `inventoryitems`/`inventoryequipment` 一起删）。

## 五、窗口（UIWindow.img/Delivery）

节点实测：`backgrnd 290x348`、`sendTB 275x77`、`receiveTB 290x108`、`Tab/{enabled,disabled}/{0,1,2}`（60/36/30 × 12）、`tax`/`tax1` 290x200、`say0/1`、`line0/1/2 256x*`、`icon0..4`、`empty 143x18`、`npc0..2`。**这套图里没有任何按钮**，也没有「收件人/金币/留言」的输入框控件图。

关键坑：`BeiDou.exe` 的字符串池里**没有** `UI/UIWindow.img/Delivery/...` 这样的字面量（同类窗口走 `UI/UIWindow.img/%s/backgrnd` + 名字参数），所以拿不到控件创建函数的地址，坐标无法反汇编取证。当前实现按底图空区重排控件（`NativeDeliveryWindow.cs`），交互字段与源端请求一一对应；若以后找到窗口类入口，再按真实坐标替换。

## 六、回归

`tools/server-check/DueyChecks.cs`（21 项）：费率六档、寄件扣费、列表/取件/删包、越权取件、投递中、部分寄出余量、数量不足、`UNTRADEABLE`、现金道具禁止、快速票无票/有票/扣票/`type=1`、收件人不存在、同账号、留言超长、负数金额、空包裹、金币不足、背包满 `0x15`、30 天过期、到件通知只发一次、`use_duey=false`。

命令：`rtk proxy dotnet run --project tools/server-check --no-restore`。
