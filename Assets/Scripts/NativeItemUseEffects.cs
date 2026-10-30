using System;
using System.Collections.Generic;
using UnityEngine;

// UseItemHandler / StatEffect: non-script item effects.
public static class NativeItemUseEffects
{
    public static void CashEquipment(int id,InventoryEntry target)
    {
        var result=GameSession.Net.UpdateEquip(new EquipPatchRequest {characterId=GameSession.CharacterId,inventoryItemId=target.itemRowId,cashItemId=id});
        if(!result.ok) { BottomBar.Toast(result.error); return; }
        GameSession.Character.inventory=result.inventory; GameSession.ApplyInventoryToProfile();
        GameLoop.I?.player?.RefreshBuffAttributes(); Profile.Save();
    }
    public static void Scroll(int id,InventoryEntry target)
    {
        bool excluded=id==2040727 || id==2041058 || id>=2049000 && id<=2049003;
        if(!excluded && Profile.ItemCount(2340000)>0)
        {
            NativeUtilPrompt.Confirm("你想使用祝福卷轴吗？",white=>ApplyScroll(id,target,white));
            return;
        }
        ApplyScroll(id,target,false);
    }
    static void ApplyScroll(int id,InventoryEntry target,bool white)
    {
        var req=new List<int>();
        var node=NativeItemSource.Node(id)?.Get("req");
        if(node!=null) foreach(var item in node.Children) req.Add(Convert.ToInt32(item.Value));
        var result=GameSession.Net.UpdateEquip(new EquipPatchRequest {
            characterId=GameSession.CharacterId,inventoryItemId=target.itemRowId,scrollId=id,scrollRequirements=req.ToArray(),whiteScroll=white });
        if(!result.ok) { BottomBar.Toast(result.error); return; }
        GameSession.Character.inventory=result.inventory; GameSession.ApplyInventoryToProfile();
        GameLoop.I?.player?.RefreshBuffAttributes(); Profile.Save();
        BottomBar.Toast(result.scrollResult=="success" ? "卷轴使用成功。" : result.scrollResult=="curse" ? "卷轴失败，装备已被摧毁。" : "卷轴使用失败。");
    }
    public static string RewardBox(int id)
    {
        var rewards=NativeItemSource.Node(id)?.Get("reward");
        if(rewards==null) return "源端缺少奖励数据";
        int total=0;
        foreach(var row in rewards.Children) total+=(sbyte)row.Int("prob");
        if(total<=0) return null;
        foreach(var row in rewards.Children)
        {
            int target=row.Int("item"),count=(short)row.Int("count");
            var capacity=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,checkItems=new[] {target},checkQuantities=new[] {count}});
            if(!capacity.ok) return "背包空间不足";
            if(UnityEngine.Random.Range(0,total)>=(sbyte)row.Int("prob")) continue;
            int period=row.Int("period",-1);
            var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=id,
                grantItemId=target,grantQuantity=count,grantMinutes=-1,consumeGrantSource=true,
                grantDurationMs=target/1000000==1 && period!=-1 ? period*60L*60*10 : -1});
            if(!result.ok) return result.error;
            GameSession.Character.inventory=result.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();
            string message=row.Text("worldMsg");
            if(!string.IsNullOrEmpty(message)) NativeSystemMessage.Drop(6,message.Replace("/name",Profile.playerName).Replace("/item",WzNames.Item(target)));
            return null;
        }
        return null;
    }
    public static string SummonBag(int id,int position)
    {
        var mobs=NativeItemSource.Node(id)?.Get("mob");
        if(mobs==null) return "源端道具缺少召唤怪物数据";
        if(GameLoop.I?.field?.Map==null) return "当前无法使用物品";
        if(!Profile.RemoveItem(id,1,position)) return "背包里没有这个物品";
        foreach(var mob in mobs.Children)
            if(UnityEngine.Random.Range(0,100)<mob.Int("prob")) GameLoop.I.field.SpawnNativeMob(mob.Int("id"),GameLoop.I.player.FeetPos);
        return null;
    }
    public static string SkillBook(int id,int position)
    {
        var info=NativeItemSource.Node(id)?.Get("info");
        int skill=0;
        var list=info?.Get("skill");
        if(list!=null) foreach(var s in list.Children)
        {
            int candidate=System.Convert.ToInt32(s.Value);
            if(candidate/10000==Profile.JobId) { skill=candidate; break; }
        }
        if(skill==0) return "当前职业无法使用这本技能册";
        int master=GameSession.Character.skills.Find(s=>s.skillId==skill)?.masterLevel ?? 0;
        if(SkillProgress.LevelOf(skill)<info.Int("reqSkillLevel") || master>=info.Int("masterLevel")) return "尚未满足技能等级要求，或技能上限已达到";
        if(!Profile.RemoveItem(id,1,position)) return "背包里没有这个物品";
        int rolls=GameServer.Local==null ? 1 : GameConfigDao.GetInt(GameServer.Local.Db,"scroll_chance_rolls",1);
        bool success=UnityEngine.Random.value>=Math.Pow(1-info.Int("success")/100.0,rolls);
        if(success)
        {
            var entry=GameSession.Character.skills.Find(s=>s.skillId==skill);
            if(entry==null) { entry=new SkillEntry {skillId=skill}; GameSession.Character.skills.Add(entry); }
            entry.masterLevel=Math.Max(master,info.Int("masterLevel"));
            GameSession.SaveToDb();
        }
        BottomBar.Toast(success ? "技能册使用成功。" : "技能册使用失败。");
        return null;
    }
    static int banishCharacter, banishMap=-1, banishPortal=-1;
    static long banishTime;
    public static void RememberBanish()
    {
        var field=GameLoop.I?.field;
        if(field?.Map==null) return;
        banishCharacter=GameSession.CharacterId; banishMap=field.Map.id;
        banishPortal=ClosestSpawn(field.Map,GameLoop.I.player.FeetPos);
        banishTime=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
    static int ClosestSpawn(FieldMap map,Vector3 point)
    {
        int best=-1; float distance=float.PositiveInfinity;
        for(int i=0;i<map.portals.Count;i++)
        {
            var p=map.portals[i]; if(!p.IsSpawn) continue;
            float d=(new Vector2(p.x/Art.PPU,-p.y/Art.PPU)-(Vector2)point).sqrMagnitude;
            if(d<distance) { distance=d; best=i; }
        }
        return best;
    }
    public static string TownScroll(int id,int position)
    {
        var field=GameLoop.I?.field; if(field?.Map==null) return "当前无法使用物品";
        int destination=NativeItemSource.Node(id).Int("spec/moveTo",-1), portal=-1;
        if(destination<0) return "此卷轴没有传送目的地";
        if(destination==field.Map.id) return "当前地图无法使用这个卷轴";
        if(destination==999999999)
        {
            if(id==2030100)
            {
                if(banishCharacter!=GameSession.CharacterId || banishMap<0 || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-banishTime>=300000)
                    return "当前无法返回被驱逐前的地图";
                destination=banishMap; portal=banishPortal;
            }
            else destination=field.Map.returnMap;
        }
        else
        {
            int target=destination/10000000,current=field.Map.id/10000000;
            if(target!=60 && current!=61 && target!=current && target!=21 && target!=20 && target!=12 && current!=10 && current!=12)
                return "当前地图无法使用这个卷轴";
        }
        var map=FieldCatalog.GetMap(destination);
        if(map==null) return "传送目的地不存在";
        if(portal<0)
        {
            var spawn=new List<int>();
            for(int i=0;i<map.portals.Count;i++) if(map.portals[i].IsSpawn) spawn.Add(i);
            if(spawn.Count>0) portal=spawn[UnityEngine.Random.Range(0,spawn.Count)];
        }
        bool remember=id!=2030100 && GameServer.Local!=null && GameConfigDao.GetBool(GameServer.Local.Db,"use_banishable_town_scroll",false);
        if(!Profile.RemoveItem(id,1,position)) return "背包里没有这个物品";
        if(remember) RememberBanish();
        field.WarpTo(destination,portalIndex:portal);
        return null;
    }
}
