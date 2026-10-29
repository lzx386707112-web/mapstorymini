using System;
using Server.Bot.Environment;

namespace Server.Bot
{
    public static class MarketShopSpots
    {
        static readonly BotPoint[] henesys = { new BotPoint(-262,-416),new BotPoint(-129,-416),new BotPoint(19,-416),new BotPoint(175,-416),new BotPoint(328,-416),new BotPoint(482,-416),new BotPoint(638,-416),new BotPoint(801,-416),new BotPoint(-300,-206),new BotPoint(-133,-206),new BotPoint(18,-206),new BotPoint(166,-206),new BotPoint(319,-206),new BotPoint(477,-206),new BotPoint(608,-86),new BotPoint(723,-146),new BotPoint(854,-146),new BotPoint(-271,34),new BotPoint(-124,34),new BotPoint(21,34),new BotPoint(169,34),new BotPoint(328,34),new BotPoint(481,34) };
        static readonly BotPoint[] ludi = { new BotPoint(-1769,-318),new BotPoint(-1595,-318),new BotPoint(-1409,-318),new BotPoint(-1236,-318),new BotPoint(-1081,-318),new BotPoint(-916,-318),new BotPoint(-751,-318),new BotPoint(-580,-318),new BotPoint(-421,-318),new BotPoint(-1775,-108),new BotPoint(-1608,-108),new BotPoint(-1455,-108),new BotPoint(-1290,-108),new BotPoint(-1146,-108),new BotPoint(-984,-108),new BotPoint(-836,-108),new BotPoint(-682,-108),new BotPoint(-506,-108),new BotPoint(-1893,102),new BotPoint(-1718,102),new BotPoint(-1561,102),new BotPoint(-1400,102),new BotPoint(-1230,102),new BotPoint(-1052,102),new BotPoint(-881,102),new BotPoint(-709,102),new BotPoint(-529,102),new BotPoint(-209,102) };
        public static BotPoint[] ForRoom(int mapId)
        {
            if (mapId < 910000001 || mapId > 910000012) throw new ArgumentOutOfRangeException(nameof(mapId));
            return (BotPoint[])(mapId <= 910000006 ? henesys : ludi).Clone();
        }
    }
}
