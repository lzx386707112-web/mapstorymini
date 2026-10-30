using System;
using System.Collections.Generic;
using UnityEngine;

// Character.equipPendantOfSpirit: session-local hourly stages, reset on removal.
public sealed class NativeEquipmentEffects : MonoBehaviour
{
    int character, pendant;
    double equippedAt, nextExpiryCheck;
    int notified;
    readonly HashSet<int> couponBuffs=new HashSet<int>();
    void RefreshCouponBuffs()
    {
        var result=GameSession.Net.Community(new CommunityRequest {characterId=GameSession.CharacterId,operation=CommunityOperation.Read});
        if(!result.ok) return;
        var buffs=NativeBuffs.For(GetComponent<PlayerCtrl>());
        var next=ShopWindow.Visible ? new HashSet<int>() : new HashSet<int>(result.couponBuffs);
        foreach(int id in couponBuffs) if(!next.Contains(id)) buffs.Cancel(-id);
        foreach(int id in next)
        {
            if(couponBuffs.Contains(id)) continue;
            var spec=NativeItemSource.Node(id)?.Get("spec");
            var stats=new Dictionary<string,int>();
            int exp=spec?.Int("expR") ?? 0,drop=spec?.Int("drpR") ?? 0;
            if(exp>=1 && exp<=4) stats["COUPON_EXP"+exp]=1;
            if(drop>=1 && drop<=3) stats["COUPON_DRP"+drop]=1;
            if(stats.Count>0) buffs.ApplyItem(id,stats,spec.Int("time",int.MaxValue),NativeBuffs.Now);
        }
        couponBuffs.Clear();foreach(int id in next) couponBuffs.Add(id);
    }
    public int PendantStage(double now)
    {
        int current=GameSession.InGame && Profile.EquipOfSlot(-17)==1122017 ? 1122017 : 0;
        if(character!=GameSession.CharacterId || pendant!=current)
        { character=GameSession.CharacterId; pendant=current; equippedAt=now; notified=0; }
        return pendant==0 ? 0 : Math.Min(3,(int)((now-equippedAt)/3600));
    }
    public static int PendantBonus(int gain)
    {
        var player=GameLoop.I?.player;
        if(player==null || gain<=0) return 0;
        var effects=player.GetComponent<NativeEquipmentEffects>();
        return effects==null ? 0 : (int)Math.Min(int.MaxValue,(long)(gain/10)*effects.PendantStage(NativeBuffs.Now));
    }
    public static void EquipmentChanged()
        => GameLoop.I?.player?.GetComponent<NativeEquipmentEffects>()?.PendantStage(NativeBuffs.Now);
    void Update()
    {
        int stage=PendantStage(NativeBuffs.Now);
        if(stage>notified) { notified=stage; BottomBar.Toast("精灵吊坠经验加成已提升至 "+stage*10+"%。"); }
        if(!GameSession.InGame || GameSession.Character==null || NativeBuffs.Now<nextExpiryCheck) return;
        nextExpiryCheck=NativeBuffs.Now+1;
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach(var item in GameSession.Character.inventory)
            if(item.expiration>0 && item.expiration<=now)
            {
                var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=item.itemId});
                if(response.ok) { GameSession.Character.inventory=response.inventory; GameSession.ApplyInventoryToProfile(); Profile.Save(); GetComponent<PlayerCtrl>()?.RefreshBuffAttributes(); }
                break;
            }
        var pets=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=5000000,petTick=true});
        if(pets.ok)
        {
            GameSession.Character.inventory=pets.inventory;GameSession.ApplyInventoryToProfile();
            if(!string.IsNullOrEmpty(pets.message)) NativeSystemMessage.Drop(6,pets.message);
        }
        RefreshCouponBuffs();
    }
}
