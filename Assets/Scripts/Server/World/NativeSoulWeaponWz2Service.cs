using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class SoulWeaponWz2State
{
    public long weapon, revision, activeUntil, cooldownUntil;
    public int level=1, grade=1, exp, fragments, option;
    public bool unlocked, converted;
    public string week="";
    public Dictionary<string,int> weekly=new Dictionary<string,int>();
    public List<int> cleared=new List<int>();
    public bool Equipped(FullCharacter character) => weapon>0 && character?.inventory!=null &&
        character.inventory.Exists(i=>i.itemRowId==weapon && i.position==-11 && (i.expiration<=0 || i.expiration>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
}

// Server-owned ledger, separate from client-saveable growth/inventory snapshots.
// User-authorized policies: level-200 unlock and equal-weight normal-grade options.
public static class NativeSoulWeaponWz2Service
{
    const int LedgerType=32;
    const string LedgerName="tms282.soulWeaponWz2";
    static DbRow Row(DbStore db,int cid) => db.Table("extend_value").Find(r=>DbStore.Int(r,"extend_type")==LedgerType &&
        DbStore.Str(r,"extend_id")==cid.ToString(CultureInfo.InvariantCulture) && DbStore.Str(r,"extend_name")==LedgerName);
    public static bool IsBound(DbStore db,int cid,long itemId) => itemId>0 && Read(db,cid).weapon==itemId;
    public static int SkillSeconds(int grade,string key) => SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Map(SoulWeaponWz2Catalog.Skill(grade),"common"),key);
    static long Long(Dictionary<string,object> d,string k) => d.TryGetValue(k,out var v)&&long.TryParse(v?.ToString(),out long x)?x:0;
    public static SoulWeaponWz2State Read(DbStore db,int cid)
    {
        var s=new SoulWeaponWz2State();var row=Row(db,cid);
        var d=row==null?null:MiniJsonDb.Parse(DbStore.Str(row,"extend_value")) as Dictionary<string,object>;
        if(d==null)return s;
        s.weapon=Long(d,"weapon");s.revision=Long(d,"revision");s.activeUntil=Long(d,"activeUntil");s.cooldownUntil=Long(d,"cooldownUntil");
        s.level=SoulWeaponWz2Catalog.Int(d,"level",1);s.grade=SoulWeaponWz2Catalog.Int(d,"grade",1);
        s.exp=SoulWeaponWz2Catalog.Int(d,"exp");s.fragments=SoulWeaponWz2Catalog.Int(d,"fragments");s.option=SoulWeaponWz2Catalog.Int(d,"option");
        s.unlocked=Long(d,"unlocked")!=0;s.converted=Long(d,"converted")!=0;s.week=SoulWeaponWz2Catalog.Text(d,"week");
        var weekly=SoulWeaponWz2Catalog.Map(d,"weekly");if(weekly!=null)foreach(var x in weekly)s.weekly[x.Key]=Convert.ToInt32(x.Value);
        if(d.TryGetValue("cleared",out var clears)&&clears is List<object> list)foreach(var x in list)s.cleared.Add(Convert.ToInt32(x));
        return s;
    }
    static void Save(DbStore db,int cid,SoulWeaponWz2State s,long now)
    {
        var text=new System.Text.StringBuilder("{");
        void Number(string key,long value){if(text.Length>1)text.Append(',');text.Append('"').Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));}
        Number("weapon",s.weapon);Number("revision",s.revision);Number("activeUntil",s.activeUntil);Number("cooldownUntil",s.cooldownUntil);
        Number("level",s.level);Number("grade",s.grade);Number("exp",s.exp);Number("fragments",s.fragments);Number("option",s.option);
        Number("unlocked",s.unlocked?1:0);Number("converted",s.converted?1:0);
        text.Append(",\"week\":\"").Append(s.week).Append("\",\"weekly\":{");
        bool first=true;foreach(var pair in s.weekly){if(!first)text.Append(',');first=false;text.Append('"').Append(pair.Key).Append("\":").Append(pair.Value.ToString(CultureInfo.InvariantCulture));}
        text.Append("},\"cleared\":[").Append(string.Join(",",s.cleared)).Append("]}");
        var row=Row(db,cid);
        if(row==null)db.Table("extend_value").Insert(new DbRow { ["extend_type"]=(long)LedgerType,["extend_id"]=cid.ToString(CultureInfo.InvariantCulture),
            ["extend_name"]=LedgerName,["extend_value"]=text.ToString(),["create_time"]=now,["update_time"]=now });
        else {row["extend_value"]=text.ToString();row["update_time"]=now;}
    }
    static SoulResponse Reject(string text,SoulWeaponWz2State s=null) => new SoulResponse {ok=false,error=text,modern=s};
    public static SoulResponse Handle(DbStore db,SoulRequest r,Random random=null,DateTimeOffset? instant=null)
    {
        if(db==null || r==null)return Reject("无效灵魂武器请求");
        lock(db)
        {
            var chr=CharactersDao.FindById(db,r.characterId);if(chr==null)return Reject("角色不存在");
            var now=instant??DateTimeOffset.UtcNow;long stamp=now.ToUnixTimeMilliseconds();
            var s=Read(db,r.characterId);
            if(r.operation==SoulOperation.ReadModern)return new SoulResponse {modern=s};
            if(!r.commit)return Reject("操作需要确认",s);
            if(r.revision!=s.revision)return Reject("灵魂武器状态已变化，请重新打开面板",s);
            if(chr.level<200)return Reject("角色达到200级后才能解锁灵魂武器",s);
            if(r.operation<SoulOperation.ActivateModern || r.operation>SoulOperation.CastModern)return Reject("无效的新版灵魂武器操作",s);
            var target=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==r.characterId && DbStore.Long(x,"inventoryitemid")==r.inventoryItemId);
            var equip=target==null?null:InventoryDao.EquipmentOf(db,r.inventoryItemId);
            string error=null;
            if(r.operation==SoulOperation.ActivateModern)
            {
                if(s.weapon!=0)error="已有启用的灵魂武器，请先停用";
                else if(target==null || equip==null)error="请放入持有的武器";
                else if(!NativeSoulService.IsWeapon(DbStore.Int(target,"itemid")))error="只有武器才可启用";
                else if(DbStore.Int(equip,"locked")!=0 || (DbStore.Int(target,"flag")&1)!=0)error="已锁定的武器无法启用";
                else if(DbStore.Long(target,"expiration",-1)>0 && DbStore.Long(target,"expiration")<=stamp)error="武器已到期";
                else
                {
                    var info=Server.Bot.BotItemCatalog.Shared.Info(DbStore.Int(target,"itemid"));
                    if(Server.Bot.BotItemCatalog.Int(info,"reqLevel")+DbStore.Int(equip,"iIncReq")<150)error="只有150级以上的武器才可启用";
                    else if((DbStore.Int(target,"flag")&8)==0 && Server.Bot.BotItemCatalog.Int(info,"tradeBlock")==0)error="只有无法交易的武器才可启用";
                    else if(DbStore.Int(target,"position")<=-100 || NativeAndroidService.IsApSlot(DbStore.Int(target,"position")))error="这件武器不能启用";
                }
            }
            else if(!s.unlocked || s.weapon<=0)error="请先启用灵魂武器";
            else if(r.operation==SoulOperation.LevelModern && (r.amount<=0 || r.amount>s.fragments || r.amount>RemainingExp(s)))error="碎片数量不足，或超过当前阶级经验上限";
            else if(r.operation==SoulOperation.GradeModern)
            {
                int quest=SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(s.grade),"GradeUpReqQuestID");
                if(s.grade>=10)error="已达到最高阶级";
                else if(s.level<SoulWeaponWz2Catalog.MaxLevel(s.grade) || s.exp<SoulWeaponWz2Catalog.RequiredExp(s.level+1))error="当前阶级经验尚未蓄满";
                else if(!s.cleared.Contains(quest))error="尚未完成本阶级要求的Boss单人击破";
            }
            else if(r.operation==SoulOperation.RollModern && s.fragments<SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(s.grade),"OptionRollCost"))error="灵魂碎片不足";
            else if(r.operation==SoulOperation.CastModern)
            {
                var weapon=db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==r.characterId && DbStore.Long(x,"inventoryitemid")==s.weapon);
                if(weapon==null || DbStore.Int(weapon,"position")!=-11)error="请装备已启用的灵魂武器";
                else if(DbStore.Long(weapon,"expiration",-1)>0 && DbStore.Long(weapon,"expiration")<=stamp)error="武器已到期";
                else if(s.cooldownUntil>stamp)error="灵魂技能仍在冷却";
            }
            if(error!=null)return Reject(error,s);
            db.BeginTransaction();
            try
            {
                if(r.operation==SoulOperation.ActivateModern)
                {
                    s.weapon=r.inventoryItemId;s.unlocked=true;
                    if(!s.converted)ConvertLegacy(equip,s,now);
                    target["flag"]=(long)(DbStore.Int(target,"flag")|8);
                    // New system replaces legacy socket/soul bonuses on this weapon.
                    foreach(string key in new[]{"soulSocketId","soulOptionId","soulItemId","soulOption"})equip[key]=0L;
                }
                else if(r.operation==SoulOperation.DeactivateModern){s.weapon=0;s.activeUntil=0;}
                else if(r.operation==SoulOperation.LevelModern){s.fragments-=r.amount;s.exp+=r.amount;NormalizeExp(s);}
                else if(r.operation==SoulOperation.GradeModern){s.grade++;NormalizeExp(s);}
                else if(r.operation==SoulOperation.RollModern)
                {
                    var pool=SoulWeaponWz2Catalog.RollOptions(s.grade);if(pool.Count==0)throw new InvalidOperationException("缺少灵魂烙印候选");
                    s.fragments-=SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(s.grade),"OptionRollCost");
                    s.option=pool[(random??new Random()).Next(pool.Count)];
                }
                else if(r.operation==SoulOperation.CastModern){s.activeUntil=stamp+1000L*SkillSeconds(s.grade,"time");s.cooldownUntil=stamp+1000L*SkillSeconds(s.grade,"cooltime");}
                s.revision++;Save(db,r.characterId,s,stamp);db.Save();db.Commit();
            }
            catch {db.Rollback();throw;}
            NativeGrowthStats.Invalidate();
            return new SoulResponse {success=true,modern=s,inventory=CharService.Load(db,r.characterId).inventory};
        }
    }
    // Trusted event-clear hook only. Never accepts fragment counts or quest IDs from a client request.
    // The original server scripts are absent; weekly maximum per BossIcon is the documented project policy.
    public static int OnBossClear(DbStore db,int cid,string fieldSetName,bool solo,DateTimeOffset? instant=null)
    {
        if(db==null || !solo || string.IsNullOrEmpty(fieldSetName))return 0;
        lock(db)
        {
            var chr=CharactersDao.FindById(db,cid);var state=Read(db,cid);
            if(chr==null || chr.level<200 || !state.unlocked)return 0;
            Dictionary<string,object> boss=null;
            foreach(var pair in SoulWeaponWz2Catalog.Bosses)
                if(pair.Value is Dictionary<string,object> b && SoulWeaponWz2Catalog.Text(b,"FieldSetName")==fieldSetName){boss=b;break;}
            if(boss==null)return 0;
            var now=instant??DateTimeOffset.UtcNow;
            string week=NativeWeeklyGemstoneService.Week(now);
            if(state.week!=week){state.week=week;state.weekly.Clear();}
            string key=SoulWeaponWz2Catalog.Int(boss,"BossIcon").ToString(CultureInfo.InvariantCulture);
            state.weekly.TryGetValue(key,out int previous);
            int total=SoulWeaponWz2Catalog.Int(boss,"FragmentAmount");
            int grant=Math.Max(0,total-previous);
            int quest=SoulWeaponWz2Catalog.Int(boss,"QuestID_GradeUp");
            bool newClear=quest>0&&!state.cleared.Contains(quest);
            if(grant==0&&!newClear)return 0;
            db.BeginTransaction();
            try
            {
                state.weekly[key]=Math.Max(previous,total);
                state.fragments=checked(state.fragments+grant);
                if(newClear)state.cleared.Add(quest);
                state.revision++;Save(db,cid,state,now.ToUnixTimeMilliseconds());db.Save();db.Commit();
            }
            catch{db.Rollback();throw;}
            NativeGrowthStats.Invalidate();return grant;
        }
    }
    public static int RemainingExp(SoulWeaponWz2State s)
    {
        if(s.level>=100)return 0;
        int max=SoulWeaponWz2Catalog.MaxLevel(s.grade),total=-s.exp;
        for(int lv=s.level+1;lv<=Math.Min(100,max+1);lv++)total+=SoulWeaponWz2Catalog.RequiredExp(lv);
        return Math.Max(0,total);
    }
    static void NormalizeExp(SoulWeaponWz2State s)
    {
        int cap=SoulWeaponWz2Catalog.MaxLevel(s.grade);
        while(s.level<cap)
        {
            int need=SoulWeaponWz2Catalog.RequiredExp(s.level+1);if(need<=0 || s.exp<need)break;
            s.exp-=need;s.level++;
        }
    }
    static void ConvertLegacy(DbRow equipment,SoulWeaponWz2State s,DateTimeOffset now)
    {
        s.converted=true;
        var info=SoulWeaponWz2Catalog.Map(SoulWeaponWz2Catalog.Rules,"TransmitInfo");
        string date=now.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMMddHHmmss",CultureInfo.InvariantCulture);
        if(string.CompareOrdinal(date,SoulWeaponWz2Catalog.Text(info,"StartTime"))<0 || string.CompareOrdinal(date,SoulWeaponWz2Catalog.Text(info,"EndTime"))>=0)return;
        int soul=DbStore.Int(equipment,"soulItemId");
        int grade=SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Map(info,"GradeConversion"),soul.ToString());
        if(grade<=0)return;
        s.grade=Math.Max(s.grade,grade);s.level=Math.Max(s.level,(s.grade-1)*10+1);
        // Old numeric option codes are not Wz2 IDs. Preserve matching stat/value only.
        var old=SoulCatalog.Of(DbStore.Int(equipment,"soulOption"));
        if(old==null)return;
        string key=old.key=="PAD"?"incPAD":old.key=="MAD"?"incMAD":old.key=="MHP"?"incMHP":old.key=="MMP"?"incMMP":"inc"+old.key;
        foreach(var pair in SoulWeaponWz2Catalog.Map(SoulWeaponWz2Catalog.Data,"options"))
            if(int.TryParse(pair.Key,out int id)&&id/10000==s.grade && SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.OptionStats(id),key)==old.value){s.option=id;break;}
    }
}
