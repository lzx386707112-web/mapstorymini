using System.Collections.Generic;
using MapleDash.Wz;

// RangedAttackHandler 1394..1502; selection and consumption are separate gates.
public static class NativeProjectiles
{
    public struct Cost { public int Item,Position,Count; public bool Allowed; }
    // 旧 TMS 五/六转的弹道豁免类。这些 id 已随旧迁移下线，迁移 282 技能时按新 id 重建。
    public static bool NightWalkerReplay(int id) => id>=14121003 && id<=14121008 || id>=14121014 && id<=14121018 ||
        id>=14121027 && id<=14121028 || id>=14121030 && id<=14121036;
    public static bool WindArcherReplay(int id) => id>=13121003 && id<=13121005 || id>=13121009 && id<=13121018 ||
        id==13121019 || id==13121020 || id==13121023;
    public static bool MarksmanReplay(int id) => id==3221009 || id==3221013 || id==3221030 || id==3221031 || id==3221032 || id==3221034;
    public static bool IsRangedSkill(int id)
    {
        int job=id/10000;
        // 282 的五六转段要一并算远程，否则 ProjectileRange 会退回 300px：
        //   314 神射手 / 324 箭神（弓弩）  414 隐士（拳套飞镖）
        //   524 船长（枪）                 1314 风灵使者（弓）  1414 夜行者（飞镖）
        // 424（侠盗）/ 514（冲锋队长）/ 1514（奇袭者）拿短刀·拳套·指虎，是近战，不能进这里。
        return id==4001344 || id==14001004 || id==5001003 ||
            ((job>=300 && job<=324 || job>=410 && job<=414 || job>=520 && job<=524 ||
              job>=1300 && job<=1314 || job>=1410 && job<=1414) && id!=3101003 && id!=3201003);
    }

    // 远程技能「要不要扣弹药」和「射程按远程算」是两件事，必须分开判。
    //
    // 282 的远程技能把 info/bulletCount 全删了（远程段 73 个 attack VI 技能一个都没有），
    // 所以旧口径（IsRangedSkill 就扣弹）会把 3141500 这种 HEXA 精通被动也吃箭。
    // 改成按「有没有真实的发射物层」判定，全库远程段只有 20 个技能带层：
    //   3141004 箭座VI ball+flipBall、3241006/3241007 光速神弩VI shootobj、
    //   5241013 子弹盛宴 shootobj、5241002/5241003 召唤型 ball ……
    // hideBullet（19 个）只是不画箭矢贴图，照样消耗；noBulletConsume 才是豁免。
    public static bool ConsumesAmmo(int id)
    {
        if (!IsRangedSkill(id)) return false;
        var row=SkillBook.Get(id)?.Level(SkillProgress.LevelOf(id));
        if (Art.Int(row,"bulletCount") > 0) return true;   // 083 的技能保留原判定
        return HasProjectileLayer(id);
    }

    static readonly Dictionary<int,bool> projectileLayer=new Dictionary<int,bool>();

    static bool HasProjectileLayer(int id)
    {
        if (projectileLayer.TryGetValue(id,out var known)) return known;
        // 定义查不到时按「要消耗」处理：宁可扣，也不要让远程技能白嫖弹药。
        bool value=true;
        var node=WzAssets.Node("Skill/"+(id/10000).ToString("D3")+".img/skill/"+id.ToString("D7"))?.TryResolve();
        if (node!=null)
        {
            value=node.Get("ball")!=null || node.Get("shootobj")!=null || node.Get("flipBall")!=null;
            // 全库只有 4141001 四飞闪VI 带这个节点，源端明确「不消耗箭矢」。
            if (value && node.Get("noBulletConsume")!=null) value=false;
        }
        projectileLayer[id]=value;
        return value;
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
        if (cost.Item!=0 && !soulArrow && !shadowClaw && skill!=11101004 && skill!=15111007 && skill!=14101006)
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
