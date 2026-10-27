using System;
using System.Collections.Generic;

namespace Server.Bot.Environment
{
    // GCMoveSystem/BotMovementProfile.java。装备/地图由调用方提供已解析的数据。
    public sealed class BotMovementProfile : IEquatable<BotMovementProfile>
    {
        public static readonly BotMovementProfile Base = new BotMovementProfile(100, 100);
        public readonly int speed, jump;
        public readonly bool snowShoes;
        public BotMovementProfile(int speed, int jump, bool snowShoes = false)
        {
            this.speed = Math.Min(Bucket(speed), 200);
            this.jump = Math.Min(Bucket(jump), 123);
            this.snowShoes = snowShoes;
        }
        static int Bucket(int stat)
        {
            stat = Math.Max(1, stat);
            return stat < 5 ? stat : (int)(Math.Floor(stat / 5.0 + .5) * 5);
        }
        static bool HasHaste(int job) => job / 10 == 41 || job / 10 == 42;
        public static BotMovementProfile FromCharacter(BotActor actor, bool forceBaseMovement,
            IEnumerable<BotActor> partyMembers = null, bool snowShoes = false)
        {
            if (actor == null || forceBaseMovement) return Base;
            int speed, jump, level = actor.level;
            if (HasHaste(actor.job))
            { speed = level >= 55 ? 155 : 150; jump = level >= 55 ? 123 : 115; }
            else if (level <= 9) { speed = 115; jump = 100; }
            else if (level <= 29) { speed = 125; jump = 105; }
            else if (level <= 50) { speed = 130; jump = 110; }
            else if (level <= 69) { speed = 135; jump = 115; }
            else if (level <= 100) { speed = 145; jump = 120; }
            else { speed = 155; jump = 123; }
            if (partyMembers != null)
                foreach (var member in partyMembers)
                    if (member != null && member.id != actor.id && member.online && member.level >= 60 && HasHaste(member.job))
                    { speed = Math.Max(speed, 155); break; }
            return new BotMovementProfile(speed, jump, snowShoes);
        }
        public double SpeedMultiplier => speed / 100.0;
        public double JumpMultiplier => jump / 100.0;
        public double WalkVelocity(double baseVelocity) => baseVelocity * SpeedMultiplier;
        public double HorizontalForce(double baseForce) => baseForce * SpeedMultiplier;
        public float JumpSpeed(float baseSpeed) => (float)(baseSpeed * JumpMultiplier);
        public bool Equals(BotMovementProfile other) => other != null && speed == other.speed && jump == other.jump && snowShoes == other.snowShoes;
        public override bool Equals(object other) => Equals(other as BotMovementProfile);
        public override int GetHashCode() => unchecked((speed * 31 + jump) * 31 + (snowShoes ? 1 : 0));
    }
}
