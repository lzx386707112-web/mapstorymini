// 假人状态机基类。对齐源端 soloMapling.ArtificialPlayer.BotSM：
//   每个 BotType 一个子类，重写 UpdateState() 实现 FSM tick。
//   源端方法：getChr()/getRunning()/setRunning()/startScheduledTask(initialDelay)/stopScheduledTask()/updateState()
//   + 钩子 isAvailableForAmbientActions()/checkPrioritySpeed()/checkIfNotRunningOrPaused()。
// 调度由 BotScheduler.Register 接管（时间轮 + 自适应 throttle），故这里只持有状态与注册入口。
using System;
using Server.Bot.Social;
using Server.Bot.Events;
using Server.Bot.Trade;

namespace Server.Bot
{
    public abstract class BotStateMachine : IEventSubscriber
    {
        public BotCharacter chr;
        public bool running;
        public string botType = "Generic";
        public enum State { IDLE, RUNNING, PAUSE, TRADING, FINISHED }
        public State state { get; protected set; } = State.IDLE;
        public readonly BotTradeHandler tradeHandler;
        public readonly BotTradeInventory tradeInventory = new BotTradeInventory();
        public readonly BotTradeWants tradeWants = new BotTradeWants();
        public BotTradeStateMachine tradeStateMachine { get; private set; }
        public TradeMode tradeMode = TradeMode.NULL;
        public TradeResult? lastTradeResult;
        public BotActor lastTradedCharacter;
        public BotOutput output = BotTypeManager.Output;
        public BotTradeLogic tradeLogic = BotTypeManager.Trades;
        public MarketBotFlavor marketFlavor = BotTypeManager.MarketFlavor;
        public MarketBotDirector marketDirector = BotTypeManager.Market;
        public Func<int, string> ItemName = BotHelpers.ConvertItemIdToName;
        public Func<bool> IsMapObserved, IsCompanion;
        long suspendedUntil;
        public virtual bool UsesSharedMarketTick => false;
        readonly BotEventBuffer eventBuffer = new BotEventBuffer(100);
        EventBus eventBus;
        public bool movementInterrupted;
        public void InterruptMovement() => movementInterrupted = true;
        public void ClearMovementInterrupt() => movementInterrupted = false;
        public void Subscribe(EventBus bus, params EventType[] types)
        {
            if (eventBus != null && !ReferenceEquals(eventBus, bus)) eventBus.UnsubscribeAll(this);
            eventBus = bus;
            foreach (var type in types) bus.Subscribe(type, this);
        }
        public void OnEvent(GameEvent ev) => eventBuffer.Add(ev);
        public bool MatchesFilter(GameEvent ev) => ev.world == chr.world && ev.channel == chr.channel && ev.mapId == chr.mapId;
        public bool HasQueuedEvents => !eventBuffer.IsEmpty;
        public void ProcessQueuedEvents()
        {
            var ev = eventBuffer.Poll();
            if (ev != null) HandleEvent(ev);
        }
        public virtual void HandleEvent(GameEvent ev) { }

        // 对话/交互上下文（阶段E 填充，先留字段）
        public readonly BotInteractors interactors = new BotInteractors();
        public bool hasActiveRespondant => interactors.Respondant != null;
        static readonly Random random = new Random();
        long currentDelay;
        long busyUntilMs;
        long lastNudgeMs;
        public Func<bool> HasPlayersOnMap;
        public bool CheckMainPlayersOnMap() => HasPlayersOnMap?.Invoke() ?? false;
        public bool IsBusy => BotScheduler.Now() < busyUntilMs;
        public void BusyFor(long ms) { busyUntilMs = BotScheduler.Now() + Math.Max(0, ms); }
        public void ClearBusy() { busyUntilMs = 0; }
        public void UpdateScheduleDelay(long ms)
        {
            if (currentDelay == ms) return;
            currentDelay = ms;
            if (UsesSharedMarketTick) return;
            BotScheduler.Reschedule(chr.id, ms);
        }
        public void SetPriorityLow() => UpdateScheduleDelay(10000);
        public void SetPriorityHigh() => UpdateScheduleDelay(2000);
        public void SetPriorityNormal() => UpdateScheduleDelay(2000 + random.Next(4000));
        public void NudgeSoon(long initialDelay)
        {
            if (UsesSharedMarketTick || !running || state == State.TRADING || state == State.FINISHED ||
                !BotScheduler.IsRegistered(chr.id) || BotScheduler.Now() - lastNudgeMs < 1500) return;
            lastNudgeMs = BotScheduler.Now();
            currentDelay = 2000 + random.Next(4000);
            BotScheduler.Nudge(chr.id, initialDelay, currentDelay);
        }

