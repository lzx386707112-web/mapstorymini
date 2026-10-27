using System.Collections.Generic;
using System.Text;

namespace Server.Bot.Social
{
    // 源端PacketCreator广播的纯数据适配。前端只消费事件，不在AI层绘制UI。
    public sealed class BotOutput
    {
        public enum Kind { Chat, Bubble, Emote, Hint, EnableActions, Chair, Skill, Chalkboard, Guide, GuideTalk, YellowMessage, Movement, Appearance, Attack }
        public sealed class Message
        {
            public Kind kind;
            public int actorId, mapId, targetId, value, width, duration;
            public string text;
            public float x, y;
        }
        readonly Queue<Message> messages = new Queue<Message>();
        public void Send(Kind kind, BotActor actor, string text = null, int value = 0, int targetId = 0, int width = 0, int duration = 0)
            => messages.Enqueue(new Message { kind = kind, actorId = actor.id, mapId = actor.mapId, text = text,
                value = value, targetId = targetId, width = width, duration = duration, x = actor.x, y = actor.y });
        public void IdleStanding(BotCharacter bot) => Send(Kind.Movement, bot, value: bot.stance % 2 == 0 ? 4 : 5, duration: 200);
        public Message Poll() => messages.Count == 0 ? null : messages.Dequeue();
        public int Count => messages.Count;
        public void Clear() => messages.Clear();
        // SocialCommands.botChatTypingStyle 默认为false，通常只产生完整聊天。
        public void Speak(BotCharacter bot, string text) => Send(Kind.Chat, bot, text);
        public void Emote(BotCharacter bot, int emote) => Send(Kind.Emote, bot, value: emote);
        public void ShowOptions(BotActor player, IList<string> options)
        {
            var text = new StringBuilder();
            int maxWidth = 0;
            for (int i = 0; i < options.Count; i++)
            {
                maxWidth = System.Math.Max(maxWidth, options[i].Length);
                text.Append(i + 1).Append(". ").Append(options[i]).Append("\r\n");
            }
            Send(Kind.Hint, player, text.ToString(), targetId: player.id, width: maxWidth < 5 ? 60 : maxWidth * 10, duration: 25);
            Send(Kind.EnableActions, player, targetId: player.id);
        }
        public void ExpireOptions(BotActor player)
        {
            Send(Kind.Hint, player, ".", targetId: player.id, width: 40);
            Send(Kind.EnableActions, player, targetId: player.id);
        }
        public void Sit(BotCharacter bot, int chair)
        { bot.chair = chair; Send(Kind.Chair, bot, value: chair); }
        public void CancelChair(BotCharacter bot)
        { bot.chair = 0; Send(Kind.Chair, bot); }
    }
}
