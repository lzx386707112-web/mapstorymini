from pathlib import Path
import re
import argparse
parser=argparse.ArgumentParser(description='Reproduce original SoloMapling preferred attack skill transcription')
parser.add_argument('--source',type=Path,default=Path(__file__).resolve().parents[3]/'Beidou-Server/gms-server/src/main/java')
root=parser.parse_args().source
source=(root/'soloMapling/ArtificialPlayer/BotCommandsPack/BotAttack.java').read_text()
jobs=dict(re.findall(r'\b([A-Z][A-Z_0-9]*)\((\d+),', (root/'org/gms/client/Job.java').read_text()))
constants={}
for p in (root/'org/gms/constants/skills').glob('*.java'):
 for name,val in re.findall(r'\b(?:public\s+)?static\s+final\s+int\s+(\w+)\s*=\s*(\d+)\s*;',p.read_text()):constants[p.stem+'.'+name]=val
body=source[source.index('    private static BossAttackSkill resolveBossAttackSkill'):source.index('    private static List<Integer> splitDamage')]
body=body.replace('private static BossAttackSkill resolveBossAttackSkill(Job job)','static Skill Preferred(int job)').replace('job == null','job < 0').replace('new BossAttackSkill','new Skill').replace('WARRIOR_SLASH_BLAST','1001005')
body=re.sub(r'job.isA\(Job\.(\w+)\)',lambda m:'IsA(job,'+jobs[m[1]]+')',body)
body=re.sub(r'Job\.(\w+)',lambda m:jobs[m[1]],body)
body=re.sub(r'\b([A-Z][a-zA-Z]*\.[A-Z_]+)\b',lambda m:constants[m[1]] if m[1] in constants else m[0],body)
assert 'Job.' not in body
p=Path(__file__).resolve().parents[1]/'Assets/Scripts/Server/Bot/BotAttackSkills.cs'
p.write_text('''// Preferred skill selection transcribed from source BotAttack.resolveBossAttackSkill.
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
'''+body+'''    }
}
''')
