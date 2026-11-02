using UnityEngine;
using System.Collections.Generic;

// Source UseItemHandler and dedicated equipment / chair / cash handlers use distinct routes.
public static class NativeInventoryActions
{
    static int carriedItem,carriedPosition;
    static bool swallowDrop,carriedFromInventory;
    public static bool Carrying => carriedItem != 0;
    public static void Pick(int id,int inventoryPosition=0) => Pick(id,inventoryPosition,false);
    public static void PickFromInventory(int id,int inventoryPosition=0) => Pick(id,inventoryPosition,true);
    static void Pick(int id,int inventoryPosition,bool fromInventory)
    {
        // Native keymap item bindings are for consumables, usable setup and cash items.
        // 背包格多一条：源端把「格子里的东西丢到地上」实现成 ITEM_MOVE(action=0)，
        // 任何背包页都能丢，所以从背包拿起时装备/材料也允许携带（它们只是不能绑键位）。
        // 但已经穿在身上的（装备页 position<0）不能拿：InventoryManipulator.drop 只认
        // position>0，源端同样要求先脱下，否则拿起后落到地面会被服务端拒绝、光标上的
        // 道具凭空消失。
        if (id / 1000000 != 2 && id / 10000 != 301 && id / 1000000 != 5 &&
            !(NativeVegaWindow.Visible && id/1000000==1) &&
            !(fromInventory && id > 0 && inventoryPosition >= 0)) return;
        SkillWindow.CancelSkillDrag();
        carriedItem = id;carriedPosition=inventoryPosition;carriedFromInventory=fromInventory;
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
        bool placed=EquipWindow.DropPetPotion(Pointer.Pos,id);
        if(!placed) placed=NativeVegaWindow.Drop(Pointer.Pos,id,carriedPosition);
        if(!placed && (id/10000==204 || IsCashEquipment(id)))
        {
            var target=EquipWindow.EquipmentAt(Pointer.Pos) ?? InvenWindow.EquipmentAt(Pointer.Pos);
            if(target!=null) { placed=true; if(IsCashEquipment(id)) NativeItemUseEffects.CashEquipment(id,target); else NativeItemUseEffects.Scroll(id,target); }
        }
        if(!placed && id/1000000!=1) placed = KeyConfigWindow.Visible && !KeyConfigWindow.Carrying && KeyConfigWindow.DropSkill(Pointer.Pos, -id);
        if (!placed && id/1000000!=1 && !KeyConfigWindow.Visible)
            for (int i = 0; i < QuickSlots.Count; i++)
                if (BottomBar.SlotRect(i).Contains(Pointer.Pos)) { QuickBarKeys.BindSkill(i, -id); placed = true; break; }
        if (!placed && carriedFromInventory && FieldAcceptsDrop(Pointer.Pos)) placed = DropToField(id, carriedPosition);
        if (placed) { swallowDrop = true; Pointer.ConsumeClick(); Pointer.Pressed = false; }
    }

    // 源端「丢到地上」= ITEM_MOVE(0x47) 且 action==0（ItemMoveHandler → InventoryManipulator.drop）。
    // 交互沿用本项目的点击口径：点一次从背包拿起，再点一次落在窗口/底栏/虚拟按键之外的地面就丢。
    static bool FieldAcceptsDrop(Vector2 pointer)
        => !NativeUi.WindowAtPointer && !VirtualControls.BlockingPointer && pointer.y < UiKit.H - 80f * BottomBar.S;

    static bool DropToField(int itemId, int position)
    {
        var loop = GameLoop.I;
        var player = loop == null ? null : loop.player;
        var field = loop == null ? null : loop.field;
        if (player == null || field == null || field.StateId < 0 || GameSession.Character == null) return false;
        var entry = GameSession.Character.inventory.Find(x => x.itemId == itemId && x.position > 0 &&
            (position == 0 || x.position == position));
        if (entry == null) return false;
        // 源端对可堆叠道具先问「扔掉多少？」（字符串池 885），装备/单件直接丢。
        if (entry.quantity > 1 && itemId / 1000000 != 1)
        {
            NativeUtilPrompt.Number("扔掉多少？", 1, 1, entry.quantity, value => DropToField(itemId, position, (int)value));
            return true;
        }
        DropToField(itemId, position, 1);
        return true;
    }

