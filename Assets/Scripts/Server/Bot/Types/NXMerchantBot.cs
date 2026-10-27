using System;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Bot.Trade;

namespace Server.Bot.Types
{
    public sealed class NXMerchantBot : BotStateMachine
    {
        public enum Step { Setup, Advertise, CheckTrades, DeliverCode, ConvertBack }
        public Step CurrentStep { get; private set; }
        readonly MarketBotAmbient ambient;
        readonly BotMessenger messenger;
        readonly BotNXCodes codes;
        readonly BotDialogue dialogue;
        readonly Random random;
        int cycles, attempts;
        bool delivering;
        BotTasks.Handle pending;
        BotActor recipient;
        static readonly string[] flavor = { "ScamMessages", "BeggingMessages", "RWTMessages", "FunnyMessages" };
        static readonly string[] advertisements = {
            "出1万点券兑换码，五千万金币，交易我！", "出点券码一万点，五千万，别刀太狠。",
            "点券码一万点换五千万，只接受交易。", "正经出点券码，一万点五千万，要的密。",
            "出点券一万点，五千万，先到先得。", "卖点券码，一万点五千万，认真的来。", "有点券码，一万点换五千万，交易窗见。"
        };
        public NXMerchantBot(BotCharacter actor, MarketBotAmbient ambient, BotMessenger messenger,
            BotNXCodes codes, BotDialogue dialogue, Random random = null) : base(actor)
        { this.ambient = ambient; this.messenger = messenger; this.codes = codes; this.dialogue = dialogue; this.random = random ?? new Random(); botType = "MerchantBot"; }
        public override bool UsesSharedMarketTick => true;
        void Advertise()
        { if (marketDirector.TrySpeak(chr)) output.Speak(chr,advertisements[random.Next(advertisements.Length)]); }
        void ConvertBack() => BotTypeManager.ConvertBotType(chr, random.Next(2) == 0 ? BotType.SELLING_MERCHANT_BOT : BotType.BUYING_MERCHANT_BOT);
        void Later(long milliseconds, Action action)
        {
            pending = BotTasks.Schedule(() => {
                if (!running || !chr.online || BotStore.GetBotById(chr.id) != this) return;
                action();
            },milliseconds);
        }
        void FinishDelivery()
        { lastTradeResult = null; lastTradedCharacter = null; recipient = null; delivering = false; CurrentStep = Step.ConvertBack; }
        void BeginDelivery()
        {
            if (lastTradeResult != TradeResult.SUCCESSFUL) { ConvertBack(); return; }
            delivering = true; recipient = lastTradedCharacter; attempts = 0;
            output.Speak(chr,"我密你了。"); messenger.Invite(chr,recipient); CheckInvitation();
        }
        void CheckInvitation()
        {
            attempts++;
            if (messenger.Accepted(chr,recipient))
            {
                string code = codes.Generate(); codes.Create(code);
                messenger.Send(chr,chr.name + "1",true);
                Later(3000, () => {
                    messenger.Send(chr,"一万点的兑换码给你，记下来，不要带横杠。");
                    messenger.Send(chr,chr.name + "1",true);
                    Later(7000, () => {
                        messenger.Send(chr,code); messenger.Send(chr,chr.name + "1",true);
                        Later(2000, () => { messenger.Send(chr,"用得开心！"); Later(2000, () => { messenger.Leave(chr.id); FinishDelivery(); }); });
                    });
                });
            }
            else if (attempts < 15) Later(2000,CheckInvitation);
            else { output.Speak(chr,"你没接聊天邀请，那就算了。"); FinishDelivery(); }
        }
        public override void UpdateState()
        {
            if (delivering) return;
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || state == State.TRADING) return;
            ambient.Act(this);
            if (chr.chair > 0 || IsBusy) { if (chr.chair > 0) Advertise(); return; }
            if (lastTradeResult == TradeResult.SUCCESSFUL && CurrentStep != Step.DeliverCode && CurrentStep != Step.ConvertBack) CurrentStep = Step.DeliverCode;
            switch (CurrentStep)
            {
                case Step.Setup:
                    var filler = BotLogic.GenerateCleanItemWithQty(4031865,100); filler.position = 1;
                    tradeInventory.SetMain(filler); tradeWants.Reset(); tradeWants.mesoWanted = 50000000;
                    tradeMode = TradeMode.SELLING; lastTradeResult = null; lastTradedCharacter = null;
                    CurrentStep = Step.Advertise; break;
                case Step.Advertise:
                    if (random.Next(25) == 0)
                    {
                        var line = dialogue.Get("MerchantBotDialogue.yaml","MerchantBot",flavor[random.Next(flavor.Length)]);
                        dialogue.Run(chr,line,output,true);
                        if (line != null) BusyFor(line.durationMs);
                    }
                    else Advertise();
                    CurrentStep = Step.CheckTrades; break;
                case Step.CheckTrades:
                    CheckForTrades(); cycles++;
                    CurrentStep = lastTradeResult == TradeResult.SUCCESSFUL ? Step.DeliverCode : cycles >= 15 ? Step.ConvertBack : Step.Advertise; break;
                case Step.DeliverCode: BeginDelivery(); break;
                case Step.ConvertBack: ConvertBack(); break;
            }
        }
        public override void DisplayCommands(BotActor player) => output.ShowOptions(player,new[] { chr.name });
        public override void StopScheduledTask()
        { pending?.Cancel(); delivering = false; messenger.Leave(chr.id); ambient.Forget(chr); BotMovementService.Disable(chr); base.StopScheduledTask(); }
    }
}
