using System;
using System.Collections.Generic;
public sealed class InventoryChangeRequest { public int characterId, itemId, quantity, equipSlot, sourcePosition; public int petSlot, evolvePetId; public int petTameness; public bool petLeaderOnly; public bool getPet; public bool equip, projectile, removeAll, togglePet, worldQuestDraw, petTick; public int grantItemId, grantQuantity, grantMinutes; public bool consumeGrantSource, breakthrough; public int bankExp, maxBankLevel; public long grantDurationMs=-1; public int[] checkItems, checkQuantities; public int removeCheckItem, removeCheckQuantity; }
public sealed class InventoryChangeResponse { public bool ok = true; public string error = "", message; public InventoryEntry pet; public int damageCap, bankedExp; public List<InventoryEntry> inventory; }
public static class NativeInventoryService {
    static readonly Dictionary<(DbStore,int),long> worldQuestUse=new Dictionary<(DbStore,int),long>();
    static readonly Random worldQuestRandom=new Random();
    // Existing gameplay is client-authoritative; persistence still runs inside transport.
    public static InventoryChangeResponse Handle(DbStore db, InventoryChangeRequest r) {
        var c = CharactersDao.FindById(db, r.characterId);
        if (c == null || r.itemId <= 0 && r.checkItems==null) return new InventoryChangeResponse { ok = false, error = "角色或道具不存在" };
        InventoryDao.ExpireItems(db,r.characterId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if(r.checkItems!=null) return new InventoryChangeResponse {ok=CanReceive(db,c,r)};
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
            if (slot == -12 && (r.equipSlot == -12 || r.equipSlot == -13 || r.equipSlot == -15 || r.equipSlot == -16)) slot = r.equipSlot;
            if (slot == 0 || slot != r.equipSlot) return new InventoryChangeResponse { ok = false, error = "无效装备槽位" };
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
            AddItems(db,c,r.itemId,r.quantity,-1);
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
    static void AddItems(DbStore db,CharRecord c,int itemId,int count,long expiration)
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
            long id=InventoryDao.InsertItem(db,(int)c.id,itemId,type,position,quantity);
            if(type==1) InventoryDao.InsertEquipment(db,id);
            items.Find(x=>DbStore.Long(x,"inventoryitemid")==id)["expiration"]=expiration;
            remaining-=quantity;
        }
    }
    static bool CanReceive(DbStore db,CharRecord c,InventoryChangeRequest r)
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
        return -Server.Bot.Decorator.BotCustomization.EquipSlotType(itemId);
    }

    static bool TwoHanded(int id) => id / 10000 >= 140 && id / 10000 <= 149;
    static int Free(DbStore db, CharRecord c, int type) {
        int max = type == 1 ? c.equipslots : type == 2 ? c.useslots : type == 3 ? c.setupslots : type == 4 ? c.etcslots : 96;
        var used = new HashSet<int>(); foreach (var row in InventoryDao.ItemsByCharacter(db, (int)c.id)) if (DbStore.Int(row, "inventorytype") == type) used.Add(DbStore.Int(row, "position"));
        for (int i = 1; i <= max; i++) if (!used.Contains(i)) return i; return 0;
    }
}