    static void DropToField(int itemId, int position, int quantity)
    {
        var loop = GameLoop.I;
        var player = loop == null ? null : loop.player;
        var field = loop == null ? null : loop.field;
        if (player == null || field == null || field.StateId < 0) return;
        // 源端 dropPos 取 Character.getPosition()（脚下），落点吸附在服务端 CalcItemDropPos 里做。
        var response = GameSession.Net.ChangeInventory(new InventoryChangeRequest
        {
            characterId = GameSession.CharacterId, itemId = itemId, sourcePosition = position,
            dropToField = true, dropQuantity = quantity, dropMapId = field.StateId,
            dropX = Mathf.RoundToInt(player.transform.position.x * Art.PPU),
            dropY = Mathf.RoundToInt(-player.transform.position.y * Art.PPU),
        });
        if (!response.ok) { BottomBar.Toast(string.IsNullOrEmpty(response.error) ? "无法丢弃这件物品" : response.error); return; }
        GameSession.Character.inventory = response.inventory;
        GameSession.ApplyInventoryToProfile(); Profile.Save();
    }
    static bool IsCashEquipment(int id)=>id==5060000 || id==5060001 || id>=5061000 && id<=5061003 || id/10000==550 || id/10000==552 || id/10000==557;
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
    public static bool UseStatPotion(int id,PlayerCtrl player,int count=1)
    {
        var spec=NativeItemSource.Node(id)?.Get("spec");
        if (spec==null) return false;
        string[] fields={"pad","pdd","mad","mdd","acc","eva","speed","jump","expBuff","barrier"};
        string[] keys={"WATK","WDEF","MATK","MDEF","ACC","AVOID","SPEED","JUMP","EXP_BUFF","AURA"};
        var allowed=new HashSet<string>(fields) { "time","hp","mp","hpR","mpR","poison","seal","darkness","weakness","curse",
            "exp","inflation","morphRandom","consumeOnPickup","onlyPickup","prob","BFSkill","party","expinc","mhpR","mmpR","mhpRRate","mmpRRate","berserk","booster","repeatEffect","morph","thaw","padRate","madRate","pddRate","mddRate","accRate","evaRate","speedRate" };
        foreach (var child in spec.Children) if (!allowed.Contains(child.Name)) return false;
        var stats=new Dictionary<string,int>();
        if (id==2022001 || id==2022186) stats["MAP_PROTECTION"]=1;
        else if (id==2022040) stats["MAP_PROTECTION"]=2;
        for (int i=0;i<fields.Length;i++) { int value=i<8 ? (short)spec.Int(fields[i]) : spec.Int(fields[i]); if (value!=0) stats[keys[i]]=value; }
        if(id>=2022450 && id<=2022452 && spec.Int("expinc")!=0) stats["EXP_INCREASE"]=spec.Int("expinc");
        if(id>=2022585 && id<=2022588 || id>=2022616 && id<=2022617)
        {
            if(spec.Int("berserk")!=0) stats["BERSERK"]=spec.Int("berserk");
            if(spec.Int("booster")!=0) stats["BOOSTER"]=spec.Int("booster");
        }
        if(id>=2022359 && id<=2022421 || id==2022198 || id==2022337)
        {
            if(spec.Int("mhpR")!=0) stats["HPREC"]=(sbyte)spec.Int("mhpR");
            if(spec.Int("mmpR")!=0) stats["MPREC"]=(sbyte)spec.Int("mmpR");
        }
        if(spec.Int("morph")>0) stats["MORPH"]=spec.Int("morph");
        bool cure=spec.Int("poison")>0 || spec.Int("seal")>0 || spec.Int("darkness")>0 || spec.Int("weakness")>0 || spec.Int("curse")>0;
        if (stats.Count==0 && !cure && spec.Int("hp")==0 && spec.Int("mp")==0 && spec.Int("hpR")==0 && spec.Int("mpR")==0) return false;
        int duration=spec.Int("time",-1),hp=(short)spec.Int("hp"),mp=(short)spec.Int("mp");
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
        if (count<=0 || !Profile.RemoveItem(id,count)) return false;
        player.hp=Mathf.Clamp(player.hp+(hp+(long)hpRate)*count,0,player.hpMax);
        player.mp=Mathf.Clamp(player.mp+(mp+(long)(player.mpMax*(spec.Int("mpR")/100.0)))*count,0,player.mpMax);
        var diseases=player.GetComponent<NativeMobDebuffs>();
        if(spec.Int("poison")>0) diseases?.Clear(125);
        if(spec.Int("seal")>0) diseases?.Clear(120);
        if(spec.Int("darkness")>0) diseases?.Clear(121);
        if(spec.Int("weakness")>0) { diseases?.Clear(122); diseases?.Clear(126); }
        if(spec.Int("curse")>0) diseases?.Clear(124);
        if(stats.Count>0 && duration>=0) NativeBuffs.For(player).ApplyItem(id,stats,duration,NativeBuffs.Now);
        GameSession.SaveToDb(); return true;
    }

