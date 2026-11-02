using System.Collections.Generic;

// Player-method adapter for the original channel-wide hunt publisher.
public static class NativeHuntRuntime
{
    static NativeHuntTasks service;
    public static void Tick()
    {
        var next=GameServer.Local?.Scripts.Hunts;
        if(next==null) {service=null;return;}
        if(!ReferenceEquals(next,service))
        {
            service=next;
            service.ItemName=WzNames.Item;service.MobName=WzNames.Mob;
            service.Notice=text=>{if(GameSession.InGame && GameSession.World==0)NativeSystemMessage.Drop(6,text);};
        }
        service.Tick();
    }
    public static void MonsterKilled(int mob)
    {
        if(!GameSession.InGame)return;
        Tick();
        string player=GameSession.Character.record.name;
        service.MonsterKilled(mob,player,()=>
        {
            var success=new List<string>();var failure=new List<string>();
            Profile.AddGold(service.Gold);success.Add("金币 +"+service.Gold);
            Profile.GrantExperience(service.Exp);success.Add("经验 +"+service.Exp);
            var cash=GameSession.Net.Cash(new CashRequest {characterId=GameSession.CharacterId,operation=CashOperation.AdjustBalance,currency=1,amount=service.Cash});
            if(cash.ok)success.Add("点券 +"+service.Cash);else failure.Add("点券 +"+service.Cash+"(发放失败)");
            foreach(var item in service.Rewards)
            {
                if(!service.RewardWon(item))continue;
                // Source explicitly checks USE space even for an ETC reward.
                var slots=new HashSet<int>();
                foreach(var held in GameSession.Character.inventory)if(held.inventoryType==2 && held.position>0)slots.Add(held.position);
                if(Profile.InventoryCapacity(1)-slots.Count>=1)
                {
                    Profile.AddItem(item.itemId,unchecked((short)item.count));
                    success.Add((string.IsNullOrEmpty(item.name)?WzNames.Item(item.itemId) ?? "物品":item.name)+" +"+item.count);
                }
                else failure.Add((string.IsNullOrEmpty(item.name)?"物品":item.name)+" +"+item.count+"(背包已满)");
            }
            service.CompletionNotice(player,success);
            AudioManager.Play("MobCarnival/Win",1f,false);
            NativeSystemMessage.Drop(6,"恭喜你完成了狩猎任务！\r\n\r\n获得奖励：\r\n"+string.Join("\r\n",success)+"\r\n"+string.Join("\r\n",failure));
        });
    }
}
