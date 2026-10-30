using System;
using System.Collections.Generic;

// Server.updateActiveCoupons + Character.activateCouponsEffects/setActiveCoupons.
public static class NativeCouponRates
{
    static List<object> rules;
    public static void Apply(DbStore db,int cid,CommunityResponse rates,DateTime now)
    {
        if(rules==null)
        {
            var root=(Dictionary<string,object>)MiniJsonDb.Parse(PlatformRes.LoadText("ItemCouponSchedules"));
            rules=(List<object>)root["rules"];
        }
        int mask=1<<((int)now.DayOfWeek+1);
        var active=new Dictionary<int,int>();
        var counts=new Dictionary<int,int>();
        long timestamp=new DateTimeOffset(now).ToUnixTimeMilliseconds();
        foreach(var row in InventoryDao.ItemsByCharacter(db,cid))
        {
            int item=DbStore.Int(row,"itemid");
            if(DbStore.Int(row,"position")<=0 || DbStore.Long(row,"expiration")>0 && DbStore.Long(row,"expiration")<=timestamp || DbStore.Int(row,"quantity")<=0) continue;
            foreach(Dictionary<string,object> rule in rules)
                if(Convert.ToInt32(rule["item"])==item && (Convert.ToInt32(rule["days"])&mask)==mask && Convert.ToInt32(rule["start"])<=now.Hour && Convert.ToInt32(rule["end"])>now.Hour)
                {
                    active[item]=Convert.ToInt32(rule["rate"]);
                    counts[item]=counts.TryGetValue(item,out int count) ? count+1 : 1; // inventory stacks count once, as source.
                    break;
                }
        }
        bool stack=GameConfigDao.GetBool(db,"use_stack_coupon_rates",false);
        int exp=1,drop=1;
        foreach(var coupon in active)
        {
            if(GameConfigDao.GetBool(db,"show_coupon_buff",true)) rates.couponBuffs.Add(coupon.Key);
            if(coupon.Key/1000==5211) exp=stack ? exp*coupon.Value*counts[coupon.Key] : Math.Max(exp,coupon.Value);
            else drop=stack ? drop*coupon.Value*counts[coupon.Key] : Math.Max(drop,coupon.Value);
        }
        rates.expRate*=exp; rates.dropRate*=drop; rates.mesoRate*=drop; rates.familyDropRate*=drop;
    }
}
