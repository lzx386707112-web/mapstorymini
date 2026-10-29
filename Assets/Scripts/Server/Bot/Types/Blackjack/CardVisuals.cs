using System.Collections.Generic;

namespace Server.Bot.Types.Blackjack
{
    // BotTypes/Blackjack/CardVisuals.java：用怪物卡片当扑克牌的可视化，
    // 每张牌以「dropItem 在地上」的形式出现，所以只需要一个 rank -> 道具 id 映射。
    public static class CardVisuals
    {
        static readonly Dictionary<int, int> CardValueToItemId = new Dictionary<int, int>
        {
            [2] = 2380004,   // 2 - 红蜗牛
            [3] = 2380005,   // 3 - 绿水灵
            [4] = 2380007,   // 4 - 橙色蘑菇
            [5] = 2380009,   // 5 - 蝴蝶结猪
            [6] = 2381007,   // 6 - 角蘑菇
            [7] = 2381024,   // 7 - 恶灵附身
            [8] = 2383012,   // 8 - 青龙
            [9] = 2383043,   // 9 - 变异石人
            [10] = 2388017,  // 10 - 残暴巴洛古
            [11] = 2388022,  // J - 帕普拉图斯
            [12] = 2388023,  // Q - 扎昆
            [13] = 2388024,  // K - 闇黑龙王
            [14] = 2388043,  // A - 粉红熊
        };

        public static int GetCardItemId(string card)
        {
            int displayValue = BlackjackRules.GetCardDisplayValue(card);
            if (!CardValueToItemId.TryGetValue(displayValue, out int itemId))
                throw new System.ArgumentException("No visual mapping for card: " + card);
            return itemId;
        }
    }
}
