// BotTypes/GameZoneHostBot.java（288 行）对齐移植：游戏区招待员。
// 流程：空闲（偶尔氛围台词 + 把询问者提升为对话对象）→ 问要不要喝的 → 等 Yes/No → 选饮料
//      → 等报名字 → 扔出饮料 → 送别 → 重置。
// 另外订阅 MAP_ENTERED：有人进图就被动打个招呼（这条与状态机解耦）。
//
// 与源端的差异（全部源于「没有独立线程 / 没有阻塞 sleep」与能力边界）：
//   1. 源端 executeBotDialogue 是阻塞的（说完才往下走）；本端口 BotDialogue.Run 是分帧的，
//      因此每个需要「说完再迁移」的状态都挂在 Run 的完成回调里（≡ TutorialBot 的 Say/scripted）。
//   2. 源端 handleDrinkPickResponse 里 sleepAmountSeconds(2000)：这里换成 BotTasks 延时 +
//      scripted 闸门，语义等价（这 2 秒内 tick 不推进内部状态）。
//   3. 源端 waitForResponse 靠 System.currentTimeMillis() 与 endTime 比较；这里同样用
//      BotScheduler.Now() 注入时钟（≡ TutorialBot.WaitForResponse）。
//   4. 源端 handleEvent 的 default 分支不处理（这里同样忽略未知事件类型）。
//   5. 源端 dialoguePath = "GameZoneHostBotDialogue.yaml"，对话节点名照搬
//      （DrinkOffer / DrinkSelection / ServeDrink / DeclineResponse / Farewell / Flavor / PassiveGreeting）。
//      节点缺失时 Say 直接放行到下一步，不会卡住状态机。
//   6. 源端 serveDrink 用 DropCommands.botThrowItem 把饮料扔到玩家脚下；本端口同款 API 可用，无需降级。
using System;
using System.Collections.Generic;
using Server.Bot.Events;
using Server.Bot.Social;

namespace Server.Bot.Types
{
    public sealed class GameZoneHostBot : BotStateMachine
    {
        // ≡ GameZoneHostBot.HostBotState（8 态）
        public enum HostBotState
        {
            Reset, IdleWaiting, DrinkOffer, WaitDrinkResponse, DrinkSelection, WaitDrinkPick, ServeDrink, Farewell
        }

        // ≡ 源端 Drink item IDs
        const int DrinkCoke = 2020031;
        const int DrinkWater = 2022000;
        const int DrinkElixir = 2000012;

        static readonly string[] DrinkNames = { "Coke", "Water", "Elixir" };
        static readonly int[] DrinkIds = { DrinkCoke, DrinkWater, DrinkElixir };

        const string DialoguePack = "GameZoneHostBotDialogue.yaml";
        const string PlayerNameToken = "%PLAYER_NAME%";
        const long DrinkOfferTimeoutMs = 20_000;
        const long DrinkPickTimeoutMs = 30_000;
        const int IdleFlavorChancePer100 = 6;

        readonly Random random;
        readonly BotDialogue dialogue;
        readonly BotMessages messages;

        public HostBotState hostBotState { get; private set; } = HostBotState.Reset;

        bool? drinkAccepted;
        int selectedDrink;
        List<string> hint;
        long startTime, endTime;
        bool scripted;
        int generation;
        BotTasks.Handle sequenceTask;

        public GameZoneHostBot(BotCharacter c, EventBus events = null, BotDialogue dialogue = null,
            BotMessages messages = null, Random random = null) : base(c)
        {
            this.dialogue = dialogue ?? BotTypeManager.Dialogue;
            this.messages = messages ?? BotTypeManager.Messages;
            this.random = random ?? new Random();
            hint = new List<string> { c.name };
            botType = "GameZoneHostBot";
            // ≡ EventBus.getInstance().subscribe(EventType.MAP_ENTERED, this)
            Subscribe(events ?? BotTypeManager.Events, EventType.MAP_ENTERED);
        }

        // =========================================================================
        // MAIN STATE MACHINE
        // =========================================================================
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused()) return;

            // ≡ 源端 updateState 开头就 processQueuedEvents()
            ProcessQueuedEvents();
            DebugLog(chr.name + " HostBotState: " + hostBotState);
            if (scripted) return;

