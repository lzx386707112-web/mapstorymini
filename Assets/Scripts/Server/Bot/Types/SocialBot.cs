using System;
using System.Collections.Generic;
using Server.Bot.Social;

namespace Server.Bot.Types
{
    // BotTypes/SocialBot.java；sleep调用翻译成可取消的主线程时序链。
    public sealed class SocialBot : BotStateMachine
    {
        public enum Variant { SINGLE_RESPONSE, INTERACTIVE }
        enum InteractionLevel { NORMAL, REDUCED, NONVERBAL, IGNORE }
        enum SocialState { IDLE_AMBIENT, GREETING, AWAITING_CHOICE, RESPONDING }
        sealed class Tracker
        {
            public int count;
            public long last = BotScheduler.Now();
            public bool Expired => BotScheduler.Now() - last > 300000;
            public void Increment() { count++; last = BotScheduler.Now(); }
            public InteractionLevel Level
            {
                get
                {
                    if (Expired) { count = 0; last = BotScheduler.Now(); return InteractionLevel.NORMAL; }
                    return count <= 3 ? InteractionLevel.NORMAL : count <= 4 ? InteractionLevel.REDUCED
                        : count == 5 ? InteractionLevel.NONVERBAL : InteractionLevel.IGNORE;
                }
            }
        }
        static readonly string[] options = { "最近在忙什么？", "有什么新鲜事？", "听到什么传闻？", "先聊到这里" };
        readonly Random random;
        readonly BotDialogue dialogue;
        readonly BotMessages messages;
        readonly Func<int> randomChair;
        readonly Dictionary<int, Tracker> trackers = new Dictionary<int, Tracker>();
        public readonly Variant variant;
        SocialState socialState;
        long lastMessageTime, lastCleanupTime;
        bool wasSitting;
        int originalChair;
        int conversationVersion;

