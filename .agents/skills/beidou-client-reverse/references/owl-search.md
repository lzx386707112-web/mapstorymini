# 物品搜索（密涅瓦的猫头鹰，523xxxx）

> 源端：`gms-server` `UseCashItemHandler.java:428-444`（itemType==523 主流程）、
> `UseOwlOfMinervaHandler.java`（打开搜索框）、`OwlWarpHandler.java`（点结果跳店）、
> `World.java:1337-1365`（owlSearched 热度表）、`World.java:1917-1940`（getAvailableItemBundles）、
> `PacketCreator.java`（owlOfMinerva type6 / getOwlOpen type7 / getOwlMessage=SHOP_LINK_RESULT）、
> `ItemId.java:392`（getOwlItems）。客户端窗口 `UI/UIWindow.img/itemSearch/`。
> 项目：`Server/World/NativeOwlService.cs` + `NativeItemSearchWindow.cs`。

## 服务端三个照抄的行为

1. **搜到才扣券**：`if (!hmsAvailable.isEmpty()) remove(c, position, itemId)` —— 空手而归不消耗
   523xxxx。项目里由客户端在 `charged==true` 时 `Profile.RemoveItem` 扣（与同类现金道具一致）。
2. **结果按单价升序、最多 200 条**：`getAvailableItemBundles` 扫 `getActiveMerchants() + getActivePlayerShops()`
   全部在售货架，`sort(price)` 后截 200。每条带：ownerName / mapId / description（商店名）/
   bundles（组合=份数）/ quantity（每份数量）/ price / ownerId / channel-1 / 装备才带 addItemInfo。
   ⚠️ 源端 `hmsAvailable.subList(0, min(size,200))` 那行**没接返回值**（bug），项目按意图实现为真截断。
3. **推荐位**（打开窗口 type7）：`owlSearched` 种类数 `< 5` → 固定 11 件
   `{1082002,2070005,2070006,1022047,1102041,2044705,2340000,2340001,2040017,1092030,2040804}`；
   `≥5` 种 → 按搜索次数降序取前 10（PriorityQueue 顺序不定，项目补 itemId 升序保证可复现）。
   `use_enforce_item_suggestion`（默认 false）为真时：不记热度（getOwlSearchedItems 返回空表）且推荐位恒空。

## 跳店（OwlWarp，SHOP_LINK_RESULT 消息码）

- 先判 `ownerId == 自己` → serverNotice「不能访问自己的店」；
- 找店：HiredMerchant 优先（同图同主时），都没有 → **码 1**；有但不匹配（mapId 不对 / 没这货）→ **码 3**；
- 未开张 → **码 18**（维护中）；满员 → **码 2**、被拉黑 → **码 17**（需要第二个真人，单机恒不触发）；
- 通过 → `changeMap(mapid)` 后 `addVisitor` 再发店窗（换图有延迟要复查 open）。
- 单机等价：`NativeOwlService.Warp` 只实现 1/3/18，成功返回 `map`；客户端 `field.WarpTo(map)` 后
  在 `Tick()` 里等 `Profile.LastMapId == map` 再 `NativeMarketWindow.Visit(ownerId)`
  （服务端 `service.Visit` 校验 `actor.mapId == shop.mapId`，必须等换图完成）。

## 单机数据源

`MarketShopService.AllOpen()`（新增）一个面就等价源端两个集合：假人开的店、玩家雇佣商店镜像
（`NativeMerchantService.SyncMarket`）、玩家个人商店镜像（`NativePlayerShopService.SyncMarket`）全在 `shops` 表里。
`MarketProvider` 在 `GameServer` 两处启动路径与另外两家服务一起注入。

## 窗口素材（UI/UIWindow.img/itemSearch/）

- `backgrnd` 248x271 入口页：气泡输入框（白框 x=25..167, y=95..118）、BtSearch 38x17、
  底部两条原图文字「人气查询前十名」(y≈211..236)、「分类查询留言」(y≈240..265)（蓝色行检测定位）。
- `resultback` 438x430 结果页：顶部大输入框 (y≈75..135)、列标题带 y≈140..150
  （原图印「角色名称 商店名称 组合 价格 数量 道具资料」）、8 行行高 30（虚线间隔）；
  底部蓝条 y≈390..426 放按钮。`BtInfo` 55x14 是「道具资料」列标题的独立图。
- `categoryback` 270x271 / `searchback` 205x271：分类与道具列表（searchback 底部印「高价排序」）。
- 按钮全部带中文：BtSearch/BtRegist 38x17「搜索/登录」、BtSearch2/BtOK/BtCancel/BtBack/BtRetry/BtLink
  59x17「搜索/确定/取消/返回/重新搜索/捷径」、BtGo 43x17「GO▶」、BtLeft/BtRight 13x13 ◀▶。
- 没有关闭钮：标题栏右侧自画 × 热区。
- 4 个页面：Entry（输入+推荐入口）→ Results（结果表，BtGo/双击行跳店）、Hot（人气前十）、
  Category→Items（分类浏览按名字过滤，客户端本地功能，等价原版「分类查询」，用 `BotItemCatalog`）。

## 接线

`ITransport.Owl`；`GameWindows`（Panel.ItemSearch / AnyVisible / VisibleOf / ClosePanel / CloseTop /
DrawAll 的 Tick+Draw）；`GameSession` 会话重置 Reset；`NativeInventoryActions` 双击 `itemId/10000==523`
开窗（注意 `NativeOwlService.Reset` 清热度表是**进程级**语义，别挂到换角色——源端 World 级）。
