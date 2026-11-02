using System;
using System.IO;

public static class GmCommandChecks
{
    public static void Run(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mxd-gm-"+Guid.NewGuid().ToString("N")+".db");
        var db=DbStore.Open(path);
        var account=LoginService.Login(db,"admin","gm-test-only");
        var created=CharFactory.CreateCharacter(db,account.accountId,0,"GM测试",1,20000,30300,0,0,1040002,1060002,1072001,1302000,0);
        int id=created.characterId;
        var social=new NativeSocialService(db);int broadcast=0,hostCalls=0;
        social.ChatAccepted=_=>broadcast++;
        social.Commands.Host=(actor,clazz,args,raw)=> {hostCalls++;var result=new NativeCommandResult();result.messages.Add(raw);return result;};
        Func<string,SocialResponse> send=text=>social.Handle(new SocialRequest {characterId=id,operation=SocialOperation.SendChat,text=text});
        var result=send("!ap 123");
        check(result.ok && result.command.changed && CharactersDao.FindById(db,id).ap==123,"GM聊天命令实际写入AP");
        check(broadcast==0,"GM命令不广播为普通聊天");
        check(send("!AP 456").ok && CharactersDao.FindById(db,id).ap==456,"命令名忽略大小写");
        check(!send("!ap invalid").ok && CharactersDao.FindById(db,id).ap==456,"非法参数不改变角色");
        check(send("!givems 500").ok && CharactersDao.FindById(db,id).meso==500,"givems是增量并落库");
        check(send("!givems max").ok && CharactersDao.FindById(db,id).meso==int.MaxValue,"金币上限不溢出");
        check(send("!givems min").ok && CharactersDao.FindById(db,id).meso==0,"金币下限不溢出");
        check(send("!givenx mp 200").ok && DbStore.Int(AccountsDao.FindById(db,account.accountId),"maplePoint")==200,"givenx mp写入抵用券");
        check(send("!givenx np 300").ok && DbStore.Int(AccountsDao.FindById(db,account.accountId),"nxPrepaid")==300,"givenx np写入信用点");
        check(send("!givenx 400").ok && DbStore.Int(AccountsDao.FindById(db,account.accountId),"nxCredit")==400,"givenx默认点券");
        check(send("!heal").ok && CharactersDao.FindById(db,id).hp==CharactersDao.FindById(db,id).maxhp,"heal实际恢复HP");
        check(send("!hurt").ok && CharactersDao.FindById(db,id).hp==1,"hurt将HP设为1");
        var before=CharactersDao.FindById(db,id);
        check(!send("!ap Offline 100").ok && CharactersDao.FindById(db,id).ap==before.ap,"离线角色不冒充在线角色");
        var row=db.Table("command_info").Find(r=>DbStore.Str(r,"syntax")=="ap");row["enabled"]=0L;
        check(!send("!ap 50").ok && CharactersDao.FindById(db,id).ap==456,"后台停用命令立即生效");row["enabled"]=1L;
        result=send("!notice Mixed CASE 中文");
        check(result.ok && hostCalls==1 && result.command.messages[0]=="Mixed CASE 中文","宿主只收到已授权命令且公告保留原文大小写");
        check(send("@全部加力量 5").ok && CharactersDao.FindById(db,id).str==before.str+5,"中文别名按类执行并消耗AP");
        check(!send("!not-a-command").ok && hostCalls==1,"未知命令不调用宿主");
        check(send("!item 2000000 25").ok && InventoryDao.CountOf(db,id,2000000)==25,"item发放原物品并持久化数量");
        check(send("!item 2000000 30").ok && InventoryDao.CountOf(db,id,2000000)==55,"重复发物品正确堆叠");
        check(!send("!item 9999999").ok,"不存在物品不能生成");
        check(send("!item 1302000").ok,"原装备生成");
        var granted=CharService.Load(db,id).inventory.Find(x=>x.itemId==1302000 && x.position>0);
        var clean=Server.Bot.BotItemCatalog.Shared.CleanEquip(1302000);
        check(granted!=null && granted.watk==clean.watk && granted.upgradeslots==clean.upgradeslots && granted.owner=="GM测试","GM装备保留原IMG攻击力卷轴槽及签名");
        check(send("!proitem 1302000 123 45").ok,"自定义属性装备生成");
        granted=CharService.Load(db,id).inventory.Find(x=>x.position>0 && x.itemId==1302000 && x.str==123);
        check(granted!=null && granted.watk==123 && granted.speed==45 && (granted.flag&8)!=0,"proitem全部属性/速度/不可交易标志落库");
        check(send("!seteqstat 77 20").ok && CharService.Load(db,id).inventory.FindAll(x=>x.position>0 && x.isEquip).TrueForAll(x=>x.str==77 && x.jump==20),"seteqstat只更新背包装备");
        check(send("!clearslot 消耗").ok && InventoryDao.CountOf(db,id,2000000)==0 && CharService.Load(db,id).inventory.Exists(x=>x.position<0),"中文清栏保留穿戴装备");
        check(send("!setslot 39").ok && CharactersDao.FindById(db,id).useslots==36,"setslot按原端4格向下取整");
        check(send("!exprate 2.5").ok && GameConfigDao.GetFloat(db,"exp_rate")==2.5,"倍率命令写入实际倍率读取源");
        check(!send("!exprate NaN").ok && GameConfigDao.GetFloat(db,"exp_rate")==2.5,"非法浮点倍率不污染配置");
        var user=LoginService.Login(db,"normal","gm-test-only");
        var normal=CharFactory.CreateCharacter(db,user.accountId,0,"普通测试",1,20000,30300,0,0,1040002,1060002,1072001,1302000,0);
        id=normal.characterId;
        check(!send("@ap 999").ok && CharactersDao.FindById(db,id).ap==0,"普通角色不能越权执行AP");
        check(send("!ap 999").command==null && broadcast==1,"普通角色的感叹号沿用原端普通聊天语义");
        check(send("@帮助").ok,"普通角色可用中文帮助");
        check(!send("@notice text").ok && hostCalls==1,"宿主世界动作不能绕过权限");
        row["level"]=0L;
        check(send("@ap 7").ok && CharactersDao.FindById(db,id).ap==7,"后台调整命令等级立即生效");
        db.Save();var reopened=DbStore.Open(path);
        check(CharactersDao.FindById(reopened,id).ap==7,"GM修改持久化后重新读库仍生效");
        File.Delete(path);
    }
}
