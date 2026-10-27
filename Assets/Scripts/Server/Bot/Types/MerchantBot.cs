using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Bot.Trade;

namespace Server.Bot.Types
{
    // SellingMerchantBot与BuyingMerchantBot共用相同五步轮；保留各自备货和报价规则。
    public sealed class MerchantBot : BotStateMachine
    {
        public enum Step { Reset, SelectItem, Advertise, CheckTrades, IdleActions }
        public Step CurrentStep { get; private set; }
        readonly bool buying;
        readonly MarketItemPool pool;
        readonly MarketEconomy economy;
        readonly MarketBotAmbient ambient;
        readonly Random random;
        List<MarketItem> items;
        int itemIndex;
        public MarketItem CurrentItem => items != null && itemIndex < items.Count ? items[itemIndex] : null;
        public MerchantBot(BotCharacter actor, bool buying, MarketItemPool pool, MarketEconomy economy,
            MarketBotAmbient ambient, Random random = null) : base(actor)
        { this.buying = buying; this.pool = pool; this.economy = economy; this.ambient = ambient; this.random = random ?? new Random(); botType = "MerchantBot"; }
        public override bool UsesSharedMarketTick => true;
        void LoadItems()
        {
            int choice = random.Next(buying ? 2 : 4);
            items = choice == 0 ? pool.Scrolls("A") : choice == 1 ? pool.Scrolls("A",true) : choice == 2 ? pool.Stars("A") : pool.Potions("S");
            if (!buying && items.Count == 0) items = pool.Potions("S");
            if (!buying && items.Count == 0) items = new List<MarketItem> { new MarketItem(2000002,1,100), new MarketItem(2000001,1,100) };
        }
        void SelectItem()
        {
            if (items == null || items.Count == 0) LoadItems();
            if (!buying && items.Count == 0) return;
            itemIndex++;
            if (itemIndex >= items.Count) { itemIndex = 0; LoadItems(); }
            var item = CurrentItem; if (item == null) return;
            tradeWants.Reset();
            if (buying)
            {
                tradeMode = TradeMode.BUYING;
                tradeWants.AddItemWanted(item.itemId);
                tradeWants.mesoOffering = economy.Price(MarketEconomy.JavaInt(item.price * (.80 + random.NextDouble() * .10)));
                tradeWants.mesoWanted = 0;
            }
            else
            {
                var sale = BotLogic.GenerateCleanItemWithQty(item.itemId,1); sale.position = 1;
                tradeInventory.SetMain(sale);
                int raw = pool.Price(item.itemId) ?? throw new InvalidOperationException("Original sale item has no market price: " + item.itemId);
                tradeWants.mesoWanted = economy.Price(MarketEconomy.JavaInt(raw * .9));
                tradeMode = TradeMode.SELLING;
            }
            lastTradeResult = null; lastTradedCharacter = null;
        }
        void Advertise()
        {
            if (CurrentItem == null) return;
            string name = ItemName(CurrentItem.itemId);
            if (name == null || random.Next(100) >= 72 || !marketDirector.TrySpeak(chr)) return;
            output.Speak(chr, buying ? marketFlavor.Buying(name,MarketEconomy.Shorthand(tradeWants.mesoOffering)) : marketFlavor.Selling(name));
        }
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || state == State.TRADING) return;
            ambient.Act(this);
            if (chr.chair > 0 || IsBusy || BotRecordedActions.IsMoving(chr))
            { if (chr.chair > 0 && BotConfig.MarketHawkEnabled(BotClient.GetBotClient()?.db)) Advertise(); return; }
            switch (CurrentStep)
            {
                case Step.Reset: itemIndex = 0; LoadItems(); CurrentStep = Step.SelectItem; break;
                case Step.SelectItem: SelectItem(); CurrentStep = Step.Advertise; break;
                case Step.Advertise:
                    if (BotConfig.MarketHawkEnabled(BotClient.GetBotClient()?.db)) Advertise();
                    CurrentStep = Step.CheckTrades; BusyFor(400); break;
                case Step.CheckTrades: CheckForTrades(); CurrentStep = Step.IdleActions; break;
                case Step.IdleActions:
                    if (random.Next(100) == 0) { BotTypeManager.ConvertBotType(chr,BotType.NX_MERCHANT_BOT); return; }
                    CurrentStep = Step.SelectItem; break;
            }
        }
        public override void DisplayCommands(BotActor player) => output.ShowOptions(player,new[] { chr.name });
        public override void StopScheduledTask()
        { ambient.Forget(chr); BotMovementService.Disable(chr); base.StopScheduledTask(); }
    }
}
