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
    // 「建了行但没种值」：inventoryequipment 全 0（属性、upgradeslots、level、vicious 都是 0）。
    // 这种行不是「砸光了次数」，而是根本没初始化过——脚本 cm.gainItem、后台发放、掉宝建行都只写
    // 0 值；旧存档还可能被 forceUpdateItem 用客户端全 0 快照盖过（nativeStats 标记写上、值仍是 0）。
    // 砸卷前必须按 Items.json 的 tuc 补种，否则会出现「一次没砸过却提示可升级次数不足」。
    static bool Pristine(DbRow row)
    {
        if(DbStore.Int(row,"level")!=0 || DbStore.Int(row,"vicious")!=0 || DbStore.Int(row,"upgradeslots")!=0) return false;
        for(int i=0;i<Columns.Length;i++) if(DbStore.Int(row,Columns[i])!=0) return false;
        return true;
    }
    // 「属性已经种上、只有槽位还是 0」：先上过星力/星火、砸卷次数没跟着种子的行（星力只种 base 属性，
    // 不写 upgradeslots；老存档建行时代也没种）。判据只看「从没砸过卷」——等级/邪惡/槽位全 0；
    // 绝不能看属性列，否则补种时会把星力基础属性一起重置回模板值。
    static bool UnsownSlots(DbRow row)
        => DbStore.Int(row,"level")==0 && DbStore.Int(row,"vicious")==0 && DbStore.Int(row,"upgradeslots")==0;
    static bool Clean(int id)=>id>=2049000 && id<=2049003;
    static bool Chaos(int id)=>id>=2049100 && id<=2049103 || id==2049115 || id==2049116;
    static bool Modifier(int id)=>id==2040727 || id==2041058;
    // 心脏卷轴不走 204 卷轴段：2616004~2616010 未來心臟專用、2643008 妖精之心強化石、
    // 2643009/2643010 卡勒馬特殊心臟卷。ItemInformationProvider 的 req/reqScope 数据在
    // Items.json（tools/export_solomapling_items.py），服务端读不到 IMG，所以适用范围
    // 只能按 167xxxx 心脏段落地。逐件限定的（未來心臟六款、妖精之心）另外用 Items.json 的
    // req 白名单在下面校验，与源端 ItemUpgradeHandler 的 getReqItemIds 同一口径。
    static bool HeartScroll(int id)=>IsHeartScroll(id);
    public static bool IsHeartScroll(int id)=>id>=2616004 && id<=2616010 || id>=2643008 && id<=2643010;
    static bool HeartEquip(int id)=>id/10000==167;
    // 心脏卷轴的拒绝提示必须自带「能砸哪些心脏」：玩家拿着 0 槽的銀心只看到 tuc=0，
    // 不知道该换哪颗。逐颗列出 req 白名单里的心脏与各自槽数；reqScope 的卡勒馬
    // 两张没有白名单（整段 167xxxx 都适用），返回 null 回退通用文案。
    static string HeartHint(int scroll)
    {
        var required=BotItemCatalog.Shared.RequiredEquips(scroll);
        if(required==null || required.Length==0) return null;
        var parts=new List<string>();
        foreach(int heart in required)
        {
            int tuc=BotItemCatalog.Int(BotItemCatalog.Shared.Info(heart),"tuc");
            parts.Add((BotItemCatalog.Shared.Name(heart) ?? ("道具 "+heart))+(tuc>0 ? "（"+tuc+" 槽）" : "（0 槽）"));
        }
        return "这张卷轴适用于："+string.Join("、",parts);
    }
    static bool Category(int scroll,int equip)
        => HeartScroll(scroll) ? HeartEquip(equip)
            : scroll/100==20492 ? equip/10000==111 || equip/10000==112 || equip/10000==113 : scroll/100%100==equip/10000%100;
    public static EquipPatchResponse Apply(DbStore db,EquipPatchRequest request,Random random=null)
    {
        if(request==null)return Reject("无效卷轴请求");
        if(NativePotentialService.IsReserved(db,request.inventoryItemId))return Reject("请先选择方块候选结果");
        var enhancement=InventoryDao.EquipmentOf(db,request.inventoryItemId);
        if(enhancement!=null&&DbStore.Int(enhancement,"starForceVestige")!=0)return Reject("装备痕迹需要先复原");
        random ??= new Random();
        var character=CharactersDao.FindById(db,request.characterId);
        if(character==null) return Reject("无效卷轴");
        if(request.scrollId/10000!=204 && !HeartScroll(request.scrollId)) return Reject("无效卷轴");
        var items=db.Table("inventoryitems");
        var item=items.Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Long(x,"inventoryitemid")==request.inventoryItemId && DbStore.Int(x,"itemid")/1000000==1);
        var scroll=items.Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Int(x,"itemid")==request.scrollId && DbStore.Int(x,"position")>0 && (request.scrollPosition==0 || DbStore.Int(x,"position")==request.scrollPosition) && DbStore.Int(x,"quantity")>0);
        if(item==null || scroll==null) return Reject("装备或卷轴不存在");
        if(DbStore.Int(item,"position")>0 && request.vegaItemId==0 && (SkillsDao.ByCharacter(db,request.characterId).Find(x=>DbStore.Int(x,"skillid")==1003 && DbStore.Int(x,"skilllevel")>0)==null)) return Reject("请将卷轴放到已穿戴的装备上，或先学习匠人之魂");
        int id=DbStore.Int(item,"itemid"),sid=request.scrollId;
        if(request.scrollRequirements!=null && request.scrollRequirements.Length>0 && Array.IndexOf(request.scrollRequirements,id)<0) return Reject("这件装备不符合卷轴要求");
        if(!Clean(sid) && !Chaos(sid) && sid!=2049920 && !Category(sid,id)) return Reject("卷轴与装备种类不符");
        // 心脏卷轴的逐件白名单是服务端权威：客户端虽然也会把 WZ 的 req 传上来，但 GM/脚本/
        // 老客户端都不保证带，源端 ItemUpgradeHandler 是用 getReqItemIds 在服务端卡的。
        if(HeartScroll(sid))
        {
            var required=BotItemCatalog.Shared.RequiredEquips(sid);
            bool listed=required!=null && required.Length>0;
            if(listed && Array.IndexOf(required,id)<0)
            {
                var hint=HeartHint(sid);
                return Reject("这件心脏不符合卷轴要求"+(hint==null ? "" : "，"+hint));
            }
            // 卡勒馬两张没有 req，只有 reqScope(167/10000)。数据缺失时宁可拒绝，也不放开整段。
            if(!listed && !BotItemCatalog.Shared.ScopedTo(sid,id)) return Reject("这张卷轴没有适用这件心脏的数据");
        }
        var stats=BotItemCatalog.Shared.EquipStats(sid);
        if(stats==null) return Reject("源端没有这张卷轴的数据");
        var storedEq=InventoryDao.EquipmentOf(db,request.inventoryItemId);
        var eq=new DbRow();
        if(storedEq!=null) foreach(var pair in storedEq) eq[pair.Key]=pair.Value;
        var entry=CharService.ToInventoryEntry(db,item);
        int seedSlots=BotItemCatalog.Int(BotItemCatalog.Shared.Info(id),"tuc");
        if(!HasStats(entry) || Pristine(eq))
        {
            var clean=BotItemCatalog.Shared.EquipStats(id);
            for(int i=0;i<Columns.Length;i++) eq[Columns[i]]=(long)(short)(clean!=null && clean.TryGetValue(WzStats[i],out int v) ? v : 0);
            eq["upgradeslots"]=(long)seedSlots;
        }
        else if(UnsownSlots(eq)) eq["upgradeslots"]=(long)seedSlots;
        int slots=DbStore.Int(eq,"upgradeslots"),level=DbStore.Int(eq,"level"),vicious=DbStore.Int(eq,"vicious");
        int total=BotItemCatalog.Int(BotItemCatalog.Shared.Info(id),"tuc");
        // 白卷：源端 canUseCleanSlate（free + applied < tuc + vicious）。良医卷 2049920 源端是
        // 「无条件 +1 槽」，但本地单机按用户决定一并收口到同一上限，否则能与普通卷轮流无限刷。
        // 失败掉过次数时 slots+level 才小于上限，恢复类卷轴只能把损失补回来，不能超过 tuc+vicious。
        bool restores=Clean(sid)||sid==2049920;
        if(restores)
        {
            if(total==0) return Reject("这件装备没有可升级次数（tuc=0），无法恢复");
            if(slots+level>=total+vicious) return Reject("没有可恢复的升级次数（已用 "+(slots+level)+" / 上限 "+(total+vicious)+" 次）");
        }
        if(!Clean(sid) && !Modifier(sid) && sid!=2049920 && slots<1)
        {
            // 报清楚是哪件装备、还剩几次、上限几次——不然「没砸过却说次数不足」无从判断。
            string name=BotItemCatalog.Shared.Name(id) ?? ("道具 "+id);
            if(total<=0)
            {
                if(!HeartScroll(sid)) return Reject(name+" 没有可升级次数（数据里 tuc=0），砸不了卷");
                var hint=HeartHint(sid);
                return Reject(name+" 是 0 槽心脏，砸不了卷"+(hint==null ? "（数据里 tuc=0）" : "，"+hint));
            }
            return Reject("装备可升级次数不足（"+name+"：剩 "+slots+" 次 / 共 "+total+" 次，已升级 "+level+" 次）");
        }
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
                // GM 完美卷轴只保留「必成功」，不再免扣升级次数。源端 use_perfect_gm_scroll 的
                // never uses up slots 语义在本地单机不成立：admin 号恒定 gm>0，不扣次数就能无限砸卷。
                // 扣槽后 slots+level 恒等于 tuc+vicious，砸满即被下面的 slots<1 拦住。
                if(!Modifier(sid)) eq["upgradeslots"]=(long)(sbyte)(slots-1);
                eq["level"]=(long)(sbyte)(level+1);
            }
        }
        else
        {
            // 失败掉一次升级次数：GM 必成功不会走到这里；普通玩家由 use_perfect_scrolling / 祝福卷保护。
            if(!GameConfigDao.GetBool(db,"use_perfect_scrolling",false) && white==null && !Clean(sid) && sid!=2049920 && !Modifier(sid)) eq["upgradeslots"]=(long)(sbyte)(slots-1);
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
