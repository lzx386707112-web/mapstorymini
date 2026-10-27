using System;

namespace Server.Bot
{
    public static partial class BotAttack
    {
        // 原BotAttackData动作桶。现金武器不改变普通-11槽的武器类别。
        public static int ActionFor(int weaponId, Random random)
        {
            int type = weaponId / 10000;
            int[] actions = type == 140 || type == 141 || type == 142 ? new[] {16,17,9,10,11}
                : type == 143 || type == 144 ? new[] {13,19}
                : type == 137 || type == 138 ? new[] {28,29}
                : type == 147 ? new[] {24,25,26}
                : type == 145 ? new[] {9,11} : type == 146 ? new[] {9,19}
                : type == 149 ? new[] {13,20} : new[] {16,17,5,6,7};
            return actions[random.Next(actions.Length)];
        }
        public static string PoseFor(int action)
        {
            switch (action)
            {
                case 5: case 24: case 28: return "swingO1";
                case 6: case 25: return "swingO2";
                case 7: case 26: case 29: return "swingO3";
                case 9: return "swingT1"; case 10: return "swingT2"; case 11: return "swingT3";
                case 13: return "swingP1"; case 14: return "swingP2";
                case 16: return "stabO1"; case 17: return "stabO2";
                case 19: return "stabT1"; case 20: return "stabT2";
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
        public static void BasicSwing(BotCharacter actor, Random random = null, Social.BotOutput output = null)
        {
            if (actor == null) return;
            actor.equips.TryGetValue(-11,out int weapon);
            int action = ActionFor(weapon,random ?? new Random());
            (output ?? BotTypeManager.Output).Send(Social.BotOutput.Kind.Attack,actor,PoseFor(action),value: 4);
        }
        public static int LineDamage(int jobTier, int level, long monsterMaxHp, Random random)
        {
            int[] low = {80,150,320,700,1200}, high = {160,360,800,1800,3200};
            int tier = Math.Max(0,Math.Min(4,jobTier));
            int raw = low[tier] + random.Next(high[tier] - low[tier] + 1);
            int scaled = unchecked((int)(long)Math.Floor(raw * (1 + Math.Max(0,level) / 80.0) + .5));
            int hp = monsterMaxHp > 0 ? (int)Math.Min(99999L,Math.Max(1L,monsterMaxHp / 18)) : 1;
            return Math.Min(99999,Math.Max(scaled,hp));
        }
        public static int JobTier(int job)
        {
            if (job == 0) return 0;
            switch (job % 100)
            {
                case 0: return 1;
                case 10: case 20: case 30: return 2;
                case 11: case 21: case 31: return 3;
                case 12: case 22: case 32: return 4;
                default: return 0;
            }
        }
        static bool Melee(int weapon) => weapon >= 130 && weapon <= 149 &&
            weapon != 134 && weapon != 135 && weapon != 136 && weapon != 139 &&
            weapon != 137 && weapon != 138 && weapon != 145 && weapon != 146 && weapon != 147 && weapon != 149;
        static bool Supports(int weapon, Skill skill)
        {
            if (skill.id <= 0 || weapon < 130 || weapon > 149) return false;
            if (skill.kind == PacketKind.MAGIC) return weapon == 137 || weapon == 138;
            if (skill.kind == PacketKind.RANGED)
            {
                switch (skill.id)
                {
                    case 4001344: case 4111005: case 4121007: case 14110005: return weapon == 147;
                    case 5001003: case 5210000: case 5221007: return weapon == 149;
                    case 3201005: case 3211006: return weapon == 146;
                    case 3001004: return weapon == 145 || weapon == 146;
                    default: return weapon == 145;
                }
            }
            switch (skill.id)
            {
                case 1311001: return weapon == 143 || weapon == 144;
                case 4001334: case 4201005: case 4211004: case 4221007: return weapon == 133;
                case 5001002: case 5101003: case 5111002: case 5121007: case 15101005: case 15111004: return weapon == 148;
                default: return Melee(weapon);
            }
        }
        public static Skill GrindSkill(int job, int weaponId)
        {
            int weapon = weaponId / 10000;
            var preferred = Preferred(job);
            if (Supports(weapon,preferred)) return preferred;
            Skill fallback = new Skill(0,0,1,PacketKind.CLOSE);
            if ((IsA(job,200) || IsA(job,1200)) && (weapon == 137 || weapon == 138)) fallback = new Skill(2001005,20,2,PacketKind.MAGIC);
            else if (IsA(job,300) || IsA(job,1300))
            { if (weapon == 145) fallback = new Skill(3001004,20,2,PacketKind.RANGED); else if (weapon == 146) fallback = new Skill(3201005,20,1,PacketKind.RANGED); }
            else if (IsA(job,400) || IsA(job,1400))
            { if (weapon == 147) fallback = new Skill(4001344,20,2,PacketKind.RANGED); else if (weapon == 133) fallback = new Skill(4001334,20,2,PacketKind.CLOSE); }
            else if (IsA(job,500) || IsA(job,1500))
            { if (weapon == 149) fallback = new Skill(5001003,20,2,PacketKind.RANGED); else if (weapon == 148) fallback = new Skill(5001002,20,1,PacketKind.CLOSE); }
            else if ((IsA(job,100) || IsA(job,1100)) && Melee(weapon)) fallback = new Skill(1001005,20,1,PacketKind.CLOSE);
            return Supports(weapon,fallback) ? fallback : new Skill(0,0,1,PacketKind.CLOSE);
        }
        public static int[] SplitDamage(int total, int hitCount)
        {
            int hits = Math.Max(1,hitCount), basic = Math.Max(1,total/hits), remaining = total;
            var result = new int[hits];
            for (int i = 0; i < hits; i++)
            { int hit = i == hits-1 ? remaining : Math.Min(basic,remaining); result[i] = Math.Max(1,hit); remaining -= hit; }
            return result;
        }
    }
}
