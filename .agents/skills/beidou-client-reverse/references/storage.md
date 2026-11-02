# 仓库 Trunk（Storage）

> 源端：`gms-server` `server/Storage.java`、`server/StorageInventory.java`、
> `client/processor/npc/StorageProcessor.java`（storageAction mode 4/5/6/7/8）、
> `CashOperationHandler.java` action 0x07（现金买仓库格）、`PacketCreator.getStorage`(STORAGE 0x16)、
> `KarmaManipulator.toggleKarmaFlagToUntradeable`、`ItemId.isWeddingRing/isWeddingToken`。
> 客户端窗口 `UI/UIWindow.img/Trunk/`（463x318 双栏）。项目：`Server/World/NativeStorageService.cs` +
> `NativeStorageWindow.cs` + `tools/export_trunk_fees.py`。

## 服务端语义（照抄点）

- **15 级门槛**：`sendStorage` 与每个 storageAction 都检查（单机在 Handle 里统一拦）。
- **费用**：`getStoreFee` = `Npc.img/<id>/info/trunkPut`（默认 **100**），`getTakeOutFee` = `info/trunkGet`（默认 **0**）。
  实测 50 个 NPC 带节点：自由市场仓库 9030100/9031016 = **500/1000**，2020004=150，
  9390221 put=100 get=5000000 等 → 用 `tools/export_trunk_fees.py` 全量导出到
  `Assets/Resources/Storage/TrunkFees.txt`（`F|npcId|put|get`），server-check 经 csproj CopyToOutput 读同一份。
  FeeProvider 在**客户端 GameSession 初始化**注入（server-check 工程不含 UnityEngine，GameServer 必须保持纯 C#）。
- **错误码**（getStorageError）：0x0A 背包满 / 0x0B 金币不足 / 0x0C 唯一道具身上已有 / 0x11 仓库满。
- **mode 5 存入**：slot 1 基；`isFull`（items.Count >= slots，初始 4 格）→ 0x11；扣 trunkPut；
  `isRechargeable`（207/233）**强制整组入仓**（quantity 参数被忽略）；婚戒 1112803/06/07/09 与订婚盒
  4031357..4031364 禁入；存入后 `toggleKarmaFlagToUntradeable` —— 带 KARMA_EQP(0x10)/KARMA_USE(0x02) 的
  道具换掉 karma 位、补 UNTRADEABLE(0x08)（取出时同样调用 → 进过仓库的可交易标记消失）。
- **mode 4 取出**：同类型列表下标 0 基；唯一道具（info/only=1）身上已有一件 → 0x0C；扣 trunkGet；
  `checkSpace` 不过 → 0x0A；取出行直接删除（源端 items.remove，无空洞）。
- **mode 7 金币**：`meso>0` 取出（仓库余额 ≥ meso）、`meso<0` 存入（身上 ≥ -meso）。
  源端 Integer.MIN/MAX 溢出钳制两分支在项目 long 金币下不可达，省略。
- **mode 6 整理**：`use_storage_item_sort`（默认 **false**）关着时是无操作；开着走
  `StorageInventory.mergeItems`（同 id 同 owner、非充值非唯一才合并，equip/cash 交换位置，
  普通叠数量超上限拆开）+ `sortItems`（inventoryType 主序 + itemId，`use_item_sort_by_name` 时按名字）。
- **槽位**：`canGainSlots` 上限 **48**；现金商店 action 0x07 买 4/8 格（`IncreaseSlots`）。

## 存储落地（与源端刻意差异）

源端用独立 `storageitems` 表（ItemFactory.STORAGE）。项目把仓库物品行放 `inventoryitems`：
`characterid = -3`（仓库标记；快递 -1 / 雇佣商店寄存 -2 已占用）+ 动态列 `storageaccountid` 按账号隔离。
`storages` 表 `{ accountid, slots=4, meso=0 }`（源端 INSERT 同款初始值）。写行复用
`NativeMerchantService.WriteEntry`（internal）+ 补 storageaccountid；读行走 `CharService.ToInventoryEntry`。

## 脚本面

25 个仓库 NPC 脚本全部只有一句真实调用：`cm.getPlayer().getStorage().sendStorage(cm.getClient(), <npcId>)`
→ `NpcApi.BuildStorage` 里 `NativeStorageWindow.Open(Int(c,1))`（arg0=client、arg1=npcId）。
其余 `sendStored/sendTakenOut/sendMeso/arrange/close/getSlots/getMeso/getItems` 只是兜底占位（脚本没用到）。
`545xxxx` 是**妙妙的旅行商店**（ShopFactory 1338），不是随身仓库 —— 项目 545 分支已实现且一致。

## 窗口（UI/UIWindow.img/Trunk/）

- `backgrnd` 463x318 **双栏**：左栏仓库（select 162x35 选中行）、右栏背包（en 199x35 行底）。
  列表 4 行，行分隔虚线 y=120/164/204/244（像素检测）→ 行 y=84+40*i；金币条 y≈288..314
  （左 BtOutCoin (4,290) 取、右 BtInCoin (236,290) 存，「金币」字印在底图上）。
- `Tab` enabled/disabled 各 **5 张** 27x16/27x14 → 左栏顶部分类过滤（装备/消耗/装饰/其他/现金，tab<0=全部）。
- 按钮（文字全在图上）：BtGet 70x19「掏出道具」、BtPut「道具保管」、BtExit「退出仓库」、
  BtGetAll「掏出道具」（逐个取出循环）、BtSort 71x18「道具排列」、BtCoin 36x18 金币图标。
  BtGetAll/BtSort 与 BtPut/BtExit 排在右栏描述区 (246/320, 24/46)。
- 没有关闭钮素材：右上角自画 × 热区（与 itemSearch 同做法）。

## 接线

`ITransport.Storage`；`GameWindows`（Panel.Storage 全套 + Draw）；`GameSession` 三处 Reset；
`NpcApi.BuildStorage.sendStorage` 真开窗。服务端无 per-session 状态，Close 是 no-op。
