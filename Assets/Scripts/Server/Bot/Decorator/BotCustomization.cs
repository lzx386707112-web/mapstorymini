using System.Collections.Generic;

namespace Server.Bot.Decorator
{
    public sealed class BotCustomization
    {
        readonly BotItemCatalog catalog;
        readonly Social.BotOutput output;
        public BotCustomization(BotItemCatalog catalog, Social.BotOutput output)
        { this.catalog = catalog; this.output = output; }
        public static int EquipSlotType(int id)
        {
            if (id == 1189999) return 53;
            int prefix = id / 10000;
            if (prefix == 134 || prefix == 135) return 51;
            if (id >= 1302000 && id < 1493000) return 11;
            return prefix switch {
                100 => 1, 101 => 2, 102 => 3, 103 => 4, 104 => 5, 105 => 5, 106 => 6, 107 => 7,
                108 => 8, 109 => 10, 110 => 9, 111 => 12, 112 => 17, 113 => 50, 114 => 49,
                115 => 20, 118 => 55, 119 => 56, 167 => 54, _ => 0
            };
        }
        public int DestinationSlot(int id)
        {
            int slot = EquipSlotType(id);
            bool cash = BotItemCatalog.Int(catalog.Info(id), "cash") == 1;
            if (cash && id >= 1700000 && id < 1800000) return -111;
            return cash ? -(slot == 11 ? 111 : slot + 100) : -slot;
        }
        public void Equip(BotCharacter bot, int? id)
        { if (id.HasValue) Equip(bot, id.Value, DestinationSlot(id.Value)); }
        public void Equip(BotCharacter bot, int id, int slot)
        {
            var item = catalog.CleanItem(id);
            if (!item.isEquip) throw new System.InvalidCastException("Bot equipment must be an equip item");
            item.position = slot; bot.equipment[slot] = item; bot.equips[slot] = id;
            output.Send(Social.BotOutput.Kind.Appearance, bot);
        }
        public void Clear(BotCharacter bot) { bot.equipment.Clear(); bot.equips.Clear(); }
        public void ClearCash(BotCharacter bot)
        {
            if (bot == null) return;
            foreach (int slot in new List<int>(bot.equips.Keys))
                if (slot > -160 && slot <= -100) { bot.equips.Remove(slot); bot.equipment.Remove(slot); }
            output.Send(Social.BotOutput.Kind.Appearance, bot);
        }
    }
}
