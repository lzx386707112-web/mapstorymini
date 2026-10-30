using UnityEngine;
using System.Collections.Generic;

// Source UseItemHandler and dedicated equipment / chair / cash handlers use distinct routes.
public static class NativeInventoryActions
{
    static int carriedItem;
    static bool swallowDrop;
    public static bool Carrying => carriedItem != 0;
    public static void Pick(int id)
    {
        // Native keymap item bindings are for consumables, usable setup and cash items.
        if (id / 1000000 != 2 && id / 10000 != 301 && id / 1000000 != 5) return;
        SkillWindow.CancelSkillDrag();
        carriedItem = id;
    }
    public static void CarryStep()
    {
        if (Event.current != null && Event.current.type == EventType.Repaint) CarryPointerStep();
    }
    public static void CarryPointerStep()
    {
        if (swallowDrop)
        {
            if (Pointer.Released) { Pointer.ConsumeClick(); swallowDrop = false; }
            return;
        }
        if (!Carrying) return;
        if (Pointer.RightPressed || Input.GetKeyDown(KeyCode.Escape) || NpcSession.Blocking)
        { carriedItem = 0; return; }
        if (!Pointer.Pressed) return;
        int id = carriedItem;
        carriedItem = 0;
        bool placed = KeyConfigWindow.Visible && !KeyConfigWindow.Carrying && KeyConfigWindow.DropSkill(Pointer.Pos, -id);
        if (!placed && !KeyConfigWindow.Visible)
            for (int i = 0; i < QuickSlots.Count; i++)
                if (BottomBar.SlotRect(i).Contains(Pointer.Pos)) { QuickBarKeys.BindSkill(i, -id); placed = true; break; }
        if (placed) { swallowDrop = true; Pointer.ConsumeClick(); Pointer.Pressed = false; }
    }
    public static void DrawCarry()
    {
        if (!Carrying) return;
        NativeCursor.Request(NativeCursor.StateHold);
        var icon = NativeItemSource.Icon(carriedItem);
        float size = 32 * BottomBar.S;
        if (icon != null) GUI.DrawTexture(new Rect(Pointer.Pos.x-size/2, Pointer.Pos.y-size/2, size, size), icon, ScaleMode.ScaleToFit);
    }
    public static void DoubleClick(int itemId, int inventoryPosition = 0)
    {
        carriedItem = 0;
        string error = TryUse(itemId, inventoryPosition);
        if (!string.IsNullOrEmpty(error)) BottomBar.Toast(error);
    }

    // StatEffect: item time is milliseconds; Alchemist scales duration by y.
    static bool UseStatPotion(int id,PlayerCtrl player)
    {
        var spec=NativeItemSource.Node(id)?.Get("spec");
        if (spec==null || spec.Int("time")<=0) return false;
        string[] fields={"pad","pdd","mad","mdd","acc","eva","speed","jump","expBuff","barrier"};
        string[] keys={"WATK","WDEF","MATK","MDEF","ACC","AVOID","SPEED","JUMP","EXP_BUFF","AURA"};
        var allowed=new HashSet<string>(fields) { "time","hp","mp","hpR","mpR" };
        foreach (var child in spec.Children) if (!allowed.Contains(child.Name)) return false;
        var stats=new Dictionary<string,int>();
        if (id==2022001 || id==2022186) stats["MAP_PROTECTION"]=1;
        else if (id==2022040) stats["MAP_PROTECTION"]=2;
        for (int i=0;i<fields.Length;i++) { int value=i<8 ? (short)spec.Int(fields[i]) : spec.Int(fields[i]); if (value!=0) stats[keys[i]]=value; }
        if (stats.Count==0) return false;
        int duration=spec.Int("time"),hp=(short)spec.Int("hp"),mp=(short)spec.Int("mp");
        int job=Profile.JobId;
        int alchemist=job/10==41 ? 4110000 : job/10==141 ? 14110003 : 0;
        int level=alchemist==0 ? 0 : SkillProgress.LevelOf(alchemist);
        if (level>0)
        {
            var row=SkillBook.Get(alchemist)?.Level(level);
            duration=(int)(duration*(Art.Int(row,"y")/100.0));
            hp=(int)(hp*(Art.Int(row,"x")/100.0)); mp=(int)(mp*(Art.Int(row,"x")/100.0));
        }
        int hpRate=(int)(player.hpMax*(spec.Int("hpR")/100.0));
        if (player.GetComponent<NativeMobDebuffs>()?.Has(133)==true) { hp/=2; hpRate/=2; }
        if (!Profile.RemoveItem(id,1)) return false;
        player.hp=Mathf.Clamp(player.hp+hp+hpRate,0,player.hpMax);
        player.mp=Mathf.Clamp(player.mp+mp+(int)(player.mpMax*(spec.Int("mpR")/100.0)),0,player.mpMax);
        NativeBuffs.For(player).ApplyItem(id,stats,duration,NativeBuffs.Now);
        GameSession.SaveToDb(); return true;
    }

    // null means handled; unsupported routes retain the item.
    public static string TryUse(int itemId, int inventoryPosition = 0)
    {
        if (!GameSession.InGame || NpcSession.Blocking) return "当前无法使用物品";
        var player = GameLoop.I?.player;
        if (player == null || player.hp <= 0) return "当前无法使用物品";
        if (itemId / 1000000 == 1) return EquipLoadout.Equip(itemId, Profile.Attributes, inventoryPosition);
        if (Profile.ItemCount(itemId) <= 0) return "背包里没有这个物品";
        if (itemId / 1000000 == 2)
        {
            // UseItemHandler.handleCureItems consumes even without a matching disease.
            if (itemId >= 2050001 && itemId <= 2050004)
            {
                if (!Profile.RemoveItem(itemId, 1)) return "背包里没有这个物品";
                var diseases = player.GetComponent<NativeMobDebuffs>();
                if (itemId == 2050001) diseases?.Clear(121);
                else if (itemId == 2050002) { diseases?.Clear(122); diseases?.Clear(126); }
                else if (itemId == 2050003) { diseases?.Clear(120); diseases?.Clear(124); }
                else foreach (int disease in new[] { 120, 121, 122, 124, 125, 126 }) diseases?.Clear(disease);
                GameSession.SaveToDb();
                return null;
            }
            var spec = NativeItemSource.Node(itemId)?.Get("spec");
            if (spec?.Int("isNpc") == 1)
            {
                if (spec.Int("remove") == 1 && !Profile.RemoveItem(itemId, 1)) return "背包里没有这个物品";
                return NpcSession.Talk(spec.Int("npc"), spec.Text("script")) != null ? null : "物品NPC脚本尚未接入";
            }
            if (!string.IsNullOrEmpty(spec?.Text("script")))
                return NpcSession.UseItem(itemId) != null ? null : "物品脚本尚未接入";
            if (NpcSession.UseRestorative(itemId)) return null;
            if (UseStatPotion(itemId,player)) return null;
            return "该消耗品的特殊使用功能尚未接入";
        }
        if (itemId / 10000 == 301) return player.UseChair(itemId) ? null : "请站在地面上使用椅子";
        if (itemId / 1000000 == 5) return "该现金道具的使用功能尚未接入";
        return "这个物品不能直接使用";
    }
}
