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
            int skin = actor.skinColor;
            int face = actor.face > 0 ? actor.face : (actor.gender == 1 ? 21009 : 20012);
            int hair = actor.hair > 0 ? actor.hair : (actor.gender == 1 ? 31000 : 30000);
            var ids = new List<string> { (2000 + skin).ToString("D8"),
                (12000 + skin).ToString("D8"), face.ToString("D8"), hair.ToString("D8") };
            int weapon = 0;
            foreach (var entry in visible)
            {
                if (entry.Value <= 0) continue;
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
