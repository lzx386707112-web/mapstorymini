using System;
using Server.Bot;

// UseCashItemHandler: original targeted cash item mutations.
public static class NativeCashEquipmentService
{
    static EquipPatchResponse Reject(string message)=>new EquipPatchResponse {ok=false,error=message};
    public static EquipPatchResponse Apply(DbStore db,EquipPatchRequest request)
    {
        var c=CharactersDao.FindById(db,request.characterId);
        var item=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Long(x,"inventoryitemid")==request.inventoryItemId);
        int cash=request.cashItemId;
        if(c==null || item==null || InventoryDao.CountOf(db,request.characterId,cash)<1) return Reject("装备或现金道具不存在");
        int position=DbStore.Int(item,"position"),id=DbStore.Int(item,"itemid"),flag=DbStore.Int(item,"flag");
        long expiration=DbStore.Long(item,"expiration"),now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var info=BotItemCatalog.Shared.Info(id);
        if(cash==5060000)
        {
            if(position>=0) return Reject("请将取名道具放到已穿戴的装备上");
            item["owner"]=c.name;
        }
        else if(cash==5060001 || cash>=5061000 && cash<=5061003)
        {
            if(expiration>-1 && (flag&1)==0) return Reject("这件时限道具不能封印");
            item["flag"]=(long)(flag|1);
            int days=cash==5061000 ? 7 : cash==5061001 ? 30 : cash==5061002 ? 90 : cash==5061003 ? 365 : 0;
            if(days>0) item["expiration"]=(expiration>-1 ? expiration : now)+days*86400000L;
        }
        else if(cash/10000==550)
        {
            var hourglass=BotItemCatalog.Shared.Info(cash);
            int seconds=BotItemCatalog.Int(hourglass,"addTime"),days=BotItemCatalog.Int(hourglass,"maxDays");
            if(position>=0 || expiration<=0) return Reject("请选择身上已穿戴的时限装备");
            if(BotItemCatalog.Int(info,"cash")==1) return Reject("现金装备无法延长使用时间");
            if(seconds<=0) return Reject("沙漏没有提供有效的延长时间");
            long end=expiration+seconds*1000L;
            if(days>0 && end>now+days*86400000L) return Reject("延长后超过此沙漏允许的天数上限");
            item["expiration"]=end;
        }
        else if(cash/10000==552)
        {
            int karma=id/1000000==1 ? 16 : 2;
            if(DbStore.Int(item,"quantity")<=0 || (flag&karma)!=0 || BotItemCatalog.Int(info,"tradeAvailable")<=0) return Reject("这件物品无法使用宿命剪刀");
            item["flag"]=(long)(flag|karma);
        }
        else if(cash/10000==557)
        {
            var eq=InventoryDao.EquipmentOf(db,request.inventoryItemId);
            if(id/1000000!=1 || position<=0 || eq==null || DbStore.Int(eq,"vicious")>=2) return Reject("请选择背包中金锤次数不足两次的装备");
            if(!NativeScrollService.HasStats(CharService.ToInventoryEntry(db,item)))
            {
                var clean=BotItemCatalog.Shared.CleanEquip(id);
                eq["upgradeslots"]=(long)clean.upgradeslots;
                string[] columns={"str","dex","int","luk","hp","mp","watk","matk","wdef","mdef","acc","avoid","speed","jump"};
                int[] values={clean.str,clean.dex,clean.int_,clean.luk,clean.hp,clean.mp,clean.watk,clean.matk,clean.wdef,clean.mdef,clean.acc,clean.avoid,clean.speed,clean.jump};
                for(int i=0;i<columns.Length;i++) eq[columns[i]]=(long)values[i];
                eq["nativeStats"]=1L;
            }
            eq["vicious"]=(long)(DbStore.Int(eq,"vicious")+1);
            eq["upgradeslots"]=(long)(DbStore.Int(eq,"upgradeslots")+1);
        }
        else return Reject("无效的装备现金道具");
        InventoryDao.ConsumeItem(db,request.characterId,cash,1); db.Save();
        return new EquipPatchResponse {inventory=CharService.Load(db,request.characterId).inventory};
    }
}
