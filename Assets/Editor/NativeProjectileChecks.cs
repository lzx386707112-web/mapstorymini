using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class NativeProjectileChecks
{
    static void Require(bool ok,string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        try { Check(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e);EditorApplication.Exit(1); }
    }
    public static void Check()
    {
        var inventory=new List<InventoryEntry> {
            new InventoryEntry {itemId=2070000,inventoryType=2,position=4,quantity=20},
            new InventoryEntry {itemId=2070000,inventoryType=2,position=1,quantity=1},
            new InventoryEntry {itemId=2060000,inventoryType=2,position=2,quantity=2} };
        NativeProjectiles.Cost Cost(int skill,int count,int consume=0,bool partner=false,bool soul=false,bool shadow=false)
            => NativeProjectiles.Select(inventory,WeaponKind.claw,1472000,100,skill,count,consume,partner,soul,shadow,1000);
        var stars=Cost(4001344,2);
        Require(stars.Allowed && stars.Position==4 && stars.Count==2,"Select first sufficiently large single stack, not first item or merged count");
        Require(Cost(4001344,2,0,true).Count==4,"Shadow Partner doubles ordinary bullet count");
        Require(Cost(4001344,2,3,true).Count==6,"Authored bulletConsume overrides count and doubles with partner");
        Require(Cost(4001344,2,0,false,false,true).Count==0,"Shadow Claw suppresses consumption");
        Require(Cost(14121004,5,0,true).Count==1,"Night Walker replay consumes one without partner count doubling");
        inventory.Clear();
        foreach (int id in new[]{4121003,4221003,5121002,15111006})
            Require(Cost(id,10).Allowed && Cost(id,10).Count==0,"Pre-selection ranged branch must not require ammunition "+id);

        Require(!Cost(4001344,2).Allowed && !Cost(4001344,2,0,false,false,true).Allowed,
            "Shadow Claw alone must not bypass projectile selection");
        Require(Cost(4001344,2,0,false,true).Allowed && Cost(4001344,2,0,false,true).Count==0,"Soul Arrow allows no projectile");
        Require(Cost(4111004,1).Allowed && Cost(4111004,1).Count==0,"Shadow Meso requires no projectile");
        Require(Cost(3221009,14).Allowed && !Cost(3221010,14).Allowed,"Only explicit source Marksman IDs bypass missing projectile");
        Require(!NativeProjectiles.Matches(2070007,WeaponKind.claw,1472000,69) && NativeProjectiles.Matches(2070007,WeaponKind.claw,1472000,70),"Hwabi level boundary");
        Require(!NativeProjectiles.Matches(2330001,WeaponKind.gun,1492000,29) && NativeProjectiles.Matches(2330001,WeaponKind.gun,1492000,30),"Bullet strict level boundary");
        Require(!NativeProjectiles.Matches(2331000,WeaponKind.gun,1492000,69) && NativeProjectiles.Matches(2331000,WeaponKind.gun,1492000,70),"Capsule level boundary");
        Require(NativeProjectiles.Matches(2061000,WeaponKind.claw,1472063,1) && !NativeProjectiles.Matches(2070000,WeaponKind.claw,1472063,100),"Magical mitten uses arrows");
        inventory.Add(new InventoryEntry {itemId=2070018,inventoryType=2,position=1,quantity=10});
        var fury=Cost(4001344,2);
        Require(fury.Count==-1000,"Balanced Fury low-stack source refill");
        inventory[0].quantity=1;
        Require(!Cost(4001344,2).Allowed,"Refill does not bypass selection count gate");
        inventory[0].itemId=2070000;
        Require(Cost(0,1,3).Count==1,"Inventory short/clamp semantics when bulletConsume exceeds selected quantity");
        Debug.Log("[NativeProjectileChecks] PASS source slot ordering, single-stack threshold, partner, soul/shadow, exceptional IDs, level gates, mitten, fury and inventory clamp");
    }
}