            switch (hostBotState)
            {
                case HostBotState.Reset:
                    ResetHostBotState();
                    SetState(HostBotState.IdleWaiting);
                    break;
                case HostBotState.IdleWaiting:
                    IdleFlavorText();
                    CheckForInquirer();
                    break;
                case HostBotState.DrinkOffer:
                    OfferDrink();
                    break;
                case HostBotState.WaitDrinkResponse:
                    WaitForResponse();
                    if (drinkAccepted == null) break;
                    if (drinkAccepted.Value) SetState(HostBotState.DrinkSelection);
                    else Say("DeclineResponse", false, () => SetState(HostBotState.Farewell));
                    break;
                case HostBotState.DrinkSelection:
                    PresentDrinkOptions();
                    break;
                case HostBotState.WaitDrinkPick:
                    WaitForResponse();
                    if (selectedDrink == 0) break;
                    SetState(HostBotState.ServeDrink);
                    break;
                case HostBotState.ServeDrink:
                    ServeDrink();
                    break;
                case HostBotState.Farewell:
                    Farewell();
                    break;
                default:
                    DebugLog("Unexpected GameZoneHostBot state: " + hostBotState);
                    state = State.FINISHED;
                    ResetHostBotState();
                    break;
            }
        }

        void SetState(HostBotState next) => hostBotState = next;

        bool Alive() => running && chr.online && ReferenceEquals(BotStore.GetBotById(chr.id), this);

        // ≡ 源端 sleepAmountSeconds 的可取消等价物
        void After(long ms, Action next)
        {
            scripted = true;
            sequenceTask?.Cancel();
            sequenceTask = BotTasks.Schedule(() =>
            {
                if (!Alive()) return;
                try { next(); }
                catch (Exception e) { DebugLog(chr.name + " sequence error: " + e.Message); scripted = false; }
            }, ms);
        }

        // ≡ getDialogueHandler().executeBotDialogue*：说完（含台词间隔与结束 wait）才回调 then
        void Say(string node, bool flavor = false, Action then = null)
        {
            BotDialogue.Dialogue entry;
            try { entry = dialogue.Get(DialoguePack, botType, node); }
            catch (Exception e) { DebugLog("dialogue read failed " + node + ": " + e.Message); entry = null; }
            if (entry == null || entry.text == null || entry.text.Count == 0) { then?.Invoke(); return; }
            scripted = true;
            int token = generation;
            dialogue.Run(chr, entry, output, flavor, () => { scripted = false; then?.Invoke(); }, () => Alive() && token == generation);
        }

        void ResetHostBotState()
        {
            SetState(HostBotState.Reset);
            drinkAccepted = null;
            selectedDrink = 0;
            hint = new List<string> { chr.name };
            startTime = 0;
            endTime = 0;
            scripted = false;
            sequenceTask?.Cancel();
            sequenceTask = null;
        }

        // ——— IDLE_WAITING ———
        void IdleFlavorText()
        {
            // ≡ random.nextInt(100) < 6
            if (random.Next(100) >= IdleFlavorChancePer100) return;
            Say("Flavor", true);
        }

        // ≡ checkForInquirer：把排队询问的玩家提升为对话对象
        void CheckForInquirer()
        {
            if (interactors.inquirers.Count == 0) return;
            var inquirer = interactors.inquirers[0];
            interactors.RemoveInquirer(inquirer);
            interactors.SetRespondant(inquirer);
            SetState(HostBotState.DrinkOffer);
        }

        // ——— DRINK_OFFER ———
        void OfferDrink()
        {
            Say("DrinkOffer", false, () =>
            {
                hint = new List<string> { "Yes", "No" };
                DisplayCommands(interactors.Respondant);
                StartTimer(DrinkOfferTimeoutMs);
                SetState(HostBotState.WaitDrinkResponse);
            });
        }

        // ——— DRINK_SELECTION ———
        void PresentDrinkOptions()
        {
            Say("DrinkSelection", false, () =>
            {
                hint = new List<string>(DrinkNames);
                DisplayCommands(interactors.Respondant);
                StartTimer(DrinkPickTimeoutMs);
                SetState(HostBotState.WaitDrinkPick);
            });
        }

        // ——— SERVE_DRINK ———
        void ServeDrink()
        {
            Say("ServeDrink", false, () =>
            {
                var player = interactors.Respondant;
                if (player != null)
                {
                    // ≡ DropCommands.botThrowItem(getChr(), selectedDrink, getRespondant().getPosition())
                    DropCommands.BotThrowItem(chr, selectedDrink, (int)player.x, (int)player.y, out _);
                }
                SetState(HostBotState.Farewell);
            });
        }

        // ——— FAREWELL ———
        void Farewell()
        {
            Say("Farewell", true, () =>
            {
                interactors.ResetRespondants();
                SetState(HostBotState.Reset);
            });
        }

        // ——— TIMER / RESPONSE ———
        void StartTimer(long durationMs)
        {
            startTime = BotScheduler.Now();
            endTime = startTime + durationMs;
        }

        void WaitForResponse()
        {
            if (BotScheduler.Now() < endTime) { ProcessMessages(); return; }
            ExpireOptions();
            output.Speak(chr, "Talk to me again if you're ready.");
            state = State.FINISHED;
            ResetHostBotState();
        }

        public override void DisplayCommands(BotActor player)
        {
            if (player == null || hint == null) return;
            output.ShowOptions(player, hint);
        }

        // =========================================================================
        // EVENTS
        // =========================================================================
        public override void HandleEvent(GameEvent ev)
        {
            if (ev == null || ev.type != EventType.MAP_ENTERED) return;
            // ≡ BotDialogueHandler.getRandomDialogueLine(this, "PassiveGreeting") + %PLAYER_NAME% 替换
            string line;
            try { line = dialogue.RandomLine(DialoguePack, botType, "PassiveGreeting"); }
            catch (Exception) { line = null; }
            if (line != null) line = line.Replace(PlayerNameToken, ev.playerName);
            if (line != null) output.Send(BotOutput.Kind.Bubble, chr, line);
            output.Emote(chr, 2);
        }

        // =========================================================================
        // MESSAGES
        // =========================================================================
        // ≡ 源端 processMessages()：共享队列取一条、超时 1 秒返回（≡ TutorialBot 的写法）
        void ProcessMessages()
        {
            Consume(QueueType.SECONDARY);
            Consume(QueueType.PRIMARY);
        }

        void Consume(QueueType type)
        {
            var message = messages.Poll(type);
            if (message == null) return;
            if (!interactors.IsMessageFromRespondant(message)) { messages.Add(message, type); return; }
            HandleMessage(message);
        }

        void HandleMessage(ChatMessage message)
        {
            string content = message.content == null ? "" : message.content.ToLowerInvariant();
            if (hostBotState == HostBotState.WaitDrinkResponse) HandleDrinkOfferResponse(content);
            else if (hostBotState == HostBotState.WaitDrinkPick) HandleDrinkPickResponse(content);
        }

        void HandleDrinkOfferResponse(string content)
        {
            if (content.Contains("yes")) drinkAccepted = true;
            else if (content.Contains("no")) drinkAccepted = false;
        }

        void HandleDrinkPickResponse(string content)
        {
            // 忽略上一轮「Yes/No」的残留消息
            if (content.Contains("yes") || content.Contains("no")) return;
            for (int i = 0; i < DrinkNames.Length; i++)
            {
                if (!content.Contains(DrinkNames[i].ToLowerInvariant())) continue;
                output.Speak(chr, "One " + DrinkNames[i] + ", coming right up!");
                int drinkId = DrinkIds[i];
                // ≡ BotHelpers.sleepAmountSeconds(2000)：先报单再上饮料
                After(2000, () => { selectedDrink = drinkId; scripted = false; });
                return;
            }
            output.Speak(chr, "Hmm, I don't have that. Try again!");
            output.Emote(chr, 6);
        }

        void ExpireOptions()
        {
            var player = interactors.Respondant;
            if (player != null) output.ExpireOptions(player);
        }

        public override void StopScheduledTask()
        {
            generation++;
            sequenceTask?.Cancel();
            sequenceTask = null;
            scripted = false;
            base.StopScheduledTask();
        }
    }
}
