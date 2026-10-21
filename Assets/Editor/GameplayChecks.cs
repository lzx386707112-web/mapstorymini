using System;
using UnityEditor;
using UnityEngine;

// 不读写真实档案的规则检查。Unity -batchmode -executeMethod GameplayChecks.Run
public static class GameplayChecks
{
    static int assertions;
    static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception("[GameplayChecks] " + label);
    }

    public static void Run()
    {
        try
        {
            var p = new PlayerProfileData();
            Check(!p.TrySpend(-1) && p.gold == 0, "negative spend");
            Check(!p.TryBuy("w_bow", 150) && !p.owned.Contains("w_bow"), "insufficient funds atomicity");
            Check(!p.TryEquip("w_bow") && p.equipWeapon == "1302000", "equip does not grant");
            p.AddGold(200);
            Check(p.TryBuy("w_bow", 150) && p.gold == 50, "catalog purchase");
            Check(p.equipWeapon == "1302000", "purchase does not equip");
            p.AddGold(200);
            Check(p.TryBuy("w_bow", 150) && p.gold == 100 && p.owned.Count == 3, "original shop permits duplicate equipment");
            p.equipmentSlots = 2;
            Check(!p.TryBuy("w_bow", 1) && p.gold == 100, "full equipment inventory does not charge");
            Check(p.TryEquip("w_bow"), "owned equip");
            p.AddGold(int.MaxValue);
            Check(p.gold == int.MaxValue, "meso overflow clamps");
            p.AddGold(int.MinValue);
            Check(p.gold == 0, "meso underflow clamps");
            Check(!WeaponDefs.TryGet("missing", out _), "unknown catalog entry rejected");

            var a = new SkillRuntimeState(); var b = new SkillRuntimeState();
            Check(a.TryStart(100, 10), "cooldown start");
            Check(!a.TryStart(109, 10) && a.Remaining(109) == 1, "cooldown cannot refresh early");
            Check(b.Remaining(109) == 0, "characters do not share cooldowns");
            Check(a.TryStart(110, 0) && a.Remaining(110) == 0, "exact expiry / no-cooldown skill");
            Check(!a.TryStart(110, -1), "invalid cooldown");
            Check(CombatRules.Resolve(100).Amount == 100 && !CombatRules.Resolve(100).Critical, "no implicit crit roll");
            Check(CombatRules.Resolve(170, true).Amount == 170 && CombatRules.Resolve(170, true).Critical, "critical flag does not double damage");
            Check(CombatRules.Resolve(double.NaN).Amount == 0 && CombatRules.Resolve(-1).Amount == 0, "invalid damage");
            Check(CombatRules.Resolve(double.PositiveInfinity).Amount == CombatRules.InitialDamageCap, "server initial cap");
            Check(CombatRules.MaxPhysicalDamage(10, 4, 15, 4.0) == 7, "server max physical formula");
            var deaths = new DeathSettlement();
            Check(deaths.TrySettle(123) && !deaths.TrySettle(123), "death settles once");
            deaths.Clear(); Check(deaths.TrySettle(123), "new session reset");
            Check(RewardRules.RollMeso(400000, 16, 24, 399999, 7) == 23, "drop range / chance success boundary");
            Check(RewardRules.RollMeso(400000, 16, 24, 400000, 0) == 0, "drop chance fail boundary");
            Check(RewardRules.RollMeso(400000, 16, 24, 0, 8) == 0, "drop upper bound excluded");
            Check(RewardRules.RollMeso(400000, 0, 0, 0, 0) == 0, "absent meso entry");
            for (int c = 0; c < ClassDefs.Count; c++)
                foreach (var slot in ClassDefs.At(c).skills)
                {
                    var skill = NativeSkillCatalog.Get(slot.art);
                    Check(skill != null && skill.Level == 1 && skill.HitCount > 0 && skill.TargetCount > 0, slot.art + " native level 1");
                    for (int lv = 1; lv <= 30; lv++)
                    {
                        var definition = NativeSkillCatalog.Get(slot.art, lv);
                        if (definition == null) break;
                        Check(definition.Level == lv && definition.MpCost >= 0 && definition.Cooldown >= 0, slot.art + " level " + lv);
                    }
                }
            Check(NativeSkillCatalog.Get("holy").Magic && NativeSkillCatalog.Get("holy").Kind == "Attack", "holy is attack, not heal");
            Check(NativeSkillCatalog.Get("crush").Kind == "Dispel" && NativeSkillCatalog.Get("crush").Cooldown == 465, "native crash cooldown");
            Check(NativeSkillCatalog.Get("rage").AttackBuff > 0 && NativeSkillCatalog.Get("rage").DamagePercent == 0, "rage adds weapon attack");
            Check(NativeSkillCatalog.Get("magnet").Kind == "Pull", "magnet not damage");
            Check(NativeSkillCatalog.Get("libra").Kind == "Summon", "libra not screen nova");
            Check(NativeSkillCatalog.Get("illusion_slash") != null && NativeSkillCatalog.Get("illusion_explosion") != null, "illusion replay children");
            CheckCombatExtension();
            CheckStorage();
            Debug.Log("[GameplayChecks] PASS " + assertions + " assertions");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static void CheckCombatExtension()
    {
        Check(WeaponDefs.Shops.Count == 5, "native town shop count");
        var beginner = WeaponDefs.Shops[0];
        Check(beginner.id == 11000 && beginner.items[0].id == "1302000" && beginner.items[0].price == 50, "SQL price/order");
        Check(WeaponDefs.Canonical("w_sword_basic") == "1302000" && WeaponDefs.Canonical("w_staff") == "1382000", "legacy item mapping");
        var basic = WeaponDefs.Get("1302000");
        Check(NativeCombatData.Int(basic.stats,"incPAD") == 17 && basic.multiplier == 4, "native sword stats/type");
        var original = new CharacterAttributes();
        var equipped = original.WithWeapon(basic,0,out int watk,out int matk);
        Check(watk == 17 && equipped.str == 12 && equipped.maxHp == 50 && equipped.maxMp == 5, "native initial stats and equipment");
        original.WithWeapon(basic,15,out int buffed,out _);
        Check(buffed == 32 && original.str == 12, "rage adds WATK without mutating base");
        Check(CombatRules.MaxMagicDamage(200,500,40) == 1400, "native magic upper bound");
        var defense = new System.Collections.Generic.Dictionary<string,object> { {"level",1},{"PDDamage",0},{"MDDamage",0},{"eva",0} };
        var hit = CombatRules.Attack(equipped,basic,watk,matk,defense,null,1,1,0);
        Check(hit.Amount == 10 && !hit.Critical, "physical max uses stats not prototype multiplier");
        defense["PDDamage"] = 20;
        Check(CombatRules.Attack(equipped,basic,watk,matk,defense,null,1,1,0).Amount < hit.Amount, "monster defense reduces damage");
        Check(CombatRules.Incoming(0,equipped,false,1,.5).Amount == 0 && CombatRules.Incoming(-1,equipped,false,1,.5).Amount == 0, "non-damage does not become minimum damage");
        equipped.weaponDefense = 100;
        Check(CombatRules.Incoming(100,equipped,false,1,.5).Amount == 50, "player defense reduces damage");
        equipped.criticalChance = 1; equipped.criticalBonus = 1;
        defense["PDDamage"] = 0;
        var critical = CombatRules.Attack(equipped,basic,watk,matk,defense,null,1,0,0);
        Check(critical.Critical && critical.Amount == 20, "attacker rolls critical once");
        defense["eva"] = 1000;
        Check(CombatRules.Attack(equipped,basic,watk,matk,defense,null,1,0,.99).Amount == 0, "accuracy can miss");
        defense["elemAttr"] = "F1I2H3";
        Check(CombatRules.ElementModifier(defense,"F") == 0 && CombatRules.ElementModifier(defense,"I") == .5 && CombatRules.ElementModifier(defense,"H") == 1.5, "native elemental categories");
        Check(NativeCombatData.Int(NativeCombatData.Mob("mushmom"),"maxHP") == 20000, "native boss HP");
        Check(WeaponDefs.CanEquip(basic,new CharacterAttributes(),"knight"), "starter sword requirements");
        Check(!WeaponDefs.CanEquip(WeaponDefs.Get("1452000"),new CharacterAttributes(),"knight"), "native equip requirements reject");
        var animations = (System.Collections.Generic.Dictionary<string,object>)NativeCombatData.Root["summon"];
        foreach (var action in new[]{"summoned","stand","fly","attack1","die"})
            foreach (System.Collections.Generic.Dictionary<string,object> frame in (System.Collections.Generic.List<object>)animations[action])
            {
                var origin=(System.Collections.Generic.List<object>)frame["origin"];
                var pivot=new Vector2(Convert.ToSingle(origin[0]),Convert.ToSingle(origin[1]));
                var sprite=Art.FrameAt((string)frame["file"],pivot);
                Check(sprite != null && Vector2.Distance(sprite.pivot,new Vector2(pivot.x,sprite.rect.height-pivot.y))<.01f,"summon canvas/origin " + frame["file"]);
            }
        foreach (var shop in WeaponDefs.Shops)
            foreach (var item in shop.items)
                Check(item.price > 0 && item.stats != null && item.multiplier > 0, "valid native shop row " + shop.id + "/" + item.id);
    }

    static void CheckStorage()
    {
        string[] keys = { "mxd.profile.v1", "mxd.profile.v1.backup", "mxd.gold", "mxd.owned", "mxd.equip" };
        var previous = new System.Collections.Generic.Dictionary<string, string>();
        foreach (string key in keys)
            if (PlayerPrefs.HasKey(key)) previous[key] = key == "mxd.gold" ? PlayerPrefs.GetInt(key).ToString() : PlayerPrefs.GetString(key);
        try
        {
            PlayerPrefs.DeleteKey(keys[0]); PlayerPrefs.DeleteKey(keys[1]);
            PlayerPrefs.SetInt("mxd.gold", 4321);
            PlayerPrefs.SetString("mxd.owned", "w_sword_basic,w_bow");
            PlayerPrefs.SetString("mxd.equip", "w_bow");
            var legacy = LocalProfileStore.Load();
            Check(legacy.gold == 4321 && legacy.owned.Contains("w_bow") && legacy.equipWeapon == "w_bow", "legacy migration");
            Check(LocalProfileStore.Save(legacy), "snapshot save");
            legacy.gold = 1234; Check(LocalProfileStore.Save(legacy), "second snapshot / backup");
            Check(LocalProfileStore.Load().gold == 1234, "snapshot round trip");
            PlayerPrefs.SetString(keys[0], "{broken");
            Check(LocalProfileStore.Load().gold == 4321, "corrupt snapshot recovers previous backup");
        }
        finally
        {
            foreach (string key in keys)
            {
                if (!previous.TryGetValue(key, out var value)) PlayerPrefs.DeleteKey(key);
                else if (key == "mxd.gold") PlayerPrefs.SetInt(key, int.Parse(value));
                else PlayerPrefs.SetString(key, value);
            }
            PlayerPrefs.Save();
        }
    }
}
