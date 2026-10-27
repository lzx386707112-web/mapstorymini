// Preferred skill selection transcribed from source BotAttack.resolveBossAttackSkill.
namespace Server.Bot
{
    public static partial class BotAttack
    {
        public enum PacketKind { CLOSE, RANGED, MAGIC }
        public readonly struct Skill
        {
            public readonly int id, level, hits;
            public readonly PacketKind kind;
            public Skill(int id,int level,int hits,PacketKind kind)
            { this.id=id; this.level=level; this.hits=hits; this.kind=kind; }
        }
        static bool IsA(int job,int parent) => job/10 == parent/10 && job >= parent || parent/10%10 == 0 && job/100 == parent/100;
    static Skill Preferred(int job) {
        if (job < 0) {
            return new Skill(0, 0, 1, PacketKind.CLOSE);
        }
        if (job == 100 || job == 110 || job == 120 || job == 130) {
            return new Skill(1001005, 20, 1, PacketKind.CLOSE);
        }
        if (job == 111) {
            return new Skill(1111005, 30, 1, PacketKind.CLOSE);
        }
        if (IsA(job,110) || IsA(job,1100)) {
            return new Skill(IsA(job,1100) ? 11111004 : 1121008, 30, 2, PacketKind.CLOSE);
        }
        if (job == 121) {
            return new Skill(1211002, 30, 1, PacketKind.CLOSE);
        }
        if (IsA(job,120)) {
            return new Skill(1221009, 30, 1, PacketKind.CLOSE);
        }
        if (job == 131) {
            return new Skill(1311001, 30, 3, PacketKind.CLOSE);
        }
        if (IsA(job,130)) {
            return new Skill(1311001, 30, 3, PacketKind.CLOSE);
        }
        if (job == 200) {
            return new Skill(2001005, 20, 2, PacketKind.MAGIC);
        }
        if (job == 210 || job == 1210) {
            return new Skill(2101004, 30, 1, PacketKind.MAGIC);
        }
        if (job == 211 || job == 1211) {
            return new Skill(job == 1211 ? 12111006 : 2111002, 30, 1, PacketKind.MAGIC);
        }
        if (IsA(job,210) || IsA(job,1200)) {
            return new Skill(IsA(job,1200) ? 12111003 : 2121007, 30, 1, PacketKind.MAGIC);
        }
        if (job == 220) {
            return new Skill(2201004, 30, 1, PacketKind.MAGIC);
        }
        if (job == 221) {
            return new Skill(2211002, 30, 1, PacketKind.MAGIC);
        }
        if (IsA(job,220)) {
            return new Skill(2221006, 30, 1, PacketKind.MAGIC);
        }
        if (job == 230) {
            return new Skill(2301005, 30, 1, PacketKind.MAGIC);
        }
        if (job == 231) {
            return new Skill(2311004, 30, 1, PacketKind.MAGIC);
        }
        if (IsA(job,230)) {
            return new Skill(2321007, 30, 1, PacketKind.MAGIC);
        }
        if (job == 300) {
            return new Skill(3001004, 20, 2, PacketKind.RANGED);
        }
        if (job == 310 || job == 320) {
            return new Skill(job == 310 ? 3101005 : 3201005, 30, 1, PacketKind.RANGED);
        }
        if (job == 311) {
            return new Skill(3111006, 30, 4, PacketKind.RANGED);
        }
        if (IsA(job,310) || IsA(job,1300)) {
            return new Skill(IsA(job,1300) ? 13111001 : 3111006, 30, 4, PacketKind.RANGED);
        }
        if (job == 321) {
            return new Skill(3211006, 30, 4, PacketKind.RANGED);
        }
        if (IsA(job,320)) {
            return new Skill(3211006, 30, 4, PacketKind.RANGED);
        }
        if (job == 400) {
            return new Skill(4001334, 20, 2, PacketKind.CLOSE);
        }
        if (job == 410) {
            return new Skill(4001344, 20, 2, PacketKind.RANGED);
        }
        if (job == 411) {
            return new Skill(4111005, 30, 1, PacketKind.RANGED);
        }
        if (IsA(job,410) || IsA(job,1400)) {
            return new Skill(IsA(job,1400) ? 14110005 : 4121007, 30, 3, PacketKind.RANGED);
        }
        if (job == 420) {
            return new Skill(4201005, 30, 6, PacketKind.CLOSE);
        }
        if (job == 421) {
            return new Skill(4211004, 30, 1, PacketKind.CLOSE);
        }
        if (IsA(job,420)) {
            return new Skill(4221007, 30, 2, PacketKind.CLOSE);
        }
        if (job == 500) {
            return new Skill(5001002, 20, 1, PacketKind.CLOSE);
        }
        if (job == 510 || job == 1510) {
            return new Skill(job == 1510 ? 15101005 : 5101003, 30, 1, PacketKind.CLOSE);
        }
        if (job == 511 || job == 1511) {
            return new Skill(job == 1511 ? 15111004 : 5111002, 30, job == 1511 ? 6 : 1, PacketKind.CLOSE);
        }
        if (IsA(job,510) || IsA(job,1500)) {
            return new Skill(IsA(job,1500) ? 15111004 : 5121007, 30, 6, PacketKind.CLOSE);
        }
        if (job == 520) {
            return new Skill(5001003, 20, 2, PacketKind.RANGED);
        }
        if (job == 521) {
            return new Skill(5210000, 30, 3, PacketKind.RANGED);
        }
        if (IsA(job,520)) {
            return new Skill(5221007, 30, 4, PacketKind.RANGED);
        }
        return new Skill(0, 0, 1, PacketKind.CLOSE);
    }

    }
}