        public SocialBot(BotCharacter chr, BotDialogue dialogue, BotOutput output, BotMessages messages, Func<int> randomChair, Random random = null) : base(chr)
        {
            this.dialogue = dialogue; this.output = output; this.messages = messages; this.randomChair = randomChair;
            this.random = random ?? new Random();
            variant = this.random.NextDouble() < .30 ? Variant.INTERACTIVE : Variant.SINGLE_RESPONSE;
            lastCleanupTime = BotScheduler.Now(); botType = "SocialBot";
        }
        public override bool IsAvailableForAmbientActions() => !hasActiveRespondant;
        public override void CheckPrioritySpeed()
        {
            if (hasActiveRespondant) SetPriorityHigh();
            else if (CheckMainPlayersOnMap()) SetPriorityNormal();
            else UpdateScheduleDelay(30000);
        }
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused()) return;
            CheckPrioritySpeed();
            if (hasActiveRespondant)
            {
                if (lastMessageTime != 0 && BotScheduler.Now() - lastMessageTime > 35000)
                { SpeakCategory("Timeout"); ResetConversation(); }
                // 源端timeout后仍执行processMessages；无respondant时消费后忽略。
                var message = messages.Poll();
                if (message != null && !BotHelpers.IsBot(message.sender) && interactors.Respondant != null
                    && message.sender.id == interactors.Respondant.id)
                { lastMessageTime = BotScheduler.Now(); HandleMessage(message); }
            }
            if (BotScheduler.Now() - lastCleanupTime >= 120000)
            {
                lastCleanupTime = BotScheduler.Now();
                var expired = new List<int>();
                foreach (var p in trackers) if (p.Value.Expired) expired.Add(p.Key);
                foreach (int id in expired) trackers.Remove(id);
            }
        }
        Tracker GetTracker(int id)
        {
            if (!trackers.TryGetValue(id, out var tracker)) trackers[id] = tracker = new Tracker();
            return tracker;
        }
        BotTiming.Chain Sequence()
        {
            int version = conversationVersion;
            return BotTiming.CreateChain().StopUnless(() => running && version == conversationVersion && BotRegistry.Contains(chr.id));
        }
        public void OnFirstInteraction(BotActor player)
        {
            lastMessageTime = BotScheduler.Now();
            var tracker = GetTracker(player.id);
            var level = tracker.Level;
            if (level == InteractionLevel.IGNORE) { BusyHint(player); ResetConversation(); return; }
            wasSitting = chr.chair > 0;
            var chain = Sequence();
            if (wasSitting)
            { originalChair = chr.chair; chain.Run(() => output.CancelChair(chr)).Pause(600); }
            chain.Run(() => Face(player)).Pause(2000 + random.Next(2000)).Run(() =>
            {
                switch (level)
                {
                    case InteractionLevel.NORMAL:
                        if (variant == Variant.SINGLE_RESPONSE)
                        {
                            string category = random.NextDouble() < .01 ? "Rare" : "SingleResponse";
                            var reply = Sequence();
                            string line = Line(category);
                            if (line != null)
                                reply.Run(() => output.Speak(chr, line)).Pause(500 + random.Next(500))
                                    .Run(() => { int emote = Emote(category); if (emote > 0) output.Emote(chr, emote); });
                            reply.Run(() => { tracker.Increment(); Face(player); })
                                .Run(() => Resit(ResetConversation)).Start();
                        }
                        else ReplyWithEmote("Greeting", () => {
                            output.ShowOptions(player, options); socialState = SocialState.GREETING;
                            tracker.Increment(); socialState = SocialState.AWAITING_CHOICE;
                        });
                        break;
                    case InteractionLevel.REDUCED:
                        SpeakCategory("Reduced"); tracker.Increment(); ResetConversation(); break;
                    case InteractionLevel.NONVERBAL:
                        SpeakCategory("Nonverbal"); tracker.Increment(); ResetConversation(); break;
                }
            }).Start();
        }
        void HandleMessage(ChatMessage message)
        {
            var tracker = GetTracker(message.sender.id);
            switch (tracker.Level)
            {
                case InteractionLevel.IGNORE: BusyHint(message.sender); ResetConversation(); return;
                case InteractionLevel.NONVERBAL: SpeakCategory("Nonverbal"); tracker.Increment(); ResetConversation(); return;
                case InteractionLevel.REDUCED: SpeakCategory("Reduced"); tracker.Increment(); ResetConversation(); return;
            }
            if (socialState != SocialState.AWAITING_CHOICE) return;
            string text = message.content.Trim().ToLowerInvariant(), category;
            if (text == "1" || text.Contains("忙什么")) category = "WhatsUp";
            else if (text == "2" || text.Contains("新鲜事")) category = "Interesting";
            else if (text == "3" || text.Contains("传闻")) category = "Rumors";
            else if (text == "4" || text.Contains("先聊到这里") || text.Contains("再见"))
            {
                tracker.Increment();
                ReplyWithEmote("Goodbye", () => Resit(ResetConversation)); return;
            }
            else category = "WhatsUp";
            if (random.NextDouble() < .01) category = "Rare";
            Sequence().Pause(2000 + random.Next(2000)).Run(() => {
                Face(message.sender);
                ReplyWithEmote(category, () => output.ShowOptions(message.sender, options));
            }).Start();
            tracker.Increment();
        }
        void ReplyWithEmote(string category, Action then)
        {
            string line = Line(category);
            if (line == null) { then(); return; }
            output.Speak(chr, line);
            int emote = Emote(category);
            if (emote > 0) Sequence().Pause(400).Run(() => output.Emote(chr, emote)).Run(then).Start();
            else then();
        }
        void Resit(Action then)
        {
            bool sit = wasSitting || random.NextDouble() < .40;
            int chair = originalChair;
            if (sit)
                Sequence().Pause(1000 + random.Next(1500)).Run(() => {
                    output.Sit(chr, chair > 0 ? chair : randomChair());
                    wasSitting = false; originalChair = 0; then();
                }).Start();
            else { wasSitting = false; originalChair = 0; then(); }
        }
        void BusyHint(BotActor player)
        {
            output.Send(BotOutput.Kind.YellowMessage, player, "对方好像正在忙...", targetId: player.id);
            output.Send(BotOutput.Kind.Hint, player, "对方好像正在忙...", targetId: player.id, width: 150, duration: 5);
            output.Send(BotOutput.Kind.EnableActions, player, targetId: player.id);
            BotTasks.Schedule(() => output.ExpireOptions(player), 5000);
        }
        void ResetConversation()
        {
            if (interactors.Respondant != null) output.ExpireOptions(interactors.Respondant);
            interactors.ResetRespondants(); socialState = SocialState.IDLE_AMBIENT; lastMessageTime = 0; conversationVersion++;
        }
        void Face(BotActor player) { chr.facing = player.x >= chr.x ? 1 : -1; }
        string Line(string category)
        { try { return dialogue.RandomLine("SocialBotDialogue.yaml", "SocialBot", category); } catch { return null; } }
        int Emote(string category)
        { try { return dialogue.RandomEmote("SocialBotDialogue.yaml", "SocialBot", category); } catch { return -1; } }
        void SpeakCategory(string category) { string line = Line(category); if (line != null) output.Speak(chr, line); }
        public override void StopScheduledTask() { conversationVersion++; base.StopScheduledTask(); }
    }
}
