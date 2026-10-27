#!/usr/bin/env python3
"""Export original server replay arrays; never derive timings from video duration."""
import argparse, hashlib, json, re, subprocess, tempfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent.parent / 'Beidou-Server/gms-server/src/main/java/org/gms'

def export():
    constants = {}
    for p in (SOURCE / 'constants/skills').glob('*.java'):
        for name, value in re.findall(r'(?:public\s+)?static\s+final\s+int\s+(\w+)\s*=\s*(\d+)\s*;', p.read_text()):
            constants[p.stem + '.' + name] = int(value)
    def sid(name):
        return int(name) if name.isdigit() else constants[name]
    paths = [SOURCE / 'constants/skills/ExplorerOtherSkillCompat.java']
    texts = {}
    arrays = {}
    for handler in ('CloseRange', 'Magic', 'Ranged'):
        p = SOURCE / ('net/server/channel/handlers/' + handler + ('AttackHandler.java' if handler=='Ranged' else 'DamageHandler.java'))
        paths.append(p)
        s = texts[handler] = p.read_text()
        for name, body, first, step, last in re.findall(r'private static final int\[\] (\w+)\s*=\s*(?:\{([^}]+)\}|intervalTimes\((\d+),\s*(\d+),\s*(\d+)\))\s*;', s):
            arrays[name] = [int(x) for x in re.findall(r'\d+', body)] if body else list(range(int(first), int(last)+1, int(step)))
    # Execute the standalone original class, including its range/concat helpers.
    src = paths[0].read_text()
    ids = sorted(set(map(int, re.findall(r'Map.entry\((\d+)', src))))
    java = 'import org.gms.constants.skills.ExplorerOtherSkillCompat; class Export { public static void main(String[] x) { for(int id:new int[]{' + ','.join(map(str,ids)) + '}) { var rows=ExplorerOtherSkillCompat.multiAttacks(id); for(var row:rows) { System.out.print(id+":"+row.skillId()+":"); for(int t:row.timesMs())System.out.print(t+","); System.out.println(); } } } }'
    result = {}
    videos = json.loads((ROOT/'Assets/Resources/NativeSkillVideos.json').read_text())
    fixed = set(re.findall(r'skillId == ([\w.]+)', texts['Ranged'].split('boolean usesFixedAttackOrigin')[1].split('\n    }')[0]))
    fixed = {sid(x) for x in fixed}
    fixed_close = {sid(x) for x in re.findall(r'skillId == ([\w.]+)', texts['CloseRange'].split('boolean usesFixedThunderBreakerOrigin')[1].split('\n    }')[0])}
    with tempfile.TemporaryDirectory() as d:
        p = Path(d)/'Export.java'; p.write_text(java)
        subprocess.run(['javac','-d',d,str(paths[0]),str(p)],check=True)
        output = subprocess.check_output(['java','-cp',d,'Export'],text=True)
    for line in output.splitlines():
        cast,replay,times=line.split(':'); cast,replay=int(cast),int(replay)
        rows=result.setdefault(str(cast),[])
        job=cast//10000
        kind='Magic' if job in (212,222,232) else 'Ranged' if job in (312,322,412,522) else 'CloseRange'
        rows.append(dict(skill=replay,times=[int(t) for t in times.split(',') if t],
            origin='fixed' if kind=='Magic' or cast in fixed else 'tracking',
            originalFirst=kind!='Ranged' and not rows,
            originalBounds=False,warrior=False,indexed=str(cast) in videos,
            total=cast==sid("Buccaneer.SEA_DRAGON_FIST") and replay==sid("Buccaneer.SEA_DRAGON_FIST_FINISH") and str(cast) not in videos,interval=120))
    # Explicit branch-to-scheduler mapping; timings remain the original arrays above.
    specs = {
    'Hero.SWORD_ILLUSION': [('SWORD_ILLUSION_SLASH','Hero.SWORD_ILLUSION_SLASH',False),('SWORD_ILLUSION_EXPLOSION','Hero.SWORD_ILLUSION_EXPLOSION',False)],
    'Hero.SPIRIT_CALIBER': [('SPIRIT_CALIBER','Hero.SPIRIT_CALIBER_FINISH',True),('SPIRIT_CALIBER_FINISH','Hero.SPIRIT_CALIBER_FINISH',False)],
    'Hero.RAGE_UPRISING_VI': [('RAGE_UPRISING_VI','Hero.RAGE_UPRISING_VI',True)],
    'Paladin.GRAND_GUARDIAN': [('GRAND_GUARDIAN','Paladin.GRAND_GUARDIAN_ATTACK',True)],
    'Paladin.MIGHTY_MJOLNIR': [('MIGHTY_MJOLNIR','Paladin.MIGHTY_MJOLNIR',True),('MIGHTY_MJOLNIR_EXPLOSION','Paladin.MIGHTY_MJOLNIR_EXPLOSION',False)],
    'Paladin.SACRED_BASTION': [('SACRED_BASTION','Paladin.SACRED_BASTION_STRIKE',True),('SACRED_BASTION_FINISH','Paladin.SACRED_BASTION_FINISH',False),('SACRED_BASTION_FIELD','Paladin.SACRED_BASTION_STRIKE',False)],
    'Paladin.HEAVENS_HAMMER_VI': [('HEAVENS_HAMMER_VI','Paladin.HEAVENS_HAMMER_VI_AFTERSHOCK',True),('RISING_JUSTICE','Paladin.RISING_JUSTICE',False)],
    'Paladin.DOMINUS_OBRION': [('DOMINUS_OBRION','Paladin.DOMINUS_OBRION_FINISH',True),('DOMINUS_OBRION_FINISH','Paladin.DOMINUS_OBRION_FINISH',False)],
    'DarkKnight.CALAMITOUS_CYCLONE': [('CALAMITOUS_CYCLONE','DarkKnight.CALAMITOUS_CYCLONE',True),('CALAMITOUS_CYCLONE_FINISH','DarkKnight.CALAMITOUS_CYCLONE_FINISH',False)],
    'DarkKnight.DEAD_SPACE': [('DEAD_SPACE','DarkKnight.DEAD_SPACE_FINISH',True),('DEAD_SPACE_FINISH','DarkKnight.DEAD_SPACE_FINISH',False)],
    'DarkKnight.DARK_HALIDOM': [('DARK_HALIDOM','DarkKnight.DARK_HALIDOM_FINISH',True),('DARK_HALIDOM_FINISH','DarkKnight.DARK_HALIDOM_FINISH',False)],
    'DawnWarrior.COSMOS': [('COSMOS_ATTACK','DawnWarrior.COSMOS',True)],
    'DawnWarrior.GALAXY_STAR_BURST': [('GALAXY_STAR_BURST_ATTACK','DawnWarrior.GALAXY_STAR_BURST',True)],
    'DawnWarrior.ECLIPSE_FORCE': [('ECLIPSE_FORCE_ATTACK','DawnWarrior.ECLIPSE_FORCE',True)],
    'DawnWarrior.SOUL_ECLIPSE': [('SOUL_ECLIPSE_ATTACK','DawnWarrior.SOUL_ECLIPSE',True)],
    'ThunderBreaker.SEA_DRAGON_SPIRAL': [('SEA_DRAGON_SPIRAL','ThunderBreaker.SEA_DRAGON_SPIRAL_TICK',True)],
    'ThunderBreaker.WAVE_RIDING_THUNDER': [('WAVE_RIDING_THUNDER_OPENING','ThunderBreaker.WAVE_RIDING_THUNDER',True),('WAVE_RIDING_THUNDER_SHOCK','ThunderBreaker.WAVE_RIDING_THUNDER_SHOCK',False)],
    'ThunderBreaker.SWIFT_ANNIHILATION': [('SWIFT_ANNIHILATION_OPENING','ThunderBreaker.SWIFT_ANNIHILATION',True),('SWIFT_ANNIHILATION_SURGE','ThunderBreaker.SWIFT_ANNIHILATION_SURGE',False)],
    'BlazeWizard.FLAME_DISCHARGE_LION': [('FLAME_DISCHARGE_LION_MAIN','BlazeWizard.FLAME_DISCHARGE_LION_BURST',True),('FLAME_DISCHARGE_LION_EMBER','BlazeWizard.FLAME_DISCHARGE_LION_EMBER',False),('FLAME_DISCHARGE_LION_FINISH','BlazeWizard.FLAME_DISCHARGE_LION_FINISH',False)],
    'BlazeWizard.INFERNO_SPHERE': [('INFERNO_SPHERE_ATTACK','BlazeWizard.INFERNO_SPHERE_TICK',True)],
    'BlazeWizard.PHOENIX_DRIVE_VI': [('PHOENIX_DRIVE_VI_ATTACK','BlazeWizard.PHOENIX_DRIVE_VI_TICK',True)],
    'BlazeWizard.ETERNAL_PHOENIX': [('ETERNAL_PHOENIX_MAIN','BlazeWizard.ETERNAL_PHOENIX_BURST',True),('ETERNAL_PHOENIX_CYCLE','BlazeWizard.ETERNAL_PHOENIX_CYCLE',False)],
    'BlazeWizard.FLAME_CONCERTO': [('FLAME_CONCERTO_MAIN','BlazeWizard.FLAME_CONCERTO_MAIN',True),('FLAME_CONCERTO_FINISH','BlazeWizard.FLAME_CONCERTO_FINISH',False)],
    'NightWalker.RAPID_THROW': [('RAPID_THROW_UPPER','NightWalker.RAPID_THROW_UPPER_DART',False),('RAPID_THROW_MIDDLE','NightWalker.RAPID_THROW_MIDDLE_DART',False),('RAPID_THROW_LOWER','NightWalker.RAPID_THROW_LOWER_DART',False)],
    'NightWalker.DARK_OMEN_VI': [('DARK_OMEN_VI','NightWalker.DARK_OMEN_VI_TICK',False)],
    'NightWalker.DOMINION_VI': [('DOMINION_VI_ATTACK','NightWalker.DOMINION_VI_TICK',False)],
    'NightWalker.SILENT_NIGHT': [('SILENT_NIGHT_OPENING','NightWalker.SILENT_NIGHT_DART',False),('SILENT_NIGHT_DART','NightWalker.SILENT_NIGHT_PROJECTILE',False)],
    'NightWalker.STYGIAN_COMMAND': [('STYGIAN_COMMAND','NightWalker.STYGIAN_COMMAND_MAIN',False),('STYGIAN_COMMAND_FINISH','NightWalker.STYGIAN_COMMAND_FINISH',False)],
    'WindArcher.GALE_BARRIER': [('GALE_BARRIER','WindArcher.GALE_BARRIER_TORNADO',False)],
    'WindArcher.ANEMOI': [('ANEMOI','WindArcher.ANEMOI_GALE',False)],
    'WindArcher.MISTRAL_SPRING': [('MISTRAL_WIND_BLADE','WindArcher.MISTRAL_WIND_BLADE',False)],
    'WindArcher.ELEMENTAL_TEMPEST': [('ELEMENTAL_TEMPEST_WAVE','WindArcher.ELEMENTAL_TEMPEST_WAVE',False),('ELEMENTAL_TEMPEST_ARROW','WindArcher.ELEMENTAL_TEMPEST_ARROW_RAIN',False)],
    }
    warriors={'Hero.SPIRIT_CALIBER','Paladin.SACRED_BASTION','Paladin.DOMINUS_OBRION','DarkKnight.DEAD_SPACE','DarkKnight.DARK_HALIDOM'}
    for cast, stages in specs.items():
        id=sid(cast); rows=[]
        for array,replay,first in stages:
            key=array+'_TIMES_MS'
            if key not in arrays: raise ValueError(key)
            rows.append(dict(skill=sid(replay),times=arrays[key],
                origin='fixed' if cast.startswith('DawnWarrior.') or id in fixed or id in fixed_close or (cast.startswith('BlazeWizard.') and not cast.endswith('.PHOENIX_DRIVE_VI')) else 'tracking',
                liveFacing=cast.endswith('.PHOENIX_DRIVE_VI'),originalFirst=first,
                originalBounds=cast in warriors or cast=='Hero.SWORD_ILLUSION',warrior=cast in warriors,
                indexed=str(id) in videos or cast=='DawnWarrior.COSMOS',interval=60 if cast=='DawnWarrior.GALAXY_STAR_BURST' else 120))
        result[str(id)]=rows
    id=sid('NightWalker.RAPID_THROW')
    ms=int(re.search(r'RAPID_THROW_FINISH_TIME_MS = (\d+)',texts['Ranged'])[1])
    result[str(id)].append(dict(skill=sid('NightWalker.RAPID_THROW_FINISH'),times=[ms],origin='tracking',originalFirst=False,originalBounds=False,warrior=False,indexed=False,interval=120))
    # These three videos use a single captured original attack, rather than a replay loop.
    for name in ('WindArcher.MONSOON_VI','ThunderBreaker.GOD_OF_THE_SEA_VI','5221012'):
        id=sid(name)
        result[str(id)]=[dict(skill=id,times=[0],origin='fixed',originalFirst=True,originalBounds=True,warrior=False,indexed=True,interval=120)]
    id=sid('Hero.DEATH_FAULT')
    delay=int(re.search(r'DEATH_FAULT_HIT_DELAY_MS = (\d+)',texts['CloseRange'])[1])
    result[str(id)]=[dict(skill=id,times=[delay],origin='fixed',originalFirst=True,originalBounds=True,warrior=False,indexed=False,interval=120)]
    result[str(sid('ThunderBreaker.LIGHTNING_SPEAR_MULTISTRIKE'))] = [
        dict(skill=sid('ThunderBreaker.'+name),times=arrays[array],origin='tracking',
             originalFirst=False,originalBounds=False,warrior=False,indexed=False,interval=120)
        for name,array in [('LIGHTNING_SPEAR_FINISH','LIGHTNING_SPEAR_FINISH_TIMES_MS'),
                           ('LIGHTNING_SPEAR_GIANT_THUNDER','LIGHTNING_SPEAR_GIANT_THUNDER_TIMES_MS')]]
    out=ROOT/'Assets/Resources/NativeSkillReplays.json'
    out.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':'))+'\n')
    (ROOT/'docs/skill-replay-source.json').write_text(json.dumps({str(p.relative_to(SOURCE)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(set(paths + list((SOURCE/'constants/skills').glob('*.java'))))},indent=2)+'\n')
    print(f'{len(result)} casts, {sum(len(v) for v in result.values())} stages, {sum(len(s["times"]) for v in result.values() for s in v)} ticks')
if __name__=='__main__': export()
