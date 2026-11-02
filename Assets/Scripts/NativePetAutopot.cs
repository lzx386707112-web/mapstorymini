using System;
using UnityEngine;

// KeymapChangeHandler mode1/2 and PetAutopotProcessor; binding is item ID, not a stack.
public static class NativePetAutopot
{
    // Original 95B9A4/95BBE0 is called for each resource loss, before the next field tick.
    public static void OnLoss(PlayerCtrl player,float previousHp,float previousMp)
    {
        if(player.hp>=previousHp && player.mp>=previousMp)return;
        bool summoned=false;
        if(GameSession.Character?.inventory!=null)
            foreach(var item in GameSession.Character.inventory)
                if(item.petData!=null && Convert.ToInt32(item.petData["summoned"])==1) {summoned=true;break;}
        Tick(player,previousHp,previousMp,summoned);
        GameLoop.I?.field?.GetComponent<NativePetField>()?.RecordVitals(player);
    }
    internal static bool BelowAlarm(float current,float maximum,double ratio,bool server)
        => ratio>0 && (server ? current<=maximum*ratio : Math.Floor(current*100/Math.Max(1,maximum))<ratio*100);
    public static void Tick(PlayerCtrl player,float previousHp,float previousMp,bool summoned)
    {
        if(player.hp<=0 || !GameSession.InGame) return;
        var db=GameServer.Local?.Db;
        bool server=db!=null && GameConfigDao.GetBool(db,"use_server_auto_pot",false);
        bool compulsory=!server && db!=null && GameConfigDao.GetBool(db,"use_compulsory_auto_pot",false);
        double hpRatio=compulsory ? GameConfigDao.GetFloat(db,"pet_auto_hp_ratio",.99) : ClientSettings.HpAlarm/100.0;
        double mpRatio=compulsory ? GameConfigDao.GetFloat(db,"pet_auto_mp_ratio",.99) : ClientSettings.MpAlarm/100.0;
        bool multi=server || compulsory;
        if(previousHp>=0 && player.hp<previousHp && BelowAlarm(player.hp,player.hpMax,hpRatio,multi) &&
            (multi || summoned && Profile.EquipOfSlot(-24)>0)) Use(player,KeyMap.PetPotion(true),hpRatio,mpRatio,multi);
        if(previousMp>=0 && player.mp<previousMp && BelowAlarm(player.mp,player.mpMax,mpRatio,multi) &&
            (multi || summoned && Profile.EquipOfSlot(-25)>0)) Use(player,KeyMap.PetPotion(false),hpRatio,mpRatio,multi);
    }
    public static bool Use(PlayerCtrl player,int id,double hpRatio,double mpRatio,bool multi)
    {
        if(id/1000000!=2 || player.hp<=0 || player.hp>=player.hpMax && player.mp>=player.mpMax) return false;
        var spec=NativeItemSource.Node(id)?.Get("spec");
        if(spec==null) return false;
        int hp=(short)spec.Int("hp"),mp=(short)spec.Int("mp");
        if(hp<=0 && spec.Int("hpR")>0) hp=(int)Math.Ceiling(player.hpMax*spec.Int("hpR")/100.0);
        if(mp<=0 && spec.Int("mpR")>0) mp=(int)Math.Ceiling(player.mpMax*spec.Int("mpR")/100.0);
        if(hp<=0 && mp<=0) return false;
        int count=1;
        if(multi)
        {
            count=0;
            if(hp>0) count=Math.Max(count,(int)Math.Ceiling((player.hpMax*hpRatio-player.hp)/hp));
            if(mp>0) count=Math.Max(count,(int)Math.Ceiling((player.mpMax*mpRatio-player.mp)/mp));
        }
        count=Math.Min(count,Profile.ItemCount(id));
        return count>0 && NativeInventoryActions.UseStatPotion(id,player,count);
    }
}
