using System;
using System.Collections.Generic;
using Server.Bot;

public sealed class PotentialRequest
{
    public int characterId,revision;
    public long inventoryItemId,cubeInventoryItemId;
    public bool commit,choose,keepNew;
}
public sealed class PotentialResponse
{
    public bool ok=true,pending;
    public string error="",scrollResult="";
    public int revision,grade;
    public int[] main,bonus,candidate;
    public long cost,meso;
    public List<InventoryEntry> inventory;
}
public static class NativePotentialService
{
    static PotentialRoller roller;
    static PotentialRoller Shared => roller ??= new PotentialRoller(PotentialCatalog.Shared,
        new CubeRateTable(PlatformRes.LoadText("Enhancement/CubeRates")));
    static PotentialResponse Reject(string message)=>new PotentialResponse {ok=false,error=message};
    public static bool IsReserved(DbStore db,long id)
    {
        var row=InventoryDao.EquipmentOf(db,id);
        return row!=null&&!string.IsNullOrEmpty(DbStore.Str(row,"potentialChoice"));
    }
    public static bool BlocksTransfer(DbStore db,InventoryEntry item)
    {
        if(item==null)return false;
        var held=db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==item.itemRowId);
        if(held!=null && NativeSoulWeaponWz2Service.IsBound(db,DbStore.Int(held,"characterid"),item.itemRowId))return true;
        var row=InventoryDao.EquipmentOf(db,item.itemRowId);
        if(row!=null&&(DbStore.Int(row,"starForceVestige")!=0||!string.IsNullOrEmpty(DbStore.Str(row,"potentialChoice"))))return true;
        var data=item.equipmentData;
        return data!=null&&(data.TryGetValue("starForceVestige",out var vestige)&&Convert.ToInt32(vestige)!=0
            ||data.TryGetValue("potentialChoice",out var pending)&&!string.IsNullOrEmpty(pending as string));
    }
    public static int[] Options(DbRow equipment,bool bonus)
    {
        string prefix=bonus?"bonusOption":"potentialOption";
        return new[]{DbStore.Int(equipment,prefix+"1"),DbStore.Int(equipment,prefix+"2"),DbStore.Int(equipment,prefix+"3")};
    }
    static bool Expired(DbRow item)
    {long time=DbStore.Long(item,"expiration",-1);return time>0&&time<=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();}
    public static PotentialResponse Handle(DbStore db,PotentialRequest request,PotentialRoller rolls=null,BotItemCatalog catalog=null,Random random=null)
    {
        if(db==null||request==null)return Reject("无效潜能请求");
        lock(db)
        {
            var character=CharactersDao.FindById(db,request.characterId);
            var target=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==request.characterId&&DbStore.Long(x,"inventoryitemid")==request.inventoryItemId);
            if(character==null||target==null||Expired(target))return Reject("装备不存在或已到期");
            int inv=DbStore.Int(target,"inventorytype"),pos=DbStore.Int(target,"position");
            if(inv!=InventoryDao.Equip&&inv!=InventoryDao.Equipped||NativeAndroidService.IsApSlot(pos))return Reject("请选择背包或已穿戴的装备");
            var equipment=InventoryDao.EquipmentOf(db,request.inventoryItemId);
            if(equipment==null||DbStore.Int(equipment,"starForceVestige")!=0)return Reject("装备痕迹不能洗潜能");
            if(DbStore.Int(equipment,"enhancementVersion")!=282&&(DbStore.Int(equipment,"starLevel")!=0||!string.IsNullOrEmpty(DbStore.Str(equipment,"expandAttribute4"))))
                return Reject("旧强化装备尚未完成版本切换");
            int revision=DbStore.Int(equipment,"potentialRevision");
            if(request.commit&&request.revision!=revision)return Reject("装备状态已变化，请重新预览");
            var main=Options(equipment,false);var bonus=Options(equipment,true);
            if(request.choose)return Choose(db,request,equipment,character.meso,main,bonus);
            if(IsReserved(db,request.inventoryItemId))return Reject("请先选择上一次方块结果");
            // One outstanding comparison per character, matching the source journal contract.
            foreach(var item in db.Table("inventoryitems").rows)
                if(DbStore.Int(item,"characterid")==request.characterId&&IsReserved(db,DbStore.Long(item,"inventoryitemid")))return Reject("请先选择上一次方块结果");
            int itemId=DbStore.Int(target,"itemid"),prefix=itemId/10000;
            if(prefix==156||prefix==157)return Reject("零职业双武器潜能链尚未接入");
            if(DbStore.Int(equipment,"locked")!=0||(DbStore.Int(target,"flag")&1)!=0)return Reject("装备已上锁");
            catalog ??= BotItemCatalog.Shared;
            var info=catalog.Info(itemId);
            if(info==null||BotItemCatalog.Int(info,"cash")!=0||BotItemCatalog.Int(info,"noPotential")!=0||BotItemCatalog.Int(info,"fixedPotential")!=0)
                return Reject("这件装备不能变更潜能");
            if(BotItemCatalog.Int(info,"tuc")<1&&!TucIgnore(itemId))return Reject("装备模板不能拥有潜能");
            if(!PotentialType(prefix))return Reject("这类装备不能拥有潜能");
            var cube=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==request.characterId&&DbStore.Long(x,"inventoryitemid")==request.cubeInventoryItemId);
            if(cube==null||Expired(cube)||DbStore.Int(cube,"quantity")<1||DbStore.Int(cube,"position")<=0
                ||DbStore.Int(cube,"inventorytype")!=2&&DbStore.Int(cube,"inventorytype")!=5)return Reject("方块不存在或已到期");
            int cubeId=DbStore.Int(cube,"itemid");
            if(cubeId==2049400||cubeId==2049401)
                return ApplyScroll(db,request,equipment,character,cube,itemId,info,main,bonus,revision,rolls??Shared,random??new Random());
            var family=CubeRules.GetFamily(cubeId);
            if(family==null)return Reject("此方块的特殊规则尚未接入");
            int grade=DbStore.Int(equipment,family.bonus?"bonusGrade":"potentialGrade");
            if(grade<family.minimum||grade>family.maximum)return Reject("潜能等级不适用此方块");
            int flag=DbStore.Int(target,"flag");
            if(family.karma&&BotItemCatalog.Int(info,"tradeBlock")==0&&(flag&8)==0)return Reject("卡勒马方块需要不可交易装备");
            int level=BotItemCatalog.Int(info,"reqLevel")+DbStore.Int(equipment,"iIncReq");
            long cost=CubeRules.AppraisalCost(level,cubeId);
            var response=new PotentialResponse {revision=revision,grade=grade,main=main,bonus=bonus,cost=cost,meso=character.meso};
            if(!request.commit)return response;
            if(character.meso<cost)return Reject("枫币不足");
            PotentialRoller.Result result;
            try {result=(rolls??Shared).Roll(cubeId,itemId,level,grade,main,bonus,random??new Random());}
            catch(InvalidOperationException e){return Reject(e.Message);}
            db.BeginTransaction();
            try
            {
                int quantity=DbStore.Int(cube,"quantity");
                if(quantity==1)db.Table("inventoryitems").Delete(x=>DbStore.Long(x,"inventoryitemid")==request.cubeInventoryItemId);
                else cube["quantity"]=(long)quantity-1;
                character.meso-=cost;CharactersDao.Save(db,character);
                equipment["enhancementVersion"]=282L;
                equipment["potentialRevision"]=checked((long)revision+1);
                if(family.choice)
                    equipment["potentialChoice"]="{\"bonus\":"+(family.bonus?"true":"false")+",\"grade\":"+result.grade
                        +",\"options\":["+string.Join(",",result.options)+"],\"cubeId\":"+cubeId+"}";
                else Write(equipment,family.bonus,result.grade,result.options);
                db.Save();db.Commit();
            }
            catch {db.Rollback();throw;}
            response.revision=revision+1;response.meso=character.meso;response.pending=family.choice;
            response.candidate=result.options;response.grade=result.grade;
            response.main=Options(equipment,false);response.bonus=Options(equipment,true);
            response.inventory=CharService.Load(db,request.characterId).inventory;
            return response;
        }
    }
    // ItemUpgradeHandler: rare potential scrolls immediately release their hidden options.
    // User correction: 2049400/1 use the client text's 90%/70%, not missing-field default 100%.
    static PotentialResponse ApplyScroll(DbStore db,PotentialRequest request,DbRow equipment,CharRecord character,
        DbRow scroll,int itemId,Dictionary<string,object> info,int[] main,int[] bonus,int revision,PotentialRoller rolls,Random random)
    {
        if(main[0]!=0)return Reject("这件装备已经拥有潜能");
        int level=BotItemCatalog.Int(info,"reqLevel")+DbStore.Int(equipment,"iIncReq");
        var response=new PotentialResponse {revision=revision,main=main,bonus=bonus,meso=character.meso};
        if(!request.commit)return response;
        bool success=random.Next(100)<(DbStore.Int(scroll,"itemid")==2049400?90:70);
        PotentialRoller.Result result=null;
        if(success)
        {
            var hidden=new[]{-1,-1,random.Next(100)<10?-1:0};
            try {result=rolls.Reveal(itemId,level,1,false,hidden,bonus,random);}
            catch(InvalidOperationException e){return Reject(e.Message);}
        }
        db.BeginTransaction();
        try
        {
            int quantity=DbStore.Int(scroll,"quantity");
            if(quantity==1)db.Table("inventoryitems").Delete(x=>DbStore.Long(x,"inventoryitemid")==request.cubeInventoryItemId);
            else scroll["quantity"]=(long)quantity-1;
            equipment["enhancementVersion"]=282L;equipment["potentialRevision"]=checked((long)revision+1);
            if(result!=null)Write(equipment,false,result.grade,result.options);
            db.Save();db.Commit();
        }
        catch {db.Rollback();throw;}
        response.revision=revision+1;response.main=Options(equipment,false);response.grade=DbStore.Int(equipment,"potentialGrade");
        response.scrollResult=success?"success":"fail";response.inventory=CharService.Load(db,request.characterId).inventory;
        return response;
    }
    static void Write(DbRow row,bool bonus,int grade,int[] options)
    {
        row[bonus?"bonusGrade":"potentialGrade"]=(long)grade;
        string prefix=bonus?"bonusOption":"potentialOption";
        for(int i=0;i<3;i++)row[prefix+(i+1)]=(long)options[i];
    }
    static PotentialResponse Choose(DbStore db,PotentialRequest request,DbRow row,long meso,int[] main,int[] bonus)
    {
        var pending=MiniJsonDb.Parse(DbStore.Str(row,"potentialChoice")) as Dictionary<string,object>;
        if(pending==null)return Reject("没有待选择的方块结果");
        int grade=Convert.ToInt32(pending["grade"]);
        var list=(List<object>)pending["options"];
        var candidate=new[]{Convert.ToInt32(list[0]),Convert.ToInt32(list[1]),Convert.ToInt32(list[2])};
        int revision=DbStore.Int(row,"potentialRevision");
        var response=new PotentialResponse {revision=revision,main=main,bonus=bonus,candidate=candidate,grade=grade,meso=meso,pending=true};
        if(!request.commit)return response;
        db.BeginTransaction();
        try
        {
            if(request.keepNew)Write(row,(bool)pending["bonus"],grade,candidate);
            row["potentialChoice"]="";row["potentialRevision"]=checked((long)revision+1);
            db.Save();db.Commit();
        }
        catch {db.Rollback();throw;}
        response.revision=revision+1;response.pending=false;response.main=Options(row,false);response.bonus=Options(row,true);
        response.inventory=CharService.Load(db,request.characterId).inventory;return response;
    }
    static bool PotentialType(int prefix)=>prefix>=100&&prefix<=115&&prefix!=114||prefix==118||prefix==119||prefix>=121&&prefix<160||prefix==167;
    static bool TucIgnore(int id)=>id/10000==135||id/10000==119||id>=1099000&&id<=1099015||id>=1098000&&id<=1098008;
}
