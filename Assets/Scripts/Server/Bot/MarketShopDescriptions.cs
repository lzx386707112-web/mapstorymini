using System;
using System.Collections.Generic;
using System.IO;

namespace Server.Bot
{
    // FMShopDescriptionManager / FMShopDescGen；保留空串与null不同的选择语义。
    public sealed class MarketShopDescriptions
    {
        readonly BotResources resources;
        readonly BotItemCatalog catalog;
        readonly Random random;
        readonly List<string> clans = new List<string>();
        public MarketShopDescriptions(BotResources resources, BotItemCatalog catalog, Random random = null)
        { this.resources = resources; this.catalog = catalog; this.random = random ?? new Random(); }
        string Pick(IReadOnlyList<string> list) => list[random.Next(list.Count)];
        string Normalize(string value) => string.IsNullOrWhiteSpace(value) || MarketShopTitles.HasLatin(value)
            ? Pick(MarketShopTitles.COMMON) : value.Trim();
        void Shuffle(int[] values)
        { for (int i = values.Length - 1; i > 0; i--) { int j = random.Next(i + 1), tmp = values[i]; values[i] = values[j]; values[j] = tmp; } }
        string Text(string type)
        {
            string file; string[] fallback;
            switch (type)
            {
                case "thief": file = "thiefDesc.txt"; fallback = MarketShopTitles.THIEF; break;
                case "warrior": file = "warriorDesc.txt"; fallback = MarketShopTitles.WARRIOR; break;
                case "mage": file = "mageDesc.txt"; fallback = MarketShopTitles.MAGE; break;
                case "bowman": file = "bowmanDesc.txt"; fallback = MarketShopTitles.BOWMAN; break;
                case "chair": file = "chairDesc.txt"; fallback = MarketShopTitles.CHAIR; break;
                case "scrolls": file = "scrollsDesc.txt"; fallback = MarketShopTitles.SCROLLS; break;
                case "useable": file = "useableDesc.txt"; fallback = MarketShopTitles.USEABLE; break;
                case "etc": file = "etcDesc.txt"; fallback = MarketShopTitles.ETC; break;
                case "fmclan": file = "FMClans.txt"; fallback = MarketShopTitles.CLAN; break;
                case "shortword": file = "shortWordDesc.txt"; fallback = MarketShopTitles.SHORT; break;
                case "emojis": file = "emojiFaces.txt"; fallback = MarketShopTitles.EMOJI; break;
                default: file = "commonDesc.txt"; fallback = MarketShopTitles.COMMON; break;
            }
            var lines = new List<string>();
            using (var reader = new StringReader(resources.ReadText("soloMapling/FreeMarket/FMNameDesc/" + file) ?? ""))
            { string line; while ((line = reader.ReadLine()) != null) if (line.Length != 0 && !MarketShopTitles.HasLatin(line)) lines.Add(line); }
            return lines.Count == 0 ? Pick(fallback) : Pick(lines);
        }
        string Clan()
        {
            if (clans.Count == 0) for (int i = 0; i < 7; i++) clans.Add(Text("fmclan"));
            return "【" + Pick(clans) + "】";
        }
        static string TypeName(MarketShopType type)
        {
            switch (type)
            {
                case MarketShopType.Thief: case MarketShopType.Stars: return "thief";
                case MarketShopType.Warrior: return "warrior";
                case MarketShopType.Mage: return "mage";
                case MarketShopType.Bowman: return "bowman";
                case MarketShopType.Chair: return "chair";
                case MarketShopType.Scroll: case MarketShopType.DarkScroll: case MarketShopType.Mastery: return "scrolls";
                case MarketShopType.Potion: return "useable";
                case MarketShopType.ETC: return "etc";
                default: return "common";
            }
        }
        string TopItem(MarketShop shop)
        {
            int max = 0, id = 0;
            foreach (var entry in shop.items) if (entry.item.inventoryType != 1 && entry.price > max) { max = entry.price; id = entry.item.itemId; }
            string name = catalog.Name(id);
            if (name == null) return null;
            if (!name.Contains("White Scroll") && !name.Contains("Chaos Scroll"))
                foreach (string word in new[] {"Dark Scroll ","Dark scroll ","Scroll ","for ","[Mastery Book] ","Throwing-Knives","Throwing-Stars"}) name = name.Replace(word, "").Trim();
            return MarketShopTitles.HasLatin(name) ? "" : name;
        }
        string TopEquip(MarketShop shop)
        {
            int max = 0; InventoryEntry item = null;
            foreach (var entry in shop.items) if (entry.item.inventoryType == 1 && entry.price > max) { max = entry.price; item = entry.item; }
            if (item == null) return TopItem(shop);
            bool displayStat = random.NextDouble() < .5;
            string name = catalog.Name(item.itemId);
            if (string.IsNullOrWhiteSpace(name) || MarketShopTitles.HasLatin(name)) return Pick(MarketShopTitles.COMMON);
            if (BotItemCatalog.Int(item.equipmentData, "level") == 0) return "未砸" + name;
            if (!displayStat) return "神砸" + name;
            string stat = MarketScrollPricing.HighestStat(item, catalog.CleanEquip(item.itemId), out _);
            string label; int value;
            switch (stat)
            {
                case "watt": label = "物攻"; value = item.watk; break;
                case "matt": label = "魔攻"; value = item.matk; break;
                case "str": label = "力量"; value = item.str; break;
                case "dex": label = "敏捷"; value = item.dex; break;
                case "int": label = "智力"; value = item.int_; break;
                default: label = "幸运"; value = item.luk; break;
            }
            return name + label + value;
        }
        string GenerateTop(int type, MarketShop shop)
        {
            switch (type)
            {
                case 0: return Clan();
                case 1: return TopItem(shop);
                case 2: return TopEquip(shop);
                case 3: return Text(TypeName(shop.primary));
                case 4: case 5: return Text("shortword");
                default: return !string.IsNullOrWhiteSpace(shop.ownerName) && !MarketShopTitles.HasLatin(shop.ownerName)
                    && shop.ownerName.Length <= 6 ? shop.ownerName + "的小店" : "欢迎光临";
            }
        }
        public string Generate(MarketShop shop)
        {
            var top = new[] {0,1,2,3,4,5,6}; var bottom = new[] {0,1,2}; Shuffle(top); Shuffle(bottom);
            int room = shop.mapId % 100, chosen = -1; string result = null;
            if (MarketEconomy.IsHotRoom(shop.mapId) && random.NextDouble() < .9) { chosen = 0; result = Clan(); }
            if (chosen < 0) foreach (int type in top)
            {
                bool tierSA = shop.tier == "S" || shop.tier == "A";
                bool valid = type == 0 ? tierSA && (room == 1 || room == 2 || room == 7) : type <= 2 ? tierSA && room >= 1 && room <= 22
                    : type == 3 ? room >= 1 && room <= 22 : type <= 5 ? room >= 3 && room <= 22 : shop.tier == "B" && room >= 13 && room <= 22;
                if (!valid) continue;
                string candidate = GenerateTop(type, shop);
                if (candidate != null) { chosen = type; result = candidate; break; }
            }
            if (random.NextDouble() < .4) foreach (int type in bottom)
            {
                double chance = chosen == 0 ? (type == 0 ? .7 : .5) : chosen == 1 || chosen == 2 ? (type == 0 ? .8 : type == 1 ? .3 : .4)
                    : chosen == 3 ? (type == 0 ? .6 : type == 1 ? .3 : .5) : chosen == 4 || chosen == 5 ? (type == 1 ? 0 : .5) : 0;
                if (chance == 0 || random.NextDouble() >= chance) continue;
                result += " " + (type == 0 ? Pick(MarketShopTitles.OFFER) : type == 1 ? Pick(MarketShopTitles.CURRENCY) : Text("emojis")); break;
            }
            return Normalize(result);
        }
    }
}
