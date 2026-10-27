using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Server.Bot.Social
{
    // BotDialogueHandler：原始YAML在导出时转JSON，台词与节点名保持源端内容。
    public sealed class BotDialogue
    {
        public sealed class Dialogue
        {
            public List<string> text;
            public List<int> emotes;
            public long durationMs;
        }
        readonly BotResources resources;
        readonly Random random;
        public BotDialogue(BotResources resources, Random random = null)
        { this.resources = resources; this.random = random ?? new Random(); }
        public Dialogue Get(string pack, string type, string node)
        {
            var root = resources.ReadData("soloMapling/ArtificialPlayer/BotDialoguePack/" + pack) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue(type, out var typeNode) || !(typeNode is Dictionary<string, object> types)
                || !types.TryGetValue(node, out var nodeValue) || !(nodeValue is Dictionary<string, object> value)) return null;
            value.TryGetValue("text", out var text);
            value.TryGetValue("emote", out var emote);
            value.TryGetValue("wait", out var wait);
            return new Dialogue {
                text = (text as List<object>)?.Select(x => x as string).ToList(),
                emotes = emote is List<object> list ? list.Select(ToInt).ToList() : new List<int> { ToInt(emote) },
                durationMs = unchecked(ToInt(wait) * 1000)
            };
        }
        static int ToInt(object value) => value is long l ? unchecked((int)l) : value is int i ? i
            : value is string s && int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? n : 0;
        public static List<string> ReplaceStrings(List<string> input, IDictionary<string, string> replacements)
        {
            if (input == null || replacements == null) throw new ArgumentException("Input list and replacements map cannot be null");
            var result = new List<string>();
            foreach (var text in input)
            {
                string current = text;
                foreach (var pair in replacements) current = current.Replace(pair.Key, pair.Value);
                result.Add(current);
            }
            return result;
        }
        public string RandomLine(string pack, string type, string node)
        {
            var d = Get(pack, type, node);
            return d?.text == null || d.text.Count == 0 ? null : d.text[random.Next(d.text.Count)];
        }
        public int RandomEmote(string pack, string type, string node)
        {
            var d = Get(pack, type, node);
            return d == null ? -1 : d.emotes[random.Next(d.emotes.Count)];
        }
        public void Run(BotCharacter bot, Dialogue dialogue, BotOutput output, bool flavor, Action then = null)
        {
            if (dialogue == null) { then?.Invoke(); return; }
            var lines = flavor ? new List<string> { dialogue.text[random.Next(dialogue.text.Count)] } : dialogue.text;
            var chain = BotTiming.CreateChain().StopUnless(() => BotRegistry.Contains(bot.id));
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i];
                chain.Run(() => output.Speak(bot, text));
                if (i < lines.Count - 1) chain.Pause(5000);
            }
            chain.Run(() => output.Emote(bot, dialogue.emotes[random.Next(dialogue.emotes.Count)]))
                .Pause(dialogue.durationMs).Run(then).Start();
        }
    }
}
