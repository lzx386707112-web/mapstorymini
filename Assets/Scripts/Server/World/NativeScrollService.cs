using System;
using System.Collections.Generic;
using Server.Bot;

// ScrollHandler + ItemInformationProvider.scrollEquipWithId. No Unity dependency.
public static class NativeScrollService
{
    static readonly string[] Columns={"str","dex","int","luk","hp","mp","watk","matk","wdef","mdef","acc","avoid","speed","jump"};
    static readonly string[] WzStats={"STR","DEX","INT","LUK","MHP","MMP","PAD","MAD","PDD","MDD","ACC","EVA","Speed","Jump"};
    static EquipPatchResponse Reject(string error)=>new EquipPatchResponse {ok=false,error=error};
    public static bool HasStats(InventoryEntry e)
        => e!=null && (e.equipmentData!=null && e.equipmentData.TryGetValue("nativeStats",out var marker) && System.Convert.ToInt32(marker)!=0 ||
            e.str!=0 || e.dex!=0 || e.int_!=0 || e.luk!=0 || e.hp!=0 || e.mp!=0 || e.watk!=0 || e.matk!=0 || e.wdef!=0 || e.mdef!=0 || e.acc!=0 || e.avoid!=0 || e.speed!=0 || e.jump!=0 || e.upgradeslots!=0 || e.starLevel!=0);
    static bool Clean(int id)=>id>=2049000 && id<=2049003;
    static bool Chaos(int id)=>id>=2049100 && id<=2049103 || id==2049115 || id==2049116;
    static bool Modifier(int id)=>id==2040727 || id==2041058;
    static bool Category(int scroll,int equip)=>scroll/100==20492 ? equip/10000==111 || equip/10000==112 || equip/10000==113 : scroll/100%100==equip/10000%100;
    public static EquipPatchResponse Apply(DbStore db,EquipPatchRequest request,Random random=null)
    {
        random ??= new Random();
        var character=CharactersDao.FindById(db,request.characterId);
        if(character==null || request.scrollId/10000!=204) return Reject("无效卷轴");
        var items=db.Table("inventoryitems");
        var item=items.Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Long(x,"inventoryitemid")==request.inventoryItemId && DbStore.Int(x,"itemid")/1000000==1);
        var scroll=items.Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Int(x,"itemid")==request.scrollId && DbStore.Int(x,"position")>0 && (request.scrollPosition==0 || DbStore.Int(x,"position")==request.scrollPosition) && DbStore.Int(x,"quantity")>0);
        if(item==null || scroll==null) return Reject("装备或卷轴不存在");
        if(DbStore.Int(item,"position")>0 && request.vegaItemId==0 && (SkillsDao.ByCharacter(db,request.characterId).Find(x=>DbStore.Int(x,"skillid")==1003 && DbStore.Int(x,"skilllevel")>0)==null)) return Reject("请将卷轴放到已穿戴的装备上，或先学习匠人之魂");
        int id=DbStore.Int(item,"itemid"),sid=request.scrollId;
        if(request.scrollRequirements!=null && request.scrollRequirements.Length>0 && Array.IndexOf(request.scrollRequirements,id)<0) return Reject("这件装备不符合卷轴要求");
        if(!Clean(sid) && !Chaos(sid) && sid!=2049920 && !Category(sid,id)) return Reject("卷轴与装备种类不符");
        var stats=BotItemCatalog.Shared.EquipStats(sid);
        if(stats==null) return Reject("源端没有这张卷轴的数据");
        var storedEq=InventoryDao.EquipmentOf(db,request.inventoryItemId);
        var eq=new DbRow();
        if(storedEq!=null) foreach(var pair in storedEq) eq[pair.Key]=pair.Value;
        var entry=CharService.ToInventoryEntry(db,item);
        if(!HasStats(entry))
        {
            var clean=BotItemCatalog.Shared.EquipStats(id);
            for(int i=0;i<Columns.Length;i++) eq[Columns[i]]=(long)(short)(clean!=null && clean.TryGetValue(WzStats[i],out int v) ? v : 0);
            eq["upgradeslots"]=(long)BotItemCatalog.Int(BotItemCatalog.Shared.Info(id),"tuc");
        }
        int slots=DbStore.Int(eq,"upgradeslots"),level=DbStore.Int(eq,"level"),vicious=DbStore.Int(eq,"vicious");
        int total=BotItemCatalog.Int(BotItemCatalog.Shared.Info(id),"tuc");
        if(Clean(sid) && (total==0 || slots+level>=total+vicious)) return Reject("没有可恢复的升级次数");
        if(!Clean(sid) && !Modifier(sid) && sid!=2049920 && slots<1) return Reject("装备可升级次数不足");
        DbRow white=null;
        if(request.whiteScroll) white=items.Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Int(x,"position")>0 && DbStore.Int(x,"quantity")>0 && DbStore.Int(x,"itemid")==2340001)
            ?? items.Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Int(x,"position")>0 && DbStore.Int(x,"quantity")>0 && DbStore.Int(x,"itemid")==2340000);
        DbRow vega=null;
        if(request.vegaItemId!=0)
        {
            if(request.vegaItemId!=5610000 && request.vegaItemId!=5610001) return Reject("无效的织女星咒语");
            vega=items.Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Int(x,"position")>0 && DbStore.Int(x,"quantity")>0 && DbStore.Int(x,"itemid")==request.vegaItemId);
            if(vega==null) return Reject("没有持有织女星咒语");
        }
        double chance=stats["success"];
        if(request.vegaItemId==5610000 && chance==10) chance=30;
        if(request.vegaItemId==5610001 && chance==60) chance=90;
        bool perfect=character.gm>0 && GameConfigDao.GetBool(db,"use_perfect_gm_scroll",true);
        bool success=perfect || random.NextDouble()>=Math.Pow(1-chance/100.0,GameConfigDao.GetInt(db,"scroll_chance_rolls",1));
        bool destroyed=false;
        if(success)
        {
            if(Clean(sid) || sid==2049920) eq["upgradeslots"]=(long)(sbyte)(slots+1);
            else
            {
                if(sid==2040727) item["flag"]=(long)(DbStore.Int(item,"flag")|2);
                else if(sid==2041058) item["flag"]=(long)(DbStore.Int(item,"flag")|4);
                else if(sid==2049100 || sid==2049101 || sid==2049102 || sid==2049115 || sid==2049116)
                {
                    bool forward=sid==2049115 || sid==2049116;
                    int range=GameConfigDao.GetInt(db,forward ? "forward_chaos_scroll_stat_range" : "chaos_scroll_stat_range",5);
                    int rolls=GameConfigDao.GetInt(db,"chaos_scroll_stat_rate",1);
                    bool enhanced=GameConfigDao.GetBool(db,"use_enhanced_chaos_scroll",false);
                    foreach(string column in Columns)
                    {
                        int current=DbStore.Int(eq,column); if(current<=0) continue;
                        int best=enhanced ? current : rolls>0 ? short.MinValue : 0;
                        for(int i=0;i<Math.Max(1,rolls);i++) best=Math.Min(short.MaxValue,Math.Max(best,(enhanced && rolls>0 ? best : current)+random.Next(forward ? 0 : -range,range+1)));
                        eq[column]=(long)Math.Max(0,best);
                    }
                }
                else for(int i=0;i<Columns.Length;i++) if(stats.TryGetValue(WzStats[i],out int delta)) eq[Columns[i]]=(long)(short)Math.Min(short.MaxValue,DbStore.Int(eq,Columns[i])+delta);
                if(!perfect && !Modifier(sid)) eq["upgradeslots"]=(long)(sbyte)(slots-1);
                eq["level"]=(long)(sbyte)(level+1);
            }
        }
        else
        {
            if(!perfect && !GameConfigDao.GetBool(db,"use_perfect_scrolling",false) && white==null && !Clean(sid) && sid!=2049920 && !Modifier(sid)) eq["upgradeslots"]=(long)(sbyte)(slots-1);
            destroyed=random.Next(100)<stats["cursed"];
        }
        if(white!=null && !Clean(sid)) Consume(db,white);
        Consume(db,scroll);
        if(vega!=null) Consume(db,vega);
        if(destroyed && id!=1112803 && id!=1112806 && id!=1112807 && id!=1112809)
        { items.Delete(x=>DbStore.Long(x,"inventoryitemid")==request.inventoryItemId); db.Table("inventoryequipment").Delete(x=>DbStore.Long(x,"inventoryitemid")==request.inventoryItemId); }
        else
        {
            destroyed=false; eq["nativeStats"]=1L;
            if(storedEq==null) { InventoryDao.InsertEquipment(db,request.inventoryItemId); storedEq=InventoryDao.EquipmentOf(db,request.inventoryItemId); }
            foreach(var pair in eq) storedEq[pair.Key]=pair.Value;
        }
        db.Save();
        return new EquipPatchResponse {scrollResult=destroyed ? "curse" : success ? "success" : "fail",inventory=CharService.Load(db,request.characterId).inventory};
    }
    static void Consume(DbStore db,DbRow row)
    {
        int count=DbStore.Int(row,"quantity"); long id=DbStore.Long(row,"inventoryitemid");
        if(count>1) row["quantity"]=(long)(count-1);
        else db.Table("inventoryitems").Delete(x=>DbStore.Long(x,"inventoryitemid")==id);
    }
}
