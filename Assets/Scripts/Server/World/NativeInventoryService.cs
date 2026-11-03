using System;
using System.Collections.Generic;
public enum ExtraStorageOperation { None, Read, Store, Withdraw }
public sealed class InventoryChangeRequest { public ExtraStorageOperation extraStorage; public int extraStorageType; public int characterId, itemId, quantity, equipSlot, sourcePosition; public int petSlot, evolvePetId; public int petTameness; public bool petLeaderOnly; public bool readPetExclusions, writePetExclusions; public int petId; public int[] petExclusions; public bool getPet, createPet; public long itemExpiration=-1; public bool equip, projectile, removeAll, togglePet, worldQuestDraw, petTick; public int grantItemId, grantQuantity, grantMinutes; public bool consumeGrantSource, breakthrough; public int bankExp, maxBankLevel; public long grantDurationMs=-1; public int[] checkItems, checkQuantities; public int removeCheckItem, removeCheckQuantity; public bool dropToField; public int dropQuantity, dropMapId, dropX, dropY; public long dropMeso; public bool sortItems, mergeItems; public int sortInventoryType; public int moveFrom, moveTo, moveInventoryType; }
public sealed class InventoryChangeResponse { public int extraStorageQuantity; public bool ok = true; public string error = "", message; public InventoryEntry pet; public List<int> petExclusions; public long damageCap; public int bankedExp, createdPetId; public bool inventoryChanged; public List<InventoryEntry> inventory; public long meso; }
public static class NativeInventoryService {
    static readonly Dictionary<(DbStore,int),long> worldQuestUse=new Dictionary<(DbStore,int),long>();
    static readonly Random worldQuestRandom=new Random();
    // Existing gameplay is client-authoritative; persistence still runs inside transport.
    public static InventoryChangeResponse Handle(DbStore db, InventoryChangeRequest r) {
        var c = CharactersDao.FindById(db, r.characterId);
        bool petOperation=r.readPetExclusions || r.writePetExclusions || r.getPet || r.petTick || r.evolvePetId>0 || r.petTameness!=0;
        if (c == null || r.itemId <= 0 && r.dropMeso<=0 && r.checkItems==null && !petOperation && !r.sortItems && !r.mergeItems && r.moveFrom<=0) return new InventoryChangeResponse { ok = false, error = "角色或道具不存在" };
        InventoryDao.ExpireItems(db,r.characterId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        // ITEM_SORT2 / ITEM_SORT：背包整理与合并，走 NativeInventorySort 的源端算法。
        if(r.sortItems) return NativeInventorySort.Sort(db,c,r.sortInventoryType);
        if(r.mergeItems) return NativeInventorySort.Merge(db,c,r.sortInventoryType);
        // ITEM_MOVE(action>0)：背包格到背包格的移动/合并/互换，同页限定。
        if(r.moveFrom>0 && r.moveTo>0) return NativeInventorySort.MoveItem(db,c,r.moveInventoryType,r.moveFrom,r.moveTo);
        if(r.extraStorage!=ExtraStorageOperation.None)return NativeExtraStorage.Handle(db,c,r);
        if(r.readPetExclusions || r.writePetExclusions) return NativePetItems.Exclusions(db,r);
        if(r.checkItems!=null) return new InventoryChangeResponse {ok=CanReceive(db,c,r)};
        if(r.dropToField) return DropToField(db,c,r);
        if(r.dropMeso>0) return DropMeso(db,c,r);
        if(r.removeAll)
        {
            InventoryDao.ConsumeItem(db,r.characterId,r.itemId,InventoryDao.CountOf(db,r.characterId,r.itemId));
            if(r.itemId/1000000==1)
            {
                var equipped=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==r.characterId &&
                    DbStore.Int(x,"itemid")==r.itemId && DbStore.Int(x,"inventorytype")==InventoryDao.Equipped);
                if(equipped!=null)
                {
                    long rowId=DbStore.Long(equipped,"inventoryitemid");
                    db.Table("inventoryitems").Delete(x=>DbStore.Long(x,"inventoryitemid")==rowId);
                    db.Table("inventoryequipment").Delete(x=>DbStore.Long(x,"inventoryitemid")==rowId);
                }
            }
            db.Save();return new InventoryChangeResponse {inventory=CharService.Load(db,r.characterId).inventory};
        }
        // ScriptedItemHandler.handleWorldQuest intercepts 2430680 before the item JS.
        if(r.worldQuestDraw && r.itemId==2430680)
        {
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var key=(db,r.characterId);
            if(worldQuestUse.TryGetValue(key,out long prior) && now-prior<1000)
                return new InventoryChangeResponse {ok=false,error="道具使用冷却中"};
            var source=InventoryDao.ItemsByCharacter(db,r.characterId).Find(row=>DbStore.Int(row,"itemid")==r.itemId &&
                DbStore.Int(row,"inventorytype")==2 && DbStore.Int(row,"position")>0 &&
                (r.sourcePosition==0 || DbStore.Int(row,"position")==r.sourcePosition) && DbStore.Int(row,"quantity")>0);
            if(source==null) return new InventoryChangeResponse {ok=false,error="没有持有这件物品"};
            int roll=worldQuestRandom.Next(105);
            int reward=roll<100 ? 2430681+roll/5 : 2430701+roll-100;
            // The original records cooldown before checkSpace, including a full bag.
            worldQuestUse[key]=now;
            if(!CanReceive(db,c,new InventoryChangeRequest {checkItems=new[] {reward},checkQuantities=new[] {1}}))
                return new InventoryChangeResponse {ok=false,error="背包空间不足，无法获得物品！"};
            AddItems(db,c,reward,1,-1);
            int quantity=DbStore.Int(source,"quantity")-1;
            if(quantity>0) source["quantity"]=(long)quantity;
            else {long rowId=DbStore.Long(source,"inventoryitemid");db.Table("inventoryitems").Delete(row=>DbStore.Long(row,"inventoryitemid")==rowId);}
            db.Save();return new InventoryChangeResponse {inventory=CharService.Load(db,r.characterId).inventory,message="获得了任务物品："+Server.Bot.BotItemCatalog.Shared.Name(reward)};
        }
        if(r.petTameness!=0) return NativePetItems.GainTameness(db,r.characterId,r.petTameness,r.petLeaderOnly);
        if(r.createPet)
        {
            if(r.itemId/1000!=5000 || Server.Bot.BotItemCatalog.Shared.Info(r.itemId)==null)
                return new InventoryChangeResponse {ok=false,error="宠物资源不存在"};
            int petId=(int)db.Table("pets").Insert(new DbRow { ["name"]=Server.Bot.BotItemCatalog.Shared.Name(r.itemId),
                ["level"]=1L,["closeness"]=0L,["fullness"]=100L,["summoned"]=0L,["flag"]=0L });
            db.Save();return new InventoryChangeResponse {createdPetId=petId};
        }
        if(r.getPet) return new InventoryChangeResponse {pet=NativePetItems.GetPet(db,r.characterId,r.petSlot)};
        if(r.evolvePetId>0) return NativePetItems.Evolve(db,r);
        if(r.petTick) return NativePetItems.Tick(db,r.characterId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if(r.togglePet && r.itemId/1000==5000) return NativePetItems.Toggle(db,r);
        if(r.quantity==0 && r.grantItemId==0 && (r.itemId/10000==212 || r.itemId/10000==524)) return NativePetItems.Feed(db,r);
        var items = db.Table("inventoryitems");
        if(r.bankExp>0)
        {
            if(c.level>r.maxBankLevel || (long)c.gachaexp+r.bankExp>int.MaxValue || InventoryDao.CountOf(db,r.characterId,r.itemId)<1)
                return new InventoryChangeResponse {ok=false,error="当前等级或暂存经验不符合使用条件"};
            c.gachaexp+=r.bankExp; CharactersDao.Save(db,c); InventoryDao.ConsumeItem(db,r.characterId,r.itemId,1); db.Save();
            return new InventoryChangeResponse {inventory=CharService.Load(db,r.characterId).inventory,bankedExp=c.gachaexp};
        }
        if(r.breakthrough) return NativeDamageCapService.Use(db,c,r.itemId);
        if (r.grantItemId > 0) {
            if (r.grantQuantity <= 0 || InventoryDao.CountOf(db, r.characterId, r.itemId) < 1)
                return new InventoryChangeResponse { ok=false,error="没有持有这件物品" };
            int type=r.grantItemId/1000000;
            var check=new InventoryChangeRequest {checkItems=new[] {r.grantItemId},checkQuantities=new[] {r.grantQuantity}};
            if(!CanReceive(db,c,check)) return new InventoryChangeResponse {ok=false,error="背包空间不足，无法获取"};
            long expiration=r.grantDurationMs>=0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+r.grantDurationMs : r.grantMinutes<0 ? -1L : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+r.grantMinutes*60000L;
            AddItems(db,c,r.grantItemId,r.grantQuantity,expiration);
            if(r.consumeGrantSource) InventoryDao.ConsumeItem(db,r.characterId,r.itemId,1);
        } else if (r.equip) {
            int slot = EquipmentSlotOf(r.itemId);
            if(NativePetItems.IsEquipment(r.itemId))
            {
                slot=NativePetItems.EquipmentSlot(r.itemId,r.petSlot);
                if(r.quantity!=0)
                {
                    var pet=NativePetItems.GetPet(db,r.characterId,r.petSlot);
                    if(pet==null || !NativePetItems.EquipmentFits(r.itemId,pet.itemId))
                        return new InventoryChangeResponse {ok=false,error="请先召唤可以穿戴此装备的宠物"};
                }
                else
                    for(int i=0;i<3;i++) if(NativePetItems.EquipmentSlot(r.itemId,i)==r.equipSlot) slot=r.equipSlot;
            }
            if (slot == -12 && (r.equipSlot == -12 || r.equipSlot == -13 || r.equipSlot == -15 || r.equipSlot == -16) ||
                slot == -112 && (r.equipSlot == -112 || r.equipSlot == -113 || r.equipSlot == -115 || r.equipSlot == -116)) slot = r.equipSlot;
            if (slot == 0 || slot != r.equipSlot)
            {
                // 源端 unequip（InventoryManipulator.move 的 src）由客户端指定格子，服务端不按道具
                // 重推规范槽位，只要求那一格真的穿着这件。现金装备坐在普通位、旧槽位映射版本留下
                // 的历史位置等漂移数据，双击装备面板脱下时按源端口径放行；穿上（quantity=1）仍严格要求。
                bool unequipByPosition = r.quantity == 0 && r.equipSlot < 0 && items.Find(x =>
                    DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "itemid") == r.itemId &&
                    DbStore.Int(x, "position") == r.equipSlot && DbStore.Int(x, "inventorytype") == InventoryDao.Equipped) != null;
                if (!unequipByPosition) return new InventoryChangeResponse { ok = false, error = "无效装备槽位" };
                slot = r.equipSlot;
            }
            var source = items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "itemid") == r.itemId &&
                (r.quantity == 0 ? DbStore.Int(x, "position") == slot : DbStore.Int(x, "position") > 0 && (r.sourcePosition == 0 || DbStore.Int(x, "position") == r.sourcePosition)));
            if (source == null) return new InventoryChangeResponse { ok = false, error = "没有持有这件装备" };
            int oldPos = DbStore.Int(source, "position");
            if (r.quantity == 0) {
                int free = Free(db, c, 1);
                if (free == 0) return new InventoryChangeResponse { ok = false, error = "装备栏已满" };
                source["position"] = (long)free; source["inventorytype"] = 1L;
            } else {
                // InventoryManipulator.equip: overall/pants and two-handed weapon/shield conflicts move to a free slot.
                int conflictSlot = slot == -6 ? -5 : slot == -5 && r.itemId / 10000 == 105 ? -6 :
                    slot == -10 ? -11 : slot == -11 && TwoHanded(r.itemId) ? -10 : 0;
                var conflict = conflictSlot == 0 ? null : items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "position") == conflictSlot && DbStore.Int(x, "inventorytype") == InventoryDao.Equipped);
                if (conflict != null && (slot != -6 || DbStore.Int(conflict, "itemid") / 10000 == 105) && (slot != -10 || TwoHanded(DbStore.Int(conflict, "itemid")))) {
                    int free = Free(db, c, 1);
                    if (free == 0) return new InventoryChangeResponse { ok = false, error = "装备栏已满" };
                    conflict["position"] = (long)free; conflict["inventorytype"] = 1L;
                }
                var prior = items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "position") == slot && DbStore.Int(x, "inventorytype") == InventoryDao.Equipped);
                if (prior != null) { prior["position"] = (long)oldPos; prior["inventorytype"] = 1L; }
                source["position"] = (long)slot; source["inventorytype"] = (long)InventoryDao.Equipped;
            }
        } else if (r.projectile) {
            bool rechargeable=r.itemId/10000==207 || r.itemId/10000==233;
            bool arrow=r.itemId/1000==2060 || r.itemId/1000==2061;
            var source=items.Find(x=>DbStore.Int(x,"characterid")==r.characterId && DbStore.Int(x,"itemid")==r.itemId &&
                DbStore.Int(x,"inventorytype")==2 && DbStore.Int(x,"position")==r.sourcePosition && r.sourcePosition>0);
            if ((!rechargeable && !arrow) || source==null || r.quantity<short.MinValue || r.quantity>short.MaxValue)
                return new InventoryChangeResponse { ok=false,error="弹药槽位或数量无效" };
            long remaining=Math.Max(0,(int)unchecked((short)(DbStore.Int(source,"quantity")+r.quantity)));
            if (remaining>0 || rechargeable) source["quantity"]=remaining;
            else { long id=DbStore.Long(source,"inventoryitemid"); items.Delete(x=>DbStore.Long(x,"inventoryitemid")==id); }
        } else if (r.quantity > 0) {
            var check=new InventoryChangeRequest {checkItems=new[] {r.itemId},checkQuantities=new[] {r.quantity}};
            if(!CanReceive(db,c,check)) return new InventoryChangeResponse {ok=false,error="背包空间不足，无法获取"};
            AddItems(db,c,r.itemId,r.quantity,r.itemExpiration,r.petId>0 ? r.petId : -1);
        } else if (r.quantity < 0) {
            if (r.sourcePosition!=0) {
                var source=items.Find(x=>DbStore.Int(x,"characterid")==r.characterId && DbStore.Int(x,"itemid")==r.itemId &&
                    DbStore.Int(x,"inventorytype")==r.itemId/1000000 && DbStore.Int(x,"position")==r.sourcePosition && r.sourcePosition>0);
                if (source==null || r.quantity==int.MinValue || DbStore.Int(source,"quantity") < -r.quantity)
                    return new InventoryChangeResponse { ok=false,error="指定槽位数量不足" };
                int remaining=DbStore.Int(source,"quantity")+r.quantity;
                if (remaining>0) source["quantity"]=(long)remaining;
                else { long id=DbStore.Long(source,"inventoryitemid"); items.Delete(x=>DbStore.Long(x,"inventoryitemid")==id);
                    db.Table("inventoryequipment").Delete(x=>DbStore.Long(x,"inventoryitemid")==id); }
            } else {
            int total = InventoryDao.CountOf(db, r.characterId, r.itemId); if (total < -r.quantity) return new InventoryChangeResponse { ok = false, error = "持有数量不足" };
            InventoryDao.ConsumeItem(db, r.characterId, r.itemId, -r.quantity);
            }
        }
        db.Save(); return new InventoryChangeResponse { inventory = CharService.Load(db, r.characterId).inventory };
    }
    // Duey / 交易等外部装箱复用同一套放包规则（源端 InventoryManipulator.addFromDrop）。
    internal static void AddItems(DbStore db,CharRecord c,int itemId,int count,long expiration,int petId=-1)
    {
        int type=itemId/1000000;
        var items=db.Table("inventoryitems");
        var actor=new Server.Bot.BotActor {id=(int)c.id,job=c.job,character=CharService.Load(db,(int)c.id)};
        var rules=new Server.Bot.Trade.TradeItemRules(db,Server.Bot.BotItemCatalog.Shared);
        bool rechargeable=Server.Bot.Trade.TradeItemRules.Rechargeable(itemId);
        bool pet=itemId/1000==5000;
        int remaining=count,max=type==1 || pet ? 1 : rechargeable ? count : rules.SlotMax(itemId,actor);
        if(type!=1 && !pet && !rechargeable)
            foreach(var held in InventoryDao.ItemsByCharacter(db,(int)c.id))
            {
                if(remaining<=0) break;
                if(DbStore.Int(held,"itemid")!=itemId || DbStore.Int(held,"position")<=0 || DbStore.Int(held,"flag")!=0) continue;
                int take=Math.Min(remaining,Math.Max(0,max-DbStore.Int(held,"quantity")));
                if(take==0) continue;
                held["quantity"]=(long)(DbStore.Int(held,"quantity")+take);held["expiration"]=expiration;remaining-=take;
            }
        while(remaining>0)
        {
            int position=Free(db,c,type),quantity=Math.Min(remaining,max);
            if(position==0 || quantity<=0) throw new InvalidOperationException("Inventory capacity preflight mismatch");
            long id=InventoryDao.InsertItem(db,(int)c.id,itemId,type,position,quantity,pet ? petId : -1);
            if(type==1) InventoryDao.InsertEquipment(db,id);
            items.Find(x=>DbStore.Long(x,"inventoryitemid")==id)["expiration"]=expiration;
            remaining-=quantity;
        }
    }
    internal static bool CanReceive(DbStore db,CharRecord c,InventoryChangeRequest r)
    {
        var character=CharService.Load(db,(int)c.id);
        var actor=new Server.Bot.BotActor {id=(int)c.id,job=c.job,character=character};
        var inventory=new List<InventoryEntry>();
        foreach(var item in character.inventory) inventory.Add(item.Copy());
        if(r.removeCheckItem>0 && r.removeCheckQuantity>0)
        {
            int remaining=r.removeCheckQuantity;
            foreach(var item in inventory)
                if(item.position>0 && item.itemId==r.removeCheckItem)
                { int take=Math.Min(remaining,item.quantity); item.quantity-=take; remaining-=take; }
            inventory.RemoveAll(i=>i.quantity<=0);
        }
        var rules=new Server.Bot.Trade.TradeItemRules(db,Server.Bot.BotItemCatalog.Shared);
        for(int i=0;i<r.checkItems.Length;i++)
        {
            int id=r.checkItems[i],count=r.checkQuantities!=null && i<r.checkQuantities.Length ? r.checkQuantities[i] : 1;
            if(id<=0 || count<=0 || count>short.MaxValue || Server.Bot.BotItemCatalog.Shared.Info(id)==null) return false;
            if(id/1000000==1 || id/1000==5000)
            {
                for(int j=0;j<count;j++)
                {
                    var offered=Server.Bot.BotItemCatalog.Shared.CleanItem(id,1);
                    if(id/1000==5000) offered.petId=0;
                    if(!rules.Receive(actor,inventory,offered)) return false;
                }
            }
            else if(!rules.Receive(actor,inventory,Server.Bot.BotItemCatalog.Shared.CleanItem(id,count))) return false;
        }
        return true;
    }
    // Pure server mapping: script harness must not depend on Unity's equipment presentation.
    public static int EquipmentSlotOf(int itemId)
    {
        var info = Server.Bot.BotItemCatalog.Shared.Info(itemId);
        // ItemInformationProvider.canWearEquipment -> constants.inventory.EquipSlot:
        // the original IMG islot, including Wp/Tm/Sd absent from the old ID-only mapping.
        string islot = info != null && info.TryGetValue("islot", out var value) ? value as string : null;
        int slot = islot switch {
            "Cp" => -1, "HrCp" => -1, "Af" => -2, "Ay" => -3, "Ae" => -4,
            "Ma" => -5, "MaPn" => -5, "Pn" => -6, "So" => -7, "GlGw" => -8, "Gv" => -8,
            "Sr" => -9, "Si" => -10, "Wp" => -11, "WpSi" => -11, "WpSp" => -11,
            "Ri" => -12, "Pe" => -17, "Tm" => -18, "Sd" => -19, "Sh" => -20,
            "Sw" => -51, "Po" => -53, "Ht" => -54, "Ba" => -55, "Em" => -56,
            "Me" => -49, "Be" => -50, _ => -Server.Bot.Decorator.BotCustomization.EquipSlotType(itemId)
        };
        return slot != 0 && Server.Bot.BotItemCatalog.Int(info, "cash") != 0 ? slot - 100 : slot;
    }

    // 源端「背包里把道具丢到地上」走的是 ITEM_MOVE(0x47)：ItemMoveHandler 读到 action==0 就转
    // InventoryManipulator.drop(c, type, src, quantity)。这里照抄那段判定：
    //   - 该格不存在 / 交易或小游戏中 → 什么都不做（这里只回 ok=false，不改背包）
    //   - quantity < 堆叠数且不是可充能道具（itemId/10000==207）→ 拆一份新的出去，原格减数量
    //   - 否则整格移除（装备连 inventoryequipment 行一起删）
    //   - 落地一律 spawnItemDrop(dropper=owner=玩家, pos=玩家脚点, playerDrop=true, ffaDrop=true)
    // 单机没有服务端角色坐标，落点由客户端回传；吸附/夹取仍由 MapItemService.CalcItemDropPos 负责。
    static InventoryChangeResponse DropToField(DbStore db, CharRecord character, InventoryChangeRequest r)
    {
        int characterId = (int)character.id;
        var full = CharService.Load(db, characterId);
        if (full == null || full.inventory == null) return new InventoryChangeResponse { ok = false, error = "角色或道具不存在" };

        InventoryEntry source = null;
        foreach (var entry in full.inventory)
            if (entry.itemId == r.itemId && entry.position > 0 &&
                (r.sourcePosition == 0 || entry.position == r.sourcePosition)) { source = entry; break; }
        if (source == null) return new InventoryChangeResponse { ok = false, error = "背包里没有这件物品" };

        // 源端客户端拦现金道具（EXE 0x5d4a2b 读 info/cash → 0x372「这种道具不能扔掉」）；服务端再兜一层。
        var itemInfo = Server.Bot.BotItemCatalog.Shared.Info(r.itemId);
        if (itemInfo != null && Server.Bot.BotItemCatalog.Int(itemInfo, "cash") == 1)
            return new InventoryChangeResponse { ok = false, error = "这种道具不能扔掉。" };
        // 任务道具（info/quest）：客户端会确认「丢弃后无法再次获得」，落地走源端 disappearingItemDrop
        // （丢出即消失、不可拾取）——这里直接不生成可拾取的地面掉落。
        bool questItem = itemInfo != null && Server.Bot.BotItemCatalog.Int(itemInfo, "quest") == 1;

        int have = source.quantity;
        int take = r.dropQuantity;
        // 源端丢弃前先判 (!isRechargeable && source.quantity < quantity) || quantity < 0
        if (take <= 0 || take > have) return new InventoryChangeResponse { ok = false, error = "数量不正确" };

        bool rechargeable = source.itemId / 10000 == 207;
        if (take < have && !rechargeable)
        {
            var row = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid", -1) == source.itemRowId);
            if (row != null) row["quantity"] = (long)(have - take);
        }
        else
        {
            take = have;
            db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == source.itemRowId);
            db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == source.itemRowId);
        }
        db.Save();

        // 任务道具丢出即消失（源端 disappearingItemDrop），不生成可拾取的地面掉落。
        if (!questItem)
        {
            var dropped = source.Copy();
            dropped.quantity = take;
            Server.Field.MapItemService.SpawnItemDrop(r.dropMapId, characterId, characterId,
                Server.Field.MapItemService.PartyOf != null ? Server.Field.MapItemService.PartyOf(characterId) : -1,
                dropped, r.dropX, r.dropY, true, true);
        }

        return new InventoryChangeResponse { inventory = CharService.Load(db, characterId).inventory };
    }

    // 源端 MesoDropHandler：isAlive → meso>9 && meso<50001 && meso<=getMeso() → gainMeso(-meso)
    // → map.spawnMesoDrop(meso, position, player, player, true, (byte)2)。
    // GM 预检（客户端文案 775 / 服务端 "You cannot drop mesos at your GM level."）在客户端做，
    // 服务端按 minimum_gm_level_to_drop 再拦一次，两侧共用 GameConfig 同名键（源端默认 4）。
    // attemptCatchFish(meso) 分支（钓鱼场消失金币）没有对应系统，按普通 spawnMesoDrop 记录。
    static InventoryChangeResponse DropMeso(DbStore db, CharRecord character, InventoryChangeRequest r)
    {
        long meso = r.dropMeso;
        if (character.hp <= 0) return new InventoryChangeResponse { ok = false, error = "死亡状态中不能执行该操作" };
        int gmLevel = GameConfigDao.GetInt(db, "minimum_gm_level_to_drop", 4);
        if (character.gm > 0 && character.gm < gmLevel)
            return new InventoryChangeResponse { ok = false, error = "管理账号不能扔掉金币。" };
        if (meso <= 9 || meso > 50000 || meso > character.meso)
            return new InventoryChangeResponse { ok = false, error = "金币数量不正确" };
        character.meso -= meso;
        CharactersDao.Save(db, character); db.Save();
        Server.Field.MapItemService.SpawnMesoDrop(r.dropMapId, (int)meso, r.dropX, r.dropY,
            (int)character.id, (int)character.id,
            Server.Field.MapItemService.PartyOf != null ? Server.Field.MapItemService.PartyOf((int)character.id) : -1,
            true, 2);
        return new InventoryChangeResponse { meso = character.meso };
    }

    static bool TwoHanded(int id) => id / 10000 >= 140 && id / 10000 <= 149;
    internal static int Free(DbStore db, CharRecord c, int type) {
        int max = type == 1 ? c.equipslots : type == 2 ? c.useslots : type == 3 ? c.setupslots : type == 4 ? c.etcslots : 96;
        var used = new HashSet<int>(); foreach (var row in InventoryDao.ItemsByCharacter(db, (int)c.id)) if (DbStore.Int(row, "inventorytype") == type) used.Add(DbStore.Int(row, "position"));
        for (int i = 1; i <= max; i++) if (!used.Contains(i)) return i; return 0;
    }
}
