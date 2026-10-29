using System.Collections.Generic;

// RangedAttackHandler 1394..1502; selection and consumption are separate gates.
public static class NativeProjectiles
{
    public struct Cost { public int Item,Position,Count; public bool Allowed; }
    public static bool NightWalkerReplay(int id) => id>=14121003 && id<=14121008 || id>=14121014 && id<=14121018 ||
        id>=14121027 && id<=14121028 || id>=14121030 && id<=14121036;
    public static bool WindArcherReplay(int id) => id>=13121003 && id<=13121005 || id>=13121009 && id<=13121018 ||
        id==13121019 || id==13121020 || id==13121023;
    public static bool MarksmanReplay(int id) => id==3221009 || id==3221013 || id==3221030 || id==3221031 || id==3221032 || id==3221034;
    public static bool IsRangedSkill(int id)
    {
        int job=id/10000;
        return id==4001344 || id==14001004 || id==5001003 ||
            ((job>=300 && job<=322 || job>=410 && job<=412 || job>=520 && job<=522 ||
              job>=1300 && job<=1312 || job>=1410 && job<=1412) && id!=3101003 && id!=3201003);
    }
    public static bool Matches(int item,WeaponKind weapon,int weaponId,int level)
    {
        bool bow=item/1000==2060,crossbow=item/1000==2061;
        if (weaponId==1472063) return bow || crossbow;
        if (weapon==WeaponKind.claw) return item/10000==207 &&
            !((item==2070007 || item==2070018) && level<70) && !(item==2070016 && level<50);
        if (weapon==WeaponKind.gun) return item/10000==233 &&
            (item==2331000 || item==2332000 ? level>=70 : level>(item%10)*20+9);
        return weapon==WeaponKind.bow && bow || weapon==WeaponKind.crossbow && crossbow;
    }
    public static Cost Select(IEnumerable<InventoryEntry> inventory,WeaponKind weapon,int weaponId,int level,
        int skill,int bulletCount,int bulletConsume,bool partner,bool soulArrow,bool shadowClaw,int furySlotMax)
    {
        // RangedAttackHandler:1375-1378 routes these before projectile selection.
        if (skill==4121003 || skill==4221003 || skill==5121002 || skill==15111006)
            return new Cost { Allowed=true };
        bool nw=NightWalkerReplay(skill),wa=WindArcherReplay(skill),mm=MarksmanReplay(skill);
        int required=skill==4111004 ? 0 : (short)bulletCount;
        if (partner && !nw && !wa) required=(short)(required*2);
        var ordered=new List<InventoryEntry>(inventory);ordered.Sort((a,b)=>a.position.CompareTo(b.position));
        int supplement=0,quantity=0;var cost=new Cost();
        foreach (var item in ordered)
        {
            if (item.inventoryType!=2 || item.position<=0) continue;
            if (item.itemId==2070018 && item.quantity-required<=10) supplement=(short)-furySlotMax;
            if (item.quantity>=required && Matches(item.itemId,weapon,weaponId,level))
            { cost.Item=item.itemId;cost.Position=item.position;quantity=item.quantity;break; }
        }
        cost.Allowed=cost.Item!=0 || soulArrow || mm || skill==11101004 || skill==15111007 ||
            skill==14101006 || skill==4111004 || skill==13101005;
        if (cost.Item!=0 && !soulArrow && !shadowClaw && skill!=14121003 && skill!=11101004 && skill!=15111007 && skill!=14101006)
        {
            cost.Count=nw || wa || mm ? 1 : required;
            if (bulletConsume!=0) cost.Count=unchecked((sbyte)(bulletConsume*(partner ? 2 : 1)));
            if (supplement<0) cost.Count=supplement;
            // Inventory.removeItem casts to short, then clamps a negative result to zero.
            cost.Count=quantity-System.Math.Max(0,(int)unchecked((short)(quantity-cost.Count)));
        }
        return cost;
    }
    public static Cost For(PlayerCtrl player,int skill,int level)
    {
        var row=SkillBook.Get(skill)?.Level(level);
        var buffs=player.GetComponent<NativeBuffs>();
        var inventory=new List<InventoryEntry>();
        if (GameSession.InGame) inventory.AddRange(GameSession.Character.inventory);
        else { int slot=0; foreach (int id in Profile.InventoryItems(1))
            inventory.Add(new InventoryEntry { itemId=id,inventoryType=2,position=++slot,quantity=Profile.ItemCount(id) }); }
        int.TryParse(WeaponDefs.Canonical(Profile.equipWeapon),out int weaponId);
        int mastery=SkillProgress.LevelOf(Profile.JobId>=1400 && Profile.JobId<=1412 ? 14100000 : 4100000);
        int maximum=WzAssets.Node("Item/Consume/0207.img/02070018/info")?.Int("slotMax",100) ?? 100;
        int configured=GameServer.Local==null ? 0 : GameConfigDao.GetInt(GameServer.Local.Db,"item_slot_max",0);
        if (maximum!=1 && configured>0) maximum=configured;
        maximum=unchecked((short)(maximum+mastery*10));
        return Select(inventory,WeaponDefs.Get(Profile.equipWeapon).kind,weaponId,Profile.level,skill,
            Art.Int(row,"bulletCount",1),Art.Int(row,"bulletConsume"),buffs?.HasStat("SHADOWPARTNER")==true,
            buffs?.HasStat("SOULARROW")==true,buffs?.HasStat("SHADOW_CLAW")==true,maximum);
    }
    public static bool Spend(Cost cost) => cost.Allowed && (cost.Count==0 || Profile.ConsumeProjectile(cost.Item,cost.Count,cost.Position));
}
