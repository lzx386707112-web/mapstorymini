using System.Collections.Generic;

namespace Server.Bot.Trade
{
    // BotTradeQueue.java：以活体角色引用为键，已有邀请不覆盖。
    public sealed class BotTradeQueue
    {
        readonly Dictionary<BotActor, BotActor> requests = new Dictionary<BotActor, BotActor>();
        public void Add(BotActor bot, BotActor partner)
        { if (!requests.ContainsKey(bot)) requests.Add(bot, partner); }
        public BotActor Get(BotActor bot) => requests.TryGetValue(bot, out var partner) ? partner : null;
        public bool HasPending(BotActor bot) => requests.ContainsKey(bot);
        public void Remove(BotActor bot) => requests.Remove(bot);
    }
    public sealed class BotTradeHandler
    {
        readonly BotActor actor;
        public BotActor confirmedPartner;
        public BotTradeHandler(BotActor actor) { this.actor = actor; }
        public BotActor RawPartner => actor.trade?.partner?.actor;
        public bool VerifyPartner() => RawPartner != null && confirmedPartner != null && confirmedPartner.mapId == actor.mapId;
        public void Reset() => confirmedPartner = null;
    }
}
