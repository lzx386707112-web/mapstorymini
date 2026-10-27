using System;
using System.Collections.Generic;
using Server.Bot.Social;
using Server.Bot.Trade;

namespace Server.Bot.Types
{
    // ScrollingBot.java: scrolling is presentation of an already-generated equipment outcome.
    public sealed class ScrollingBot : BotStateMachine
    {
        public enum Step { Reset, SetScrollVars, ScrollingItems, AdvertiseSales, WaitingAfterAd }
        public Step CurrentStep { get; private set; }
        readonly Func<List<MarketEquipment>> generate;
        readonly MarketScrollPricing pricing;
        readonly MarketEconomy economy;
        readonly BotDialogue dialogue;
        readonly Random random;
        List<MarketEquipment> items;
        bool[] results=Array.Empty<bool>();
        int position;
        long waitUntil;
        bool awaitingTradeCheck;
        public ScrollingBot(BotCharacter actor,Func<List<MarketEquipment>> generate,MarketScrollPricing pricing,
            MarketEconomy economy,BotDialogue dialogue,Random random=null) : base(actor)
        { this.generate=generate; this.pricing=pricing; this.economy=economy; this.dialogue=dialogue; this.random=random ?? new Random(); botType="ScrollingBot"; }
        public static bool[] ExactScrollList(int successes,int slots,Random random)
        {
            var list=new bool[slots];
            for (int i=0;i<successes;i++) list[i]=true;
            for (int i=slots-1;i>0;i--)
            { int j=random.Next(i+1); bool value=list[i]; list[i]=list[j]; list[j]=value; }
            return list;
        }
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || state==State.TRADING || IsBusy) return;
            // Original blocking sleep returns before checkForTrades. Resume at that point.
            if (awaitingTradeCheck) { awaitingTradeCheck=false; CheckForTrades(); return; }
            switch (CurrentStep)
            {
                case Step.Reset: CurrentStep=Step.SetScrollVars; break;
                case Step.SetScrollVars:
                    items=generate();
                    // A missing resource cannot block Unity's main thread in the original retry loop.
                    if (items.Count==0) return;
                    var equipment=items[0].Item;
                    int successes=BotItemCatalog.Int(equipment.equipmentData,"level");
                    int slots=BotItemCatalog.Shared.CleanEquip(equipment.itemId).upgradeslots;
                    results=ExactScrollList(successes,slots,random); position=0;
                    CurrentStep=Step.ScrollingItems; break;
                case Step.ScrollingItems:
                    if (position>=results.Length) { CheckForTrades(); CurrentStep=Step.AdvertiseSales; break; }
                    bool success=results[position++];
                    // Source failure sends CURSE, not ordinary FAIL.
                    output.Send(BotOutput.Kind.Scroll,chr,value:success ? 1 : 2);
                    output.Emote(chr,success ? 2 : 4);
                    long flavorWait=0;
                    if (random.NextDouble()<.20)
                    {
                        var line=dialogue.Get("ScrollingBotDialogue.yaml",botType,success ? "ScrollSuccess" : "ScrollFail");
                        dialogue.Run(chr,line,output,true); flavorWait=line?.durationMs ?? 0;
                    }
                    BusyFor(flavorWait+10000+random.Next(5001)); awaitingTradeCheck=true; break;
                case Step.AdvertiseSales:
                    if (random.NextDouble()<.10)
                    {
                        var sale=items[0].Item;
                        tradeInventory.SetMain(sale); tradeWants.Reset();
                        tradeWants.mesoWanted=economy.Price(pricing.EquipValue(sale));
                        string line=dialogue.RandomLine("ScrollingBotDialogue.yaml",botType,"AdvertiseSale");
                        output.Send(BotOutput.Kind.ItemMegaphone,chr,line?.Replace("%PRICE%",MarketEconomy.Shorthand(tradeWants.mesoWanted)),item:sale);
                        tradeMode=TradeMode.SELLING;
                    }
                    waitUntil=BotScheduler.Now()+300000+random.Next(180001); CurrentStep=Step.WaitingAfterAd; break;
                case Step.WaitingAfterAd:
                    CheckForTrades(); if (BotScheduler.Now()>=waitUntil) CurrentStep=Step.SetScrollVars; break;
            }
        }
        public override void DisplayCommands(BotActor player) => output.ShowOptions(player,new[] { chr.name });
    }
}
