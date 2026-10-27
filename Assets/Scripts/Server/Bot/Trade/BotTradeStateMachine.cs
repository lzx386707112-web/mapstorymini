using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Server.Bot.Social;

namespace Server.Bot.Trade
{
    public enum TradeMode { SELLING, BUYING, NULL }
    public sealed class BotTradeStateMachine
    {
        public enum State { INITIALIZE, WAITING_RESPONSE, RESPONDING, AWAITING_CONFIRMATION, CONFIRMING,
            CONFIRMED_LOCKED, CLEANUP, COMPLETED, TIMED_OUT, DECLINE }
        public State state { get; private set; } = State.INITIALIZE;
        public readonly TradeMode mode;
        public bool OfferAccepted { get; private set; }
        public bool TradeComplete { get; private set; }
        readonly BotStateMachine parent;
        long startedAt, resumeAt;
        Action pending;
        TradeResult? lastResult;
        BotCharacter Chr => parent.chr;
        public BotTradeStateMachine(BotStateMachine parent, TradeMode mode = TradeMode.SELLING)
        { this.parent = parent; this.mode = mode; startedAt = BotScheduler.Now(); }
        void Delay(Action continuation)
        { resumeAt = BotScheduler.Now() + 2000; pending = continuation; }
        public void Update()
        {
            if (pending != null)
            {
                if (BotScheduler.Now() < resumeAt) return;
                var continuation = pending; pending = null; continuation();
                CheckTimeout(); return;
            }
            switch (state)
            {
                case State.INITIALIZE:
                    StartCallback();
                    if (mode == TradeMode.SELLING) state = PostItems() ? State.RESPONDING : State.WAITING_RESPONSE;
                    else
                    {
                        BotTradeCommands.WriteChat(Chr, mode == TradeMode.BUYING ? WantsMessage() : "我暂时没什么货。");
                        state = State.WAITING_RESPONSE;
                    }
                    break;
                case State.RESPONDING:
                    if (mode == TradeMode.SELLING)
                    { startedAt = BotScheduler.Now(); BotTradeCommands.WriteChat(Chr, WantsMessage()); }
                    else if (mode == TradeMode.BUYING && CorrectItems())
                    {
                        int mesos = parent.tradeWants.mesoOffering;
                        if (mesos > 0)
                        {
                            BotTradeCommands.SetMeso(Chr, mesos);
                            BotTradeCommands.WriteChat(Chr, "这件我出 " + FormatPrice(mesos) + "。");
                        }
                        state = State.CONFIRMING; break;
                    }
                    state = State.WAITING_RESPONSE; break;
                case State.WAITING_RESPONSE:
                    if (mode == TradeMode.SELLING)
                    {
                        if (!Sufficient() && BotTradeCommands.IsPartnerLocked(Chr)) state = State.DECLINE;
                        else if (Sufficient()) state = State.CONFIRMING;
                    }
                    else if (mode == TradeMode.BUYING)
                    {
                        if (CorrectItems()) state = State.RESPONDING;
                        else if (BotTradeCommands.IsPartnerLocked(Chr) && !CorrectItems()) state = State.DECLINE;
                    }
                    break;
                case State.CONFIRMING:
                    startedAt = BotScheduler.Now(); OfferAccepted = true;
                    BotTradeCommands.WriteChat(Chr, parent.marketFlavor.TradeReply());
                    BotTradeCommands.Confirm(Chr); state = State.CONFIRMED_LOCKED; break;
                case State.CONFIRMED_LOCKED:
                    if (!parent.tradeHandler.VerifyPartner()) state = State.CLEANUP;
                    break;
                case State.CLEANUP:
                    parent.tradeInventory.Reset(); parent.tradeWants.Reset(); parent.tradeMode = TradeMode.NULL;
                    bool successful = lastResult == TradeResult.SUCCESSFUL;
                    parent.output.Emote(Chr, successful ? 2 : 4);
                    parent.output.Speak(Chr, successful ? "谢谢，下次再来。" : "怎么取消了？");
                    Delay(() => {
                        if (successful) parent.lastTradeResult = TradeResult.SUCCESSFUL;
                        lastResult = null; state = State.COMPLETED;
                    });
                    return;
                case State.COMPLETED: TradeComplete = true; break;
                case State.TIMED_OUT:
                    BotTradeCommands.WriteChat(Chr, "等太久了，我先关了。");
                    Delay(() => { BotTradeCommands.Decline(Chr); TradeComplete = true; state = State.COMPLETED; });
                    return;
                case State.DECLINE:
                    parent.tradeLogic.blocked.AddToBlockList(Chr.id, Chr.trade.partner.actor.id);
                    BotTradeCommands.WriteChat(Chr, "这个算了，下次再聊。");
                    Delay(() => { BotTradeCommands.Decline(Chr); state = State.COMPLETED; });
                    return;
                default: throw new InvalidOperationException("Unexpected trade state: " + state);
            }
            CheckTimeout();
        }
        void CheckTimeout()
        { if (BotScheduler.Now() > startedAt + 60000) state = State.TIMED_OUT; }
        bool Sufficient() => parent.tradeWants.VerifyTrade(BotTradeCommands.ReadPartnerMeso(Chr), BotTradeCommands.PartnerItems(Chr));
        bool CorrectItems() => parent.tradeWants.VerifySufficientItems(BotTradeCommands.PartnerItems(Chr));
        bool PostItems()
        {
            var item = parent.tradeInventory.MainItem;
            if (item == null) { BotTradeCommands.WriteChat(Chr, "我现在没什么能出的，抱歉。"); return false; }
            if (item.isEquip) BotTradeCommands.AddEquip(Chr, item, 1);
            else BotTradeCommands.AddItem(Chr, item.itemId, 1, 1);
            BotTradeCommands.WriteChat(Chr, "货在这，你先看。"); return true;
        }
        void StartCallback()
        {
            parent.lastTradedCharacter = Chr.trade.partner.actor;
            Chr.trade.ResultCallback = result => { if (result == TradeResult.SUCCESSFUL) lastResult = result; };
        }
        public string WantsMessage()
        {
            int mesos = parent.tradeWants.mesoWanted;
            var wanted = parent.tradeWants.itemsWanted;
            var text = new StringBuilder("我想要");
            if (mesos > 0)
            { text.Append(FormatPrice(mesos)); if (wanted.Count > 0) text.Append("，还要"); }
            for (int i = 0; i < wanted.Count; i++)
            {
                if (i > 0) text.Append("、");
                if (wanted[i].quantity > 1) text.Append(wanted[i].quantity).Append("个");
                text.Append(parent.ItemName(wanted[i].itemId));
            }
            if (mesos == 0 && wanted.Count == 0) text.Append("先看看货");
            return text.ToString();
        }
        public static string FormatPrice(int price, int decimalPlaces = 1)
        {
            if (price < 1000) return price.ToString(CultureInfo.InvariantCulture);
            string[] suffixes = { "", "k", "m", "b" };
            int index = 0; decimal value = price;
            while (value >= 1000 && index < 3) { value /= 1000; index++; }
            if (decimalPlaces <= 0) return decimal.Floor(value + .5m).ToString(CultureInfo.InvariantCulture) + suffixes[index];
            // Java Formatter 使用 HALF_UP，避免C#默认银行家舍入。
            string text = decimal.Round(value, decimalPlaces, MidpointRounding.AwayFromZero)
                .ToString("F" + decimalPlaces, CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            return text + suffixes[index];
        }
    }
}
