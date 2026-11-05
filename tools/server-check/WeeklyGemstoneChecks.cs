using System;
using System.IO;
using System.Collections.Generic;

static class WeeklyGemstoneChecks
{
    public static void Run(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"weekly-gemstone-"+Guid.NewGuid().ToString("N")+".json");
        var db=DbStore.Create(path);
        int cid=(int)db.Table("characters").Insert(new DbRow { ["name"]="Weekly",["level"]=200L,["useslots"]=1L });
        var req=new WeeklyGemstoneRequest {characterId=cid};
        var wed=new DateTimeOffset(2026,11,4,12,0,0,TimeSpan.FromHours(8));
        try
        {
            check(!NativeWeeklyGemstoneService.Handle(db,req,wed).eligible,"周任务：前置未完成不可领");
            QuestStatusDao.Upsert(db,cid,WeeklyGemstoneData.Prerequisite-65536,2,1);
            check(NativeWeeklyGemstoneService.Handle(db,req,wed).eligible,"周任务：承认已迁移原任务ID映射");
            req.operation=WeeklyGemstoneOperation.Claim;
            check(!NativeWeeklyGemstoneService.Handle(db,req,wed).ok,"周任务：次数不足拒绝领取");
            req.operation=WeeklyGemstoneOperation.Read;
            NativeWeeklyGemstoneService.OnCompleted(db,cid,-31407,wed);
            check(NativeWeeklyGemstoneService.Handle(db,req,wed).clear==0,"旧调查引导不算一次每日调查");
            void Tasks(DateTimeOffset t)
            {
                for(int i=0;i<3;i++) NativeWeeklyGemstoneService.OnCompleted(db,cid,WeeklyGemstoneData.DailyIds[i]-65536,t);
            }
            Tasks(wed); Tasks(wed);
            check(NativeWeeklyGemstoneService.Handle(db,req,wed).clear==1,"同日完成三项调查计一次，重复回调不加次数");
            for(int i=3;i<6;i++) NativeWeeklyGemstoneService.OnCompleted(db,cid,WeeklyGemstoneData.DailyIds[i],wed);
            check(NativeWeeklyGemstoneService.Handle(db,req,wed).clear==1,"同日额外三项也不重复计次");
            var thu=wed.AddDays(1);
            check(NativeWeeklyGemstoneService.Week(thu.AddHours(-12))!=NativeWeeklyGemstoneService.Week(thu.AddHours(-12).AddTicks(-1)),"周三结束/周四00:00准确跨周");
            check(NativeWeeklyGemstoneService.Handle(db,req,thu).clear==0,"跨周清除未领取进度");
            Tasks(thu);
            var fri=thu.AddDays(1);
            NativeWeeklyGemstoneService.Handle(db,req,fri); Tasks(fri);
            check(NativeWeeklyGemstoneService.Handle(db,req,fri).clear==2,"不同两天完成调查达到周领奖条件");
            long filler=InventoryDao.InsertItem(db,cid,2000000,2,1,1);
            req.operation=WeeklyGemstoneOperation.Claim;
            var full=NativeWeeklyGemstoneService.Handle(db,req,fri);
            check(!full.ok && !full.claimed && InventoryDao.CountOf(db,cid,2435902)==0,"满包原子回滚，不吞领奖机会");
            CharService.RemoveItem(db,filler);
            var claim=NativeWeeklyGemstoneService.Handle(db,req,fri);
            check(claim.ok && claim.claimed && InventoryDao.CountOf(db,cid,2435902)==3,"奖励按IMG发放3颗2435902");
            check(!NativeWeeklyGemstoneService.Handle(db,req,fri).ok && InventoryDao.CountOf(db,cid,2435902)==3,"重复领奖不会重复发放");
            db.Save(); db=DbStore.Open(path);
            check(!NativeWeeklyGemstoneService.Handle(db,req,fri).ok,"重开存档仍拒绝重复领奖");
            req.operation=WeeklyGemstoneOperation.Read;
            check(!NativeWeeklyGemstoneService.Handle(db,req,thu.AddDays(7)).claimed,"新周恢复可参与状态");

            int hookCid=(int)db.Table("characters").Insert(new DbRow { ["name"]="Hook",["level"]=200L });
            QuestStatusDao.Upsert(db,hookCid,WeeklyGemstoneData.Prerequisite,2,1);
            var hookReq=new WeeklyGemstoneRequest {characterId=hookCid};
            NativeWeeklyGemstoneService.Handle(db,hookReq);
            for(int i=0;i<3;i++)
            {
                int id=WeeklyGemstoneData.DailyIds[i]-65536;
                QuestStatusDao.Upsert(db,hookCid,id,1);
                QuestStatusDao.Upsert(db,hookCid,id,2,1);
                QuestStatusDao.Upsert(db,hookCid,id,2,1);
            }
            check(NativeWeeklyGemstoneService.Handle(db,hookReq).clear==1,"生产QuestStatusDao完成链计数，重复保存不增加次数");
            var reset=NativeWeeklyGemstoneService.Handle(db,hookReq,DateTimeOffset.UtcNow.AddDays(1));
            check(reset.resetQuests.Count==3 && QuestStatusDao.StatusOf(db,hookCid,WeeklyGemstoneData.DailyIds[0]-65536)==0,"跨日恢复旧版调查的可接状态并下发镜像重置列表");
            QuestStatusDao.Upsert(db,hookCid,WeeklyGemstoneData.Id,2,1);
            check(QuestStatusDao.StatusOf(db,hookCid,WeeklyGemstoneData.Id)==0,"客户端普通存档不能伪造周任务已领奖状态");

            var fullChar=new FullCharacter { inventory=new List<InventoryEntry> {
                new InventoryEntry {itemId=1714000,position=-221,quantity=1,symbolLevel=1,symbolInc=10},
                new InventoryEntry {itemId=1714001,position=-222,quantity=1,symbolLevel=11,symbolInc=110},
                new InventoryEntry {itemId=1714000,position=1,quantity=1,symbolLevel=11},
                new InventoryEntry {itemId=1714001,position=-11,quantity=1,symbolLevel=11} } };
            var delta=NativeGrowthStats.Accumulate(fullChar,285,112);
            check(delta.Get("ExpRate")==60 && delta.Get("MesoRate")==20 && delta.Get("DropRate")==20,"豪华AUT：1级+11级叠加60/20/20，背包及错误槽不生效");
            fullChar.inventory[0].symbolLevel=2;
            delta=NativeGrowthStats.Accumulate(fullChar,285,112);
            check(delta.Get("ExpRate")==64 && delta.Get("MesoRate")==21,"豪华AUT升级立即使收益缓存失效");
            check(SymbolCatalog.GrandBonus(1713000,11,"ExpRate")==0 && SymbolCatalog.GrandBonus(1714000,0,"ExpRate")==0 && SymbolCatalog.GrandBonus(1714000,12,"ExpRate")==0,"普通AUT及无效等级没有豪华收益");
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }
}
