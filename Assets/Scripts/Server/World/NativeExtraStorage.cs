using System;

// ExtraStorage.java + ExtraStorageUtil.java: separate counts by account, character,
// item and script category; inventory and storage changes commit together.
public static class NativeExtraStorage
{
    public static InventoryChangeResponse Handle(DbStore db,CharRecord character,InventoryChangeRequest request)
    {
        lock(db)
        {
            var table=db.Table("extrastorage");
            DbRow Find()=>table.Find(row=>DbStore.Int(row,"accountid")==character.accountid &&
                DbStore.Int(row,"characterid")==character.id && DbStore.Int(row,"itemid")==request.itemId &&
                DbStore.Int(row,"type")==request.extraStorageType);
            var row=Find();
            int stored=row==null?0:DbStore.Int(row,"quantity");
            if(request.extraStorage==ExtraStorageOperation.Read)
                return new InventoryChangeResponse {extraStorageQuantity=stored};
            if(request.quantity<=0 || request.quantity>short.MaxValue || request.itemId/1000000<2 || request.itemId/1000000>5)
                return new InventoryChangeResponse {ok=false,error="无效物品或存取数量"};
            bool withdraw=request.extraStorage==ExtraStorageOperation.Withdraw;
            if(withdraw ? stored<request.quantity : InventoryDao.CountOf(db,(int)character.id,request.itemId)<request.quantity)
                return new InventoryChangeResponse {ok=false,error=withdraw?"物品仓库中数量不足":"背包中物品数量不足"};
            if(!withdraw && stored>int.MaxValue-request.quantity)
                return new InventoryChangeResponse {ok=false,error="仓库数量已达上限"};
            using(db.DeferSaves())
            {
                db.BeginTransaction();
                try
                {
                    var changed=NativeInventoryService.Handle(db,new InventoryChangeRequest {
                        characterId=(int)character.id,itemId=request.itemId,quantity=withdraw?request.quantity:-request.quantity });
                    if(!changed.ok){db.Rollback();return changed;}
                    int remaining=stored+(withdraw?-request.quantity:request.quantity);
                    if(remaining==0)
                        table.Delete(value=>ReferenceEquals(value,row));
                    else if(row!=null)row["quantity"]=(long)remaining;
                    else table.Insert(new DbRow { ["accountid"]=(long)character.accountid,["characterid"]=character.id,
                        ["itemid"]=(long)request.itemId,["type"]=(long)request.extraStorageType,["quantity"]=(long)remaining });
                    db.Commit();db.Save();
                    changed.extraStorageQuantity=remaining;
                    return changed;
                }
                catch {db.Rollback();throw;}
            }
        }
    }
}
