using System;
using UnityEngine;

// Original UseCatchItemHandler; target rectangle comes from the item's IMG info.
public static class NativeCatchItems
{
    static double lastAttempt=-100;
    static int owner;
    public static string Use(int id,int position)
    {
        var player=GameLoop.I?.player;
        var info=NativeItemSource.Node(id)?.Get("info");
        if(player==null || info==null) return "当前无法使用捕捉道具";
        int wanted=info.Int("mob"),reward=info.Int("create"),threshold=info.Int("mobHP"),delay=info.Int("useDelay");
        if(owner!=GameSession.CharacterId) {owner=GameSession.CharacterId;lastAttempt=-100;}
        switch(id)
        {
            case 2270000: wanted=9300101;reward=1902000;threshold=0;break;
            case 2270001: wanted=9500197;reward=4031830;threshold=40;delay=1000;break;
            case 2270002: wanted=9300157;reward=4031868;threshold=40;delay=800;break;
            case 2270003: wanted=9500320;reward=4031887;threshold=40;break;
            case 2270004: wanted=9300175;reward=4001169;threshold=40;break;
            case 2270005: wanted=9300187;reward=2109001;threshold=30;break;
            case 2270006: wanted=9300189;reward=2109002;threshold=30;break;
            case 2270007: wanted=9300191;reward=2109003;threshold=30;break;
            case 2270008: wanted=9500336;reward=2022323;threshold=0;delay=3000;break;
            default: if(delay==0 || threshold==0) return null; break;
        }
        EnemyUnit target=null; float nearest=float.MaxValue;
        foreach(var mob in EnemyUnit.All)
        {
            if(mob.dead || !mob.fieldMode || mob.fieldMobId!=wanted) continue;
            var delta=((Vector2)mob.transform.position-player.FeetPos)*Art.PPU;
            if(delta.x<info.Int("left") || delta.x>info.Int("right") || -delta.y<info.Int("top") || -delta.y>info.Int("bottom")) continue;
            if(delta.sqrMagnitude<nearest) {target=mob;nearest=delta.sqrMagnitude;}
        }
        bool throttled=id==2270001 || id==2270002 || id>=2270008;
        if(target==null || reward<=0 || throttled && NativeBuffs.Now-lastAttempt<=delay/1000.0) return null;
        long hp=target.NativeController?.CurrentHp ?? (long)target.hp,max=target.NativeController?.MaximumHp ?? (long)target.hpMax;
        if(threshold>0 && hp>=(max/(id>=2270009 ? 100 : 10))*(id>=2270009 ? threshold : threshold/10))
        { if(id==2270001 || id>=2270009) lastAttempt=NativeBuffs.Now; return "怪物体力还太多，无法捕捉"; }
        if(id==2270002)
        {
            var capacity=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,checkItems=new[] {reward},checkQuantities=new[] {1}});
            if(!capacity.ok) return "请先在其他栏保留空位";
            lastAttempt=NativeBuffs.Now;
            if(UnityEngine.Random.value>=.5f)
            { NativeImgAnimation.Spawn("Effect/ItemEff.img/"+id+"/fail",null,target.transform.position,target.BodySortingOrder+1);return null; }
        }
        if(id==2270008) lastAttempt=NativeBuffs.Now;
        if(!Profile.RemoveItem(id,1,position)) return "背包里没有这个物品";
        NativeImgAnimation.Spawn("Effect/ItemEff.img/"+id,null,target.transform.position,target.BodySortingOrder+1);
        target.ExpireNative(1); Profile.AddItem(reward,1);
        return null;
    }
}
