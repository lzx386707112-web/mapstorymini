using System;
using System.Collections.Generic;

// PetFoodHandler / UseCashItemHandler(524) / Pet.gainTamenessFullness.
public static class NativePetItems
{
    static Dictionary<string,object> rules;
    static readonly Dictionary<(DbStore,int),long> lastFood=new Dictionary<(DbStore,int),long>();
    static readonly Dictionary<(DbStore,int),List<int>> petOrder=new Dictionary<(DbStore,int),List<int>>();
    // Character.addPet fills the first free pet slot; changing inventory
    // positions does not reorder already summoned pets during the session.
    static List<InventoryEntry> ActivePets(DbStore db,int cid)
    {
        var active=new List<InventoryEntry>();
        foreach(var item in CharService.Load(db,cid).inventory)
            if(item.petData!=null && Convert.ToInt32(item.petData["summoned"])==1) active.Add(item);
        var key=(db,cid);
        if(!petOrder.TryGetValue(key,out var order)) petOrder[key]=order=new List<int>();
        order.RemoveAll(id=>!active.Exists(item=>item.petId==id));
        foreach(var item in active) if(!order.Contains(item.petId)) order.Add(item.petId);
        active.Sort((a,b)=>order.IndexOf(a.petId).CompareTo(order.IndexOf(b.petId)));
        return active;
    }
    public static InventoryEntry GetPet(DbStore db,int cid,int slot)
    {
        var pets=ActivePets(db,cid);
        return slot>=0 && slot<pets.Count ? pets[slot] : null;
    }
    public static InventoryChangeResponse GainTameness(DbStore db,int cid,int increase,bool leaderOnly=false)
    {
        rules ??= (Dictionary<string,object>)MiniJsonDb.Parse(PlatformRes.LoadText("PetItemRules"));
        var thresholds=(List<object>)rules["tameness"];
        var active=ActivePets(db,cid);
        if(leaderOnly && active.Count>1) active.RemoveRange(1,active.Count-1);
        foreach(var pet in active)
        {
            var row=db.Table("pets").Find(x=>DbStore.Int(x,"petid")==pet.petId);
            int closeness=DbStore.Int(row,"closeness"),level=DbStore.Int(row,"level");
            if(increase>0 && closeness<30000)
            {
                closeness=(int)Math.Min(30000L,(long)closeness+increase);
                while(level<30 && closeness>=Convert.ToInt32(thresholds[level])) level++;
                row["closeness"]=(long)closeness;row["level"]=(long)level;
            }
        }
        db.Save();return Snapshot(db,cid);
    }
    // AbstractPlayerInteraction.evolvePet and gainItem(..., Pet from).
    public static InventoryChangeResponse Evolve(DbStore db,InventoryChangeRequest request)
    {
        int cid=request.characterId,after=request.evolvePetId;
        var before=GetPet(db,cid,request.petSlot);
        if(before==null) return new InventoryChangeResponse {ok=false,error="Pet could not be evolved..."};
        rules ??= (Dictionary<string,object>)MiniJsonDb.Parse(PlatformRes.LoadText("PetItemRules"));
        if(!((Dictionary<string,object>)rules["pets"]).ContainsKey(after.ToString()))
            return new InventoryChangeResponse {ok=false,error="无效宠物"};
        var capacity=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,
            checkItems=new[] {after},checkQuantities=new[] {1}});
        if(!capacity.ok) return capacity;
        var source=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==cid && DbStore.Int(x,"petid")==before.petId);
        int position=1;
        var inventory=CharService.Load(db,cid).inventory;
        while(inventory.Exists(x=>x.inventoryType==5 && x.position==position)) position++;
        long id=InventoryDao.InsertItem(db,cid,after,5,position,1);
        var added=db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==id);
        added["expiration"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+90L*86400000;
        var pet=db.Table("pets").Find(x=>DbStore.Int(x,"petid")==DbStore.Int(added,"petid"));
        foreach(string key in new[] {"level","closeness","fullness"}) pet[key]=before.petData[key];
        string name=before.petData["name"].ToString();
        pet["name"]=name==Server.Bot.BotItemCatalog.Shared.Name(before.itemId) ? Server.Bot.BotItemCatalog.Shared.Name(after) : name;
        pet["summoned"]=1L;
        ActivePets(db,cid);
        long oldId=DbStore.Long(source,"inventoryitemid");
        db.Table("inventoryitems").Delete(x=>DbStore.Long(x,"inventoryitemid")==oldId);
        db.Save();return Snapshot(db,cid);
    }
    sealed class HungerClock
    {
        public long updated;
        public readonly Dictionary<(int,int),int> counts=new Dictionary<(int,int),int>();
        public readonly Dictionary<int,long> processed=new Dictionary<int,long>();
    }
    static readonly Dictionary<DbStore,HungerClock> hungerClocks=new Dictionary<DbStore,HungerClock>();
    public static void EndSession(DbStore db,int cid)
    {
        lastFood.Remove((db,cid));
        petOrder.Remove((db,cid));
        if(!hungerClocks.TryGetValue(db,out var clock)) return;
        clock.processed.Remove(cid);
        var remove=new List<(int,int)>();
        foreach(var key in clock.counts.Keys) if(key.Item1==cid) remove.Add(key);
        foreach(var key in remove) clock.counts.Remove(key);
        if(clock.counts.Count==0) hungerClocks.Remove(db);
    }
    static HungerClock Clock(DbStore db,long now)
    {
        if(!hungerClocks.TryGetValue(db,out var clock)) hungerClocks[db]=clock=new HungerClock {updated=now};
        return clock;
    }
    // World.runPetSchedule: global minute ticks, original configured proc count.
    // Offline elapsed time is not replayed as hunger when a local session resumes.
    public static InventoryChangeResponse Tick(DbStore db,int cid,long now)
    {
        var character=CharactersDao.FindById(db,cid);
        if(character==null) return new InventoryChangeResponse {ok=false,error="角色不存在"};
        var clock=Clock(db,now);
        if(!clock.processed.ContainsKey(cid)) clock.processed[cid]=clock.updated;
        int exhaust=GameConfigDao.GetInt(db,"pet_exhaust_count",3);
        bool disabled=GameConfigDao.GetBool(db,"pets_never_hungry",false) ||
            character.gm>0 && GameConfigDao.GetBool(db,"gm_pets_never_hungry",true);
        var active=new HashSet<(int,int)>();
        var pets=new List<InventoryEntry>();
        foreach(var item in ActivePets(db,cid))
            {
                var key=(cid,item.petId);active.Add(key);pets.Add(item);
                if(!clock.counts.ContainsKey(key) && !disabled)
                    clock.counts[key]=exhaust-(now-clock.updated>55000 ? 2 : 1);
            }
        var remove=new List<(int,int)>();
        foreach(var key in clock.counts.Keys) if(key.Item1==cid && (!active.Contains(key) || disabled)) remove.Add(key);
        foreach(var key in remove) clock.counts.Remove(key);
        if(now-clock.updated>=60000) clock.updated=now;
        if(clock.processed[cid]>=clock.updated) return Snapshot(db,cid);
        clock.processed[cid]=clock.updated;
        rules ??= (Dictionary<string,object>)MiniJsonDb.Parse(PlatformRes.LoadText("PetItemRules"));
        var templates=(Dictionary<string,object>)rules["pets"];
        bool changed=false,returned=false;
        foreach(var item in pets)
        {
            var key=(cid,item.petId);
            if(!clock.counts.TryGetValue(key,out int count)) continue;
            count++;
            if(count==exhaust)
            {
                count=0;
                var row=db.Table("pets").Find(x=>DbStore.Int(x,"petid")==item.petId);
                int hungry=templates.TryGetValue(item.itemId.ToString(),out var raw) ? Convert.ToInt32(((Dictionary<string,object>)raw)["hungry"]) : 0;
                int fullness=DbStore.Int(row,"fullness")-hungry;
                if(fullness<=5) {fullness=15;row["summoned"]=0L;returned=true;}
                row["fullness"]=(long)fullness;changed=true;
            }
            clock.counts[key]=count;
        }
        if(changed) db.Save();
        var response=Snapshot(db,cid);
        if(returned) response.message="您的宠物饿了！给它一些宠物食物以保持它的健康！";
        return response;
    }
    // SpawnPetProcessor: toggle without consumption; an egg hatches but does
    // not summon until the next use. Expiration survives replacement.
    public static InventoryChangeResponse Toggle(DbStore db,InventoryChangeRequest request)
    {
        int cid=request.characterId,id=request.itemId;
        var held=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==cid && DbStore.Int(x,"itemid")==id &&
            DbStore.Int(x,"inventorytype")==5 && DbStore.Int(x,"position")>0 && (request.sourcePosition==0 || DbStore.Int(x,"position")==request.sourcePosition));
        var pet=held==null ? null : db.Table("pets").Find(x=>DbStore.Int(x,"petid")==DbStore.Int(held,"petid",-1));
        if(pet==null) return new InventoryChangeResponse {ok=false,error="宠物不存在"};
        if(DbStore.Long(held,"expiration",-1)<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            return new InventoryChangeResponse {ok=false,error="请先用生命之水复活宠物"};
        rules ??= (Dictionary<string,object>)MiniJsonDb.Parse(PlatformRes.LoadText("PetItemRules"));
        if(id==5000028 || id==5000047)
        {
            if(InventoryDao.CountOf(db,cid,id+1)>0) return new InventoryChangeResponse {ok=false,error="已经拥有孵化后的宠物"};
            var data=(Dictionary<string,object>)((Dictionary<string,object>)rules["pets"])[id.ToString()];
            int evolved=Convert.ToInt32(data["evol1"]);
            long expiration=DbStore.Long(held,"expiration",-1),rowId=DbStore.Long(held,"inventoryitemid");
            int position=DbStore.Int(held,"position");
            db.Table("inventoryitems").Delete(x=>DbStore.Long(x,"inventoryitemid")==rowId);
            long newId=InventoryDao.InsertItem(db,cid,evolved,5,position,1);
            db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==newId)["expiration"]=expiration;
        }
        else if(DbStore.Int(pet,"summoned")==1) pet["summoned"]=0L;
        else
        {
            bool multiple=SkillsDao.ByCharacter(db,cid).Exists(x=>DbStore.Int(x,"skillid")==8 && DbStore.Int(x,"skilllevel")>0);
            var active=new List<DbRow>();
            foreach(var item in ActivePets(db,cid))
                active.Add(db.Table("pets").Find(x=>DbStore.Int(x,"petid")==item.petId));
            if(!multiple) foreach(var other in active) other["summoned"]=0L;
            else if(active.Count>=3) return Snapshot(db,cid);
            pet["summoned"]=1L;
        }
        db.Save();Tick(db,cid,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());return Snapshot(db,cid);
    }
    public static InventoryChangeResponse Feed(DbStore db,InventoryChangeRequest request)
    {
        int id=request.itemId,cid=request.characterId;
        bool cash=id/10000==524;
        if(!cash && id/10000!=212) return new InventoryChangeResponse {ok=false,error="无效宠物食品"};
        var food=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==cid && DbStore.Int(x,"itemid")==id &&
            DbStore.Int(x,"inventorytype")== (cash ? 5 : 2) && DbStore.Int(x,"position")>0 &&
            (request.sourcePosition==0 || DbStore.Int(x,"position")==request.sourcePosition) && DbStore.Int(x,"quantity")>0);
        if(food==null) return new InventoryChangeResponse {ok=false,error="背包里没有这个物品"};
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if(!cash)
        {
            if(lastFood.TryGetValue((db,cid),out long previous) && previous+500>now) return Snapshot(db,cid);
            lastFood[(db,cid)]=now;
        }
        rules ??= (Dictionary<string,object>)MiniJsonDb.Parse(PlatformRes.LoadText("PetItemRules"));
        var pets=ActivePets(db,cid);
        InventoryEntry selected=null;
        int increase=0;
        if(cash)
        {
            var foods=(Dictionary<string,object>)rules["foods"];
            if(foods.TryGetValue(id.ToString(),out var raw))
            {
                var spec=(Dictionary<string,object>)raw;
                foreach(var pet in pets)
                {
                    foreach(var pair in spec)
                        if(int.TryParse(pair.Key,out _) && Convert.ToInt32(pair.Value)==pet.itemId) {selected=pet;break;}
                    if(selected!=null) break;
                }
                increase=spec.TryGetValue("inc",out var inc) ? Convert.ToInt32(inc) : 1;
            }
        }
        else if(pets.Count>0)
        {
            selected=pets[0];
            foreach(var pet in pets) if(Convert.ToInt32(pet.petData["fullness"])<Convert.ToInt32(selected.petData["fullness"])) selected=pet;
            increase=Convert.ToInt32(selected.petData["fullness"])<=75 ? 1 : 0;
        }
        if(selected==null) return cash ? new InventoryChangeResponse {ok=false,error="没有可以食用此食品的已召唤宠物"} : Snapshot(db,cid);
        var row=db.Table("pets").Find(x=>DbStore.Int(x,"petid")==selected.petId);
        int fullness=DbStore.Int(row,"fullness"),closeness=DbStore.Int(row,"closeness"),level=DbStore.Int(row,"level");
        var thresholds=(List<object>)rules["tameness"];
        if(fullness<100 || cash)
        {
            fullness=Math.Min(100,fullness+(cash ? 100 : 30));
            if(increase>0 && closeness<30000)
            {
                closeness=Math.Min(30000,closeness+increase);
                while(level<30 && closeness>=Convert.ToInt32(thresholds[level])) level++;
            }
        }
        else
        {
            closeness=Math.Max(0,closeness-1);
            if(level>1 && closeness<Convert.ToInt32(thresholds[level-1])) level--;
        }
        row["fullness"]=(long)fullness;row["closeness"]=(long)closeness;row["level"]=(long)level;
        int quantity=DbStore.Int(food,"quantity");
        if(quantity>1) food["quantity"]=(long)(quantity-1);
        else db.Table("inventoryitems").Delete(x=>DbStore.Long(x,"inventoryitemid")==DbStore.Long(food,"inventoryitemid"));
        db.Save();
        return Snapshot(db,cid);
    }
    static InventoryChangeResponse Snapshot(DbStore db,int cid)=>new InventoryChangeResponse {inventory=CharService.Load(db,cid).inventory};
}
