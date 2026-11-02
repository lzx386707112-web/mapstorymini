using System;
using System.Collections.Generic;

// PacketCreator.charInfo: only summoned pets, equipped mount, wishes and book/medals.
// Read on the server so inspecting another actor never returns the viewer's data.
public sealed class NativeCharacterInfoData
{
    public string guild = "-";
    public readonly List<InventoryEntry> pets = new List<InventoryEntry>();
    public readonly List<int> wishes = new List<int>();
    public readonly List<int> items = new List<int>();
    public readonly List<int> medalQuests = new List<int>();
    public readonly List<int> mountItems = new List<int>();
    public readonly List<List<int>> petItems = new List<List<int>> { new List<int>(), new List<int>(), new List<int>() };
    public int mount, mountLevel, mountExp, mountTiredness, medal, petClothing, bookLevel = 1, bookCover, normalCards, specialCards;
    public static NativeCharacterInfoData Read(DbStore db, CharRecord self, Server.Bot.BotActor actor)
    {
        var result = new NativeCharacterInfoData();
        var full = actor == null ? CharService.Load(db, (int)self.id) : actor.character;
        var record = full?.record;
        int cid = actor?.id ?? (int)self.id;
        var inventory = full?.inventory ?? (actor as Server.Bot.BotCharacter)?.inventory;
        if (inventory != null)
            foreach (var item in inventory)
            {
                if (item.petData != null && Convert.ToInt32(item.petData["summoned"]) == 1 && result.pets.Count < 3)
                    result.pets.Add(item.Copy());
                if (item.inventoryType != -1) continue;
                if (item.position == -18) result.mount = item.itemId;
                if (item.position == -49) result.medal = item.itemId;
                if (item.position == -114) result.petClothing = item.itemId;
                if (item.position == -18 || item.position == -19 || item.position == -20) result.mountItems.Add(item.itemId);
                for(int i=0;i<3;i++)
                    if (NativePetItems.IsEquipment(item.itemId) && NativePetItems.EquipmentSlot(item.itemId,i)==item.position)
                        result.petItems[i].Add(item.itemId);
            }
        if (record != null)
        {
            var guild = db.Table("guilds").Find(r => DbStore.Int(r, "guildid") == record.guildid);
            if (guild != null) result.guild = DbStore.Str(guild, "name");
            result.mountLevel = record.mountlevel; result.mountExp = record.mountexp; result.mountTiredness = record.mounttiredness;
            result.bookCover = record.monsterbookcover;
        }
        if (actor is Server.Bot.BotCharacter mountedBot && result.mount == 0 && mountedBot.equips.TryGetValue(-18, out int mount))
            result.mount = mount;
        int actorLevel = actor?.level ?? record?.level ?? 0;
        if (result.mount != 0 && Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(result.mount), "reqLevel") > actorLevel)
            result.mount = 0;
        // CUIUserInfo 903E24 walks avatar body parts, skipping body, pet and mount.
        var visibleItems = new SortedDictionary<int, List<InventoryEntry>>();
        if (inventory != null) foreach (var item in inventory)
        {
            if (item.inventoryType != -1) continue;
            int slot = -item.position;
            if (slot > 100) slot -= 100;
            if (slot <= 0 || slot == 14 || slot == 18 || slot == 19 || slot == 20 || item.itemId / 10000 == 181) continue;
            if (!visibleItems.TryGetValue(slot,out var items)) visibleItems[slot]=items=new List<InventoryEntry>();
            items.Add(item);
        }
        if (actor is Server.Bot.BotCharacter bot) foreach (var pair in bot.equips)
        {
            int slot = -pair.Key;
            if (slot > 100) slot -= 100;
            if (slot > 0 && slot != 14 && slot != 18 && slot != 19 && slot != 20 && pair.Value / 10000 != 181)
            {
                if (!visibleItems.TryGetValue(slot,out var items)) visibleItems[slot]=items=new List<InventoryEntry>();
                if (!items.Exists(item=>item.itemId==pair.Value)) items.Add(new InventoryEntry {itemId=pair.Value,position=pair.Key});
            }
        }
        foreach (var items in visibleItems.Values) {
            items.Sort((a,b)=>b.position.CompareTo(a.position));
            foreach(var item in items)result.items.Add(item.itemId);
        }
        for(int i=0;i<3;i++) if(result.petClothing!=0 && !result.petItems[i].Contains(result.petClothing))result.petItems[i].Insert(0,result.petClothing);
        if (actor == null)
        {
            // Preserve the server's active pet order rather than cash inventory order.
            result.pets.Clear();
            for (int i = 0; i < 3; i++) { var pet = NativePetItems.GetPet(db, cid, i); if (pet != null) result.pets.Add(pet.Copy()); }
        }
        foreach (var row in db.Table("cash_wishlist").Select(r => DbStore.Int(r, "characterid") == cid))
            result.wishes.Add(DbStore.Int(row, "sn"));
        foreach (var row in db.Table("monsterbook").Select(r => DbStore.Int(r, "charid") == cid))
            if (DbStore.Int(row, "cardid") / 1000 >= 2388) result.specialCards++; else result.normalCards++;
        int threshold = 1;
        do { threshold += result.bookLevel * 10; if (result.normalCards + result.specialCards < threshold) break; result.bookLevel++; } while (true);
        if (full != null) foreach (var q in full.quests)
            if (q.status == 2 && q.quest >= 29000) result.medalQuests.Add(q.quest);
        result.medalQuests.Sort();
        return result;
    }
}
