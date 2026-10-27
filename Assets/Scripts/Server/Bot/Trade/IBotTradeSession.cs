using System;
using System.Collections.Generic;

namespace Server.Bot.Trade
{
    // 原端org.gms.server.Trade边界。结算由交易服务实现，不允许AI直接修改玩家背包。
    public enum TradeResult
    {
        NO_RESPONSE = 1, PARTNER_CANCEL = 2, SUCCESSFUL = 7, UNSUCCESSFUL = 8,
        UNSUCCESSFUL_UNIQUE_ITEM_LIMIT = 9, UNSUCCESSFUL_ANOTHER_MAP = 12, UNSUCCESSFUL_DAMAGED_FILES = 13
    }
    public interface IBotTradeSession
    {
        BotActor actor { get; }
        IBotTradeSession partner { get; }
        int meso { get; }
        bool locked { get; }
        IList<InventoryEntry> Items { get; }
        Action<TradeResult> ResultCallback { get; set; }
        bool AddItem(InventoryEntry item, int slot);
        bool SwapItem(InventoryEntry item, int slot);
        void Chat(string text);
        void SetMesoBot(int amount);
        void Accept();
        void Confirm();
        void Decline();
    }
}