    // null means handled; unsupported routes retain the item.
    public static string TryUse(int itemId, int inventoryPosition = 0)
    {
        if (!GameSession.InGame || NpcSession.Blocking) return "当前无法使用物品";
        var player = GameLoop.I?.player;
        if (player == null || player.hp <= 0) return "当前无法使用物品";
        if(NativeVegaWindow.Visible && (itemId/1000000==1 || itemId/10000==204))
        {
            var item=GameSession.Character.inventory.Find(x=>x.itemId==itemId && x.position>0 && (inventoryPosition==0 || x.position==inventoryPosition));
            return NativeVegaWindow.Select(item);
        }
        if (itemId / 1000000 == 1) return EquipLoadout.Equip(itemId, Profile.Attributes, inventoryPosition);
        if (Profile.ItemCount(itemId) <= 0) return "背包里没有这个物品";
        if(itemId/10000==212 || itemId/10000==524)
        {
            var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=itemId,sourcePosition=inventoryPosition});
            if(!response.ok) return response.error;
            GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();return null;
        }
        if (itemId / 1000000 == 2)
        {
            if(itemId==2430680)
            {
                var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,
                    itemId=itemId,sourcePosition=inventoryPosition,worldQuestDraw=true});
                if(!result.ok) return result.error;
                GameSession.Character.inventory=result.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();
                if(!string.IsNullOrEmpty(result.message)) NativeSystemMessage.Drop(5,result.message);
                return null;
            }
            if(itemId/10000==237)
            {
                var node=NativeItemSource.Node(itemId);
                var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=itemId,
                    bankExp=node?.Int("spec/exp") ?? 0,maxBankLevel=node?.Int("info/maxLevel",256) ?? 256});
                if(!result.ok) return result.error;
                GameSession.Character.inventory=result.inventory;
                GameSession.Character.record.gachaexp=result.bankedExp;
                GameSession.ApplyInventoryToProfile(); Profile.Save(); return null;
            }
            if(itemId==2003609)
            {
                int exp=NativeItemSource.Node(itemId)?.Int("spec/exp") ?? 0;
                if(exp<=0) return null;
                if(Profile.RemoveItem(itemId,1,inventoryPosition)) Profile.GrantExperience(exp);
                return null;
            }
            if(itemId/10000==204) { Pick(itemId); return null; }
            if(itemId/10000==227) return NativeCatchItems.Use(itemId,inventoryPosition);
            if(itemId/10000==210) return NativeItemUseEffects.SummonBag(itemId,inventoryPosition);
            if(itemId/10000==228 || itemId/10000==229) return NativeItemUseEffects.SkillBook(itemId,inventoryPosition);
            if(itemId/10000==238)
            {
                if(!NativeMonsterBook.Collect(itemId,1)) return "无法收录这张怪物卡片";
                return Profile.RemoveItem(itemId,1,inventoryPosition) ? null : "背包里没有这个物品";
            }
            if(itemId>=2030000 && itemId<=2030100) return NativeItemUseEffects.TownScroll(itemId,inventoryPosition);
            if(itemId/10000==226)
            {
                int fatigue=NativeItemSource.Node(itemId)?.Int("spec/incFatigue") ?? 0;
                if(!Profile.RemoveItem(itemId,1,inventoryPosition)) return "背包里没有这个物品";
                var record=GameSession.Character.record;
                record.mounttiredness=System.Math.Max(0,record.mounttiredness+fatigue);
                GameSession.SaveToDb();return null;
            }
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
            if(spec?.Int("isHasItem")==1)
            {
                var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {
                    characterId=GameSession.CharacterId,itemId=itemId,grantItemId=spec.Int("hasItemId"),grantQuantity=spec.Int("hasItemQuantity"),
                    grantMinutes=spec.Int("hasItemTime",-1),consumeGrantSource=spec.Int("remove")==1 });
                if(!result.ok) return result.error;
                GameSession.Character.inventory=result.inventory; GameSession.ApplyInventoryToProfile(); Profile.Save();
                return null;
            }
            if (spec?.Int("isNpc") == 1)
            {
                if (spec.Int("remove") == 1 && !Profile.RemoveItem(itemId, 1)) return "背包里没有这个物品";
                return NpcSession.Talk(spec.Int("npc"), spec.Text("script")) != null ? null : "物品NPC脚本尚未接入";
            }
            if (!string.IsNullOrEmpty(spec?.Text("script")))
            {
                if(string.IsNullOrEmpty(ScriptSource.Load("item",spec.Text("script")))) return "源端缺失此物品的使用脚本，无法使用";
                return NpcSession.UseItem(itemId) != null ? null : "物品脚本无法启动";
            }
            if(NativeItemSource.Node(itemId)?.Get("reward")!=null) return NativeItemUseEffects.RewardBox(itemId);
            // ItemInformationProvider.getItemEffect prefers specEx over spec.
            // These original items deliberately apply a disease, not their empty spec.
            var extended=NativeItemSource.Node(itemId)?.Get("specEx");
            if(extended!=null)
            {
                var disease=extended.Get("0");
                int skill=disease?.Int("mobSkill") ?? 0,level=disease?.Int("level") ?? 0;
                var effect=skill>0 && level>0 ? WzAssets.Node("Skill/MobSkill.img/"+skill+"/level/"+level) : null;
                if(effect==null) return "源端物品效果数据缺失";
                if(!Profile.RemoveItem(itemId,1,inventoryPosition)) return "背包里没有这个物品";
                // StatEffect target>0 excludes the user. This local session has no
                // other human characters; a target-others item must not debuff self.
                if(disease.Int("target")==0)
                    NativeMobDebuffs.For(player).Apply(skill,effect.Int("x"),effect.Int("y"),effect.Int("time")*1000L);
                GameSession.SaveToDb();return null;
            }
            if (NpcSession.UseRestorative(itemId)) return null;
            if (UseStatPotion(itemId,player)) return null;
            return "该消耗品的特殊使用功能尚未接入";
        }
        if (itemId / 10000 == 301) return player.UseChair(itemId) ? null : "请站在地面上使用椅子";
        if (itemId / 1000000 == 5)
        {
            // 4EFD25 -> 48645B / 4863D5 / 486845 -> A1DC5B:
            // these tickets have no ordinary double-click request. Their real
            // use remains in the original NPC / quest scripts.
            int family=itemId/10000;
            if(family==515 || family==522 || family==525 || family==538 || family==542 || family==546 || family==549) return null;
            if(itemId==5610000 || itemId==5610001) {NativeVegaWindow.Open(itemId);return null;}
            if(itemId==5050000) {NativeResetWindow.Open(itemId,inventoryPosition);return null;}
            // 505 SP 洗点：cashType24 → 0xA0C6DD；5050001..4 = 1..4 转技能点重置卷
            if(itemId>=5050001 && itemId<=5050004) {NativeSpResetWindow.Open(itemId,inventoryPosition);return null;}
            // 快速投递券（itemType 533）：只开寄件窗口，券在真正寄出时才扣（DueyProcessor.dueySendItem）。
            if(itemId/10000==533) {NativeDeliveryWindow.Open(true);return null;}
            // 传送石：cashType22 → 0xA0CAB0；5040000..4 普通 / 5041000 VIP
            if(itemId/10000==504) {NativeTeleportWindow.Open(itemId,inventoryPosition);return null;}
            // 改名（领头宠物）：0xA0BA15 → 7A06F3 过滤链；服务端 getPet(0) 无宠物不消耗
            if(itemId/10000==517) {NativeRenameWindow.Open(itemId,inventoryPosition);return null;}
            // 黑板：cashType32 → 0xA0CE2E（CUtilDlgEx type3 max40）
            if(itemId/10000==537) {NativeAdBoard.Open(itemId);return null;}
            if(itemId==5510000) return "死亡后可在复活确认窗口使用原地复活术";
            if(itemId/1000==5000)
            {
                var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=itemId,sourcePosition=inventoryPosition,togglePet=true});
                if(!response.ok) return response.error;
                GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();return null;
            }
            if(itemId/10000==516)
            {
                player.Express(itemId-5159992);return null;
            }
            if(itemId/10000==510)
            {
                if(!Profile.RemoveItem(itemId,1,inventoryPosition)) return "背包里没有这个物品";
                AudioManager.PlayBgm("Jukebox/Congratulation");return null;
            }
            if(itemId==5180000) return NpcSession.Talk(1032102,"waterOfLife")!=null ? null : "生命之水脚本无法启动";
            // BeiDou cashType28 dispatches to A0EAAC without sending a use
            // request. Pet skill scrolls are retained, exactly as in that client.
            if(itemId/10000==519) return null;
            if(itemId/10000==529 || itemId/10000==528 && itemId!=5281000)
                return "源端未启用此道具";
            if(itemId/1000==5211 || itemId/1000==5360)
                return "此倍率券在背包中按指定时段自动生效";
            if(IsCashEquipment(itemId)) { Pick(itemId); return null; }
            if(itemId/10000==520)
            {
                int meso=NativeItemSource.Node(itemId)?.Int("info/meso",-1) ?? -1;
                if(!Profile.RemoveItem(itemId,1,inventoryPosition)) return "背包里没有这个物品";
                Profile.AddGold(meso); return null;
            }
            if(itemId/10000==545)
            {
                if(NativeNpcShopWindow.Visible) return null;
                NativeNpcShopWindow.OpenShop(1338,false);
                if(NativeNpcShopWindow.Visible) Profile.RemoveItem(itemId,1,inventoryPosition);
                return null;
            }
            if(itemId/10000==530) return UseStatPotion(itemId,player) ? null : "无法应用此变身道具";
            return "该现金道具的使用功能尚未接入";
        }
        return "这个物品不能直接使用";
    }
}