        protected BotStateMachine(BotCharacter c) { chr = c; tradeHandler = new BotTradeHandler(c); }

        // FSM tick —— 子类重写。源端 super.updateState() 先跑基类逻辑。
        public virtual void UpdateState()
        {
            if (BotScheduler.Now() < suspendedUntil) return;
            switch (state)
            {
                case State.IDLE:
                    if (CheckRunningOnline()) state = State.RUNNING;
                    break;
                case State.RUNNING:
                    CheckPrioritySpeed();
                    if (!CheckRunningOnline()) { state = State.FINISHED; break; }
                    if (!IsBusy && chr.trade == null && !chr.moving && (IsMapObserved?.Invoke() ?? false)
                        && !(IsCompanion?.Invoke() ?? false) && (chr.stance == 4 || chr.stance == 5))
                        output.IdleStanding(chr);
                    if (tradeHandler.VerifyPartner())
                    { tradeStateMachine = new BotTradeStateMachine(this, tradeMode); state = State.TRADING; }
                    break;
                case State.PAUSE:
                    if (UsesSharedMarketTick || CheckMainPlayersOnMap()) state = State.RUNNING;
                    break;
                case State.TRADING:
                    if (!tradeHandler.VerifyPartner() && (tradeStateMachine.TradeComplete || !tradeStateMachine.OfferAccepted))
                    {
                        tradeStateMachine = null; tradeLogic.Clear(chr); tradeHandler.Reset();
                        if (UsesSharedMarketTick) BusyFor(2000);
                        else suspendedUntil = BotScheduler.Now() + 2000;
                        state = State.RUNNING;
                    }
                    else tradeStateMachine.Update();
                    break;
                case State.FINISHED:
                    running = false; interactors.ResetRespondants(); StopScheduledTask(); state = State.IDLE;
                    break;
                default: throw new InvalidOperationException("Unexpected bot state: " + state);
            }
        }
        public bool CheckRunningOnline() => running && BotStore.BotLoggedIn(chr.id);
        public void CheckForTrades()
        { if (tradeLogic.CheckQueue(chr)) tradeHandler.confirmedPartner = tradeHandler.RawPartner; }

        public virtual void StartScheduledTask(long initialDelayMs)
        {
            running = true;
            if (UsesSharedMarketTick)
            { BotScheduler.Unregister(chr.id); marketDirector.Register(this, initialDelayMs); return; }
            currentDelay = DefaultPeriodMs();
            BotScheduler.Register(chr.id, Tick, initialDelayMs, currentDelay);
        }

        public virtual void StopScheduledTask()
        {
            running = false;
            eventBus?.UnsubscribeAll(this);
            output.Send(BotOutput.Kind.Chalkboard, chr, "");
            if (UsesSharedMarketTick) marketDirector.Unregister(this);
            BotWander.Stop(chr);
            BotScheduler.Unregister(chr.id);
            interactors.ResetRespondants();
            interactors.ResetInquirers();
        }

        // 每次 tick 的包装：先置 running 守卫，跑 UpdateState，由调度器管理下次触发。
        internal void Tick()
        {
            if (!running || BotScheduler.Now() < suspendedUntil) return;
            UpdateState();
        }

        // 各类型的默认 tick 周期（ms），子类可重写
        protected virtual long DefaultPeriodMs() => 2000 + random.Next(4000);

        // —— 源端钩子 ——
        public virtual bool IsAvailableForAmbientActions() => false;
        public virtual void DisplayCommands(BotActor player)
            => output.Send(BotOutput.Kind.GuideTalk, player, "1. " + chr.name + "\r\n", targetId: player.id);

        public virtual void CheckPrioritySpeed()
        {
            if (CheckMainPlayersOnMap()) SetPriorityNormal(); else SetPriorityLow();
        }

        public bool CheckIfNotRunningOrPaused() => !running || state == State.PAUSE || BotScheduler.Now() < suspendedUntil;

        // 调试（阶段A 占位，后续接 BotDebugHandler）
        public virtual void DebugLog(string msg) { }
    }
}
