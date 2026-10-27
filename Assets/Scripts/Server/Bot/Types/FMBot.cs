using System;
using Server.Bot.Environment;
using Server.Bot.Social;

namespace Server.Bot.Types
{
    public sealed class FMBot : BotStateMachine
    {
        public enum MarketState { Reset, Ambient, NavToRoom, ProcessRoom, BrowsingRoom, Action, EnterShop, ProcessShop, ExitShop, ExitRoom }
        public MarketState marketState { get; private set; }
        public int Room { get; private set; } = 1;
        readonly MarketBotAmbient ambient;
        readonly Random random;
        public FMBot(BotCharacter actor, MarketBotAmbient ambient, Random random = null) : base(actor)
        { this.ambient = ambient; this.random = random ?? new Random(); botType = "FMBot"; }
        public override bool UsesSharedMarketTick => true;
        public override bool IsAvailableForAmbientActions() => state != State.TRADING;
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || state == State.TRADING) return;
            ambient.Act(this);
            if (chr.chair > 0 || IsBusy) return;
            switch (marketState)
            {
                case MarketState.Reset:
                    Room = chr.mapId == 910000000 ? random.Next(1, 13) : chr.mapId - 910000000;
                    marketState = MarketState.Ambient; break;
                // 原updateState已把所有旧浏览/购物分支改为回到AMBIENT；不重新启用死分支。
                default: marketState = MarketState.Ambient; break;
            }
        }
        public override void DisplayCommands(BotActor player) => output.ShowOptions(player, new[] { chr.name });
        public static bool ShouldPurchaseItem(int? askingPrice, int? marketValue, Random random = null)
        {
            if (!askingPrice.HasValue || !marketValue.HasValue || askingPrice <= 0 || marketValue <= 0) return false;
            double ratio = (double)askingPrice.Value / marketValue.Value;
            double[] thresholds = { .7, .85, .95, 1, 1.05, 1.1, 1.15, 1.2 };
            double[] probabilities = { 1, .95, .85, .75, .55, .35, .15, .05 };
            if (ratio > 1.2) return false;
            for (int i = 0; i < thresholds.Length; i++) if (ratio <= thresholds[i]) return (random ?? new Random()).NextDouble() < probabilities[i];
            return false;
        }
        public override void StopScheduledTask()
        { ambient.Forget(chr); BotMovementService.Disable(chr); base.StopScheduledTask(); }
    }
}
