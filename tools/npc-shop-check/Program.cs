using System;
using System.Collections.Generic;
using System.IO;
using Server.Bot;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static int Count(FullCharacter c, int id) { int n = 0; foreach (var item in c.inventory) if (item.itemId == id && item.position > 0) n += item.quantity; return n; }
    static void Main()
    {
        var path = Path.Combine(Path.GetTempPath(), "npc-shop-check-" + Guid.NewGuid() + ".db");
        try
        {
            var db = DbStore.Open(path);
            var login = LoginService.Login(db, "shopcheck", "pw");
            var created = CharFactory.CreateCharacter(db, login.accountId, 0, "ShopCheck", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            Check(created.code == 0, "create test character");
            int cid = (int)CharactersDao.ByAccount(db, login.accountId, 0)[0].id;
            var service = new NativeNpcShopService(db);
            NpcShopResponse Send(NpcShopOperation operation, int id = 0, int index = 0, int item = 0, int q = 1, int type = 0, int slot = 0, bool fallback = false)
                => service.Handle(new NpcShopRequest { operation = operation, characterId = cid, id = id, index = index, itemId = item, quantity = q, inventoryType = type, position = slot, fallback = fallback });
            Check(service.HasNpc(11100), "silent potion NPC resolves shop");
            Check(db.Table("shops").rows.Count == 121 && db.Table("shopitems").rows.Count == 4404, "all migrated shops and stock seeded");
            foreach (var row in db.Table("shops").rows)
            {
                var result = Send(NpcShopOperation.OpenShop, DbStore.Int(row, "shopid"));
                Check(result.ok && result.opened && result.items.Count > 0, "open every original shop " + DbStore.Int(row, "shopid"));
            }
            var original = Send(NpcShopOperation.OpenShop, 11000);
            Check(original.npcId == 11000, "ShopFactory shopid is not npcid");
            Check(Send(NpcShopOperation.OpenShop, 9110100, fallback: true).shopId == 11000, "cm missing shop falls back to 11000");
            Check(!Send(NpcShopOperation.OpenShop, 9110100).opened, "factory missing shop does not invent stock");
            var shop = Send(NpcShopOperation.OpenNpc, 11100);
            int potion = shop.items.FindIndex(x => x.itemId == 2000000 && x.price > 0);
            Check(potion >= 0, "potion stock"); int price = shop.items[potion].price;
            var before = CharService.Load(db, cid); int held = Count(before, 2000000);
            Check(!Send(NpcShopOperation.Buy, index: potion, item: 2000000, q: 0).ok, "reject zero quantity");
            Check(!Send(NpcShopOperation.Buy, index: potion, item: 2000001).ok, "reject forged stock item");
            Check(!Send(NpcShopOperation.Buy, index: potion, item: 2000000, q: 32767).ok, "reject insufficient mesos");
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["meso"] = 1000000L });
            var bought = Send(NpcShopOperation.Buy, index: potion, item: 2000000, q: 3);
            Check(bought.ok && Count(bought.character, 2000000) == held + 3 && bought.character.record.meso == 1000000 - 3 * price, "potion buy persists exact quantity and debit");
            var stack = bought.character.inventory.Find(x => x.itemId == 2000000 && x.position > 0);
            int sellPrice = service.SellPrice(2000000, 2);
            var sold = Send(NpcShopOperation.Sell, q: 2, type: 2, slot: stack.position);
            Check(sold.ok && Count(sold.character, 2000000) == held + 1 && sold.character.record.meso == bought.character.record.meso + sellPrice, "sell exact slot and credit");
            Check(!Send(NpcShopOperation.Sell, q: 32767, type: 2, slot: stack.position).ok, "reject oversell");
            var equipmentShop = Send(NpcShopOperation.OpenShop, 11000);
            int equipment = equipmentShop.items.FindIndex(x => x.itemId / 1000000 == 1 && x.price > 0);
            var equip = equipmentShop.items[equipment];
            int equipBalance = CharService.Load(db, cid).record.meso;
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["equipslots"] = 0L });
            Check(!Send(NpcShopOperation.Buy, index: equipment, item: equip.itemId).ok && CharService.Load(db, cid).record.meso == equipBalance, "full equipment inventory rolls back without debit");
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["equipslots"] = 24L });
            var equipBuy = Send(NpcShopOperation.Buy, index: equipment, item: equip.itemId);
            var entry = equipBuy.character.inventory.Find(x => x.itemId == equip.itemId && x.position > 0);
            Check(equipBuy.ok && entry != null && entry.isEquip && entry.equipmentData != null, "weapon clean stats persisted");
            Check(Send(NpcShopOperation.Sell, type: 1, slot: entry.position).ok, "equipment sale persists");
            equipmentShop = Send(NpcShopOperation.OpenNpc, 1001100);
            int star = equipmentShop.items.FindIndex(x => x.itemId == 2070000 && x.price > 0);
            Check(star >= 0, "recharge stock");
            var stars = Send(NpcShopOperation.Buy, index: star, item: 2070000);
            var throwing = stars.character.inventory.Find(x => x.itemId == 2070000 && x.position > 0);
            Check(stars.ok && throwing.quantity > 1, "rechargeable purchase full stack");
            db.Table("inventoryitems").Update(r => DbStore.Long(r,"inventoryitemid") == throwing.itemRowId, new DbRow { ["quantity"] = (long)(throwing.quantity - 10) });
            int rechargeCost = (int)Math.Ceiling(service.UnitPrice(2070000) * 10);
            var recharged = Send(NpcShopOperation.Recharge, type: 2, slot: throwing.position);
            Check(recharged.ok && Count(recharged.character, 2070000) == throwing.quantity && recharged.character.record.meso == stars.character.record.meso - rechargeCost, "recharge stack and price");
            int balance = recharged.character.record.meso;
            Check(Send(NpcShopOperation.Recharge, type: 2, slot: throwing.position).character.record.meso == balance, "full recharge no double debit");
            Send(NpcShopOperation.Close);
            Check(!Send(NpcShopOperation.Buy, index: potion, item: 2000000).ok, "closed session cannot buy");
            Send(NpcShopOperation.OpenNpc, 11100);
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["map"] = 100000000L });
            Check(!Send(NpcShopOperation.Buy, index: potion, item: 2000000).opened, "map change invalidates session");
            db.Table("shopitems").Delete(r => DbStore.Int(r,"shopid") == 11100);
            Check(new NativeNpcShopService(db).HasNpc(11100) && db.Table("shopitems").Select(r => DbStore.Int(r,"shopid") == 11100).Count == 0, "seed marker preserves admin deletions");
            Console.WriteLine($"NPC shop checks: {checks} passed");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
