using System.Collections.Generic;

namespace Server.Bot
{
    // PacketCreator.addCharEquips：现金覆盖普通槽，-111现金武器单独发送。
    // 这里只投影纸娃娃支持的可见部位，戒指/吊坠等属性装备不进入合成器。
    public static class BotAppearance
    {
        public static string[] CharacterIds(BotCharacter actor)
        {
            var visible = new SortedDictionary<int, int>();
            int cashWeapon = 0;
            foreach (var entry in actor.equips)
            {
                int slot = -entry.Key;
                if (slot == 111) { cashWeapon = entry.Value; continue; }
                if (slot > 0 && slot < 100 && !visible.ContainsKey(slot)) visible.Add(slot, entry.Value);
            }
            foreach (var entry in actor.equips)
            {
                int slot = -entry.Key;
                if (slot > 100 && slot != 111) visible[slot - 100] = entry.Value;
            }
            var ids = new List<string> { (2000 + actor.skinColor).ToString("D8"),
                (12000 + actor.skinColor).ToString("D8"), actor.face.ToString("D8"), actor.hair.ToString("D8") };
            int weapon = 0;
            foreach (var entry in visible)
            {
                int type = entry.Value / 10000;
                if (entry.Key == 11) { weapon = entry.Value; continue; }
                if (type == 100 || type == 104 || type == 105 || type == 106 || type == 107 ||
                    type == 108 || type == 109 || type == 110) ids.Add(entry.Value.ToString("D8"));
            }
            if (cashWeapon > 0) weapon = cashWeapon;
            if (weapon > 0) ids.Add(weapon.ToString("D8"));
            return ids.ToArray();
        }
    }
}
