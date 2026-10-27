using System;
using System.Collections.Generic;
using Server.Bot.Types;

namespace Server.Bot.Social
{
    // Dispatcher.java / QueueCleaner.java：共享队列，每2秒最多分派一条primary消息。
    public sealed class BotDispatcher : IDisposable
    {
        readonly BotMessages messages;
        readonly BotOutput output;
        readonly Func<ChatMessage, IEnumerable<BotActor>> charactersOnMap;
        readonly Action<BotStateMachine, BotActor> listOptions;
        BotTasks.Handle dispatchTask, cleanTask;
        public Action<Exception> OnError;

        public BotDispatcher(BotMessages messages, BotOutput output,
            Func<ChatMessage, IEnumerable<BotActor>> charactersOnMap,
            Action<BotStateMachine, BotActor> listOptions)
        {
            this.messages = messages ?? throw new ArgumentNullException(nameof(messages));
            this.output = output ?? throw new ArgumentNullException(nameof(output));
            this.charactersOnMap = charactersOnMap ?? throw new ArgumentNullException(nameof(charactersOnMap));
            this.listOptions = listOptions ?? throw new ArgumentNullException(nameof(listOptions));
        }

        public void Start()
        {
            if (dispatchTask != null && !dispatchTask.Cancelled) return;
            dispatchTask = BotTasks.ScheduleFixedRate(ProcessOne, 0, 2000);
            cleanTask = BotTasks.ScheduleFixedRate(() => messages.Clean(BotScheduler.Now()), 0, 2000);
        }

        public void ProcessOne()
        {
            try
            {
                var message = messages.Poll(QueueType.PRIMARY);
                if (message == null) return;
                foreach (var actor in charactersOnMap(message))
                {
                    // 源端按地图角色遍历顺序匹配，不仅遍历假人；匹配真人名字也消费消息。
                    if (!message.content.Contains(actor.name) || BotStore.IsInvisibleBot(actor.id)) continue;
                    var bot = BotStore.GetBotById(actor.id);
                    if (bot == null) return;
                    if (!bot.running)
                    {
                        bot.interactors.SetRespondant(message.sender);
                        bot.StartScheduledTask(0);
                        if (bot is SocialBot social) social.OnFirstInteraction(message.sender);
                    }
                    else if (bot is SocialBot social)
                    {
                        if (!social.hasActiveRespondant)
                        {
                            social.interactors.SetRespondant(message.sender);
                            social.OnFirstInteraction(message.sender);
                        }
                    }
                    else
                    {
                        bot.interactors.SetInquirer(message.sender);
                        listOptions(bot, message.sender);
                    }
                    return;
                }
                if (BotStore.IsRespondant(message.sender))
                {
                    output.ExpireOptions(message.sender);
                    messages.Add(message);
                }
                else if (BotStore.IsInquirer(message.sender)) messages.Add(message, QueueType.TERTIARY);
            }
            catch (Exception error) { OnError?.Invoke(error); }
        }

        public void Dispose()
        {
            dispatchTask?.Cancel(); cleanTask?.Cancel();
            dispatchTask = null; cleanTask = null;
        }
    }
}
