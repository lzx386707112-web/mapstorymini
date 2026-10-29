#!/usr/bin/env python3
"""Extract player BuffStat mappings (including Java switch fallthrough) from the original server."""
import argparse, hashlib, json, re
from pathlib import Path

def generate(source, output):
    constants = {}
    for p in (source / 'constants/skills').glob('*.java'):
        for name, value in re.findall(r'\b(?:public\s+)?static\s+final\s+int\s+(\w+)\s*=\s*(\d+)', p.read_text()):
            constants[p.stem + '.' + name] = int(value)
    effect = (source / 'server/StatEffect.java').read_text()
    factory = (source / 'client/SkillFactory.java').read_text()
    # Remove comments first: a commented-out break is intentional fallthrough.
    clean = lambda s: re.sub(r'/\*.*?\*/|//[^\n]*', '', s, flags=re.S)
    switch = clean(effect[effect.index('switch (sourceid)', effect.index('Map<MonsterStatus')):effect.index('if (ret.isMorph())')])
    matches = list(re.finditer(r'case ([\w.]+):', switch))
    mapping = {}
    for i, match in enumerate(matches):
        tail = switch[match.end():]
        tail = tail[:tail.index('break;')]
        pairs = re.findall(r'statups.add\(new Pair<>\(BuffStat.(\w+), ([^;]+?)\)\);', tail)
        if pairs:
            assert match[1] in constants, match[1]
            mapping[str(constants[match[1]])] = [[key, expr.replace('ret.', '').strip()] for key, expr in pairs]
    forced = {}
    for group in re.finditer(r'((?:\s*case [\w.]+:)+)\s*isBuff = (true|false);', clean(factory)):
        for name in re.findall(r'case ([\w.]+):', group[1]):
            forced[str(constants[name])] = group[2] == 'true'
    movement = effect[effect.index('private SummonMovementType getSummonMovementType()'):]
    movement = movement[:movement.index('return null;\n    }')]
    summons = [constants[name] for name in re.findall(r'case ([\w.]+):', movement)]
    will = effect[effect.index('public static boolean isHerosWill('):effect.index('private boolean isWkCharge(')]
    supported = {'x','y','iprop','sourceid','1','0','100','x << 8 | y','duration'}
    for pairs in mapping.values():
        for _, expr in pairs: assert expr in supported, expr
    data = {'sourceSha256': hashlib.sha256(effect.encode()).hexdigest(), 'statups': mapping,
            'forcedOverTime': forced, 'summons': summons,
            'herosWill': [constants[name] for name in re.findall(r'case ([\w.]+):', clean(will))],
            'mapChairs': [constants[name] for name in ['Beginner.MAP_CHAIR','Noblesse.MAP_CHAIR','Legend.MAP_CHAIR']],
            'nimbleFeet': [constants[name] for name in ['Beginner.NIMBLE_FEET','Noblesse.NIMBLE_FEET','Evan.NIMBLE_FEET','Legend.AGILE_BODY']]}
    output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n')
    print(f'{len(mapping)} skill mappings, {len(forced)} explicit classifications, {len(summons)} summons')

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, default=Path.home() / 'Documents/mxd/Beidou-Server/gms-server/src/main/java/org/gms')
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'Assets/Resources/NativeBuffContract.json')
    args = parser.parse_args()
    generate(args.source, args.output)
