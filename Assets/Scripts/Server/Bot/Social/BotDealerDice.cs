// 双骰命令与结果的共享封装。对齐源端
//   soloMapling.ArtificialPlayer.BotCommands#botDealerRollDoubleDice（BotCommands.java:21-30）
//   soloMapling.MapVFX.CustomReactor 里的骰子特效广播（源端这两行已被注释掉，见下）。
//
// 源端原本逻辑：
//   int skillid = 5320007;  // Cannon Master Double Down
//   int level   = 5;
//   dice1 = Randomizer.nextInt(6) + 1;
//   dice2 = Randomizer.nextInt(6) + 1;
//   SocialCommands.BotSpeak(fakechar, "test <d1> <d2>");
//   // showDiceEffect / showDoubleDiceEffect 两行广播被注释掉了
//   return new int[]{dice1, dice2};
// 本工程 Server/Bot 层原先没有这套封装（grep DealerRoll 无命中），故新建此文件，
// 供 DiceBot 与其它需要「掷双骰 + 播报结果」的假人复用。
using System;

namespace Server.Bot.Social
{
    public static class BotDealerDice
    {
        /// <summary>≡ 源端 skillid：Cannon Master Double Down。</summary>
        public const int DoubleDownSkillId = 5320007;
        /// <summary>≡ 源端 level：技能等级（只参与源端已注释掉的特效包）。</summary>
        public const int SkillLevel = 5;
        public const int DiceSides = 6;

        static readonly Random fallback = new Random();

        /// <summary>
        /// ≡ botDealerRollDoubleDice：掷两颗骰子（各 1..6），按源端原样喊一句测试结果，返回点数数组。
        /// announce=false 时只掷不喊（供调用方自己拼播报文案）。
        /// </summary>
        public static int[] Roll(BotCharacter dealer, Random random = null, BotOutput output = null, bool announce = true)
        {
            var rng = random ?? fallback;
            int dice1 = rng.Next(DiceSides) + 1;
            int dice2 = rng.Next(DiceSides) + 1;
            if (announce && dealer != null)
            {
                try { (output ?? BotTypeManager.Output).Speak(dealer, AnnounceText(dice1, dice2)); }
                catch (Exception) { /* 播报失败不影响点数（源端同样只是一次 Broadcast） */ }
            }
            return new[] { dice1, dice2 };
        }

        /// <summary>≡ 源端 String.format("test %d %d", dice1, dice2)。</summary>
        public static string AnnounceText(int dice1, int dice2) => "test " + dice1 + " " + dice2;

        /// <summary>≡ DiceBot.processBet 的 "Result: [a] [b] - Han/Cho"（Han=单，Cho=双）。</summary>
        public static string ResultText(int[] rolls)
        {
            if (rolls == null || rolls.Length < 2) return "Result: -";
            bool isOdd = Sum(rolls) % 2 != 0;
            return "Result: [" + rolls[0] + "] [" + rolls[1] + "] - " + (isOdd ? "Han" : "Cho");
        }

        public static int Sum(int[] rolls)
        {
            if (rolls == null || rolls.Length < 2) return 0;
            return rolls[0] + rolls[1];
        }

        public static bool IsOdd(int[] rolls) => Sum(rolls) % 2 != 0;

        /// <summary>≡ DiceBot.calculateIfPlayerWins：HAN 押单、CHO 押双。</summary>
        public static bool PlayerWins(BetType bet, int[] rolls)
        {
            bool isOdd = IsOdd(rolls);
            switch (bet)
            {
                case BetType.Han: return isOdd;
                case BetType.Cho: return !isOdd;
                default: return false;
            }
        }

        /// <summary>源端 DiceBot.BetType：CHO=双（even），HAN=单（odd）。</summary>
        public enum BetType { Cho, Han, None }

        /// <summary>≡ DiceBot.processBet 的 "%s bets on %s!"。</summary>
        public static string BetAnnouncement(string playerName, BetType bet)
            => playerName + " bets on " + bet.ToString().ToUpperInvariant() + "!";
    }
}
