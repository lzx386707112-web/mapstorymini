using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Bot.Types
{
    // BotTypes/OPQ/OPQRecruitMessages.java：大厅喊话生成器。
    // 结构同 MerchantBotMessageCreator：[前缀] [副本名] [可选等级职业标签] [可选尾巴]
    public static class OpqRecruitMessages
    {
        static readonly string[] Prefixes = { "J>", "Joining>", "J>>", "Join>", "LFP>", "lf>" };
        static readonly string[] PqNames = { "OPQ", "Orbis PQ", "Orbis Party Quest", "pq", "Orbis", "OPQ please" };
        static readonly string[] Fillers = { "@@@@@@@@", "!!!", "plz", "asap" };

        public static string Generate(BotActor chr, Random random = null)
        {
            random ??= new Random();
            string prefix = Prefixes[random.Next(Prefixes.Length)];
            string pqName = PqNames[random.Next(PqNames.Length)];

            var builder = new StringBuilder();
            // 35% 概率带上自己的等级（源端还会带职业名，这里用 JobDefs 里的中文名）
            if (chr != null && random.NextDouble() < 0.35)
                builder.Append("Lvl ").Append(chr.level).Append(' ').Append(JobName(chr.job)).Append(' ');
            builder.Append(prefix).Append(' ').Append(pqName);

            int fillerCount = random.Next(3);
            for (int i = 0; i < fillerCount; i++) builder.Append(' ').Append(Fillers[random.Next(Fillers.Length)]);

            string output = builder.ToString().Replace("[", "").Replace("]", "");
            // 15% 概率整句大写（和商人喊话一个风格）
            return random.NextDouble() < 0.15 ? output.ToUpperInvariant() : output;
        }

        // 源端用 chr.getJob().name()（枚举名）；JobDefs 只在前端程序集里，Server 侧不能引用，
        // 这里按职业分支给一个等价的可读名（1 战士 / 2 法师 / 3 弓手 / 4 盗贼 / 5 海盗）。
        static string JobName(int job)
        {
            if (job <= 0) return "Beginner";
            switch (job / 100 % 10)
            {
                case 1: return "Warrior";
                case 2: return "Magician";
                case 3: return "Bowman";
                case 4: return "Thief";
                case 5: return "Pirate";
                default: return "Beginner";
            }
        }
    }
}
