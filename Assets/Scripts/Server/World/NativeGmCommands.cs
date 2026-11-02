using System;
using System.Collections.Generic;
using System.Globalization;

// CommandsExecutor: @ for players, @/! for GMs. Resolve enabled/current rank from command_info.
// Scene effects are supplied by the client host; authorization always happens here first.
public sealed class NativeCommandResult
{
    public bool ok = true, changed;
    public string error = "";
    public readonly List<string> messages = new List<string>();
    public static NativeCommandResult Fail(string text) => new NativeCommandResult { ok=false,error=text };
}
public sealed class NativeGmCommands
{
    readonly DbStore db;
    readonly DateTime started = DateTime.UtcNow;
    public Func<CharRecord,string,string[],string,NativeCommandResult> Host;
    public NativeGmCommands(DbStore db) { this.db=db; }
    public static bool IsCommand(CharRecord actor,string text) => !string.IsNullOrEmpty(text) && (text[0]=='@' || actor.gm>0 && text[0]=='!');
    public void EnsureSeed()
    {
        if(db.Table("command_info").rows.Count!=0) return;
        foreach(var line in NativeCommandCatalog.Seed.Split('\n'))
        {
            var p=line.Trim().Split('|'); if(p.Length!=3) continue;
            db.Table("command_info").Insert(new DbRow { ["syntax"]=p[0],["clazz"]=p[1],["level"]=long.Parse(p[2]),["default_level"]=long.Parse(p[2]),["enabled"]=1L });
        }
        db.Save();
    }
    public NativeCommandResult Execute(CharRecord actor,string text)
    {
        EnsureSeed();
        if(actor.map==300000012 && actor.gm==0) return NativeCommandResult.Fail("监狱中不能使用命令");
        if(actor.gm==0 && GameConfigDao.GetBool(db,"deterred_player_command")) return NativeCommandResult.Fail("玩家命令已关闭");
        // 源端只认 @/! 前缀，且命令名与参数直接以空格分隔。手机键盘上玩家常把指令表里的
        // 「@job」敲成「@//job/ 100」「@job/100」，这里把多余的前缀斜杠和斜杠分隔一并吞掉，
        // 免得同一个命令因为多打两个斜杠就变成「命令不存在或已停用：//job/」。
        string body=text.Substring(1).Trim().TrimStart('/');
        int split=body.IndexOfAny(new[]{' ','/'});
        string syntax=(split<0?body:body.Substring(0,split)).Trim().ToLowerInvariant();
        if(syntax.Length==0) return NativeCommandResult.Fail("用法：@<命令> [参数]，用 @help 查看可用命令");
        string raw=split<0?"":body.Substring(split+1).Trim();
        string[] args=raw.ToLowerInvariant().Split(new[]{' ','/'},StringSplitOptions.RemoveEmptyEntries);
        DbRow command=null;
        for(int rank=0;rank<=6 && command==null;rank++)
            command=db.Table("command_info").Find(r=>DbStore.Int(r,"level")==rank && DbStore.Int(r,"enabled")!=0 && DbStore.Str(r,"syntax").ToLowerInvariant()==syntax);
        if(command==null) return NativeCommandResult.Fail("命令不存在或已停用："+syntax);
        int required=DbStore.Int(command,"level");
        if(actor.gm<required) return NativeCommandResult.Fail("没有使用此命令的权限："+syntax+" 需要 GM"+required+"，当前 GM"+actor.gm);
        string clazz=DbStore.Str(command,"clazz");
        try
        {
            var result=Run(actor,clazz,args,raw);
            if(result.ok && result.changed) db.Save();
            return result;
        }
        catch(ArgumentException e) { return NativeCommandResult.Fail(e.Message); }
        catch(OverflowException) { return NativeCommandResult.Fail("参数数值超出范围"); }
        // 场景宿主里任何未预料的异常都不能悄悄把聊天吞掉：以前 UI 只看到「没反应」。
        catch(Exception e) { return NativeCommandResult.Fail("指令执行失败："+e.GetType().Name+" "+e.Message); }
    }
    public static int Number(string value)
    {
        if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)) throw new ArgumentException("请输入有效整数："+value);
        return n;
    }
    static void Need(string[] args,int count,string usage) { if(args.Length<count) throw new ArgumentException("用法："+usage); }
    CharRecord Target(CharRecord self,string value)
    {
        // This server has one connected human. Saved characters must not be treated as online.
        if(string.Equals(self.name,value,StringComparison.OrdinalIgnoreCase) || self.id.ToString(CultureInfo.InvariantCulture)==value) return self;
        throw new ArgumentException("角色不在线："+value);
    }
    NativeCommandResult Run(CharRecord c,string name,string[] a,string raw)
    {
        var r=new NativeCommandResult();
        switch(name)
        {
            case "HelpCommand":
                var descriptions=new Dictionary<string,string>();
                foreach(var line in NativeCommandCatalog.Descriptions.Split('\n')) {var p=line.Trim().Split('|');if(p.Length==2) descriptions[p[0]]=p[1];}
                foreach(var row in db.Table("command_info").rows)
                    if(DbStore.Int(row,"enabled")!=0 && DbStore.Int(row,"level")<=c.gm && descriptions.TryGetValue(DbStore.Str(row,"clazz"),out var description))
                        r.messages.Add((c.gm>0?"!":"@")+DbStore.Str(row,"syntax")+" — "+description);
                return r;
            case "TimeCommand": r.messages.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));return r;
            case "UptimeCommand":r.messages.Add("服务器已运行："+(DateTime.UtcNow-started).ToString(@"dd\.hh\:mm\:ss"));return r;
            case "ReadPointsCommand":
                var account=AccountsDao.FindById(db,c.accountid);
                foreach(var key in new[] {"nxCredit","maplePoint","nxPrepaid","rewardpoints","votepoints"}) r.messages.Add(key+"："+DbStore.Long(account,key));
                return r;
            case "OnlineCommand": case "OnlineTwoCommand": case "MapPlayersCommand": case "InMapCommand":
                r.messages.Add(c.name+" 等级 "+c.level+" 职业 "+c.job+" 地图 "+c.map);return r;
            case "GetAccCommand":Need(a,1,"!getacc <角色名>");Target(c,a[0]);r.messages.Add(DbStore.Str(AccountsDao.FindById(db,c.accountid),"name"));return r;
            case "WhereaMiCommand":r.messages.Add("地图 ID："+c.map);return r;
            case "ApCommand":case "SpCommand":case "FameCommand":case "HpMpCommand":case "MaxHpMpCommand":
                Need(a,1,"!"+name.Replace("Command","").ToLowerInvariant()+" [角色名] <数值>");
                var t=a.Length>1?Target(c,a[0]):c; int value=Number(a[a.Length>1?1:0]);
                if(name=="ApCommand") t.ap=Clamp(value,0,MaxAp());
                else if(name=="SpCommand") t.SetSp(JobBook.Of(t.job),Clamp(value,0,MaxAp()));
                else if(name=="FameCommand") t.fame=Clamp(value,short.MinValue,short.MaxValue);
                else if(name=="HpMpCommand") {t.hp=Clamp(value,0,t.maxhp);t.mp=Clamp(value,0,t.maxmp);}
                else {t.maxhp=t.maxmp=Clamp(value,1,30000);t.hp=Math.Min(t.hp,t.maxhp);t.mp=Math.Min(t.mp,t.maxmp);}
                Save(t,r);break;
            case "SetStatCommand":case "MaxStatCommand":
                if(name=="MaxStatCommand") {c.level=255;c.exp=0;c.fame=13337;c.maxhp=c.maxmp=30000;value=short.MaxValue;}
                else {Need(a,1,"!setstat <数值>");value=Clamp(Number(a[0]),4,short.MaxValue);}
                c.str=c.dex=c.int_=c.luk=value;Save(c,r);break;
            case "StatStrCommand":case "StatDexCommand":case "StatIntCommand":case "StatLukCommand":
                int old=name=="StatStrCommand"?c.str:name=="StatDexCommand"?c.dex:name=="StatIntCommand"?c.int_:c.luk;
                value=a.Length>0?Math.Max(0,Math.Min(c.ap,Number(a[0]))):Math.Max(0,Math.Min(c.ap,MaxAp()-old));
                if((long)old+value>MaxAp()) return NativeCommandResult.Fail("属性不能超过 "+MaxAp());
                if(name=="StatStrCommand")c.str+=value;else if(name=="StatDexCommand")c.dex+=value;else if(name=="StatIntCommand")c.int_+=value;else c.luk+=value;
                c.ap-=value;Save(c,r);break;
            case "GiveMesosCommand":
                Need(a,1,"!givems [角色名] <数量|max|min>");t=a.Length>1?Target(c,a[0]):c;var amount=a[a.Length>1?1:0];
                long delta=amount=="max"?int.MaxValue:amount=="min"?int.MinValue:long.TryParse(amount,out long parsed)?parsed:throw new ArgumentException("无效金币数量");
                t.meso=(int)Math.Max(0,Math.Min(int.MaxValue,(long)t.meso+Math.Max(int.MinValue,Math.Min(int.MaxValue,delta))));Save(t,r);break;
            case "GiveNxCommand": case "GiveVpCommand":case "GiveRpCommand":
                Need(a,1,"!givenx [nx|mp|np] [角色名] <数量> / !givevp 或 !giverp [角色名] <数量>");
                string currency=name=="GiveVpCommand"?"votepoints":name=="GiveRpCommand"?"rewardpoints":"nxCredit";
                int offset=0;
                if(name=="GiveNxCommand" && a.Length>1)
                {
                    if(a[0]=="nx"||a[0]=="点券")offset=1;
                    else if(a[0]=="mp"||a[0]=="抵用券") {currency="maplePoint";offset=1;}
                    else if(a[0]=="np"||a[0]=="信用点") {currency="nxPrepaid";offset=1;}
                }
                t=a.Length-offset>1?Target(c,a[offset]):c;
                value=Number(a[a.Length-1]);account=AccountsDao.FindById(db,t.accountid);
                long balance=DbStore.Long(account,currency)+value;
                if(balance<0||balance>int.MaxValue) return NativeCommandResult.Fail("点数余额超出范围");
                account[currency]=balance;r.changed=true;break;
            case "HealCommand":case "HealMapCommand":case "HealPersonCommand":case "HurtCommand":case "KillCommand":case "KillMapCommand":
                t=c;if(name=="HealPersonCommand"||name=="KillCommand") {Need(a,1,"!healperson / !kill <角色名>");t=Target(c,a[0]);}
                if(name=="HurtCommand") {if(a.Length>0)t=Target(c,a[0]);t.hp=1;}
                else if(name=="KillCommand"||name=="KillMapCommand")t.hp=0;
                else {t.hp=t.maxhp;t.mp=t.maxmp;}Save(t,r);break;
            case "SetGmLevelCommand":Need(a,2,"!setgmlevel <角色名> <0~6>");t=Target(c,a[0]);value=Number(a[1]);if(value<0||value>6)throw new ArgumentException("GM 等级范围为0~6");t.gm=value;Save(t,r);break;
            case "ClearSavedLocationsCommand":db.Table("savedlocations").Delete(x=>DbStore.Int(x,"characterid")==c.id);r.changed=true;break;
            case "SetSlotCommand":Need(a,1,"!setslot <24~96>");value=Clamp(Number(a[0])/4*4,4,96);c.equipslots=c.useslots=c.setupslots=c.etcslots=value;Save(c,r);break;
            case "ClearSlotCommand":
                Need(a,1,"!clearslot <equip|use|setup|etc|cash|all>");
                string[] types={"","equip","use","setup","etc","cash"};
                string[] chinese={"全部","装备","消耗","设置","其他","现金"};int translated=Array.IndexOf(chinese,a[0]);if(translated>=0)a[0]=translated==0?"all":types[translated];
                int type=Array.IndexOf(types,a[0]);
                if(type<1 && a[0]!="all")throw new ArgumentException("背包栏名称无效");
                var removed=new HashSet<long>();
                db.Table("inventoryitems").Delete(x=> {bool match=DbStore.Int(x,"characterid")==c.id && DbStore.Int(x,"position")>0 && (a[0]=="all" || DbStore.Int(x,"inventorytype")==type);if(match)removed.Add(DbStore.Long(x,"inventoryitemid"));return match;});
                db.Table("inventoryequipment").Delete(x=>removed.Contains(DbStore.Long(x,"inventoryitemid")));r.changed=true;break;
            case "LootCommand":foreach(var item in CharService.Load(db,(int)c.id).inventory)r.messages.Add(item.position+": "+item.itemId+" × "+item.quantity);return r;
            case "SaveAllCommand":db.Save();r.messages.Add("角色已存档");return r;
            case "RechargeCommand":
                var actor=new Server.Bot.BotActor {id=(int)c.id,job=c.job,character=CharService.Load(db,(int)c.id)};
                var rules=new Server.Bot.Trade.TradeItemRules(db,Server.Bot.BotItemCatalog.Shared);
                foreach(var held in InventoryDao.ItemsByCharacter(db,(int)c.id))
                    if(DbStore.Int(held,"inventorytype")==2 && DbStore.Int(held,"position")>0)
                        held["quantity"]=(long)rules.SlotMax(DbStore.Int(held,"itemid"),actor);
                r.changed=true;break;
            case "SetEqStatCommand":
                Need(a,1,"!seteqstat <属性> [速度跳跃]");value=Clamp(Number(a[0]),0,short.MaxValue);int speed=a.Length>1?Clamp(Number(a[1]),0,short.MaxValue):0;
                foreach(var held in InventoryDao.ItemsByCharacter(db,(int)c.id))
                    if(DbStore.Int(held,"inventorytype")==1 && DbStore.Int(held,"position")>0) SetEquipment(held,value,speed);
                r.changed=true;break;
            case "ProItemCommand":
                Need(a,2,"!proitem <装备ID> <属性> [速度跳跃]");int equipId=Number(a[0]);value=Clamp(Number(a[1]),0,short.MaxValue);speed=a.Length>2?Clamp(Number(a[2]),0,short.MaxValue):0;
                if(equipId/1000000!=1 || Server.Bot.BotItemCatalog.Shared.Info(equipId)==null)throw new ArgumentException("装备不存在："+equipId);
                var pro=Server.Bot.BotItemCatalog.Shared.CleanEquip(equipId);pro.owner=c.name;pro.flag|=8;
                pro.str=pro.dex=pro.int_=pro.luk=pro.hp=pro.mp=pro.watk=pro.matk=pro.wdef=pro.mdef=pro.acc=pro.avoid=value;
                pro.speed=pro.jump=speed;
                Grant(c,pro,1);
                r.changed=true;break;
            case "RatesCommand":case "ShowRatesCommand":
                var rates=new NativeCommunityService(db).Rates((int)c.id);
                r.messages.Add("经验倍率："+rates.expRate+"，金币倍率："+rates.mesoRate+"，掉落倍率："+rates.dropRate);
                r.messages.Add("Boss掉落倍率："+GameConfigDao.GetFloat(db,"boss_drop_rate",1)+"，任务倍率："+GameConfigDao.GetFloat(db,"quest_rate",1)+"，传送倍率："+GameConfigDao.GetFloat(db,"travel_rate",1));return r;
            case "ExpRateCommand":case "MesoRateCommand":case "DropRateCommand":case "BossDropRateCommand":case "QuestRateCommand":case "TravelRateCommand":case "FishingRateCommand":
                Need(a,1,"!exprate / !mesorate / !droprate / !bossdroprate / !questrate / !travelrate / !fishrate <倍率>");
                if(!double.TryParse(a[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double rate) || double.IsNaN(rate)||double.IsInfinity(rate))throw new ArgumentException("无效倍率");
                string code=name=="ExpRateCommand"?"exp_rate":name=="MesoRateCommand"?"meso_rate":name=="DropRateCommand"?"drop_rate":name=="BossDropRateCommand"?"boss_drop_rate":name=="QuestRateCommand"?"quest_rate":name=="TravelRateCommand"?"travel_rate":"fishing_rate";
                var config=db.Table("game_config").Find(x=>DbStore.Str(x,"config_code")==code);
                string rateValue=Math.Max(1,rate).ToString(CultureInfo.InvariantCulture);
                if(config==null)db.Table("game_config").Insert(new DbRow {["config_code"]=code,["config_type"]="world",["config_value"]=rateValue});else config["config_value"]=rateValue;
                r.changed=true;break;
            case "ItemCommand":
                Need(a,1,"!item <物品ID> [数量]");int id=Number(a[0]);value=a.Length>1?Number(a[1]):1;
                if(Server.Bot.BotItemCatalog.Shared.Info(id)==null)throw new ArgumentException("物品不存在："+id);
                if(GameConfigDao.GetBool(db,"block_generate_cash_item") && Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(id),"cash")!=0)throw new ArgumentException("禁止生成现金物品");
                bool pet=id/1000==5000;
                if(pet && a.Length<2)throw new ArgumentException("用法：!item <宠物ID> <天数>");
                if(value<1||value>short.MaxValue)throw new ArgumentException("数量或天数范围为1~32767");
                var offered=Server.Bot.BotItemCatalog.Shared.CleanItem(id,pet?1:value);offered.owner=c.name;
                if(c.gm<3)offered.flag|=8|256;
                if(pet) {offered.petId=0;offered.expiration=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+value*86400000L;}
                Grant(c,offered,pet?1:value);
                r.changed=true;break;
            // —————— 纯数据类指令（不碰场景）——————
            case "StaffCommand":
                r.messages.Add(GameConfigDao.Get(db,"staff_list","北斗冒险岛服务端 / MapleDash 安卓移植（逆向依据见 MapleDash/docs 与 beidou-client-reverse）"));
                return r;
            case "ChangeLanguageCommand":
                r.messages.Add("客户端只提供简体中文（源端 zh-CN 资源与脚本），没有其它语言包");return r;
            case "EnableAuthCommand":
                r.messages.Add("本服未启用 PIC/PIN 二次验证，无需重置冷却");return r;
            case "ExpedsCommand":
                r.messages.Add("当前没有进行中的远征");return r;
            case "IpListCommand":
                r.messages.Add("127.0.0.1（本机单进程服务端，没有外部连接）");return r;
            case "GmCommand":
                Need(a,1,"!gm <内容>");
                SetConfig("gm_message_log",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+c.name+"："+raw);
                r.messages.Add("本机只有你一个 GM，消息已记录");return r;
            case "ReportBugCommand":
                Need(a,1,"!reportbug <内容>");
                SetConfig("bug_report_log",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+c.name+"："+raw);
                r.messages.Add("问题已记录，可在后台 game_config/bug_report_log 查看");return r;
            case "EquipLvCommand":
                int equipped=0;
                foreach(var held in CharService.Load(db,(int)c.id).inventory)
                {
                    if(held.inventoryType!=InventoryDao.Equipped||held.position>=0)continue;
                    var info=Server.Bot.BotItemCatalog.Shared.Info(held.itemId);
                    int req=info==null?0:Server.Bot.BotItemCatalog.Int(info,"reqLevel");
                    r.messages.Add((held.position<0?"[已装备] ":"")+Server.Bot.BotItemCatalog.Shared.Name(held.itemId)+" 需求等级 "+req+(c.level<req?"（当前等级不足）":""));
                    equipped++;
                }
                if(equipped==0)r.messages.Add("当前没有已装备的物品");
                return r;
            case "RanksCommand":
                var ranked=new List<DbRow>();foreach(var row in db.Table("characters").rows)ranked.Add(row);
                ranked.Sort((x,y)=>DbStore.Int(y,"level").CompareTo(DbStore.Int(x,"level")));
                int place=0;
                foreach(var row in ranked){if(place>=10)break;place++;r.messages.Add(place+". "+DbStore.Str(row,"name")+" Lv."+DbStore.Int(row,"level")+" 职业 "+DbStore.Int(row,"job"));}
                if(place==0)r.messages.Add("没有角色记录");
                return r;
            case "ToggleExpCommand":
                bool expOn=!GameConfigDao.GetBool(db,"exp_enabled",true);
                SetConfig("exp_enabled",expOn?"1":"0");
                r.messages.Add("经验获取已"+(expOn?"开启":"关闭"));return r;
            case "IgnoreCommand":
                Need(a,1,"!ignore <角色名>");
                var ignoreList=new List<string>(GameConfigDao.Get(db,"gm_ignore_list","").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries));
                if(ignoreList.RemoveAll(x=>string.Equals(x,a[0],StringComparison.OrdinalIgnoreCase))>0)r.messages.Add("已移出自动封禁白名单："+a[0]);
                else {ignoreList.Add(a[0]);r.messages.Add("已加入自动封禁白名单："+a[0]);}
                SetConfig("gm_ignore_list",string.Join(",",ignoreList.ToArray()));return r;
            case "IgnoredCommand":
                string whitelist=GameConfigDao.Get(db,"gm_ignore_list","");
                if(string.IsNullOrWhiteSpace(whitelist))r.messages.Add("自动封禁白名单为空");
                else foreach(string entry in whitelist.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))r.messages.Add(entry);
                return r;
            case "BanCommand":
            {
                Need(a,1,"!ban <角色名> [原因]");
                var target=db.Table("characters").Find(x=>string.Equals(DbStore.Str(x,"name"),a[0],StringComparison.OrdinalIgnoreCase));
                if(target==null)throw new ArgumentException("角色不存在："+a[0]);
                var banAccount=AccountsDao.FindById(db,(int)DbStore.Long(target,"accountid"));
                if(banAccount==null)throw new ArgumentException("账号不存在");
                banAccount["banned"]=1L;
                banAccount["banreason"]=a.Length>1?raw.Substring(raw.IndexOf(' ')+1):"GM封禁";
                r.changed=true;r.messages.Add("已封禁 "+DbStore.Str(target,"name")+"（账号 "+DbStore.Str(banAccount,"name")+"）");return r;
            }
            case "UnBanCommand":
            {
                Need(a,1,"!unban <账号名|角色名>");
                var unbanAccount=db.Table("accounts").Find(x=>string.Equals(DbStore.Str(x,"name"),a[0],StringComparison.OrdinalIgnoreCase));
                if(unbanAccount==null)
                {
                    var target=db.Table("characters").Find(x=>string.Equals(DbStore.Str(x,"name"),a[0],StringComparison.OrdinalIgnoreCase));
                    if(target!=null)unbanAccount=AccountsDao.FindById(db,(int)DbStore.Long(target,"accountid"));
                }
                if(unbanAccount==null)throw new ArgumentException("账号或角色不存在："+a[0]);
                unbanAccount["banned"]=0L;unbanAccount["banreason"]="";
                r.changed=true;r.messages.Add("已解封 "+DbStore.Str(unbanAccount,"name"));return r;
            }
            case "SetCommand":
                Need(a,2,"!set <变量名> <值>");
                SetConfig("gm_var_"+a[0],raw.Substring(raw.IndexOf(' ')+1));
                r.messages.Add("已写入服务端变量 "+a[0]);return r;
            case "ServerMessageCommand":
                Need(a,1,"!servermessage <内容>");
                var worldRow=db.Table("world_prop").Find(DbStore.Eq("id",c.world));
                if(worldRow==null)SetConfig("server_message",raw);else {worldRow["server_message"]=raw;r.changed=true;}
                r.messages.Add("滚动公告已更新（重新进入选区可见）");return r;
            case "SupplyRateCouponCommand":
                bool coupon=!GameConfigDao.GetBool(db,"rate_coupon_enabled",true);
                SetConfig("rate_coupon_enabled",coupon?"1":"0");
                r.messages.Add("倍率卡使用已"+(coupon?"开启":"关闭"));return r;
            case "ServerAddChannelCommand":
            {
                int world=a.Length>0?Number(a[0]):c.world;
                var row=db.Table("world_prop").Find(DbStore.Eq("id",(long)world));
                if(row==null)throw new ArgumentException("大区不存在："+world);
                row["channel_size"]=DbStore.Long(row,"channel_size",3)+1;
                r.changed=true;r.messages.Add("大区 "+world+" 频道数 "+row["channel_size"]+"（重新进入选区可见）");return r;
            }
            case "ServerRemoveChannelCommand":
            {
                int world=a.Length>0?Number(a[0]):c.world;
                var row=db.Table("world_prop").Find(DbStore.Eq("id",(long)world));
                if(row==null)throw new ArgumentException("大区不存在："+world);
                long size=DbStore.Long(row,"channel_size",3);
                if(size<=1)throw new ArgumentException("至少要保留一个频道");
                row["channel_size"]=size-1;
                r.changed=true;r.messages.Add("大区 "+world+" 频道数 "+row["channel_size"]+"（重新进入选区可见）");return r;
            }
            case "ServerAddWorldCommand":
            {
                int next=0;foreach(var row in db.Table("world_prop").rows)next=Math.Max(next,(int)DbStore.Long(row,"id",0)+1);
                db.Table("world_prop").Insert(new DbRow {["id"]=(long)next,["flag"]=0L,["server_message"]="Welcome to MapleDash!",["event_message"]="MapleDash",
                    ["recommend_message"]="Welcome to MapleDash!",["channel_size"]=3L,["exp_rate"]=1.0,["meso_rate"]=1.0,["drop_rate"]=1.0,
                    ["boss_drop_rate"]=1.0,["quest_rate"]=1.0,["fishing_rate"]=1.0,["travel_rate"]=1.0,["enabled"]=1L});
                r.changed=true;r.messages.Add("已添加大区 "+next+"（重新进入选区可见）");return r;
            }
            case "ServerRemoveWorldCommand":
            {
                int last=-1;foreach(var row in db.Table("world_prop").rows)last=Math.Max(last,(int)DbStore.Long(row,"id",0));
                if(last<=0)throw new ArgumentException("至少保留一个大区");
                db.Table("world_prop").Delete(x=>DbStore.Long(x,"id",0)==last);
                r.changed=true;r.messages.Add("已移除大区 "+last+"（重新进入选区可见）");return r;
            }
            case "WarpWorldCommand":
                Need(a,1,"!warpworld <大区ID>");
                value=Number(a[0]);
                if(db.Table("world_prop").Find(DbStore.Eq("id",(long)value))==null && value!=c.world)
                    throw new ArgumentException("大区不存在："+value);
                c.world=value;Save(c,r);r.messages.Add("已切到大区 "+value+"（重新登录生效）");return r;
            case "SummonCommand":case "ReachCommand":
                Need(a,1,"!"+name.Replace("Command","").ToLowerInvariant()+" <角色名>");
                throw new ArgumentException("本服只有你一个在线真人，没有可传送的对象："+a[0]);
            default:return Host?.Invoke(c,name,a,raw) ?? NativeCommandResult.Fail("此命令所需运行模块尚未接入："+name);
        }
        r.messages.Add("命令执行完成");return r;
    }
    void Grant(CharRecord c,InventoryEntry offered,int quantity)
    {
        var catalog=Server.Bot.BotItemCatalog.Shared;
        var full=CharService.Load(db,(int)c.id);
        var actor=new Server.Bot.BotActor {id=(int)c.id,job=c.job,character=full};
        var inventory=new List<InventoryEntry>();foreach(var item in full.inventory)inventory.Add(item.Copy());
        var trades=new Server.Bot.Trade.BotTradeService(db,catalog);
        int times=offered.isEquip?quantity:1;
        for(int i=0;i<times;i++)
            if(!trades.Rules.Receive(actor,inventory,offered))throw new ArgumentException("背包空间不足");
        if(offered.itemId/1000==5000)
        {
            int petId=(int)db.Table("pets").Insert(new DbRow {["name"]=catalog.Name(offered.itemId),["level"]=1L,["closeness"]=0L,["fullness"]=100L,["summoned"]=0L,["flag"]=0L});
            foreach(var item in inventory)if(item.itemRowId==0 && item.itemId==offered.itemId)item.petId=petId;
        }
        full.inventory=inventory;trades.Persist(actor);
    }
    void SetEquipment(DbRow held,int stat,int speed)
    {
        var eq=db.Table("inventoryequipment").Find(x=>DbStore.Long(x,"inventoryitemid")==DbStore.Long(held,"inventoryitemid"));
        if(eq==null)throw new ArgumentException("缺少装备属性记录");
        foreach(string key in new[]{"str","dex","int","luk","hp","mp","watk","matk","wdef","mdef","acc","avoid"})eq[key]=(long)stat;
        eq["speed"]=(long)speed;eq["jump"]=(long)speed;held["flag"]=(long)(DbStore.Int(held,"flag")|8);
    }
    int MaxAp() {int max=GameConfigDao.GetInt(db,"max_ap");return max>0?max:short.MaxValue;}
    // game_config 没有 setter：GM 指令要写开关/变量时就地 upsert 一行。
    void SetConfig(string code,string value,string type="world")
    {
        var config=db.Table("game_config").Find(x=>DbStore.Str(x,"config_code")==code);
        if(config==null)db.Table("game_config").Insert(new DbRow {["config_code"]=code,["config_type"]=type,["config_value"]=value});
        else config["config_value"]=value;
        db.Save();
    }
    static int Clamp(int n,int min,int max)=>Math.Max(min,Math.Min(max,n));
    void Save(CharRecord c,NativeCommandResult r) {CharactersDao.Save(db,c);r.changed=true;}
}
