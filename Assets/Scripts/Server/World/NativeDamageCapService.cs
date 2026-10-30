using System;

// DamageCapService + NPCConversationManager.useDamageCapBreakthroughStone.
public static class NativeDamageCapService
{
    public static int Increment(int id)=>id switch {
        2431152=>300000,2431153=>500000,2431154=>2000000,2431155=>10000000,2431156=>100000000,2431157=>500000000,_=>0 };
    public static int Resolve(int current,int increment,bool success)
    {
        current=Math.Max(19999999,current);
        return success && (long)current+increment<=int.MaxValue ? current+increment : current;
    }
    public static InventoryChangeResponse Use(DbStore db,CharRecord character,int id,Random random=null)
    {
        int step=Increment(id);
        if(step==0 || InventoryDao.CountOf(db,(int)character.id,id)<1) return new InventoryChangeResponse {ok=false,error="突破石不存在，无法使用。"};
        random ??= new Random(); bool success=random.Next(2)!=0;
        int before=Math.Max(19999999,character.damageCap),after=Resolve(before,step,success);
        character.damageCap=after; CharactersDao.Save(db,character);
        InventoryDao.ConsumeItem(db,(int)character.id,id,1); db.Save();
        string message=!success ? "突破失败，当前伤害上限仍为 "+after+"。" : before==after ? "突破成功，但增加后会超过 2,147,483,647，当前伤害上限未变化。" : "突破成功，伤害上限提升至 "+after+"。";
        return new InventoryChangeResponse {inventory=CharService.Load(db,(int)character.id).inventory,damageCap=after,message=message};
    }
}
